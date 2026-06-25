using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using Microsoft.EntityFrameworkCore;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Globalization;
using System;

namespace TurizmOperasyonSistemi.Views
{
    public partial class LedgerDetailWindow : Window
    {
        private readonly AppDbContext _db = new AppDbContext();

        private int _personId;
        private string _tbTotalOriginal = string.Empty;

        public LedgerDetailWindow(int personId)
        {
            InitializeComponent();
            _personId = personId;
            LoadPerson(_personId);
            TurizmOperasyonSistemi.Services.NotificationCenter.Subscribe(OnNotification);
        }

        private void LoadPerson(int personId)
        {
            using (var db = new AppDbContext())
            {
                var regs = db.Registrations.Include(r => r.Person).Where(r => r.PersonId == personId).ToList();
                if (!regs.Any()) return;

                var p = regs.First().Person;
            TbName.Text = (p?.FirstName ?? "") + " " + (p?.LastName ?? "");
            TbTc.Text = p?.TcKimlikNo ?? p?.PassportNumber ?? "";

            TbTotal.Text = regs.Sum(r => r.TotalPrice).ToString("N2");
            // store original formatted value so we can restore if edit is invalid
            _tbTotalOriginal = TbTotal.Text;

            TbPaid.Text = regs.Sum(r => r.PaidAmount).ToString("N2");
            TbRemaining.Text = regs.Sum(r => r.RemainingDebt).ToString("N2");

                var list = regs.Select(r => new RegistrationDetail
            {
                Id = r.Id,
                TourType = r.TourType,
                Period = r.Period,
                GroupType = r.GroupType,
                DaysCount = r.DaysCount,
                Currency = r.Currency,
                TotalPrice = r.TotalPrice,
                PaidAmount = r.PaidAmount,
                RemainingDebt = r.RemainingDebt,
                IsOverpaid = r.RemainingDebt < 0
            }).ToList();
                DetailGrid.ItemsSource = list;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            TurizmOperasyonSistemi.Services.NotificationCenter.Unsubscribe(OnNotification);
            this.Close();
        }

        private void OnNotification(string key, object payload)
        {
            if (key == "payment.updated")
            {
                // reload UI on UI thread
                this.Dispatcher.Invoke(() => LoadPerson(_personId));
            }
        }

        private void PaymentRecord_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            var item = btn.DataContext as RegistrationDetail;
            if (item == null) return;

            var wnd = new PaymentPopupWindow(item.Id, item.IsOverpaid);
            wnd.Owner = this;
            var res = wnd.ShowDialog();
            if (res == true)
            {
                // refresh data
                LoadPerson(_personId);
            }
        }

        private void PaymentHistory_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            var item = btn.DataContext as RegistrationDetail;
            if (item == null) return;
            var wnd = new PaymentHistoryWindow(item.Id);
            wnd.Owner = this;
            wnd.ShowDialog();
        }

        // Allow clicking the content host area to focus and edit quickly
        private void TbTotal_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;
            if (!tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
                tb.SelectAll();
            }
        }

        private void TbTotal_GotFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;
            // keep raw value while editing
            _tbTotalOriginal = tb.Text;
            tb.SelectAll();
        }

        private void TbTotal_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;

            // Try parse using current culture (handles "," in tr-TR)
            if (decimal.TryParse(tb.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var val))
            {
                // Persist change to DB: adjust registrations so their sum equals the entered value.
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var regs = db.Registrations.Where(r => r.PersonId == _personId).ToList();
                        if (regs.Any())
                        {
                            var currentSum = regs.Sum(r => r.TotalPrice);
                            if (Math.Abs(currentSum - val) > 0.0001m)
                            {
                                if (regs.Count == 1)
                                {
                                    var reg = regs[0];
                                    reg.TotalPrice = val;
                                    reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                                    db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                }
                                else
                                {
                                    decimal diff = val - currentSum;
                                    // Distribute difference proportionally to existing TotalPrice shares.
                                    if (currentSum > 0)
                                    {
                                        decimal accumulated = 0m;
                                        for (int i = 0; i < regs.Count; i++)
                                        {
                                            var reg = regs[i];
                                            if (i < regs.Count - 1)
                                            {
                                                var share = reg.TotalPrice / currentSum;
                                                var newPrice = Math.Round(reg.TotalPrice + (share * diff), 2);
                                                accumulated += newPrice;
                                                reg.TotalPrice = newPrice;
                                                reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                                                db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                            }
                                            else
                                            {
                                                // Last one absorbs rounding to match exact total
                                                var lastPrice = Math.Round(val - accumulated, 2);
                                                reg.TotalPrice = lastPrice;
                                                reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                                                db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // If currentSum == 0, distribute equally
                                        var n = regs.Count;
                                        var baseVal = Math.Floor((val / n) * 100m) / 100m;
                                        decimal accumulated = 0m;
                                        for (int i = 0; i < regs.Count; i++)
                                        {
                                            var reg = regs[i];
                                            if (i < regs.Count - 1)
                                            {
                                                reg.TotalPrice = baseVal;
                                                accumulated += reg.TotalPrice;
                                                reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                                                db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                            }
                                            else
                                            {
                                                reg.TotalPrice = Math.Round(val - accumulated, 2);
                                                reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                                                db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                            }
                                        }
                                    }
                                }

db.SaveChanges();
                                TurizmOperasyonSistemi.Services.NotificationCenter.Publish("payment.updated", _personId, persistToDb: true);
                                // refresh UI from DB to reflect persisted values
                                LoadPerson(_personId);
                                return; // LoadPerson already set textbox and original value
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Toplam borç kaydedilirken hata oluştu: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // If nothing to persist or after save, format value for display
                tb.Text = val.ToString("N2");
                _tbTotalOriginal = tb.Text;
            }
            else
            {
                // restore previous valid value
                tb.Text = _tbTotalOriginal;
            }
        }

        private static readonly Regex _numericRegex = new Regex("^[0-9\\.,]+$");

        private void TbNumeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_numericRegex.IsMatch(e.Text);
        }

        private void TbNumeric_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = e.DataObject.GetData(typeof(string)) as string;
                if (string.IsNullOrEmpty(text) || !_numericRegex.IsMatch(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private class RegistrationDetail
        {
            public int Id { get; set; }
            public string TourType { get; set; }
            public string Period { get; set; }
            public string GroupType { get; set; }
            public string DaysCount { get; set; }
            public string Currency { get; set; }
            public decimal TotalPrice { get; set; }
            public decimal PaidAmount { get; set; }
            public decimal RemainingDebt { get; set; }
            public bool IsOverpaid { get; set; }
        }
    }
}
