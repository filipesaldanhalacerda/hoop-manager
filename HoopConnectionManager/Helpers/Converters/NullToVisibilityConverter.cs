using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HoopConnectionManager.Helpers.Converters;

/// <summary>
/// Visível quando o valor está preenchido; recolhido quando é nulo ou texto em branco.
/// Evita reservar espaço para resultados que ainda não existem.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasContent = value is string text ? !string.IsNullOrWhiteSpace(text) : value is not null;
        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
