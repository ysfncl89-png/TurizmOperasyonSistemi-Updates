using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;

namespace TurizmOperasyonSistemi.Services
{
    public static class ExportHelper
    {
        public static void ExportWord(string filePath, List<Dictionary<string, string>> rows, List<string> headers, string title = "Liste")
        {
            if (rows == null) rows = new List<Dictionary<string, string>>();
            if (headers == null) headers = new List<string>();
            if (File.Exists(filePath)) File.Delete(filePath);
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddDocxContentTypes(archive);
            AddDocxRels(archive);
            AddDocxDocumentRels(archive);
            AddDocxDocumentXml(archive, rows, headers, title);
        }

        public static void ExportExcel(string filePath, List<Dictionary<string, string>> rows, List<string> headers, string title = "Liste")
        {
            if (rows == null) rows = new List<Dictionary<string, string>>();
            if (headers == null) headers = new List<string>();
            if (File.Exists(filePath)) File.Delete(filePath);
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddXlsxContentTypes(archive);
            AddXlsxRels(archive);
            AddXlsxWorkbook(archive);
            AddXlsxWorkbookRels(archive);
            AddXlsxStyles(archive);
            AddXlsxSheet(archive, rows, headers, title);
        }

        public static void ExportPdf(string filePath, List<Dictionary<string, string>> rows, List<string> headers, string title = "Liste")
        {
            if (rows == null) rows = new List<Dictionary<string, string>>();
            if (headers == null) headers = new List<string>();
            if (File.Exists(filePath)) File.Delete(filePath);
            var pdf = BuildSimplePdf(rows, headers, title);
            File.WriteAllBytes(filePath, pdf);
        }

        // --- DOCX helpers ---
        private static void AddDocxContentTypes(ZipArchive archive)
        {
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\n</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private static void AddDocxRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"R1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
        }

        private static void AddDocxDocumentRels(ZipArchive archive)
        {
            AddZipEntry(archive, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        }

        private static void AddDocxDocumentXml(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers, string title)
        {
            var tableRows = new List<string> { BuildWordTableRow(headers, true) };
            tableRows.AddRange(rows.Select(r => BuildWordTableRow(headers.Select(h => r.ContainsKey(h) ? r[h] : string.Empty).ToList(), false)));
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n  <w:body>\n    <w:p><w:r><w:t>{EscapeXml(title)}</w:t></w:r></w:p>\n    <w:tbl>\n      {string.Join(Environment.NewLine, tableRows)}\n    </w:tbl>\n    <w:sectPr>\n      <w:pgSz w:w=\"16838\" w:h=\"11906\"/>\n      <w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"450\" w:footer=\"450\" w:gutter=\"0\"/>\n    </w:sectPr>\n  </w:body>\n</w:document>";
            AddZipEntry(archive, "word/document.xml", document);
        }

        private static string BuildWordTableRow(IReadOnlyList<string> values, bool header)
        {
            var cells = values.Select(value => $"<w:tc><w:tcPr>{(header ? "<w:shd w:fill=\"22324A\"/>" : string.Empty)}</w:tcPr><w:p><w:r><w:t>{EscapeXml(value ?? string.Empty)}</w:t></w:r></w:p></w:tc>");
            return "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
        }

        // --- XLSX helpers ---
        private static void AddXlsxContentTypes(ZipArchive archive)
        {
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\n  <Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>\n  <Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>\n</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private static void AddXlsxRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
            AddZipEntry(archive, "docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">  <dc:title>Genel Liste</dc:title></cp:coreProperties>");
            AddZipEntry(archive, "docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>TurizmOperasyonSistemi</Application><TitlesOfParts><vt:vector size=\"1\" baseType=\"lpstr\"><vt:lpstr>Genel Liste</vt:lpstr></vt:vector></TitlesOfParts></Properties>");
        }

        private static void AddXlsxWorkbook(ZipArchive archive)
        {
            var workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <bookViews>\n    <workbookView activeTab=\"0\"/>\n  </bookViews>\n  <sheets>\n    <sheet name=\"Genel Liste\" sheetId=\"1\" r:id=\"rId1\"/>\n  </sheets>\n</workbook>";
            AddZipEntry(archive, "xl/workbook.xml", workbook);
        }

        private static void AddXlsxWorkbookRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", rels);
        }

