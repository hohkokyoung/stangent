using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>Builds workbooks and OLE containers by hand. No real Excel FSD has been seen, so these are all the tests have (D123).</summary>
static class XlsxFixture
{
    public const string Ns = """xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """;

    public sealed record Sheet(string Name, string Xml = "", string? State = null, Action<WorksheetPart>? Parts = null, string After = "", string Before = "");

    public static void Text(OpenXmlPart part, string xml)
    {
        using var w = new StreamWriter(part.GetStream(FileMode.Create));
        w.Write(xml);
    }

    public static string Row(int r, params string[] cells) => $"""<row r="{r}">{string.Concat(cells)}</row>""";
    public static string Str(string at, string text) => $"""<c r="{at}" t="inlineStr"><is><t>{text}</t></is></c>""";
    public static string Num(string at, string value, int? style = null) => $"""<c r="{at}"{(style is { } s ? $" s=\"{s}\"" : "")}><v>{value}</v></c>""";

    public static byte[] Build(Sheet[] sheets, string? shared = null, string? styles = null, string workbookExtra = "", Action<WorkbookPart>? parts = null, bool macros = false)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, macros ? SpreadsheetDocumentType.MacroEnabledWorkbook : SpreadsheetDocumentType.Workbook))
        {
            var book = doc.AddWorkbookPart();
            var list = new StringBuilder();
            foreach (var (s, i) in sheets.Select((s, i) => (s, i + 1)))
            {
                var part = book.AddNewPart<WorksheetPart>();
                Text(part, $"<worksheet {Ns}>{s.Before}<sheetData>{s.Xml}</sheetData>{s.After}</worksheet>");
                s.Parts?.Invoke(part);
                list.Append($"""<sheet name="{s.Name}" sheetId="{i}" r:id="{book.GetIdOfPart(part)}"{(s.State is null ? "" : $" state=\"{s.State}\"")}/>""");
            }
            Text(book, $"<workbook {Ns}>{workbookExtra}<sheets>{list}</sheets></workbook>");
            if (shared is not null) Text(book.AddNewPart<SharedStringTablePart>(), $"<sst {Ns}>{shared}</sst>");
            if (styles is not null) Text(book.AddNewPart<WorkbookStylesPart>(), $"<styleSheet {Ns}>{styles}</styleSheet>");
            parts?.Invoke(book);
        }
        return stream.ToArray();
    }

    /// <summary>A workbook with a <c>Rates</c> sheet of rows: Tier and Fee, then Gold and 12, then Silver and 7.</summary>
    public static byte[] Rates(string gold = "12") => Build([new("Rates", Row(1, Str("A1", "Tier"), Str("B1", "Fee")) + Row(2, Str("A2", "Gold"), Num("B2", gold)) + Row(3, Str("A3", "Silver tier fee is seven"), Num("B3", "7")))]);

    /// <summary>A small OLE compound file. Streams under 4096 bytes go in the mini stream, the way real files keep them. Stream names with a slash sit in a storage.</summary>
    public static byte[] Ole(params (string Name, byte[] Bytes)[] streams)
    {
        const int Sector = 512, Mini = 64, Cutoff = 4096, End = -2, Free = -1;
        var mini = new MemoryStream();
        var miniFat = new List<int>();
        var big = new List<byte[]>();
        var places = new List<(int Start, long Size)>();
        var nextBig = 0;
        foreach (var (_, bytes) in streams)
        {
            if (bytes.Length < Cutoff)
            {
                var start = (int)(mini.Length / Mini);
                var count = Math.Max(1, (bytes.Length + Mini - 1) / Mini);
                for (var i = 0; i < count; i++) miniFat.Add(i + 1 < count ? start + i + 1 : End);
                mini.Write(bytes);
                mini.Write(new byte[count * Mini - bytes.Length]);
                places.Add((start, bytes.Length));
            }
            else
            {
                places.Add((nextBig, bytes.Length));
                big.Add(bytes);
                nextBig += (bytes.Length + Sector - 1) / Sector;
            }
        }
        // Sectors in order: big streams, mini stream, mini FAT, directory, FAT.
        var chains = new List<int>();
        void Chain(int start, int count) { for (var i = 0; i < count; i++) chains.Add(i + 1 < count ? start + i + 1 : End); }
        foreach (var (b, i) in big.Select((b, i) => (b, i))) Chain(chains.Count, (b.Length + Sector - 1) / Sector);
        var miniStart = chains.Count;
        var miniSectors = (int)((mini.Length + Sector - 1) / Sector);
        Chain(chains.Count, miniSectors);
        var miniFatStart = chains.Count;
        var miniFatSectors = Math.Max(1, (miniFat.Count * 4 + Sector - 1) / Sector);
        Chain(chains.Count, miniFatSectors);

        var dirEntries = new List<byte[]>();
        byte[] Entry(string name, byte kind, int child, int start, long size, int right = -1)
        {
            var e = new byte[128];
            var n = Encoding.Unicode.GetBytes(name);
            n.CopyTo(e, 0);
            BitConverter.GetBytes((ushort)(n.Length + 2)).CopyTo(e, 64);
            e[66] = kind;
            BitConverter.GetBytes(-1).CopyTo(e, 68);
            BitConverter.GetBytes(right).CopyTo(e, 72);
            BitConverter.GetBytes(child).CopyTo(e, 76);
            BitConverter.GetBytes(start).CopyTo(e, 116);
            BitConverter.GetBytes(size).CopyTo(e, 120);
            return e;
        }
        dirEntries.Add([]); // root, filled below
        var top = new List<int>();
        var storages = new Dictionary<string, List<int>>();
        foreach (var ((name, _), place) in streams.Zip(places))
        {
            var slash = name.IndexOf('/');
            dirEntries.Add(Entry(slash < 0 ? name : name[(slash + 1)..], 2, -1, place.Start, place.Size));
            if (slash < 0) top.Add(dirEntries.Count - 1);
            else (storages.TryGetValue(name[..slash], out var l) ? l : storages[name[..slash]] = []).Add(dirEntries.Count - 1);
        }
        void Link(List<int> siblings) { for (var i = 0; i + 1 < siblings.Count; i++) BitConverter.GetBytes(siblings[i + 1]).CopyTo(dirEntries[siblings[i]], 72); }
        foreach (var (name, kids) in storages)
        {
            Link(kids);
            dirEntries.Add(Entry(name, 1, kids[0], 0, 0));
            top.Add(dirEntries.Count - 1);
        }
        Link(top);
        dirEntries[0] = Entry("Root Entry", 5, top.Count > 0 ? top[0] : -1, miniSectors > 0 ? miniStart : End, mini.Length);
        var directoryStart = chains.Count;
        var directorySectors = Math.Max(1, (dirEntries.Count * 128 + Sector - 1) / Sector);
        Chain(chains.Count, directorySectors);
        var fatStart = chains.Count;
        var fatSectors = (chains.Count + 1) * 4 / Sector + 1;
        for (var i = 0; i < fatSectors; i++) chains.Add(-3);
        while (chains.Count % (Sector / 4) != 0) chains.Add(Free);

        var header = new byte[Sector];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(header, 0);
        BitConverter.GetBytes((ushort)9).CopyTo(header, 30);
        BitConverter.GetBytes((ushort)6).CopyTo(header, 32);
        BitConverter.GetBytes(fatSectors).CopyTo(header, 44);
        BitConverter.GetBytes(directoryStart).CopyTo(header, 48);
        BitConverter.GetBytes(Cutoff).CopyTo(header, 56);
        BitConverter.GetBytes(miniFatStart).CopyTo(header, 60);
        BitConverter.GetBytes(miniFatSectors).CopyTo(header, 64);
        BitConverter.GetBytes(End).CopyTo(header, 68);
        for (var i = 0; i < 109; i++) BitConverter.GetBytes(i < fatSectors ? fatStart + i : Free).CopyTo(header, 76 + i * 4);

        using var o = new MemoryStream();
        void Pad(byte[] b, int sectors) { o.Write(b); o.Write(new byte[sectors * Sector - b.Length]); }
        o.Write(header);
        foreach (var b in big) Pad(b, (b.Length + Sector - 1) / Sector);
        if (miniSectors > 0) Pad(mini.ToArray(), miniSectors);
        Pad([.. miniFat.SelectMany(BitConverter.GetBytes)], miniFatSectors);
        Pad([.. dirEntries.SelectMany(e => e)], directorySectors);
        Pad([.. chains.SelectMany(BitConverter.GetBytes)], fatSectors);
        return o.ToArray();
    }

    /// <summary>An <c>Ole10Native</c> stream holding a file.</summary>
    public static byte[] Native(string name, byte[] bytes)
    {
        using var s = new MemoryStream();
        void Int(int i) => s.Write(BitConverter.GetBytes(i));
        void Z(string t) { s.Write(Encoding.Latin1.GetBytes(t)); s.WriteByte(0); }
        Int(0);
        s.Write(BitConverter.GetBytes((ushort)2));
        Z(name);
        Z($"C:\\temp\\{name}");
        Int(0x00030000);
        var temp = Encoding.Latin1.GetBytes($"C:\\temp\\{name}\0");
        Int(temp.Length);
        s.Write(temp);
        Int(bytes.Length);
        s.Write(bytes);
        return s.ToArray();
    }
}

