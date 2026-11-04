using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sketchora.App.Interop;
using Sketchora.App.Services;
using Sketchora.App.ViewModels;
using Sketchora.Core;

namespace Sketchora.App.Views;

public partial class OverlayWindow : Window
{
    private readonly ToolbarViewModel _model;
    private readonly LessonWorkspace _workspace;
    private readonly TemporaryInk<Stroke> _temporary = new();
    private readonly DispatcherTimer _haloTimer;
    private HwndSource? _source;
    private bool _syncing, _suspendHalo;
    private BitmapSource? _frozenFrame, _pageBitmap;
    private string _pageImageData = "";
    private Point? _panStart;
    private double _panOriginX, _panOriginY;
    private TouchDevice? _panTouch;
    private Guid _renderedPage;
    private string _renderedInk = "";
    private TextLabel[] _renderedTexts = [];
    private Point? _shapeStart;
    private TouchDevice? _shapeTouch;
    private readonly Dictionary<int, (Stroke Stroke, bool Fading)> _touchStrokes = [];
    private readonly Dictionary<int, TouchDevice> _touchDevices = [];
    private readonly HashSet<int> _penDevices = [];
    internal int ActiveTouchStrokeCount => _touchStrokes.Count;
    private (Border Border, TextLabel Label, Point Start)? _textDrag;
    public event Action<Point, TextLabel?>? TextRequested;
    public event Action? DisplayChanged;
    public Size CanvasSize
    {
        get
        {
            var bounds = NativeMethods.DesktopBounds();
            var dpi = VisualTreeHelper.GetDpi(this);
            return new Size(bounds.Width / dpi.DpiScaleX, bounds.Height / dpi.DpiScaleY);
        }
    }

