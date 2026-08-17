using System;
using System.Globalization;
using System.Windows.Data;

namespace TurizmOperasyonSistemi.Views
{
    public class HalfConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                // Reduce slightly to account for padding/margins
                var half = d * 0.5;
                return Math.Max(120, half - 48); // ensure a reasonable minimum width
            }
            return 200;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
