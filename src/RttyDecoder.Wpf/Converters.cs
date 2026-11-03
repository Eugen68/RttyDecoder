using System.Globalization;
using System.Windows.Data;

namespace RttyDecoder.Wpf;

public static class Converters
{
    /// <summary>Negatia unui bool (pentru IsEnabled = !IsRunning).</summary>
    public static IValueConverter Not { get; } = new NotConverter();

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }
}