/// <summary>A workbook as a main document (A.5, D123).</summary>
public sealed class XlsxImportTests
{
    static ConvertedDocument Convert(byte[] file) => ImportPipeline.Run(ImportPipeline.ForFile("a.xlsx")!, file, new()).Document;

    static string Text(ConvertedDocument d, string section) => d.Sections.Single(s => s.Id == section).Text;

    static string Row(int r, params string[] cells) => XlsxFixture.Row(r, cells);
    static string Str(string at, string text) => XlsxFixture.Str(at, text);
    static string Num(string at, string value, int? style = null) => XlsxFixture.Num(at, value, style);

    [Fact]
    public void Each_visible_sheet_is_a_top_section_in_workbook_order_holding_one_table_with_no_heading_guessed()
    {
        var file = XlsxFixture.Build([
            new("Zebra", Row(2, Str("B2", "Tier"), Str("C2", "Fee")) + Row(3, Str("B3", "1 Introduction"), Num("C3", "12"))),
            new("Apple", Row(1, Str("A1", "Only"))),
            new("Empty"),
        ]);
        var d = Convert(file);
        Assert.Equal(["Zebra", "Apple", "Empty"], d.Sections.Select(s => s.Id));
        Assert.All(d.Sections, s => Assert.Equal((1, "path", (string?)null), (s.Level, s.IdKind, s.Parent)));
        // Outer empty rows and columns are trimmed, and a cell that reads like a heading stays a cell.
        Assert.Equal("| Tier | Fee |\n|---|---|\n| 1 Introduction | 12 |", Text(d, "Zebra"));
        Assert.Equal("# Zebra\n\n| Tier | Fee |\n|---|---|\n| 1 Introduction | 12 |\n\n# Apple\n\n| Only |\n|---|\n\n# Empty\n", d.Markdown);
        Assert.Equal(1, d.Sections.First().Line);
        Assert.Equal(7, d.Sections[1].Line);
        Assert.Contains("sheet \"Empty\": no visible text", d.Problems);
        Assert.Equal(Convert(file).Markdown, d.Markdown);
        Assert.Equal(Convert(file).Problems, d.Problems);
    }

