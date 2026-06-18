using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using System.Collections.Generic;
using Microsoft.Win32;
using System.IO;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentHistoryWindow : Window
    {
        private int _registrationId;
        public PaymentHistoryWindow(int registrationId)
        {
            InitializeComponent();
            _registrationId = registrationId;
            LoadPayments(registrationId);
        }

        // --- Export helpers ---
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var popup = this.FindName("ExportPopup") as System.Windows.Controls.Primitives.Popup;
                if (popup != null)
                {
                    popup.IsOpen = true;
                    return;
                }
            }
            catch { }

            // fallback to context menu on the ExportButton
            try
            {
                var btn = this.FindName("ExportButton") as System.Windows.Controls.Button;
                if (btn?.ContextMenu != null)
                {
                    btn.ContextMenu.PlacementTarget = btn;
                    btn.ContextMenu.IsOpen = true;
                }
            }
            catch { }
        }

        private void ExportWord_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("docx");
            var path = AskForPath("Word belgesi", "Word dosyası (*.docx)|*.docx", defaultName);
            if (path == null) return;
            ExportWord(NormalizeExtension(path, ".docx"));
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("xlsx");
            var path = AskForPath("Excel çalışma kitabı", "Excel dosyası (*.xlsx)|*.xlsx", defaultName);
            if (path == null) return;
            ExportExcel(NormalizeExtension(path, ".xlsx"));
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("pdf");
            var path = AskForPath("PDF belgesi", "PDF dosyası (*.pdf)|*.pdf", defaultName);
            if (path == null) return;
            ExportPdf(NormalizeExtension(path, ".pdf"));
        }

        private string AskForPath(string title, string filter, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName,
                AddExtension = true,
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private string NormalizeExtension(string filePath, string extension)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return filePath;
            return Path.ChangeExtension(filePath, extension);
        }

        private string GetExportDefaultFileName(string extWithoutDot)
        {
            var name = "Ödeme Hareketleri";
            try
            {
                // Try to resolve person name and registration's tour metadata to make filename more meaningful
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var reg = db.Registrations
                            .Include(r => r.Person)
                            .FirstOrDefault(r => r.Id == _registrationId);
                        if (reg != null)
                        {
                            var personName = (reg.Person?.FirstName ?? "").Trim() + " " + (reg.Person?.LastName ?? "").Trim();
                            personName = personName.Trim();
                            if (!string.IsNullOrEmpty(personName))
                            {
                                name += " - " + personName;
                            }
                            else
                            {
                                name += " - " + _registrationId.ToString();
                            }

                            // collect tour metadata
                            var tourParts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(reg.TourType)) tourParts.Add(reg.TourType.Trim());
                            if (!string.IsNullOrWhiteSpace(reg.Period)) tourParts.Add(reg.Period.Trim());
                            if (!string.IsNullOrWhiteSpace(reg.GroupType)) tourParts.Add(reg.GroupType.Trim());

                            var daysDisplay = (reg.DaysCount ?? string.Empty).Trim();
                            if (!string.IsNullOrEmpty(daysDisplay))
                            {
                                // normalize plain numeric day counts to include 'Gün'
                                if (int.TryParse(daysDisplay, out var di)) daysDisplay = di + " Gün";
                                tourParts.Add(daysDisplay);
                            }

                            if (tourParts.Count > 0)
                            {
                                var meta = string.Join(" ", tourParts).Trim();
                                if (!string.IsNullOrEmpty(meta))
                                {
                                    name += " - " + meta;
                                }
                            }
                        }
                        else
                        {
                            name += " - " + _registrationId.ToString();
                        }
                    }
                }
                catch { name += " - " + _registrationId.ToString(); }
            }
            catch { }

            // sanitize filename
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');

            // limit length to avoid issues with very long registrations
            const int maxLength = 120;
            if (name.Length > maxLength) name = name.Substring(0, maxLength).Trim();

            return name + "." + extWithoutDot;
        }

        private void ExportWord(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            var title = "Ödeme Hareketleri";
            ExportHelper.ExportWord(filePath, rows, cols, title);
        }

        private void ExportExcel(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            var title = "Ödeme Hareketleri";
            ExportHelper.ExportExcel(filePath, rows, cols, title);
        }

        private void ExportPdf(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            var title = "Ödeme Hareketleri";
            ExportHelper.ExportPdf(filePath, rows, cols, title);
        }

        private List<string> GetExportHeaders()
        {
            var headers = PaymentsGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .Select(c => c.Header?.ToString() ?? string.Empty)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToList();
            // remove Dekont column since it's templated buttons
            headers = headers.Where(h => !h.Equals("Dekont", StringComparison.OrdinalIgnoreCase)).ToList();
            return headers;
        }

        private List<Dictionary<string, string>> GetExportRows(List<string> headers)
        {
            var source = PaymentsGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
            var visibleCols = PaymentsGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            var getters = new List<Func<object, string>>();
            var rawHeaders = new List<string>();
            foreach (var col in visibleCols)
            {
                var header = col.Header?.ToString() ?? string.Empty;
                rawHeaders.Add(header);
                if (header.Equals("Dekont", StringComparison.OrdinalIgnoreCase))
                {
                    getters.Add(obj => string.Empty);
                    continue;
                }

                try
                {
                    if (col is System.Windows.Controls.DataGridBoundColumn dbc)
                    {
                        if (dbc.Binding is System.Windows.Data.Binding b && b.Path != null && !string.IsNullOrWhiteSpace(b.Path.Path))
                        {
                            var propName = b.Path.Path;
                            getters.Add(obj =>
                            {
                                try
                                {
                                    var p = obj.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                                    if (p == null) return string.Empty;
                                    var v = p.GetValue(obj);
                                    if (v == null) return string.Empty;
                                    if (v is System.DateTime dt) return dt.ToString("dd.MM.yyyy HH:mm");
                                    return v.ToString();
                                }
                                catch { return string.Empty; }
                            });
                            continue;
                        }
                    }
                }
                catch { }

                // fallback: try map header to known property names
                var mapped = MapPaymentHeaderToProperty(header);
                getters.Add(obj => GetPropertyStringValue(obj, mapped ?? header) ?? string.Empty);
            }

            var rows = new List<Dictionary<string, string>>();
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                var dict = new Dictionary<string, string>();
                for (int ci = 0; ci < rawHeaders.Count; ci++)
                {
                    var raw = rawHeaders[ci];
                    var outHeader = headers.Contains(raw) ? raw : raw;
                    string val = string.Empty;
                    try { val = getters[ci]?.Invoke(item) ?? string.Empty; } catch { val = string.Empty; }
                    dict[outHeader] = val;
                }
                rows.Add(dict);
            }
            return rows;
        }

        private string MapPaymentHeaderToProperty(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return header;
            var h = header.Trim();
            if (h.Equals("Tarih", StringComparison.OrdinalIgnoreCase)) return "CreatedAt";
            if (h.StartsWith("Miktar", StringComparison.OrdinalIgnoreCase)) return "OriginalAmountDisplay";
            if (h.Equals("Dönüştürülmüş", StringComparison.OrdinalIgnoreCase)) return "ConvertedDisplay";
            if (h.Equals("Kur", StringComparison.OrdinalIgnoreCase)) return "Rate";
            if (h.Equals("Ödeme Yöntemi", StringComparison.OrdinalIgnoreCase)) return "PaymentMethod";
            if (h.Equals("Açıklama", StringComparison.OrdinalIgnoreCase)) return "Note";
            return h;
        }

        private string GetPropertyStringValue(object obj, string propName)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(propName)) return string.Empty;
                var p = obj.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p == null) return string.Empty;
                var v = p.GetValue(obj);
                if (v == null) return string.Empty;
                if (v is System.DateTime dt) return dt.ToString("dd.MM.yyyy HH:mm");
                return v.ToString();
            }
            catch { return string.Empty; }
        }

        private void LoadPayments(int registrationId)
        {
            using (var db = new AppDbContext())
            {
                db.EnsurePaymentsTableExists();
                // Load entities into memory first to avoid provider translation and NULL read issues
                var payments = db.Payments
                    .Where(p => p.RegistrationId == registrationId)
                    .OrderByDescending(p => p.CreatedAt)
                    .Include(p => p.Registration)
                    .ToList();

                var list = payments.Select(p => new
                {
                    p.Id,
                    p.CreatedAt,
                    OriginalAmountDisplay = (p.OriginalAmount.ToString("N2") + " " + (p.OriginalCurrency ?? "")),
                    p.AmountInRegistrationCurrency,
                    ConvertedDisplay = (p.AmountInRegistrationCurrency.ToString("N2") + " " + (p.Registration?.Currency ?? "")),
                    Rate = p.Rate,
                    IsRefund = p.AmountInRegistrationCurrency < 0,
                    PaymentMethod = p.PaymentMethod ?? "",
                    UploadedDocumentPath = p.UploadedDocumentPath,
                    UploadedFileName = string.IsNullOrEmpty(p.UploadedDocumentPath) ? "" : System.IO.Path.GetFileName(p.UploadedDocumentPath),
                    Note = p.Note ?? ""
                }).ToList();

                PaymentsGrid.ItemsSource = list;
            }
        }

        private void DeletePaymentBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            if (!int.TryParse(btn.CommandParameter?.ToString(), out var paymentId)) return;

            var res = MessageBox.Show("Seçili ödeme kaydını silmek istediğinize emin misiniz? Bu işlem geri alınamaz.", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var payment = db.Payments.FirstOrDefault(p => p.Id == paymentId);
                    if (payment == null)
                    {
                        MessageBox.Show("Ödeme kaydı bulunamadı.");
                        return;
                    }

                    var regId = payment.RegistrationId;
                    db.Payments.Remove(payment);
                    db.SaveChanges();

                    // Recalculate registration sums from remaining payments
                    var reg = db.Registrations.FirstOrDefault(r => r.Id == regId);
                    if (reg != null)
                    {
                        // SQLite may store numeric columns as TEXT; perform aggregation on client side to avoid provider errors
                        var paymentAmounts = db.Payments.Where(p => p.RegistrationId == regId).Select(p => p.AmountInRegistrationCurrency).ToList();
                        var paid = paymentAmounts.Sum();
                        reg.PaidAmount = paid;
                        reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                        db.SaveChanges();
                    }

                    // publish notification so ledger views refresh
                    TurizmOperasyonSistemi.Services.NotificationCenter.Publish("payment.updated", regId);
                }

                // reload list
                LoadPayments(_registrationId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ödeme silinirken hata: " + ex.Message);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenDocumentBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            // CommandParameter contains payment Id
            if (!int.TryParse(btn.CommandParameter?.ToString(), out var paymentId)) return;

            try
            {
                using var db = new AppDbContext();
                var payment = db.Payments.FirstOrDefault(p => p.Id == paymentId);
                if (payment == null)
                {
                    MessageBox.Show("Ödeme kaydı bulunamadı.");
                    return;
                }

                var path = payment.UploadedDocumentPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    // Reuse existing UploadChoiceWindow and QRUploadWindow from PaymentPopupWindow
                    var choiceWnd = new UploadChoiceWindow();
                    choiceWnd.Owner = this;
                    var res = choiceWnd.ShowDialog();
                    if (res != true) return;

                    if (choiceWnd.Choice == UploadChoiceWindow.UploadChoice.File)
                    {
                        var dlg = new OpenFileDialog();
                        dlg.Title = "Dekont seçin (PDF/resim)";
                        dlg.Filter = "Tüm desteklenen dosyalar|*.pdf;*.jpg;*.jpeg;*.png;*.bmp|PDF Dosyaları (*.pdf)|*.pdf|Resimler (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Tüm dosyalar (*.*)|*.*";
                        if (dlg.ShowDialog() != true) return;

                        try
                        {
                            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi", "UploadedDocuments");
                            Directory.CreateDirectory(appDir);
                            var destFileName = paymentId + "_" + System.DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Path.GetFileName(dlg.FileName);
                            var destPath = Path.Combine(appDir, destFileName);
                            File.Copy(dlg.FileName, destPath, true);

                            payment.UploadedDocumentPath = destPath;
                            db.SaveChanges();

                            MessageBox.Show("Dekont başarıyla yüklendi.");
                            // reload grid
                            LoadPayments(_registrationId);
                            return;
                        }
                        catch (System.Exception ex)
                        {
                            MessageBox.Show("Dekont yüklenirken hata: " + ex.Message);
                            return;
                        }
                    }

                    if (choiceWnd.Choice == UploadChoiceWindow.UploadChoice.Qr)
                    {
                        var qrWnd = new QRUploadWindow((savedPath, originalName) =>
                        {
                            try
                            {
                                using var db2 = new AppDbContext();
                                var p2 = db2.Payments.FirstOrDefault(p => p.Id == paymentId);
                                if (p2 != null)
                                {
                                    p2.UploadedDocumentPath = savedPath;
                                    db2.SaveChanges();
                                }
                            }
                            catch { }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try { MessageBox.Show("Dekont başarıyla yüklendi via QR."); } catch { }
                                try { LoadPayments(_registrationId); } catch { }
                            });
                        });
                        qrWnd.Owner = this;
                        qrWnd.ShowDialog();
                        return;
                    }
                }

                // Open existing file
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("Dekont açılamadı: " + ex.Message);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("İşlem sırasında hata: " + ex.Message);
            }
        }

        // Note: QR/file upload UI reuse implemented via UploadChoiceWindow and QRUploadWindow
    }
}
