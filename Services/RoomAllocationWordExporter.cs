using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace TurizmOperasyonSistemi.Services
{
    public static class RoomAllocationWordExporter
    {
        public static void Export(
            string filePath,
            List<Dictionary<string, string>> rows,
            string hotelName,
            string checkInDate,
            string checkOutDate,
            string tourType,
            string period,
            string groupType,
            string daysCount)
        {
            rows ??= new List<Dictionary<string, string>>();
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddEntry(archive, "[Content_Types].xml", BuildContentTypes());
            AddEntry(archive, "_rels/.rels", BuildRels());
            AddEntry(archive, "word/_rels/document.xml.rels", BuildDocumentRels());
            AddEntry(archive, "word/document.xml", BuildDocument(rows, hotelName, checkInDate, checkOutDate, tourType, period, groupType, daysCount));
        }

        private static string BuildContentTypes()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/word/document.xml"" ContentType=""application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml""/>
</Types>";
        }

        private static string BuildRels()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""R1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""word/document.xml""/>
</Relationships>";
        }

        private static string BuildDocumentRels()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""/>";
        }

        private static string BuildDocument(
            List<Dictionary<string, string>> rows,
            string hotelName,
            string checkInDate,
            string checkOutDate,
            string tourType,
            string period,
            string groupType,
            string daysCount)
        {
            var normalizedRows = rows.Select((row, index) => NormalizeRow(row, index)).ToList();
            var roomTypeSummary = normalizedRows
                .GroupBy(row => row["Oda Türü"])
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => new RoomTypeSummary { RoomType = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => GetRoomTypeSortOrder(item.RoomType))
                .ToList();

            var summaryLine = roomTypeSummary.Count == 0
                ? "-"
                : string.Join(", ", roomTypeSummary.Select(item => $"{item.Count} {item.RoomType}"));

            return $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    {BuildInfoSection(hotelName, checkInDate, checkOutDate, tourType, period, groupType, daysCount)}
    <w:p><w:r><w:t xml:space=""preserve""> </w:t></w:r></w:p>
    {BuildRoomTable(normalizedRows)}
    <w:p><w:r><w:t xml:space=""preserve""> </w:t></w:r></w:p>
    {BuildSummarySection(roomTypeSummary, summaryLine)}
    <w:sectPr>
      <w:pgSz w:w=""16838"" w:h=""11906"" w:orient=""landscape""/>
      <w:pgMar w:top=""540"" w:right=""540"" w:bottom=""540"" w:left=""540"" w:header=""400"" w:footer=""400"" w:gutter=""0""/>
    </w:sectPr>
  </w:body>
</w:document>";
        }

        private static Dictionary<string, string> NormalizeRow(Dictionary<string, string> row, int index)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Oda No"] = GetValue(row, "Oda No"),
                ["Oda Türü"] = GetValue(row, "Oda Türü"),
                ["Kapasite"] = GetValue(row, "Kapasite"),
                ["Yerleşen Kişi Sayısı"] = GetValue(row, "Yerleşen Kişi Sayısı"),
                ["Yerleşen Kişiler"] = FormatOccupants(GetValue(row, "Yerleşen Kişiler")),
                ["Connection"] = GetValue(row, "Connection")
            };

            if (string.IsNullOrWhiteSpace(normalized["Oda No"]))
            {
                normalized["Oda No"] = (index + 1).ToString();
            }

            return normalized;
        }

        private static string BuildInfoSection(string hotelName, string checkInDate, string checkOutDate, string tourType, string period, string groupType, string daysCount)
        {
            var firstRow = BuildCardRow(
                new[]
                {
                    ("OTEL", string.IsNullOrWhiteSpace(hotelName) ? "-" : hotelName),
                    ("GİRİŞ TARİHİ", FormatDate(checkInDate)),
                    ("ÇIKIŞ TARİHİ", FormatDate(checkOutDate))
                });

            var secondRow = BuildCardRow(
                new[]
                {
                    ("TUR TÜRÜ", string.IsNullOrWhiteSpace(tourType) ? "-" : tourType),
                    ("DÖNEM", string.IsNullOrWhiteSpace(period) ? "-" : period),
                    ("GRUP TÜRÜ", string.IsNullOrWhiteSpace(groupType) ? "-" : groupType),
                    ("GRUP SÜRESİ", string.IsNullOrWhiteSpace(daysCount) ? "-" : daysCount)
                });

            return firstRow + "\n" + secondRow;
        }

        private static string BuildCardRow((string Label, string Value)[] cards)
        {
            var widths = cards.Length == 4
                ? new[] { 3825, 3825, 3825, 3825 }
                : new[] { 5100, 5100, 5100 };

            var cells = new List<string>();
            for (var i = 0; i < cards.Length; i++)
            {
                cells.Add(BuildCardCell(cards[i].Label, cards[i].Value, widths[i]));
            }

            var grid = string.Join(Environment.NewLine, widths.Select(width => $"    <w:gridCol w:w=\"{width}\"/>"));
            var cellXml = string.Join(Environment.NewLine, cells.Select(cell => $"    {cell}"));

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
{grid}
  </w:tblGrid>
  <w:tr>
{cellXml}
  </w:tr>
</w:tbl>";
        }

        private static string BuildCardCell(string label, string value, int width)
        {
            var content = BuildParagraph(label, "center", "64748B", 18, true) + BuildParagraph(value, "center", "1E293B", 24, false);
            return $@"<w:tc>
  <w:tcPr>
    <w:tcW w:w=""{width}"" w:type=""dxa""/>
    <w:tcBorders>
      <w:top w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:left w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:bottom w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:right w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
    </w:tcBorders>
    <w:shd w:val=""clear"" w:color=""auto"" w:fill=""FFFFFF""/>
    <w:vAlign w:val=""center""/>
  </w:tcPr>
  {content}
</w:tc>";
        }

        private static string BuildRoomTable(IReadOnlyList<Dictionary<string, string>> rows)
        {
            var headers = new[] { "Oda No", "Oda Türü", "Kapasite", "Yerleşen Kişi Sayısı", "Yerleşen Kişiler", "Connection" };
            var widths = new[] { 1200, 1600, 1000, 1800, 6200, 1400 };

            var headerCells = headers.Select((header, index) => BuildBodyCell(BuildParagraph(header.ToUpperInvariant(), "center", "FFFFFF", 18, true), widths[index], "173B72"));
            var rowsXml = new List<string> { $"<w:tr>{string.Join(string.Empty, headerCells)}</w:tr>" };

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var fill = rowIndex % 2 == 0 ? "FFFFFF" : "F8FBFF";
                var cells = new List<string>();

                for (var i = 0; i < headers.Length; i++)
                {
                    var header = headers[i];
                    var value = row.ContainsKey(header) ? row[header] : string.Empty;
                    if (header == "Connection")
                    {
                        cells.Add(BuildConnectionCell(value, widths[i], fill));
                    }
                    else if (header == "Yerleşen Kişiler")
                    {
                        cells.Add(BuildBodyCell(BuildParagraph(value, "left", "1F2937", 18, false), widths[i], fill));
                    }
                    else
                    {
                        cells.Add(BuildBodyCell(BuildParagraph(string.IsNullOrWhiteSpace(value) ? "-" : value, "center", "1F2937", 18, false), widths[i], fill));
                    }
                }

                rowsXml.Add($"<w:tr>{string.Join(string.Empty, cells)}</w:tr>");
            }

            var grid = string.Join(Environment.NewLine, widths.Select(width => $"    <w:gridCol w:w=\"{width}\"/>"));

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
{grid}
  </w:tblGrid>
  {string.Join(Environment.NewLine, rowsXml)}
