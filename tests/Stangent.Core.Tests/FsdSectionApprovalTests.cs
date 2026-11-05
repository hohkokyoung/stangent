using System.Text.Json.Nodes;
using Stangent.Core.State;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>D150: section-level approval behind section-approval, --partial, a stale candidate's sections, and readable section files. Fakes only, no AI.</summary>
public sealed class FsdSectionApprovalTests : IDisposable
{
    const string Pat = "Pat Example", Sam = "Sam Example";

    const string Apples = "Apples are sorted by size before they are packed.";
    const string Pears = "Pears wait three days in the cold room first.";
    const string Plums = "Plums ship the same day they are picked.";
    const string Figs = "Figs are only sold to trade customers on request.";
    const string Dates = "Dates come in boxes of twelve from the supplier.";

    readonly TempRepo archive = new();
    readonly TempRepo repo;
    readonly Dictionary<string, byte[]> files = [];

    public FsdSectionApprovalTests() : this(true) { }

    FsdSectionApprovalTests(bool on) => repo = new TempRepo(
        $"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\ncomponents: {{ section-approval: {(on ? "true" : "false")} }}\n");

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    string DocDir => FsdImport.DocDir(Root, "shop");

    JsonObject Import(string outline, bool partial = false)
    {
        var file = Path.Combine(Root, "shop.docx");
        if (!files.TryGetValue(outline, out var bytes)) files[outline] = bytes = DocxWriter.Write(outline);
        File.WriteAllBytes(file, bytes);
        return FsdImport.Import(repo.Repo, file, partial: partial);
    }

    JsonObject Current() => JsonNode.Parse(File.ReadAllText(Path.Combine(DocDir, "document.json")))!.AsObject();
    List<(string Id, string Title, string Pid, string Text)> Sections() =>
        [.. Current()["sections"]!.AsArray().Select(s => ((string)s!["id"]!, (string)s["title"]!, (string)s["permanentId"]!, (string)s["text"]!))];
    string Markdown() => File.ReadAllText(Path.Combine(DocDir, "document.md"));

    JsonObject Approve(string section, string who = Pat) => FsdSectionApproval.Answer(repo.Repo, "shop", section, true, null, who);
    JsonObject Reject(string section, string? why = null) => FsdSectionApproval.Answer(repo.Repo, "shop", section, false, why, Pat);
    JsonObject Promote() => FsdVersions.Promote(repo.Repo, "shop", Pat);
    StangentException Refused(Action act) => Assert.Throws<StangentException>(act);

