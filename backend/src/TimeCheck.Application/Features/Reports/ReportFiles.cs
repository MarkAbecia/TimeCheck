using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;

namespace TimeCheck.Application.Features.Reports;

public sealed record ReportFile(byte[] Bytes, string ContentType, string Extension);

/// <summary>
/// CSV, XLSX and PDF from a <see cref="ReportDto"/> (§6.8). Small enough to write by hand, so no document library: the XLSX is
/// the minimal OpenXML package and the PDF uses the built-in Helvetica in the light palette (§11 printing rule).
/// </summary>
public static class ReportFiles
{
    public static readonly IReadOnlyList<string> Formats = ["csv", "xlsx", "pdf"];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ReportFile Write(ReportDto r, string format) => format switch
    {
        "csv" => new(Csv(r), "text/csv", "csv"),
        "xlsx" => new(Xlsx(r), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
        "pdf" => new(Pdf(r), "application/pdf", "pdf"),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
    };

    // ---------------------------------------------------------------- Cells

    /// <summary>A cell as text for people: "—" for empty, "87.5%" for percents, "1 Nov 2026" for dates.</summary>
    public static string Display(object? value, string kind)
    {
        if (Number(value) is { } n) return kind == ColumnKind.Percent ? $"{n.ToString("0.#", Inv)}%" : n.ToString("0.##", Inv);
        var s = Text(value);
        if (s is null) return "—";
        return kind == ColumnKind.Date && DateOnly.TryParse(s, Inv, out var d) ? d.ToString("d MMM yyyy", Inv) : s;
    }

    /// <summary>Numbers come back from the cache as JSON numbers; fresh ones are ints and decimals.</summary>
    private static decimal? Number(object? v) => v switch
    {
        int i => i,
        long l => l,
        decimal d => d,
        double f => (decimal)f,
        JsonElement { ValueKind: JsonValueKind.Number } e => e.GetDecimal(),
        _ => null,
    };

    private static string? Text(object? v) => v switch
    {
        null => null,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement e => e.GetRawText(),
        DateOnly d => d.ToString("yyyy-MM-dd", Inv),
        IFormattable f => f.ToString(null, Inv),
        _ => v.ToString(),
    };

    private static string Machine(object? v) => Number(v) is { } n ? n.ToString(Inv) : Text(v) ?? "";

    // ---------------------------------------------------------------- CSV

    private static byte[] Csv(ReportDto r)
    {
        var sb = new StringBuilder();
        void Line(IEnumerable<string> cells) => sb.Append(string.Join(',', cells.Select(Quote))).Append("\r\n");
        Line([r.Title]);
        Line([string.Join("; ", r.Scope)]);
        Line([$"Generated {r.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", Inv)} UTC"]);
        foreach (var s in r.Sections)
        {
            sb.Append("\r\n");
            Line([s.Title]);
            Line(s.Columns.Select(c => c.Label));
            foreach (var row in s.Rows) Line(row.Select((v, i) => s.Columns[i].Kind == ColumnKind.Text ? Guard(Text(v) ?? "") : Machine(v)));
            if (s.Note is { } note) Line([note]);
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())]; // BOM so Excel reads UTF-8
    }

    private static string Quote(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;

    /// <summary>A name like "=HYPERLINK(...)" must not run as a formula when the file is opened (CSV injection).</summary>
    public static string Guard(string s) => s.Length > 0 && "=+-@\t\r".Contains(s[0], StringComparison.Ordinal) ? $"'{s}" : s;

    // ---------------------------------------------------------------- XLSX (one sheet per section)

    private static byte[] Xlsx(ReportDto r)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string name, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }

