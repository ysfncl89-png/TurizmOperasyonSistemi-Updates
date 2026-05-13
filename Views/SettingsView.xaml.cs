using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class SettingsView : Page
    {
        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (TypeComboBox.SelectedItem == null ||
                string.IsNullOrWhiteSpace(ValueTextBox.Text))
            {
                MessageBox.Show("Lütfen alanları doldurun.");
                return;
            }

            string selectedType =
                (TypeComboBox.SelectedItem as ComboBoxItem)
                ?.Content?.ToString();

            using (var db = new AppDbContext())
            {
                bool exists = db.AppSettings.Any(x =>
                    x.Type == selectedType &&
                    x.Value == ValueTextBox.Text);

                if (exists)
                {
                    MessageBox.Show("Bu kayıt zaten موجود.");
                    return;
                }

                AppSetting setting = new AppSetting()
                {
                    Type = selectedType,
                    Value = ValueTextBox.Text
                };

                db.AppSettings.Add(setting);
                db.SaveChanges();
            }

            MessageBox.Show("Ayar kaydedildi.");

            ValueTextBox.Clear();

            LoadSettings();
        }

        private void LoadSettings()
        {
            using (var db = new AppDbContext())
            {
                SettingsDataGrid.ItemsSource =
                    db.AppSettings.ToList();
            }
        }
    }
}