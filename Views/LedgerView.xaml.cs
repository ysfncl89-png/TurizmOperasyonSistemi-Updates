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
        private readonly AppDbContext _db = new AppDbContext();
        private ObservableCollection<LedgerRow> _rows = new ObservableCollection<LedgerRow>();

        public LedgerView()
        {
            InitializeComponent();
            LoadData();
            LedgerDataGrid.ItemsSource = _rows;
            // initial hint visibility
            SearchHint.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            SearchTextBox.GotFocus += (s, e) => SearchHint.Visibility = Visibility.Collapsed;
            SearchTextBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(SearchTextBox.Text)) SearchHint.Visibility = Visibility.Visible; };
            UpdateInfoBox();
        }

        private void LoadData()
        {
            // Group registrations by person and compute totals per currency
            var query = _db.Registrations
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
