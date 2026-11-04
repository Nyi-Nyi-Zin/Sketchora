using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Sketchora.App.Interop;
using Sketchora.App.Services;
using Sketchora.Core;

namespace Sketchora.App.Views;

/// <summary>Select from a frozen desktop image; no selection chrome enters the capture.</summary>
internal sealed class RegionCaptureWindow : Window
{
    public ScreenRegion? Selection { get; private set; }
    public bool CopyRequested { get; private set; }
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Rectangle _selection = new() { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 99, 102, 241)), IsHitTestVisible = false };
    private Point? _start;
    private readonly int _pixelWidth, _pixelHeight;
    private HwndSource? _source;

    public RegionCaptureWindow(System.Drawing.Bitmap bitmap)
    {
        _pixelWidth = bitmap.Width; _pixelHeight = bitmap.Height;
        Title = "Select screenshot region"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = false; Background = Brushes.Black; Cursor = Cursors.Cross;
        var root = new Grid();
        root.Children.Add(new Image { Source = ScreenCapture.ToBitmapSource(bitmap), Stretch = Stretch.Fill });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), IsHitTestVisible = false });
        _canvas.Children.Add(_selection); root.Children.Add(_canvas);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.White, Margin = new Thickness(12), Cursor = Cursors.Arrow };
        panel.Children.Add(new TextBlock { Text = "Drag a region · Esc cancels", Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Black });
        Button Add(string label)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(4) };
            panel.Children.Add(button); return button;
        }
        var save = Add("Save PNG"); var copy = Add("Copy"); var cancel = Add("Cancel");
        save.IsEnabled = copy.IsEnabled = false; cancel.IsCancel = true;
        save.Click += (_, _) => { if (Selection is not null) DialogResult = true; };
        copy.Click += (_, _) => { if (Selection is not null) { CopyRequested = true; DialogResult = true; } };
        root.Children.Add(panel); Content = root;
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            Selection = null; save.IsEnabled = copy.IsEnabled = false;
            _start = e.GetPosition(_canvas); _canvas.CaptureMouse(); Draw(_start.Value, _start.Value); e.Handled = true;
        };
        _canvas.MouseMove += (_, e) => { if (_start is { } start) Draw(start, e.GetPosition(_canvas)); };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } start) return;
            var end = e.GetPosition(_canvas); _start = null; _canvas.ReleaseMouseCapture();
            Selection = ScreenRegion.FromSelection(start.X, start.Y, end.X, end.Y, _canvas.ActualWidth, _canvas.ActualHeight, _pixelWidth, _pixelHeight);
            save.IsEnabled = copy.IsEnabled = Selection is not null; e.Handled = true;
        };
        _canvas.LostMouseCapture += (_, _) => { _start = null; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            NativeMethods.PositionOverlay(new WindowInteropHelper(this).Handle, NativeMethods.DesktopBounds());
        };
        Closed += (_, _) => _source?.RemoveHook(WndProc);
    }
    private void Draw(Point start, Point end)
    {
        Canvas.SetLeft(_selection, Math.Min(start.X, end.X)); Canvas.SetTop(_selection, Math.Min(start.Y, end.Y));
        _selection.Width = Math.Abs(end.X - start.X); _selection.Height = Math.Abs(end.Y - start.Y);
    }
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmDisplayChange) Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) DialogResult = false; }));
        return 0;
    }
}
