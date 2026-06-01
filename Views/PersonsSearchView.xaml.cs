using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsSearchView : Page
    {
        private System.ComponentModel.ICollectionView _personsView;

        public PersonsSearchView()
        {
            InitializeComponent();
            LoadPersons();
        }

        private void PersonsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // find the DataGridCell that was double-clicked
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is DataGridCell))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell)
            {
                var column = cell.Column;
                if (column != null && column.Header != null && column.Header.ToString() == "Telefon")
                {
                    // find owning row
                    var row = ItemsControl.ContainerFromElement(PersonsDataGrid, cell) as DataGridRow;
                    if (row == null)
                    {
                        // try walking up to find row
                        var parent = VisualTreeHelper.GetParent(cell);
                        while (parent != null && !(parent is DataGridRow))
                        {
                            parent = VisualTreeHelper.GetParent(parent);
                        }
                        row = parent as DataGridRow;
                    }

                    if (row?.Item is Person person)
                    {
                        var dlg = new EditPhoneWindow(person.Id, person.PhoneNumber);
                        dlg.Owner = Application.Current?.MainWindow;
                        var res = dlg.ShowDialog();
                        if (res == true)
                        {
                            LoadPersons();
                        }
                    }
                }
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _personsView?.Refresh();
            if (SearchHint == null) return;
            SearchHint.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadPersons()
        {
            using (var db = new AppDbContext())
            {
                var list = db.Persons
                    .OrderByDescending(x => x.Id)
                    .ToList();

                PersonsDataGrid.ItemsSource = list;
                _personsView = System.Windows.Data.CollectionViewSource.GetDefaultView(PersonsDataGrid.ItemsSource);
                _personsView.Filter = FilterPersons;
            }
        }

        private bool FilterPersons(object obj)
        {
            if (!(obj is Person person)) return false;
            var text = Normalize(SearchTextBox.Text);
            if (string.IsNullOrWhiteSpace(text)) return true;
            var fn = Normalize(person.FirstName);
            var ln = Normalize(person.LastName);
            var tc = Normalize(person.TcKimlikNo);
            var full = (fn + " " + ln).Trim();

            // also support phone searches: strip non-digit chars for comparison
            var phoneDigits = string.Empty;
            if (!string.IsNullOrWhiteSpace(person.PhoneNumber))
            {
                phoneDigits = new string(person.PhoneNumber.Where(char.IsDigit).ToArray());
            }
            var searchDigits = new string(text.Where(char.IsDigit).ToArray());

            bool phoneMatch = false;
            if (!string.IsNullOrWhiteSpace(searchDigits) && !string.IsNullOrWhiteSpace(phoneDigits))
            {
                phoneMatch = phoneDigits.Contains(searchDigits);
            }

            return full.Contains(text) || fn.Contains(text) || ln.Contains(text) || tc.Contains(text) || phoneMatch;
        }

        private string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return string.Join(" ", s.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }


    }
}
