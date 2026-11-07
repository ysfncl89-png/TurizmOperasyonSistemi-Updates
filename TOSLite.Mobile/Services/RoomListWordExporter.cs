using System.IO.Compression;
using System.Security;
using System.Text;

namespace TOSLite.Mobile.Services;

internal static class RoomListWordExporter
{
    public static byte[] Create(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions options)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");

            var info = new StringBuilder();
            info.Append(BuildInfoRow([("OTEL", options.HotelName), ("GİRİŞ TARİHİ", options.CheckInDate), ("ÇIKIŞ TARİHİ", options.CheckOutDate)]));
            info.Append(BuildInfoRow([("TUR TÜRÜ", options.TourType), ("DÖNEM", options.Period), ("GRUP TÜRÜ", options.GroupType), ("GRUP SÜRESİ", options.DaysCount)]));
            info.Append("<w:p><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p>");

            string[] headers = ["Oda No", "Oda Türü", "Kapasite", "Yerleşen Kişi Sayısı", "Yerleşen Kişiler", "Connection"];
            int[] widths = [1200, 1600, 1000, 1800, 6200, 1400];
            var tableRows = new List<string>();
            tableRows.Add("<w:tr>" + string.Join(string.Empty, headers.Select((header, index) => Cell(Paragraph(header.ToUpperInvariant(), "center", "FFFFFF", 18, true), widths[index], "173B72"))) + "</w:tr>");
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var fill = index % 2 == 0 ? "FFFFFF" : "F8FBFF";
                var cells = new List<string>();
                foreach (var (header, column) in headers.Select((header, column) => (header, column)))
                {
                    var value = GetValue(row, header);
                    if (header == "Connection")
                    {
                        var isNo = value.Equals("Hayır", StringComparison.OrdinalIgnoreCase);
                        cells.Add(Cell(Paragraph(string.IsNullOrWhiteSpace(value) ? "-" : value, "center", isNo ? "EF4444" : "2563EB", 18, false), widths[column], isNo ? "FFF1F2" : "EEF4FF"));
                    }
                    else if (header == "Yerleşen Kişiler")
                        cells.Add(Cell(Paragraph(string.IsNullOrWhiteSpace(value) ? "-" : value, "left", "1F2937", 20, true), widths[column], fill));
                    else
                        cells.Add(Cell(Paragraph(header == "Oda No" && string.IsNullOrEmpty(value) ? string.Empty : string.IsNullOrWhiteSpace(value) ? "-" : value, "center", "1F2937", 18, false), widths[column], fill));
                }
                tableRows.Add($"<w:tr><w:trPr><w:trHeight w:val=\"360\"/></w:trPr>{string.Join(string.Empty, cells)}</w:tr>");
            }
            var roomGrid = string.Join(string.Empty, widths.Select(width => $"<w:gridCol w:w=\"{width}\"/>"));
            var roomTable = $"<w:tbl><w:tblPr><w:tblW w:w=\"15300\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>{Borders()}</w:tblBorders></w:tblPr><w:tblGrid>{roomGrid}</w:tblGrid>{string.Join(string.Empty, tableRows)}</w:tbl>";

            var summaries = rows.Select(row => GetValue(row, "Oda Türü").Trim()).Where(type => !string.IsNullOrWhiteSpace(type))
                .GroupBy(type => type, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new { RoomType = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count).ThenBy(item => SortOrder(item.RoomType)).ToList();
            var summaryRows = new List<string>
            {
                $"<w:tr>{Cell(Paragraph("ODA TÜRÜ", "center", "173B72", 16, true), 4200, "EEF4FF", 6)}{Cell(Paragraph("ODA SAYISI", "center", "173B72", 16, true), 1200, "EEF4FF", 6)}</w:tr>"
            };
            summaryRows.AddRange(summaries.Select(item => $"<w:tr>{Cell(Paragraph(item.RoomType, "center", "1F2937", 16, false), 4200, "FFFFFF", 6)}{Cell(Paragraph(item.Count.ToString(), "center", "1F2937", 16, false), 1200, "FFFFFF", 6)}</w:tr>"));
            summaryRows.Add($"<w:tr>{Cell(Paragraph("TOPLAM ODA", "center", "173B72", 16, true), 4200, "DCE8F8", 6)}{Cell(Paragraph(rows.Count.ToString(), "center", "173B72", 16, true), 1200, "DCE8F8", 6)}</w:tr>");
            var distribution = $"<w:tbl><w:tblPr><w:tblW w:w=\"5400\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>{Borders(6)}</w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"4200\"/><w:gridCol w:w=\"1200\"/></w:tblGrid>{string.Join(string.Empty, summaryRows)}</w:tbl>";
            var summaryLine = summaries.Count == 0 ? "-" : string.Join(", ", summaries.Select(item => $"{item.Count} {item.RoomType}"));
            var total = $"<w:tbl><w:tblPr><w:tblW w:w=\"5400\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>{Borders(6)}</w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"5400\"/></w:tblGrid><w:tr>{Cell(Paragraph("TOPLAM ODA", "center", "173B72", 18, true) + Paragraph(rows.Count.ToString(), "center", "173B72", 40, true) + Paragraph(summaryLine, "center", "334155", 16, false), 5400, "FFFFFF", 6)}</w:tr></w:tbl>";
            var summary = $"<w:tbl><w:tblPr><w:tblW w:w=\"15300\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"7600\"/><w:gridCol w:w=\"7600\"/></w:tblGrid><w:tr>{OuterCell(distribution, 7600)}{OuterCell(total, 7600)}</w:tr></w:tbl>";
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{info}{roomTable}<w:p><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p>{summary}<w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/><w:pgMar w:top=\"540\" w:right=\"540\" w:bottom=\"540\" w:left=\"540\" w:header=\"400\" w:footer=\"400\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>";
            AddEntry(archive, "word/document.xml", document);
        }
        return output.ToArray();
    }

    private static string BuildInfoRow((string Label, string Value)[] items)
    {
        var widths = items.Length == 4 ? new[] { 3825, 3825, 3825, 3825 } : new[] { 5100, 5100, 5100 };
        var cells = items.Select((item, index) =>
            Cell(Paragraph(item.Label, "center", "64748B", 18, true) + Paragraph(string.IsNullOrWhiteSpace(item.Value) ? "-" : item.Value, "center", "1E293B", 24, false), widths[index], "FFFFFF", 8));
        var grid = string.Join(string.Empty, widths.Select(width => $"<w:gridCol w:w=\"{width}\"/>"));
        return $"<w:tbl><w:tblPr><w:tblW w:w=\"15300\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders></w:tblPr><w:tblGrid>{grid}</w:tblGrid><w:tr>{string.Join(string.Empty, cells)}</w:tr></w:tbl>";
    }

    private static string Cell(string content, int width, string fill, int borderSize = 8) =>
        $"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/><w:tcBorders>{Borders(borderSize)}</w:tcBorders><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/><w:vAlign w:val=\"center\"/></w:tcPr>{content}</w:tc>";

    private static string OuterCell(string content, int width) =>
        $"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/><w:tcBorders>{Borders()}</w:tcBorders><w:shd w:fill=\"FFFFFF\"/><w:vAlign w:val=\"top\"/></w:tcPr><w:p><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p>{content}<w:p><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p></w:tc>";

    private static string Borders(int size = 8) =>
        $"<w:top w:val=\"single\" w:sz=\"{size}\" w:color=\"D8E0EC\"/><w:left w:val=\"single\" w:sz=\"{size}\" w:color=\"D8E0EC\"/><w:bottom w:val=\"single\" w:sz=\"{size}\" w:color=\"D8E0EC\"/><w:right w:val=\"single\" w:sz=\"{size}\" w:color=\"D8E0EC\"/><w:insideH w:val=\"single\" w:sz=\"{size}\" w:color=\"D8E0EC\"/><w:insideV w:val=\"single\" w:sz=\"{size}\" w:color=\"D8E0EC\"/>";

    private static string Paragraph(string text, string alignment, string color, int size, bool bold)
    {
        var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        var content = string.Join(string.Empty, lines.Select((line, index) => $"{(index == 0 ? string.Empty : "<w:br/>")}<w:t xml:space=\"preserve\">{EscapeXml(line)}</w:t>"));
        return $"<w:p><w:pPr><w:jc w:val=\"{alignment}\"/><w:spacing w:after=\"0\" w:before=\"0\"/></w:pPr><w:r><w:rPr>{(bold ? "<w:b/>" : string.Empty)}<w:color w:val=\"{color}\"/><w:sz w:val=\"{size}\"/></w:rPr>{content}</w:r></w:p>";
    }

    private static string GetValue(IReadOnlyDictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static int SortOrder(string roomType)
    {
        var type = roomType.Trim().ToUpperInvariant();
        if (type.Contains("SINGLE") || type.Contains("SNGL")) return 0;
        if (type.Contains("DBL")) return 1;
        if (type.Contains("TRPL") || type.Contains("TRIPLE")) return 2;
        if (type.Contains("TWIN")) return 3;
        if (type.Contains("QUAD")) return 4;
        if (type.Contains("QUINT")) return 5;
        return 50;
    }

    private static string EscapeXml(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
