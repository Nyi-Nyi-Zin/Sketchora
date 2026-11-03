using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Sketchora.App.Interop;

namespace Sketchora.App.Services;

internal static class ScreenCapture
{
    public static Bitmap CapturePrimaryDisplay()
    {
        var bounds = NativeMethods.PrimaryDisplayBounds();
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
