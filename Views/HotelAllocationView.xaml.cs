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

        // Bağlanacak ilk odayı tutar
        private RoomViewModel _firstRoomToConnect = null;

        private int _connectionGroupCounter = 1; // Her gruba benzersiz numara vermek için

        // Muhteşem Neon Renk Havuzumuz (Koyu temaya cuk oturan cinsten)
        private readonly string[] NeonColors = new string[] {
            "#A855F7", // Neon Mor
            "#00E5FF", // Neon Turkuaz
            "#39FF14", // Neon Yeşil
            "#FF007F", // Neon Pembe
            "#FFD700"  // Neon Altın Sarısı
        };

        public HotelAllocationView()
        {
            InitializeComponent();
            LoadComboBoxData();

            // XAML'deki kontrollere listelerimizi bağlıyoruz
            RoomsItemsControl.ItemsSource = RoomsList;
            UnallocatedPassengersGrid.ItemsSource = PassengerPool;
        }

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

        private void LoadFilteredPassengers()
        {
            try
            {
                string selectedTourType = TourTypeComboBox.SelectedItem?.ToString();
                string selectedPeriod = PeriodComboBox.SelectedItem?.ToString();
                string selectedGroupType = GroupTypeComboBox.SelectedItem?.ToString();
                string selectedDuration = DurationComboBox.SelectedItem?.ToString();

                PassengerPool.Clear();
                var occupiedPersonIds = GetOccupiedPersonIds();

                using (var db = new AppDbContext())
                {
                    var connection = db.Database.GetDbConnection();

                    if (connection.State != System.Data.ConnectionState.Open)
                        connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                    SELECT Id, PersonId, TourType, Period, GroupType, RoomType 
                    FROM Registrations 
                    WHERE TourType = @tourType AND Period = @period AND GroupType = @groupType";

                        var p1 = command.CreateParameter();
                        p1.ParameterName = "@tourType";
                        p1.Value = selectedTourType ?? (object)DBNull.Value;
                        command.Parameters.Add(p1);

                        var p2 = command.CreateParameter();
                        p2.ParameterName = "@period";
                        p2.Value = selectedPeriod ?? (object)DBNull.Value;
                        command.Parameters.Add(p2);

                        var p3 = command.CreateParameter();
                        p3.ParameterName = "@groupType";
                        p3.Value = selectedGroupType ?? (object)DBNull.Value;
                        command.Parameters.Add(p3);
                        
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int personId = reader.IsDBNull(reader.GetOrdinal("PersonId")) ? 0 : reader.GetInt32(reader.GetOrdinal("PersonId"));
                                string roomType = reader.IsDBNull(reader.GetOrdinal("RoomType")) ? "Belirtilmemiş" : reader.GetString(reader.GetOrdinal("RoomType"));

                                if (personId > 0)
                                {
                                    var person = db.Persons.AsNoTracking().FirstOrDefault(p => p.Id == personId);

                                    if (person != null && !occupiedPersonIds.Contains(person.Id))
                                    {
                                        AddPassengerToPoolIfMissing(new PassengerUiModel
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

                RefreshPassengerCountLabel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yolcu havuzu yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                    return;
                }
            }

            _selectedRoomForAllocation.Occupants.Add(selectedPassenger);
            PassengerPool.Remove(selectedPassenger);
            _selectedRoomForAllocation.UpdateStatus();

            RefreshPassengerCountLabel();
        }

        private void RemovePassengerFromRoomButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            int personId = (button?.Tag is int id) ? id : 0;

            if (personId == 0) return;

            var room = RoomsList.FirstOrDefault(r => r.Occupants.Any(o => o.PersonId == personId));

            if (room != null)
            {
                var passenger = room.Occupants.FirstOrDefault(o => o.PersonId == personId);

                if (passenger != null)
                {
                    room.Occupants.Remove(passenger);
                    AddPassengerToPoolIfMissing(passenger);
                    room.UpdateStatus();

                    RefreshPassengerCountLabel();
                }
            }
        }

        private void RoomCard_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var clickedRoom = border?.DataContext as RoomViewModel;

            if (clickedRoom == null) return;

            _selectedRoomForAllocation = clickedRoom;

            SelectedHotelTitleLabel.Text = $"🏨 Otel: {HotelSelectComboBox.SelectedItem} | ŞU AN SEÇİLİ ODA: Oda {clickedRoom.RoomNumber}";
        }

        private HashSet<int> GetOccupiedPersonIds()
        {
            return RoomsList
                .SelectMany(r => r.Occupants)
                .Select(o => o.PersonId)
                .Where(id => id > 0)
                .ToHashSet();
        }

        private void AddPassengerToPoolIfMissing(PassengerUiModel passenger)
        {
            if (passenger == null)
            {
                return;
            }

            bool exists = PassengerPool.Any(x => x.PersonId == passenger.PersonId);
            if (!exists)
            {
                PassengerPool.Add(passenger);
            }
        }

        private void RefreshPassengerCountLabel()
        {
            PassengerCountLabel.Text = $"Seçilen Kriterlere Uygun: {PassengerPool.Count} Yolcu Bulundu";
        }

        // İKİ ODAYI CONNECTION YAP
        private void ConnectRoomsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRoomForAllocation == null)
            {
                MessageBox.Show("Lütfen önce bağlantı kurmak istediğiniz İLK odaya tıklayın.", "Seçim Eksik");
                return;
            }

            if (_firstRoomToConnect == null)
            {
                _firstRoomToConnect = _selectedRoomForAllocation;
                _firstRoomToConnect.ConnectionBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7"));
                _firstRoomToConnect.ConnectionGroupText = "🔗 Bağlantı Bekliyor...";
                MessageBox.Show($"Oda {_firstRoomToConnect.RoomNumber} seçildi. Şimdi bağlamak istediğiniz İKİNCİ odaya tıklayıp tekrar butona basın.", "İkinci Oda");
            }
            else
            {
                if (_firstRoomToConnect.RoomNumber == _selectedRoomForAllocation.RoomNumber)
                {
                    MessageBox.Show("Bir odayı kendisiyle bağlayamazsınız!", "Hata");
                    return;
                }

                RoomViewModel secondRoom = _selectedRoomForAllocation;

                // 1. Bu grup için havuzdan sıradaki neon rengi seçiyoruz
                string selectedNeonHex = NeonColors[(_connectionGroupCounter - 1) % NeonColors.Length];
                var neonBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedNeonHex));

                string gId = $"Group_{_connectionGroupCounter}";
                string groupTitle = $"🔗 CONNECTION GRUBU #{_connectionGroupCounter} (Oda {_firstRoomToConnect.RoomNumber} + {secondRoom.RoomNumber})";

                // 2. Odaları mühürle ve renklerini ata
                _firstRoomToConnect.IsConnection = true;
                _firstRoomToConnect.ConnectionGroupId = gId;
                _firstRoomToConnect.ConnectionGroupText = groupTitle;
                _firstRoomToConnect.ConnectionBorderBrush = neonBrush;
                _firstRoomToConnect.RoomMargin = new Thickness(5, 5, 0, 5); // Aradaki boşluğu daralt

                secondRoom.IsConnection = true;
                secondRoom.ConnectionGroupId = gId;
                secondRoom.ConnectionGroupText = groupTitle;
                secondRoom.ConnectionBorderBrush = neonBrush;
                secondRoom.RoomMargin = new Thickness(0, 5, 5, 5); // İlk odaya yapışık dursun

                // 3. EKRANDAKİ LİSTEYİ YENİDEN SIRALA
                var sortedRooms = RoomsList.OrderBy(r => r.IsConnection ? r.ConnectionGroupId : "ZZZ_" + r.RoomNumber).ToList();

                RoomsList.Clear();
                foreach (var r in sortedRooms)
                {
                    RoomsList.Add(r);
                }

                MessageBox.Show($"Mükemmel! Oda {_firstRoomToConnect.RoomNumber} ve Oda {secondRoom.RoomNumber} yan yana getirildi ve mühürlendi!", "Bağlantı Tamam");

                _connectionGroupCounter++;
                _firstRoomToConnect = null;
            }
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
                if (TourTypeComboBox.SelectedItem == null ||
                    PeriodComboBox.SelectedItem == null ||
                    GroupTypeComboBox.SelectedItem == null ||
                    DurationComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen tüm tur filtreleme kriterlerini seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

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

        // ODA EKLE
        private void AddRoomButton_Click(object sender, RoutedEventArgs e)
        {
            if (NewRoomCapacityComboBox.SelectedItem == null)
            {
                MessageBox.Show("Lütfen açılacak oda türünü seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedRoomType = NewRoomCapacityComboBox.SelectedItem.ToString().ToUpper();

            int capacity = 2;

            if (selectedRoomType.Contains("SNGL") || selectedRoomType.Contains("SINGLE")) capacity = 1;
            else if (selectedRoomType.Contains("DBL") || selectedRoomType.Contains("TWIN")) capacity = 2;
            else if (selectedRoomType.Contains("TRPL") || selectedRoomType.Contains("TRİPLE")) capacity = 3;
            else if (selectedRoomType.Contains("QUAD")) capacity = 4;
            else if (selectedRoomType.Contains("QUINT")) capacity = 5;

            int existingCount = RoomsList.Count + 1;
            string autoRoomNo = $"{existingCount}";

            RoomsList.Add(new RoomViewModel
            {
                RoomNumber = autoRoomNo,
                RoomTypeName = selectedRoomType,
                MaxCapacity = capacity
            });
        }
    
    // 1. ODALARI KOPARMA (CONNECTION İPTALİ) METODU
private void BreakConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var currentRoom = button?.Tag as RoomViewModel;

            if (currentRoom == null || !currentRoom.IsConnection) return;

            string targetGroupId = currentRoom.ConnectionGroupId;

            // Aynı gruptaki tüm odaları bulup bağlarını koparıyoruz
            var connectedRooms = RoomsList.Where(r => r.ConnectionGroupId == targetGroupId).ToList();

            foreach (var room in connectedRooms)
            {
                room.IsConnection = false;
                room.ConnectionGroupId = "";
                room.ConnectionGroupText = "";
                // Çerçeve rengini standart koyu griye çekiyoruz
                room.ConnectionBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                room.RoomMargin = new Thickness(5); // Marjları normal haline getir
            }

            // Listeyi bağlantı durumlarına göre tekrar alfabetik/normal sıraya diziyoruz
            var sortedRooms = RoomsList.OrderBy(r => r.IsConnection ? r.ConnectionGroupId : "ZZZ_" + r.RoomNumber).ToList();
            RoomsList.Clear();
            foreach (var r in sortedRooms) RoomsList.Add(r);

            MessageBox.Show("Odaların arasındaki bağlantı başarıyla koparıldı, odalar bağımsız hale geldi.", "Bağlantı İptal");
        }

        // 2. ODA KAPATMA (SİLME VE YOLCULARI HAVUZA GERİ ATMA) METODU
        private void CloseRoomButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var currentRoom = button?.Tag as RoomViewModel;

            if (currentRoom == null) return;

            // Eğer oda bir connection grubuna dahilse önce uyaralım veya bağını koparalım
            if (currentRoom.IsConnection)
            {
                var connResult = MessageBox.Show("Bu oda bir Connection grubuna bağlı. Kapatırsanız önce diğer odalarla bağı koparılacak. Onaylıyor musunuz?",
                                                 "Bağlantılı Oda Kapatma", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (connResult == MessageBoxResult.No) return;

                // Önce yukarıdaki metodu tetikleyip bağları çözelim
                BreakConnectionButton_Click(sender, e);
            }

            // İÇİNDEKİ KİŞİLERİ SOLDAKİ HAVUZA GERİ GÖNDERME SİHRİ
            if (currentRoom.Occupants.Count > 0)
            {
                // Odadaki tüm yolcuları tek tek sol listeye (PassengerPool) iade ediyoruz
                foreach (var passenger in currentRoom.Occupants.ToList())
                {
                    AddPassengerToPoolIfMissing(passenger);
                }
                currentRoom.Occupants.Clear();
            }

            // Odayı sağ taraftaki listeden tamamen siliyoruz
            RoomsList.Remove(currentRoom);

            // Sol taraftaki yolcu sayacı etiketini güncelle
            RefreshPassengerCountLabel();
        }
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

        private Brush _connectionBorderBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));

        private string _connectionGroupText = "";
        private Thickness _roomMargin = new Thickness(5);

        public string RoomNumber { get; set; }
        public string RoomTypeName { get; set; }

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

        public ObservableCollection<PassengerUiModel> Occupants { get; set; } =
            new ObservableCollection<PassengerUiModel>();

        public string RoomNumberDisplay =>
            $"Oda {RoomNumber} [{RoomTypeName}] ({MaxCapacity} Yatak)";

        // NEON BAĞLANTI ÖZELLİKLERİ
        public bool IsConnection { get; set; } = false;

        public string ConnectionGroupId { get; set; } = "";

        public string ConnectionGroupText
        {
            get => _connectionGroupText;
            set
            {
                _connectionGroupText = value;
                OnPropertyChanged(nameof(ConnectionGroupText));
            }
        }

        public Brush ConnectionBorderBrush
        {
            get => _connectionBorderBrush;
            set
            {
                _connectionBorderBrush = value;
                OnPropertyChanged(nameof(ConnectionBorderBrush));
            }
        }

        // Odalar grup içinde yan yana gelirken dış çerçevenin marjını sıfırlamak için
        public Thickness RoomMargin
        {
            get => _roomMargin;
            set
            {
                _roomMargin = value;
                OnPropertyChanged(nameof(RoomMargin));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public void UpdateStatus()
        {
            if (Occupants.Count == 0)
            {
                StatusText = "Boş";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else if (Occupants.Count < MaxCapacity)
            {
                StatusText = $"{Occupants.Count}/{MaxCapacity} Dolu";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
            else if (Occupants.Count == MaxCapacity)
            {
                StatusText = "Dolu";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }
            else
            {
                StatusText = $"+{Occupants.Count - MaxCapacity} Çocuk/Ekstra";
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7"));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
