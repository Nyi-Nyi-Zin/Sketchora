using System.Windows.Ink;
using Sketchora.Core;

namespace Sketchora.App.Services;

internal sealed class InkDocument
{
    public StrokeCollection Strokes { get; set; } = new();
    public SnapshotHistory<StrokeCollection> History { get; } = new(new StrokeCollection(), value => value.Clone());
}
