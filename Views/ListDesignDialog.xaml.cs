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
using System.ComponentModel;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class ListDesignDialog : Window
    {
        public bool WasSaved { get; private set; } = false;
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
                    // prefer stable key from binding path when possible (e.g., FirstName/LastName)
                    string key = string.Empty;
                    try
                    {
                        if (c is DataGridBoundColumn bound)
                        {
                            // try get binding path
                            if (bound.Binding is System.Windows.Data.Binding b && b.Path != null)
                            {
                                key = (b.Path.Path ?? string.Empty).Trim().ToLowerInvariant();
                            }
                        }
                    }
                    catch { }
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        // generate a stable key based on original header text
                        string MakeKey(string s)
                        {
                            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                            var t = s.Trim().ToLowerInvariant();
                            var sb = new StringBuilder();
                            foreach (var ch in t)
                            {
                                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                            }
                            return sb.ToString();
                        }
                        key = MakeKey(title);
                    }
                    // To ensure a stable unique key when multiple columns normalize to same key,
                    // append index when duplicate key already exists in _columns.
                    if (_columns.Any(c2 => string.Equals(c2.Key, key, StringComparison.OrdinalIgnoreCase)))
                    {
                        key = key + "_" + order.ToString();
                    }
                    _columns.Add(new ColumnOption(title, order, isVisible, key));
                    order++;
                }
            }

            ColumnsItemsControl.ItemsSource = _columns;

            // expose whether edit button should be visible (only for Havayolu mode)
            EditButtonVisibility = string.Equals(_modeName, "Havayolu", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

            // set window-level property as DataContext for binding isn't needed because we bind RelativeSource=Window

            // support drag-drop by wiring events
            ColumnsItemsControl.PreviewMouseLeftButtonDown += ColumnsItemsControl_PreviewMouseLeftButtonDown;
            ColumnsItemsControl.MouseMove += ColumnsItemsControl_MouseMove;
        }

        public Visibility EditButtonVisibility { get; private set; }

        private void EditTitle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button btn)) return;
                if (!(btn.DataContext is ColumnOption opt)) return;

                var oldTitle = opt.Title;
                var dlg = new EditTitleDialog(oldTitle) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    var newTitle = dlg.EditedTitle ?? oldTitle;

                    // if there is a target grid, try to update the actual column header so Apply/Save can match
                    try
                    {
                        if (_targetGrid != null)
                        {
                            DataGridColumn GetMatchingColumn()
                            {
                                string GetHeaderText(DataGridColumn c)
                                {
                                    if (c == null) return string.Empty;
                                    if (c.Header == null) return string.Empty;
                                    if (c.Header is string s) return s;
                                    if (c.Header is TextBlock tb) return tb.Text ?? string.Empty;
                                    try { return c.Header.ToString(); } catch { return string.Empty; }
                                }
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

                                var exact = _targetGrid.Columns.FirstOrDefault(c => string.Equals(GetHeaderText(c).Trim(), oldTitle?.Trim(), StringComparison.OrdinalIgnoreCase));
                                if (exact != null) return exact;
                                var normKey = Norm(oldTitle);
                                var norm = _targetGrid.Columns.FirstOrDefault(c => Norm(GetHeaderText(c)) == normKey);
                                if (norm != null) return norm;
                                var contains = _targetGrid.Columns.FirstOrDefault(c => Norm(GetHeaderText(c)).Contains(normKey) || normKey.Contains(Norm(GetHeaderText(c))));
                                return contains;
                            }

                            var col = GetMatchingColumn();
                            if (col != null)
                            {
                                var stableKey = TryGetStableColumnKey(col);
                                if (!string.IsNullOrWhiteSpace(stableKey))
                                {
                                    opt.Key = stableKey;
                                }
                                col.Header = newTitle;
                                try { _targetGrid.Items.Refresh(); _targetGrid.UpdateLayout(); } catch { }
                            }
                        }
                    }
                    catch { }

                    opt.Title = newTitle;
                }
            }
            catch { }
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
                                   .Select(c => new ListTemplateStore.ColumnDto { Key = c.Key, Title = c.Title, Order = c.Order, IsVisible = c.IsVisible })
                                   .ToList();
                try { FixTemplateKeysFromTargetGrid(list); } catch { }
                _store.SaveTemplate(_modeName, list);

                if (_targetGrid != null)
                {
                    ApplyToTargetGrid(list);
                    try { _targetGrid.Items.Refresh(); } catch { }
                }

                // mark that a persistent save occurred so caller can react
                WasSaved = true;
                var mbRes = MessageBox.Show("Şablon kaydedildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                if (mbRes == MessageBoxResult.OK)
                {
                    // close the dialog after user acknowledges the message
                    try { this.DialogResult = true; } catch { this.Close(); }
                }
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
                        // align existing _columns with saved template using Key when available
                        foreach (var saved in tpl)
                        {
                            ColumnOption col = null;
                            if (!string.IsNullOrWhiteSpace(saved.Key))
                            {
                                col = _columns.FirstOrDefault(c => string.Equals(c.Key, saved.Key, StringComparison.OrdinalIgnoreCase));
                            }
                            if (col == null)
                            {
                                col = _columns.FirstOrDefault(c => string.Equals(c.Title, saved.Title, StringComparison.OrdinalIgnoreCase));
                            }
                            if (col != null)
                            {
                                // update title from saved (preserve edited title)
                                try { col.Title = saved.Title ?? col.Title; } catch { }
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
                // Apply changes to the target grid for the current session only.
                // Do NOT persist to the ListTemplateStore here so the change remains temporary
                // until the user leaves the PersonsGeneralView module.
                if (!string.IsNullOrWhiteSpace(_modeName))
                {
                    SyncUiToModel();
                    var list = _columns.OrderBy(c => c.Order)
                                       .Select(c => new ListTemplateStore.ColumnDto { Key = c.Key, Title = c.Title, Order = c.Order, IsVisible = c.IsVisible })
                                       .ToList();
                    try { FixTemplateKeysFromTargetGrid(list); } catch { }
                    // Intentionally do NOT call _store.SaveTemplate(...) here to avoid making the template persistent.
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
            var matchedCols = new HashSet<DataGridColumn>();
            for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
            {
                var opt = ordered[targetIndex];
                // prefer stored Key for stable matching when available, otherwise normalize title
                var optKey = !string.IsNullOrWhiteSpace(opt.Key) ? opt.Key.Trim().ToLowerInvariant() : Norm(opt.Title);
                // helper to robustly extract header text when Header may be string, TextBlock, etc.
                string GetHeaderText(DataGridColumn c)
                {
                    if (c == null) return string.Empty;
                    if (c.Header == null) return string.Empty;
                    if (c.Header is string s) return s;
                    if (c.Header is TextBlock tb) return tb.Text ?? string.Empty;
                    try { return c.Header.ToString(); } catch { return string.Empty; }
                }
                DataGridColumn col = null;
                try
                {
                    // 1) if opt.Key present, try to match DataGridBoundColumn binding path first
                    if (!string.IsNullOrWhiteSpace(opt.Key))
                    {
                        foreach (var c in _targetGrid.Columns)
                        {
                            if (c is DataGridBoundColumn dbc && dbc.Binding is System.Windows.Data.Binding b && b.Path != null)
                            {
                                var p = (b.Path.Path ?? string.Empty).Trim().ToLowerInvariant();
                                if (p == optKey)
                                {
                                    col = c; break;
                                }
                            }
                        }
                    }

                    // 2) try exact header string match (case-insensitive, trimmed)
                    if (col == null) col = _targetGrid.Columns.FirstOrDefault(c => string.Equals(GetHeaderText(c).Trim(), opt.Title?.Trim(), StringComparison.OrdinalIgnoreCase));
                    // 3) try normalized match against header
                    if (col == null) col = _targetGrid.Columns.FirstOrDefault(c => Norm(GetHeaderText(c)) == optKey);
                    // 4) contains match fallback
                    if (col == null) col = _targetGrid.Columns.FirstOrDefault(c => Norm(GetHeaderText(c)).Contains(optKey) || optKey.Contains(Norm(GetHeaderText(c))));
                }
                catch { }
                if (col == null) continue;
                if (matchedCols.Contains(col)) continue; // avoid assigning same physical column multiple times
                // update header to the new title so edited titles become visible in the grid
                try
                {
                    if (col.Header is TextBlock htb)
                    {
                        htb.Text = opt.Title ?? string.Empty;
                    }
                    else
                    {
                        col.Header = opt.Title ?? string.Empty;
                    }
                }
                catch { }

                col.DisplayIndex = targetIndex;
                col.Visibility = opt.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                matchedCols.Add(col);
            }
            try
            {
                _targetGrid.Dispatcher.Invoke(() => { _targetGrid.UpdateLayout(); });
            }
            catch { }
        }

        private string TryGetStableColumnKey(DataGridColumn col)
        {
            try
            {
                if (col is DataGridBoundColumn dbc && dbc.Binding is System.Windows.Data.Binding b && b.Path != null)
                {
                    var path = (b.Path.Path ?? string.Empty).Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(path)) return path;
                }

                if (col is DataGridTemplateColumn dtc)
                {
                    var path = GetBindingPathFromTemplate(dtc.CellTemplate) ?? GetBindingPathFromTemplate(dtc.CellEditingTemplate);
                    if (!string.IsNullOrWhiteSpace(path)) return path.Trim().ToLowerInvariant();
                }
            }
            catch { }

            try
            {
                var header = (col?.Header as string) ?? col?.Header?.ToString();
                if (!string.IsNullOrWhiteSpace(header))
                {
                    var normalized = NormalizeStableKey(header);
                    switch (normalized)
                    {
                        case "sira": return "sequence";
                        case "ad": return "firstname";
                        case "soyad": return "lastname";
                        case "tckimlik": return "tckimlikno";
                        case "cinsiyet": return "gender";
                        case "dogumtarihi": return "birthdate";
                        case "telefonnumarasi": return "phonenumber";
                        case "adres": return "address";
                        case "pasaporttipi": return "passporttype";
                        case "pasaportnumarasi": return "passportnumber";
                        case "pasaportbaslangictarihi": return "passportstartdate";
                        case "pasaportbitistarihi": return "passportenddate";
                        case "kangrubu": return "kangrubu";
                        case "asidurumu": return "vaccinestatus";
                        case "hastalikdurumu": return "healthstatus";
                        case "yasaraligi": return "agerange";
                        case "vizetalebi": return "vizetalebi";
                        case "uyruk": return "nationality";
                        case "bilettalebi": return "bilettalebi";
                        case "kargotalebi": return "kargotalebi";
                        case "odatipi": return "roomtype";
                        case "katilimyapilacaksehir":
                        case "city": return "departurecity";
                        case "aktarmavarmi":
                        case "istransfer": return "hastransfer";
                        case "aktarmasehri":
                        case "transfercity": return "transfercity";
                        case "ilce":
                        case "district": return "district";
                        case "toplamfiyat": return "totalprice";
                        case "parabirimi": return "parabirimi";
                        case "odenen": return "paidamount";
                        case "kalan": return "remainingdebt";
                        case "referans": return "reference";
                    }
                    if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
                }
            }
            catch { }

            return string.Empty;
        }

        private string NormalizeStableKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in text.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            }
            return sb.ToString();
        }

        private string GetBindingPathFromTemplate(DataTemplate template)
        {
            try
            {
                if (template == null) return null;
                var content = template.LoadContent();
                if (content is TextBlock tb)
                {
                    if (tb.GetBindingExpression(TextBlock.TextProperty) is System.Windows.Data.BindingExpression be && be.ParentBinding?.Path != null)
                        return be.ParentBinding.Path.Path;
                }
                if (content is DependencyObject dob)
                {
                    var path = FindBindingPathInVisualTree(dob);
                    if (!string.IsNullOrWhiteSpace(path)) return path;
                }
            }
            catch { }
            return null;
        }

        private string FindBindingPathInVisualTree(DependencyObject obj)
        {
            if (obj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is FrameworkElement fe)
                {
                    foreach (var dp in new[] { TextBlock.TextProperty, ComboBox.SelectedValueProperty, ComboBox.TextProperty })
                    {
                        try
                        {
                            if (fe.GetBindingExpression(dp) is System.Windows.Data.BindingExpression be && be.ParentBinding?.Path != null)
                                return be.ParentBinding.Path.Path;
                        }
                        catch { }
                    }
                }
                var found = FindBindingPathInVisualTree(child);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
            return null;
        }

        private void EnsureSavedTemplateKeys(List<ListTemplateStore.ColumnDto> list)
        {
            if (list == null || _targetGrid == null) return;
            try
            {
                bool changed = false;
                string NormLocal(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                    var t = s.Trim().ToLowerInvariant();
                    var sb = new StringBuilder();
                    foreach (var ch in t) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                    return sb.ToString();
                }

                foreach (var item in list)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key)) continue;
                    // try to find matching column by title and extract binding path
                    var col = _targetGrid.Columns.FirstOrDefault(c => string.Equals((c.Header as string) ?? c.Header?.ToString(), item.Title, StringComparison.OrdinalIgnoreCase));
                    if (col == null)
                    {
                        var key = NormLocal(item.Title ?? string.Empty);
                        col = _targetGrid.Columns.FirstOrDefault(c => NormLocal((c.Header as string) ?? c.Header?.ToString()) == key);
                    }
                    if (col is DataGridBoundColumn dbc)
                    {
                        if (dbc.Binding is System.Windows.Data.Binding b && b.Path != null)
                        {
                            item.Key = (b.Path.Path ?? string.Empty).Trim().ToLowerInvariant();
                            changed = true;
                        }
                        else
                        {
                            // if no binding path but we have a header that seems stable, use normalized header as fallback key
                            item.Key = NormLocal(item.Title ?? string.Empty);
                            changed = true;
                        }
                    }
                    else if (col is DataGridTemplateColumn dtc)
                    {
                        // attempt to extract binding path from template if possible
                        try
                        {
                            var path = GetBindingPathFromTemplate(dtc.CellTemplate) ?? GetBindingPathFromTemplate(dtc.CellEditingTemplate);
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                item.Key = path.Trim().ToLowerInvariant();
                                changed = true;
                            }
                            else
                            {
                                item.Key = NormLocal(item.Title ?? string.Empty);
                                changed = true;
                            }
                        }
                        catch { item.Key = NormLocal(item.Title ?? string.Empty); changed = true; }
                    }
                }
                if (changed)
                {
                    _store.SaveTemplate(_modeName, list);
                }
            }
            catch { }
        }

        public sealed class ColumnOption : INotifyPropertyChanged
        {
            private string _title;
            private int _order;
            private bool _isVisible;
            private string _key;

            public ColumnOption(string title, int order, bool isVisible, string key = null)
            {
                _title = title;
                _order = order;
                _isVisible = isVisible;
                _key = key ?? string.Empty;
            }

            public string Title
            {
                get => _title;
                set
                {
                    if (_title == value) return;
                    _title = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                }
            }

            public int Order
            {
                get => _order;
                set
                {
                    if (_order == value) return;
                    _order = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
                }
            }

            public bool IsVisible
            {
                get => _isVisible;
                set
                {
                    if (_isVisible == value) return;
                    _isVisible = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public string Key
            {
                get => _key;
                set => _key = value ?? string.Empty;
            }
        }

        private void FixTemplateKeysFromTargetGrid(List<ListTemplateStore.ColumnDto> list)
        {
            if (list == null || _targetGrid == null) return;
            try
            {
                foreach (var item in list)
                {
                    var col = _targetGrid.Columns.FirstOrDefault(c =>
                        string.Equals((c.Header as string) ?? c.Header?.ToString(), item.Title, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals((c.Header as string) ?? c.Header?.ToString(), item.Title?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (col == null) continue;

                    var key = TryGetStableColumnKey(col);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        item.Key = key;
                    }
                }
            }
            catch { }
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
