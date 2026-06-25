using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using TurizmOperasyonSistemi.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace TurizmOperasyonSistemi.Views
{
    public partial class UserManagementView : UserControl
    {
        public UserManagementView()
        {
            InitializeComponent();
            LoadUsers();
        }

        private async void LoadUsers()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var users = await db.Users.ToListAsync();
                    UsersDataGrid.ItemsSource = users;
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Kullanıcılar yüklenirken hata: {ex.Message}");
            }
        }

        private async void CreateUserButton_Click(object sender, RoutedEventArgs e)
        {
            var username = NewUsernameTextBox.Text?.Trim();
            var password = NewPasswordBox.Password;
            var role = (RoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "user";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Kullanıcı adı ve şifre giriniz.");
                return;
            }

            try
            {
                var auth = new AuthenticationService(new AppDbContext());
                var (success, message, user) = await auth.RegisterAsync(username, password, password, role);
                if (!success)
                {
                    MessageBox.Show(message);
                    return;
                }

                MessageBox.Show("Kullanıcı oluşturuldu.");
                LoadUsers();
                NewUsernameTextBox.Clear();
                NewPasswordBox.Clear();
                RoleComboBox.SelectedIndex = -1;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Kayıt hatası: {ex.Message}");
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int userId))
            {
                MessageBox.Show("Silinecek kullanıcı bulunamadı.");
                return;
            }

            // Mevcut admin'in kendini silmemesi için kontrol
            var currentUser = CurrentUser.Instance.User;
            if (currentUser?.Id == userId)
            {
                MessageBox.Show("Kendi hesabınızı silemezsiniz.");
                return;
            }

            var result = MessageBox.Show("Bu kullanıcıyı silmek istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                var auth = new AuthenticationService(new AppDbContext());
                var (success, message) = await auth.DeleteUserAsync(userId);

                if (!success)
                {
                    MessageBox.Show(message);
                    return;
                }

                MessageBox.Show("Kullanıcı başarıyla silindi.");
                LoadUsers();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Silme hatası: {ex.Message}");
            }
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int userId))
            {
                MessageBox.Show("Kullanıcı bulunamadı.");
                return;
            }

            var passwordDialog = new PasswordChangeDialog();
            if (passwordDialog.ShowDialog() != true)
                return;

            var newPassword = passwordDialog.NewPassword;
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                MessageBox.Show("Şifre boş olamaz.");
                return;
            }

            try
            {
                var auth = new AuthenticationService(new AppDbContext());
                var (success, message) = await auth.UpdateUserPasswordAsync(userId, newPassword);

                if (!success)
                {
                    MessageBox.Show(message);
                    return;
                }

                MessageBox.Show("Şifre başarıyla güncellendi.");
                LoadUsers();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Şifre güncelleme hatası: {ex.Message}");
            }
        }

        private async void ChangeRoleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int userId))
            {
                MessageBox.Show("Kullanıcı bulunamadı.");
                return;
            }

            // Mevcut admin'in rolünü değiştirmemesi için kontrol
            var currentUser = CurrentUser.Instance.User;
            if (currentUser?.Id == userId)
            {
                MessageBox.Show("Kendi rolünüzü değiştiremezsiniz.");
                return;
            }

            var roleDialog = new RoleChangeDialog();
            if (roleDialog.ShowDialog() != true)
                return;

            var newRole = roleDialog.SelectedRole;
            if (string.IsNullOrWhiteSpace(newRole))
            {
                MessageBox.Show("Rol seçiniz.");
                return;
            }

            try
            {
                var auth = new AuthenticationService(new AppDbContext());
                var (success, message) = await auth.UpdateUserRoleAsync(userId, newRole);

                if (!success)
                {
                    MessageBox.Show(message);
                    return;
                }

                MessageBox.Show("Rol başarıyla güncellendi.");
                LoadUsers();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Rol güncelleme hatası: {ex.Message}");
            }
        }

        private void EnableUserLoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var setting = db.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
                    if (setting != null)
                    {
                        bool currentValue = setting.Value.ToString().ToLower() == "true";
                        setting.Value = (!currentValue).ToString().ToLower();
                        db.SaveChanges();

                        string status = currentValue ? "devre dışı bırakıldı" : "etkinleştirildi";
                        MessageBox.Show($"Kullanıcı girişi {status}.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                        UpdateLoginButtonAppearance(db);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateLoginButtonAppearance(AppDbContext db)
        {
            var setting = db.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
            if (setting != null && FindName("EnableUserLoginButton") is Button btn)
            {
                bool isEnabled = setting.Value.ToString().ToLower() == "true";
                btn.Content = isEnabled ? "✓ Kullanıcı Girişi (Aktif)" : "Kullanıcı Girişi";
                btn.Foreground = isEnabled ? new SolidColorBrush(Color.FromRgb(0, 255, 136)) : new SolidColorBrush(Color.FromRgb(255, 255, 255));
            }
        }
    }
}
