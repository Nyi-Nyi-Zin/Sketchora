using System.Text;
using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class TeachingFeatureTests
{
    public const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP48Os/AAXHAup2PuarAAAAAElFTkSuQmCC";
    [Fact]
    public void OldShortcutMapsUpgradeWithoutChangingExistingKeys()
    {
        var old = ShortcutCatalog.Defaults(); old.Remove("Tool.Select"); old.Remove("ExportPdf"); old.Remove("Freeze");
        var upgraded = new AppPreferences { Shortcuts = old }.Normalize().EffectiveShortcuts();
        Assert.True(ShortcutCatalog.IsValid(upgraded)); Assert.Equal(old["Draw"], upgraded["Draw"]);
        Assert.Equal(old["Tool.Pen"], upgraded["Tool.Pen"]); Assert.Equal(0u, upgraded["ExportPdf"]);
    }
    [Fact]
    public void ImportReusesOnlyBlankBoardAndEmbedsExplicitImagePlacement()
    {
        var workspace = new LessonWorkspace(); workspace.New(1000, 500);
        workspace.ImportPages([new LessonPage { Name = "Worksheet", ImagePng = TinyPng }]);
        Assert.Equal(1, workspace.PageCount); Assert.False(workspace.IsDesktop); Assert.True(workspace.IsDirty);
        Assert.Equal(2, workspace.Snapshot.SchemaVersion); Assert.Equal(new PageImageRect(250, 0, 500, 500), workspace.ActivePage.ImagePlacement);
        workspace.UpsertText(new TextLabel { Text = "Keep this" });
        workspace.ImportPages([new LessonPage { Name = "Second", ImagePng = TinyPng }]);
        Assert.Equal(2, workspace.PageCount); Assert.Equal("Keep this", Assert.Single(workspace.Snapshot.Pages[0].Texts).Text);
    }
    [Fact]
    public void ImportPreservesTheUndoHistoryOfAClearedBoard()
    {
        var workspace = new LessonWorkspace(); workspace.ShowDesktop(false);
        workspace.UpsertText(new TextLabel { Text = "Restore me" }); workspace.Clear();
        workspace.ImportPages([new LessonPage { ImagePng = TinyPng }]);
        Assert.Equal(2, workspace.PageCount); workspace.Navigate(-1); workspace.Undo();
        Assert.Equal("Restore me", Assert.Single(workspace.ActivePage.Texts).Text);
    }
    [Fact]
    public void ReusingTheBlankBoardPreservesDesktopUndoAndRedo()
    {
        var workspace = new LessonWorkspace(); workspace.UpsertText(new TextLabel { Text = "Desktop annotation" });
        workspace.ImportPages([new LessonPage { ImagePng = TinyPng }]); workspace.ShowDesktop(true);
        Assert.True(workspace.CanUndo); workspace.Undo(); Assert.Empty(workspace.ActivePage.Texts);
        workspace.Redo(); Assert.Equal("Desktop annotation", Assert.Single(workspace.ActivePage.Texts).Text);
    }
    [Fact]
    public void InvalidImportLeavesLessonAndUndoHistoryIntact()
    {
        var workspace = new LessonWorkspace(); workspace.ShowDesktop(false); workspace.UpsertText(new TextLabel { Text = "Original" });
        var revision = workspace.Revision;
        Assert.Throws<InvalidDataException>(() => workspace.ImportPages([new LessonPage { ImagePng = "bad" }]));
        Assert.Equal(revision, workspace.Revision); Assert.Equal(1, workspace.PageCount); Assert.True(workspace.CanUndo);
        Assert.Equal("Original", Assert.Single(workspace.ActivePage.Texts).Text);
    }
    [Fact]
    public void GridRuledAndImageRemovalAreUndoableAndPersistWithTheLesson()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sketchora");
        try
        {
            var workspace = new LessonWorkspace(); workspace.ImportPages([new LessonPage { ImagePng = TinyPng }]);
            workspace.SetPattern(PagePattern.Grid); workspace.SetPattern(PagePattern.Ruled); workspace.Undo();
            Assert.Equal(PagePattern.Grid, workspace.ActivePage.Pattern);
            workspace.RemovePageImage(); Assert.Empty(workspace.ActivePage.ImagePng); workspace.Undo();
            Assert.Equal(TinyPng, workspace.ActivePage.ImagePng);
            var store = new LessonStore(); store.Save(path, workspace.Snapshot); var loaded = store.Load(path);
            Assert.Equal(PagePattern.Grid, loaded.Pages[0].Pattern); Assert.Equal(TinyPng, loaded.Pages[0].ImagePng);
            Assert.Equal(workspace.ActivePage.ImagePlacement, loaded.Pages[0].ImagePlacement);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void PngHeaderLimitsAndUnknownPatternsAreRejected()
    {
        EmbeddedPng.Validate(TinyPng);
        var bytes = Convert.FromBase64String(TinyPng); bytes[16] = 0x7F;
        Assert.Throws<InvalidDataException>(() => EmbeddedPng.ValidateBytes(bytes));
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(new LessonDocument { Pages = [new() { Pattern = (PagePattern)99 }] }));
    }
    [Fact]
    public void PdfHasRealPageTreeAndCorrectCrossReferenceOffsets()
    {
        using var output = new MemoryStream();
        var page = new PdfRasterPage([255, 216, 255, 217], 100, 50);
        RasterPdf.Write(output, [page, page]);
        var text = Encoding.ASCII.GetString(output.ToArray());
        Assert.StartsWith("%PDF-1.4", text); Assert.Contains("/Count 2", text); Assert.Contains("/MediaBox [0 0 842 595]", text);
        var start = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        var xref = int.Parse(text[start..].Split('\n')[0]); Assert.Equal("xref", text.Substring(xref, 4));
        var lines = text[xref..].Split('\n');
        for (var id = 1; id < 9; id++)
        {
            var offset = int.Parse(lines[2 + id][..10]); Assert.StartsWith($"{id} 0 obj", text[offset..]);
        }
    }
    [Fact]
    public void PdfFailureDoesNotReplaceExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            File.WriteAllText(path, "Keep original");
            Assert.Throws<InvalidDataException>(() => RasterPdf.Save(path, [new PdfRasterPage([1, 2], 10, 10)]));
            Assert.Equal("Keep original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
