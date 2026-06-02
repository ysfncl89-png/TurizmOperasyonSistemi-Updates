using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsGeneralView : Page
    {
        private List<GeneralRow> _allRows = new List<GeneralRow>();
        private Button _applyButton;

        public PersonsGeneralView()
        {
            InitializeComponent();
            Loaded += PersonsGeneralView_Loaded;
            LoadFilterOptions();
            // initial state: show filters, hide topbar
            TopBar.Visibility = Visibility.Collapsed;
            FiltersPanel.Visibility = Visibility.Visible;
        }

        private void PersonsGeneralView_Loaded(object sender, RoutedEventArgs e)
        {
            try { SearchTextBox.Focus(); } catch { }

            // If XAML missing the ApplyFiltersButton (was edited incorrectly), create it dynamically
            try
            {
                // If there is already a named button in XAML, skip dynamic creation
                var named = this.FindName("ApplyFiltersButton") as Button;
                if (named != null)
                {
                    _applyButton = named;
                }
                else
                {
                    if (FilterWarning != null)
                    {
                        if (FilterWarning.Parent is Panel parent)
                        {
                            _applyButton = new Button
                            {
                                Content = "Listeyi Göster",
                                Width = 160,
                                Padding = new Thickness(10, 8, 10, 8),
                                IsEnabled = false
                            };
                            // apply style if available
                            try { _applyButton.Style = (Style)FindResource("PrimaryButtonStyle"); } catch { }
                            _applyButton.Click += ApplyFiltersButton_Click;
                            parent.Children.Add(_applyButton);
                        }
                    }
                }
            }
            catch { }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.NavigationService != null && this.NavigationService.CanGoBack)
                {
                    this.NavigationService.GoBack();
                    return;
                }

                var win = Window.GetWindow(this);
                if (win != null) win.Close();
            }
            catch { }
        }

        private void LoadFilterOptions()
        {
            try
            {
                using var db = new AppDbContext();
                var all = db.AppSettings.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList();

                var tour = all.Where(x => x.Type == "TourType").Select(x => x.Value).ToList();
                tour.Insert(0, "Seçiniz");
                if (CbTourType != null) CbTourType.ItemsSource = tour;

                var period = all.Where(x => x.Type == "Dönem").Select(x => x.Value).ToList();
                period.Insert(0, "Seçiniz");
                if (CbPeriod != null) CbPeriod.ItemsSource = period;

                var group = all.Where(x => x.Type == "GroupType").Select(x => x.Value).ToList();
                group.Insert(0, "Seçiniz");
                if (CbGroupType != null) CbGroupType.ItemsSource = group;

                var days = all.Where(x => x.Type == "Kaç Günlük Grup").Select(x => x.Value).ToList();
                days.Insert(0, "Seçiniz");
                if (CbGroupDays != null) CbGroupDays.ItemsSource = days;
            }
            catch { }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var tourReady = CbTourType?.SelectedItem as string;
                var periodReady = CbPeriod?.SelectedItem as string;
                var ready = !string.IsNullOrWhiteSpace(tourReady) && tourReady != "Seçiniz" &&
                            !string.IsNullOrWhiteSpace(periodReady) && periodReady != "Seçiniz";

                if (_applyButton != null)
                    _applyButton.IsEnabled = ready;

                if (FilterWarning != null)
                    FilterWarning.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }

        private void ApplyFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // load results and swap UI: hide filters, show top search bar
            LoadResults();
            FiltersPanel.Visibility = Visibility.Collapsed;
            TopBar.Visibility = Visibility.Visible;
            ResultsBorder.Visibility = Visibility.Visible;
        }

        private void LoadResults()
        {
            try
            {
                using var db = new AppDbContext();

                var query = db.Registrations.Include(r => r.Person).AsQueryable();

                var tour = CbTourType.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(tour) && tour != "Seçiniz") query = query.Where(r => r.TourType == tour);

                var period = CbPeriod.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(period) && period != "Seçiniz") query = query.Where(r => r.Period == period);

                var group = CbGroupType.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(group) && group != "Seçiniz") query = query.Where(r => r.GroupType == group);

                var days = CbGroupDays.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(days) && days != "Seçiniz") query = query.Where(r => r.DaysCount == days);

                var list = query.OrderBy(r => r.Id).ToList();

                var rows = new List<GeneralRow>();
                var seq = 1;
                foreach (var reg in list)
                {
                    var p = reg.Person;
                    var r = new GeneralRow
                    {
                        Sequence = seq++,
                        FirstName = p?.FirstName ?? string.Empty,
                        LastName = p?.LastName ?? string.Empty,
                        TcKimlikNo = p?.TcKimlikNo ?? string.Empty,
                        Gender = p?.Gender ?? string.Empty,
                        BirthDate = p?.BirthDate.ToString("yyyy-MM-dd") ?? string.Empty,
                        PhoneNumber = p?.PhoneNumber ?? string.Empty,
                        Address = p?.Address ?? string.Empty,
                        PassportType = p?.PassportType ?? string.Empty,
                        PassportNumber = p?.PassportNumber ?? string.Empty,
                        PassportStartDate = p?.PassportStartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                        PassportEndDate = p?.PassportEndDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                        BloodType = p?.BloodType ?? string.Empty,
                        VaccineStatus = p?.VaccineStatus ?? string.Empty,
                        HealthStatus = p?.HealthStatus ?? string.Empty,
                        AgeRange = p?.AgeRange ?? string.Empty,

                        VisaRequest = reg.VisaRequest ?? string.Empty,
                        TicketRequest = reg.TicketRequest ?? string.Empty,
                        CargoRequest = reg.CargoRequest ?? string.Empty,
                        RoomType = reg.RoomType ?? string.Empty,
                        DepartureCity = reg.DepartureCity ?? string.Empty,
                        HasTransfer = reg.HasTransfer ?? string.Empty,
                        TransferCity = reg.TransferCity ?? string.Empty,
                        District = reg.District ?? string.Empty,
                        TotalPrice = reg.TotalPrice.ToString("0.##"),
                        Currency = reg.Currency ?? string.Empty,
                        PaidAmount = reg.PaidAmount.ToString("0.##"),
                        RemainingDebt = reg.RemainingDebt.ToString("0.##"),
                        Reference = reg.Reference ?? string.Empty
                    };
                    rows.Add(r);
                }

                _allRows = rows;
                ResultsDataGrid.ItemsSource = _allRows;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = (SearchTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultsDataGrid.ItemsSource = _allRows;
                SearchHint.Visibility = Visibility.Visible;
                return;
            }

            SearchHint.Visibility = Visibility.Collapsed;
            var q = text.ToLowerInvariant();

            // support column search like "Ad:ali" or "Ad: ali"
            if (q.Contains(":"))
            {
                var parts = q.Split(new[] { ':' }, 2);
                var key = parts[0].Trim();
                var val = parts[1].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    Func<GeneralRow, string> selector = GetSelectorForHeader(key);
                    if (selector != null)
                    {
                        ResultsDataGrid.ItemsSource = _allRows.Where(r => (selector(r) ?? string.Empty).ToLowerInvariant().Contains(val)).ToList();
                        return;
                    }
                }
            }

            // generic full-text search across all visible fields
            ResultsDataGrid.ItemsSource = _allRows.Where(r =>
                (r.FirstName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.LastName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.TcKimlikNo ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.PhoneNumber ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.Address ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.BloodType ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.HealthStatus ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.VaccineStatus ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.VisaRequest ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.TicketRequest ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.Reference ?? string.Empty).ToLowerInvariant().Contains(q)
            ).ToList();
        }

        private Func<GeneralRow, string> GetSelectorForHeader(string headerKey)
        {
            switch (headerKey.Trim().ToLowerInvariant())
            {
                case "ad": return r => r.FirstName;
                case "soyad": return r => r.LastName;
                case "tc" :
                case "tc kimlik":
                case "tckimlik": return r => r.TcKimlikNo;
                case "cinsiyet": return r => r.Gender;
                case "telefon":
                case "telefon numarası": return r => r.PhoneNumber;
                case "adres": return r => r.Address;
                case "kan grubu": return r => r.BloodType;
                case "hastalık":
                case "hastalık durumu": return r => r.HealthStatus;
                case "aşı":
                case "aşı durumu": return r => r.VaccineStatus;
                case "vize":
                case "vize talebi": return r => r.VisaRequest;
                case "bilet":
                case "bilet talebi": return r => r.TicketRequest;
                case "referans": return r => r.Reference;
                default: return null;
            }
        }

        private void DesignButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ListDesignDialog(ResultsDataGrid.Columns);
                dialog.Owner = Application.Current?.MainWindow;
                var res = dialog.ShowDialog();
                if (res != true) return;

                var options = dialog.ColumnOptions;
                if (options == null) return;

                for (int i = 0; i < ResultsDataGrid.Columns.Count && i < options.Count; i++)
                {
                    ResultsDataGrid.Columns[i].Visibility = options[i].IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "CSV olarak kaydet", Filter = "CSV dosyası (*.csv)|*.csv", FileName = "genel-listesi.csv" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var visibleCols = ResultsDataGrid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
                var headers = visibleCols.Select(c => c.Header?.ToString() ?? string.Empty).ToList();
                using var sw = new StreamWriter(dlg.FileName, false, Encoding.UTF8);
                sw.WriteLine(string.Join(',', headers.Select(h => EscapeCsv(h))));

                var rows = ResultsDataGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
                foreach (var item in rows)
                {
                    var vals = new List<string>();
                    foreach (var col in visibleCols)
                    {
                        var header = col.Header?.ToString() ?? string.Empty;
                        var val = GetPropertyStringValue(item, header);
                        vals.Add(EscapeCsv(val));
                    }
                    sw.WriteLine(string.Join(',', vals));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private string GetPropertyStringValue(object item, string header)
        {
            if (item == null) return string.Empty;
            var prop = item.GetType().GetProperties().FirstOrDefault(p => string.Equals(p.Name, MapHeaderToProperty(header), StringComparison.OrdinalIgnoreCase));
            if (prop == null)
            {
                // fallback: try by header name
                prop = item.GetType().GetProperty(header.Replace(" ", ""), System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            if (prop == null) return string.Empty;
            var val = prop.GetValue(item);
            return val?.ToString() ?? string.Empty;
        }

        private string MapHeaderToProperty(string header)
        {
            return header?.Trim().ToLowerInvariant() switch
            {
                "sıra" => "Sequence",
                "ad" => "FirstName",
                "soyad" => "LastName",
                "tc kimlik" => "TcKimlikNo",
                "cinsiyet" => "Gender",
                "doğum tarihi" => "BirthDate",
                "telefon numarası" => "PhoneNumber",
                "adres" => "Address",
                "pasaport tipi" => "PassportType",
                "pasaport numarası" => "PassportNumber",
                "pasaport başlangıç tarihi" => "PassportStartDate",
                "pasaport bitiş tarihi" => "PassportEndDate",
                "kan grubu" => "BloodType",
                "aşı durumu" => "VaccineStatus",
                "hastalık durumu" => "HealthStatus",
                "yaş aralığı" => "AgeRange",
                "vize talebi" => "VisaRequest",
                "bilet talebi" => "TicketRequest",
                "kargo talebi" => "CargoRequest",
                "oda tipi" => "RoomType",
                "katılım yapılacak şehir" => "DepartureCity",
                "aktarma var mı ?" => "HasTransfer",
                "aktarma şehri" => "TransferCity",
                "ilçe" => "District",
                "toplam fiyat" => "TotalPrice",
                "para birimi" => "Currency",
                "ödenen" => "PaidAmount",
                "kalan" => "RemainingDebt",
                "referans" => "Reference",
                _ => header?.Replace(" ", "")
            };
        }

        private string EscapeCsv(string v)
        {
            if (v == null) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return '"' + v.Replace("\"", "\"\"") + '"';
            return v;
        }

        private class GeneralRow
        {
            public int Sequence { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string TcKimlikNo { get; set; }
            public string Gender { get; set; }
            public string BirthDate { get; set; }
            public string PhoneNumber { get; set; }
            public string Address { get; set; }
            public string PassportType { get; set; }
            public string PassportNumber { get; set; }
            public string PassportStartDate { get; set; }
            public string PassportEndDate { get; set; }
            public string BloodType { get; set; }
            public string VaccineStatus { get; set; }
            public string HealthStatus { get; set; }
            public string AgeRange { get; set; }
            public string VisaRequest { get; set; }
            public string TicketRequest { get; set; }
            public string CargoRequest { get; set; }
            public string RoomType { get; set; }
            public string DepartureCity { get; set; }
            public string HasTransfer { get; set; }
            public string TransferCity { get; set; }
            public string District { get; set; }
            public string TotalPrice { get; set; }
            public string Currency { get; set; }
            public string PaidAmount { get; set; }
            public string RemainingDebt { get; set; }
            public string Reference { get; set; }
        }
    }
}
