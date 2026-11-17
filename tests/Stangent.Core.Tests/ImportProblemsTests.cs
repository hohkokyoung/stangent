using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>Structured import problems (<c>source-document</c> v3, D121).</summary>
public sealed class ImportProblemsTests : IDisposable
{
    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public ImportProblemsTests() => repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    static List<JsonObject> Sections(params (string Id, int Line)[] ids) =>
        [.. ids.Select(i => new JsonObject { ["id"] = i.Id, ["line"] = i.Line })];

    static byte[] OnePagePdf(params string[] lines)
    {
        using var pdf = new PdfFixture();
        pdf.Page(lines);
        return pdf.Build(PdfFixture.Mark(lines[0], 0, 1));
    }

    string ImportFile(string name, byte[] bytes, out JsonObject result, bool flat = false)
    {
        var file = Path.Combine(archive.Root, name);
        File.WriteAllBytes(file, bytes);
        result = FsdImport.Import(repo.Repo, file, "shop", flat);
        Committed.All(repo.Repo);
        return Path.Combine(FsdImport.DocDir(repo.Root, "shop"), "document.json");
    }

    [Fact]
    public void A_comment_line_becomes_its_fields_and_the_detail_keeps_the_line()
    {
        var line = new DocxComment("Rules: part one", "Jane \"BA\" Doe", true, "Is it 3: or 5?").Line();
        var p = ImportProblems.Of(line, Sections(("Rules: part one", 3)));
        Assert.Equal("comment", (string)p["kind"]!);
        Assert.Equal(line, (string)p["detail"]!);
        Assert.Equal("Rules: part one", (string)p["sectionNumber"]!);
        Assert.Equal(("Jane \"BA\" Doe", "resolved", "Is it 3: or 5?"), ((string)p["author"]!, (string)p["state"]!, (string)p["text"]!));
    }

    [Fact]
    public void A_comment_with_no_anchor_names_no_section()
    {
        var p = ImportProblems.Of(new DocxComment(DocxComment.NoAnchor, "Sam", false, "Lost.").Line(), Sections(("1", 1)));
        Assert.Null(p["sectionNumber"]);
        Assert.Equal("open", (string)p["state"]!);
    }

    [Theory]
    [InlineData("section 4.2: a chart not read", "element", "4.2")]
    [InlineData("section Rules: part one: hidden text left out (5 words)", "element", "Rules: part one")]
    [InlineData("completeness: section 4.2: 3 words in the file aren't in document.md (\"x\")", "completeness", "4.2")]
    [InlineData("completeness: before the first heading: 3 words in the file aren't in document.md (\"x\")", "completeness", null)]
    [InlineData("section 4.2 follows 4: a number is skipped or out of order", "numbering", "4.2")]
    [InlineData("line 9: section 4.2 is out of step with its parent 3; it keeps its number", "numbering", "4.2")]
    [InlineData("line 2: heading \"2023 Roadmap\" starts with 2023, which doesn't fit the numbering around it; read as a plain heading", "numbering", "4")]
    [InlineData("--accept-changes: 4 tracked changes accepted (x); this is the accepted view, not an agreed document", "tracked-changes", null)]
    [InlineData("imported with --flat: the whole document is one section, with no structure", "flat", null)]
    [InlineData("page headers and footers aren't imported (3 words)", "outside-body", null)]
    [InlineData("page 2: no text (scanned or picture-only); nothing was read from it", "page-text", null)]
    [InlineData("page 2: a table wasn't brought in exactly (x); kept as plain text", "table", null)]
    [InlineData("page 2: the section title \"T\" isn't a whole line on its page; its heading is placed", "heading", null)]
    [InlineData("removed a running footer on 6 pages: \"x\"", "running-line", null)]
    [InlineData("a repeated line was kept, because it doesn't stand apart from the text, on 3 pages: \"x\"", "running-line", null)]
    [InlineData("printed contents: the line \"x\" has no page number; not used as a section", "contents", null)]
    [InlineData("sections came from the printed contents, so they may be coarser than the document's real structure", "contents", null)]
    [InlineData("bookmark \"x\" has no page inside the document; not used", "bookmark", null)]
    [InlineData("1 images in this document (1 different): no AI or code reads them", "image", null)]
    [InlineData("2 words come before the first section, so they're in the document but in no section", "before-first-section", null)]
    [InlineData("something nobody has thought of", "other", null)]
    public void A_line_gets_its_kind_and_its_section(string line, string kind, string? section)
    {
        var p = ImportProblems.Of(line, Sections(("4", 1), ("4.2", 5), ("Rules: part one", 20)));
        Assert.Equal(kind, (string)p["kind"]!);
        Assert.Equal(line, (string)p["detail"]!);
        Assert.Equal(section, (string?)p["sectionNumber"]);
    }

