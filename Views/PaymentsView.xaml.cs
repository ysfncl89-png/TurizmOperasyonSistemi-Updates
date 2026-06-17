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



        private void Ledger_Click(object sender, RoutedEventArgs e)
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new LedgerView());
            }
        }

        private void PaymentList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                // Ensure PaymentsListCardMode is set before configuring mode so UI overrides (combobox color, labels)
                try { personsView.PaymentsListCardMode = true; } catch { }
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }

                // If hosted in a NavigationService-capable host, navigate there
                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null)
                {
                    nav.Navigate(personsView);
                    return;
                }

                // Otherwise open a new window that hosts the page inside a Frame
                var win = new Window
                {
                    Title = "Tur Kayıtları - Genel (Ödeme Listesi)",
                    Content = new Frame { Content = personsView, NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}