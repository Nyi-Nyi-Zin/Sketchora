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

namespace Sketchora.App.Views;

public partial class OverlayWindow : Window
{
    private readonly ToolbarViewModel _model;
    private readonly InkDocument _desktop = new();
    private readonly InkDocument _board = new();
    private readonly DispatcherTimer _haloTimer;
    private InkDocument _active;
    private HwndSource? _source;
    private bool _restoring;
    private Point? _shapeStart;
    private TouchDevice? _shapeTouch;
    private bool _suspendHalo;

    public OverlayWindow(ToolbarViewModel model)
    {
        _model = model;
        _active = _desktop;
        InitializeComponent();
        InkSurface.Strokes = _active.Strokes;
        InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
        _model.PropertyChanged += OnModelChanged;
        _haloTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background,
            (_, _) => UpdateHalo(), Dispatcher);
        _haloTimer.Stop();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            Reposition();
            ApplyState();
        };
        IsVisibleChanged += (_, _) => UpdateHaloTimer();
        Closed += (_, _) =>
        {
            _haloTimer.Stop();
            _model.PropertyChanged -= OnModelChanged;
            InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
            _source?.RemoveHook(WndProc);
        };
        UpdateHistoryState();
    }

    public bool HasInk => _desktop.Strokes.Count > 0 || _board.Strokes.Count > 0;
    public bool SuspendHalo
    {
        get => _suspendHalo;
        set { _suspendHalo = value; UpdateHaloTimer(); }
    }

    public void Undo() { if (_active.History.CanUndo) ReplaceStrokes(_active.History.Undo()); }
    public void Redo() { if (_active.History.CanRedo) ReplaceStrokes(_active.History.Redo()); }
    public void ClearInk() { CancelShape(); if (InkSurface.Strokes.Count > 0) InkSurface.Strokes.Clear(); }
    public void CancelPendingShape() => CancelShape();

    private void ReplaceStrokes(StrokeCollection strokes)
    {
        CancelShape();
        _restoring = true;
        try
        {
            InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
            _active.Strokes = strokes;
            InkSurface.Strokes = strokes;
            InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
        }
        finally { _restoring = false; }
        UpdateHistoryState();
    }

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (!_restoring) _active.History.Capture(InkSurface.Strokes);
        UpdateHistoryState();
    }

    private void UpdateHistoryState() => _model.UpdateHistory(
        _active.History.CanUndo, _active.History.CanRedo, _active.Strokes.Count);

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarViewModel.Board))
        {
            var document = _model.Board == BoardStyle.Desktop ? _desktop : _board;
            if (document != _active)
            {
                InkSurface.Strokes.StrokesChanged -= OnStrokesChanged;
                _active = document;
                InkSurface.Strokes = _active.Strokes;
                InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
                UpdateHistoryState();
            }
        }
        if (e.PropertyName is nameof(ToolbarViewModel.IsDrawing) or nameof(ToolbarViewModel.Tool)
            or nameof(ToolbarViewModel.Board) or nameof(ToolbarViewModel.ShowInk)) CancelShape();
        if (e.PropertyName is nameof(ToolbarViewModel.IsDrawing) or nameof(ToolbarViewModel.Tool)
            or nameof(ToolbarViewModel.Board) or nameof(ToolbarViewModel.ShowInk)
            or nameof(ToolbarViewModel.Color) or nameof(ToolbarViewModel.Thickness)
            or nameof(ToolbarViewModel.HaloEnabled) or nameof(ToolbarViewModel.TouchEnabled)) ApplyState();
    }

    private bool IsShapeTool => _model.Tool is DrawingTool.Line or DrawingTool.Arrow
        or DrawingTool.Rectangle or DrawingTool.Ellipse;

    private void ApplyState()
    {
        var color = (Color)ColorConverter.ConvertFromString(_model.Color);
        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = _model.Tool == DrawingTool.Highlighter ? _model.Thickness * 4 : _model.Thickness,
            Height = _model.Tool == DrawingTool.Highlighter ? _model.Thickness * 4 : _model.Thickness,
            IsHighlighter = _model.Tool == DrawingTool.Highlighter,
            IgnorePressure = _model.Tool == DrawingTool.Highlighter,
            FitToCurve = true
        };
        InkSurface.EditingMode = !_model.IsDrawing || !_model.ShowInk || IsShapeTool
            ? InkCanvasEditingMode.None
            : _model.Tool == DrawingTool.Eraser ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
        InkSurface.Visibility = _model.ShowInk ? Visibility.Visible : Visibility.Hidden;
        InkSurface.Background = _model.IsDrawing && _model.ShowInk
            ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        SurfaceRoot.Background = _model.Board switch
        {
            BoardStyle.White => Brushes.White,
            BoardStyle.Black => new SolidColorBrush(Color.FromRgb(18, 20, 28)),
            _ => new SolidColorBrush(Color.FromArgb(_model.IsDrawing && _model.ShowInk ? (byte)1 : (byte)0, 0, 0, 0))
        };
        InkSurface.Cursor = _model.Tool == DrawingTool.Eraser ? Cursors.Cross : Cursors.Pen;
        if (_source is not null)
            NativeMethods.SetPassThrough(_source.Handle, !_model.IsDrawing || !_model.ShowInk);
        UpdateHaloTimer();
    }

    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        if (!_model.TouchEnabled)
        {
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
        if (_model.HaloEnabled && IsVisible && !_suspendHalo)
        {
            _haloTimer.Start();
            UpdateHalo();
        }
        else
        {
            _haloTimer.Stop();
            PointerHalo.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateHalo()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var inside = point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
        PointerHalo.Visibility = inside ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(PointerHalo, point.X - PointerHalo.Width / 2);
        Canvas.SetTop(PointerHalo, point.Y - PointerHalo.Height / 2);
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
