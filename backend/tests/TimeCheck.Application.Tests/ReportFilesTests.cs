using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AwesomeAssertions;
using TimeCheck.Application.Features.Reports;

namespace TimeCheck.Application.Tests;

/// <summary>The hand-written export formats (§6.8) and the small rules the reports lean on.</summary>
public class ReportFilesTests
{
    private static ReportDto Sample(int rows = 3)
    {
        var data = Enumerable.Range(1, rows)
            .Select(i => (IReadOnlyList<object?>)[i == 1 ? "=HYPERLINK(\"x\")" : $"Niño {i}, Jr.", i, 50m + i, i == 2 ? null : new DateOnly(2026, 11, 1 + (i - 1) % 28)])
            .ToList();
        return new ReportDto("attendance", "Attendance", ["1 Nov 2026 – 30 Nov 2026", "All groups"], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
            [new ReportSection("byMember", "By member",
                [new("member", "Member", ColumnKind.Text), new("n", "Attended", ColumnKind.Number), new("rate", "Rate", ColumnKind.Percent), new("on", "Last", ColumnKind.Date)],
                data, "Unmarked counts as absent.")],
            new ReportChart("Rate", "percent", [new ChartBar("2 Nov", 50)]), DateTimeOffset.Parse("2026-11-25T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Csv_quotes_values_and_never_starts_a_cell_with_a_formula()
    {
        var csv = Encoding.UTF8.GetString(ReportFiles.Write(Sample(), "csv").Bytes);
        csv.Should().StartWith("﻿Attendance").And.Contain("Member,Attended,Rate,Last");
        csv.Should().Contain("\"'=HYPERLINK(\"\"x\"\")\",1,51,2026-11-01", "a name that looks like a formula is kept as text");
        csv.Should().Contain("\"Niño 2, Jr.\",2,52,\r\n", "an empty cell stays empty");
        ReportFiles.Guard("-5").Should().Be("'-5");
        ReportFiles.Guard("Ana").Should().Be("Ana");
    }

    [Fact]
    public void Xlsx_is_a_workbook_with_a_sheet_per_section_and_numbers_as_numbers()
    {
        using var zip = new ZipArchive(new MemoryStream(ReportFiles.Write(Sample(), "xlsx").Bytes));
        zip.Entries.Select(e => e.FullName).Should().Contain(["[Content_Types].xml", "xl/workbook.xml", "xl/styles.xml", "xl/worksheets/sheet1.xml"]);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var book = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open());
        book.Descendants(ns + "sheet").Single().Attribute("name")!.Value.Should().Be("By member");
        var sheet = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        sheet.Descendants(ns + "v").Select(v => v.Value).Should().Contain(["1", "51"]);
        sheet.Descendants(ns + "t").Select(t => t.Value).Should().Contain(["Niño 2, Jr.", "=HYPERLINK(\"x\")"], "inline strings are never formulas");
    }

    [Fact]
    public void Pdf_is_well_formed_and_breaks_long_tables_across_pages()
    {
        var pdf = Encoding.Latin1.GetString(ReportFiles.Write(Sample(200), "pdf").Bytes);
        pdf.Should().StartWith("%PDF-1.4").And.EndWith("%%EOF\n");
        pdf.Should().Contain("/BaseFont /Helvetica").And.Contain("(Ni\\361o 2, Jr.)", "WinAnsi ñ");
        var count = int.Parse(System.Text.RegularExpressions.Regex.Match(pdf, @"/Count (\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        count.Should().BeGreaterThan(1);
        pdf.Should().Contain($"(Page {count} of {count})");

        // Every xref offset points at the start of its object.
        var start = pdf.LastIndexOf("startxref\n", StringComparison.Ordinal);
        var xref = int.Parse(pdf[(start + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        pdf[xref..].Should().StartWith("xref");
        var offsets = pdf[xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ", StringComparison.Ordinal)).Select(l => int.Parse(l[..10], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        offsets.Select((o, i) => pdf[o..].StartsWith($"{i + 1} 0 obj", StringComparison.Ordinal)).Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public void Cached_cells_read_the_same_as_fresh_ones()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(Sample(), ReportJson.Options);
        var cached = JsonSerializer.Deserialize<ReportDto>(json, ReportJson.Options)!;
        Encoding.UTF8.GetString(ReportFiles.Write(cached, "csv").Bytes).Should().Be(Encoding.UTF8.GetString(ReportFiles.Write(Sample(), "csv").Bytes));
        ReportFiles.Display(cached.Sections[0].Rows[0][2], ColumnKind.Percent).Should().Be("51%");
        ReportFiles.Display(cached.Sections[0].Rows[0][3], ColumnKind.Date).Should().Be("1 Nov 2026");
        ReportFiles.Display(cached.Sections[0].Rows[1][3], ColumnKind.Date).Should().Be("—");
    }

    [Theory]
    [InlineData(5, "12 and under")]
    [InlineData(12, "12 and under")]
    [InlineData(13, "13–17")]
    [InlineData(35, "25–35")]
    [InlineData(36, "36 and over")]
    public void Age_groups_follow_the_default_brackets(int age, string label) =>
        ReportBuilder.AgeBracket(age, ReportBuilder.AgeBrackets("{}")).Should().Be(label);

    [Fact]
    public void Age_brackets_come_from_church_settings_when_valid()
    {
        ReportBuilder.AgeBrackets("""{"ageBrackets":[17,29]}""").Should().Equal(17, 29);
        ReportBuilder.AgeBrackets("""{"ageBrackets":[30,20]}""").Should().Equal(12, 17, 24, 35);
        ReportBuilder.AgeBrackets("""{"ageBrackets":"x"}""").Should().Equal(12, 17, 24, 35);
    }

    [Fact]
    public void A_devotion_streak_counts_days_in_a_row_and_today_unfinished_does_not_break_it()
    {
        var today = new DateOnly(2026, 11, 25);
        ReportBuilder.Streak([today, today.AddDays(-1), today.AddDays(-2), today.AddDays(-4)], today).Should().Be(3);
        ReportBuilder.Streak([today.AddDays(-1), today.AddDays(-2)], today).Should().Be(2);
        ReportBuilder.Streak([today.AddDays(-2)], today).Should().Be(0);
    }
}
