using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PasswordChangeDialog : Window
    {
        public string NewPassword { get; private set; }

        public PasswordChangeDialog()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                MessageBox.Show("Yeni şifre boş olamaz.");
                return;
            }

            if (PasswordBox.Password.Length < 6)
            {
                MessageBox.Show("Şifre en az 6 karakter olmalıdır.");
                return;
            }

            NewPassword = PasswordBox.Password;
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
