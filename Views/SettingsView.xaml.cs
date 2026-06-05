using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

            // SAYFA AÇILDIĞINDA: SQLite'taki verileri okuyup kartlardaki listelere basıyoruz!
            LoadExistingSettingsIntoUi();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = (SearchTextBox.Text ?? string.Empty).Trim().ToLower();

            foreach (var child in SettingsWrapPanel.Children)
            {
                if (child is Button b)
                {
                    string text = (b.Content ?? string.Empty).ToString().ToLower();
                    b.Visibility = string.IsNullOrEmpty(q) || text.Contains(q) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // VERİTABANINDAN VERİLERİ ÇEKİP XAML'DEKİ LİSTELERE DOLDURAN SİHİRLİ METOT
        private void LoadExistingSettingsIntoUi()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Ensure Uyruk settings contain only the two new options (TC, Yabancı).
                    // If legacy country list exists, replace it with the minimal two-choice set.
                    var uyrukValues = db.AppSettings.Where(x => x.Type == "Uyruk").Select(x => x.Value).ToList();
                    // Only seed default Uyruk values when none exist. Do not replace user-added nationalities.
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

                    var allSettings = db.AppSettings.ToList();

                    // 1. Zaten Gelenler (Tur, Grup, Oda, Dönem, Gün, Otel)
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


                    // 2. YENİ EKLENENLER: Vize, Kargo, Bilet, Para Birimi ve Şehir Kartları
                    // Not: Eğer XAML tarafında bu kartların içindeki ListBox/ItemsControl isimleri farklıysa derleyici hata verir.
                    // Tahmini standart isimlendirmeleri yazdım, hata alırsan XAML'deki x:Name değerleriyle değiştirebilirsin.

                    if (FindName("VisaListControl") is ItemsControl visaList)
                        visaList.ItemsSource = allSettings.Where(x => x.Type == "Vize Talebi").Select(x => x.Value).ToList();

                    if (FindName("TicketListControl") is ItemsControl ticketList)
                        ticketList.ItemsSource = allSettings.Where(x => x.Type == "Bilet Talebi").Select(x => x.Value).ToList();

                    if (FindName("CargoListControl") is ItemsControl cargoList)
                        cargoList.ItemsSource = allSettings.Where(x => x.Type == "Kargo Talebi").Select(x => x.Value).ToList();

                    if (FindName("CurrencyListControl") is ItemsControl currencyList)
                        currencyList.ItemsSource = allSettings.Where(x => x.Type == "Para Birimi").Select(x => x.Value).ToList();

                    if (FindName("CityListControl") is ItemsControl cityList)
                        cityList.ItemsSource = allSettings.Where(x => x.Type == "Katılım Yapılacak Şehir").Select(x => x.Value).ToList();

                    if (FindName("TransferListControl") is ItemsControl transferList)
                        transferList.ItemsSource = allSettings.Where(x => x.Type == "Aktarma Var mı ?").Select(x => x.Value).ToList();

                    if (FindName("TransferCityListControl") is ItemsControl transferCityList)
                        transferCityList.ItemsSource = allSettings.Where(x => x.Type == "Aktarma Şehri").Select(x => x.Value).ToList();

                    if (FindName("NationalityListControl") is ItemsControl nationalityList)
                        nationalityList.ItemsSource = allSettings.Where(x => x.Type == "Uyruk").Select(x => x.Value).ToList();

                    if (FindName("VisaStatusListControl") is ItemsControl visaStatusList)
                        visaStatusList.ItemsSource = allSettings.Where(x => x.Type == "Vize Durumu").Select(x => x.Value).ToList();

                    if (FindName("PassportArrivedListControl") is ItemsControl passportList)
                        passportList.ItemsSource = allSettings.Where(x => x.Type == "Pasaportu Geldi mi ?").Select(x => x.Value).ToList();

                    if (FindName("TicketStatusListControl") is ItemsControl ticketStatusList)
                        ticketStatusList.ItemsSource = allSettings.Where(x => x.Type == "Bilet Durumu").Select(x => x.Value).ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ayarlar listelenirken hata: {ex.Message}");
            }
        }

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;

            string settingName = button.Content.ToString();
            string settingType = "";

            // NOKTA ATIŞI DÜZELTME: ComboBox'lar ve veritabanı şemasıyla tam eşleşen kelimeleri atıyoruz
            switch (settingName)
            {
                case "Tur Türü":
                case "TourType":
                    settingType = "TourType";
                    break;

                case "Grup Türü":
                case "GroupType":
                    settingType = "GroupType";
                    break;

                case "Oda Türü":
                case "RoomType":
                    settingType = "RoomType";
                    break;

                case "Vize Talebi":
                    settingType = "Vize Talebi";
                    break;

                case "Bilet Talebi":
                    settingType = "Bilet Talebi";
                    break;

                case "Kargo Talebi":
                    settingType = "Kargo Talebi";
                    break;

                case "Para Birimi":
                    settingType = "Para Birimi";
                    break;

                case "Katılım Yapılacak Şehir":
                    settingType = "Katılım Yapılacak Şehir";
                    break;

                case "Aktarma Var mı ?":
                    settingType = "Aktarma Var mı ?";
                    break;

                case "Aktarma Şehri":
                    settingType = "Aktarma Şehri";
                    break;

                case "Dönem":
                    settingType = "Dönem";
                    break;

                case "Kaç Günlük Grup":
                    settingType = "Kaç Günlük Grup";
                    break;

                default:
                    // Eğer butonda direkt "Otel Ekle" yazıyorsa onu da yakala
                    if (settingName.Contains("Otel")) settingType = "Otel Ekle";
                    else settingType = settingName;
                    break;
            }

            // Uyruk için özel isim eşlemesi
            if (settingType == "Uyruk")
            {
                settingType = "Uyruk";
            }

            if (settingType == "Dönem")
            {
                PeriodDialog dialog = new PeriodDialog();
                dialog.ShowDialog();
                LoadExistingSettingsIntoUi(); // Dialog kapandığında listeyi hemen yenile!
            }
            else
            {
                SettingDialog dialog = new SettingDialog(settingType);
                dialog.ShowDialog();
                LoadExistingSettingsIntoUi(); // Dialog kapandığında listeyi hemen yenile!
            }
        }
    }
}