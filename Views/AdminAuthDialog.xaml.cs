using System.Windows;
using TurizmOperasyonSistemi.Services;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class AdminAuthDialog : Window
    {
        public Models.User? AuthenticatedUser { get; private set; }

        private readonly AuthenticationService _auth;

        public AdminAuthDialog()
        {
            InitializeComponent();
            _auth = new AuthenticationService(new AppDbContext());
        }

        private async void Ok_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text?.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Lütfen kullanıcı adı ve şifre giriniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var (success, message, user) = await _auth.LoginAsync(username, password);
                if (success && user != null && string.Equals(user.Role, "admin", System.StringComparison.OrdinalIgnoreCase))
                {
                    AuthenticatedUser = user;
                    this.DialogResult = true;
                    this.Close();
                    return;
                }

                MessageBox.Show("Admin yetkisi doğrulanamadı.", "Erişim Reddedildi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Doğrulama hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
