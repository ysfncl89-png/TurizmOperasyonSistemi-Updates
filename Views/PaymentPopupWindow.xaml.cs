using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentPopupWindow : Window
    {
        private readonly int _registrationId;
        public PaymentPopupWindow(int registrationId)
        {
            InitializeComponent();
            _registrationId = registrationId;
            LoadCurrencies();
        }

        private void CbCurrency_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var sel = CbCurrency.SelectedItem as string;
            if (string.IsNullOrEmpty(sel))
            {
                RatePanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            // show rate input when Turkish Lira selected (support common labels)
            var isTL = sel.IndexOf("Türk", StringComparison.OrdinalIgnoreCase) >= 0
                       || sel.IndexOf("TL", StringComparison.OrdinalIgnoreCase) >= 0
                       || sel.IndexOf("TRY", StringComparison.OrdinalIgnoreCase) >= 0
                       || sel.IndexOf("₺", StringComparison.OrdinalIgnoreCase) >= 0;

            RatePanel.Visibility = isTL ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void LoadCurrencies()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var list = db.AppSettings.Where(x => x.Type == "Para Birimi").Select(x => x.Value).ToList();
                    CbCurrency.ItemsSource = list;
                    if (list.Any()) CbCurrency.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TbAmount.Text, out var amount) || amount <= 0)
            {
                MessageBox.Show("Lütfen geçerli bir ödeme miktarı girin.");
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var reg = db.Registrations.FirstOrDefault(r => r.Id == _registrationId);
                    if (reg == null)
                    {
                        MessageBox.Show("Kayıt bulunamadı.");
                        return;
                    }

                    var selectedCurrency = CbCurrency.SelectedItem as string;
                    decimal added = 0;
                    decimal? rateUsed = null;

                    if (!string.IsNullOrEmpty(selectedCurrency) && (
                        selectedCurrency.IndexOf("Türk", StringComparison.OrdinalIgnoreCase) >= 0
                        || selectedCurrency.IndexOf("TL", StringComparison.OrdinalIgnoreCase) >= 0
                        || selectedCurrency.IndexOf("TRY", StringComparison.OrdinalIgnoreCase) >= 0
                        || selectedCurrency.IndexOf("₺", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        // TL entered; need rate
                        if (!decimal.TryParse(TbRate.Text, out var rate) || rate <= 0)
                        {
                            MessageBox.Show("Lütfen geçerli bir kur değeri girin.");
                            return;
                        }

                        // convert TL to registration currency by dividing by rate
                        added = amount / rate;
                        rateUsed = rate;
                    }
                    else
                    {
                        // same currency or other currency selected: assume amount is already in registration currency
                        added = amount;
                    }

                    // create payment record
                    db.EnsurePaymentsTableExists();
                    var payment = new TurizmOperasyonSistemi.Models.Payment
                    {
                        RegistrationId = _registrationId,
                        OriginalAmount = amount,
                        OriginalCurrency = selectedCurrency,
                        AmountInRegistrationCurrency = added,
                        Rate = rateUsed,
                        Note = TbNote.Text
                    };
                    db.Payments.Add(payment);

                    // Güncelleme: PaidAmount ve RemainingDebt'ı güncelle (kayıt birimine göre)
                    reg.PaidAmount += added;
                    reg.RemainingDebt = Math.Max(0, reg.TotalPrice - reg.PaidAmount);
                    db.SaveChanges();
                }

                // Bildirim gönder: ödeme güncellendi
                TurizmOperasyonSistemi.Services.NotificationCenter.Publish("payment.updated", _registrationId);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ödeme kaydedilirken hata: " + ex.Message);
            }
        }
    }
}
