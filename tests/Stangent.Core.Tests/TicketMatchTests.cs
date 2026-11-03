using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;
using Stangent.Tickets;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>Phase 2c's matching (D99) with canned outputs: who can be a candidate, both quotes checked, the fresh check's veto. No LLM.</summary>
public class TicketMatchTests
{
    const string Linked = "R-0001";
    const string NoCode = "R-0002";
    const string Elsewhere = "R-0003";
    const string Quote1 = "Orders over 100 need approval.";
    const string Quote2 = "Orders can be exported to a CSV file.";
    const string Quote3 = "Guests can see the opening hours page.";

    /// <summary>The small app with its pages recorded and one ticket pulled and bundled, from the POST endpoint's page.</summary>
    sealed class TicketApp : IDisposable
    {
        public const string TicketKey = "SHOP-1";
        public string Key => TicketKey;
        public SpecApp Spec { get; }
        public string Root => Spec.Root;
        public StangentRepo Repo => Spec.Repo;
        public TicketContext Context() => TicketContext.Load(Repo);
        public void Dispose() => Spec.Dispose();

        public TicketApp(string fsdConfig)
        {
            var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
            Spec = new SpecApp($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}' }}, findings: true, {fsdConfig} }}\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n  aiPoints: false\n");
            Record(SpecApp.Post, "Place an order", S("Orders over 100 are refused with \"Orders over 100 need approval\".", SpecApp.Place));
            Record("page:/cart", "Cart", S("The cart shows a Cart button.", "ts|web/src/Other.tsx#CartPage"));
            var file = Path.Combine(Root, "shop.docx");
            File.WriteAllBytes(file, DocxWriter.Write($"#! Orders\n{Quote1}\n{Quote2}\n{Quote3}\n"));
            FsdImport.Import(Repo, file);
        }

        static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

        void Record(string page, string title, JsonObject rule)
        {
            var def = Spec.Pages().Find(page)!;
            var step = PageRun.Prepare(Spec.Pages(), def, Repo);
            File.WriteAllText(Path.Combine(Root, step.Output!), new JsonObject
            {
                ["title"] = title, ["shows"] = new JsonArray(page == SpecApp.Post ? [] : [rule]), ["actions"] = new JsonArray(),
                ["rules"] = new JsonArray(page == SpecApp.Post ? [rule] : []), ["sideEffects"] = new JsonArray(),
            }.ToJsonString());
            step = PageRun.Check(Spec.Pages(), def, Repo, null);
            File.WriteAllText(Path.Combine(Root, step.Output!), "{\"findings\":[]}");
            Assert.Equal("recorded", PageRun.Check(Spec.Pages(), def, Repo, null).Status);
        }

        public void Ticket(string summary, string description)
        {
            TicketFixtures.Make(Path.Combine(Root, "replay"), TicketKey, "Bug", summary, description);
            var tracker = Trackers.Open(Repo, new MemorySecretStore(), TextWriter.Null, null);
            TicketPull.RunAsync(Repo, tracker, TicketKey).GetAwaiter().GetResult();
            TicketPoints.Update(Repo, Context(), TicketKey, [SpecApp.Post], [], "Pat Developer");
            TicketBundle.Build(Repo, Context(), TicketKey);
        }
    }

    static TicketApp Build(string config = "skipApproval: true")
    {
        var app = new TicketApp(config);
        Extract(app);
        Link(app);
        app.Ticket("Orders over 100 go through without approval", "I placed an order of 150 and it went straight through, which seems wrong to me.");
        return app;
    }