    [Fact]
    public void A_page_line_keeps_its_page_and_a_markdown_line_its_line()
    {
        Assert.Equal(2, (int)ImportProblems.Of("page 2: no text", [])["page"]!);
        Assert.Equal(9, (int)ImportProblems.Of("line 9: section 1 is out of step with its parent 2; it keeps its number", [])["line"]!);
    }

    [Fact]
    public void An_import_writes_v4_with_permanent_ids_on_its_problems()
    {
        var json = ImportFile("a.docx", FsdDocxTests.Docx(FsdDocxTests.H("Rules") + FsdDocxTests.P("Text.") + $"<w:p>{FsdDocxTests.R("gone", "<w:vanish/>")}</w:p>"), out var result);
        var p = Assert.Single(result["problems"]!.AsArray())!;
        Assert.Equal(("element", "S-0001", "Rules"), ((string)p["kind"]!, (string)p["section"]!, (string)p["sectionNumber"]!));
        var doc = JsonNode.Parse(File.ReadAllText(json))!.AsObject();
        Assert.Equal(4, (int)doc["schemaVersion"]!);
        Assert.Empty(SchemaRegistry.Embedded.Validate("source-document.v4.json", doc.ToJsonString()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_document_from_before_is_left_out_until_migrate_reads_its_lines(int version)
    {
        var bytes = FsdDocxTests.Docx(FsdDocxTests.H("Rules") + FsdDocxTests.P("Text.") + $"<w:p>{FsdDocxTests.R("gone", "<w:vanish/>")}</w:p>");
        var json = ImportFile("a.docx", bytes, out _);
        var md = Chapters.Bytes(Path.GetDirectoryName(json)!);

        // As an older build left it: document.md, the sections in document.json, its problems as plain lines.
        var dir = Path.GetDirectoryName(json)!;
        var old = FsdDocument.Load(repo.Root, "shop");
        old.Remove("importCommit");
        old.Remove("fileSections");
        old.Remove("converter");
        old["markdown"] = "document.md";
        File.WriteAllText(Path.Combine(dir, "document.md"), Chapters.Text(dir));
        Directory.Delete(FsdChapters.Dir(dir), recursive: true);
        old["schemaVersion"] = version;
        old["problems"] = new JsonArray([.. old["problems"]!.AsArray().Select(p => (JsonNode)(string)p!["detail"]!)]);
        old.Remove("tables");
        foreach (var s in old["sections"]!.AsArray().OfType<JsonObject>()) { s.Remove("contentHash"); s.Remove("attachments"); s.Remove("attachmentLines"); }
        foreach (var i in old["images"]!.AsArray().OfType<JsonObject>()) { i.Remove("id"); i.Remove("section"); i.Remove("sectionNumber"); }
        if (version == 1) foreach (var s in old["sections"]!.AsArray().OfType<JsonObject>()) s.Remove("permanentId");
        Assert.Empty(SchemaRegistry.Embedded.Validate($"source-document.v{version}.json", old.ToJsonString()));
        File.WriteAllText(json, CanonicalJson.Serialize(old));
        Committed.All(repo.Repo);
        // Since phase fs layer 2 (D392) it isn't read until `fsd migrate` moves it, and migrating reads its lines.
        Assert.Empty(FsdImport.Documents(repo.Root));
        Assert.Equal("fsd_not_migrated", Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, Path.Combine(archive.Root, "a.docx"), "shop")).Code);
        Assert.Equal("migrated", (string)FsdMigrate.Run(repo.Repo, "shop")["status"]!);
        Committed.All(repo.Repo);
        var now = JsonNode.Parse(File.ReadAllText(json))!.AsObject();
        Assert.Equal(4, (int)now["schemaVersion"]!);
        Assert.Equal("element", (string)Assert.Single(now["problems"]!.AsArray())!["kind"]!);
        Assert.Equal(md, Chapters.Bytes(Path.GetDirectoryName(json)!));
    }

    [Fact]
    public void A_new_files_problems_are_listed_in_its_comparison_on_the_permanent_id_of_their_section()
    {
        var json = ImportFile("a.docx", FsdDocxTests.Docx(FsdDocxTests.H("Rules") + FsdDocxTests.P("Apples are sorted by size before packing.")), out _);
        var file = Path.Combine(archive.Root, "a.docx");
        File.WriteAllBytes(file, FsdDocxTests.Docx(FsdDocxTests.H("Rules") + FsdDocxTests.P("Apples are sorted by size before packing.") + $"<w:p>{FsdDocxTests.R("gone", "<w:vanish/>")}</w:p>"));
        var offered = FsdImport.Import(repo.Repo, file, "shop");
        Committed.All(repo.Repo);
        Assert.Equal("compared", (string)offered["status"]!);
        Assert.Equal(5, offered["steps"]!.AsArray().Count);
        Assert.Equal("S-0001", (string)Assert.Single(offered["problems"]!.AsArray())!["section"]!);
        // The repo's record holds the problems of the file it came from; the new file's reach it only for a section taken (D385).
        Assert.Empty(JsonNode.Parse(File.ReadAllText(json))!["problems"]!.AsArray());
    }
}
