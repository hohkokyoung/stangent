using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Permanent section ids and the section matcher (D117). The comparison and `fsd take` built on it are FsdCompareTests (D385). No AI.</summary>
public sealed class FsdVersionTests : IDisposable
{
    const string Pat = "Pat Example";

    const string Apples = "Apples are sorted by size before they are packed.";
    const string Pears = "Pears wait three days in the cold room first.";
    const string Plums = "Plums ship the same day they are picked.";
    const string Figs = "Figs are only sold to trade customers on request.";
    const string Dates = "Dates come in boxes of twelve from the supplier.";

    readonly TempRepo archive = new();
    readonly TempRepo repo;
    readonly Dictionary<string, byte[]> files = [];

    public FsdVersionTests() => repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;

    /// <summary>Imports an outline as a Word file. The same outline is the same file, so importing it again is the same bytes.</summary>
    JsonObject Import(string outline, string id = "shop")
    {
        var file = Path.Combine(Root, $"{id}.docx");
        if (!files.TryGetValue(outline, out var bytes)) files[outline] = bytes = DocxWriter.Write(outline);
        File.WriteAllBytes(file, bytes);
        var imported = FsdImport.Import(repo.Repo, file);
        File.Delete(file);
        Committed.All(repo.Repo);
        return imported;
    }

    /// <summary>The current version as steps read it: the committed chapters' sections.</summary>
    JsonObject Current() => FsdDocument.Load(Root, "shop");

    string Id(string section) => (string)Current()["sections"]!.AsArray().First(s => (string)s!["id"]! == section)!["permanentId"]!;

    List<JsonObject> last = [];

    List<JsonObject> Entries() => last;

    /// <summary>One entry as a line, so a whole diff reads as a list.</summary>
    static string Show(JsonObject e) => (string)e["kind"]! switch
    {
        "doubtful" => $"doubtful {(string?)e["to"] ?? (string)e["from"]!}: {string.Join(",", e["candidates"]!.AsArray().Select(c => (string?)c!["to"] ?? (string)c!["from"]!))}",
        "added" => $"added {e["to"]}",
        "removed" => $"removed {e["from"]}",
        var k => $"{k} {e["from"]}>{e["to"]}",
    };

    /// <summary>The matcher on its own: the committed FSD against a second file's sections, as a first import reads them.</summary>
    List<string> Diff(string first, string second)
    {
        Import(first);
        Import(second, "next");
        var next = new JsonArray([.. FsdDocument.Load(Root, "next")["sections"]!.AsArray().OfType<JsonObject>().Select(s => { var c = s.DeepClone().AsObject(); c.Remove("permanentId"); return (JsonNode)c; })]);
        last = FsdSectionDiff.Compare(new JsonArray([.. Current()["sections"]!.AsArray().Select(x => x!.DeepClone())]), next);
        return [.. Entries().Select(Show)];
    }

    // ---- Permanent ids ----

