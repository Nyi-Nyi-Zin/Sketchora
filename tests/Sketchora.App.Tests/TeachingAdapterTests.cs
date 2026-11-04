using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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

public sealed class TeachingAdapterTests
{
    private static void Sta(Action action) => StaAsync(() => { action(); return Task.CompletedTask; });
    private static void StaAsync(Func<Task> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); } catch (Exception ex) { error = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)), "Teaching adapter timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static Stroke Line() => new(new StylusPointCollection(Enumerable.Range(0, 101).Select(i => new StylusPoint(i + 10, 50))),
        new DrawingAttributes { Width = 3, Height = 3, FitToCurve = false, Color = Colors.Blue });
    private static BitmapSource Frame()
    {
        var bitmap = new RenderTargetBitmap(200, 100, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0, 0, 200, 100));
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    [Fact]
    public void SelectionCopiesDeletesAndCommitsMovesAsUndoableInk() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.IsDrawing = true; model.Tool = DrawingTool.Select;
            var ink = (InkCanvas)overlay.FindName("InkSurface"); ink.Strokes.Add(Line()); ink.Select(ink.Strokes);
            Assert.Equal(InkCanvasEditingMode.Select, ink.EditingMode); Assert.Equal(1, model.SelectionCount);
            overlay.DuplicateSelection(); Assert.Equal(2, ink.Strokes.Count); Assert.Single(ink.GetSelectedStrokes());
            var copied = ink.GetSelectedStrokes()[0]; copied.Transform(new Matrix(1, 0, 0, 1, 50, 20), false);
            overlay.CommitSelection();
            Assert.InRange(InkSerializer.Decode(workspace.ActivePage.Ink)[1].StylusPoints[0].X, 79.9, 80.1);
            workspace.Undo(); Assert.InRange(InkSerializer.Decode(workspace.ActivePage.Ink)[1].StylusPoints[0].X, 29.9, 30.1);
            ink.Select(new StrokeCollection { ink.Strokes[1] }); overlay.DeleteSelection(); Assert.Single(ink.Strokes);
            workspace.Undo(); Assert.Equal(2, ink.Strokes.Count);
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void PartialEraserCutsTheMiddleAndKeepsBothEndsWithUndo() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.IsDrawing = true; model.Tool = DrawingTool.PartialEraser;
            var ink = (InkCanvas)overlay.FindName("InkSurface"); ink.Strokes.Add(Line());
            Assert.Equal(InkCanvasEditingMode.EraseByPoint, ink.EditingMode);
            overlay.EraseAt(new Point(60, 50));
            Assert.Equal(2, ink.Strokes.Count); Assert.True(ink.Strokes[0].GetBounds().Right < 60); Assert.True(ink.Strokes[1].GetBounds().Left > 60);
            workspace.Undo(); Assert.Single(ink.Strokes); Assert.InRange(ink.Strokes[0].GetBounds().Width, 102, 104);
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void FreezeIsEphemeralAndInteractionReleasesItWithoutLosingInk() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var overlay = new OverlayWindow(model, workspace);
        try
        {
            model.IsDrawing = true; ((InkCanvas)overlay.FindName("InkSurface")).Strokes.Add(Line());
            var revision = workspace.Revision; overlay.SetFrozenFrame(Frame());
            Assert.True(model.FrozenScreen); Assert.True(model.CanNavigateCanvas); Assert.Equal(revision, workspace.Revision);
            Assert.Empty(workspace.Snapshot.Desktop.ImagePng); Assert.NotNull(((Image)overlay.FindName("PageImage")).Source);
            model.IsDrawing = false;
            Assert.False(model.FrozenScreen); Assert.Null(((Image)overlay.FindName("PageImage")).Source); Assert.Equal(revision, workspace.Revision);
            Assert.Single(InkSerializer.Decode(workspace.ActivePage.Ink));
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void ZoomAndPanAffectOnlyViewTransformAndResetOnPageChange() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); workspace.ShowDesktop(false);
        var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var overlay = new OverlayWindow(model, workspace);
        try
        {
            ((InkCanvas)overlay.FindName("InkSurface")).Strokes.Add(Line()); var original = workspace.ActivePage.Ink; var revision = workspace.Revision;
            overlay.ChangeZoom(2, new Point(100, 100));
            Assert.Equal(2, model.ZoomLevel); Assert.Equal(-100, model.PanX); Assert.Equal(-100, model.PanY);
            model.SetView(2, -50, -75);
            var transform = (MatrixTransform)((FrameworkElement)overlay.FindName("CanvasContent")).RenderTransform;
            Assert.Equal(2, transform.Matrix.M11); Assert.Equal(-50, transform.Matrix.OffsetX);
            Assert.Equal(original, workspace.ActivePage.Ink); Assert.Equal(revision, workspace.Revision);
            workspace.AddPage(); Assert.Equal(1, model.ZoomLevel); Assert.Equal(0, model.PanX); Assert.Equal(0, model.PanY);
        }
        finally { overlay.Close(); }
    });
    [Fact]
    public void ImportedImagePlacementTransformsTogetherWithInkAndText() => Sta(() =>
    {
        var image = LessonRenderer.EncodePng(Frame());
        var source = new LessonDocument { SchemaVersion = 2, CanvasWidth = 1000, CanvasHeight = 500,
            Pages = [new LessonPage { ImagePng = image, ImagePlacement = new PageImageRect(100, 50, 800, 400), Texts = [new TextLabel { Text = "မြန်မာ", X = 100, Y = 50 }] }] };
        var resized = InkSerializer.Resize(source, 500, 500, true);
        Assert.Equal(new PageImageRect(50, 150, 400, 200), resized.Pages[0].ImagePlacement);
        Assert.Equal(50, resized.Pages[0].Texts[0].X); Assert.Equal(150, resized.Pages[0].Texts[0].Y);
        var original = InkSerializer.Resize(source, 500, 500, false); Assert.Equal(source.Pages[0].ImagePlacement, original.Pages[0].ImagePlacement);
    });
    [Fact]
    public void PdfExportIsReadableByWindowsAndImportsMultipleRasterPages() => StaAsync(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            var image = LessonRenderer.EncodePng(Frame());
            var first = new LessonPage { ImagePng = image, Pattern = PagePattern.Grid, Texts = [new TextLabel { Text = "မြန်မာ English", X = 30, Y = 20 }] };
            var second = new LessonPage { Background = PageBackground.Black, Pattern = PagePattern.Ruled, Ink = InkSerializer.Encode(new StrokeCollection { Line() }) };
            RasterPdf.Save(path, [LessonRenderer.RenderPage(first, 800, 600), LessonRenderer.RenderPage(second, 800, 600)]);
            var seen = 0;
            var imported = await PdfImport.ReadAsync(path, count => { seen = count; return (1, count); });
            Assert.Equal(2, seen); Assert.Equal(2, imported.Count);
            Assert.All(imported, page => { EmbeddedPng.Validate(page.ImagePng); Assert.True(LessonRenderer.DecodeImage(page.ImagePng).PixelWidth > 100); });
            var workspace = new LessonWorkspace(); workspace.ImportPages(imported); Assert.Equal(2, workspace.PageCount);
            await Assert.ThrowsAsync<OperationCanceledException>(() => PdfImport.ReadAsync(path, _ => null));
            await Assert.ThrowsAsync<OperationCanceledException>(() => PdfImport.ReadAsync(path, _ => (1, 2), () => true));
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public void ImageFileImportEmbedsPixelsWithoutKeepingAnExternalFileDependency() => Sta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Frame()));
            using (var file = File.Create(path)) encoder.Save(file);
            var page = LessonRenderer.ImportImage(path); File.Delete(path);
            var image = LessonRenderer.DecodeImage(page.ImagePng);
            Assert.Equal(200, image.PixelWidth); Assert.Equal(100, image.PixelHeight);
            var workspace = new LessonWorkspace(); workspace.ImportPages(new[] { page });
            Assert.NotNull(workspace.ActivePage.ImagePlacement);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public void InvalidPngIsRejectedBeforeReplacingAWorkspace() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); workspace.UpsertText(new TextLabel { Text = "Keep this" });
        var malformed = new byte[40]; byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10]; signature.CopyTo(malformed, 0);
        "IHDR"u8.CopyTo(malformed.AsSpan(12)); malformed[19] = malformed[23] = 1;
        var lesson = new LessonDocument { SchemaVersion = 2, Pages = [new LessonPage { ImagePng = Convert.ToBase64String(malformed) }] };
        Assert.ThrowsAny<Exception>(() => { InkSerializer.Validate(lesson); workspace.Load(lesson); });
        Assert.Equal("Keep this", Assert.Single(workspace.ActivePage.Texts).Text);
    });
    [Fact]
    public void GridAndRuledBackgroundsRenderDifferentlyAndDoNotAddAuthoredInk() => Sta(() =>
    {
        var plain = LessonRenderer.RenderPage(new LessonPage(), 800, 600);
        var grid = LessonRenderer.RenderPage(new LessonPage { Pattern = PagePattern.Grid }, 800, 600);
        var ruled = LessonRenderer.RenderPage(new LessonPage { Pattern = PagePattern.Ruled }, 800, 600);
        Assert.False(plain.Jpeg.SequenceEqual(grid.Jpeg)); Assert.False(grid.Jpeg.SequenceEqual(ruled.Jpeg));
        var workspace = new LessonWorkspace(); workspace.ShowDesktop(false); workspace.SetPattern(PagePattern.Grid);
        Assert.Empty(workspace.ActivePage.Ink); Assert.Equal(2, workspace.Snapshot.SchemaVersion);
    });
    [Fact]
    public void SpotlightIsSeparateFromAuthoredStateAndHasAnAdjustableRadius() => Sta(() =>
    {
        var workspace = new LessonWorkspace(); var model = new ToolbarViewModel(new AppPreferences(), _ => { }); var overlay = new OverlayWindow(model, workspace);
        try
        {
            overlay.Show(); model.SpotlightEnabled = true; model.SpotlightRadius = 170;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Assert.Equal(170, model.SpotlightRadius); Assert.False(workspace.IsDirty);
            overlay.SuspendHalo = true;
            Assert.Equal(Visibility.Collapsed, ((UIElement)overlay.FindName("SpotlightMask")).Visibility);
            Assert.Empty(workspace.Snapshot.Desktop.Ink);
        }
        finally { overlay.Close(); }
    });
}
