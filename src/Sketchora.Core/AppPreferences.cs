namespace Sketchora.Core;

public sealed record AppPreferences
{
    public string Color { get; init; } = "#6366F1";
    public double Thickness { get; init; } = 4;
    public bool PointerHalo { get; init; } = true;
    public bool TouchDrawing { get; init; } = true;
    public double? ToolbarLeft { get; init; }
    public double? ToolbarTop { get; init; }
    public char DrawShortcut { get; init; } = 'D';
    public char HideShortcut { get; init; } = 'H';
    public bool ExpandedToolbar { get; init; } = true;
    public int FadeSeconds { get; init; } = 3;
    public double TextSize { get; init; } = 24;

    public AppPreferences Normalize() => this with
    {
        Color = IsRgbHex(Color) ? Color.ToUpperInvariant() : "#6366F1",
        Thickness = double.IsFinite(Thickness) ? Math.Clamp(Thickness, 1, 20) : 4,
        ToolbarLeft = ToolbarLeft is { } x && double.IsFinite(x) ? x : null,
        ToolbarTop = ToolbarTop is { } y && double.IsFinite(y) ? y : null,
        DrawShortcut = ValidShortcuts(DrawShortcut, HideShortcut) ? DrawShortcut : 'D',
        HideShortcut = ValidShortcuts(DrawShortcut, HideShortcut) ? HideShortcut : 'H',
        FadeSeconds = FadeSeconds is 1 or 3 or 5 ? FadeSeconds : 3,
        TextSize = double.IsFinite(TextSize) ? Math.Clamp(TextSize, 8, 200) : 24
    };

    private static bool IsRgbHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);
    public static bool ValidShortcuts(char draw, char hide) => draw is >= 'A' and <= 'Z'
        && hide is >= 'A' and <= 'Z' && draw != hide;
}
