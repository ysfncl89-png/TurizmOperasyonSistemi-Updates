using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class ListDesignDialog : Window
    {
        private readonly ObservableCollection<ColumnOption> _columns;

        public ListDesignDialog(IEnumerable<System.Windows.Controls.DataGridColumn> columns)
        {
            InitializeComponent();
            _columns = new ObservableCollection<ColumnOption>();
            if (columns != null)
            {
                var order = 1;
                foreach (var c in columns)
                {
                    var title = c.Header?.ToString() ?? ("Kolon " + order);
                    var isVisible = c.Visibility == System.Windows.Visibility.Visible;
                    _columns.Add(new ColumnOption(title, order, isVisible));
                    order++;
                }
            }

            ColumnsItemsControl.ItemsSource = _columns;
        }

        // Expose the column options so caller can read what the user selected
        public IReadOnlyList<ColumnOption> ColumnOptions => _columns.ToList();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        public sealed class ColumnOption
        {
            public ColumnOption(string title, int order, bool isVisible)
            {
                Title = title;
                Order = order;
                IsVisible = isVisible;
            }

            public string Title { get; }
            public int Order { get; }
            public bool IsVisible { get; set; }
        }
    }
}
