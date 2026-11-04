using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Sketchora.App.Interop;

namespace Sketchora.App.Services;

internal static class ScreenCapture
{
    public static System.Windows.Media.Imaging.BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png); stream.Position = 0;
        var source = new System.Windows.Media.Imaging.BitmapImage();
        source.BeginInit(); source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        source.StreamSource = stream; source.EndInit(); source.Freeze();
        return source;
    }

    public static Bitmap CaptureDesktop()
    {
        var bounds = NativeMethods.DesktopBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height > 100_000_000)
            throw new InvalidOperationException("The combined display area is too large to capture safely.");
        // Desktop captures are opaque. RGB avoids GDI's unused alpha byte becoming
        // transparent pixels when the result is encoded as PNG.
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var screenDc = NativeMethods.GetDC(0);
            if (screenDc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var destinationDc = graphics.GetHdc();
                try
                {
                    const uint sourceCopy = 0x00CC0020;
                    const uint captureLayeredWindows = 0x40000000;
                    // Use the native raster flags directly. Graphics.CopyFromScreen
                    // validates individual enum members and may reject this combination.
                    if (!NativeMethods.BitBlt(destinationDc, 0, 0, bounds.Width, bounds.Height,
                            screenDc, bounds.Left, bounds.Top, sourceCopy | captureLayeredWindows))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { graphics.ReleaseHdc(destinationDc); }
            }
            finally { NativeMethods.ReleaseDC(0, screenDc); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