    [Fact]
    public void An_empty_row_inside_a_table_stays_and_a_bar_is_escaped()
    {
        var d = Convert(XlsxFixture.Build([new("S", Row(1, Str("A1", "a|b")) + Row(3, Str("A3", "c")))]));
        Assert.Equal("| a\\|b |\n|---|\n|  |\n| c |", Text(d, "S"));
        Assert.DoesNotContain(d.Problems, p => p.StartsWith("completeness:", StringComparison.Ordinal));
    }

    const string Styles = """
        <numFmts count="2"><numFmt numFmtId="164" formatCode="0.00%"/><numFmt numFmtId="165" formatCode="dd/mm/yyyy"/></numFmts>
        <cellXfs count="6"><xf numFmtId="0"/><xf numFmtId="14"/><xf numFmtId="20"/><xf numFmtId="164"/><xf numFmtId="165"/><xf numFmtId="22"/></cellXfs>
        """;

    [Fact]
    public void A_value_shows_what_is_stored_and_a_built_in_date_is_an_iso_date()
    {
        var d = Convert(XlsxFixture.Build([new("V", Row(1,
                """<c r="A1" t="s"><v>0</v></c>""", """<c r="B1" t="s"><v>1</v></c>""", Num("C1", "1.10"), Num("D1", "1E+3"),
                """<c r="E1" t="b"><v>1</v></c>""", """<c r="F1" t="b"><v>0</v></c>""", """<c r="G1" t="e"><v>#REF!</v></c>""") +
            Row(2, Num("A2", "45366", 1), Num("B2", "0.5", 2), Num("C2", "0.125", 3), Num("D2", "45366", 4), Num("E2", "45366.5", 5), Num("F2", "45366.5", 1)))],
            shared: """<si><t>plain</t></si><si><r><t>rich </t></r><r><t>text</t></r><rPh sb="0" eb="1"><t>skip</t></rPh></si>""", styles: Styles));
        Assert.Equal(
            "| plain | rich text | 1.10 | 1E+3 | TRUE | FALSE | #REF! |\n|---|---|---|---|---|---|---|\n| 2024-03-15 | 12:00:00 | 0.125 | 45366 | 2024-03-15T12:00:00 | 2024-03-15T12:00:00 |  |",
            Text(d, "V"));
        Assert.Contains("sheet \"V\": 1 cells use the custom number format \"0.00%\", from C2; the raw number is shown", d.Problems);
        Assert.Contains("sheet \"V\": 1 cells use the custom number format \"dd/mm/yyyy\", from D2; the raw number is shown", d.Problems);
    }

