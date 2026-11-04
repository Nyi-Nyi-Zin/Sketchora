using System.Text.Json;

namespace Sketchora.Core;

public sealed class LessonStore
{
    public const long MaximumFileBytes = 32 * 1024 * 1024;
    public const int MaximumPages = 100;
    public const int MaximumTextLength = 10_000;
    public const int MaximumLabelsPerPage = 1_000;
    public const int MaximumInkCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 32, WriteIndented = true };
    private readonly Action<string, string> _replace;

    public LessonStore(Action<string, string>? replace = null) =>
        _replace = replace ?? ((temporary, target) => File.Move(temporary, target, overwrite: true));

    public LessonDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<LessonDocument>(ReadBounded(path), Options)
            ?? throw new InvalidDataException("The lesson is empty.");
        Validate(document);
        return document;
    }

    public void Save(string path, LessonDocument document)
    {
        Validate(document);
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(document, Options));
    }

    public RecoveryDocument LoadRecovery(string path)
    {
        var recovery = JsonSerializer.Deserialize<RecoveryDocument>(ReadBounded(path), Options)
            ?? throw new InvalidDataException("The recovery file is empty.");
        if (recovery.Lesson is null) throw new InvalidDataException("Missing recovery lesson.");
        Validate(recovery.Lesson);
        return recovery;
    }

    public void SaveRecovery(string path, RecoveryDocument recovery)
    {
        Validate(recovery.Lesson);
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(recovery, Options));
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("This lesson exceeds the 32 MiB limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void WriteAtomic(string path, byte[] bytes)
    {
        if (bytes.LongLength > MaximumFileBytes) throw new InvalidDataException("This lesson exceeds the 32 MiB limit.");
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            _replace(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Validate(LessonDocument document)
    {
        if (document.SchemaVersion is not (1 or 2))
            throw new InvalidDataException($"Unsupported lesson format version {document.SchemaVersion}. Update Sketchora to open newer files.");
        if (!ValidDimension(document.CanvasWidth) || !ValidDimension(document.CanvasHeight))
            throw new InvalidDataException("Invalid canvas dimensions.");
        if (document.Desktop is null || document.Pages is null || document.Pages.Count is < 1 or > MaximumPages
            || document.ActivePageIndex < 0 || document.ActivePageIndex >= document.Pages.Count)
            throw new InvalidDataException("Invalid board page list or selection.");
        var ids = new HashSet<Guid>();
        foreach (var page in document.Pages.Prepend(document.Desktop))
        {
            if (page is null || page.Id == Guid.Empty || !ids.Add(page.Id) || string.IsNullOrWhiteSpace(page.Name)
                || page.Name.Length > 120 || !Enum.IsDefined(page.Background) || page.Ink is null
                || page.Ink.Length > MaximumInkCharacters || page.Texts is null || page.Texts.Count > MaximumLabelsPerPage)
                throw new InvalidDataException("Invalid page or page limit exceeded.");
            if (!Enum.IsDefined(page.Pattern) || page.ImagePng is null) throw new InvalidDataException("Invalid page pattern or image.");
            if (page.ImagePng.Length > 0) EmbeddedPng.Validate(page.ImagePng);
            if (page.ImagePlacement is { } rect && (!new[] { rect.X, rect.Y, rect.Width, rect.Height }.All(double.IsFinite)
                || Math.Abs(rect.X) > 1_000_000 || Math.Abs(rect.Y) > 1_000_000 || rect.Width is <= 0 or > 1_000_000 || rect.Height is <= 0 or > 1_000_000))
                throw new InvalidDataException("Invalid page image placement.");
            if (page.Ink.Length > 0)
            {
                var buffer = new byte[page.Ink.Length / 4 * 3 + 3];
                if (!Convert.TryFromBase64String(page.Ink, buffer, out _))
                    throw new InvalidDataException("Invalid ink encoding.");
            }
            var labelIds = new HashSet<Guid>();
            foreach (var label in page.Texts)
                if (label is null || label.Id == Guid.Empty || !labelIds.Add(label.Id)
                    || string.IsNullOrWhiteSpace(label.Text) || label.Text.Length > MaximumTextLength
                    || !double.IsFinite(label.X) || !double.IsFinite(label.Y) || Math.Abs(label.X) > 1_000_000 || Math.Abs(label.Y) > 1_000_000
                    || !double.IsFinite(label.FontSize) || label.FontSize is < 8 or > 200
                    || !IsColor(label.Color) || label.FontFamily is not ("Myanmar Text, Segoe UI" or "Myanmar Text" or "Segoe UI"))
                    throw new InvalidDataException("Invalid text label or text limit exceeded.");
        }
    }

    public static void ValidateStorageSize(LessonDocument document)
    {
        Validate(document);
        if (JsonSerializer.SerializeToUtf8Bytes(document, Options).LongLength > MaximumFileBytes)
            throw new InvalidDataException("This lesson exceeds the 32 MiB limit. Import fewer or smaller pages.");
    }
    public static bool IsColor(string? color) => color is { Length: 7 } && color[0] == '#'
        && color.AsSpan(1).ToArray().All(Uri.IsHexDigit);
    private static bool ValidDimension(double number) => double.IsFinite(number) && number is >= 1 and <= 32768;
}
