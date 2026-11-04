using System.Windows.Ink;
using System.Windows.Media;
using Sketchora.Core;

namespace Sketchora.App.Services;

public static class InkSerializer
{
    public static string Encode(StrokeCollection strokes)
    {
        if (strokes.Count == 0) return "";
        using var stream = new MemoryStream();
        strokes.Save(stream, compress: true);
        return Convert.ToBase64String(stream.ToArray());
    }

    public static StrokeCollection Decode(string ink)
    {
        if (ink.Length == 0) return new StrokeCollection();
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(ink));
            var strokes = new StrokeCollection(stream);
            if (strokes.Count > 10_000 || strokes.Sum(stroke => (long)stroke.StylusPoints.Count) > 1_000_000)
                throw new InvalidDataException("This canvas exceeds the ink point limit.");
            return strokes;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or System.Runtime.InteropServices.ExternalException)
        {
            throw new InvalidDataException("The lesson contains invalid ink data.", ex);
        }
    }

    public static void Validate(LessonDocument document)
    {
        LessonStore.Validate(document);
        foreach (var page in document.Pages.Prepend(document.Desktop)) _ = Decode(page.Ink);
    }

    public static LessonDocument Resize(LessonDocument source, double width, double height, bool fit)
    {
        Validate(source);
        var scale = fit ? Math.Min(width / source.CanvasWidth, height / source.CanvasHeight) : 1;
        var dx = fit ? (width - source.CanvasWidth * scale) / 2 : 0;
        var dy = fit ? (height - source.CanvasHeight * scale) / 2 : 0;
        LessonPage Transform(LessonPage page)
        {
            if (!fit) return page.Copy();
            var strokes = Decode(page.Ink);
            strokes.Transform(new Matrix(scale, 0, 0, scale, dx, dy), applyToStylusTip: true);
            return page with
            {
                Ink = Encode(strokes),
                Texts = page.Texts.Select(text => text with
                {
                    X = text.X * scale + dx, Y = text.Y * scale + dy,
                    FontSize = Math.Clamp(text.FontSize * scale, 8, 200)
                }).ToList()
            };
        }
        var result = source with
        {
            CanvasWidth = width, CanvasHeight = height,
            Desktop = Transform(source.Desktop), Pages = source.Pages.Select(Transform).ToList()
        };
        Validate(result);
        return result;
    }
}
