using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Services;
using System.Windows.Controls;

namespace TurizmOperasyonSistemi.Views
{
    public partial class ListDesignDialog : Window
    {
        private readonly ObservableCollection<ColumnOption> _columns;
        private readonly string _modeName;
        private readonly Services.ListTemplateStore _store = new Services.ListTemplateStore();
        private System.Windows.Controls.DataGrid _targetGrid;
        private bool _isDragging;
        private object _draggedItem;

        public ListDesignDialog(IEnumerable<System.Windows.Controls.DataGridColumn> columns, string modeName = null, System.Windows.Controls.DataGrid targetGrid = null)
        {
            InitializeComponent();
            _modeName = modeName;
            _targetGrid = targetGrid;
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

        private void SaveTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Mode name required to save a template
                if (string.IsNullOrWhiteSpace(_modeName))
                {
                    MessageBox.Show("Şablon kaydetmek için hangi modda olduğunuz bilinmiyor.");
                    return;
                }

                var list = _columns.OrderBy(c => c.Order).Select(c => new Services.ListTemplateStore.ColumnDto { Title = c.Title, Order = c.Order, IsVisible = c.IsVisible }).ToList();
                _store.SaveTemplate(_modeName, list);
                // apply to target grid immediately if provided
                try
                {
                    if (_targetGrid != null)
                    {
                        var ordered = list.OrderBy(o => o.Order).ToList();
                        for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
                        {
                            var opt = ordered[targetIndex];
                            var col = _targetGrid.Columns.FirstOrDefault(c => string.Equals(c.Header?.ToString(), opt.Title, StringComparison.OrdinalIgnoreCase));
                            if (col == null) continue;
                            col.DisplayIndex = targetIndex;
                            col.Visibility = opt.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                        }
                        try { _targetGrid.Items.Refresh(); } catch { }
                    }
                }
                catch { }
                MessageBox.Show("Şablon kaydedildi.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Şablon kaydetme sırasında hata: " + ex.Message);
            }
        }

        // Expose the column options so caller can read what the user selected
        public IReadOnlyList<ColumnOption> ColumnOptions => _columns.ToList();

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            try
            {
                if (!string.IsNullOrWhiteSpace(_modeName))
                {
                    var tpl = _store.LoadTemplate(_modeName);
                    if (tpl != null && tpl.Count > 0)
                    {
                        // try to align existing _columns with saved template
                        foreach (var saved in tpl)
                        {
                            var col = _columns.FirstOrDefault(c => string.Equals(c.Title, saved.Title, StringComparison.OrdinalIgnoreCase));
                            if (col != null)
                            {
                                col.IsVisible = saved.IsVisible;
                                col.Order = saved.Order;
                            }
                        }
                        // reorder collection to match saved order
                        var reordered = _columns.OrderBy(c => c.Order).ToList();
                        _columns.Clear();
                        int i = 1;
                        foreach (var c in reordered)
                        {
                            c.Order = i++;
                            _columns.Add(c);
                        }
                    }
                }
            }
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // persist template on apply as convenience
            try
            {
                if (!string.IsNullOrWhiteSpace(_modeName))
                {
                    var list = _columns.OrderBy(c => c.Order).Select(c => new ListTemplateStore.ColumnDto { Title = c.Title, Order = c.Order, IsVisible = c.IsVisible }).ToList();
                    _store.SaveTemplate(_modeName, list);
                }
            }
            catch { }
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
            // Auto-scroll parent ScrollViewer when dragging near edges so items can be moved beyond visible area
            try
            {
                // find enclosing ScrollViewer
                DependencyObject cur = sender as DependencyObject;
                ScrollViewer sv = null;
                while (cur != null)
                {
                    if (cur is ScrollViewer s) { sv = s; break; }
                    cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
                }
                if (sv == null)
                {
                    // fallback: search up from ColumnsItemsControl
                    var parent = ColumnsItemsControl as DependencyObject;
                    while (parent != null)
                    {
                        if (parent is System.Windows.Controls.ScrollViewer ss) { sv = ss; break; }
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    }
                }
                if (sv != null)
                {
                    var pos = e.GetPosition(sv);
                    const double margin = 40.0;
                    const double step = 10.0;
                    if (pos.Y < margin)
                    {
                        sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset - step));
                    }
                    else if (pos.Y > sv.ActualHeight - margin)
                    {
                        sv.ScrollToVerticalOffset(sv.VerticalOffset + step);
                    }
                }
            }
            catch { }
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
