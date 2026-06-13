using System.Windows.Controls;
using System.Windows;

using System.Windows.Navigation;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CargoTrackingInfoView : UserControl
    {
        public CargoTrackingInfoView()
        {
            InitializeComponent();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nav = NavigationService.GetNavigationService(this);
                if (nav != null && nav.CanGoBack)
                {
                    nav.GoBack();
                    return;
                }

                var win = Window.GetWindow(this);
                if (win != null) win.Close();
            }
            catch { }
        }
    }
}
