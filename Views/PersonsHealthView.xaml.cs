using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsHealthView : Page
    {
        private sealed class ExportColumn
        {
            public string Header { get; init; }
            public string PropertyName { get; init; }
        }

        private static readonly IReadOnlyList<ExportColumn> ExportColumns = new[]
        {
            new ExportColumn { Header = "Ad", PropertyName = "FirstName" },
            new ExportColumn { Header = "Soyad", PropertyName = "LastName" },
            new ExportColumn { Header = "TC Kimlik", PropertyName = "TcKimlikNo" },
            new ExportColumn { Header = "Kan Grubu", PropertyName = "BloodType" },
            new ExportColumn { Header = "Hastalık Durumu", PropertyName = "HealthStatus" },
            new ExportColumn { Header = "Aşı Durumu", PropertyName = "VaccineStatus" }
        };

        private List<Person> _all;
        public List<string> BloodOptions { get; private set; }

        public PersonsHealthView()
        {
            InitializeComponent();
            LoadAll();
            Loaded += PersonsHealthView_Loaded;
            LoadBloodOptions();
            // subscribe to notifications in case PersonsWizardView updates underlying data
            Services.NotificationCenter.Subscribe(OnNotification);

            // Ensure Design button is visible next to Export button at runtime
            try
            {
                if (ExportButton != null)
                {
                    var parent = ExportButton.Parent as Grid;
                    if (parent != null)
                    {
                        // create horizontal stack panel in same grid cell
                        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

                        // remove existing ExportButton from grid children and add into stackpanel
                        parent.Children.Remove(ExportButton);
                        Grid.SetColumn(sp, 1);
                        sp.Children.Add(ExportButton);

                        // create design button and wire click to existing handler
                        var designBtn = new Button
                        {
                            Name = "DesignButton",
                            Content = "Listeyi tasarla",
                            Margin = new System.Windows.Thickness(12, 0, 0, 0),
                            Padding = new System.Windows.Thickness(14, 9, 14, 9),
                            FontSize = 14,
                            FontWeight = System.Windows.FontWeights.SemiBold,
                            Foreground = System.Windows.Media.Brushes.White,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#123046"),
                            BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#60A5FA"),
                            BorderThickness = new System.Windows.Thickness(1.2)
                        };
                        designBtn.Click += DesignButton_Click;
                        sp.Children.Add(designBtn);

                        parent.Children.Add(sp);
                    }
                }
            }
            catch
            {
                // ignore any runtime issues when manipulating visual tree
            }
        }

        private void OnNotification(string key, object payload)
        {
            if (key == "PersonUpdated")
            {
                // reload local list
                LoadAll();
            }
        }

        private void LoadBloodOptions()
        {
            using (var db = new AppDbContext())
            {
                BloodOptions = db.AppSettings.Where(x => x.Type == "Kan Grubu" && x.IsActive).OrderBy(x => x.SortOrder).Select(x => x.Value).ToList();
            }
            BloodOptions.Insert(0, "Seçiniz");
        }

        private void PersonsHealthView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchTextBox.Focus();
            }
            catch
            {
                // ignore
            }
        }

        private void LoadAll()
        {
            using (var db = new AppDbContext())
            {
                _all = db.Persons.OrderByDescending(x => x.Id).ToList();
                ResultsDataGrid.ItemsSource = _all;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = (SearchTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultsDataGrid.ItemsSource = _all;
                SearchHint.Visibility = Visibility.Visible;
                return;
            }

            SearchHint.Visibility = Visibility.Collapsed;

            // Normalize input for comparisons
            var q = text.ToLowerInvariant();

            // If input looks like a blood type (A, B, AB, O with optional + or -)
            bool isBlood = false;
            var t = q.Replace(" ", "");
            var bloodCandidates = new[] { "a", "b", "ab", "o" };
            if (t.Length >= 1 && t.Length <= 3)
            {
                var basePart = t;
                string sign = null;
                if (t.EndsWith("+") || t.EndsWith("-"))
                {
                    sign = t.Substring(t.Length - 1);
                    basePart = t.Substring(0, t.Length - 1);
                }

                if (bloodCandidates.Contains(basePart) && (sign == null || sign == "+" || sign == "-"))
                {
                    isBlood = true;
                    // normalize display form like "b+" -> "B+"
                    var normalized = (basePart + (sign ?? string.Empty)).ToUpperInvariant().Replace(" ", string.Empty);
                ResultsDataGrid.ItemsSource = _all.Where(p => !string.IsNullOrWhiteSpace(p.BloodType)
                                                             && (p.BloodType ?? string.Empty).ToUpperInvariant().Replace(" ", string.Empty).Contains(normalized))
                                                         .Select(p => new { p.FirstName, p.LastName, p.TcKimlikNo, BloodType = p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
                    return;
                }
            }

            // If contains letters and likely a disease/vaccine term
            // We'll exclude entries where the field is empty, "yok" or "belirtmedi" (turkish variations)
            var excludeValues = new[] { "", "yok", "belirtmedi", "belirtmedi.", "-" };

            var diseaseMatches = _all.Where(p => !string.IsNullOrWhiteSpace(p.HealthStatus)
                                                 && !excludeValues.Contains(p.HealthStatus.Trim().ToLowerInvariant())
                                                 && p.HealthStatus.ToLowerInvariant().Contains(q)).ToList();

            if (diseaseMatches.Any())
            {
                ResultsDataGrid.ItemsSource = diseaseMatches.Select(p => new { p.FirstName, p.LastName, p.TcKimlikNo, p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
                return;
            }

            var vaccineMatches = _all.Where(p => !string.IsNullOrWhiteSpace(p.VaccineStatus)
                                                 && !excludeValues.Contains(p.VaccineStatus.Trim().ToLowerInvariant())
                                                 && p.VaccineStatus.ToLowerInvariant().Contains(q)).ToList();

            if (vaccineMatches.Any())
            {
                ResultsDataGrid.ItemsSource = vaccineMatches.Select(p => new { p.FirstName, p.LastName, p.TcKimlikNo, p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
                return;
            }

            // fallback: try full-text match across the three fields
            var fallback = _all.Where(p => (p.BloodType ?? string.Empty).ToLowerInvariant().Contains(q)
                                           || (p.HealthStatus ?? string.Empty).ToLowerInvariant().Contains(q)
                                           || (p.VaccineStatus ?? string.Empty).ToLowerInvariant().Contains(q)).ToList();
            ResultsDataGrid.ItemsSource = fallback.Select(p => new { p.FirstName, p.LastName, p.TcKimlikNo, p.BloodType, HealthStatus = p.HealthStatus, VaccineStatus = p.VaccineStatus }).ToList();
        }

        private void ResultsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Commit edit first
            if (e.EditAction != DataGridEditAction.Commit) return;

            var row = e.Row.Item as Person;
            // when ItemsSource is anonymous list we need to map back by TC or name
            if (row == null)
            {
                // try to extract Tc from row cells
                dynamic dyn = e.Row.Item;
                string tc = dyn?.TcKimlikNo;
                if (string.IsNullOrWhiteSpace(tc)) return;
                using (var db = new AppDbContext())
                {
                    var person = db.Persons.FirstOrDefault(p => p.TcKimlikNo == tc);
                    if (person == null) return;
                    // map edited cell back by column
                    var col = e.Column.Header?.ToString();
                    if (col == "Hastalık Durumu")
                    {
                        var tb = e.EditingElement as TextBox;
                        person.HealthStatus = string.IsNullOrWhiteSpace(tb?.Text) ? "Belirtmedi" : tb.Text;
                    }
                    else if (col == "Aşı Durumu")
                    {
                        var tb = e.EditingElement as TextBox;
                        person.VaccineStatus = string.IsNullOrWhiteSpace(tb?.Text) ? "Belirtmedi" : tb.Text;
                    }
                    else if (col == "Kan Grubu")
                    {
                        var cb = e.EditingElement as ComboBox;
                        var val = cb?.SelectedValue as string;
                        person.BloodType = string.IsNullOrWhiteSpace(val) || string.Equals(val, "Seçiniz", System.StringComparison.OrdinalIgnoreCase) ? "Belirtmedi" : val;
                    }

                    db.SaveChanges();
                    Services.NotificationCenter.Publish("PersonUpdated", person.Id);
                    LoadAll();
                }
            }
            else
            {
                // editing when ItemsSource was real Person list
                using (var db = new AppDbContext())
                {
                    var person = db.Persons.FirstOrDefault(p => p.TcKimlikNo == row.TcKimlikNo);
                    if (person == null) return;
                    // columns already bound two-way => values copied
                    person.BloodType = string.IsNullOrWhiteSpace(row.BloodType) ? "Belirtmedi" : row.BloodType;
                    person.HealthStatus = string.IsNullOrWhiteSpace(row.HealthStatus) ? "Belirtmedi" : row.HealthStatus;
                    person.VaccineStatus = string.IsNullOrWhiteSpace(row.VaccineStatus) ? "Belirtmedi" : row.VaccineStatus;
                    db.SaveChanges();
                    Services.NotificationCenter.Publish("PersonUpdated", person.Id);
                    LoadAll();
                }
            }
        }

        private void ResultsDataGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // allow double-clicks on Kan Grubu column to begin edit (handled in MouseDoubleClick)
        }

        private void ResultsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is DataGridCell))
            {
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell)
            {
                var column = cell.Column;
                if (column != null && column.Header != null && column.Header.ToString() == "Kan Grubu")
                {
                    // begin edit for the row
                    var row = DataGridRow.GetRowContainingElement(cell);
                    if (row != null)
                    {
                        ResultsDataGrid.BeginEdit();
                    }
                }
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (ExportButton.ContextMenu != null)
            {
                ExportButton.ContextMenu.PlacementTarget = ExportButton;
                ExportButton.ContextMenu.IsOpen = true;
            }
        }

        private void ExportWord_Click(object sender, RoutedEventArgs e)
        {
            var path = AskForPath("Word belgesi", "Word dosyası (*.docx)|*.docx", "saglik-listesi.docx");
            if (path == null) return;
            ExportWord(NormalizeExtension(path, ".docx"));
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var path = AskForPath("Excel çalışma kitabı", "Excel dosyası (*.xlsx)|*.xlsx", "saglik-listesi.xlsx");
            if (path == null) return;
            ExportExcel(NormalizeExtension(path, ".xlsx"));
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var path = AskForPath("PDF belgesi", "PDF dosyası (*.pdf)|*.pdf", "saglik-listesi.pdf");
            if (path == null) return;
            ExportPdf(NormalizeExtension(path, ".pdf"));
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

                // Apply visibility: match by header text order
                for (int i = 0; i < ResultsDataGrid.Columns.Count && i < options.Count; i++)
                {
                    ResultsDataGrid.Columns[i].Visibility = options[i].IsVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
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
            var cols = GetActiveExportColumns();
            var rows = GetExportRows(cols);
            DeleteIfExists(filePath);
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddDocxContentTypes(archive);
            AddDocxRels(archive);
            AddDocxDocumentRels(archive);
            AddDocxDocumentXml(archive, rows, cols);
        }

        private void ExportExcel(string filePath)
        {
            var cols = GetActiveExportColumns();
            var rows = GetExportRows(cols);
            DeleteIfExists(filePath);
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddXlsxContentTypes(archive);
            AddXlsxRels(archive);
            AddXlsxWorkbook(archive);
            AddXlsxWorkbookRels(archive);
            AddXlsxStyles(archive);
            AddXlsxSheet(archive, rows, cols);
        }

        private void ExportPdf(string filePath)
        {
            var cols = GetActiveExportColumns();
            var rows = GetExportRows(cols);
            DeleteIfExists(filePath);
            var pdf = BuildSimplePdf(rows, cols);
            File.WriteAllBytes(filePath, pdf);
        }
        private List<Dictionary<string, string>> GetExportRows(IReadOnlyList<ExportColumn> columns)
        {
            var source = ResultsDataGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
            // build dictionary per row using only requested columns
            return source.Select(item => columns.ToDictionary(
                    column => column.Header,
                    column => GetPropertyValue(item, column.PropertyName)))
                .ToList();
        }

        // Build active export column list based on current DataGrid column visibility and order
        private IReadOnlyList<ExportColumn> GetActiveExportColumns()
        {
            var cols = new List<ExportColumn>();
            foreach (var dgCol in ResultsDataGrid.Columns)
            {
                if (dgCol.Visibility != System.Windows.Visibility.Visible) continue;
                var header = dgCol.Header?.ToString() ?? string.Empty;
                // map header to known export columns
                var match = ExportColumns.FirstOrDefault(c => string.Equals(c.Header, header, System.StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    cols.Add(match);
                }
            }
            // fallback: if none matched (rare), return all
            if (!cols.Any()) return ExportColumns;
            return cols;
        }

        private string GetPropertyValue(object item, string propertyName)
        {
            if (item == null) return string.Empty;

            var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null) return string.Empty;
            var value = property.GetValue(item);
            return value?.ToString() ?? string.Empty;
        }

        private void OpenFile(string filePath)
        {
            // intentionally not auto-opening exports; user opens them manually
        }

        private void DeleteIfExists(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private void AddDocxContentTypes(ZipArchive archive)
        {
            var content = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/word/document.xml"" ContentType=""application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml""/>
</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private void AddDocxRels(ZipArchive archive)
        {
            var rels = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""R1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""word/document.xml""/>
</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
        }

        private void AddDocxDocumentRels(ZipArchive archive)
        {
            AddZipEntry(archive, "word/_rels/document.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""/>");
        }

        private void AddDocxDocumentXml(ZipArchive archive, List<Dictionary<string, string>> rows, IReadOnlyList<ExportColumn> columns)
        {
            var headers = columns.Select(c => c.Header).ToList();
            var tableRows = new List<string>
            {
                BuildWordTableRow(headers, true)
            };

            tableRows.AddRange(rows.Select(row => BuildWordTableRow(headers.Select(h => row.ContainsKey(h) ? row[h] : string.Empty).ToList(), false)));

            var document = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    <w:p><w:r><w:t>Sağlık Listesi</w:t></w:r></w:p>
    <w:tbl>
      {string.Join(Environment.NewLine, tableRows)}
    </w:tbl>
    <w:sectPr>
      <w:pgSz w:w=""16838"" w:h=""11906""/>
      <w:pgMar w:top=""720"" w:right=""720"" w:bottom=""720"" w:left=""720"" w:header=""450"" w:footer=""450"" w:gutter=""0""/>
    </w:sectPr>
  </w:body>
</w:document>";
            AddZipEntry(archive, "word/document.xml", document);
        }

        private string BuildWordTableRow(IReadOnlyList<string> values, bool header)
        {
            var cells = values.Select(value =>
                $@"<w:tc><w:tcPr>{(header ? @"<w:shd w:fill=""22324A""/>" : string.Empty)}</w:tcPr><w:p><w:r><w:t>{EscapeXml(value ?? string.Empty)}</w:t></w:r></w:p></w:tc>");
            return "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
        }

        private void AddXlsxContentTypes(ZipArchive archive)
        {
            var content = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
  <Override PartName=""/docProps/core.xml"" ContentType=""application/vnd.openxmlformats-package.core-properties+xml""/>
  <Override PartName=""/docProps/app.xml"" ContentType=""application/vnd.openxmlformats-officedocument.extended-properties+xml""/>
</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private void AddXlsxRels(ZipArchive archive)
        {
            var rels = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
            AddZipEntry(archive, "docProps/core.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:dcterms=""http://purl.org/dc/terms/"" xmlns:dcmitype=""http://purl.org/dc/dcmitype/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <dc:title>Sağlık Listesi</dc:title>
  <dc:creator>TurizmOperasyonSistemi</dc:creator>
  <cp:lastModifiedBy>TurizmOperasyonSistemi</cp:lastModifiedBy>
  <dcterms:created xsi:type=""dcterms:W3CDTF"">2026-06-01T00:00:00Z</dcterms:created>
  <dcterms:modified xsi:type=""dcterms:W3CDTF"">2026-06-01T00:00:00Z</dcterms:modified>
</cp:coreProperties>");
            AddZipEntry(archive, "docProps/app.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Properties xmlns=""http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"" xmlns:vt=""http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"">
  <Application>TurizmOperasyonSistemi</Application>
  <HeadingPairs>
    <vt:vector size=""2"" baseType=""variant"">
      <vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant>
      <vt:variant><vt:i4>1</vt:i4></vt:variant>
    </vt:vector>
  </HeadingPairs>
  <TitlesOfParts>
    <vt:vector size=""1"" baseType=""lpstr"">
      <vt:lpstr>Sağlık Listesi</vt:lpstr>
    </vt:vector>
  </TitlesOfParts>
</Properties>");
        }

        private void AddXlsxWorkbook(ZipArchive archive)
        {
            var workbook = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <bookViews>
    <workbookView activeTab=""0""/>
  </bookViews>
  <sheets>
    <sheet name=""Saglik Listesi"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
  <calcPr calcId=""124519"" fullCalcOnLoad=""1""/>
</workbook>";
            AddZipEntry(archive, "xl/workbook.xml", workbook);
        }

        private void AddXlsxWorkbookRels(ZipArchive archive)
        {
            var rels = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>";
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", rels);
        }

        private void AddXlsxStyles(ZipArchive archive)
        {
            var styles = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2"">
    <font><sz val=""11""/><name val=""Calibri""/></font>
    <font><b/><sz val=""11""/><name val=""Calibri""/></font>
  </fonts>
  <fills count=""2"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FF22324A""/><bgColor indexed=""64""/></patternFill></fill>
  </fills>
  <borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
  <cellXfs count=""2"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""1"" fillId=""1"" borderId=""0"" xfId=""0"" applyFill=""1"" applyFont=""1""/>
  </cellXfs>
</styleSheet>";
            AddZipEntry(archive, "xl/styles.xml", styles);
        }

        private void AddXlsxSheet(ZipArchive archive, List<Dictionary<string, string>> rows, IReadOnlyList<ExportColumn> columns)
        {
            var rowXml = new List<string>();
            // compute widths using headers and values for selected columns
            var headers = columns.Select(c => c.Header).ToList();
            var widths = GetExcelColumnWidths(rows, headers);
            // attempt to use DataGrid actual widths if available
            try
            {
                if (ResultsDataGrid != null && ResultsDataGrid.Columns != null)
                {
                    var visibleCols = ResultsDataGrid.Columns.Where(c => c.Visibility == System.Windows.Visibility.Visible).ToList();
                    for (var i = 0; i < Math.Min(widths.Count, visibleCols.Count); i++)
                    {
                        var w = visibleCols[i].ActualWidth;
                        if (!double.IsNaN(w) && w > 0)
                        {
                            widths[i] = Math.Min(42, Math.Max(widths[i], w / 7)); // heuristic: pixels to excel width units
                        }
                    }
                }
            }
            catch
            {
                // ignore if UI not available
            }

            // build rows (header + data)
            rowXml.Add(BuildXlsxRow(1, headers, header: true));
            for (var i = 0; i < rows.Count; i++)
            {
                rowXml.Add(BuildXlsxRow(i + 2, headers.Select(h => rows[i].ContainsKey(h) ? rows[i][h] : string.Empty).ToList(), header: false));
            }

            var lastColumn = ColumnName(headers.Count);
            var lastRow = Math.Max(1, rows.Count + 1);
            var sheet = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <dimension ref=""A1:{lastColumn}{lastRow}""/>
  <sheetViews>
    <sheetView workbookViewId=""0""/>
  </sheetViews>
  <sheetData>
    {string.Join(Environment.NewLine, rowXml)}
  </sheetData>
  <autoFilter ref=""A1:{lastColumn}{lastRow}""/>
</worksheet>";
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }

        private List<double> GetExcelColumnWidths(List<Dictionary<string, string>> rows, List<string> headers)
        {
            var widths = headers.Select(h => (double)Math.Max(h.Length + 4, 14)).ToList();
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var key = headers[i];
                    var val = row.ContainsKey(key) ? row[key] : string.Empty;
                    var valueLength = (val ?? string.Empty).Length + 2;
                    widths[i] = Math.Min(42, Math.Max(widths[i], valueLength));
                }
            }
            return widths;
        }

        private string BuildXlsxRow(int rowNumber, IReadOnlyList<string> values, bool header)
        {
            var cells = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var cellRef = $"{ColumnName(i + 1)}{rowNumber}";
                var style = header ? " s=\"1\"" : string.Empty;
                cells.Add($@"<c r=""{cellRef}"" t=""inlineStr""{style}><is><t>{EscapeXml(values[i] ?? string.Empty)}</t></is></c>");
            }

            return $@"<row r=""{rowNumber}"">{string.Join(string.Empty, cells)}</row>";
        }

        private byte[] BuildSimplePdf(List<Dictionary<string, string>> rows, IReadOnlyList<ExportColumn> columns)
        {
            var pageWidth = 842;
            var pageHeight = 595;
            var margin = 28;
            var top = 40;
            var rowHeight = 22;
            var usableWidth = pageWidth - margin * 2;
            var columnWidth = usableWidth / columns.Count;

            var content = new List<string>
            {
                $"BT /F1 16 Tf 40 560 Td ({EscapePdfText("Sağlık Listesi")}) Tj ET"
            };

            var y = 520;
            var headers = columns.Select(c => c.Header).ToList();
            content.AddRange(BuildPdfRow(headers, margin, y, columnWidth, rowHeight, true));
            y -= rowHeight;

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
                var top = y + rowHeight;
                // for header row draw filled rectangle with stroke and white text
                var text = EscapePdf(values[i] ?? string.Empty);
                var textX = left + 4;
                var textY = y + 7;
                if (header)
                {
                    // header color #22324A -> normalized
                    var rf = 34.0/255.0;
                    var gf = 50.0/255.0;
                    var bf = 74.0/255.0;
                    // set fill color (use invariant culture to force dot decimal separator)
                    yield return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} rg", rf, gf, bf);
                    // set stroke color to black
                    yield return "0 0 0 RG";
                    // fill and stroke rectangle
                    yield return $"{left} {y} {right - left} {rowHeight} re B";
                    // set text color to white
                    yield return "1 1 1 rg";
                    yield return $"BT /F2 9 Tf {textX} {textY} Td ({EscapePdfText(values[i] ?? string.Empty)}) Tj ET";
                    // reset fill color to black for subsequent operations
                    yield return "0 0 0 rg";
                }
                else
                {
                    // ensure stroke is black and draw border
                    yield return "0 0 0 RG";
                    yield return $"{left} {y} {right - left} {rowHeight} re S";
                    // text color black
                    yield return "0 0 0 rg";
                    yield return $"BT /F1 8 Tf {textX} {textY} Td ({EscapePdfText(values[i] ?? string.Empty)}) Tj ET";
                }
            }
        }

        private byte[] BuildMinimalPdfBytes(int pageWidth, int pageHeight, string content)
        {
            var objects = new List<string>();
            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
            objects.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

            var streamBytes = System.Text.Encoding.ASCII.GetBytes(content);
            var contentObject = $"<< /Length {streamBytes.Length} >>\nstream\n{content}\nendstream";
            objects.Add(contentObject);

            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, 1024, leaveOpen: true);
            writer.WriteLine("%PDF-1.4");

            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                writer.WriteLine($"{i + 1} 0 obj");
                writer.WriteLine(objects[i]);
                writer.WriteLine("endobj");
            }

            writer.Flush();
            var xrefPosition = ms.Position;
            writer.WriteLine("xref");
            writer.WriteLine($"0 {objects.Count + 1}");
            writer.WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets)
            {
                writer.WriteLine($"{offset:0000000000} 00000 n ");
            }
            writer.WriteLine("trailer");
            writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
            writer.WriteLine("startxref");
            writer.WriteLine(xrefPosition);
            writer.WriteLine("%%EOF");
            writer.Flush();
            return ms.ToArray();
        }

        private void AddZipEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.Write(content);
        }

        private string EscapeXml(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }

        private string EscapePdf(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("(", "\\(")
                .Replace(")", "\\)");
        }

        private string EscapePdfText(string value)
        {
            return EscapePdf(TransliterateTurkish(value));
        }

        private string EscapeHtml(string value)
        {
            return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private string TransliterateTurkish(string value)
        {
            return (value ?? string.Empty)
                .Replace("İ", "I")
                .Replace("ı", "i")
                .Replace("Ş", "S")
                .Replace("ş", "s")
                .Replace("Ğ", "G")
                .Replace("ğ", "g")
                .Replace("Ü", "U")
                .Replace("ü", "u")
                .Replace("Ö", "O")
                .Replace("ö", "o")
                .Replace("Ç", "C")
                .Replace("ç", "c");
        }

        private string ColumnName(int index)
        {
            var dividend = index;
            var columnName = string.Empty;
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }
            return columnName;
        }
    }
}
