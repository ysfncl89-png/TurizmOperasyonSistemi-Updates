using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class SettingsView : Page
    {
        private List<string> cities = new List<string> {
            "Adana","Adıyaman","Afyonkarahisar","Ağrı","Amasya","Ankara","Antalya","Artvin","Aydın",
            "Balıkesir","Bilecik","Bingöl","Bitlis","Bolu","Burdur","Bursa","Çanakkale","Çankırı","Çorum",
            "Denizli","Diyarbakır","Edirne","Elazığ","Erzincan","Erzurum","Eskişehir","Gaziantep","Giresun","Gümüşhane",
            "Hakkari","Hatay","Isparta","Mersin","İstanbul","İzmir","Kars","Kastamonu","Kayseri","Kırıkkale","Kırklareli",
            "Kırşehir","Kocaeli","Konya","Kütahya","Malatya","Manisa","Kahramanmaraş","Mardin","Muğla","Muş",
            "Nevşehir","Niğde","Ordu","Rize","Sakarya","Samsun","Siirt","Sinop","Sivas","Tekirdağ","Tokat",
            "Trabzon","Tunceli","Uşak","Van","Yozgat","Zonguldak","Aksaray","Bayburt","Karaman","Batman",
            "Şırnak","Bartın","Ardahan","Iğdır","Yalova","Karabük","Kilis","Osmaniye","Düzce"
        };

        public SettingsView()
        {
            InitializeComponent();
            SortSettingCardsAlphabetically();
            LoadExistingSettingsIntoUi();
        }

        private void SortSettingCardsAlphabetically()
        {
            try
            {
                // EnableUserLoginButton'u kaydet
                var enableLoginBtn = SettingsWrapPanel.Children.OfType<Button>()
                    .FirstOrDefault(b => b.Name == "EnableUserLoginButton");

                var comparer = StringComparer.Create(new CultureInfo("tr-TR"), false);
                var buttons = SettingsWrapPanel.Children.OfType<Button>()
                    .Where(b => b.Name != "EnableUserLoginButton") // Kullanıcı Girişi butonunu hariç tut
                    .ToList();
                var sorted = buttons.OrderBy(b => (b.Content ?? string.Empty).ToString(), comparer).ToList();

                SettingsWrapPanel.Children.Clear();
                foreach (var b in sorted)
                    SettingsWrapPanel.Children.Add(b);

                // Sonda EnableUserLoginButton'u ekle (sağ alt köşede kalması için)
                if (enableLoginBtn != null)
                    SettingsWrapPanel.Children.Add(enableLoginBtn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Kartları alfabetik sıraya dizerken hata: {ex.Message}");
            }
        }

        private void LoadExistingSettingsIntoUi()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var uyrukValues = db.AppSettings.Where(x => x.Type == "Uyruk").Select(x => x.Value).ToList();
                    if (uyrukValues.Count == 0)
                    {
                        var desired = new[] { "TC", "Yabancı" };
                        int sort = 1;
                        foreach (var val in desired)
                        {
                            db.AppSettings.Add(new AppSetting
                            {
                                Type = "Uyruk",
                                Value = val,
                                IsActive = true,
                                SortOrder = sort++
                            });
                        }
                        db.SaveChanges();
                    }

                    // Ensure EnableUserLogin setting exists
                    var enableLoginSetting = db.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
                    if (enableLoginSetting == null)
                    {
                        db.AppSettings.Add(new AppSetting
                        {
                            Type = "EnableUserLogin",
                            Value = "false",
                            IsActive = true,
                            SortOrder = 1
                        });
                        db.SaveChanges();
                    }

                    var allSettings = db.AppSettings.ToList();

                    if (FindName("TourTypeListControl") is ItemsControl tourList)
                        tourList.ItemsSource = allSettings.Where(x => x.Type == "TourType").Select(x => x.Value).ToList();

                    if (FindName("GroupTypeListControl") is ItemsControl groupList)
                        groupList.ItemsSource = allSettings.Where(x => x.Type == "GroupType").Select(x => x.Value).ToList();

                    if (FindName("RoomTypeListControl") is ItemsControl roomList)
                        roomList.ItemsSource = allSettings.Where(x => x.Type == "RoomType").Select(x => x.Value).ToList();

                    if (FindName("PeriodListControl") is ItemsControl periodList)
                        periodList.ItemsSource = allSettings.Where(x => x.Type == "Dönem").Select(x => x.Value).ToList();

                    if (FindName("DurationListControl") is ItemsControl durationList)
                        durationList.ItemsSource = allSettings.Where(x => x.Type == "Kaç Günlük Grup").Select(x => x.Value).ToList();

                    if (FindName("HotelListControl") is ItemsControl hotelList)
                        hotelList.ItemsSource = allSettings.Where(x => x.Type == "Otel Ekle").Select(x => x.Value).ToList();

                    if (FindName("VisaListControl") is ItemsControl visaList)
                        visaList.ItemsSource = allSettings.Where(x => x.Type == "Vize Talebi").Select(x => x.Value).ToList();

                    if (FindName("TicketListControl") is ItemsControl ticketList)
                        ticketList.ItemsSource = allSettings.Where(x => x.Type == "Bilet Talebi").Select(x => x.Value).ToList();

                    if (FindName("CargoListControl") is ItemsControl cargoList)
                        cargoList.ItemsSource = allSettings.Where(x => x.Type == "Kargo Talebi").Select(x => x.Value).ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ayarlar listelenirken hata: {ex.Message}");
            }
        }

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string settingType = btn.Content.ToString();
                SettingDialog dialog = new SettingDialog(settingType);
                dialog.ShowDialog();
                LoadExistingSettingsIntoUi();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox searchBox)
            {
                string searchText = searchBox.Text.ToLower();
                var buttons = SettingsWrapPanel.Children.OfType<Button>().ToList();
                foreach (var btn in buttons)
                {
                    btn.Visibility = btn.Content.ToString().ToLower().Contains(searchText) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }
}
