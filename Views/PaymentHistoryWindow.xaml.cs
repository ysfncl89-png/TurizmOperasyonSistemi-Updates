using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentHistoryWindow : Window
    {
        private int _registrationId;
        public PaymentHistoryWindow(int registrationId)
        {
            InitializeComponent();
            _registrationId = registrationId;
            LoadPayments(registrationId);
        }

        private void LoadPayments(int registrationId)
        {
            using (var db = new AppDbContext())
            {
                db.EnsurePaymentsTableExists();
                // Load entities into memory first to avoid provider translation and NULL read issues
                var payments = db.Payments
                    .Where(p => p.RegistrationId == registrationId)
                    .OrderByDescending(p => p.CreatedAt)
                    .Include(p => p.Registration)
                    .ToList();

                var list = payments.Select(p => new
                {
                    p.Id,
                    p.CreatedAt,
                    OriginalAmountDisplay = (p.OriginalAmount.ToString("N2") + " " + (p.OriginalCurrency ?? "")),
                    p.AmountInRegistrationCurrency,
                    ConvertedDisplay = (p.AmountInRegistrationCurrency.ToString("N2") + " " + (p.Registration?.Currency ?? "")),
                    Rate = p.Rate,
                    IsRefund = p.AmountInRegistrationCurrency < 0,
                    PaymentMethod = p.PaymentMethod ?? "",
                    UploadedDocumentPath = p.UploadedDocumentPath,
                    UploadedFileName = string.IsNullOrEmpty(p.UploadedDocumentPath) ? "" : System.IO.Path.GetFileName(p.UploadedDocumentPath),
                    Note = p.Note ?? ""
                }).ToList();

                PaymentsGrid.ItemsSource = list;
            }
        }

        private void DeletePaymentBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            if (!int.TryParse(btn.CommandParameter?.ToString(), out var paymentId)) return;

            var res = MessageBox.Show("Seçili ödeme kaydını silmek istediğinize emin misiniz? Bu işlem geri alınamaz.", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var payment = db.Payments.FirstOrDefault(p => p.Id == paymentId);
                    if (payment == null)
                    {
                        MessageBox.Show("Ödeme kaydı bulunamadı.");
                        return;
                    }

                    var regId = payment.RegistrationId;
                    db.Payments.Remove(payment);
                    db.SaveChanges();

                    // Recalculate registration sums from remaining payments
                    var reg = db.Registrations.FirstOrDefault(r => r.Id == regId);
                    if (reg != null)
                    {
                        // SQLite may store numeric columns as TEXT; perform aggregation on client side to avoid provider errors
                        var paymentAmounts = db.Payments.Where(p => p.RegistrationId == regId).Select(p => p.AmountInRegistrationCurrency).ToList();
                        var paid = paymentAmounts.Sum();
                        reg.PaidAmount = paid;
                        reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                        db.SaveChanges();
                    }

                    // publish notification so ledger views refresh
                    TurizmOperasyonSistemi.Services.NotificationCenter.Publish("payment.updated", regId);
                }

                // reload list
                LoadPayments(_registrationId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ödeme silinirken hata: " + ex.Message);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenDocumentBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            var path = btn.CommandParameter as string;
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Bu ödeme için kayıtlı bir dekont bulunamadı.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dekont açılamadı: " + ex.Message);
            }
        }
    }
}
