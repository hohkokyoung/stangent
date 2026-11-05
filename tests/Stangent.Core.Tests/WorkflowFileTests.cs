using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using static Stangent.Core.Tests.FsdLinkTests;

namespace Stangent.Core.Tests;

/// <summary>Workflow files (D144, D147), from fake links. Nothing here has run on a real judge.</summary>
public class WorkflowFileTests
{
    const string On = "components: { fsd-claims: true, claim-links: true, workflow-files: true }\n";
    const string Rule = "Orders over 100 are refused with \"Orders over 100 need approval\".";

    const string Flows = """
        | Main Flow: | User | System |
        |---|---|---|
        |  | 1. Enter the name. | 2. Save the name. |
        | Other Flow: | Bad name. |  |
        |  | User | System |
        |  | 1. Enter the name. | 2. Show an error. |
        """;

    static string DocFile(SpecApp app) => Path.Combine(FsdImport.DocDir(app.Root, "shop"), "document.json");

    static void EditSection(SpecApp app, Action<JsonObject> edit)
    {
        var doc = JsonNode.Parse(File.ReadAllText(DocFile(app)))!.AsObject();
        edit(doc["sections"]!.AsArray().OfType<JsonObject>().First(s => (string)s["id"]! == "1"));
        File.WriteAllText(DocFile(app), CanonicalJson.Serialize(doc));
    }

    static SpecApp Fixture(string text = Flows)
    {
        var app = App(On);
        EditSection(app, s => s["text"] = text);
        ClaimFile.Sync(app.Root, "shop");
        return app;
    }

    static string Dir(SpecApp app) => WorkflowFiles.DirIn(app.Root, "shop");

    static JsonObject Run(SpecApp app) => WorkflowFiles.Command(app.Repo.Config, app.Root, "shop");

    static StoredClaim Step(SpecApp app, string words, string? flowHas = null) =>
        ClaimFile.Load(app.Root, "shop").All.First(c => c.Words == words && (flowHas is null || c.Flow!.Contains(flowHas, StringComparison.Ordinal)));

    static JsonObject Entry(StoredClaim claim, string state = "agree", string? page = SpecApp.Post, string? statement = "R1", string? check = "confirmed") => new()
    {
        ["id"] = "L" + claim.Id + state, ["state"] = state,
        ["fsd"] = new JsonObject { ["doc"] = "shop", ["section"] = "1", ["sectionId"] = claim.Section, ["title"] = "Orders", ["claim"] = claim.Id, ["quote"] = claim.Words },
        ["page"] = page is null ? null : new JsonObject { ["id"] = page, ["statement"] = statement, ["quote"] = "the words a judge copied" },
        ["by"] = "judge", ["check"] = check, ["note"] = "n", ["decision"] = null, ["decided_by"] = null,
    };

    static JsonObject Finding(StoredClaim claim, string state = "conflict", string? verdict = null, string? review = null, string? decision = null)
    {
        var e = Entry(claim, state, check: null);
        e["finding"] = "X-0007";
        if (verdict is not null) { e["verdict"] = verdict; e["disproof_hash"] = TraceFile.DisproofHash(verdict, null); }
        if (review is not null)
        {
            var note = new JsonObject { ["by"] = "Ann", ["why"] = "w" };
            e["review"] = review;
            e["review_note"] = note;
            e["review_hash"] = TraceFile.ReviewHash(review, note, (string?)e["disproof_hash"]);
        }
        if (decision is not null) { e["decision"] = decision; e["decided_by"] = "Ann"; }
        return e;
    }

    static void Links(SpecApp app, params JsonObject[] entries)
    {
        var trace = TraceFile.Empty();
        trace["entries"] = new JsonArray([.. entries.Cast<JsonNode>()]);
        TraceFile.Save(app.Root, trace);
    }

    /// <summary>The main flow's two steps, the first as given and the second agreeing.</summary>
    static void MainFlow(SpecApp app, JsonObject first) => Links(app, first, Entry(Step(app, "2. Save the name.")));

    static string FlowFile(SpecApp app, string heading) =>
        Directory.EnumerateFiles(Dir(app), "*.md", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "index.md").Select(File.ReadAllText).Single(t => t.Split('\n')[2].StartsWith($"# {heading} ·", StringComparison.Ordinal));