    [Fact]
    public void A_first_import_goes_straight_in_with_an_id_per_section_beside_the_number()
    {
        var result = Import($"#! Fruit\n{Apples}\n##! Pears\n{Pears}\n");

        Assert.Equal("imported", (string)result["status"]!);
        Assert.Equal(4, (int)Current()["schemaVersion"]!);
        Assert.Equal([("1", "S-0001"), ("1.1", "S-0002")], Current()["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["permanentId"]!)));
        Assert.Equal(["S-0001", "S-0002"], FsdVersions.Ledger(Root, "shop").Active("sections"));
    }

    [Fact]
    public void A_document_from_before_permanent_ids_is_refused_until_it_is_migrated()
    {
        Import($"#! Fruit\n{Apples}\n");
        var path = Path.Combine(FsdImport.DocDir(Root, "shop"), "document.json");
        File.WriteAllText(path, CanonicalJson.Serialize(AsItWasWritten(Current())));
        File.Delete(Path.Combine(FsdImport.DocDir(Root, "shop"), "ids.json"));

        // Since phase fs layer 2 a document from before is moved by `fsd migrate`, not by the next import (D392).
        Assert.Equal("fsd_not_migrated", Assert.Throws<StangentException>(() => Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n")).Code);
    }

    [Fact]
    public void Who_decides_comes_from_git()
    {
        Git.Run(Root, "init -q");
        Git.Run(Root, "config user.name \"Git Person\"");
        Assert.Equal("Git Person", FsdVersions.GitPerson(Root));
    }

    // ---- The rules ----

    [Fact]
    public void Same_body_and_path_is_unchanged_even_when_a_new_chapter_changes_the_number() =>
        Assert.Equal(["added 1", "unchanged 1>2"], Diff($"#! Fruit\n{Apples}\n", $"#! New\n{Dates}\n#! Fruit\n{Apples}\n"));

    [Fact]
    public void Same_body_under_another_parent_is_moved() =>
        Assert.Equal(["unchanged 1>1", "unchanged 2>2", "moved 1.1>2.1"], Order(Diff($"#! A\n{Apples}\n##! Kept\n{Pears}\n#! B\n{Plums}\n", $"#! A\n{Apples}\n#! B\n{Plums}\n##! Kept\n{Pears}\n")));

    static List<string> Order(List<string> lines) => [.. lines.OrderBy(l => l.StartsWith("moved", StringComparison.Ordinal))];

    [Fact]
    public void Same_body_with_a_new_title_in_the_same_place_is_renamed() =>
        Assert.Equal(["renamed 1>1"], Diff($"#! Fruit\n{Apples}\n", $"#! Produce\n{Apples}\n"));

    [Fact]
    public void Same_path_with_another_body_is_modified() =>
        Assert.Equal(["modified 1>1"], Diff($"#! Fruit\n{Apples}\n", $"#! Fruit\n{Pears}\n"));

    [Fact]
    public void No_shared_sentence_and_no_exact_signal_is_added_and_removed() =>
        Assert.Equal(["added 1", "removed 1"], Diff($"#! Fruit\n{Apples}\n", $"#! Veg\n{Pears}\n"));

    [Fact]
    public void Similarity_only_orders_suggestions_and_never_settles_a_match()
    {
        var entries = FsdSectionDiff.Compare(
            new JsonArray(Sec("1", "Fruit", $"{Apples} {Pears} Same words here."), Sec("2", "Other", "Quite different words in here.")),
            new JsonArray(Sec("1", "Red", $"{Apples} More words here.")));
        var open = entries.First(e => (string)e["kind"]! == "doubtful" && (string?)e["side"] == "candidate");
        var ranked = open["suggestions"]!.AsArray().Select(s => (string)s!["from"]!).ToList();
        Assert.Equal(["1", "2"], ranked);
        Assert.True((double)open["suggestions"]![0]!["similarity"]! > (double)open["suggestions"]![1]!["similarity"]!);
        Assert.Equal("doubtful", (string)open["kind"]!);
        Assert.DoesNotContain(entries, e => (string)e["kind"]! is "modified" or "unchanged");
    }

    static JsonObject Sec(string id, string title, string text) => new() { ["id"] = id, ["title"] = title, ["text"] = text, ["permanentId"] = $"S-{id}", ["level"] = 1, ["textHash"] = id };

    [Fact]
    public void A_split_is_doubtful_on_both_sides_with_its_candidates() =>
        Assert.Equal(["doubtful 1: 1", "doubtful 2: 1", "doubtful 1: 1,2"], Diff($"#! Fruit\n{Apples} {Pears}\n", $"#! Red\n{Apples}\n#! Green\n{Pears}\n"));

    [Fact]
    public void A_merge_is_doubtful_on_both_sides_with_its_candidates() =>
        Assert.Equal(["doubtful 1: 1,2", "doubtful 1: 1", "doubtful 2: 1"], Diff($"#! Red\n{Apples}\n#! Green\n{Pears}\n", $"#! Fruit\n{Apples} {Pears}\n"));

    [Fact]
    public void A_split_that_keeps_its_heading_still_flags_the_new_half()
    {
        Assert.Equal(["modified 1>1", "doubtful 2: 1"], Diff($"#! Fruit\n{Apples} {Pears}\n", $"#! Fruit\n{Apples}\n#! More\n{Pears}\n"));
        Assert.True((bool)Entries()[1]["candidates"]![0]!["alreadyMatched"]!);
    }

    [Fact]
    public void Duplicate_bodies_cant_match_on_their_body()
    {
        // Two sections with the same text, each under a new heading: picking a pair would be a guess.
        var lines = Diff($"#! One\n{Apples}\n#! Two\n{Apples}\n", $"#! Three\n{Apples}\n#! Four\n{Apples}\n");
        Assert.Equal(["doubtful 1: 1,2", "doubtful 2: 1,2", "doubtful 1: 1,2", "doubtful 2: 1,2"], lines);
    }

    [Fact]
    public void A_duplicate_body_still_matches_where_its_path_says_so() =>
        Assert.Equal(["unchanged 1>1", "unchanged 2>2", "added 3"], Diff($"#! One\n{Apples}\n#! Two\n{Apples}\n", $"#! One\n{Apples}\n#! Two\n{Apples}\n#! Three\n{Dates}\n"));

    [Fact]
    public void Empty_sections_cant_match_on_their_body()
    {
        // Both chapters are headings with nothing under them, so an empty body says nothing.
        Assert.Equal(["added 1", "unchanged 2>2", "removed 1"], Diff($"#! Empty\n#! Fruit\n{Apples}\n", $"#! Blank\n#! Fruit\n{Apples}\n"));
    }

    [Fact]
    public void An_empty_section_with_the_same_path_is_unchanged() =>
        Assert.Equal(["unchanged 1>1", "unchanged 2>2", "added 3"], Diff($"#! Empty\n#! Fruit\n{Apples}\n", $"#! Empty\n#! Fruit\n{Apples}\n#! Veg\n{Dates}\n"));

    [Fact]
    public void A_short_sentence_is_not_a_shared_sentence()
    {
        Assert.Equal(["added 1", "removed 1"], Diff("#! Fruit\nSee table below.\n", "#! Veg\nSee table below.\nAnd more words follow it here.\n"));
        Assert.Empty(FsdSectionDiff.SharedSentences("Not applicable. See above. Yes."));
        Assert.Single(FsdSectionDiff.SharedSentences("See the table below now. See above."));
    }

    [Fact]
    public void Whitespace_doesnt_stop_a_sentence_being_shared_and_a_table_row_counts_as_one() =>
        Assert.Equal(["Plums and pears | ship the same day"], FsdSectionDiff.SharedSentences("| Plums   and pears | ship  the  same\tday |"));

    [Fact]
    public void A_duplicate_heading_with_a_shared_sentence_lists_that_sentence_once()
    {
        Diff($"#! Notes\n{Apples}\n#! Notes\n{Pears}\n", $"#! Notes\n{Apples} {Plums}\n#! Notes\n{Pears} {Figs}\n");
        var entries = Entries().Where(e => (string?)e["side"] == "candidate").ToList();
        Assert.Equal(2, entries.Count);
        // Each holds the note it shares a sentence with first, then the other under the same heading, each once.
        Assert.Equal([("1", Apples), ("2", null)], entries[0]["candidates"]!.AsArray().Select(c => ((string)c!["from"]!, (string?)c["sentence"])));
    }

    // ---- The hard bar: saved paid work stays valid ----

    static string SonarrDir()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "benchmark", "fsd", "sonarr", "sonarr-fsd.docx"))) root = Directory.GetParent(root)!.FullName;
        return Path.Combine(root, "benchmark", "fsd", "sonarr");
    }

