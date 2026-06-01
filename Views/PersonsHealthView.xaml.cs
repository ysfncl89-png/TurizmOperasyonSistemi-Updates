using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Collections.Generic;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsHealthView : Page
    {
        private List<Person> _all;
        public List<string> BloodOptions { get; private set; }

        public PersonsHealthView()
        {
            InitializeComponent();
            LoadAll();
            Loaded += PersonsHealthView_Loaded;
            LoadBloodOptions();
            // subscribe to notifications in case PersonsWizardView updates underlying data
            Services.NotificationCenter.Subscribe(OnNotification);
        }

        private void OnNotification(string key, object payload)
        {
            if (key == "PersonUpdated")
            {
                // reload local list
                LoadAll();
            }
        }

        private void LoadBloodOptions()
        {
            using (var db = new AppDbContext())
            {
                BloodOptions = db.AppSettings.Where(x => x.Type == "Kan Grubu" && x.IsActive).OrderBy(x => x.SortOrder).Select(x => x.Value).ToList();
            }
            BloodOptions.Insert(0, "Seçiniz");
        }

        private void PersonsHealthView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchTextBox.Focus();
            }
            catch
            {
                // ignore
            }
        }

        private void LoadAll()
        {
            using (var db = new AppDbContext())
            {
                _all = db.Persons.OrderByDescending(x => x.Id).ToList();
                ResultsDataGrid.ItemsSource = _all;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = (SearchTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultsDataGrid.ItemsSource = _all;
                SearchHint.Visibility = Visibility.Visible;
                return;
            }

            SearchHint.Visibility = Visibility.Collapsed;

            // Normalize input for comparisons
            var q = text.ToLowerInvariant();

            // If input looks like a blood type (A, B, AB, O with optional + or -)
            bool isBlood = false;
            var t = q.Replace(" ", "");
            var bloodCandidates = new[] { "a", "b", "ab", "o" };
            if (t.Length >= 1 && t.Length <= 3)
            {
                var basePart = t;
                string sign = null;
                if (t.EndsWith("+") || t.EndsWith("-"))
                {
                    sign = t.Substring(t.Length - 1);
                    basePart = t.Substring(0, t.Length - 1);
                }

                if (bloodCandidates.Contains(basePart) && (sign == null || sign == "+" || sign == "-"))
                {
                    isBlood = true;
                    // normalize display form like "b+" -> "B+"
                    var normalized = (basePart + (sign ?? string.Empty)).ToUpperInvariant().Replace(" ", string.Empty);
                    ResultsDataGrid.ItemsSource = _all.Where(p => !string.IsNullOrWhiteSpace(p.BloodType)
                                                                 && (p.BloodType ?? string.Empty).ToUpperInvariant().Replace(" ", string.Empty).Contains(normalized))
                                                         .Select(p => new { p.FirstName, p.LastName, BloodType = p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
                    return;
                }
            }

            // If contains letters and likely a disease/vaccine term
            // We'll exclude entries where the field is empty, "yok" or "belirtmedi" (turkish variations)
            var excludeValues = new[] { "", "yok", "belirtmedi", "belirtmedi.", "-" };

            var diseaseMatches = _all.Where(p => !string.IsNullOrWhiteSpace(p.HealthStatus)
                                                 && !excludeValues.Contains(p.HealthStatus.Trim().ToLowerInvariant())
                                                 && p.HealthStatus.ToLowerInvariant().Contains(q)).ToList();

            if (diseaseMatches.Any())
            {
                ResultsDataGrid.ItemsSource = diseaseMatches.Select(p => new { p.FirstName, p.LastName, p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
                return;
            }

            var vaccineMatches = _all.Where(p => !string.IsNullOrWhiteSpace(p.VaccineStatus)
                                                 && !excludeValues.Contains(p.VaccineStatus.Trim().ToLowerInvariant())
                                                 && p.VaccineStatus.ToLowerInvariant().Contains(q)).ToList();

            if (vaccineMatches.Any())
            {
                ResultsDataGrid.ItemsSource = vaccineMatches.Select(p => new { p.FirstName, p.LastName, p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
                return;
            }

            // fallback: try full-text match across the three fields
            var fallback = _all.Where(p => (p.BloodType ?? string.Empty).ToLowerInvariant().Contains(q)
                                           || (p.HealthStatus ?? string.Empty).ToLowerInvariant().Contains(q)
                                           || (p.VaccineStatus ?? string.Empty).ToLowerInvariant().Contains(q)).ToList();
            ResultsDataGrid.ItemsSource = fallback.Select(p => new { p.FirstName, p.LastName, p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
        }

        private void ResultsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Commit edit first
            if (e.EditAction != DataGridEditAction.Commit) return;

            var row = e.Row.Item as Person;
            // when ItemsSource is anonymous list we need to map back by TC or name
            if (row == null)
            {
                // try to extract Tc from row cells
                dynamic dyn = e.Row.Item;
                string tc = dyn?.TcKimlikNo;
                if (string.IsNullOrWhiteSpace(tc)) return;
                using (var db = new AppDbContext())
                {
                    var person = db.Persons.FirstOrDefault(p => p.TcKimlikNo == tc);
                    if (person == null) return;
                    // map edited cell back by column
                    var col = e.Column.Header?.ToString();
                    if (col == "Hastalık Durumu")
                    {
                        var tb = e.EditingElement as TextBox;
                        person.HealthStatus = string.IsNullOrWhiteSpace(tb?.Text) ? "Belirtmedi" : tb.Text;
                    }
                    else if (col == "Aşı Durumu")
                    {
                        var tb = e.EditingElement as TextBox;
                        person.VaccineStatus = string.IsNullOrWhiteSpace(tb?.Text) ? "Belirtmedi" : tb.Text;
                    }
                    else if (col == "Kan Grubu")
                    {
                        var cb = e.EditingElement as ComboBox;
                        var val = cb?.SelectedValue as string;
                        person.BloodType = string.IsNullOrWhiteSpace(val) || string.Equals(val, "Seçiniz", System.StringComparison.OrdinalIgnoreCase) ? "Belirtmedi" : val;
                    }

                    db.SaveChanges();
                    Services.NotificationCenter.Publish("PersonUpdated", person.Id);
                    LoadAll();
                }
            }
            else
            {
                // editing when ItemsSource was real Person list
                using (var db = new AppDbContext())
                {
                    var person = db.Persons.FirstOrDefault(p => p.TcKimlikNo == row.TcKimlikNo);
                    if (person == null) return;
                    // columns already bound two-way => values copied
                    person.BloodType = string.IsNullOrWhiteSpace(row.BloodType) ? "Belirtmedi" : row.BloodType;
                    person.HealthStatus = string.IsNullOrWhiteSpace(row.HealthStatus) ? "Belirtmedi" : row.HealthStatus;
                    person.VaccineStatus = string.IsNullOrWhiteSpace(row.VaccineStatus) ? "Belirtmedi" : row.VaccineStatus;
                    db.SaveChanges();
                    Services.NotificationCenter.Publish("PersonUpdated", person.Id);
                    LoadAll();
                }
            }
        }

        private void ResultsDataGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // allow double-clicks on Kan Grubu column to begin edit (handled in MouseDoubleClick)
        }

        private void ResultsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is DataGridCell))
            {
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell)
            {
                var column = cell.Column;
                if (column != null && column.Header != null && column.Header.ToString() == "Kan Grubu")
                {
                    // begin edit for the row
                    var row = DataGridRow.GetRowContainingElement(cell);
                    if (row != null)
                    {
                        ResultsDataGrid.BeginEdit();
                    }
                }
            }
        }
    }
}
