using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class OdaAramaPage : Page
    {
        // When true, the page is shown embedded inside the Lists card and should adapt UI
        public bool IsEmbeddedMode { get; set; } = false;
        private List<SavedAllocationRow> _allRows = new List<SavedAllocationRow>();
        private ObservableCollection<SavedAllocationRow> _rows = new ObservableCollection<SavedAllocationRow>();

        public OdaAramaPage()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = _rows;
            LoadData();
        }

        private void CiktiAlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Button;
                var row = btn?.DataContext as SavedAllocationRow;
                if (row == null)
                {
                    MessageBox.Show("Seçili kayıt bulunamadı.");
                    return;
                }

                // Before export: check if there are registrations matching the same criteria that are not yet allocated
                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations.Include(x => x.Occupants).Include(x => x.Hotel)
                        .Where(x => x.Hotel != null && x.Hotel.Name == row.HotelName && x.TourType == row.TourType && x.Period == row.Period && x.GroupType == row.GroupType && x.DaysCount == row.DaysCount)
                        .ToList();

                    var allocatedPassengerIds = allocations.SelectMany(a => a.Occupants).Select(o => o.PassengerId).Distinct().ToList();

                    var regs = db.Registrations.AsNoTracking()
                        .Where(r => r.TourType == row.TourType && r.Period == row.Period && r.GroupType == row.GroupType && r.DaysCount == row.DaysCount && r.IsActive)
                        .ToList();

                    var unallocated = regs.Where(r => !allocatedPassengerIds.Contains(r.PersonId)).ToList();
                    if (unallocated.Count > 0)
                    {
                        var msg = $"Seçili kriterler için hâlâ odalanmamış {unallocated.Count} kişi var. İndirmeye izin verilmiyor.\n\nOdalama motoruna gitmek ister misiniz?";
                        var res = MessageBox.Show(msg, "Eksik Oda Atamaları", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (res == MessageBoxResult.Yes)
                        {
                            // navigate to HotelAllocationView and preselect filters
                            var hv = new HotelAllocationView();
                            hv.Loaded += (s2, e2) =>
                            {
                                try
                                {
                                    var cbTour = hv.FindName("Step1TourTypeComboBox") as ComboBox;
                                    var cbPeriod = hv.FindName("Step1PeriodComboBox") as ComboBox;
                                    var cbGroup = hv.FindName("Step1GroupTypeComboBox") as ComboBox;
                                    var cbDays = hv.FindName("Step1DaysComboBox") as ComboBox;
                                    var cbHotel = hv.FindName("Step1HotelComboBox") as ComboBox;

                                    if (cbTour != null) cbTour.SelectedItem = row.TourType;
                                    if (cbPeriod != null) cbPeriod.SelectedItem = row.Period;
                                    if (cbGroup != null) cbGroup.SelectedItem = row.GroupType;
                                    if (cbDays != null) cbDays.SelectedItem = row.DaysCount;
                                    if (cbHotel != null) cbHotel.SelectedItem = row.HotelName;
                                }
                                catch { }
                            };

                            if (this.NavigationService != null)
                            {
                                this.NavigationService.Navigate(hv);
                                return;
                            }

                            var win = new Window
                            {
                                Title = "Odalama Motoru",
                                Content = new Frame { Content = hv, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                                Width = 1200,
                                Height = 800,
                                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                Owner = Application.Current?.MainWindow
                            };
                            win.Show();
                        }
                        return;
                    }
                }

                // Build a small context menu at the button to choose format
                var context = new ContextMenu();
                var miWord = new MenuItem { Header = "Word (.docx)" };
                miWord.Click += (s, ev) => { ExportSavedAllocation(row, "docx"); };
                var miExcel = new MenuItem { Header = "Excel (.xlsx)" };
                miExcel.Click += (s, ev) => { ExportSavedAllocation(row, "xlsx"); };
                var miPdf = new MenuItem { Header = "PDF (.pdf)" };
                miPdf.Click += (s, ev) => { ExportSavedAllocation(row, "pdf"); };
                context.Items.Add(miWord);
                context.Items.Add(miExcel);
                context.Items.Add(miPdf);
                context.PlacementTarget = btn;
                context.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                context.IsOpen = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ExportSavedAllocation(SavedAllocationRow row, string format)
        {
            try
            {
                var headers = new List<string> { "Oda No", "Oda Türü", "Kapasite", "Yerleşen Kişi Sayısı", "Yerleşen Kişiler", "Connection" };
                var rows = new List<Dictionary<string, string>>();
                var checkInDisplay = "-";
                var checkOutDisplay = "-";

                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations
                        .Include(x => x.Hotel)
                        .Include(x => x.Occupants)
                        .Where(x => x.Hotel != null
                            && x.Hotel.Name == row.HotelName
                            && x.TourType == row.TourType
                            && x.Period == row.Period
                            && x.GroupType == row.GroupType
                            && x.DaysCount == row.DaysCount)
                        .ToList();

                    var passengerIds = allocations
                        .SelectMany(x => x.Occupants)
                        .Select(x => x.PassengerId)
                        .Distinct()
                        .ToList();

                    var personLookup = db.Persons
                        .AsNoTracking()
                        .Where(x => passengerIds.Contains(x.Id))
                        .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}");

                    var ordered = allocations
                        .OrderBy(x => int.TryParse(x.RoomNumber, out var number) ? number : int.MaxValue)
                        .ThenBy(x => x.RoomNumber)
                        .ToList();

                    var connectionLookup = BuildConnectionLabelLookup(ordered);
                    var personCounter = 1;

                    foreach (var allocation in ordered)
                    {
                        var capacity = ResolveRoomCapacity(allocation.RoomType);
                        var occupants = allocation.Occupants
                            .Select(o => personLookup.TryGetValue(o.PassengerId, out var name) ? name : $"Yolcu #{o.PassengerId}")
                            .ToList();

                        for (var i = 0; i < occupants.Count; i++)
                        {
                            occupants[i] = $"{personCounter}. {occupants[i]}";
                            personCounter++;
                        }

                        var occupantCount = occupants.Count;
                        var connectionLabel = ResolveConnectionLabel(allocation, connectionLookup);

                        rows.Add(new Dictionary<string, string>
                        {
                            ["Oda No"] = allocation.RoomNumber ?? string.Empty,
                            ["Oda Türü"] = allocation.RoomType ?? string.Empty,
                            ["Kapasite"] = capacity.ToString(),
                            ["Yerleşen Kişi Sayısı"] = occupantCount.ToString(),
                            ["Yerleşen Kişiler"] = occupantCount > 0 ? string.Join(", ", occupants) : "Boş Oda",
                            ["Connection"] = connectionLabel
                        });
                    }

                    var anyAlloc = allocations.FirstOrDefault();
                    if (anyAlloc != null)
                    {
                        checkInDisplay = FormatDateDisplay(anyAlloc.CheckInDate);
                        checkOutDisplay = FormatDateDisplay(anyAlloc.CheckOutDate);
                    }
                }

                var baseName = BuildExportBaseFileName(row);
                var exportTitle = string.Join(Environment.NewLine, new[]
                {
                    baseName,
                    row.HotelName ?? string.Empty,
                    $"Giriş: {checkInDisplay}",
                    $"Çıkış: {checkOutDisplay}"
                });

                var totalRooms = rows.Count;
                var typeSummary = rows
                    .GroupBy(r => r.TryGetValue("Oda Türü", out var roomType) ? roomType : string.Empty)
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .Select(g => $"{g.Count()} {g.Key}")
                    .ToList();
                var footerLines = new List<string> { $"TOPLAM ODA : {totalRooms}", string.Join(", ", typeSummary) };

                var filter = format == "docx"
                    ? "Word dosyası (*.docx)|*.docx"
                    : format == "xlsx"
                        ? "Excel dosyası (*.xlsx)|*.xlsx"
                        : "PDF dosyası (*.pdf)|*.pdf";
                var defaultName = baseName + (format == "docx" ? ".docx" : format == "xlsx" ? ".xlsx" : ".pdf");
                var path = AskForPath(
                    format == "docx" ? "Word belgesi" : format == "xlsx" ? "Excel çalışma kitabı" : "PDF belgesi",
                    filter,
                    defaultName);

                if (path == null)
                {
                    return;
                }

                if (format == "docx")
                {
                    Services.ExportHelper.ExportRoomAllocationWord(
                        path,
                        rows,
                        row.HotelName,
                        checkInDisplay,
                        checkOutDisplay,
                        row.TourType,
                        row.Period,
                        row.GroupType,
                        row.DaysCount);
                }
                else if (format == "xlsx")
                {
                    Services.ExportHelper.ExportExcel(
                        path,
                        rows,
                        headers,
                        exportTitle,
                        footerLines,
                        row.HotelName,
                        checkInDisplay,
                        checkOutDisplay,
                        row.TourType,
                        row.Period,
                        row.GroupType,
                        row.DaysCount);
                }
                else if (format == "pdf")
                {
                    Services.ExportHelper.ExportPdf(
                        path,
                        rows,
                        headers,
                        exportTitle,
                        footerLines,
                        row.HotelName,
                        checkInDisplay,
                        checkOutDisplay,
                        row.TourType,
                        row.Period,
                        row.GroupType,
                        row.DaysCount);
                }

                MessageBox.Show("Dosya başarıyla oluşturuldu.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export hatası: {ex.Message}");
            }
        }

        private Dictionary<string, string> BuildConnectionLabelLookup(List<RoomAllocation> allocations)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in allocations
                .Where(x => x.IsConnection && !string.IsNullOrWhiteSpace(x.ConnectionGroupId))
                .GroupBy(x => x.ConnectionGroupId))
            {
                var roomNumbers = group
                    .Select(x => NormalizeRoomNumber(x.RoomNumber))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => int.TryParse(x, out var number) ? number : int.MaxValue)
                    .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (roomNumbers.Count == 0)
                {
                    continue;
                }

                var label = string.Join("+", roomNumbers);
                foreach (var room in group)
                {
                    var roomNumber = NormalizeRoomNumber(room.RoomNumber);
                    if (!string.IsNullOrWhiteSpace(roomNumber))
                    {
                        lookup[roomNumber] = label;
                    }
                }
            }

            return lookup;
        }

        private string ResolveConnectionLabel(RoomAllocation allocation, Dictionary<string, string> connectionLookup)
        {
            if (allocation == null || !allocation.IsConnection)
            {
                return "Hayır";
            }

            var roomNumber = NormalizeRoomNumber(allocation.RoomNumber);
            if (!string.IsNullOrWhiteSpace(roomNumber) && connectionLookup != null && connectionLookup.TryGetValue(roomNumber, out var label))
            {
                return label;
            }

            return string.IsNullOrWhiteSpace(roomNumber) ? "Hayır" : roomNumber;
        }

        private string NormalizeRoomNumber(string roomNumber)
        {
            return (roomNumber ?? string.Empty).Trim();
        }

        private string FormatDateDisplay(string value)
        {
            if (DateTime.TryParse(value, out var dateTime))
            {
                return dateTime.ToString("dd.MM.yyyy");
            }

            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private string BuildExportBaseFileName(SavedAllocationRow row)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.TourType)) parts.Add(row.TourType);
            if (!string.IsNullOrWhiteSpace(row.Period)) parts.Add(row.Period);
            if (!string.IsNullOrWhiteSpace(row.GroupType)) parts.Add(row.GroupType + " " + row.DaysCount);
            if (!string.IsNullOrWhiteSpace(row.HotelName)) parts.Add(row.HotelName);
            parts.Add("Otel Odalama Listesi");
            var name = string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            // sanitize filename
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private string AskForPath(string title, string filter, string defaultFileName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName,
                AddExtension = true,
                OverwritePrompt = true
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private int ResolveRoomCapacity(string roomType)
        {
            string selectedRoomType = (roomType ?? string.Empty).ToUpper();
            if (selectedRoomType.Contains("SNGL") || selectedRoomType.Contains("SINGLE")) return 1;
            if (selectedRoomType.Contains("DBL") || selectedRoomType.Contains("TWIN")) return 2;
            if (selectedRoomType.Contains("TRPL") || selectedRoomType.Contains("TRİPLE") || selectedRoomType.Contains("TRIPLE")) return 3;
            if (selectedRoomType.Contains("QUAD")) return 4;
            if (selectedRoomType.Contains("QUINT")) return 5;
            return 2;
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = btn?.DataContext as SavedAllocationRow;
            if (row == null) return;

            try
            {
                var hv = new HotelAllocationView();
                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(hv);
                    return;
                }

                var win = new Window
                {
                    Title = "Oda Düzenle - " + row.HotelName,
                    Content = new Frame { Content = hv, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = btn?.DataContext as SavedAllocationRow;
            if (row == null) return;

            var res = MessageBox.Show($"{row.HotelName} için {row.TourType} / {row.Period} odalama kaydı tamamen silinsin mi?", "Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations.Include(x => x.Hotel).Include(x => x.Occupants)
                        .Where(x => x.Hotel != null && x.Hotel.Name == row.HotelName && x.TourType == row.TourType && x.Period == row.Period && x.GroupType == row.GroupType && x.DaysCount == row.DaysCount)
                        .ToList();

                    if (allocations.Count == 0)
                    {
                        MessageBox.Show("Kayıt bulunamadı");
                        return;
                    }

                    db.RoomOccupants.RemoveRange(allocations.SelectMany(x => x.Occupants));
                    db.RoomAllocations.RemoveRange(allocations);
                    db.SaveChanges();
                }

                // refresh
                _allRows.Remove(row);
                ApplyFilters();
                MessageBox.Show("Kayıt silindi");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Silme hatası: {ex.Message}");
            }
        }

        private void LoadData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var allocations = db.RoomAllocations
                        .AsNoTracking()
                        .Include(x => x.Hotel)
                        .Include(x => x.Occupants)
                        .ToList();

                    var passengerIds = allocations.SelectMany(x => x.Occupants).Select(x => x.PassengerId).Distinct().ToList();
                    var personLookup = db.Persons.AsNoTracking().Where(x => passengerIds.Contains(x.Id)).ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}");

                    var grouped = allocations.GroupBy(x => new { HotelName = x.Hotel?.Name ?? "Bilinmiyor", x.TourType, x.Period, x.GroupType, x.DaysCount })
                        .OrderBy(x => x.Key.HotelName).ThenBy(x => x.Key.TourType).ThenBy(x => x.Key.Period)
                        .ThenBy(x => x.Key.GroupType).ThenBy(x => x.Key.DaysCount)
                        .ToList();

                    foreach (var g in grouped)
                    {
                        var list = g.OrderBy(x => int.TryParse(x.RoomNumber, out int rn) ? rn : int.MaxValue).ThenBy(x => x.RoomNumber).ToList();
                        var occupantNames = list.SelectMany(x => x.Occupants).Select(x => personLookup.TryGetValue(x.PassengerId, out string name) ? name : $"Yolcu #{x.PassengerId}").Distinct().ToList();

                        _allRows.Add(new SavedAllocationRow
                        {
                            HotelName = g.Key.HotelName,
                            TourType = g.Key.TourType,
                            Period = g.Key.Period,
                            GroupType = g.Key.GroupType,
                            DaysCount = g.Key.DaysCount,
                            RoomCount = list.Count,
                            RoomSummary = string.Join(", ", list.GroupBy(x => x.RoomType).OrderBy(x => x.Key).Select(x => $"{x.Count()} {x.Key}")),
                            OccupantCount = list.SelectMany(x => x.Occupants).Select(x => x.PassengerId).Distinct().Count(),
                            OccupantNames = occupantNames.Count > 0 ? string.Join(", ", occupantNames) : "Boş Oda"
                        });
                    }

                    // populate filter comboboxes
                    var settings = db.AppSettings.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
                    var tours = settings.Where(x => x.Type != null && (x.Type.Trim().Equals("TourType", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("Tur Türü", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();
                    var periods = settings.Where(x => x.Type != null && (x.Type.Trim().Equals("Dönem", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("Period", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();
                    var groupTypes = settings.Where(x => x.Type != null && (x.Type.Trim().Equals("GroupType", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("Grup Türü", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();
                    var days = settings.Where(x => x.Type != null && (x.Type.Trim().Equals("Kaç Günlük Grup", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("CountOfDays", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();
                    var hotelsFromSettings = settings.Where(x => x.Type != null && (x.Type.Trim().Equals("Otel Ekle", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("Hotel", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();
                    var hotelsFromDb = db.Hotels.AsNoTracking().Select(x => x.Name).Distinct().ToList();

                    var hotels = new List<string>();
                    hotels.AddRange(hotelsFromSettings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
                    foreach (var h in hotelsFromDb)
                    {
                        if (string.IsNullOrWhiteSpace(h)) continue;
                        if (!hotels.Contains(h)) hotels.Add(h);
                    }

                    TourTypeFilter.ItemsSource = new List<string> { "Tümü" }.Concat(tours).ToList();
                    PeriodFilter.ItemsSource = new List<string> { "Tümü" }.Concat(periods).ToList();
                    GroupTypeFilter.ItemsSource = new List<string> { "Tümü" }.Concat(groupTypes).ToList();
                    DaysFilter.ItemsSource = new List<string> { "Tümü" }.Concat(days).ToList();
                    HotelFilter.ItemsSource = new List<string> { "Tümü" }.Concat(hotels).ToList();
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Veri yüklenirken hata: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            _rows.Clear();
            string search = SearchTextBox.Text?.Trim() ?? string.Empty;
            string tour = TourTypeFilter.SelectedItem?.ToString();
            string period = PeriodFilter.SelectedItem?.ToString();
            string group = GroupTypeFilter.SelectedItem?.ToString();
            string days = DaysFilter.SelectedItem?.ToString();
            string hotel = HotelFilter.SelectedItem?.ToString();

            var filtered = _allRows
                .Where(x => (string.IsNullOrWhiteSpace(tour) || tour == "Tümü" || string.Equals(x.TourType, tour, StringComparison.OrdinalIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(period) || period == "Tümü" || string.Equals(x.Period, period, StringComparison.OrdinalIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(group) || group == "Tümü" || string.Equals(x.GroupType, group, StringComparison.OrdinalIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(days) || days == "Tümü" || string.Equals(x.DaysCount, days, StringComparison.OrdinalIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(hotel) || hotel == "Tümü" || string.Equals(x.HotelName, hotel, StringComparison.OrdinalIgnoreCase)))
                .Where(x => string.IsNullOrWhiteSpace(search) ||
                            (x.OccupantNames != null && x.OccupantNames.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (x.HotelName != null && x.HotelName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (x.RoomSummary != null && x.RoomSummary.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (x.TourType != null && x.TourType.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (x.Period != null && x.Period.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                            )
                .ToList();

            foreach (var r in filtered)
            {
                _rows.Add(r);
            }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // navigate back if possible
            if (this.NavigationService != null && this.NavigationService.CanGoBack)
            {
                this.NavigationService.GoBack();
                return;
            }

            // fallback: navigate to an empty page
            this.NavigationService?.Navigate(new Page());
        }
    }
}