    /// <summary>What document.md holds under each heading is the section's text, and the record passes the schema: the result's own check, from outside.</summary>
    void Consistent()
    {
        var lines = Markdown().Split('\n');
        var sections = Current()["sections"]!.AsArray().OfType<JsonObject>().ToList();
        for (var i = 0; i < sections.Count; i++)
        {
            var start = (int)sections[i]["line"]!;
            Assert.StartsWith("#", lines[start - 1], StringComparison.Ordinal);
            var end = i + 1 < sections.Count ? (int)sections[i + 1]["line"]! - 1 : lines.Length;
            Assert.Equal((string)sections[i]["text"]!, string.Join('\n', lines[start..end]).Trim('\n'));
            Assert.Equal(CanonicalJson.Hash((string)sections[i]["text"]!), (string)sections[i]["textHash"]!);
        }
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, Current().ToJsonString()));
    }

    // ---- Answers ----

    [Fact]
    public void Promote_waits_for_an_answer_on_every_changed_section_and_lists_them()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");

        var e = Refused(() => Promote());
        Assert.Equal("unanswered_sections", e.Code);
        Assert.Equal(["1", "3"], e.Details);
        Approve("1");
        Assert.Equal(["3"], Refused(() => Promote()).Details);
        // An unchanged section needs nothing, and saying so is refused.
        Assert.Equal("not_changed", Refused(() => Approve("2")).Code);
    }

    [Fact]
    public void Everything_approved_promotes_the_candidate_exactly_as_before()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");
        var candidateMd = File.ReadAllText(Path.Combine(FsdVersions.CandidateDir(Root, "shop"), "document.md"));
        Approve("1");
        Approve("3");

        var result = Promote();

        Assert.Equal(candidateMd, Markdown());
        Assert.Equal([("1", "Fruit", "S-0001", Plums), ("2", "Veg", "S-0002", Pears), ("3", "Nuts", "S-0003", Figs)], Sections());
        Assert.Equal(["approved", "approved"], result["answers"]!.AsArray().Select(a => (string)a!["answer"]!));
        Assert.Null(result["rejected"]);
        Consistent();
    }

    [Fact]
    public void A_rejected_change_keeps_the_section_as_it_is_now_and_an_approved_one_lands()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");
        Reject("S-0001", "the client hasn't signed this");
        Approve("3");

        var result = Promote();

        Assert.Equal([("1", "Fruit", "S-0001", Apples), ("2", "Veg", "S-0002", Pears), ("3", "Nuts", "S-0003", Figs)], Sections());
        Assert.DoesNotContain(Plums, Markdown(), StringComparison.Ordinal);
        Assert.Equal("1", (string)result["rejected"]![0]!["section"]!);
        Assert.False(Directory.Exists(FsdVersions.CandidateDir(Root, "shop")));
        Assert.Contains("\"rejected\"", File.ReadAllText(Path.Combine(DocDir, "promotions.jsonl")), StringComparison.Ordinal);
        Consistent();
    }

    [Fact]
    public void A_rejected_addition_isnt_there_and_its_id_is_never_issued()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");
        Reject("3");

        Promote();

        Assert.Equal([("1", "Fruit", "S-0001", Apples), ("2", "Veg", "S-0002", Pears)], Sections());
        Assert.Equal(["S-0001", "S-0002"], FsdVersions.Ledger(Root, "shop").Active("sections"));
        Consistent();
    }

    [Fact]
    public void A_rejected_removal_keeps_the_section_its_id_and_its_link()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");
        var link = Link("3", "Nuts", Figs);
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Assert.Equal("removed", FsdSectionApproval.Answer(repo.Repo, "shop", "S-0003", false, null, Pat)["change"]!.ToString());

        var result = Promote();

        Assert.Equal([("1", "Fruit", "S-0001", Apples), ("2", "Veg", "S-0002", Pears), ("3", "Nuts", "S-0003", Figs)], Sections());
        Assert.Empty(result["carried"]!["removed"]!.AsArray());
        var entry = TraceFile.Load(Root)["entries"]![0]!.AsObject();
        Assert.Equal((string)link["id"]!, (string)entry["id"]!);
        Assert.Equal(("3", "agree", Pat), ((string)entry["fsd"]!["section"]!, (string)entry["decision"]!, (string)entry["decided_by"]!));
        Assert.Empty(result["staleQuotes"]!.AsArray());
        Consistent();
    }

    [Fact]
    public void An_approved_removal_goes_as_it_did_before()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Apples}\n");
        Approve("S-0002");

        var result = Promote();

        Assert.Equal([("1", "Fruit", "S-0001", Apples)], Sections());
        Assert.Single(result["carried"]!["removed"]!.AsArray());
    }

    [Fact]
    public void Links_on_a_rejected_change_keep_their_decision_and_on_an_approved_one_drop_back()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Link("1", "Fruit", Apples);
        Link("2", "Veg", Pears);
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Dates}\n");
        Reject("1");
        Approve("2");

        var result = Promote();

        var bySection = TraceFile.Load(Root)["entries"]!.AsArray().OfType<JsonObject>().ToDictionary(e => (string)e["fsd"]!["section"]!);
        Assert.Equal("agree", (string?)bySection["1"]["decision"]);
        Assert.NotEqual("agree", (string?)bySection["2"]["decision"]);
        Assert.Single(result["staleQuotes"]!.AsArray());
        Consistent();
    }

    [Fact]
    public void A_mix_of_answers_whose_numbering_doesnt_fit_is_refused_not_renumbered()
    {
        Import($"#! Fruit\n{Apples}\n##! Kept\n{Pears}\n#! Veg\n{Plums}\n");
        Import($"#! Veg\n{Plums}\n##! Kept\n{Pears}\n");
        Approve("1.1");
        // Keeping Fruit as chapter 1 beside the candidate's Veg, now numbered 1, would give two sections one number.
        Reject("S-0001");

        var e = Refused(() => Promote());

        Assert.Equal("answers_dont_fit", e.Code);
        Assert.Contains(e.Details!, d => d.Contains("two sections", StringComparison.Ordinal));
        Assert.True(Directory.Exists(FsdVersions.CandidateDir(Root, "shop")));
        Assert.Equal(3, Sections().Count);
    }

    [Fact]
    public void An_answer_drops_when_the_text_it_was_given_on_changes_and_holds_when_it_doesnt()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Dates}\n");
        Approve("1");
        Approve("2");

        // A newer file: Fruit's change is the same, Veg's isn't.
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Figs}\n");

        Assert.Equal(["2"], Refused(() => Promote()).Details);
        var review = FsdSectionApproval.Review(repo.Repo, "shop")["sectionAnswers"]!.AsArray().OfType<JsonObject>().ToDictionary(r => (string)r["section"]!);
        Assert.Equal(("approved", Pat), ((string)review["1"]["answer"]!, (string)review["1"]["by"]!));
        Assert.Equal("unanswered", (string)review["2"]["answer"]!);
    }

    [Fact]
    public void An_answer_typed_into_a_file_counts_for_nothing()
    {
        Import($"#! Fruit\n{Apples}\n");
        Import($"#! Fruit\n{Plums}\n");
        var path = Path.Combine(FsdVersions.CandidateDir(Root, "shop"), "section-diff.json");
        var diff = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var e in diff["sections"]!.AsArray().OfType<JsonObject>()) e["answer"] = "approved";
        File.WriteAllText(path, diff.ToJsonString());

        Assert.Equal("unanswered_sections", Refused(() => Promote()).Code);
    }

    [Fact]
    public void A_person_can_change_their_answer_and_the_last_one_counts()
    {
        Import($"#! Fruit\n{Apples}\n");
        Import($"#! Fruit\n{Plums}\n");
        Approve("1", Sam);
        Reject("1");

        Promote();

        Assert.Equal(Apples, Sections().Single().Text);
    }

    [Fact]
    public void A_doubtful_section_is_settled_before_it_can_be_answered()
    {
        Import($"#! Fruit\n{Apples} {Pears}\n");
        Import($"#! Red\n{Apples}\n#! Green\n{Pears}\n");

        Assert.Equal("not_settled", Refused(() => Approve("1")).Code);
        FsdVersions.Resolve(Root, "shop", "1", "S-0001", false, false, Pat);
        FsdVersions.Resolve(Root, "shop", "2", null, true, false, Pat);
        Approve("1");
        Reject("2");

        Promote();

        Assert.Equal([("1", "Red", "S-0001", Apples)], Sections());
        Consistent();
    }

    // ---- --partial ----

    [Fact]
    public void A_partial_file_proposes_no_removals_and_keeps_what_it_doesnt_hold()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");
        Import($"#! Fruit\n{Plums}\n", partial: true);

        Assert.DoesNotContain(FsdVersions.Diff(Root, "shop")["sections"]!.AsArray(), e => (string)e!["kind"]! == "removed");
        Assert.Equal(["1"], Refused(() => Promote()).Details);
        Approve("1");

        var result = Promote();

        Assert.Equal([("1", "Fruit", "S-0001", Plums), ("2", "Veg", "S-0002", Pears), ("3", "Nuts", "S-0003", Figs)], Sections());
        Assert.Empty(result["carried"]!["removed"]!.AsArray());
        Consistent();
    }

    [Fact]
    public void Without_partial_a_missing_section_is_still_proposed_for_removal()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Plums}\n");

        Assert.Contains(FsdVersions.Diff(Root, "shop")["sections"]!.AsArray(), e => (string)e!["kind"]! == "removed");
        Assert.Equal(["1", "S-0002"], Refused(() => Promote()).Details);
    }

    [Fact]
    public void Partial_needs_a_current_version()
    {
        Assert.Equal("usage", Refused(() => Import($"#! Fruit\n{Apples}\n", partial: true)).Code);
    }

    // ---- A spec that moved on ----

    [Fact]
    public void A_candidate_compared_with_an_older_version_is_refused_naming_the_sections()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Plums}\n");
        // The current version moves on after the comparison: here, as if another version had been promoted with a new Fruit.
        var current = Current();
        current["original"]!["hash"] = new string('0', 64);
        var fruit = current["sections"]![0]!.AsObject();
        fruit["text"] = Dates;
        fruit["textHash"] = CanonicalJson.Hash(Dates);
        fruit.Remove("contentHash");
        File.WriteAllText(Path.Combine(DocDir, "document.json"), CanonicalJson.Serialize(current));

        var e = Refused(() => Promote());

        Assert.Equal("candidate_out_of_date", e.Code);
        Assert.Contains(e.Details!, d => d.Contains("1>1", StringComparison.Ordinal));
    }

    // ---- Readable section files (D142) ----

    [Fact]
    public void Section_files_are_written_at_the_first_import_and_at_promote_the_same_bytes_each_time()
    {
        Import($"#! Fruit\n##! Kept\n{Pears}\n#! Veg\n{Plums}\n");
        var dir = FsdSectionFiles.Dir(Root, "shop");
        // Fruit has no text of its own, so it has no file: its heading is in Kept's.
        Assert.Equal(["S-0002-kept.md", "S-0003-veg.md"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());
        var kept = File.ReadAllText(Path.Combine(dir, "S-0002-kept.md"));
        Assert.StartsWith("<!-- Generated by stangent", kept, StringComparison.Ordinal);
        Assert.Contains("under 1 Fruit", kept, StringComparison.Ordinal);
        Assert.Contains(Pears, kept, StringComparison.Ordinal);
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(dir, "S-0002-kept.md"));

        Import($"#! Fruit\n##! Kept\n{Pears}\n#! Nuts\n{Figs}\n");
        Approve("2");
        Approve("S-0003");
        Promote();

        Assert.Equal(["S-0002-kept.md", "S-0004-nuts.md"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());
        Assert.Equal(kept, File.ReadAllText(Path.Combine(dir, "S-0002-kept.md")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(dir, "S-0002-kept.md")));
        Assert.Equal(FsdSectionFiles.Write(Root, "shop", Current()), ["S-0002-kept.md", "S-0004-nuts.md"]);
    }

    // ---- The review view ----

    [Fact]
    public void The_review_view_shows_each_answer_and_the_commands_and_calls_nothing()
    {
        Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Import($"#! Fruit\n{Plums}\n#! Veg\n{Pears}\n#! Nuts\n{Figs}\n");
        Approve("1");

        var review = FsdSectionApproval.Review(repo.Repo, "shop");
        var md = FsdSectionDiff.Markdown(review) + FsdSectionApproval.ReviewMarkdown(review);

        Assert.Contains("## Review (section-approval)", md, StringComparison.Ordinal);
        Assert.Contains($"approved by {Pat}", md, StringComparison.Ordinal);
        Assert.Contains("`stangent fsd approve shop 3`", md, StringComparison.Ordinal);
        Assert.Contains("`stangent fsd reject shop 3 --why \"...\"`", md, StringComparison.Ordinal);
        Assert.Equal("", FsdSectionApproval.ReviewMarkdown(FsdVersions.Diff(Root, "shop")));
    }

    // ---- The switch off ----

    [Fact]
    public void With_the_switch_off_nothing_changes()
    {
        using var off = new FsdSectionApprovalTests(false);
        off.Import($"#! Fruit\n{Apples}\n#! Veg\n{Pears}\n");
        Assert.False(Directory.Exists(FsdSectionFiles.Dir(off.Root, "shop")));
        off.Import($"#! Fruit\n{Plums}\n");

        Assert.Equal("section_approval_off", Assert.Throws<StangentException>(() => off.Approve("1")).Code);
        Assert.Equal("section_approval_off", Assert.Throws<StangentException>(() => off.Import($"#! Fruit\n{Figs}\n", partial: true)).Code);
        Assert.Null(FsdSectionApproval.Review(off.repo.Repo, "shop")["sectionAnswers"]);
        off.Promote();

        Assert.Equal([("1", "Fruit", "S-0001", Plums)], off.Sections());
        Assert.False(Directory.Exists(FsdSectionFiles.Dir(off.Root, "shop")));
        Assert.False(File.Exists(Path.Combine(off.Root, "specs", "fsd", "history.jsonl")) && File.ReadAllText(Path.Combine(off.Root, "specs", "fsd", "history.jsonl")).Contains(StateKinds.FsdSectionAnswer, StringComparison.Ordinal));
    }

    JsonObject Link(string section, string title, string quote)
    {
        var entry = LinkRun.Entry("gap", "shop", new JsonObject { ["id"] = section, ["title"] = title }, quote, null, null, null, "code", null, null);
        entry["decision"] = "agree";
        entry["decided_by"] = Pat;
        var trace = TraceFile.Load(Root);
        trace["entries"]!.AsArray().Add(entry);
        TraceFile.Save(Root, trace);
        return entry;
    }
}
