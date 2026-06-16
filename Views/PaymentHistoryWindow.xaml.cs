using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentHistoryWindow : Window
    {
        public PaymentHistoryWindow(int registrationId)
        {
            InitializeComponent();
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
                    p.CreatedAt,
                    OriginalAmountDisplay = (p.OriginalAmount.ToString("N2") + " " + (p.OriginalCurrency ?? "")),
                    p.AmountInRegistrationCurrency,
                    ConvertedDisplay = (p.AmountInRegistrationCurrency.ToString("N2") + " " + (p.Registration?.Currency ?? "")),
                    Rate = p.Rate,
                    PaymentMethod = p.PaymentMethod ?? "",
                    UploadedFileName = string.IsNullOrEmpty(p.UploadedDocumentPath) ? "" : System.IO.Path.GetFileName(p.UploadedDocumentPath),
                    Note = p.Note ?? ""
                }).ToList();

                PaymentsGrid.ItemsSource = list;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
