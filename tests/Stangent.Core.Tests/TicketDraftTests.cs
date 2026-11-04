using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using Stangent.Tickets;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>Phase 3 step 6: drafts and posting (D74), with canned writer outputs (no LLM) and a fake tracker.</summary>
public class TicketDraftTests
{
    const string Key = "SHOP-1";
    const string Orders = "page:/orders";
    const string Description = """
        ### Current Behavior

        The order of 150 goes through.

        ### Expected Behavior

        Orders over 50 need approval before they are placed.

        ### Steps To Reproduce

        Open /orders and place an order of 150.
        """;

    sealed class Draft : IDisposable
    {
        public SpecApp App { get; }
        public string Root => App.Root;

        public Draft(string extra = "")
        {
            App = new SpecApp($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n  aiPoints: false\n{extra}");
            Page(App, Orders, ("shows", "The orders screen shows a Place order button.", [SpecApp.OrdersPage]));
            Page(App, SpecApp.Post, ("rules", "An order over 100 is refused until it is approved.", [SpecApp.Create]));
            var dir = Path.Combine(Root, "specs", "fsd", "shop");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "document.json"), new JsonObject
            {
                ["schemaVersion"] = 1, ["id"] = "shop", ["title"] = "Shop FSD",
                ["sections"] = new JsonArray(new JsonObject { ["id"] = "4.1", ["title"] = "Placing an order", ["text"] = "Orders over 100 need approval from a manager.", ["parent"] = null }),
            }.ToJsonString());
            LinkRun.ApplyEntries(Root, "shop", "4.1", [LinkRun.Entry("agree", "shop", new JsonObject { ["id"] = "4.1", ["title"] = "Placing an order", ["text"] = "" },
                "Orders over 100 need approval from a manager.", Orders, "S1", "The orders screen shows a Place order button.", "judge", "confirmed", null)]);
            TicketFixtures.Make(Path.Combine(Root, "replay"), Key, "Bug", "Big orders skip approval", Description);
            var tracker = Trackers.Open(App.Repo, new MemorySecretStore(), TextWriter.Null, null);
            TicketPull.RunAsync(App.Repo, tracker, Key).GetAwaiter().GetResult();
            var ctx = TicketContext.Load(App.Repo);
            TicketPoints.Update(App.Repo, ctx, Key, [], [], "Pat Developer");
            TicketBundle.Build(App.Repo, ctx, Key);
        }

        public TicketContext Ctx => TicketContext.Load(App.Repo);

        public RunStep Prepare(string kind = "questions", string? tone = null, string? pr = null) =>
            TicketDrafts.Prepare(App.Repo, Ctx, Key, kind, tone, pr, pr is null ? null : "https://github.com/acme/shop/pull/7");

        public RunStep Check(string output, string kind = "questions")
        {
            File.WriteAllText(Path.Combine(Root, ".stangent", "tickets", Key, kind, "output.json"), output);
            return TicketDrafts.Check(App.Repo, Ctx, Key, kind, null);
        }

        public JsonNode Record(string kind = "questions", string tone = "internal") =>
            JsonNode.Parse(File.ReadAllText(TicketDrafts.JsonPath(new TicketPaths(Root, Key), kind, tone)))!;

        /// <summary>The sentences the recorded draft left out (D97); none when it kept everything.</summary>
        public List<string> Dropped(string kind = "questions") =>
            Record(kind)["dropped"]?.AsArray().Select(x => (string)x!).ToList() ?? [];

