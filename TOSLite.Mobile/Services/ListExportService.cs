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

#if ANDROID
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
