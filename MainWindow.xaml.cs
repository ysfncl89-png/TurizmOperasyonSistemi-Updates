using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Views;

namespace TurizmOperasyonSistemi
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent(); 

            MainFrame.Navigate(new HomeView());
            // Varsayılan olarak Anasayfa seçili görünmeli
            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9C1A15"));
        }

        private void ResetMenuColors()
        {
            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            PersonsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            ListsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            SettingsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            HotelAllocationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            PaymentsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            CargoButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));

            // 🔹 Yeni eklediğimiz kayıt butonunun rengini de burada sıfırlıyoruz
            RegistrationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();
            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9C1A15"));
            MainFrame.Navigate(new HomeView());
        }

        private void PersonsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();
            PersonsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF06B6D4"));
            MainFrame.Navigate(new PersonsWizardView());
        }

        private void ListsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();
            ListsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            MainFrame.Navigate(new ListsView());
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();
            SettingsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EC4899"));
            MainFrame.Navigate(new SettingsView());
        }

        // 🔥 YENİ: KAYIT MODÜLÜ BUTON TIKLAMA OLAYI
        private void RegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            // Buton aktifleşince Amber/Turuncu renge bürünsün
            RegistrationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            // Yeni oluşturduğumuz sihirbaz sayfasına yönlendiriyoruz
            MainFrame.Navigate(new RegistrationsWizardView());
        }
        private void HotelAllocationButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();
            HotelAllocationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            MainFrame.Navigate(new HotelAllocationView());
        }

        // Yeni: Ödemeler ve Finans modülü buton tıklama olayı
        private void PaymentsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            // Ödemeler için özel renk (mor tonlu, başka butonlardan farklı)
            PaymentsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));

            // Ödemeler için oluşturduğumuz PaymentsView'e yönlendiriyoruz
            MainFrame.Navigate(new PaymentsView());
        }

        private void CargoButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            // Kargo seçili iken özel bir mavi renge bürünsün
            CargoButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));

            // Kargo sayfasına git
            MainFrame.Navigate(new Views.CargoView());
        }

        // Programatik olarak anasayfaya geçiş yapmak isteyen diğer bileşenler için
        public void NavigateToHome()
        {
            ResetMenuColors();
            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9C1A15"));
            MainFrame.Navigate(new HomeView());
        }

        // Genel navigasyon yardımcı metodu: dışarıdan UserControl'a geçiş yapar
        public void NavigateTo(System.Windows.Controls.UserControl view)
        {
            MainFrame.Navigate(view);
        }
    }
}
