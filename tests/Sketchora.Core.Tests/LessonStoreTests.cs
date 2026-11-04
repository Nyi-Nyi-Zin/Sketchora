using System.Text.Json;
using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class LessonStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Sketchora.Lessons", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_folder, "lesson.sketchora");
    private LessonDocument Example() => new()
    {
        CanvasWidth = 1440, CanvasHeight = 900, IsDesktop = false, ActivePageIndex = 1,
        Desktop = new LessonPage { Name = "Desktop", Ink = Convert.ToBase64String(new byte[] { 1, 2, 3 }) },
        Pages = [new(), new() { Name = "Python သင်ခန်းစာ", Background = PageBackground.Black,
            Texts = [new() { Text = "မင်္ဂလာပါ\nPython lesson", X = 12, Y = 35, FontSize = 32, Color = "#FFFFFF" }] }]
    };

    [Fact]
    public void RoundTripPreservesDesktopPagesTextAndMetadata()
    {
        var expected = Example();
        var store = new LessonStore();
        store.Save(FilePath, expected);
        var actual = store.Load(FilePath);
        Assert.Equal(expected.CanvasWidth, actual.CanvasWidth);
        Assert.Equal(expected.ActivePageIndex, actual.ActivePageIndex);
        Assert.Equal(expected.Desktop.Ink, actual.Desktop.Ink);
        Assert.Equal(expected.Pages[1].Texts[0], actual.Pages[1].Texts[0]);
        Assert.Equal(PageBackground.Black, actual.Pages[1].Background);
        Assert.Equal(expected.Pages[1].Id, actual.Pages[1].Id);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("null")]
    public void InvalidInputLeavesTheActiveLessonUnchanged(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, content);
        var workspace = new LessonWorkspace();
        workspace.UpsertText(new TextLabel { Text = "Keep me" });
        Assert.ThrowsAny<Exception>(() => workspace.Load(new LessonStore().Load(FilePath)));
        Assert.Equal("Keep me", Assert.Single(workspace.ActivePage.Texts).Text);
        Assert.True(workspace.IsDirty);
    }

    [Fact]
    public void RejectsNewerFormatsInvalidInkAndDuplicatePageIds()
    {
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(Example() with { SchemaVersion = 99 }));
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(Example() with { Desktop = new LessonPage { Ink = "not base64!" } }));
        var example = Example();
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(example with { Pages = [example.Pages[0], example.Pages[0]] }));
    }

    [Fact]
    public void RejectsOversizedFilesBeforeDeserialization()
    {
        Directory.CreateDirectory(_folder);
        using (var stream = File.Create(FilePath)) stream.SetLength(LessonStore.MaximumFileBytes + 1);
        Assert.Throws<InvalidDataException>(() => new LessonStore().Load(FilePath));
    }

    [Fact]
    public void FailedReplacementPreservesLastGoodFileAndRemovesTemporaryFile()
    {
        var store = new LessonStore();
        store.Save(FilePath, Example());
        var original = File.ReadAllBytes(FilePath);
        var failing = new LessonStore((_, _) => throw new IOException("Simulated disk failure"));
        Assert.Throws<IOException>(() => failing.Save(FilePath, new LessonDocument()));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void RecoveryRoundTripDoesNotOverwriteNamedLesson()
    {
        var store = new LessonStore();
        store.Save(FilePath, Example());
        var saved = File.ReadAllBytes(FilePath);
        var recoveryPath = Path.Combine(_folder, "recovery.json");
        var recovery = new RecoveryDocument { OriginalPath = FilePath, Lesson = new LessonDocument() };
        store.SaveRecovery(recoveryPath, recovery);
        var actual = store.LoadRecovery(recoveryPath);
        Assert.Equal(recovery.SavedAt, actual.SavedAt);
        Assert.Equal(FilePath, actual.OriginalPath);
        Assert.Equal(recovery.Lesson.Desktop.Id, actual.Lesson.Desktop.Id);
        Assert.Equal(saved, File.ReadAllBytes(FilePath));
    }

    [Theory]
    [InlineData("#GGGGGG", "Myanmar Text, Segoe UI")]
    [InlineData("#FFFFFF", "https://example.invalid/font#Remote")]
    public void RejectsInvalidColorsAndExternalFontLocations(string color, string font)
    {
        var document = new LessonDocument { Desktop = new LessonPage { Texts = [new() { Text = "Label", Color = color, FontFamily = font }] } };
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(document));
    }

    [Fact]
    public void PageAndLabelLimitsAreEnforced()
    {
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(new LessonDocument { Pages = Enumerable.Range(0, 101).Select(_ => new LessonPage()).ToList() }));
        Assert.Throws<InvalidDataException>(() => LessonStore.Validate(new LessonDocument
        {
            Desktop = new LessonPage { Texts = [new() { Text = new string('a', LessonStore.MaximumTextLength + 1) }] }
        }));
    }

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
