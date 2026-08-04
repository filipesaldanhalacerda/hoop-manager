using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HoopConnectionManager.Helpers.Converters;

/// <summary>
/// Converte uma contagem para Visibility: visível quando zero, colapsado quando maior.
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value as int? ?? 0;
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
