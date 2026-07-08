using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TurizmOperasyonSistemi
{
    // Converter: returns Visible when bound object's runtime type name matches ConverterParameter (string), otherwise Collapsed
    public class ContentTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (parameter == null) return Visibility.Collapsed;
                var expected = parameter.ToString();
                if (value == null) return Visibility.Collapsed;
                var type = value.GetType();
                if (string.Equals(type.Name, expected, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type.FullName, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return Visibility.Visible;
                }
            }
            catch { }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
