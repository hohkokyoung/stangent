using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Tickets.Proof;

namespace Stangent.Core.Tests;

/// <summary>The Word importer lists what it can't bring in exactly (D116). Each file is built by hand with the Open XML SDK.</summary>
public class FsdDocxTests
{
    const string Ns = """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape" """;

    internal static string P(string text) => $"""<w:p><w:r><w:t xml:space="preserve">{text}</w:t></w:r></w:p>""";
    internal static string H(string text, int level = 1) => $"""<w:p><w:pPr><w:pStyle w:val="Heading{level}"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>""";
    internal static string R(string text, string props = "") => $"""<w:r><w:rPr>{props}</w:rPr><w:t xml:space="preserve">{text}</w:t></w:r>""";
    static string Tc(string inner, string props = "") => $"""<w:tc><w:tcPr>{props}</w:tcPr>{(inner.StartsWith('<') ? inner : P(inner))}</w:tc>""";
    static string Tr(params string[] cells) => $"<w:tr>{string.Concat(cells)}</w:tr>";
    static string Tbl(params string[] rows) => $"<w:tbl>{string.Concat(rows)}</w:tbl>";
    internal const string Track = """w:id="1" w:author="Ann" w:date="2026-01-01T00:00:00Z" """;

    internal static byte[] Docx(string body, Action<MainDocumentPart>? parts = null, string styles = "")
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document($"<w:document {Ns}><w:body>{body}</w:body></w:document>");
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles($"""
                <w:styles {Ns}>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/></w:style>
                  {styles}
                </w:styles>
                """);
            parts?.Invoke(main);
            doc.Save();
        }
        return stream.ToArray();
    }

    static string Notes(string kind, string container, params string[] texts)
    {
        var sb = new StringBuilder($"<w:{container} {Ns}>");
        sb.Append($"""<w:{kind} w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:{kind}>""");
        for (var i = 0; i < texts.Length; i++) sb.Append($"""<w:{kind} w:id="{i + 1}">{P(texts[i])}</w:{kind}>""");
        return sb.Append($"</w:{container}>").ToString();
    }

    /// <summary>A section's text with no completeness problem, which every rule's file must also pass.</summary>
    static ConvertedDocument Clean(byte[] docx, bool flat = false, bool accept = false)
    {
        var doc = DocxConverter.Convert(docx, flat, accept);
        Assert.DoesNotContain(doc.Problems, p => p.StartsWith("completeness:", StringComparison.Ordinal));
        return doc;
    }

    static string Text(ConvertedDocument doc, string section) => doc.Sections.Single(s => s.Id == section).Text;

    internal const string WithChanges = $"""
        <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Rules</w:t></w:r></w:p>
        <w:p><w:r><w:t xml:space="preserve">Limit is </w:t></w:r><w:del {Track}><w:r><w:delText>three</w:delText></w:r></w:del><w:ins {Track}><w:r><w:t>five</w:t></w:r></w:ins><w:r><w:t xml:space="preserve"> orders.</w:t></w:r></w:p>
        <w:p><w:moveFrom {Track}><w:r><w:t>Moved sentence.</w:t></w:r></w:moveFrom></w:p>
        <w:p><w:r><w:t xml:space="preserve">Stays. </w:t></w:r><w:moveTo {Track}><w:r><w:t>Moved sentence.</w:t></w:r></w:moveTo></w:p>
        """;

    [Fact]
    public void Tracked_changes_stop_the_import_with_a_count_and_nothing_is_written()
    {
        var e = Assert.Throws<StangentException>(() => DocxConverter.Convert(Docx(WithChanges)));
        Assert.Equal("docx_tracked_changes", e.Code);
        Assert.Contains("4 tracked changes", e.Message);
        Assert.Contains("isn't settled", e.Message);
        Assert.Contains("--accept-changes", e.Message);

        using var temp = new TempRepo("fsd:\n  archive: { name: a, path: ../stangent-docx-archive, git: false }\n");
        var file = Path.Combine(temp.Root, "changed.docx");
        File.WriteAllBytes(file, Docx(WithChanges));
        Assert.Equal("docx_tracked_changes", Assert.Throws<StangentException>(() => FsdImport.Import(temp.Repo, file)).Code);
        Assert.False(Directory.Exists(FsdImport.DocDir(temp.Root, "changed")));
    }

    [Fact]
    public void Accepting_changes_keeps_inserts_drops_deletes_and_moved_text_never_doubles()
    {
        var doc = Clean(Docx(WithChanges), accept: true);
        Assert.Equal("Limit is five orders.\n\nStays. Moved sentence.", Text(doc, "Rules"));
        Assert.Contains(doc.Problems, p => p.StartsWith("--accept-changes: 4 tracked changes accepted (1 inserted, 1 deleted, 1 moved from, 1 moved to)", StringComparison.Ordinal));
    }

    [Fact]
    public void Tracked_changes_around_whole_paragraphs_rows_and_paragraph_marks_are_accepted()
    {
        var body = H("Rules")
            + $"""<w:p><w:pPr><w:rPr><w:ins {Track}/></w:rPr></w:pPr><w:ins {Track}><w:r><w:t>Whole new paragraph.</w:t></w:r></w:ins></w:p>"""
            + $"""<w:p><w:pPr><w:rPr><w:del {Track}/></w:rPr></w:pPr><w:del {Track}><w:r><w:delText>Whole old paragraph.</w:delText></w:r></w:del></w:p>"""
            + $"""<w:p><w:pPr><w:rPr><w:del {Track}/></w:rPr></w:pPr><w:r><w:t xml:space="preserve">Joined </w:t></w:r></w:p>{P("sentence.")}"""
            + Tbl(Tr(Tc("Kept row")), $"""<w:tr><w:trPr><w:del {Track}/></w:trPr>{Tc("Gone row")}</w:tr>""");
        var doc = Clean(Docx(body), accept: true);
        Assert.Equal("Whole new paragraph.\n\nJoined sentence.\n\n| Kept row |\n|---|", Text(doc, "Rules"));
    }

    [Fact]
    public void A_format_change_alone_does_not_stop_the_import()
    {
        var body = H("Rules") + $"""<w:p><w:r><w:rPr><w:b/><w:rPrChange {Track}><w:rPr/></w:rPrChange></w:rPr><w:t>Bold now.</w:t></w:r></w:p>""";
        var doc = Clean(Docx(body));
        Assert.Equal("Bold now.", Text(doc, "Rules"));
    }

    [Fact]
    public void Tracked_changes_in_a_footnote_stop_the_import_too()
    {
        var footnotes = $"""<w:footnotes {Ns}><w:footnote w:id="1"><w:p><w:ins {Track}><w:r><w:t>New.</w:t></w:r></w:ins></w:p></w:footnote></w:footnotes>""";
        var bytes = Docx(H("Rules") + P("Text."), m => m.AddNewPart<FootnotesPart>().Footnotes = new Footnotes(footnotes));
        Assert.Equal("docx_tracked_changes", Assert.Throws<StangentException>(() => DocxConverter.Convert(bytes)).Code);
    }

    [Fact]
    public void Accept_changes_is_refused_for_a_pdf()
    {
        using var temp = new TempRepo("fsd:\n  archive: { name: a, path: ../stangent-docx-archive, git: false }\n");
        var file = Path.Combine(temp.Root, "x.pdf");
        File.WriteAllBytes(file, [1]);
        Assert.Equal("usage", Assert.Throws<StangentException>(() => FsdImport.Import(temp.Repo, file, acceptChanges: true)).Code);
    }

    [Fact]
    public void Hidden_text_is_left_out_and_listed_with_its_section()
    {
        var body = H("Rules") + $"<w:p>{R("Shown. ")}{R("Secret rule here.", "<w:vanish/>")}</w:p>"
            + $"""<w:p><w:r><w:rPr><w:rStyle w:val="Ghost"/></w:rPr><w:t>Styled away.</w:t></w:r></w:p>""";
        var doc = Clean(Docx(body, styles: """<w:style w:type="character" w:styleId="Ghost"><w:name w:val="Ghost"/><w:rPr><w:vanish/></w:rPr></w:style>"""));
        Assert.Equal("Shown.", Text(doc, "Rules"));
        Assert.Contains("section Rules: hidden text left out (5 words)", doc.Problems);
    }

    [Fact]
    public void A_hidden_paragraph_mark_alone_does_not_hide_the_visible_text_in_it()
    {
        var body = H("Rules") + $"""<w:p><w:pPr><w:rPr><w:vanish/></w:rPr></w:pPr>{R("Still seen.")}</w:p>"""
            + $"""<w:p><w:pPr><w:rPr><w:vanish/></w:rPr></w:pPr>{R("All gone.", "<w:vanish/>")}</w:p>""";
        var doc = Clean(Docx(body));
        Assert.Equal("Still seen.", Text(doc, "Rules"));
        Assert.Contains(doc.Problems, p => p.StartsWith("section Rules: a hidden paragraph mark", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hidden_heading_makes_no_section_and_a_hidden_row_no_row()
    {
        var body = H("Rules") + $"""<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr>{R("Ghost", "<w:vanish/>")}</w:p>"""
            + Tbl(Tr(Tc("Seen")), $"""<w:tr><w:trPr><w:hidden/></w:trPr>{Tc("Unseen")}</w:tr>""");
        var doc = Clean(Docx(body));
        Assert.Equal(["Rules"], doc.Sections.Select(s => s.Id));
        Assert.DoesNotContain("Unseen", doc.Markdown);
        Assert.Contains("section Rules: a hidden table row left out (1 words)", doc.Problems);
    }

    [Fact]
    public void Struck_through_text_is_kept_and_listed()
    {
        var doc = Clean(Docx(H("Rules") + $"<w:p>{R("Old rule.", "<w:strike/>")}</w:p>"));
        Assert.Equal("Old rule.", Text(doc, "Rules"));
        Assert.Contains("section Rules: struck-through text kept as ordinary text (2 words)", doc.Problems);
    }

    [Fact]
    public void Comments_stay_out_of_the_body_and_are_listed_with_author_section_and_state()
    {
        var body = H("Rules") + H("Limits", 2) + """<w:p><w:commentRangeStart w:id="7"/><w:r><w:t>At most 3.</w:t></w:r><w:commentRangeEnd w:id="7"/><w:r><w:commentReference w:id="7"/></w:r></w:p>"""
            + """<w:p><w:commentRangeStart w:id="8"/><w:r><w:t>Done.</w:t></w:r><w:commentRangeEnd w:id="8"/></w:p>""";
        var doc = Clean(Docx(body, m =>
        {
            m.AddNewPart<WordprocessingCommentsPart>().Comments = new Comments($"""
                <w:comments {Ns}>
                  <w:comment w:id="9" w:author="Nobody"><w:p><w:r><w:t>Lost anchor.</w:t></w:r></w:p></w:comment>
                  <w:comment w:id="7" w:author="Jane &quot;BA&quot; Doe"><w:p><w:r><w:t>Is it 3 or 5?</w:t></w:r></w:p></w:comment>
                  <w:comment w:id="8" w:author="Sam"><w:p w14:paraId="0000ABCD"><w:r><w:t>Settled.</w:t></w:r></w:p></w:comment>
                </w:comments>
                """);
            m.AddNewPart<WordprocessingCommentsExPart>().CommentsEx = new DocumentFormat.OpenXml.Office2013.Word.CommentsEx(
                $"""<w15:commentsEx {Ns}><w15:commentEx w15:paraId="0000ABCD" w15:done="1"/></w15:commentsEx>""");
        }));
        Assert.DoesNotContain("Is it 3 or 5", doc.Markdown);
        var comments = doc.Problems.Select(DocxComment.Parse).OfType<DocxComment>().ToList();
        Assert.Equal(
            [
                new DocxComment("Rules / Limits", "Jane \"BA\" Doe", false, "Is it 3 or 5?"),
                new DocxComment("Rules / Limits", "Sam", true, "Settled."),
                new DocxComment(DocxComment.NoAnchor, "Nobody", false, "Lost anchor."),
            ], comments);
        Assert.Contains("comment: section \"Rules / Limits\" by \"Sam\" resolved: \"Settled.\"", doc.Problems);
    }

    [Fact]
    public void A_comment_on_a_heading_belongs_to_that_headings_section_even_with_a_footnote_before_it()
    {
        var body = H("First") + """<w:p><w:r><w:t>Text.</w:t></w:r><w:r><w:footnoteReference w:id="1"/></w:r></w:p>"""
            + """<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:commentRangeStart w:id="1"/><w:r><w:t>Second</w:t></w:r></w:p>""";
        var doc = Clean(Docx(body, m =>
        {
            m.AddNewPart<FootnotesPart>().Footnotes = new Footnotes(Notes("footnote", "footnotes", "A note."));
            m.AddNewPart<WordprocessingCommentsPart>().Comments = new Comments($"""<w:comments {Ns}><w:comment w:id="1" w:author="Jo"><w:p><w:r><w:t>Why?</w:t></w:r></w:p></w:comment></w:comments>""");
        }));
        Assert.Equal("Second", doc.Problems.Select(DocxComment.Parse).OfType<DocxComment>().Single().Section);
    }

    [Fact]
    public void Footnotes_and_endnotes_come_in_at_the_end_of_their_section_labelled()
    {
        var body = H("Rules") + """<w:p><w:r><w:t>Only admins may delete.</w:t></w:r><w:r><w:footnoteReference w:id="1"/></w:r><w:r><w:t xml:space="preserve"> Then more.</w:t></w:r></w:p>"""
            + P("Second paragraph.")
            + """<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Next</w:t></w:r><w:r><w:endnoteReference w:id="1"/></w:r></w:p>""" + P("Body.");
        var doc = Clean(Docx(body, m =>
        {
            m.AddNewPart<FootnotesPart>().Footnotes = new Footnotes(Notes("footnote", "footnotes", "Except on Sundays."));
            m.AddNewPart<EndnotesPart>().Endnotes = new Endnotes(Notes("endnote", "endnotes", "See appendix."));
        }));
        Assert.Equal("Only admins may delete. Then more.\n\nSecond paragraph.\n\n[Footnote 1, after \"Only admins may delete.\"] Except on Sundays.", Text(doc, "Rules"));
        Assert.Equal("Body.\n\n[Endnote 1, after \"Next\"] See appendix.", Text(doc, "Next"));
    }

    [Fact]
    public void An_embedded_file_a_chart_and_an_equation_are_named_by_kind_and_section()
    {
        var body = H("Rules") + """
            <w:p><w:r><w:object><v:shape id="s"/><o:OLEObject ProgID="Excel.Sheet.12" r:id="x"/></w:object></w:r></w:p>
            <w:p><w:r><w:drawing><wp:inline><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"/></a:graphic></wp:inline></w:drawing></w:r></w:p>
            <w:p><w:r><w:t xml:space="preserve">Fee is </w:t></w:r><m:oMath><m:r><m:t>x+1</m:t></m:r></m:oMath></w:p>
            <w:p><w:r><w:sym w:font="Wingdings" w:char="F0FC"/></w:r></w:p>
            """;
        var doc = Clean(Docx(body));
        Assert.Equal("Fee is", Text(doc, "Rules"));
        Assert.Contains("section Rules: an embedded file not read (Excel.Sheet.12)", doc.Problems);
        Assert.Contains("section Rules: a chart not read", doc.Problems);
        Assert.Contains("section Rules: an equation not read", doc.Problems);
        Assert.Contains("section Rules: a symbol not read", doc.Problems);
    }

    [Fact]
    public void A_text_box_keeps_its_words_once_apart_from_the_words_around_it_and_is_listed()
    {
        var box = """<w:txbxContent><w:p><w:r><w:t>Boxed</w:t></w:r></w:p></w:txbxContent>""";
        var shape = $"""<w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing><wp:inline><a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>Boxed</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape><v:textbox>{box}</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r>""";
        var body = H("Rules") + $"""<w:p><w:r><w:t>Before</w:t></w:r>{shape}<w:r><w:t>After</w:t></w:r></w:p>"""
            + Tbl(Tr(Tc($"""<w:p><w:r><w:t>Cell</w:t></w:r>{shape}</w:p>""")));
        var doc = Clean(Docx(body));
        Assert.Equal("Before Boxed After\n\n| Cell Boxed |\n|---|", Text(doc, "Rules"));
        Assert.Contains(doc.Problems, p => p.StartsWith("section Rules: a text box: its text is kept", StringComparison.Ordinal));
    }

    [Fact]
    public void A_content_control_keeps_its_content_loses_its_prompt_and_rows_inside_one_stay()
    {
        var body = H("Rules")
            + $"""<w:sdt><w:sdtPr/><w:sdtContent>{P("Chosen value.")}</w:sdtContent></w:sdt>"""
            + $"""<w:sdt><w:sdtPr><w:showingPlcHdr/></w:sdtPr><w:sdtContent>{P("Click here to enter text.")}</w:sdtContent></w:sdt>"""
            + $"""<w:p><w:sdt><w:sdtPr><w:showingPlcHdr/></w:sdtPr><w:sdtContent><w:r><w:t>Pick one.</w:t></w:r></w:sdtContent></w:sdt></w:p>"""
            + Tbl(Tr(Tc("Top")), $"""<w:sdt><w:sdtPr/><w:sdtContent>{Tr(Tc("Wrapped row"), $"<w:sdt><w:sdtPr/><w:sdtContent>{Tc("Wrapped cell")}</w:sdtContent></w:sdt>")}</w:sdtContent></w:sdt>""");
        var doc = Clean(Docx(body));
        Assert.Equal("Chosen value.\n\n| Top |  |\n|---|---|\n| Wrapped row | Wrapped cell |", Text(doc, "Rules"));
        Assert.Contains("section Rules: a content control's placeholder text left out (7 words)", doc.Problems);
    }

    [Fact]
    public void A_field_that_can_go_stale_is_listed_and_a_page_number_is_not()
    {
        var body = H("Rules") + """
            <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText> DOCPROPERTY Limit </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>Five</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            <w:p><w:fldSimple w:instr=" PAGE "><w:r><w:t>3</w:t></w:r></w:fldSimple></w:p>
            """;
        var doc = Clean(Docx(body));
        Assert.Equal("Five\n\n3", Text(doc, "Rules"));
        Assert.Equal(["section Rules: a DOCPROPERTY field: its text is what Word last saved"], doc.Problems);
    }

    [Fact]
    public void SmartArt_words_become_a_list_and_are_listed()
    {
        const string graphic = """<w:p><w:r><w:drawing><wp:inline><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/diagram"><dgm:relIds xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram" r:dm="rIdD"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>""";
        var doc = Clean(Docx(H("Rules") + graphic, m =>
        {
            var data = m.AddNewPart<DiagramDataPart>("rIdD");
            using var s = new MemoryStream(Encoding.UTF8.GetBytes("""<dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><dgm:ptLst><dgm:pt modelId="1"><dgm:t><a:bodyPr/><a:p><a:r><a:t>Approve order</a:t></a:r></a:p></dgm:t></dgm:pt><dgm:pt modelId="2"><dgm:t><a:bodyPr/><a:p><a:r><a:t>Ship</a:t></a:r></a:p></dgm:t></dgm:pt></dgm:ptLst></dgm:dataModel>"""));
            data.FeedData(s);
        }));
        Assert.Equal("- Approve order\n- Ship", Text(doc, "Rules"));
        Assert.Contains("section Rules: SmartArt: its words are kept as a list, its shape and order aren't", doc.Problems);
    }

    [Fact]
    public void A_nested_table_keeps_its_words_and_is_listed_as_flattened()
    {
        var doc = Clean(Docx(H("Rules") + Tbl(Tr(Tc(P("Outer") + Tbl(Tr(Tc("Inner one"), Tc("Inner two"))) + P("End"))))));
        Assert.Equal("| Outer Inner one Inner two End |\n|---|", Text(doc, "Rules"));
        Assert.Contains("section Rules: a nested table flattened: its words are kept, its rows and columns aren't", doc.Problems);
    }

    [Fact]
    public void A_cell_merged_down_rows_repeats_its_value_and_is_listed()
    {
        var doc = Clean(Docx(H("Rules") + Tbl(
            Tr(Tc("Status"), Tc("Rule")),
            Tr(Tc("Open", """<w:vMerge w:val="restart"/>"""), Tc("Can cancel")),
            Tr(Tc("<w:p/>", "<w:vMerge/>"), Tc("Can edit")),
            Tr(Tc("Stale text", "<w:vMerge/>"), Tc("Can ship")),
            Tr(Tc("Closed"), Tc("Read only")))));
        Assert.Equal("| Status | Rule |\n|---|---|\n| Open | Can cancel |\n| Open | Can edit |\n| Open | Can ship |\n| Closed | Read only |", Text(doc, "Rules"));
        Assert.Contains("section Rules: a cell merged down rows repeats its value in each row (2 cells)", doc.Problems);
        Assert.Contains("section Rules: text Word doesn't show in a cell merged down left out (2 words)", doc.Problems);
    }

    [Fact]
    public void A_row_that_starts_part_way_across_keeps_its_cells_under_the_right_columns()
    {
        var doc = Clean(Docx(H("Rules") + Tbl(Tr(Tc("A"), Tc("B")), $"""<w:tr><w:trPr><w:gridBefore w:val="1"/></w:trPr>{Tc("Under B")}</w:tr>""")));
        Assert.Equal("| A | B |\n|---|---|\n|  | Under B |", Text(doc, "Rules"));
    }

    [Fact]
    public void Headers_and_footers_are_listed_with_a_word_count()
    {
        var doc = Clean(Docx(H("Rules") + P("Text."), m =>
            m.AddNewPart<HeaderPart>().Header = new Header($"""<w:hdr {Ns}>{P("Confidential draft")}</w:hdr>""")));
        Assert.Contains("page headers and footers aren't imported (2 words)", doc.Problems);
    }

    [Fact]
    public void The_completeness_check_lists_a_block_nobody_thought_of_by_section_and_keeps_going()
    {
        var unknown = $"""<mc:AlternateContent><mc:Choice Requires="w14">{P("Vanishing words here")}</mc:Choice></mc:AlternateContent>""";
        var doc = DocxConverter.Convert(Docx(H("Rules") + P("Kept.") + unknown + H("Next") + P("Fine.")));
        Assert.Equal("Kept.", Text(doc, "Rules"));
        var problem = Assert.Single(doc.Problems);
        Assert.Equal("""completeness: section Rules: 3 words in the file aren't in document.md ("Vanishing", "here", "words")""", problem);
    }

    [Fact]
    public void A_fallback_with_no_choice_is_what_the_file_holds_so_it_is_read_and_checked()
    {
        var box = $"""<w:p><w:r><w:t xml:space="preserve">Before </w:t></w:r><w:r><mc:AlternateContent><mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent>{P("Boxed rule here")}</w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>""";
        var inline = """<w:p><mc:AlternateContent><mc:Fallback><w:r><w:t>Inline rule here</w:t></w:r></mc:Fallback></mc:AlternateContent></w:p>""";
        var block = $"""<mc:AlternateContent><mc:Fallback>{P("Block rule here")}</mc:Fallback></mc:AlternateContent>""";

        var doc = DocxConverter.Convert(Docx(H("Rules") + box + inline + block));

        var text = Text(doc, "Rules");
        Assert.Contains("Boxed rule here", text);
        Assert.Contains("Inline rule here", text);
        Assert.Contains("Block rule here", text);
        // Listed like any text box, and nothing's missing from the render.
        Assert.Contains(doc.Problems, p => p.Contains("a text box", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Problems, p => p.StartsWith("completeness:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_completeness_check_sees_a_fallback_with_no_choice_where_the_converter_reads_nothing()
    {
        // No rule reads a paragraph's text out of a picture's old-style shape data, so this is the check's to catch.
        var doc = DocxConverter.Convert(Docx(H("Rules") + """<w:p><w:r><mc:AlternateContent><mc:Fallback><w:pict><v:shape><v:textpath string="Big claim"/></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>"""));
        Assert.Contains(doc.Problems, p => p.StartsWith("completeness: section Rules: 2 words in the file", StringComparison.Ordinal));
    }

    [Fact]
    public void The_completeness_check_lists_word_art_it_has_no_rule_for()
    {
        var doc = DocxConverter.Convert(Docx(H("Rules") + """<w:p><w:r><w:pict><v:shape><v:textpath string="Big claim"/></v:shape></w:pict></w:r></w:p>"""));
        Assert.Contains(doc.Problems, p => p.StartsWith("completeness: section Rules: 2 words in the file", StringComparison.Ordinal));
    }

    [Fact]
    public void The_completeness_check_stays_quiet_on_labels_numbers_escapes_and_flat_imports()
    {
        var numbered = DocxConverter.Convert(DocxWriter.Write("#! Orders\n1. Pick.\n- Bullet.\n| A | B |\n|---|---|\n| x | y |\n", "."));
        Assert.Empty(numbered.Problems);
        var body = P("Before the heading.") + H("Rules") + Tbl(Tr(Tc("a|b"), Tc("c")));
        Assert.Contains(@"a\|b", Clean(Docx(body)).Markdown);
        var flat = Clean(Docx(P("No headings here.")), flat: true);
        Assert.Single(flat.Problems);
    }

    [Fact]
    public void The_same_file_gives_the_same_markdown_and_problems()
    {
        var bytes = Docx(WithChanges + H("Rules two") + $"<w:p>{R("Hidden", "<w:vanish/>")}{R("Shown")}</w:p>");
        var (a, b) = (DocxConverter.Convert(bytes, acceptChanges: true), DocxConverter.Convert(bytes, acceptChanges: true));
        Assert.Equal(a.Markdown, b.Markdown);
        Assert.Equal(a.Problems, b.Problems);
    }

    [Fact]
    public void The_sonarr_fsd_has_nothing_to_list()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "benchmark", "fsd", "sonarr", "sonarr-fsd.docx"))) root = Directory.GetParent(root)!.FullName;
        var doc = DocxConverter.Convert(File.ReadAllBytes(Path.Combine(root, "benchmark", "fsd", "sonarr", "sonarr-fsd.docx")));
        Assert.Empty(doc.Problems);
        Assert.Equal("b546bb24c70f6901327bdc6298db22e136aec9ca7ff2c3ae76cb39e7d5254f50", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(doc.Markdown))));
    }

    [Fact]
    public void A_spec_link_with_no_target_is_skipped_when_looking_for_related_specs()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempRepo("tickets:\n  site: acme.atlassian.net\n  app: { start: ./run.sh, url: \"http://127.0.0.1:3000\" }\n");
        File.WriteAllText(Path.Combine(temp.Root, "real.spec.ts"), "await page.goto('/shop');");
        File.CreateSymbolicLink(Path.Combine(temp.Root, "dangling.spec.ts"), Path.Combine(temp.Root, "gone.ts"));
        var bundle = new JsonObject { ["pages"] = new JsonArray(new JsonObject { ["kind"] = "screen", ["id"] = "page:/shop" }) };
        Assert.Equal(["real.spec.ts"], AppSession.RelatedSpecs(temp.Repo, bundle));
    }
}

