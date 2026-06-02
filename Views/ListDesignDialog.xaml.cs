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
        private bool _isDragging;
        private object _draggedItem;

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
            // support drag-drop by wiring events
            ColumnsItemsControl.PreviewMouseLeftButtonDown += ColumnsItemsControl_PreviewMouseLeftButtonDown;
            ColumnsItemsControl.MouseMove += ColumnsItemsControl_MouseMove;
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
            public int Order { get; set; }
            public bool IsVisible { get; set; }
        }

        // Drag-drop helpers
        private void ColumnsItemsControl_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDragging = false;
            _draggedItem = null;
            var src = e.OriginalSource as System.Windows.DependencyObject;
            while (src != null && !(src is System.Windows.Controls.ContentPresenter))
            {
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            }
            if (src is System.Windows.Controls.ContentPresenter cp)
            {
                _draggedItem = cp.Content;
            }
        }

        private void ColumnsItemsControl_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
            {
                _isDragging = true;
                try
                {
                    System.Windows.DragDrop.DoDragDrop(ColumnsItemsControl, _draggedItem, System.Windows.DragDropEffects.Move);
                }
                finally
                {
                    _isDragging = false;
                    _draggedItem = null;
                }
            }
        }

        private void Item_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // handled by container-level preview
        }

        private void Item_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // container-level mouse move handles starting drag
        }

        private void Item_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }

        private void Item_Drop(object sender, System.Windows.DragEventArgs e)
        {
            try
            {
                var source = e.Data.GetData(typeof(ColumnOption)) as ColumnOption ?? e.Data.GetData(typeof(object)) as ColumnOption;
                // find target item
                var cp = sender as System.Windows.Controls.ContentPresenter;
                ColumnOption target = null;
                if (cp != null) target = cp.Content as ColumnOption;
                if (target == null)
                {
                    // try to find DataContext
                    if (sender is System.Windows.FrameworkElement fe) target = fe.DataContext as ColumnOption;
                }

                if (source == null || target == null || ReferenceEquals(source, target)) return;

                var srcIndex = _columns.IndexOf(source);
                var tgtIndex = _columns.IndexOf(target);
                if (srcIndex < 0 || tgtIndex < 0) return;

                _columns.Move(srcIndex, tgtIndex);
                // reassign order values
                for (int i = 0; i < _columns.Count; i++) _columns[i].Order = i + 1;
            }
            catch { }
        }
    }
}
