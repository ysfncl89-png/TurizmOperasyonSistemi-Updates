using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class EditTitleDialog : Window
    {
        public EditTitleDialog(string initial)
        {
            InitializeComponent();
            TitleTextBox.Text = initial ?? string.Empty;
            TitleTextBox.SelectAll();
            TitleTextBox.Focus();
        }

        public string EditedTitle => TitleTextBox.Text;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
