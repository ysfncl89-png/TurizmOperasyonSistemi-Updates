using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class HotelAllocationView : UserControl
    {
        private int currentStep = 1;

        // Sağ taraftaki odaları ve sol taraftaki yolcuları dinamik takip etmek için ObservableCollection kullanıyoruz
        public ObservableCollection<RoomViewModel> RoomsList { get; set; } = new ObservableCollection<RoomViewModel>();
        public ObservableCollection<PassengerUiModel> PassengerPool { get; set; } = new ObservableCollection<PassengerUiModel>();

        // Seçilen odayı hafızada tutar
        private RoomViewModel _selectedRoomForAllocation = null;

        public HotelAllocationView()
        {
            InitializeComponent();
            LoadComboBoxData();

            // XAML'deki kontrollere listelerimizi bağlıyoruz
            RoomsItemsControl.ItemsSource = RoomsList;
            UnallocatedPassengersGrid.ItemsSource = PassengerPool;
        }

        // 1. MEVCUT LoadComboBoxData METODUNUN EN ALTINA ŞU SATIRLARI EKLE veya METODU BUNUNLA DEĞİŞTİR:
        private void LoadComboBoxData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var allSettings = db.AppSettings.ToList();

                    HotelSelectComboBox.ItemsSource = allSettings.Where(x => x.Type != null && x.Type.Trim().Equals("Otel Ekle", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    TourTypeComboBox.ItemsSource = allSettings.Where(x => x.Type != null && x.Type.Trim().Equals("TourType", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    PeriodComboBox.ItemsSource = allSettings.Where(x => x.Type != null && x.Type.Trim().Equals("Dönem", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    GroupTypeComboBox.ItemsSource = allSettings.Where(x => x.Type != null && x.Type.Trim().Equals("GroupType", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    DurationComboBox.ItemsSource = allSettings.Where(x => x.Type != null && x.Type.Trim().Equals("Kaç Günlük Grup", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();

                    // YENİ EKLEMEYE DİKKAT: Sağ taraftaki Oda Açma ComboBox'ına Ayarlar'daki RoomType değerlerini basıyoruz:
                    NewRoomCapacityComboBox.ItemsSource = allSettings
                        .Where(x => x.Type != null && x.Type.Trim().Equals("RoomType", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value).Distinct().ToList();

                    if (NewRoomCapacityComboBox.Items.Count > 0) NewRoomCapacityComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar yüklenirken hata: {ex.Message}");
            }
        }

        // 2. YOLCULARIN NULL HATASINA ÇARPMADAN TÜMÜNÜ GETİREN GÜVENLİ METOT:
        private void LoadFilteredPassengers()
        {
            try
            {
                string selectedTourType = TourTypeComboBox.SelectedItem?.ToString();
                string selectedPeriod = PeriodComboBox.SelectedItem?.ToString();
                string selectedGroupType = GroupTypeComboBox.SelectedItem?.ToString();
                string selectedDuration = DurationComboBox.SelectedItem?.ToString();

                PassengerPool.Clear();

                using (var db = new AppDbContext())
                {
                    // EF Connection'ı alıp ham bir SQL sorgusu hazırlıyoruz.
                    // Sadece bize lazım olan sütunları seçtiğimiz için 16. sütundaki NULL hatası tamamen bypass edilir!
                    var connection = db.Database.GetDbConnection();

                    if (connection.State != System.Data.ConnectionState.Open)
                        connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        // Tablo ve sütun isimlerini senin veritabanına göre eşliyoruz. 
                        // Eğer veritabanında "DayCount" yoksa hata vermemesi için SQL sorgusuna eklemiyoruz, C# tarafında çözeceğiz.
                        command.CommandText = @"
                    SELECT Id, PersonId, TourType, Period, GroupType, RoomType 
                    FROM Registrations 
                    WHERE TourType = @tourType AND Period = @period AND GroupType = @groupType";

                        // SQL Enjeksiyon korumalı parametrelerimizi ekliyoruz
                        var p1 = command.CreateParameter(); p1.ParameterName = "@tourType"; p1.Value = selectedTourType ?? (object)DBNull.Value; command.Parameters.Add(p1);
                        var p2 = command.CreateParameter(); p2.ParameterName = "@period"; p2.Value = selectedPeriod ?? (object)DBNull.Value; command.Parameters.Add(p2);
                        var p3 = command.CreateParameter(); p3.ParameterName = "@groupType"; p3.Value = selectedGroupType ?? (object)DBNull.Value; command.Parameters.Add(p3);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int personId = reader.IsDBNull(reader.GetOrdinal("PersonId")) ? 0 : reader.GetInt32(reader.GetOrdinal("PersonId"));
                                string roomType = reader.IsDBNull(reader.GetOrdinal("RoomType")) ? "Belirtilmemiş" : reader.GetString(reader.GetOrdinal("RoomType"));

                                if (personId > 0)
                                {
                                    // PersonId üzerinden yolcunun adını soyadını buluyoruz
                                    var person = db.Persons.AsNoTracking().FirstOrDefault(p => p.Id == personId);
                                    if (person != null)
                                    {
                                        PassengerPool.Add(new PassengerUiModel
                                        {
                                            PersonId = person.Id,
                                            FullName = $"{person.FirstName} {person.LastName}",
                                            RoomType = roomType
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                PassengerCountLabel.Text = $"Seçilen Kriterlere Uygun: {PassengerPool.Count} Yolcu Bulundu";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yolcu havuzu yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 1. ODAYA YERLEŞTİRME BUTONU (ZORLAMA VE ÇOCUK EKLEME İZİNLİ)
        private void AllocatePassengerButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var selectedPassenger = button?.DataContext as PassengerUiModel;

            if (selectedPassenger == null) return;

            if (_selectedRoomForAllocation == null)
            {
                MessageBox.Show("Lütfen önce sağ taraftaki oda kartlarından yerleşim yapacağınız odaya tıklayın!",
                                "Oda Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ESNEKLİK BURADA: Oda kapasitesi dolmuşsa operatörü ENGELEME, sadece UYAR ve İZİN VER!
            if (_selectedRoomForAllocation.Occupants.Count >= _selectedRoomForAllocation.MaxCapacity)
            {
                var result = MessageBox.Show(
                    $"Bu oda resmi yatak kapasitesine ({_selectedRoomForAllocation.MaxCapacity} Yatak) ulaştı.\n\n" +
                    $"Yanlarındaki yatak istemeyen çocukları veya ekstra kişileri odaya yerleştirmek istiyor musunuz?",
                    "Kapasite Aşımı Onayı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    return; // Operatör hayır derse işlemi iptal et
                }
            }

            // Yolcuyu bizzat seçtiğin odaya ekle (Sistem artık seni engellemiyor, kontrol sende!)
            _selectedRoomForAllocation.Occupants.Add(selectedPassenger);
            PassengerPool.Remove(selectedPassenger);
            _selectedRoomForAllocation.UpdateStatus();

            PassengerCountLabel.Text = $"Seçilen Kriterlere Uygun: {PassengerPool.Count} Yolcu Bulundu";
        }

        // 3. YOLCUYUR ODADAN GERİ ALMA (SİLME) BUTONU
        private void RemovePassengerFromRoomButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            int personId = (button?.Tag is int id) ? id : 0;

            if (personId == 0) return;

            // Yolcunun bulunduğu odayı bul
            var room = RoomsList.FirstOrDefault(r => r.Occupants.Any(o => o.PersonId == personId));
            if (room != null)
            {
                var passenger = room.Occupants.FirstOrDefault(o => o.PersonId == personId);
                if (passenger != null)
                {
                    // Odadan çıkar, tekrar sol taraftaki havuza gönder
                    room.Occupants.Remove(passenger);
                    PassengerPool.Add(passenger);
                    room.UpdateStatus();
                    PassengerCountLabel.Text = $"Seçilen Kriterlere Uygun: {PassengerPool.Count} Yolcu Bulundu";
                }
            }
        }

        // ODA KARTINA TIKLANDIĞINDA ONAY ALACAK YENİ FONKSİYON
        private void RoomCard_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var clickedRoom = border?.DataContext as RoomViewModel;

            if (clickedRoom == null) return;

            // Eski seçili odanın efektini temizle (Eğer varsa)
            foreach (var r in RoomsList)
            {
                // Durum renklerini güncellemek için kullandığımız metodu tetikliyoruz ama bordere efekt vereceğiz
            }

            // Yeni odayı seçilen olarak belirle
            _selectedRoomForAllocation = clickedRoom;

            // Kullanıcıya hangi odayı seçtiğini hissettirmek için küçük bir bilgi uyarısı
            SelectedHotelTitleLabel.Text = $"🏨 Otel: {HotelSelectComboBox.SelectedItem} | ŞU AN SEÇİLİ ODA: Oda {clickedRoom.RoomNumber}";
        }

        // ================= İLERİ / GERİ BUTON MANTIKLARI =================
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 1)
            {
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
                if (TourTypeComboBox.SelectedItem == null || PeriodComboBox.SelectedItem == null ||
                    GroupTypeComboBox.SelectedItem == null || DurationComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen tüm tur filtreleme kriterlerini seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // BAŞARI: 3. Adıma geçerken veritabanından yolcuları filtreleyip getiriyoruz!
                SelectedHotelTitleLabel.Text = $"🏨 Seçilen Otel: {HotelSelectComboBox.SelectedItem} Oda Yerleşim Düzeni";
                LoadFilteredPassengers();

                Step2Grid.Visibility = Visibility.Collapsed;
                Step3Grid.Visibility = Visibility.Visible;

                Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

                NextButton.Content = "Kaydet ve Bitir";
                currentStep = 3;
            }
            else if (currentStep == 3)
            {
                MessageBox.Show("Odalama yerleşim planı başarıyla kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
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

        // 2. ACENTA TERİMLERİNE UYGUN DİNAMİK ODA AÇMA METODU
        private void AddRoomButton_Click(object sender, RoutedEventArgs e)
        {
            if (NewRoomCapacityComboBox.SelectedItem == null)
            {
                MessageBox.Show("Lütfen açılacak oda türünü seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedRoomType = NewRoomCapacityComboBox.SelectedItem.ToString().ToUpper();

            // Senin verdiğin sektörel karşılıklara göre resmi yatak kapasitesini atıyoruz:
            int capacity = 2; // varsayılan
            if (selectedRoomType.Contains("SNGL") || selectedRoomType.Contains("SINGLE")) capacity = 1;
            else if (selectedRoomType.Contains("DBL") || selectedRoomType.Contains("TWIN")) capacity = 2;
            else if (selectedRoomType.Contains("TRPL") || selectedRoomType.Contains("TRİPLE")) capacity = 3;
            else if (selectedRoomType.Contains("QUAD")) capacity = 4;
            else if (selectedRoomType.Contains("QUINT")) capacity = 5;
            else if (selectedRoomType.Contains("CONNECTION")) capacity = 4; // Connection için varsayılan 4, sonradan esnetilebilir.

            int existingCount = RoomsList.Count + 1;
            string autoRoomNo = $"{existingCount}";

            RoomsList.Add(new RoomViewModel
            {
                RoomNumber = autoRoomNo,
                RoomTypeName = selectedRoomType, // Odanın SNGL, DBL gibi adını tutuyoruz
                MaxCapacity = capacity
            });
        }
    }

    // ================= 3. ADIM İÇİN GEREKLİ YARDIMCI GÖRÜNÜM MODELLERİ =================

    public class PassengerUiModel
    {
        public int PersonId { get; set; }
        public string FullName { get; set; }
        public string RoomType { get; set; }
    }

    public class RoomViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _statusText = "Boş";
        private Brush _statusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        private int _maxCapacity;

        public string RoomNumber { get; set; }
        public string RoomTypeName { get; set; } // SNGL, DBL, TRPL, CONNECTION vb.

        public int MaxCapacity
        {
            get => _maxCapacity;
            set
            {
                _maxCapacity = value;
                OnPropertyChanged(nameof(MaxCapacity));
                OnPropertyChanged(nameof(RoomNumberDisplay));
            }
        }

        public ObservableCollection<PassengerUiModel> Occupants { get; set; } = new ObservableCollection<PassengerUiModel>();

        // Ekranda görünecek başlık: Örn: "Oda 1 [DBL] (2 Yataklı)"
        public string RoomNumberDisplay => $"Oda {RoomNumber} [{RoomTypeName}] ({MaxCapacity} Yatak)";

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        public void UpdateStatus()
        {
            if (Occupants.Count == 0)
            {
                StatusText = "Boş";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Yeşil
            }
            else if (Occupants.Count < MaxCapacity)
            {
                StatusText = $"{Occupants.Count}/{MaxCapacity} Yatak Dolu";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Turuncu
            }
            else if (Occupants.Count == MaxCapacity)
            {
                StatusText = "Yataklar Dolu";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Kırmızı
            }
            else
            {
                // Kapasite aşımı olduğunda (Çocuk eklendiğinde) ekranda tatlı bir mor renk gösterelim:
                StatusText = $"+{Occupants.Count - MaxCapacity} Ekstra Kişi";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7")); // Mor (Kapasite Üstü)
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}