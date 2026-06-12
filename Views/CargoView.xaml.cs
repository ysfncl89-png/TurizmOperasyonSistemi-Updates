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
            // Geçici: ilerde gerçek sayfaya yönlendir
            System.Windows.MessageBox.Show("Kargo Listesi açıldı (placeholder)");
        }

        private void CargoDocs_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Kargo Evrak Hazırlama açıldı (placeholder)");
        }

        private void CargoTracking_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Kargo Takip açıldı (placeholder)");
        }
    }
}
