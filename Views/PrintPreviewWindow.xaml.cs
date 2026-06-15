using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    // Converter: get initials from full name (top-level so XAML can reference)
    public class InitialsConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var s = (value as string) ?? string.Empty;
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return string.Empty;
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0].Substring(0,1) + parts[parts.Length-1].Substring(0,1)).ToUpperInvariant();
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    // Converter: deterministic color from name (top-level so XAML can reference)
    public class NameToBrushConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var s = (value as string) ?? string.Empty;
            int hash = 0; foreach (var ch in s) hash = (hash * 31) + ch;
            var r = (byte)(100 + (Math.Abs(hash) % 120));
            var g = (byte)(60 + (Math.Abs(hash/7) % 160));
            var b = (byte)(80 + (Math.Abs(hash/13) % 140));
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r,g,b));
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    public partial class PrintPreviewWindow : Window
    {
        private class GroupViewModel
        {
            public string Key { get; set; }
            public string Title { get; set; }
            public string ItemSummary { get; set; }
            public System.Windows.Media.Brush BorderBrush { get; set; }
            public TurizmOperasyonSistemi.Services.PrintQueueItem[] Items { get; set; }
        }

        private void RemovePerson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Controls.Button btn)) return;
                if (!(btn.DataContext is TurizmOperasyonSistemi.Services.PrintQueueItem it)) return;
                CargoPrintQueue.Instance.Remove(it);
                BindQueueList();
                RefreshPreview();
            }
            catch { }
        }

        private GroupViewModel[] _groups = new GroupViewModel[0];
        private GroupViewModel _selectedGroup = null;
        public PrintPreviewWindow()
        {
            InitializeComponent();
            TemplateCombo.ItemsSource = new[] {
                "2x2 (4)", "2x3 (6)", "2x4 (8)",
                "3x2 (6)", "3x3 (9)", "3x4 (12)", "3x5 (15)", "3x6 (18)",
                "3x7 (21)", "3x8 (24)", "3x9 (27)", "3x10 (30)", "3x11 (33)", "3x12 (36)", "3x13 (39)", "3x14 (42)", "3x15 (45)"
            };
            TemplateCombo.SelectedIndex = 0; // default 2x2
            OrientationCombo.SelectedIndex = 0;
            this.Loaded += (s, e) => RefreshPreview();
            this.Loaded += (s, e) => BindQueueList();
            TemplateCombo.SelectionChanged += (s, e) => RefreshPreview();
            OrientationCombo.SelectionChanged += (s, e) => RefreshPreview();
        }

        // Sender info applied to all labels in this preview session
        public SenderInfo Sender { get; set; }

        private void RefreshPreview()
        {
            try
            {
                // parse selected template like "3x5 (15)" -> cols=3 rows=5
                var sel = TemplateCombo.SelectedItem?.ToString() ?? "3x5 (15)";
                int cols = 3, rows = 5;
                try
                {
                    var parts = sel.Split(new[] { ' ', 'x', '(' }, StringSplitOptions.RemoveEmptyEntries);
                    // expected parts like ["3","5","15)"] but be defensive
                    int c = 3, r = 5;
                    if (parts.Length >= 2 && int.TryParse(parts[0], out c) && int.TryParse(parts[1], out r)) { cols = c; rows = r; }
                }
                catch { }

                var items = CargoPrintQueue.Instance.Items.ToArray();
                // Use selected group if any
                var sourceItems = items;
                if (_selectedGroup != null && _selectedGroup.Items != null && _selectedGroup.Items.Length > 0)
                {
                    sourceItems = _selectedGroup.Items;
                }
                // Use Sender property provided by caller
                var doc = PrintEngine.BuildFixedDocument(sourceItems, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender);
                DocViewer.Document = doc;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Önizleme hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BindQueueList()
        {
            try
            {
                // Group queue items by TourType+Period
                var items = CargoPrintQueue.Instance.Items.ToArray();
                var dict = new Dictionary<string, List<TurizmOperasyonSistemi.Services.PrintQueueItem>>();
                foreach (var it in items)
                {
                    var key = (it.TourType ?? string.Empty) + "|" + (it.Period ?? string.Empty);
                    if (!dict.ContainsKey(key)) dict[key] = new List<TurizmOperasyonSistemi.Services.PrintQueueItem>();
                    dict[key].Add(it);
                }
                var rnd = new Random(1);
                var cols = new List<GroupViewModel>();
                foreach (var kv in dict)
                {
                    var parts = kv.Key.Split('|');
                    var title = string.IsNullOrWhiteSpace(parts[0]) ? "(Bilinmeyen Tur)" : parts[0];
                    if (!string.IsNullOrWhiteSpace(parts.Length > 1 ? parts[1] : null)) title += " - " + parts[1];
                    var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)rnd.Next(60,200), (byte)rnd.Next(60,200), (byte)rnd.Next(60,200)));
                    cols.Add(new GroupViewModel { Key = kv.Key, Title = title, ItemSummary = $"{kv.Value.Count} öğe", BorderBrush = brush, Items = kv.Value.ToArray() });
                }
                _groups = cols.ToArray();
                QueueListBox.ItemsSource = _groups;
                // select first group by default if exists
                if (_groups.Length > 0) { QueueListBox.SelectedIndex = 0; _selectedGroup = _groups[0]; }
            }
            catch { }
        }

        private void QueueListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                _selectedGroup = QueueListBox.SelectedItem as GroupViewModel;
                RefreshPreview();
            }
            catch { }
        }

        private void RemoveQueueItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Controls.Button btn)) return;
                // If button is inside group item template, removal of single item is ambiguous; remove whole group if user clicks group remove
                var gvm = btn.DataContext as GroupViewModel;
                if (gvm != null)
                {
                    // remove all items in group
                    foreach (var qi in gvm.Items) CargoPrintQueue.Instance.Remove(qi);
                    BindQueueList();
                    RefreshPreview();
                    return;
                }
                if (btn.DataContext is TurizmOperasyonSistemi.Services.PrintQueueItem it)
                {
                    CargoPrintQueue.Instance.Remove(it);
                    BindQueueList();
                    RefreshPreview();
                }
            }
            catch { }
        }

        private void RefreshPreviewButton_Click(object sender, RoutedEventArgs e) => RefreshPreview();

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pd = new System.Windows.Controls.PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    var sel = TemplateCombo.SelectedItem?.ToString() ?? "3x5 (15)";
                    int cols = 3, rows = 5;
                    try
                    {
                        var parts = sel.Split(new[] { ' ', 'x', '(' }, StringSplitOptions.RemoveEmptyEntries);
                        int c = 3, r = 5;
                        if (parts.Length >= 2 && int.TryParse(parts[0], out c) && int.TryParse(parts[1], out r)) { cols = c; rows = r; }
                    }
                    catch { }
                    var items = CargoPrintQueue.Instance.Items.ToArray();
                    var sourceItems = items;
                    if (_selectedGroup != null && _selectedGroup.Items != null && _selectedGroup.Items.Length > 0) sourceItems = _selectedGroup.Items;
                    var doc = PrintEngine.BuildFixedDocument(sourceItems, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender);
                    // print
                    pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Kargo Etiketleri");

                    // mark printed for printed group/items
                    foreach (var it in sourceItems) CargoPrintQueue.Instance.MarkPrinted(it);
                    BindQueueList();
                    RefreshPreview();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Yazdırma hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open PrintDialog so user can choose 'Microsoft Print to PDF' as target; print only selected group
                var pd = new System.Windows.Controls.PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    var sel = TemplateCombo.SelectedItem?.ToString() ?? "3x5 (15)";
                    int cols = 3, rows = 5;
                    try
                    {
                        var parts = sel.Split(new[] { ' ', 'x', '(' }, StringSplitOptions.RemoveEmptyEntries);
                        int c = 3, r = 5;
                        if (parts.Length >= 2 && int.TryParse(parts[0], out c) && int.TryParse(parts[1], out r)) { cols = c; rows = r; }
                    }
                    catch { }
                    var items = CargoPrintQueue.Instance.Items.ToArray();
                    var sourceItems = items;
                    if (_selectedGroup != null && _selectedGroup.Items != null && _selectedGroup.Items.Length > 0) sourceItems = _selectedGroup.Items;
                    var doc = PrintEngine.BuildFixedDocument(sourceItems, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender);
                    pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Kargo Etiketleri PDF");
                    foreach (var it in sourceItems) CargoPrintQueue.Instance.MarkPrinted(it);
                    BindQueueList();
                    RefreshPreview();
                }
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
