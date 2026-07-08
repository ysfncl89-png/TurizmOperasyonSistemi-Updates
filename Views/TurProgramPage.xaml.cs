using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace TurizmOperasyonSistemi.Views
{
    public partial class TurProgramPage : Page, INotifyPropertyChanged
    {
        private System.Windows.Point _dragStartPoint;
        private ProgramEntry _draggedEntry;
        public ObservableCollection<ProgramEntry> Entries { get; set; } = new ObservableCollection<ProgramEntry>();

        private string _imagePath;
        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
        }

        public TurProgramPage()
        {
            InitializeComponent();
            EntriesControl.ItemsSource = Entries;
            Entries.Add(new ProgramEntry { Date = DateTime.Today });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.NavigationService != null && this.NavigationService.CanGoBack)
                {
                    this.NavigationService.GoBack();
                    return;
                }

                // If not hosted in a NavigationService, try to navigate main frame back to ListsView
                var main = Window.GetWindow(this) as MainWindow;
                if (main != null)
                {
                    try { main.MainFrame.Navigate(new ListsView()); return; } catch { }
                }

                // fallback: close host window (if this page is inside its own window)
                var wnd = Window.GetWindow(this);
                wnd?.Close();
            }
            catch { }
        }

        private void UploadImageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Offer choice between local file and QR upload (reuse existing dialogs)
                var choiceWnd = new UploadChoiceWindow();
                choiceWnd.Owner = Window.GetWindow(this);
                var res = choiceWnd.ShowDialog();
                if (res != true) return;

                if (choiceWnd.Choice == UploadChoiceWindow.UploadChoice.File)
                {
                    var dlg = new OpenFileDialog();
                    dlg.Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*";
                    if (dlg.ShowDialog() == true)
                    {
                        try
                        {
                            var orig = File.ReadAllBytes(dlg.FileName);
                            var resized = TurizmOperasyonSistemi.Services.ExportHelper.ResizeImageBytesIfNeeded(orig, 560) ?? orig;

                            var ext = Path.GetExtension(dlg.FileName);
                            var temp = Path.Combine(Path.GetTempPath(), $"TurProgImg_{Guid.NewGuid():N}{ext}");
                            File.WriteAllBytes(temp, resized);
                            ImagePath = temp;

                            using var ms = new MemoryStream(resized);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.DecodePixelWidth = 560;
                            bmp.EndInit();
                            bmp.Freeze();
                            PreviewImage.Source = bmp;
                        }
                        catch { }
                    }
                }
                else if (choiceWnd.Choice == UploadChoiceWindow.UploadChoice.Qr)
                {
                    var qrWnd = new QRUploadWindow((savedPath, originalName) =>
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(savedPath) || !File.Exists(savedPath)) return;
                            // Optionally resize to target width
                            var bytes = File.ReadAllBytes(savedPath);
                            var resized = TurizmOperasyonSistemi.Services.ExportHelper.ResizeImageBytesIfNeeded(bytes, 560) ?? bytes;
                            var ext = Path.GetExtension(savedPath);
                            var temp = Path.Combine(Path.GetTempPath(), $"TurProgImg_{Guid.NewGuid():N}{ext}");
                            File.WriteAllBytes(temp, resized);
                            ImagePath = temp;

                            using var ms = new MemoryStream(resized);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.DecodePixelWidth = 560;
                            bmp.EndInit();
                            bmp.Freeze();
                            Dispatcher.Invoke(() => { PreviewImage.Source = bmp; });
                        }
                        catch { }
                    });
                    qrWnd.Owner = Window.GetWindow(this);
                    qrWnd.ShowDialog();
                }
            }
            catch { }
        }

        private void AddRowButton_Click(object sender, RoutedEventArgs e)
        {
            Entries.Add(new ProgramEntry { Date = DateTime.Today });
        }

        private void EntriesList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            // find ListBoxItem under mouse
            var lst = sender as ListBox;
            var dep = (DependencyObject)e.OriginalSource;
            var item = FindAncestor<ListBoxItem>(dep);
            if (item != null)
            {
                _draggedEntry = item.DataContext as ProgramEntry;
            }
            else
            {
                _draggedEntry = null;
            }
        }

        private void EntriesList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            var diff = System.Windows.Point.Subtract(pos, _dragStartPoint);
            if (_draggedEntry == null) return;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var data = new DataObject(typeof(ProgramEntry), _draggedEntry);
                DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
            }
        }

        private void EntriesList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void EntriesList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ProgramEntry))) return;
            var droppedData = e.Data.GetData(typeof(ProgramEntry)) as ProgramEntry;
            if (droppedData == null) return;

            var list = sender as ListBox ?? EntriesControl;
            // find target item
            var pos = e.GetPosition(list);
            var element = list.InputHitTest(pos) as DependencyObject;
            var targetItem = FindAncestor<ListBoxItem>(element);

            int oldIndex = Entries.IndexOf(droppedData);
            int newIndex = -1;
            if (targetItem != null)
            {
                var targetData = targetItem.DataContext as ProgramEntry;
                newIndex = Entries.IndexOf(targetData);
            }
            else
            {
                // dropped on empty area -> move to end
                newIndex = Entries.Count - 1;
            }

            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            {
                // adjust newIndex if removing earlier item
                if (oldIndex < newIndex) newIndex--; 
                Entries.Move(oldIndex, newIndex);
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t) return t;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void RemoveEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ProgramEntry pe)
            {
                Entries.Remove(pe);
            }
        }

        // Removed custom calendar button logic; using built-in DatePicker small button instead.

        private void ExportWordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Word HTML Dosyası (*.doc)|*.doc|HTML File|*.html", DefaultExt = ".doc", FileName = GetDefaultFileName() };
                if (dlg.ShowDialog() != true) return;

                var html = BuildHtmlDocumentEmbedImage();
                File.WriteAllText(dlg.FileName, html, Encoding.UTF8);
                MessageBox.Show("Word (HTML) dosyası başarıyla kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Word export hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildHtmlDocument()
        {
            return BuildHtmlDocumentEmbedImage();
        }

        private string BuildHtmlDocumentEmbedImage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><style>body{font-family:Segoe UI, Arial; color:#111} .date{color:#e11d48; font-weight:700; font-size:18px; margin-top:18px} .time{font-weight:700;} .desc{margin-bottom:18px}</style></head><body>");
            if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(ImagePath);
                    var ext = Path.GetExtension(ImagePath).ToLowerInvariant();
                    var mime = ext == ".png" ? "image/png" : (ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : "image/png");

                    const double printableMm = 160.0;
                    const double dpi = 96.0;
                    var targetPx = (int)Math.Round(printableMm / 25.4 * dpi);

                    var resized = TurizmOperasyonSistemi.Services.ExportHelper.ResizeImageBytesIfNeeded(bytes, targetPx) ?? bytes;
                    var b64 = Convert.ToBase64String(resized);

                    sb.AppendLine($"<div style=\"max-width:{printableMm}mm;margin:0 auto;\"><img src=\"data:{mime};base64,{b64}\" style=\"width:{targetPx}px; max-width:{printableMm}mm; height:auto; margin-bottom:18px; display:block;\" /></div>");
                }
                catch { }
            }
            foreach (var entry in Entries)
            {
                var dt = entry.Date?.ToString("dd.MM.yyyy") ?? string.Empty;
                sb.AppendLine($"<div class=\"date\">{dt}</div>");
                sb.AppendLine($"<div class=\"time\">Saat: {System.Net.WebUtility.HtmlEncode(entry.Time ?? string.Empty)}</div>");
                sb.AppendLine($"<div class=\"desc\">{System.Net.WebUtility.HtmlEncode(entry.Description ?? string.Empty)}</div>");
            }
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF Dosyası (*.pdf)|*.pdf", DefaultExt = ".pdf", FileName = GetDefaultFileName() };
                if (dlg.ShowDialog() != true) return;

                var headers = new List<string> { "Tarih", "Saat", "Açıklama" };
                var rows = GetExportRows(headers);
                var footers = GetExportFooters();
                var title = "Tur Programı";

                TurizmOperasyonSistemi.Services.ExportHelper.ExportPdf(dlg.FileName, rows, headers, title, footers, null, null, null, null, null, null, null, ImagePath);
                MessageBox.Show("PDF dosyası başarıyla kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PDF export hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Dictionary<string, string>> GetExportRows(List<string> headers)
        {
            var rows = new List<Dictionary<string, string>>();
            foreach (var entry in Entries)
            {
                var dict = new Dictionary<string, string>
                {
                    ["Tarih"] = entry.Date?.ToString("dd.MM.yyyy") ?? string.Empty,
                    ["Saat"] = entry.Time ?? string.Empty,
                    ["Açıklama"] = entry.Description ?? string.Empty
                };
                rows.Add(dict);
            }
            return rows;
        }

        private List<string> GetExportFooters()
        {
            var footers = new List<string>();
            if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
            {
                footers.Add("[Resim eklendi]");
            }
            footers.Add($"Hazırlama Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            return footers;
        }

        private string GetDefaultFileName()
        {
            return $"TurProgrami_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ProgramEntry is defined in TurProgramWindow.xaml.cs; reuse that type to avoid duplicate definitions.
}
