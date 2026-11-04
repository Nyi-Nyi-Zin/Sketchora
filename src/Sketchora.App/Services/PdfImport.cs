using Windows.Data.Pdf;
using Windows.Storage.Streams;
using Sketchora.Core;

namespace Sketchora.App.Services;

internal static class PdfImport
{
    public static async Task<IReadOnlyList<LessonPage>> ReadAsync(string path, Func<int, (int First, int Last)?> chooseRange, Func<bool>? shouldCancel = null)
    {
        if (new FileInfo(path).Length > LessonStore.MaximumFileBytes) throw new InvalidDataException("The PDF exceeds 32 MiB.");
        var sourceBytes = await File.ReadAllBytesAsync(path);
        using var source = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(source))
        {
            writer.WriteBytes(sourceBytes); await writer.StoreAsync(); writer.DetachStream();
        }
        source.Seek(0);
        var pdf = await PdfDocument.LoadFromStreamAsync(source);
        var range = chooseRange(checked((int)pdf.PageCount)) ?? throw new OperationCanceledException();
        if (range.First < 1 || range.Last < range.First || range.Last > pdf.PageCount || range.Last - range.First >= LessonStore.MaximumPages)
            throw new InvalidDataException("Choose a valid PDF page range of at most 100 pages.");
        var pages = new List<LessonPage>();
        for (var i = range.First - 1; i < range.Last; i++)
        {
            if (shouldCancel?.Invoke() == true) throw new OperationCanceledException();
            using var page = pdf.GetPage((uint)i);
            var scale = Math.Min(1920 / page.Size.Width, 1920 / page.Size.Height);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = Math.Max(1u, (uint)Math.Ceiling(page.Size.Width * scale)),
                DestinationHeight = Math.Max(1u, (uint)Math.Ceiling(page.Size.Height * scale)),
                BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId
            });
            if (stream.Size > EmbeddedPng.MaximumBytes) throw new InvalidDataException("A rendered PDF page exceeds 12 MiB.");
            using var input = stream.GetInputStreamAt(0); using var reader = new DataReader(input);
            var count = checked((uint)stream.Size); await reader.LoadAsync(count);
            var bytes = new byte[count]; reader.ReadBytes(bytes); EmbeddedPng.ValidateBytes(bytes);
            var name = Path.GetFileNameWithoutExtension(path); name = name[..Math.Min(100, name.Length)];
            pages.Add(new LessonPage { Name = $"{name} · {i + 1}", ImagePng = Convert.ToBase64String(bytes) });
        }
        return pages;
    }
}
