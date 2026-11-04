using System.IO.Compression;
using System.Security;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TOSLite.Mobile.Services;

public sealed class ListExportService
{
    public byte[] CreateWord(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
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

    public byte[] CreateExcel(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
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

    public byte[] CreatePdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
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
    }

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
