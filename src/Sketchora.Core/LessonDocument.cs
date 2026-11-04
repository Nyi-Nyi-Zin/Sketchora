using System.Text.Json.Serialization;

namespace Sketchora.Core;

public enum PageBackground { White, Black }

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
