using System;
using System.Globalization;
using System.Windows.Data;

namespace TurizmOperasyonSistemi.Views.Converters
{
    public class BoolToSentTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return "Kargo Havuzuna Gönderildi";
            return "Kargo havuzuna gönder";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