    public OverlayWindow(ToolbarViewModel model, LessonWorkspace workspace)
    {
        _model = model;
        _workspace = workspace;
        InitializeComponent();
        InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
        InkSurface.SelectionChanged += (_, _) => _model.SelectionCount = InkSurface.GetSelectedStrokes().Count;
        InkSurface.SelectionMoved += (_, _) => CommitSelection();
        InkSurface.SelectionResized += (_, _) => CommitSelection();
        SizeChanged += (_, _) => RefreshBackground();
        TemporarySurface.StrokeCollected += (_, e) =>
        {
            _temporary.Add(e.Stroke, TimeSpan.FromSeconds(_model.FadeSeconds));
            UpdateHaloTimer();
        };
        _model.PropertyChanged += OnModelChanged;
        _workspace.Changed += OnWorkspaceChanged;
        _haloTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background,
            (_, _) => Tick(), Dispatcher);
        _haloTimer.Stop();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            Reposition();
            RefreshCanvas();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _penDevices.Clear(); CancelTouchStrokes(); }
            UpdateHaloTimer();
        };
        Closed += (_, _) =>
        {
            _haloTimer.Stop();
            _model.PropertyChanged -= OnModelChanged;
            _workspace.Changed -= OnWorkspaceChanged;
            InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
            _source?.RemoveHook(WndProc);
        };
        // Establish the rendered canvas identity before any input can commit.
        // Otherwise the first stroke's Changed event looks like a page switch
        // and cancels other concurrent finger strokes.
        RefreshCanvas();
    }

    public void SetFrozenFrame(BitmapSource? frame)
    {
        _frozenFrame = frame; _model.FrozenScreen = frame is not null;
        ResetView(); RefreshBackground();
    }
    public void ResetView() => _model.SetView(1, 0, 0);
    public void ChangeZoom(double factor, Point? anchor = null)
    {
        if (!_model.CanNavigateCanvas) return;
        var point = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var next = Math.Clamp(_model.ZoomLevel * factor, .25, 4); var ratio = next / _model.ZoomLevel;
        _model.SetView(next, point.X - (point.X - _model.PanX) * ratio, point.Y - (point.Y - _model.PanY) * ratio);
    }
    public void DuplicateSelection()
    {
        var copies = InkSurface.GetSelectedStrokes().Clone();
        if (copies.Count == 0) return;
        copies.Transform(new Matrix(1, 0, 0, 1, 20, 20), false);
        InkSurface.Strokes.Add(copies); InkSurface.Select(copies);
    }
    public void DeleteSelection()
    {
        var strokes = InkSurface.GetSelectedStrokes();
        if (strokes.Count > 0) InkSurface.Strokes.Remove(strokes);
    }
    internal void CommitSelection()
    {
        if (!_syncing) OnStrokesChanged(this, null!);
    }
    public bool SuspendHalo
    {
        get => _suspendHalo;
        set { _suspendHalo = value; ApplyState(); }
    }
    public void ClearTemporary()
    {
        if (_touchStrokes.Values.Any(pending => pending.Fading)) CancelTouchStrokes();
        _temporary.Clear();
        TemporarySurface.Strokes.Clear();
        UpdateHaloTimer();
    }
    public void CancelPendingShape()
    {
        CancelShape();
        CancelTextDrag();
        CancelPan();
        CancelTouchStrokes();
        TemporarySurface.EditingMode = InkCanvasEditingMode.None;
        ClearTemporary();
    }
    private void OnWorkspaceChanged(object? sender, EventArgs e) => RefreshCanvas();

    private void RefreshCanvas()
    {
        var page = _workspace.ActivePage;
        if (page.Id != _renderedPage)
        {
            ResetView(); CancelPan();
            CancelTouchStrokes();
            CancelShape();
            CancelTextDrag();
            ClearTemporary();
        }
        if (!_syncing && (page.Id != _renderedPage || page.Ink != _renderedInk || !page.Texts.SequenceEqual(_renderedTexts)))
        {
            CancelShape();
            CancelTextDrag();
            CancelTouchStrokes();
            InkSurface.EditingMode = InkCanvasEditingMode.None;
            InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
            InkSurface.Strokes = InkSerializer.Decode(page.Ink);
            InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
            RenderText(page.Texts);
        }
        _renderedPage = page.Id;
        _renderedInk = page.Ink;
        _renderedTexts = page.Texts.ToArray();
        _model.UpdateWorkspace(_workspace, InkSurface.Strokes.Count);
        RefreshBackground();
        ApplyState();
    }

    private void RefreshBackground()
    {
        if (PageBackgroundLayer is null) return;
        var page = _workspace.ActivePage;
        SurfaceRoot.Background = !_workspace.IsDesktop
            ? page.Background == PageBackground.White ? Brushes.White : new SolidColorBrush(Color.FromRgb(18, 20, 28))
            : _frozenFrame is not null ? Brushes.Black : Brushes.Transparent;
        PageBackgroundLayer.Fill = _workspace.IsDesktop ? Brushes.Transparent : LessonRenderer.BackgroundBrush(page);
        if (_pageImageData != page.ImagePng)
        {
            _pageImageData = page.ImagePng;
            _pageBitmap = page.ImagePng.Length > 0 ? LessonRenderer.DecodeImage(page.ImagePng) : null;
        }
        var image = _workspace.IsDesktop && _frozenFrame is not null ? _frozenFrame : _pageBitmap;
        PageImage.Source = image;
        if (image is not null)
        {
            var snapshot = _workspace.Snapshot;
            var width = ActualWidth > 0 ? ActualWidth : snapshot.CanvasWidth; var height = ActualHeight > 0 ? ActualHeight : snapshot.CanvasHeight;
            var placement = !(_workspace.IsDesktop && _frozenFrame is not null) ? page.ImagePlacement : null;
            var rect = placement is not null ? new Rect(placement.X, placement.Y, placement.Width, placement.Height) : LessonRenderer.FitImage(image, width, height);
            Canvas.SetLeft(PageImage, rect.X); Canvas.SetTop(PageImage, rect.Y); PageImage.Width = rect.Width; PageImage.Height = rect.Height;
        }
    }
    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        try { _workspace.SetContent(InkSerializer.Encode(InkSurface.Strokes), _workspace.ActivePage.Texts); }
        catch (InvalidDataException ex)
        {
            _model.Notice = ex.Message;
            InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
            InkSurface.Strokes = InkSerializer.Decode(_workspace.ActivePage.Ink);
            InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
        }
        finally { _syncing = false; }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarViewModel.ZoomLevel))
            CanvasContent.RenderTransform = new MatrixTransform(_model.ZoomLevel, 0, 0, _model.ZoomLevel, _model.PanX, _model.PanY);
        if (e.PropertyName is nameof(ToolbarViewModel.PanX) or nameof(ToolbarViewModel.PanY))
            CanvasContent.RenderTransform = new MatrixTransform(_model.ZoomLevel, 0, 0, _model.ZoomLevel, _model.PanX, _model.PanY);
        if (e.PropertyName == nameof(ToolbarViewModel.IsDrawing) && !_model.IsDrawing && !_model.IsBusy && _frozenFrame is not null)
            SetFrozenFrame(null);
        if (e.PropertyName is nameof(ToolbarViewModel.IsDrawing) or nameof(ToolbarViewModel.Tool) or nameof(ToolbarViewModel.ShowInk))
        {
            CancelShape();
            CancelTextDrag();
            CancelPan();
            CancelTouchStrokes();
            TemporarySurface.EditingMode = InkCanvasEditingMode.None;
            ClearTemporary();
        }
        if (e.PropertyName is nameof(ToolbarViewModel.IsDrawing) or nameof(ToolbarViewModel.Tool)
            or nameof(ToolbarViewModel.ShowInk) or nameof(ToolbarViewModel.Color) or nameof(ToolbarViewModel.Thickness)
            or nameof(ToolbarViewModel.HaloEnabled) or nameof(ToolbarViewModel.TouchEnabled) or nameof(ToolbarViewModel.IsBusy)
            or nameof(ToolbarViewModel.PressureEnabled) or nameof(ToolbarViewModel.PalmRejection)
            or nameof(ToolbarViewModel.SpotlightEnabled) or nameof(ToolbarViewModel.SpotlightRadius))
        {
            if (!_model.TouchEnabled || _model.IsBusy || (_model.PalmRejection && _penDevices.Count > 0)) CancelTouchStrokes();
            ApplyState();
        }
    }

    private bool IsShapeTool => _model.Tool is DrawingTool.Line or DrawingTool.Arrow or DrawingTool.Rectangle or DrawingTool.Ellipse;
    private void ApplyState()
    {
        var drawing = _model.IsDrawing && _model.ShowInk && !_model.IsBusy;
        var fading = drawing && _model.Tool == DrawingTool.Fading;
        var color = (Color)ColorConverter.ConvertFromString(_model.Color);
        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color, Width = _model.Tool == DrawingTool.Highlighter ? _model.Thickness * 4 : _model.Thickness,
            Height = _model.Tool == DrawingTool.Highlighter ? _model.Thickness * 4 : _model.Thickness,
            IsHighlighter = _model.Tool == DrawingTool.Highlighter, IgnorePressure = !_model.PressureEnabled || _model.Tool == DrawingTool.Highlighter,
            FitToCurve = false
        };
        TemporarySurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color, Width = _model.Thickness, Height = _model.Thickness, IgnorePressure = true, FitToCurve = false
        };
        InkSurface.EditingMode = !drawing || _touchStrokes.Count > 0 || IsShapeTool || _model.Tool is DrawingTool.Text or DrawingTool.Fading or DrawingTool.Pan
            ? InkCanvasEditingMode.None : _model.Tool switch
            {
                DrawingTool.Eraser => InkCanvasEditingMode.EraseByStroke,
                DrawingTool.PartialEraser => InkCanvasEditingMode.EraseByPoint,
                DrawingTool.Select => InkCanvasEditingMode.Select,
                _ => InkCanvasEditingMode.Ink
            };
        InkSurface.EraserShape = new EllipseStylusShape(Math.Max(4, _model.Thickness * 4), Math.Max(4, _model.Thickness * 4));
        TemporarySurface.EditingMode = fading && _touchStrokes.Count == 0 ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        TemporarySurface.IsHitTestVisible = fading;
        TemporarySurface.Visibility = _model.ShowInk && !_suspendHalo ? Visibility.Visible : Visibility.Hidden;
        InkSurface.Visibility = TextLayer.Visibility = _model.ShowInk ? Visibility.Visible : Visibility.Hidden;
        TouchPreview.Visibility = _model.ShowInk && !_suspendHalo ? Visibility.Visible : Visibility.Hidden;
        TextLayer.IsHitTestVisible = drawing && _model.Tool == DrawingTool.Text;
        InkSurface.Background = drawing ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        TemporarySurface.Background = fading ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        InkSurface.Cursor = _model.Tool is DrawingTool.Eraser or DrawingTool.PartialEraser ? Cursors.Cross
            : _model.Tool == DrawingTool.Text ? Cursors.IBeam : _model.Tool == DrawingTool.Pan ? Cursors.Hand : Cursors.Pen;
        if (_source is not null) NativeMethods.SetPassThrough(_source.Handle, !drawing);
        UpdateHaloTimer();
    }

    private void RenderText(IEnumerable<TextLabel> labels)
    {
        TextLayer.Children.Clear();
        foreach (var label in labels)
        {
            var border = new Border
            {
                DataContext = label, Background = Brushes.Transparent, Padding = new Thickness(3), Cursor = Cursors.SizeAll,
                ToolTip = "Drag to move. Double-click to edit or delete.",
                Child = new TextBlock
                {
                    Text = label.Text, FontFamily = new FontFamily(label.FontFamily), FontSize = label.FontSize,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(label.Color))
                }
            };
            System.Windows.Automation.AutomationProperties.SetName(border, "Text label: " + label.Text);
            Canvas.SetLeft(border, label.X); Canvas.SetTop(border, label.Y);
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (!_model.IsDrawing || _model.Tool != DrawingTool.Text) return;
                e.Handled = true;
                if (e.ClickCount == 2) { CancelTextDrag(); TextRequested?.Invoke(new Point(label.X, label.Y), label); return; }
                _textDrag = (border, label, e.GetPosition(TextLayer));
                border.CaptureMouse();
            };
            border.MouseMove += (_, e) =>
            {
                if (_textDrag is not { } drag || drag.Border != border || e.LeftButton != MouseButtonState.Pressed) return;
                var offset = e.GetPosition(TextLayer) - drag.Start;
                Canvas.SetLeft(border, Math.Max(0, drag.Label.X + offset.X));
                Canvas.SetTop(border, Math.Max(0, drag.Label.Y + offset.Y));
            };
            border.MouseLeftButtonUp += (_, e) =>
            {
                if (_textDrag is not { } drag || drag.Border != border) return;
                var x = Canvas.GetLeft(border); var y = Canvas.GetTop(border);
                _textDrag = null;
                border.ReleaseMouseCapture();
                if (Math.Abs(x - label.X) + Math.Abs(y - label.Y) >= 2)
                    _workspace.UpsertText(label with { X = x, Y = y });
                e.Handled = true;
            };
            border.LostMouseCapture += (_, _) => { if (_textDrag?.Border == border) CancelTextDrag(); };
            TextLayer.Children.Add(border);
        }
    }
    private void CancelTextDrag()
    {
        if (_textDrag is not { } drag) return;
        _textDrag = null;
        Canvas.SetLeft(drag.Border, drag.Label.X); Canvas.SetTop(drag.Border, drag.Label.Y);
        if (drag.Border.IsMouseCaptured) drag.Border.ReleaseMouseCapture();
    }
    private bool CanTouch => _model.TouchEnabled && _model.IsDrawing && _model.ShowInk && !_model.IsBusy
        && !(_model.PalmRejection && _penDevices.Count > 0);
    internal bool BeginTouchStroke(int id, Point point)
    {
        if (!CanTouch || _touchStrokes.Count >= 32 || _touchStrokes.ContainsKey(id) || _model.Tool is not (DrawingTool.Pen or DrawingTool.Highlighter or DrawingTool.Fading)) return false;
        var fading = _model.Tool == DrawingTool.Fading;
        var attributes = (fading ? TemporarySurface : InkSurface).DefaultDrawingAttributes.Clone();
        attributes.IgnorePressure = true; // Fingers do not report pen pressure.
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(point.X, point.Y) }, attributes);
        // Stop InkCanvas's single-stylus collector before handling finger sessions ourselves.
        InkSurface.EditingMode = TemporarySurface.EditingMode = InkCanvasEditingMode.None;
        _touchStrokes[id] = (stroke, fading); TouchPreview.Strokes.Add(stroke); return true;
    }
    internal void MoveTouchStroke(int id, Point point)
    {
        if (_touchStrokes.TryGetValue(id, out var pending) && pending.Stroke.StylusPoints.Count < 50_000) pending.Stroke.StylusPoints.Add(new StylusPoint(point.X, point.Y));
    }
    internal void CompleteTouchStroke(int id, Point point)
    {
        if (!_touchStrokes.Remove(id, out var pending)) return;
        pending.Stroke.StylusPoints.Add(new StylusPoint(point.X, point.Y));
        TouchPreview.Strokes.Remove(pending.Stroke);
        if (pending.Fading)
        {
            TemporarySurface.Strokes.Add(pending.Stroke);
            _temporary.Add(pending.Stroke, TimeSpan.FromSeconds(_model.FadeSeconds)); UpdateHaloTimer();
        }
        else InkSurface.Strokes.Add(pending.Stroke);
        ApplyState();
    }
    private void CancelTouchStrokes()
    {
        _touchStrokes.Clear(); TouchPreview.Strokes.Clear();
        var devices = _touchDevices.Values.ToArray(); _touchDevices.Clear();
        foreach (var device in devices) device.Capture(null);
        ApplyState();
    }
    internal void EraseAt(Point point)
    {
        if (_model.Tool == DrawingTool.PartialEraser)
        {
            var result = InkSurface.Strokes.Clone();
            result.Erase(new[] { point }, InkSurface.EraserShape);
            if (InkSerializer.Encode(result) != InkSerializer.Encode(InkSurface.Strokes))
            {
                InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
                InkSurface.Strokes = result; InkSurface.Strokes.StrokesChanged += OnStrokesChanged; OnStrokesChanged(this, null!);
            }
            return;
        }
        var hits = InkSurface.Strokes.Where(stroke => stroke.HitTest(point, Math.Max(8, _model.Thickness * 2))).ToArray();
        if (hits.Length > 0) InkSurface.Strokes.Remove(new StrokeCollection(hits));
    }
    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        if (_model.Tool == DrawingTool.Select && CanTouch) return;
        e.Handled = true;
        if (!CanTouch) return;
        var point = e.GetTouchPoint(InkSurface).Position;
        if (_model.Tool == DrawingTool.Text) { TextRequested?.Invoke(point, null); return; }
        if (_model.Tool == DrawingTool.Pan)
        {
            BeginPan(e.GetTouchPoint(SurfaceRoot).Position); _panTouch = e.TouchDevice; InkSurface.CaptureTouch(e.TouchDevice); return;
        }
        if (IsShapeTool)
        {
            if (_shapeStart is null) { _shapeTouch = e.TouchDevice; BeginShape(point); InkSurface.CaptureTouch(e.TouchDevice); }
            return;
        }
        if (_model.Tool is DrawingTool.Eraser or DrawingTool.PartialEraser) EraseAt(point);
        else if (!BeginTouchStroke(e.TouchDevice.Id, point)) return;
        _touchDevices[e.TouchDevice.Id] = e.TouchDevice;
        ((UIElement)sender).CaptureTouch(e.TouchDevice);
    }
    private void OnTouchMove(object sender, TouchEventArgs e)
    {
        if (_model.Tool == DrawingTool.Select) return;
        e.Handled = true;
        if (_panTouch == e.TouchDevice) { MovePan(e.GetTouchPoint(SurfaceRoot).Position); return; }
        var point = e.GetTouchPoint(InkSurface).Position;
        if (_shapeTouch == e.TouchDevice && _shapeStart is { } start) ShapePreview.Points = ShapePoints(start, point);
        else if (_touchDevices.ContainsKey(e.TouchDevice.Id))
        {
            if (_model.Tool is DrawingTool.Eraser or DrawingTool.PartialEraser) EraseAt(point); else MoveTouchStroke(e.TouchDevice.Id, point);
        }
    }
    private void OnTouchEnd(object sender, TouchEventArgs e)
    {
        if (_model.Tool == DrawingTool.Select) return;
        e.Handled = true;
        if (_panTouch == e.TouchDevice) { MovePan(e.GetTouchPoint(SurfaceRoot).Position); CancelPan(); return; }
        var point = e.GetTouchPoint(InkSurface).Position;
        if (_shapeTouch == e.TouchDevice) { CommitShape(point); return; }
        CompleteTouchStroke(e.TouchDevice.Id, point);
        _touchDevices.Remove(e.TouchDevice.Id); e.TouchDevice.Capture(null);
    }
    private void OnTouchCaptureLost(object sender, TouchEventArgs e)
    {
        if (_shapeTouch == e.TouchDevice) CancelShape();
        if (_panTouch == e.TouchDevice) CancelPan();
        _touchDevices.Remove(e.TouchDevice.Id);
        if (_touchStrokes.Remove(e.TouchDevice.Id, out var pending)) { TouchPreview.Strokes.Remove(pending.Stroke); ApplyState(); }
    }
    private void OnStylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch && !CanTouch) e.Handled = true;
    }
    private void OnPenInRange(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Stylus) return;
        _penDevices.Add(e.StylusDevice.Id);
        if (_model.PalmRejection) { CancelTouchStrokes(); if (_shapeTouch is not null) CancelShape(); }
    }
    private void OnPenOutOfRange(object sender, StylusEventArgs e) => _penDevices.Remove(e.StylusDevice.Id);

    private void BeginPan(Point point)
    {
        if (!_model.CanNavigateCanvas) return;
        _panStart = point; _panOriginX = _model.PanX; _panOriginY = _model.PanY;
    }
    private void MovePan(Point point)
    {
        if (_panStart is { } start) _model.SetView(_model.ZoomLevel, _panOriginX + point.X - start.X, _panOriginY + point.Y - start.Y);
    }
    private void CancelPan()
    {
        _panStart = null; var touch = _panTouch; _panTouch = null;
        if (touch is not null) InkSurface.ReleaseTouchCapture(touch);
        if (InkSurface.IsMouseCaptured) InkSurface.ReleaseMouseCapture();
    }
    private void OnPanStylusUp(object sender, StylusEventArgs e) { if (_panStart is not null && _panTouch is null) CancelPan(); }
    private void OnViewWheel(object sender, MouseWheelEventArgs e)
    {
        if (_model.CanNavigateCanvas && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { ChangeZoom(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(SurfaceRoot)); e.Handled = true; }
    }
    private void OnShapeStart(object sender, MouseButtonEventArgs e)
    {
        if (_model.IsDrawing && _model.Tool == DrawingTool.Pan)
        { BeginPan(e.GetPosition(SurfaceRoot)); InkSurface.CaptureMouse(); e.Handled = true; return; }
        if (_model.IsDrawing && _model.ShowInk && _model.Tool == DrawingTool.Text)
        {
            TextRequested?.Invoke(e.GetPosition(InkSurface), null);
            e.Handled = true;
            return;
        }
        if (!_model.IsDrawing || !IsShapeTool || !_model.ShowInk || _shapeStart is not null) return;
        BeginShape(e.GetPosition(InkSurface));
        InkSurface.CaptureMouse();
        e.Handled = true;
    }

    private void BeginShape(Point start)
    {
        _shapeStart = start;
        ShapePreview.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_model.Color));
        ShapePreview.StrokeThickness = _model.Thickness;
        ShapePreview.Points = new PointCollection();
        ShapePreview.Visibility = Visibility.Visible;
    }

    private void OnShapeMove(object sender, MouseEventArgs e)
    {
        if (_panStart is not null && _panTouch is null) { MovePan(e.GetPosition(SurfaceRoot)); e.Handled = true; return; }
        if (_shapeTouch is not null) return;
        if (_shapeStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelShape(); return; }
        ShapePreview.Points = ShapePoints(start, e.GetPosition(InkSurface));
        e.Handled = true;
    }

    private void OnShapeEnd(object sender, MouseButtonEventArgs e)
    {
        if (_panStart is not null && _panTouch is null) { MovePan(e.GetPosition(SurfaceRoot)); CancelPan(); e.Handled = true; return; }
        if (_shapeStart is null || _shapeTouch is not null) return;
        CommitShape(e.GetPosition(InkSurface));
        e.Handled = true;
    }

    private void CommitShape(Point end)
    {
        if (_shapeStart is not { } start) return;
        var points = ShapePoints(start, end);
        CancelShape();
        if ((end - start).Length >= 2)
        {
            var attributes = InkSurface.DefaultDrawingAttributes.Clone();
            attributes.FitToCurve = false;
            attributes.IgnorePressure = true;
            InkSurface.Strokes.Add(new Stroke(new StylusPointCollection(
                points.Select(point => new StylusPoint(point.X, point.Y))), attributes));
        }
    }

    private PointCollection ShapePoints(Point start, Point end)
    {
        switch (_model.Tool)
        {
            case DrawingTool.Rectangle:
                return new PointCollection { start, new(end.X, start.Y), end, new(start.X, end.Y), start };
            case DrawingTool.Ellipse:
                var center = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
                var rx = Math.Abs(end.X - start.X) / 2;
                var ry = Math.Abs(end.Y - start.Y) / 2;
                return new PointCollection(Enumerable.Range(0, 65).Select(i =>
                    new Point(center.X + rx * Math.Cos(i * Math.PI / 32), center.Y + ry * Math.Sin(i * Math.PI / 32))));
            case DrawingTool.Arrow:
                var vector = end - start;
                if (vector.Length < 1) return new PointCollection { start, end };
                vector.Normalize();
                var headLength = Math.Min((end - start).Length * 0.4, Math.Max(14, _model.Thickness * 4));
                var perpendicular = new Vector(-vector.Y, vector.X);
                var left = end - vector * headLength + perpendicular * headLength * 0.5;
                var right = end - vector * headLength - perpendicular * headLength * 0.5;
                return new PointCollection { start, end, left, end, right };
            default:
                return new PointCollection { start, end };
        }
    }

    private void OnCaptureLost(object sender, MouseEventArgs e) { CancelShape(); _panStart = null; }
    private void CancelShape()
    {
        _shapeStart = null;
        var touch = _shapeTouch;
        _shapeTouch = null;
        ShapePreview.Visibility = Visibility.Collapsed;
        if (InkSurface.IsMouseCaptured) InkSurface.ReleaseMouseCapture();
        if (touch is not null) InkSurface.ReleaseTouchCapture(touch);
    }

    private void UpdateHaloTimer()
    {
        if (_haloTimer is null) return;
        if (IsVisible && (((_model.HaloEnabled || _model.SpotlightEnabled) && !_suspendHalo) || _temporary.Count > 0))
        {
            _haloTimer.Start();
            Tick();
        }
        else { _haloTimer.Stop(); PointerHalo.Visibility = SpotlightMask.Visibility = Visibility.Collapsed; }
    }
    private void Tick()
    {
        foreach (var (stroke, opacity) in _temporary.Tick())
        {
            if (opacity <= 0) { TemporarySurface.Strokes.Remove(stroke); continue; }
            var color = stroke.DrawingAttributes.Color;
            stroke.DrawingAttributes.Color = Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);
        }
        if (_temporary.Count == 0 && !_model.HaloEnabled && !_model.SpotlightEnabled) _haloTimer.Stop();
        if ((!_model.HaloEnabled && !_model.SpotlightEnabled) || _suspendHalo || !IsVisible)
        {
            PointerHalo.Visibility = SpotlightMask.Visibility = Visibility.Collapsed;
            return;
        }
        if (_source is null || !NativeMethods.GetCursorPos(out var cursor)) return;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var inside = point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
        PointerHalo.Visibility = inside && _model.HaloEnabled ? Visibility.Visible : Visibility.Collapsed;
        SpotlightMask.Visibility = inside && _model.SpotlightEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_model.SpotlightEnabled)
        {
            var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            geometry.Children.Add(new EllipseGeometry(point, _model.SpotlightRadius, _model.SpotlightRadius));
            SpotlightMask.Data = geometry;
        }
        Canvas.SetLeft(PointerHalo, point.X - PointerHalo.Width / 2);
        Canvas.SetTop(PointerHalo, point.Y - PointerHalo.Height / 2);
        if (_temporary.Count == 0 && !_model.HaloEnabled && !_model.SpotlightEnabled) _haloTimer.Stop();
    }
    private void Reposition()
    {
        if (_source is not null) NativeMethods.PositionOverlay(_source.Handle, NativeMethods.DesktopBounds());
    }
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDpiChanged)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                CancelPendingShape(); _penDevices.Clear(); Reposition(); DisplayChanged?.Invoke();
                _model.Notice = "Display layout changed. Canvas positions are preserved; check alignment before continuing.";
            }));
        return 0;
    }
}
