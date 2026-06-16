using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using Microsoft.EntityFrameworkCore;

namespace TurizmOperasyonSistemi.Views
{
    public partial class LedgerDetailWindow : Window
    {
        private readonly AppDbContext _db = new AppDbContext();

        private int _personId;

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
