using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using Sketchora.App.Services;
using Sketchora.App.ViewModels;
using Sketchora.App.Views;
using Sketchora.Core;
using Xunit;

namespace Sketchora.App.Tests;

public sealed class AnnotationAdapterTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Adapter test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void ToolWidthsRemainIndependentAcrossSwitchesAndRestart() => Sta(() =>
    {
        var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        model.Thickness = 3; model.Tool = DrawingTool.Highlighter; model.Thickness = 11;
        model.Tool = DrawingTool.Pen; Assert.Equal(3, model.Thickness);
        model.Tool = DrawingTool.Highlighter; Assert.Equal(11, model.Thickness);
        var restarted = new ToolbarViewModel(new AppPreferences { ToolWidths = model.ToolWidths }, _ => { });
        Assert.Equal(3, restarted.Thickness); restarted.Tool = DrawingTool.Highlighter; Assert.Equal(11, restarted.Thickness);
    });
    [Fact]
    public void PaletteContains24DistinctColorsAndAcceptsCustomRgb() => Sta(() =>
    {
        var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        Assert.Equal(24, model.Colors.Distinct().Count());
        model.Color = "#123456"; Assert.Equal("#123456", model.Color);
        model.Color = "invalid"; Assert.Equal("#123456", model.Color);
        Assert.All(Enum.GetValues<CanvasAction>(), action => Assert.Contains(action.ToString(), ShortcutCatalog.Names));
    });
    [Fact]
    public void GhostAndEmergencyHideDispatchDifferentActions() => Sta(() =>
    {
        var dispatched = new List<CanvasAction>();
        var model = new ToolbarViewModel(new AppPreferences(), dispatched.Add) { IsDrawing = true };
        model.GhostCommand.Execute(null); model.HideCommand.Execute(null);
        Assert.Equal(new[] { CanvasAction.Ghost, CanvasAction.Hide }, dispatched);
        Assert.True(model.IsDrawing);
    });
    [Fact]
    public void PressureSettingControlsInkWhileHighlighterUsesConstantWidth() => Sta(() =>
    {
        var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var overlay = new OverlayWindow(model, new LessonWorkspace());
        try
        {
            var ink = (InkCanvas)overlay.FindName("InkSurface");
            model.IsDrawing = true; Assert.False(ink.DefaultDrawingAttributes.IgnorePressure);
            model.PressureEnabled = false; Assert.True(ink.DefaultDrawingAttributes.IgnorePressure);
            model.PressureEnabled = true; model.Tool = DrawingTool.Highlighter; Assert.True(ink.DefaultDrawingAttributes.IgnorePressure);
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void TwoConcurrentTouchStrokesCommitSeparatelyAndUndoIndependently() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.IsDrawing = true;
            Assert.True(overlay.BeginTouchStroke(1, new Point(10, 10)));
            Assert.True(overlay.BeginTouchStroke(2, new Point(100, 100)));
            Assert.False(workspace.IsDirty); Assert.Equal(2, overlay.ActiveTouchStrokeCount);
            overlay.MoveTouchStroke(1, new Point(20, 20)); overlay.MoveTouchStroke(2, new Point(110, 110));
            overlay.CompleteTouchStroke(1, new Point(30, 30)); Assert.Equal(1, overlay.ActiveTouchStrokeCount);
            overlay.CompleteTouchStroke(2, new Point(120, 120));
            Assert.Equal(2, InkSerializer.Decode(workspace.ActivePage.Ink).Count);
            workspace.Undo(); Assert.Single(InkSerializer.Decode(workspace.ActivePage.Ink));
            workspace.Undo(); Assert.Empty(InkSerializer.Decode(workspace.ActivePage.Ink));
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void SwitchingPageOrDisablingTouchCancelsUncommittedFingerInk() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.IsDrawing = true; Assert.True(overlay.BeginTouchStroke(1, new Point(10, 10)));
            workspace.AddPage(); Assert.Equal(0, overlay.ActiveTouchStrokeCount);
            Assert.Empty(workspace.ActivePage.Ink); Assert.Empty(workspace.Snapshot.Desktop.Ink);
            Assert.True(overlay.BeginTouchStroke(2, new Point(20, 20)));
            model.TouchEnabled = false; Assert.Equal(0, overlay.ActiveTouchStrokeCount);
            Assert.False(overlay.BeginTouchStroke(3, new Point(30, 30)));
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void FadingFingerInkIsExcludedFromPermanentState() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { });
        var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.Tool = DrawingTool.Fading; model.IsDrawing = true;
            Assert.True(overlay.BeginTouchStroke(1, new Point(10, 10)));
            overlay.CompleteTouchStroke(1, new Point(30, 30));
            Assert.False(workspace.IsDirty); Assert.Empty(workspace.ActivePage.Ink);
            Assert.Single(((InkCanvas)overlay.FindName("TemporarySurface")).Strokes);
            overlay.ClearTemporary(); Assert.Empty(((InkCanvas)overlay.FindName("TemporarySurface")).Strokes);
            Assert.True(overlay.BeginTouchStroke(2, new Point(50, 50)));
            overlay.ClearTemporary(); Assert.Equal(0, overlay.ActiveTouchStrokeCount);
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void FrozenScreenshotSurvivesBitmapDisposalAndRegionWindowConstruction() => Sta(() =>
    {
        System.Windows.Media.Imaging.BitmapSource source;
        using (var bitmap = new System.Drawing.Bitmap(200, 100))
        {
            bitmap.SetPixel(10, 10, System.Drawing.Color.Red);
            source = ScreenCapture.ToBitmapSource(bitmap);
            var picker = new RegionCaptureWindow(bitmap);
            Assert.Null(picker.Selection); picker.Close();
        }
        Assert.True(source.IsFrozen); Assert.Equal(200, source.PixelWidth); Assert.Equal(100, source.PixelHeight);
        var pixels = new byte[4]; source.CopyPixels(new Int32Rect(10, 10, 1, 1), pixels, 4, 0);
        Assert.Equal(255, pixels[2]);
    });
}
