using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsGeneralView : Page
    {
        private List<GeneralRow> _allRows = new List<GeneralRow>();
        private Button _applyButton;
        private DataGridColumn _editingColumn;

        public PersonsGeneralView()
        {
            InitializeComponent();
            ResultsDataGrid.MouseDoubleClick += ResultsDataGrid_MouseDoubleClick;
            ResultsDataGrid.CellEditEnding += ResultsDataGrid_CellEditEnding;
            Loaded += PersonsGeneralView_Loaded;
            LoadFilterOptions();
            // initial state: show filters, hide topbar
            TopBar.Visibility = Visibility.Collapsed;
            FiltersPanel.Visibility = Visibility.Visible;
        }

        private void PersonsGeneralView_Loaded(object sender, RoutedEventArgs e)
        {
            try { SearchTextBox.Focus(); } catch { }

            // If XAML missing the ApplyFiltersButton (was edited incorrectly), create it dynamically
            try
            {
                // If there is already a named button in XAML, skip dynamic creation
                var named = this.FindName("ApplyFiltersButton") as Button;
                if (named != null)
                {
                    _applyButton = named;
                }
                else
                {
                    if (FilterWarning != null)
                    {
                        if (FilterWarning.Parent is Panel parent)
                        {
                            _applyButton = new Button
                            {
                                Content = "Listeyi Göster",
                                Width = 160,
                                Padding = new Thickness(10, 8, 10, 8),
                                IsEnabled = false
                            };
                            // apply style if available
                            try { _applyButton.Style = (Style)FindResource("PrimaryButtonStyle"); } catch { }
                            _applyButton.Click += ApplyFiltersButton_Click;
                            parent.Children.Add(_applyButton);
                        }
                    }
                }
            }
            catch { }
            // ensure initial filter UI state reflects current selections
            try { Filter_Changed(null, null); } catch { }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If we're currently showing results, treat Back as "go back to filters" instead of navigating the app back stack
                if (ResultsBorder != null && ResultsBorder.Visibility == Visibility.Visible)
                {
                    // hide results, show filters and hide top search bar
                    ResultsBorder.Visibility = Visibility.Collapsed;
                    TopBar.Visibility = Visibility.Collapsed;
                    FiltersPanel.Visibility = Visibility.Visible;
                    return;
                }

                // Otherwise perform normal navigation (go back in NavService) or close window
                if (this.NavigationService != null && this.NavigationService.CanGoBack)
                {
                    this.NavigationService.GoBack();
                    return;
                }

                var win = Window.GetWindow(this);
                if (win != null) win.Close();
            }
            catch { }
        }

        private void LoadFilterOptions()
        {
            try
            {
                using var db = new AppDbContext();
                var all = db.AppSettings.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList();

                var tour = all.Where(x => x.Type == "TourType").Select(x => x.Value).ToList();
                tour.Insert(0, "Seçiniz");
                if (CbTourType != null) CbTourType.ItemsSource = tour;

                var period = all.Where(x => x.Type == "Dönem").Select(x => x.Value).ToList();
                period.Insert(0, "Seçiniz");
                if (CbPeriod != null) CbPeriod.ItemsSource = period;

                var group = all.Where(x => x.Type == "GroupType").Select(x => x.Value).ToList();
                group.Insert(0, "Seçiniz");
                if (CbGroupType != null) CbGroupType.ItemsSource = group;

                var days = all.Where(x => x.Type == "Kaç Günlük Grup").Select(x => x.Value).ToList();
                days.Insert(0, "Seçiniz");
                if (CbGroupDays != null) CbGroupDays.ItemsSource = days;
            }
            catch { }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var tourReady = CbTourType?.SelectedItem as string;
                var periodReady = CbPeriod?.SelectedItem as string;
                var ready = !string.IsNullOrWhiteSpace(tourReady) && tourReady != "Seçiniz" &&
                            !string.IsNullOrWhiteSpace(periodReady) && periodReady != "Seçiniz";

                if (_applyButton != null)
                    _applyButton.IsEnabled = ready;

                if (FilterWarning != null)
                    FilterWarning.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }

        private void ApplyFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // load results and swap UI: hide filters, show top search bar
            LoadResults();
            FiltersPanel.Visibility = Visibility.Collapsed;
            TopBar.Visibility = Visibility.Visible;
            ResultsBorder.Visibility = Visibility.Visible;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            // Prefer showing the custom popup if available
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

            // fallback to old context menu
            if (ExportButton?.ContextMenu != null)
            {
                ExportButton.ContextMenu.PlacementTarget = ExportButton;
                ExportButton.ContextMenu.IsOpen = true;
            }
        }

        private void ExportWord_Click(object sender, RoutedEventArgs e)
        {
            var path = AskForPath("Word belgesi", "Word dosyası (*.docx)|*.docx", "genel-listesi.docx");
            if (path == null) return;
            ExportWord(NormalizeExtension(path, ".docx"));
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var path = AskForPath("Excel çalışma kitabı", "Excel dosyası (*.xlsx)|*.xlsx", "genel-listesi.xlsx");
            if (path == null) return;
            ExportExcel(NormalizeExtension(path, ".xlsx"));
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var path = AskForPath("PDF belgesi", "PDF dosyası (*.pdf)|*.pdf", "genel-listesi.pdf");
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

        private void ExportWord(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            Services.ExportHelper.ExportWord(filePath, rows, cols, "Genel Liste");
        }

        private void ExportExcel(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            Services.ExportHelper.ExportExcel(filePath, rows, cols, "Genel Liste");
        }

        private void ExportPdf(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            Services.ExportHelper.ExportPdf(filePath, rows, cols, "Genel Liste");
        }

        private void DeleteIfExists(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        private List<string> GetExportHeaders()
        {
            // Respect visual order (DisplayIndex) when building export headers
            var headers = ResultsDataGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .Select(c => c.Header?.ToString() ?? string.Empty)
                .ToList();
            if (!headers.Any())
            {
                // fallback to all known headers from MapHeaderToProperty
                headers = new List<string> { "Ad", "Soyad", "TC Kimlik", "Telefon numarası", "Adres", "Kan Grubu", "Hastalık durumu" };
            }
            return headers;
        }

        private List<Dictionary<string, string>> GetExportRows(List<string> headers)
        {
            var source = ResultsDataGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
            return source.Select(item => headers.ToDictionary(h => h, h => GetPropertyStringValue(item, MapHeaderToProperty(h)))).ToList();
        }

        // --- Below: utility methods for building docx/xlsx/pdf adapted from PersonsHealthView ---
        private void AddZipEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.Write(content);
        }

        private void AddDocxContentTypes(ZipArchive archive)
        {
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\n</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private void AddDocxRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"R1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
        }

        private void AddDocxDocumentRels(ZipArchive archive)
        {
            AddZipEntry(archive, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        }

        private void AddDocxDocumentXml(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers)
        {
            var tableRows = new List<string> { BuildWordTableRow(headers, true) };
            tableRows.AddRange(rows.Select(r => BuildWordTableRow(headers.Select(h => r.ContainsKey(h) ? r[h] : string.Empty).ToList(), false)));
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n  <w:body>\n    <w:p><w:r><w:t>Genel Liste</w:t></w:r></w:p>\n    <w:tbl>\n      {string.Join(Environment.NewLine, tableRows)}\n    </w:tbl>\n  </w:body>\n</w:document>";
            AddZipEntry(archive, "word/document.xml", document);
        }

        private string BuildWordTableRow(IReadOnlyList<string> values, bool header)
        {
            var cells = values.Select(value => $"<w:tc><w:tcPr>{(header ? "<w:shd w:fill=\"22324A\"/>" : string.Empty)}</w:tcPr><w:p><w:r><w:t>{EscapeXml(value ?? string.Empty)}</w:t></w:r></w:p></w:tc>");
            return "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
        }

        private void AddXlsxContentTypes(ZipArchive archive)
        {
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\n  <Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>\n  <Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>\n</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private void AddXlsxRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
            AddZipEntry(archive, "docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">  <dc:title>Genel Liste</dc:title></cp:coreProperties>");
            AddZipEntry(archive, "docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>TurizmOperasyonSistemi</Application><TitlesOfParts><vt:vector size=\"1\" baseType=\"lpstr\"><vt:lpstr>Genel Liste</vt:lpstr></vt:vector></TitlesOfParts></Properties>");
        }

        private void AddXlsxWorkbook(ZipArchive archive)
        {
            var workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <bookViews>\n    <workbookView activeTab=\"0\"/>\n  </bookViews>\n  <sheets>\n    <sheet name=\"Genel Liste\" sheetId=\"1\" r:id=\"rId1\"/>\n  </sheets>\n</workbook>";
            AddZipEntry(archive, "xl/workbook.xml", workbook);
        }

        private void AddXlsxWorkbookRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", rels);
        }

        private void AddXlsxStyles(ZipArchive archive)
        {
            var styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts></styleSheet>";
            AddZipEntry(archive, "xl/styles.xml", styles);
        }

        private void AddXlsxSheet(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers)
        {
            var rowXml = new List<string>();
            rowXml.Add(BuildXlsxRow(1, headers, true));
            for (var i = 0; i < rows.Count; i++)
            {
                rowXml.Add(BuildXlsxRow(i + 2, headers.Select(h => rows[i].ContainsKey(h) ? rows[i][h] : string.Empty).ToList(), false));
            }
            var lastColumn = ColumnName(headers.Count);
            var lastRow = Math.Max(1, rows.Count + 1);
            var sheet = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <dimension ref=\"A1:{lastColumn}{lastRow}\"/>\n  <sheetData>\n    {string.Join(Environment.NewLine, rowXml)}\n  </sheetData>\n</worksheet>";
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }

        private string BuildXlsxRow(int rowNumber, IReadOnlyList<string> values, bool header)
        {
            var cells = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var cellRef = $"{ColumnName(i + 1)}{rowNumber}";
                cells.Add($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeXml(values[i] ?? string.Empty)}</t></is></c>");
            }
            return $"<row r=\"{rowNumber}\">{string.Join(string.Empty, cells)}</row>";
        }

        private byte[] BuildSimplePdf(List<Dictionary<string, string>> rows, List<string> headers)
        {
            var pageWidth = 842; var pageHeight = 595; var margin = 28; var rowHeight = 22; var usableWidth = pageWidth - margin * 2; var columnWidth = usableWidth / Math.Max(1, headers.Count);
            var content = new List<string> { $"BT /F1 16 Tf 40 560 Td ({EscapePdfText("Genel Liste")}) Tj ET" };
            var y = 520; content.AddRange(BuildPdfRow(headers, margin, y, columnWidth, rowHeight, true)); y -= rowHeight;
            foreach (var row in rows)
            {
                if (y < 50) break;
                content.AddRange(BuildPdfRow(headers.Select(h => row.ContainsKey(h) ? row[h] : string.Empty).ToList(), margin, y, columnWidth, rowHeight, false));
                y -= rowHeight;
            }
            var contentText = string.Join("\n", content);
            return BuildMinimalPdfBytes(pageWidth, pageHeight, contentText);
        }

        private IEnumerable<string> BuildPdfRow(IReadOnlyList<string> values, int x, int y, double columnWidth, int rowHeight, bool header)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var left = x + (int)(i * columnWidth);
                var right = x + (int)((i + 1) * columnWidth);
                var text = EscapePdf(values[i] ?? string.Empty);
                var textX = left + 4; var textY = y + 7;
                if (header)
                {
                    yield return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} rg", 34.0/255.0, 50.0/255.0, 74.0/255.0);
                    yield return "0 0 0 RG";
                    yield return $"{left} {y} {right - left} {rowHeight} re B";
                    yield return "1 1 1 rg";
                    yield return $"BT /F2 9 Tf {textX} {textY} Td ({EscapePdfText(values[i] ?? string.Empty)}) Tj ET";
                    yield return "0 0 0 rg";
                }
                else
                {
                    yield return "0 0 0 RG";
                    yield return $"{left} {y} {right - left} {rowHeight} re S";
                    yield return "0 0 0 rg";
                    yield return $"BT /F1 8 Tf {textX} {textY} Td ({EscapePdfText(values[i] ?? string.Empty)}) Tj ET";
                }
            }
        }

        private byte[] BuildMinimalPdfBytes(int pageWidth, int pageHeight, string content)
        {
            var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>" };
            var streamBytes = System.Text.Encoding.ASCII.GetBytes(content);
            var contentObject = $"<< /Length {streamBytes.Length} >>\nstream\n{content}\nendstream";
            objects.Add(contentObject);
            using var ms = new MemoryStream(); using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, 1024, leaveOpen: true);
            writer.WriteLine("%PDF-1.4");
            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                writer.WriteLine($"{i + 1} 0 obj"); writer.WriteLine(objects[i]); writer.WriteLine("endobj");
            }
            writer.Flush(); var xrefPosition = ms.Position; writer.WriteLine("xref"); writer.WriteLine($"0 {objects.Count + 1}"); writer.WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets) writer.WriteLine($"{offset:0000000000} 00000 n ");
            writer.WriteLine("trailer"); writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>"); writer.WriteLine("startxref"); writer.WriteLine(xrefPosition); writer.WriteLine("%%EOF"); writer.Flush(); return ms.ToArray();
        }

        private string EscapeXml(string value) { return SecurityElement.Escape(value) ?? string.Empty; }
        private string EscapePdf(string value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)"); }
        private string EscapePdfText(string value) { return EscapePdf(TransliterateTurkish(value)); }
        private string TransliterateTurkish(string value)
        {
            return (value ?? string.Empty).Replace("İ", "I").Replace("ı", "i").Replace("Ş", "S").Replace("ş", "s").Replace("Ğ", "G").Replace("ğ", "g").Replace("Ü", "U").Replace("ü", "u").Replace("Ö", "O").Replace("ö", "o").Replace("Ç", "C").Replace("ç", "c");
        }

        private string ColumnName(int index)
        {
            var dividend = index; var columnName = string.Empty;
            while (dividend > 0) { var modulo = (dividend - 1) % 26; columnName = Convert.ToChar(65 + modulo) + columnName; dividend = (dividend - modulo) / 26; }
            return columnName;
        }

        private void LoadResults()
        {
            try
            {
                using var db = new AppDbContext();

                var query = db.Registrations.Include(r => r.Person).AsQueryable();

                var tour = CbTourType.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(tour) && tour != "Seçiniz") query = query.Where(r => r.TourType == tour);

                var period = CbPeriod.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(period) && period != "Seçiniz") query = query.Where(r => r.Period == period);

                var group = CbGroupType.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(group) && group != "Seçiniz") query = query.Where(r => r.GroupType == group);

                var days = CbGroupDays.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(days) && days != "Seçiniz") query = query.Where(r => r.DaysCount == days);

                var list = query.OrderBy(r => r.Id).ToList();

                var rows = new List<GeneralRow>();
                var seq = 1;
                foreach (var reg in list)
                {
                    var p = reg.Person;
                    var r = new GeneralRow
                    {
                        RegistrationId = reg.Id,
                        PersonId = p?.Id ?? 0,
                        Sequence = seq++,
                        FirstName = p?.FirstName ?? string.Empty,
                        LastName = p?.LastName ?? string.Empty,
                        TcKimlikNo = p?.TcKimlikNo ?? string.Empty,
                        Gender = p?.Gender ?? string.Empty,
                        BirthDate = p?.BirthDate.ToString("yyyy-MM-dd") ?? string.Empty,
                        PhoneNumber = p?.PhoneNumber ?? string.Empty,
                        Address = p?.Address ?? string.Empty,
                        PassportType = p?.PassportType ?? string.Empty,
                        PassportNumber = p?.PassportNumber ?? string.Empty,
                        PassportStartDate = p?.PassportStartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                        PassportEndDate = p?.PassportEndDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                        BloodType = p?.BloodType ?? string.Empty,
                        VaccineStatus = p?.VaccineStatus ?? string.Empty,
                        HealthStatus = p?.HealthStatus ?? string.Empty,
                        AgeRange = p?.AgeRange ?? string.Empty,

                        VisaRequest = reg.VisaRequest ?? string.Empty,
                        TicketRequest = reg.TicketRequest ?? string.Empty,
                        CargoRequest = reg.CargoRequest ?? string.Empty,
                        RoomType = reg.RoomType ?? string.Empty,
                        DepartureCity = reg.DepartureCity ?? string.Empty,
                        HasTransfer = reg.HasTransfer ?? string.Empty,
                        TransferCity = reg.TransferCity ?? string.Empty,
                        District = reg.District ?? string.Empty,
                        TotalPrice = reg.TotalPrice.ToString("0.##"),
                        Currency = reg.Currency ?? string.Empty,
                        PaidAmount = reg.PaidAmount.ToString("0.##"),
                        RemainingDebt = reg.RemainingDebt.ToString("0.##"),
                        Reference = reg.Reference ?? string.Empty
                    };
                    rows.Add(r);
                }

                _allRows = rows;
                ResultsDataGrid.ItemsSource = _allRows;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var dep = (DependencyObject)e.OriginalSource;
                var cell = FindParent<DataGridCell>(dep);
                if (cell == null) return;
                var column = cell.Column;
                var header = (column.Header?.ToString() ?? string.Empty).Trim();
                // allowed editable headers
                var editable = new[] { "telefon numarası", "adres", "pasaport numarası", "aşı durumu", "hastalık durumu", "ilçe", "toplam fiyat", "ödenen", "kalan", "referans" };
                if (!editable.Contains(header.ToLowerInvariant())) return;

                // make only this column editable and begin edit
                ResultsDataGrid.IsReadOnly = false;
                foreach (var col in ResultsDataGrid.Columns) col.IsReadOnly = true;
                column.IsReadOnly = false;
                _editingColumn = column;

                // select row and begin edit
                if (cell.DataContext != null)
                {
                    ResultsDataGrid.SelectedItem = cell.DataContext;
                    ResultsDataGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, column);
                    ResultsDataGrid.BeginEdit();
                }
            }
            catch { }
        }

        private void ResultsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            try
            {
                if (_editingColumn == null) return;
                // commit already in progress; save after UI update
                if (e.EditAction == DataGridEditAction.Commit)
                {
                    var rowItem = e.Row.Item as GeneralRow;
                    var header = e.Column.Header?.ToString() ?? string.Empty;
                    string newText = null;
                    try
                    {
                        if (e.EditingElement is TextBox tb) newText = tb.Text;
                    }
                    catch { }
                    // use dispatcher to ensure binding updated
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { SaveEditedValue(rowItem, header, newText); } catch { }
                        // restore grid to read-only default
                        ResultsDataGrid.CommitEdit();
                        ResultsDataGrid.IsReadOnly = true;
                        foreach (var col in ResultsDataGrid.Columns) col.IsReadOnly = true;
                        _editingColumn = null;
                    }));
                }
                else
                {
                    // canceled - restore
                    ResultsDataGrid.IsReadOnly = true;
                    foreach (var col in ResultsDataGrid.Columns) col.IsReadOnly = true;
                    _editingColumn = null;
                }
            }
            catch { }
        }

        private void SaveEditedValue(GeneralRow row, string header, string newText = null)
        {
            if (row == null) return;
            var propName = MapHeaderToProperty(header);
            if (string.IsNullOrWhiteSpace(propName)) return;

            // find property on row
            var prop = row.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) return;

            // determine new value: prefer provided newText (from editing control), otherwise current row value
            object newValObj = null;
            if (!string.IsNullOrEmpty(newText))
            {
                newValObj = newText;
                // update row property so UI reflects change
                try { prop.SetValue(row, newText); } catch { }
            }
            else
            {
                newValObj = prop.GetValue(row);
            }

            var newVal = newValObj?.ToString();

            try
            {
                using var db = new AppDbContext();
                // person properties
                var personProps = new[] { "PhoneNumber", "Address", "PassportNumber", "VaccineStatus", "HealthStatus" };
                var registrationProps = new[] { "District", "TotalPrice", "PaidAmount", "RemainingDebt", "Reference" };

                if (personProps.Contains(propName, StringComparer.OrdinalIgnoreCase))
                {
                    var p = db.Persons.FirstOrDefault(x => x.Id == row.PersonId);
                    if (p == null) return;
                    // assign string properties
                    if (propName.Equals("PhoneNumber", StringComparison.OrdinalIgnoreCase)) p.PhoneNumber = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("Address", StringComparison.OrdinalIgnoreCase)) p.Address = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("PassportNumber", StringComparison.OrdinalIgnoreCase)) p.PassportNumber = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("VaccineStatus", StringComparison.OrdinalIgnoreCase)) p.VaccineStatus = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("HealthStatus", StringComparison.OrdinalIgnoreCase)) p.HealthStatus = newVal?.ToString() ?? string.Empty;
                    db.Entry(p).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                    db.SaveChanges();
                    // refresh grid so change is visible
                    try { ResultsDataGrid.Items.Refresh(); } catch { }
                }
                else if (registrationProps.Contains(propName, StringComparer.OrdinalIgnoreCase))
                {
                    var reg = db.Registrations.FirstOrDefault(x => x.Id == row.RegistrationId);
                    if (reg == null) return;
                    if (propName.Equals("District", StringComparison.OrdinalIgnoreCase)) { reg.District = newVal?.ToString() ?? string.Empty; }
                    else if (propName.Equals("Reference", StringComparison.OrdinalIgnoreCase)) { reg.Reference = newVal?.ToString() ?? string.Empty; }
                    else if (propName.Equals("TotalPrice", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseDecimalFlexible(newVal, out var d))
                        {
                            reg.TotalPrice = d;
                            // recalc remaining debt
                            reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                            row.TotalPrice = reg.TotalPrice.ToString("0.##");
                            row.RemainingDebt = reg.RemainingDebt.ToString("0.##");
                        }
                    }
                    else if (propName.Equals("PaidAmount", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseDecimalFlexible(newVal, out var d))
                        {
                            reg.PaidAmount = d;
                            // recalc remaining debt
                            reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                            row.PaidAmount = reg.PaidAmount.ToString("0.##");
                            row.RemainingDebt = reg.RemainingDebt.ToString("0.##");
                        }
                    }
                    else if (propName.Equals("RemainingDebt", StringComparison.OrdinalIgnoreCase))
                    {
                        // If user edits remaining debt, adjust PaidAmount = TotalPrice - RemainingDebt
                        if (TryParseDecimalFlexible(newVal, out var d))
                        {
                            reg.RemainingDebt = d;
                            reg.PaidAmount = reg.TotalPrice - reg.RemainingDebt;
                            row.RemainingDebt = reg.RemainingDebt.ToString("0.##");
                            row.PaidAmount = reg.PaidAmount.ToString("0.##");
                        }
                    }
                    db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                    db.SaveChanges();
                    try { ResultsDataGrid.Items.Refresh(); } catch { }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kaydetme sırasında hata: " + ex.Message);
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = child;
            while (parent != null)
            {
                if (parent is T correctlyTyped) return correctlyTyped;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private bool TryParseDecimalFlexible(string s, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(s)) return false;
            // try invariant
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
            // try current culture
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out value)) return true;
            // replace comma/point heuristics
            var alt = s.Replace(',', '.');
            if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
            alt = s.Replace('.', ',');
            if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out value)) return true;
            return false;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = (SearchTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultsDataGrid.ItemsSource = _allRows;
                SearchHint.Visibility = Visibility.Visible;
                return;
            }

            SearchHint.Visibility = Visibility.Collapsed;
            var q = text.ToLowerInvariant();

            // support column search like "Ad:ali" or "Ad: ali"
            if (q.Contains(":"))
            {
                var parts = q.Split(new[] { ':' }, 2);
                var key = parts[0].Trim();
                var val = parts[1].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    Func<GeneralRow, string> selector = GetSelectorForHeader(key);
                    if (selector != null)
                    {
                        ResultsDataGrid.ItemsSource = _allRows.Where(r => (selector(r) ?? string.Empty).ToLowerInvariant().Contains(val)).ToList();
                        return;
                    }
                }
            }

            // generic full-text search across all visible fields
            ResultsDataGrid.ItemsSource = _allRows.Where(r =>
                (r.FirstName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.LastName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.TcKimlikNo ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.PhoneNumber ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.Address ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.BloodType ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.HealthStatus ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.VaccineStatus ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.VisaRequest ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.TicketRequest ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (r.Reference ?? string.Empty).ToLowerInvariant().Contains(q)
            ).ToList();
        }

        private Func<GeneralRow, string> GetSelectorForHeader(string headerKey)
        {
            switch (headerKey.Trim().ToLowerInvariant())
            {
                case "ad": return r => r.FirstName;
                case "soyad": return r => r.LastName;
                case "tc" :
                case "tc kimlik":
                case "tckimlik": return r => r.TcKimlikNo;
                case "cinsiyet": return r => r.Gender;
                case "telefon":
                case "telefon numarası": return r => r.PhoneNumber;
                case "adres": return r => r.Address;
                case "kan grubu": return r => r.BloodType;
                case "hastalık":
                case "hastalık durumu": return r => r.HealthStatus;
                case "aşı":
                case "aşı durumu": return r => r.VaccineStatus;
                case "vize":
                case "vize talebi": return r => r.VisaRequest;
                case "bilet":
                case "bilet talebi": return r => r.TicketRequest;
                case "referans": return r => r.Reference;
                default: return null;
            }
        }

        private void DesignButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ListDesignDialog(ResultsDataGrid.Columns);
                dialog.Owner = Application.Current?.MainWindow;
                var res = dialog.ShowDialog();
                if (res != true) return;

                var options = dialog.ColumnOptions;
                if (options == null) return;

                // Apply ordering (DisplayIndex) and visibility based on returned options
                var ordered = options.OrderBy(o => o.Order).ToList();
                for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
                {
                    var opt = ordered[targetIndex];
                    var col = ResultsDataGrid.Columns.FirstOrDefault(c => string.Equals(c.Header?.ToString(), opt.Title, StringComparison.OrdinalIgnoreCase));
                    if (col == null) continue;
                    col.DisplayIndex = targetIndex;
                    col.Visibility = opt.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "CSV olarak kaydet", Filter = "CSV dosyası (*.csv)|*.csv", FileName = "genel-listesi.csv" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var visibleCols = ResultsDataGrid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
                var headers = visibleCols.Select(c => c.Header?.ToString() ?? string.Empty).ToList();
                using var sw = new StreamWriter(dlg.FileName, false, Encoding.UTF8);
                sw.WriteLine(string.Join(',', headers.Select(h => EscapeCsv(h))));

                var rows = ResultsDataGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
                foreach (var item in rows)
                {
                    var vals = new List<string>();
                    foreach (var col in visibleCols)
                    {
                        var header = col.Header?.ToString() ?? string.Empty;
                        var val = GetPropertyStringValue(item, header);
                        vals.Add(EscapeCsv(val));
                    }
                    sw.WriteLine(string.Join(',', vals));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private string GetPropertyStringValue(object item, string header)
        {
            if (item == null) return string.Empty;
            var prop = item.GetType().GetProperties().FirstOrDefault(p => string.Equals(p.Name, MapHeaderToProperty(header), StringComparison.OrdinalIgnoreCase));
            if (prop == null)
            {
                // fallback: try by header name
                prop = item.GetType().GetProperty(header.Replace(" ", ""), System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            if (prop == null) return string.Empty;
            var val = prop.GetValue(item);
            return val?.ToString() ?? string.Empty;
        }

        private string MapHeaderToProperty(string header)
        {
            return header?.Trim().ToLowerInvariant() switch
            {
                "sıra" => "Sequence",
                "ad" => "FirstName",
                "soyad" => "LastName",
                "tc kimlik" => "TcKimlikNo",
                "cinsiyet" => "Gender",
                "doğum tarihi" => "BirthDate",
                "telefon numarası" => "PhoneNumber",
                "adres" => "Address",
                "pasaport tipi" => "PassportType",
                "pasaport numarası" => "PassportNumber",
                "pasaport başlangıç tarihi" => "PassportStartDate",
                "pasaport bitiş tarihi" => "PassportEndDate",
                "kan grubu" => "BloodType",
                "aşı durumu" => "VaccineStatus",
                "hastalık durumu" => "HealthStatus",
                "yaş aralığı" => "AgeRange",
                "vize talebi" => "VisaRequest",
                "bilet talebi" => "TicketRequest",
                "kargo talebi" => "CargoRequest",
                "oda tipi" => "RoomType",
                "katılım yapılacak şehir" => "DepartureCity",
                "aktarma var mı ?" => "HasTransfer",
                "aktarma şehri" => "TransferCity",
                "ilçe" => "District",
                "toplam fiyat" => "TotalPrice",
                "para birimi" => "Currency",
                "ödenen" => "PaidAmount",
                "kalan" => "RemainingDebt",
                "referans" => "Reference",
                _ => header?.Replace(" ", "")
            };
        }

        private string EscapeCsv(string v)
        {
            if (v == null) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return '"' + v.Replace("\"", "\"\"") + '"';
            return v;
        }

        private class GeneralRow : System.ComponentModel.INotifyPropertyChanged
        {
            public int RegistrationId { get; set; }
            public int PersonId { get; set; }
            public int Sequence { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string TcKimlikNo { get; set; }
            public string Gender { get; set; }
            public string BirthDate { get; set; }
            public string PhoneNumber { get; set; }
            public string Address { get; set; }
            public string PassportType { get; set; }
            public string PassportNumber { get; set; }
            public string PassportStartDate { get; set; }
            public string PassportEndDate { get; set; }
            public string BloodType { get; set; }
            public string VaccineStatus { get; set; }
            public string HealthStatus { get; set; }
            public string AgeRange { get; set; }
            public string VisaRequest { get; set; }
            public string TicketRequest { get; set; }
            public string CargoRequest { get; set; }
            public string RoomType { get; set; }
            public string DepartureCity { get; set; }
            public string HasTransfer { get; set; }
            public string TransferCity { get; set; }
            public string District { get; set; }
            private string _totalPrice;
            public string TotalPrice
            {
                get => _totalPrice;
                set
                {
                    if (_totalPrice != value)
                    {
                        _totalPrice = value;
                        OnPropertyChanged(nameof(TotalPrice));
                        RecalculateRemainingFromStrings();
                    }
                }
            }
            public string Currency { get; set; }
            private string _paidAmount;
            public string PaidAmount
            {
                get => _paidAmount;
                set
                {
                    if (_paidAmount != value)
                    {
                        _paidAmount = value;
                        OnPropertyChanged(nameof(PaidAmount));
                        RecalculateRemainingFromStrings();
                    }
                }
            }
            private string _remainingDebt;
            public string RemainingDebt
            {
                get => _remainingDebt;
                set { if (_remainingDebt != value) { _remainingDebt = value; OnPropertyChanged(nameof(RemainingDebt)); } }
            }
            public string Reference { get; set; }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
            private void RecalculateRemainingFromStrings()
            {
                var t = ParseDecimalFlexible(_totalPrice);
                var p = ParseDecimalFlexible(_paidAmount);
                if (t.HasValue && p.HasValue)
                {
                    var rem = t.Value - p.Value;
                    var remStr = rem.ToString("0.##");
                    if (_remainingDebt != remStr)
                    {
                        _remainingDebt = remStr;
                        OnPropertyChanged(nameof(RemainingDebt));
                    }
                }
            }

            private decimal? ParseDecimalFlexible(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out v)) return v;
                var alt = s.Replace(',', '.');
                if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
                alt = s.Replace('.', ',');
                if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out v)) return v;
                return null;
            }
        }
    }
}
