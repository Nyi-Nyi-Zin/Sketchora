using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class SnapshotHistoryTests
{
    [Fact]
    public void UndoAndRedoRestoreCompleteScenes()
    {
        var history = new SnapshotHistory<List<int>>([], value => [.. value]);
        history.Capture([1]);
        history.Capture([1, 2]);
        Assert.Equal(new[] { 1 }, history.Undo());
        Assert.Equal(new[] { 1, 2 }, history.Redo());
    }

    [Fact]
    public void EditingAfterUndoDiscardsRedoBranch()
    {
        var history = new SnapshotHistory<int>(0, value => value);
        history.Capture(1);
        history.Capture(2);
        history.Undo();
        history.Capture(3);
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.Undo());
        Assert.Equal(3, history.Redo());
    }

    [Fact]
    public void MutatingInputsOrReturnedScenesCannotChangeStoredHistory()
    {
        var input = new List<int> { 1 };
        var history = new SnapshotHistory<List<int>>([], value => [.. value]);
        history.Capture(input);
        input.Add(2);
        history.Current.Add(3);
        history.Undo().Add(4);
        Assert.Equal(new[] { 1 }, history.Redo());
    }

    [Fact]
    public void CapacityEvictsOldestSnapshotAndPreservesValidUndoBoundary()
    {
        var history = new SnapshotHistory<int>(0, value => value, 3);
        history.Capture(1);
        history.Capture(2);
        history.Capture(3);
        Assert.Equal(2, history.Undo());
        Assert.Equal(1, history.Undo());
        Assert.False(history.CanUndo);
        Assert.Throws<InvalidOperationException>(() => history.Undo());
    }

    [Fact]
    public void ClearCanBeUndoneLikeAnyOtherEdit()
    {
        var history = new SnapshotHistory<List<int>>([], value => [.. value]);
        history.Capture([1, 2, 3]);
        history.Capture([]);
        Assert.Equal(new[] { 1, 2, 3 }, history.Undo());
        Assert.Empty(history.Redo());
    }

    [Fact]
    public void FailedSnapshotDoesNotDiscardRedoBranch()
    {
        var history = new SnapshotHistory<int>(0,
            value => value == 99 ? throw new InvalidOperationException() : value);
        history.Capture(1);
        history.Undo();
        Assert.Throws<InvalidOperationException>(() => history.Capture(99));
        Assert.Equal(1, history.Redo());
    }
}
