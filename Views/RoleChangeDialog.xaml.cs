using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RoleChangeDialog : Window
    {
        public string SelectedRole { get; private set; }

        public RoleChangeDialog()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            RoleComboBox.Items.Add("admin");
            RoleComboBox.Items.Add("moderatör");
            RoleComboBox.Items.Add("muhasebe");
            RoleComboBox.Items.Add("kargo");
            RoleComboBox.Items.Add("kullanıcı");
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (RoleComboBox.SelectedItem == null)
            {
                MessageBox.Show("Rol seçiniz.");
                return;
            }

            SelectedRole = RoleComboBox.SelectedItem.ToString();
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
