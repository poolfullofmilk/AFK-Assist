using System.Globalization;
using System.Windows.Data;
using AFK_Assist.ViewModels;
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
    ) =>
        values[0] is Preset preset
        && Equals(preset.Value, preset.IsStartDelay ? values[1] : values[2])
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
