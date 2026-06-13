using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Globalization;

namespace TurizmOperasyonSistemi.Services
{
    public static class ExportHelper
    {
        public static void ExportWord(string filePath, List<Dictionary<string, string>> rows, List<string> headers, string title = "Liste", List<string> footers = null)
        {
            if (rows == null) rows = new List<Dictionary<string, string>>();
            if (headers == null) headers = new List<string>();
            if (File.Exists(filePath)) File.Delete(filePath);
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddDocxContentTypes(archive);
            AddDocxRels(archive);
            AddDocxDocumentRels(archive);
            AddDocxDocumentXml(archive, rows, headers, title, footers);
        }

        public static void ExportExcel(
            string filePath,
            List<Dictionary<string, string>> rows,
            List<string> headers,
            string title = "Liste",
            List<string> footers = null,
            string hotelName = null,
            string checkInDate = null,
            string checkOutDate = null,
            string tourType = null,
            string period = null,
            string groupType = null,
            string daysCount = null)
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
            AddXlsxSheet(archive, rows, headers, title, footers, hotelName, checkInDate, checkOutDate, tourType, period, groupType, daysCount);
        }

        public static void ExportPdf(
            string filePath,
            List<Dictionary<string, string>> rows,
            List<string> headers,
            string title = "Liste",
            List<string> footers = null,
            string hotelName = null,
            string checkInDate = null,
            string checkOutDate = null,
            string tourType = null,
            string period = null,
            string groupType = null,
            string daysCount = null)
        {
            if (rows == null) rows = new List<Dictionary<string, string>>();
            if (headers == null) headers = new List<string>();
            if (File.Exists(filePath)) File.Delete(filePath);
            var pdf = BuildSimplePdf(rows, headers, title, footers, hotelName, checkInDate, checkOutDate, tourType, period, groupType, daysCount);
            File.WriteAllBytes(filePath, pdf);
        }

        public static void ExportRoomAllocationWord(string filePath, List<Dictionary<string, string>> rows, string hotelName, string checkInDate, string checkOutDate, string tourType, string period, string groupType, string daysCount)
        {
            RoomAllocationWordExporter.Export(filePath, rows, hotelName, checkInDate, checkOutDate, tourType, period, groupType, daysCount);
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

        private static void AddRoomAllocationDocumentXml(ZipArchive archive, List<Dictionary<string, string>> rows, string hotelName, string checkInDate, string checkOutDate)
        {
            var headers = new List<string>
            {
                "Oda No",
                "Oda Türü",
                "Kapasite",
                "Yerleşen Kişi Sayısı",
                "Yerleşen Kişiler",
                "Connection"
            };

            var roomTypeSummary = rows
                .GroupBy(row => NormalizeRoomType(row.ContainsKey("Oda Türü") ? row["Oda Türü"] : string.Empty))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => new
                {
                    RoomType = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => GetRoomTypeSortOrder(item.RoomType))
                .ToList();

            var totalRooms = rows.Count;
            var summaryLine = roomTypeSummary.Count > 0
                ? string.Join(", ", roomTypeSummary.Select(item => $"{item.Count} {item.RoomType}"))
                : "-";

            var topInfoTable = BuildTopInfoTable(hotelName, checkInDate, checkOutDate);
            var mainTable = BuildRoomTable(rows, headers);
            var summarySection = BuildSummarySection(roomTypeSummary, totalRooms, summaryLine);

            var document = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    {topInfoTable}
    <w:p><w:r><w:t xml:space=""preserve""> </w:t></w:r></w:p>
    {mainTable}
    <w:p><w:r><w:t xml:space=""preserve""> </w:t></w:r></w:p>
    {summarySection}
    <w:sectPr>
      <w:pgSz w:w=""16838"" w:h=""11906"" w:orient=""landscape""/>
      <w:pgMar w:top=""540"" w:right=""540"" w:bottom=""540"" w:left=""540"" w:header=""400"" w:footer=""400"" w:gutter=""0""/>
    </w:sectPr>
  </w:body>
</w:document>";

            AddZipEntry(archive, "word/document.xml", document);
        }

        private static string BuildTopInfoTable(string hotelName, string checkInDate, string checkOutDate)
        {
            var widths = new[] { 5100, 5100, 5100 };
            var cells = new[]
            {
                BuildInfoCardCell("OTEL", string.IsNullOrWhiteSpace(hotelName) ? "-" : hotelName, widths[0]),
                BuildInfoCardCell("GİRİŞ TARİHİ", string.IsNullOrWhiteSpace(checkInDate) ? "-" : checkInDate, widths[1]),
                BuildInfoCardCell("ÇIKIŞ TARİHİ", string.IsNullOrWhiteSpace(checkOutDate) ? "-" : checkOutDate, widths[2])
            };

            return $@"<w:tbl>
  <w:tblPr>
    <w:tblW w:w=""15300"" w:type=""dxa""/>
    <w:tblLayout w:type=""fixed""/>
    <w:tblBorders>
      <w:top w:val=""nil""/>
      <w:left w:val=""nil""/>
      <w:bottom w:val=""nil""/>
      <w:right w:val=""nil""/>
      <w:insideH w:val=""nil""/>
      <w:insideV w:val=""nil""/>
    </w:tblBorders>
  </w:tblPr>
  <w:tblGrid>
    <w:gridCol w:w=""{widths[0]}""/>
    <w:gridCol w:w=""{widths[1]}""/>
    <w:gridCol w:w=""{widths[2]}""/>
  </w:tblGrid>
  <w:tr>
    {cells[0]}
    {cells[1]}
    {cells[2]}
  </w:tr>
</w:tbl>";
        }

        private static string BuildInfoCardCell(string label, string value, int width)
        {
            var content = BuildParagraph(label, "center", "64748B", 18, true)
                + BuildParagraph(value, "center", "1E293B", 24, false);
            return BuildTableCell(content, width, "FFFFFF", "D8E0EC", "center");
        }

        private static string BuildRoomTable(List<Dictionary<string, string>> rows, IReadOnlyList<string> headers)
        {
            var widths = new[] { 1200, 1600, 1000, 1800, 6200, 1400 };
            var headerCells = headers.Select((header, index) => BuildTableCell(BuildParagraph(header.ToUpperInvariant(), "center", "FFFFFF", 18, true), widths[index], "173B72", "173B72", "center"));
            var tableRows = new List<string>
            {
                $"<w:tr>{string.Join(string.Empty, headerCells)}</w:tr>"
            };

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var fill = rowIndex % 2 == 0 ? "FFFFFF" : "F8FBFF";
                var cells = new List<string>();

                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.ContainsKey(header) ? row[header] : string.Empty;
                    if (header == "Yerleşen Kişiler")
                    {
                        value = FormatOccupantNamesForWord(value);
                    }

                    if (header == "Connection")
                    {
                        cells.Add(BuildConnectionCell(value, widths[i], fill));
                        continue;
                    }

                    var align = header == "Yerleşen Kişiler" ? "left" : "center";
                    var content = BuildParagraph(string.IsNullOrWhiteSpace(value) ? "-" : value, align, "1F2937", 18, false);
                    cells.Add(BuildTableCell(content, widths[i], fill, "D8E0EC", align));
                }

                tableRows.Add($"<w:tr>{string.Join(string.Empty, cells)}</w:tr>");
            }