    static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>What the code before permanent ids wrote for this file: its hashes were taken from that build (6b3c9d4).</summary>
    static JsonObject AsItWasWritten(JsonObject doc)
    {
        var copy = (JsonObject)doc.DeepClone();
        copy["schemaVersion"] = 1;
        // v4 keeps no Markdown's name and adds the import commit (phase fs layer 2); that build wrote the one and not the other.
        copy["markdown"] = "document.md";
        copy.Remove("importCommit");
        // Phase fs layer 5 (D385) adds what the file gave each section, and the converter.
        copy.Remove("fileSections");
        copy.Remove("converter");
        foreach (var s in copy["sections"]!.AsArray().OfType<JsonObject>()) { s.Remove("permanentId"); s.Remove("contentHash"); }
        copy.Remove("tables");
        foreach (var i in copy["images"]!.AsArray().OfType<JsonObject>()) { i.Remove("id"); i.Remove("section"); i.Remove("sectionNumber"); }
        return copy;
    }

    [Fact]
    public void An_unchanged_document_keeps_its_bytes_and_every_hash_saved_for_it()
    {
        var file = Path.Combine(SonarrDir(), "sonarr-fsd.docx");
        Assert.Equal("imported", (string)FsdImport.Import(repo.Repo, file, "sonarr")["status"]!);
        var dir = FsdImport.DocDir(Root, "sonarr");
        var jsonPath = Path.Combine(dir, "document.json");
        Committed.All(repo.Repo);
        var imported = FsdDocument.Load(Root, "sonarr");

        // The same bytes as before this work: document.md whole, and document.json but for the added ids.
        Assert.Equal("b546bb24c70f6901327bdc6298db22e136aec9ca7ff2c3ae76cb39e7d5254f50", Sha(Chapters.Bytes(dir)));
        var written = CanonicalJson.Serialize(AsItWasWritten(imported));
        Assert.Equal("c18cbded20fc31bbdcac45e8e71671f29efcac855516862246905a43b9f6a621", Sha(System.Text.Encoding.UTF8.GetBytes(written)));

        // A document imported earlier, with links: document.md and the sections in document.json.
        File.WriteAllText(jsonPath, written);
        File.WriteAllText(Path.Combine(dir, "document.md"), Chapters.Text(dir));
        Directory.Delete(FsdChapters.Dir(dir), recursive: true);
        var v1 = JsonNode.Parse(written)!.AsObject();
        var withText = v1["sections"]!.AsArray().OfType<JsonObject>().Where(s => Sentences.Of((string)s["text"]!).Count > 0).ToList();
        var linked = withText.ToDictionary(s => (string)s["id"]!, s => TraceFile.EntryId("gap", "sonarr", (string)s["id"]!, null, (string)s["text"]!, null));
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add(TraceStore.Entry("gap", "sonarr", withText[0], Sentences.Of((string)withText[0]["text"]!)[0].Text, null, null, null, "code", null, null));
        TraceFile.Save(Root, trace);
        var md = Chapters.Bytes(dir);
        Committed.All(repo.Repo);
        var saved = new[] { TraceFile.PathIn(Root) }.ToDictionary(p => p, File.ReadAllBytes);

        // Since phase fs layer 2 (D392) its next import is refused, and `fsd migrate` moves it: the same text, and its ids.
        Assert.Equal("fsd_not_migrated", Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, file, "sonarr")).Code);
        var again = FsdMigrate.Run(repo.Repo, "sonarr");
        Assert.Equal(v1["sections"]!.AsArray().Count, (int)again["permanentIdsAdded"]!);
        Committed.All(repo.Repo);
        // The same file again is compared with what's committed (D385): nothing differs, and nothing changes.
        var compared = FsdImport.Import(repo.Repo, file, "sonarr");
        Assert.Equal("compared", (string)compared["status"]!);
        Assert.Equal(v1["sections"]!.AsArray().Count, (int)compared["counts"]!["same"]!);
        Assert.Equal(md, Chapters.Bytes(dir));

        var upgraded = FsdDocument.Load(Root, "sonarr");
        Assert.Equal(4, (int)upgraded["schemaVersion"]!);
        Assert.All(upgraded["sections"]!.AsArray(), s => Assert.Matches("^S-[0-9]{4}$", (string)s!["permanentId"]!));
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, FsdDocument.Facts(Root, "sonarr")!.ToJsonString()));
        // Taking the ids and the import commit off gives back the exact bytes: the ids are the only change to what was saved.
        upgraded.Remove("importCommit");
        Assert.Equal(written, CanonicalJson.Serialize(AsItWasWritten(upgraded)));
        foreach (var (path, bytes) in saved) Assert.Equal(bytes, File.ReadAllBytes(path));

        // Every link id worked out again from the upgraded file is the one saved.
        foreach (var s in upgraded["sections"]!.AsArray().OfType<JsonObject>().Where(s => linked.ContainsKey((string)s["id"]!)))
            Assert.Equal(linked[(string)s["id"]!], TraceFile.EntryId("gap", "sonarr", (string)s["id"]!, null, (string)s["text"]!, null));
        Assert.True((bool)TraceCheck.Run(Root, null, null, fix: false)["ok"]!);
    }

    // ---- The pinned pair, and the planted measure ----

    const string PairBefore = $"""
        #! Orders
        {Apples}
        ##! Placing
        {Pears}
        ##! Cancelling
        {Plums} {Figs}
        #! Delivery
        {Dates}
        ##! Tracking
        Tracking numbers are sent by email once a parcel leaves.
        ##! Returns
        Returns are accepted for thirty days after delivery.
        #! Old notes
        These notes describe a process nobody uses any longer.
        """;

    const string PairAfter = $"""
        #! Accounts
        Every customer signs in with an email address.
        #! Orders
        {Apples}
        ##! Placing
        {Pears} A second sentence was added to this rule here.
        ##! Stopping
        {Plums}
        ##! Trade only
        {Figs}
        #! Shipping
        {Dates}
        ##! Tracking
        Tracking numbers are sent by email once a parcel leaves.
        #! Refunds
        ##! Returns
        Returns are accepted for thirty days after delivery.
        """;

    [Fact]
    public void The_reviewed_pair_stays_as_reviewed()
    {
        Diff(PairBefore, PairAfter);

        // Reviewed by hand. Delivery's words stay under a new title: renamed. Tracking and Returns have new parents: moved.
        // Cancelling split in two, neither half keeping its name: doubtful. Old notes shares nothing: removed.
        Assert.Equal(
        [
            "added 1", "unchanged 1>2", "modified 1.1>2.1", "doubtful 2.2: 1.2", "doubtful 2.3: 1.2", "renamed 2>3", "moved 2.1>3.1", "added 4", "moved 2.2>4.1", "doubtful 1.2: 2.2,2.3", "removed 3",
        ], Entries().Select(Show));

        // The same pair through the comparison (D385): its doubtful sections say how to settle them, with the sentence shared.
        Assert.Equal("compared", (string)Import(PairAfter)["status"]!);
        var markdown = File.ReadAllText(Path.Combine(FsdCompare.Dir(Root, "shop"), "comparison.md"));
        Assert.Contains("stangent fsd resolve shop \"2.2\" --same-as <S-id>", markdown, StringComparison.Ordinal);
        Assert.Contains($"\"{Plums}\"", markdown, StringComparison.Ordinal);
    }

    /// <summary>A planted edit, not a real version change (D56): a chapter inserted, a section moved, one reworded, one split.</summary>
    [Fact]
    public void Sonarrs_planted_edit_finds_what_was_planted()
    {
        var dir = SonarrDir();
        var outline = File.ReadAllText(Path.Combine(dir, "sonarr-fsd.outline.md"));
        FsdImport.Import(repo.Repo, Path.Combine(dir, "sonarr-fsd.docx"), "sonarr");
        Committed.All(repo.Repo);
        var before = FsdDocument.Load(Root, "sonarr")["sections"]!.AsArray().Count;

        string Sub(string text, string from, string to)
        {
            Assert.Contains(from, text, StringComparison.Ordinal);
            return text.Replace(from, to, StringComparison.Ordinal);
        }

        // Insert a chapter first: the numbered chapters after it shift, and the typed ones are retyped, as an editor would.
        var edited = Sub(outline, "#! Library\n", "#! Getting started\nThis new chapter explains how an administrator signs in for the first time.\n#! Library\n");
        foreach (var (from, to) in new[] { ("# 3 Activity", "# 4 Activity"), ("## 3.1 Queue", "## 4.1 Queue"), ("## 3.2 History", "## 4.2 History") }) edited = Sub(edited, from + "\n", to + "\n");
        // Move a section: Backups goes from System to Settings.
        var lines = edited.Split('\n').ToList();
        var backup = lines.FindIndex(l => l == "## Backups");
        var end = lines.FindIndex(backup + 1, l => l.StartsWith('#'));
        if (end < 0) end = lines.Count;
        var moved = lines.GetRange(backup, end - backup).Where(l => l.Length > 0).ToList();
        lines.RemoveRange(backup, end - backup);
        lines.InsertRange(lines.FindIndex(l => l == "## Tags"), moved);
        // Reword one: a sentence of Calendar.
        edited = string.Join('\n', lines);
        edited = Sub(edited, "Episodes can be dragged to another day to change their air date.", "Dragging an episode to a different day changes when it airs.");
        // Split one: Queue into two sections, neither keeping its name.
        var queue = edited.Split('\n').ToList();
        var at = queue.FindIndex(l => l == "## 4.1 Queue");
        var stop = queue.FindIndex(at + 1, l => l.StartsWith('#'));
        var body = queue.GetRange(at + 1, stop - at - 1);
        Assert.True(body.Count >= 2);
        queue[at] = "## 4.1 Queue items";
        queue.Insert(at + 1 + (body.Count / 2), "## 4.2 Queue actions");
        edited = Sub(string.Join('\n', queue), "## 4.2 History\n", "## 4.3 History\n");

        var file = Path.Combine(Root, "sonarr-next.docx");
        File.WriteAllBytes(file, DocxWriter.Write(edited, dir));
        var result = FsdImport.Import(repo.Repo, file, "sonarr");
        Committed.All(repo.Repo);

        var counts = result["counts"]!;
        // Planted, compared against the file it was imported from (D385): the chapter added is new; the reworded section and the
        // renumbered ones (their headings changed) are changed only in the file; the split's old section and its two halves are
        // doubtful; the moved one reads the same, its move listed; nothing was changed in the repo or is gone.
        Assert.Equal("compared", (string)result["status"]!);
        Assert.Equal((1, 6, 3, 0, 0, 0, before - 7), ((int)counts["new"]!, (int)counts["file-changed"]!, (int)counts["doubtful"]!, (int)counts["repo-changed"]!, (int)counts["both-changed"]!, (int)counts["gone"]!, (int)counts["same"]!));
        Assert.Single(FsdCompare.Show(Root, "sonarr")["sections"]!.AsArray(), e => e!["fileMovedIt"] is not null && (string)e["case"]! == "same");
    }
}

