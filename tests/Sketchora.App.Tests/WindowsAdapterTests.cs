using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Sketchora.App.Services;
using Sketchora.App.ViewModels;
using Sketchora.App.Views;
using Sketchora.Core;
using Xunit;

namespace Sketchora.App.Tests;

public sealed class WindowsAdapterTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Windows adapter test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static Stroke ExampleStroke() => new(new StylusPointCollection
    {
        new StylusPoint(10, 20, 0.2f), new StylusPoint(30, 40, 0.8f)
    }, new DrawingAttributes { Color = Colors.Red, Width = 4, Height = 4, IgnorePressure = false });

    [Fact]
    public void InkSerializationPreservesGeometryPressureAndPenAttributes() => Sta(() =>
    {
        var source = new StrokeCollection { ExampleStroke() };
        var decoded = InkSerializer.Decode(InkSerializer.Encode(source));
        var stroke = Assert.Single(decoded);
        Assert.Equal(Colors.Red, stroke.DrawingAttributes.Color);
        Assert.Equal(4, stroke.DrawingAttributes.Width);
        Assert.Equal(10, stroke.StylusPoints[0].X);
        Assert.InRange(stroke.StylusPoints[0].PressureFactor, 0.18f, 0.22f);
        Assert.InRange(stroke.StylusPoints[1].PressureFactor, 0.78f, 0.82f);
    });

    [Fact]
    public void InvalidInkInALaterPageDoesNotReplaceTheCurrentWorkspace() => Sta(() =>
    {
        var current = new LessonWorkspace();
        current.UpsertText(new TextLabel { Text = "Keep this" });
        var incoming = new LessonDocument { Pages = [new(), new() { Ink = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) }] };
        Assert.ThrowsAny<Exception>(() => { InkSerializer.Validate(incoming); current.Load(incoming); });
        Assert.Equal("Keep this", Assert.Single(current.ActivePage.Texts).Text);
    });

    [Fact]
    public void FitTransformsEveryCanvasAndTextWithoutChangingTheSource() => Sta(() =>
    {
        var ink = InkSerializer.Encode(new StrokeCollection { ExampleStroke() });
        var source = new LessonDocument
        {
            CanvasWidth = 1000, CanvasHeight = 500,
            Desktop = new LessonPage { Ink = ink, Texts = [new() { Text = "မြန်မာ English", X = 100, Y = 100, FontSize = 32 }] },
            Pages = [new() { Ink = ink }, new() { Ink = ink }]
        };
        var fit = InkSerializer.Resize(source, 500, 500, true);
        Assert.Equal(50, fit.Desktop.Texts[0].X);
        Assert.Equal(175, fit.Desktop.Texts[0].Y);
        Assert.Equal(16, fit.Desktop.Texts[0].FontSize);
        Assert.InRange(InkSerializer.Decode(fit.Pages[1].Ink)[0].StylusPoints[0].Y, 134.9, 135.1);
        Assert.Equal(100, source.Desktop.Texts[0].X);
        Assert.Equal("မြန်မာ English", fit.Desktop.Texts[0].Text);
    });

    [Fact]
    public void OriginalPositionsOptionDoesNotScaleOrMoveAnnotations() => Sta(() =>
    {
        var source = new LessonDocument { Desktop = new LessonPage { Texts = [new() { Text = "Original", X = 120, Y = 240 }] } };
        var resized = InkSerializer.Resize(source, 800, 600, false);
        Assert.Equal(source.Desktop.Texts[0], resized.Desktop.Texts[0]);
        Assert.Equal(800, resized.CanvasWidth);
    });

    [Fact]
    public void ToolbarAndOverlayConstructAndNavigatePagesOnAnStaThread() => Sta(() =>
    {
        var workspace = new LessonWorkspace();
        var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        var overlay = new OverlayWindow(model, workspace);
        var toolbar = new ToolbarWindow(model);
        try
        {
            workspace.UpsertText(new TextLabel { Text = "မြန်မာ\nEnglish" });
            var labels = (Canvas)overlay.FindName("TextLayer");
            Assert.Single(labels.Children.Cast<UIElement>());
            workspace.AddPage();
            Assert.Empty(labels.Children.Cast<UIElement>());
            workspace.ShowDesktop(true);
            Assert.Single(labels.Children.Cast<UIElement>());
            Assert.Contains("2", model.PageLabel);
        }
        finally { toolbar.Close(); overlay.Close(); }
    });

    [Fact]
    public void FadingStrokeCollectionNeverDirtiesOrEntersTheLesson() => Sta(() =>
    {
        var workspace = new LessonWorkspace();
        var model = new ToolbarViewModel(new AppPreferences { PointerHalo = false }, _ => { });
        var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.Tool = DrawingTool.Fading; model.IsDrawing = true;
            var temporary = (InkCanvas)overlay.FindName("TemporarySurface");
            var stroke = ExampleStroke();
            temporary.Strokes.Add(stroke);
            temporary.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
            Assert.False(workspace.IsDirty);
            Assert.Empty(workspace.Snapshot.Desktop.Ink);
            model.Tool = DrawingTool.Pen;
            Assert.Empty(temporary.Strokes);
            Assert.False(workspace.CanUndo);
        }
        finally { overlay.Close(); }
    });
}
