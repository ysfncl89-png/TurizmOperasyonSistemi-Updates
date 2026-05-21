using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data; // Senin AppDbContext'in bulunduğu klasör

namespace TurizmOperasyonSistemi.Views
{
    public partial class HotelAllocationView : UserControl
    {
        private int currentStep = 1;

        public HotelAllocationView()
        {
            InitializeComponent();

            // Sayfa açılır açılmaz senin AppSettings tablundan verileri yüklüyoruz
            LoadComboBoxData();
        }

        private void LoadComboBoxData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Tüm ayarlar listesini veritabanından çekip hafızaya alıyoruz
                    var allSettings = db.AppSettings.ToList();

                    // 1. ADIM: Otelleri Yükleme (Veritabanındaki tam adı: "Otel Ekle")
                    HotelSelectComboBox.ItemsSource = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("Otel Ekle", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value)
                        .Distinct()
                        .ToList();

                    // 2. ADIM: Tur Türleri (Veritabanındaki tam adı: "TourType")
                    TourTypeComboBox.ItemsSource = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("TourType", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value)
                        .Distinct()
                        .ToList();

                    // 3. ADIM: Dönemler (Veritabanındaki tam adı: "Dönem")
                    PeriodComboBox.ItemsSource = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("Dönem", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value)
                        .Distinct()
                        .ToList();

                    // 4. ADIM: Grup Türleri (Veritabanındaki tam adı: "GroupType")
                    GroupTypeComboBox.ItemsSource = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("GroupType", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value)
                        .Distinct()
                        .ToList();

                    // 5. ADIM: Süreler / Gün Sayısı (Veritabanındaki tam adı: "Kaç Günlük Grup")
                    DurationComboBox.ItemsSource = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("Kaç Günlük Grup", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value)
                        .Distinct()
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar veritabanından yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Model içindeki sütun isimleri ne olursa olsun (Type, SettingType, Tip, Key vb.) 
        // dinamik olarak bulup hatayı engelleyen yardımcı fonksiyon:
        private string GetModelPropertyValue(object obj, string targetType)
        {
            if (obj == null) return "";

            var properties = obj.GetType().GetProperties();

            // Eğer "Type" (Tip/Key) aranıyorsa
            if (targetType == "Type")
            {
                var prop = properties.FirstOrDefault(p => p.Name.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("SettingType", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("Tip", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("Key", StringComparison.OrdinalIgnoreCase));
                return prop?.GetValue(obj)?.ToString() ?? "";
            }
            // Eğer "Value" (Değer) aranıyorsa
            else
            {
                var prop = properties.FirstOrDefault(p => p.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("SettingValue", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("Deger", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("Content", StringComparison.OrdinalIgnoreCase));
                return prop?.GetValue(obj)?.ToString() ?? "";
            }
        }

        // ================= ADIM GEÇİŞLERİ VE KORUMA MANTIKLARI =================
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 1)
            {
                // KORUMA: Otel seçilmeden geçişi engelle
                if (HotelSelectComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen önce odalama yapacağınız oteli seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Step1Grid.Visibility = Visibility.Collapsed;
                Step2Grid.Visibility = Visibility.Visible;

                Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

                BackButton.Visibility = Visibility.Visible;
                currentStep = 2;
            }
            else if (currentStep == 2)
            {
                // KORUMA: 4 kriterin de seçildiğinden emin ol
                if (TourTypeComboBox.SelectedItem == null || PeriodComboBox.SelectedItem == null ||
                    GroupTypeComboBox.SelectedItem == null || DurationComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen tüm tur filtreleme kriterlerini seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Step2Grid.Visibility = Visibility.Collapsed;
                Step3Grid.Visibility = Visibility.Visible;

                Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

                NextButton.Content = "Kaydet ve Bitir";
                currentStep = 3;

                // TODO: 3. Adıma geçildiğinde seçilen filtrelere göre yolcuları listeleyecek fonksiyon buraya gelecek.
            }
            else if (currentStep == 3)
            {
                MessageBox.Show("Odalama işlemi başarıyla kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 3)
            {
                Step3Grid.Visibility = Visibility.Collapsed;
                Step2Grid.Visibility = Visibility.Visible;

                Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

                NextButton.Content = "İleri ▶";
                currentStep = 2;
            }
            else if (currentStep == 2)
            {
                Step2Grid.Visibility = Visibility.Collapsed;
                Step1Grid.Visibility = Visibility.Visible;

                Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

                BackButton.Visibility = Visibility.Collapsed;
                currentStep = 1;
            }
        }
    }
}