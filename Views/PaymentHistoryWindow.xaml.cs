using System.Linq;
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
                var list = db.Payments.Where(p => p.RegistrationId == registrationId)
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => new
                    {
                        p.CreatedAt,
                        OriginalAmountDisplay = (p.OriginalAmount.ToString("N2") + " " + (p.OriginalCurrency ?? "")),
                        p.AmountInRegistrationCurrency,
                        ConvertedDisplay = (p.AmountInRegistrationCurrency.ToString("N2") + " " + (p.Registration.Currency ?? "")),
                        p.Rate,
                        p.Note
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