    [Theory]
    [InlineData("1", false, false, "1900-01-01")]
    [InlineData("59", false, false, "1900-02-28")]
    [InlineData("61", false, false, "1900-03-01")]
    [InlineData("60", false, false, null)]
    [InlineData("-1", false, false, null)]
    [InlineData("0", true, false, "1904-01-01")]
    [InlineData("0.25", false, true, "06:00:00")]
    [InlineData("0.99999999", false, false, "1900-01-01")]
    [InlineData("99999999", false, false, null)]
    [InlineData("oops", false, false, null)]
    public void A_serial_number_is_a_date_only_when_it_is_a_real_one(string stored, bool date1904, bool time, string? iso) =>
        Assert.Equal(iso, XlsxConverter.IsoDate(stored, date1904, time));

    [Fact]
    public void A_date_that_cant_be_real_keeps_its_number_and_is_listed()
    {
        var d = Convert(XlsxFixture.Build([new("S", Row(1, Num("A1", "60", 1)))], styles: Styles));
        Assert.Equal("| 60 |\n|---|", Text(d, "S"));
        Assert.Contains("sheet \"S\": A1: isn't a real date in its date format; the raw number is shown", d.Problems);
    }

    [Fact]
    public void A_workbook_on_the_1904_calendar_counts_from_1904()
    {
        var d = Convert(XlsxFixture.Build([new("S", Row(1, Num("A1", "1", 1)))], styles: Styles, workbookExtra: """<workbookPr date1904="1"/>"""));
        Assert.Equal("| 1904-01-02 |\n|---|", Text(d, "S"));
    }

    [Fact]
    public void A_formula_shows_its_cached_value_and_is_listed_and_one_with_none_is_listed_and_not_computed()
    {
        var d = Convert(XlsxFixture.Build([new("F",
            Row(1, Num("A1", "2"), """<c r="B1"><f>A1*2</f><v>4</v></c>""", """<c r="C1"><f>A1+1</f></c>""") +
            Row(2, """<c r="A2"><f t="shared" ref="A2:A3" si="0">B1+1</f><v>5</v></c>""") +
            Row(3, """<c r="A3"><f t="shared" si="0"/><v>6</v></c>"""))]));
        Assert.Equal("| 2 | 4 |\n|---|---|\n| 5 |  |\n| 6 |  |", Text(d, "F"));
        Assert.Contains("sheet \"F\": formula at B1: A1*2; its cached value is shown", d.Problems);
        Assert.Contains("sheet \"F\": formula at C1 has no cached value and wasn't computed: A1+1", d.Problems);
        // A shared formula is one line for its whole range.
        Assert.Contains("sheet \"F\": formula at A2:A3 (shared): B1+1; its cached value is shown", d.Problems);
        Assert.DoesNotContain(d.Problems, p => p.Contains("formula at A3", StringComparison.Ordinal));
    }

    [Fact]
    public void Merged_cells_repeat_their_value_and_are_listed_and_text_under_a_merge_is_listed()
    {
        var d = Convert(XlsxFixture.Build([new("M", Row(1, Str("A1", "wide"), Str("B1", "under"), Str("C1", "x")) + Row(2, Str("C2", "y")),
            After: """<mergeCells count="2"><mergeCell ref="A1:B2"/><mergeCell ref="D5:E5"/></mergeCells>""")]));
        Assert.Equal("| wide | wide | x |\n|---|---|---|\n| wide | wide | y |", Text(d, "M"));
        Assert.Contains("sheet \"M\": merged cells A1:B2 repeat their value; 1 cells under the merge had text of their own, left out", d.Problems);
        Assert.DoesNotContain(d.Problems, p => p.Contains("D5", StringComparison.Ordinal));
        Assert.DoesNotContain(d.Problems, p => p.StartsWith("completeness:", StringComparison.Ordinal));
    }

