using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Windows.Threading;
using System.Windows.Input;
using System.Globalization;

namespace TurizmOperasyonSistemi.Views
{
    public partial class OdaAramaPopup : Window
    {
        private List<SavedAllocationRow> _allRows = new List<SavedAllocationRow>();
        private ObservableCollection<SavedAllocationRow> _rows = new ObservableCollection<SavedAllocationRow>();
        // Period filter için klavye arama tamponu ve zamanlayıcı
        private DispatcherTimer _periodFilterTimer;
        private string _periodFilterBuffer = string.Empty;
        private readonly int _periodFilterTimeoutMs = 800;

        public OdaAramaPopup()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = _rows;
            LoadData();
        }

        // Verilen DbContext üzerinde, odalara atanmış yolculardan
        // Registration tablosunda ilgili oda kaydının TourType/Period/GroupType/DaysCount
        // kriterlerini aynı anda karşılayan bir kayıt bulunmayanları temizle.
        private void CleanupOrphanOccupants(AppDbContext db)
        {
            try
            {
                var occupants = db.RoomOccupants.Include(o => o.RoomAllocation).ToList();
                var toRemove = new List<RoomOccupant>();

                foreach (var occ in occupants)
                {
                    var alloc = occ.RoomAllocation;
                    if (alloc == null) continue;

                    bool hasMatchingRegistration = db.Registrations.Any(r =>
                        r.PersonId == occ.PassengerId
                        && r.TourType == alloc.TourType
                        && r.Period == alloc.Period
                        && r.GroupType == alloc.GroupType
                        && r.DaysCount == alloc.DaysCount);

                    if (!hasMatchingRegistration)
                    {
                        toRemove.Add(occ);
                    }
                }

                if (toRemove.Count > 0)
                {
                    db.RoomOccupants.RemoveRange(toRemove);
                    db.SaveChanges();
                }

                // Her durumda: oda içinde hiçbir Occupant kalmamış olan RoomAllocation kayıtlarını sil
                try
                {
                    var emptyAllocations = db.RoomAllocations
                        .Include(a => a.Occupants)
                        .Where(a => !a.Occupants.Any())
                        .ToList();

                    if (emptyAllocations.Count > 0)
                    {
                        db.RoomAllocations.RemoveRange(emptyAllocations);
                        db.SaveChanges();
                    }
                }
                catch
                {
                    // Sessizce atla
                }
            }
            catch
            {
                // Temizlik sırasında hata olsa bile uygulamanın çalışmasına devam et.
            }
        }

        private void LoadData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Temizlik: Oda kayıtlarında bulunan, ancak Registrations tablosunda
                    // aynı TurType, Period, GroupType ve DaysCount kriterlerini aynı anda
                    // karşılayan bir kaydı olmayan yolcuları (RoomOccupant) sil.
                    CleanupOrphanOccupants(db);

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

                    // Preserve the order of hotels as defined in AppSettings (SortOrder then Id)
                    var hotels = new List<string>();
                    // add hotels from settings first in the order they come (settings already ordered)
                    hotels.AddRange(hotelsFromSettings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
                    // then append any hotels that exist in DB but not in settings, preserving DB order
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
                .Where(x => (string.IsNullOrWhiteSpace(tour) || tour == "Tümü" || string.Equals(x.TourType, tour, StringComparison.CurrentCultureIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(period) || period == "Tümü" || string.Equals(x.Period, period, StringComparison.CurrentCultureIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(group) || group == "Tümü" || string.Equals(x.GroupType, group, StringComparison.CurrentCultureIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(days) || days == "Tümü" || string.Equals(x.DaysCount, days, StringComparison.CurrentCultureIgnoreCase)))
                .Where(x => (string.IsNullOrWhiteSpace(hotel) || hotel == "Tümü" || string.Equals(x.HotelName, hotel, StringComparison.CurrentCultureIgnoreCase)))
                .Where(x => string.IsNullOrWhiteSpace(search) ||
                            (x.OccupantNames != null && x.OccupantNames.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                            (x.HotelName != null && x.HotelName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                            (x.RoomSummary != null && x.RoomSummary.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                            (x.TourType != null && x.TourType.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                            (x.Period != null && x.Period.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)
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
            this.Close();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = btn?.DataContext as SavedAllocationRow;
            if (row == null) return;

            // find owner HotelAllocationView and call Inspect
            var owner = this.Owner as Window;
            if (owner != null)
            {
                var hv = owner.Content as Frame;
            }

            // close and signal selection by storing into Tag
            this.Tag = row;
            this.DialogResult = true;
            this.Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = btn?.DataContext as SavedAllocationRow;
            if (row == null) return;

            var res = MessageBox.Show($"{row.HotelName} için {row.TourType} / {row.Period} odalama kaydı tamamen silinsin mi?","Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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

        // Button for parent to show popup programmatically
        public static SavedAllocationRow ShowDialogAndReturn(Window owner)
        {
            var dlg = new OdaAramaPopup();
            dlg.Owner = owner;
            var res = dlg.ShowDialog();
            if (res == true)
            {
                return dlg.Tag as SavedAllocationRow;
            }
            return null;
        }

        // PreviewTextInput handler for PeriodFilter
        private void PeriodFilter_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(e?.Text)) return;
                var cb = sender as ComboBox;
                if (cb == null) return;

                if (_periodFilterTimer == null)
                {
                    _periodFilterTimer = new DispatcherTimer(DispatcherPriority.Input)
                    {
                        Interval = TimeSpan.FromMilliseconds(_periodFilterTimeoutMs)
                    };
                    _periodFilterTimer.Tick += (s, ev) =>
                    {
                        _periodFilterTimer.Stop();
                        _periodFilterBuffer = string.Empty;
                    };
                }

                _periodFilterBuffer += e.Text;
                _periodFilterTimer.Stop();
                _periodFilterTimer.Start();

                var items = cb.Items.Cast<object>().Select(i => i?.ToString() ?? string.Empty).ToList();
                if (items.Count == 0) return;

                var culture = CultureInfo.CurrentCulture;
                string buffer = _periodFilterBuffer.Trim();
                if (string.IsNullOrEmpty(buffer)) return;

                string matched = null;
                if (char.IsDigit(buffer[0]))
                {
                    // For numeric starts we still want culture-aware comparison to handle localized digits/formatting
                    matched = items.FirstOrDefault(it => it.StartsWith(buffer, true, culture));
                    if (matched == null)
                        matched = items.FirstOrDefault(it => it.IndexOf(buffer, StringComparison.CurrentCultureIgnoreCase) >= 0);
                }
                else
                {
                    matched = items.FirstOrDefault(it => it.StartsWith(buffer, true, culture));
                }

                if (matched != null)
                {
                    cb.SelectedItem = matched;
                    if (!cb.IsDropDownOpen) cb.IsDropDownOpen = true;
                    var container = cb.ItemContainerGenerator.ContainerFromItem(matched) as FrameworkElement;
                    if (container != null)
                    {
                        container.BringIntoView();
                        try { container.Focus(); } catch { }
                    }
                    e.Handled = true;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void PeriodFilter_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Back)
                {
                    _periodFilterBuffer = string.Empty;
                    _periodFilterTimer?.Stop();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                {
                    _periodFilterBuffer = string.Empty;
                    _periodFilterTimer?.Stop();
                    e.Handled = true;
                    return;
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
