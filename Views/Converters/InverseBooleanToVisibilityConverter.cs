using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TurizmOperasyonSistemi.Views.Converters
{
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter != null && parameter.ToString().Equals("invert", StringComparison.OrdinalIgnoreCase);
            bool b = false;
            if (value is bool bb) b = bb;
            if (invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                bool result = v == Visibility.Visible;
                bool invert = parameter != null && parameter.ToString().Equals("invert", StringComparison.OrdinalIgnoreCase);
                if (invert) result = !result;
                return result;
            }
            return false;
        }
    }
}
