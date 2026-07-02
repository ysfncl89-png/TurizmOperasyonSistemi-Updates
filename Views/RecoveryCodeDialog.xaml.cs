using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RecoveryCodeDialog : Window
    {
        public string EnteredCode { get; private set; }

        public RecoveryCodeDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            EnteredCode = CodeTextBox.Text?.Trim() ?? string.Empty;
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
