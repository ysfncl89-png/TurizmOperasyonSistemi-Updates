using System.IO.Compression;
using System.Security;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Services;

public sealed record ListExportOptions
{
    public bool UseDesktopTemplate { get; init; }
    public bool IsRoomAllocation { get; init; }
    public string HotelName { get; init; } = string.Empty;
    public string CheckInDate { get; init; } = string.Empty;
    public string CheckOutDate { get; init; } = string.Empty;
    public string TourType { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string GroupType { get; init; } = string.Empty;
    public string DaysCount { get; init; } = string.Empty;
    public IReadOnlyList<string> Footers { get; init; } = [];
}

public sealed class ListExportService
{
    public byte[] CreateWord(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions? options = null)
    {
        if (options?.UseDesktopTemplate == true)
            return options.IsRoomAllocation ? CreateRoomWord(title, headers, rows, options) : CreateDesktopWord(title, headers, rows);
        return CreateLegacyWord(title, headers, rows);
    }

    private static byte[] CreateDesktopWord(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            var titleXml = string.Join(string.Empty, title.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => $"<w:p><w:r><w:t>{EscapeXml(line.Trim())}</w:t></w:r></w:p>"));
            var rowXml = new StringBuilder();
            rowXml.Append(BuildDesktopWordRow(headers, true));
            foreach (var row in rows)
                rowXml.Append(BuildDesktopWordRow(headers.Select(header => GetValue(row, header)).ToList(), false));
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{titleXml}<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>{rowXml}</w:tbl><w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\"/></w:sectPr></w:body></w:document>";
            AddEntry(archive, "word/document.xml", document);
        }
        return output.ToArray();
    }

    private static byte[] CreateRoomWord(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions options)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            var infoRows = new[]
            {
                new[] { ("OTEL", options.HotelName), ("GÝRÝÞ TARÝHÝ", options.CheckInDate), ("ÇIKIÞ TARÝHÝ", options.CheckOutDate) },
                new[] { ("TUR TÜRÜ", options.TourType), ("DÖNEM", options.Period), ("GRUP TÜRÜ", options.GroupType), ("GRUP SÜRESÝ", options.DaysCount) }
            };
            var infoXml = new StringBuilder("<w:tbl><w:tblPr><w:tblW w:w=\"15300\" w:type=\"dxa\"/></w:tblPr>");
            foreach (var cells in infoRows)
            {
                infoXml.Append("<w:tr>");
                foreach (var (label, value) in cells)
                    infoXml.Append(BuildRoomInfoCell(label, value));
                infoXml.Append("</w:tr>");
            }
            infoXml.Append("</w:tbl><w:p><w:r><w:t> </w:t></w:r></w:p>");

            var tableRows = new StringBuilder();
            tableRows.Append(BuildDesktopWordRow(headers, true));
            foreach (var row in rows)
                tableRows.Append(BuildDesktopWordRow(headers.Select(header => GetValue(row, header)).ToList(), false));

            var summaries = rows
                .Select(row => GetValue(row, "Oda T\u00FCr\u00FC"))
                .Where(roomType => !string.IsNullOrWhiteSpace(roomType))
                .GroupBy(roomType => roomType.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new { RoomType = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => GetRoomTypeSortOrder(item.RoomType))
                .ToList();
            var summaryRows = new StringBuilder();
            summaryRows.Append("<w:tr><w:tc><w:tcPr><w:shd w:fill=\"EEF4FF\"/></w:tcPr><w:p><w:r><w:rPr><w:b/><w:color w:val=\"173B72\"/></w:rPr><w:t>ODA T&#x00DC;R&#x00DC;</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:shd w:fill=\"EEF4FF\"/></w:tcPr><w:p><w:r><w:rPr><w:b/><w:color w:val=\"173B72\"/></w:rPr><w:t>ODA SAYISI</w:t></w:r></w:p></w:tc></w:tr>");
            foreach (var item in summaries)
                summaryRows.Append($"<w:tr><w:tc><w:p><w:r><w:t>{EscapeXml(item.RoomType)}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>{item.Count}</w:t></w:r></w:p></w:tc></w:tr>");
            var summaryLine = summaries.Count == 0 ? "-" : string.Join(", ", summaries.Select(item => $"{item.Count} {item.RoomType}"));
            summaryRows.Append($"<w:tr><w:tc><w:tcPr><w:shd w:fill=\"DCE8F8\"/></w:tcPr><w:p><w:r><w:rPr><w:b/><w:color w:val=\"173B72\"/></w:rPr><w:t>TOPLAM ODA</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:shd w:fill=\"DCE8F8\"/></w:tcPr><w:p><w:r><w:t>{rows.Count}</w:t></w:r></w:p></w:tc></w:tr>");
            var summaryXml = $"<w:p><w:r><w:rPr><w:b/><w:color w:val=\"173B72\"/></w:rPr><w:t>ODA DA&#x011E;ILIM &#x00D6;ZET&#x0130;</w:t></w:r></w:p><w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/></w:tblPr>{summaryRows}</w:tbl><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>TOPLAM ODA: {rows.Count} - {EscapeXml(summaryLine)}</w:t></w:r></w:p>";
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{infoXml}<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>{tableRows}</w:tbl><w:p><w:r><w:t> </w:t></w:r></w:p>{summaryXml}<w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/><w:pgMar w:top=\"540\" w:right=\"540\" w:bottom=\"540\" w:left=\"540\"/></w:sectPr></w:body></w:document>";
            AddEntry(archive, "word/document.xml", document);
        }
        return output.ToArray();
    }

    private static string BuildDesktopWordRow(IReadOnlyList<string> values, bool header)
    {
        var cells = values.Select(value =>
        {
            var content = string.Join(string.Empty, (value ?? string.Empty).Split('\n').Select(line => $"<w:p><w:r>{(header ? "<w:rPr><w:b/></w:rPr>" : string.Empty)}<w:t xml:space=\"preserve\">{EscapeXml(line)}</w:t></w:r></w:p>"));
            var shading = header ? "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"22324A\"/>" : string.Empty;
            return $"<w:tc><w:tcPr>{shading}</w:tcPr>{content}</w:tc>";
        });
        return "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
    }

    private static string BuildRoomInfoCell(string label, string value)
    {
        var shownValue = string.IsNullOrWhiteSpace(value) ? "-" : value;
        return $"<w:tc><w:tcPr><w:shd w:fill=\"F8FBFF\"/></w:tcPr><w:p><w:r><w:rPr><w:b/><w:color w:val=\"173B72\"/></w:rPr><w:t>{EscapeXml(label)}</w:t></w:r></w:p><w:p><w:r><w:t>{EscapeXml(shownValue)}</w:t></w:r></w:p></w:tc>";
    }

    private static int GetRoomTypeSortOrder(string roomType)
    {
        var type = roomType.Trim().ToUpperInvariant();
        if (type.Contains("SINGLE") || type.Contains("SNGL")) return 0;
        if (type.Contains("DBL")) return 1;
        if (type.Contains("TRPL") || type.Contains("TRIPLE") || type.Contains("TRÝPLE")) return 2;
        if (type.Contains("TWIN")) return 3;
        if (type.Contains("QUAD")) return 4;
        if (type.Contains("QUINT")) return 5;
        return 50;
    }

    private static byte[] CreateLegacyWord(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            var tableRows = new StringBuilder();
            tableRows.Append(BuildWordRow(headers, true));
            foreach (var row in rows)
                tableRows.Append(BuildWordRow(headers.Select(header => GetValue(row, header)).ToList(), false));
            var titleXml = $"<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t>{EscapeXml(title)}</w:t></w:r></w:p>";
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{titleXml}<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>{tableRows}</w:tbl><w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\"/></w:sectPr></w:body></w:document>";
            AddEntry(archive, "word/document.xml", document);
        }
        return output.ToArray();
    }

    public byte[] CreateExcel(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions? options = null)
    {
        if (options?.UseDesktopTemplate == true)
            return CreateDesktopExcel(title, headers, rows, options);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Liste\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            AddEntry(archive, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            var sheetRows = new StringBuilder();
            var rowNumber = 1;
            sheetRows.Append(BuildExcelRow(rowNumber++, new[] { title }, true));
            sheetRows.Append(BuildExcelRow(rowNumber++, headers, true));
            foreach (var row in rows)
                sheetRows.Append(BuildExcelRow(rowNumber++, headers.Select(header => GetValue(row, header)).ToList(), false));
            var lastColumn = ColumnName(Math.Max(headers.Count, 1));
            var sheet = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><dimension ref=\"A1:{lastColumn}{rowNumber - 1}\"/><sheetData>{sheetRows}</sheetData></worksheet>";
            AddEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }
        return output.ToArray();
    }

    private static byte[] CreateDesktopExcel(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions options)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Genel Liste\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            AddEntry(archive, "xl/styles.xml", DesktopExcelStyles);

            var sheetRows = new StringBuilder();
            var merges = new List<string>();
            var rowNumber = 1;
            var columnCount = Math.Max(headers.Count, 1);
            if (options.IsRoomAllocation)
            {
                columnCount = 12;
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, [Card("OTEL", options.HotelName), string.Empty, string.Empty, Card("GÝRÝÞ TARÝHÝ", options.CheckInDate), string.Empty, string.Empty, Card("ÇIKIÞ TARÝHÝ", options.CheckOutDate), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty], false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, 12).ToList(), false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, 12).ToList(), false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, [Card("TUR TÜRÜ", options.TourType), string.Empty, string.Empty, Card("DÖNEM", options.Period), string.Empty, string.Empty, Card("GRUP TÜRÜ", options.GroupType), string.Empty, string.Empty, Card("GRUP SÜRESÝ", options.DaysCount), string.Empty, string.Empty], false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, 12).ToList(), false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, 12).ToList(), false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, 12).ToList(), false));
                merges.AddRange(["A1:C2", "D1:F2", "G1:I2", "A4:C5", "D4:F5", "G4:I5", "J4:L5"]);
            }
            else if (!string.IsNullOrWhiteSpace(title))
            {
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, [title], false));
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, columnCount).ToList(), false));
            }

            sheetRows.Append(BuildDesktopExcelRow(rowNumber++, headers, true));
            foreach (var row in rows)
            {
                var values = headers.Select(header => GetValue(row, header)).ToList();
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, values, false, (index, value) =>
                    options.IsRoomAllocation && index == 4 ? 4
                    : options.IsRoomAllocation && index == 5 && string.Equals(value, "Hay\u0131r", StringComparison.OrdinalIgnoreCase) ? 5
                    : 3));
            }

            if (options.IsRoomAllocation)
            {
                var summaryStart = rowNumber + 1;
                merges.Add($"A{summaryStart}:L{summaryStart}");
                sheetRows.Append(BuildDesktopExcelRow(summaryStart++, ["ODA DAÐILIM ÖZETÝ"], false, (_, _) => 1));
                sheetRows.Append(BuildDesktopExcelRow(summaryStart++, ["ODA TÜRÜ", "ODA SAYISI", string.Empty, string.Empty, "TOPLAM ODA"], true));
                var summary = rows.Select(row => GetValue(row, "Oda Türü"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .GroupBy(value => value.Trim(), StringComparer.CurrentCultureIgnoreCase)
                    .Select(group => new { RoomType = group.Key, Count = group.Count() })
                    .OrderByDescending(item => item.Count)
                    .ThenBy(item => GetRoomTypeSortOrder(item.RoomType))
                    .ToList();
                foreach (var item in summary)
                    sheetRows.Append(BuildDesktopExcelRow(summaryStart++, [item.RoomType, item.Count.ToString()], false, (index, _) => index == 0 ? 4 : 3));
                sheetRows.Append(BuildDesktopExcelRow(summaryStart++, ["TOPLAM ODA", rows.Count.ToString(), string.Empty, string.Empty, string.Join(", ", summary.Select(item => $"{item.Count} {item.RoomType}"))], false, (_, _) => 6));
                rowNumber = summaryStart;
            }
            else
            {
                sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, columnCount).ToList(), false));
                if (options.Footers.Count > 0)
                {
                    sheetRows.Append(BuildDesktopExcelRow(rowNumber++, Enumerable.Repeat(string.Empty, columnCount).ToList(), false));
                    foreach (var footer in options.Footers)
                        sheetRows.Append(BuildDesktopExcelRow(rowNumber++, [footer], false));
                }
            }

            var lastRow = Math.Max(1, rowNumber - 1);
            var lastColumn = ColumnName(columnCount);
            var mergeXml = merges.Count == 0 ? string.Empty : $"<mergeCells count=\"{merges.Count}\">{string.Join(string.Empty, merges.Select(reference => $"<mergeCell ref=\"{reference}\"/>"))}</mergeCells>";
            var colsXml = options.IsRoomAllocation ? "<cols>" + string.Join(string.Empty, Enumerable.Range(1, 12).Select(index => $"<col min=\"{index}\" max=\"{index}\" width=\"10\" customWidth=\"1\"/>")) + "</cols>" : string.Empty;
            var sheet = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><dimension ref=\"A1:{lastColumn}{lastRow}\"/><sheetFormatPr defaultRowHeight=\"22\"/>{colsXml}<sheetData>{sheetRows}</sheetData>{mergeXml}</worksheet>";
            AddEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }
        return output.ToArray();

        static string Card(string label, string value) => $"{label}\n{(string.IsNullOrWhiteSpace(value) ? "-" : value)}";
    }

    private const string DesktopExcelStyles = """
        <?xml version="1.0" encoding="UTF-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="4"><font><sz val="11"/><name val="Calibri"/><charset val="1"/></font><font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/><charset val="1"/></font><font><b/><sz val="11"/><color rgb="FF173B72"/><name val="Calibri"/><charset val="1"/></font><font><b/><sz val="12"/><color rgb="FF173B72"/><name val="Calibri"/><charset val="1"/></font></fonts>
          <fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF173B72"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEEF4FF"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF8FBFF"/></patternFill></fill></fills>
          <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD8E0EC"/></left><right style="thin"><color rgb="FFD8E0EC"/></right><top style="thin"><color rgb="FFD8E0EC"/></top><bottom style="thin"><color rgb="FFD8E0EC"/></bottom><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="8"><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="1" fillId="1" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="2" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="1" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf></cellXfs>
        </styleSheet>
        """;

    private static string BuildDesktopExcelRow(int rowNumber, IReadOnlyList<string> values, bool header, Func<int, string, int>? styleSelector = null)
    {
        var cells = new StringBuilder();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ?? string.Empty;
            var style = header ? 1 : styleSelector?.Invoke(index, value) ?? 3;
            var reference = $"{ColumnName(index + 1)}{rowNumber}";
            if (!header && double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var number))
                cells.Append($"<c r=\"{reference}\" s=\"{style}\"><v>{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
            else
                cells.Append($"<c r=\"{reference}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{EscapeXml(value).Replace("\n", "&#10;")}</t></is></c>");
        }
        return $"<row r=\"{rowNumber}\">{cells}</row>";
    }

    public byte[] CreatePdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions? options = null)
    {
        if (options?.UseDesktopTemplate == true)
        {
#if ANDROID
            return CreateAndroidDesktopPdf(title, headers, rows, options);
#else
            return CreateDesktopQuestPdf(title, headers, rows, options);
#endif
        }

#if ANDROID
        return CreateAndroidPdf(title, headers, rows);
#else
        QuestPDF.Settings.License = LicenseType.Community;
        var landscape = headers.Count > 5;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(24);
            page.DefaultTextStyle(style => style.FontFamily(Fonts.Arial).FontSize(9));
            page.Header().PaddingBottom(12).Text(title).FontSize(14).Bold().FontColor("#173B72");
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (var index = 0; index < Math.Max(headers.Count, 1); index++)
                        columns.RelativeColumn();
                });
                foreach (var header in headers)
                    table.Cell().Background("#173B72").Padding(5).Text(header).FontColor("#FFFFFF").Bold().FontSize(8);
                foreach (var row in rows)
                    foreach (var header in headers)
                        table.Cell().BorderBottom(0.5f).BorderColor("#D8E0EC").Padding(4).Text(GetValue(row, header)).FontSize(8);
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Sayfa ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
#endif
    }

    private static byte[] CreateDesktopQuestPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions options)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var landscape = headers.Count > 5;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(20);
            page.DefaultTextStyle(style => style.FontFamily("Arial").FontSize(10));
            page.Header().PaddingBottom(8).Column(column =>
            {
                column.Item().Text(title).FontSize(14).Bold();
                if (options.IsRoomAllocation)
                    column.Item().Row(row =>
                    {
                        row.RelativeColumn().Text(options.HotelName).FontSize(10);
                        row.RelativeColumn().Text($"Giriþ: {options.CheckInDate}").FontSize(10);
                        row.RelativeColumn().Text($"Çýkýþ: {options.CheckOutDate}").FontSize(10);
                    });
            });
            page.Content().PaddingVertical(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in headers)
                        columns.RelativeColumn();
                });
                table.Header(header =>
                {
                    foreach (var item in headers)
                        header.Cell().Background(QuestPDF.Helpers.Colors.Blue.Medium).Padding(6).Text(item).FontColor(QuestPDF.Helpers.Colors.White).FontSize(9).Bold();
                });
                foreach (var row in rows)
                    foreach (var item in headers)
                        table.Cell().Padding(6).Text(GetValue(row, item)).FontSize(9);
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    public byte[] CreateCargoLabelsPdf(IReadOnlyList<CargoQueueRecord> items)
    {
        var groups = items
            .SelectMany(item => Enumerable.Repeat(item, Math.Max(1, item.Quantity)))
            .GroupBy(item => new { item.TourType, item.Period, item.GroupType, item.DaysCount })
            .Select(group => (Title: string.Join(" · ", new[] { group.Key.TourType, group.Key.Period, group.Key.GroupType, group.Key.DaysCount }.Where(value => !string.IsNullOrWhiteSpace(value))), Items: group.ToList()))
            .ToList();
#if ANDROID
        return CreateAndroidCargoLabelsPdf(groups);
#else
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document =>
        {
            foreach (var group in groups)
            {
                foreach (var pageItems in group.Items.Chunk(12))
                {
                    document.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(22.7f);
                        page.DefaultTextStyle(style => style.FontFamily(Fonts.Arial).FontSize(9));
                        page.Content().Column(column =>
                        {
                            column.Item().Height(28).AlignCenter().AlignMiddle().Text(group.Title).Bold().FontSize(12);
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });
                                for (var index = 0; index < 12; index++)
                                {
                                    var item = index < pageItems.Length ? pageItems[index] : null;
                                    table.Cell().Padding(3).Border(1).BorderColor("#334155").Height(185).Padding(7).Column(cell =>
                                    {
                                        if (item is null) return;
                                        cell.Item().Text("ALICI").Bold().FontSize(9);
                                        cell.Item().PaddingTop(5).Text(item.FullName).SemiBold().FontSize(11);
                                        cell.Item().PaddingTop(3).Text($"Tel: {item.Phone}").FontSize(10);
                                        cell.Item().PaddingTop(3).Text(item.Address).FontSize(9);
                                    });
                                }
                            });
                        });
                    });
                }
            }
        }).GeneratePdf();
