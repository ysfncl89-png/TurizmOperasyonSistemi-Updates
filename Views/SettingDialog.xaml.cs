using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using TurizmOperasyonSistemi.Services;

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

            // Turkish -> English mapping for database compatibility
            SettingType = TranslateSettingType(settingType);

            TitleText.Text = settingType + " Ekle";

            LoadSettings();
            // Eğer Uyruk dialogu ilk kez açılıyorsa ve veri yoksa, ülkeleri otomatik seed'le
            if (SettingType == "Uyruk")
            {
                using (var db = new AppDbContext())
                {
                    if (!db.AppSettings.Any(x => x.Type == "Uyruk"))
                    {
                        SeedCountries();
                        LoadSettings();
                    }
                }
            }
            // Yeni eklenen kısmi sabit setler için başlangıç seçeneklerini otomatik ekle
            if (SettingType == "Vize Durumu" || SettingType == "Pasaportu Geldi mi ?" || SettingType == "Bilet Durumu")
            {
                using (var db = new AppDbContext())
                {
                    if (!db.AppSettings.Any(x => x.Type == SettingType))
                    {
                        SeedDefaultOptionsFor(SettingType);
                        LoadSettings();
                    }
                }
            }
        }

        private void SeedDefaultOptionsFor(string type)
        {
            using (var db = new AppDbContext())
            {
                List<string> options = new List<string>();

                if (type == "Vize Durumu")
                    options = new List<string> { "Vize Çıktı", "Vize Çıkmadı" };
                else if (type == "Pasaportu Geldi mi ?")
                    options = new List<string> { "Evet", "Hayır" };
                else if (type == "Bilet Durumu")
                    options = new List<string> { "Bileti geldi", "Bileti henüz gelmedi" };

                int sort = 1;

                foreach (var opt in options)
                {
                    if (!db.AppSettings.Any(x => x.Type == type && x.Value == opt))
                    {
                        db.AppSettings.Add(new AppSetting
                        {
                            Type = type,
                            Value = opt,
                            IsActive = true,
                            SortOrder = sort++
                        });
                    }
                }

                db.SaveChanges();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool isNew = selectedSettingId == 0;
            string addedValue = null;
            using (AppDbContext db = new AppDbContext())
            {
                AppSetting setting;
                if (selectedSettingId > 0)
                {
                    setting = db.AppSettings.FirstOrDefault(x => x.Id == selectedSettingId);
                    if (setting == null) return;
                }
                else
                {
                    setting = new AppSetting { Type = SettingType };
                    db.AppSettings.Add(setting);
                }

                string newValue = ValueTextBox.Text.Trim();
                addedValue = newValue;

                bool exists = db.AppSettings.Any(x => x.Type == SettingType && x.Value.ToLower() == newValue.ToLower() && x.Id != selectedSettingId);

                if (exists)
                {
                    MessageBox.Show("Bu kayıt zaten mevcut.");
                    return;
                }

                setting.Value = newValue;

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

            // Notify subscribers that settings changed
            OnSettingsChanged?.Invoke();

            // Publish a user-visible notification when a NEW option was added to a setting card
            try
            {
                if (isNew)
                {
                    // map setting types to modules / combobox names where they will appear
                    var appearsIn = new List<string>();
                    switch (SettingType)
                    {
                        case "TourType":
                            appearsIn.AddRange(new[] { "Kişiler sayfası -> Tur Türü", "Kayıt Sihirbazı -> Tur Türü", "Ödemeler -> Tur seçimi" });
                            break;
                        case "Dönem":
                            appearsIn.AddRange(new[] { "Kayıt Sihirbazı -> Dönem", "Kişiler sayfası -> Dönem", "Ödemeler -> Dönem" });
                            break;
                        case "GroupType":
                            appearsIn.AddRange(new[] { "Kayıt Sihirbazı -> Grup Türü", "Kişiler sayfası -> Grup Türü", "Ödemeler -> Grup Türü" });
                            break;
                        case "Kaç Günlük Grup":
                            appearsIn.AddRange(new[] { "Kayıt Sihirbazı -> Gün Sayısı", "Kişiler sayfası -> Grup Gün Sayısı", "Ödemeler -> Gün Sayısı" });
                            break;
                        case "Otel Ekle":
                            appearsIn.AddRange(new[] { "Odalama modülü -> Otel seçimi", "Kayıt Sihirbazı -> Otel seçim kutusu" });
                            break;
                        case "Para Birimi":
                            appearsIn.AddRange(new[] { "Ödeme penceresi -> Para Birimi", "Ödemeler sayfası -> Para Birimi listesi" });
                            break;
                        case "Kargo Talebi":
                            appearsIn.AddRange(new[] { "Kişiler sayfası -> Kargo Talebi" });
                            break;
                        case "Vize Talebi":
                            appearsIn.AddRange(new[] { "Kişiler sayfası -> Vize Talebi" });
                            break;
                        case "Bilet Talebi":
                            appearsIn.AddRange(new[] { "Kişiler sayfası -> Bilet Talebi" });
                            break;
                        default:
                            appearsIn.Add("Programdaki ilgili açılır listelerde görünecektir");
                            break;
                    }

                    var payload = new
                    {
                        Type = SettingType,
                        Value = addedValue,
                        AppearsIn = appearsIn,
                        Message = $"Ayarlar -> '{SettingType}' kartına '{addedValue}' seçeneği eklendi. Görüneceği yerler: " + string.Join("; ", appearsIn),
                        Timestamp = DateTime.UtcNow
                    };

                    NotificationCenter.Publish("setting.added", payload, persistToDb: true);
                }
            }
            catch { }

            MessageBox.Show(isNew ? "Kayıt eklendi" : "Kayıt güncellendi");
        }

        private void SeedCountries()
        {
            using (AppDbContext db = new AppDbContext())
            {
                // Sil eski Uyruk kayıtlarını ve yalnızca TC / Yabancı olarak yeniden oluştur
                var existingSettings = db.AppSettings.Where(x => x.Type == "Uyruk").ToList();
                if (existingSettings.Any())
                {
                    db.AppSettings.RemoveRange(existingSettings);
                    db.SaveChanges();
                }

                var countries = new List<string> { "TC", "Yabancı" };
                int sort = 1;
                foreach (var c in countries)
                {
                    db.AppSettings.Add(new AppSetting
                    {
                        Type = "Uyruk",
                        Value = c,
                        IsActive = true,
                        SortOrder = sort++
                    });
                }

                db.SaveChanges();
            }
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
                var settings = db.AppSettings
                    .Where(x => x.Type == SettingType)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();

                SettingsDataGrid.ItemsSource = settings;
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
                    .FirstOrDefault(x => x.Id == id);

                if (setting != null)
                {
                    selectedSettingId = setting.Id;

                    ValueTextBox.Text = setting.Value;

                    SortOrderTextBox.Text =
                        setting.SortOrder.ToString();
                }
            }
        }

        /// <summary>
        /// Turkish setting labels to database type names mapping
        /// </summary>
        private string TranslateSettingType(string turkishLabel)
        {
            return turkishLabel switch
            {
                "Tur Türü" => "TourType",
                "Grup Türü" => "GroupType",
                "Oda Türü" => "RoomType",
                _ => turkishLabel  // Return as-is if no mapping found
            };
        }
    }
}