using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Permanent section ids, candidates, the section diff, settling, promotion and carrying work across (D117). No AI.</summary>
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
    JsonObject Import(string outline)
    {
        var file = Path.Combine(Root, "shop.docx");
        if (!files.TryGetValue(outline, out var bytes)) files[outline] = bytes = DocxWriter.Write(outline);
        File.WriteAllBytes(file, bytes);
        return FsdImport.Import(repo.Repo, file);
    }

    JsonObject Current() => JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(Root, "shop"), "document.json")))!.AsObject();

    string Id(string section) => (string)Current()["sections"]!.AsArray().First(s => (string)s!["id"]! == section)!["permanentId"]!;

    List<JsonObject> Entries() => [.. FsdVersions.Diff(Root, "shop")["sections"]!.AsArray().OfType<JsonObject>()];

    /// <summary>One entry as a line, so a whole diff reads as a list.</summary>
    static string Show(JsonObject e) => (string)e["kind"]! switch
    {
        "doubtful" => $"doubtful {(string?)e["to"] ?? (string)e["from"]!}: {string.Join(",", e["candidates"]!.AsArray().Select(c => (string?)c!["to"] ?? (string)c!["from"]!))}",
        "added" => $"added {e["to"]}",
        "removed" => $"removed {e["from"]}",
        var k => $"{k} {e["from"]}>{e["to"]}",
    };

    List<string> Diff(string first, string second)
    {
        Import(first);
        Assert.Equal("candidate", (string)Import(second)["status"]!);
        return [.. Entries().Select(Show)];
    }

    // ---- Permanent ids ----

    [Fact]
    public void A_first_import_goes_straight_in_with_an_id_per_section_beside_the_number()
    {
        var result = Import($"#! Fruit\n{Apples}\n##! Pears\n{Pears}\n");

        Assert.Equal("imported", (string)result["status"]!);
        Assert.Equal(3, (int)Current()["schemaVersion"]!);
        Assert.Equal([("1", "S-0001"), ("1.1", "S-0002")], Current()["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["permanentId"]!)));
        Assert.Equal(["S-0001", "S-0002"], FsdVersions.Ledger(Root, "shop").Active("sections"));
        Assert.False(Directory.Exists(FsdVersions.CandidateDir(Root, "shop")));
    }

    [Fact]
    public void A_changed_file_waits_as_a_candidate_and_the_current_version_keeps_serving()
    {
        Import($"#! Fruit\n{Apples}\n");
        var before = File.ReadAllBytes(Path.Combine(FsdImport.DocDir(Root, "shop"), "document.md"));
        var json = File.ReadAllBytes(Path.Combine(FsdImport.DocDir(Root, "shop"), "document.json"));

        var result = Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");

        Assert.Equal("candidate", (string)result["status"]!);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(FsdImport.DocDir(Root, "shop"), "document.md")));
        Assert.Equal(json, File.ReadAllBytes(Path.Combine(FsdImport.DocDir(Root, "shop"), "document.json")));
        Assert.True(File.Exists(Path.Combine(Root, (string)result["diff"]!)));
        Assert.True(File.Exists(Path.Combine(FsdVersions.CandidateDir(Root, "shop"), "section-diff.json")));
        Assert.Single(FsdImport.Documents(Root));
        Assert.Single(SpecIndex.Load(Root).Sections);
        Assert.Equal("waiting", (string)Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n")["status"]!);
    }

    [Fact]
    public void The_same_file_stays_unchanged_and_says_a_candidate_is_waiting()
    {
        var old = $"#! Fruit\n{Apples}\n";
        Import(old);
        Assert.Equal("unchanged", (string)Import(old)["status"]!);
        Assert.Null(Import(old)["candidate"]);

        var waiting = (string)Import($"#! Fruit\n{Pears}\n")["hash"]!;
        Assert.Equal(waiting, (string)Import(old)["candidate"]!);
    }

    [Fact]
    public void A_newer_file_replaces_a_waiting_candidate_and_what_was_settled_goes_with_it()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n");
        var firstHash = (string)Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n")["hash"]!;
        FsdVersions.Resolve(Root, "shop", "1", Id("1"), false, false, Pat);

        var second = Import($"#! Red\n{Apples}\n#! Blue\n{Pears}\n");

        Assert.Equal(3, FsdVersions.Diff(Root, "shop")["counts"]!["doubtful"]!.GetValue<int>());
        Assert.Equal("replaced", StateChanges.History(Root, StateKinds.FsdCandidate, $"shop@{firstHash[..12]}")[^1].To);
        Assert.Equal("candidate", StateChanges.History(Root, StateKinds.FsdCandidate, $"shop@{((string)second["hash"]!)[..12]}").Single().To);
    }

    [Fact]
    public void A_document_from_before_permanent_ids_gets_them_when_a_changed_file_comes_in()
    {
        Import($"#! Fruit\n{Apples}\n");
        var path = Path.Combine(FsdImport.DocDir(Root, "shop"), "document.json");
        File.WriteAllText(path, CanonicalJson.Serialize(AsItWasWritten(Current())));
        File.Delete(Path.Combine(FsdImport.DocDir(Root, "shop"), "ids.json"));

        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");

        Assert.Equal(3, (int)Current()["schemaVersion"]!);
        Assert.Equal("S-0001", (string)Entries()[0]["permanentId"]!);
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
    public void A_new_number_is_counted_on_an_unchanged_section()
    {
        Diff($"#! Fruit\n{Apples}\n", $"#! New\n{Dates}\n#! Fruit\n{Apples}\n");
        Assert.Equal(1, (int)FsdVersions.Diff(Root, "shop")["counts"]!["numberChanged"]!);
    }

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

    // ---- A person settles ----

    [Fact]
    public void Promote_is_refused_while_any_remain_and_lists_them()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n");
        var doc = Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n");
        var id = Id("1");

        var e = Assert.Throws<StangentException>(() => FsdVersions.Promote(repo.Repo, "shop", Pat));

        Assert.Equal(("doubtful_sections_remain", ExitCodes.Failed), (e.Code, e.ExitCode));
        Assert.Equal(["1", "2", id], e.Details);
        Assert.Equal(["1", "2", id], doc["doubtful"]!.AsArray().Select(d => (string)d!));
        Assert.Equal("Fruit", (string)Current()["sections"]![0]!["title"]!);
    }

    [Fact]
    public void Resolve_then_promote_keeps_the_id_on_the_same_section_and_records_who_from_the_person()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n");
        Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n");
        var id = Id("1");

        var settled = FsdVersions.Resolve(Root, "shop", "1", id, false, false, Pat);
        Assert.Equal(("same", id, Pat), ((string)settled["state"]!, (string)settled["same_as"]!, (string)settled["decided_by"]!));
        // The current section it claimed is settled with it, so only the other half is open.
        Assert.Equal(["2"], settled["doubtful"]!.AsArray().Select(d => (string)d!));
        FsdVersions.Resolve(Root, "shop", "2", null, true, false, Pat);

        var history = StateChanges.History(Root, StateKinds.FsdSectionMatch, $"shop@{((string)FsdVersions.Diff(Root, "shop")["to"]!)[..12]}#1");
        Assert.Equal([("doubtful", StateActor.CodeKind), ("same", StateActor.PersonKind)], history.Select(m => (m.To, m.Who.Kind)));
        Assert.Equal(Pat, history[^1].Who.Name);

        var result = FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.Equal([("1", "Red", id), ("2", "Green", "S-0002")], Current()["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["title"]!, (string)s["permanentId"]!)));
        Assert.Equal(2, (int)result["sections"]!);
        Assert.False(Directory.Exists(FsdVersions.CandidateDir(Root, "shop")));
        Assert.Contains(Apples, File.ReadAllText(Path.Combine(FsdImport.DocDir(Root, "shop"), "document.md")), StringComparison.Ordinal);
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, Current().ToJsonString()));
        Assert.Equal("promoted", StateChanges.History(Root, StateKinds.FsdCandidate, $"shop@{((string)result["hash"]!)[..12]}")[^1].To);
    }

    [Fact]
    public void A_person_can_change_their_mind_and_the_id_is_let_go()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n");
        Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n");
        var id = Id("1");

        FsdVersions.Resolve(Root, "shop", "1", id, false, false, Pat);
        Assert.Equal("section_id_taken", Assert.Throws<StangentException>(() => FsdVersions.Resolve(Root, "shop", "2", id, false, false, Pat)).Code);
        Assert.Equal("section_id_taken", Assert.Throws<StangentException>(() => FsdVersions.Resolve(Root, "shop", id, null, false, true, Pat)).Code);

        var changed = FsdVersions.Resolve(Root, "shop", "1", null, true, false, Pat);
        Assert.Equal(["2", id], changed["doubtful"]!.AsArray().Select(d => (string)d!));
        Assert.Equal("same", (string)FsdVersions.Resolve(Root, "shop", "2", id, false, false, Pat)["state"]!);
    }

    [Fact]
    public void Resolve_refuses_what_it_cant_do()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n#! Veg\n{Dates}\n");
        Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n#! Veg\n{Dates}\n");
        var id = Id("1");

        StangentException Refused(string section, string? same, bool isNew = false, bool removed = false) =>
            Assert.Throws<StangentException>(() => FsdVersions.Resolve(Root, "shop", section, same, isNew, removed, Pat));

        Assert.Equal("usage", Refused("1", id, isNew: true).Code);
        Assert.Equal("usage", Refused("1", null).Code);
        Assert.Equal("not_doubtful", Refused("3", id).Code);
        Assert.Equal("not_found", Refused("1", "S-0099").Code);
        Assert.Equal("section_id_taken", Refused("1", Id("2")).Code);
        Assert.Equal("usage", Refused("1", null, removed: true).Code);
        Assert.Equal("usage", Refused(id, null, isNew: true).Code);
    }

    [Fact]
    public void A_doubtful_section_of_the_current_version_is_settled_as_removed_in_a_merge()
    {
        Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n");
        Import($"#! Fruit\n{Apples} {Pears}\n");
        var (red, green) = (Id("1"), Id("2"));

        FsdVersions.Resolve(Root, "shop", "1", red, false, false, Pat);
        Assert.Equal([green], FsdVersions.Diff(Root, "shop")["sections"]!.AsArray().Where(e => (string?)e!["state"] == "doubtful").Select(e => (string)e!["permanentId"]!));
        FsdVersions.Resolve(Root, "shop", green, null, false, true, Pat);
        var result = FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.Equal(red, Id("1"));
        Assert.Equal([green], FsdVersions.Ledger(Root, "shop").Removed("sections"));
        Assert.Equal(green, (string)result["carried"]!["removed"]![0]!["permanentId"]!);
    }

    [Fact]
    public void An_id_removed_earlier_never_comes_back()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        var (fruit, veg) = (Id("1"), Id("2"));
        Import($"#! Fruit\n{Apples}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);
        Assert.Equal([veg], FsdVersions.Ledger(Root, "shop").Removed("sections"));

        // The removed section's words come back, under another name that shares a sentence with one still here.
        Import($"#! Fruit\n{Apples}\n#! Veg again\n{Pears} {Apples}\n");
        var e = Assert.Throws<StangentException>(() => FsdVersions.Resolve(Root, "shop", "2", veg, false, false, Pat));
        Assert.Equal("section_id_removed", e.Code);

        FsdVersions.Resolve(Root, "shop", "2", null, true, false, Pat);
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.Equal([fruit, "S-0003"], Current()["sections"]!.AsArray().Select(s => (string)s!["permanentId"]!));
        Assert.Equal([veg], FsdVersions.Ledger(Root, "shop").Removed("sections"));
    }

    [Fact]
    public void A_deleted_ledger_cant_give_a_live_id_again()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        File.Delete(Path.Combine(FsdImport.DocDir(Root, "shop"), "ids.json"));

        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n#! Nuts\n{Plums}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.Equal(["S-0001", "S-0002", "S-0003"], Current()["sections"]!.AsArray().Select(s => (string)s!["permanentId"]!));
    }

    [Fact]
    public void A_second_round_of_versions_follows_the_first()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        var (fruit, veg) = (Id("1"), Id("2"));

        Import($"#! New\n{Dates}\n#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);
        Assert.Equal([("1", "S-0003"), ("2", fruit), ("3", veg)], Current()["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["permanentId"]!)));

        Import($"#! New\n{Dates}\n#! Veg\n{Pears}\n#! Produce\n{Apples}\n");
        Assert.Equal(["unchanged 1>1", "unchanged 3>2", "renamed 2>3"], Entries().Select(Show));
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.Equal([("1", "S-0003"), ("2", veg), ("3", fruit)], Current()["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["permanentId"]!)));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(FsdImport.DocDir(Root, "shop"), "promotions.jsonl")).Length);
    }

    // ---- Carrying work across ----

    static JsonObject Quote(string section, string text) => new() { ["doc"] = "shop", ["section"] = section, ["text"] = text, ["hash"] = TraceFile.QuoteHash(text) };

    /// <summary>A feature as the real writer leaves it, holding one approved requirement quoting the section.</summary>
    void Feature(string id, string section, string title, string text, string? parent = null)
    {
        var requirement = new JsonObject
        {
            ["id"] = "R-" + id[2..], ["title"] = "A rule", ["kind"] = "rule", ["statement"] = text, ["quotes"] = new JsonArray(Quote(section, text)), ["acceptance"] = new JsonArray(),
            ["refers"] = new JsonArray(), ["status"] = "approved", ["check"] = "confirmed", ["note"] = null, ["hash"] = "h", ["decided_by"] = Pat,
        };
        RequirementFile.Save(Root, new JsonObject
        {
            ["version"] = 1, ["id"] = id, ["doc"] = "shop", ["section"] = section, ["title"] = title, ["parent"] = parent, ["children"] = new JsonArray(),
            ["textHash"] = CanonicalJson.Hash(text), ["requirements"] = new JsonArray(requirement), ["definitions"] = new JsonArray(), ["journeys"] = new JsonArray(), ["background"] = new JsonArray(), ["retired"] = new JsonArray(),
        });
    }

    JsonObject Link(string section, string title, string quote, string decision = "agree")
    {
        var entry = LinkRun.Entry("gap", "shop", new JsonObject { ["id"] = section, ["title"] = title }, quote, null, null, null, "code", null, null);
        entry["decision"] = decision;
        entry["decided_by"] = Pat;
        var trace = TraceFile.Load(Root);
        trace["entries"]!.AsArray().Add(entry);
        TraceFile.Save(Root, trace);
        return entry;
    }

    JsonObject Trace(int at = 0) => TraceFile.Load(Root)["entries"]![at]!.AsObject();

    [Fact]
    public void A_moved_section_takes_its_links_decisions_requirements_and_questions_to_its_new_number()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Link("2", "Veg", Pears);
        Feature("F-0001", "2", "Veg", Pears);
        RequirementFile.SaveQuestions(Root, "shop", [new JsonObject { ["id"] = "Q-0001", ["section"] = "2", ["text"] = "Which?", ["quotes"] = new JsonArray(Quote("2", Pears)), ["status"] = "open", ["answer"] = null }]);
        var oldId = (string)Trace()["id"]!;

        Import($"#! New\n{Dates}\n#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        var result = FsdVersions.Promote(repo.Repo, "shop", Pat);

        var entry = Trace();
        Assert.Equal(("3", "agree", Pat), ((string)entry["fsd"]!["section"]!, (string)entry["decision"]!, (string)entry["decided_by"]!));
        Assert.NotEqual(oldId, (string)entry["id"]!);
        Assert.Equal(TraceFile.EntryId("gap", "shop", "3", null, Pears, null), (string)entry["id"]!);
        Assert.Equal(oldId, (string)result["carried"]!["traceIds"]![0]!["from"]!);
        var feature = RequirementFile.Features(Root, "shop").Single();
        Assert.Equal("3", (string)feature["section"]!);
        Assert.Equal("3", (string)feature["requirements"]![0]!["quotes"]![0]!["section"]!);
        Assert.Equal(("approved", Pat), ((string)feature["requirements"]![0]!["status"]!, (string)feature["requirements"]![0]!["decided_by"]!));
        var question = RequirementFile.Questions(Root, "shop").Single();
        Assert.Equal("3", (string)question["section"]!);
        Assert.Equal("3", (string)question["quotes"]![0]!["section"]!);
        // The trace check agrees: the id is the hash of the new link, and the section's there.
        var check = TraceCheck.Run(Root, null, null, fix: false);
        Assert.True((bool)check["ok"]!);
        Assert.Contains("\"3\"", File.ReadAllText(Path.Combine(RequirementFile.DocDir(Root, "shop"), "index.yaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_that_shift_past_each_other_dont_run_together()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Link("1", "Fruit", Apples);
        Link("2", "Veg", Pears);

        Import($"#! Veg\n{Pears}\n#! Fruit\n{Apples}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        var bySection = TraceFile.Load(Root)["entries"]!.AsArray().ToDictionary(e => (string)e!["fsd"]!["section"]!, e => (string)e!["fsd"]!["quote"]!);
        Assert.Equal(Pears, bySection["1"]);
        Assert.Equal(Apples, bySection["2"]);
    }

    [Fact]
    public void A_ci_check_and_attribution_dont_count_a_moved_entry_as_a_new_decision()
    {
        Import($"#! Fruit\n{Apples}\n");
        Link("1", "Fruit", Apples);
        var before = TraceFile.Load(Root);

        Import($"#! New\n{Dates}\n#! Fruit\n{Apples}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.True((bool)TraceCheck.Run(Root, null, before, fix: false)["ok"]!);
        Assert.Equal(0, TraceCheck.Attribute(Root, before, "The Merger"));
        Assert.Equal(Pat, (string)Trace()["decided_by"]!);
    }

    [Fact]
    public void A_typed_name_on_a_moved_entry_is_still_caught()
    {
        Import($"#! Fruit\n{Apples}\n");
        Link("1", "Fruit", Apples);
        var before = TraceFile.Load(Root);
        Import($"#! New\n{Dates}\n#! Fruit\n{Apples}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);
        var trace = TraceFile.Load(Root);
        trace["entries"]![0]!["decided_by"] = "Typed Name";
        TraceFile.Save(Root, trace);

        Assert.False((bool)TraceCheck.Run(Root, null, before, fix: false)["ok"]!);
    }

    [Fact]
    public void A_renumbered_section_keeps_its_paid_run_valid_and_a_renamed_one_says_it_runs_again()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        var sections = Current()["sections"]!.AsArray();
        var runs = new JsonObject
        {
            ["shop#1"] = new JsonObject { ["inputs"] = RequirementRun.InputsOf(sections[0]!.AsObject(), "shop") },
            ["shop#2"] = new JsonObject { ["inputs"] = RequirementRun.InputsOf(sections[1]!.AsObject(), "shop") },
        };
        Directory.CreateDirectory(RequirementFile.Dir(Root));
        File.WriteAllText(RequirementFile.RunsFile(Root), CanonicalJson.Serialize(runs));

        Import($"#! New\n{Dates}\n#! Fruit\n{Apples}\n#! Greens\n{Pears}\n");
        var carried = FsdVersions.Promote(repo.Repo, "shop", Pat)["carried"]!;

        // Fruit: only its number changed, so the saved items need no paid run.
        Assert.Equal("current", RequirementRun.Prepare(repo.Repo, "shop", "2").Status);
        // Greens: its title changed, which the AI reads.
        Assert.NotEqual("current", RequirementRun.Prepare(repo.Repo, "shop", "3").Status);
        Assert.Equal(["3"], carried["runAgain"]!.AsArray().Select(s => (string)s!));
    }

    [Fact]
    public void A_stale_run_isnt_made_valid_by_a_move()
    {
        Import($"#! Fruit\n{Apples}\n");
        Directory.CreateDirectory(RequirementFile.Dir(Root));
        File.WriteAllText(RequirementFile.RunsFile(Root), CanonicalJson.Serialize(new JsonObject { ["shop#1"] = new JsonObject { ["inputs"] = "not the hash of this section" } }));

        Import($"#! New\n{Dates}\n#! Fruit\n{Apples}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        Assert.Equal("not the hash of this section", (string)RequirementRun.LoadRuns(Root)["shop#2"]!["inputs"]!);
    }

    [Fact]
    public void A_modified_section_goes_through_the_existing_drop_back_when_a_quote_changes()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n");
        Link("1", "Fruit", Apples);
        Link("1", "Fruit", Pears);

        Import($"#! Fruit\n{Apples} {Plums}\n");
        var result = FsdVersions.Promote(repo.Repo, "shop", Pat);

        var entries = TraceFile.Load(Root)["entries"]!.AsArray().OfType<JsonObject>().ToDictionary(e => (string)e["fsd"]!["quote"]!);
        Assert.Equal("agree", (string)entries[Apples]["decision"]!);
        Assert.Null(entries[Pears]["decision"]);
        Assert.Null(entries[Pears]["decided_by"]);
        Assert.Equal([(string)entries[Pears]["id"]!], result["staleQuotes"]!.AsArray().Select(s => (string)s!));
    }

    [Fact]
    public void A_removed_sections_items_are_reported_and_stay_out_of_reach_of_a_new_section_with_its_number()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        var veg = Id("2");
        var link = Link("2", "Veg", Pears);
        Feature("F-0001", "2", "Veg", Pears);

        // Veg goes, and a different chapter takes its number.
        Import($"#! Fruit\n{Apples}\n#! Nuts\n{Plums}\n");
        var result = FsdVersions.Promote(repo.Repo, "shop", Pat);

        var removed = result["carried"]!["removed"]![0]!;
        Assert.Equal((veg, "2", true), ((string)removed["permanentId"]!, (string)removed["section"]!, (bool)removed["numberUsedAgain"]!));
        Assert.Equal([TraceFile.EntryId("gap", "shop", $"(removed {veg}) 2", null, Pears, null)], removed["traceEntries"]!.AsArray().Select(s => (string)s!));
        Assert.Equal((string)link["id"]!, (string)result["carried"]!["traceIds"]![0]!["from"]!);
        Assert.Equal(["F-0001"], removed["features"]!.AsArray().Select(s => (string)s!));
        Assert.Equal(["R-0001"], removed["requirements"]!.AsArray().Select(s => (string)s!));
        // Never dropped: the items are still on file, under a mark no heading can be, and the new section 2 doesn't pick them up.
        var feature = RequirementFile.Features(Root, "shop").Single();
        Assert.Equal($"(removed {veg}) 2", (string)feature["section"]!);
        Assert.Equal("approved", (string)feature["requirements"]![0]!["status"]!);
        Assert.Null(LinkRun.Requirements(Root, "shop", Current()["sections"]![1]!.AsObject()));
        Assert.Equal($"(removed {veg}) 2", (string)Assert.Single(TraceFile.Load(Root)["entries"]!.AsArray())!["fsd"]!["section"]!);
    }

    [Fact]
    public void A_removed_sections_trace_entry_takes_the_mark_and_keeps_its_decision_and_who_decided()
    {
        Import($"#! Intro\n{Apples}\n#! Orders\n{Pears}\n");
        var orders = Id("2");
        var oldId = (string)Link("2", "Orders", Pears, "fsd-wrong")["id"]!;
        var before = TraceFile.Load(Root);

        // Orders goes, and Billing takes its number.
        Import($"#! Intro\n{Apples}\n#! Billing\n{Plums}\n");
        var result = FsdVersions.Promote(repo.Repo, "shop", Pat);

        var mark = $"(removed {orders}) 2";
        var newId = TraceFile.EntryId("gap", "shop", mark, null, Pears, null);
        void Kept()
        {
            var entry = Assert.Single(TraceFile.Load(Root)["entries"]!.AsArray())!;
            Assert.Equal((newId, mark, "Orders"), ((string)entry["id"]!, (string)entry["fsd"]!["section"]!, (string)entry["fsd"]!["title"]!));
            Assert.Equal(("fsd-wrong", Pat), ((string?)entry["decision"], (string?)entry["decided_by"]));
        }
        Kept();

        // Reported on promote: where it went and what it still holds. Nothing moved, so there's no history line and it isn't a stale quote.
        var removed = result["carried"]!["removed"]![0]!;
        Assert.Equal([newId], removed["traceEntries"]!.AsArray().Select(s => (string)s!));
        var decision = Assert.Single(removed["decisions"]!.AsArray())!;
        Assert.Equal((newId, "fsd-wrong", Pat), ((string)decision["id"]!, (string)decision["value"]!, (string)decision["by"]!));
        Assert.Equal((oldId, newId), ((string)result["carried"]!["traceIds"]![0]!["from"]!, (string)result["carried"]!["traceIds"]![0]!["to"]!));
        Assert.Empty(result["staleQuotes"]!.AsArray());
        Assert.Empty(StateChanges.History(Root, StateKinds.TraceDecision, oldId));
        Assert.Empty(StateChanges.History(Root, StateKinds.TraceDecision, newId));

        // `trace check` keeps flagging it for a person, and its fix leaves it alone. CI doesn't read the new id as a typed name or a new decision.
        var check = TraceCheck.Run(Root, null, null, fix: true);
        Assert.Contains(check["problems"]!.AsArray(), p => (string)p! == $"{newId}: FSD section shop §{mark} was removed; its decision stays until a person deals with the entry");
        Assert.Contains(result["traceProblems"]!.AsArray(), p => ((string)p!).Contains("was removed", StringComparison.Ordinal));
        Assert.Empty(check["staleQuotes"]!.AsArray());
        Kept();
        Assert.DoesNotContain(TraceCheck.Run(Root, null, before, fix: false)["problems"]!.AsArray(), p => ((string)p!).Contains("decided_by", StringComparison.Ordinal));
        Assert.Equal(0, TraceCheck.Attribute(Root, before, "The Merger"));
        Kept();

        // A judge's new build of the new section 2 doesn't reach it, and approving that chapter doesn't either.
        LinkRun.ApplyEntries(Root, "shop", "2", []);
        Assert.Equal(0, TraceCheck.ApproveChapter(Root, "shop", "2"));
        Kept();
    }

    [Fact]
    public void A_person_discards_a_candidate_and_the_current_version_stays()
    {
        var first = $"#! Fruit\n{Apples}\n";
        var second = $"#! Fruit\n{Pears}\n";
        Import(first);
        var hash = (string)Import(second)["hash"]!;

        var result = FsdVersions.Discard(Root, "shop", Pat);

        Assert.Equal(("shop", hash, Pat), ((string)result["id"]!, (string)result["discarded"]!, (string)result["by"]!));
        Assert.False(Directory.Exists(FsdVersions.CandidateDir(Root, "shop")));
        Assert.Null(FsdVersions.PendingHash(Root, "shop"));
        Assert.Equal(Apples, ((string)Current()["sections"]![0]!["text"]!).Trim());
        var last = StateChanges.History(Root, StateKinds.FsdCandidate, $"shop@{hash[..12]}")[^1];
        Assert.Equal(("candidate", "discarded", "person", Pat), (last.From, last.To, last.Who.Kind, last.Who.Name));

        Assert.Equal("no_candidate", Assert.Throws<StangentException>(() => FsdVersions.Discard(Root, "shop", Pat)).Code);
        Assert.Equal("no_candidate", Assert.Throws<StangentException>(() => FsdVersions.Promote(repo.Repo, "shop", Pat)).Code);
        // The same file can come back as a candidate later.
        Assert.Equal("candidate", (string)Import(second)["status"]!);
    }

    [Fact]
    public void The_current_file_again_says_plainly_a_candidate_is_still_waiting_and_how_to_discard_it()
    {
        var first = $"#! Fruit\n{Apples}\n";
        Import(first);
        Assert.Null(Import(first)["note"]);

        var hash = (string)Import($"#! Fruit\n{Pears}\n")["hash"]!;
        var same = Import(first);

        Assert.Equal(("unchanged", hash), ((string)same["status"]!, (string)same["candidate"]!));
        var note = (string)same["note"]!;
        Assert.Contains($"candidate {hash[..12]} is still waiting", note);
        Assert.Contains("`stangent fsd promote shop` would apply it", note);
        Assert.Contains("`stangent fsd discard shop`", note);
    }

    [Fact]
    public void A_modified_section_under_a_duplicate_heading_goes_to_a_person_even_with_no_sentence_shared()
    {
        Import($"#! Notes\n{Apples}\n#! Notes\n{Pears}\n");
        var (apples, pears) = (Id("1"), Id("2"));
        Import($"#! Notes\n{Pears}\n#! Notes\n{Apples}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);
        Assert.Equal((pears, apples), (Id("1"), Id("2")));

        // The first note is rewritten: not a word shared, and the heading is the same as the other note's.
        Import($"#! Notes\n{Plums}\n#! Notes\n{Apples}\n");

        Assert.Equal(["doubtful 1: 1", "unchanged 2>2", "doubtful 1: 1"], Entries().Select(Show));
        var open = Entries()[0];
        var candidate = open["candidates"]![0]!;
        Assert.Equal((pears, true), ((string)candidate["permanentId"]!, (bool)candidate["sameHeading"]!));
        Assert.Null(candidate["sentence"]);
        Assert.Contains("the same heading", FsdSectionDiff.Markdown(FsdVersions.Diff(Root, "shop")));
        Assert.Equal("doubtful_sections_remain", Assert.Throws<StangentException>(() => FsdVersions.Promote(repo.Repo, "shop", Pat)).Code);

        FsdVersions.Resolve(Root, "shop", "1", pears, isNew: false, removed: false, Pat);
        FsdVersions.Promote(repo.Repo, "shop", Pat);
        Assert.Equal((pears, apples), (Id("1"), Id("2")));
    }

    [Fact]
    public void A_duplicate_heading_with_a_shared_sentence_lists_that_sentence_once()
    {
        Import($"#! Notes\n{Apples}\n#! Notes\n{Pears}\n");
        Import($"#! Notes\n{Apples} {Plums}\n#! Notes\n{Pears} {Figs}\n");
        var entries = Entries().Where(e => (string?)e["side"] == "candidate").ToList();
        Assert.Equal(2, entries.Count);
        // Each holds the note it shares a sentence with first, then the other under the same heading, each once.
        Assert.Equal([("1", Apples), ("2", null)], entries[0]["candidates"]!.AsArray().Select(c => ((string)c!["from"]!, (string?)c["sentence"])));
    }

    [Fact]
    public void A_moved_feature_goes_under_its_new_parents_feature()
    {
        Import($"#! A\n{Apples}\n##! Kept\n{Pears}\n#! B\n{Plums}\n");
        Feature("F-0001", "1", "A", Apples);
        Feature("F-0002", "1.1", "Kept", Pears, parent: "F-0001");
        Feature("F-0003", "2", "B", Plums);
        var parent = RequirementFile.Features(Root, "shop")[0];
        parent["children"]!.AsArray().Add((JsonNode)"F-0002");
        RequirementFile.Save(Root, parent);

        Import($"#! A\n{Apples}\n#! B\n{Plums}\n##! Kept\n{Pears}\n");
        FsdVersions.Promote(repo.Repo, "shop", Pat);

        var features = RequirementFile.Features(Root, "shop").ToDictionary(f => (string)f["id"]!);
        Assert.Equal(("2.1", "F-0003"), ((string)features["F-0002"]["section"]!, (string)features["F-0002"]["parent"]!));
        Assert.Empty(features["F-0001"]["children"]!.AsArray());
        Assert.Equal(["F-0002"], features["F-0003"]["children"]!.AsArray().Select(c => (string)c!));
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
        foreach (var s in copy["sections"]!.AsArray().OfType<JsonObject>()) s.Remove("permanentId");
        return copy;
    }

    [Fact]
    public void An_unchanged_document_keeps_its_bytes_and_every_hash_saved_for_it()
    {
        var file = Path.Combine(SonarrDir(), "sonarr-fsd.docx");
        Assert.Equal("imported", (string)FsdImport.Import(repo.Repo, file, "sonarr")["status"]!);
        var dir = FsdImport.DocDir(Root, "sonarr");
        var jsonPath = Path.Combine(dir, "document.json");
        var imported = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();

        // The same bytes as before this work: document.md whole, and document.json but for the added ids.
        Assert.Equal("b546bb24c70f6901327bdc6298db22e136aec9ca7ff2c3ae76cb39e7d5254f50", Sha(File.ReadAllBytes(Path.Combine(dir, "document.md"))));
        var written = CanonicalJson.Serialize(AsItWasWritten(imported));
        Assert.Equal("c18cbded20fc31bbdcac45e8e71671f29efcac855516862246905a43b9f6a621", Sha(System.Text.Encoding.UTF8.GetBytes(written)));

        // A document imported earlier, with saved runs and links: the state an owner's repo is in today.
        File.WriteAllText(jsonPath, written);
        var v1 = JsonNode.Parse(written)!.AsObject();
        var withText = v1["sections"]!.AsArray().OfType<JsonObject>().Where(s => Sentences.Of((string)s["text"]!).Count > 0).ToList();
        var index = SpecIndex.Load(Root);
        var inputs = withText.ToDictionary(s => (string)s["id"]!, s => RequirementRun.InputsOf(s, "sonarr"));
        var judged = withText.ToDictionary(s => (string)s["id"]!, s => LinkRun.JudgeInputs(s, "sonarr", [], index, null, findings: true));
        var linked = withText.ToDictionary(s => (string)s["id"]!, s => TraceFile.EntryId("gap", "sonarr", (string)s["id"]!, null, (string)s["text"]!, null));
        Directory.CreateDirectory(RequirementFile.Dir(Root));
        File.WriteAllText(RequirementFile.RunsFile(Root), CanonicalJson.Serialize(new JsonObject(inputs.Select(i => KeyValuePair.Create($"sonarr#{i.Key}", (JsonNode?)new JsonObject { ["inputs"] = i.Value })))));
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add(LinkRun.Entry("gap", "sonarr", withText[0], Sentences.Of((string)withText[0]["text"]!)[0].Text, null, null, null, "code", null, null));
        TraceFile.Save(Root, trace);
        var saved = new[] { RequirementFile.RunsFile(Root), TraceFile.PathIn(Root), Path.Combine(dir, "document.md") }.ToDictionary(p => p, File.ReadAllBytes);

        // Its next import: the same file. It stays unchanged, and gets its ids.
        var again = FsdImport.Import(repo.Repo, file, "sonarr");
        Assert.Equal("unchanged", (string)again["status"]!);
        Assert.Equal(v1["sections"]!.AsArray().Count, (int)again["permanentIdsAdded"]!);
        Assert.Null(FsdImport.Import(repo.Repo, file, "sonarr")["permanentIdsAdded"]);

        var upgraded = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
        Assert.Equal(3, (int)upgraded["schemaVersion"]!);
        Assert.All(upgraded["sections"]!.AsArray(), s => Assert.Matches("^S-[0-9]{4}$", (string)s!["permanentId"]!));
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, upgraded.ToJsonString()));
        // Taking the ids off gives back the exact bytes: the ids are the only change.
        Assert.Equal(written, CanonicalJson.Serialize(AsItWasWritten(upgraded)));
        foreach (var (path, bytes) in saved) Assert.Equal(bytes, File.ReadAllBytes(path));

        // Every hash worked out again from the upgraded file is the one saved, so no paid step runs.
        index = SpecIndex.Load(Root);
        foreach (var s in upgraded["sections"]!.AsArray().OfType<JsonObject>().Where(s => inputs.ContainsKey((string)s["id"]!)))
        {
            var id = (string)s["id"]!;
            Assert.Equal(inputs[id], RequirementRun.InputsOf(s, "sonarr"));
            Assert.Equal(judged[id], LinkRun.JudgeInputs(s, "sonarr", [], index, null, findings: true));
            Assert.Equal(linked[id], TraceFile.EntryId("gap", "sonarr", id, null, (string)s["text"]!, null));
            Assert.Equal("current", RequirementRun.Prepare(repo.Repo, "sonarr", id).Status);
        }
        Assert.True((bool)TraceCheck.Run(Root, null, null, fix: false)["ok"]!);
        Assert.False(Directory.Exists(Path.Combine(Root, ".stangent", "req", "sonarr")));
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
        Import(PairBefore);
        Import(PairAfter);

        // Reviewed by hand. Delivery's words stay under a new title: renamed. Tracking and Returns have new parents: moved.
        // Cancelling split in two, neither half keeping its name: doubtful. Old notes shares nothing: removed.
        Assert.Equal(
        [
            "added 1", "unchanged 1>2", "modified 1.1>2.1", "doubtful 2.2: 1.2", "doubtful 2.3: 1.2", "renamed 2>3", "moved 2.1>3.1", "added 4", "moved 2.2>4.1", "doubtful 1.2: 2.2,2.3", "removed 3",
        ], Entries().Select(Show));
        var counts = FsdVersions.Diff(Root, "shop")["counts"]!;
        Assert.Equal((1, 2, 1, 1, 2, 1, 3, 0), ((int)counts["unchanged"]!, (int)counts["moved"]!, (int)counts["renamed"]!, (int)counts["modified"]!, (int)counts["added"]!, (int)counts["removed"]!, (int)counts["doubtful"]!, (int)counts["settled"]!));
        var markdown = File.ReadAllText(Path.Combine(FsdVersions.CandidateDir(Root, "shop"), "SECTION_DIFF.md"));
        Assert.Contains("stangent fsd resolve shop \"2.2\" --same-as <S-id>", markdown, StringComparison.Ordinal);
        Assert.Contains($"\"{Plums}\"", markdown, StringComparison.Ordinal);
        Assert.Equal(FsdSectionDiff.Markdown(FsdVersions.Diff(Root, "shop")), markdown);
    }

    /// <summary>A planted edit, not a real version change (D56): a chapter inserted, a section moved, one reworded, one split.</summary>
    [Fact]
    public void Sonarrs_planted_edit_finds_what_was_planted()
    {
        var dir = SonarrDir();
        var outline = File.ReadAllText(Path.Combine(dir, "sonarr-fsd.outline.md"));
        FsdImport.Import(repo.Repo, Path.Combine(dir, "sonarr-fsd.docx"), "sonarr");
        var before = JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(Root, "sonarr"), "document.json")))!["sections"]!.AsArray().Count;

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

        var counts = result["counts"]!;
        // Planted: one added, one moved, one modified, one split (its old section and two new ones are doubtful), the rest unchanged.
        Assert.Equal(("candidate", before + 2), ((string)result["status"]!, (int)result["sections"]!));
        Assert.Equal((before - 3, 1, 0, 1, 1, 0, 3), ((int)counts["unchanged"]!, (int)counts["moved"]!, (int)counts["renamed"]!, (int)counts["modified"]!, (int)counts["added"]!, (int)counts["removed"]!, (int)counts["doubtful"]!));
        Assert.Equal(7, (int)counts["numberChanged"]!);
    }
}
