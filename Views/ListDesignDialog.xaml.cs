using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class ListDesignDialog : Window
    {
        private readonly ObservableCollection<ColumnOption> _columns;
        private readonly string _modeName;
        private readonly ListTemplateStore _store = new ListTemplateStore();
        private DataGrid _targetGrid;
        private bool _isDragging;
        private object _draggedItem;

        public ListDesignDialog(IEnumerable<DataGridColumn> columns, string modeName = null, DataGrid targetGrid = null)
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
                    var isVisible = c.Visibility == Visibility.Visible;
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
                if (string.IsNullOrWhiteSpace(_modeName))
                {
                    MessageBox.Show("Şablon kaydetmek için hangi modda olduğunuz bilinmiyor.");
                    return;
                }

                SyncUiToModel();
                var list = _columns.OrderBy(c => c.Order)
                                   .Select(c => new ListTemplateStore.ColumnDto { Title = c.Title, Order = c.Order, IsVisible = c.IsVisible })
                                   .ToList();
                _store.SaveTemplate(_modeName, list);

                if (_targetGrid != null)
                {
                    ApplyToTargetGrid(list);
                    try { _targetGrid.Items.Refresh(); } catch { }
                }

                MessageBox.Show("Şablon kaydedildi.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Şablon kaydetme sırasında hata: " + ex.Message);
            }
        }

        private void SyncUiToModel()
        {
            try
            {
                foreach (var item in ColumnsItemsControl.Items)
                {
                    var container = ColumnsItemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    if (container == null)
                    {
                        // try to bring into view so container generated
                        ColumnsItemsControl.UpdateLayout();
                        container = ColumnsItemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    }
                    if (container == null) continue;
                    if (container.DataContext is ColumnOption dataCtx)
                    {
                        var cb = FindVisualChild<CheckBox>(container);
                        if (cb != null)
                        {
                            dataCtx.IsVisible = cb.IsChecked == true;
                        }
                    }
                }
            }
            catch { }
        }

        private static TChild FindVisualChild<TChild>(DependencyObject obj) where TChild : DependencyObject
        {
            if (obj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is TChild correctlyTyped) return correctlyTyped;
                var result = FindVisualChild<TChild>(child);
                if (result != null) return result;
            }
            return null;
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
                        // align existing _columns with saved template
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
            try
            {
                if (!string.IsNullOrWhiteSpace(_modeName))
                {
                    SyncUiToModel();
                    var list = _columns.OrderBy(c => c.Order)
                                       .Select(c => new ListTemplateStore.ColumnDto { Title = c.Title, Order = c.Order, IsVisible = c.IsVisible })
                                       .ToList();
                    _store.SaveTemplate(_modeName, list);
                    if (_targetGrid != null)
                    {
                        ApplyToTargetGrid(list);
                        try { _targetGrid.Items.Refresh(); } catch { }
                    }
                }
            }
            catch { }
            DialogResult = true;
        }

        private void ApplyToTargetGrid(List<ListTemplateStore.ColumnDto> list)
        {
            // Normalize comparison to be robust against whitespace/punctuation
            string Norm(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                var t = s.Trim().ToLowerInvariant();
                var sb = new StringBuilder();
                foreach (var ch in t)
                {
                    if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)) sb.Append(ch);
                }
                return sb.ToString().Replace(" ", string.Empty);
            }

            var ordered = list.OrderBy(o => o.Order).ToList();
            for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
            {
                var opt = ordered[targetIndex];
                var optKey = Norm(opt.Title);
                // helper to robustly extract header text when Header may be string, TextBlock, etc.
                string GetHeaderText(DataGridColumn c)
                {
                    if (c == null) return string.Empty;
                    if (c.Header == null) return string.Empty;
                    if (c.Header is string s) return s;
                    if (c.Header is TextBlock tb) return tb.Text ?? string.Empty;
                    try { return c.Header.ToString(); } catch { return string.Empty; }
                }

                // 1) try exact header string match (case-insensitive, trimmed)
                var col = _targetGrid.Columns.FirstOrDefault(c => string.Equals(GetHeaderText(c).Trim(), opt.Title?.Trim(), StringComparison.OrdinalIgnoreCase));
                // 2) try normalized match
                if (col == null) col = _targetGrid.Columns.FirstOrDefault(c => Norm(GetHeaderText(c)) == optKey);
                if (col == null)
                {
                    // try contains match as fallback
                    col = _targetGrid.Columns.FirstOrDefault(c => Norm(GetHeaderText(c)).Contains(optKey) || optKey.Contains(Norm(GetHeaderText(c))));
                }
                if (col == null) continue;
                col.DisplayIndex = targetIndex;
                col.Visibility = opt.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            try
            {
                _targetGrid.Dispatcher.Invoke(() => { _targetGrid.UpdateLayout(); });
            }
            catch { }
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
        private void ColumnsItemsControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _draggedItem = null;
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is ContentPresenter))
            {
                src = VisualTreeHelper.GetParent(src);
            }
            if (src is ContentPresenter cp)
            {
                _draggedItem = cp.Content;
            }
        }

        private void ColumnsItemsControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
            {
                _isDragging = true;
                try
                {
                    DragDrop.DoDragDrop(ColumnsItemsControl, _draggedItem, DragDropEffects.Move);
                }
                finally
                {
                    _isDragging = false;
                    _draggedItem = null;
                }
            }
        }

        private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // handled by container-level preview
        }

        private void Item_MouseMove(object sender, MouseEventArgs e)
        {
            // container-level mouse move handles starting drag
        }

        private void Item_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            // Auto-scroll parent ScrollViewer when dragging near edges so items can be moved beyond visible area
            try
            {
                // find enclosing ScrollViewer
                DependencyObject cur = sender as DependencyObject;
                ScrollViewer sv = null;
                while (cur != null)
                {
                    if (cur is ScrollViewer s) { sv = s; break; }
                    cur = VisualTreeHelper.GetParent(cur);
                }
                if (sv == null)
                {
                    // fallback: search up from ColumnsItemsControl
                    var parent = ColumnsItemsControl as DependencyObject;
                    while (parent != null)
                    {
                        if (parent is ScrollViewer ss) { sv = ss; break; }
                        parent = VisualTreeHelper.GetParent(parent);
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

        private void Item_Drop(object sender, DragEventArgs e)
        {
            try
            {
                var source = e.Data.GetData(typeof(ColumnOption)) as ColumnOption ?? e.Data.GetData(typeof(object)) as ColumnOption;
                // find target item
                var cp = sender as ContentPresenter;
                ColumnOption target = null;
                if (cp != null) target = cp.Content as ColumnOption;
                if (target == null)
                {
                    // try to find DataContext
                    if (sender is FrameworkElement fe) target = fe.DataContext as ColumnOption;
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