/// <summary>I.5: a split as its own outcome, on the diff alone.</summary>
public sealed class SectionSplitTests
{
    static JsonArray Sections(params (string Id, string Perm, string Title, string Text)[] list) => new([.. list.Select(s => (JsonNode)new JsonObject
    {
        ["id"] = s.Id, ["idKind"] = "number", ["permanentId"] = s.Perm, ["title"] = s.Title, ["level"] = 1, ["line"] = 1, ["text"] = s.Text, ["textHash"] = "h",
    })]);

    const string One = "The first long sentence about refunds goes here.";
    const string Two = "The second long sentence about shipping goes here.";
    const string Other = "A third long sentence about tax goes over here.";

    static JsonArray Before => Sections(("1", "S-0001", "Orders", $"{One} {Two}"), ("2", "S-0002", "Tax", Other));

    [Fact]
    public void One_section_going_to_two_that_nothing_else_claims_is_a_split_and_stays_a_persons_call()
    {
        var entries = FsdSectionDiff.Compare(Before, Sections(("1", "", "Refunds", One), ("2", "", "Shipping", Two), ("3", "", "Tax", Other)));
        var old = entries.Single(e => (string?)e["permanentId"] == "S-0001" && (string?)e["side"] == "current");
        Assert.Equal("doubtful", (string)old["kind"]!);
        Assert.Equal(["1", "2"], old["splitInto"]!.AsArray().Select(t => (string)t!));
        Assert.All(entries.Where(e => (string?)e["side"] == "candidate"), e => Assert.Equal("S-0001", (string)e["splitFrom"]!));
        Assert.Equal(3, entries.Count(e => (string)e["kind"]! == "doubtful"));
    }

    [Fact]
    public void A_part_that_also_shares_with_another_old_section_is_only_doubtful()
    {
        var entries = FsdSectionDiff.Compare(Before, Sections(("1", "", "Refunds", One), ("2", "", "Shipping", $"{Two} {Other}"), ("3", "", "Tax two", "Nothing shared at all in this one.")));
        Assert.DoesNotContain(entries, e => e["splitInto"] is not null || e["splitFrom"] is not null);
    }

    [Fact]
    public void A_section_with_one_shared_sentence_is_only_doubtful()
    {
        var entries = FsdSectionDiff.Compare(Before, Sections(("1", "", "Refunds", One), ("2", "", "Tax", Other)));
        Assert.DoesNotContain(entries, e => e["splitInto"] is not null || e["splitFrom"] is not null);
    }

    [Fact]
    public void A_new_title_on_the_same_words_under_the_same_parent_was_already_its_own_outcome()
    {
        var entries = FsdSectionDiff.Compare(Before, Sections(("1", "", "Orders renamed", $"{One} {Two}"), ("2", "", "Tax", Other)));
        Assert.Equal("renamed", (string)entries.Single(e => (string?)e["to"] == "1")["kind"]!);
    }
}
