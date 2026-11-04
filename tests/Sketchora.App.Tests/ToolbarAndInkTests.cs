using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sketchora.App.Services;
using Sketchora.App.ViewModels;
using Sketchora.App.Views;
using Sketchora.Core;
using Xunit;

namespace Sketchora.App.Tests;

public sealed class ToolbarAndInkTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Toolbar test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private static void Show(ToolbarWindow toolbar)
    {
        toolbar.MaxHeight = 660; toolbar.Show(); Pump(); toolbar.UpdateLayout();
    }
    [Fact]
    public void UpgradeAlwaysStartsAsIconRailAndExpansionDoesNotDuplicateActions() => Sta(() =>
    {
        var model = new ToolbarViewModel(new AppPreferences { ExpandedToolbar = true }, _ => { });
        var toolbar = new ToolbarWindow(model);
        try
        {
            Show(toolbar);
            Assert.False(model.IsExpanded); Assert.Equal(60, toolbar.Width);
            var details = (FrameworkElement)toolbar.FindName("DetailsPanel");
            Assert.Equal(Visibility.Collapsed, details.Visibility);
            foreach (var name in new[] { "ToolPopup", "ColourPopup", "CapturePopup" })
            {
                var popup = (Popup)toolbar.FindName(name); popup.IsOpen = true; Pump(); popup.IsOpen = false; Pump();
            }
            var actions = Descendants(toolbar.Content as DependencyObject).OfType<ButtonBase>()
                .Where(button => button.Command is not null && button.Tag is string).ToArray();
            Assert.Single(actions, button => button.Command == model.SaveLessonCommand);
            Assert.Single(actions, button => button.Command == model.CycleBoardCommand);
            Assert.Single(actions, button => button.Command == model.ExportCommand);
            Assert.Equal(actions.Length, actions.Select(button => button.Tag).Distinct().Count());
            model.CollapseCommand.Execute(null); Pump(); toolbar.UpdateLayout();
            Assert.True(model.IsExpanded); Assert.Equal(344, toolbar.Width); Assert.Equal(Visibility.Visible, details.Visibility);
            model.CollapseCommand.Execute(null); Pump(); Assert.Equal(60, toolbar.Width);
        }
        finally { toolbar.Close(); }
    });
    [Fact]
    public void HoverTipsFollowRemappedAndUnassignedKeysAndKeepLocalKeysDistinct() => Sta(() =>
    {
        var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var toolbar = new ToolbarWindow(model);
        try
        {
            Show(toolbar);
            var toolPopup = (Popup)toolbar.FindName("ToolPopup"); toolPopup.IsOpen = true; Pump();
            var pen = (Button)toolbar.FindName("PenChoice");
            var save = (Button)toolbar.FindName("SaveLessonButton");
            BindingOperations.GetMultiBindingExpression(pen, FrameworkElement.ToolTipProperty)?.UpdateTarget();
            Assert.Contains("Ctrl + Alt + P", pen.ToolTip.ToString());
            var keys = ShortcutCatalog.Defaults(); keys["Tool.Pen"] = 0x7B; // F12
            model.UpdateShortcuts(keys); Pump();
            BindingOperations.GetMultiBindingExpression(pen, FrameworkElement.ToolTipProperty)?.UpdateTarget();
            Assert.Contains("Ctrl + Alt + F12", pen.ToolTip.ToString());
            Assert.Contains("F12", model.PrimaryToolTip);
            keys["Tool.Pen"] = 0; model.UpdateShortcuts(keys); Pump();
            BindingOperations.GetMultiBindingExpression(pen, FrameworkElement.ToolTipProperty)?.UpdateTarget();
            Assert.Contains("not assigned", pen.ToolTip.ToString()); Assert.DoesNotContain("Ctrl + Alt + P", pen.ToolTip.ToString());
            toolPopup.IsOpen = false; model.IsExpanded = true; Pump();
            foreach (var name in new[] { "ColourPopup", "CapturePopup" })
            {
                var popup = (Popup)toolbar.FindName(name); popup.IsOpen = true; Pump(); popup.IsOpen = false; Pump();
            }
            BindingOperations.GetMultiBindingExpression(save, FrameworkElement.ToolTipProperty)?.UpdateTarget();
            Assert.Contains("Ctrl + S (toolbar focused)", save.ToolTip.ToString());
            Assert.All(Descendants(toolbar.Content as DependencyObject).OfType<ButtonBase>()
                .Where(button => button.Tag is string), button => Assert.NotNull(button.ToolTip));
        }
        finally { toolbar.Close(); }
    });
    [Theory]
    [InlineData(DrawingTool.Pen)]
    [InlineData(DrawingTool.Highlighter)]
    public void PenCornersAreNotCurveFittedOnCompletionOrHistory(DrawingTool tool) => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.Tool = tool; model.IsDrawing = true;
            var ink = (InkCanvas)overlay.FindName("InkSurface");
            Assert.False(ink.DefaultDrawingAttributes.FitToCurve);
            var points = new StylusPointCollection { new StylusPoint(10, 10, .4f), new StylusPoint(20, 40, .7f), new StylusPoint(30, 10, .6f), new StylusPoint(40, 40, .5f) };
            var stroke = new Stroke(points.Clone(), ink.DefaultDrawingAttributes.Clone());
            ink.Strokes.Add(stroke);
            ink.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
            Assert.False(stroke.DrawingAttributes.FitToCurve);
            Assert.Equal(points.Select(p => (p.X, p.Y)), stroke.StylusPoints.Select(p => (p.X, p.Y)));
            workspace.Undo(); workspace.Redo();
            var restored = Assert.Single(InkSerializer.Decode(workspace.ActivePage.Ink));
            Assert.False(restored.DrawingAttributes.FitToCurve); Assert.Equal(points.Count, restored.StylusPoints.Count);
            for (var i = 0; i < points.Count; i++)
            {
                Assert.InRange(Math.Abs(points[i].X - restored.StylusPoints[i].X), 0, .05);
                Assert.InRange(Math.Abs(points[i].Y - restored.StylusPoints[i].Y), 0, .05);
            }
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void FingerCornersAndTemporaryInkUseTheSameUnsmoothedPath() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.IsDrawing = true; overlay.BeginTouchStroke(1, new Point(10, 10));
            overlay.MoveTouchStroke(1, new Point(20, 40)); overlay.MoveTouchStroke(1, new Point(30, 10));
            overlay.CompleteTouchStroke(1, new Point(40, 40));
            var stroke = Assert.Single(InkSerializer.Decode(workspace.ActivePage.Ink));
            Assert.False(stroke.DrawingAttributes.FitToCurve); Assert.Equal(4, stroke.StylusPoints.Count);
            Assert.InRange(stroke.StylusPoints[1].Y, 39.95, 40.05);
            model.Tool = DrawingTool.Fading;
            Assert.False(((InkCanvas)overlay.FindName("TemporarySurface")).DefaultDrawingAttributes.FitToCurve);
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void ToolbarRendersAtCompactAndExpandedSizesAndUsesEmbeddedAppIcon() => Sta(() =>
    {
        var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var toolbar = new ToolbarWindow(model);
        try
        {
            Assert.NotNull(toolbar.Icon); Assert.True(toolbar.Icon.Width > 0);
            Show(toolbar);
            var root = (FrameworkElement)toolbar.Content;
            foreach (var expanded in new[] { false, true })
            {
                model.IsExpanded = expanded; Pump(); toolbar.UpdateLayout();
                var width = expanded ? 344 : 60;
                root.Measure(new Size(width, 660));
                var height = Math.Min(660, Math.Ceiling(root.DesiredSize.Height));
                root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                Assert.True(root.ActualHeight > 300); Assert.InRange(root.ActualWidth, width - 1, width + 1);
                var bitmap = new RenderTargetBitmap(width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Assert.True(pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0) > bitmap.PixelWidth * bitmap.PixelHeight / 2, "The toolbar render must contain real visible UI.");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var directory = Path.Combine(AppContext.BaseDirectory, "TestResults"); Directory.CreateDirectory(directory);
                using var file = File.Create(Path.Combine(directory, expanded ? "toolbar-expanded.png" : "toolbar-compact.png")); encoder.Save(file); file.Close();
                Console.WriteLine("SKETCHORA_PREVIEW:" + (expanded ? "toolbar-expanded" : "toolbar-compact") + ":" + Convert.ToBase64String(File.ReadAllBytes(file.Name)));
            }
        }
        finally { toolbar.Close(); }
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null) yield break;
        yield return root;
        if (root is Popup { Child: { } child }) { foreach (var nested in Descendants(child)) yield return nested; yield break; }
        foreach (var item in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(item)) yield return nested;
    }
}