    static void Extract(TicketApp app)
    {
        JsonObject R(string key, string statement, string quote) => new()
        {
            ["key"] = key, ["kind"] = "rule", ["title"] = statement, ["statement"] = statement, ["quotes"] = new JsonArray((JsonNode)quote), ["acceptance"] = new JsonArray(),
        };
        var step = RequirementRun.Prepare(app.Repo, "shop", "1");
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["requirements"] = new JsonArray((JsonNode)R("r1", Quote1, Quote1), (JsonNode)R("r2", Quote2, Quote2), (JsonNode)R("r3", Quote3, Quote3)),
            ["definitions"] = new JsonArray(), ["journeys"] = new JsonArray(), ["questions"] = new JsonArray(), ["background"] = new JsonArray(),
        }.ToJsonString());
        step = RequirementRun.Check(app.Repo, "shop", "1", null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["checks"] = new JsonArray([.. new[] { "r1", "r2", "r3" }.Select(k => (JsonNode)new JsonObject { ["item"] = k, ["verdict"] = "ok", ["note"] = "n" })]),
        }.ToJsonString());
        Assert.Equal("recorded", RequirementRun.Check(app.Repo, "shop", "1", null).Status);
    }

    /// <summary>R-0001 links to the page at the ticket's code, R-0002 has only a gap, R-0003 links to a page this ticket doesn't reach.</summary>
    static void Link(TicketApp app)
    {
        var section = SpecIndex.Load(app.Root).Sections[("shop", "1")];
        LinkRun.ApplyEntries(app.Root, "shop", "1", [
            LinkRun.Entry("agree", "shop", section, Quote1, SpecApp.Post, "R1", "Orders over 100 are refused", "judge", "confirmed", null, Linked),
            LinkRun.Entry("gap", "shop", section, Quote2, null, null, null, "judge", null, "n", NoCode),
            LinkRun.Entry("agree", "shop", section, Quote3, "page:/cart", "S1", "The cart shows a Cart button.", "judge", "confirmed", null, Elsewhere),
        ]);
    }

    static RunStep Give(TicketApp app, RunStep step, JsonObject output)
    {
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        return TicketMatch.Check(app.Repo, app.Context(), app.Key, null);
    }

    static JsonObject Pick(string? pick, string? ticket = "I placed an order of 150", string? requirement = Quote1)
    {
        var o = new JsonObject { ["pick"] = pick, ["why"] = "because" };
        if (pick is not null) { o["ticketQuote"] = ticket; o["requirementQuote"] = requirement; }
        return o;
    }

    static JsonObject Decides(bool decides = true, string reporter = "agrees")
    {
        var o = new JsonObject { ["decides"] = decides, ["reporter"] = reporter, ["why"] = "reasons" };
        if (decides) { o["ticketQuote"] = "I placed an order of 150"; o["requirementQuote"] = Quote1; }
        return o;
    }

    static string Input(TicketApp app, RunStep step, string file = "input.md") =>
        File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output)!, file));

    [Fact]
    public void Candidates_are_linked_at_the_tickets_code_then_those_with_no_code_never_those_linked_elsewhere()
    {
        using var app = Build();
        var step = TicketMatch.Prepare(app.Repo, app.Context(), app.Key);
        Assert.Equal(TicketMatch.Matcher, step.Agent);
        var input = Input(app, step);
        var linked = input.IndexOf("# Requirements linked", StringComparison.Ordinal);
        var noCode = input.IndexOf("# Requirements no code", StringComparison.Ordinal);
        Assert.InRange(input.IndexOf($"**{Linked}**", StringComparison.Ordinal), linked, noCode);
        Assert.True(input.IndexOf($"**{NoCode}**", StringComparison.Ordinal) > noCode);
        Assert.DoesNotContain(Elsewhere, input);
    }

    [Fact]
    public void A_pick_the_fresh_check_agrees_with_reads_suggested_and_is_never_confirmed_by_code()
    {
        using var app = Build();
        var step = Give(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key), Pick(Linked));
        Assert.Equal(TicketMatch.Checker, step.Agent);
        // The fresh check isn't told why it was picked.
        Assert.DoesNotContain("because", Input(app, step, "check-input.md"));
        Assert.Equal("recorded", Give(app, step, Decides()).Status);
        var match = TicketMatch.Load(app.Root, app.Key)!;
        Assert.Equal("suggested", (string)match["outcome"]!);
        Assert.Equal($"suggested: {Linked}", (string)match["reading"]!);
        Assert.Equal("agrees", (string)match["reporter"]!);
        Assert.Null(match["confirmed"]);
        Assert.True((bool)match["approvalSkipped"]!);
    }

    [Fact]
    public void A_pick_the_fresh_check_disagrees_with_is_no_match_and_says_so()
    {
        using var app = Build();
        var step = Give(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key), Pick(Linked));
        Assert.Equal("recorded", Give(app, step, Decides(decides: false)).Status);
        var match = TicketMatch.Load(app.Root, app.Key)!;
        Assert.Equal("none", (string)match["outcome"]!);
        Assert.Null(match["requirement"]);
        Assert.Equal(Linked, (string)match["refused"]!["pick"]!);
    }

    [Fact]
    public void A_requirement_with_no_code_reads_so()
    {
        using var app = Build();
        var step = Give(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key), Pick(NoCode, requirement: Quote2));
        var decides = Decides();
        decides["requirementQuote"] = Quote2;
        Assert.Equal("recorded", Give(app, step, decides).Status);
        var match = TicketMatch.Load(app.Root, app.Key)!;
        Assert.False((bool)match["requirement"]!["codeFound"]!);
        Assert.Equal($"suggested: {NoCode} (no code found for this rule)", (string)match["reading"]!);
    }

    [Fact]
    public void No_pick_is_an_answer_and_makes_no_second_AI_call()
    {
        using var app = Build();
        var step = Give(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key), Pick(null));
        Assert.Equal(("recorded", "none"), (step.Status, step.Action));
        Assert.Equal("none", (string)TicketMatch.Load(app.Root, app.Key)!["outcome"]!);
        Assert.Equal("current", TicketMatch.Prepare(app.Repo, app.Context(), app.Key).Status);
    }

    [Theory]
    [InlineData("R-0099", "I placed an order of 150", Quote1, "isn't one of the candidates")]
    [InlineData(Elsewhere, "I placed an order of 150", Quote3, "isn't one of the candidates")]
    [InlineData(Linked, "A quote the ticket never had in it", Quote1, "isn't from the ticket")]
    [InlineData(Linked, "I placed an order of 150", "Words the requirement never quoted anywhere", "isn't from the requirement")]
    public void Ids_and_both_quotes_are_checked_with_one_retry_then_it_fails(string pick, string ticket, string requirement, string error)
    {
        using var app = Build();
        var step = Give(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key), Pick(pick, ticket, requirement));
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains(error, StringComparison.Ordinal));
        step = Give(app, step, Pick(pick, ticket, requirement));
        Assert.Equal("failed", step.Status);
        Assert.Equal("failed", (string)TicketMatch.Load(app.Root, app.Key)!["outcome"]!);
        // A failure isn't an answer: the next run tries again.
        Assert.Equal(TicketMatch.Matcher, TicketMatch.Prepare(app.Repo, app.Context(), app.Key).Agent);
    }

    [Fact]
    public void The_fresh_checks_quotes_are_checked_and_it_gets_its_own_retry()
    {
        using var app = Build();
        var step = Give(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key), new JsonObject { ["pick"] = "nope", ["why"] = "w" });
        Assert.Equal("retry", step.Status);
        step = Give(app, step, Pick(Linked));
        Assert.Equal(TicketMatch.Checker, step.Agent);
        var bad = Decides();
        bad["ticketQuote"] = "A quote the ticket never had in it";
        step = Give(app, step, bad);
        Assert.Equal(("retry", TicketMatch.Checker), (step.Status, step.Agent));
        Assert.Contains("check-input.md", step.Prompt!);
        Assert.Contains("What to fix", step.Prompt!);
        step = Give(app, step, bad);
        Assert.Equal("failed", step.Status);
    }

    [Fact]
    public void Proposed_requirements_judge_nothing_unless_a_benchmarks_setting_says_so()
    {
        using var app = Build("skipApproval: false");
        var step = TicketMatch.Prepare(app.Repo, app.Context(), app.Key);
        Assert.Equal(("recorded", "none"), (step.Status, step.Action));
        var match = TicketMatch.Load(app.Root, app.Key)!;
        Assert.Equal("none", (string)match["outcome"]!);
        Assert.False((bool)match["approvalSkipped"]!);

        // An approved one does.
        var feature = RequirementFile.Features(app.Root, "shop").First();
        feature["requirements"]![0]!["status"] = "approved";
        RequirementFile.Save(app.Root, feature);
        Assert.Equal(TicketMatch.Matcher, TicketMatch.Prepare(app.Repo, app.Context(), app.Key).Agent);
    }

    [Fact]
    public void A_disputed_requirement_is_never_a_candidate_even_with_approval_skipped()
    {
        using var app = Build();
        var feature = RequirementFile.Features(app.Root, "shop").First();
        feature["requirements"]![0]!["check"] = "disputed";
        RequirementFile.Save(app.Root, feature);
        Assert.DoesNotContain($"**{Linked}**", Input(app, TicketMatch.Prepare(app.Repo, app.Context(), app.Key)));
    }

    [Fact]
    public void A_ticket_with_no_bundle_says_so()
    {
        using var app = Build();
        File.Delete(new TicketPaths(app.Root, app.Key).BundleJson);
        Assert.Equal("no_bundle", Assert.Throws<StangentException>(() => TicketMatch.Prepare(app.Repo, app.Context(), app.Key)).Code);
    }
}
