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
        private SavedAllocationRow _matchedSavedAllocation = null;
        private bool _suppressHotelSelectionChanged;
        private bool _suppressStep1SelectionChanged;
        private bool _suppressStep2SelectionChanged;
        private bool _isPreviewMode;

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
                ResetModuleToInitialState();
            }), DispatcherPriority.Loaded);

            // Combobox'lara kutucuğa tıklandığında da açılma davranışı ekle
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AttachComboBoxOpenOnBoxClick();
            }), DispatcherPriority.Loaded);

            // Ayrıca tüm görsel ağa çarpan tıklamaları yakalayarak
            // herhangi bir ComboBox içindeki kutucuğa tıklanırsa açılmasını garantileyelim.
            // handledEventsToo=true ile daha önce yakalanmış olayları da işleyebiliriz.
            this.AddHandler(
                System.Windows.UIElement.PreviewMouseLeftButtonDownEvent,
                new System.Windows.Input.MouseButtonEventHandler(Root_PreviewMouseLeftButtonDown),
                true);




        }

        private void ShowAllRecordsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ownerWindow = Window.GetWindow(this);
                var selected = OdaAramaPopup.ShowDialogAndReturn(ownerWindow);
                if (selected != null)
                {
                    LoadSavedAllocationIntoRooming(selected);
                }
            }
            catch
            {
                // ignore
            }
        }



        private void AttachComboBoxOpenOnBoxClick()
        {
            try
            {
                AttachToChildren(this);
            }
            catch
            {
                // Ignore attach failures
            }
        }

        private void AttachToChildren(DependencyObject parent)
        {
            if (parent == null) return;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is ComboBox cb)
                {
                    // Avoid multiple attachments
                    cb.PreviewMouseLeftButtonDown -= ComboBox_PreviewMouseLeftButtonDown;
                    cb.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
                }

                AttachToChildren(child);
            }
        }

        private void ComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                // Eğer zaten açık değilse aç
                if (!cb.IsDropDownOpen)
                {
                    cb.IsDropDownOpen = true;
                    e.Handled = true;
                }
            }
        }

        private void Root_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var source = e.OriginalSource as DependencyObject;
                var combo = FindAncestor<ComboBox>(source);
                if (combo != null)
                {
                    if (!combo.IsDropDownOpen)
                    {
                        combo.IsDropDownOpen = true;
                        // İşlemi sonlandırıyoruz böylece varsayılan davranışla çakışma azalır
                        e.Handled = true;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t) return t;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void LoadComboBoxData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var allSettings = db.AppSettings
                        .Where(x => x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Id)
                        .ToList();

                    var hotels = allSettings
                        .Where(x => IsSettingTypeMatch(x.Type, "Otel Ekle"))
                        .Select(x => x.Value);
                    var tourTypes = allSettings
                        .Where(x => IsSettingTypeMatch(x.Type, "TourType", "Tur Türü"))
                        .Select(x => x.Value);
                    var periods = allSettings
                        .Where(x => IsSettingTypeMatch(x.Type, "Dönem"))
                        .Select(x => x.Value);
                    var groupTypes = allSettings
                        .Where(x => IsSettingTypeMatch(x.Type, "GroupType", "Grup Türü"))
                        .Select(x => x.Value);
                    var durations = allSettings
                        .Where(x => IsSettingTypeMatch(x.Type, "Kaç Günlük Grup", "CountOfDays"))
                        .Select(x => x.Value);

                    SetComboItems(HotelSelectComboBox, hotels);
                    SetComboItems(Step2HotelComboBox, hotels);
                    SetComboItems(Step1TourTypeComboBox, tourTypes);
                    SetComboItems(TourTypeComboBox, tourTypes);
                    SetComboItems(Step1PeriodComboBox, periods);
                    SetComboItems(PeriodComboBox, periods);
                    SetComboItems(Step1GroupTypeComboBox, groupTypes);
                    SetComboItems(GroupTypeComboBox, groupTypes);
                    SetComboItems(Step1DurationComboBox, durations);
                    SetComboItems(DurationComboBox, durations);

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

        private void SetComboItems(ComboBox comboBox, IEnumerable<string> values)
        {
            if (comboBox == null)
            {
                return;
            }

            comboBox.ItemsSource = values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
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
                    var matchingRegistrations = db.Registrations
                        .AsNoTracking()
                        .Where(x => x.TourType == selectedTourType &&
                                    x.Period == selectedPeriod &&
                                    x.GroupType == selectedGroupType &&
                                    x.DaysCount == selectedDuration)
                        .Select(x => new
                        {
                            x.PersonId,
                            x.RoomType
                        })
                        .ToList();

                    var personIds = matchingRegistrations
                        .Select(x => x.PersonId)
                        .Where(x => x > 0)
                        .Distinct()
                        .ToList();

                    var persons = db.Persons
                        .AsNoTracking()
                        .Where(x => personIds.Contains(x.Id))
                        .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}");

                    foreach (var registration in matchingRegistrations)
                    {
                        if (registration.PersonId <= 0 || occupiedPersonIds.Contains(registration.PersonId))
                        {
                            continue;
                        }

                        if (!persons.TryGetValue(registration.PersonId, out string fullName))
                        {
                            continue;
                        }

                        AddPassengerToPoolIfMissing(new PassengerUiModel
                        {
                            PersonId = registration.PersonId,
                            FullName = fullName,
                            RoomType = string.IsNullOrWhiteSpace(registration.RoomType) ? "Belirtilmemiş" : registration.RoomType
                        });
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

        private void ResetModuleToInitialState()
        {
            _suppressStep1SelectionChanged = true;
            _suppressStep2SelectionChanged = true;
            _suppressHotelSelectionChanged = true;

            Step1TourTypeComboBox.SelectedItem = null;
            Step1PeriodComboBox.SelectedItem = null;
            Step1GroupTypeComboBox.SelectedItem = null;
            Step1DurationComboBox.SelectedItem = null;
            HotelSelectComboBox.SelectedItem = null;

            TourTypeComboBox.SelectedItem = null;
            PeriodComboBox.SelectedItem = null;
            GroupTypeComboBox.SelectedItem = null;
            DurationComboBox.SelectedItem = null;
            Step2HotelComboBox.SelectedItem = null;

            _suppressHotelSelectionChanged = false;
            _suppressStep2SelectionChanged = false;
            _suppressStep1SelectionChanged = false;

            _matchedSavedAllocation = null;
            _isPreviewMode = false;
            ClearMatchedHighlight();
            ClearRoomAllocationDraft();
            SavedAllocations.Clear();
            LoadSavedAllocations();
            UpdateStep1ProgressiveVisibility();
            UpdateStep1ResultArea();
            UpdateStep2ProceedButtonVisibility();
            ShowStep1(false);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Sadece odak ver, açma davranışını iptal et
                Step1TourTypeComboBox.Focus();
                Step1TourTypeComboBox.IsDropDownOpen = false;
            }), DispatcherPriority.Background);
        }

        private void UpdateStep1ProgressiveVisibility()
        {
            Step1PeriodBorder.Visibility = Step1TourTypeComboBox.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;
            Step1GroupTypeBorder.Visibility = Step1PeriodComboBox.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;
            Step1DurationBorder.Visibility = Step1GroupTypeComboBox.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;
            HotelComboHighlightBorder.Visibility = Step1DurationComboBox.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;

            // Yeni açılan alanların kullanıcı tarafından görülmesini sağlamak için
            // Step1 Grid içindeki son görünür alanı otomatik olarak kaydır.
            try
            {
                var progressive = new Border[] {
                    Step1TourTypeBorder,
                    Step1PeriodBorder,
                    Step1GroupTypeBorder,
                    Step1DurationBorder,
                    HotelComboHighlightBorder
                };

                Border lastVisible = null;
                foreach (var b in progressive)
                {
                    if (b != null && b.Visibility == Visibility.Visible)
                        lastVisible = b;
                }

                if (lastVisible != null)
                {
                    ScrollElementIntoView(lastVisible);
                }
            }
            catch
            {
                // ignore scroll failures
            }
        }

        private void ScrollElementIntoView(FrameworkElement element)
        {
            if (element == null) return;

            // Delay until layout pass completes so ActualHeight/offsets are available
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // If Step1Grid is a ScrollViewer, bring element into view
                    element.BringIntoView();

                    // Additionally nudge parent ScrollViewer to center the element if possible
                    if (Step1Grid is ScrollViewer sv)
                    {
                        // Calculate element offset relative to ScrollViewer
                        var transform = element.TransformToAncestor(sv) as System.Windows.Media.Transform;
                        if (transform != null)
                        {
                            var point = transform.Transform(new System.Windows.Point(0, 0));
                            double target = point.Y - 10; // small top margin
                            if (target < 0) target = 0;
                            sv.ScrollToVerticalOffset(target);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private bool AreAllStep1SelectionsMade()
        {
            return Step1TourTypeComboBox.SelectedItem != null &&
                   Step1PeriodComboBox.SelectedItem != null &&
                   Step1GroupTypeComboBox.SelectedItem != null &&
                   Step1DurationComboBox.SelectedItem != null &&
                   HotelSelectComboBox.SelectedItem != null;
        }

        private bool AreAllStep2SelectionsMade()
        {
            return TourTypeComboBox.SelectedItem != null &&
                   PeriodComboBox.SelectedItem != null &&
                   GroupTypeComboBox.SelectedItem != null &&
                   DurationComboBox.SelectedItem != null &&
                   Step2HotelComboBox.SelectedItem != null;
        }

        private SavedAllocationRow FindMatchingSavedAllocationForStep1()
        {
            if (!AreAllStep1SelectionsMade())
            {
                return null;
            }

            string selectedHotelName = HotelSelectComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedTourType = Step1TourTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedPeriod = Step1PeriodComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedGroupType = Step1GroupTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedDaysCount = Step1DurationComboBox.SelectedItem?.ToString() ?? string.Empty;

            return _allSavedAllocations.FirstOrDefault(x =>
                string.Equals(x.HotelName, selectedHotelName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TourType, selectedTourType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Period, selectedPeriod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.GroupType, selectedGroupType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.DaysCount, selectedDaysCount, StringComparison.OrdinalIgnoreCase));
        }

        private void ClearMatchedHighlight()
        {
            foreach (var row in _allSavedAllocations)
            {
                row.IsMatchedHighlight = false;
            }
        }

        private void UpdateStep1ResultArea()
        {
            UpdateStep1ProgressiveVisibility();
            Step1NoMatchPanel.Visibility = Visibility.Collapsed;
            Step1PreviewPanel.Visibility = _isPreviewMode ? Visibility.Visible : Visibility.Collapsed;
            HotelSelectionGuideOverlay.Visibility = Visibility.Collapsed;
            NoSavedAllocationResultPanel.Visibility = Visibility.Collapsed;

            ClearMatchedHighlight();
            SavedAllocations.Clear();

            // Apply frosted look to combo boxes when preview mode is active
            SetComboBoxesFrosted(_isPreviewMode);

            if (_isPreviewMode && _matchedSavedAllocation != null)
            {
                _matchedSavedAllocation.IsMatchedHighlight = true;
                SavedAllocations.Add(_matchedSavedAllocation);
                SavedAllocationsContainer.Effect = null;
                SavedAllocationsContainer.IsEnabled = true;
                return;
            }

            if (!AreAllStep1SelectionsMade())
            {
                SavedAllocationsContainer.Effect = new BlurEffect { Radius = 10 };
                SavedAllocationsContainer.IsEnabled = false;
                SetComboBoxesFrosted(false);
                return;
            }

            _matchedSavedAllocation = FindMatchingSavedAllocationForStep1();

            if (_matchedSavedAllocation != null)
            {
                _matchedSavedAllocation.IsMatchedHighlight = true;
                SavedAllocations.Add(_matchedSavedAllocation);
                SavedAllocationsContainer.Effect = null;
                SavedAllocationsContainer.IsEnabled = true;
                return;
            }

            SavedAllocationsContainer.Effect = new BlurEffect { Radius = 10 };
            SavedAllocationsContainer.IsEnabled = false;
            Step1NoMatchPanel.Visibility = Visibility.Visible;
        }

        private void SetComboBoxesFrosted(bool frosted)
        {
            try
            {
                var blur = frosted ? new BlurEffect { Radius = 4 } : null as Effect;
                double opacity = frosted ? 0.7 : 1.0;

                ComboBox[] targets = new ComboBox[] {
                    Step1TourTypeComboBox,
                    Step1PeriodComboBox,
                    Step1GroupTypeComboBox,
                    Step1DurationComboBox,
                    HotelSelectComboBox,
                    TourTypeComboBox,
                    PeriodComboBox,
                    GroupTypeComboBox,
                    DurationComboBox,
                    Step2HotelComboBox,
                    SavedAllocationTourTypeFilterComboBox,
                    SavedAllocationPeriodFilterComboBox,
                    SavedAllocationGroupTypeFilterComboBox,
                    SavedAllocationDaysFilterComboBox
                };

                foreach (var cb in targets)
                {
                    if (cb == null) continue;
                    cb.Effect = blur;
                    cb.Opacity = opacity;
                    cb.IsHitTestVisible = !frosted;
                }
            }
            catch
            {
                // ignore UI tweak failures
            }
        }

        private void SyncStep1SelectionsToStep2()
        {
            _suppressStep2SelectionChanged = true;
            TourTypeComboBox.SelectedItem = Step1TourTypeComboBox.SelectedItem;
            PeriodComboBox.SelectedItem = Step1PeriodComboBox.SelectedItem;
            GroupTypeComboBox.SelectedItem = Step1GroupTypeComboBox.SelectedItem;
            DurationComboBox.SelectedItem = Step1DurationComboBox.SelectedItem;
            Step2HotelComboBox.SelectedItem = HotelSelectComboBox.SelectedItem;
            _suppressStep2SelectionChanged = false;
            UpdateStep2ProceedButtonVisibility();
            UpdateAllocationModeTitle();
        }

        private void SyncStep2SelectionsToStep1()
        {
            _suppressStep1SelectionChanged = true;
            _suppressHotelSelectionChanged = true;
            Step1TourTypeComboBox.SelectedItem = TourTypeComboBox.SelectedItem;
            Step1PeriodComboBox.SelectedItem = PeriodComboBox.SelectedItem;
            Step1GroupTypeComboBox.SelectedItem = GroupTypeComboBox.SelectedItem;
            Step1DurationComboBox.SelectedItem = DurationComboBox.SelectedItem;
            HotelSelectComboBox.SelectedItem = Step2HotelComboBox.SelectedItem;
            _suppressHotelSelectionChanged = false;
            _suppressStep1SelectionChanged = false;
            UpdateStep1ProgressiveVisibility();
        }

        private void UpdateStep2ProceedButtonVisibility()
        {
            ProceedToRoomingButton.Visibility = AreAllStep2SelectionsMade()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ShowSavedAllocationPreview(string title, string description)
        {
            SyncStep2SelectionsToStep1();
            _matchedSavedAllocation = FindMatchingSavedAllocationForStep1();
            _isPreviewMode = _matchedSavedAllocation != null;
            Step1PreviewTitleText.Text = title;
            Step1PreviewDescriptionText.Text = description;
            ShowStep1(false);
            UpdateStep1ResultArea();
        }

        private void ClearRoomAllocationDraft()
        {
            RoomsList.Clear();
            PassengerPool.Clear();
            _selectedRoomForAllocation = null;
            _firstRoomToConnect = null;
            RefreshPassengerCountLabel();
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
            try
            {
                using (var db = new AppDbContext())
                {
                    var allSettings = db.AppSettings
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Id)
                        .ToList();

                    SetFilterItems(
                        SavedAllocationTourTypeFilterComboBox,
                        allSettings
                            .Where(x => IsSettingTypeMatch(x.Type, "TourType", "Tur Türü"))
                            .Select(x => x.Value));

                    SetFilterItems(
                        SavedAllocationPeriodFilterComboBox,
                        allSettings
                            .Where(x => IsSettingTypeMatch(x.Type, "Dönem"))
                            .Select(x => x.Value));

                    SetFilterItems(
                        SavedAllocationGroupTypeFilterComboBox,
                        allSettings
                            .Where(x => IsSettingTypeMatch(x.Type, "GroupType", "Grup Türü"))
                            .Select(x => x.Value));

                    SetFilterItems(
                        SavedAllocationDaysFilterComboBox,
                        allSettings
                            .Where(x => IsSettingTypeMatch(x.Type, "Kaç Günlük Grup", "CountOfDays"))
                            .Select(x => x.Value));
                }
            }
            catch
            {
                SetFilterItems(SavedAllocationTourTypeFilterComboBox, Enumerable.Empty<string>());
                SetFilterItems(SavedAllocationPeriodFilterComboBox, Enumerable.Empty<string>());
                SetFilterItems(SavedAllocationGroupTypeFilterComboBox, Enumerable.Empty<string>());
                SetFilterItems(SavedAllocationDaysFilterComboBox, Enumerable.Empty<string>());
            }
        }

        private bool IsSettingTypeMatch(string type, params string[] expectedTypes)
        {
            string normalizedType = type?.Trim() ?? string.Empty;
            return expectedTypes.Any(expected =>
                normalizedType.Equals(expected, StringComparison.OrdinalIgnoreCase));
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
                .Distinct());

            comboBox.ItemsSource = items;
            comboBox.SelectedIndex = -1;
        }

        private void ResetSavedAllocationSearchAndFilters()
        {
            if (SavedAllocationSearchTextBox != null)
            {
                SavedAllocationSearchTextBox.Text = string.Empty;
            }

            if (SavedAllocationTourTypeFilterComboBox != null)
            {
                SavedAllocationTourTypeFilterComboBox.SelectedIndex = -1;
            }

            if (SavedAllocationPeriodFilterComboBox != null)
            {
                SavedAllocationPeriodFilterComboBox.SelectedIndex = -1;
            }

            if (SavedAllocationGroupTypeFilterComboBox != null)
            {
                SavedAllocationGroupTypeFilterComboBox.SelectedIndex = -1;
            }

            if (SavedAllocationDaysFilterComboBox != null)
            {
                SavedAllocationDaysFilterComboBox.SelectedIndex = -1;
            }
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

            if (NoSavedAllocationResultPanel != null)
            {
                bool hasNoResult = filteredRows.Count == 0;
                NoSavedAllocationResultPanel.Visibility = hasNoResult ? Visibility.Visible : Visibility.Collapsed;
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

        private void ShowStep1(bool _)
        {
            Step3Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step1Grid.Visibility = Visibility.Visible;

            Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

            NextButton.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            currentStep = 1;
            HotelSelectionPanel.Visibility = Visibility.Visible;
            UpdateStep1ResultArea();
        }

        private void ShowStep2()
        {
            Step1Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Visible;

            Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

            BackButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Collapsed;
            currentStep = 2;
            UpdateStep2ProceedButtonVisibility();
        }

        private void ShowStep3()
        {
            _suppressHotelSelectionChanged = true;
            HotelSelectComboBox.SelectedItem = Step2HotelComboBox.SelectedItem;
            _suppressHotelSelectionChanged = false;

            SelectedHotelTitleLabel.Text = $"🏨 Seçilen Otel: {HotelSelectComboBox.SelectedItem} Oda Yerleşim Düzeni";
            LoadFilteredPassengers();

            // Eğer seçilen kriterlere ait hiç kayıt yoksa kullanıcıyı uyar ve anasayfaya yönlendir
            try
            {
                string selectedTourType = TourTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
                string selectedPeriod = PeriodComboBox.SelectedItem?.ToString() ?? string.Empty;
                string selectedGroupType = GroupTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
                string selectedDaysCount = DurationComboBox.SelectedItem?.ToString() ?? string.Empty;

                using (var db = new AppDbContext())
                {
                    bool anyRegistration = db.Registrations
                        .AsNoTracking()
                        .Any(x => x.TourType == selectedTourType
                               && x.Period == selectedPeriod
                               && x.GroupType == selectedGroupType
                               && x.DaysCount == selectedDaysCount);

                    if (!anyRegistration)
                    {
                        MessageBox.Show(
                            "Henüz bu tura, bu döneme, bu grup türüne ve bu grup süresine kaydettiğiniz kimse yok.\nOtel odalamasını yapmadan önce gerekli kayıtları yapın.",
                            "Kayıt Bulunamadı",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        var main = Window.GetWindow(this) as MainWindow;
                        if (main != null)
                        {
                            main.NavigateToHome();
                        }

                        return;
                    }
                }
            }
            catch
            {
                // ignore
            }

            Step1Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Visible;

            Tab1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Tab3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

            BackButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Visible;
            NextButton.Content = "Kaydet ve Bitir";
            currentStep = 3;
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
            if (currentStep == 3)
            {
                if (!SaveRoomAllocationsToDatabase())
                {
                    return;
                }

                LoadSavedAllocations();
                ShowSavedAllocationPreview(
                    "Kayıt Başarılı",
                    "Kaydettiğiniz odalama kaydı aşağıda önizlemede gösteriliyor. Önizlemeyi kapatınca Otel Odalama modülü başlangıç ekranına döneceksiniz.");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 3)
            {
                ShowStep2();
            }
            else if (currentStep == 2)
            {
                SyncStep2SelectionsToStep1();
                ShowStep1(false);
            }
        }

        private void HotelSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressHotelSelectionChanged)
            {
                return;
            }

            UpdateStep1ResultArea();
        }

        private void HotelSelectionGuideOkButton_Click(object sender, RoutedEventArgs e)
        {
            HotelSelectionGuideOverlay.Visibility = Visibility.Collapsed;
        }

        private void ResetCriteriaSelections()
        {
            TourTypeComboBox.SelectedItem = null;
            PeriodComboBox.SelectedItem = null;
            GroupTypeComboBox.SelectedItem = null;
            DurationComboBox.SelectedItem = null;
            Step2HotelComboBox.SelectedItem = null;
            _matchedSavedAllocation = null;
            UpdateStep2ProceedButtonVisibility();
        }

        private void Step1Selection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressStep1SelectionChanged)
            {
                return;
            }

            if (sender == Step1TourTypeComboBox)
            {
                _suppressStep1SelectionChanged = true;
                Step1PeriodComboBox.SelectedItem = null;
                Step1GroupTypeComboBox.SelectedItem = null;
                Step1DurationComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = true;
                HotelSelectComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = false;
                _suppressStep1SelectionChanged = false;
            }
            else if (sender == Step1PeriodComboBox)
            {
                _suppressStep1SelectionChanged = true;
                Step1GroupTypeComboBox.SelectedItem = null;
                Step1DurationComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = true;
                HotelSelectComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = false;
                _suppressStep1SelectionChanged = false;
            }
            else if (sender == Step1GroupTypeComboBox)
            {
                _suppressStep1SelectionChanged = true;
                Step1DurationComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = true;
                HotelSelectComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = false;
                _suppressStep1SelectionChanged = false;
            }
            else if (sender == Step1DurationComboBox)
            {
                _suppressHotelSelectionChanged = true;
                HotelSelectComboBox.SelectedItem = null;
                _suppressHotelSelectionChanged = false;
            }

            _isPreviewMode = false;
            UpdateStep1ResultArea();
        }

        private void Step2Selection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressStep2SelectionChanged)
            {
                return;
            }

            UpdateStep2ProceedButtonVisibility();
            UpdateAllocationModeTitle();
        }

        private void UpdateAllocationModeTitle()
        {
            // Step 2'deki 5 ComboBox'ın seçimlerini al
            string tourType = TourTypeComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
            string period = PeriodComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
            string groupType = GroupTypeComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
            string daysCount = DurationComboBox.SelectedItem?.ToString() ?? "Seçilmedi";
            string hotel = Step2HotelComboBox.SelectedItem?.ToString() ?? "Seçilmedi";

            // Başlığı güncelle: "Tur Türü | Dönem | Grup Türü | Gün Sayısı | Otel Adı"
            AllocationModeTitle.Text = $"{tourType} | {period} | {groupType} | {daysCount} | {hotel}";
        }

        private void Step1ProceedToCriteriaButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AreAllStep1SelectionsMade())
            {
                return;
            }

            // Eğer sonuç yok uyarısı görünüyorsa, kullanıcı 'İleri'ye bastığında
            // Kriter Seçimi (Step2) adımını atlayıp doğrudan Odalama Motoru (Step3)'na geçilsin.
            if (Step1NoMatchPanel != null && Step1NoMatchPanel.Visibility == Visibility.Visible)
            {
                // Kriterleri Step1'den Step2'ye kopyala, başlığı güncelle ve doğrudan Step3'e geç
                SyncStep1SelectionsToStep2();
                UpdateAllocationModeTitle();
                ShowStep3();
                return;
            }

            // Normal akış: Step2'ye geç
            SyncStep1SelectionsToStep2();
            ShowStep2();
        }

        private void ProceedToRoomingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AreAllStep2SelectionsMade())
            {
                return;
            }

            // Önce seçilen kriterlere uyan kayıt (yolcu) var mı kontrol edelim
            string selectedTourType = TourTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedPeriod = PeriodComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedGroupType = GroupTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            string selectedDuration = DurationComboBox.SelectedItem?.ToString() ?? string.Empty;

            try
            {
                using (var db = new AppDbContext())
                {
                    bool anyMatching = db.Registrations
                        .AsNoTracking()
                        .Any(x => x.TourType == selectedTourType
                               && x.Period == selectedPeriod
                               && x.GroupType == selectedGroupType
                               && x.DaysCount == selectedDuration);

                    if (!anyMatching)
                    {
                        // Kullanıcıyı uyar ve OK ile anasayfaya gönder
                        MessageBox.Show(
                            "Henüz bu tura, bu döneme, bu grup türüne ve bu grup süresine kaydettiğiniz kimse yok.\nOtel odalamasını yapmadan önce gerekli kayıtları yapın.",
                            "Kayıt Bulunamadı",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        var main = Window.GetWindow(this) as MainWindow;
                        if (main != null)
                        {
                            main.NavigateToHome();
                        }

                        return;
                    }
                }
            }
            catch
            {
                // Hata durumunda normal akışı devam ettirelim
            }

            SyncStep2SelectionsToStep1();
            // Sync selections and check if there is an existing saved allocation for these criteria.
            // If varsa yükle; yoksa önce geçiş öncesi draft odaları temizle ki eski odalar yeni kriterlerle karışmasın.
            UpdateAllocationModeTitle();

            try
            {
                // refresh saved allocations lookup
                LoadSavedAllocations();
            }
            catch
            {
                // ignore failures
            }

            var matched = FindMatchingSavedAllocationForStep1();
            if (matched != null)
            {
                // Eğer daha önce bu kriterler için kaydedilmiş bir odalama varsa,
                // onu yükleyip düzenlemeye geç.
                LoadSavedAllocationIntoRooming(matched);
                return;
            }

            // Kayıt yoksa önce mevcut draft'ı temizle ki eski odalar yeni kriterlerle karışmasın
            ClearRoomAllocationDraft();
            ShowStep3();
        }

        private void CloseStep1PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            ResetModuleToInitialState();
        }

        private void GoToNewAllocationFromGridButton_Click(object sender, RoutedEventArgs e)
        {
            ResetModuleToInitialState();
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
                        ResetModuleToInitialState();
                        return;
                    }

                    db.RoomOccupants.RemoveRange(allocations.SelectMany(x => x.Occupants));
                    db.RoomAllocations.RemoveRange(allocations);
                    db.SaveChanges();
                }

                MessageBox.Show("Seçilen odalama kaydı tamamen silindi.",
                                "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetModuleToInitialState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Odalama kaydı silinirken hata oluştu: {ex.Message}",
                                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InspectSavedAllocationButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var selectedAllocation = button?.DataContext as SavedAllocationRow;

            if (selectedAllocation == null)
            {
                return;
            }

            LoadSavedAllocationIntoRooming(selectedAllocation);
        }

        private void EditSavedAllocationButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var selectedAllocation = button?.DataContext as SavedAllocationRow;

            if (selectedAllocation == null)
            {
                return;
            }

            LoadSavedAllocationIntoRooming(selectedAllocation);
        }

        private void LoadSavedAllocationIntoRooming(SavedAllocationRow selectedAllocation)
        {
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

                    _suppressStep1SelectionChanged = true;
                    _suppressStep2SelectionChanged = true;
                    _suppressHotelSelectionChanged = true;

                    Step1TourTypeComboBox.SelectedItem = selectedAllocation.TourType;
                    Step1PeriodComboBox.SelectedItem = selectedAllocation.Period;
                    Step1GroupTypeComboBox.SelectedItem = selectedAllocation.GroupType;
                    Step1DurationComboBox.SelectedItem = selectedAllocation.DaysCount;
                    HotelSelectComboBox.SelectedItem = selectedAllocation.HotelName;

                    TourTypeComboBox.SelectedItem = selectedAllocation.TourType;
                    PeriodComboBox.SelectedItem = selectedAllocation.Period;
                    GroupTypeComboBox.SelectedItem = selectedAllocation.GroupType;
                    DurationComboBox.SelectedItem = selectedAllocation.DaysCount;
                    Step2HotelComboBox.SelectedItem = selectedAllocation.HotelName;

                    _suppressHotelSelectionChanged = false;
                    _suppressStep2SelectionChanged = false;
                    _suppressStep1SelectionChanged = false;

                    LoadSavedAllocations(selectedAllocation.HotelName);

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
                _matchedSavedAllocation = selectedAllocation;
                _isPreviewMode = false;
                UpdateAllocationModeTitle();
                ShowStep3();
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
    public bool IsMatchedHighlight { get; set; }
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
    private string _roomTypeName;
    public string RoomTypeName
    {
        get => _roomTypeName;
        set
        {
            _roomTypeName = value;
            OnPropertyChanged(nameof(RoomTypeName));
            OnPropertyChanged(nameof(RoomNumberDisplay));
            OnPropertyChanged(nameof(BedCount));
        }
    }

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

    public int BedCount
    {
        get
        {
            var type = (RoomTypeName ?? string.Empty).ToUpper();
            if (type.Contains("SNGL") || type.Contains("SINGLE")) return 1;
            if (type.Contains("DBL")) return 1; // DBL: 1 yatak ama 2 kişilik
            if (type.Contains("TWIN")) return 2;
            if (type.Contains("TRPL") || type.Contains("TRİPLE") || type.Contains("TRIPLE")) return 3;
            if (type.Contains("QUAD")) return 4;
            if (type.Contains("QUINT")) return 5;
            // fallback: assume beds equal to person capacity
            return MaxCapacity;
        }
    }

    public string RoomNumberDisplay =>
        $"Oda {RoomNumber} [{RoomTypeName}] ({BedCount} Yatak, {MaxCapacity} Kişilik)";

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
