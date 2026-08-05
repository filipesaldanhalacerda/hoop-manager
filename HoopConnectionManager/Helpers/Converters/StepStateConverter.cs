using System.Globalization;
using System.Windows.Data;

namespace HoopConnectionManager.Helpers.Converters;

/// <summary>
/// Converte o passo atual (int) e um parâmetro (int) no estado visual daquele passo:
/// <c>Completed</c>, <c>Current</c> ou <c>Pending</c>. Permite que a trilha do assistente
/// mostre o progresso já percorrido em vez de destacar apenas o passo corrente.
/// </summary>
[ValueConversion(typeof(int), typeof(string))]
public sealed class StepStateConverter : IValueConverter
{
    public const string Completed = "Completed";
    public const string Current = "Current";
    public const string Pending = "Pending";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int currentStep || !int.TryParse(parameter?.ToString(), out var targetStep))
        {
            return Pending;
        }

        if (currentStep > targetStep)
        {
            return Completed;
        }

        return currentStep == targetStep ? Current : Pending;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
