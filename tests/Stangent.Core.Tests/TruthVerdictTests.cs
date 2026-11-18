using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Proof;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>Truth map layer 4 (D361, D364, D366 (4)): the bundle's truth list, the reproducer's verdict checked by code,
/// the stops that write no test, and the PM's question. On the shop app with a hand-written truth-map.json; canned
/// reproducer outputs and a fake test runner, no AI.</summary>
public sealed class TruthVerdictTests : IDisposable
{
    const string Key = "SHOP-1";
    const string TestFile = "Tests/OrderTests.cs";
    const string TestName = "Shop.Tests.OrderTests.Rejects_orders_over_100";
    const string Rule = "Orders over 100 are rejected until someone approves them.";
    const string Description = """
        ### Current Behavior

        An order of 150 is placed without approval.

        ### Expected Behavior

        Orders over 100 are rejected until someone approves them.

        ### Steps To Reproduce

        Call `OrderService.Place` with 150.
        """;

    readonly SpecApp app;
    readonly BugProofTests.FakeRunner runner = new();
    string Root => app.Root;

    public TruthVerdictTests() : this(true) { }

    TruthVerdictTests(bool on)
    {
        app = new SpecApp($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n  aiPoints: false\ncomponents:\n  truth-verdict: {(on ? "true" : "false")}\n");
        app.Write("Core/Shop.Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        app.Write("Tests/Shop.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /><PackageReference Include="xunit" /><ProjectReference Include="..\Core\Shop.Core.csproj" /></ItemGroup>
            </Project>
            """);
        TicketFixtures.Make(Path.Combine(Root, "replay"), Key, "Bug", "Big orders skip approval", Description);
        TicketPull.RunAsync(app.Repo, Trackers.Open(app.Repo, new MemorySecretStore(), TextWriter.Null, null), Key).GetAwaiter().GetResult();
    }

    public void Dispose() => app.Dispose();

    static JsonObject Entry(int n, string state, string standing, string? ruling, string? quote, string evidence, string? confidence = null, string? statement = null) => new()
    {
        ["n"] = n, ["key"] = quote is null ? new JsonObject { ["section"] = "S-0001", ["link"] = "file:x" } : new JsonObject { ["section"] = "S-0001", ["quote"] = quote },
        ["state"] = state, ["standing"] = standing, ["ruling"] = ruling, ["statement"] = statement, ["confidence"] = confidence, ["by"] = ruling is null ? null : "Pat PM",
        ["notes"] = new JsonArray("The review's note."), ["evidence"] = new JsonArray(evidence),
    };

    /// <summary>A truth map for the shop FSD with these entries in its §1, and the ticket's starting point in OrderService.</summary>
    void Map(params JsonObject[] entries)
    {
        app.Write("specs/fsd/shop/truth-map.json", new JsonObject
        {
            ["doc"] = "shop",
            ["sections"] = new JsonArray(new JsonObject
            {
                ["section"] = "1", ["sectionId"] = "S-0001", ["title"] = "Orders", ["text"] = Rule + "\nRefunds are manual.\n", ["entries"] = new JsonArray([.. entries]),
            }),
            ["undocumented"] = new JsonArray(),
        }.ToJsonString());
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Update(app.Repo, ctx, Key, [SpecApp.Place], [], "Pat Developer");
        TicketBundle.Build(app.Repo, ctx, Key);
    }

    RunStep Prepare() => BugProof.Prepare(app.Repo, TicketContext.Load(app.Repo), Key, null);

    RunStep Check(JsonObject output)
    {
        File.WriteAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "output.json"), output.ToJsonString());
        var ok = JsonNode.Parse("""{"type":"result","subtype":"success","num_turns":3,"rounds":3,"total_cost_usd":0.1}""")!;
        return BugProof.CheckAsync(app.Repo, Key, ok, runner).GetAwaiter().GetResult();
    }

    JsonObject Attempt => (JsonObject)BugProof.Load(new TicketPaths(Root, Key))!["attempts"]!.AsArray().Last()!;

    static JsonObject Verdict(string verdict, string? entry = "T1", string? quote = Rule) => new()
    {
        ["verdict"] = verdict, ["entry"] = entry, ["quote"] = quote, ["why"] = "Because of the rule.",
    };

    static JsonObject Written(JsonObject? verdict) => new JsonObject()
    {
        ["outcome"] = "test-written", ["target"] = "dotnet:Tests/Shop.Tests.csproj", ["testFile"] = TestFile, ["testName"] = TestName,
        ["assertion"] = new JsonObject { ["line"] = 8, ["text"] = "Assert.Throws<ArgumentException>(() => new OrderService().Place(150));" },
        ["expectedQuote"] = Rule, ["actualQuote"] = "An order of 150 is placed without approval.", ["shows"] = "No exception was thrown",
    }.Also(o => { if (verdict is not null) o["verdict"] = verdict; });

    static JsonObject Stopped(JsonObject verdict) => new() { ["outcome"] = "stopped", ["verdict"] = verdict };

    void WriteTest() => app.Write(TestFile, """
        using Xunit;
        namespace Shop.Tests;
        public class OrderTests
        {
            [Fact]
            public void Rejects_orders_over_100()
            {
                Assert.Throws<ArgumentException>(() => new OrderService().Place(150));
            }
        }
        """);

    [Fact]
    public void The_bundle_lists_the_entries_on_the_tickets_files_and_a_backed_verdict_goes_on_to_the_test()
    {
        Map(Entry(1, "gap", "ruled", "fsd-right", Rule, "Core/OrderService.cs:5"), Entry(2, "gap", "ruled", "code-right", "Refunds are manual.", "Web/Refunds.cs:3"));
        var bundle = TicketBundle.LoadOrFail(Root, Key);
        var truth = bundle["truth"]!;
        Assert.Equal("built", (string)truth["state"]!);
        var t1 = Assert.Single(truth["entries"]!.AsArray())!;   // the refund entry's file isn't the ticket's
        Assert.Equal(("T1", true, 1), ((string)t1["id"]!, (bool)t1["usable"]!, (int)t1["n"]!));
        Assert.Contains("Refunds are manual.", (string)truth["sections"]![0]!["text"]!);
        Assert.Contains("## The truth map (what the PM agreed)", File.ReadAllText(new TicketPaths(Root, Key).BundleMarkdown));

        Assert.Equal("prepared", Prepare().Status);
        var input = File.ReadAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("# The truth map and your verdict", input);
        Assert.Contains("**T1**", input);
        WriteTest();
        runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure: No exception was thrown", $"   at Shop.Tests.OrderTests.Rejects_orders_over_100() in /ci/{TestFile}:line 8")));
        runner.All = TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure", ""));
        Assert.Equal("recorded", Check(Written(Verdict("backed"))).Status);
        Assert.Equal("reproduced", (string)Attempt["outcome"]!);
        Assert.Equal(("backed", false, true), ((string)Attempt["verdict"]!["verdict"]!, (bool)Attempt["verdict"]!["forced"]!, (bool)Attempt["verdict"]!["pmConfirmed"]!));
    }

    [Fact]
    public void A_contradicted_ticket_stops_with_no_test_run()
    {
        Map(Entry(1, "gap", "ruled", "code-right", Rule, "Core/OrderService.cs:5"));
        Prepare();
        Assert.Equal("recorded", Check(Stopped(Verdict("contradicted"))).Status);
        Assert.Equal(("not-provable", "contradicted"), ((string)Attempt["outcome"]!, (string)Attempt["noProof"]!));
        Assert.Contains("the truth map contradicts the ticket: T1 (shop item 1)", (string)Attempt["reason"]!);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public void Code_makes_it_unclear_when_the_cited_entry_cannot_back_a_ticket()
    {
        Map(Entry(1, "gap", "ruled", "fsd-right", "Refunds are manual.", "Core/OrderService.cs:9"),
            Entry(2, "match", "unconfirmed", null, Rule, "Core/OrderService.cs:5", confidence: "medium"));
        Prepare();
        WriteTest();
        Assert.Equal("recorded", Check(Written(Verdict("backed", "T2"))).Status);
        var v = Attempt["verdict"]!;
        Assert.Equal(("unclear", true, "backed"), ((string)v["verdict"]!, (bool)v["forced"]!, (string)v["said"]!));
        Assert.Equal("unclear", (string)Attempt["noProof"]!);
        Assert.Contains("can't back a ticket (a match of medium confidence the PM hasn't confirmed)", (string)Attempt["reason"]!);
        Assert.Empty(runner.Commands);   // the test it wrote isn't run
    }

    [Fact]
    public void With_nothing_that_can_back_it_there_is_no_ai_call()
    {
        Map(Entry(1, "gap", "blank", null, Rule, "Core/OrderService.cs:5"));
        var step = Prepare();
        Assert.Equal("recorded", step.Status);
        Assert.False(File.Exists(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "input.md")));
        Assert.Equal("unclear", (string)Attempt["noProof"]!);
        Assert.Contains("T1 (shop item 1) nobody has ruled on it", (string)Attempt["reason"]!);
        Assert.Equal("current", Prepare().Status);
        // Nothing on the ticket's files at all: not covered.
        Map(Entry(1, "gap", "ruled", "fsd-right", Rule, "Web/Elsewhere.cs:5"));
        Prepare();
        Assert.Contains("nothing in the truth map covers this ticket's files", (string)Attempt["reason"]!);
        Assert.Null(Attempt["verdict"]!["entry"]);
    }

    [Fact]
    public void With_no_test_target_the_verdict_still_comes_and_no_test_is_written()
    {
        // D408: no test project covers the code (and no app for Playwright), but an entry can back the ticket.
        File.Delete(Path.Combine(Root, "Tests", "Shop.Tests.csproj"));
        Map(Entry(1, "gap", "ruled", "fsd-right", Rule, "Core/OrderService.cs:5"));
        Assert.NotEqual("recorded", Prepare().Status);
        var input = File.ReadAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("# No test target", input);
        Assert.DoesNotContain("# Test targets", input);
        // A test written anyway gets the retry; stopped with backed is recorded with its verdict and no test.
        Assert.Equal("retry", Check(Written(Verdict("backed"))).Status);
        Assert.Equal("recorded", Check(Stopped(Verdict("backed"))).Status);
        Assert.Equal(("not-provable", "backed"), ((string)Attempt["outcome"]!, (string)Attempt["verdict"]!["verdict"]!));
        Assert.Null(Attempt["noProof"]);
        Assert.Contains("the truth map backs the ticket (T1, shop item 1), but no test project covers its code", (string)Attempt["reason"]!);
        Assert.Empty(runner.Commands);

        // Contradicted stops as always.
        Map(Entry(1, "gap", "ruled", "code-right", Rule, "Core/OrderService.cs:5"));
        Prepare();
        Assert.Equal("recorded", Check(Stopped(Verdict("contradicted"))).Status);
        Assert.Equal(("not-provable", "contradicted"), ((string)Attempt["outcome"]!, (string)Attempt["noProof"]!));
    }

    [Fact]
    public void A_verdict_that_cites_nothing_real_gets_the_retry()
    {
        Map(Entry(1, "gap", "ruled", "fsd-right", Rule, "Core/OrderService.cs:5"));
        Prepare();
        WriteTest();
        var none = Check(Written(null));
        Assert.Equal("retry", none.Status);
        Assert.Contains(none.Errors!, e => e.StartsWith("Give `verdict`", StringComparison.Ordinal));
        var made = Check(Written(Verdict("backed", "T9", "Orders over 100 ship at once without anyone looking.")));
        Assert.Equal("failed", made.Status);
        Assert.Contains("entry `T9` isn't one of the truth map entries", (string)Attempt["reason"]!);
    }

    [Fact]
    public void The_question_draft_quotes_the_rule_and_says_how_to_answer()
    {
        var blank = new JsonObject { ["verdict"] = "unclear", ["forced"] = true, ["entry"] = Entry(1, "gap", "blank", null, Rule, "Core/OrderService.cs:5").Also(e => { e["section"] = "1"; e["title"] = "Orders"; e["why"] = "nobody has ruled on it"; }) };
        var text = TicketDrafts.TruthQuestion(blank, "internal");
        Assert.Contains("the FSD's §1 Orders says “" + Rule + "”", text);
        Assert.Contains("(the code: `Core/OrderService.cs:5`)", text);
        Assert.EndsWith("or the rule in your own words.", text);
        Assert.Contains("Reply with a line starting `Ruling:`", text);
        Assert.DoesNotContain("OrderService.cs", TicketDrafts.TruthQuestion(blank, "client"));   // no code names for a client
        var contradicted = new JsonObject { ["verdict"] = "contradicted", ["forced"] = false, ["entry"] = Entry(1, "gap", "ruled", "code-right", Rule, "Core/OrderService.cs:5", statement: "Orders up to 200 need no approval.").Also(e => { e["section"] = "1"; e["title"] = "Orders"; }) };
        Assert.Contains("the agreed rule is “Orders up to 200 need no approval.”", TicketDrafts.TruthQuestion(contradicted, "internal"));
        Assert.Contains("nothing in the FSD or the agreed rules says", TicketDrafts.TruthQuestion(new JsonObject { ["verdict"] = "unclear", ["entry"] = null }, "internal"));
    }

    [Fact]
    public void A_verdict_that_goes_against_its_entrys_ruling_gets_the_retry()
    {
        Map(Entry(1, "gap", "ruled", "code-right", Rule, "Core/OrderService.cs:5"));
        Prepare();
        WriteTest();
        var made = Check(Written(Verdict("backed")));
        Assert.Equal("retry", made.Status);
        Assert.Contains(made.Errors!, e => e.Contains("can't back this bug", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pms_words_decide_over_a_keep_ruling()
    {
        // Only backed on keep: contradicted on "remove", even quoting the PM's words, still goes against its ruling.
        Map(Entry(1, "undocumented", "ruled", "remove", null, "Core/OrderService.cs:5", statement: "Take this approval step out of the order flow."));
        Prepare();
        var removed = Check(Stopped(Verdict("contradicted", quote: "Take this approval step out of the order flow.")));
        Assert.Contains(removed.Errors!, e => e.Contains("can't contradict this bug", StringComparison.Ordinal));

        // D411: undocumented code ruled "keep", with the PM's words saying how it should work. A backed verdict that quotes
        // those words stands; one resting on anything else of that entry still goes against "keep".
        const string Words = "An order over 100 waits for someone to approve it.";
        Map(Entry(1, "undocumented", "ruled", "keep", null, "Core/OrderService.cs:5", statement: Words));
        Assert.Equal("prepared", Prepare().Status);
        var input = File.ReadAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("keeps the feature, working as the words say", input);
        Assert.Contains("The PM's words (how the kept code should work)", File.ReadAllText(new TicketPaths(Root, Key).BundleMarkdown));
        WriteTest();
        var other = Check(Written(Verdict("backed", quote: "The review's note.")));
        Assert.Equal("retry", other.Status);
        Assert.Contains(other.Errors!, e => e.Contains("can't back this bug", StringComparison.Ordinal));
        Prepare();
        var shortQuote = Check(Written(Verdict("backed", quote: "waits for someone")));   // under four words: the ruling stands
        Assert.Contains(shortQuote.Errors!, e => e.Contains("can't back this bug", StringComparison.Ordinal));
        Prepare();
        runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure: No exception was thrown", $"   at Shop.Tests.OrderTests.Rejects_orders_over_100() in /ci/{TestFile}:line 8")));
        runner.All = TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure", ""));
        Assert.Equal("recorded", Check(Written(Verdict("backed", quote: Words))).Status);
        Assert.Equal(("backed", false), ((string)Attempt["verdict"]!["verdict"]!, (bool)Attempt["verdict"]!["forced"]!));
    }

    [Fact]
    public void A_reproduced_proof_is_kept_when_its_rule_later_needs_the_pm_and_a_stale_bundle_is_refused()
    {
        Map(Entry(1, "gap", "ruled", "fsd-right", Rule, "Core/OrderService.cs:5"));
        Prepare();
        WriteTest();
        runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure: No exception was thrown", $"   at Shop.Tests.OrderTests.Rejects_orders_over_100() in /ci/{TestFile}:line 8")));
        runner.All = TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure", ""));
        Check(Written(Verdict("backed")));
        Map(Entry(1, "gap", "flagged", "fsd-right", Rule, "Core/OrderService.cs:5"));
        var kept = Prepare();
        Assert.Equal("current", kept.Status);
        Assert.Contains("The reproduced proof is kept", kept.Warning);
        Assert.Equal("reproduced", (string)BugProof.Load(new TicketPaths(Root, Key))!["outcome"]!);

        // A bundle built before the switch was turned on has no truth list: refused, not judged without it.
        var bundle = TicketBundle.LoadOrFail(Root, Key);
        bundle["truth"]!["state"] = "off";
        File.WriteAllText(new TicketPaths(Root, Key).BundleJson, bundle.ToJsonString());
        Assert.Equal("stale_bundle", Assert.Throws<StangentException>(Prepare).Code);
    }

    [Fact]
    public void Evidence_lines_are_read_back_to_their_file_and_a_broken_map_is_an_error()
    {
        Assert.Equal("src/a.cs", TruthList.FileOf("src/a.cs:10"));
        Assert.Equal("src/a.cs", TruthList.FileOf(" ./src\\a.cs:10-12 "));
        Assert.Equal("src/a.cs", TruthList.FileOf("src/a.cs:10:5"));
        Assert.Equal("src/a.cs", TruthList.FileOf("src/a.cs"));
        app.Write("specs/fsd/shop/truth-map.json", "{ \"doc\": \"shop\" }");
        Assert.Equal("bad_truth_map", Assert.Throws<StangentException>(() => TruthList.Build(app.Repo, ["Core/OrderService.cs"])).Code);
    }

    [Fact]
    public void The_question_reads_right_for_undocumented_code_and_flagged_rulings()
    {
        var loose = Entry(1, "undocumented", "ruled", "keep", null, "Core/OrderService.cs:9");
        var text = TicketDrafts.TruthQuestion(new JsonObject { ["verdict"] = "contradicted", ["entry"] = loose }, "internal");
        Assert.StartsWith("**For the PM:** this code isn't in the FSD, and the agreed rule is to keep this code as it is", text);
        var flagged = Entry(1, "gap", "flagged", "fsd-right", Rule, "Core/OrderService.cs:5").Also(e => { e["section"] = "1"; e["title"] = "Orders"; e["why"] = "its ruling needs another look"; });
        var f = TicketDrafts.TruthQuestion(new JsonObject { ["verdict"] = "unclear", ["entry"] = flagged }, "client");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(f, "needs another look"));
        Assert.DoesNotContain("`Core", f);
    }

    /// <summary>A flows index with these flows, each <c>(id, kind, point, links, step lines)</c>.</summary>
    void Flows(params (string Id, string Kind, string? Point, string[] Links, string[] Ats)[] flows)
    {
        var index = new JsonArray();
        foreach (var f in flows)
        {
            var file = $"specs/flows/{f.Id}.json";
            index.Add(new JsonObject
            {
                ["id"] = f.Id, ["kind"] = f.Kind, ["point"] = f.Point, ["file"] = file, ["links"] = new JsonArray([.. f.Links.Select(l => (JsonNode)l)]),
            });
            app.Write(file, new JsonObject
            {
                ["id"] = f.Id,
                ["steps"] = new JsonArray([.. f.Ats.Select((at, i) => (JsonNode)new JsonObject { ["n"] = i + 1, ["at"] = at, ["conditions"] = new JsonArray() })]),
            }.ToJsonString());
        }
        app.Write("specs/flows/index.json", new JsonObject { ["contract"] = "flows", ["flows"] = index }.ToJsonString());
    }

    static JsonObject Linked(JsonObject entry, string kind, string id) => entry.Also(e => e["link"] = new JsonObject { ["kind"] = kind, ["id"] = id });

    [Fact]
    public void An_entry_reaches_the_ticket_through_the_flow_its_code_is_on_or_the_screen_that_flow_opens()
    {
        // D410: the ticket's point is OrderService.Place (lines 3–8). The orders screen's opening flow walks into it at
        // line 5; the refund click never does. A rule cited only by the screen's template still judges the ticket.
        Flows(("screen-opens-orders", "screen-opens", "page:Web|/orders", [], ["Web/Orders.tsx:2", "Core/OrderService.cs:5"]),
            ("click-refund", "click", null, [], ["Web/Refunds.cs:3", "Core/OrderService.cs:12"]));
        Map(Entry(1, "gap", "ruled", "fsd-right", Rule, "Core/OrderService.cs:5"),
            Linked(Entry(2, "undocumented", "ruled", "keep", null, "Web/orders.html:4"), "screen", "page:Web|/orders"),
            Linked(Entry(3, "undocumented", "ruled", "keep", null, "Web/Orders.tsx:9"), "flow", "screen-opens-orders"),
            Linked(Entry(4, "undocumented", "ruled", "keep", null, "Web/Refunds.cs:3"), "flow", "click-refund"),
            Linked(Entry(5, "undocumented", "ruled", "keep", null, "Web/other.html:1"), "screen", "page:Web|/other"));
        var truth = TicketBundle.LoadOrFail(Root, Key)["truth"]!;
        var got = truth["entries"]!.AsArray().Select(e => ((int)e!["n"]!, (string)e["via"]!)).ToList();
        Assert.Equal([(1, "file"), (2, "screen:screen-opens-orders"), (3, "flow:screen-opens-orders")], got);
        Assert.Equal(["screen-opens-orders"], truth["reach"]!["flows"]!.AsArray().Select(f => (string)f!));
        Assert.False((bool)truth["reach"]!["tooMany"]!);
        var md = File.ReadAllText(new TicketPaths(Root, Key).BundleMarkdown);
        Assert.Contains("Found through its screen, which the flow `screen-opens-orders` opens", md);
        Assert.Contains("Found through the flow `screen-opens-orders`, which the ticket's code is on (not by file)", md);
        Assert.Equal("prepared", Prepare().Status);
        Assert.Contains("and the flows and screens its code is on", File.ReadAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "input.md")));
    }

    [Fact]
    public void Shared_code_reaches_the_flows_that_continue_into_it_and_code_on_too_many_flows_brings_in_none()
    {
        // One hop up from a shared flow: the checkout click continues into shared-place, which walks Place.
        Flows(("shared-place", "shared", null, [], ["Core/OrderService.cs:6"]),
            ("click-checkout", "click", null, ["shared-place"], ["Web/Checkout.tsx:4"]),
            ("click-other", "click", null, [], ["Web/Other.tsx:1"]));
        Map(Linked(Entry(1, "undocumented", "ruled", "keep", null, "Web/Checkout.tsx:4"), "flow", "click-checkout"),
            Linked(Entry(2, "undocumented", "ruled", "keep", null, "Web/Other.tsx:1"), "flow", "click-other"));
        var truth = TicketBundle.LoadOrFail(Root, Key)["truth"]!;
        Assert.Equal([(1, "flow:click-checkout")], truth["entries"]!.AsArray().Select(e => ((int)e!["n"]!, (string)e["via"]!)));
        Assert.Equal(["click-checkout", "shared-place"], truth["reach"]!["flows"]!.AsArray().Select(f => (string)f!));

        // Place on six screens' flows: too widely shared to tell which one the ticket is about.
        Flows([.. Enumerable.Range(1, TruthList.MaxFlows + 1).Select(i => ($"screen-opens-s{i}", "screen-opens", (string?)$"page:Web|/s{i}", Array.Empty<string>(), new[] { "Core/OrderService.cs:4" }))]);
        Map(Linked(Entry(1, "undocumented", "ruled", "keep", null, "Web/s1.html:1"), "screen", "page:Web|/s1"));
        truth = TicketBundle.LoadOrFail(Root, Key)["truth"]!;
        Assert.Empty(truth["entries"]!.AsArray());
        Assert.True((bool)truth["reach"]!["tooMany"]!);
        Assert.Equal(TruthList.MaxFlows + 1, truth["reach"]!["flows"]!.AsArray().Count);
        Assert.Contains($"sits in more than {TruthList.MaxFlows} flows (shared code aside), too many to tell which screen", File.ReadAllText(new TicketPaths(Root, Key).BundleMarkdown));

        // Flow files and index lines that don't read are skipped, never a failed bundle.
        Flows(("screen-opens-orders", "screen-opens", "page:Web|/orders", [], ["Core/OrderService.cs:5"]));
        app.Write("specs/flows/broken.json", "not json");
        app.Write("specs/flows/array.json", "[]");
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specs/flows/index.json")))!.AsObject();
        index["flows"]!.AsArray().Add(new JsonObject { ["id"] = "broken", ["file"] = "specs/flows/broken.json" });
        index["flows"]!.AsArray().Add(new JsonObject { ["id"] = "array", ["file"] = "specs/flows/array.json" });
        index["flows"]!.AsArray().Add(new JsonObject { ["id"] = 5, ["file"] = "specs/flows/screen-opens-orders.json", ["links"] = new JsonArray(5) });
        app.Write("specs/flows/index.json", index.ToJsonString());
        Map(Linked(Entry(1, "undocumented", "ruled", "keep", null, "Web/orders.html:4"), "screen", "page:Web|/orders"),
            Entry(2, "undocumented", "ruled", "keep", null, "Web/x.html:1").Also(e => e["link"] = new JsonObject { ["kind"] = "flow", ["id"] = 7 }));
        truth = TicketBundle.LoadOrFail(Root, Key)["truth"]!;
        Assert.Equal([(1, "screen:screen-opens-orders")], truth["entries"]!.AsArray().Select(e => ((int)e!["n"]!, (string)e["via"]!)));
    }

    [Fact]
    public void A_found_files_quoted_line_is_read_back_to_the_smallest_node_holding_it()
    {
        Node[] nodes = [new("t", NodeKind.Type, "OrderService", null, "Core/OrderService.cs", 1, 9), new("m", NodeKind.Method, "OrderService.Place", null, "Core/OrderService.cs", 3, 8)];
        Assert.Equal([new TruthList.Span("Core/OrderService.cs", 3, 8)],
            TruthList.SpansOfLine(Root, "Core/OrderService.cs", "  if (total > 100) throw new ArgumentException(\"Orders over 100 need approval\");", _ => nodes));
        Assert.Empty(TruthList.SpansOfLine(Root, "Core/OrderService.cs", "not in the file", _ => nodes));
        Assert.Empty(TruthList.SpansOfLine(Root, "Core/Missing.cs", "public class OrderService", _ => nodes));
        Assert.Empty(TruthList.SpansOfLine(Root, "Core/OrderService.cs", "   ", _ => nodes));
        // A line outside any method gives no span (the class would be too wide); one that reads the same in two methods gives none.
        Assert.Empty(TruthList.SpansOfLine(Root, "Core/OrderService.cs", "public class OrderService", _ => nodes));
        app.Write("Core/Twice.cs", "class T\n{\n  int A() {\n    return 1;\n  }\n  int B() {\n    return 1;\n  }\n}\n");
        Node[] twice = [new("a", NodeKind.Method, "T.A", null, "Core/Twice.cs", 3, 5), new("b", NodeKind.Method, "T.B", null, "Core/Twice.cs", 6, 8)];
        Assert.Empty(TruthList.SpansOfLine(Root, "Core/Twice.cs", "return 1;", _ => twice));
        Assert.Equal([new TruthList.Span("Core/Twice.cs", 6, 8)], TruthList.SpansOfLine(Root, "Core/Twice.cs", "int B() {", _ => twice));
    }
}
