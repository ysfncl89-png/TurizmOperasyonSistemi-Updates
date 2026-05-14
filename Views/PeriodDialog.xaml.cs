using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Linq;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PeriodDialog : Window
    {
        private int selectedId = 0;

        public PeriodDialog()
        {
            InitializeComponent();

            for (int year = 2026; year <= 2126; year++)
            {
                YearComboBox.Items.Add(year.ToString());
            }

            LoadPeriods();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string month =
                ((ComboBoxItem)MonthComboBox.SelectedItem).Content.ToString();

            string year = YearComboBox.SelectedItem.ToString();

            string period = month + " " + year;

            using (AppDbContext db = new AppDbContext())
            {
                AppSetting setting;

                if (selectedId > 0)
                {
                    setting = db.AppSettings
                        .FirstOrDefault(x => x.Id == selectedId);
                }
                else
                {
                    setting = new AppSetting();

                    db.AppSettings.Add(setting);
                }

                setting.Type = "Dönem";
                setting.Value = period;
                setting.IsActive = true;

                db.SaveChanges();

                LoadPeriods();
            }

            selectedId = 0;

            MessageBox.Show("Dönem eklendi");
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            if (button.Tag == null)
                return;

            int id = (int)button.Tag;

            using (AppDbContext db = new AppDbContext())
            {
                AppSetting setting = db.AppSettings
                    .FirstOrDefault(x => x.Id == id);

                if (setting != null)
                {
                    db.AppSettings.Remove(setting);

                    db.SaveChanges();
                }
            }

            LoadPeriods();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            int id = (int)button.Tag;

            using (AppDbContext db = new AppDbContext())
            {
                var setting = db.AppSettings
                    .FirstOrDefault(x => x.Id == id);

                if (setting == null)
                    return;

                selectedId = setting.Id;

                string[] parts = setting.Value.Split(' ');

                if (parts.Length >= 2)
                {
                    MonthComboBox.Text = parts[0];
                    YearComboBox.Text = parts[1];
                }
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            int id = (int)button.Tag;

            using (AppDbContext db = new AppDbContext())
            {
                var setting = db.AppSettings
                    .FirstOrDefault(x => x.Id == id);

                if (setting != null)
                {
                    setting.IsActive = !setting.IsActive;

                    db.SaveChanges();
                }
            }

            LoadPeriods();
        }

        private void LoadPeriods()
        {
            using (AppDbContext db = new AppDbContext())
            {
                PeriodsDataGrid.ItemsSource = db.AppSettings
                    .Where(x => x.Type == "Dönem")
                    .ToList();
            }
        }
    }
}