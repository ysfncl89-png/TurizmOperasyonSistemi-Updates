using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media.Effects;
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
        public ObservableCollection<SavedAllocationRow> SavedAllocations { get; set; } = new ObservableCollection<SavedAllocationRow>();
        private List<SavedAllocationRow> _allSavedAllocations = new List<SavedAllocationRow>();
        private bool _isPostHotelSelectionGuidanceActive = false;

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
            EnsureRoomAllocationColumns();
            LoadComboBoxData();

            // XAML'deki kontrollere listelerimizi bağlıyoruz
            RoomsItemsControl.ItemsSource = RoomsList;
            UnallocatedPassengersGrid.ItemsSource = PassengerPool;
            SavedAllocationsGrid.ItemsSource = SavedAllocations;

            LoadSavedAllocations();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (HotelSelectComboBox.SelectedItem == null)
                {
                    ShowHotelSelectionGuidance();
                }
            }), DispatcherPriority.Loaded);
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

            SelectRoom(clickedRoom);

            SelectedHotelTitleLabel.Text = $"🏨 Otel: {HotelSelectComboBox.SelectedItem} | ŞU AN SEÇİLİ ODA: Oda {clickedRoom.RoomNumber}";
        }

        private void ShowHotelSelectionGuidance()
        {
            SavedAllocationsContainer.Effect = new BlurEffect { Radius = 10 };
            SavedAllocationsContainer.IsEnabled = false;
            HotelSelectionGuideOverlay.Visibility = Visibility.Visible;
            HotelSelectionGuideTitleText.Text = "Önce yukarıdan otel seçin";
            HotelSelectionGuideDescriptionText.Text = "Kayıt listesinden değil, üstteki otel kutusundan seçim yapmalısınız.";
            HotelSelectionGuideOkButton.Visibility = Visibility.Collapsed;
            HotelSelectComboBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            _isPostHotelSelectionGuidanceActive = false;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                HotelSelectComboBox.Focus();
                HotelSelectComboBox.IsDropDownOpen = true;
            }), DispatcherPriority.Background);
        }

        private void ShowPostHotelSelectionGuidance()
        {
            SavedAllocationsContainer.Effect = new BlurEffect { Radius = 10 };
            SavedAllocationsContainer.IsEnabled = false;
            HotelSelectionGuideOverlay.Visibility = Visibility.Visible;
            HotelSelectionGuideTitleText.Text = "Şimdi kayıt yolunu seçin";
            HotelSelectionGuideDescriptionText.Text = "Odalama kaydını yapacağınız kayıt listede varsa listeden seçin. Listede yoksa sadece İleri butonuna tıklayın.";
            HotelSelectionGuideOkButton.Visibility = Visibility.Visible;
            HotelSelectComboBox.ClearValue(Border.BorderBrushProperty);
            _isPostHotelSelectionGuidanceActive = true;
        }

        private void HideHotelSelectionGuidance()
        {
            SavedAllocationsContainer.Effect = null;
            SavedAllocationsContainer.IsEnabled = true;
            HotelSelectionGuideOverlay.Visibility = Visibility.Collapsed;
            HotelSelectComboBox.ClearValue(Border.BorderBrushProperty);
            _isPostHotelSelectionGuidanceActive = false;
        }

        private void SelectRoom(RoomViewModel room)
        {
            foreach (var currentRoom in RoomsList)
            {
                currentRoom.SetSelected(false);
            }

            _selectedRoomForAllocation = room;
            _selectedRoomForAllocation?.SetSelected(true);
        }

        private void ClearSelectedRoom()
        {
            foreach (var currentRoom in RoomsList)
            {
                currentRoom.SetSelected(false);
            }

            _selectedRoomForAllocation = null;
            SelectedHotelTitleLabel.Text = $"🏨 Seçilen Otel: {HotelSelectComboBox.SelectedItem} Oda Yerleşim Düzeni";
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

        private bool HasRoomCardAncestor(DependencyObject source)
        {
            DependencyObject current = source;

            while (current != null)
            {
                if (current is FrameworkElement element && element.DataContext is RoomViewModel)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private bool HasInteractiveAncestor(DependencyObject source)
        {
            DependencyObject current = source;

            while (current != null)
            {
                if (current is Button ||
                    current is DataGrid ||
                    current is DataGridRow ||
                    current is DataGridCell ||
                    current is ComboBox ||
                    current is ComboBoxItem ||
                    current is ListBox ||
                    current is ListBoxItem ||
                    current is ScrollBar)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void EnsureRoomAllocationColumns()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE RoomAllocations ADD COLUMN TourType TEXT NOT NULL DEFAULT ''");
                }
            }
            catch
            {
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE RoomAllocations ADD COLUMN Period TEXT NOT NULL DEFAULT ''");
                }
            }
            catch
            {
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE RoomAllocations ADD COLUMN GroupType TEXT NOT NULL DEFAULT ''");
                }
            }
            catch
            {
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE RoomAllocations ADD COLUMN DaysCount TEXT NOT NULL DEFAULT ''");
                }
            }
            catch
            {
            }
        }

        private void LoadSavedAllocations(string hotelNameFilter = null)
        {
            try
            {
                var loadedRows = new List<SavedAllocationRow>();

                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations
                        .AsNoTracking()
                        .Include(x => x.Hotel)
                        .Include(x => x.Occupants)
                        .ToList();

                    if (!string.IsNullOrWhiteSpace(hotelNameFilter))
                    {
                        allocations = allocations
                            .Where(x => x.Hotel != null && x.Hotel.Name == hotelNameFilter)
                            .ToList();
                    }

                    var passengerIds = allocations
                        .SelectMany(x => x.Occupants)
                        .Select(x => x.PassengerId)
                        .Distinct()
                        .ToList();

                    var personLookup = db.Persons
                        .AsNoTracking()
                        .Where(x => passengerIds.Contains(x.Id))
                        .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}");

                    var groupedAllocations = allocations
                        .GroupBy(x => new
                        {
                            HotelName = x.Hotel?.Name ?? "Bilinmiyor",
                            x.TourType,
                            x.Period,
                            x.GroupType,
                            x.DaysCount
                        })
                        .OrderBy(x => x.Key.HotelName)
                        .ThenBy(x => x.Key.TourType)
                        .ThenBy(x => x.Key.Period)
                        .ThenBy(x => x.Key.GroupType)
                        .ThenBy(x => x.Key.DaysCount)
                        .ToList();

                    foreach (var allocationGroup in groupedAllocations)
                    {
                        var allocationList = allocationGroup
                            .OrderBy(x => int.TryParse(x.RoomNumber, out int roomNo) ? roomNo : int.MaxValue)
                            .ThenBy(x => x.RoomNumber)
                            .ToList();

                        var occupantNames = allocationList
                            .SelectMany(x => x.Occupants)
                            .Select(x => personLookup.TryGetValue(x.PassengerId, out string fullName)
                                ? fullName
                                : $"Yolcu #{x.PassengerId}")
                            .Distinct()
                            .ToList();

                        loadedRows.Add(new SavedAllocationRow
                        {
                            HotelName = allocationGroup.Key.HotelName,
                            TourType = allocationGroup.Key.TourType,
                            Period = allocationGroup.Key.Period,
                            GroupType = allocationGroup.Key.GroupType,
                            DaysCount = allocationGroup.Key.DaysCount,
                            RoomCount = allocationList.Count,
                            RoomSummary = string.Join(", ", allocationList
                                .GroupBy(x => x.RoomType)
                                .OrderBy(x => x.Key)
                                .Select(x => $"{x.Count()} {x.Key}")),
                            OccupantCount = allocationList.SelectMany(x => x.Occupants).Select(x => x.PassengerId).Distinct().Count(),
                            OccupantNames = occupantNames.Count > 0
                                ? string.Join(", ", occupantNames)
                                : "Boş Oda"
                        });
                    }
                }

                _allSavedAllocations = loadedRows;
                if (SavedAllocationSearchTextBox != null)
                {
                    SavedAllocationSearchTextBox.Text = string.Empty;
                }
                PopulateSavedAllocationFilters();
                ApplySavedAllocationFilters();

                SavedAllocationsTitleLabel.Text = string.IsNullOrWhiteSpace(hotelNameFilter)
                    ? "Kaydedilen Oda Yerleşimleri"
                    : $"{hotelNameFilter} İçin Kaydedilen Oda Yerleşimleri";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaydedilen odalamalar yüklenirken hata oluştu: {ex.Message}",
                                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateSavedAllocationFilters()
        {
            SetFilterItems(SavedAllocationTourTypeFilterComboBox, _allSavedAllocations.Select(x => x.TourType));
            SetFilterItems(SavedAllocationPeriodFilterComboBox, _allSavedAllocations.Select(x => x.Period));
            SetFilterItems(SavedAllocationGroupTypeFilterComboBox, _allSavedAllocations.Select(x => x.GroupType));
            SetFilterItems(SavedAllocationDaysFilterComboBox, _allSavedAllocations.Select(x => x.DaysCount));
        }

        private void SetFilterItems(ComboBox comboBox, IEnumerable<string> values)
        {
            if (comboBox == null)
            {
                return;
            }

            var items = new List<string> { "Tümü" };
            items.AddRange(values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x));

            comboBox.ItemsSource = items;
            comboBox.SelectedIndex = 0;
        }

        private void ApplySavedAllocationFilters()
        {
            if (SavedAllocations == null)
            {
                return;
            }

            string searchText = SavedAllocationSearchTextBox?.Text?.Trim() ?? string.Empty;
            string selectedTourType = SavedAllocationTourTypeFilterComboBox?.SelectedItem?.ToString();
            string selectedPeriod = SavedAllocationPeriodFilterComboBox?.SelectedItem?.ToString();
            string selectedGroupType = SavedAllocationGroupTypeFilterComboBox?.SelectedItem?.ToString();
            string selectedDays = SavedAllocationDaysFilterComboBox?.SelectedItem?.ToString();

            var filteredRows = _allSavedAllocations
                .Where(x => IsFilterMatch(x.TourType, selectedTourType))
                .Where(x => IsFilterMatch(x.Period, selectedPeriod))
                .Where(x => IsFilterMatch(x.GroupType, selectedGroupType))
                .Where(x => IsFilterMatch(x.DaysCount, selectedDays))
                .Where(x => string.IsNullOrWhiteSpace(searchText) ||
                            ContainsText(x.HotelName, searchText) ||
                            ContainsText(x.TourType, searchText) ||
                            ContainsText(x.Period, searchText) ||
                            ContainsText(x.GroupType, searchText) ||
                            ContainsText(x.DaysCount, searchText) ||
                            ContainsText(x.RoomSummary, searchText) ||
                            ContainsText(x.OccupantNames, searchText))
                .ToList();

            SavedAllocations.Clear();
            foreach (var row in filteredRows)
            {
                SavedAllocations.Add(row);
            }
        }

        private bool IsFilterMatch(string value, string selectedFilter)
        {
            return string.IsNullOrWhiteSpace(selectedFilter) ||
                   selectedFilter == "Tümü" ||
                   string.Equals(value ?? string.Empty, selectedFilter, StringComparison.OrdinalIgnoreCase);
        }

        private bool ContainsText(string source, string searchText)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   source.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SavedAllocationFilter_Changed(object sender, RoutedEventArgs e)
        {
            ApplySavedAllocationFilters();
        }

        private bool SaveRoomAllocationsToDatabase()
        {
            if (HotelSelectComboBox.SelectedItem == null)
            {
                MessageBox.Show("Lütfen önce odalama yapılacak oteli seçin.",
                                "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (RoomsList.Count == 0)
            {
                MessageBox.Show("Kaydedilecek en az bir oda açmanız gerekiyor.",
                                "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string selectedHotelName = HotelSelectComboBox.SelectedItem.ToString();
            string selectedTourType = TourTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedPeriod = PeriodComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedGroupType = GroupTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedDaysCount = DurationComboBox.SelectedItem?.ToString() ?? string.Empty;

            try
            {
                using (var db = new AppDbContext())
                {
                    var hotel = db.Hotels.FirstOrDefault(x => x.Name == selectedHotelName);

                    if (hotel == null)
                    {
                        hotel = new Hotel
                        {
                            Name = selectedHotelName
                        };

                        db.Hotels.Add(hotel);
                        db.SaveChanges();
                    }

                    var previousAllocations = db.RoomAllocations
                        .Include(x => x.Occupants)
                        .Where(x => x.HotelId == hotel.Id &&
                                    x.TourType == selectedTourType &&
                                    x.Period == selectedPeriod &&
                                    x.GroupType == selectedGroupType &&
                                    x.DaysCount == selectedDaysCount)
                        .ToList();

                    if (previousAllocations.Count > 0)
                    {
                        db.RoomOccupants.RemoveRange(previousAllocations.SelectMany(x => x.Occupants));
                        db.RoomAllocations.RemoveRange(previousAllocations);
                        db.SaveChanges();
                    }

                    foreach (var room in RoomsList)
                    {
                        var roomAllocation = new RoomAllocation
                        {
                            HotelId = hotel.Id,
                            RoomNumber = room.RoomNumber ?? string.Empty,
                            RoomType = room.RoomTypeName ?? string.Empty,
                            TourType = selectedTourType,
                            Period = selectedPeriod,
                            GroupType = selectedGroupType,
                            DaysCount = selectedDaysCount,
                            IsConnection = room.IsConnection,
                            ConnectionGroupId = room.ConnectionGroupId ?? string.Empty,
                            Occupants = room.Occupants
                                .Where(x => x.PersonId > 0)
                                .Select(x => new RoomOccupant
                                {
                                    PassengerId = x.PersonId
                                })
                                .ToList()
                        };

                        db.RoomAllocations.Add(roomAllocation);
                    }

                    db.SaveChanges();
                }

                LoadSavedAllocations(selectedHotelName);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Odalama kaydı veritabanına yazılırken hata oluştu: {ex.Message}",
                                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ShowStep1()
        {
            Step3Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step1Grid.Visibility = Visibility.Visible;

            Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

            NextButton.Content = "İleri ▶";
            BackButton.Visibility = Visibility.Collapsed;
            currentStep = 1;

            if (HotelSelectComboBox.SelectedItem == null)
            {
                ShowHotelSelectionGuidance();
            }
            else
            {
                ShowPostHotelSelectionGuidance();
            }
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

                // 2. Odaları mühürle ve renklerini ata
                _firstRoomToConnect.IsConnection = true;
                _firstRoomToConnect.ConnectionGroupId = $"TEMP_{Guid.NewGuid():N}";
                _firstRoomToConnect.RoomMargin = new Thickness(5, 5, 0, 5); // Aradaki boşluğu daralt

                secondRoom.IsConnection = true;
                secondRoom.ConnectionGroupId = _firstRoomToConnect.ConnectionGroupId;
                secondRoom.RoomMargin = new Thickness(0, 5, 5, 5); // İlk odaya yapışık dursun

                NormalizeConnectionGroups();

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
                    ShowHotelSelectionGuidance();
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
                if (!SaveRoomAllocationsToDatabase())
                {
                    return;
                }

                MessageBox.Show("Odalama yerleşim planı veritabanına kaydedildi ve kayıt listesi güncellendi!",
                                "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowStep1();
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

        private void HotelSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedHotelName = HotelSelectComboBox.SelectedItem?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedHotelName))
            {
                ShowPostHotelSelectionGuidance();
            }
            else
            {
                ShowHotelSelectionGuidance();
            }

            LoadSavedAllocations(selectedHotelName);
        }

        private void HotelSelectionGuideOkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPostHotelSelectionGuidanceActive)
            {
                HideHotelSelectionGuidance();
            }
        }

        private void Step3Grid_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            if (HasRoomCardAncestor(source) || HasInteractiveAncestor(source))
            {
                return;
            }

            ClearSelectedRoom();
        }

        private void DeleteSavedAllocationButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var selectedAllocation = button?.DataContext as SavedAllocationRow;

            if (selectedAllocation == null)
            {
                return;
            }

            var result = MessageBox.Show(
                $"{selectedAllocation.HotelName} için {selectedAllocation.TourType} / {selectedAllocation.Period} odalama kaydı tamamen silinsin mi?",
                "Odalama Kaydını Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations
                        .Include(x => x.Occupants)
                        .Include(x => x.Hotel)
                        .Where(x => x.Hotel != null &&
                                    x.Hotel.Name == selectedAllocation.HotelName &&
                                    x.TourType == selectedAllocation.TourType &&
                                    x.Period == selectedAllocation.Period &&
                                    x.GroupType == selectedAllocation.GroupType &&
                                    x.DaysCount == selectedAllocation.DaysCount)
                        .ToList();

                    if (allocations.Count == 0)
                    {
                        MessageBox.Show("Silinmek istenen kayıt bulunamadı.",
                                        "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadSavedAllocations(HotelSelectComboBox.SelectedItem?.ToString());
                        return;
                    }

                    db.RoomOccupants.RemoveRange(allocations.SelectMany(x => x.Occupants));
                    db.RoomAllocations.RemoveRange(allocations);
                    db.SaveChanges();
                }

                LoadSavedAllocations(HotelSelectComboBox.SelectedItem?.ToString());

                MessageBox.Show("Seçilen odalama kaydı tamamen silindi.",
                                "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Odalama kaydı silinirken hata oluştu: {ex.Message}",
                                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditSavedAllocationButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var selectedAllocation = button?.DataContext as SavedAllocationRow;

            if (selectedAllocation == null)
            {
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations
                        .AsNoTracking()
                        .Include(x => x.Hotel)
                        .Include(x => x.Occupants)
                        .Where(x => x.Hotel != null &&
                                    x.Hotel.Name == selectedAllocation.HotelName &&
                                    x.TourType == selectedAllocation.TourType &&
                                    x.Period == selectedAllocation.Period &&
                                    x.GroupType == selectedAllocation.GroupType &&
                                    x.DaysCount == selectedAllocation.DaysCount)
                        .ToList();

                    allocations = allocations
                        .OrderBy(x => int.TryParse(x.RoomNumber, out int roomNo) ? roomNo : int.MaxValue)
                        .ThenBy(x => x.RoomNumber)
                        .ToList();

                    if (allocations.Count == 0)
                    {
                        MessageBox.Show("Düzenlenecek odalama kaydı bulunamadı.",
                                        "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var passengerIds = allocations
                        .SelectMany(x => x.Occupants)
                        .Select(x => x.PassengerId)
                        .Distinct()
                        .ToList();

                    var persons = db.Persons
                        .AsNoTracking()
                        .Where(x => passengerIds.Contains(x.Id))
                        .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}");

                    HotelSelectComboBox.SelectedItem = selectedAllocation.HotelName;
                    TourTypeComboBox.SelectedItem = selectedAllocation.TourType;
                    PeriodComboBox.SelectedItem = selectedAllocation.Period;
                    GroupTypeComboBox.SelectedItem = selectedAllocation.GroupType;
                    DurationComboBox.SelectedItem = selectedAllocation.DaysCount;

                    RoomsList.Clear();
                    PassengerPool.Clear();
                    _selectedRoomForAllocation = null;
                    _firstRoomToConnect = null;

                    foreach (var allocation in allocations)
                    {
                        int capacity = ResolveRoomCapacity(allocation.RoomType);
                        var room = new RoomViewModel
                        {
                            RoomNumber = allocation.RoomNumber,
                            RoomTypeName = allocation.RoomType,
                            MaxCapacity = capacity,
                            IsConnection = allocation.IsConnection,
                            ConnectionGroupId = allocation.ConnectionGroupId ?? string.Empty
                        };

                        foreach (var occupant in allocation.Occupants)
                        {
                            room.Occupants.Add(new PassengerUiModel
                            {
                                PersonId = occupant.PassengerId,
                                FullName = persons.TryGetValue(occupant.PassengerId, out string fullName)
                                    ? fullName
                                    : $"Yolcu #{occupant.PassengerId}",
                                RoomType = allocation.RoomType
                            });
                        }

                        room.UpdateStatus();
                        RoomsList.Add(room);
                    }
                }

                NormalizeConnectionGroups();
                LoadFilteredPassengers();
                ClearSelectedRoom();

                Step1Grid.Visibility = Visibility.Collapsed;
                Step2Grid.Visibility = Visibility.Collapsed;
                Step3Grid.Visibility = Visibility.Visible;

                Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

                BackButton.Visibility = Visibility.Visible;
                NextButton.Content = "Kaydet ve Bitir";
                currentStep = 3;

                SelectedHotelTitleLabel.Text = $"🏨 Seçilen Otel: {selectedAllocation.HotelName} Oda Yerleşim Düzeni";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Odalama kaydı düzenlenirken hata oluştu: {ex.Message}",
                                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private int ResolveRoomCapacity(string roomType)
        {
            string selectedRoomType = (roomType ?? string.Empty).ToUpper();

            if (selectedRoomType.Contains("SNGL") || selectedRoomType.Contains("SINGLE")) return 1;
            if (selectedRoomType.Contains("DBL") || selectedRoomType.Contains("TWIN")) return 2;
            if (selectedRoomType.Contains("TRPL") || selectedRoomType.Contains("TRİPLE")) return 3;
            if (selectedRoomType.Contains("QUAD")) return 4;
            if (selectedRoomType.Contains("QUINT")) return 5;

            return 2;
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

            NormalizeConnectionGroups();

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

            if (_selectedRoomForAllocation == currentRoom)
            {
                ClearSelectedRoom();
            }

            NormalizeConnectionGroups();

            // Sol taraftaki yolcu sayacı etiketini güncelle
            RefreshPassengerCountLabel();
        }

        private void NormalizeConnectionGroups()
        {
            var connectionGroups = RoomsList
                .Where(r => r.IsConnection && !string.IsNullOrWhiteSpace(r.ConnectionGroupId))
                .GroupBy(r => r.ConnectionGroupId)
                .OrderBy(g => g.Min(r => int.TryParse(r.RoomNumber, out int roomNo) ? roomNo : int.MaxValue))
                .ToList();

            int groupIndex = 1;
            foreach (var group in connectionGroups)
            {
                string newGroupId = $"Group_{groupIndex}";
                string selectedNeonHex = NeonColors[(groupIndex - 1) % NeonColors.Length];
                var neonBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedNeonHex));
                string orderedRooms = string.Join(" + ", group
                    .OrderBy(r => int.TryParse(r.RoomNumber, out int roomNo) ? roomNo : int.MaxValue)
                    .Select(r => r.RoomNumber));
                string groupTitle = $"🔗 CONNECTION GRUBU #{groupIndex} (Oda {orderedRooms})";

                foreach (var room in group)
                {
                    room.IsConnection = true;
                    room.ConnectionGroupId = newGroupId;
                    room.ConnectionGroupText = groupTitle;
                    room.ConnectionBorderBrush = neonBrush;
                }

                groupIndex++;
            }

            foreach (var room in RoomsList.Where(r => !r.IsConnection))
            {
                room.ConnectionGroupId = "";
                room.ConnectionGroupText = "";
                room.ConnectionBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                room.RoomMargin = new Thickness(5);
            }

            var sortedRooms = RoomsList
                .OrderBy(r => r.IsConnection ? r.ConnectionGroupId : $"ZZZ_{r.RoomNumber}")
                .ThenBy(r => int.TryParse(r.RoomNumber, out int roomNo) ? roomNo : int.MaxValue)
                .ToList();

            RoomsList.Clear();
            foreach (var room in sortedRooms)
            {
                RoomsList.Add(room);
            }
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

public class SavedAllocationRow
    {
        public string HotelName { get; set; }
        public string TourType { get; set; }
        public string Period { get; set; }
        public string GroupType { get; set; }
        public string DaysCount { get; set; }
        public int RoomCount { get; set; }
        public string RoomSummary { get; set; }
        public int OccupantCount { get; set; }
        public string OccupantNames { get; set; }
    }

    public class RoomViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _statusText = "Boş";
        private Brush _statusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        private int _maxCapacity;
        private Brush _selectionBorderBrush = Brushes.Transparent;
        private Thickness _selectionBorderThickness = new Thickness(0);
        private Brush _selectionBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));

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

        public Brush SelectionBorderBrush
        {
            get => _selectionBorderBrush;
            set
            {
                _selectionBorderBrush = value;
                OnPropertyChanged(nameof(SelectionBorderBrush));
            }
        }

        public Thickness SelectionBorderThickness
        {
            get => _selectionBorderThickness;
            set
            {
                _selectionBorderThickness = value;
                OnPropertyChanged(nameof(SelectionBorderThickness));
            }
        }

        public Brush SelectionBackground
        {
            get => _selectionBackground;
            set
            {
                _selectionBackground = value;
                OnPropertyChanged(nameof(SelectionBackground));
            }
        }

        public void SetSelected(bool isSelected)
        {
            SelectionBorderBrush = isSelected
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
                : Brushes.Transparent;
            SelectionBorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
            SelectionBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                isSelected ? "#172554" : "#0F172A"));
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
