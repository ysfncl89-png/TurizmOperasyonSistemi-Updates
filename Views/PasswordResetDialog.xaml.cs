using System;
using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PasswordResetDialog : Window
    {
        public PasswordResetDialog()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text?.Trim();
            var pw = NewPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pw))
            {
                MessageBox.Show("Lütfen kullanıcı adı ve yeni şifre giriniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (pw.Length < 6)
            {
                MessageBox.Show("Şifre en az 6 karakter olmalıdır.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (pw != confirm)
            {
                MessageBox.Show("Şifreler eşleşmiyor.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var user = db.Users.FirstOrDefault(u => u.Username == username && u.Role == "admin");
                    if (user == null)
                    {
                        MessageBox.Show("Belirtilen admin kullanıcı bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    user.PasswordHash = PasswordHasher.HashPassword(pw);
                    db.SaveChanges();
                }

                MessageBox.Show("Parola başarıyla sıfırlandı. Lütfen yeni şifre ile giriş yapınız.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sıfırlama hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
