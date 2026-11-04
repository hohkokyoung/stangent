using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;

namespace Stangent.Core.Tests;

/// <summary>Builds PDFs in memory, so no real document enters the repo.</summary>
sealed class PdfFixture : IDisposable
{
    readonly PdfDocumentBuilder builder = new();
    readonly PdfDocumentBuilder.AddedFont font;
    public const double Top = 700, Pitch = 14;

    public PdfFixture() => font = builder.AddStandard14Font(Standard14Font.Helvetica);

    public PdfDocumentBuilder.AddedFont Font => font;

    public void Dispose() => builder.Dispose();

    public PdfPageBuilder Page(params string[] lines)
    {
        var page = builder.AddPage(PageSize.A4);
        for (var i = 0; i < lines.Length; i++) Text(page, lines[i], 72, Top - i * Pitch);
        return page;
    }

    public void Text(PdfPageBuilder page, string text, double x, double y, double size = 12) => page.AddText(text, size, new PdfPoint(x, y), font);

    public static void Grid(PdfPageBuilder page, double[] xs, double[] ys)
    {
        foreach (var x in xs) page.DrawLine(new PdfPoint(x, ys[0]), new PdfPoint(x, ys[^1]), 0.5);
        foreach (var y in ys) page.DrawLine(new PdfPoint(xs[0], y), new PdfPoint(xs[^1], y), 0.5);
    }

    public static BookmarkNode Mark(string title, int level, int page, params BookmarkNode[] children) =>
        new DocumentBookmarkNode(title, level, new ExplicitDestination(page, ExplicitDestinationType.FitPage, ExplicitDestinationCoordinates.Empty), children);

    public static BookmarkNode MarkAt(string title, int level, int page, double top) =>
        new DocumentBookmarkNode(title, level, new ExplicitDestination(page, ExplicitDestinationType.XyzCoordinates, new ExplicitDestinationCoordinates(72, top)), []);

    public byte[] Build(params BookmarkNode[] marks)
    {
        if (marks.Length > 0) builder.Bookmarks = new Bookmarks(marks);
        return builder.Build();
    }
}

public class FsdPdfTests
{
    /// <summary>One page per title, each holding its title and a line of text, bookmarked at the given depths.</summary>
    static byte[] Outlined(params (string Title, int Depth)[] items)
    {
        using var pdf = new PdfFixture();
        var roots = new List<(int Depth, string Title, int Page, List<BookmarkNode> Children)>();
        for (var i = 0; i < items.Length; i++) pdf.Page(items[i].Title, $"Text of page {i + 1}.");
        BookmarkNode[] Nest(int start, int end, int depth)
        {
            var nodes = new List<BookmarkNode>();
            for (var i = start; i < end; i++)
            {
                if (items[i].Depth != depth) continue;
                var next = i + 1;
                while (next < end && items[next].Depth > depth) next++;
                nodes.Add(PdfFixture.Mark(items[i].Title, depth, i + 1, Nest(i + 1, next, depth + 1)));
            }
            return [.. nodes];
        }
        return pdf.Build(Nest(0, items.Length, 0));
    }

    static string Code(Action act) => Assert.Throws<StangentException>(act).Code;

    /// <summary>Every section of an <see cref="Outlined"/> document, with its body text still there.</summary>
    static ConvertedDocument ConvertOutlined(params (string Title, int Depth)[] items)
    {
        var doc = PdfConverter.Convert(Outlined(items));
        Assert.Equal(items.Length, doc.Sections.Count);
        for (var i = 0; i < items.Length; i++) Assert.Equal($"Text of page {i + 1}.", doc.Sections[i].Text);
        return doc;
    }