#endif
    }

#if ANDROID
    private static byte[] CreateAndroidDesktopPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ListExportOptions options)
    {
        const int margin = 20;
        const float fontSize = 9;
        const float lineHeight = 11;
        var columns = headers.Count == 0 ? new[] { string.Empty } : headers.ToArray();
        var landscape = headers.Count > 5;
        var pageWidth = landscape ? 842 : 595;
        var pageHeight = landscape ? 595 : 842;
        var columnWidth = (pageWidth - margin * 2f) / columns.Length;
        using var titlePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.Black, TextSize = 14 };
        titlePaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var headerPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.White, TextSize = fontSize };
        headerPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var bodyPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.Black, TextSize = fontSize };
        bodyPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Normal));
        using var headerFill = new Android.Graphics.Paint { Color = Android.Graphics.Color.Rgb(33, 150, 243) };
        using var gridPaint = new Android.Graphics.Paint { Color = Android.Graphics.Color.Rgb(216, 224, 236), StrokeWidth = 0.6f };
        using var pageNumberPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.Black, TextSize = 9 };

        var titleLines = title.Split('\n', StringSplitOptions.RemoveEmptyEntries).SelectMany(line => WrapText(line, pageWidth - margin * 2, titlePaint)).ToList();
        var headerLines = columns.Select(header => WrapText(header, columnWidth - 8, headerPaint)).ToArray();
        var headerHeight = Math.Max(20, headerLines.Max(lines => lines.Count) * lineHeight + 10);
        var contentStart = margin + 10 + titleLines.Count * 18 + (options.IsRoomAllocation ? 20 : 0) + 8 + headerHeight;
        var availableBottom = pageHeight - 38;
        var preparedRows = rows.Select(row =>
        {
            var lines = columns.Select(header => WrapText(GetValue(row, header), columnWidth - 8, bodyPaint)).ToArray();
            return (Lines: lines, Height: Math.Max(20, lines.Max(cell => cell.Count) * lineHeight + 10));
        }).ToList();
        var pages = new List<List<(List<string>[] Lines, float Height)>>();
        var currentPageRows = new List<(List<string>[] Lines, float Height)>();
        var yPosition = contentStart;
        foreach (var item in preparedRows)
        {
            if (currentPageRows.Count > 0 && yPosition + item.Height > availableBottom)
            {
                pages.Add(currentPageRows);
                currentPageRows = [];
                yPosition = contentStart;
            }
            currentPageRows.Add(item);
            yPosition += item.Height;
        }
        if (currentPageRows.Count > 0 || pages.Count == 0)
            pages.Add(currentPageRows);

        using var document = new Android.Graphics.Pdf.PdfDocument();
        using var output = new MemoryStream();
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var pageInfo = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageWidth, pageHeight, pageIndex + 1).Create();
            var page = document.StartPage(pageInfo);
            var canvas = page.Canvas;
            var y = (float)margin + 10;
            foreach (var line in titleLines)
            {
                canvas.DrawText(line, margin, y + titlePaint.TextSize, titlePaint);
                y += 18;
            }
            if (options.IsRoomAllocation)
            {
                using var infoPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.Black, TextSize = 10 };
                canvas.DrawText(options.HotelName, margin, y + infoPaint.TextSize, infoPaint);
                canvas.DrawText($"Giri\u015F: {options.CheckInDate}", margin + (pageWidth - margin * 2) / 3f, y + infoPaint.TextSize, infoPaint);
                canvas.DrawText($"\u00C7\u0131k\u0131\u015F: {options.CheckOutDate}", margin + 2 * (pageWidth - margin * 2) / 3f, y + infoPaint.TextSize, infoPaint);
                y += 20;
            }
            y += 8;
            canvas.DrawRect(margin, y, pageWidth - margin, y + headerHeight, headerFill);
            for (var column = 0; column < columns.Length; column++)
            {
                for (var lineIndex = 0; lineIndex < headerLines[column].Count; lineIndex++)
                    canvas.DrawText(headerLines[column][lineIndex], margin + column * columnWidth + 4, y + 13 + lineIndex * lineHeight, headerPaint);
            }
            y += headerHeight;
            foreach (var item in pages[pageIndex])
            {
                for (var column = 0; column < item.Lines.Length; column++)
                    for (var lineIndex = 0; lineIndex < item.Lines[column].Count; lineIndex++)
                        canvas.DrawText(item.Lines[column][lineIndex], margin + column * columnWidth + 4, y + 13 + lineIndex * lineHeight, bodyPaint);
                canvas.DrawLine(margin, y + item.Height, pageWidth - margin, y + item.Height, gridPaint);
                y += item.Height;
            }
            var pageText = $"{pageIndex + 1} / {pages.Count}";
            canvas.DrawText(pageText, (pageWidth - pageNumberPaint.MeasureText(pageText)) / 2, pageHeight - 16, pageNumberPaint);
            document.FinishPage(page);
        }
        document.WriteTo(output);
        return output.ToArray();
    }

    private static byte[] CreateAndroidCargoLabelsPdf(IReadOnlyList<(string Title, List<CargoQueueRecord> Items)> groups)
    {
        const int pageWidth = 595;
        const int pageHeight = 842;
        const int margin = 23;
        const int headerHeight = 28;
        const int columns = 3;
        const int rows = 4;
        const int perPage = columns * rows;
        var cellWidth = (pageWidth - margin * 2) / columns;
        var cellHeight = (pageHeight - margin * 2 - headerHeight) / rows;
        using var document = new Android.Graphics.Pdf.PdfDocument();
        using var output = new MemoryStream();
        var pageNumber = 0;
        using var titlePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.Rgb(23, 35, 54),
            TextSize = 12
        };
        titlePaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var headingPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.Rgb(51, 65, 85),
            TextSize = 9
        };
        headingPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var namePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.Rgb(15, 23, 42),
            TextSize = 11
        };
        namePaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var detailPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.Rgb(15, 23, 42),
            TextSize = 9
        };
        using var borderPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.Rgb(51, 65, 85),
            StrokeWidth = 1
        };
        using var labelFillPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.White
        };

        foreach (var group in groups)
        {
            foreach (var pageItems in group.Items.Chunk(perPage))
            {
                var info = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageWidth, pageHeight, ++pageNumber).Create();
                var page = document.StartPage(info);
                var canvas = page.Canvas;
                var title = group.Title;
                while (title.Length > 0 && titlePaint.MeasureText(title) > pageWidth - margin * 2)
                    title = title[..^1];
                canvas.DrawText(title, margin, margin + 15, titlePaint);

                for (var index = 0; index < perPage; index++)
                {
                    var column = index % columns;
                    var row = index / columns;
                    var left = margin + column * cellWidth + 3;
                    var top = margin + headerHeight + row * cellHeight + 3;
                    var right = margin + (column + 1) * cellWidth - 3;
                    var bottom = margin + headerHeight + (row + 1) * cellHeight - 3;
                    canvas.DrawRect(left + 1, top + 1, right - 1, bottom - 1, labelFillPaint);
                    canvas.DrawLine(left, top, right, top, borderPaint);
                    canvas.DrawLine(right, top, right, bottom, borderPaint);
                    canvas.DrawLine(right, bottom, left, bottom, borderPaint);
                    canvas.DrawLine(left, bottom, left, top, borderPaint);
                    if (index >= pageItems.Length) continue;

                    var item = pageItems[index];
                    var textX = left + 9;
                    float textY = top + 18;
                    canvas.DrawText("ALICI", textX, textY, headingPaint);
                    textY += 19;
                    textY = DrawCargoLabelText(canvas, item.FullName, textX, textY, right - left - 18, namePaint, 16, bottom);
                    textY = DrawCargoLabelText(canvas, $"Tel: {item.Phone}", textX, textY + 4, right - left - 18, detailPaint, 13, bottom);
                    DrawCargoLabelText(canvas, item.Address, textX, textY + 4, right - left - 18, detailPaint, 13, bottom);
                }
                document.FinishPage(page);
            }
        }

        document.WriteTo(output);
        return output.ToArray();
    }

    private static float DrawCargoLabelText(Android.Graphics.Canvas canvas, string? text, float x, float y, float maxWidth, Android.Graphics.Paint paint, float lineHeight, float bottom)
    {
        foreach (var line in WrapText(text ?? string.Empty, maxWidth, paint))
        {
            if (y > bottom - 8) break;
            canvas.DrawText(line, x, y, paint);
            y += lineHeight;
        }
        return y;
    }

    private static byte[] CreateAndroidPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        const int margin = 28;
        const float headerFontSize = 8;
        const float bodyFontSize = 7;
        const float lineHeight = 10;
        var landscape = headers.Count > 5;
        var pageWidth = landscape ? 842 : 595;
        var pageHeight = landscape ? 595 : 842;
        var columns = headers.Count == 0 ? new[] { string.Empty } : headers.ToArray();
        var columnWidth = (pageWidth - margin * 2f) / columns.Length;
        using var document = new Android.Graphics.Pdf.PdfDocument();
        using var output = new MemoryStream();
        Android.Graphics.Pdf.PdfDocument.Page? page = null;
        Android.Graphics.Canvas? canvas = null;
        var pageNumber = 0;
        var y = 0f;

        using var titlePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias);
        titlePaint.Color = Android.Graphics.Color.Rgb(23, 59, 114);
        titlePaint.TextSize = 15;
        titlePaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var headerPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias);
        headerPaint.Color = Android.Graphics.Color.White;
        headerPaint.TextSize = headerFontSize;
        headerPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold));
        using var bodyPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias);
        bodyPaint.Color = Android.Graphics.Color.Rgb(15, 23, 42);
        bodyPaint.TextSize = bodyFontSize;
        bodyPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Normal));
        using var linePaint = new Android.Graphics.Paint { Color = Android.Graphics.Color.Rgb(216, 224, 236), StrokeWidth = 0.6f };
        using var headerBackground = new Android.Graphics.Paint { Color = Android.Graphics.Color.Rgb(23, 59, 114) };

        void StartPage()
        {
            pageNumber++;
            var info = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageWidth, pageHeight, pageNumber).Create();
            page = document.StartPage(info);
            canvas = page.Canvas;
            canvas.DrawText(title, margin, 32, titlePaint);
            y = 52;
            canvas.DrawRect(margin, y, pageWidth - margin, y + 22, headerBackground);
            for (var index = 0; index < columns.Length; index++)
            {
                var text = WrapText(columns[index], columnWidth - 8, headerPaint).FirstOrDefault() ?? string.Empty;
                canvas.DrawText(text, margin + index * columnWidth + 4, y + 14, headerPaint);
            }
            y += 22;
        }

        void FinishPage()
        {
            if (page is not null)
            {
                document.FinishPage(page);
                page = null;
                canvas = null;
            }
        }

        StartPage();
        foreach (var row in rows)
        {
            var cellLines = columns.Select(header => WrapText(GetValue(row, header), columnWidth - 8, bodyPaint)).ToArray();
            var rowHeight = Math.Max(20, cellLines.Max(lines => lines.Count) * lineHeight + 8);
            if (y + rowHeight > pageHeight - 36)
            {
                FinishPage();
                StartPage();
            }
            for (var column = 0; column < cellLines.Length; column++)
            {
                for (var line = 0; line < cellLines[column].Count; line++)
                    canvas!.DrawText(cellLines[column][line], margin + column * columnWidth + 4, y + 13 + line * lineHeight, bodyPaint);
                canvas!.DrawLine(margin + column * columnWidth, y + rowHeight, margin + (column + 1) * columnWidth, y + rowHeight, linePaint);
            }
            y += rowHeight;
        }
        FinishPage();
        document.WriteTo(output);
        return output.ToArray();
    }

    private static List<string> WrapText(string text, float maxWidth, Android.Graphics.Paint paint)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (paint.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            current = word;
        }
        if (!string.IsNullOrEmpty(current)) lines.Add(current);
        return lines.Count == 0 ? [string.Empty] : lines;
    }