/// <summary>I.11: Word's table of contents is a list of headings and page numbers, not the document's text.</summary>
public sealed class WordTableOfContentsTests
{
    const string Heading = """<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Rules</w:t></w:r></w:p>""";
    const string Body = """<w:p><w:r><w:t>Orders over fifty need approval.</w:t></w:r></w:p>""";
    const string Toc = """
        <w:p><w:pPr><w:pStyle w:val="TOCHeading"/></w:pPr><w:r><w:t>Contents</w:t></w:r></w:p>
        <w:p><w:pPr><w:pStyle w:val="TOC1"/></w:pPr>
          <w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> TOC \o "1-3" \h </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r>
          <w:hyperlink w:anchor="_Toc1"><w:r><w:t>Rules</w:t></w:r><w:r><w:tab/></w:r>
            <w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> PAGEREF _Toc1 \h </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>3</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r>
          </w:hyperlink>
        </w:p>
        <w:p><w:pPr><w:pStyle w:val="TOC2"/></w:pPr><w:hyperlink w:anchor="_Toc2"><w:r><w:t>More rules</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>4</w:t></w:r></w:hyperlink></w:p>
        <w:p><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
        """;
    const string Styles = """
        <w:style w:type="paragraph" w:styleId="TOC1"><w:name w:val="toc 1"/></w:style>
        <w:style w:type="paragraph" w:styleId="TOC2"><w:name w:val="toc 2"/></w:style>
        <w:style w:type="paragraph" w:styleId="TOCHeading"><w:name w:val="TOC Heading"/></w:style>
        """;

