using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using TurizmOperasyonSistemi.Services;
using Microsoft.EntityFrameworkCore;

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
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Kayıt hatası: {ex.Message}");
            }
        }
    }
}
