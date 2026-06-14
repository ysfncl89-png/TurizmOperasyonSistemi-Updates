using System.Windows.Controls;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CargoView : UserControl
    {
        public CargoView()
        {
            InitializeComponent();
        }

        private void CargoList_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }
                try { personsView.CargoOnlyMode = true; } catch { }

                // Try to use NavigationService if available
                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null)
                {
                    nav.Navigate(personsView);
                    return;
                }

                // Otherwise open in a new window inside a Frame
                var win = new System.Windows.Window
                {
                    Title = "Kargo Listesi - Filtreler",
                    Content = new System.Windows.Controls.Frame { Content = personsView, NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);
            }
        }

        private void CargoDocs_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                try { personsView.CargoOnlyMode = true; } catch { }
                try { personsView.IsCargoDocsMode = true; } catch { }
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }

                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null)
                {
                    nav.Navigate(personsView);
                    return;
                }

                var win = new System.Windows.Window
                {
                    Title = "Kargo Evrak Hazırlama - Filtreler",
                    Content = new System.Windows.Controls.Frame { Content = personsView, NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);
            }
        }

        private void CargoTracking_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // MainWindow üzerinden navigation yap
            var main = System.Windows.Application.Current.MainWindow as MainWindow;
            if (main != null)
            {
                main.NavigateTo(new CargoTrackingInfoView());
            }
        }
    }
}
