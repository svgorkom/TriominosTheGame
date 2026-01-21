using System.Globalization;
using System.Windows.Data;

namespace Triominos.Converters;

/// <summary>
/// Converts an integer to a boolean for RadioButton binding.
/// The ConverterParameter should be the value that makes this RadioButton checked.
/// </summary>
public class NumberToRadioConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramString && int.TryParse(paramString, out int paramValue))
        {
            return intValue == paramValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string paramString && int.TryParse(paramString, out int paramValue))
        {
            return paramValue;
        }
        return Binding.DoNothing;
    }
}
