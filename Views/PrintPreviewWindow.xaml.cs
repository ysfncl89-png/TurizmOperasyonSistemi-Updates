using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PrintPreviewWindow : Window
    {
        public enum TemplateSpec { T3x4, T3x5, T2x6, T2x7 }

        public PrintPreviewWindow()
        {
            InitializeComponent();
            TemplateCombo.ItemsSource = new[] { "3x4 (12)", "3x5 (15)", "2x6 (12)", "2x7 (14)" };
            TemplateCombo.SelectedIndex = 1; // default 3x5
            OrientationCombo.SelectedIndex = 0;
            this.Loaded += (s, e) => RefreshPreview();
        }

        // Sender info applied to all labels in this preview session
        public SenderInfo Sender { get; set; }

        private void RefreshPreview()
        {
            try
            {
                var template = (TemplateCombo.SelectedIndex == 0) ? TemplateSpec.T3x4
                             : (TemplateCombo.SelectedIndex == 1) ? TemplateSpec.T3x5
                             : (TemplateCombo.SelectedIndex == 2) ? TemplateSpec.T2x6
                             : TemplateSpec.T2x7;

                var items = CargoPrintQueue.Instance.Items.ToArray();
                // Use Sender property provided by caller
                var doc = PrintEngine.BuildFixedDocument(items, template, OrientationCombo.SelectedIndex == 1, this.Sender);
                DocViewer.Document = doc;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Önizleme hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshPreviewButton_Click(object sender, RoutedEventArgs e) => RefreshPreview();

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pd = new System.Windows.Controls.PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    var template = (TemplateCombo.SelectedIndex == 0) ? TemplateSpec.T3x4
                                 : (TemplateCombo.SelectedIndex == 1) ? TemplateSpec.T3x5
                                 : (TemplateCombo.SelectedIndex == 2) ? TemplateSpec.T2x6
                                 : TemplateSpec.T2x7;
                    var items = CargoPrintQueue.Instance.Items.ToArray();
                    var doc = PrintEngine.BuildFixedDocument(items, template, OrientationCombo.SelectedIndex == 1, this.Sender);
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
