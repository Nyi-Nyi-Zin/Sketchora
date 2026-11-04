using System.Globalization;
using System.Windows.Data;
using Sketchora.App.ViewModels;

namespace Sketchora.App.Converters;

public sealed class ShortcutTipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "").Split('|');
        return values.FirstOrDefault() is ToolbarViewModel model && parts.Length >= 2
            ? model.GetToolTip(parts[0], parts[1].Replace("{colour}", values.Length > 2 ? values[2]?.ToString() ?? "" : ""), parts.Length > 2 ? parts[2] : null) : parts.LastOrDefault() ?? "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
