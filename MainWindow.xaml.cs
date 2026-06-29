using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Views;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent(); 

            // Kullanıcı yönetimi ve modül butonlarını kullanıcının izinlerine göre göster
            // Show buttons if user has specific permission OR is admin (backward compatible)

            // Anasayfa: herkes görebilir
            HomeButton.Visibility = Visibility.Visible;

            // Kişiler modülü: admin, moderatör ve genel modüllere erişebilenlere göster
            PersonsButton.Visibility = (CurrentUser.Instance.IsAdmin || 
                string.Equals(CurrentUser.Instance.User?.Role, "moderatör", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CurrentUser.Instance.User?.Role, "moderator", StringComparison.OrdinalIgnoreCase)) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Tur Kayıt: admin, moderatör ve genel modüllere erişebilenlere göster
            RegistrationButton.Visibility = (CurrentUser.Instance.IsAdmin || 
                string.Equals(CurrentUser.Instance.User?.Role, "moderatör", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CurrentUser.Instance.User?.Role, "moderator", StringComparison.OrdinalIgnoreCase)) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Odalama Motoru: admin, moderatör ve genel modüllere erişebilenlere göster
            HotelAllocationButton.Visibility = (CurrentUser.Instance.IsAdmin || 
                string.Equals(CurrentUser.Instance.User?.Role, "moderatör", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CurrentUser.Instance.User?.Role, "moderator", StringComparison.OrdinalIgnoreCase)) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Ayarlar: sadece admin görebilir
            SettingsButton.Visibility = (CurrentUser.Instance.IsAdmin) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Ödemeler ve Finans: admin, moderatör, muhasebe görebilir
            PaymentsButton.Visibility = (CurrentUser.Instance.IsAdmin || 
                CurrentUser.Instance.HasPermission(PermissionKeys.Payments)) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Kargo: admin, moderatör, kargo görebilir
            CargoButton.Visibility = (CurrentUser.Instance.IsAdmin || 
                CurrentUser.Instance.HasPermission(PermissionKeys.Cargo)) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Listeler: admin, moderatör, kargo, kullanıcı görebilir
            ListsButton.Visibility = (CurrentUser.Instance.IsAdmin || 
                CurrentUser.Instance.HasPermission(PermissionKeys.ListsView)) 
                ? Visibility.Visible : Visibility.Collapsed;

            // Kullanıcı yönetimi: sadece admin görebilir
            UserManagementButton.Visibility = (CurrentUser.Instance.IsAdmin || CurrentUser.Instance.HasPermission(PermissionKeys.UserManagement)) ? Visibility.Visible : Visibility.Collapsed;

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

            // Yeni eklediğimiz kayıt butonunun rengini de burada sıfırlıyoruz
            RegistrationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));

            // Kullanıcı Yönetimi butonunun rengini de sıfırlıyoruz
            UserManagementButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();
            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9C1A15"));
            MainFrame.Navigate(new HomeView());
        }

        private void PersonsButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: kişiler modülüne erişim izni var mı?
            if (!CurrentUser.Instance.IsAdmin && 
                !string.Equals(CurrentUser.Instance.User?.Role, "moderatör", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(CurrentUser.Instance.User?.Role, "moderator", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();
            PersonsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF06B6D4"));
            MainFrame.Navigate(new PersonsWizardView());
        }

        private void ListsButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: listeler modülüne erişim izni var mı?
            if (!CurrentUser.Instance.IsAdmin && !CurrentUser.Instance.HasPermission(PermissionKeys.ListsView))
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();
            ListsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            MainFrame.Navigate(new ListsView());
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: ayarlara erişim izni var mı? (sadece admin)
            if (!CurrentUser.Instance.IsAdmin)
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();
            SettingsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EC4899"));
            MainFrame.Navigate(new SettingsView());
        }

        // YENİ: KAYIT MODÜLÜ BUTON TIKLAMA OLAYI
        private void RegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: tur kayıt modülüne erişim izni var mı?
            if (!CurrentUser.Instance.IsAdmin && 
                !string.Equals(CurrentUser.Instance.User?.Role, "moderatör", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(CurrentUser.Instance.User?.Role, "moderator", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();

            // Buton aktifleşince Amber/Turuncu renge bürünsün
            RegistrationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            // Yeni oluşturduğumuz sihirbaz sayfasına yönlendiriyoruz
            MainFrame.Navigate(new RegistrationsWizardView());
        }

        private void HotelAllocationButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: odalama modülüne erişim izni var mı?
            if (!CurrentUser.Instance.IsAdmin && 
                !string.Equals(CurrentUser.Instance.User?.Role, "moderatör", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(CurrentUser.Instance.User?.Role, "moderator", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();
            HotelAllocationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            MainFrame.Navigate(new HotelAllocationView());
        }

        // Yeni: Ödemeler ve Finans modülü buton tıklama olayı
        private void PaymentsButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: ödemeler modülüne erişim izni var mı?
            if (!CurrentUser.Instance.IsAdmin && !CurrentUser.Instance.HasPermission(PermissionKeys.Payments))
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();

            // Ödemeler için özel renk (mor tonlu, başka butonlardan farklı)
            PaymentsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));

            // Ödemeler için oluşturduğumuz PaymentsView'e yönlendiriyoruz
            MainFrame.Navigate(new PaymentsView());
        }

        private void CargoButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: kargo modülüne erişim izni var mı?
            if (!CurrentUser.Instance.IsAdmin && !CurrentUser.Instance.HasPermission(PermissionKeys.Cargo))
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();

            // Kargo seçili iken özel bir mavi renge bürünsün
            CargoButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));

            // Kargo sayfasına git
            MainFrame.Navigate(new Views.CargoView());
        }

        private void UserManagementButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontrol: kullanıcı yönetimine erişim izni var mı? (sadece admin)
            if (!CurrentUser.Instance.IsAdmin)
            {
                System.Windows.MessageBox.Show("Bu modüle erişim izniniz yok.", "Erişim Reddedildi", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetMenuColors();
            UserManagementButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
            MainFrame.Navigate(new Views.UserManagementView());
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
            System.Diagnostics.Debug.WriteLine("=== NavigateTo başladı ===");
            try
            {
                System.Diagnostics.Debug.WriteLine($"View tipi: {view.GetType().Name}");
                System.Diagnostics.Debug.WriteLine("ResetMenuColors çağrılıyor...");
                ResetMenuColors();

                // PaymentsView'e geri dönüyorsak, buton rengini ayarla
                if (view is PaymentsView)
                {
                    System.Diagnostics.Debug.WriteLine("PaymentsView tespit edildi.");
                    PaymentsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
                }

                // Tur kârlılık analizi, payments modülünün alt ekranı gibi davransın
                if (view is TourProfitabilityAnalysisWindow)
                {
                    System.Diagnostics.Debug.WriteLine("TourProfitabilityAnalysisWindow tespit edildi.");
                    PaymentsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
                }

                // CargoTrackingInfoView'e geri dönüyorsak, buton rengini ayarla
                if (view is CargoTrackingInfoView)
                {
                    System.Diagnostics.Debug.WriteLine("CargoTrackingInfoView tespit edildi, buton rengi ayarlanıyor...");
                    CargoButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
                }

                System.Diagnostics.Debug.WriteLine($"MainFrame: {MainFrame}");
                System.Diagnostics.Debug.WriteLine($"MainFrame.Name: {MainFrame?.Name}");
                System.Diagnostics.Debug.WriteLine("MainFrame.Content ayarlanıyor...");

                // MainFrame.Content kullanarak UserControl'u doğrudan göster
                MainFrame.Content = view;

                System.Diagnostics.Debug.WriteLine($"MainFrame.Content set edildi: {MainFrame.Content?.GetType().Name}");
                System.Diagnostics.Debug.WriteLine("NavigateTo başarılı!");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in NavigateTo: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                System.Windows.MessageBox.Show($"NavigateTo Error:\n{ex.GetType().Name}\n\n{ex.Message}\n\n{ex.StackTrace}", "Hata",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            System.Diagnostics.Debug.WriteLine("=== NavigateTo bitti ===");
        }
    }
}
