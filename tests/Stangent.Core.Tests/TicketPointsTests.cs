using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>Phase 3 step 3: starting points (D70) on the shop app, with canned point-picker outputs (no LLM).</summary>
public class TicketPointsTests
{
    const string OrdersRoute = "page:/orders";

    static SpecApp App(string extra = "") => new($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");

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
    public void A_stack_frame_finds_its_method_by_file_and_line_or_by_name()
    {
        using var app = App();
        var p = Points(app, "SHOP-1", """
            It crashed:
            System.ArgumentException: Orders over 100 need approval
               at Shop.Core.OrderService.Place(Decimal total) in /home/build/src/Core/OrderService.cs:line 5
               at Shop.Api.OrdersController.<Create>d__2.MoveNext()
            """);
        var points = List(p);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Place, "stack-frame"), points);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Create, "stack-frame"), points);
        Assert.Equal("not-needed", (string?)p["ai"]!["status"]);
    }

    [Fact]
    public void A_stack_trace_in_a_text_attachment_counts_too()
    {
        using var app = App();
        var trace = "at Shop.Core.ReceiptHandler.Handle(OrderPlaced e) in C:\\work\\shop\\Core\\OrderService.cs:line 12\n"u8.ToArray();
        var p = Points(app, "SHOP-2", "See the attached log.", files: [new("1", "error.log", "text/plain", trace)]);
        var point = Assert.Single(p["points"]!.AsArray());
        Assert.Equal(SpecApp.Receipt, (string?)point!["id"]);
        Assert.Equal("attachment error.log", (string?)point["where"]);
    }

    [Fact]
    public void URLs_find_route_pages_and_endpoint_pages_and_file_paths_are_ignored()
    {
        using var app = App();
        var p = Points(app, "SHOP-3", "On http://localhost:5000/orders the button sends POST /api/orders and gets a 500. Log: /var/log/shop/app.log");
        var points = List(p);
        Assert.Contains((StartingPoint.Page, OrdersRoute, "url"), points);
        Assert.Contains((StartingPoint.Page, SpecApp.Post, "url"), points);
        Assert.Equal(2, points.Count);
    }

    [Fact]
    public void Quoted_screen_names_and_symbol_names_find_pages_and_symbols()
    {
        using var app = App();
        var p = Points(app, "SHOP-4", "The \"Orders\" screen fails in `OrderService.Place` when called from ReceiptHandler.Handle.");
        var points = List(p);
        Assert.Contains((StartingPoint.Page, OrdersRoute, "title"), points);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Place, "symbol"), points);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Receipt, "symbol"), points);
    }

    [Fact]
    public void With_no_code_signal_the_picker_runs_and_its_quote_is_checked_word_for_word()
    {
        using var app = App();
        const string description = "When I try to buy something the total is wrong after I press the big button.";
        var p = Points(app, "SHOP-5", description);
        Assert.Empty(p["points"]!.AsArray());
        Assert.Equal("needed", (string?)p["ai"]!["status"]);

        var ctx = TicketContext.Load(app.Repo);
        var step = TicketPoints.Prepare(app.Repo, ctx, "SHOP-5");
        Assert.Equal(StepSpec.PointPicker, step.Agent);
        var input = File.ReadAllText(Path.Combine(app.Root, ".stangent", "tickets", "SHOP-5", "points", "input.md"));
        var orders = ctx.Set.Pages.Select(x => x.Id).ToList().IndexOf(OrdersRoute) + 1;
        Assert.Contains("# The pages code ranked highest for this ticket", input);
        Assert.Matches($@"(?m)^{orders}\. .+ \(screen\)$", input);   // every page, one numbered line (D87)
        Assert.Equal(ctx.Set.Pages.Count, System.Text.RegularExpressions.Regex.Matches(input[input.IndexOf("# Every page", StringComparison.Ordinal)..], @"(?m)^\d+\. ").Count);

        Canned(app, "SHOP-5", $$"""{"pages":[{"page":"{{orders}}","quote":"the total is wrong after I press the big button"}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-5", null, "Pat Developer").Status);
        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-5");
        Assert.Equal([(StartingPoint.Page, OrdersRoute, "ai")], List(after));

        // Nothing changed: no AI call.
        var again = Points(app, "SHOP-5", description);
        Assert.Equal("picked", (string?)again["ai"]!["status"]);
        Assert.Equal("current", TicketPoints.Prepare(app.Repo, ctx, "SHOP-5").Status);
    }

    [Fact]
    public void The_picker_sees_the_pages_ranked_for_the_ticket_in_detail_with_the_phrase_they_share()
    {
        using var app = App();
        WritePage(app, OrdersRoute, "Order checkout screen");
        Points(app, "SHOP-11", "Somehow the order checkout screen shows nothing for me today.");
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-11");
        var input = File.ReadAllText(Path.Combine(app.Root, ".stangent", "tickets", "SHOP-11", "points", "input.md"));
        var orders = ctx.Set.Pages.Select(x => x.Id).ToList().IndexOf(OrdersRoute) + 1;
        var detail = input[input.IndexOf("# The pages code ranked highest", StringComparison.Ordinal)..input.IndexOf("# Every page", StringComparison.Ordinal)];
        Assert.StartsWith($"# The pages code ranked highest for this ticket\n\n## {orders}. Order checkout screen (screen)", detail);
        Assert.Contains("- shares \"order checkout\" (ticket description), in its title: Order checkout screen\n", detail);
        Assert.Contains("- route: /orders\n", detail);
        Assert.DoesNotContain("- name: /orders\n", detail);   // the same text is said once
        Assert.Contains("- from the code: ", detail);
    }

    [Fact]
    public void Page_numbers_resolve_with_a_hash_or_spaces_and_out_of_range_ones_are_rejected()
    {
        using var app = App();
        const string description = "When I try to buy something the total is wrong after I press the big button.";
        Points(app, "SHOP-12", description);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-12");
        var orders = ctx.Set.Pages.Select(x => x.Id).ToList().IndexOf(OrdersRoute) + 1;
        const string quote = "the total is wrong after I press the big button";

        Canned(app, "SHOP-12", $$"""{"pages":[{"page":"0","quote":"{{quote}}"},{"page":"-1","quote":"{{quote}}"}]}""");
        var retry = TicketPoints.Check(app.Repo, ctx, "SHOP-12", null, "Pat Developer");
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("`0` isn't a page"));
        Assert.Contains(retry.Errors!, e => e.Contains("`-1` isn't a page"));

        // "#N" and " N " are the same page, and the same page by id: recorded once, by id.
        Canned(app, "SHOP-12", $$"""{"pages":[{"page":"#{{orders}}","quote":"{{quote}}"},{"page":" {{orders}} ","quote":"{{quote}}"},{"page":"{{OrdersRoute}}","quote":"{{quote}}"}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-12", null, "Pat Developer").Status);
        Assert.Equal([(StartingPoint.Page, OrdersRoute, "ai")], List(TicketPoints.LoadOrFail(app.Root, "SHOP-12")));
    }

    [Fact]
    public void The_picker_input_is_the_same_on_a_second_run_and_says_when_no_page_shares_a_word()
    {
        using var app = App();
        var text = new TicketText([("summary", "Zzyzx"), ("description", "Qwerty plover.")], []);
        var first = TicketPoints.PickerInput(app.Repo, TicketContext.Load(app.Repo), text);
        Assert.Equal(first, TicketPoints.PickerInput(app.Repo, TicketContext.Load(app.Repo), text));
        Assert.Contains("# The pages code ranked highest for this ticket\n\nNo page shares a word with the ticket.\n", first);
    }

    [Fact]
    public void Writing_a_picked_page_later_does_not_ask_for_a_new_pick()
    {
        using var app = App();
        const string description = "When I try to buy something the total is wrong after I press the big button.";
        Points(app, "SHOP-5", description);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-5");
        Canned(app, "SHOP-5", $$"""{"pages":[{"page":"{{OrdersRoute}}","quote":"the total is wrong after I press the big button"}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-5", null, "Pat Developer").Status);

        WritePage(app, OrdersRoute, "Order checkout screen");   // the page gets its written title afterwards
        var after = Points(app, "SHOP-5", description);
        Assert.Equal("picked", (string?)after["ai"]!["status"]);
        Assert.Equal("current", TicketPoints.Prepare(app.Repo, TicketContext.Load(app.Repo), "SHOP-5").Status);
    }

    [Fact]
    public void A_made_up_quote_or_a_page_that_does_not_exist_is_rejected_then_recorded_as_failed()
    {
        using var app = App();
        Points(app, "SHOP-6", "Something about buying is broken today for many of our users.");
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-6");

        Canned(app, "SHOP-6", $$"""{"pages":[{"page":"{{OrdersRoute}}","quote":"the checkout total is calculated wrongly"},{"page":"page:/nowhere","quote":"Something about buying is broken today"},{"page":"9999","quote":"Something about buying is broken today"}]}""");
        var retry = TicketPoints.Check(app.Repo, ctx, "SHOP-6", null, "Pat Developer");
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("isn't in the ticket word for word"));
        Assert.Contains(retry.Errors!, e => e.Contains("`page:/nowhere` isn't a page"));
        Assert.Contains(retry.Errors!, e => e.Contains("`9999` isn't a page"));

        Canned(app, "SHOP-6", $$"""{"pages":[{"page":"{{OrdersRoute}}","quote":"the checkout total is calculated wrongly"}]}""");
        Assert.Equal("failed", TicketPoints.Check(app.Repo, ctx, "SHOP-6", null, "Pat Developer").Status);
        var after = TicketPoints.LoadOrFail(app.Root, "SHOP-6");
        Assert.Empty(after["points"]!.AsArray());
        Assert.Equal("failed", (string?)after["ai"]!["status"]);
    }

    [Fact]
    public void The_developer_adds_and_removes_and_the_record_says_who()
    {
        using var app = App();
        Points(app, "SHOP-7", "Fails in `OrderService.Place` on \"Orders\".");
        var p = Points(app, "SHOP-7", "", add: ["Billing.Charge", "page:/cart"], remove: [OrdersRoute]);
        var points = List(p);
        Assert.Contains((StartingPoint.Symbol, SpecApp.Charge, "developer"), points);
        Assert.Contains((StartingPoint.Page, "page:/cart", "developer"), points);
        Assert.DoesNotContain(points, x => x.Id == OrdersRoute);
        Assert.All(p["changes"]!.AsArray(), c => Assert.Equal("Pat Developer", (string?)c!["by"]));

        // A later run keeps the changes.
        Assert.Equal(points, List(Points(app, "SHOP-7", "")));
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => Points(app, "SHOP-7", "", add: ["NoSuchThing"])).Code);
    }

    [Fact]
    public void The_picker_is_skipped_when_ai_points_or_ai_are_off()
    {
        using (var app = App("  aiPoints: false\n"))
            Assert.Equal("off", (string?)Points(app, "SHOP-8", "Nothing to match here at all.")["ai"]!["status"]);
        using (var app = new SpecApp("version: 1\nai:\n  enabled: false\ntickets:\n  replay: replay\n"))
            Assert.Equal("disabled", (string?)Points(app, "SHOP-9", "Nothing to match here at all.")["ai"]!["status"]);
    }

    [Fact]
    public void Same_ticket_and_graph_give_the_same_points_file()
    {
        using var app = App();
        Points(app, "SHOP-10", "On /orders `OrderService.Place` fails.");
        var path = new TicketPaths(app.Root, "SHOP-10").PointsJson;
        var first = File.ReadAllText(path);
        Points(app, "SHOP-10", "");
        Assert.Equal(first, File.ReadAllText(path));
    }

    [Fact]
    public void A_changed_ticket_still_asks_for_a_new_pick()
    {
        using var app = App();
        const string description = "When I try to buy something the total is wrong after I press the big button.";
        Points(app, "SHOP-5", description);
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, "SHOP-5");
        Canned(app, "SHOP-5", $$"""{"pages":[{"page":"{{OrdersRoute}}","quote":"the total is wrong after I press the big button"}]}""");
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, "SHOP-5", null, "Pat Developer").Status);

        Directory.Delete(Path.Combine(app.Root, "replay", "SHOP-5"), recursive: true);   // the ticket is pulled again, edited
        var changed = Points(app, "SHOP-5", description + " It happens every time.");
        Assert.Equal("needed", (string?)changed["ai"]!["status"]);
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
