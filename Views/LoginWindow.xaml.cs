using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;
using System.Linq;

namespace TurizmOperasyonSistemi.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AuthenticationService _authService;
        private bool _isFirstUser = false;

        public LoginWindow()
        {
            InitializeComponent();
            _authService = new AuthenticationService(new AppDbContext());
            InitializeLogin();
        }

        private void ForgotPasswordLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new RecoveryCodeDialog();
                dlg.Owner = this;
                var res = dlg.ShowDialog();
                if (res != true)
                    return;

                var entered = dlg.EnteredCode ?? string.Empty;
                // Developer code required
                if (entered == "2026TOSYSFNCL")
                {
                    var resetDlg = new PasswordResetDialog();
                    resetDlg.Owner = this;
                    resetDlg.ShowDialog();
                }
                else
                {
                    MessageBox.Show("Girilen kod geçersiz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InitializeLogin()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Kullanıcı girişi ayarını kontrol et
                    var enableLoginSetting = db.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
                    bool enableLogin = enableLoginSetting != null && enableLoginSetting.Value.ToString().ToLower() == "true";

                    // Eğer EnableUserLogin ayarı mevcutsa ve false ise (no-login modu seçilmişse)
                    // kayıtlı kullanıcı olmasına bakılmaksızın uygulamayı doğrudan aç
                    if (enableLoginSetting != null && !enableLogin)
                    {
                        // Otomatik sistem kullanıcıyı admin olarak ayarla ki menüler görünsün
                        CurrentUser.Instance.SetUser(new Models.User { Id = -1, Username = "System", PasswordHash = "", Role = "admin", PermissionsJson = "[\"all\"]", CreatedAt = DateTime.Now });
                        MainWindow mainWindow = new MainWindow();
                        mainWindow.Show();
                        this.Close();
                        return;
                    }

                    // Önce sistemde kullanıcı var mı kontrol et (ilk kurulumda AppSetting eksikse kayıt ekranı gösterilecek)
                    var hasUsers = await _authService.HasUsersAsync();

                    if (!hasUsers)
                    {
                        // İlk kullanıcı oluşturma modu
                        _isFirstUser = true;
                        ShowRegisterPanel();

                        // Başlık değiştir
                        var title = (TextBlock)this.FindName("ErrorMessage") ?? new TextBlock();
                        var loginPanel = (StackPanel)this.FindName("LoginPanel");
                        var header = loginPanel?.Children[0] as TextBlock;
                        if (header != null)
                        {
                            header.Text = "İlk Kullanıcı Oluştur";
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}");
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Lütfen kullanıcı adı ve şifre giriniz.", ErrorMessage);
                return;
            }

            try
            {
                var (success, message, user) = await _authService.LoginAsync(username, password);

                if (success && user != null)
                {
                    // Giriş başarılı - MainWindow'u aç
                    CurrentUser.Instance.SetUser(user);
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();
                    this.Close();
                }
                else
                {
                    ShowError(message, ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Giriş hatası: {ex.Message}", ErrorMessage);
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            // Eğer ilk kurulumda ve no-login modu seçildiyse, kullanıcı oluşturmadan doğrudan no-login moda geç
            if (_isFirstUser && NoLoginModeCheckBox != null && NoLoginModeCheckBox.IsChecked == true)
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var setting = db.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
                        if (setting == null)
                        {
                            db.AppSettings.Add(new global::AppSetting { Type = "EnableUserLogin", Value = "false" });
                        }
                        else
                        {
                            setting.Value = "false";
                        }
                        db.SaveChanges();
                    }
                }
                catch { }

                // Kullanıcı oluşturulmadan sistem admin olarak oturum açılıyor (geçici System user)
                CurrentUser.Instance.SetUser(new Models.User { Id = -1, Username = "System", PasswordHash = "", Role = "admin", PermissionsJson = "[\"all\"]", CreatedAt = DateTime.Now });
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
                return;
            }

            string username = NewUsernameTextBox.Text;
            string password = NewPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Lütfen kullanıcı adı ve şifre giriniz.", RegisterErrorMessage);
                return;
            }

            try
            {
                var (success, message, user) = await _authService.RegisterAsync(username, password, confirmPassword, _isFirstUser ? "admin" : "user");

                if (success && user != null)
                {
                    if (_isFirstUser)
                    {
                        // Eğer kullanıcı 'no-login' modunu seçtiyse, sistemde EnableUserLogin ayarını false yap
                        if (NoLoginModeCheckBox != null && NoLoginModeCheckBox.IsChecked == true)
                        {
                            try
                            {
                                using (var db = new AppDbContext())
                                {
                                    var setting = db.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
                                    if (setting == null)
                                    {
                                        db.AppSettings.Add(new global::AppSetting { Type = "EnableUserLogin", Value = "false" });
                                    }
                                    else
                                    {
                                        setting.Value = "false";
                                    }
                                    db.SaveChanges();
                                }
                            }
                            catch { /* Hata loglanabilir, ama kayıt akışını etkilemesin */ }
                        }

                        // İlk kullanıcı oluşturuldu - direkt giriş yap
                        CurrentUser.Instance.SetUser(user);
                        MainWindow mainWindow = new MainWindow();
                        mainWindow.Show();
                        this.Close();
                    }
                    else
                    {
                        // Yeni kullanıcı oluşturuldu - login sayfasına dön
                        MessageBox.Show("Kayıt başarılı! Lütfen giriş yapınız.");
                        ShowLoginPanel();
                        ClearFields();
                    }
                }
                else
                {
                    ShowError(message, RegisterErrorMessage);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Kayıt hatası: {ex.Message}", RegisterErrorMessage);
            }
        }

        private void RegisterLink_Click(object sender, RoutedEventArgs e)
        {
            ShowRegisterPanel();
            ClearFields();
        }

        private void BackToLoginLink_Click(object sender, RoutedEventArgs e)
        {
            if (!_isFirstUser)
            {
                ShowLoginPanel();
                ClearFields();
            }
        }

        private void ShowLoginPanel()
        {
            LoginPanel.Visibility = Visibility.Visible;
            RegisterPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowRegisterPanel()
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Visible;
        }

        private void ShowError(string message, TextBlock errorTextBlock)
        {
            errorTextBlock.Text = message;
            errorTextBlock.Visibility = Visibility.Visible;
        }

        private void ClearFields()
        {
            UsernameTextBox.Clear();
            PasswordBox.Clear();
            NewUsernameTextBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            ErrorMessage.Visibility = Visibility.Collapsed;
            RegisterErrorMessage.Visibility = Visibility.Collapsed;
        }
    }
}
