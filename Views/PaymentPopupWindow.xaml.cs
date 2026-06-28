using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Services;

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

        private async void CbCurrency_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var sel = CbCurrency.SelectedItem as string;
            if (string.IsNullOrEmpty(sel))
            {
                RatePanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            // show rate input for TRY and other currencies where user may supply a rate (EUR, GBP, SAR)
            var needRate = sel.IndexOf("Türk", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("TL", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("TRY", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("₺", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("EUR", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("Euro", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("GBP", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("Sterlin", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("SAR", StringComparison.OrdinalIgnoreCase) >= 0
                           || sel.IndexOf("Riyal", StringComparison.OrdinalIgnoreCase) >= 0;

            RatePanel.Visibility = needRate ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // If this is a non-TRY currency, try to prefill the rate from TCMB (TRY per currency)
            try
            {
                if (needRate)
                {
                    var code = "";
                    if (sel.ToLowerInvariant().Contains("eur") || sel.ToLowerInvariant().Contains("euro") || sel.Contains("€")) code = "EUR";
                    else if (sel.ToLowerInvariant().Contains("usd") || sel.ToLowerInvariant().Contains("dolar") || sel.Contains("$")) code = "USD";
                    else if (sel.ToLowerInvariant().Contains("gbp") || sel.ToLowerInvariant().Contains("sterlin") || sel.Contains("£")) code = "GBP";
                    else if (sel.ToLowerInvariant().Contains("sar") || sel.ToLowerInvariant().Contains("suudi") || sel.ToLowerInvariant().Contains("riyal")) code = "SAR";
                    else if (sel.ToLowerInvariant().Contains("tl") || sel.ToLowerInvariant().Contains("tür")) code = "TRY";

                    if (!string.IsNullOrEmpty(code) && code != "TRY")
                    {
                        var svc = new ExchangeRateService();
                        var rates = await svc.GetLatestRatesAsync();
                        string rateTxt = "-";
                        if (rates != null)
                        {
                            switch (code)
                            {
                                case "USD": rateTxt = rates.UsdSell ?? "-"; break;
                                case "EUR": rateTxt = rates.EurSell ?? "-"; break;
                                case "GBP": rateTxt = rates.GbpSell ?? "-"; break;
                                case "SAR": rateTxt = rates.SarSell ?? "-"; break;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(rateTxt) && rateTxt != "-")
                        {
                            TbRate.Text = rateTxt;
                        }
                    }
                }
            }
            catch { }
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
            // hide previous success indicator when user starts a new upload flow
            try { UploadSuccessIndicator.Visibility = System.Windows.Visibility.Collapsed; UploadSuccessIndicator.Opacity = 0; } catch { }

            // show choice window: QR or File
            var choiceWnd = new UploadChoiceWindow();
            choiceWnd.Owner = this;
            var res = choiceWnd.ShowDialog();
            if (res != true) return;

            if (choiceWnd.Choice == UploadChoiceWindow.UploadChoice.File)
            {
                var ofd = new OpenFileDialog();
                ofd.Filter = "Tüm Dosyalar|*.*";
                if (ofd.ShowDialog() == true)
                {
                    _uploadedPath = ofd.FileName;
                    UploadedFileName.Text = System.IO.Path.GetFileName(_uploadedPath);
                    ShowUploadSuccess();
                }
            }
            else if (choiceWnd.Choice == UploadChoiceWindow.UploadChoice.Qr)
            {
                // open QR upload window; when file saved, callback updates uploaded path and shows original filename
                var qrWnd = new QRUploadWindow((savedPath, originalName) =>
                {
                    _uploadedPath = savedPath;
                    UploadedFileName.Text = originalName ?? System.IO.Path.GetFileName(_uploadedPath);
                    ShowUploadSuccess();
                });
                qrWnd.Owner = this;
                qrWnd.ShowDialog();
            }
        }

        private void ShowUploadSuccess()
        {
            try
            {
                UploadSuccessIndicator.Visibility = System.Windows.Visibility.Visible;
                UploadSuccessIndicator.Opacity = 1;
            }
            catch { }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void ConfirmBtn_Click(object sender, RoutedEventArgs e)
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

                    // helper: map display text to ISO-like code used by ExchangeRateService
                    string MapToCode(string text)
                    {
                        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                        text = text.ToLowerInvariant();
                        if (text.Contains("tür") || text.Contains("tl") || text.Contains("try") || text.Contains("₺")) return "TRY";
                        if (text.Contains("eur") || text.Contains("euro") || text.Contains("€")) return "EUR";
                        if (text.Contains("usd") || text.Contains("dolar") || text.Contains("$")) return "USD";
                        if (text.Contains("gbp") || text.Contains("sterlin") || text.Contains("£")) return "GBP";
                        if (text.Contains("sar") || text.Contains("suudi")) return "SAR";
                        return string.Empty;
                    }

                    var regCurrency = reg.Currency ?? string.Empty;
                    var selCode = MapToCode(selectedCurrency);
                    var regCode = MapToCode(regCurrency);

                    // If user entered TL, keep previous behaviour: require TbRate (user-provided rate)
                    if (!string.IsNullOrEmpty(selCode) && selCode == "TRY")
                    {
                        if (!decimal.TryParse(TbRate.Text, out var rate) || rate <= 0)
                        {
                            MessageBox.Show("Lütfen geçerli bir kur değeri girin.");
                            return;
                        }
                        // convert TL to registration currency by dividing by user-provided rate (existing behaviour)
                        added = amount / rate;
                        rateUsed = rate;
                    }
                    else if (!string.IsNullOrEmpty(selCode) && !string.IsNullOrEmpty(regCode) && selCode != regCode)
                    {
                        try
                        {
                            // If user provided a custom rate in TbRate, interpret it as TRY per selectedCurrency (like TCMB) and use it.
                            decimal userSelSell = 0m;
                            if (decimal.TryParse(TbRate.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var u) || decimal.TryParse(TbRate.Text, System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("tr-TR"), out u))
                            {
                                if (u > 0) userSelSell = u;
                            }

                            var svc = new ExchangeRateService();
                            var rates = await svc.GetLatestRatesAsync();
                            decimal ParseRate(string s)
                            {
                                if (string.IsNullOrWhiteSpace(s)) return 0m;
                                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
                                return 0m;
                            }

                            decimal selSell = 0m;
                            decimal regSell = 1m; // for TRY use 1 as base

                            switch (selCode)
                            {
                                case "USD": selSell = ParseRate(rates?.UsdSell); break;
                                case "EUR": selSell = ParseRate(rates?.EurSell); break;
                                case "GBP": selSell = ParseRate(rates?.GbpSell); break;
                                case "SAR": selSell = ParseRate(rates?.SarSell); break;
                            }
                            switch (regCode)
                            {
                                case "USD": regSell = ParseRate(rates?.UsdSell); break;
                                case "EUR": regSell = ParseRate(rates?.EurSell); break;
                                case "GBP": regSell = ParseRate(rates?.GbpSell); break;
                                case "SAR": regSell = ParseRate(rates?.SarSell); break;
                                case "TRY": regSell = 1m; break;
                            }

                            // If user provided selSell explicitly, use it instead of TCMB selSell
                            if (userSelSell > 0)
                            {
                                selSell = userSelSell;
                            }

                            if (selSell > 0 && regSell > 0)
                            {
                                // amount in registration currency = amount * (selSell / regSell)

                                added = amount * (selSell / regSell);
                                rateUsed = selSell / regSell;
                            }
                            else
                            {
                                // fallback: treat as same currency (no conversion)
                                added = amount;
                            }
                        }
                        catch
                        {
                            added = amount; // fallback
                        }
                    }
                    else
                    {
                        // same currency or unknown mapping: assume amount is already in registration currency
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

                    // Prepare notification payload with payment details
                    try
                    {
                        // reload related person for details
                        var regWithPerson = db.Registrations.Include(r => r.Person).FirstOrDefault(r => r.Id == _registrationId);
                        var personName = regWithPerson?.Person != null ? (regWithPerson.Person.FirstName + " " + regWithPerson.Person.LastName).Trim() : "(bilinmiyor)";
                        var tc = regWithPerson?.Person?.TcKimlikNo ?? string.Empty;
                        var isRefundNotice = (payment.AmountInRegistrationCurrency < 0);
                        var paymentNotice = new
                        {
                            RegistrationId = _registrationId,
                            PersonId = regWithPerson?.Person?.Id ?? 0,
                            PersonName = personName,
                            Tc = tc,
                            Amount = payment.OriginalAmount,
                            Currency = payment.OriginalCurrency,
                            AmountInRegistrationCurrency = payment.AmountInRegistrationCurrency,
                            IsRefund = isRefundNotice,
                            UploadedDocument = !string.IsNullOrWhiteSpace(payment.UploadedDocumentPath),
                            PaymentMethod = payment.PaymentMethod,
                            Message = isRefundNotice
                                ? $"{personName} (TC: {tc}) için {Math.Abs(payment.OriginalAmount):N2} {payment.OriginalCurrency ?? ""} ({Math.Abs(payment.AmountInRegistrationCurrency):N2} {regWithPerson?.Currency ?? ""}) iade (ödeme çıkışı) kaydedildi. Dekont yüklendi: {(!string.IsNullOrWhiteSpace(payment.UploadedDocumentPath) ? "Evet" : "Hayır")}" 
                                : $"{personName} (TC: {tc}) için +{Math.Abs(payment.OriginalAmount):N2} {payment.OriginalCurrency ?? ""} ({Math.Abs(payment.AmountInRegistrationCurrency):N2} {regWithPerson?.Currency ?? ""}) ödeme kaydı yapıldı. Dekont yüklendi: {(!string.IsNullOrWhiteSpace(payment.UploadedDocumentPath) ? "Evet" : "Hayır")}",
                            Timestamp = DateTime.UtcNow
                        };

                        TurizmOperasyonSistemi.Services.NotificationCenter.Publish("payment.updated", paymentNotice, persistToDb: true);
                    }
                    catch { }
                }

                this.DialogResult = true;
                this.Close();
                try { UploadSuccessIndicator.Visibility = System.Windows.Visibility.Collapsed; UploadSuccessIndicator.Opacity = 0; } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ödeme kaydedilirken hata: " + ex.Message);
            }
        }
    }
}
