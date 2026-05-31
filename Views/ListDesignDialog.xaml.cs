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

        public ListDesignDialog(IEnumerable<object> rows)
        {
            InitializeComponent();

            _columns = new ObservableCollection<ColumnOption>
            {
                new ColumnOption("Kişi", 1, true),
                new ColumnOption("TC", 2, true),
                new ColumnOption("Tur Türü", 3, true),
                new ColumnOption("Dönem", 4, true),
                new ColumnOption("Grup Türü", 5, true),
                new ColumnOption("Gün", 6, true),
                new ColumnOption("Şehir", 7, true),
                new ColumnOption("Oda", 8, true),
                new ColumnOption("Durum", 9, true),
                new ColumnOption("Tarih", 10, true)
            };

            ColumnsItemsControl.ItemsSource = _columns;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private sealed class ColumnOption
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