    [Fact]
    public void Hidden_sheets_rows_and_columns_are_left_out_and_listed_including_very_hidden()
    {
        var d = Convert(XlsxFixture.Build([
            new("Seen", """<row r="1"><c r="A1" t="inlineStr"><is><t>shown</t></is></c><c r="B1" t="inlineStr"><is><t>secret column</t></is></c></row><row r="2" hidden="1"><c r="A2" t="inlineStr"><is><t>secret row</t></is></c></row><row r="3"><c r="A3" t="inlineStr"><is><t>also shown</t></is></c></row>""",
                Before: """<cols><col min="2" max="2" hidden="1"/></cols>"""),
            new("Back", Row(1, Str("A1", "secret sheet")), State: "hidden"),
            new("Deep", Row(1, Str("A1", "very secret")), State: "veryHidden"),
        ]));
        Assert.Equal(["Seen"], d.Sections.Select(s => s.Id));
        Assert.Equal("| shown |\n|---|\n| also shown |", Text(d, "Seen"));
        Assert.DoesNotContain("secret", d.Markdown);
        Assert.Contains("hidden sheet \"Back\" (hidden): left out, with 1 cells holding text", d.Problems);
        Assert.Contains("hidden sheet \"Deep\" (veryHidden): left out, with 1 cells holding text", d.Problems);
        Assert.Contains("sheet \"Seen\": hidden row 2 left out, with 1 cells holding text", d.Problems);
        Assert.Contains("sheet \"Seen\": hidden column B left out, with 1 cells holding text", d.Problems);
        Assert.DoesNotContain(d.Problems, p => p.StartsWith("completeness:", StringComparison.Ordinal));
        Assert.Equal(("hidden", "Seen"), Kind("sheet \"Seen\": hidden row 2 left out, with 1 cells holding text", d));
    }

    static (string, string?) Kind(string line, ConvertedDocument d)
    {
        var o = ImportProblems.Of(line, [.. d.Sections.Select(FsdImport.SectionJson)]);
        return ((string)o["kind"]!, (string?)o["sectionNumber"]);
    }

    [Fact]
    public void No_visible_text_stops_even_with_text_on_a_hidden_sheet()
    {
        var hidden = XlsxFixture.Build([new("Blank"), new("Back", Row(1, Str("A1", "text")), State: "hidden")]);
        Assert.Equal("no_text", Assert.Throws<StangentException>(() => Convert(hidden)).Code);
    }

    [Fact]
    public void Notes_and_threaded_comments_are_listed_as_structured_comments_never_in_the_text()
    {
        var d = Convert(XlsxFixture.Build([new("C", Row(1, Str("A1", "cell")), Parts: part =>
        {
            XlsxFixture.Text(part.AddNewPart<WorksheetCommentsPart>(), $"""
                <comments {XlsxFixture.Ns}><authors><author>Jane</author><author>tc={"{"}A{"}"}</author></authors><commentList>
                <comment ref="A1" authorId="0"><text><t>Is this right?</t></text></comment>
                <comment ref="B1" authorId="1"><text><t>[Threaded comment] old readers only</t></text></comment></commentList></comments>
                """);
            XlsxFixture.Text(part.AddNewPart<WorksheetThreadedCommentsPart>(), """
                <ThreadedComments xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
                <threadedComment ref="B1" personId="{P1}" id="{T1}" done="1"><text>Settled it.</text></threadedComment></ThreadedComments>
                """);
        })], parts: book => XlsxFixture.Text(book.AddNewPart<WorkbookPersonPart>(), """
            <personList xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments"><person displayName="Omar" id="{P1}"/></personList>
            """)));
        Assert.Equal("| cell |\n|---|", Text(d, "C"));
        Assert.Contains("comment: section \"C\" by \"Jane\" open: \"A1: Is this right?\"", d.Problems);
        Assert.Contains("comment: section \"C\" by \"Omar\" resolved: \"B1: Settled it.\"", d.Problems);
        Assert.DoesNotContain(d.Problems, p => p.Contains("old readers", StringComparison.Ordinal));
        var s = ImportProblems.Of("comment: section \"C\" by \"Jane\" open: \"A1: Is this right?\"", [.. d.Sections.Select(FsdImport.SectionJson)]);
        Assert.Equal(("comment", "Jane", "open", "C"), ((string)s["kind"]!, (string)s["author"]!, (string)s["state"]!, (string)s["sectionNumber"]!));
    }

