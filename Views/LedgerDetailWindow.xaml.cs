using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using Microsoft.EntityFrameworkCore;

namespace TurizmOperasyonSistemi.Views
{
    public partial class LedgerDetailWindow : Window
    {
        private readonly AppDbContext _db = new AppDbContext();

        public LedgerDetailWindow(int personId)
        {
            InitializeComponent();
            LoadPerson(personId);
        }

        private void LoadPerson(int personId)
        {
            var regs = _db.Registrations.Include(r => r.Person).Where(r => r.PersonId == personId).ToList();
            if (!regs.Any()) return;

            var p = regs.First().Person;
            TbName.Text = (p?.FirstName ?? "") + " " + (p?.LastName ?? "");
            TbTc.Text = p?.TcKimlikNo ?? p?.PassportNumber ?? "";

            TbTotal.Text = regs.Sum(r => r.TotalPrice).ToString("N2");
            TbPaid.Text = regs.Sum(r => r.PaidAmount).ToString("N2");
            TbRemaining.Text = regs.Sum(r => r.RemainingDebt).ToString("N2");

            var list = regs.Select(r => new
            {
                r.TourType,
                r.Period,
                r.GroupType,
                DaysCount = r.DaysCount,
                Currency = r.Currency,
                r.TotalPrice,
                r.PaidAmount,
                r.RemainingDebt
            }).ToList();

            DetailGrid.ItemsSource = list;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PaymentRecord_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            var item = btn.DataContext; // anonymous type from list
            MessageBox.Show("Ödeme Kaydı butonu tıklandı. İşlem davranışı daha sonra eklenecek.");
        }
    }
}
