using System.Text.Json.Serialization;

namespace Sketchora.Core;

public enum PageBackground { White, Black }
public enum PagePattern { None, Grid, Ruled }

public sealed record TextLabel
{
    [JsonRequired]
    public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public string Text { get; init; } = "";
    [JsonRequired]
    public double X { get; init; }
    [JsonRequired]
    public double Y { get; init; }
    [JsonRequired]
    public double FontSize { get; init; } = 24;
    [JsonRequired]
    public string Color { get; init; } = "#6366F1";
    [JsonRequired]
    public string FontFamily { get; init; } = "Myanmar Text, Segoe UI";
}

public sealed record PageImageRect(double X, double Y, double Width, double Height)
{
    public static PageImageRect Fit(double pixelWidth, double pixelHeight, double width, double height)
    {
        var scale = Math.Min(width / pixelWidth, height / pixelHeight);
        var w = pixelWidth * scale; var h = pixelHeight * scale;
        return new((width - w) / 2, (height - h) / 2, w, h);
    }
}

public sealed record LessonPage
{
    [JsonRequired]
    public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public string Name { get; init; } = "Board 1";
    [JsonRequired]
    public PageBackground Background { get; init; }
    [JsonRequired]
    public string Ink { get; init; } = "";
    [JsonRequired]
    public List<TextLabel> Texts { get; init; } = [];
    public PagePattern Pattern { get; init; }
    public string ImagePng { get; init; } = "";
    public PageImageRect? ImagePlacement { get; init; }
    public LessonPage Copy() => this with { Texts = Texts.Select(text => text with { }).ToList() };
}

public sealed record LessonDocument
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public double CanvasWidth { get; init; } = 1920;
    [JsonRequired]
    public double CanvasHeight { get; init; } = 1080;
    [JsonRequired]
    public LessonPage Desktop { get; init; } = new() { Name = "Desktop" };
    [JsonRequired]
    public List<LessonPage> Pages { get; init; } = [new()];
    [JsonRequired]
    public int ActivePageIndex { get; init; }
    [JsonRequired]
    public bool IsDesktop { get; init; } = true;
    public LessonDocument Copy() => this with { Desktop = Desktop.Copy(), Pages = Pages.Select(page => page.Copy()).ToList() };
}

public sealed record RecoveryDocument
{
    [JsonRequired]
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;
    public string? OriginalPath { get; init; }
    [JsonRequired]
    public LessonDocument Lesson { get; init; } = new();
}
