using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TurizmOperasyonSistemi.Views.Converters
{
    // Converts remaining debt value to a Foreground brush:
    // - remaining == 0  => White
    // - remaining < 0   => Yellow
    // - otherwise       => Red
    public class RemainingToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Brushes.White;

            if (!double.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out double remaining))
                return Brushes.White;

            if (Math.Abs(remaining) < 0.0001)
                return Brushes.White; // settled

            if (remaining < 0)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // yellow for overpayment

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")); // red for remaining debt
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
