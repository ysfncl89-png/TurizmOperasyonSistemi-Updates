using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PrintPreviewWindow : Window
    {
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
                // Use Sender property provided by caller
                var doc = PrintEngine.BuildFixedDocument(items, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender);
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
                QueueListBox.ItemsSource = CargoPrintQueue.Instance.Items;
            }
            catch { }
        }

        private void RemoveQueueItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Controls.Button btn)) return;
                if (!(btn.DataContext is PrintQueueItem it)) return;
                CargoPrintQueue.Instance.Remove(it);
                RefreshPreview();
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
                    var doc = PrintEngine.BuildFixedDocument(items, cols, rows, OrientationCombo.SelectedIndex == 1, this.Sender);
                    // print
                    pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Kargo Etiketleri");

                    // mark printed
                    foreach (var it in items) CargoPrintQueue.Instance.MarkPrinted(it);
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
                // Advise user to use Microsoft Print to PDF via PrintDialog
                MessageBox.Show("PDF oluşturmak için 'Yazdır' tuşunu kullanıp hedef yazıcı olarak 'Microsoft Print to PDF' seçin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
