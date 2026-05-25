using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class OdaAramaPopup : Window
    {
        private List<SavedAllocationRow> _allRows = new List<SavedAllocationRow>();
        private ObservableCollection<SavedAllocationRow> _rows = new ObservableCollection<SavedAllocationRow>();

        public OdaAramaPopup()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = _rows;
            LoadData();
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
                    var tours = settings.Where(x => x.Type != null && x.Type.Trim().Equals("TourType", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    var periods = settings.Where(x => x.Type != null && x.Type.Trim().Equals("Dönem", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    var groupTypes = settings.Where(x => x.Type != null && x.Type.Trim().Equals("GroupType", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).Distinct().ToList();
                    var days = settings.Where(x => x.Type != null && (x.Type.Trim().Equals("Kaç Günlük Grup", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("CountOfDays", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();
                    var hotels = db.Hotels.AsNoTracking().Select(x => x.Name).Distinct().ToList();

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
    }
}
