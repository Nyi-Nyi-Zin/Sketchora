using System.Globalization;
using System.Text;

namespace Sketchora.Core;

public sealed record PdfRasterPage(byte[] Jpeg, int PixelWidth, int PixelHeight);

/// <summary>Portable PDF image pages: content is flattened deliberately to preserve any installed font's appearance.</summary>
public static class RasterPdf
{
    public static void Write(Stream output, IReadOnlyList<PdfRasterPage> pages)
    {
        if (!output.CanSeek || !output.CanWrite || pages.Count is < 1 or > LessonStore.MaximumPages)
            throw new ArgumentException("A writable, seekable stream and 1–100 pages are required.");
        foreach (var page in pages)
            if (page.PixelWidth < 1 || page.PixelHeight < 1 || page.Jpeg.Length < 4 || page.Jpeg.Length > 20 * 1024 * 1024
                || page.Jpeg[0] != 255 || page.Jpeg[1] != 216) throw new InvalidDataException("Invalid JPEG page.");
        var offsets = new List<long> { 0 };
        void Text(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        void Object(int id, string content) { offsets.Add(output.Position); Text($"{id} 0 obj\n{content}\nendobj\n"); }
        void StreamObject(int id, string attributes, byte[] data)
        {
            offsets.Add(output.Position); Text($"{id} 0 obj\n<< {attributes} /Length {data.Length} >>\nstream\n");
            output.Write(data); Text("\nendstream\nendobj\n");
        }
        Text("%PDF-1.4\n%Sketchora\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 3} 0 R"))}] >>");
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i]; var id = 3 + i * 3;
            var width = page.PixelWidth >= page.PixelHeight ? 842.0 : 595.0;
            var height = page.PixelWidth >= page.PixelHeight ? 595.0 : 842.0;
            var fit = Math.Min(width / page.PixelWidth, height / page.PixelHeight);
            var iw = page.PixelWidth * fit; var ih = page.PixelHeight * fit;
            string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
            var w = N(width); var h = N(height);
            Object(id, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 {id + 1} 0 R >> >> /Contents {id + 2} 0 R >>");
            StreamObject(id + 1, $"/Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", page.Jpeg);
            StreamObject(id + 2, "", Encoding.ASCII.GetBytes($"q {N(iw)} 0 0 {N(ih)} {N((width - iw) / 2)} {N((height - ih) / 2)} cm /Im0 Do Q"));
        }
        var xref = output.Position;
        Text($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Text(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
    public static void Save(string path, IReadOnlyList<PdfRasterPage> pages)
    {
        var full = Path.GetFullPath(path); var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) { Write(stream, pages); stream.Flush(true); }
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
