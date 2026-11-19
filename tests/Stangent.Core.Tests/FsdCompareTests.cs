using System.Text.Json.Nodes;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>Phase fs layer 5 (D385, D388): a new file is compared three ways and never imported over; `fsd take` copies in
/// what a person picks, and the commit approves. Real .docx files from outlines. No AI.</summary>
public sealed class FsdCompareTests : IDisposable
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

    public FsdCompareTests()
    {
        repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        Committed.All(repo.Repo);
        Git.Run(Root, $"config user.name \"{Pat}\"");
    }

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    string DocDir => FsdImport.DocDir(Root, "shop");

    JsonObject Import(string outline, string name = "shop.docx")
    {
        var file = Path.Combine(Root, "..", Path.GetFileName(Root) + "-" + name);
        // The same outline is the same file, as a client sending it again would.
        if (!files.TryGetValue(outline, out var bytes)) files[outline] = bytes = DocxWriter.Write(outline);
        File.WriteAllBytes(file, bytes);
        try { return FsdImport.Import(repo.Repo, file, "shop"); }
        finally { File.Delete(file); }
    }

    JsonObject First(string outline)
    {
        var r = Import(outline);
        Committed.All(repo.Repo, "import");
        return r;
    }

    string Id(string number) => (string)FsdDocument.Load(Root, "shop")["sections"]!.AsArray().First(s => (string)s!["id"]! == number)!["permanentId"]!;

    List<JsonObject> Entries() => [.. FsdCompare.Show(Root, "shop")["sections"]!.AsArray().OfType<JsonObject>()];

    string Case(string pidOrFile) => (string)Entries().First(e => (string?)e["section"] == pidOrFile || ((string?)e["section"] is null && (string?)e["file"] == pidOrFile))["case"]!;

    string Text(string pid) => (string)FsdDocument.Load(Root, "shop")["sections"]!.AsArray().First(s => (string?)s!["permanentId"] == pid)!["text"]!;

    const string Intro = "This document says how the shop handles fresh produce.";

    static string V1 => $"#! Intro\n{Intro}\n#! Fruit\n{Apples}\n##! Pears\n{Pears}\n#! Veg\n{Plums}\n#! Notes\n{Figs}\n#! Kept\n{Dates}\n";

    // The client's next file: Intro as it was, Fruit and Pears reworded, Veg as it was, Notes gone (so Word renumbers Kept
    // from 5 to 4), and a new chapter 5 at the end.
    static string V2 => $"#! Intro\n{Intro}\n#! Fruit\n{Apples} They are washed first.\n##! Pears\n{Pears} The room is kept at four degrees.\n#! Veg\n{Plums}\n#! Kept\n{Dates}\n#! Extras\nGift boxes are wrapped by hand before they ship.\n";

    /// <summary>The PM's own edits, committed: Pears and Veg reworded in the repo.</summary>
    void PmEdits()
    {
        Chapters.Edit(DocDir, Pears, "Pears rest for three days in the cold room.");
        Chapters.Edit(DocDir, Plums, "Plums leave on the day they are picked.");
        Committed.All(repo.Repo, "pm edits");
    }

    [Fact]
    public void A_new_file_is_compared_per_section_and_changes_no_chapter()
    {
        First(V1);
        var (intro, fruit, pears, veg, notes, kept) = (Id("1"), Id("2"), Id("2.1"), Id("3"), Id("4"), Id("5"));
        PmEdits();
        var before = Chapters.Files(DocDir);
        var facts = File.ReadAllBytes(FsdDocument.JsonPath(Root, "shop"));

        var result = Import(V2);

        Assert.Equal("compared", (string)result["status"]!);
        Assert.Equal("file-changed", Case(fruit));   // only the file changed it: offered
        Assert.Equal("both-changed", Case(pears));   // both changed: side by side, the repo's kept
        Assert.Equal("repo-changed", Case(veg));     // only the repo changed it: kept
        Assert.Equal("gone", Case(notes));           // gone from the file: listed
        Assert.Equal("same", Case(intro));
        Assert.Equal("file-changed", Case(kept));    // renumbered by the file: its heading changed, its words didn't
        Assert.Equal((false, true), ((bool)Entries().First(e => (string?)e["section"] == kept)["textChanged"]!, (bool)Entries().First(e => (string?)e["section"] == kept)["headingChanged"]!));
        Assert.Equal("new", Case("5"));              // new in the file: offered
        Assert.Equal([fruit, kept], result["updates"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["5"], result["new"]!.AsArray().Select(n => (string)n!));
        Assert.Equal([pears], result["bothChanged"]!.AsArray().Select(n => (string)n!));

        // Nothing in the repo moved: the chapters, document.json, and so what every step reads.
        Assert.Equal(before, Chapters.Files(DocDir));
        Assert.Equal(facts, File.ReadAllBytes(FsdDocument.JsonPath(Root, "shop")));
        Assert.Empty(Git.Run(Root, "status --porcelain -- specs")!.Trim());
        var md = File.ReadAllText(Path.Combine(FsdCompare.Dir(Root, "shop"), "comparison.md"));
        Assert.Contains("## Changed in both: the repo's text is kept", md, StringComparison.Ordinal);
        Assert.Contains("> Pears rest for three days in the cold room.", md, StringComparison.Ordinal);
        Assert.Contains($"> {Pears} The room is kept at four degrees.", md, StringComparison.Ordinal);
        Assert.Contains($"stangent fsd take shop {fruit}", md, StringComparison.Ordinal);
        Assert.Null(result["converterChanged"]);
    }

    [Fact]
    public void Take_copies_only_what_the_file_alone_changed_and_never_a_pm_edit()
    {
        First(V1);
        var (fruit, pears, veg, notes, kept) = (Id("2"), Id("2.1"), Id("3"), Id("4"), Id("5"));
        PmEdits();
        Import(V2);

        // Named: a section the repo changed, alone or with the file, is refused and nothing is written.
        foreach (var pid in new[] { pears, veg, notes })
        {
            var refused = Assert.Throws<StangentException>(() => FsdCompare.Take(repo.Repo, "shop", [pid], false));
            Assert.Equal("nothing_taken", refused.Code);
        }
        Assert.Empty(Git.Run(Root, "status --porcelain -- specs")!.Trim());

        var taken = FsdCompare.Take(repo.Repo, "shop", [], allUpdates: true);
        Assert.Equal([fruit, kept], taken["taken"]!.AsArray().Select(t => (string)t!["section"]!));
        var text = Chapters.Text(DocDir);
        Assert.Contains($"{Apples} They are washed first.", text, StringComparison.Ordinal);
        Assert.Contains("Pears rest for three days in the cold room.", text, StringComparison.Ordinal);
        Assert.Contains("Plums leave on the day they are picked.", text, StringComparison.Ordinal);
        Assert.Contains(Figs, text, StringComparison.Ordinal);
        // Uncommitted: steps still read HEAD until the commit approves it (D380), and fsd check passes it.
        Assert.Equal(Apples, Text(fruit));
        Assert.True((bool)FsdCheck.Run(repo.Repo, "shop", [], [])["ok"]!);
    }

    [Fact]
    public void An_uncommitted_pm_edit_on_an_offered_section_is_never_overwritten()
    {
        First(V1);
        var fruit = Id("2");
        Import(V2);
        Assert.Equal("file-changed", Case(fruit));
        Chapters.Edit(DocDir, Apples, "Apples are graded by hand.");

        var refused = Assert.Throws<StangentException>(() => FsdCompare.Take(repo.Repo, "shop", [fruit], false));
        Assert.Equal("nothing_taken", refused.Code);
        Assert.Contains(refused.Details!, d => d.Contains("edited since HEAD", StringComparison.Ordinal));
        Assert.Contains("Apples are graded by hand.", Chapters.Text(DocDir), StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_the_original_again_keeps_an_uncommitted_takes_record()
    {
        First(V1);
        var fruit = Id("2");
        Import(V2);
        FsdCompare.Take(repo.Repo, "shop", [fruit], false);
        var taken = File.ReadAllText(FsdDocument.JsonPath(Root, "shop"));
        FsdImport.Reread(repo.Repo, "shop");
        var after = JsonNode.Parse(File.ReadAllText(FsdDocument.JsonPath(Root, "shop")))!.AsObject();
        Assert.Equal((string)JsonNode.Parse(taken)!["original"]!["hash"]!, (string)after["original"]!["hash"]!);
        Assert.Equal(JsonNode.Parse(taken)!["fileSections"]!.ToJsonString(), after["fileSections"]!.ToJsonString());
    }

    [Fact]
    public void A_new_section_is_taken_after_the_one_it_follows_with_a_new_id()
    {
        First(V1);
        var kept = Id("5");
        Import(V2);

        // Kept takes the file's number 4, which Notes (gone from the file, never deleted) still holds here: warned, as fsd check warns.
        var taken = FsdCompare.Take(repo.Repo, "shop", ["5"], allUpdates: true);
        Assert.Equal("S-0007", (string)taken["idsGiven"]![0]!);
        Assert.Equal(["4"], taken["numbersRepeated"]!.AsArray().Select(n => (string)n!));
        Assert.True((bool)FsdCheck.Run(repo.Repo, "shop", [], [])["ok"]!);
        Committed.All(repo.Repo, "take");

        var sections = FsdDocument.Load(Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal((kept, "S-0007"), ((string)sections[^2]["permanentId"]!, (string)sections[^1]["permanentId"]!));
        Assert.Equal(("5", "Extras", "Gift boxes are wrapped by hand before they ship."), ((string)sections[^1]["id"]!, (string)sections[^1]["title"]!, (string)sections[^1]["text"]!));
        Import(V2);
        Assert.Equal(("same", "same"), (Case(kept), Case("S-0007")));
    }

    [Fact]
    public void Take_then_commit_moves_the_base_so_a_third_file_compares_against_the_second()
    {
        First(V1);
        var (fruit, pears, veg) = (Id("2"), Id("2.1"), Id("3"));
        var importCommit = (string)FsdDocument.Facts(Root, "shop")!["importCommit"]!;
        Import(V2);
        FsdCompare.Take(repo.Repo, "shop", [pears], false);
        FsdCompare.Take(repo.Repo, "shop", [], allUpdates: true);
        Committed.All(repo.Repo, "take v2");

        var facts = FsdDocument.CommittedFacts(Root, "shop");
        Assert.NotEqual(importCommit, (string)facts["importCommit"]!);
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, facts.ToJsonString()));
        Assert.Equal($"{Apples} They are washed first.", Text(fruit));

        // The second file again: nothing is offered, since its sections are the base now.
        Import(V2);
        Assert.Equal(["same", "same"], new[] { Case(fruit), Case(pears) });

        // The PM edits Fruit after the take; a third file changes only Veg.
        Chapters.Edit(DocDir, "They are washed first.", "They are washed and dried first.");
        Committed.All(repo.Repo, "pm edit");
        var v3 = V2.Replace(Plums, "Plums ship within a day of picking.", StringComparison.Ordinal);
        Import(v3);
        Assert.Equal("repo-changed", Case(fruit));   // against the second file, not the first
        Assert.Equal("same", Case(pears));
        Assert.Equal("file-changed", Case(veg));
    }

    [Fact]
    public void A_doubtful_match_waits_for_a_person_and_is_then_offered()
    {
        First($"#! Fruit\n{Apples} {Pears}\n#! Veg\n{Plums}\n");
        var (fruit, veg) = (Id("1"), Id("2"));
        var result = Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n#! Veg\n{Plums}\n");

        Assert.Equal(["1", "2", fruit], result["doubtful"]!.AsArray().Select(d => (string)d!));
        Assert.Equal("nothing_taken", Assert.Throws<StangentException>(() => FsdCompare.Take(repo.Repo, "shop", ["1"], false)).Code);

        FsdCompare.Resolve(Root, "shop", "1", fruit, false, false, Pat);
        var settled = FsdCompare.Resolve(Root, "shop", "2", null, true, false, Pat);
        Assert.Empty(settled["doubtful"]!.AsArray());
        Assert.Equal("file-changed", Case(fruit));
        Assert.Equal("new", Case("2"));
        Assert.Equal("file-changed", Case(veg));   // renumbered 2 → 3 by the file
        var history = StateChanges.History(Root, StateKinds.FsdSectionMatch, $"shop@{((string)result["hash"]!)[..12]}#1");
        Assert.Equal([("doubtful", StateActor.CodeKind), ("same", StateActor.PersonKind)], history.Select(m => (m.To, m.Who.Kind)));

        // The same file again keeps what was settled.
        Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n#! Veg\n{Plums}\n");
        Assert.Equal("file-changed", Case(fruit));

        FsdCompare.Take(repo.Repo, "shop", ["2"], allUpdates: true);
        Committed.All(repo.Repo, "take");
        Assert.Equal([("1", "Red", fruit), ("2", "Green", "S-0003"), ("3", "Veg", veg)],
            FsdDocument.Load(Root, "shop")["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["title"]!, (string)s["permanentId"]!)));
    }

    [Fact]
    public void A_converter_change_is_named()
    {
        First(V1);
        var path = FsdDocument.JsonPath(Root, "shop");
        var facts = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(("docx", 1), ((string)facts["converter"]!["format"]!, (int)facts["converter"]!["version"]!));

        facts["converter"]!["version"] = 2;
        File.WriteAllText(path, CanonicalJson.Serialize(facts));
        Committed.All(repo.Repo, "older converter");
        var changed = Import(V2);
        Assert.Contains("differs from the last import's (docx v2 then, docx v1 now)", (string)changed["converterChanged"]!, StringComparison.Ordinal);
        Assert.Contains("**Converter:**", File.ReadAllText(Path.Combine(FsdCompare.Dir(Root, "shop"), "comparison.md")), StringComparison.Ordinal);

        facts.Remove("converter");
        File.WriteAllText(path, CanonicalJson.Serialize(facts));
        Committed.All(repo.Repo, "no converter");
        Assert.Contains("didn't record its converter", (string)Import(V2)["converterChanged"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comparison_made_before_a_commit_is_refused()
    {
        First(V1);
        var fruit = Id("2");
        Import(V2);
        Chapters.Edit(DocDir, Dates, "Dates come in boxes of six.");
        Committed.All(repo.Repo, "pm edit");

        Assert.Equal("comparison_out_of_date", Assert.Throws<StangentException>(() => FsdCompare.Take(repo.Repo, "shop", [fruit], false)).Code);
    }

    [Fact]
    public void A_document_written_before_layer_5_compares_against_the_commit_that_approved_its_import()
    {
        First(V1);
        var (fruit, veg) = (Id("2"), Id("3"));
        var path = FsdDocument.JsonPath(Root, "shop");
        var facts = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        facts.Remove("fileSections");
        File.WriteAllText(path, CanonicalJson.Serialize(facts));
        Committed.All(repo.Repo, "as layer 4 wrote it");
        Chapters.Edit(DocDir, Plums, "Plums leave on the day they are picked.");
        Committed.All(repo.Repo, "pm edit");

        Import(V2);
        Assert.StartsWith("commit ", (string)FsdCompare.Show(Root, "shop")["base"]!["from"]!, StringComparison.Ordinal);
        // Matched by that commit's chapters, which may hold the PM's own edits: so nothing is offered, and each difference is
        // shown side by side with the repo's kept. The PM's edit to Veg can't come back as a "client update".
        Assert.Equal(("both-changed", "both-changed", "same"), (Case(fruit), Case(veg), Case(Id("1"))));
        Assert.Equal("nothing_taken", Assert.Throws<StangentException>(() => FsdCompare.Take(repo.Repo, "shop", [], allUpdates: true)).Code);
        Assert.Equal("nothing_taken", Assert.Throws<StangentException>(() => FsdCompare.Take(repo.Repo, "shop", [veg], false)).Code);
    }

    [Fact]
    public void A_first_import_not_committed_yet_is_left_alone_and_another_file_waits()
    {
        Import(V1);
        Assert.Equal("unchanged", (string)Import(V1)["status"]!);
        Assert.Equal("fsd_not_committed", Assert.Throws<StangentException>(() => Import(V2)).Code);
        Assert.False(Directory.Exists(FsdCompare.Dir(Root, "shop")));
    }
}
