using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class SettingDialog : Window
    {
        public static event Action OnSettingsChanged;

        public string SettingType { get; set; }

        private int selectedSettingId = 0;

        public SettingDialog(string settingType)
        {
            InitializeComponent();

            SettingType = settingType;

            TitleText.Text = settingType + " Ekle";

            LoadSettings();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            using (AppDbContext db = new AppDbContext())
            {
                AppSetting setting;

                if (selectedSettingId > 0)
                {
                    setting = db.AppSettings
                        .FirstOrDefault(x => x.Id == selectedSettingId);

                    if (setting == null)
                        return;
                }
                else
                {
                    setting = new AppSetting();

                    setting.Type = SettingType;

                    db.AppSettings.Add(setting);
                }

                string newValue = ValueTextBox.Text.Trim().ToLower();

                bool exists = db.AppSettings.Any(x =>
                    x.Type == SettingType &&
                    x.Value.ToLower() == newValue &&
                    x.Id != selectedSettingId);

                if (exists)
                {
                    MessageBox.Show("Bu kayıt zaten mevcut.");
                    return;
                }

                setting.Value = ValueTextBox.Text.Trim();

                setting.SortOrder =
                    int.TryParse(SortOrderTextBox.Text, out int order)
                    ? order
                    : 0;

                setting.IsActive = true;

                db.SaveChanges();

                selectedSettingId = 0;

                ValueTextBox.Clear();
                SortOrderTextBox.Clear();

                SettingsDataGrid.SelectedItem = null;

                LoadSettings();

                if (SettingType == "Aktarma Şehri")
                {
                    SeedCities();

                    LoadSettings();
                }
            }

            MessageBox.Show("Kayıt eklendi");
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            int id = (int)button.Tag;

            using (var db = new AppDbContext())
            {
                var setting = db.AppSettings.FirstOrDefault(x => x.Id == id);

                if (setting != null)
                {
                    setting.IsActive = !setting.IsActive;

                    db.SaveChanges();
                }
            }

            LoadSettings();

            OnSettingsChanged?.Invoke();
        }

        private void SeedCities()
        {
            using (AppDbContext db = new AppDbContext())
            {
                var existingCities = db.AppSettings
                    .Where(x => x.Type == "Aktarma Şehri")
                    .Select(x => x.Value)
                    .ToList();

                List<string> cities = new List<string>()
                {
                    "Adana","Adıyaman","Afyonkarahisar","Ağrı","Amasya","Ankara","Antalya","Artvin",
                    "Aydın","Balıkesir","Bilecik","Bingöl","Bitlis","Bolu","Burdur","Bursa",
                    "Çanakkale","Çankırı","Çorum","Denizli","Diyarbakır","Edirne","Elazığ","Erzincan",
                    "Erzurum","Eskişehir","Gaziantep","Giresun","Gümüşhane","Hakkari","Hatay","Isparta",
                    "Mersin","İstanbul","İzmir","Kars","Kastamonu","Kayseri","Kırklareli","Kırşehir",
                    "Kocaeli","Konya","Kütahya","Malatya","Manisa","Kahramanmaraş","Mardin","Muğla",
                    "Muş","Nevşehir","Niğde","Ordu","Rize","Sakarya","Samsun","Siirt","Sinop","Sivas",
                    "Tekirdağ","Tokat","Trabzon","Tunceli","Şanlıurfa","Uşak","Van","Yozgat","Zonguldak",
                    "Aksaray","Bayburt","Karaman","Kırıkkale","Batman","Şırnak","Bartın","Ardahan","Iğdır",
                    "Yalova","Karabük","Kilis","Osmaniye","Düzce"
                };

                int sort = 1;

                foreach (var city in cities)
                {
                    if (!existingCities.Contains(city))
                    {
                        db.AppSettings.Add(new AppSetting
                        {
                            Type = "Aktarma Şehri",
                            Value = city,
                            IsActive = true,
                            SortOrder = sort++
                        });
                    }
                }

                db.SaveChanges();
            }

            LoadSettings();
        }

        private void LoadSettings()
        {
            using (AppDbContext db = new AppDbContext())
            {
                SettingsDataGrid.ItemsSource = db.AppSettings
                    .Where(x => x.Type == SettingType)
                    .ToList();
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            if (button.Tag == null)
                return;

            int id = (int)button.Tag;

            using (AppDbContext db = new AppDbContext())
            {
                AppSetting setting = db.AppSettings
                    .FirstOrDefault(x =>
                        x.Id == id &&
                        x.IsActive);

                if (setting != null)
                {
                    selectedSettingId = setting.Id;

                    ValueTextBox.Text = setting.Value;

                    SortOrderTextBox.Text =
                        setting.SortOrder.ToString();
                }
            }
        }
    }
}