#endif

    private static string BuildWordRow(IReadOnlyList<string> values, bool header)
    {
        var cells = values.Select(value => $"<w:tc><w:tcPr>{(header ? "<w:shd w:fill=\"22324A\"/>" : string.Empty)}</w:tcPr><w:p><w:r>{(header ? "<w:rPr><w:b/><w:color w:val=\"FFFFFF\"/></w:rPr>" : string.Empty)}<w:t xml:space=\"preserve\">{EscapeXml(value)}</w:t></w:r></w:p></w:tc>");
        return "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
    }

    private static string BuildExcelRow(int rowNumber, IReadOnlyList<string> values, bool bold)
    {
        var cells = values.Select((value, index) => $"<c r=\"{ColumnName(index + 1)}{rowNumber}\" t=\"inlineStr\"{(bold ? " s=\"1\"" : string.Empty)}><is><t xml:space=\"preserve\">{EscapeXml(value)}</t></is></c>");
        return $"<row r=\"{rowNumber}\">{string.Join(string.Empty, cells)}</row>";
    }

    private static string GetValue(IReadOnlyDictionary<string, string> row, string header) => row.TryGetValue(header, out var value) ? value ?? string.Empty : string.Empty;

    private static string ColumnName(int column)
    {
        var result = string.Empty;
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }
        return result;
    }

    private static string EscapeXml(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