    static Stangent.Spec.Fsd.ConvertedDocument Convert(string body) => Stangent.Spec.Fsd.DocxConverter.Convert(FsdDocxTests.Docx(body, styles: Styles));

    static void AssertRemoved(Stangent.Spec.Fsd.ConvertedDocument doc)
    {
        Assert.DoesNotContain("Contents", doc.Markdown);
        Assert.DoesNotContain("More rules", doc.Markdown);
        Assert.DoesNotContain("_Toc", doc.Markdown);
        Assert.Contains("Orders over fifty need approval.", doc.Markdown);
        Assert.Equal("Rules", Assert.Single(doc.Sections).Title);
    }

    [Fact]
    public void A_field_table_of_contents_is_removed() => AssertRemoved(Convert(Toc + Heading + Body));

    [Fact]
    public void One_in_a_content_control_as_Word_saves_it_is_removed() => AssertRemoved(Convert(
        $"""<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val="Table of Contents"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtContent>{Toc}</w:sdtContent></w:sdt>""" + Heading + Body));

    [Fact]
    public void Text_in_no_such_style_or_field_is_kept()
    {
        var doc = Convert(Heading + Body + """<w:p><w:r><w:t>Contents of the box are listed.</w:t></w:r></w:p>""");
        Assert.Contains("Contents of the box are listed.", doc.Markdown);
    }
}
