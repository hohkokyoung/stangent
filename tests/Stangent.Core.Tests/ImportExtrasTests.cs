using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>Reading a file again, the verdict and report, labels and locks, bold lines, table and picture ids, and first-import approval (D122). No AI.</summary>
public sealed class ImportExtrasTests : IDisposable
{
    const string Outline = "# Fruit\n\nApples are sorted by size before they are packed.\n\n| Fruit | Days |\n| Pears | 3 |\n\n# Veg\n\nLeeks wait a day in the cold room.\n";
    const string Label = "2096f6a2-d2f7-48be-b329-b73aaa526e5d";

    readonly TempRepo archive = new();
    readonly TempRepo repo;
    readonly Dictionary<string, byte[]> files = [];

    public ImportExtrasTests()
    {
        repo = new TempRepo(Config());
        Assert.NotNull(Git.Run(repo.Root, "init -q"));
        Git.Run(repo.Root, "config user.name \"Pat Example\"");
    }

    string Config(string extra = "") => $"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n{extra}";

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    string Dir => FsdImport.DocDir(Root, "shop");

    string File_(byte[] bytes, string name = "shop.docx")
    {
        var path = Path.Combine(Root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The same outline is the same file: Word's package isn't the same bytes each time it's written.</summary>
    byte[] Bytes(string outline)
    {
        if (!files.TryGetValue(outline, out var bytes)) files[outline] = bytes = DocxWriter.Write(outline);
        return bytes;
    }

    JsonObject Import(string outline = Outline, IReadOnlyList<string>? allow = null) => FsdImport.Import(repo.Repo, File_(Bytes(outline)), allowLabels: allow);

    JsonObject Doc() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "document.json")))!.AsObject();

    static string Code(Action a) => Assert.Throws<StangentException>(a).Code;

    static byte[] WithEntry(byte[] docx, params (string Path, string Xml)[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            foreach (var (path, xml) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(path).Open())) w.Write(xml);
        return stream.ToArray();
    }

    static string Custom(string name, string enabled = "true") =>
        $"""<?xml version="1.0"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><property fmtid="x" pid="2" name="MSIP_Label_{Label}_Enabled"><vt:lpwstr>{enabled}</vt:lpwstr></property><property fmtid="x" pid="3" name="MSIP_Label_{Label}_Name"><vt:lpwstr>{name}</vt:lpwstr></property></Properties>""";

    // ---- A.1: reading the original again ----

    [Fact]
    public void Reading_the_same_file_again_with_the_same_text_is_unchanged_and_writes_nothing_new()
    {
        Import();
        var before = File.ReadAllBytes(Path.Combine(Dir, "document.json"));
        var again = FsdImport.Reread(repo.Repo, "shop");
        Assert.Equal(("unchanged", true, false), ((string)again["status"]!, (bool)again["reread"]!, (bool)again["problemsChanged"]!));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Dir, "document.json")));
        Assert.False(Directory.Exists(FsdVersions.CandidateDir(Root, "shop")));
    }

    [Fact]
    public void Text_a_newer_build_reads_differently_comes_in_as_a_candidate_and_the_current_version_stays()
    {
        Import();
        // What an older converter wrote: the words differ, and the file doesn't.
        var path = Path.Combine(Dir, "document.md");
        var older = File.ReadAllText(path).Replace("Leeks wait a day", "Leeks wait aday");
        File.WriteAllText(path, older);
        var hash = (string)Doc()["original"]!["hash"]!;

        var again = FsdImport.Reread(repo.Repo, "shop");
        Assert.Equal(("candidate", true, hash, hash), ((string)again["status"]!, (bool)again["reread"]!, (string)again["hash"]!, (string)again["from"]!));
        Assert.Equal(older, File.ReadAllText(path));
        Assert.Contains("Leeks wait a day", File.ReadAllText(Path.Combine(FsdVersions.CandidateDir(Root, "shop"), "document.md")));
        Assert.Equal("waiting", (string)FsdImport.Reread(repo.Repo, "shop")["status"]!);

        FsdVersions.Promote(repo.Repo, "shop", "Pat");
        Assert.Contains("Leeks wait a day", File.ReadAllText(path));
        Assert.Equal("unchanged", (string)FsdImport.Reread(repo.Repo, "shop")["status"]!);
    }

    [Fact]
    public void The_same_text_with_more_problems_found_updates_them_without_a_candidate()
    {
        Import();
        var doc = Doc();
        doc["problems"] = new JsonArray(new JsonObject { ["kind"] = "other", ["detail"] = "an older build said this" });
        File.WriteAllText(Path.Combine(Dir, "document.json"), CanonicalJson.Serialize(doc));
        var again = FsdImport.Reread(repo.Repo, "shop");
        Assert.Equal(("unchanged", true), ((string)again["status"]!, (bool)again["problemsChanged"]!));
        Assert.Empty(Doc()["problems"]!.AsArray());
    }

    [Fact]
    public void Reading_again_refuses_a_changed_or_missing_original_and_wont_replace_another_files_candidate()
    {
        Import();
        var archived = Path.Combine(archive.Root, (string)Doc()["original"]!["path"]!);
        File.WriteAllText(archived, "not the original");
        Assert.Equal("archive_file_changed", Code(() => FsdImport.Reread(repo.Repo, "shop")));
        File.Delete(archived);
        Assert.Equal("archive_file_missing", Code(() => FsdImport.Reread(repo.Repo, "shop")));
        Assert.Equal("not_found", Code(() => FsdImport.Reread(repo.Repo, "other")));

        Import(Outline + "\n# Herbs\n\nMint is cut to order every single morning.\n");
        Import();
        File.WriteAllBytes(archived, Bytes(Outline));
        Assert.Equal("candidate_waiting", Code(() => FsdImport.Reread(repo.Repo, "shop")));
    }

    // ---- A.2: the verdict and the report ----

    [Fact]
    public void A_clean_import_is_ok_and_a_report_lists_problems_by_kind_with_counts()
    {
        var clean = Import();
        Assert.Equal(("ok", "specs/fsd/shop/import-report.md"), ((string)clean["verdict"]!, (string)clean["report"]!));
        Assert.Contains("**Verdict: ok.**", File.ReadAllText(Path.Combine(Dir, "import-report.md")));

        var doc = Doc();
        doc["problems"] = new JsonArray(
            new JsonObject { ["kind"] = "table", ["detail"] = "page 1: a table" },
            new JsonObject { ["kind"] = "completeness", ["detail"] = "completeness: 3 words" },
            new JsonObject { ["kind"] = "table", ["detail"] = "page 2: a table" });
        Assert.Equal("ok-with-warnings", ImportReport.Verdict(doc));
        var report = ImportReport.Markdown(doc);
        Assert.Contains("| table | 2 |", report);
        Assert.Contains("## table (2)\n\n- page 1: a table\n- page 2: a table\n", report);
        Assert.True(report.IndexOf("## completeness", StringComparison.Ordinal) < report.IndexOf("## table", StringComparison.Ordinal), "words that didn't arrive come first");
    }

    [Fact]
    public void A_candidate_has_its_own_report_and_promotion_brings_it_over()
    {
        Import();
        var offered = Import(Outline + "\n# Herbs\n\nMint is cut to order every single morning.\n");
        Assert.Equal("specs/fsd/shop/candidate/import-report.md", (string)offered["report"]!);
        Assert.True(File.Exists(Path.Combine(FsdVersions.CandidateDir(Root, "shop"), "import-report.md")));
        Assert.DoesNotContain("Sections: 3", File.ReadAllText(Path.Combine(Dir, "import-report.md")));
        var promoted = FsdVersions.Promote(repo.Repo, "shop", "Pat");
        Assert.Equal("ok", (string)promoted["verdict"]!);
        Assert.Contains("Sections: 3", File.ReadAllText(Path.Combine(Dir, "import-report.md")));
    }

    [Fact]
    public void The_report_is_rewritten_when_missing_and_nothing_in_src_reads_it_back()
    {
        Import();
        File.Delete(Path.Combine(Dir, "import-report.md"));
        Assert.Equal("unchanged", (string)Import()["status"]!);
        Assert.True(File.Exists(Path.Combine(Dir, "import-report.md")));

        var uses = Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("import-report.md", StringComparison.Ordinal)).Select(Path.GetFileName);
        Assert.Equal(["ImportReport.cs"], uses);
    }

    // ---- A.3: locks and labels ----

    [Fact]
    public void A_locked_word_file_gets_its_own_stop_with_instructions()
    {
        byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. new byte[64], .. Encoding.Unicode.GetBytes("EncryptedPackage")];
        var e = Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, File_(ole)));
        Assert.Equal("docx_locked", e.Code);
        Assert.Contains("Encrypt with Password", e.Message);
        Assert.False(Directory.Exists(Dir));
    }

    [Fact]
    public void A_labelled_word_file_stops_until_a_person_names_the_label_and_the_record_says_who()
    {
        var labelled = WithEntry(DocxWriter.Write(Outline), ("docProps/custom.xml", Custom("Confidential")));
        var e = Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, File_(labelled)));
        Assert.Equal("document_labelled", e.Code);
        Assert.Contains("Confidential", e.Message);
        Assert.False(Directory.Exists(Dir));
        Assert.False(Directory.Exists(Path.Combine(archive.Root, "shop")));
        Assert.Equal("document_labelled", Code(() => FsdImport.Import(repo.Repo, File_(labelled), allowLabels: ["Public"])));

        var allowed = FsdImport.Import(repo.Repo, File_(labelled), allowLabels: ["confidential"]);
        Assert.Equal("imported", (string)allowed["status"]!);
        var record = Doc()["labels"]![0]!;
        Assert.Equal(("Confidential", Label, "Pat Example"), ((string)record["name"]!, (string)record["id"]!, (string)record["allowedBy"]!));
        Assert.Contains("Confidential", File.ReadAllText(Path.Combine(Dir, "import-report.md")));
        Assert.Equal("unchanged", (string)FsdImport.Reread(repo.Repo, "shop")["status"]!);
    }

    [Theory]
    [InlineData("""<labelList xmlns="urn:x"><label id="{2096F6A2-D2F7-48BE-B329-B73AAA526E5D}" enabled="1" removed="0"/></labelList>""", true)]
    [InlineData("""<labelList xmlns="urn:x"><label id="{2096F6A2-D2F7-48BE-B329-B73AAA526E5D}" enabled="0" removed="0"/></labelList>""", false)]
    [InlineData("""<labelList xmlns="urn:x"><label id="{2096F6A2-D2F7-48BE-B329-B73AAA526E5D}" enabled="1" removed="1"/></labelList>""", false)]
    public void LabelInfo_counts_only_a_label_that_is_on(string xml, bool stops)
    {
        var labels = ImportLabels.OfWord(WithEntry(DocxWriter.Write(Outline), ("docMetadata/LabelInfo.xml", xml)));
        Assert.Equal(stops, labels.Count == 1);
        if (stops) Assert.True(labels[0].Named(Label.ToUpperInvariant()));
    }

    [Fact]
    public void A_label_switched_off_in_custom_properties_doesnt_stop_it()
    {
        Assert.Empty(ImportLabels.OfWord(WithEntry(DocxWriter.Write(Outline), ("docProps/custom.xml", Custom("Confidential", "false")))));
    }

    [Fact]
    public void A_pdf_label_in_its_information_stops_the_import()
    {
        var pdf = FsdPdfTests.PlainPdf([$"<< /MSIP_Label_{Label}_Enabled (true) /MSIP_Label_{Label}_Name (Secret) >>"], "", "/Info 6 0 R");
        Assert.Equal("Secret", Assert.Single(PdfConverter.Labels(pdf)).Name);
        var e = Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, File_(pdf, "shop.pdf")));
        Assert.Equal("document_labelled", e.Code);
        Assert.Empty(PdfConverter.Labels(FsdPdfTests.PlainPdf([], "")));
    }

    [Fact]
    public void A_ticket_attachment_with_a_label_isnt_read_and_says_why()
    {
        var a = ImportPipeline.Attachment("spec.docx", WithEntry(DocxWriter.Write(Outline), ("docProps/custom.xml", Custom("Confidential"))))!;
        Assert.Null(a.Text);
        Assert.Contains("sensitivity label", a.NotConverted);
    }

    // ---- A.4: bold lines ----

    [Fact]
    public void A_word_file_with_no_headings_lists_its_bold_lines_by_location_and_guesses_nothing()
    {
        var bold = string.Concat(Enumerable.Range(1, 22).Select(i => $"""<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Part {i}</w:t></w:r></w:p>"""));
        var body = """<w:p><w:r><w:t>Plain words here.</w:t></w:r></w:p><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>A bold whole sentence that ends.</w:t></w:r></w:p><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Half </w:t></w:r><w:r><w:t>bold</w:t></w:r></w:p>""" + bold;
        var e = Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, File_(FsdDocxTests.Docx(body))));
        Assert.Equal("no_structure", e.Code);
        Assert.Contains("never guesses", e.Details[0]);
        Assert.Contains("first 20 of 22", e.Details[0]);
        Assert.Equal(21, e.Details.Count);
        Assert.Equal("paragraph 4: \"Part 1\"", e.Details[1]);
        Assert.DoesNotContain(e.Details, d => d.Contains("sentence", StringComparison.Ordinal) || d.Contains("Half", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Dir));
    }

    [Fact]
    public void Bold_from_a_paragraph_style_counts_and_a_file_with_no_bold_lines_lists_nothing()
    {
        const string styles = """<w:style w:type="paragraph" w:styleId="Loud"><w:name w:val="Loud"/><w:rPr><w:b/></w:rPr></w:style>""";
        var styled = Assert.Throws<StangentException>(() => DocxConverter.Convert(FsdDocxTests.Docx("""<w:p><w:pPr><w:pStyle w:val="Loud"/></w:pPr><w:r><w:t>Overview</w:t></w:r></w:p>""", styles: styles), false));
        Assert.Contains("paragraph 1: \"Overview\"", styled.Details);
        Assert.Empty(Assert.Throws<StangentException>(() => DocxConverter.Convert(FsdDocxTests.Docx("<w:p><w:r><w:t>Plain</w:t></w:r></w:p>"), false)).Details);
    }

    // ---- A.6: ids for tables and pictures ----

    static string Sketch(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "sketch.png"), File.ReadAllBytes(Path.Combine(RepoRoot.Find(), "benchmark/fsd/sonarr/sketch.png")));
        return dir;
    }

    [Fact]
    public void Tables_and_pictures_get_permanent_ids_with_their_section_and_the_text_doesnt_change()
    {
        var withPicture = Outline + "\n![The sketch](sketch.png)\n";
        var bytes = DocxWriter.Write(withPicture, Sketch(Root));
        FsdImport.Import(repo.Repo, File_(bytes));
        var doc = Doc();
        var table = Assert.Single(doc["tables"]!.AsArray())!;
        var veg = doc["sections"]!.AsArray().First(s => (string)s!["title"]! == "Veg")!;
        var fruit = doc["sections"]!.AsArray().First(s => (string)s!["title"]! == "Fruit")!;
        Assert.Equal(("T-0001", (string)fruit["permanentId"]!, 2, 2), ((string)table["id"]!, (string)table["section"]!, (int)table["rows"]!, (int)table["columns"]!));
        var image = Assert.Single(doc["images"]!.AsArray())!;
        Assert.Equal(("F-0001", (string)veg["permanentId"]!), ((string)image["id"]!, (string)image["section"]!));
        Assert.Equal("T-0001", Assert.Single(FsdVersions.Ledger(Root, "shop").Active("tables")));
        Assert.Equal("F-0001", Assert.Single(FsdVersions.Ledger(Root, "shop").Active("figures")));
    }

    [Fact]
    public void A_table_keeps_its_id_when_it_moves_or_is_edited_and_a_removed_one_never_comes_back()
    {
        const string two = "# Fruit\n\nApples are sorted by size before they are packed.\n\n| A | B |\n| 1 | 2 |\n\n| C | D |\n| 3 | 4 |\n\n# Veg\n\nLeeks wait a day in the cold room.\n";
        Import(two);
        // The second table moves to Veg and the first is edited where it is.
        const string moved = "# Fruit\n\nApples are sorted by size before they are packed.\n\n| A | B |\n| 1 | 9 |\n\n# Veg\n\nLeeks wait a day in the cold room.\n\n| C | D |\n| 3 | 4 |\n";
        Import(moved);
        FsdVersions.Promote(repo.Repo, "shop", "Pat");
        var tables = Doc()["tables"]!.AsArray();
        Assert.Equal(["T-0001", "T-0002"], tables.Select(t => (string)t!["id"]!));
        Assert.Equal((string)Doc()["sections"]![1]!["permanentId"]!, (string)tables[1]!["section"]!);

        const string gone = "# Fruit\n\nApples are sorted by size before they are packed.\n\n| A | B |\n| 1 | 9 |\n\n# Veg\n\nLeeks wait a day in the cold room.\n";
        Import(gone);
        FsdVersions.Promote(repo.Repo, "shop", "Pat");
        Assert.Contains("T-0002", FsdVersions.Ledger(Root, "shop").Removed("tables"));

        Import(moved);
        FsdVersions.Promote(repo.Repo, "shop", "Pat");
        Assert.Equal(["T-0001", "T-0003"], Doc()["tables"]!.AsArray().Select(t => (string)t!["id"]!));
    }

    [Fact]
    public void A_document_from_before_gets_its_ids_on_its_next_import_and_only_the_record_changes()
    {
        Import();
        var doc = Doc();
        doc.Remove("tables");
        File.WriteAllText(Path.Combine(Dir, "document.json"), CanonicalJson.Serialize(doc));
        var md = File.ReadAllBytes(Path.Combine(Dir, "document.md"));
        Assert.Equal("unchanged", (string)Import()["status"]!);
        Assert.StartsWith("T-", (string)Assert.Single(Doc()["tables"]!.AsArray())!["id"]!);
        Assert.Equal(md, File.ReadAllBytes(Path.Combine(Dir, "document.md")));
    }

    // ---- A.9: first-import approval ----

    TempRepo Approving(TempRepo archiveRepo) => new($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archiveRepo.Root}', git: false }} }}\ncomponents: {{ first-import-approval: true }}\n");

    [Fact]
    public void First_import_approval_is_off_by_default_and_listed_as_a_component()
    {
        Assert.False(StangentComponents.Find("first-import-approval").OnByDefault);
        Assert.False(StangentComponents.IsOn(repo.Repo.Config, "first-import-approval"));
        Assert.Equal("imported", (string)Import()["status"]!);
    }

    [Fact]
    public void With_approval_on_a_first_import_waits_where_nothing_reads_it_until_a_person_approves()
    {
        using var on = Approving(archive);
        var file = Path.Combine(on.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        var waiting = FsdImport.Import(on.Repo, file);
        Assert.Equal(("pending-approval", "stangent fsd approve shop"), ((string)waiting["status"]!, (string)waiting["next"]!));
        Assert.Empty(FsdImport.Documents(on.Root));
        Assert.False(File.Exists(Path.Combine(FsdImport.DocDir(on.Root, "shop"), "document.json")));
        Assert.Equal("pending-approval", (string)FsdImport.Import(on.Repo, file)["status"]!);
        Assert.Equal("needs_approval", Code(() => FsdVersions.Promote(on.Repo, "shop", "Pat")));

        var approved = FsdVersions.Approve(on.Root, "shop", "Pat");
        Assert.Equal(("imported", "Pat"), ((string)approved["status"]!, (string)approved["approvedBy"]!));
        var doc = Assert.Single(FsdImport.Documents(on.Root));
        Assert.Equal("S-0001", (string)doc["sections"]![0]!["permanentId"]!);
        Assert.False(Directory.Exists(FsdVersions.CandidateDir(on.Root, "shop")));
        var moves = StateChanges.History(on.Root, StateKinds.FsdCandidate, $"shop@{((string)doc["original"]!["hash"]!)[..12]}");
        Assert.Equal([("pending-approval", "code"), ("approved", "person")], moves.Select(m => (m.To, m.Who.Kind)));

        Assert.Equal("no_candidate", Code(() => FsdVersions.Approve(on.Root, "shop", "Pat")));
        File.WriteAllBytes(file, DocxWriter.Write(Outline + "\n# Herbs\n\nMint is cut to order every single morning.\n"));
        Assert.Equal("candidate", (string)FsdImport.Import(on.Repo, file)["status"]!);
        Assert.Equal("not_pending_approval", Code(() => FsdVersions.Approve(on.Root, "shop", "Pat")));
    }

    [Fact]
    public void A_waiting_first_import_can_be_discarded_or_replaced_by_another_file()
    {
        using var on = Approving(archive);
        var file = Path.Combine(on.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(on.Repo, file);
        File.WriteAllBytes(file, DocxWriter.Write(Outline + "\n# Herbs\n\nMint is cut to order every single morning.\n"));
        Assert.Equal("pending-approval", (string)FsdImport.Import(on.Repo, file)["status"]!);
        Assert.Contains("Herbs", File.ReadAllText(Path.Combine(FsdVersions.CandidateDir(on.Root, "shop"), "document.md")));
        Assert.Contains("first import", FsdSectionDiff.Markdown(FsdVersions.Diff(on.Root, "shop")));
        FsdVersions.Discard(on.Root, "shop", "Pat");
        Assert.Empty(FsdImport.Documents(on.Root));
        Assert.Equal("no_candidate", Code(() => FsdVersions.Diff(on.Root, "shop")));
    }

    [Fact]
    public void Status_shows_a_first_import_waiting_with_no_current_version()
    {
        using var on = Approving(archive);
        var file = Path.Combine(on.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(on.Repo, file);
        var status = FsdStatus.Of(on.Root, "shop");
        Assert.Null(status["current"]);
        Assert.True((bool)status["candidate"]!["first"]!);
        Assert.Equal("pending-approval", (string)status["candidate"]!["state"]!);
        Assert.Equal("stangent fsd approve shop", (string)status["next"]!["command"]!);
    }

    [Fact]
    public void Status_names_a_doubtful_section_of_the_candidate()
    {
        Import("# Fruit\n\nApples are sorted by size before they are packed.\n");
        Import("# Produce\n\nApples are sorted by size before they are packed.\n\nMore words were added to this section here.\n");
        var status = FsdStatus.Of(Root, "shop");
        Assert.Equal(["Produce", "S-0001"], status["doubtful"]!.AsArray().Select(d => (string)d!));
        Assert.Equal("stangent fsd resolve shop Produce --same-as <S-id>", (string)status["next"]!["command"]!);
    }
}
