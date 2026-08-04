using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class KisiBazliTurFiyatiView : UserControl
    {
        public KisiBazliTurFiyatiView()
        {
            InitializeComponent();
            Loaded += KisiBazliTurFiyatiView_Loaded;
        }

        private void KisiBazliTurFiyatiView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDurationOptions();
        }

        private void LoadDurationOptions()
        {
            try
            {
                using var db = new AppDbContext();
                var allSettings = db.AppSettings.Where(x => x.IsActive).ToList();

                var durations = allSettings
                    .Where(x => x.Type != null && x.Type.Trim().Equals("Kaç Günlük Grup", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .ToList();

                // Fallback: eğer "CountOfDays" tipiyle kayıtlı değerler varsa onları da alın
                if (durations.Count == 0)
                {
                    durations = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("CountOfDays", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct()
                        .ToList();
                }

                if (MekkeDaysComboBox != null) { MekkeDaysComboBox.ItemsSource = durations; if (durations.Count>0) MekkeDaysComboBox.SelectedIndex = 0; }
                if (MedineDaysComboBox != null) { MedineDaysComboBox.ItemsSource = durations; if (durations.Count>0) MedineDaysComboBox.SelectedIndex = 0; }
                if (WMekkeDaysComboBox != null) { WMekkeDaysComboBox.ItemsSource = durations; if (durations.Count>0) WMekkeDaysComboBox.SelectedIndex = 0; }
                if (WMedineDaysComboBox != null) { WMedineDaysComboBox.ItemsSource = durations; if (durations.Count>0) WMedineDaysComboBox.SelectedIndex = 0; }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gün seçenekleri yüklenirken hata: {ex.Message}");
            }
        }

        private int ParseDays(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            // extract digits
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var v)) return v;
            return 0;
        }

        private decimal ParseDecimal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("tr-TR"), out d)) return d;
            var cleaned = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
            if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("tr-TR"), out d)) return d;
            return 0m;
        }

        private void MekkePriceOrDays_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var price = ParseDecimal(MekkePriceTextBox?.Text);
                var days = ParseDays(MekkeDaysComboBox?.SelectedItem?.ToString());
                var total = price * days;
                if (MekkeTotalText != null) MekkeTotalText.Text = total.ToString("0.##");
            }
            catch { }
        }

        private void MedinePriceOrDays_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var price = ParseDecimal(MedinePriceTextBox?.Text);
                var days = ParseDays(MedineDaysComboBox?.SelectedItem?.ToString());
                var total = price * days;
                if (MedineTotalText != null) MedineTotalText.Text = total.ToString("0.##");
            }
            catch { }
        }

        private void WMekkePriceOrDays_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var price = ParseDecimal(WMekkePriceTextBox?.Text);
                var days = ParseDays(WMekkeDaysComboBox?.SelectedItem?.ToString());
                var total = price * days;
                if (WMekkeTotalText != null) WMekkeTotalText.Text = total.ToString("0.##");
            }
            catch { }
        }

        private void WMedinePriceOrDays_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var price = ParseDecimal(WMedinePriceTextBox?.Text);
                var days = ParseDays(WMedineDaysComboBox?.SelectedItem?.ToString());
                var total = price * days;
                if (WMedineTotalText != null) WMedineTotalText.Text = total.ToString("0.##");
            }
            catch { }
        }

        private void StaffOrGroupSize_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                // Left
                var staff = ParseDecimal(StaffCostTextBox?.Text);
                var group = ParseDecimal(GroupSizeTextBox?.Text);
                decimal per = 0m;
                if (group > 0) per = staff / group;
                if (StaffPerPersonText != null) StaffPerPersonText.Text = per.ToString("0.##");

                // Right
                var wstaff = ParseDecimal(WStaffCostTextBox?.Text);
                var wgroup = ParseDecimal(WGroupSizeTextBox?.Text);
                decimal wper = 0m;
                if (wgroup > 0) wper = wstaff / wgroup;
                if (WStaffPerPersonText != null) WStaffPerPersonText.Text = wper.ToString("0.##");
            }
            catch { }
        }

        private void SimpleValue_Changed(object sender, RoutedEventArgs e)
        {
            // placeholder for future aggregated calculations
        }
    }
}
