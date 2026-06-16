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

                    // Güncelleme: PaidAmount ve RemainingDebt'ı güncelle
                    reg.PaidAmount += amount;
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
