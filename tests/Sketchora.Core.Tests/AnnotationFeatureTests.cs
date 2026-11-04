using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class AnnotationFeatureTests
{
    [Theory]
    [InlineData('D', 'H')]
    [InlineData('P', 'G')]
    [InlineData('G', 'F')]
    public void MigratedRecoveryKeysStayUniqueWithNewToolBindings(char draw, char hide)
    {
        var keys = ShortcutCatalog.Defaults(draw, hide);
        Assert.True(ShortcutCatalog.IsValid(keys));
        Assert.Equal((uint)draw, keys["Draw"]);
        Assert.Equal((uint)hide, keys["Hide"]);
        Assert.All(ShortcutCatalog.Tools, tool => Assert.NotEqual(0u, keys["Tool." + tool]));
    }
    [Fact]
    public void DuplicateDisabledAndInvalidRecoveryBindingsAreRejected()
    {
        var keys = ShortcutCatalog.Defaults(); keys["Ghost"] = keys["Draw"];
        Assert.False(ShortcutCatalog.IsValid(keys));
        keys["Ghost"] = 0; Assert.False(ShortcutCatalog.IsValid(keys));
        keys["Ghost"] = 0xFF; Assert.False(ShortcutCatalog.IsValid(keys));
        keys["Ghost"] = 'G'; keys["Tool.Pen"] = 0;
        Assert.True(ShortcutCatalog.IsValid(keys));
    }
    [Fact]
    public void IncompleteSettingsFallBackWithoutLosingLegacyRecoveryKeys()
    {
        var preferences = new AppPreferences { DrawShortcut = 'P', HideShortcut = 'G', Shortcuts = new() { ["Draw"] = 'Z' } }.Normalize();
        Assert.Null(preferences.Shortcuts);
        Assert.Equal((uint)'P', preferences.EffectiveShortcuts()["Draw"]);
        Assert.Equal((uint)'G', preferences.EffectiveShortcuts()["Hide"]);
    }
    [Fact]
    public void NewPreferencesPersistAllBindingsWidthsAndInputOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var preferences = new AppPreferences
            {
                Shortcuts = ShortcutCatalog.Defaults('P', 'G'), ToolWidths = new() { ["Pen"] = 3, ["Highlighter"] = 12 },
                Color = "#123456", PressureEnabled = false, PalmRejection = false
            };
            var store = new PreferencesStore(path); store.Save(preferences);
            var loaded = store.Load();
            Assert.Equal(preferences.Shortcuts.OrderBy(p => p.Key), loaded.Shortcuts!.OrderBy(p => p.Key));
            Assert.Equal(3, loaded.ToolWidths!["Pen"]); Assert.Equal(12, loaded.ToolWidths["Highlighter"]);
            Assert.Equal("#123456", loaded.Color); Assert.False(loaded.PressureEnabled); Assert.False(loaded.PalmRejection);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void InvalidWidthsAreClampedAndUnknownToolsDiscarded()
    {
        var preferences = new AppPreferences { ToolWidths = new() { ["Pen"] = double.NaN, ["Eraser"] = 90, ["Unknown"] = 5 } }.Normalize();
        Assert.Equal(4, preferences.ToolWidths!["Pen"]); Assert.Equal(20, preferences.ToolWidths["Eraser"]);
        Assert.False(preferences.ToolWidths.ContainsKey("Unknown"));
    }
    [Theory]
    [InlineData(100, 50, 300, 150)]
    [InlineData(300, 150, 100, 50)]
    public void RegionSelectionHandlesReverseDragAndDpiScaling(double x1, double y1, double x2, double y2)
    {
        Assert.Equal(new ScreenRegion(200, 100, 400, 200), ScreenRegion.FromSelection(x1, y1, x2, y2, 1000, 500, 2000, 1000));
    }
    [Fact]
    public void RegionSelectionClampsToBitmapAndRejectsEmptyOrNonFiniteBounds()
    {
        Assert.Equal(new ScreenRegion(0, 0, 2000, 1000), ScreenRegion.FromSelection(-20, -30, 9000, 9000, 1000, 500, 2000, 1000));
        Assert.Null(ScreenRegion.FromSelection(2, 2, 2, 20, 100, 100, 100, 100));
        Assert.Null(ScreenRegion.FromSelection(double.NaN, 0, 20, 20, 100, 100, 100, 100));
    }
    [Fact]
    public void DisplayResizePreservesInkHistoryAndRejectsInvalidDimensionsAtomically()
    {
        var workspace = new LessonWorkspace(); workspace.UpsertText(new TextLabel { Text = "Keep me", X = 10, Y = 20 });
        workspace.ResizeCanvas(3840, 1080);
        Assert.Equal(3840, workspace.Snapshot.CanvasWidth); Assert.True(workspace.CanUndo);
        Assert.Throws<InvalidDataException>(() => workspace.ResizeCanvas(-1, 1080));
        Assert.Equal(3840, workspace.Snapshot.CanvasWidth);
        Assert.Equal(10, workspace.ActivePage.Texts[0].X);
        workspace.Undo(); Assert.Empty(workspace.ActivePage.Texts);
    }
}
