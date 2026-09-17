using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace AFK_Assist.Views;

internal sealed class PresetAppearanceConverter : IValueConverter
{
    public static PresetAppearanceConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString()
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
