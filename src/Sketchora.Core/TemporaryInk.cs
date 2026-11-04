using System.Diagnostics;

namespace Sketchora.Core;

public sealed class TemporaryInk<T> where T : notnull
{
    private readonly Func<TimeSpan> _clock;
    private readonly Dictionary<T, (TimeSpan Start, TimeSpan Duration)> _entries = [];
    public TemporaryInk(Func<TimeSpan>? clock = null)
    {
        var start = Stopwatch.GetTimestamp();
        _clock = clock ?? (() => Stopwatch.GetElapsedTime(start));
    }
    public int Count => _entries.Count;
    public void Add(T value, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _entries[value] = (_clock(), duration);
    }
    public IReadOnlyList<(T Value, double Opacity)> Tick()
    {
        var now = _clock();
        var result = new List<(T, double)>();
        foreach (var (value, timing) in _entries.ToArray())
        {
            var remaining = timing.Duration - (now - timing.Start);
            var opacity = Math.Clamp(remaining.TotalMilliseconds / 300, 0, 1);
            result.Add((value, opacity));
            if (remaining <= TimeSpan.Zero) _entries.Remove(value);
        }
        return result;
    }
    public void Clear() => _entries.Clear();
}
