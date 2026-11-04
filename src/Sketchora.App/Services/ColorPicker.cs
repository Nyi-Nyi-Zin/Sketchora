using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Sketchora.App.Services;

internal static class ColorPicker
{
    private sealed class Owner(nint handle) : Forms.IWin32Window { public nint Handle => handle; }
    public static string? Choose(Window owner, string current)
    {
        var color = (Color)ColorConverter.ConvertFromString(current);
        using var picker = new Forms.ColorDialog
        {
            FullOpen = true, AnyColor = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };
        return picker.ShowDialog(new Owner(new WindowInteropHelper(owner).Handle)) == Forms.DialogResult.OK
            ? $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}" : null;
    }
}