        private static void AddXlsxStyles(ZipArchive archive)
        {
            var styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF22324A\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"1\" borderId=\"0\" xfId=\"0\" applyFill=\"1\" applyFont=\"1\"/></cellXfs></styleSheet>";
            AddZipEntry(archive, "xl/styles.xml", styles);
        }

        private static void AddXlsxSheet(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers, string title)
        {
            var rowXml = new List<string>();
            // build rows (header + data)
            rowXml.Add(BuildXlsxRow(1, headers, true));
            for (var i = 0; i < rows.Count; i++)
            {
                rowXml.Add(BuildXlsxRow(i + 2, headers.Select(h => rows[i].ContainsKey(h) ? rows[i][h] : string.Empty).ToList(), false));
            }
            var lastColumn = ColumnName(headers.Count);
            var lastRow = Math.Max(1, rows.Count + 1);
            var sheet = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <dimension ref=\"A1:{lastColumn}{lastRow}\"/>\n  <sheetViews>\n    <sheetView workbookViewId=\"0\"/>\n  </sheetViews>\n  <sheetData>\n    {string.Join(Environment.NewLine, rowXml)}\n  </sheetData>\n  <autoFilter ref=\"A1:{lastColumn}{lastRow}\"/>\n</worksheet>";
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }

        private static string BuildXlsxRow(int rowNumber, IReadOnlyList<string> values, bool header)
        {
            var cells = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var cellRef = $"{ColumnName(i + 1)}{rowNumber}";
                var style = header ? " s=\"1\"" : string.Empty;
                cells.Add($"<c r=\"{cellRef}\" t=\"inlineStr\"{style}><is><t>{EscapeXml(values[i] ?? string.Empty)}</t></is></c>");
            }
            return $"<row r=\"{rowNumber}\">{string.Join(string.Empty, cells)}</row>";
        }

        // --- PDF builders ---
        private static byte[] BuildSimplePdf(List<Dictionary<string, string>> rows, List<string> headers, string title)
        {
            var pageWidth = 842; var pageHeight = 595; var margin = 28; var top = 40; var rowHeight = 22; var usableWidth = pageWidth - margin * 2; var columnWidth = usableWidth / Math.Max(1, headers.Count);
            var content = new List<string> { $"BT /F1 16 Tf 40 560 Td ({EscapePdfText(title)}) Tj ET" };
            var y = 520; var headersList = headers.ToList();
            content.AddRange(BuildPdfRow(headersList, margin, y, columnWidth, rowHeight, true)); y -= rowHeight;
            foreach (var row in rows)
            {
                if (y < 50) break;
                content.AddRange(BuildPdfRow(headersList.Select(h => row.ContainsKey(h) ? row[h] : string.Empty).ToList(), margin, y, columnWidth, rowHeight, false));
                y -= rowHeight;
            }
            var contentText = string.Join("\n", content);
            return BuildMinimalPdfBytes(pageWidth, pageHeight, contentText);
        }

        private static IEnumerable<string> BuildPdfRow(IReadOnlyList<string> values, int x, int y, double columnWidth, int rowHeight, bool header)
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

        private static byte[] BuildMinimalPdfBytes(int pageWidth, int pageHeight, string content)
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

        private static void AddZipEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.Write(content);
        }

        private static string EscapeXml(string value) { return SecurityElement.Escape(value) ?? string.Empty; }
        private static string EscapePdf(string value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)"); }
        private static string EscapePdfText(string value) { return EscapePdf(TransliterateTurkish(value)); }
        private static string TransliterateTurkish(string value)
        {
            return (value ?? string.Empty).Replace("İ", "I").Replace("ı", "i").Replace("Ş", "S").Replace("ş", "s").Replace("Ğ", "G").Replace("ğ", "g").Replace("Ü", "U").Replace("ü", "u").Replace("Ö", "O").Replace("ö", "o").Replace("Ç", "C").Replace("ç", "c");
        }

        private static string ColumnName(int index)
        {
            var dividend = index; var columnName = string.Empty;
            while (dividend > 0) { var modulo = (dividend - 1) % 26; columnName = Convert.ToChar(65 + modulo) + columnName; dividend = (dividend - modulo) / 26; }
            return columnName;
        }
    }
}
