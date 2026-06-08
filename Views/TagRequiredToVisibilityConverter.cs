using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TurizmOperasyonSistemi.Views
{
    public class TagRequiredToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return Visibility.Collapsed;
                var s = value as string;
                if (s == "RequiredError") return Visibility.Visible;
                return Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
