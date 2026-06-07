using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TurizmOperasyonSistemi.Views.Converters
{
    public class ForegroundContrastConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush scb)
            {
                var c = scb.Color;
                double brightness = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return brightness > 0.6 ? Brushes.Black : Brushes.White;
            }

            // If it's a GradientBrush try average of gradient stops
            if (value is GradientBrush gb && gb.GradientStops.Count > 0)
            {
                double r = 0, g = 0, b = 0;
                foreach (var stop in gb.GradientStops)
                {
                    r += stop.Color.R;
                    g += stop.Color.G;
                    b += stop.Color.B;
                }
                int count = gb.GradientStops.Count;
                double brightness = (0.299 * (r / count) + 0.587 * (g / count) + 0.114 * (b / count)) / 255.0;
                return brightness > 0.6 ? Brushes.Black : Brushes.White;
            }

            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
