using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HoopConnectionManager.Helpers.Converters;

/// <summary>
/// Converte o passo atual (int) e um parâmetro (int) para Visibility.
/// Visível quando forem iguais.
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class StepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int currentStep)
        {
            return Visibility.Collapsed;
        }

        if (!int.TryParse(parameter?.ToString(), out var targetStep))
        {
            return Visibility.Collapsed;
        }

        return currentStep == targetStep ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