    [Fact]
    public void Charts_pictures_pivot_tables_links_macros_and_protection_are_named_and_not_read()
    {
        var vba = XlsxFixture.Ole(("VBA/Module1", new byte[10]), ("VBA/ThisWorkbook", new byte[10]), ("VBA/dir", new byte[10]), ("PROJECT", new byte[10]));
        var file = XlsxFixture.Build([new("S", Row(1, Str("A1", "text")), After: """<sheetProtection sheet="1"/><headerFooter><oddHeader>Top secret</oddHeader></headerFooter>""", Parts: part =>
        {
            var drawing = part.AddNewPart<DrawingsPart>();
            XlsxFixture.Text(drawing, """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                <xdr:pic/><xdr:pic/><xdr:sp><xdr:txBody><a:p><a:r><a:t>A text box</a:t></a:r></a:p></xdr:txBody></xdr:sp></xdr:wsDr>
                """);
            XlsxFixture.Text(drawing.AddNewPart<ChartPart>(), """<c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"/>""");
            XlsxFixture.Text(part.AddNewPart<PivotTablePart>(), $"<pivotTableDefinition {XlsxFixture.Ns}/>");
        })], workbookExtra: """<workbookProtection lockStructure="1"/>""", macros: true, parts: book =>
        {
            using (var v = book.AddNewPart<VbaProjectPart>().GetStream(FileMode.Create)) v.Write(vba);
            var link = book.AddNewPart<ExternalWorkbookPart>();
            XlsxFixture.Text(link, $"<externalLink {XlsxFixture.Ns}/>");
            link.AddExternalRelationship("http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath", new Uri("file:///other.xlsx"));
        });
        var d = Convert(file);
        Assert.Equal("| text |\n|---|", Text(d, "S"));
        Assert.Contains("sheet \"S\": a chart not read", d.Problems);
        Assert.Contains("sheet \"S\": 2 pictures not read", d.Problems);
        Assert.Contains("sheet \"S\": a shape with text not read", d.Problems);
        Assert.Contains("sheet \"S\": a pivot table not read", d.Problems);
        Assert.Contains("sheet \"S\": page headers and footers not read", d.Problems);
        Assert.Contains("sheet \"S\": is protected; it's readable, and read as stored", d.Problems);
        Assert.Contains("workbook: its structure is protected; it's readable, and read as stored", d.Problems);
        Assert.Contains("workbook: an external link not read (file:///other.xlsx)", d.Problems);
        Assert.Contains("workbook: macros not read, and never run: Module1, ThisWorkbook", d.Problems);
        var kinds = d.Problems.Select(p => Kind(p, d).Item1).ToHashSet();
        Assert.Contains("not-read", kinds);
        Assert.Contains("protected", kinds);
        Assert.Equal("xlsx", ImportPipeline.ForFile("book.XLSM")!.Name);
        Assert.Null(ImportPipeline.ForFile("old.xls"));
    }

    [Fact]
    public void A_locked_workbook_stops_with_instructions_and_a_damaged_one_says_so()
    {
        var locked = XlsxFixture.Ole(("EncryptedPackage", new byte[10]), ("EncryptionInfo", new byte[10]));
        var format = ImportPipeline.ForFile("a.xlsx")!;
        var stop = Assert.Throws<StangentException>(() => format.Safety(locked, new()));
        Assert.Equal("xlsx_locked", stop.Code);
        Assert.Contains("Encrypt with Password", stop.Message);
        Assert.Equal("invalid_xlsx", Assert.Throws<StangentException>(() => format.Safety([1, 2, 3], new())).Code);
        Assert.Contains("xlsx_locked", ImportReport.Stops);
    }

