using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TurizmOperasyonSistemi.Views
{
    public partial class TurProgramWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<ProgramEntry> Entries { get; set; } = new ObservableCollection<ProgramEntry>();

        private string _imagePath;
        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
        }

        public TurProgramWindow()
        {
            InitializeComponent();
            EntriesControl.ItemsSource = Entries;
            // start with one empty row
            Entries.Add(new ProgramEntry { Date = DateTime.Today });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void UploadImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*";
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var orig = File.ReadAllBytes(dlg.FileName);
                    // resize to fit Word/HTML max width before saving/preview
                    var resized = TurizmOperasyonSistemi.Services.ExportHelper.ResizeImageBytesIfNeeded(orig, 560) ?? orig;

                    var ext = Path.GetExtension(dlg.FileName);
                    var temp = Path.Combine(Path.GetTempPath(), $"TurProgImg_{Guid.NewGuid():N}{ext}");
                    File.WriteAllBytes(temp, resized);
                    ImagePath = temp;

                    // load preview from memory so file can be deleted later if needed
                    using var ms = new MemoryStream(resized);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    // hint decode width for faster preview
                    bmp.DecodePixelWidth = 560;
                    bmp.EndInit();
                    bmp.Freeze();
                    PreviewImage.Source = bmp;
                }
                catch { }
            }
        }

        private void AddRowButton_Click(object sender, RoutedEventArgs e)
        {
            Entries.Add(new ProgramEntry { Date = DateTime.Today });
        }

        private void RemoveEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ProgramEntry pe)
            {
                Entries.Remove(pe);
            }
        }

        private void ExportWordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog { Filter = "Word HTML Dosyası (*.doc)|*.doc|HTML File|*.html", DefaultExt = ".doc", FileName = GetDefaultFileName() };
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

                    // Ensure image will not exceed printable A4 width in the Word document.
                    // Use conservative printable width (A4 210mm - margins ≈ 160mm).
                    const double printableMm = 160.0;
                    const double dpi = 96.0; // typical screen DPI used for HTML rendering
                    var targetPx = (int)Math.Round(printableMm / 25.4 * dpi);

                    // Attempt to resize bytes to target pixel width; fallback to original bytes
                    var resized = TurizmOperasyonSistemi.Services.ExportHelper.ResizeImageBytesIfNeeded(bytes, targetPx) ?? bytes;
                    var b64 = Convert.ToBase64String(resized);

                    // Use both pixel width and mm-based max-width so Word respects page dimensions
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
                var dlg = new SaveFileDialog { Filter = "PDF Dosyası (*.pdf)|*.pdf", DefaultExt = ".pdf", FileName = GetDefaultFileName() };
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

    public class ProgramEntry : INotifyPropertyChanged
    {
        private DateTime? _date;
        public DateTime? Date { get => _date; set { _date = value; OnPropertyChanged(nameof(Date)); } }

        private string _time;
        public string Time { get => _time; set { _time = value; OnPropertyChanged(nameof(Time)); } }

        private string _description;
        public string Description { get => _description; set { _description = value; OnPropertyChanged(nameof(Description)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