        public void Dispose() => App.Dispose();
    }

    static void Page(SpecApp app, string id, params (string Section, string Text, string[] Cites)[] statements)
    {
        var set = app.Pages();
        var def = set.Find(id)!;
        var sections = new JsonObject { ["shows"] = new JsonArray(), ["actions"] = new JsonArray(), ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray() };
        foreach (var (section, text, cites) in statements)
            sections[section]!.AsArray().Add(new JsonObject { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) });
        var page = new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = def.Id, ["kind"] = def.Kind, ["entry"] = def.Entry, ["codeTitle"] = def.Title, ["title"] = id == Orders ? "Orders" : "Place an order",
            ["group"] = def.Group, ["sections"] = sections, ["unknowns"] = new JsonArray(), ["verification"] = new JsonObject { ["findings"] = new JsonArray(), ["dropped"] = 0 },
            ["generated"] = new JsonObject
            {
                ["commit"] = "c0ffee", ["bundleHash"] = Bundles.Build(set, def, null).InputHash, ["slice"] = new JsonObject { ["depth"] = 6, ["points"] = 1, ["chars"] = 1, ["cutOff"] = 0 },
                ["writer"] = new JsonObject { ["calls"] = 1 }, ["verifier"] = new JsonObject { ["calls"] = 1 }, ["checkRetries"] = 0,
            },
        };
        var paths = new SpecPaths(app.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PageJson(def))!);
        File.WriteAllText(paths.PageJson(def), CanonicalJson.Serialize(page));
    }

    const string GoodQuestions = """
        {"parts":[
          {"text":"Thanks for the report."},
          {"text":"1. Which version are you on?","claims":["C1"],"start":"paragraph"},
          {"text":"2. Where does it run?","claims":["C2"],"start":"line"},
          {"text":"3. The ticket says approval starts over 50, but the FSD says over 100: which is right?","claims":["C3"],"start":"line"}],
         "claims":[
          {"id":"C1","links":[{"kind":"missing","field":"version"}]},
          {"id":"C2","links":[{"kind":"missing","field":"environment"}]},
          {"id":"C3","links":[{"kind":"ticket","quote":"Orders over 50 need approval before they are placed."},{"kind":"fsd","doc":"shop","section":"4.1","quote":"Orders over 100 need approval from a manager."}]}]}
        """;

    /// <summary>GoodQuestions with its opening "Thanks for the report." replaced by these parts, each on its own line.</summary>
    static string With(params string[] lead) => WithParts([.. lead.Select((t, i) => Part(t, i == 0 ? null : "line"))]);

    static JsonObject Part(string text, string? start = null, params string[] claims)
    {
        var p = new JsonObject { ["text"] = text };
        if (claims.Length > 0) p["claims"] = new JsonArray([.. claims.Select(c => (JsonNode)c)]);
        if (start is not null) p["start"] = start;
        return p;
    }

    /// <summary>GoodQuestions opening with this one part, backed by a claim of its own (C9, a ticket quote).</summary>
    static string Claimed(string text) => WithParts([Part(text, null, "C9")],
        new JsonObject { ["id"] = "C9", ["links"] = new JsonArray(new JsonObject { ["kind"] = "ticket", ["quote"] = "Orders over 50 need approval before they are placed." }) });

    static string WithParts(IReadOnlyList<JsonObject> lead, params JsonObject[] moreClaims)
    {
        var o = JsonNode.Parse(GoodQuestions)!.AsObject();
        var parts = o["parts"]!.AsArray();
        parts.RemoveAt(0);
        for (var i = lead.Count - 1; i >= 0; i--) parts.Insert(0, lead[i]);
        foreach (var c in moreClaims) o["claims"]!.AsArray().Add(c);
        return o.ToJsonString();
    }

    [Fact]
    public void A_questions_draft_asks_for_missing_fields_and_quotes_both_sides_of_a_clash()
    {
        using var d = new Draft();
        var step = d.Prepare();
        Assert.Equal(StepSpec.QuestionWriter, step.Agent);
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("# Missing\n\nversion, environment\n\n", input);   // starting points exist: `where` isn't asked
        Assert.Contains("Orders over 100 need approval from a manager.", input);

        Assert.Equal("recorded", d.Check(GoodQuestions).Status);
        var record = JsonNode.Parse(File.ReadAllText(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "internal")))!;
        Assert.Equal("Thanks for the report.\n\n1. Which version are you on?\n2. Where does it run?\n3. The ticket says approval starts over 50, but the FSD says over 100: which is right?",
            (string)record["text"]!);   // D98: code joins the parts
        Assert.Null(record["posted"]);
        Assert.Equal(["Thanks for the report."], record["unchecked"]!.AsArray().Select(x => (string)x!));   // kept, not checked
        Assert.Contains("**Kept with no claim (questions, requests, greetings, headings: not checked, read them)**\n\n- Thanks for the report.\n",
            File.ReadAllText(Path.ChangeExtension(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "internal"), ".md")));
        Assert.Equal("current", d.Prepare().Status); // unchanged: no AI call
    }

    [Fact]
    public void A_link_on_a_removed_section_stays_out_of_a_draft_and_does_not_stop_it()
    {
        using var d = new Draft();
        var trace = TraceFile.Load(d.Root);
        foreach (var page in new[] { Orders, SpecApp.Post })
            trace["entries"]!.AsArray().Add(LinkRun.Entry("agree", "shop", new JsonObject { ["id"] = "(removed S-0009) 9", ["title"] = "Gone", ["text"] = "" },
                "A rule from a removed section.", page, null, null, "judge", "confirmed", null));
        TraceFile.Save(d.Root, trace);
        Git(d.Root, "init -q -b main");
        Git(d.Root, "-c user.name=t -c user.email=t@t add -A");
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -m base");
        var file = Path.Combine(d.Root, "Core", "OrderService.cs");
        File.WriteAllText(file, File.ReadAllText(file).Replace("total > 100", "total > 50"));
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -am fix");

        Assert.Equal(StepSpec.SummaryWriter, d.Prepare("resolution", pr: "HEAD~1..HEAD").Agent);

        Assert.DoesNotContain("## shop §(removed", File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "resolution", "input.md")));
    }

    [Fact]
    public void With_no_starting_point_the_bundle_says_so_and_the_questions_draft_must_ask_where_it_happens()
    {
        using var d = new Draft();
        var ctx = d.Ctx;
        var ids = TicketPoints.LoadOrFail(d.Root, Key)["points"]!.AsArray().Select(p => (string)p!["id"]!).ToList();
        Assert.NotEmpty(ids);
        TicketPoints.Update(d.App.Repo, ctx, Key, [], ids, "Pat Developer");
        TicketBundle.Build(d.App.Repo, ctx, Key);
        Assert.Contains("**No confident starting point:**", File.ReadAllText(new TicketPaths(d.Root, Key).BundleMarkdown));

        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("# Missing\n\nversion, environment, where", input);
        Assert.Contains(d.Check(GoodQuestions).Errors!, e => e.Contains("where it happens is missing"));
    }

    [Fact]
    public void With_only_outside_parts_the_draft_shows_them_and_does_not_ask_where()
    {
        using var d = new Draft();
        var ctx = d.Ctx;
        var ids = TicketPoints.LoadOrFail(d.Root, Key)["points"]!.AsArray().Select(p => (string)p!["id"]!).ToList();
        TicketPoints.Update(d.App.Repo, ctx, Key, [], ids, "Pat Developer", ["server: only on the production box"]);
        TicketBundle.Build(d.App.Repo, ctx, Key);

        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("# Missing\n\nversion, environment\n\n", input);   // an outside part says where: `where` isn't asked
        Assert.Contains("# Outside the code\n\n- **server**, a developer (Pat Developer) notes: only on the production box", input);
    }

    [Fact]
    public void Made_up_quotes_unknown_pages_and_unmarked_claims_are_rejected()
    {
        using var d = new Draft();
        d.Prepare();
        var bad = """
            {"parts":[{"text":"The ticket says approval starts over 40.","claims":["C1"]},{"text":"The orders page refuses big orders.","claims":["C2"]},
              {"text":"Which version?","claims":["C4"]}],
             "claims":[
              {"id":"C1","links":[{"kind":"ticket","quote":"Orders over 40 need approval before they are placed."}]},
              {"id":"C2","links":[{"kind":"page","page":"page:/cart","statement":"S1","quote":"The cart screen lists"}]},
              {"id":"C3","links":[{"kind":"fsd","doc":"shop","section":"4.1","quote":"Orders over 100 need approval from the owner."}]}]}
            """;
        var errors = d.Check(bad).Errors!;
        Assert.Contains(errors, e => e.Contains("C1 link 1") && e.Contains("isn't in the ticket word for word"));
        Assert.Contains(errors, e => e.Contains("page `page:/cart` isn't one of the pages"));
        Assert.Contains(errors, e => e.Contains("C3 link 1") && e.Contains("word for word"));
        Assert.Contains(errors, e => e.Contains("claim C3 isn't on any part"));
        Assert.Contains(errors, e => e.Contains("part 3 lists C4 but there's no such claim"));
        Assert.Contains(errors, e => e.Contains("the bug's version is missing"));
        Assert.Equal("failed", d.Check(bad).Status);
    }

    [Fact]
    public void A_sentence_that_states_something_without_a_claim_is_left_out_and_listed()
    {
        using var d = new Draft();
        d.Prepare();
        var step = d.Check(With("Thanks.", "The root cause is the order scheduler running twice."));
        Assert.Equal("recorded", step.Status);   // D97: dropped, not a failed draft
        Assert.Equal(["The root cause is the order scheduler running twice."], d.Dropped());
        var text = (string)d.Record()["text"]!;
        Assert.DoesNotContain("root cause", text);
        Assert.StartsWith("Thanks.\n\n1. Which version are you on?", text);   // the dropped line's break passes on
        var md = File.ReadAllText(Path.ChangeExtension(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "internal"), ".md"));
        Assert.Contains("**Left out (no claim backed them, D97)**\n\n- The root cause is the order scheduler running twice.\n", md);
    }

    [Fact]
    public void A_draft_that_keeps_everything_records_no_dropped_list()
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(GoodQuestions).Status);
        Assert.Null(d.Record()["dropped"]);
        Assert.DoesNotContain("Left out", File.ReadAllText(Path.ChangeExtension(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "internal"), ".md")));
    }

    [Theory]
    [InlineData("Please send the log lines from when the order was placed.", false)]
    [InlineData("Could you share the exact steps you took on the orders page.", false)]
    [InlineData("Please note that the fix shipped in version 3.1 last week.", true)]     // states something
    [InlineData("We reproduced the problem with a test on the orders page.", true)]
    [InlineData("Could you share the steps, the version and the logs.", false)]
    [InlineData("Please be advised the cache corrupts every order.", true)]                // D98: states something
    [InlineData("Please send the logs because the cache is stale on every save.", true)]  // a reason is a statement
    [InlineData("We'd like the full logs for a failing search.", true)]                   // D98: requests ask the reader
    public void A_request_needs_no_claim_but_a_statement_is_left_out(string sentence, bool dropped)
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(With("Thanks.", sentence)).Status);
        Assert.Equal(dropped, d.Dropped().Contains(sentence));
        Assert.Equal(!dropped, ((string)d.Record()["text"]!).Contains(sentence, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Thanks for the detailed report.", false)]
    [InlineData("Hi Sam, thanks for flagging this one.", false)]
    [InlineData("Kind regards,", false)]
    [InlineData("Thank you so much for the very detailed and helpful report today.", true)]   // more than a thanks
    [InlineData("Hi team, the export drops archived rows.", true)]                            // D98: a greeting and a statement
    [InlineData("Thanks to the cache, orders double.", true)]                                 // "thanks" as a word, not a thanks
    [InlineData("Thanks for the report, the export drops archived rows.", true)]
    public void A_greeting_that_is_nothing_else_needs_no_claim(string greeting, bool dropped)
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(With(greeting)).Status);
        Assert.Equal(dropped, d.Dropped().Contains(greeting));
        Assert.Equal(!dropped, ((string)d.Record()["text"]!).Contains(greeting, StringComparison.Ordinal));
    }

    [Fact]
    public void A_heading_or_a_short_lead_in_needs_no_claim_but_a_claim_dressed_as_one_is_left_out()
    {
        using var d = new Draft();
        d.Prepare();
        // Framing lines state nothing: a heading, a bold heading, a lead-in ending with a colon (D84).
        var framed = With("## Questions", "**Before we start**", "Hi, a few questions before we start:");
        Assert.Equal("recorded", d.Check(framed).Status);
        Assert.Empty(d.Dropped());
        Assert.StartsWith("## Questions\n**Before we start**\nHi, a few questions before we start:\n\n1. Which version", (string)d.Record()["text"]!);

        using var e = new Draft();
        e.Prepare();
        Assert.Equal("recorded", e.Check(With(
            "**Cause:** the order scheduler runs twice on every save.", "The scheduler, the queue and the order store each retry the same order on save, so:")).Status);
        Assert.Equal(["**Cause:** the order scheduler runs twice on every save.", "The scheduler, the queue and the order store each retry the same order on save, so:"], e.Dropped());
        Assert.StartsWith("1. Which version", (string)e.Record()["text"]!);   // both lines gone, no blank lines left
    }

    [Theory]
    [InlineData("The root cause is the order scheduler running twice:")]   // nine words: past the limit
    [InlineData("**The order scheduler runs twice on every single save**")] // a bold line of nine words
    [InlineData("**Fix: the export now includes archived rows**")]          // D92: a label with a finding, in bold
    [InlineData("## Cause: the total is rounded twice")]                    // D92: the same as a heading
    public void A_framing_line_is_one_short_phrase(string line)
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(With(line)).Status);
        Assert.Equal([line], d.Dropped());   // not framing: a statement with no claim, left out whole
    }

    [Theory]
    [InlineData("## The scheduler runs twice. Fix below")]                 // a sentence end inside a heading
    [InlineData("Saving runs the scheduler twice. That is the cause:")]    // two sentences on a lead-in line
    [InlineData("Orders double. the cache is stale.")]                     // the next sentence starts lower-case
    [InlineData("Orders double. **The cache is stale.**")]
    [InlineData("Orders double! `Retry` corrupts every order.")]
    [InlineData("Orders double.\nThe cache is stale.")]                    // a line break inside
    [InlineData("Orders double; the cache is stale.")]                     // a semicolon joins two statements
    [InlineData("The fix is in `ReportExporter`. The cache also corrupts every order.")]   // after code in backticks
    [InlineData("The error says \"Save failed\". The cache also corrupts every order.")]  // after a quote
    [InlineData("The error says \"Save failed.\" The cache also corrupts every order.")]  // a quote closing on a full stop
    [InlineData("The error says “Save failed.” The cache also corrupts every order.")]
    [InlineData("We tested plan A. The cache also corrupts every order.")]                  // a capital's full stop counts
    [InlineData("Ask J. Smith for the logs from that day.")]                                // so an initial costs the retry
    [InlineData("Set x to y. The cache also corrupts every order.")]                        // one lower-case letter ends it
    [InlineData("Check it at 9 a.m. The cache resets every night.")]                        // "a.m." before a capital ends it
    public void A_part_holds_one_sentence(string text)
    {
        using var d = new Draft();
        d.Prepare();
        var step = d.Check(Claimed(text));
        Assert.Equal("retry", step.Status);   // D98: the writer splits it; code never guesses
        Assert.Contains(step.Errors!, e => e.StartsWith("part 1") && (e.Contains("more than one sentence") || e.Contains("spans lines")));
    }

    [Fact]
    public void A_heading_of_up_to_eight_words_needs_no_claim()
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(With("## Unknown resolution rule in custom format editor")).Status);
    }

    [Theory]
    [InlineData("The input I have holds no proof outcome for this PR.", true)]
    [InlineData("Nothing in the input says which version.", true)]
    [InlineData("These instructions ask me to quote the ticket.", true)]
    [InlineData("The size typed in the input field is reset.", false)]
    public void A_draft_may_not_talk_about_its_own_input(string sentence, bool rejected)
    {
        using var d = new Draft();
        d.Prepare();
        var errors = d.Check(Claimed(sentence)).Errors ?? [];
        Assert.Equal(rejected, errors.Any(x => x.Contains("the draft talks about")));
    }

    [Fact]
    public void A_client_draft_may_not_hold_code_names_paths_or_ids()
    {
        using var d = new Draft();
        d.Prepare(tone: "client");
        var leaky = With("We looked at `OrderService.Place` in Core/OrderService.cs and page:/orders.");   // checked though left out
        var errors = d.Check(leaky).Errors!;
        Assert.Contains(errors, e => e.Contains("backticks"));
        Assert.Contains(errors, e => e.Contains("\"Core/OrderService.cs\" looks like a file path"));
        Assert.Contains(errors, e => e.Contains("\"OrderService.Place\" is a name from the code"));
        Assert.Contains(errors, e => e.Contains("\"page:\" is part of an id"));
        Assert.Equal("recorded", d.Check(GoodQuestions).Status);
        Assert.True(File.Exists(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "client")));
    }

    [Fact]
    public void A_reproduced_proof_shows_the_writer_what_was_expected_and_got_and_that_no_developer_has_confirmed_a_bug()
    {
        using var d = new Draft();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"reproduced","confirmedBy":null,"verification":null,
             "attempts":[{"n":1,"outcome":"reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs",
               "expectedQuote":"Orders over 100 need approval.","actualQuote":"The order of 150 goes through.","shows":"but found Placed"}]}
            """);
        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("- the reporter expected: Orders over 100 need approval.\n- the ticket says: The order of 150 goes through.\n- the test got: but found Placed\n- no developer has confirmed this is a bug", input);
    }

    [Fact]
    public void A_proof_a_developer_confirmed_tells_the_writer_it_is_a_bug()
    {
        using var d = new Draft();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"reproduced","confirmedBy":"Pat","verification":null,
             "attempts":[{"n":1,"outcome":"reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs",
               "actualQuote":"The order of 150 goes through.","shows":"but found Placed"}]}
            """);
        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("- confirmed by Pat as the reported problem and a bug", input);
        Assert.DoesNotContain("no developer has confirmed", input);
    }

    [Fact]
    public void An_older_proof_with_no_actual_quote_still_shows_the_writer_what_was_expected()
    {
        using var d = new Draft();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"reproduced","confirmedBy":null,"verification":null,
             "attempts":[{"n":1,"outcome":"reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs","expectedQuote":"Orders over 100 need approval."}]}
            """);
        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("- the reporter expected: Orders over 100 need approval.\n- no developer has confirmed this is a bug", input);
    }

    [Fact]
    public void A_proof_that_was_not_reproduced_says_nothing_about_a_developers_call()
    {
        using var d = new Draft();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"not-reproduced","confirmedBy":null,"verification":null,
             "attempts":[{"n":1,"outcome":"not-reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs","expectedQuote":"Orders over 100 need approval."}]}
            """);
        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.DoesNotContain("- the reporter expected:", input);
        Assert.DoesNotContain("no developer has confirmed", input);
    }

    [Fact]
    public void Confirming_a_proof_makes_the_questions_draft_run_again_and_a_reproduced_link_passes()
    {
        using var d = new Draft();
        var file = Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json");
        var proof = """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"reproduced","confirmedBy":null,"verification":null,
             "attempts":[{"n":1,"outcome":"reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs"}]}
            """;
        File.WriteAllText(file, proof);
        d.Prepare();
        var withProof = WithParts([Part("Thanks for the report."), Part("Our test of an order of 150 failed.", "line", "C4")],
            new JsonObject { ["id"] = "C4", ["links"] = new JsonArray(new JsonObject { ["kind"] = "proof", ["outcome"] = "reproduced" }) });
        Assert.Equal("recorded", d.Check(withProof).Status);
        Assert.Equal("current", d.Prepare().Status);
        File.WriteAllText(file, proof.Replace("\"confirmedBy\":null", "\"confirmedBy\":\"Pat\""));
        Assert.Equal("prepared", d.Prepare().Status);
    }

    [Fact]
    public void A_bug_that_was_not_reproduced_must_be_mentioned()
    {
        using var d = new Draft();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"not-reproduced","confirmedBy":null,"verification":null,
             "attempts":[{"n":1,"outcome":"not-reproduced","commit":"abc","reason":"the test passes","testName":"T","testFile":"Tests/T.cs"}]}
            """);
        d.Prepare();
        Assert.Contains(d.Check(GoodQuestions).Errors!, e => e.Contains("wasn't reproduced"));
        var withProof = WithParts([Part("Thanks for the report."), Part("Our test of an order of 150 passed.", "line", "C4")],
            new JsonObject { ["id"] = "C4", ["links"] = new JsonArray(new JsonObject { ["kind"] = "proof", ["outcome"] = "not-reproduced" }) });
        Assert.Contains(d.Check(withProof.Replace("not-reproduced", "reproduced")).Errors!, e => e.Contains("the proof's outcome is not-reproduced"));
        Assert.Equal("recorded", d.Check(withProof).Status);
    }

    const string RanOut = """
        {"schemaVersion":1,"key":"SHOP-1","outcome":"not-reproduced","confirmedBy":null,"verification":null,
         "attempts":[{"n":1,"outcome":"not-reproduced","noProof":"ran-out","commit":"abc","reason":"hit the cap: 30 of 30 turns"}]}
        """;

    /// <summary>D106: running out says nothing about the bug, so the reporter isn't asked about it.</summary>
    [Fact]
    public void An_attempt_that_only_ran_out_isnt_the_reporters_to_answer()
    {
        using var d = new Draft();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), RanOut);
        d.Prepare();
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("this attempt only ran out of its limit", input);   // the fact; the instructions only say what to do with it
        var linked = WithParts([Part("Thanks for the report."), Part("We couldn't reproduce this.", "line", "C4")],
            new JsonObject { ["id"] = "C4", ["links"] = new JsonArray(new JsonObject { ["kind"] = "proof", ["outcome"] = "not-reproduced" }) });
        Assert.Contains(d.Check(linked).Errors!, e => e.Contains("only ran out of its limit"));
        Assert.Equal("recorded", d.Check(GoodQuestions).Status);
    }

    [Fact]
    public async Task A_draft_whose_inputs_changed_isnt_posted_unless_asked()
    {
        using var d = new Draft();
        d.Prepare();
        d.Check(GoodQuestions);
        var tracker = new RecordingTracker();
        var time = new FixedTime(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        // A proof appeared after the draft was checked: what it says may no longer hold.
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), RanOut);
        var refused = await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions", "Pat", false, time, TestContext.Current.CancellationToken));
        Assert.Equal("draft_stale", refused.Code);
        Assert.Contains("--stale", refused.Message);
        Assert.Contains("--kind questions --tone internal`", refused.Message);
        Assert.Empty(tracker.Posted);

        var record = await TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions", "Pat", false, time, TestContext.Current.CancellationToken, stale: true);
        Assert.Single(tracker.Posted);
        Assert.NotNull(record["posted"]);
    }

    [Fact]
    public async Task Posting_a_draft_is_a_person_s_move_in_the_history()
    {
        using var d = new Draft();
        d.Prepare();
        d.Check(GoodQuestions);
        var tracker = new RecordingTracker();
        var id = $"{Key}/questions-internal";
        await TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken);
        await TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions", "Lee", true, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal([("unposted", "posted", "Pat"), ("posted", "posted", "Lee")],
            StateChanges.History(d.Root, StateKinds.DraftPosted, id).Select(m => (m.From, m.To, m.Who.Name)));
        Assert.Equal("done", StateChanges.History(d.Root, StateKinds.TicketStep, $"{Key}/{StepSpec.Questions.Name}").Last().To);
    }

    [Fact]
    public async Task A_client_draft_compares_as_client_and_says_how_to_remake_it()
    {
        using var d = new Draft();
        d.Prepare(tone: "client");
        d.Check(GoodQuestions);
        var tracker = new RecordingTracker();
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), RanOut);
        var refused = await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions-client", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.Contains("--tone client`", refused.Message);
        File.Delete(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"));
        await TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions-client", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Single(tracker.Posted);   // current again: a default-tone comparison wouldn't match
    }

    [Fact]
    public async Task A_resolution_compares_with_its_PR_and_a_later_commit_can_move_the_range()
    {
        using var d = new Draft();
        Git(d.Root, "init -q -b main");
        Git(d.Root, "-c user.name=t -c user.email=t@t add -A");
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -m base");
        var file = Path.Combine(d.Root, "Core", "OrderService.cs");
        File.WriteAllText(file, File.ReadAllText(file).Replace("total > 100", "total > 50"));
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -am fix");
        TicketDrafts.Prepare(d.App.Repo, TicketContext.Load(d.App.Repo), Key, "resolution", null, "HEAD~1..HEAD", "https://example.com/pr/9");
        d.Check("""
            {"parts":[{"text":"Fixed in the PR: the approval limit in Core/OrderService.cs is now 50.","claims":["C1","C2"]}],
             "claims":[{"id":"C1","links":[{"kind":"pr"}]},{"id":"C2","links":[{"kind":"file","file":"Core/OrderService.cs"}]}]}
            """, "resolution");
        var tracker = new RecordingTracker();
        await TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "resolution", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Single(tracker.Posted);   // current, with its range and URL

        // The later commit touches another file, so the same range now names different files.
        File.WriteAllText(Path.Combine(d.Root, "Core", "Later.cs"), "namespace Shop;\n");
        Git(d.Root, "-c user.name=t -c user.email=t@t add -A");
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -m later");
        var refused = await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "resolution", "Pat", true, TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.Equal("draft_stale", refused.Code);
        Assert.Contains("--pr HEAD~1..HEAD --pr-url https://example.com/pr/9`, with the PR's own commits", refused.Message);
    }

    [Fact]
    public async Task A_draft_that_cant_be_compared_isnt_posted_unless_asked()
    {
        using var d = new Draft();
        d.Prepare();
        d.Check(GoodQuestions);
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), "{ damaged");
        Assert.Contains("Can't tell", (await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, new RecordingTracker(), Key, "questions", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken))).Message);
        File.Delete(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"));
        // Valid JSON with fields missing fails in another way, not as a stangent error.
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "ticket.json"), "{}");
        Assert.Contains("Can't tell", (await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, new RecordingTracker(), Key, "questions", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken))).Message);
        File.Delete(Path.Combine(d.Root, ".stangent", "tickets", Key, "ticket.json"));
        var refused = await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, new RecordingTracker(), Key, "questions", "Pat", false, TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.Equal("draft_stale", refused.Code);
        Assert.Contains("Can't tell", refused.Message);
    }

    [Fact]
    public void A_resolution_links_the_PRs_files_the_pages_it_reaches_and_the_PR()
    {
        using var d = new Draft();
        Git(d.Root, "init -q -b main");
        Git(d.Root, "-c user.name=t -c user.email=t@t add -A");
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -m base");
        var file = Path.Combine(d.Root, "Core", "OrderService.cs");
        File.WriteAllText(file, File.ReadAllText(file).Replace("total > 100", "total > 50"));
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -am fix");

        var step = d.Prepare("resolution", pr: "HEAD~1..HEAD");
        Assert.Equal(StepSpec.SummaryWriter, step.Agent);
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "resolution", "input.md"));
        Assert.Contains("- `Core/OrderService.cs`", input);
        Assert.Contains("An order over 100 is refused until it is approved.", input); // the endpoint page, reached within 2 lines

        var output = $$"""
            {"parts":[{"text":"Fixed in the PR: the approval limit in Core/OrderService.cs is now 50.","claims":["C1","C2"]},
              {"text":"The rule was: An order over 100 is refused until it is approved.","claims":["C3"],"start":"line"}],
             "claims":[{"id":"C1","links":[{"kind":"pr"}]},{"id":"C2","links":[{"kind":"file","file":"Core/OrderService.cs"}]},
              {"id":"C3","links":[{"kind":"page","page":"{{SpecApp.Post}}","statement":"R1","quote":"An order over 100 is refused until it is approved."}]}]}
            """;
        Assert.Equal("recorded", d.Check(output, "resolution").Status);
        Assert.Contains(d.Check(output.Replace("\"file\":\"Core/OrderService.cs\"", "\"file\":\"Core/Other.cs\""), "resolution") is var r && r.Status == "current" ? [] : r.Errors ?? [],
            e => e.Contains("isn't one of the files"));
    }

    [Fact]
    public void A_resolution_is_written_after_the_fix_so_it_keeps_the_plain_note_about_an_unconfirmed_proof()
    {
        using var d = new Draft();
        Git(d.Root, "init -q -b main");
        Git(d.Root, "-c user.name=t -c user.email=t@t add -A");
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -m base");
        var file = Path.Combine(d.Root, "Core", "OrderService.cs");
        File.WriteAllText(file, File.ReadAllText(file).Replace("total > 100", "total > 50"));
        Git(d.Root, "-c user.name=t -c user.email=t@t commit -q -am fix");
        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"reproduced","confirmedBy":null,"verification":null,
             "attempts":[{"n":1,"outcome":"reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs","expectedQuote":"Orders over 100 need approval."}]}
            """);
        d.Prepare("resolution", pr: "HEAD~1..HEAD");
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "resolution", "input.md"));
        Assert.Contains("- not confirmed by a developer yet: the failure may not be the reported one (D95)", input);
        Assert.DoesNotContain("no developer has confirmed", input);
        Assert.DoesNotContain("- the reporter expected:", input);

        File.WriteAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "bug-proof.json"), """
            {"schemaVersion":1,"key":"SHOP-1","outcome":"reproduced","confirmedBy":"Pat","verification":null,
             "attempts":[{"n":1,"outcome":"reproduced","commit":"abc","testName":"T","testFile":"Tests/T.cs"}]}
            """);
        d.Prepare("resolution", pr: "HEAD~1..HEAD");
        input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "resolution", "input.md"));
        Assert.Contains("- confirmed by Pat as the reported problem\n", input);
    }

    [Fact]
    public void A_dropped_list_item_leaves_no_gap_and_its_break_passes_on()
    {
        using var d = new Draft();
        d.Prepare();
        var o = JsonNode.Parse(GoodQuestions)!.AsObject();
        var parts = o["parts"]!.AsArray();
        parts[0] = Part("Hi Sam,");
        parts.Insert(1, Part("1. The scheduler runs twice on every save.", "paragraph"));   // no claim: left out
        for (var i = 2; i < parts.Count; i++)   // the rest move down one number and lose their own break
        {
            var t = (string)parts[i]!["text"]!;
            parts[i]!["text"] = $"{int.Parse(t[..1]) + 1}{t[1..]}";
            parts[i]!["start"] = "line";
        }
        parts[2]!["text"] = (string)parts[2]!["text"]! + " [C1]";   // a stray marker in the text is dropped too
        Assert.Equal("recorded", d.Check(o.ToJsonString()).Status);
        Assert.Equal(["1. The scheduler runs twice on every save."], d.Dropped());
        Assert.StartsWith("Hi Sam,\n\n1. Which version are you on?\n2. Where does it run?\n3. The ticket says", (string)d.Record()["text"]!);
    }

    [Theory]
    [InlineData("Which version are you on?", false)]
    [InlineData("Any logs from that time?", false)]
    [InlineData("Could you send the logs as well?", false)]
    [InlineData("Hi Sam, could you send the logs?", false)]                     // a greeting, then an ask
    [InlineData("Hi Sam, please send the logs.", false)]
    [InlineData("**Please send the logs.**", false)]
    [InlineData("**Thanks!**", false)]
    [InlineData("Thanks in advance!", false)]
    [InlineData("Thanks, Sam!", false)]
    [InlineData("Cheers, Sam", false)]
    [InlineData("Hi Émile, could you send the logs?", false)]
    [InlineData("Please, could you send the logs?", false)]
    [InlineData("Please share the logs as soon as possible.", false)]
    [InlineData("Let us know if anything changes on your side.", false)]
    [InlineData("Does the fix work on your side?", false)]
    [InlineData("Could you send the logs as an attachment?", false)]     // "as" here isn't a reason
    [InlineData("Could you log in as the affected user?", false)]
    [InlineData("Could you tell us which version you're on?", false)]    // "which" here isn't a clause
    [InlineData("Is there anyone who can reproduce it?", false)]
    [InlineData("Could you try https://staging.example.com/export?", false)]   // a link's colon
    [InlineData("Could you try it again at 9:30?", false)]                   // a time's colon
    [InlineData("Could you let us know as soon as it happens again?", false)]
    [InlineData("Could you tell us which is the right total?", false)]
    [InlineData("Do you know who has access?", false)]
    [InlineData("Could you correct that entry?", false)]
    [InlineData("The ticket says approval starts over 40, but the FSD says over 100: which is right?", true)]   // states first
    [InlineData("The cache corrupts every order, right?", true)]
    [InlineData("Can you confirm the scheduler runs twice on every save?", true)]                            // a fact to confirm
    [InlineData("Could you check the scheduler, which doubles each order?", true)]
    [InlineData("Please send the logs, as the cache corrupts every order.", true)]                           // a reason
    [InlineData("Please send the logs so we can fix the double charge in the scheduler.", true)]
    [InlineData("Please be informed the cache corrupts every order.", true)]
    [InlineData("Please, the cache corrupts every order.", true)]
    [InlineData("Could you confirm the scheduler runs twice on every save.", true)]
    [InlineData("Hey cache corrupts orders.", true)]                                                         // not a name
    [InlineData("Please — the cache corrupts every order.", true)]
    [InlineData("Please update to 3.2 which fixes the export.", true)]
    [InlineData("Please retry now that the rounding fix is deployed.", true)]
    [InlineData("Please send the logs (the cache corrupts every order).", true)]
    [InlineData("Did you know the cache corrupts every order?", true)]
    [InlineData("Let me know the cache drops rows.", true)]
    [InlineData("Please see https://x.com: the cache corrupts every order.", true)]   // the colon after a link counts
    [InlineData("Is it true that the cache corrupts every order?", true)]
    [InlineData("Please send the logs as it fails on every save.", true)]
    public void A_question_or_request_with_no_claim_must_only_ask(string part, bool dropped)
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(With(part)).Status);
        Assert.Equal(dropped, d.Dropped().Contains(part));
        Assert.Equal(!dropped, ((string)d.Record()["text"]!).Contains(part, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("The fix ships at 5 p.m. on Friday.")]                       // a single letter's full stop
    [InlineData("Step 2 says \"Click Save.\" but nothing happens.")]         // a quoted sentence
    [InlineData("Orders double... sometimes, on a slow save.")]               // an ellipsis
    [InlineData("See `Order.Place(). Retry()` for the cause.")]               // code in backticks
    [InlineData("Version 3.1 is affected.")]
    public void One_sentence_with_quotes_initials_or_an_ellipsis_is_still_one(string text)
    {
        using var d = new Draft();
        d.Prepare();
        Assert.Equal("recorded", d.Check(Claimed(text)).Status);
    }

    [Theory]
    [InlineData("[C1]")]   // only a marker: empty once markers go
    [InlineData(" ")]
    public void An_empty_part_is_an_error_not_a_crash(string text)
    {
        using var d = new Draft();
        d.Prepare();
        var step = d.Check(WithParts([Part("## Details"), Part(text, "line")]));
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e == "part 2 is empty.");
    }

    [Fact]
    public void A_claim_backs_one_part_only()
    {
        using var d = new Draft();
        d.Prepare();
        var step = d.Check(WithParts([Part("The PR changes the approval limit.", null, "C1"), Part("It also touches every other screen.", "line", "C1")]));
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.StartsWith("claim C1 is on parts 1 and 2 and 3"));
    }

    [Fact]
    public void Markdown_blocks_get_the_break_they_need_and_items_keep_their_numbers()
    {
        using var d = new Draft();
        d.Prepare();
        static JsonObject Claim(string id) => new() { ["id"] = id, ["links"] = new JsonArray(new JsonObject { ["kind"] = "ticket", ["quote"] = "Orders over 50 need approval before they are placed." }) };
        var o = JsonNode.Parse(GoodQuestions)!.AsObject();
        var parts = o["parts"]!.AsArray();
        parts[0] = Part("Thanks for the report.");
        parts.Insert(1, Part("## Details"));                                    // no start: a heading still starts a line
        parts.Insert(2, Part("We read the ticket closely.", null, "C7"));        // after a heading: a new line
        parts.Insert(3, Part("---", "line"));                                   // a rule needs a blank line before it
        parts.Insert(5, Part("It matters for the fix.", null, "C8"));            // same line as item 1: not an item
        parts.Insert(6, Part("  - on every save", "line", "C9"));               // nested: keeps its indent and the run
        parts.Add(Part("Kind regards,", "line"));
        foreach (var id in new[] { "C7", "C8", "C9" }) o["claims"]!.AsArray().Add(Claim(id));
        Assert.Equal("recorded", d.Check(o.ToJsonString()).Status);
        var text = (string)d.Record()["text"]!;
        Assert.StartsWith("Thanks for the report.\n## Details\nWe read the ticket closely.\n\n---\n\n1. Which version are you on? It matters for the fix.\n  - on every save\n2. Where does it run?\n3. The ticket says",
            text);
        Assert.EndsWith("which is right?\n\nKind regards,", text);   // a plain line after a list item: a new paragraph, not part of it
    }

    [Fact]
    public async Task Posting_sends_the_text_once_and_records_who_and_when()
    {
        using var d = new Draft();
        d.Prepare();
        d.Check(GoodQuestions);
        var tracker = new RecordingTracker();
        var time = new FixedTime(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        var record = await TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions", "Pat Developer", false, time, TestContext.Current.CancellationToken);
        Assert.Equal("Pat Developer", (string?)record["posted"]!["by"]);
        Assert.Equal("2026-10-25T12:00:00Z", (string?)record["posted"]!["at"]);
        Assert.Equal("42", (string?)record["posted"]!["commentId"]);
        var (key, text) = Assert.Single(tracker.Posted);
        Assert.Equal(Key, key);
        Assert.DoesNotContain("[C", text);
        Assert.Equal("already_posted", (await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "questions", "Pat", false, time, TestContext.Current.CancellationToken))).Code);
        Assert.Single(tracker.Posted);
        Assert.Equal("no_draft", (await Assert.ThrowsAsync<StangentException>(() =>
            TicketDrafts.PostAsync(d.App.Repo, tracker, Key, "resolution", "Pat", false, time, TestContext.Current.CancellationToken))).Code);
    }

    static void Git(string dir, string args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args.Split(' ')) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, p.StandardError.ReadToEnd());
    }

    sealed class RecordingTracker : IIssueTracker
    {
        public List<(string Key, string Text)> Posted { get; } = [];
        public string Source => "jira";
        public bool Replaying => false;
        public Task<Ticket> GetAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SavedFile> DownloadAsync(Ticket ticket, TicketAttachment attachment, string destination, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PostedComment> PostCommentAsync(string key, string markdown, CancellationToken ct = default)
        {
            Posted.Add((key, markdown));
            return Task.FromResult(new PostedComment(key, "42"));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
