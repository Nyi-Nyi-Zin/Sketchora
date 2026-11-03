namespace Sketchora.Core;

/// <summary>Bounded history with defensive snapshots and conventional redo invalidation.</summary>
public sealed class SnapshotHistory<T>
{
    private readonly Func<T, T> _clone;
    private readonly int _capacity;
    private readonly List<T> _states = [];
    private int _index;

    public SnapshotHistory(T initial, Func<T, T> clone, int capacity = 60)
    {
        ArgumentNullException.ThrowIfNull(clone);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _clone = clone;
        _capacity = capacity;
        _states.Add(_clone(initial));
    }

    public bool CanUndo => _index > 0;
    public bool CanRedo => _index < _states.Count - 1;
    public T Current => _clone(_states[_index]);

    public void Capture(T value)
    {
        // Clone first so a failed clone cannot destroy the redo branch.
        var snapshot = _clone(value);
        if (CanRedo)
            _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        _states.Add(snapshot);
        if (_states.Count > _capacity)
            _states.RemoveAt(0);
        _index = _states.Count - 1;
    }

    public T Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
        var result = _clone(_states[_index - 1]);
        _index--;
        return result;
    }

    public T Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("Nothing to redo.");
        var result = _clone(_states[_index + 1]);
        _index++;
        return result;
    }
}