            return $@"<w:tbl>
  <w:tblPr>
    <w:tblW w:w=""15300"" w:type=""dxa""/>
    <w:tblLayout w:type=""fixed""/>
    <w:tblBorders>
      <w:top w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:left w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:bottom w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:right w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:insideH w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:insideV w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
    </w:tblBorders>
  </w:tblPr>
  <w:tblGrid>
    <w:gridCol w:w=""{widths[0]}""/>
    <w:gridCol w:w=""{widths[1]}""/>
    <w:gridCol w:w=""{widths[2]}""/>
    <w:gridCol w:w=""{widths[3]}""/>
    <w:gridCol w:w=""{widths[4]}""/>
    <w:gridCol w:w=""{widths[5]}""/>
  </w:tblGrid>
  {string.Join(Environment.NewLine, tableRows)}
</w:tbl>";
        }

        private static string BuildConnectionCell(string value, int width, string fill)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            var isNo = text.Equals("Hayır", StringComparison.OrdinalIgnoreCase);
            var color = isNo ? "EF4444" : "2563EB";
            var content = BuildParagraph(text, "center", color, 18, false);
            var cellFill = isNo ? "FFF1F2" : "EEF4FF";
            return BuildTableCell(content, width, cellFill, "D8E0EC", "center");
        }

        private static string BuildSummarySection(IReadOnlyList<dynamic> roomTypeSummary, int totalRooms, string summaryLine)
        {
            var leftContent = BuildSummaryDistributionBlock(roomTypeSummary, totalRooms);
            var rightContent = BuildTotalRoomBlock(totalRooms, summaryLine);

            return $@"<w:tbl>
  <w:tblPr>
    <w:tblW w:w=""15300"" w:type=""dxa""/>
    <w:tblLayout w:type=""fixed""/>
    <w:tblBorders>
      <w:top w:val=""nil""/>
      <w:left w:val=""nil""/>
      <w:bottom w:val=""nil""/>
      <w:right w:val=""nil""/>
      <w:insideH w:val=""nil""/>
      <w:insideV w:val=""nil""/>
    </w:tblBorders>
  </w:tblPr>
  <w:tblGrid>
    <w:gridCol w:w=""7600""/>
    <w:gridCol w:w=""7600""/>
  </w:tblGrid>
  <w:tr>
    {BuildTableCell(leftContent, 7600, "FFFFFF", "D8E0EC", "center")}
    {BuildTableCell(rightContent, 7600, "F8FBFF", "D8E0EC", "center")}
  </w:tr>
</w:tbl>";
        }

        private static string BuildSummaryDistributionBlock(IReadOnlyList<dynamic> roomTypeSummary, int totalRooms)
        {
            var header = BuildParagraph("ODA DAĞILIM ÖZETİ", "center", "173B72", 20, true);
            var rows = new List<string>();
            var widths = new[] { 4200, 1200 };

            rows.Add($@"<w:tr>
  {BuildTableCell(BuildParagraph("ODA TÜRÜ", "center", "173B72", 16, true), widths[0], "EEF4FF", "D8E0EC", "center")}
  {BuildTableCell(BuildParagraph("ODA SAYISI", "center", "173B72", 16, true), widths[1], "EEF4FF", "D8E0EC", "center")}
</w:tr>");

            foreach (var item in roomTypeSummary)
            {
                rows.Add($@"<w:tr>
  {BuildTableCell(BuildParagraph(item.RoomType, "center", "1F2937", 16, false), widths[0], "FFFFFF", "D8E0EC", "center")}
  {BuildTableCell(BuildParagraph(item.Count.ToString(), "center", "1F2937", 16, false), widths[1], "FFFFFF", "D8E0EC", "center")}
</w:tr>");
            }

            rows.Add($@"<w:tr>
  {BuildTableCell(BuildParagraph("TOPLAM ODA", "center", "173B72", 16, true), widths[0], "DCE8F8", "D8E0EC", "center")}
  {BuildTableCell(BuildParagraph(totalRooms.ToString(), "center", "173B72", 16, true), widths[1], "DCE8F8", "D8E0EC", "center")}
</w:tr>");

            return $@"<w:tbl>
  <w:tblPr>
    <w:tblW w:w=""5400"" w:type=""dxa""/>
    <w:tblLayout w:type=""fixed""/>
    <w:tblBorders>
      <w:top w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:left w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:bottom w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:right w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:insideH w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
      <w:insideV w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
    </w:tblBorders>
  </w:tblPr>
  <w:tblGrid>
    <w:gridCol w:w=""5400""/>
  </w:tblGrid>
  <w:tr>
    {BuildTableCell(header, 5400, "FFFFFF", "FFFFFF", "center")}
  </w:tr>
  <w:tr>
    {BuildTableCell($@"<w:tbl>
      <w:tblPr>
        <w:tblW w:w=""5400"" w:type=""dxa""/>
        <w:tblLayout w:type=""fixed""/>
        <w:tblBorders>
          <w:top w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
          <w:left w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
          <w:bottom w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
          <w:right w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
          <w:insideH w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
          <w:insideV w:val=""single"" w:sz=""6"" w:color=""D8E0EC""/>
        </w:tblBorders>
      </w:tblPr>
      <w:tblGrid>
        <w:gridCol w:w=""4200""/>
        <w:gridCol w:w=""1200""/>
      </w:tblGrid>
      {string.Join(Environment.NewLine, rows)}
    </w:tbl>", 5400, "FFFFFF", "FFFFFF", "center")}
  </w:tr>
</w:tbl>";
        }

        private static string BuildTotalRoomBlock(int totalRooms, string summaryLine)
        {
            return $@"{BuildParagraph("TOPLAM ODA", "center", "173B72", 18, true)}
{BuildParagraph(totalRooms.ToString(), "center", "173B72", 40, true)}
{BuildParagraph(summaryLine, "center", "334155", 16, false)}";
        }

        private static string BuildTableCell(string contentXml, int width, string fillColor, string borderColor, string align)
        {
            return $@"<w:tc>
  <w:tcPr>
    <w:tcW w:w=""{width}"" w:type=""dxa""/>
    <w:tcBorders>
      <w:top w:val=""single"" w:sz=""8"" w:color=""{borderColor}""/>
      <w:left w:val=""single"" w:sz=""8"" w:color=""{borderColor}""/>
      <w:bottom w:val=""single"" w:sz=""8"" w:color=""{borderColor}""/>
      <w:right w:val=""single"" w:sz=""8"" w:color=""{borderColor}""/>
    </w:tcBorders>
    <w:shd w:val=""clear"" w:color=""auto"" w:fill=""{fillColor}""/>
    <w:vAlign w:val=""center""/>
  </w:tcPr>
  {contentXml}
</w:tc>";
        }

        private static string BuildParagraph(string text, string align, string color, int size, bool bold)
        {
            var safeText = (text ?? string.Empty).Split(new[] { '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = string.Join("", safeText).Split(new[] { '\n' }, StringSplitOptions.None);
            var builder = new StringBuilder();
            builder.Append($"<w:p><w:pPr><w:jc w:val=\"{align}\"/><w:spacing w:after=\"0\" w:before=\"0\"/></w:pPr><w:r><w:rPr>");
            if (bold)
            {
                builder.Append("<w:b/>");
            }
            builder.Append($"<w:color w:val=\"{color}\"/><w:sz w:val=\"{size}\"/></w:rPr>");
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append("<w:br/>");
                }

                builder.Append($"<w:t xml:space=\"preserve\">{EscapeXml(lines[i])}</w:t>");
            }

            builder.Append("</w:r></w:p>");
            return builder.ToString();
        }

        private static string FormatOccupantNamesForWord(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return string.Join("\n", value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()));
        }

        private static string NormalizeRoomType(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static int GetRoomTypeSortOrder(string roomType)
        {
            var normalized = (roomType ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized.Contains("SINGLE") || normalized.Contains("SNGL")) return 0;
            if (normalized.Contains("DBL")) return 1;
            if (normalized.Contains("TRPL") || normalized.Contains("TRIPLE") || normalized.Contains("TRİPLE")) return 2;
            if (normalized.Contains("TWIN")) return 3;
            if (normalized.Contains("QUAD")) return 4;
            if (normalized.Contains("QUINT")) return 5;
            return 50;
        }

        private static void AddDocxDocumentXml(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers, string title, List<string> footers)
        {
            // define default column widths (in twentieths of a point / dxa) for 6 columns
            var defaultWidths = new[] { 1200, 1600, 1000, 1600, 3600, 1200 };
            var tableRows = new List<string> { BuildWordTableRow(headers, true, defaultWidths) };
            tableRows.AddRange(rows.Select(r => BuildWordTableRow(headers.Select(h => r.ContainsKey(h) ? r[h] : string.Empty).ToList(), false, defaultWidths)));

            // build table grid
            var tblGrid = string.Join(string.Empty, defaultWidths.Select(w => $"<w:gridCol w:w=\"{w}\"/>"));

            var titleLines = (title ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            var titleXml = string.Join("\n", titleLines.Select(l => $"    <w:p><w:r><w:t>{EscapeXml(l)}</w:t></w:r></w:p>"));

            var footerXml = string.Empty;
            if (footers != null && footers.Count > 0)
            {
                footerXml = string.Join("\n", footers.Select(f => $"    <w:p><w:r><w:t>{EscapeXml(f)}</w:t></w:r></w:p>"));
            }

            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n  <w:body>\n{titleXml}\n    <w:tbl>\n      <w:tblPr>\n        <w:tblStyle w:val=\"TableGrid\"/>\n        <w:tblW w:w=\"0\" w:type=\"auto\"/>\n      </w:tblPr>\n      <w:tblGrid>\n        {tblGrid}\n      </w:tblGrid>\n      {string.Join(Environment.NewLine, tableRows)}\n    </w:tbl>\n{footerXml}\n    <w:sectPr>\n      <w:pgSz w:w=\"16838\" w:h=\"11906\"/>\n      <w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"450\" w:footer=\"450\" w:gutter=\"0\"/>\n    </w:sectPr>\n  </w:body>\n</w:document>";
            AddZipEntry(archive, "word/document.xml", document);
        }

        private static string BuildWordTableRow(IReadOnlyList<string> values, bool header, int[] widths)
        {
            var cells = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var v = values[i] ?? string.Empty;
                var isRed = v.StartsWith("!!RED!!");
                if (isRed) v = v.Substring("!!RED!!".Length);
                var text = EscapeXml(v);
                var cellWidth = (i < widths.Length ? widths[i] : 1500);

                // build cell properties: width and optional shading for header
                var tcPr = $"<w:tcPr><w:tcW w:w=\"{cellWidth}\" w:type=\"dxa\"/>";
                if (header)
                {
                    tcPr += "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"22324A\"/>";
                }
                tcPr += "</w:tcPr>";
                // support multiline values separated by '\n'
                var parts = text.Split(new[] { '\n' }, StringSplitOptions.None);
                if (header)
                {
                    // header: single paragraph, bold
                    cells.Add($"<w:tc>{tcPr}<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>{parts[0]}</w:t></w:r></w:p></w:tc>");
                }
                else
                {
                    // build multiple paragraphs inside cell
                    var inner = string.Join(string.Empty, parts.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>"));
                    if (isRed)
                        cells.Add($"<w:tc>{tcPr}<w:p><w:r><w:rPr><w:color w:val=\"FF0000\"/></w:rPr><w:t>{parts[0]}</w:t></w:r></w:p>{(parts.Length>1? string.Join(string.Empty, parts.Skip(1).Select(p=>$"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>")) : string.Empty)}</w:tc>");
                    else
                        cells.Add($"<w:tc>{tcPr}{inner}</w:tc>");
                }
            }
            // add bottom border for non-header rows by wrapping tr with trPr when not header
            var rowXml = "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
            return rowXml;
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
            var styles = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""3"">
    <font><sz val=""11""/><name val=""Calibri""/></font>
    <font><b/><sz val=""11""/><color rgb=""FFFFFFFF""/><name val=""Calibri""/></font>
    <font><b/><sz val=""11""/><color rgb=""FF173B72""/><name val=""Calibri""/></font>
  </fonts>
  <fills count=""4"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FF173B72""/></patternFill></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFEEF4FF""/></patternFill></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFF8FBFF""/></patternFill></fill>
  </fills>
  <borders count=""2"">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border>
      <left style=""thin""><color rgb=""FFD8E0EC""/></left>
      <right style=""thin""><color rgb=""FFD8E0EC""/></right>
      <top style=""thin""><color rgb=""FFD8E0EC""/></top>
      <bottom style=""thin""><color rgb=""FFD8E0EC""/></bottom>
      <diagonal/>
    </border>
  </borders>
  <cellStyleXfs count=""1"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/>
  </cellStyleXfs>
  <cellXfs count=""7"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""center"" vertical=""center"" wrapText=""1""/>
    </xf>
    <xf numFmtId=""0"" fontId=""2"" fillId=""2"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""center"" vertical=""center"" wrapText=""1""/>
    </xf>
    <xf numFmtId=""0"" fontId=""1"" fillId=""1"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""center"" vertical=""center"" wrapText=""1""/>
    </xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""center"" vertical=""center"" wrapText=""1""/>
    </xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""left"" vertical=""center"" wrapText=""1""/>
    </xf>
    <xf numFmtId=""0"" fontId=""2"" fillId=""3"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""center"" vertical=""center"" wrapText=""1""/>
    </xf>
    <xf numFmtId=""0"" fontId=""1"" fillId=""3"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1"" applyAlignment=""1"">
      <alignment horizontal=""center"" vertical=""center"" wrapText=""1""/>
    </xf>
  </cellXfs>
</styleSheet>";
            AddZipEntry(archive, "xl/styles.xml", styles);
        }

        private static void AddXlsxSheet(
            ZipArchive archive,
            List<Dictionary<string, string>> rows,
            List<string> headers,
            string title,
            List<string> footers,
            string hotelName,
            string checkInDate,
            string checkOutDate,
            string tourType,
            string period,
            string groupType,
            string daysCount)
        {
            var rowXml = new List<string>();
            var infoRows = new[]
            {
                new[] { ("OTEL", hotelName), ("GİRİŞ TARİHİ", checkInDate), ("ÇIKIŞ TARİHİ", checkOutDate) },
                new[] { ("TUR TÜRÜ", tourType), ("DÖNEM", period), ("GRUP TÜRÜ", groupType), ("GRUP SÜRESİ", daysCount) }
            };

            var currentRow = 1;
            rowXml.Add(BuildXlsxRow(currentRow++, new List<string>
            {
                FormatCardText(infoRows[0][0].Item1, infoRows[0][0].Item2),
                string.Empty, string.Empty,
                FormatCardText(infoRows[0][1].Item1, infoRows[0][1].Item2),
                string.Empty, string.Empty,
                FormatCardText(infoRows[0][2].Item1, infoRows[0][2].Item2),
                string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty
            }, false));
            rowXml.Add(BuildXlsxRow(currentRow++, Enumerable.Repeat(string.Empty, 12).ToList(), false));

            rowXml.Add(BuildXlsxRow(currentRow++, new List<string>
            {
                FormatCardText(infoRows[1][0].Item1, infoRows[1][0].Item2),
                string.Empty, string.Empty,
                FormatCardText(infoRows[1][1].Item1, infoRows[1][1].Item2),
                string.Empty, string.Empty,
                FormatCardText(infoRows[1][2].Item1, infoRows[1][2].Item2),
                string.Empty, string.Empty,
                FormatCardText(infoRows[1][3].Item1, infoRows[1][3].Item2),
                string.Empty, string.Empty
            }, false));
            rowXml.Add(BuildXlsxRow(currentRow++, Enumerable.Repeat(string.Empty, 12).ToList(), false));

            var tableStartRow = currentRow;
            rowXml.Add(BuildXlsxRow(currentRow++, Enumerable.Repeat(string.Empty, 12).ToList(), false));
            rowXml.Add(BuildXlsxRow(currentRow++, headers, true));

            for (var i = 0; i < rows.Count; i++)
            {
                var data = headers.Select(h => rows[i].ContainsKey(h) ? rows[i][h] : string.Empty).ToList();
                rowXml.Add(BuildXlsxRow(currentRow++, data, false, (index, value) =>
                {
                    if (headers[index] == "Connection")
                    {
                        return string.Equals(value, "Hayır", StringComparison.OrdinalIgnoreCase) ? 5 : 2;
                    }

                    return index == 4 ? 4 : 3;
                }));
            }

            var summaryStartRow = currentRow + 1;
            var roomTypeSummary = rows
                .GroupBy(row => NormalizeRoomType(row.ContainsKey("Oda Türü") ? row["Oda Türü"] : string.Empty))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => new { RoomType = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => GetRoomTypeSortOrder(item.RoomType))
                .ToList();

            rowXml.Add(BuildXlsxRow(summaryStartRow, new List<string> { "ODA DAĞILIM ÖZETİ", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }, false, (index, value) => 1));
            rowXml.Add(BuildXlsxRow(summaryStartRow + 1, new List<string> { "ODA TÜRÜ", "ODA SAYISI", string.Empty, string.Empty, "TOPLAM ODA", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }, true));

            var summaryRow = summaryStartRow + 2;
            foreach (var item in roomTypeSummary)
            {
                rowXml.Add(BuildXlsxRow(summaryRow++, new List<string> { item.RoomType, item.Count.ToString(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }, false, (index, value) => index == 0 ? 4 : 3));
            }

            rowXml.Add(BuildXlsxRow(summaryRow++, new List<string> { "TOPLAM ODA", rows.Count.ToString(), string.Empty, string.Empty, totalRoomsLine(roomTypeSummary), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }, false, (index, value) => index == 0 ? 6 : 6));

            var lastRow = Math.Max(currentRow, summaryRow);
            var lastColumn = "L";
            var colWidths = new[] { 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0 };
            var colsXml = string.Join(string.Empty, Enumerable.Range(1, 12).Select(i => $"<col min=\"{i}\" max=\"{i}\" width=\"{colWidths[i - 1]}\" customWidth=\"1\"/>"));
            var mergeCells = string.Join(string.Empty, new[]
            {
                "<mergeCell ref=\"A1:C2\"/>",
                "<mergeCell ref=\"D1:F2\"/>",
                "<mergeCell ref=\"G1:I2\"/>",
                "<mergeCell ref=\"A4:C5\"/>",
                "<mergeCell ref=\"D4:F5\"/>",
                "<mergeCell ref=\"G4:I5\"/>",
                "<mergeCell ref=\"J4:L5\"/>",
                $"<mergeCell ref=\"A{summaryStartRow}:L{summaryStartRow}\"/>"
            });

            var sheet = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <dimension ref=\"A1:{lastColumn}{lastRow}\"/>\n  <sheetViews>\n    <sheetView workbookViewId=\"0\"/>\n  </sheetViews>\n  <sheetFormatPr defaultRowHeight=\"22\"/>\n  <cols>\n    {colsXml}\n  </cols>\n  <sheetData>\n    {string.Join(Environment.NewLine, rowXml)}\n  </sheetData>\n  <mergeCells count=\"8\">\n    {mergeCells}\n  </mergeCells>\n</worksheet>";
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }

        private static string BuildXlsxRow(int rowNumber, IReadOnlyList<string> values, bool header, Func<int, string, int> styleSelector = null)
        {
            var cells = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]) && !header && styleSelector == null)
                {
                    continue;
                }

                var cellRef = $"{ColumnName(i + 1)}{rowNumber}";
                var styleIndex = header ? 1 : (styleSelector != null ? styleSelector(i, values[i] ?? string.Empty) : 3);
                var styleAttr = $" s=\"{styleIndex}\"";
                var raw = values[i] ?? string.Empty;

                // If not a header and value is numeric, write as numeric cell to avoid Excel "number stored as text" indicators.
                if (!header && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric))
                {
                    // Use invariant culture for value formatting (no thousands separator)
                    var formatted = numeric.ToString(CultureInfo.InvariantCulture);
                    cells.Add($"<c r=\"{cellRef}\"{styleAttr}><v>{formatted}</v></c>");
                }
                else
                {
                    var txt = EscapeXml(raw).Replace("\n", "&#10;");
                    cells.Add($"<c r=\"{cellRef}\" t=\"inlineStr\"{styleAttr}><is><t xml:space=\"preserve\">{txt}</t></is></c>");
                }
            }
            return $"<row r=\"{rowNumber}\">{string.Join(string.Empty, cells)}</row>";
        }

        private static string FormatCardText(string label, string value)
        {
            var safeValue = string.IsNullOrWhiteSpace(value) ? "-" : value;
            return $"{label}\n{safeValue}";
        }

        private static string totalRoomsLine(IReadOnlyList<dynamic> roomTypeSummary)
        {
            if (roomTypeSummary == null || roomTypeSummary.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", roomTypeSummary.Select(item => $"{item.Count} {item.RoomType}"));
        }

        // --- PDF builders ---
        private static byte[] BuildSimplePdf(
            List<Dictionary<string, string>> rows,
            List<string> headers,
            string title,
            List<string> footers,
            string hotelName,
            string checkInDate,
            string checkOutDate,
            string tourType,
            string period,
            string groupType,
            string daysCount)
        {
            var pageWidth = 842;
            var pageHeight = 595;
            var margin = 24;
            var content = new List<string>();
            var y = pageHeight - 24;
            var headersList = headers.ToList();

            DrawPdfInfoCards(content, margin, ref y, hotelName, checkInDate, checkOutDate, tourType, period, groupType, daysCount);

            y -= 12;
            var usableWidth = pageWidth - margin * 2;
            var colCount = headersList.Count;
            double[] weights;
            if (colCount <= 0)
            {
                weights = new double[] { 1 };
            }
            else if (colCount == 6)
            {
                weights = new double[] { 56, 76, 50, 90, 450, 70 };
            }
            else if (colCount == 7)
            {
                // Sıra, Ad, Soyad, TC Kimlik, Telefon, Adres, Kargo Talebi
                weights = new double[] { 40, 140, 140, 100, 110, 320, 90 };
            }
            else
            {
                // distribute evenly as fallback
                weights = Enumerable.Repeat(1.0, colCount).ToArray();
            }

            var scale = usableWidth / weights.Sum();
            var columnWidths = weights.Select(value => value * scale).ToArray();

            DrawPdfTableHeader(content, margin, y, columnWidths, headersList);
            y -= 20;

            foreach (var row in rows)
            {
                if (y < 120)
                {
                    break;
                }

                var values = headersList.Select(header => row.ContainsKey(header) ? row[header] : string.Empty).ToList();
                var occupantsText = values.Count > 4 ? values[4] : string.Empty;
                var occupantLines = Math.Max(1, occupantsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length);
                var rowHeight = Math.Max(20, 14 * occupantLines + 6);
                DrawPdfTableRow(content, margin, y, columnWidths, values, rowHeight);
                y -= rowHeight;
            }

            y -= 14;
            DrawPdfSummary(content, margin, ref y, rows);

            if (footers != null && footers.Count > 0)
            {
                y -= 8;
                foreach (var footer in footers)
                {
                    if (y < 40)
                    {
                        break;
                    }

                    content.Add($"BT /F1 9 Tf {margin} {y} Td ({EscapePdfText(footer)}) Tj ET");
                    y -= 14;
                }
            }

            var contentText = string.Join("\n", content);
            return BuildMinimalPdfBytes(pageWidth, pageHeight, contentText);
        }

        private static void DrawPdfInfoCards(
            List<string> content,
            int margin,
            ref int y,
            string hotelName,
            string checkInDate,
            string checkOutDate,
            string tourType,
            string period,
            string groupType,
            string daysCount)
        {
            var firstRowTop = y;
            DrawPdfCard(content, margin, firstRowTop, 248, 48, "OTEL", string.IsNullOrWhiteSpace(hotelName) ? "-" : hotelName);
            DrawPdfCard(content, margin + 254, firstRowTop, 248, 48, "GİRİŞ TARİHİ", string.IsNullOrWhiteSpace(checkInDate) ? "-" : checkInDate);
            DrawPdfCard(content, margin + 508, firstRowTop, 248, 48, "ÇIKIŞ TARİHİ", string.IsNullOrWhiteSpace(checkOutDate) ? "-" : checkOutDate);

            var secondRowTop = firstRowTop - 60;
            DrawPdfCard(content, margin, secondRowTop, 185, 48, "TUR TÜRÜ", string.IsNullOrWhiteSpace(tourType) ? "-" : tourType);
            DrawPdfCard(content, margin + 191, secondRowTop, 185, 48, "DÖNEM", string.IsNullOrWhiteSpace(period) ? "-" : period);
            DrawPdfCard(content, margin + 382, secondRowTop, 185, 48, "GRUP TÜRÜ", string.IsNullOrWhiteSpace(groupType) ? "-" : groupType);
            DrawPdfCard(content, margin + 573, secondRowTop, 185, 48, "GRUP SÜRESİ", string.IsNullOrWhiteSpace(daysCount) ? "-" : daysCount);

            y = secondRowTop - 60;
        }

        private static void DrawPdfCard(List<string> content, int x, int topY, int width, int height, string label, string value)
        {
            var y = topY - height;
            content.Add("1 1 1 rg");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re f");
            content.Add("0.847 0.878 0.925 RG");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re S");
            content.Add("0.404 0.455 0.533 rg");
            content.Add($"BT /F2 8 Tf {(x + 12).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 14).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(label)}) Tj ET");
            content.Add("0.106 0.169 0.286 rg");
            content.Add($"BT /F1 12 Tf {(x + 12).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 31).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(value)}) Tj ET");
        }

        private static void DrawPdfTableHeader(List<string> content, int margin, int topY, double[] columnWidths, IReadOnlyList<string> headers)
        {
            double x = margin;
            var height = 20;
            for (var i = 0; i < headers.Count; i++)
            {
                DrawPdfTableCell(content, x, topY - height, columnWidths[i], height, headers[i], true, false, false);
                x += columnWidths[i];
            }
        }

        private static void DrawPdfTableRow(List<string> content, int margin, int topY, double[] columnWidths, IReadOnlyList<string> values, int rowHeight)
        {
            double x = margin;
            for (var i = 0; i < values.Count; i++)
            {
                var isConnection = i == 5;
                var isPeople = i == 4;
                DrawPdfTableCell(content, x, topY - rowHeight, columnWidths[i], rowHeight, values[i], false, isPeople, isConnection);
                x += columnWidths[i];
            }
        }

        private static void DrawPdfTableCell(List<string> content, double x, double y, double width, double height, string value, bool header, bool leftAlign, bool connectionColumn)
        {
            var text = value ?? string.Empty;
            if (leftAlign)
            {
                text = FormatPdfOccupants(text);
            }

            var isNo = connectionColumn && text.Equals("Hayır", StringComparison.OrdinalIgnoreCase);
            var fill = header ? "0.091 0.231 0.447" : isNo ? "0.996 0.945 0.949" : "1 1 1";
            var textColor = header ? "1 1 1" : isNo ? "0.933 0.267 0.267" : "0.122 0.161 0.224";
            content.Add($"{fill} rg");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re f");
            content.Add("0.847 0.878 0.925 RG");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re S");
            content.Add($"{textColor} rg");

            var lines = (string.IsNullOrWhiteSpace(text) ? "-" : text)
                .Split(new[] { '\n' }, StringSplitOptions.None);
            var textX = leftAlign ? x + 4 : x + 6;
            var textY = y + height - 12;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = header ? lines[i].ToUpperInvariant() : lines[i];
                var font = header ? "/F2" : "/F1";
                var size = header ? 8.5 : 7.8;
                content.Add($"BT {font} {size.ToString(System.Globalization.CultureInfo.InvariantCulture)} Tf {textX.ToString(System.Globalization.CultureInfo.InvariantCulture)} {textY.ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(line)}) Tj ET");
                textY -= 9;
            }
        }

        private static void DrawPdfSummary(List<string> content, int margin, ref int y, List<Dictionary<string, string>> rows)
        {
            var roomTypeSummary = rows
                .GroupBy(row => NormalizeRoomType(row.ContainsKey("Oda Türü") ? row["Oda Türü"] : string.Empty))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => new { RoomType = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => GetRoomTypeSortOrder(item.RoomType))
                .ToList();

            var leftX = margin;
            var topY = y;
            var leftWidth = 230;
            var rowHeight = 18;
            var leftHeight = 38 + (roomTypeSummary.Count + 1) * rowHeight;
            DrawPdfBlock(content, leftX, topY, leftWidth, leftHeight, "ODA DAĞILIM ÖZETİ");

            var currentTop = topY - 24;
            DrawPdfMiniRow(content, leftX + 8, currentTop, leftWidth - 16, rowHeight, "ODA TÜRÜ", "ODA SAYISI", true);
            currentTop -= rowHeight;
            foreach (var item in roomTypeSummary)
            {
                DrawPdfMiniRow(content, leftX + 8, currentTop, leftWidth - 16, rowHeight, item.RoomType, item.Count.ToString(), false);
                currentTop -= rowHeight;
            }
            DrawPdfMiniRow(content, leftX + 8, currentTop, leftWidth - 16, rowHeight, "TOPLAM ODA", rows.Count.ToString(), true);

            var rightX = margin + 260;
            var rightWidth = 300;
            var rightHeight = 110;
            DrawPdfBlock(content, rightX, topY, rightWidth, rightHeight, "TOPLAM ODA");
            content.Add("0.094 0.231 0.447 rg");
            content.Add($"BT /F2 24 Tf {(rightX + 136).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 55).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({rows.Count}) Tj ET");
            content.Add("0.200 0.294 0.373 rg");
            content.Add($"BT /F1 8.5 Tf {(rightX + 18).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 80).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(JoinSummaryLine(roomTypeSummary))}) Tj ET");

            y = topY - Math.Max(leftHeight, rightHeight) - 12;
        }

        private static void DrawPdfBlock(List<string> content, int x, int topY, int width, int height, string title)
        {
            var y = topY - height;
            content.Add("1 1 1 rg");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re f");
            content.Add("0.847 0.878 0.925 RG");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re S");
            content.Add("0.094 0.231 0.447 rg");
            content.Add($"BT /F2 10 Tf {(x + 12).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 16).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(title)}) Tj ET");
        }

        private static void DrawPdfMiniRow(List<string> content, int x, int topY, int width, int height, string leftText, string rightText, bool header)
        {
            var y = topY - height;
            var leftWidth = (int)(width * 0.72);
            content.Add(header ? "0.933 0.957 1 rg" : "1 1 1 rg");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re f");
            content.Add("0.847 0.878 0.925 RG");
            content.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)} {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height.ToString(System.Globalization.CultureInfo.InvariantCulture)} re S");
            content.Add("0.094 0.231 0.447 rg");
            content.Add($"BT /F2 7.5 Tf {(x + 4).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 12).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(leftText)}) Tj ET");
            content.Add($"BT /F2 7.5 Tf {(x + leftWidth + 4).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(topY - 12).ToString(System.Globalization.CultureInfo.InvariantCulture)} Td ({EscapePdfText(rightText)}) Tj ET");
        }

        private static string FormatPdfOccupants(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return string.Join("\n", value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()));
        }

        private static string JoinSummaryLine(IReadOnlyList<dynamic> roomTypeSummary)
        {
            if (roomTypeSummary == null || roomTypeSummary.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", roomTypeSummary.Select(item => $"{item.Count} {item.RoomType}"));
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
