using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RocketReplayUploader.Infrastructure.UI;

// Convierte "string no vacío/null" a bool para habilitar botones según si hay
// un valor (p. ej. el enlace de ballchasing de un replay subido).
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// Igual pero devolviendo Visibility (para ocultar textos vacíos, p. ej. las
// estadísticas antes de la primera subida).
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
