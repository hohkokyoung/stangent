using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using static Stangent.Core.Tests.FsdLinkTests;

namespace Stangent.Core.Tests;

/// <summary>Claim ids, a judge naming ids, and links by ids (D145), with a fake AI's answers. Nothing here has run on a real AI.</summary>
public class ClaimLinkTests
{
    const string On = "components: { fsd-claims: true, claim-links: true }\n";
    const string Approval = "Orders over 50 need approval.";
    const string Refused = "Orders over 100 are refused with \"Orders over 100 need approval\".";

    const string Flows = """
        Table 9: Use Case Specification

        | ID: | UC9 |
        |---|---|
        | Name: | Login |
        | Description: | Logs the user in. |

        | Main Flow: | User | System |
        |---|---|---|
        |  | 1. Enter the name. | 2. Save the name. |
        | Other Flow: | Bad name. |  |
        |  | User | System |
        |  | 1. Enter the name. | 2. Show an error. |
        """;

    static RunStep Prepare(SpecApp app) => LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");

    static RunStep Check(SpecApp app) => LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", "1", null);

    static RunStep Answer(SpecApp app, RunStep step, params JsonObject[] links)
    {
        Write(app, step, new JsonObject { ["links"] = new JsonArray(links) });
        return Check(app);
    }

    /// <summary>A judge's answer and, if it asks, the second check saying yes to all.</summary>
    static RunStep Judged(SpecApp app, params JsonObject[] links)
    {
        var step = Answer(app, Prepare(app), links);
        if (step.Agent != LinkRun.Checker) return step;
        var agrees = links.Count(l => (string)l["state"]! == "agree");
        Write(app, step, new JsonObject { ["checks"] = new JsonArray([.. Enumerable.Range(1, agrees).Select(i => (JsonNode)new JsonObject { ["link"] = $"K{i}", ["verdict"] = "confirm", ["note"] = "n" })]) });
        return Check(app);
    }

    static JsonObject Link(string state, string? page, string? statement, params string[] claims)
    {
        var o = new JsonObject { ["state"] = state, ["note"] = "n" };
        if (claims.Length > 0) o["claims"] = new JsonArray([.. claims.Select(c => (JsonNode)c)]);
        if (page is not null) o["page"] = page;
        if (statement is not null) o["statement"] = statement;
        return o;
    }