            var names = SheetNames(r.Sections.Select(s => s.Title));
            var count = r.Sections.Count;
            Entry("[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
                """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>""" +
                """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""" +
                """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""" +
                string.Concat(Enumerable.Range(1, count).Select(i =>
                    $"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""")) +
                "</Types>");
            Entry("_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
                """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry("xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""" +
                string.Concat(names.Select((n, i) => $"""<sheet name="{X(n)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""")) + "</sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
                string.Concat(Enumerable.Range(1, count).Select(i =>
                    $"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""")) +
                $"""<Relationship Id="rId{count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            // Style 1: bold (titles and headers).
            Entry("xl/styles.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""" +
                """<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>""" +
                """<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>""" +
                """<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>""" +
                """<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>""" +
                """<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>""" +
                "</styleSheet>");

            for (var i = 0; i < count; i++)
            {
                var s = r.Sections[i];
                var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
                var rowNo = 0;
                void Row(IEnumerable<string> cells) => sb.Append(Inv, $"<row r=\"{++rowNo}\">").Append(string.Concat(cells)).Append("</row>");
                Row([StrCell(r.Title, true)]);
                Row([StrCell(string.Join("; ", r.Scope), false)]);
                Row([StrCell(s.Title, true)]);
                Row(s.Columns.Select(c => StrCell(c.Label, true)));
                foreach (var row in s.Rows)
                    Row(row.Select((v, c) => s.Columns[c].Kind is ColumnKind.Number or ColumnKind.Percent && Number(v) is { } n
                        ? $"<c><v>{n.ToString(Inv)}</v></c>"
                        : StrCell(Text(v) ?? "", false)));
                if (s.Note is { } note) Row([StrCell(note, false)]);
                sb.Append("</sheetData></worksheet>");
                Entry($"xl/worksheets/sheet{i + 1}.xml", sb.ToString());
            }
        }
        return ms.ToArray();
    }

    private static string StrCell(string text, bool bold) =>
        $"<c t=\"inlineStr\"{(bold ? " s=\"1\"" : "")}><is><t xml:space=\"preserve\">{X(text)}</t></is></c>";

    /// <summary>XML-escaped, without characters XML can't hold.</summary>
    private static string X(string s) => SecurityElement.Escape(new string(s.Where(c => c == '\t' || c == '\n' || c == '\r' || c >= ' ').ToArray()))!;

    /// <summary>Excel sheet names: at most 31 characters, none of []:*?/\ and unique.</summary>
    private static List<string> SheetNames(IEnumerable<string> titles)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var t in titles)
        {
            var clean = new string(t.Where(c => "[]:*?/\\".IndexOf(c, StringComparison.Ordinal) < 0).ToArray()).Trim();
            if (clean.Length == 0) clean = "Sheet";
            var name = clean.Length > 31 ? clean[..31] : clean;
            for (var n = 2; !used.Add(name); n++) name = $"{(clean.Length > 27 ? clean[..27] : clean)} ({n})";
            names.Add(name);
        }
        return names;
    }

    // ---------------------------------------------------------------- PDF (A4 landscape, Helvetica, black on white)

    private const float PageW = 842, PageH = 595, Margin = 36, BodySize = 8.5f, LineH = 13;

    private static byte[] Pdf(ReportDto r)
    {
        var pages = new List<StringBuilder>();
        StringBuilder page = null!;
        float y = 0;

        void NewPage()
        {
            page = new StringBuilder();
            pages.Add(page);
            y = PageH - Margin;
            TextAt(Margin, y - 6, r.Title, 7.5f, false, 0.35f);
            y -= 20;
        }
        void TextAt(float x, float at, string text, float size, bool bold, float gray = 0) =>
            page.Append(Inv, $"{gray:0.##} g BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {at:0.##} Td ({PdfText(text)}) Tj ET\n");
        void Rule(float at) => page.Append(Inv, $"0.75 G 0.5 w {Margin:0.##} {at:0.##} m {PageW - Margin:0.##} {at:0.##} l S\n");
        bool Fits(float needed) => y - needed >= Margin + 16;

        NewPage();
        TextAt(Margin, y - 14, r.Title, 16, true);
        y -= 30;
        TextAt(Margin, y, string.Join("   |   ", r.Scope), 9.5f, false, 0.25f);
        y -= 14;
        TextAt(Margin, y, $"Generated {r.GeneratedAt.UtcDateTime.ToString("d MMM yyyy, HH:mm", Inv)} UTC", 8, false, 0.4f);
        y -= 22;

        foreach (var s in r.Sections)
        {
            var widths = ColumnWidths(s);
            void Header()
            {
                var x = Margin;
                for (var c = 0; c < s.Columns.Count; c++) { TextAt(x, y, Fit(s.Columns[c].Label, widths[c], BodySize), BodySize, true); x += widths[c]; }
                y -= 4;
                Rule(y);
                y -= LineH - 3;
            }

            if (!Fits(60)) NewPage();
            TextAt(Margin, y, s.Title, 11.5f, true);
            y -= 16;
            if (s.Note is { } note)
            {
                TextAt(Margin, y, Fit(note, PageW - 2 * Margin, 8), 8, false, 0.35f);
                y -= 13;
            }
            Header();
            if (s.Rows.Count == 0) { TextAt(Margin, y, "Nothing in this period.", BodySize, false, 0.35f); y -= LineH; }
            foreach (var row in s.Rows)
            {
                if (!Fits(LineH)) { NewPage(); Header(); }
                var x = Margin;
                for (var c = 0; c < s.Columns.Count; c++)
                {
                    TextAt(x, y, Fit(Display(row[c], s.Columns[c].Kind), widths[c], BodySize), BodySize, false);
                    x += widths[c];
                }
                y -= LineH;
            }
            y -= 14;
        }

        for (var i = 0; i < pages.Count; i++)
        {
            page = pages[i];
            TextAt(PageW - Margin - 60, Margin - 14, $"Page {i + 1} of {pages.Count}", 7.5f, false, 0.4f);
        }
        return Assemble(pages);
    }

    /// <summary>Shares the width by the longest text in each column (capped), so names get room and counts stay narrow.</summary>
    private static float[] ColumnWidths(ReportSection s)
    {
        var want = s.Columns.Select((c, i) => (float)Math.Clamp(
            s.Rows.Select(row => Display(row[i], c.Kind).Length).Append(c.Label.Length).Max(), 4, 40)).ToArray();
        var scale = (PageW - 2 * Margin) / want.Sum();
        return want.Select(w => w * scale).ToArray();
    }

    /// <summary>Cut to the column, by Helvetica's average width; a little room is left between columns.</summary>
    private static string Fit(string text, float width, float size)
    {
        var max = (int)((width - 6) / (size * 0.52f));
        return text.Length <= max ? text : max <= 1 ? "" : $"{text[..(max - 1)]}…";
    }

    /// <summary>A PDF string in WinAnsiEncoding; anything it can't show becomes "?".</summary>
    private static string PdfText(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            var code = ch switch
            {
                '–' => 0x96, '—' => 0x97, '…' => 0x85, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94, '•' => 0x95, '€' => 0x80,
                _ when ch < 0x20 => ' ',
                _ when ch <= 0xFF && (ch < 0x80 || ch >= 0xA0) => ch,
                _ => '?',
            };
            if (code is '(' or ')' or '\\') sb.Append('\\');
            if (code < 0x80) sb.Append((char)code);
            else sb.Append('\\').Append(Convert.ToString(code, 8));
        }
        return sb.ToString();
    }

    private static byte[] Assemble(List<StringBuilder> pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // pages, filled in below
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        var kids = new List<string>();
        foreach (var content in pages.Select(p => p.ToString()))
        {
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}endstream");
            var contentId = objects.Count;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW} {PageH}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            kids.Add($"{objects.Count} 0 R");
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {kids.Count} >>";

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var (o, i) in objects.Select((o, i) => (o, i)))
        {
            offsets.Add(sb.Length); // all ASCII: one char, one byte
            sb.Append(Inv, $"{i + 1} 0 obj\n{o}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append(Inv, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets) sb.Append(Inv, $"{off:D10} 00000 n \n");
        sb.Append(Inv, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
