using System.Globalization;
using System.Windows.Data;

namespace RocketReplayUploader.Infrastructure.UI;

// Convierte "el valor es igual al parámetro" para enlazar un valor string a
// varios RadioButtons (p. ej. la visibilidad: public/unlisted/private).
public class StringEqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter as string : Binding.DoNothing;
}