    static string Bundle(SpecApp app, RunStep step) => SharedHead.Assemble(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md")));

    static List<JsonObject> Entries(SpecApp app) => TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().ToList();

    static void Change(SpecApp app, Action<JsonObject> edit)
    {
        var trace = TraceFile.Load(app.Root);
        edit(trace);
        TraceFile.Save(app.Root, trace);
    }

    static void Decide(SpecApp app, string decision) => Change(app, t => { foreach (var e in t["entries"]!.AsArray().OfType<JsonObject>()) e["decision"] = decision; });

    static void JudgeAgain(SpecApp app) => LinkRun.UpdateRun(app.Root, LinkRun.Key("shop", "1"), r => r.Remove("inputs"));

    static string DocFile(SpecApp app) => Path.Combine(FsdImport.DocDir(app.Root, "shop"), "document.json");

    static void SetText(SpecApp app, string text)
    {
        var doc = JsonNode.Parse(File.ReadAllText(DocFile(app)))!.AsObject();
        doc["sections"]!.AsArray().OfType<JsonObject>().First(s => (string)s["id"]! == "1")["text"] = text;
        File.WriteAllText(DocFile(app), CanonicalJson.Serialize(doc));
    }

    static void TurnOn(SpecApp app)
    {
        var path = Path.Combine(app.Root, "stangent.yaml");
        File.WriteAllText(path, File.ReadAllText(path) + On);
        typeof(SpecApp).GetProperty(nameof(SpecApp.Repo))!.SetValue(app, StangentRepo.Init(app.Root).Repo);
    }

    static StoredClaim Claim(SpecApp app, string words) => ClaimFile.Load(app.Root, "shop").All.First(c => c.Words == words);

    // ---- off ----

    [Fact]
    public void Off_nothing_is_written_and_the_judge_gets_what_it_always_got()
    {
        using var app = App("components: { fsd-claims: true }\n");
        var ids = File.ReadAllBytes(Path.Combine(FsdImport.DocDir(app.Root, "shop"), "ids.json"));
        var step = Prepare(app);
        FsdClaims.Report(app.Root, "shop", null);
        Assert.Equal(ids, File.ReadAllBytes(Path.Combine(FsdImport.DocDir(app.Root, "shop"), "ids.json")));
        Assert.False(File.Exists(ClaimFile.PathIn(app.Root, "shop")));
        Assert.Contains(Bundles.Instructions("link-judge.md"), Bundle(app, step));
        Assert.Equal("link-judge-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
    }

    [Fact]
    public void Claim_links_on_with_fsd_claims_off_is_off_and_says_it_is_blocked()
    {
        using var app = App("components: { claim-links: true }\n");
        Assert.False(ClaimFile.On(app.Repo.Config));
        Assert.Contains("fsd-claims", StangentComponents.MissingNeed(app.Repo.Config, ClaimFile.Component)!);
        Prepare(app);
        Assert.False(File.Exists(ClaimFile.PathIn(app.Root, "shop")));
    }

    // ---- ids ----

    [Fact]
    public void Doing_it_twice_changes_no_byte_and_repeats_keep_their_ids_by_order()
    {
        using var app = App(On);
        SetText(app, Flows);
        var first = ClaimFile.Sync(app.Root, "shop");
        Assert.True((int)first["issued"]! > 0);
        var ids = Path.Combine(FsdImport.DocDir(app.Root, "shop"), "ids.json");
        var (ledger, file) = (File.ReadAllBytes(ids), File.ReadAllBytes(ClaimFile.PathIn(app.Root, "shop")));
        var second = ClaimFile.Sync(app.Root, "shop");
        Assert.Equal(0, (int)second["issued"]!);
        Assert.Equal(0, (int)second["retired"]!);
        Assert.Equal(ledger, File.ReadAllBytes(ids));
        Assert.Equal(file, File.ReadAllBytes(ClaimFile.PathIn(app.Root, "shop")));
        Assert.Contains("\"claims\"", File.ReadAllText(ids));
    }

    [Fact]
    public void Exact_repeats_keep_their_order_a_change_gets_a_new_id_and_a_retired_id_never_comes_back()
    {
        using var app = App(On);
        SetText(app, "Save it.\n\nCheck it.\n\nSave it.");
        ClaimFile.Sync(app.Root, "shop");
        var before = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words is "Save it." or "Check it.").ToList();
        Assert.Equal(["Save it.", "Check it.", "Save it."], before.Select(c => c.Words));
        Assert.Equal(3, before.Select(c => c.Id).Distinct().Count());

        // Words between the repeats, with no change to them: all three stay. The second repeat goes: its id goes, and the first keeps its own.
        SetText(app, "New one.\n\nSave it.\n\nCheck it.");
        var moved = ClaimFile.Sync(app.Root, "shop");
        Assert.Equal(1, (int)moved["issued"]!);
        Assert.Equal(1, (int)moved["retired"]!);
        Assert.Equal(before[0].Id, Claim(app, "Save it.").Id);
        Assert.Equal(before[1].Id, Claim(app, "Check it.").Id);
        Assert.True(ClaimFile.Retired(app.Root, "shop", before[2].Id));

        // The words come back: a new id, not the old one.
        SetText(app, "New one.\n\nSave it.\n\nCheck it.\n\nSave it.");
        ClaimFile.Sync(app.Root, "shop");
        Assert.DoesNotContain(ClaimFile.Load(app.Root, "shop").All, c => c.Id == before[2].Id);
    }

    [Fact]
    public void The_same_words_in_another_flow_are_another_claim()
    {
        using var app = App(On);
        SetText(app, Flows);
        ClaimFile.Sync(app.Root, "shop");
        var enter = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words == "1. Enter the name.").ToList();
        Assert.Equal(2, enter.Count);
        Assert.NotEqual(enter[0].Id, enter[1].Id);
        Assert.NotEqual(enter[0].Flow, enter[1].Flow);

        // The first flow is renamed: its step is a new claim, and the other's stays.
        SetText(app, Flows.Replace("Main Flow:", "First Flow:"));
        ClaimFile.Sync(app.Root, "shop");
        var after = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words == "1. Enter the name.").ToList();
        Assert.NotEqual(enter[0].Id, after[0].Id);
        Assert.Equal(enter[1].Id, after[1].Id);
    }

    [Fact]
    public void A_ledger_deleted_catches_up_so_no_live_id_is_given_again()
    {
        using var app = App(On);
        SetText(app, "One thing.\n\nTwo things.");
        ClaimFile.Sync(app.Root, "shop");
        var held = ClaimFile.Load(app.Root, "shop").All.Select(c => c.Id).ToList();
        File.Delete(Path.Combine(FsdImport.DocDir(app.Root, "shop"), "ids.json"));
        SetText(app, "One thing.\n\nTwo things.\n\nThree things.");
        ClaimFile.Sync(app.Root, "shop");
        var now = ClaimFile.Load(app.Root, "shop").All.Select(c => c.Id).ToList();
        Assert.All(held, id => Assert.Contains(id, now));
        Assert.Equal(now.Count, now.Distinct().Count());
    }

    // ---- what's set aside ----

    [Fact]
    public void Column_headings_labels_and_flow_labels_are_set_aside_by_where_they_sit()
    {
        var kinds = FsdClaims.Split(Flows).ToDictionary(c => $"{c.Position} {c.Words}", c => c.JudgeKind);
        Assert.Equal(FsdClaims.JudgeKinds.Claim, kinds.Single(k => k.Key.EndsWith("Table 9: Use Case Specification")).Value);
        Assert.Equal(FsdClaims.JudgeKinds.LabelRow, kinds.Single(k => k.Key.EndsWith("ID: | UC9")).Value);
        Assert.Equal(FsdClaims.JudgeKinds.LabelRow, kinds.Single(k => k.Key.EndsWith("Name: | Login")).Value);
        Assert.Equal(FsdClaims.JudgeKinds.Claim, kinds.Single(k => k.Key.EndsWith("Description: | Logs the user in.")).Value);
        Assert.Equal(FsdClaims.Kinds.FlowLabel, kinds.Single(k => k.Key.EndsWith("Main Flow: | User | System")).Value);
        Assert.Equal(FsdClaims.JudgeKinds.ColumnHeading, kinds.Single(k => k.Key.EndsWith(" User | System") && !k.Key.Contains("Flow")).Value);
    }

    [Theory]
    [InlineData("| Field | Rule |\n|---|---|\n| Name | Required |", "Field | Rule")] // A first row can't be told from data by where it sits, so it stays.
    [InlineData("| ID | UC9 |\n|---|---|\n| Name | Login |", "ID | UC9")] // No colon: nothing says it's a label.
    [InlineData("| Status: | Must be active |\n|---|---|\n| Other | row |", "Status: | Must be active")] // A value of several words can say something.
    [InlineData("| | One | Two | Three |\n|---|---|---|---|\n| | 1. Do it. | 2. Done. | |", "One | Two | Three")] // Three values above two cells of steps.
    public void What_position_can_t_settle_stays_a_claim(string text, string words) =>
        Assert.Equal(FsdClaims.JudgeKinds.Claim, FsdClaims.Split(text).Single(c => c.Words == words).JudgeKind);

    // ---- the judge's input ----

    [Fact]
    public void The_judge_reads_ids_once_for_repeated_words_and_no_section_text()
    {
        using var app = App(On);
        SetText(app, Flows);
        var step = Prepare(app);
        var bundle = Bundle(app, step);
        var enter = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words == "1. Enter the name.").ToList();
        Assert.Contains(Bundles.Instructions(ClaimLinks.Instructions), bundle);
        Assert.Contains($"**{enter[0].Id}, {enter[1].Id}** (Main Flow:; Other Flow: Bad name.) 1. Enter the name.", bundle);
        Assert.Equal(1, bundle.Split("1. Enter the name.").Length - 1);
        Assert.Contains("# Headings and labels in the section", bundle);
        Assert.DoesNotContain("Its text, exactly", bundle);
        Assert.Equal(ClaimLinks.OutputSchema, (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.True(SharedHead.IsStored(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md"))));
    }

    [Fact]
    public void With_finer_verdicts_the_finer_variant_is_asked_for_and_partial_and_unclear_go_through()
    {
        using var app = App("components: { fsd-claims: true, claim-links: true, judge-finer-verdicts: true }\n");
        var step = Prepare(app);
        Assert.Contains(Bundles.Instructions(ClaimLinks.FinerInstructions), Bundle(app, step));
        Assert.Equal(ClaimLinks.FinerOutputSchema, (string)step.ToJson("x")["next"]!["schema"]!);
        var partial = Link("partial", SpecApp.Post, "R1", Claim(app, Approval).Id);
        partial["covered"] = "the limit"; partial["missing"] = "who approves";
        var unclear = Link("unclear", null, null, Claim(app, "Every order sends a receipt.").Id);
        Assert.Equal("recorded", Answer(app, step, partial, unclear).Status);
        Assert.Equal(["partial", "unclear"], Entries(app).Select(e => (string)e["state"]!).Order());
        Assert.NotNull(Entries(app).Single(e => (string)e["state"]! == "partial")["partial"]);
    }

    [Fact]
    public void Off_finer_partial_is_refused_by_the_schema()
    {
        using var app = App(On);
        var step = Prepare(app);
        var partial = Link("partial", SpecApp.Post, "R1", Claim(app, Approval).Id);
        partial["covered"] = "x"; partial["missing"] = "y";
        Assert.Equal("retry", Answer(app, step, partial).Status);
    }

    // ---- ids back, words filled by code ----

    [Fact]
    public void Code_fills_both_quotes_from_the_ids_and_the_id_comes_from_ids()
    {
        using var app = App(On);
        var step = Prepare(app);
        var claim = Claim(app, Approval);
        Assert.Equal("recorded", Answer(app, step, Link("conflict", SpecApp.Post, "R1", claim.Id)).Status);
        var e = Entries(app).Single();
        Assert.Equal(claim.Id, (string)e["fsd"]!["claim"]!);
        Assert.Equal(Approval, (string)e["fsd"]!["quote"]!);
        Assert.Equal(Refused, (string)e["page"]!["quote"]!);
        Assert.Equal(TraceFile.ClaimEntryId("conflict", "shop", claim.Section, SpecApp.Post, claim.Id, "R1"), (string)e["id"]!);
        Assert.Equal((string)e["id"]!, TraceFile.IdOf(e, "99"));
        var report = TraceCheck.Run(app.Root, null, null, fix: false);
        Assert.True((bool)report["ok"]!, report["problems"]!.ToJsonString());
    }

    [Fact]
    public void An_unknown_id_a_set_aside_id_and_one_from_another_section_take_the_one_retry()
    {
        using var app = App(On);
        SetText(app, Flows);
        var step = Prepare(app);
        var label = Claim(app, "ID: | UC9");
        var other = ClaimFile.Load(app.Root, "shop").All.First(c => c.Section != label.Section);
        var retry = Answer(app, step, Link("gap", null, null, "C-9999"), Link("gap", null, null, label.Id), Link("gap", null, null, other.Id));
        Assert.Equal("retry", retry.Status);
        Assert.Equal(3, retry.Errors!.Count);
        Assert.All(retry.Errors, e => Assert.Contains("isn't one of the claims listed", e));
        Write(app, retry, new JsonObject { ["links"] = new JsonArray(Link("gap", null, null, "C-9999")) });
        Assert.Equal("recorded", Check(app).Status);
        Assert.Empty(Entries(app));
    }

    [Fact]
    public void A_claim_is_not_named_on_an_undocumented_link_and_one_is_needed_on_the_others()
    {
        using var app = App(On);
        var step = Prepare(app);
        var retry = Answer(app, step, Link("undocumented", SpecApp.Post, "R1", Claim(app, Approval).Id), Link("gap", null, null));
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("names no claim"));
        Assert.Contains(retry.Errors!, e => e.Contains("needs claims"));
    }

    [Fact]
    public void An_undocumented_link_rests_on_no_claim_and_its_id_on_the_statement()
    {
        using var app = App(On);
        Assert.Equal("recorded", Judged(app, Link("undocumented", SpecApp.Post, "E1")).Status);
        var e = Entries(app).Single();
        Assert.Equal(TraceFile.NoClaim, (string)e["fsd"]!["claim"]!);
        Assert.Null(e["fsd"]!["quote"]);
        Assert.Equal(TraceFile.ClaimEntryId("undocumented", "shop", (string)e["fsd"]!["sectionId"]!, SpecApp.Post, TraceFile.NoClaim, "E1"), (string)e["id"]!);
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, fix: false)["ok"]!);
    }

    [Fact]
    public void An_answer_on_a_line_goes_to_all_its_claims_and_a_flow_may_answer_for_itself()
    {
        using var app = App(On);
        SetText(app, Flows);
        var step = Prepare(app);
        var enter = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words == "1. Enter the name.").ToList();
        Answer(app, step, Link("gap", null, null, enter[0].Id));
        Assert.Equal(enter.Select(c => c.Id), Entries(app).Select(e => (string)e["fsd"]!["claim"]!).Order());

        JudgeAgain(app);
        step = Prepare(app);
        Answer(app, step, Link("gap", null, null, enter[0].Id), Link("conflict", SpecApp.Post, "R1", enter[1].Id));
        Assert.Equal("gap", (string)Entries(app).Single(e => (string)e["fsd"]!["claim"]! == enter[0].Id)["state"]!);
        Assert.Equal("conflict", (string)Entries(app).Single(e => (string)e["fsd"]!["claim"]! == enter[1].Id)["state"]!);
    }

    [Fact]
    public void Two_states_for_one_claim_and_one_statement_take_the_retry()
    {
        using var app = App(On);
        var step = Prepare(app);
        var id = Claim(app, Approval).Id;
        var retry = Answer(app, step, Link("conflict", SpecApp.Post, "R1", id), Link("partial2", SpecApp.Post, "R1", id));
        Assert.Equal("retry", retry.Status);
        step = Prepare(app);
        retry = Answer(app, step, Link("conflict", SpecApp.Post, "R1", id), Link("gap", SpecApp.Post, "R1", id));
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("both conflict and gap"));
    }

    // ---- a ruling survives ----

    [Fact]
    public void The_real_bug_a_ruling_stays_on_the_same_id_whichever_words_a_judge_would_once_have_copied()
    {
        // The bug: two cuts of the same statement, two ids, so a person's ruling was lost.
        Assert.NotEqual(
            TraceFile.EntryId("conflict", "shop", "1", SpecApp.Post, Approval, Refused),
            TraceFile.EntryId("conflict", "shop", "1", SpecApp.Post, Approval, "Orders over 100 are refused"));

        using var app = App(On);
        var claim = Claim2(app);
        Judged(app, Link("conflict", SpecApp.Post, "R1", claim.Id));
        var id = (string)Entries(app).Single()["id"]!;
        Decide(app, "code-wrong");
        TraceCheck.Run(app.Root, null, null, fix: false);
        var finding = (string)Entries(app).Single()["finding"]!;

        JudgeAgain(app);
        Judged(app, Link("conflict", SpecApp.Post, "R1", claim.Id));
        var e = Entries(app).Single();
        Assert.Equal(id, (string)e["id"]!);
        Assert.Equal("code-wrong", (string?)e["decision"]);
        Assert.Equal(finding, (string?)e["finding"]);
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, fix: false)["ok"]!);
    }

    static StoredClaim Claim2(SpecApp app)
    {
        ClaimFile.Sync(app.Root, "shop");
        return Claim(app, Approval);
    }

    [Fact]
    public void A_ruling_on_agree_does_not_carry_over_to_a_conflict()
    {
        using var app = App(On);
        var claim = Claim2(app);
        Judged(app, Link("agree", SpecApp.Post, "R1", claim.Id));
        Decide(app, "agree");
        JudgeAgain(app);
        Judged(app, Link("conflict", SpecApp.Post, "R1", claim.Id));
        Assert.Null(Entries(app).Single(e => (string)e["state"]! == "conflict")["decision"]);
    }

    [Fact]
    public void A_section_that_only_moved_keeps_its_link_id_and_the_hash_has_no_number()
    {
        var e = new JsonObject { ["state"] = "gap", ["fsd"] = new JsonObject { ["doc"] = "d", ["section"] = "1", ["sectionId"] = "S-0001", ["claim"] = "C-0001" } };
        Assert.Equal(TraceFile.IdOf(e, "1"), TraceFile.IdOf(e, "7"));

        using var app = App(On);
        var bundle = Bundle(app, Prepare(app));
        Assert.DoesNotContain("section: `1`", bundle);
    }

    [Fact]
    public void A_rephrased_statement_on_the_same_id_can_not_keep_a_ruling()
    {
        using var app = App(On);
        var claim = Claim2(app);
        Judged(app, Link("conflict", SpecApp.Post, "R1", claim.Id));
        Decide(app, "code-wrong");
        TraceCheck.Run(app.Root, null, null, fix: false);
        // What the page says now isn't what was ruled on: the same as a quote that stopped matching, however the words were made.
        Change(app, t => t["entries"]![0]!["page"]!["quote"] = "Orders over 100 are refused");
        var report = TraceCheck.Run(app.Root, null, null, fix: true);
        Assert.Contains((string)Entries(app).Single()["id"]!, report["staleQuotes"]!.AsArray().Select(s => (string)s!));
        Assert.Null(Entries(app).Single()["decision"]);
    }

    // ---- trace check ----

    [Fact]
    public void Trace_check_refuses_a_claim_nobody_gave_out_and_an_edited_id()
    {
        using var app = App(On);
        var claim = Claim2(app);
        Judged(app, Link("gap", null, null, claim.Id));
        Change(app, t => t["entries"]![0]!["fsd"]!["claim"] = "C-8888");
        var problems = TraceCheck.Run(app.Root, null, null, fix: false)["problems"]!.AsArray().Select(p => (string)p!).ToList();
        Assert.Contains(problems, p => p.Contains("edited by hand"));
        Assert.Contains(problems, p => p.Contains("claim C-8888 doesn't exist"));
    }

    [Fact]
    public void A_claim_whose_words_left_the_section_or_was_retired_drops_the_decision_and_says_so()
    {
        using var app = App(On);
        var claim = Claim2(app);
        Judged(app, Link("gap", null, null, claim.Id));
        Decide(app, "code-wrong");
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, fix: false)["ok"]!);

        // Promoted, not synced yet: claims.json still has it, and the section no longer does.
        SetText(app, "Orders over 60 need approval.");
        Assert.Contains((string)Entries(app).Single()["id"]!, TraceCheck.Run(app.Root, null, null, fix: false)["staleQuotes"]!.AsArray().Select(s => (string)s!));

        // Synced: the id is retired.
        ClaimFile.Sync(app.Root, "shop");
        var report = TraceCheck.Run(app.Root, null, null, fix: true);
        Assert.Equal(claim.Id, (string)report["retiredClaims"]![0]!["claim"]!);
        Assert.Null(Entries(app).Single()["decision"]);
        Assert.Equal("recheck", TraceFindings.Standing(Entries(app).Single()));
    }

    [Fact]
    public void A_quote_edited_on_a_link_by_ids_is_stale_though_the_id_does_not_read_it()
    {
        using var app = App(On);
        var claim = Claim2(app);
        Judged(app, Link("gap", null, null, claim.Id));
        Change(app, t => t["entries"]![0]!["fsd"]!["quote"] = "Every order sends a receipt.");
        Assert.Single(TraceCheck.Run(app.Root, null, null, fix: false)["staleQuotes"]!.AsArray());
    }

    [Fact]
    public void Trace_check_accepts_entries_by_ids_with_the_switch_off_too()
    {
        using var app = App(On);
        Judged(app, Link("gap", null, null, Claim2(app).Id));
        var path = Path.Combine(app.Root, "stangent.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace(On, ""));
        var report = TraceCheck.Run(app.Root, null, null, fix: false);
        Assert.True((bool)report["ok"]!, report["problems"]!.ToJsonString());
    }

    // ---- the first time the switch goes on ----

    /// <summary>A link judged the old way, with the words a judge chose, decided by a person.</summary>
    static string OldRuling(SpecApp app, string state, string? pageQuote, string? statement, string decision, string fsdQuote = "Orders over 50 need")
    {
        var step = Prepare(app);
        var link = new JsonObject { ["state"] = state, ["fsdQuote"] = fsdQuote, ["note"] = "n" };
        if (statement is not null) { link["page"] = SpecApp.Post; link["statement"] = statement; link["pageQuote"] = pageQuote; }
        step = Answer(app, step, link);
        if (step.Agent == LinkRun.Checker)
        {
            Write(app, step, new JsonObject { ["checks"] = new JsonArray(new JsonObject { ["link"] = "K1", ["verdict"] = "confirm", ["note"] = "n" }) });
            Check(app);
        }
        Change(app, t =>
        {
            var e = t["entries"]!.AsArray().OfType<JsonObject>().Last();
            e["decision"] = decision;
            e["decided_by"] = "a person";
        });
        TraceCheck.Run(app.Root, null, null, fix: false);
        return (string)Entries(app).Last()["id"]!;
    }

    [Fact]
    public void An_old_ruling_on_the_same_statement_carries_to_the_new_id_even_where_it_quoted_less()
    {
        using var app = App();
        var old = OldRuling(app, "conflict", "Orders over 100 are refused", "R1", "code-wrong");
        var baseTrace = TraceFile.Parse(File.ReadAllText(TraceFile.PathIn(app.Root)));
        var finding = (string)Entries(app).Single()["finding"]!;

        TurnOn(app);
        Judged(app, Link("conflict", SpecApp.Post, "R1", Claim2(app).Id));
        var e = Entries(app).Single();
        Assert.NotEqual(old, (string)e["id"]!);
        Assert.Equal("code-wrong", (string?)e["decision"]);
        Assert.Equal("a person", (string?)e["decided_by"]);
        Assert.Equal(finding, (string?)e["finding"]);
        Assert.Equal(old, (string?)e["carried_from"]);

        // Against the base it's not a typed decision, and a merge names nobody new.
        var report = TraceCheck.Run(app.Root, null, baseTrace, fix: false);
        Assert.True((bool)report["ok"]!, report["problems"]!.ToJsonString());
        Assert.Equal(0, TraceCheck.Attribute(app.Root, baseTrace, "the merger"));
        Assert.Equal("a person", (string?)Entries(app).Single()["decided_by"]);
    }

    [Fact]
    public void An_old_ruling_goes_to_every_flow_with_those_words_and_one_finding_id_stays_on_one()
    {
        using var app = App();
        SetText(app, Flows);
        var old = OldRuling(app, "gap", null, null, "fsd-wrong", "1. Enter the name.");
        TurnOn(app);
        var step = Prepare(app);
        var enter = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words == "1. Enter the name.").ToList();
        Answer(app, step, Link("gap", null, null, enter[0].Id));
        var entries = Entries(app);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("fsd-wrong", (string?)e["decision"]));
        Assert.All(entries, e => Assert.Equal(old, (string?)e["carried_from"]));
        var report = TraceCheck.Run(app.Root, null, null, fix: false);
        Assert.True((bool)report["ok"]!, report["problems"]!.ToJsonString());
        Assert.Equal(2, Entries(app).Select(e => (string)e["finding"]!).Distinct().Count());
    }

    [Fact]
    public void A_different_state_or_statement_does_not_carry_and_the_old_finding_is_kept_as_gone()
    {
        using var app = App();
        OldRuling(app, "conflict", "Orders over 100 are refused", "R1", "code-wrong");
        TurnOn(app);
        Judged(app, Link("conflict", SpecApp.Post, "E1", Claim2(app).Id));
        var entries = Entries(app);
        Assert.Null(entries.Single(e => e["fsd"]!["claim"] is not null)["decision"]);
        var gone = entries.Single(e => e["fsd"]!["claim"] is null);
        Assert.True(TraceFindings.IsGone(gone));
        Assert.Equal("code-wrong", (string?)gone["standing_note"]!["was"]);
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, fix: false)["ok"]!);
    }

    [Fact]
    public void A_quote_spanning_two_claims_does_not_carry()
    {
        using var app = App();
        OldRuling(app, "gap", null, null, "fsd-wrong", "The orders screen has a Place order button. Orders over 50 need approval.");
        TurnOn(app);
        Judged(app, Link("gap", null, null, Claim2(app).Id));
        Assert.Single(Entries(app), e => TraceFindings.IsGone(e));
        Assert.Null(Entries(app).Single(e => !TraceFindings.IsGone(e))["decision"]);
    }

    [Fact]
    public void A_decided_agree_link_nothing_matches_is_kept_as_gone_not_dropped()
    {
        using var app = App();
        var old = OldRuling(app, "agree", "Orders over 100 are refused", "R1", "agree");
        TurnOn(app);
        Judged(app, Link("gap", null, null, Claim2(app).Id));
        var gone = Entries(app).Single(e => (string)e["id"]! == old);
        Assert.True(TraceFindings.IsGone(gone));
        Assert.Equal("agree", (string?)gone["standing_note"]!["was"]);
        var report = TraceCheck.Run(app.Root, null, null, fix: false);
        Assert.True((bool)report["ok"]!, report["problems"]!.ToJsonString());
        Assert.Contains(report["gone"]!.AsArray(), g => (string?)g!["entry"] == old);
        Assert.Contains(old, report["queues"]!["gone"]!.AsArray().Select(g => (string?)g));
    }

    [Fact]
    public void A_matching_agree_ruling_carries_and_two_old_rulings_on_one_new_link_leave_the_second_gone()
    {
        using var app = App();
        OldRuling(app, "agree", "Orders over 100 are refused", "R1", "agree");
        Change(app, t =>
        {
            var copy = t["entries"]![0]!.DeepClone().AsObject();
            copy["fsd"]!["quote"] = "over 50 need approval";
            copy["id"] = TraceFile.IdOf(copy, (string)copy["fsd"]!["section"]!);
            copy["decision"] = "accepted";
            t["entries"]!.AsArray().Add(copy);
        });
        TurnOn(app);
        Judged(app, Link("agree", SpecApp.Post, "R1", Claim2(app).Id));
        var entries = Entries(app);
        Assert.NotNull(entries.Single(e => e["fsd"]!["claim"] is not null)["decision"]);
        Assert.Single(entries, e => TraceFindings.IsGone(e));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void A_section_whose_requirements_are_built_stays_on_its_requirements_with_the_switch_on()
    {
        using var app = App(On);
        var section = SpecIndex.Load(app.Root).Sections[("shop", "1")];
        Assert.Null(LinkRun.Requirements(app.Root, "shop", section));
        Assert.DoesNotContain("requirement-judge", (string)Prepare(app).Agent!);
    }

    // ---- unanswered ----

    [Fact]
    public void Fsd_check_lists_claims_with_no_link_with_a_count_and_never_fails()
    {
        using var app = App(On);
        var step = Prepare(app);
        var done = Answer(app, step, Link("gap", null, null, Claim(app, Approval).Id));
        Assert.Equal("recorded", done.Status);
        var json = done.ToJson("x");
        Assert.Equal(2, (int)json["unansweredCount"]!);
        Assert.Contains(json["unanswered"]!.AsArray(), u => (string?)u!["words"] == "Every order sends a receipt.");
        Assert.DoesNotContain(json["unanswered"]!.AsArray(), u => (string?)u!["words"] == Approval);
        Assert.Equal(2, LinkRun.LoadRuns(app.Root)[LinkRun.Key("shop", "1")]!["unansweredClaims"]!.AsArray().Count);
    }

    [Fact]
    public void A_set_aside_row_is_never_unanswered()
    {
        using var app = App(On);
        SetText(app, Flows);
        var done = Answer(app, Prepare(app));
        Assert.DoesNotContain(done.ToJson("x")["unanswered"]!.AsArray(), u => ((string?)u!["words"])!.StartsWith("ID:"));
    }

    [Fact]
    public void Off_a_run_has_no_unanswered_field()
    {
        using var app = App();
        var step = Prepare(app);
        Write(app, step, new JsonObject { ["links"] = new JsonArray() });
        var json = Check(app).ToJson("x");
        Assert.Null(json["unanswered"]);
        Assert.Null(LinkRun.LoadRuns(app.Root)[LinkRun.Key("shop", "1")]!["unansweredClaims"]);
    }

    // ---- the checker and the disprove step ----

    [Fact]
    public void The_second_check_reads_code_s_words_and_what_it_raises_stays_on_the_claim()
    {
        using var app = App(On);
        var claim = Claim2(app);
        var step = Answer(app, Prepare(app), Link("agree", SpecApp.Post, "R1", claim.Id));
        Assert.Equal(LinkRun.Checker, step.Agent);
        var bundle = SharedHead.Assemble(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "checker.md")));
        Assert.Contains($"FSD \"{Approval}\"", bundle);
        Assert.Contains($"R1 \"{Refused}\"", bundle);
        Write(app, step, new JsonObject { ["checks"] = new JsonArray(new JsonObject
        {
            ["link"] = "K1", ["verdict"] = "dispute", ["state"] = "conflict", ["note"] = "100 is not 50", ["fsdQuote"] = "Orders over 50 need", ["pageQuote"] = "Orders over 100 are",
        }) });
        Assert.Equal("recorded", Check(app).Status);
        var e = Entries(app).Single();
        Assert.Equal("conflict", (string)e["state"]!);
        Assert.Equal("disputed", (string)e["check"]!);
        Assert.Equal(claim.Id, (string)e["fsd"]!["claim"]!);
        Assert.Equal(Approval, (string)e["fsd"]!["quote"]!);
        Assert.Equal(Refused, (string)e["page"]!["quote"]!);
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, fix: false)["ok"]!);
    }

    [Fact]
    public void The_second_check_confirming_records_one_entry_per_claim_of_the_line()
    {
        using var app = App(On);
        SetText(app, Flows);
        var step = Prepare(app);
        var enter = ClaimFile.Load(app.Root, "shop").All.Where(c => c.Words == "1. Enter the name.").ToList();
        step = Answer(app, step, Link("agree", SpecApp.Post, "R1", enter[0].Id));
        Write(app, step, new JsonObject { ["checks"] = new JsonArray(new JsonObject { ["link"] = "K1", ["verdict"] = "confirm", ["note"] = "n" }) });
        Assert.Equal("recorded", Check(app).Status);
        Assert.Equal(2, Entries(app).Count);
        Assert.All(Entries(app), e => Assert.Equal("confirmed", (string?)e["check"]));
    }

    [Fact]
    public void The_disprove_step_takes_a_finding_code_filled_in()
    {
        using var app = App("components: { fsd-claims: true, claim-links: true, disprove: true }\n");
        SavedWorkTests.CleanGraph(app);
        var claim = Claim2(app);
        Judged(app, Link("conflict", SpecApp.Post, "R1", claim.Id));
        TraceCheck.Run(app.Root, null, null, fix: false);
        var step = DisproveRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.True(step.Agent == DisproveRun.Disprover, step.ToJson("x").ToJsonString());
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "bundle.md"));
        Assert.Contains(Approval, bundle);
        Assert.Contains(Refused, bundle);
    }
}
