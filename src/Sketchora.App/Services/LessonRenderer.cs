using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sketchora.Core;

namespace Sketchora.App.Services;

internal static class LessonRenderer
{
    public static BitmapSource DecodeImage(string png)
    {
        EmbeddedPng.Validate(png);
        using var stream = new MemoryStream(Convert.FromBase64String(png));
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public static string EncodePng(BitmapSource image)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || (long)image.PixelWidth * image.PixelHeight > 32_000_000)
            throw new InvalidDataException("Images are limited to 32 megapixels.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var bytes = stream.ToArray(); EmbeddedPng.ValidateBytes(bytes); return Convert.ToBase64String(bytes);
    }
    public static LessonPage ImportImage(string path)
    {
        if (new FileInfo(path).Length > LessonStore.MaximumFileBytes) throw new InvalidDataException("The image file exceeds 32 MiB.");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var image = decoder.Frames[0];
        return new LessonPage { Name = Path.GetFileNameWithoutExtension(path)[..Math.Min(120, Path.GetFileNameWithoutExtension(path).Length)], ImagePng = EncodePng(image) };
    }
    public static Brush BackgroundBrush(LessonPage page)
    {
        var dark = page.Background == PageBackground.Black;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(dark ? new SolidColorBrush(Color.FromRgb(18, 20, 28)) : Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 28, 28))));
        if (page.Pattern != PagePattern.None)
        {
            var pen = new Pen(new SolidColorBrush(dark ? Color.FromRgb(66, 77, 96) : Color.FromRgb(211, 222, 237)), .7);
            group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M0,28 L28,28")));
            if (page.Pattern == PagePattern.Grid) group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M28,0 L28,28")));
        }
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 28, 28), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
    public static Rect FitImage(BitmapSource image, double width, double height)
    {
        var scale = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
        var w = image.PixelWidth * scale; var h = image.PixelHeight * scale;
        return new Rect((width - w) / 2, (height - h) / 2, w, h);
    }
    public static PdfRasterPage RenderPage(LessonPage page, double width, double height)
    {
        var scale = Math.Min(2, 1920 / Math.Max(width, height));
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale)); var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawRectangle(BackgroundBrush(page), null, new Rect(0, 0, width, height));
            if (page.ImagePng.Length > 0)
            {
                var image = DecodeImage(page.ImagePng);
                var rect = page.ImagePlacement is { } place ? new Rect(place.X, place.Y, place.Width, place.Height) : FitImage(image, width, height);
                dc.DrawImage(image, rect);
            }
            foreach (var stroke in InkSerializer.Decode(page.Ink)) stroke.Draw(dc);
            foreach (var text in page.Texts)
            {
                var formatted = new FormattedText(text.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(text.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), text.FontSize,
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(text.Color)), 1);
                dc.DrawText(formatted, new Point(text.X + 3, text.Y + 3));
            }
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        var jpeg = new JpegBitmapEncoder { QualityLevel = 95 }; jpeg.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); jpeg.Save(output);
        return new PdfRasterPage(output.ToArray(), pixelWidth, pixelHeight);
    }
}
