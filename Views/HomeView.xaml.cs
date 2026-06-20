using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Globalization;

namespace TurizmOperasyonSistemi.Views
{
    /// <summary>
    /// HomeView.xaml etkileşim mantığı
    /// </summary>
    public partial class HomeView : Page
    {
        private readonly DispatcherTimer _clockTimer;

        public HomeView()
        {
            InitializeComponent();

            // Create minute/hour markers on the analog face
            CreateMarkers();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();
        }

        private void CreateMarkers()
        {
            try
            {
                double width = MarkersCanvas.Width;
                double height = MarkersCanvas.Height;
                double cx = width / 2.0;
                double cy = height / 2.0;
                double outer = Math.Min(width, height) / 2.0 - 6.0; // biraz iç boşluk
                double innerMajor = outer - 10.0; // saat işaretleri
                double innerMinor = outer - 6.0; // dakika işaretleri

                MarkersCanvas.Children.Clear();

                var baseColor = (Color)ColorConverter.ConvertFromString("#94A3B8");
                var majorBrush = new SolidColorBrush(baseColor) { Opacity = 0.95 };
                var minorBrush = new SolidColorBrush(baseColor) { Opacity = 0.45 };

                for (int i = 0; i < 60; i++)
                {
                    double angle = i * 6.0 * Math.PI / 180.0; // derece -> radyan
                    double sin = Math.Sin(angle);
                    double cos = Math.Cos(angle);

                    bool isMajor = (i % 5 == 0);
                    double inner = isMajor ? innerMajor : innerMinor;
                    double thickness = isMajor ? 2.2 : 0.8;
                    var brush = isMajor ? (Brush)majorBrush : (Brush)minorBrush;

                    double x1 = cx + sin * inner;
                    double y1 = cy - cos * inner;
                    double x2 = cx + sin * outer;
                    double y2 = cy - cos * outer;

                    var ln = new Line
                    {
                        X1 = x1,
                        Y1 = y1,
                        X2 = x2,
                        Y2 = y2,
                        Stroke = brush,
                        StrokeThickness = thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };

                    MarkersCanvas.Children.Add(ln);
                }
            }
            catch
            {
                // ignore errors during design-time
            }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;

            // Saat kollarını açıya çevir
            double ms = now.Millisecond / 1000.0;
            double seconds = now.Second + ms;
            double minutes = now.Minute + seconds / 60.0;
            double hours = (now.Hour % 12) + minutes / 60.0;

            // Her saniye 6 derece, her saat 30 derece
            SecondRotate.Angle = seconds * 6.0; // 360/60
            MinuteRotate.Angle = minutes * 6.0;
            HourRotate.Angle = hours * 30.0;

            // Tarih - Türkçe format
            DateText.Text = now.ToString("dddd, dd MMMM yyyy", new CultureInfo("tr-TR"));
        }
    }
}
