using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Models;
using System.Collections.ObjectModel;

namespace TurizmOperasyonSistemi.Views
{
    public partial class LedgerView : UserControl
    {
        // kept long-lived context to avoid Edit-and-Continue removal issues; load uses short-lived contexts
        private readonly AppDbContext _db = new AppDbContext();
        private ObservableCollection<LedgerRow> _rows = new ObservableCollection<LedgerRow>();

        public LedgerView()
        {
            InitializeComponent();
            LoadData();
            LedgerDataGrid.ItemsSource = _rows;
            // Apply conditional styling for RemainingDebt column (0 -> white, negative -> ice-blue)
            ApplyRemainingDebtStyling();
            // initial hint visibility
            SearchHint.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            SearchTextBox.GotFocus += (s, e) => SearchHint.Visibility = Visibility.Collapsed;
            SearchTextBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(SearchTextBox.Text)) SearchHint.Visibility = Visibility.Visible; };
            UpdateInfoBox();
            TurizmOperasyonSistemi.Services.NotificationCenter.Subscribe(OnNotification);
            this.Unloaded += (s, e) => TurizmOperasyonSistemi.Services.NotificationCenter.Unsubscribe(OnNotification);
        }

        private void ApplyRemainingDebtStyling()
        {
            // find the column with header "Toplam Kalan"
            var col = LedgerDataGrid.Columns.FirstOrDefault(c => (c.Header ?? "").ToString() == "Toplam Kalan") as DataGridTextColumn;
            if (col == null) return;

            // create converters (local implementations)
            var isZero = new LocalIsZeroConverter();
            var isNegative = new LocalIsNegativeConverter();

            // style for TextBlock inside the cell
            var style = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            // default foreground = red
            style.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DC2626"))));
            // drop shadow effect
            var drop = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Colors.White, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.28 };
            style.Setters.Add(new Setter(System.Windows.Controls.TextBlock.EffectProperty, drop));

            // DataTriggers for zero and negative values
            var dataTriggerZero = new System.Windows.DataTrigger
            {
                Binding = new System.Windows.Data.Binding("RemainingDebt") { Converter = isZero },
                Value = true
            };
            dataTriggerZero.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White));

            var dataTriggerNeg = new System.Windows.DataTrigger
            {
                Binding = new System.Windows.Data.Binding("RemainingDebt") { Converter = isNegative },
                Value = true
            };
            // ice-blue color
            // use yellow for overpaid (sarı)
            dataTriggerNeg.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FBBF24"))));

            // Ensure negative styling takes precedence if both triggers somehow evaluate true
            style.Triggers.Add(dataTriggerNeg);
            style.Triggers.Add(dataTriggerZero);

            col.ElementStyle = style;
        }

        // Local converters used only for styling; kept internal to avoid XAML resolution issues
        private class LocalIsZeroConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value == null) return false;
                // For decimal prefer exact zero comparison to avoid treating tiny negatives as zero
                if (value is decimal dm) return dm == 0m;
                if (value is double d) return Math.Abs(d) < 0.0001 && Math.Sign(d) == 0;
                if (double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, culture, out var parsed)) return Math.Abs(parsed) < 0.0001 && Math.Sign(parsed) == 0;
                return false;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
        }

        private class LocalIsNegativeConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value == null) return false;
                if (value is decimal dm) return dm < 0;
                if (value is double d) return d < 0;
                if (double.TryParse(value.ToString(), out var parsed)) return parsed < 0;
                return false;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
        }

        private void OnNotification(string key, object payload)
        {
            if (key == "payment.updated")
            {
                // reload data
                this.Dispatcher.Invoke(() => { LoadData(); LedgerDataGrid.ItemsSource = _rows; UpdateInfoBox(); });
            }
        }

        private void LoadData()
        {
            // Use short-lived context so we see external updates immediately
            using (var db = new AppDbContext())
            {
                var query = db.Registrations
                    .Include(r => r.Person)
                    .AsEnumerable()
                    .GroupBy(r => r.PersonId)
                    .Select(g => new
                    {
                        PersonId = g.Key,
                        FirstName = g.FirstOrDefault()?.Person?.FirstName ?? g.FirstOrDefault()?.Person?.FirstName ?? "",
                        LastName = g.FirstOrDefault()?.Person?.LastName ?? "",
                        Tc = g.FirstOrDefault()?.Person?.TcKimlikNo ?? "",
                        Passport = g.FirstOrDefault()?.Person?.PassportNumber ?? "",
                        Currency = g.FirstOrDefault()?.Currency ?? "",
                        TotalPrice = g.Sum(x => x.TotalPrice),
                        Paid = g.Sum(x => x.PaidAmount),
                        Remaining = g.Sum(x => x.RemainingDebt)
                    })
                    .ToList();

                _rows.Clear();
                int seq = 1;
                foreach (var item in query)
                {
                    _rows.Add(new LedgerRow
                    {
                        Sequence = seq++,
                        PersonId = item.PersonId,
                        FirstName = item.FirstName,
                        LastName = item.LastName,
                        TcOrPassport = string.IsNullOrWhiteSpace(item.Tc) ? item.Passport : item.Tc,
                        TotalPrice = item.TotalPrice,
                        PaidAmount = item.Paid,
                        RemainingDebt = item.Remaining,
                        Currency = item.Currency
                    });
                }
            }
            UpdateInfoBox();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // hide hint when user types
            SearchHint.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            var q = SearchTextBox.Text?.Trim().ToLower();
            if (string.IsNullOrEmpty(q))
            {
                LedgerDataGrid.ItemsSource = _rows;
                return;
            }

            var filtered = _rows.Where(r => (r.FirstName+" "+r.LastName).ToLower().Contains(q)
                || (r.TcOrPassport ?? "").ToLower().Contains(q)).ToList();
            LedgerDataGrid.ItemsSource = filtered;
            UpdateInfoBox(filtered);
        }

        private void UpdateInfoBox(IEnumerable<LedgerRow> source = null)
        {
            var rows = (source ?? _rows).ToList();
            InfoCountText.Text = $"Cari hesap: {rows.Count}";

            // Para birimi bazında toplamlar göster
            var groups = rows.GroupBy(r => string.IsNullOrEmpty(r.Currency) ? "Seçilmedi" : r.Currency)
                .Select(g => new
                {
                    Currency = g.Key,
                    Total = g.Sum(x => x.TotalPrice),
                    Paid = g.Sum(x => x.PaidAmount),
                    Remaining = g.Sum(x => x.RemainingDebt)
                }).ToList();

            InfoTotalsText.Text = string.Join("\n", groups.Select(g => $"{g.Currency}: Toplam {g.Total:N2}  Tahsil {g.Paid:N2}  Kalan {g.Remaining:N2}"));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate back to PaymentsView
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new PaymentsView());
            }
        }

        private void InspectAccount_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            var row = btn.DataContext as LedgerRow;
            if (row == null) return;

            var wnd = new LedgerDetailWindow(row.PersonId);
            wnd.Owner = System.Windows.Application.Current.MainWindow;
            wnd.ShowDialog();
        }
    }

    public class LedgerRow
    {
        public int PersonId { get; set; }
        public int Sequence { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string TcOrPassport { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingDebt { get; set; }
        public string Currency { get; set; }
    }
}
