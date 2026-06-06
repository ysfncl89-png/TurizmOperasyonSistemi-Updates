using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using System.Collections.Generic;
using System.Linq;

namespace TurizmOperasyonSistemi.Views.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        private static readonly object _initLock = new object();
        private static bool _initialized = false;
        private static HashSet<string> _visaValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _ticketValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;
                try
                {
                    using var db = new AppDbContext();
                    var all = db.AppSettings.Where(x => x.IsActive).ToList();
                    string Normalize(string s)
                    {
                        if (s == null) return string.Empty;
                        return s.Trim().ToLowerInvariant();
                    }
                    foreach (var a in all)
                    {
                        var type = Normalize(a.Type ?? string.Empty);
                        var val = Normalize(a.Value ?? string.Empty);
                        if (type.Contains("vize")) { if (!string.IsNullOrWhiteSpace(val)) _visaValues.Add(val); }
                        if (type.Contains("bilet") || type.Contains("ticket")) { if (!string.IsNullOrWhiteSpace(val)) _ticketValues.Add(val); }
                    }
                }
                catch { }
                _initialized = true;
            }
        }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value ?? string.Empty).ToString().Trim().ToLowerInvariant();
            // Normalize common Turkish characters for simple matching
            s = s.Replace("ı", "i").Replace("ş", "s").Replace("ğ", "g").Replace("ü", "u").Replace("ö", "o").Replace("ç", "c");

            bool positive = false;
            bool negative = false;

            if (string.IsNullOrWhiteSpace(s) || s.Contains("seç") || s.Contains("sec"))
            {
                // treat as neutral unless positive/negative keywords found
            }

            // Use AppSettings lists when available (initialized once)
            try
            {
                EnsureInitialized();
                // check visa keywords
                if (_visaValues != null && _visaValues.Count > 0)
                {
                    if (_visaValues.Contains(s))
                    {
                        // decide negative or positive based on common words in the value
                        // check negative forms first (e.g. "çıkmadı" -> normalized contains "cikm")
                        if (s.Contains("gelmedi") || s.Contains("cikm") || s.Contains("yok") || s.Contains("hayir") || s.Contains("secilmedi") || s.Contains("bekliyor")) negative = true;
                        else if (s.Contains("gel") || s.Contains("cik") || s.Contains("cikti") || s.Contains("geldi") || s.Contains("evet") || s.Contains("var")) positive = true;
                    }
                    else
                    {
                        // fallback: if any AppSetting visa positive keywords contained within value
                        foreach (var v in _visaValues)
                        {
                            if (string.IsNullOrWhiteSpace(v)) continue;
                            if (s.Contains(v)) { positive = true; break; }
                        }
                    }
                }

                if (_ticketValues != null && _ticketValues.Count > 0)
                {
                    if (_ticketValues.Contains(s))
                    {
                        // negative checks first to avoid matching short positive substrings inside negatives (eg. "cikmadi")
                        if (s.Contains("gelmedi") || s.Contains("cikm") || s.Contains("yok") || s.Contains("hayir") || s.Contains("secilmedi")) negative = true;
                        else if (s.Contains("gel") || s.Contains("geldi") || s.Contains("evet") || s.Contains("var")) positive = true;
                    }
                    else
                    {
                        foreach (var v in _ticketValues)
                        {
                            if (string.IsNullOrWhiteSpace(v)) continue;
                            if (s.Contains(v)) { positive = true; break; }
                        }
                    }
                }
            }
            catch { }

            // final fallback to simple heuristics if no AppSettings hit
            if (!positive && !negative)
            {
                // negative patterns first (e.g. "cikmadi" contains "cikm")
                if (s.Contains("gelmedi") || s.Contains("cikm") || s.Contains("yok") || s.Contains("hayir") || s.Contains("hayır") || s.Contains("bekliyor") || s.Contains("secilmedi")) negative = true;
                else if (s.Contains("cik") || s.Contains("cikti") || s.Contains("geldi") || s.Contains("evet") || s.Contains("var") || s.Contains("alac") || s.Contains("alindi") || s.Contains("alin")) positive = true;
            }

            if (positive && !negative)
            {
                var brush = new LinearGradientBrush();
                brush.StartPoint = new System.Windows.Point(0, 0);
                brush.EndPoint = new System.Windows.Point(0, 1);
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(8, 51, 30), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(11, 122, 82), 1));
                brush.Freeze();
                return brush;
            }

            if (negative && !positive)
            {
                var brush = new LinearGradientBrush();
                brush.StartPoint = new System.Windows.Point(0, 0);
                brush.EndPoint = new System.Windows.Point(0, 1);
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(74, 15, 18), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(127, 29, 29), 1));
                brush.Freeze();
                return brush;
            }

            // neutral / unknown
            return new SolidColorBrush(Color.FromRgb(11, 18, 32)); // #0B1220
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
