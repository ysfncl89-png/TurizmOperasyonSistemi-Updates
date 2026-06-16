using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentPopupWindow : Window
    {
        private readonly int _registrationId;
        private string _uploadedPath;
        private bool _isRefund = false;
        public PaymentPopupWindow(int registrationId)
        {
            InitializeComponent();
            _registrationId = registrationId;
            LoadCurrencies();
            LoadPaymentMethods();
        }

        public PaymentPopupWindow(int registrationId, bool isRefund) : this(registrationId)
        {
            _isRefund = isRefund;
            if (_isRefund)
            {
                // change confirm button appearance to indicate refund mode
                ConfirmBtn.Content = "Onayla (İade)";
            }
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

        private void LoadPaymentMethods()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var list = db.AppSettings.Where(x => x.Type == "Ödeme Yöntemi").Select(x => x.Value).ToList();
                    CbPaymentMethod.ItemsSource = list;
                    if (list.Any()) CbPaymentMethod.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "Tüm Dosyalar|*.*";
            if (ofd.ShowDialog() == true)
            {
                _uploadedPath = ofd.FileName;
                UploadedFileName.Text = System.IO.Path.GetFileName(_uploadedPath);
            }
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
                    // If refund mode, record negative amount so PaidAmount decreases
                    if (_isRefund)
                    {
                        added = -added;
                        // store original amount negative to indicate refund in history
                        amount = -amount;
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
                        Note = TbNote.Text,
                        PaymentMethod = CbPaymentMethod.SelectedItem as string,
                        UploadedDocumentPath = _uploadedPath
                    };
                    db.Payments.Add(payment);

                    // Güncelleme: PaidAmount ve RemainingDebt'ı güncelle (kayıt birimine göre)
                    reg.PaidAmount += added;
                    // Allow RemainingDebt to go negative if overpayment occurs
                    reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
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