    [Fact]
    public void A_sensitivity_label_stops_the_workbook_unless_it_is_allowed()
    {
        var file = XlsxFixture.Build([new("S", Row(1, Str("A1", "text")))]);
        using (var stream = new MemoryStream())
        {
            stream.Write(file);
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, true))
            using (var w = new StreamWriter(zip.CreateEntry("docProps/custom.xml").Open()))
                w.Write("""<Properties xmlns="x"><property name="MSIP_Label_1234-ab_Name"><vt>Secret</vt></property><property name="MSIP_Label_1234-ab_Enabled"><vt>true</vt></property></Properties>""");
            file = stream.ToArray();
        }
        var format = ImportPipeline.ForFile("a.xlsx")!;
        Assert.Equal("document_labelled", Assert.Throws<StangentException>(() => format.Safety(file, new())).Code);
        Assert.Equal("ran", format.Safety(file, new(AllowLabels: ["Secret"])).Status);
    }

    static byte[] Shared(string log) => XlsxFixture.Build([new("S", Row(1, Str("A1", "text")))], parts: book =>
    {
        var headers = book.AddNewPart<WorkbookRevisionHeaderPart>();
        XlsxFixture.Text(headers, $"<headers {XlsxFixture.Ns}/>");
        XlsxFixture.Text(headers.AddNewPart<WorkbookRevisionLogPart>(), $"<revisions {XlsxFixture.Ns}>{log}</revisions>");
    });

    [Fact]
    public void A_shared_workbooks_revisions_stop_unless_accepted_and_with_none_to_show_it_is_listed()
    {
        var withChanges = Shared("""<rcc rId="1" sId="1"/><rrc rId="2" sId="1"/>""");
        var format = ImportPipeline.ForFile("a.xlsx")!;
        var stop = Assert.Throws<StangentException>(() => format.Safety(withChanges, new()));
        Assert.Equal("xlsx_tracked_changes", stop.Code);
        Assert.Contains("--accept-changes", stop.Message);
        Assert.Equal(2, format.Safety(withChanges, new(AcceptChanges: true)).Count);
        var accepted = XlsxConverter.Convert(withChanges, acceptChanges: true);
        Assert.Contains(accepted.Problems, p => p.StartsWith("--accept-changes: 2 revisions", StringComparison.Ordinal));
        Assert.Equal("tracked-changes", Kind(accepted.Problems[0], accepted).Item1);

        var none = Shared("");
        Assert.Equal(0, format.Safety(none, new()).Count);
        var listed = Convert(none);
        Assert.Contains(listed.Problems, p => p.StartsWith("shared workbook: ", StringComparison.Ordinal) && p.Contains("no accepted state", StringComparison.Ordinal));
        Assert.Equal("shared-workbook", Kind(listed.Problems[0], listed).Item1);
    }

    [Fact]
    public void A_very_large_workbook_is_flagged_by_its_stated_settings_and_still_imports()
    {
        var rows = string.Concat(Enumerable.Range(1, 5).Select(r => Row(r, Num($"A{r}", "1"))));
        var file = XlsxFixture.Build([new("A", rows), new("B", Row(1, Num("A1", "1")))]);
        var d = XlsxConverter.Convert(file, largeSheets: 1, largeRows: 4);
        Assert.Contains("workbook: 2 sheets, over fsd.excel.largeSheets (1)", d.Problems);
        Assert.Contains("sheet \"A\": 5 rows, over fsd.excel.largeRows (4)", d.Problems);
        Assert.DoesNotContain(d.Problems, p => p.Contains("sheet \"B\": ", StringComparison.Ordinal));
        Assert.DoesNotContain(XlsxConverter.Convert(file).Problems, p => p.Contains("fsd.excel", StringComparison.Ordinal));
        Assert.Equal("large-workbook", Kind("workbook: 2 sheets, over fsd.excel.largeSheets (1)", d).Item1);
    }

    [Fact]
    public void A_sheet_too_big_to_write_out_stops_rather_than_filling_memory()
    {
        var far = XlsxFixture.Build([new("S", Row(1, Str("A1", "a")) + Row(1048576, Str("XFD1048576", "z")))]);
        Assert.Equal("xlsx_too_large", Assert.Throws<StangentException>(() => Convert(far)).Code);
        var merged = XlsxFixture.Build([new("S", Row(1, Str("A1", "a")), After: """<mergeCells><mergeCell ref="A1:XFD1048576"/></mergeCells>""")]);
        Assert.Equal("xlsx_too_large", Assert.Throws<StangentException>(() => Convert(merged)).Code);
    }

    [Fact]
    public void The_completeness_check_reads_the_table_back_and_finds_a_cell_that_isnt_there()
    {
        var stored = new Dictionary<(int Row, int Column), string> { [(1, 1)] = "a", [(2, 1)] = "b|c", [(2, 2)] = "hidden" };
        var left = new HashSet<(int Row, int Column)> { (2, 2) };
        Assert.Empty(XlsxConverter.Unaccounted(stored, left, [1, 2], [1], "| a |\n|---|\n| b\\|c |"));
        Assert.Equal(["A2"], XlsxConverter.Unaccounted(stored, left, [1, 2], [1], "| a |\n|---|\n| other |"));
        Assert.Equal(["B2"], XlsxConverter.Unaccounted(stored, new HashSet<(int Row, int Column)>(), [1, 2], [1], "| a |\n|---|\n| b\\|c |"));
    }

    [Fact]
    public void Cells_with_no_address_follow_the_one_before_them()
    {
        var d = Convert(XlsxFixture.Build([new("S", """<row><c t="inlineStr"><is><t>a</t></is></c><c t="inlineStr"><is><t>b</t></is></c></row><row><c t="inlineStr"><is><t>c</t></is></c></row>""")]));
        Assert.Equal("| a | b |\n|---|---|\n| c |  |", Text(d, "S"));
    }

    [Fact]
    public void Cell_addresses_read_both_ways()
    {
        Assert.Equal((12, 28), XlsxConverter.ParseRef("AB12"));
        Assert.Equal((3, 1), XlsxConverter.ParseRef("$A$3"));
        Assert.Null(XlsxConverter.ParseRef("12"));
        Assert.Equal("AB12", XlsxConverter.Name(12, 28));
    }

    [Fact]
    public void Word_and_excel_and_pdf_fill_in_the_same_steps()
    {
        var run = ImportPipeline.Run(ImportPipeline.ForFile("a.xlsx")!, XlsxFixture.Rates(), new());
        Assert.Equal(["safety", "convert", "comments", "extract-files", "completeness"], run.Steps.Select(s => s.Step));
        Assert.All(run.Steps, s => Assert.Equal(ImportStepResult.Ran, s.Status));
    }

    [Fact]
    public void Flat_is_refused_for_a_workbook()
    {
        Assert.Equal("usage", Assert.Throws<StangentException>(() => ImportPipeline.ForFile("a.xlsx")!.CheckOptions(new(Flat: true))).Code);
    }

    [Fact]
    public void A_workbook_imports_through_the_command_with_ids_a_report_and_a_second_read_changes_nothing()
    {
        using var archive = new TempRepo();
        using var repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        var file = Path.Combine(repo.Root, "rates.xlsx");
        File.WriteAllBytes(file, XlsxFixture.Build([new("Rates", Row(1, Str("A1", "Tier"))), new("Back", Row(1, Str("A1", "x")), State: "hidden")]));
        var result = FsdImport.Import(repo.Repo, file);
        Assert.Equal(("imported", "ok-with-warnings"), ((string)result["status"]!, (string)result["verdict"]!));
        var dir = FsdImport.DocDir(repo.Root, "rates");
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "document.json")))!;
        Assert.Equal(("Rates", "S-0001"), ((string)doc["sections"]![0]!["id"]!, (string)doc["sections"]![0]!["permanentId"]!));
        Assert.Equal("T-0001", (string)doc["tables"]![0]!["id"]!);
        Assert.Contains("hidden sheet \"Back\"", File.ReadAllText(Path.Combine(dir, ImportReport.FileName)));
        var md = File.ReadAllBytes(Path.Combine(dir, "document.md"));
        Assert.Equal("unchanged", (string)FsdImport.Import(repo.Repo, file)["status"]!);
        var again = FsdImport.Reread(repo.Repo, "rates");
        Assert.Equal("unchanged", (string)again["status"]!);
        Assert.Equal(md, File.ReadAllBytes(Path.Combine(dir, "document.md")));
    }

    [Fact]
    public void An_old_excel_file_is_refused_with_what_to_do()
    {
        using var archive = new TempRepo();
        using var repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        var file = Path.Combine(repo.Root, "old.xls");
        File.WriteAllBytes(file, [1]);
        var stop = Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, file));
        Assert.Equal("unsupported_format", stop.Code);
        Assert.Contains("Save it as .xlsx", stop.Message);
    }
}
