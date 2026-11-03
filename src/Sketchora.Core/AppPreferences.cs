namespace Sketchora.Core;

public sealed record AppPreferences
{
    public string Color { get; init; } = "#6366F1";
    public double Thickness { get; init; } = 4;
    public bool PointerHalo { get; init; } = true;
    public bool TouchDrawing { get; init; } = true;
    public double? ToolbarLeft { get; init; }
    public double? ToolbarTop { get; init; }

    public AppPreferences Normalize() => this with
    {
        Color = IsRgbHex(Color) ? Color.ToUpperInvariant() : "#6366F1",
        Thickness = double.IsFinite(Thickness) ? Math.Clamp(Thickness, 1, 20) : 4,
        ToolbarLeft = ToolbarLeft is { } x && double.IsFinite(x) ? x : null,
        ToolbarTop = ToolbarTop is { } y && double.IsFinite(y) ? y : null
    };

    private static bool IsRgbHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);
}
