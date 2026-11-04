using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private Guid _renderedPage;
    private string _renderedInk = "";
    private TextLabel[] _renderedTexts = [];
    private Point? _shapeStart;
    private TouchDevice? _shapeTouch;
    private (Border Border, TextLabel Label, Point Start)? _textDrag;
    public event Action<Point, TextLabel?>? TextRequested;
    public Size CanvasSize
    {
        get
        {
            var bounds = NativeMethods.PrimaryDisplayBounds();
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
        IsVisibleChanged += (_, _) => UpdateHaloTimer();
        Closed += (_, _) =>
        {
            _haloTimer.Stop();
            _model.PropertyChanged -= OnModelChanged;
            _workspace.Changed -= OnWorkspaceChanged;
            InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
            _source?.RemoveHook(WndProc);
        };
    }

    public bool SuspendHalo
    {
        get => _suspendHalo;
        set { _suspendHalo = value; ApplyState(); }
    }
    public void ClearTemporary()
    {
        _temporary.Clear();
        TemporarySurface.Strokes.Clear();
        UpdateHaloTimer();
    }
    public void CancelPendingShape()
    {
        CancelShape();
        CancelTextDrag();
        TemporarySurface.EditingMode = InkCanvasEditingMode.None;
        ClearTemporary();
    }
    private void OnWorkspaceChanged(object? sender, EventArgs e) => RefreshCanvas();

    private void RefreshCanvas()
    {
        var page = _workspace.ActivePage;
        if (page.Id != _renderedPage)
        {
            CancelShape();
            CancelTextDrag();
            ClearTemporary();
        }
        if (!_syncing && (page.Id != _renderedPage || page.Ink != _renderedInk || !page.Texts.SequenceEqual(_renderedTexts)))
        {
            CancelShape();
            CancelTextDrag();
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
        ApplyState();
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
        if (e.PropertyName is nameof(ToolbarViewModel.IsDrawing) or nameof(ToolbarViewModel.Tool) or nameof(ToolbarViewModel.ShowInk))
        {
            CancelShape();
            CancelTextDrag();
            TemporarySurface.EditingMode = InkCanvasEditingMode.None;
            ClearTemporary();
        }
        if (e.PropertyName is nameof(ToolbarViewModel.IsDrawing) or nameof(ToolbarViewModel.Tool)
            or nameof(ToolbarViewModel.ShowInk) or nameof(ToolbarViewModel.Color) or nameof(ToolbarViewModel.Thickness)
            or nameof(ToolbarViewModel.HaloEnabled) or nameof(ToolbarViewModel.TouchEnabled) or nameof(ToolbarViewModel.IsBusy)) ApplyState();
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
            IsHighlighter = _model.Tool == DrawingTool.Highlighter, IgnorePressure = _model.Tool == DrawingTool.Highlighter,
            FitToCurve = true
        };
        TemporarySurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color, Width = _model.Thickness, Height = _model.Thickness, IgnorePressure = true, FitToCurve = true
        };
        InkSurface.EditingMode = !drawing || IsShapeTool || _model.Tool is DrawingTool.Text or DrawingTool.Fading
            ? InkCanvasEditingMode.None : _model.Tool == DrawingTool.Eraser ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
        TemporarySurface.EditingMode = fading ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        TemporarySurface.IsHitTestVisible = fading;
        TemporarySurface.Visibility = _model.ShowInk && !_suspendHalo ? Visibility.Visible : Visibility.Hidden;
        InkSurface.Visibility = TextLayer.Visibility = _model.ShowInk ? Visibility.Visible : Visibility.Hidden;
        TextLayer.IsHitTestVisible = drawing && _model.Tool == DrawingTool.Text;
        InkSurface.Background = drawing ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        TemporarySurface.Background = fading ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        SurfaceRoot.Background = !_workspace.IsDesktop
            ? _workspace.ActivePage.Background == PageBackground.White ? Brushes.White : new SolidColorBrush(Color.FromRgb(18, 20, 28))
            : Brushes.Transparent;
        InkSurface.Cursor = _model.Tool == DrawingTool.Eraser ? Cursors.Cross : _model.Tool == DrawingTool.Text ? Cursors.IBeam : Cursors.Pen;
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
    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        if (!_model.TouchEnabled)
        {
            e.Handled = true;
            return;
        }
        if (_model.IsDrawing && _model.ShowInk && _model.Tool == DrawingTool.Text)
        {
            TextRequested?.Invoke(e.GetTouchPoint(InkSurface).Position, null);
            e.Handled = true;
            return;
        }
        if (!_model.IsDrawing || !_model.ShowInk || !IsShapeTool) return;
        if (_shapeStart is null)
        {
            _shapeTouch = e.TouchDevice;
            BeginShape(e.GetTouchPoint(InkSurface).Position);
            InkSurface.CaptureTouch(e.TouchDevice);
        }
        e.Handled = true;
    }

    private void OnTouchMove(object sender, TouchEventArgs e)
    {
        if (_shapeTouch != e.TouchDevice || _shapeStart is not { } start) return;
        ShapePreview.Points = ShapePoints(start, e.GetTouchPoint(InkSurface).Position);
        e.Handled = true;
    }

    private void OnTouchEnd(object sender, TouchEventArgs e)
    {
        if (_shapeTouch != e.TouchDevice) return;
        CommitShape(e.GetTouchPoint(InkSurface).Position);
        e.Handled = true;
    }

    private void OnTouchCaptureLost(object sender, TouchEventArgs e)
    {
        if (_shapeTouch == e.TouchDevice) CancelShape();
    }

    private void OnStylusDown(object sender, StylusDownEventArgs e)
    {
        if (!_model.TouchEnabled && e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch)
            e.Handled = true;
    }

    private void OnShapeStart(object sender, MouseButtonEventArgs e)
    {
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
        if (_shapeTouch is not null) return;
        if (_shapeStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelShape(); return; }
        ShapePreview.Points = ShapePoints(start, e.GetPosition(InkSurface));
        e.Handled = true;
    }

    private void OnShapeEnd(object sender, MouseButtonEventArgs e)
    {
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

    private void OnCaptureLost(object sender, MouseEventArgs e) => CancelShape();
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
        if (IsVisible && ((_model.HaloEnabled && !_suspendHalo) || _temporary.Count > 0))
        {
            _haloTimer.Start();
            Tick();
        }
        else { _haloTimer.Stop(); PointerHalo.Visibility = Visibility.Collapsed; }
    }
    private void Tick()
    {
        foreach (var (stroke, opacity) in _temporary.Tick())
        {
            if (opacity <= 0) { TemporarySurface.Strokes.Remove(stroke); continue; }
            var color = stroke.DrawingAttributes.Color;
            stroke.DrawingAttributes.Color = Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);
        }
        if (_temporary.Count == 0 && !_model.HaloEnabled) _haloTimer.Stop();
        if (!_model.HaloEnabled || _suspendHalo || !IsVisible)
        {
            PointerHalo.Visibility = Visibility.Collapsed;
            return;
        }
        if (_source is null || !NativeMethods.GetCursorPos(out var cursor)) return;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var inside = point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
        PointerHalo.Visibility = inside ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(PointerHalo, point.X - PointerHalo.Width / 2);
        Canvas.SetTop(PointerHalo, point.Y - PointerHalo.Height / 2);
        if (_temporary.Count == 0 && !_model.HaloEnabled) _haloTimer.Stop();
    }
    private void Reposition()
    {
        if (_source is not null) NativeMethods.PositionOverlay(_source.Handle, NativeMethods.PrimaryDisplayBounds());
    }
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDpiChanged)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reposition));
        return 0;
    }
}
