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
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;

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
            // Aktif turlar combobox'unu doldur
            LoadActiveTours();
        }

        private void LoadActiveTours()
        {
            try
            {
                var list = new List<string>();
                using (var db = new AppDbContext())
                {
                    // Registrations tablosuna göre grupları al; aktivite sonu ise RoomAllocations'daki en ileri CheckOutDate'e göre belirlenecek
                    var regs = db.Registrations.AsNoTracking().ToList();
                    var allocations = db.RoomAllocations.AsNoTracking().ToList();

                    var grouped = regs
                        .GroupBy(r => new { r.TourType, r.Period, r.GroupType, r.DaysCount })
                        .ToList();

                    var culture = new CultureInfo("tr-TR");

                    foreach (var g in grouped)
                    {
                        // Ön şart: grupta en az bir aktif kayıt (Status == "Aktif" veya IsActive == true) olmalı
                        bool registrationActive = g.Any(r => string.Equals(r.Status, "Aktif", StringComparison.OrdinalIgnoreCase) || r.IsActive);
                        if (!registrationActive) continue;

                        // Aynı grup için odalama kayıtlarını bul
                        var allocsForGroup = allocations.Where(a =>
                            string.Equals(a.TourType ?? "", g.Key.TourType ?? "", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.Period ?? "", g.Key.Period ?? "", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.GroupType ?? "", g.Key.GroupType ?? "", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.DaysCount ?? "", g.Key.DaysCount ?? "", StringComparison.OrdinalIgnoreCase)
                        ).ToList();

                        bool allocationDeterminesActive = false; // true means allocations exist and determine active status
                        bool allocationActive = true; // default if no allocations

                        if (allocsForGroup.Any())
                        {
                            allocationDeterminesActive = true;

                            // Eğer herhangi bir CheckOutDate boşsa bunu aktif kabul et (henüz çıkış belirlenmemiş)
                            if (allocsForGroup.Any(a => string.IsNullOrWhiteSpace(a.CheckOutDate)))
                            {
                                allocationActive = true;
                            }
                            else
                            {
                                DateTime? maxCheckout = null;
                                foreach (var a in allocsForGroup)
                                {
                                    if (DateTime.TryParse(a.CheckOutDate, culture, DateTimeStyles.None, out var dt) || DateTime.TryParse(a.CheckOutDate, out var dt2))
                                    {
                                        if (!maxCheckout.HasValue || dt.Date > maxCheckout.Value.Date) maxCheckout = dt.Date;
                                    }
                                }

                                if (maxCheckout.HasValue)
                                {
                                    // Grup, en ileri çıkış tarihi bugünden önce ise pasif sayılacak
                                    allocationActive = maxCheckout.Value.Date >= DateTime.Today;
                                }
                                else
                                {
                                    // Eğer hiçbir tarih parse edilemediyse temkinli davran: aktif say
                                    allocationActive = true;
                                }
                            }
                        }

                        // Nihai karar: kayıtlar aktifse ve (eğer odalama kayıtları varsa) odalama da aktif gösteriyorsa grubu ekle
                        if (!registrationActive) continue;
                        if (allocationDeterminesActive && !allocationActive) continue;

                        string tour = string.IsNullOrWhiteSpace(g.Key.TourType) ? "(Belirtilmemiş)" : g.Key.TourType;
                        string period = string.IsNullOrWhiteSpace(g.Key.Period) ? "(Belirtilmemiş)" : g.Key.Period;
                        string groupType = string.IsNullOrWhiteSpace(g.Key.GroupType) ? "(Belirtilmemiş)" : g.Key.GroupType;
                        string days = string.IsNullOrWhiteSpace(g.Key.DaysCount) ? "?" : g.Key.DaysCount;

                        list.Add($"{tour} + {period} + {groupType} + {days} gün");
                    }
                }

                var distinctList = list.Distinct().OrderBy(x => x).ToList();
                var items = new List<string> { "Tümü" };
                items.AddRange(distinctList);
                ActiveToursCombo.ItemsSource = items;
                ActiveToursCombo.SelectedIndex = 0;

                // Aktif tur sayısını göster
                try
                {
                    int count = distinctList.Count;
                    var tb = this.FindName("ActiveToursCountText") as TextBlock;
                    if (tb != null)
                    {
                        tb.Text = $"Aktif: {count}";
                    }
                }
                catch
                {
                    // ignore
                }
            }
            catch
            {
                // Sessizce yoksay
            }
        }

        private void ActiveToursCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Şimdilik sadece seçim yapılmasını sağlıyoruz. İleride seçime göre filtreleme veya yönlendirme eklenebilir.
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
