using System.Windows.Controls;
using System.Windows;
using TurizmOperasyonSistemi.Views;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentsView : UserControl
    {
        public PaymentsView()
        {
            InitializeComponent();
        }

        private void PaymentMovements_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Ödeme Hareketleri tıklandı - detay sayfası yapılacak.");
        }

        private void Ledger_Click(object sender, RoutedEventArgs e)
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new LedgerView());
            }
        }

        private void Documents_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Evrak ve Dekont tıklandı - detay sayfası yapılacak.");
        }

        private void PaymentList_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Ödeme Listesi tıklandı - detay sayfası yapılacak.");
        }
    }
}