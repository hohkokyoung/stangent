using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>Phase 3 step 3: starting points on the shop app: the file finder (D101) with canned outputs (no LLM), and
/// code's signals (D70) when the finder is off.</summary>
public class TicketPointsTests
{
    const string OrdersRoute = "page:/orders";

    static SpecApp App(string extra = "") => new($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");

    /// <summary>The finder off: code's own signals are the starting points (D70, D101).</summary>
    static SpecApp CodeOnly() => App("  aiPoints: false\n");

    static JsonObject Points(SpecApp app, string key, string description, IEnumerable<string>? comments = null, IEnumerable<TicketFixtures.File>? files = null,
        string[]? add = null, string[]? remove = null)
    {
        if (!Directory.Exists(Path.Combine(app.Root, "replay", key)))
        {
            TicketFixtures.Make(Path.Combine(app.Root, "replay"), key, "Bug", "Orders", description, comments, files);
            var tracker = Trackers.Open(app.Repo, new MemorySecretStore(), TextWriter.Null, null);
            TicketPull.RunAsync(app.Repo, tracker, key).GetAwaiter().GetResult();
            tracker.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        return TicketPoints.Update(app.Repo, TicketContext.Load(app.Repo), key, add ?? [], remove ?? [], "Pat Developer");
    }

    static List<(string Kind, string Id, string Signal)> List(JsonObject points) =>
        [.. points["points"]!.AsArray().Select(p => ((string)p!["kind"]!, (string)p["id"]!, (string)p["signal"]!))];

    [Fact]
    public void The_file_finder_off_by_its_component_works_as_the_old_key_does()
    {
        using var app = App("");
        app.Repo.Config.Tickets.AiPoints = true;
        using var off = App("");
        off.Repo.Config.Components["file-finder"] = false;
        StangentComponents.Apply(off.Repo.Config);
        Assert.NotEqual("off", (string)Points(app, "SON-20", "Orders fail")["ai"]!["status"]!);
        Assert.Equal("off", (string)Points(off, "SON-20", "Orders fail")["ai"]!["status"]!);
    }

    [Fact]
    public void A_stack_frame_finds_its_method_by_file_and_line_or_by_name()
    {
        using var app = CodeOnly();
        var p = Points(app, "SHOP-1", """
            It crashed:
            System.ArgumentException: Orders over 100 need approval
               at Shop.Core.OrderService.Place(Decimal total) in /home/build/src/Core/OrderService.cs:line 5
               at Shop.Api.OrdersController.<Create>d__2.MoveNext()
            """);
        var points = List(p);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Place, "stack-frame"), points);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Create, "stack-frame"), points);
        Assert.Equal("off", (string?)p["ai"]!["status"]);
    }

    [Fact]
    public void A_stack_trace_in_a_text_attachment_counts_too()
    {
        using var app = CodeOnly();
        var trace = "at Shop.Core.ReceiptHandler.Handle(OrderPlaced e) in C:\\work\\shop\\Core\\OrderService.cs:line 12\n"u8.ToArray();
        var p = Points(app, "SHOP-2", "See the attached log.", files: [new("1", "error.log", "text/plain", trace)]);
        var point = Assert.Single(p["points"]!.AsArray());
        Assert.Equal(SpecApp.Receipt, (string?)point!["id"]);
        Assert.Equal("attachment error.log", (string?)point["where"]);
    }

    [Fact]
    public void URLs_find_route_pages_and_endpoint_pages_and_file_paths_are_ignored()
    {
        using var app = CodeOnly();
        var p = Points(app, "SHOP-3", "On http://localhost:5000/orders the button sends POST /api/orders and gets a 500. Log: /var/log/shop/app.log");
        var points = List(p);
        Assert.Contains((StartingPoint.Page, OrdersRoute, "url"), points);
        Assert.Contains((StartingPoint.Page, SpecApp.Post, "url"), points);
        Assert.Equal(2, points.Count);
    }

    [Fact]
    public void Quoted_screen_names_and_symbol_names_find_pages_and_symbols()
    {
        using var app = CodeOnly();
        var p = Points(app, "SHOP-4", "The \"Orders\" screen fails in `OrderService.Place` when called from ReceiptHandler.Handle.");
        var points = List(p);
        Assert.Contains((StartingPoint.Page, OrdersRoute, "title"), points);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Place, "symbol"), points);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Receipt, "symbol"), points);
    }

    const string Crash = """
        It crashed when I placed a big order:
        System.ArgumentException: Orders over 100 need approval
           at Shop.Core.OrderService.Place(Decimal total) in /home/build/src/Core/OrderService.cs:line 5
        """;
    const string Line = "if (total > 100) throw new ArgumentException(\"Orders over 100 need approval\");";

    static string File1(string path = "Core/OrderService.cs", string quote = "System.ArgumentException: Orders over 100 need approval", string line = Line) =>
        new JsonObject { ["path"] = path, ["quote"] = quote, ["line"] = line, ["why"] = "Throws the error the ticket shows." }.ToJsonString();

    [Fact]
    public void With_the_finder_on_code_signals_are_not_starting_points_and_the_finder_gets_the_ticket_only()
    {
        using var app = App();
        var p = Points(app, "SHOP-5", Crash);
        Assert.Empty(p["points"]!.AsArray());               // the stack frame is a lead for the finder, not a point (D101)
        Assert.Equal("needed", (string?)p["ai"]!["status"]);

        var step = TicketPoints.Prepare(app.Repo, TicketContext.Load(app.Repo), "SHOP-5");
        Assert.Equal(StepSpec.FileFinder, step.Agent);
        var input = File.ReadAllText(Path.Combine(app.Root, ".stangent", "tickets", "SHOP-5", "points", "input.md"));
        Assert.Contains("# The ticket\n\n## summary", input);
        Assert.Contains("Orders over 100 need approval", input);
        Assert.Contains("file-finder-output", input);
        Assert.DoesNotContain("# Every page", input);         // no page list: it searches the repo itself
    }

    [Fact]
    public void A_found_file_is_checked_by_path_quote_and_line_and_recorded_with_them()
    {
        using var app = App();
        Points(app, "SHOP-5", Crash);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-5");
        Canned(app, "SHOP-5", $$"""{"files":[{{File1("./Core/OrderService.cs")}},{{File1()}}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-5", null, "Pat Developer").Status);

        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-5");
        Assert.Equal([(StartingPoint.File, "Core/OrderService.cs", "ai")], List(after));   // "./" dropped, the same file once
        var point = after["points"]![0]!;
        Assert.Equal(Line, (string?)point["line"]);
        Assert.Equal("Throws the error the ticket shows.", (string?)point["why"]);
        Assert.Equal("OrderService.cs", (string?)point["title"]);
        Assert.Equal("found", (string?)after["ai"]!["status"]);

        // Nothing changed: no AI call.
        Assert.Equal("found", (string?)Points(app, "SHOP-5", Crash)["ai"]!["status"]);
        Assert.Equal("current", TicketPoints.Prepare(app.Repo, ctx, "SHOP-5").Status);
    }

    [Fact]
    public void A_made_up_path_quote_or_line_gets_one_retry_and_what_still_fails_is_listed_apart()
    {
        using var app = App();
        Points(app, "SHOP-6", Crash);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-6");

        Canned(app, "SHOP-6", $$"""{"files":[{{File1("Core/Nowhere.cs")}},{{File1("../outside.cs")}},{{File1(".stangent/tickets/SHOP-6/ticket.json")}},{{File1(quote: "the order total is computed wrongly")}},{{File1(line: "throw new InvalidOperationException(\"nope\");")}}]}""");
        var retry = TicketPoints.Check(app.Repo, ctx, "SHOP-6", null, "Pat Developer");
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[0]: `Core/Nowhere.cs` isn't a file in the repo"));
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[1]: `../outside.cs` isn't a file in the repo"));
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[2]: `.stangent/"));
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[3]: the quote") && e.Contains("isn't in the ticket word for word"));
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[4]: the line") && e.Contains("isn't in Core/OrderService.cs"));

        // The retry gets one right and one still wrong: the right one is kept, the wrong one listed apart, never dropped silently.
        Canned(app, "SHOP-6", $$"""{"files":[{{File1()}},{{File1("Core/Nowhere.cs")}}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-6", null, "Pat Developer").Status);
        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-6");
        Assert.Equal([(StartingPoint.File, "Core/OrderService.cs", "ai")], List(after));
        Assert.Equal("Core/Nowhere.cs", (string?)Assert.Single(after["ai"]!["rejected"]!.AsArray())!["path"]);
    }

    [Fact]
    public void When_nothing_passes_after_the_retry_the_finder_is_recorded_as_failed()
    {
        using var app = App();
        Points(app, "SHOP-7", Crash);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-7");
        Canned(app, "SHOP-7", $$"""{"files":[{{File1("Core/Nowhere.cs")}}]}""");
        Assert.Equal("retry", TicketPoints.Check(app.Repo, ctx, "SHOP-7", null, "Pat Developer").Status);
        Canned(app, "SHOP-7", $$"""{"files":[{{File1("Core/Nowhere.cs")}}]}""");
        Assert.Equal("failed", TicketPoints.Check(app.Repo, ctx, "SHOP-7", null, "Pat Developer").Status);
        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-7");
        Assert.Empty(after["points"]!.AsArray());
        Assert.Equal("failed", (string?)after["ai"]!["status"]);

        // No files and nothing outside is a valid answer, not a failure.
        Points(app, "SHOP-14", "Something about buying is broken today for many of our users.");
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-14");
        Canned(app, "SHOP-14", """{"files":[]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-14", null, "Pat Developer").Status);
        Assert.Equal("none", (string?)TicketPoints.LoadOrFail(app.Root, "SHOP-14")["ai"]!["status"]);
    }

    [Fact]
    public void The_developer_adds_and_removes_and_the_record_says_who()
    {
        using var app = CodeOnly();
        Points(app, "SHOP-7", "Fails in `OrderService.Place` on \"Orders\".");
        var p = Points(app, "SHOP-7", "", add: ["Billing.Charge", "page:/cart", "web/src/Button.tsx"], remove: [OrdersRoute]);
        var points = List(p);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Charge, "developer"), points);
        Assert.Contains((StartingPoint.Page, "page:/cart", "developer"), points);
        Assert.Contains((StartingPoint.File, "web/src/Button.tsx", "developer"), points);   // a file by its path (D101)
        Assert.DoesNotContain(points, x => x.Id == OrdersRoute);
        Assert.All(p["changes"]!.AsArray(), c => Assert.Equal("Pat Developer", (string?)c!["by"]));

        // A later run keeps the changes; a path outside the repo or in stangent's folder isn't a file to add.
        Assert.Equal(points, List(Points(app, "SHOP-7", "")));
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => Points(app, "SHOP-7", "", add: ["NoSuchThing"])).Code);
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => Points(app, "SHOP-7", "", add: ["../etc/passwd"])).Code);
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => Points(app, "SHOP-7", "", add: [".stangent/tickets/SHOP-7/points.json"])).Code);
    }

    [Fact]
    public void The_finder_may_mark_parts_outside_the_code_and_their_quotes_are_checked_too()
    {
        using var app = App();
        const string description = "Since Friday every request times out behind our nginx proxy, from all offices.";
        Points(app, "SHOP-12", description);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-12");
        Assert.Contains("**Outside the code.**", File.ReadAllText(Path.Combine(app.Root, ".stangent", "tickets", "SHOP-12", "points", "input.md")));

        Canned(app, "SHOP-12", """{"files":[],"outside":[{"area":"network","quote":"the proxy drops every request"}]}""");
        var retry = TicketPoints.Check(app.Repo, ctx, "SHOP-12", null, "Pat Developer");
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.StartsWith("outside[0]: the quote"));

        Canned(app, "SHOP-12", """{"files":[],"outside":[{"area":"network","quote":"every request times out behind our nginx proxy"}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-12", null, "Pat Developer").Status);
        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-12");
        Assert.Equal("found", (string?)after["ai"]!["status"]);        // only outside parts is still an answer: no new search
        Assert.Empty(after["points"]!.AsArray());
        var part = Assert.Single(after["outside"]!.AsArray())!;
        Assert.Equal(("network", "every request times out behind our nginx proxy", "ai"), ((string)part["area"]!, (string)part["quote"]!, (string)part["by"]!));
        Assert.True(OutsidePart.OnlyOutside(after));

        var bundle = TicketBundle.Build(app.Repo, ctx, "SHOP-12");
        var md = TicketBundle.Markdown(bundle);
        Assert.Contains("**None in the code:**", md);
        Assert.Contains("## Outside the code", md);
        Assert.DoesNotContain("No confident starting point", md);
    }

    [Fact]
    public void The_developer_adds_and_removes_outside_parts_by_area()
    {
        using var app = CodeOnly();
        Points(app, "SHOP-13", "Fails in `OrderService.Place` on \"Orders\".");
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "Pat Developer", ["server: disk full on prod", "Data: a corrupt order row"]);
        var p = TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "Sam Lead", removeOutside: ["server"]);
        var part = Assert.Single(p["outside"]!.AsArray())!;
        Assert.Equal(("data", "a corrupt order row", "Pat Developer"), ((string)part["area"]!, (string)part["quote"]!, (string)part["by"]!));
        Assert.Equal("Sam Lead", (string?)p["changes"]!.AsArray().Last()!["by"]);
        Assert.False(OutsidePart.OnlyOutside(p));                       // code found points too

        Assert.Empty(TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "Sam Lead", removeOutside: ["all"])["outside"]!.AsArray());

        // Remove-then-add in one call replaces an area's parts; a duplicate is kept once and counted once.
        var replaced = TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "Sam Lead", ["network: our proxy", "network: our proxy"], ["network"]);
        Assert.Equal("our proxy", (string?)Assert.Single(replaced["outside"]!.AsArray())!["quote"]);
        Assert.Equal(1, (int?)replaced["counts"]!["outside"]);
        Assert.Equal("usage", Assert.Throws<StangentException>(() => TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "x", ["firewall: blocked"])).Code);
        Assert.Equal("usage", Assert.Throws<StangentException>(() => TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "x", ["network:"])).Code);
        Assert.Equal("usage", Assert.Throws<StangentException>(() => TicketPoints.Update(app.Repo, ctx, "SHOP-13", [], [], "x", removeOutside: ["dns"])).Code);
    }

    [Fact]
    public void With_the_finder_off_or_ai_off_code_signals_are_the_starting_points()
    {
        using (var app = CodeOnly())
        {
            var p = Points(app, "SHOP-8", Crash);
            Assert.Equal("off", (string?)p["ai"]!["status"]);
            Assert.Contains((StartingPoint.Symbol, SpecApp.Place, "stack-frame"), List(p));
            Assert.Equal("current", TicketPoints.Prepare(app.Repo, TicketContext.Load(app.Repo), "SHOP-8").Status);
        }
        using (var app = new SpecApp("version: 1\nai:\n  enabled: false\ntickets:\n  replay: replay\n"))
        {
            var p = Points(app, "SHOP-9", Crash);
            Assert.Equal("disabled", (string?)p["ai"]!["status"]);
            Assert.Contains((StartingPoint.Symbol, SpecApp.Place, "stack-frame"), List(p));
        }
    }

    [Fact]
    public void The_finder_runs_read_only_inside_the_repo_and_never_in_stangents_folder()
    {
        using var app = App("  findBudget:\n    turns: 12\n    usd: 0.5\n");
        var limits = TicketPoints.DriverLimits(app.Repo);
        var root = Stangent.Tickets.Proof.ClaudeRules.Absolute(app.Root);
        Assert.Equal("Read,Grep,Glob", (string?)limits["tools"]);
        Assert.Equal([$"Read({root}/**)"], limits["allowedTools"]!.AsArray().Select(x => (string)x!));
        Assert.Contains($"Read({root}/.stangent/**)", limits["disallowedTools"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("dontAsk", (string?)limits["permissionMode"]);
        Assert.Equal(12, (int)limits["maxTurns"]!);
        Assert.Equal(0.5, (double)limits["maxBudgetUsd"]!);
    }

    [Fact]
    public void Same_ticket_and_graph_give_the_same_points_file()
    {
        using var app = CodeOnly();
        Points(app, "SHOP-10", "On /orders `OrderService.Place` fails.");
        var path = new TicketPaths(app.Root, "SHOP-10").PointsJson;
        var first = File.ReadAllText(path);
        Points(app, "SHOP-10", "");
        Assert.Equal(first, File.ReadAllText(path));
    }

    [Fact]
    public void A_changed_ticket_asks_for_a_new_search()
    {
        using var app = App();
        Points(app, "SHOP-5", Crash);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-5");
        Canned(app, "SHOP-5", $$"""{"files":[{{File1()}}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-5", null, "Pat Developer").Status);

        Directory.Delete(Path.Combine(app.Root, "replay", "SHOP-5"), recursive: true);   // the ticket is pulled again, edited
        var changed = Points(app, "SHOP-5", Crash + "\nIt happens every time.");
        Assert.Equal("needed", (string?)changed["ai"]!["status"]);
    }

    [Fact]
    public void Rejected_files_reach_the_bundle_and_a_ticket_with_them_is_not_only_outside_the_code()
    {
        using var app = App();
        const string description = "Since Friday every request times out behind our nginx proxy, from all offices.";
        Points(app, "SHOP-15", description);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-15");
        var bad = File1("Core/Nowhere.cs", quote: "every request times out behind our nginx proxy");
        var outside = """{"area":"network","quote":"every request times out behind our nginx proxy"}""";
        Canned(app, "SHOP-15", $$"""{"files":[{{bad}}],"outside":[{{outside}}]}""");
        Assert.Equal("retry", TicketPoints.Check(app.Repo, ctx, "SHOP-15", null, "Pat Developer").Status);
        Canned(app, "SHOP-15", $$"""{"files":[{{bad}}],"outside":[{{outside}}]}""");
        Assert.Equal("failed", TicketPoints.Check(app.Repo, ctx, "SHOP-15", null, "Pat Developer").Status);

        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-15");
        Assert.Equal("failed", (string?)after["ai"]!["status"]);       // it named code that failed: not "found"
        Assert.False(OutsidePart.OnlyOutside(after));                  // so proof isn't skipped as "only outside"
        var bundle = TicketBundle.Build(app.Repo, ctx, "SHOP-15");
        Assert.Equal("Core/Nowhere.cs", (string?)Assert.Single(bundle["rejected"]!.AsArray())!["path"]);
        Assert.False(OutsidePart.OnlyOutside(bundle));
        Assert.Contains("## Named, not shown in code", TicketBundle.Markdown(bundle));
    }

    [Fact]
    public void An_import_or_namespace_line_is_no_evidence_and_running_out_of_turns_is_said_on_the_retry()
    {
        using var app = App();
        app.Write("Core/Usings.cs", "using System.Collections.Generic;\nnamespace Shop.Core;\npublic class Usings { }\n");
        Points(app, "SHOP-16", Crash);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-16");
        Canned(app, "SHOP-16", $$"""{"files":[{{File1("Core/Usings.cs", line: "using System.Collections.Generic;")}}]}""");
        var retry = TicketPoints.Check(app.Repo, ctx, "SHOP-16", null, "Pat Developer");
        Assert.Contains(retry.Errors!, e => e.Contains("is an import or namespace line"));

        Points(app, "SHOP-17", Crash);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-17");                 // no output written: out of turns
        var empty = TicketPoints.Check(app.Repo, ctx, "SHOP-17", null, "Pat Developer");
        Assert.Equal("retry", empty.Status);
        Assert.StartsWith("You returned no answer", empty.Errors![0]);
    }

    [Fact]
    public void A_file_point_can_be_removed_after_its_file_is_gone()
    {
        using var app = CodeOnly();
        Points(app, "SHOP-18", "", add: ["web/src/Button.tsx"]);
        File.Delete(Path.Combine(app.Root, "web/src/Button.tsx"));
        var p = Points(app, "SHOP-18", "", remove: ["web/src/Button.tsx"]);
        Assert.DoesNotContain(List(p), x => x.Id == "web/src/Button.tsx");
    }

    [Fact]
    public void In_a_git_repo_only_tracked_files_count_lines_are_read_at_HEAD_and_the_answer_is_kept_but_stale_after_a_commit()
    {
        using var app = App();
        Git(app.Root, "init -q"); Git(app.Root, "add -A"); Git(app.Root, "-c user.name=t -c user.email=t@t commit -q -m one");
        app.Write("Core/Untracked.cs", "public class Untracked { } // Orders over 100 need approval\n");
        Points(app, "SHOP-19", Crash);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-19");
        // An edit after the commit: the finder searched HEAD, so the line is checked against HEAD, not the edited file.
        File.WriteAllText(Path.Combine(app.Root, "Core/OrderService.cs"), "// rewritten\n");
        Canned(app, "SHOP-19", $$"""{"files":[{{File1()}},{{File1("Core/Untracked.cs", line: "public class Untracked { }")}},{{File1("core/orderservice.cs")}}]}""");
        var retry = TicketPoints.Check(app.Repo, ctx, "SHOP-19", null, "Pat Developer");
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[1]: `Core/Untracked.cs` isn't a file in the repo"));   // untracked
        Assert.Contains(retry.Errors!, e => e.StartsWith("files[2]: `core/orderservice.cs` isn't a file in the repo")); // wrong case
        Assert.DoesNotContain(retry.Errors!, e => e.StartsWith("files[0]"));                                           // read at HEAD
        Canned(app, "SHOP-19", $$"""{"files":[{{File1()}}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-19", null, "Pat Developer").Status);
        Assert.Null(TicketPoints.LoadOrFail(app.Root, "SHOP-19")["ai"]!["stale"]);   // a local edit isn't a new commit
        Git(app.Root, "checkout -q -- Core/OrderService.cs");

        Git(app.Root, "add -A"); Git(app.Root, "-c user.name=t -c user.email=t@t commit -q -m two");
        var later = Points(app, "SHOP-19", Crash);
        Assert.Equal("found", (string?)later["ai"]!["status"]);        // kept: a new search would see the half-done fix
        Assert.True((bool)later["ai"]!["stale"]!);
        Assert.Equal([(StartingPoint.File, "Core/OrderService.cs", "ai")], List(later));
        var refound = TicketPoints.Update(app.Repo, ctx, "SHOP-19", [], [], "Pat Developer", refind: true);
        Assert.Equal("needed", (string?)refound["ai"]!["status"]);
    }

    static void Git(string root, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    /// <summary>A written reference page with its own title, as Phase 2 records it.</summary>
    static void WritePage(SpecApp app, string id, string title)
    {
        var set = app.Pages();
        var def = set.Find(id)!;
        var page = new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = def.Id, ["kind"] = def.Kind, ["entry"] = def.Entry, ["codeTitle"] = def.Title, ["title"] = title,
            ["group"] = def.Group, ["unknowns"] = new JsonArray(),
            ["sections"] = new JsonObject { ["shows"] = new JsonArray(), ["actions"] = new JsonArray(), ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray() },
            ["verification"] = new JsonObject { ["findings"] = new JsonArray(), ["dropped"] = 0 },
            ["generated"] = new JsonObject
            {
                ["commit"] = "c0ffee", ["bundleHash"] = Bundles.Build(set, def, null).InputHash,
                ["slice"] = new JsonObject { ["depth"] = 6, ["points"] = 1, ["chars"] = 1, ["cutOff"] = 0 },
                ["writer"] = new JsonObject { ["calls"] = 1 }, ["verifier"] = new JsonObject { ["calls"] = 1 }, ["checkRetries"] = 0,
            },
        };
        var paths = new SpecPaths(app.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PageJson(def))!);
        File.WriteAllText(paths.PageJson(def), CanonicalJson.Serialize(page));
    }

    static void Canned(SpecApp app, string key, string json) =>
        File.WriteAllText(Path.Combine(app.Root, ".stangent", "tickets", key, "points", "output.json"), json);
}
