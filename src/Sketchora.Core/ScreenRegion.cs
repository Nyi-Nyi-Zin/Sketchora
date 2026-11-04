namespace Sketchora.Core;

/// <summary>Convert a selection in WPF coordinates to clamped bitmap pixel bounds.</summary>
public readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    public static ScreenRegion? FromSelection(double x1, double y1, double x2, double y2,
        double viewWidth, double viewHeight, int pixelWidth, int pixelHeight)
    {
        if (!new[] { x1, y1, x2, y2, viewWidth, viewHeight }.All(double.IsFinite)
            || viewWidth <= 0 || viewHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0) return null;
        var left = (int)Math.Floor(Math.Clamp(Math.Min(x1, x2) / viewWidth, 0, 1) * pixelWidth);
        var top = (int)Math.Floor(Math.Clamp(Math.Min(y1, y2) / viewHeight, 0, 1) * pixelHeight);
        var right = (int)Math.Ceiling(Math.Clamp(Math.Max(x1, x2) / viewWidth, 0, 1) * pixelWidth);
        var bottom = (int)Math.Ceiling(Math.Clamp(Math.Max(y1, y2) / viewHeight, 0, 1) * pixelHeight);
        return right - left >= 2 && bottom - top >= 2 ? new(left, top, right - left, bottom - top) : null;
    }
}