    static string Heading(string file) => file.Split('\n')[2];

    static string Index(SpecApp app) => File.ReadAllText(Path.Combine(Dir(app), "index.md"));

    static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllBytes);

    // ---- off ----

    [Theory]
    [InlineData("components: { fsd-claims: true, claim-links: true }\n", "workflow-files is off")]
    [InlineData("components: { fsd-claims: true, workflow-files: true }\n", "workflow-files needs claim-links")]
    [InlineData("components: { claim-links: true, workflow-files: true }\n", "claim-links needs fsd-claims")]
    public void Off_it_says_so_and_writes_nothing(string switches, string says)
    {
        using var app = App(switches);
        var before = Snapshot(app.Root);
        var result = Run(app);
        Assert.False((bool)result["written"]!);
        Assert.Contains(says, (string)result["off"]!);
        Assert.False(Directory.Exists(Path.Combine(app.Root, "specs", "workflows")));
        Assert.Equal(before.Keys.Order(), Snapshot(app.Root).Keys.Order());
    }

    [Fact]
    public void Off_by_default_and_named_as_needing_claim_links()
    {
        Assert.False(StangentComponents.IsOn(new StangentConfig(), WorkflowFiles.Component));
        Assert.Equal(["claim-links"], StangentComponents.All.Single(c => c.Name == WorkflowFiles.Component).Needs);
    }

    // ---- a security flag (D148) ----

    static JsonObject WithFlag(JsonObject e, string? answer = null)
    {
        var flag = new JsonObject { ["why"] = "The server trusts the name it was sent.", ["path"] = "a.cs", ["start"] = 2, ["end"] = 2, ["quote"] = "save(name)", ["symbol"] = "s", ["commit"] = new string('a', 40), ["inputs"] = new string('b', 64) };
        e["security"] = flag;
        e["security_hash"] = TraceFile.SecurityHash(flag);
        e["security_state"] = answer ?? "unanswered";
        if (answer is null) return e;
        var note = new JsonObject { ["by"] = "Ann", ["why"] = "read it" };
        e["security_note"] = note;
        e["security_answer_hash"] = TraceFile.SecurityAnswerHash(answer, note, (string?)e["security_hash"]);
        return e;
    }

    [Fact]
    public void A_flagged_unanswered_finding_makes_a_flow_open_with_the_flag_on_the_step_even_when_closed()
    {
        using var app = Fixture();
        MainFlow(app, WithFlag(Finding(Step(app, "1. Enter the name.", "Main"), verdict: "refuted", review: "confirmed")));
        var result = Run(app);
        Assert.Equal(1, (int)result["statuses"]!["open"]!);
        var file = FlowFile(app, "Main Flow:");
        Assert.Equal("# Main Flow: · OPEN, 1 open question", Heading(file));
        Assert.Contains("· open\n", file);
        Assert.Contains("**security flag, unanswered:** The server trusts the name it was sent. (`a.cs:2-2`)", file);
    }

    [Fact]
    public void An_answered_flag_opens_nothing_and_is_shown_with_who_answered()
    {
        using var app = Fixture();
        MainFlow(app, WithFlag(Finding(Step(app, "1. Enter the name.", "Main"), verdict: "refuted", review: "confirmed"), "not-security"));
        var result = Run(app);
        Assert.Equal(0, (int)result["statuses"]!["open"]!);
        var file = FlowFile(app, "Main Flow:");
        Assert.StartsWith("# Main Flow: · MATCHES", Heading(file));
        Assert.Contains("security flag not-security by Ann", file);
    }

    [Fact]
    public void A_typed_answer_leaves_the_flow_open()
    {
        using var app = Fixture();
        var e = WithFlag(Finding(Step(app, "1. Enter the name.", "Main"), verdict: "refuted", review: "confirmed"));
        e["security_state"] = "not-security";
        MainFlow(app, e);
        Assert.Equal(1, (int)Run(app)["statuses"]!["open"]!);
    }

    [Fact]
    public void A_flow_that_differs_stays_so_and_still_shows_and_counts_the_flag()
    {
        using var app = Fixture();
        MainFlow(app, WithFlag(Finding(Step(app, "1. Enter the name.", "Main"), decision: "code-wrong")));
        Assert.Equal(1, (int)Run(app)["statuses"]!["differs"]!);
        var file = FlowFile(app, "Main Flow:");
        Assert.Equal("# Main Flow: · DIFFERS, 1 open question", Heading(file));
        Assert.Contains("**security flag, unanswered:**", file);
    }

    [Fact]
    public void A_flag_on_a_link_no_step_holds_is_named_in_the_flow_file_and_the_index()
    {
        using var app = Fixture();
        var older = Finding(Step(app, "1. Enter the name.", "Main"));
        older["fsd"]!.AsObject().Remove("claim");
        MainFlow(app, WithFlag(older));
        var result = Run(app);
        Assert.Equal(1, (int)result["securityFlagsOnNoStep"]!);
        Assert.Contains("Unanswered security flag on this section, on no step here: X-0007.", FlowFile(app, "Main Flow:"));
        Assert.Contains("Unanswered security flag on no step: X-0007.", Index(app));
    }

    [Fact]
    public void With_no_flag_the_files_say_nothing_of_it()
    {
        using var app = Fixture();
        MainFlow(app, Finding(Step(app, "1. Enter the name.", "Main")));
        var result = Run(app);
        Assert.Null(result["securityFlagsOnNoStep"]);
        Assert.DoesNotContain("security", FlowFile(app, "Main Flow:"));
        Assert.DoesNotContain("security", Index(app));
    }

    // ---- statuses ----

    [Fact]
    public void Matches_when_every_step_has_an_agree_link_and_the_index_counts_it()
    {
        using var app = Fixture();
        MainFlow(app, Entry(Step(app, "1. Enter the name.", "Main")));
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.Equal("# Main Flow: · MATCHES", Heading(main));
        var id = Step(app, "1. Enter the name.", "Main").Id;
        Assert.Contains($"**1. Enter the name.** `{id}` · agree\n- `agree` ↔ `{SpecApp.Post}` R1: \"{Rule}\"\n", main);
        Assert.Contains("1 of 2 flows match, 1 not answered.", Index(app));
    }

    [Fact]
    public void Not_judged_when_no_link_names_a_claim_and_older_links_are_counted_not_placed()
    {
        using var app = Fixture();
        var old = Entry(Step(app, "1. Enter the name.", "Main"));
        ((JsonObject)old["fsd"]!).Remove("claim");
        Links(app, old);
        var result = Run(app);
        Assert.Equal(2, (int)result["statuses"]!["not judged"]!);
        var main = FlowFile(app, "Main Flow:");
        Assert.Equal("# Main Flow: · NOT JUDGED", Heading(main));
        Assert.Contains("· not judged\n", main);
        Assert.Contains("1 older link on this section names no claim", main);
        Assert.Contains("0 of 2 flows match, 2 not judged.", Index(app));
        Assert.Contains("1 link on no flow's step.", Index(app));
    }

    [Fact]
    public void Not_answered_when_the_section_has_links_and_a_step_has_none()
    {
        using var app = Fixture();
        Links(app, Entry(Step(app, "2. Save the name.")));
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.Equal("# Main Flow: · NOT ANSWERED", Heading(main));
        Assert.Contains("· not answered\n", main);
        Assert.Contains("· agree\n", main);
    }

    [Fact]
    public void A_judges_link_that_names_no_claim_still_says_the_section_was_judged()
    {
        using var app = Fixture();
        var undocumented = Finding(Step(app, "2. Save the name."), "undocumented");
        undocumented["fsd"]!["claim"] = TraceFile.NoClaim;
        Links(app, undocumented);
        var result = Run(app);
        Assert.Equal(2, (int)result["statuses"]!["not answered"]!);
        Assert.Equal(1, (int)result["linksOnNoStep"]!);
    }

    [Theory]
    [InlineData(null, null, null, "OPEN, 1 open question", "no verdict · no decision · with ba")]
    [InlineData("cant-tell", null, null, "OPEN, 1 open question", "verdict cant-tell · no decision · with ba")]
    [InlineData("refuted", null, null, "OPEN, 1 open question", "verdict refuted, not confirmed · no decision · with developer-confirm")]
    [InlineData("unreachable", null, null, "OPEN, 1 open question", "verdict unreachable, not confirmed · no decision · with developer-confirm")]
    [InlineData("refuted", "sent-back", null, "OPEN, 1 open question", "verdict refuted, sent back · no decision · with ba")]
    [InlineData("refuted", "confirmed", null, "MATCHES", "verdict refuted, confirmed by Ann · no decision · closed")]
    [InlineData("stands", null, null, "DIFFERS, 1 open question", "verdict stands · no decision · with ba")]
    [InlineData(null, null, "code-wrong", "DIFFERS", "no verdict · decision code-wrong by Ann")]
    [InlineData("refuted", null, "fsd-wrong", "DIFFERS", "verdict refuted, not confirmed · decision fsd-wrong by Ann")]
    [InlineData(null, null, "accepted", "DIFFERS", "no verdict · decision accepted by Ann")]
    [InlineData(null, null, "not-a-difference", "MATCHES", "no verdict · decision not-a-difference by Ann")]
    [InlineData(null, null, "defer", "OPEN, 1 open question", "no verdict · decision defer by Ann")]
    [InlineData(null, null, "page-wrong", "OPEN, 1 open question", "no verdict · decision page-wrong by Ann · with page-owner")]
    public void A_flow_with_one_finding_at_each_stage(string? verdict, string? review, string? decision, string status, string line)
    {
        using var app = Fixture();
        MainFlow(app, Finding(Step(app, "1. Enter the name.", "Main"), "conflict", verdict, review, decision));
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.Equal($"# Main Flow: · {status}", Heading(main));
        Assert.Contains($"· `X-0007` · {line}\n", main);
    }

    [Fact]
    public void A_refuted_finding_nobody_confirmed_is_open_and_a_typed_confirmation_closes_nothing()
    {
        using var app = Fixture();
        var typed = Finding(Step(app, "1. Enter the name.", "Main"), "conflict", "refuted", "confirmed");
        typed["review_hash"] = "typed by hand";
        MainFlow(app, typed);
        Run(app);
        Assert.StartsWith("# Main Flow: · OPEN", Heading(FlowFile(app, "Main Flow:")));
    }

    [Fact]
    public void Only_a_conflict_that_stands_differs_before_a_person_rules()
    {
        using var app = Fixture();
        MainFlow(app, Finding(Step(app, "1. Enter the name.", "Main"), "gap", "stands"));
        Run(app);
        Assert.StartsWith("# Main Flow: · OPEN", Heading(FlowFile(app, "Main Flow:")));
    }

    [Fact]
    public void A_question_beside_an_agree_link_is_counted_and_on_its_own_it_is_what_is_open()
    {
        using var app = Fixture();
        var main = Step(app, "1. Enter the name.", "Main");
        var other = Step(app, "1. Enter the name.", "Other");
        Links(app, Entry(main), Finding(main, "unclear"), Entry(Step(app, "2. Save the name.")), Finding(other, "unclear"), Entry(Step(app, "2. Show an error.")));
        Run(app);
        Assert.Equal("# Main Flow: · MATCHES, 1 open question", Heading(FlowFile(app, "Main Flow:")));
        Assert.Equal("# Other Flow: Bad name. · OPEN, 1 open question", Heading(FlowFile(app, "Other Flow: Bad name.")));
    }

    [Fact]
    public void A_disputed_agree_link_is_not_an_agree_and_a_gone_finding_is_shown_but_answers_nothing()
    {
        using var app = Fixture();
        var gone = Finding(Step(app, "2. Save the name."));
        var note = new JsonObject { ["was"] = null, ["by"] = null, ["why"] = "w" };
        gone["standing"] = "gone";
        gone["standing_note"] = note;
        gone["standing_hash"] = TraceFile.StandingHash("gone", note);
        Links(app, Entry(Step(app, "1. Enter the name.", "Main"), check: "disputed"), gone);
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.Equal("# Main Flow: · OPEN, 1 open question", Heading(main));
        Assert.Contains("· disputed by the second check\n", main);
        Assert.Contains("· gone\n", main);
        Assert.Contains("· not answered\n", main);
    }

    // ---- what a step shows ----

    [Fact]
    public void A_statements_words_come_from_the_page_and_one_no_longer_there_says_so()
    {
        using var app = Fixture();
        Links(app, Entry(Step(app, "1. Enter the name.", "Main")), Entry(Step(app, "2. Save the name."), statement: "R99"), NoPage(Finding(Step(app, "2. Save the name."), "gap")));
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.Contains(Rule, main);
        Assert.DoesNotContain("the words a judge copied", main);
        Assert.Contains($"`{SpecApp.Post}` R99: (no such statement on the page now)", main);
        Assert.Contains("`gap` ↔ no page", main);
    }

    static JsonObject NoPage(JsonObject e)
    {
        e["page"] = null;
        return e;
    }

    [Fact]
    public void A_step_shared_by_two_flows_appears_in_each_with_its_own_claim()
    {
        using var app = Fixture();
        var main = Step(app, "1. Enter the name.", "Main");
        var other = Step(app, "1. Enter the name.", "Other");
        Assert.NotEqual(main.Id, other.Id);
        Links(app, Entry(main), Finding(other));
        Run(app);
        Assert.Contains($"**1. Enter the name.** `{main.Id}` · agree", FlowFile(app, "Main Flow:"));
        Assert.Contains($"**1. Enter the name.** `{other.Id}` · open", FlowFile(app, "Other Flow: Bad name."));
    }

    [Fact]
    public void Steps_read_by_their_own_numbers_when_they_all_have_distinct_ones_and_otherwise_as_they_sit()
    {
        using var app = Fixture("""
            | Main Flow: | User | System |
            |---|---|---|
            |  | 1. Enter. | 3. Save. |
            |  | 2. Submit. | 4. Show. |
            | Other Flow: | Bad. |  |
            |  | User | System |
            |  | 1. Enter. | 2. Save. |
            |  | 1. Again. | 2. Show. |
            """);
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.True(main.IndexOf("**1. Enter.**", StringComparison.Ordinal) < main.IndexOf("**2. Submit.**", StringComparison.Ordinal));
        Assert.True(main.IndexOf("**2. Submit.**", StringComparison.Ordinal) < main.IndexOf("**3. Save.**", StringComparison.Ordinal));
        var other = FlowFile(app, "Other Flow: Bad.");
        Assert.True(other.IndexOf("**2. Save.**", StringComparison.Ordinal) < other.IndexOf("**1. Again.**", StringComparison.Ordinal));
    }

    [Fact]
    public void A_step_a_page_break_cut_appears_as_one_as_it_is_in_claims_json()
    {
        using var app = Fixture("""
            | Main Flow: | User | System |
            |---|---|---|
            |  | 1. Enter the | 2. Save it. |
            |  | name. |  |
            """);
        Assert.Contains(ClaimFile.Load(app.Root, "shop").All, c => c.Words == "1. Enter the name.");
        Run(app);
        var main = FlowFile(app, "Main Flow:");
        Assert.Contains("**1. Enter the name.**", main);
        Assert.Equal(2, main.Split("**", StringSplitOptions.None).Length / 2);
    }

    // ---- what goes in ----

    [Fact]
    public void A_document_with_no_flows_has_only_an_index_that_says_so()
    {
        using var app = App(On);
        ClaimFile.Sync(app.Root, "shop");
        var result = Run(app);
        Assert.Equal(0, (int)result["flows"]!);
        Assert.Equal(2, (int)result["sectionsWithNoFlows"]!);
        Assert.Equal(["index.md"], Directory.EnumerateFiles(Dir(app), "*", SearchOption.AllDirectories).Select(Path.GetFileName));
        Assert.Contains("2 of 2 sections had no flows, so they have no file here.", Index(app));
    }

    [Fact]
    public void Sections_with_none_and_claims_in_no_flow_are_counted()
    {
        using var app = Fixture("A sentence before.\n\n" + Flows);
        var result = Run(app);
        Assert.Equal(1, (int)result["sectionsWithNoFlows"]!);
        Assert.Equal(1, (int)result["claimsInNoFlow"]!);
        Assert.Contains("1 of 2 sections had no flows", Index(app));
        Assert.Contains("1 claim in these use cases is in no flow", Index(app));
    }

    // ---- names ----

    [Fact]
    public void Two_flows_with_the_same_words_do_not_collide()
    {
        using var app = Fixture(Flows + "\n| Other Flow: | Bad name. |  |\n|  | User | System |\n|  | 1. Try again. | 2. Refuse it. |");
        var result = Run(app);
        Assert.Equal(3, (int)result["flows"]!);
        Assert.Equal(4, Directory.EnumerateFiles(Dir(app), "*.md", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void A_new_section_number_renames_nothing_because_names_come_from_ids()
    {
        using var app = Fixture();
        Run(app);
        var names = Directory.EnumerateFiles(Dir(app), "*.md", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Dir(app), f)).Order().ToList();
        Assert.Contains(names, n => n.StartsWith("s-0001-orders" + Path.DirectorySeparatorChar + "c-", StringComparison.Ordinal) && n.EndsWith("-main-flow.md", StringComparison.Ordinal));
        EditSection(app, s => s["id"] = "9.9");
        Assert.Equal(0, (int)Run(app)["removed"]!);
        Assert.Equal(names, Directory.EnumerateFiles(Dir(app), "*.md", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Dir(app), f)).Order());
        Assert.Contains("Section 9.9 Orders", FlowFile(app, "Main Flow:"));
    }

    // ---- rebuilding ----

    [Fact]
    public void A_rebuild_changes_no_byte_and_every_file_says_it_is_generated()
    {
        using var app = Fixture();
        MainFlow(app, Finding(Step(app, "1. Enter the name.", "Main")));
        Run(app);
        var first = Snapshot(Dir(app));
        Directory.Delete(Dir(app), recursive: true);
        Run(app);
        var second = Snapshot(Dir(app));
        Run(app);
        Assert.Equal(first.Keys.Order(), second.Keys.Order());
        Assert.All(first, f => Assert.Equal(f.Value, second[f.Key]));
        Assert.All(second, f => Assert.Equal(f.Value, Snapshot(Dir(app))[f.Key]));
        Assert.All(first.Keys, f => Assert.StartsWith("<!-- Generated by stangent workflows from specs/fsd/shop/claims.json", File.ReadAllText(Path.Combine(Dir(app), f))));
        Assert.All(first.Keys, f => Assert.Contains("Never read back", File.ReadLines(Path.Combine(Dir(app), f)).First()));
    }

    [Fact]
    public void A_rebuild_removes_what_no_longer_applies_keeps_what_a_person_put_there_and_touches_nothing_else()
    {
        using var app = Fixture();
        Run(app);
        var person = Path.Combine(Dir(app), "notes.md");
        var folder = Directory.EnumerateDirectories(Dir(app)).Single();
        File.WriteAllText(person, "Mine.\n");
        File.WriteAllText(Path.Combine(folder, "mine.md"), "Also mine.\n");
        var emptied = Path.Combine(Dir(app), "s-9999-old");
        Directory.CreateDirectory(emptied);
        File.WriteAllText(Path.Combine(emptied, "c-9999-old.md"), "<!-- Generated by stangent workflows from an older run. -->\n");
        // Something outside the folder that looks like ours stays too.
        var neighbour = Path.Combine(app.Root, "specs", "workflows", "other");
        Directory.CreateDirectory(neighbour);
        File.WriteAllText(Path.Combine(neighbour, "c-1-x.md"), "<!-- Generated by stangent workflows from another document. -->\n");

        EditSection(app, s => s["text"] = Flows.Replace("Main Flow:", "First Flow:"));
        ClaimFile.Sync(app.Root, "shop");
        var outside = Snapshot(app.Root).Where(f => !f.Key.StartsWith(Path.Combine("specs", "workflows", "shop"), StringComparison.Ordinal)).ToDictionary();
        var result = Run(app);
        Assert.Equal(2, (int)result["removed"]!);
        Assert.False(Directory.Exists(emptied));
        Assert.True(File.Exists(person));
        Assert.True(File.Exists(Path.Combine(folder, "mine.md")));
        Assert.DoesNotContain(Directory.EnumerateFiles(Dir(app), "*.md", SearchOption.AllDirectories), f => f.EndsWith("-main-flow.md", StringComparison.Ordinal));
        Assert.Contains(Directory.EnumerateFiles(Dir(app), "*.md", SearchOption.AllDirectories), f => f.EndsWith("-first-flow.md", StringComparison.Ordinal));
        var after = Snapshot(app.Root).Where(f => !f.Key.StartsWith(Path.Combine("specs", "workflows", "shop"), StringComparison.Ordinal)).ToDictionary();
        Assert.Equal(outside.Keys.Order(), after.Keys.Order());
        Assert.All(outside, f => Assert.Equal(f.Value, after[f.Key]));
    }

    [Fact]
    public void Old_claims_are_refused_and_nothing_is_written()
    {
        using var app = Fixture();
        EditSection(app, s => s["text"] = Flows.Replace("Save the name", "Keep the name"));
        Assert.Equal("claims_out_of_step", Assert.Throws<StangentException>(() => Run(app)).Code);
        Assert.False(Directory.Exists(Dir(app)));
    }

    [Fact]
    public void A_document_name_that_climbs_out_is_not_found()
    {
        using var app = Fixture();
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => WorkflowFiles.Run(app.Root, "../fsd/shop")).Code);
    }

    // ---- read by nothing ----

    [Fact]
    public void Nothing_in_src_reads_a_workflow_file()
    {
        var src = Path.Combine(RepoRoot.Find(), "src");
        var uses = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("\"workflows\"", StringComparison.Ordinal) || t.Contains("WorkflowFiles", StringComparison.Ordinal) || t.Contains("workflows/", StringComparison.Ordinal)))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal);
        // Components names the command, FsdCommands only runs it, and TestJourneys writes its own folder under specs/workflows/ with WorkflowFiles' sweep (D149).
        Assert.Equal(["Components.cs", "FsdCommands.cs", "TestJourneys.cs", "WorkflowFiles.cs"], uses);
        Assert.DoesNotContain("WorkflowFiles.DirIn", File.ReadAllText(Path.Combine(src, "Stangent.Cli/FsdCommands.cs")));
        // It reads its own folder only for the first line of what it may remove.
        var own = File.ReadAllText(Path.Combine(src, "Stangent.Spec/Fsd/WorkflowFiles.cs"));
        Assert.Single(own.Split("new StreamReader(").Skip(1));
        Assert.Contains("using (var r = new StreamReader(old)) { if (r.ReadLine()?.StartsWith(marker", own);
    }

    // ---- through the real recording, with a fake judge ----

    [Fact]
    public void Links_a_fake_judge_recorded_land_on_their_steps()
    {
        using var app = Fixture();
        var enter = Step(app, "1. Enter the name.", "Main");
        var save = Step(app, "2. Save the name.");
        var error = Step(app, "2. Show an error.");
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Write(app, step, new JsonObject
        {
            ["links"] = new JsonArray(
                new JsonObject { ["state"] = "agree", ["note"] = "n", ["claims"] = new JsonArray(enter.Id, save.Id), ["page"] = SpecApp.Post, ["statement"] = "R1" },
                new JsonObject { ["state"] = "conflict", ["note"] = "n", ["claims"] = new JsonArray(error.Id), ["page"] = SpecApp.Post, ["statement"] = "R1" }),
        });
        step = LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", "1", null);
        if (step.Agent == LinkRun.Checker)
        {
            Write(app, step, new JsonObject { ["checks"] = new JsonArray(new JsonObject { ["link"] = "K1", ["verdict"] = "confirm", ["note"] = "n" }) });
            LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", "1", null);
        }
        Assert.Contains(TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>(), e => (string?)e["fsd"]?["claim"] == error.Id);
        Run(app);
        Assert.Equal("# Main Flow: · MATCHES", Heading(FlowFile(app, "Main Flow:")));
        Assert.StartsWith("# Other Flow: Bad name. · OPEN", Heading(FlowFile(app, "Other Flow: Bad name.")));
        Assert.Contains("`conflict` ↔", FlowFile(app, "Other Flow: Bad name."));
    }
}