    [Fact]
    public void Bookmarks_become_sections_with_levels_ids_and_text()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Before anything.");
        pdf.Page("1 Orders", "Customers place orders.", "", "1.1 Placing", "The cart shows a total.");
        pdf.Page("1.2 Cancelling", "Until it ships.");
        var doc = PdfConverter.Convert(pdf.Build(
            PdfFixture.Mark("1 Orders", 0, 2, PdfFixture.Mark("1.1 Placing", 1, 2), PdfFixture.Mark("1.2 Cancelling", 1, 3))));
        Assert.Equal(["1", "1.1", "1.2"], doc.Sections.Select(s => s.Id));
        Assert.Equal([1, 2, 2], doc.Sections.Select(s => s.Level));
        Assert.Equal(["number", "number", "number"], doc.Sections.Select(s => s.IdKind));
        Assert.Equal(["Orders", "Placing", "Cancelling"], doc.Sections.Select(s => s.Title));
        Assert.Equal("1", doc.Sections[1].Parent);
        Assert.Contains("# 1 Orders", doc.Markdown);
        Assert.Contains("## 1.2 Cancelling", doc.Markdown);
        Assert.Equal("Customers place orders.", doc.Sections[0].Text);
        Assert.Equal("Until it ships.", doc.Sections[2].Text);
        Assert.Equal(["2 words come before the first section, so they're in the document but in no section"], doc.Problems);
        var lines = doc.Markdown.Split('\n');
        Assert.All(doc.Sections, s => Assert.EndsWith(s.Title, lines[s.Line - 1]));
    }

    [Fact]
    public void Numbered_children_keep_their_numbers_under_a_chapter_with_none()
    {
        var doc = ConvertOutlined(
            ("Chapter 7: Design", 0), ("7.1 Foo", 1), ("7.2 Bar", 1), ("7.2.1 Baz", 2),
            ("Chapter 8: Build", 0), ("8.2 Parts", 1), ("8.3.2 Stray", 2), ("Chapter 9: End", 0), ("2023 Roadmap", 1));
        Assert.Equal(["Chapter 7: Design", "7.1", "7.2", "7.2.1", "Chapter 8: Build", "8.2", "8.3.2", "Chapter 9: End", "Chapter 9: End / 2023 Roadmap"], doc.Sections.Select(s => s.Id));
        Assert.Equal("Chapter 7: Design", doc.Sections[1].Parent);
        Assert.Contains(doc.Problems, p => p.Contains("section 8.3.2 is out of step with its parent 8.2"));
        Assert.Contains(doc.Problems, p => p.Contains("\"2023 Roadmap\" starts with 2023, which doesn't fit"));
    }

    [Fact]
    public void A_duplicate_number_gets_a_suffix_and_a_problem()
    {
        var doc = ConvertOutlined(("1 Series", 0), ("1.1 List", 1), ("1.1 Details", 1));
        Assert.Equal(["1", "1.1", "1.1 (2)"], doc.Sections.Select(s => s.Id));
        Assert.Contains(doc.Problems, p => p.Contains("section id \"1.1\" is used again"));
    }

    [Fact]
    public void Headings_sit_where_their_titles_are_including_wrapped_ones()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Some text first on this page.", "", "", "A title long enough that it wraps", "onto a second line", "", "", "Text under it.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("A title long enough that it wraps onto a second line", 0, 1)));
        Assert.Contains("Some text first on this page.\n\n# A title long enough that it wraps onto a second line\n\nText under it.", doc.Markdown);
        Assert.Equal("Text under it.", Assert.Single(doc.Sections).Text);
        Assert.Equal(["6 words come before the first section, so they're in the document but in no section"], doc.Problems);
    }

    [Fact]
    public void A_title_not_on_its_page_is_listed_and_placed_at_the_top_of_the_page()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Found", "Words.");
        pdf.Page("Other words.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Found", 0, 1), PdfFixture.Mark("Missing title", 0, 2)));
        Assert.Contains("# Missing title\n\nOther words.", doc.Markdown);
        Assert.Equal("Other words.", doc.Sections[1].Text);
        Assert.Contains(doc.Problems, p => p.Contains("\"Missing title\" isn't a whole line on its page"));
    }

    [Fact]
    public void A_bookmark_with_no_page_inside_is_listed_not_used_and_its_children_still_count()
    {
        var problems = new List<string>();
        var targets = PdfConverter.OutlineTargetsForTest(new Bookmarks([
            PdfFixture.Mark("Beyond the end", 0, 99, PdfFixture.Mark("Child", 1, 2)),
            new UriBookmarkNode("A web link", 0, "https://example.invalid", []),
            PdfFixture.Mark("Fine", 0, 3),
        ]), 5, problems);
        Assert.Equal([("Child", 2, 2), ("Fine", 1, 3)], targets);
        Assert.Contains(problems, p => p.Contains("\"Beyond the end\" has no page inside the document"));
        Assert.Contains(problems, p => p.Contains("\"A web link\" has no page inside the document"));
    }

    static byte[] WithContents(int offsetPages, string? wrongTitle = null)
    {
        using var pdf = new PdfFixture();
        for (var i = 0; i < offsetPages - 1; i++) pdf.Page("Cover");
        pdf.Page("Contents", "1 Orders ........ 1", "1.1 Placing ...... 2", wrongTitle ?? "Appendix ......... 3");
        pdf.Page("1 Orders", "Text of orders.");
        pdf.Page("1.1 Placing", "Text of placing.");
        pdf.Page("Appendix", "Text of the appendix.");
        return pdf.Build();
    }

    [Fact]
    public void The_printed_contents_gives_sections_when_every_entry_checks_out_with_an_offset()
    {
        var doc = PdfConverter.Convert(WithContents(offsetPages: 2));
        Assert.Equal(["1", "1.1", "Appendix"], doc.Sections.Select(s => s.Id));
        Assert.Equal([1, 2, 1], doc.Sections.Select(s => s.Level));
        Assert.Equal("Text of placing.", doc.Sections[1].Text);
        Assert.Contains(doc.Problems, p => p.Contains("sections came from the printed contents") && p.Contains("coarser"));
    }

    [Fact]
    public void A_printed_entry_that_fails_its_check_stops_the_import()
    {
        var e = Assert.Throws<StangentException>(() => PdfConverter.Convert(WithContents(offsetPages: 1, wrongTitle: "Glossary ......... 3")));
        Assert.Equal("no_structure", e.Code);
        Assert.Contains("--flat", e.Message);
        Assert.Contains("Glossary", e.Message);
    }

    [Fact]
    public void No_bookmarks_or_contents_stops_and_flat_imports_one_labelled_section()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Just some text.");
        pdf.Page("And some more.");
        var bytes = pdf.Build();
        var e = Assert.Throws<StangentException>(() => PdfConverter.Convert(bytes));
        Assert.Equal("no_structure", e.Code);
        Assert.Contains("--flat", e.Message);

        var doc = PdfConverter.Convert(bytes, flat: true);
        var whole = Assert.Single(doc.Sections);
        Assert.Equal(FsdSections.FlatId, whole.Id);
        Assert.Equal("path", whole.IdKind);
        Assert.Contains("Just some text.", whole.Text);
        Assert.Contains("And some more.", whole.Text);
        Assert.StartsWith($"# {FsdSections.FlatId}\n", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("no structure"));
    }

    [Fact]
    public void A_docx_with_no_headings_stops_and_flat_imports_it()
    {
        var bytes = DocxWriter.Write("Just a paragraph.\nAnother one.");
        var e = Assert.Throws<StangentException>(() => DocxConverter.Convert(bytes));
        Assert.Equal("no_structure", e.Code);
        Assert.Contains("--flat", e.Message);

        var doc = DocxConverter.Convert(bytes, flat: true);
        Assert.Equal(FsdSections.FlatId, Assert.Single(doc.Sections).Id);
        Assert.Contains("Just a paragraph.", doc.Sections[0].Text);
        Assert.Contains(doc.Problems, p => p.Contains("no structure"));
    }

    [Fact]
    public void Flat_on_a_docx_with_headings_keeps_them_as_plain_text()
    {
        var doc = DocxConverter.Convert(DocxWriter.Write("# 1 Series\nText."), flat: true);
        Assert.Single(doc.Sections);
        Assert.Contains("1 Series", doc.Sections[0].Text);
        Assert.DoesNotContain("# 1 Series", doc.Sections[0].Text);
    }

    [Fact]
    public void A_locked_pdf_is_refused_cleanly()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Secret words.");
        // The builder can't encrypt, so the trailer gains an /Encrypt entry whose password checks the empty password fails.
        var text = Encoding.Latin1.GetString(pdf.Build());
        var at = text.LastIndexOf("trailer", StringComparison.Ordinal);
        var root = text.IndexOf("/Root", at, StringComparison.Ordinal);
        var filler = new string('x', 32);
        var locked = text[..root] + $"/Encrypt << /Filter /Standard /V 1 /R 2 /O ({filler}) /U ({filler}) /P -4 >> /ID [<00> <00>] " + text[root..];
        Assert.Equal("pdf_locked", Code(() => PdfConverter.Convert(Encoding.Latin1.GetBytes(locked))));
    }

    [Fact]
    public void Files_that_wont_open_get_clean_errors_and_write_nothing()
    {
        using var archive = new TempRepo();
        using var repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive.Root}', git: false }} }}\n");
        var pdf = Path.Combine(repo.Root, "bad.pdf");
        File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("this is not a pdf at all, only some plain words in a file"));
        Assert.Equal("invalid_pdf", Code(() => FsdImport.Import(repo.Repo, pdf)));
        var docx = Path.Combine(repo.Root, "locked.docx");
        File.WriteAllBytes(docx, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0, 0, 0, 0, 0]);  // an OLE file: how Word stores an encrypted .docx
        Assert.Equal("invalid_docx", Code(() => FsdImport.Import(repo.Repo, docx)));
        Assert.False(Directory.Exists(FsdImport.DocDir(repo.Root, "bad")));
        Assert.False(Directory.Exists(FsdImport.DocDir(repo.Root, "locked")));
        Assert.False(Directory.Exists(Path.Combine(archive.Root, "bad")));
        Assert.False(Directory.Exists(Path.Combine(archive.Root, "locked")));
    }

    [Fact]
    public void Excel_says_it_isnt_built_yet_and_other_formats_are_unsupported()
    {
        using var repo = new TempRepo();
        var xlsx = Path.Combine(repo.Root, "book.xlsx");
        File.WriteAllBytes(xlsx, [0]);
        var e = Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, xlsx));
        Assert.Equal("unsupported_format", e.Code);
        Assert.Contains("isn't built yet", e.Message);
        var txt = Path.Combine(repo.Root, "notes.txt");
        File.WriteAllBytes(txt, [0]);
        Assert.Equal("unsupported_format", Code(() => FsdImport.Import(repo.Repo, txt)));
    }

    [Fact]
    public void A_document_with_no_text_stops_and_a_page_with_none_is_listed()
    {
        using var empty = new PdfFixture();
        empty.Page();
        empty.Page();
        var bytes = empty.Build();
        Assert.Equal("no_text", Code(() => PdfConverter.Convert(bytes)));
        Assert.Equal("no_text", Code(() => PdfConverter.Convert(bytes, flat: true)));

        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "Words.");
        pdf.Page();
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains(doc.Problems, p => p.StartsWith("page 2: no text"));
        Assert.DoesNotContain(doc.Problems, p => p.StartsWith("page 1:"));
    }

    [Fact]
    public void Running_headers_and_footers_are_removed_and_listed_while_a_one_off_line_is_kept()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 5; n++)
        {
            var page = pdf.Page($"{n} Section", "Body words.", n == 2 ? "The one-off line." : $"More words {n}.");
            pdf.Text(page, "Shop FSD draft", 72, 800);
            pdf.Text(page, $"Page {n * 11}", 72, 30);
            if (n == 3) pdf.Text(page, "A note at the top once", 72, 780);
            marks.Add(PdfFixture.Mark($"{n} Section", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.DoesNotContain("Shop FSD draft", doc.Markdown);
        Assert.Contains("Page 11", doc.Markdown);                                       // 11, 22, 33: a number that doesn't follow the page isn't a page number
        Assert.Contains("The one-off line.", doc.Markdown);
        Assert.Contains("A note at the top once", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("running header on 5 pages") && p.Contains("\"Shop FSD draft\""));
        Assert.Contains("Body words.", doc.Sections[0].Text);                                         // it repeats, but sits right under its heading
        Assert.Contains(doc.Problems, p => p.Contains("a repeated line was kept") && p.Contains("\"Body words.\""));
    }

    [Fact]
    public void The_same_words_lower_down_a_page_are_not_a_running_header()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 4; n++)
        {
            // Same words near the top of every page, but never at the same height: it moves with the text above it.
            var page = pdf.Page($"{n} Section", $"Body words {n}.");
            pdf.Text(page, "The same closing line.", 72, 660 - n * 30);
            pdf.Text(page, $"Words below it {n}.", 72, 300);
            pdf.Text(page, $"Words further below {n}.", 72, 280);
            pdf.Text(page, $"Words at the end {n}.", 72, 260);
            marks.Add(PdfFixture.Mark($"{n} Section", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.Equal(4, doc.Markdown.Split("The same closing line.").Length - 1);
    }

    static double[] Xs => [72, 200, 330, 460];
    static double[] Ys => [640, 620, 600, 580];

    static void FillTable(PdfFixture pdf, PdfPageBuilder page, string[][] rows)
    {
        PdfFixture.Grid(page, Xs, Ys);
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c].Length > 0) pdf.Text(page, rows[r][c], Xs[c] + 4, Ys[r] - 14);
    }

    [Fact]
    public void A_drawn_grid_becomes_a_table_with_its_columns_separate_in_reading_position()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields", "Rules for the fields.");
        FillTable(pdf, page, [["Field", "Rule", "Note"], ["Total", "at most 100", "a|b"], ["Tags", "at most 3", "fixed"]]);
        pdf.Text(page, "Words after the table.", 72, 540);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains("Rules for the fields.\n\n| Field | Rule | Note |\n|---|---|---|\n| Total | at most 100 | a\\|b |\n| Tags | at most 3 | fixed |\n\nWords after the table.", doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("table"));
    }

    [Fact]
    public void A_merged_cell_keeps_its_place_with_blanks_for_the_rest()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        // The first row has no rule between its first two columns.
        page.DrawLine(new PdfPoint(72, 700), new PdfPoint(72, 640), 0.5);
        page.DrawLine(new PdfPoint(330, 700), new PdfPoint(330, 640), 0.5);
        page.DrawLine(new PdfPoint(460, 700), new PdfPoint(460, 640), 0.5);
        page.DrawLine(new PdfPoint(200, 680), new PdfPoint(200, 640), 0.5);
        foreach (var y in new double[] { 700, 680, 660, 640 }) page.DrawLine(new PdfPoint(72, y), new PdfPoint(460, y), 0.5);
        pdf.Text(page, "Spanning", 76, 686);
        pdf.Text(page, "Note", 334, 686);
        pdf.Text(page, "a1", 76, 666); pdf.Text(page, "b1", 204, 666); pdf.Text(page, "c1", 334, 666);
        pdf.Text(page, "a2", 76, 646); pdf.Text(page, "b2", 204, 646); pdf.Text(page, "c2", 334, 646);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains("| Spanning |  | Note |\n|---|---|---|\n| a1 | b1 | c1 |\n| a2 | b2 | c2 |", doc.Markdown);
    }

    [Fact]
    public void A_page_border_is_not_a_table_and_a_table_inside_it_still_is()
    {
        using var pdf = new PdfFixture();
        var plain = pdf.Page("1 Framed", "Words inside a frame.", "More words inside a frame.");
        plain.DrawRectangle(new PdfPoint(20, 20), 550, 800, 1);
        var framed = pdf.Page("2 Framed", "Words beside a table.");
        framed.DrawRectangle(new PdfPoint(20, 20), 550, 800, 1);
        FillTable(pdf, framed, [["a1", "b1", "c1"], ["a2", "b2", "c2"], ["a3", "b3", "c3"]]);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Framed", 0, 1), PdfFixture.Mark("2 Framed", 0, 2)));
        Assert.Equal("Words inside a frame. More words inside a frame.", doc.Sections[0].Text);
        Assert.Contains("| a1 | b1 | c1 |\n|---|---|---|\n| a2 | b2 | c2 |", doc.Sections[1].Text);
        Assert.Contains("Words beside a table.", doc.Sections[1].Text);
    }

    [Fact]
    public void Underlined_text_and_single_rows_or_columns_are_not_tables()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Rules");
        pdf.Text(page, "Underlined", 76, 646);
        page.DrawLine(new PdfPoint(72, 642), new PdfPoint(200, 642), 0.5);
        // A one-row grid.
        PdfFixture.Grid(page, [72, 200, 330], [620, 600]);
        pdf.Text(page, "only", 76, 606); pdf.Text(page, "row", 204, 606);
        // A one-column grid.
        PdfFixture.Grid(page, [72, 200], [560, 540, 520]);
        pdf.Text(page, "only", 76, 546); pdf.Text(page, "column", 76, 526);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Rules", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains("Underlined", doc.Markdown);
    }

    [Fact]
    public void A_table_whose_cell_fails_the_check_falls_back_to_plain_text_and_is_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 200, 330], [700, 660, 620]);
        // Drawn line by line across the columns, so a cell's two lines aren't next to each other in the page's own text.
        pdf.Text(page, "a-one", 76, 686); pdf.Text(page, "b-one", 204, 686);
        pdf.Text(page, "a-two", 76, 670); pdf.Text(page, "b-two", 204, 670);
        pdf.Text(page, "c-one", 76, 640); pdf.Text(page, "d-one", 204, 640);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        foreach (var word in new[] { "a-one", "b-one", "a-two", "b-two", "c-one", "d-one" }) Assert.Contains(word, doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: a table wasn't brought in exactly") && p.EndsWith("kept as plain text"));
    }

    [Fact]
    public void A_page_whose_words_dont_match_its_own_text_comes_out_plain_and_is_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields", "Words before.");
        FillTable(pdf, page, [["a1", "b1", "c1"], ["a2", "b2", "c2"], ["a3", "b3", "c3"]]);
        var bytes = pdf.Build(PdfFixture.Mark("1 Fields", 0, 1));
        Assert.Contains("| a1 | b1 | c1 |", PdfConverter.Convert(bytes).Markdown);

        // A bug that loses a block is what the check exists for: simulated here, and the page's words all survive.
        var doc = PdfConverter.Convert(bytes, false, (_, blocks) => [.. blocks.Where(b => b.Kind != PdfBlockKind.Table)]);
        Assert.DoesNotContain("|", doc.Markdown);
        foreach (var word in new[] { "Words", "before.", "a1", "b2", "c3" }) Assert.Contains(word, doc.Markdown);
        Assert.Contains("page 1: the words laid out didn't match the page's own words; kept as plain text", doc.Problems);
    }

    [Fact]
    public void The_word_check_counts_every_word_and_ignores_only_bullet_glyphs()
    {
        Assert.True(PdfConverter.SameWords(["b", "a", "•", "•c"], ["a", "b", "c"]));
        Assert.False(PdfConverter.SameWords(["a", "a"], ["a"]));
        Assert.False(PdfConverter.SameWords(["a", "b"], ["a", "c"]));
    }

    [Fact]
    public void Paragraphs_split_by_spacing_wrapped_lines_join_and_lists_are_kept()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders");
        double y = 660;
        void Line(string text, double x = 72, double step = 14) { pdf.Text(page, text, x, y); y -= step; }
        Line("A paragraph whose first line runs almost to the right of the text area, as wrapped lines do,");
        Line("and whose second line keeps the sentence going.", step: 34);
        Line("A second paragraph after a bigger gap.", step: 34);
        Line("• First point of the list", 90);
        Line("• Second point of the list", 90);
        Line("– not a bullet this one", 120, step: 34);
        Line("1. Step one here");
        Line("2. Step two here");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("A paragraph whose first line runs almost to the right of the text area, as wrapped lines do, and whose second line keeps the sentence going.\n\nA second paragraph after a bigger gap.", doc.Markdown);
        Assert.Contains("- First point of the list\n- Second point of the list", doc.Markdown);
        Assert.Contains("1. Step one here\n2. Step two here", doc.Markdown);
        Assert.DoesNotContain("•", doc.Markdown);
    }

    [Fact]
    public void A_sentence_over_a_page_break_stays_one_paragraph_and_no_word_is_changed()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "This sentence goes over the break with a self-");
        pdf.Page("contained word and ends here.");
        pdf.Page("A new start on this page.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Equal("This sentence goes over the break with a self- contained word and ends here.\n\nA new start on this page.", doc.Sections[0].Text);
    }

    [Fact]
    public void Images_keep_their_place_named_by_content_and_are_counted_as_unread()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Diagrams", "Above the picture.");
        page.AddPng(png, new PdfRectangle(72, 500, 172, 600));
        pdf.Text(page, "Below the picture.", 72, 480);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Diagrams", 0, 1)));
        var image = Assert.Single(doc.Images);
        Assert.Matches(@"^images/[0-9a-f]{12}\.png$", image.Name);
        Assert.Contains($"Above the picture.\n\n![]({image.Name})\n\nBelow the picture.", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("1 images in this document") && p.Contains("no AI or code reads them"));
    }

    static string Config(string archive) => $"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}', git: false }} }}\n";

    [Fact]
    public void Import_gives_the_same_bytes_every_time_and_keeps_the_original()
    {
        using var archive = new TempRepo();
        using var a = new TempRepo(Config(archive.Root));
        using var b = new TempRepo(Config(archive.Root));
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields", "Words before.");
        FillTable(pdf, page, [["a1", "b1", "c1"], ["a2", "b2", "c2"], ["a3", "b3", "c3"]]);
        pdf.Page("1.1 More", "Words after.");
        var file = Path.Combine(a.Root, "Shop.pdf");
        File.WriteAllBytes(file, pdf.Build(PdfFixture.Mark("1 Fields", 0, 1, PdfFixture.Mark("1.1 More", 1, 2))));

        var first = FsdImport.Import(a.Repo, file);
        Assert.Equal(("shop", "imported", 2), ((string)first["id"]!, (string)first["status"]!, (int)first["sections"]!));
        FsdImport.Import(b.Repo, file);
        foreach (var name in new[] { "document.md", "document.json" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(FsdImport.DocDir(a.Root, "shop"), name)), File.ReadAllBytes(Path.Combine(FsdImport.DocDir(b.Root, "shop"), name)));

        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(a.Root, "shop"), "document.json")))!;
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, doc.ToJsonString()));
        Assert.True(File.Exists(Path.Combine(archive.Root, (string)doc["original"]!["path"]!)));
        Assert.Equal("unchanged", (string)FsdImport.Import(a.Repo, file)["status"]!);
    }

    [Fact]
    public void Flat_replaces_a_structured_import_of_the_same_file_and_validates()
    {
        using var archive = new TempRepo();
        using var repo = new TempRepo(Config(archive.Root));
        var file = Path.Combine(repo.Root, "shop.pdf");
        File.WriteAllBytes(file, Outlined(("1 Series", 0)));
        FsdImport.Import(repo.Repo, file);
        var flat = FsdImport.Import(repo.Repo, file, flat: true);
        Assert.Equal(("candidate", 1), ((string)flat["status"]!, (int)flat["sections"]!));
        Assert.Equal("waiting", (string)FsdImport.Import(repo.Repo, file, flat: true)["status"]!);
        // Both sections of the current version go, with no text in common: nothing to settle.
        FsdVersions.Promote(repo.Repo, "shop", "Pat");
        Assert.Equal("unchanged", (string)FsdImport.Import(repo.Repo, file, flat: true)["status"]!);
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(repo.Root, "shop"), "document.json")))!;
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, doc.ToJsonString()));
    }

    // ---- Running lines (fresh check, finding 1)

    [Fact]
    public void A_rule_whose_number_doesnt_follow_the_page_is_kept()
    {
        using var pdf = new PdfFixture();
        for (var n = 1; n <= 3; n++)
        {
            var page = pdf.Page($"{n} Rule", $"Body of rule {n} here.");
            pdf.Text(page, $"Orders are limited to {n * 5} items.", 72, 100);
        }
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Rule", 0, 1), PdfFixture.Mark("2 Rule", 0, 2), PdfFixture.Mark("3 Rule", 0, 3)));
        for (var n = 1; n <= 3; n++)
        {
            Assert.Contains($"Body of rule {n} here.", doc.Sections[n - 1].Text);
            Assert.Contains($"Orders are limited to {n * 5} items.", doc.Sections[n - 1].Text);
        }
        Assert.DoesNotContain(doc.Problems, p => p.StartsWith("removed"));
    }

    [Fact]
    public void A_footer_whose_number_follows_the_page_is_removed_and_shown_as_it_reads()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 5; n++)
        {
            var page = pdf.Page($"{n} Sec", $"Body words {new string('a', n)}.");
            pdf.Text(page, $"Shop FSD page {n + 10}", 72, 30);
            marks.Add(PdfFixture.Mark($"{n} Sec", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.DoesNotContain("Shop FSD page", doc.Markdown);
        var problem = Assert.Single(doc.Problems, p => p.StartsWith("removed a running footer"));
        Assert.Contains("on 5 pages", problem);
        Assert.Contains("\"Shop FSD page 11\" to \"Shop FSD page 15\"", problem);
        Assert.DoesNotContain("#", problem);
    }

    [Fact]
    public void Roman_and_arabic_page_numbers_in_one_footer_are_removed_together()
    {
        using var pdf = new PdfFixture();
        string[] roman = ["i", "ii", "iii"];
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 6; n++)
        {
            var page = pdf.Page($"{n} Sec", $"Body words {new string('a', n)}.");
            pdf.Text(page, $"{(n <= 3 ? roman[n - 1] : (n - 3).ToString())} Acme Corporation", 72, 30);
            marks.Add(PdfFixture.Mark($"{n} Sec", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.DoesNotContain("Acme", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("running footer on 6 pages"));
    }

    [Fact]
    public void Odd_and_even_headers_are_judged_separately()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 8; n++)
        {
            var page = pdf.Page($"{n} Sec", $"Body words of section {new string('x', n)}.");
            pdf.Text(page, n % 2 == 1 ? "Shop FSD draft" : "Acme Corporation", 72, 800);
            marks.Add(PdfFixture.Mark($"{n} Sec", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.DoesNotContain("Shop FSD draft", doc.Markdown);
        Assert.DoesNotContain("Acme Corporation", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("running header on 4 pages") && p.Contains("Acme"));
        Assert.Equal(8, doc.Sections.Count);
    }

    [Fact]
    public void A_section_title_is_never_a_running_line_even_when_its_number_follows_the_page()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 4; n++)
        {
            var page = pdf.Page($"Chapter {n}");
            pdf.Text(page, $"Words for this chapter {new string('x', n)}.", 72, 500);
            marks.Add(PdfFixture.Mark($"Chapter {n}", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.Contains("# Chapter 3", doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("whole line") || p.StartsWith("removed"));
    }

    [Fact]
    public void A_running_line_is_removed_only_at_its_own_distance_from_the_edge()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 5; n++)
        {
            var page = pdf.Page($"{n} Sec", $"Body words {new string('a', n)}.");
            pdf.Text(page, "Shop FSD draft", 72, 800);
            if (n == 2) pdf.Text(page, "Shop FSD draft", 72, 780);                           // the same words, as text just under the header
            marks.Add(PdfFixture.Mark($"{n} Sec", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.Single(doc.Markdown.Split("Shop FSD draft").Skip(1));
    }

    // ---- Headings (finding 2)

    [Fact]
    public void A_bookmarks_position_picks_the_real_heading_over_a_whole_line_mention()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("Contents of this page:", "Summary", "Details");
        pdf.Text(page, "Words before the section.", 72, 600);
        pdf.Text(page, "Summary", 72, 500);
        pdf.Text(page, "The real summary text is here.", 72, 480);
        pdf.Text(page, "Details", 72, 400);
        pdf.Text(page, "The real details text.", 72, 380);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.MarkAt("Summary", 0, 1, 515), PdfFixture.MarkAt("Details", 0, 1, 415)));
        Assert.Equal("The real summary text is here.", doc.Sections[0].Text);
        Assert.Equal("The real details text.", doc.Sections[1].Text);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("whole line"));
    }

    [Fact]
    public void Without_a_position_the_first_whole_line_is_taken_and_listed_as_ambiguous()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Summary", "", "", "Words.", "", "", "Summary", "More words.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("Summary", 0, 1)));
        Assert.StartsWith("# Summary", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("\"Summary\" is on 2 whole lines") && p.Contains("first was taken"));
    }

    [Fact]
    public void A_heading_never_cuts_a_line_in_two()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Some opening words on the page.", "Introduction of new tariffs is decided by the board.", "", "", "The tariff applies to all orders.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("Introduction", 0, 1)));
        Assert.Contains("Introduction of new tariffs is decided by the board.", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("\"Introduction\" isn't a whole line on its page") && p.Contains("left as it is"));
    }

    [Fact]
    public void Titles_out_of_bookmark_order_are_placed_where_they_are_and_listed()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Beta", "", "", "Beta words.", "", "", "Alpha", "", "", "Alpha words.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("Alpha", 0, 1), PdfFixture.Mark("Beta", 0, 1)));
        Assert.Equal(["Beta", "Alpha"], doc.Sections.Select(s => s.Id));
        Assert.Equal("Beta words.", doc.Sections[0].Text);
        Assert.Contains(doc.Problems, p => p.Contains("\"Beta\" is above the one listed before it"));
    }

    [Fact]
    public void Sentence_start_mentions_dont_verify_a_printed_contents()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Contents", "Orders ........ 1", "Payments ...... 2", "Refunds ....... 3");
        pdf.Page("Orders must be placed before noon on any working day.", "More text here.");
        pdf.Page("Payments are taken on delivery of the goods.", "More text here too.");
        pdf.Page("Refunds take ten days to reach the account.", "Still more text here.");
        Assert.Equal("no_structure", Code(() => PdfConverter.Convert(pdf.Build())));
    }

    [Fact]
    public void Two_offsets_that_both_fit_stop_the_import()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Contents", "Orders ........ 1", "Payments ...... 2", "Refunds ....... 3");
        for (var repeat = 0; repeat < 2; repeat++)
            foreach (var title in new[] { "Orders", "Payments", "Refunds" }) pdf.Page(title, "Some words.");
        var e = Assert.Throws<StangentException>(() => PdfConverter.Convert(pdf.Build()));
        Assert.Equal("no_structure", e.Code);
        Assert.Contains("2 different page offsets", e.Message);
    }

    [Fact]
    public void A_contents_page_cant_verify_itself()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Contents", "Contents ...... 1", "Orders ........ 2", "Refunds ....... 3");
        pdf.Page("Orders", "Order words.");
        pdf.Page("Refunds", "Refund words.");
        Assert.Equal("no_structure", Code(() => PdfConverter.Convert(pdf.Build())));
    }

    [Fact]
    public void When_page_numbers_fall_the_contents_has_ended_and_a_wrapped_entry_is_joined()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Contents", "1 Orders ........ 1", "2 A title long enough", "to wrap ........ 2", "3 Refunds ....... 3");
        pdf.Page("List of tables", "Table 1 Prices .... 1", "Table 2 Dates ..... 2", "Table 3 Costs ..... 3");
        pdf.Page("1 Orders", "Order words.");
        pdf.Page("2 A title long enough to wrap", "Other words.");
        pdf.Page("3 Refunds", "Refund words.");
        var doc = PdfConverter.Convert(pdf.Build());
        Assert.Equal(["1", "2", "3"], doc.Sections.Select(s => s.Id));
        Assert.Equal("A title long enough to wrap", doc.Sections[1].Title);
        Assert.Contains(doc.Problems, p => p.Contains("3 dotted lines after the page numbers start again"));
    }

    // ---- Tables (finding 3)

    [Fact]
    public void A_table_with_open_sides_has_words_outside_its_cells_so_it_fails()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        double[] ys = [640, 620, 600, 580];
        double[] xs = [200, 330, 460];
        foreach (var x in xs) page.DrawLine(new PdfPoint(x, ys[0]), new PdfPoint(x, ys[^1]), 0.5);
        foreach (var y in ys) page.DrawLine(new PdfPoint(72, y), new PdfPoint(560, y), 0.5);
        string[][] rows = [["Field", "Rule", "Note", "Owner"], ["Total", "max 100", "fixed", "Ann"], ["Tags", "max 3", "open", "Bob"]];
        double[] tx = [76, 204, 334, 464];
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 4; c++) pdf.Text(page, rows[r][c], tx[c], ys[r] - 14);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: a table wasn't brought in exactly") && p.Contains("no closed cell"));
    }

    [Theory]
    [InlineData(160, "Max total is 100 pounds")]                                                  // the run overflows: its last word starts past the rule
    [InlineData(110, "Administrator")]                                                            // one word straddles the rule
    public void Text_across_a_cell_rule_fails_the_table(double rule, string text)
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, rule, 330], [640, 620, 600]);
        pdf.Text(page, text, 76, 626);
        pdf.Text(page, "Rule", 204, 626);
        pdf.Text(page, "a2", 76, 606);
        pdf.Text(page, "b2", 204, 606);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: a table wasn't brought in exactly") && p.Contains("across a cell's rule"));
    }

    [Fact]
    public void A_cell_left_open_by_a_missing_rule_fails_the_table()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        foreach (var x in new double[] { 72, 200, 330 }) page.DrawLine(new PdfPoint(x, 640), new PdfPoint(x, 600), 0.5);
        foreach (var y in new double[] { 640, 620 }) page.DrawLine(new PdfPoint(72, y), new PdfPoint(330, y), 0.5);
        page.DrawLine(new PdfPoint(72, 600), new PdfPoint(200, 600), 0.5);                            // the last rule stops short: the bottom-right cell is open
        pdf.Text(page, "a1", 76, 626); pdf.Text(page, "b1", 204, 626);
        pdf.Text(page, "a2", 76, 606); pdf.Text(page, "b2", 204, 606);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: a table wasn't brought in exactly") && p.Contains("no closed cell"));
    }

    [Fact]
    public void A_grid_with_text_in_only_one_row_is_listed_and_its_text_kept()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 200, 330], [640, 620, 600]);
        pdf.Text(page, "Field", 76, 626);
        pdf.Text(page, "Rule", 204, 626);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains("Field", doc.Markdown);
        Assert.Contains("Field\n\nRule", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("a drawn grid of 2 rows and 2 columns starting \"Field\"") && p.Contains("kept apart"));
    }

    [Fact]
    public void A_grid_with_no_text_is_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields", "Words.");
        PdfFixture.Grid(page, [72, 200, 330], [500, 480, 460]);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains(doc.Problems, p => p.Contains("a drawn grid of 2 rows and 2 columns has no text in it"));
    }

    [Fact]
    public void A_one_column_grid_is_read_as_text_and_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 400], [640, 620, 600, 580]);
        pdf.Text(page, "The first rule", 76, 626);
        pdf.Text(page, "The second rule", 76, 606);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains("The first rule\n\nThe second rule", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("3 rows and 1 columns"));
    }

    [Fact]
    public void A_grid_that_frames_the_page_is_listed_and_not_a_table()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Framed", "Words in the frame.", "More words in the frame.");
        PdfFixture.Grid(page, [20, 500, 570], [820, 60, 20]);
        pdf.Text(page, "Side", 504, 700);
        pdf.Text(page, "Foot", 72, 30);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Framed", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("frames the page"));
    }

    [Fact]
    public void Shading_behind_a_cell_doesnt_split_the_grid()
    {
        // The builder strokes every rectangle it draws, so this content is written by hand: a fill-only rectangle, inset in its cell as Word draws it.
        var content = new StringBuilder("0.8 g 78 622 110 16 re f 0 g\n");
        foreach (var x in new[] { 72, 200, 330 }) content.Append($"{x} 640 m {x} 600 l S\n");
        foreach (var y in new[] { 640, 620, 600 }) content.Append($"72 {y} m 330 {y} l S\n");
        foreach (var (text, x, y) in new[] { ("Field", 76, 626), ("Rule", 204, 626), ("a2", 76, 606), ("b2", 204, 606), ("Words", 72, 700) })
            content.Append($"BT /F1 12 Tf {x} {y} Td ({text}) Tj ET\n");
        var doc = PdfConverter.Convert(PlainPdf([], "", content: content.ToString()), flat: true);
        Assert.Contains("| Field | Rule |\n|---|---|\n| a2 | b2 |", doc.Markdown);
    }

    [Fact]
    public void Horizontal_rules_with_text_between_are_listed_as_a_possible_table()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        foreach (var y in new double[] { 655, 635, 615, 595 }) page.DrawLine(new PdfPoint(72, y), new PdfPoint(500, y), 0.5);
        pdf.Text(page, "Total", 76, 640);
        pdf.Text(page, "max 100", 300, 640);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains("page 1: drawn lines with text between them may be a table; not brought in as one", doc.Problems);
    }

    // ---- Second reading, each check failing on a crafted PDF with no hook (finding 4)

    [Fact]
    public void Lines_drawn_bottom_up_are_read_by_position_and_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page();
        pdf.Text(page, "1 Orders", 72, 720);
        pdf.Text(page, "the approval has been recorded before it ships.", 72, 672);
        pdf.Text(page, "the administrator unless a manager has approved and", 72, 686);
        pdf.Text(page, "The system shall reject any order above the limit set by", 72, 700);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1:") && p.Contains("order is uncertain"));
        var body = doc.Markdown;
        Assert.True(body.IndexOf("The system shall", StringComparison.Ordinal) < body.IndexOf("the administrator", StringComparison.Ordinal));
        Assert.True(body.IndexOf("the administrator", StringComparison.Ordinal) < body.IndexOf("the approval", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_columns_read_in_order_are_left_alone_and_drawn_backwards_are_listed()
    {
        static byte[] Columns(bool rightFirst)
        {
            using var pdf = new PdfFixture();
            var page = pdf.Page("1 Orders");
            void Column(string first, string second, double x)
            {
                pdf.Text(page, first, x, 660);
                pdf.Text(page, second, x, 646);
                pdf.Text(page, "Closing words here.", x, 632);
            }
            if (rightFirst) { Column("Right one", "right two", 320); Column("Left one", "left two", 72); }
            else { Column("Left one", "left two", 72); Column("Right one", "right two", 320); }
            return pdf.Build(PdfFixture.Mark("1 Orders", 0, 1));
        }
        var inOrder = PdfConverter.Convert(Columns(false));
        Assert.DoesNotContain(inOrder.Problems, p => p.Contains("order is uncertain"));
        var backwards = PdfConverter.Convert(Columns(true));
        Assert.Contains(backwards.Problems, p => p.Contains("order is uncertain"));
        Assert.True(backwards.Markdown.IndexOf("Left one", StringComparison.Ordinal) < backwards.Markdown.IndexOf("Right one", StringComparison.Ordinal));
        Assert.True(backwards.Markdown.IndexOf("left two", StringComparison.Ordinal) < backwards.Markdown.IndexOf("Right one", StringComparison.Ordinal));
    }

    [Fact]
    public void Cells_drawn_right_to_left_dont_fuse_and_the_table_stands()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 200, 330], [640, 620, 600]);
        pdf.Text(page, "b1", 204, 626); pdf.Text(page, "a1", 76, 626);
        pdf.Text(page, "b2", 204, 606); pdf.Text(page, "a2", 76, 606);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains("| a1 | b1 |\n|---|---|\n| a2 | b2 |", doc.Markdown);
    }

    [Fact]
    public void A_word_drawn_in_pieces_makes_the_two_readings_differ_and_is_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders");
        // "split" drawn as "sp", then something else, then "lit" right after "sp".
        pdf.Text(page, "sp", 72, 650);
        var end = 72 + page.MeasureText("sp", 12, new PdfPoint(72, 650), pdf.Font).Max(l => l.EndBaseLine.X - 72);
        pdf.Text(page, "other", 300, 650);
        pdf.Text(page, "lit", end, 650);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: reading its words by position gives different words"));
    }

    [Fact]
    public void A_cell_drawn_bottom_up_fails_the_cell_order_check()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 200, 330], [700, 640, 620]);
        pdf.Text(page, "lower", 76, 650); pdf.Text(page, "upper", 76, 680);
        pdf.Text(page, "b1", 204, 680);
        pdf.Text(page, "a2", 76, 626); pdf.Text(page, "b2", 204, 626);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: a table wasn't brought in exactly") && p.Contains("out of order"));
    }

    [Fact]
    public void If_the_plain_text_fallback_fails_too_it_says_so_and_repeats_nothing()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "Only these words.");
        var bytes = pdf.Build(PdfFixture.Mark("1 Orders", 0, 1));
        var doc = PdfConverter.Convert(bytes, false, (_, blocks) => [.. blocks.Append(new PdfBlock("", "stray"))]);
        Assert.Contains("page 1: the words laid out didn't match the page's own words; kept as plain text", doc.Problems);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: its plain text didn't match either"));
        Assert.DoesNotContain("stray", doc.Markdown);
        Assert.Single(doc.Markdown.Split("Only these words.").Skip(1));
    }

    // ---- Sentences (finding 5)

    [Fact]
    public void One_wide_line_doesnt_split_wrapped_lines_that_start_with_a_capital()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders", "The system shall send every message about the order to the Customer", "Service Team and the Warehouse Team, and to the Regional Manager when", "Orders are above the limit.");
        pdf.Text(page, "See https://example.invalid/a/very/long/url/that/runs/past/the/normal/text/width/of/the/page/for/details", 72, 500);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("to the Customer Service Team and the Warehouse Team, and to the Regional Manager when Orders are above the limit.", doc.Markdown);
    }

    [Fact]
    public void A_line_that_ends_a_sentence_with_room_left_ends_its_paragraph()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "A line that reaches the widest extent of all the lines on this page here.", "A short one.", "Another line follows it.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("A short one.\n\nAnother line follows it.", doc.Markdown);
    }

    [Fact]
    public void A_number_at_the_start_of_a_wrapped_line_isnt_a_list_item()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "The system shall reject any order whose total is above the value of", "100. Orders below that are accepted without any other check by the", "system on the next working day.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("the value of 100. Orders below", doc.Sections[0].Text);
    }

    [Fact]
    public void A_label_set_apart_from_its_text_is_a_list_item_even_alone()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders", "Words first.");
        pdf.Text(page, "7.", 72, 600);
        pdf.Text(page, "A lone numbered item.", 110, 600);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("\n7. A lone numbered item.", doc.Markdown);
    }

    [Fact]
    public void Paragraphs_on_one_page_are_not_run_together_by_the_page_break_rule()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders", "A first paragraph that stops without an end mark and has no room left");
        pdf.Text(page, "a second paragraph after a bigger gap.", 72, 600);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("has no room left\n\na second paragraph", doc.Sections[0].Text);
    }

    [Fact]
    public void A_table_holding_a_section_title_on_its_own_line_is_kept_as_plain_text()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("Words first.");
        PdfFixture.Grid(page, [72, 200, 330], [640, 620, 600, 580]);
        pdf.Text(page, "Summary", 76, 626);
        pdf.Text(page, "a2", 76, 606); pdf.Text(page, "b2", 204, 606);
        pdf.Text(page, "a3", 76, 586); pdf.Text(page, "b3", 204, 586);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("Summary", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains("# Summary", doc.Markdown);
        Assert.Contains("page 1: a table holds the section title \"Summary\"; kept as plain text", doc.Problems);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("didn't match"));
    }

    [Fact]
    public void A_capitalised_sentence_over_a_page_break_stays_split()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "The system shall send the confirmation message to the user through the");
        pdf.Page("Email service once the order has been accepted.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("through the\n\nEmail service", doc.Sections[0].Text);
    }

    [Fact]
    public void A_footnote_or_a_page_number_never_joins_the_next_page()
    {
        using var footnote = new PdfFixture();
        var page = footnote.Page("1 Orders", "Body words end here.");
        footnote.Text(page, "See the handbook at https://example.invalid/notice", 72, 60, 8);
        footnote.Page("the next page starts in lower case by chance.", "And it goes on.", "And on.");
        var doc = PdfConverter.Convert(footnote.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.DoesNotContain("notice the next page", doc.Markdown);

        using var number = new PdfFixture();
        var numbered = number.Page("1 Orders", "Body words with no end mark on this line");
        number.Text(numbered, "7", 300, 60);
        number.Page("rest of the words.");
        Assert.DoesNotContain("7 rest", PdfConverter.Convert(number.Build(PdfFixture.Mark("1 Orders", 0, 1))).Markdown);
    }

    [Fact]
    public void Lower_case_text_after_unfinished_body_text_joins_over_the_page()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "Body words with no end mark on this line and no more");
        pdf.Page("words on this page than a few.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("and no more words on this page", doc.Sections[0].Text);
    }

    [Fact]
    public void A_one_row_box_is_prose()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 200, 330, 460], [640, 580]);
        pdf.Text(page, "The system", 76, 626); pdf.Text(page, "shall reject", 76, 612); pdf.Text(page, "the order.", 76, 598);
        pdf.Text(page, "Refunds are", 204, 626); pdf.Text(page, "paid soon.", 204, 612);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains("The system shall reject the order.", doc.Markdown);
        Assert.Contains("Refunds are paid soon.", doc.Markdown);
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains("The system shall reject the order.\n\nRefunds are paid soon.", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("1 rows and 3 columns starting \"The system") && p.Contains("columns aren't kept"));
    }

    // ---- A damaged outline (finding 6)

    static byte[] PlainPdf(string[] extraObjects, string catalogExtra, string trailerExtra = "", string content = "BT /F1 12 Tf 72 700 Td (Hello words on the page.) Tj ET", string pageResources = "")
    {
        string[] objects = [
            $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> " + pageResources + " >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>", .. extraObjects];
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R {trailerExtra} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Theory]
    [InlineData("<< /Title (Loop) /Parent 6 0 R /First 7 0 R /Last 7 0 R /Count 1 /Dest [3 0 R /Fit] >>")]
    [InlineData("<< /Title (Loop) /Parent 6 0 R /Next 7 0 R /Dest [3 0 R /Fit] >>")]
    public void An_outline_that_loops_is_invalid_not_a_crash(string node)
    {
        var bytes = PlainPdf(["<< /Type /Outlines /First 7 0 R /Last 7 0 R /Count 1 >>", node], "/Outlines 6 0 R");
        Assert.Equal("invalid_pdf", Code(() => PdfConverter.Convert(bytes)));
    }

    // ---- Before the first section; symbols (findings 7, 8)

    [Fact]
    public void Words_before_the_first_section_are_counted()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Rule zero: all prices include tax.");
        pdf.Page("1 Orders", "Words.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 2)));
        Assert.Contains("6 words come before the first section, so they're in the document but in no section", doc.Problems);
        Assert.Contains("Rule zero", doc.Markdown);
    }

    [Theory]
    [InlineData('•', true)]
    [InlineData('', true)]
    [InlineData('✓', false)]
    [InlineData('□', false)]
    [InlineData('○', false)]
    [InlineData('▫', false)]
    [InlineData('', false)]
    public void Only_real_bullets_become_list_markers(char glyph, bool bullet) => Assert.Equal(bullet, PdfConverter.IsBullet(glyph));

    [Fact]
    public void Unmapped_glyphs_are_counted_but_real_bullets_arent()
    {
        Assert.Equal(2, PdfConverter.CountUnmapped("a �•"));
        Assert.Equal(0, PdfConverter.CountUnmapped("plain ✓ text"));
    }

    // ---- Re-import, protection (findings 9, 10)

    [Fact]
    public void A_reimport_lists_the_sections_that_left_and_the_trace_entries_on_them()
    {
        using var archive = new TempRepo();
        using var repo = new TempRepo(Config(archive.Root));
        var file = Path.Combine(repo.Root, "shop.pdf");
        File.WriteAllBytes(file, Outlined(("1 Orders", 0), ("2 Refunds", 0)));
        FsdImport.Import(repo.Repo, file);
        var entry = LinkRun.Entry("gap", "shop", new JsonObject { ["id"] = "1", ["title"] = "Orders" }, "Text of page 1.", null, null, null, "code", null, null);
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add(entry);
        TraceFile.Save(repo.Root, trace);

        Assert.Equal("candidate", (string)FsdImport.Import(repo.Repo, file, flat: true)["status"]!);
        foreach (var open in FsdVersions.Diff(repo.Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful" && (string?)e["side"] == "candidate"))
            FsdVersions.Resolve(repo.Root, "shop", (string)open["to"]!, null, isNew: true, removed: false, "Pat");
        foreach (var open in FsdVersions.Diff(repo.Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful"))
            FsdVersions.Resolve(repo.Root, "shop", (string)open["permanentId"]!, null, isNew: false, removed: true, "Pat");
        var removed = FsdVersions.Promote(repo.Repo, "shop", "Pat")["carried"]!["removed"]!.AsArray();
        Assert.Equal(["1", "2"], removed.Select(s => (string)s!["section"]!));
        Assert.Single(removed[0]!["traceEntries"]!.AsArray());
        Assert.Null(JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(repo.Root, "shop"), "document.json")))!["replacedSections"]);
    }

    static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = Enumerable.Range(0, 256).ToArray();
        for (int i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) % 256; (s[i], s[j]) = (s[j], s[i]); }
        var o = new byte[data.Length];
        for (int k = 0, i = 0, j = 0; k < data.Length; k++) { i = (i + 1) % 256; j = (j + s[i]) % 256; (s[i], s[j]) = (s[j], s[i]); o[k] = (byte)(data[k] ^ s[(s[i] + s[j]) % 256]); }
        return o;
    }

    [Fact]
    public void A_pdf_with_only_an_owner_password_opens_with_no_password_but_is_still_refused()
    {
#pragma warning disable CA5351 // MD5 and RC4 are what this old PDF encryption is made of.
        var pad = Convert.FromHexString("28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");
        var owner = Rc4(MD5.HashData(Encoding.ASCII.GetBytes("ownerpass").Concat(pad).Take(32).ToArray())[..5], pad);
        const int permissions = -64;
        var id = Encoding.ASCII.GetBytes("0123456789abcdef");
        var key = MD5.HashData([.. pad, .. owner, .. BitConverter.GetBytes(permissions), .. id])[..5];
        var user = Rc4(key, pad);
#pragma warning restore CA5351
        var bytes = PlainPdf([$"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P {permissions} >>"], "",
            $"/Encrypt 6 0 R /ID [<{Convert.ToHexString(id)}> <{Convert.ToHexString(id)}>]");
        var e = Assert.Throws<StangentException>(() => PdfConverter.Convert(bytes));
        Assert.Equal("pdf_locked", e.Code);
        Assert.Contains("protected", e.Message);
        Assert.Contains("unprotected copy", e.Message);
    }

    // ---- Round 3: a false sentence is worse than a broken one, and a listed problem is better than either

    [Fact]
    public void A_number_after_an_unfinished_sentence_isnt_a_label_even_with_a_justified_gap()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders", "The limit on any order placed by a new customer is set out in rule");
        pdf.Text(page, "3.", 72, 672);
        pdf.Text(page, "Orders above that limit are held until a manager approves them.", 110, 672);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("set out in rule 3. Orders above", doc.Sections[0].Text);
    }

    const string Diagonal = "BT /F1 40 Tf 0.7 0.7 -0.7 0.7 150 200 Tm (CONFIDENTIAL) Tj ET\n";

    static string TableStream()
    {
        var content = new StringBuilder();
        foreach (var x in new[] { 72, 200, 330 }) content.Append($"{x} 640 m {x} 600 l S\n");
        foreach (var y in new[] { 640, 620, 600 }) content.Append($"72 {y} m 330 {y} l S\n");
        foreach (var (text, x, y) in new[] { ("Field", 76, 626), ("Rule", 204, 626), ("a2", 76, 606), ("b2", 204, 606) })
            content.Append($"BT /F1 12 Tf {x} {y} Td ({text}) Tj ET\n");
        return "BT /F1 12 Tf 72 700 Td (Words before it.) Tj ET\n" + content;
    }

    [Theory]
    [InlineData(Diagonal, "CONFIDENTIAL")]
    [InlineData("BT /F1 10 Tf 0 1 -1 0 30 300 Tm (DRAFT) Tj ET\n", "DRAFT")]
    public void A_watermark_is_listed_and_disturbs_nothing(string mark, string text)
    {
        foreach (var content in new[] { mark + TableStream(), TableStream() + mark })
        {
            var doc = PdfConverter.Convert(PlainPdf([], "", content: content), flat: true);
            Assert.Contains("| Field | Rule |\n|---|---|\n| a2 | b2 |", doc.Markdown);
            Assert.DoesNotContain(text, doc.Markdown);
            Assert.DoesNotContain(doc.Problems, p => p.Contains("may be wrong") || p.Contains("uncertain"));
            Assert.Contains(doc.Problems, p => p.Contains("another direction on 1 pages") && p.Contains($"\"{text}\""));
        }
    }

    [Fact]
    public void A_page_turned_as_a_whole_reads_normally_with_its_table()
    {
        // Text runs up the page, with the table's rules drawn to match: how a landscape page sits in a portrait file.
        var content = new StringBuilder();
        foreach (var y in new[] { 100, 300, 500 }) content.Append($"300 {y} m 340 {y} l S\n");
        foreach (var x in new[] { 300, 320, 340 }) content.Append($"{x} 100 m {x} 500 l S\n");
        foreach (var (text, x, y) in new[] { ("Field", 314, 106), ("Rule", 314, 306), ("a2", 334, 106), ("b2", 334, 306) })
            content.Append($"BT /F1 12 Tf 0 1 -1 0 {x} {y} Tm ({text}) Tj ET\n");
        content.Append("BT /F1 12 Tf 0 1 -1 0 280 106 Tm (Words before the table.) Tj ET\n");
        var doc = PdfConverter.Convert(PlainPdf([], "", content: content.ToString()), flat: true);
        Assert.Contains("Words before the table.", doc.Markdown);
        Assert.Contains("| Field | Rule |\n|---|---|\n| a2 | b2 |", doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("another direction") || p.Contains("may be wrong"));
    }

    [Fact]
    public void Inner_rules_only_are_listed_as_a_possible_table()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        foreach (var x in new double[] { 200, 330 }) page.DrawLine(new PdfPoint(x, 640), new PdfPoint(x, 580), 0.5);
        foreach (var y in new double[] { 620, 600 }) page.DrawLine(new PdfPoint(72, y), new PdfPoint(460, y), 0.5);
        string[][] rows = [["Name", "Role", "Limit"], ["Alice", "Admin", "500"], ["Bob", "Guest", "100"]];
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++) pdf.Text(page, rows[r][c], new double[] { 78, 206, 336 }[c], 624 - r * 20);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1:") && p.Contains("may be a table"));
    }

    [Theory]
    [InlineData(1, 2)]                                                                                 // one rule across, two dividers
    [InlineData(3, 1)]                                                                                 // rules across, one divider
    [InlineData(0, 3)]                                                                                 // dividers only
    public void Rules_and_dividers_that_arent_a_grid_are_listed(int across, int dividers)
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        for (var i = 0; i < across; i++) page.DrawLine(new PdfPoint(72, 620 - i * 20), new PdfPoint(460, 620 - i * 20), 0.5);
        for (var i = 0; i < dividers; i++) page.DrawLine(new PdfPoint(200 + i * 100, 640), new PdfPoint(200 + i * 100, 560), 0.5);
        pdf.Text(page, "Name", 78, 624); pdf.Text(page, "Role", 350, 624);
        pdf.Text(page, "Alice", 78, 604); pdf.Text(page, "Admin", 350, 604);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1:") && p.Contains("may be a table"));
    }

    [Fact]
    public void A_heading_underline_or_two_far_apart_isnt_a_possible_table()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders");
        pdf.Text(page, "Underlined heading", 72, 660);
        page.DrawLine(new PdfPoint(72, 656), new PdfPoint(500, 656), 0.5);
        for (var i = 0; i < 12; i++) pdf.Text(page, "A body line that goes on and on.", 72, 640 - i * 14);
        pdf.Text(page, "Another underlined heading", 72, 440);
        page.DrawLine(new PdfPoint(72, 436), new PdfPoint(500, 436), 0.5);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.DoesNotContain(doc.Problems, p => p.Contains("may be a table"));
    }

    [Fact]
    public void A_table_drawn_last_or_first_takes_its_place_by_position_and_is_listed()
    {
        static byte[] Pdf(bool tableLast)
        {
            using var pdf = new PdfFixture();
            var page = pdf.Page();
            void Table() => FillTable(pdf, page, [["Field", "Rule", "Note"], ["a2", "b2", "c2"], ["a3", "b3", "c3"]]);
            if (!tableLast) Table();
            pdf.Text(page, "1 Orders", 72, 720);
            pdf.Text(page, "Words of orders.", 72, 700);
            pdf.Text(page, "2 Refunds", 72, 500);
            pdf.Text(page, "Words of refunds.", 72, 480);
            if (tableLast) Table();
            return pdf.Build(PdfFixture.Mark("1 Orders", 0, 1), PdfFixture.Mark("2 Refunds", 0, 1));
        }
        foreach (var last in new[] { true, false })
        {
            var doc = PdfConverter.Convert(Pdf(last));
            Assert.Contains("| Field | Rule | Note |", doc.Sections[0].Text);
            Assert.DoesNotContain("| Field", doc.Sections[1].Text);
            Assert.Contains(doc.Problems, p => p.StartsWith("page 1:") && p.Contains("order is uncertain"));
        }
    }

    [Fact]
    public void A_lone_row_drawn_line_by_line_never_makes_one_sentence_of_two_cells()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 300, 500], [640, 560]);
        pdf.Text(page, "Left column text goes on", 78, 620); pdf.Text(page, "Right column text goes on", 306, 620);
        pdf.Text(page, "over two lines here.", 78, 606); pdf.Text(page, "over two lines there.", 306, 606);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("goes on Right", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("columns aren't kept"));
    }

    [Fact]
    public void Repeated_table_rows_arent_running_lines()
    {
        using var pdf = new PdfFixture();
        for (var n = 1; n <= 6; n++)
        {
            var page = pdf.Page();
            PdfFixture.Grid(page, [72, 250, 450], [780, 750, 720, 690]);
            pdf.Text(page, "Field", 80, 760); pdf.Text(page, "Meaning", 260, 760);
            pdf.Text(page, $"f{n}", 80, 730); pdf.Text(page, $"means {new string('x', n)}", 260, 730);
            pdf.Text(page, "Approved by", 80, 700); pdf.Text(page, "Finance Manager", 260, 700);
            pdf.Text(page, $"Words below the table {new string('y', n)}.", 72, 400);
        }
        var doc = PdfConverter.Convert(pdf.Build(), flat: true);
        Assert.Equal(6, doc.Markdown.Split("| Field | Meaning |").Length - 1);
        Assert.Equal(6, doc.Markdown.Split("| Approved by | Finance Manager |").Length - 1);
        Assert.DoesNotContain(doc.Problems, p => p.StartsWith("removed"));
    }

    [Fact]
    public void A_repeated_line_that_doesnt_stand_apart_is_kept_and_listed()
    {
        using var pdf = new PdfFixture();
        var marks = new List<BookmarkNode>();
        for (var n = 1; n <= 6; n++)
        {
            pdf.Page($"Acme Orders Specification", $"{n} Sec", $"Body words {new string('y', n)} of this part.");
            marks.Add(PdfFixture.Mark($"{n} Sec", 0, n));
        }
        var doc = PdfConverter.Convert(pdf.Build([.. marks]));
        Assert.Contains("Acme Orders Specification", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.Contains("a repeated line was kept") && p.Contains("on 6 pages") && p.Contains("Acme Orders Specification"));
    }

    [Fact]
    public void Only_text_never_painted_is_removed_and_white_is_kept_and_listed()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders", "Visible words.");
        page.SetTextRenderingMode(TextRenderingMode.Neither);
        pdf.Text(page, "Secret rule one two", 72, 650);
        page.SetTextRenderingMode(TextRenderingMode.Fill);
        pdf.Text(page, "Off the page", 900, 610);
        page.SetTextAndFillColor(255, 255, 255);
        pdf.Text(page, "White rule three", 72, 630);
        page.ResetColor();
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        foreach (var hidden in new[] { "Secret", "Off the page" }) Assert.DoesNotContain(hidden, doc.Markdown);
        Assert.Contains("White rule three", doc.Markdown);
        Assert.Contains("page 1: 7 words are hidden (invisible or off the page); removed from the text", doc.Problems);
        Assert.Contains("3 words are text in white, may not be visible; kept, on pages 1", doc.Problems);
    }

    [Theory]
    [InlineData("2 Tr 1 1 1 rg 0 0 0 RG BT /F1 12 Tf 72 600 Td (Outlined rule here.) Tj ET", "Outlined rule here.")]        // a black outline
    [InlineData("7 Tr BT /F1 12 Tf 72 600 Td (Clipped rule here.) Tj ET", "Clipped rule here.")]                               // a clip: painted through
    public void Text_painted_in_other_ways_stays_and_isnt_called_white(string content, string text)
    {
        var doc = PdfConverter.Convert(PlainPdf([], "", content: content), flat: true);
        Assert.Contains(text, doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("in white"));
    }

    [Fact]
    public void A_pattern_coloured_word_is_kept_and_doesnt_stop_the_import()
    {
        const string pattern = "<< /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [72 0 400 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0.4] /C1 [0 0 0.1] /N 1 >> >> >>";
        var bytes = PlainPdf([], "", content: "/Pattern cs /P1 scn BT /F1 12 Tf 72 600 Td (Gradient text says refunds are paid.) Tj ET 0 g", pageResources: $"/Pattern << /P1 {pattern} >>");
        var doc = PdfConverter.Convert(bytes, flat: true);
        Assert.Contains("Gradient text says refunds are paid.", doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("white"));
    }

    [Fact]
    public void Invisible_text_over_a_full_page_image_is_an_ocr_layer_kept_and_listed()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        using var pdf = new PdfFixture();
        var page = pdf.Page();
        page.AddPng(png, new PdfRectangle(0, 0, page.PageSize.Width, page.PageSize.Height));
        page.SetTextRenderingMode(TextRenderingMode.Neither);
        pdf.Text(page, "1 Orders", 72, 700);
        pdf.Text(page, "Scanned words read by OCR.", 72, 680);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("Scanned words read by OCR.", doc.Sections[0].Text);
        Assert.Contains("page 1: 7 words are text from an OCR layer over a full-page image, not checked", doc.Problems);
    }

    [Fact]
    public void A_line_starting_with_a_symbol_starts_a_new_line_and_keeps_it()
    {
        // The builder's standard fonts can't write these, so the page is written by hand: \\251 and \\256 are the symbols' codes in the encoding.
        const string content = "BT /F1 12 Tf 14 TL 72 700 Td (\\251 Admin may delete orders) Tj T* (\\256 Guest may read orders) Tj ET";
        var doc = PdfConverter.Convert(PlainPdf([], "", content: content), flat: true);
        Assert.Contains("© Admin may delete orders\n\n® Guest may read orders", doc.Sections[0].Text);
    }

    [Fact]
    public void Where_the_two_readings_differ_the_page_comes_out_plain()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders");
        FillTable(pdf, page, [["Field", "Rule", "Note"], ["a2", "b2", "c2"], ["a3", "b3", "c3"]]);
        pdf.Text(page, "sp", 72, 500);
        var end = 72 + page.MeasureText("sp", 12, new PdfPoint(72, 500), pdf.Font).Max(l => l.EndBaseLine.X - 72);
        pdf.Text(page, "other", 300, 500);
        pdf.Text(page, "lit", end, 500);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.DoesNotContain("|", doc.Markdown);
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1: reading its words by position gives different words"));
    }

    [Fact]
    public void An_outline_nested_past_the_limit_is_invalid()
    {
        const int depth = 100;
        var nodes = Enumerable.Range(0, depth).Select(i =>
            $"<< /Title (Level {i}) /Parent {(i == 0 ? 6 : 6 + i)} 0 R /Dest [3 0 R /Fit]" + (i + 1 < depth ? $" /First {8 + i} 0 R /Last {8 + i} 0 R /Count 1" : "") + " >>").ToList();
        var bytes = PlainPdf(["<< /Type /Outlines /First 7 0 R /Last 7 0 R /Count 1 >>", .. nodes], "/Outlines 6 0 R");
        Assert.Equal("invalid_pdf", Code(() => PdfConverter.Convert(bytes)));
    }

    [Fact]
    public void The_trace_count_only_counts_this_document()
    {
        using var archive = new TempRepo();
        using var repo = new TempRepo(Config(archive.Root));
        var file = Path.Combine(repo.Root, "shop.pdf");
        File.WriteAllBytes(file, Outlined(("1 Orders", 0), ("2 Refunds", 0)));
        FsdImport.Import(repo.Repo, file);
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add(LinkRun.Entry("gap", "another-doc", new JsonObject { ["id"] = "1", ["title"] = "Orders" }, "Text.", null, null, null, "code", null, null));
        TraceFile.Save(repo.Root, trace);
        FsdImport.Import(repo.Repo, file, flat: true);
        foreach (var open in FsdVersions.Diff(repo.Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful" && (string?)e["side"] == "candidate"))
            FsdVersions.Resolve(repo.Root, "shop", (string)open["to"]!, null, isNew: true, removed: false, "Pat");
        foreach (var open in FsdVersions.Diff(repo.Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful"))
            FsdVersions.Resolve(repo.Root, "shop", (string)open["permanentId"]!, null, isNew: false, removed: true, "Pat");
        var removed = FsdVersions.Promote(repo.Repo, "shop", "Pat")["carried"]!["removed"]!.AsArray();
        Assert.All(removed, s => Assert.Empty(s!["traceEntries"]!.AsArray()));
    }

    [Fact]
    public void Our_copy_of_the_extractors_rule_gives_its_output()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("A first line of words.", "A second line of words.");
        pdf.Text(page, "b2", 300, 500); pdf.Text(page, "a2", 72, 500);
        pdf.Text(page, "Far right", 400, 400); pdf.Text(page, "same line", 72, 400);
        using var read = PdfDocument.Open(pdf.Build());
        var own = ContentOrderTextExtractor.GetText(read.GetPage(1), new ContentOrderTextExtractor.Options { NegativeGapAsWhitespace = true });
        Assert.Equal(own.Replace("\r\n", "\n"), PdfConverter.ReadTextForTest(read.GetPage(1)));
    }

    // ---- Corrections from the final check

    [Fact]
    public void A_grid_with_text_in_one_column_never_merges_its_cells()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 300, 500], [640, 620, 600, 580]);
        pdf.Text(page, "The user selects the option", 78, 626);
        pdf.Text(page, "The system shows the page", 78, 606);
        pdf.Text(page, "on the page to go on.", 78, 586);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("option The system", doc.Markdown);
        Assert.Contains("The system shows the page\n\non the page", doc.Markdown);
    }

    [Theory]
    [InlineData("5.", "6.", true)]
    [InlineData("a)", "b)", true)]
    [InlineData("(1)", "(2)", true)]
    [InlineData("-", "-", true)]
    [InlineData("5.", "7.", false)]
    [InlineData("5.", "6)", false)]
    [InlineData("a)", "c)", false)]
    [InlineData("99.", "b.", false)]
    public void A_marker_follows_only_the_one_before_it(string marker, string next, bool follows) => Assert.Equal(follows, PdfConverter.Reader.Follows(marker, next));

    [Fact]
    public void A_marker_continuing_a_sequence_is_a_label_whatever_the_line_before_ends_with()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Steps", "5. Assess the network called a recurrent network (RNN)", "6. Assess the second network with the same data");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Steps", 0, 1)));
        Assert.Contains("(RNN)\n6. Assess", doc.Markdown);
    }

    [Fact]
    public void Wrapped_lettered_items_after_an_unfinished_intro_stay_items()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Steps", "The manager shall approve an order when the order is");
        foreach (var (y, text) in new[] { (672.0, "a) above the limit set by the"), (658.0, "administrator of the shop"), (644.0, "b) sent to a new address that"), (630.0, "the customer has not used") })
            pdf.Text(page, text, text.Contains(')') ? 72 : 90, y);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Steps", 0, 1)));
        Assert.Contains("order is\n\n- a) above the limit set by the administrator of the shop\n- b) sent to a new address", doc.Markdown);
    }

    [Fact]
    public void Joins_across_pages_are_counted_with_their_pages()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Orders", "Body words with no end mark on this line and no more");
        pdf.Page("words on this page than a few.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.Contains("1 sentences were joined across a page break, after pages 1", doc.Problems);
    }

    [Fact]
    public void A_cell_paragraph_never_joins_over_a_page()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 300, 500], [640, 600]);
        pdf.Text(page, "other cell", 306, 620); pdf.Text(page, "signature of the", 74, 606);    // the last cell sits at the body's left edge
        pdf.Page("the customer may ask.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.DoesNotContain("signature of the the customer", doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("joined across"));
    }

    [Fact]
    public void A_marker_continuing_from_the_page_before_is_a_label()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Steps", "5. Assess the network with the training data");
        pdf.Page("A closing part of the item called a recurrent network (RNN)", "6. Assess the second network with the same data");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Steps", 0, 1)));
        Assert.Contains("(RNN)\n\n6. Assess", doc.Markdown);
    }

    [Fact]
    public void White_text_gets_one_line_for_the_whole_document()
    {
        using var pdf = new PdfFixture();
        foreach (var (title, white) in new[] { ("1 Orders", "White one two"), ("2 Refunds", "Black words."), ("3 Stock", "White three") })
        {
            var page = pdf.Page(title, "Body words.");
            if (white.StartsWith("White")) page.SetTextAndFillColor(255, 255, 255);
            pdf.Text(page, white, 72, 600);
            page.ResetColor();
        }
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1), PdfFixture.Mark("2 Refunds", 0, 2), PdfFixture.Mark("3 Stock", 0, 3)));
        Assert.Equal("5 words are text in white, may not be visible; kept, on pages 1, 3", Assert.Single(doc.Problems, p => p.Contains("in white")));
    }

    [Fact]
    public void A_cell_never_runs_into_the_prose_below_it()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders");
        PdfFixture.Grid(page, [72, 300, 500], [680, 642]);
        pdf.Text(page, "Second cell here", 306, 664);
        pdf.Text(page, "Orders ship in one week", 74, 650);
        pdf.Text(page, "and the shop keeps a copy of the order.", 74, 636);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.DoesNotContain("one week and the shop", doc.Markdown);
    }

    [Fact]
    public void A_header_rule_as_wide_as_its_text_still_counts_in_a_stack_of_rules()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        foreach (var y in new double[] { 655, 635, 615 }) page.DrawLine(new PdfPoint(72, y), new PdfPoint(338, y), 0.5);
        const string amount = "Amount";
        var right = page.MeasureText(amount, 12, new PdfPoint(0, 0), pdf.Font).Max(l => l.EndBaseLine.X);
        pdf.Text(page, "Name", 72, 640); pdf.Text(page, amount, 338 - right, 640);                    // the last column is right aligned, so the text is as wide as the rule
        pdf.Text(page, "Alice", 72, 620); pdf.Text(page, "500", 320, 620);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains(doc.Problems, p => p.StartsWith("page 1:") && p.Contains("may be a table"));
    }

    [Fact]
    public void A_list_items_flush_left_wrap_joins_it_unless_it_has_its_own_marker()
    {
        using var pdf = new PdfFixture();
        pdf.Page("1 Steps", "8. The user enters the", "pin code on the screen.", "9. The system checks it", "against the record.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Steps", 0, 1)));
        Assert.Contains("8. The user enters the pin code on the screen.\n9. The system checks it against the record.", doc.Markdown);
    }

    [Fact]
    public void A_fallen_back_tables_text_never_joins_the_next_page()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Fields");
        PdfFixture.Grid(page, [72, 200, 330], [660, 620, 600]);
        pdf.Text(page, "a-one", 74, 646); pdf.Text(page, "b-one", 204, 646);
        pdf.Text(page, "a-two", 74, 630); pdf.Text(page, "b-two", 204, 630);
        pdf.Text(page, "c-one", 74, 606); pdf.Text(page, "d-one", 204, 606);
        pdf.Page("the customer may ask.");
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Fields", 0, 1)));
        Assert.Contains(doc.Problems, p => p.Contains("wasn't brought in exactly"));
        Assert.DoesNotContain("d-one the customer", doc.Markdown);
        Assert.DoesNotContain(doc.Problems, p => p.Contains("joined across"));
    }

    [Fact]
    public void Rules_of_different_widths_arent_a_stack()
    {
        using var pdf = new PdfFixture();
        var page = pdf.Page("1 Orders");
        page.DrawLine(new PdfPoint(72, 650), new PdfPoint(200, 650), 0.5);
        pdf.Text(page, "Words between the rules.", 72, 636);
        page.DrawLine(new PdfPoint(72, 630), new PdfPoint(500, 630), 0.5);
        var doc = PdfConverter.Convert(pdf.Build(PdfFixture.Mark("1 Orders", 0, 1)));
        Assert.DoesNotContain(doc.Problems, p => p.Contains("may be a table"));
    }
}
