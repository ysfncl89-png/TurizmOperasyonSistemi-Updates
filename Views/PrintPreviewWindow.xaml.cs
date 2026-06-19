using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PrintPreviewWindow : Window
    {
        // Optional initial group key set by caller so BindQueueList can pre-select the requested group
        public string InitialGroupKey { get; set; }

        private class GroupViewModel
        {
            public string Key { get; set; }
            public string Title { get; set; }
            public string ItemSummary { get; set; }
            public System.Windows.Media.Brush BorderBrush { get; set; }
            public TurizmOperasyonSistemi.Services.PrintQueueItem[] Items { get; set; }
        }

        private GroupViewModel[] _groups = new GroupViewModel[0];
        private GroupViewModel _selectedGroup = null;
        private TurizmOperasyonSistemi.Services.PrintQueueItem[] _currentPreviewItems = new TurizmOperasyonSistemi.Services.PrintQueueItem[0];
        public PrintPreviewWindow()
        {
            InitializeComponent();
            TemplateCombo.ItemsSource = new[] {
                "2x2 (4)", "2x3 (6)", "2x4 (8)",
                "3x2 (6)", "3x3 (9)", "3x4 (12)", "3x5 (15)", "3x6 (18)",
                "3x7 (21)", "3x8 (24)", "3x9 (27)", "3x10 (30)", "3x11 (33)", "3x12 (36)", "3x13 (39)", "3x14 (42)", "3x15 (45)"
            };
            TemplateCombo.SelectedIndex = 5; // default 3x4 (12)
            OrientationCombo.SelectedIndex = 0;
            this.Loaded += (s, e) => RefreshPreview();
            this.Loaded += (s, e) => BindQueueList();
            TemplateCombo.SelectionChanged += (s, e) => RefreshPreview();
            OrientationCombo.SelectionChanged += (s, e) => RefreshPreview();
            // Listen for remove requests originating from generated document buttons
            TurizmOperasyonSistemi.Services.PrintPreviewInteraction.RemoveRequested += PrintPreviewInteraction_RemoveRequested;
            Unloaded += (s, e) => TurizmOperasyonSistemi.Services.PrintPreviewInteraction.RemoveRequested -= PrintPreviewInteraction_RemoveRequested;
        }

        private void PrintPreviewInteraction_RemoveRequested(int registrationId)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var itm = CargoPrintQueue.Instance.Items.FirstOrDefault(x => x.RegistrationId == registrationId);
                        if (itm != null)
                        {
                            // If there are multiple copies queued for this registration, decrement quantity by one
                            // otherwise remove the item completely.
                            try
                            {
                                if (itm.Quantity > 1)
                                {
                                    CargoPrintQueue.Instance.DecrementQuantity(itm, 1);
                                }
                                else
                                {
                                    CargoPrintQueue.Instance.Remove(itm);
                                }
                            }
                            catch
                            {
                                try { CargoPrintQueue.Instance.Remove(itm); } catch { }
                            }
                        }
                        // Refresh groups and preview
                        BindQueueList();
                        RefreshPreview();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        // Sender info applied to all labels in this preview session
        public SenderInfo Sender { get; set; }

        private void RefreshPreview()
        {
            try
            {
                // parse selected template like "3x5 (15)" -> cols=3 rows=5
                var sel = TemplateCombo.SelectedItem?.ToString() ?? "3x4 (12)";
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
                    // base set for this preview = the group's items
                    sourceItems = _selectedGroup.Items;
                }
                // apply preview-only search filter
                var q = (SearchTextBox.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var parts = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    sourceItems = sourceItems.Where(it => {
                        try
                        {
                            var hay = ($"{it.FullName} {it.Tc} {it.Phone} {it.Address}").ToLowerInvariant();
                            return parts.All(p => hay.Contains(p.ToLowerInvariant()));
                        }
                        catch { return false; }
                    }).ToArray();
                }
                _currentPreviewItems = sourceItems;
                // Use Sender property provided by caller
                var doc = PrintEngine.BuildFixedDocument(sourceItems, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender, hideTc: true);
                DocViewer.Document = doc;
                try { DocViewer.FitToWidth(); } catch { }
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
                // Preserve previous selection key (if any) so UI does not jump to another group after rebuild
                // If caller provided InitialGroupKey (from PersonsGeneralView filters), prefer that when no previous selection exists
                var previousKey = _selectedGroup?.Key ?? this.InitialGroupKey;

                // Group queue items by TourType+Period
                var items = CargoPrintQueue.Instance.Items.ToArray();
                var dict = new Dictionary<string, List<TurizmOperasyonSistemi.Services.PrintQueueItem>>();
                foreach (var it in items)
                {
                    var key = string.Join("|", new[] { it.TourType ?? string.Empty, it.Period ?? string.Empty, it.GroupType ?? string.Empty, it.DaysCount ?? string.Empty });
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
                    // append group type and days for clarity when non-matching
                    if (!string.IsNullOrWhiteSpace(parts.Length > 2 ? parts[2] : null)) title += " | " + parts[2];
                    if (!string.IsNullOrWhiteSpace(parts.Length > 3 ? parts[3] : null)) title += " - " + parts[3];
                    var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)rnd.Next(60,200), (byte)rnd.Next(60,200), (byte)rnd.Next(60,200)));
                    // compute total cards by summing Quantity (at least 1 each)
                    int totalCards = 0;
                    try { totalCards = kv.Value.Sum(it => Math.Max(1, it.Quantity)); } catch { totalCards = kv.Value.Count; }
                    cols.Add(new GroupViewModel { Key = kv.Key, Title = title, ItemSummary = $"{totalCards} adet kart", BorderBrush = brush, Items = kv.Value.ToArray() });
                }
                _groups = cols.ToArray();
                QueueListBox.ItemsSource = _groups;

                // Try to restore previous selection if present; otherwise select first group
                var prev = (previousKey ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(prev))
                {
                    var idx = Array.FindIndex(_groups, g => string.Equals((g.Key ?? string.Empty).Trim(), prev, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        QueueListBox.SelectedIndex = idx;
                        _selectedGroup = _groups[idx];
                    }
                    else
                    {
                        // previous group no longer exists (all items removed) -> select first if exists
                        if (_groups.Length > 0) { QueueListBox.SelectedIndex = 0; _selectedGroup = _groups[0]; }
                        else { QueueListBox.SelectedIndex = -1; _selectedGroup = null; }
                    }
                }
                else
                {
                    if (_groups.Length > 0) { QueueListBox.SelectedIndex = 0; _selectedGroup = _groups[0]; }
                }
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

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try { RefreshPreview(); } catch { }
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            try { SearchTextBox.Text = string.Empty; } catch { }
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
                    var sel = TemplateCombo.SelectedItem?.ToString() ?? "3x4 (12)";
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
                    if (_selectedGroup != null && _selectedGroup.Items != null && _selectedGroup.Items.Length > 0) sourceItems = _selectedGroup.Items; // printing uses full group, not preview filter
                    var doc = PrintEngine.BuildFixedDocument(sourceItems, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender, hideTc: true);
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
                    var sel = TemplateCombo.SelectedItem?.ToString() ?? "3x4 (12)";
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
                    if (_selectedGroup != null && _selectedGroup.Items != null && _selectedGroup.Items.Length > 0) sourceItems = _selectedGroup.Items; // export uses full group
                    var doc = PrintEngine.BuildFixedDocument(sourceItems, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender, hideTc: true);
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
