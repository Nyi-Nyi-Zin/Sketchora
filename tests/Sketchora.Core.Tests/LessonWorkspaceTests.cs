using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class LessonWorkspaceTests
{
    [Fact]
    public void HistoryIsIsolatedAcrossBoardsAndDesktop()
    {
        var workspace = new LessonWorkspace();
        workspace.UpsertText(new TextLabel { Text = "Desktop" });
        workspace.ShowDesktop(false);
        workspace.UpsertText(new TextLabel { Text = "Board one" });
        workspace.AddPage();
        workspace.UpsertText(new TextLabel { Text = "Board two" });
        workspace.Undo();
        Assert.Empty(workspace.ActivePage.Texts);
        workspace.Navigate(-1);
        Assert.Equal("Board one", Assert.Single(workspace.ActivePage.Texts).Text);
        workspace.ShowDesktop(true);
        Assert.Equal("Desktop", Assert.Single(workspace.ActivePage.Texts).Text);
    }

    [Fact]
    public void DuplicateHasIndependentIdentityContentsAndHistory()
    {
        var workspace = new LessonWorkspace();
        workspace.ShowDesktop(false);
        workspace.UpsertText(new TextLabel { Text = "Original" });
        var source = workspace.ActivePage;
        workspace.AddPage(duplicate: true);
        var duplicate = workspace.ActivePage;
        Assert.NotEqual(source.Id, duplicate.Id);
        Assert.NotEqual(source.Texts[0].Id, duplicate.Texts[0].Id);
        workspace.Clear();
        workspace.Navigate(-1);
        Assert.Equal("Original", Assert.Single(workspace.ActivePage.Texts).Text);
        duplicate.Texts.Clear();
        Assert.Single(workspace.ActivePage.Texts);
    }

    [Fact]
    public void DeletedPageCanBeRestoredWithItsInkTextAndHistory()
    {
        var workspace = new LessonWorkspace();
        workspace.AddPage();
        workspace.RenamePage("Keep this board");
        workspace.UpsertText(new TextLabel { Text = "Recovered" });
        var page = workspace.ActivePage;
        workspace.DeletePage();
        Assert.Equal(1, workspace.PageCount);
        workspace.DeletePage();
        Assert.Equal(1, workspace.PageCount);
        workspace.RestorePage();
        Assert.Equal(page.Id, workspace.ActivePage.Id);
        Assert.Equal("Recovered", Assert.Single(workspace.ActivePage.Texts).Text);
        workspace.Undo();
        Assert.Empty(workspace.ActivePage.Texts);
    }

    [Fact]
    public void ClearRestoresTextAndInkTogetherOnUndo()
    {
        var workspace = new LessonWorkspace();
        var ink = Convert.ToBase64String(new byte[] { 1 });
        workspace.SetContent(ink, [new TextLabel { Text = "Keep" }]);
        workspace.Clear();
        workspace.Undo();
        Assert.Equal(ink, workspace.ActivePage.Ink);
        Assert.Single(workspace.ActivePage.Texts);
    }

    [Fact]
    public void EditsDuringAnOlderSaveRemainDirty()
    {
        var workspace = new LessonWorkspace();
        workspace.UpsertText(new TextLabel { Text = "Saved snapshot" });
        var savingRevision = workspace.Revision;
        workspace.UpsertText(new TextLabel { Text = "Newer edit" });
        workspace.MarkSaved("lesson.sketchora", savingRevision);
        Assert.True(workspace.IsDirty);
        workspace.MarkSaved("lesson.sketchora");
        Assert.False(workspace.IsDirty);
    }

    [Fact]
    public void RecoveryIsDirtyAndLoadDoesNotAliasCallerData()
    {
        var workspace = new LessonWorkspace();
        var source = new LessonDocument();
        workspace.Load(source, recovered: true);
        source.Pages.Clear();
        workspace.Snapshot.Pages.Clear();
        Assert.Equal(1, workspace.PageCount);
        Assert.True(workspace.IsDirty);
        Assert.Null(workspace.FilePath);
    }

    [Fact]
    public void RejectedEditDoesNotChangeHistoryOrCurrentContents()
    {
        var workspace = new LessonWorkspace();
        workspace.UpsertText(new TextLabel { Text = "Keep" });
        var revision = workspace.Revision;
        Assert.Throws<InvalidDataException>(() => workspace.UpsertText(new TextLabel { Text = "Bad", FontSize = -1 }));
        Assert.Equal(revision, workspace.Revision);
        Assert.Equal("Keep", Assert.Single(workspace.ActivePage.Texts).Text);
        workspace.Undo();
        Assert.Empty(workspace.ActivePage.Texts);
    }

    [Fact]
    public void SwitchingBackgroundPreservesBoardContentsAndCanBeUndone()
    {
        var workspace = new LessonWorkspace();
        workspace.ShowDesktop(false);
        workspace.UpsertText(new TextLabel { Text = "Board" });
        workspace.SetBackground(PageBackground.Black);
        Assert.Single(workspace.ActivePage.Texts);
        workspace.Undo();
        Assert.Equal(PageBackground.White, workspace.ActivePage.Background);
        Assert.Single(workspace.ActivePage.Texts);
    }
}
