using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace AFK_Assist.Views;

internal sealed class PresetAppearanceConverter : IMultiValueConverter
{
    public static PresetAppearanceConverter Instance { get; } = new();

    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => Equals(values[0], values[1]) ? ControlAppearance.Primary : ControlAppearance.Secondary;

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
