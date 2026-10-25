using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
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
            App = new SpecApp($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");
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
        {"text":"Thanks for the report. 1. Which version are you on? [C1] 2. Where does it run? [C2] 3. The ticket says approval starts over 50 [C3], but the FSD says over 100 [C3]: which is right?",
         "claims":[
          {"id":"C1","links":[{"kind":"missing","field":"version"}]},
          {"id":"C2","links":[{"kind":"missing","field":"environment"}]},
          {"id":"C3","links":[{"kind":"ticket","quote":"Orders over 50 need approval before they are placed."},{"kind":"fsd","doc":"shop","section":"4.1","quote":"Orders over 100 need approval from a manager."}]}]}
        """;

    [Fact]
    public void A_questions_draft_asks_for_missing_fields_and_quotes_both_sides_of_a_clash()
    {
        using var d = new Draft();
        var step = d.Prepare();
        Assert.Equal(StepSpec.QuestionWriter, step.Agent);
        var input = File.ReadAllText(Path.Combine(d.Root, ".stangent", "tickets", Key, "questions", "input.md"));
        Assert.Contains("# Missing\n\nversion, environment", input);
        Assert.Contains("Orders over 100 need approval from a manager.", input);

        Assert.Equal("recorded", d.Check(GoodQuestions).Status);
        var record = JsonNode.Parse(File.ReadAllText(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "internal")))!;
        Assert.DoesNotContain("[C", (string)record["text"]!);
        Assert.Null(record["posted"]);
        Assert.Equal("current", d.Prepare().Status); // unchanged: no AI call
    }

    [Fact]
    public void Made_up_quotes_unknown_pages_and_unmarked_claims_are_rejected()
    {
        using var d = new Draft();
        d.Prepare();
        var bad = """
            {"text":"The ticket says approval starts over 40 [C1]. The orders page refuses big orders [C2]. Which version? [C4]",
             "claims":[
              {"id":"C1","links":[{"kind":"ticket","quote":"Orders over 40 need approval before they are placed."}]},
              {"id":"C2","links":[{"kind":"page","page":"page:/cart","statement":"S1","quote":"The cart screen lists"}]},
              {"id":"C3","links":[{"kind":"fsd","doc":"shop","section":"4.1","quote":"Orders over 100 need approval from the owner."}]}]}
            """;
        var errors = d.Check(bad).Errors!;
        Assert.Contains(errors, e => e.Contains("C1 link 1") && e.Contains("isn't in the ticket word for word"));
        Assert.Contains(errors, e => e.Contains("page `page:/cart` isn't one of the pages"));
        Assert.Contains(errors, e => e.Contains("C3 link 1") && e.Contains("word for word"));
        Assert.Contains(errors, e => e.Contains("claim C3 isn't marked"));
        Assert.Contains(errors, e => e.Contains("marks [C4] but there's no such claim"));
        Assert.Contains(errors, e => e.Contains("the bug's version is missing"));
        Assert.Equal("failed", d.Check(bad).Status);
    }

    [Fact]
    public void A_client_draft_may_not_hold_code_names_paths_or_ids()
    {
        using var d = new Draft();
        d.Prepare(tone: "client");
        var leaky = GoodQuestions.Replace("Thanks for the report.", "We looked at `OrderService.Place` in Core/OrderService.cs and page:/orders.");
        var errors = d.Check(leaky).Errors!;
        Assert.Contains(errors, e => e.Contains("backticks"));
        Assert.Contains(errors, e => e.Contains("\"Core/OrderService.cs\" looks like a file path"));
        Assert.Contains(errors, e => e.Contains("\"OrderService.Place\" is a name from the code"));
        Assert.Contains(errors, e => e.Contains("\"page:\" is part of an id"));
        Assert.Equal("recorded", d.Check(GoodQuestions).Status);
        Assert.True(File.Exists(TicketDrafts.JsonPath(new TicketPaths(d.Root, Key), "questions", "client")));
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
        var withProof = GoodQuestions.Replace("which is right?", "which is right? Our test of an order of 150 passed [C4].")
            .Replace("]}]}", "]},{\"id\":\"C4\",\"links\":[{\"kind\":\"proof\"}]}]}");
        Assert.Equal("recorded", d.Check(withProof).Status);
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
            {"text":"Fixed in the PR [C1]: the approval limit in Core/OrderService.cs [C2] is now 50. The rule was: An order over 100 is refused until it is approved [C3].",
             "claims":[{"id":"C1","links":[{"kind":"pr"}]},{"id":"C2","links":[{"kind":"file","file":"Core/OrderService.cs"}]},
              {"id":"C3","links":[{"kind":"page","page":"{{SpecApp.Post}}","statement":"R1","quote":"An order over 100 is refused until it is approved."}]}]}
            """;
        Assert.Equal("recorded", d.Check(output, "resolution").Status);
        Assert.Contains(d.Check(output.Replace("\"file\":\"Core/OrderService.cs\"", "\"file\":\"Core/Other.cs\""), "resolution") is var r && r.Status == "current" ? [] : r.Errors ?? [],
            e => e.Contains("isn't one of the files"));
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