</w:tbl>";
        }

        private static string BuildConnectionCell(string value, int width, string fill)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            var isNo = text.Equals("Hayır", StringComparison.OrdinalIgnoreCase);
            var color = isNo ? "EF4444" : "2563EB";
            var background = isNo ? "FFF1F2" : "EEF4FF";
            return BuildBodyCell(BuildParagraph(text, "center", color, 18, false), width, background);
        }

        private static string BuildSummarySection(IReadOnlyList<RoomTypeSummary> roomTypeSummary, string summaryLine)
        {
            var leftTable = BuildSummaryDistributionTable(roomTypeSummary);
            var rightTable = BuildTotalRoomTable(roomTypeSummary.Sum(item => item.Count), summaryLine);

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
    {BuildOuterCell(leftTable, 7600)}
    {BuildOuterCell(rightTable, 7600)}
  </w:tr>
</w:tbl>";
        }

        private static string BuildSummaryDistributionTable(IReadOnlyList<RoomTypeSummary> roomTypeSummary)
        {
            var widths = new[] { 4200, 1200 };
            var rowsXml = new List<string>
            {
                $@"<w:tr>
  {BuildBodyCell(BuildParagraph("ODA TÜRÜ", "center", "173B72", 16, true), widths[0], "EEF4FF")}
  {BuildBodyCell(BuildParagraph("ODA SAYISI", "center", "173B72", 16, true), widths[1], "EEF4FF")}
</w:tr>"
            };

            foreach (var item in roomTypeSummary)
            {
                rowsXml.Add($@"<w:tr>
  {BuildBodyCell(BuildParagraph(item.RoomType, "center", "1F2937", 16, false), widths[0], "FFFFFF")}
  {BuildBodyCell(BuildParagraph(item.Count.ToString(), "center", "1F2937", 16, false), widths[1], "FFFFFF")}
</w:tr>");
            }

            rowsXml.Add($@"<w:tr>
  {BuildBodyCell(BuildParagraph("TOPLAM ODA", "center", "173B72", 16, true), widths[0], "DCE8F8")}
  {BuildBodyCell(BuildParagraph(roomTypeSummary.Sum(item => item.Count).ToString(), "center", "173B72", 16, true), widths[1], "DCE8F8")}
</w:tr>");

            return $@"<w:tbl>
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
  {string.Join(Environment.NewLine, rowsXml)}
</w:tbl>";
        }

        private static string BuildTotalRoomTable(int totalRooms, string summaryLine)
        {
            return $@"<w:tbl>
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
    <w:gridCol w:w=""5400""/>
  </w:tblGrid>
  <w:tr>
    {BuildBodyCell(BuildParagraph("TOPLAM ODA", "center", "173B72", 18, true) + BuildParagraph(totalRooms.ToString(), "center", "173B72", 40, true) + BuildParagraph(summaryLine, "center", "334155", 16, false), 5400, "FFFFFF")}
  </w:tr>
</w:tbl>";
        }

        private static string BuildOuterCell(string innerTableXml, int width)
        {
            return $@"<w:tc>
  <w:tcPr>
    <w:tcW w:w=""{width}"" w:type=""dxa""/>
    <w:tcBorders>
      <w:top w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:left w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:bottom w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:right w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
    </w:tcBorders>
    <w:shd w:val=""clear"" w:color=""auto"" w:fill=""FFFFFF""/>
    <w:vAlign w:val=""top""/>
  </w:tcPr>
  <w:p><w:r><w:t xml:space=""preserve""> </w:t></w:r></w:p>
  {innerTableXml}
  <w:p><w:r><w:t xml:space=""preserve""> </w:t></w:r></w:p>
</w:tc>";
        }

        private static string BuildBodyCell(string contentXml, int width, string fillColor)
        {
            return $@"<w:tc>
  <w:tcPr>
    <w:tcW w:w=""{width}"" w:type=""dxa""/>
    <w:tcBorders>
      <w:top w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:left w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:bottom w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
      <w:right w:val=""single"" w:sz=""8"" w:color=""D8E0EC""/>
    </w:tcBorders>
    <w:shd w:val=""clear"" w:color=""auto"" w:fill=""{fillColor}""/>
    <w:vAlign w:val=""center""/>
  </w:tcPr>
  {contentXml}
</w:tc>";
        }

        private static string BuildParagraph(string text, string align, string color, int size, bool bold)
        {
            var normalized = (text ?? string.Empty).Replace("\r", string.Empty);
            var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
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

        private static string FormatOccupants(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            var items = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            return items.Count == 0 ? "-" : string.Join("\n", items);
        }

        private static string FormatDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return DateTime.TryParse(value, out var parsed) ? parsed.ToString("dd.MM.yyyy") : value;
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

        private static string GetValue(Dictionary<string, string> row, string key)
        {
            return row != null && row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        }

        private static void AddEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(content);
        }

        private static string EscapeXml(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private sealed class RoomTypeSummary
        {
            public string RoomType { get; set; }
            public int Count { get; set; }
        }
    }
}
