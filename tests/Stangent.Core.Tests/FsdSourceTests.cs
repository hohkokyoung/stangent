using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>Sections read from the committed chapters (phase fs layer 2, D380, D383, D389 call 1, D392): an uncommitted edit is
/// never read, the cache follows the commit, <c>document.json</c> is v4, and the reader's refusals. No AI.</summary>
public sealed class FsdSourceTests : IDisposable
{
    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public FsdSourceTests() => repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive.Root}', git: false }} }}\n");

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    string Dir => FsdImport.DocDir(Root, "shop");

    const string Outline = """
        # 1 Orders
        Orders are placed from the cart.
        ## 1.1 Refunds
        Refunds take five days.
        # 2 Glossary
        Terms.
        """;

    void Imported(bool commit = true)
    {
        Assert.NotNull(Git.Run(Root, "init -q"));
        Committed.All(Root, "the repo before the import");
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(repo.Repo, file);
        if (commit) Committed.All(Root, "import shop");
    }

    static string Text(JsonObject document, string number) =>
        (string)document["sections"]!.AsArray().OfType<JsonObject>().Single(s => (string)s["id"]! == number)["text"]!;

    JsonObject Facts() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "document.json")))!.AsObject();

    [Fact]
    public void An_uncommitted_edit_changes_nothing_a_step_reads_and_committing_it_does()
    {
        Imported();
        Chapters.Edit(Dir, "Refunds take five days.", "Refunds take ten days.");

        Assert.Equal("Refunds take five days.", Text(FsdDocument.Load(Root, "shop"), "1.1"));
        Assert.Equal("Refunds take five days.", (string)FsdOutline.Show(Root, "shop", "1.1")["text"]!);
        Assert.Equal("Refunds take five days.", Text(Assert.Single(FsdImport.Documents(Root)), "1.1"));
        var exported = (string)FsdExport.Run(Root, "shop", null, 1500, whole: true)["files"]!.AsArray().Single()!["file"]!;
        Assert.DoesNotContain("ten days", File.ReadAllText(Path.Combine(FsdExport.DefaultDir(Root, "shop"), exported)), StringComparison.Ordinal);
        var outline = FsdOutline.Outline(Root, "shop");
        Assert.True((bool)outline["uncommitted"]!);
        Assert.True((bool)FsdStatus.Of(Root, "shop")["uncommitted"]!);

        Committed.All(Root, "the PM's edit");
        Assert.Equal("Refunds take ten days.", Text(FsdDocument.Load(Root, "shop"), "1.1"));
        Assert.Equal("Refunds take ten days.", (string)FsdOutline.Show(Root, "shop", "1.1")["text"]!);
        Assert.Null(FsdOutline.Outline(Root, "shop")["uncommitted"]);
    }

    [Fact]
    public void The_cache_follows_a_commit_and_a_broken_one_is_read_again()
    {
        Imported();
        // The memory cache is per process and keyed by tree hash: another test with the same chapters may have filled it,
        // and then this repo's file is never written. Start each load from nothing, as a new command does.
        FsdDocument.Forget();
        FsdDocument.Load(Root, "shop");
        var cache = Path.Combine(Root, ".stangent", "fsd", "shop", "chapters-cache.json");
        var tree = (string)JsonNode.Parse(File.ReadAllText(cache))!["tree"]!;
        Assert.Equal(Git.Run(Root, "rev-parse HEAD:./specs/fsd/shop/chapters")!.Trim(), tree);

        Chapters.Edit(Dir, "Terms.", "Words and what they mean.");
        Committed.All(Root, "edit");
        FsdDocument.Forget();
        Assert.Equal("Words and what they mean.", Text(FsdDocument.Load(Root, "shop"), "2"));
        Assert.NotEqual(tree, (string)JsonNode.Parse(File.ReadAllText(cache))!["tree"]!);

        // A commit that doesn't touch the chapters keeps the same tree, so the same cache.
        var before = File.ReadAllText(cache);
        File.WriteAllText(Path.Combine(Root, "notes.txt"), "unrelated");
        Committed.All(Root, "unrelated");
        FsdDocument.Load(Root, "shop");
        Assert.Equal(before, File.ReadAllText(cache));

        // A new command starts with nothing in memory, so the file on disk is what's read: a broken or wrong one is read again from git.
        foreach (var broken in new[] { "{ not json", $"{{\"tree\": \"{Git.Run(Root, "rev-parse HEAD:./specs/fsd/shop/chapters")!.Trim()}\"}}", "{\"tree\": \"0000\", \"files\": []}" })
        {
            File.WriteAllText(cache, broken);
            FsdDocument.Forget();
            Assert.Equal("Words and what they mean.", Text(FsdDocument.Load(Root, "shop"), "2"));
        }
    }

    [Fact]
    public void Document_json_is_v4_with_no_sections_and_the_import_commit()
    {
        Assert.NotNull(Git.Run(Root, "init -q"));
        Committed.All(Root, "before");
        var head = Git.Run(Root, "rev-parse HEAD")!.Trim();
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        var result = FsdImport.Import(repo.Repo, file);
        Assert.Contains("commit", (string)result["next"]!, StringComparison.Ordinal);

        var facts = Facts();
        Assert.Equal(4, (int)facts["schemaVersion"]!);
        Assert.Null(facts["sections"]);
        Assert.Null(facts["markdown"]);
        Assert.Equal(head, (string)facts["importCommit"]!);
        Assert.Empty(SchemaRegistry.Embedded.Validate("source-document.v4.json", facts.ToJsonString()));

        // A v3 record (sections and markdown) breaks v4.
        var old = facts.DeepClone().AsObject();
        old["sections"] = new JsonArray();
        old["markdown"] = "document.md";
        Assert.NotEmpty(SchemaRegistry.Embedded.Validate("source-document.v4.json", old.ToJsonString()));
        var v3 = facts.DeepClone().AsObject();
        v3["schemaVersion"] = 4;
        v3.Remove("title");
        Assert.NotEmpty(SchemaRegistry.Embedded.Validate("source-document.v4.json", v3.ToJsonString()));
    }

    [Fact]
    public void An_uncommitted_edit_to_document_json_is_not_read_and_is_named()
    {
        Imported();
        var path = Path.Combine(Dir, "document.json");
        var facts = Facts();
        facts["title"] = "Edited by hand";
        File.WriteAllText(path, CanonicalJson.Serialize(facts));
        Assert.NotEqual("Edited by hand", (string)FsdDocument.Load(Root, "shop")["title"]!);
        Assert.True((bool)FsdOutline.Outline(Root, "shop")["uncommitted"]!);
        Committed.All(Root, "the title");
        Assert.Equal("Edited by hand", (string)FsdDocument.Load(Root, "shop")["title"]!);
    }

    [Fact]
    public void A_removed_id_typed_back_onto_a_heading_is_refused()
    {
        Imported();
        var ledger = FsdVersions.Ledger(Root, "shop");
        var glossary = Directory.EnumerateFiles(FsdChapters.Dir(Dir), "02-*.md").Single();
        var own = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(glossary), @"<!-- (S-\d+) -->").Groups[1].Value;
        ledger.Remove("sections", own);
        Committed.All(Root, "the ledger says it's gone, the chapter still has it");
        var refused = Assert.Throws<StangentException>(() => FsdDocument.Load(Root, "shop"));
        Assert.Equal("invalid_fsd", refused.Code);
        Assert.Contains("removed", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_edited_away_is_listed_not_read()
    {
        Assert.NotNull(Git.Run(Root, "init -q"));
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# 1 Orders\n\n| Status | Code |\n| Open | 0 |\n\n# 2 Glossary\n\nTerms.\n"));
        FsdImport.Import(repo.Repo, file);
        Committed.All(Root, "import");
        var id = (string)Assert.Single(FsdTables.Listing(Root, "shop")["tables"]!.AsArray())!["id"]!;
        Chapters.Edit(Dir, "| Open | 0 |", "Open is 0.");
        Chapters.Edit(Dir, "| Status | Code |", "Statuses:");
        Committed.All(Root, "the table becomes words");
        var listing = FsdTables.Listing(Root, "shop");
        Assert.Empty(listing["tables"]!.AsArray());
        Assert.Equal(id, (string)Assert.Single(listing["tablesNotFound"]!.AsArray())!["id"]!);
    }

    [Fact]
    public void An_import_not_committed_is_read_by_no_step_and_status_says_commit()
    {
        Imported(commit: false);
        Assert.Equal("fsd_not_committed", Assert.Throws<StangentException>(() => FsdDocument.Load(Root, "shop")).Code);
        Assert.Empty(FsdImport.Documents(Root));
        var status = FsdStatus.Of(Root, "shop");
        Assert.Equal("fsd_not_committed", (string)status["unread"]!["code"]!);
        Assert.StartsWith("git add specs/fsd/shop", (string)status["next"]!["command"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_git_the_fsd_cant_be_read()
    {
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(repo.Repo, file);
        Assert.Equal("not_git", Assert.Throws<StangentException>(() => FsdOutline.Load(Root, "shop")).Code);
    }

    [Fact]
    public void A_heading_committed_with_no_marker_reads_with_no_id()
    {
        Imported();
        File.AppendAllText(Directory.EnumerateFiles(FsdChapters.Dir(Dir), "02-*.md").Single(), "\n## 2.1 Added by hand\nNew words.");
        Committed.All(Root, "a heading with no marker");
        var added = FsdDocument.Load(Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().Single(s => (string)s["id"]! == "2.1");
        Assert.Null(added["permanentId"]);
        Assert.Equal("New words.", (string)added["text"]!);
    }

    [Theory]
    [InlineData("repeated")]
    [InlineData("never-given")]
    public void A_repeated_id_or_one_the_ledger_never_gave_is_refused(string kind)
    {
        Imported();
        var glossary = Directory.EnumerateFiles(FsdChapters.Dir(Dir), "02-*.md").Single();
        var text = File.ReadAllText(glossary);
        var own = System.Text.RegularExpressions.Regex.Match(text, @"<!-- (S-\d+) -->").Groups[1].Value;
        var other = kind == "repeated" ? FsdDocument.Load(Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().Select(s => (string)s["permanentId"]!).First(id => id != own) : "S-9999";
        File.WriteAllText(glossary, text.Replace($"<!-- {own} -->", $"<!-- {other} -->", StringComparison.Ordinal));
        Committed.All(Root, "a wrong id");
        var refused = Assert.Throws<StangentException>(() => FsdDocument.Load(Root, "shop"));
        Assert.Equal("invalid_fsd", refused.Code);
        Assert.Contains(other, refused.Message, StringComparison.Ordinal);
        Assert.Empty(FsdImport.Documents(Root));
    }

    [Fact]
    public void A_document_not_migrated_is_refused_with_the_migrate_command_and_migrate_drops_its_sections()
    {
        Imported();
        // As layer 1 left it: chapter files, and the sections still in document.json.
        var facts = Facts();
        var read = FsdChapters.Read(FsdChapters.Files(Dir));
        facts["schemaVersion"] = 3;
        facts["markdown"] = "document.md";
        facts["sections"] = new JsonArray([.. read.Sections.Select(s => (JsonNode)FsdChapters.Json(s))]);
        facts.Remove("importCommit");
        File.WriteAllText(Path.Combine(Dir, "document.json"), CanonicalJson.Serialize(facts));
        Committed.All(Root, "a layer 1 document");

        var refused = Assert.Throws<StangentException>(() => FsdDocument.Load(Root, "shop"));
        Assert.Equal("fsd_not_migrated", refused.Code);
        Assert.Contains("stangent fsd migrate shop", refused.Message, StringComparison.Ordinal);

        var chapters = Chapters.Files(Dir);
        Assert.Equal("migrated", (string)FsdMigrate.Run(repo.Repo, "shop")["status"]!);
        Assert.Equal(chapters, Chapters.Files(Dir));
        Assert.Null(Facts()["sections"]);
        Assert.Equal(4, (int)Facts()["schemaVersion"]!);
        Committed.All(Root, "migrate");
        Assert.Equal("Refunds take five days.", Text(FsdDocument.Load(Root, "shop"), "1.1"));
        Assert.Equal("unchanged", (string)FsdMigrate.Run(repo.Repo, "shop")["status"]!);
    }
}
