using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>Rebuild layer 3 (D170–D184): flows laid out from the Shop fixture's graph by code.</summary>
public class FlowsTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    const string Core = "Shop.Core|M:Shop.Core.";

    [Fact]
    public void Every_call_the_compiler_resolves_records_the_line_it_is_written_on()
    {
        // D184: a call-site fact per call, on the caller, at the line the call starts.
        var execute = $"{Core}RefreshStockService.Execute(Shop.Core.RefreshStockCommand)";
        var sites = shop.Graph.AllFacts.Where(f => f.Kind == "call-site" && f.Owner == execute && f["target"] == $"{Core}RefreshStockService.Recount(System.Int32)")
            .Select(f => f.Line).Order().ToList();
        Assert.Equal([42, 45, 46], sites);
        // TypeScript too, a JSX tag as a render.
        Assert.Contains(shop.Graph.AllFacts, f => f.Kind == "call-site" && f["kind"] == "renders" && f.File!.EndsWith("App.tsx", StringComparison.Ordinal));
    }

    [Fact]
    public void A_job_is_a_flow_whose_steps_quote_each_call_line_with_its_conditions()
    {
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        var job = set.Flows.Single(f => f.Kind == "job" && f.Start.Point.Contains("RefreshStockService.Execute", StringComparison.Ordinal));
        Assert.Equal("system", job.Actor.Who);
        var recounts = job.Steps.Where(s => s.Point is { } p && p.Contains("Recount", StringComparison.Ordinal)).ToList();
        Assert.Equal([42, 45, 46], recounts.Select(s => s.At.Line).Order().ToList());
        Assert.All(recounts, s => Assert.Null(s.From)); // the job's own calls hang from its start
        // Each step carries the conditions around its call, innermost first (D174).
        Assert.Equal(["case", "loop"], recounts.Single(s => s.At.Line == 42).Conditions.Select(c => c.Kind).ToList());
        Assert.Equal("catch", recounts.Single(s => s.At.Line == 46).Conditions[0].Kind);
        // The same point reached again in one flow isn't walked twice.
        Assert.Equal(2, recounts.Count(s => s.SameAs is not null));
    }

    [Fact]
    public void Two_builds_of_one_graph_write_the_same_bytes_and_the_check_passes_on_what_was_written()
    {
        var a = FlowFiles.Render(FlowBuilder.Build(shop.Graph, shop.Root));
        var b = FlowFiles.Render(FlowBuilder.Build(shop.Graph, shop.Root));
        Assert.Equal(a, b);
        FlowFiles.Write(shop.Root, FlowBuilder.Build(shop.Graph, shop.Root));
        var check = FlowFiles.Check(shop.Root, shop.Graph, new FlowSettings());
        Assert.True((bool)check["ok"]!, check.ToJsonString());
        Assert.Contains($"{FlowFiles.Folder}/index.json", a.Keys);
    }

    [Fact]
    public void Every_start_is_a_flow_or_a_stated_reason_and_every_guess_says_why()
    {
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        Assert.All(set.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.All(set.Flows.SelectMany(f => f.Guesses), g => Assert.False(string.IsNullOrWhiteSpace(g.Reason)));
        Assert.All(set.Flows.SelectMany(f => f.Unknowns), u => Assert.False(string.IsNullOrWhiteSpace(u.Why)));
        // A click whose handler does no server work is screen behaviour, said so (D171).
        Assert.Contains(set.Skipped, s => s.Kind == "click" && s.Reason.StartsWith("screen behaviour only", StringComparison.Ordinal)
            || s.Kind == "click" && s.Reason.Contains("handler", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_hub_and_depth_stop_a_flow_continues_into_is_a_shared_flow_walked_once()
    {
        // D189: low limits so the fixture has hubs and depth stops.
        var set = FlowBuilder.Build(shop.Graph, shop.Root, new FlowSettings(HubCallers: 2, Depth: 1));
        var shared = set.Flows.Where(f => f.Kind == "shared").ToList();
        Assert.NotEmpty(shared);
        Assert.Equal(shared.Count, shared.Select(f => f.Start.Point).Distinct(StringComparer.Ordinal).Count());
        var ids = shared.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var continues = set.Flows.SelectMany(f => f.Links).Where(l => l.Kind == "continues").ToList();
        Assert.NotEmpty(continues);
        // Every continues link, the shared flows' own included, names the shared flow that walks on from it.
        Assert.All(continues, l => Assert.Contains(l.ToFlow ?? "(none)", ids));
        // D196: a hub that reads or saves a table ends `database` there and still continues into its shared walk.
        var reading = set.Flows.SelectMany(f => f.Steps.Where(st => st.Kind == "database" && st.Point is not null).Select(st => (Flow: f, Step: st))).ToList();
        Assert.All(reading, x => Assert.Contains(x.Flow.Links, l => l.Kind == "continues" && l.Step == x.Step.N && l.ToFlow is not null));
        Assert.All(shared, f => Assert.Equal("the flow that continues here", f.Actor.Who));
        Assert.Equal(FlowFiles.Render(set), FlowFiles.Render(FlowBuilder.Build(shop.Graph, shop.Root, new FlowSettings(HubCallers: 2, Depth: 1))));
    }

    [Fact]
    public void A_field_or_property_holding_a_function_is_code_and_one_holding_a_value_is_not()
    {
        // D189: `this.seriesId()` reads a signal, no step; `getPageUrl = (n) => …` is a function, called like a method.
        var root = Directory.CreateTempSubdirectory("stangent-props-").FullName;
        try
        {
            File.WriteAllLines(Path.Combine(root, "c.ts"), [
                "export class C {",
                "  seriesId = input.required<number>();",
                "  getPageUrl = (pageNum: number, chapterId: number = this.chapterId()) => {",
                "    return `${pageNum}`;",
                "  }",
                "  load = async id => this.http.get(id);",
                "  total = computed(() => this.seriesId() * 2);",
                "}",
            ]);
            var g = new FlowGraph(shop.Graph, root);
            Node Prop(int line, int end = 0) => new("ts|c.ts#C.x", NodeKind.Property, "C.x", null, "c.ts", line, end == 0 ? line : end);
            Assert.False(g.HoldsFunction(Prop(2)));
            Assert.True(g.HoldsFunction(Prop(3, 5)));
            Assert.True(g.HoldsFunction(Prop(6)));
            Assert.False(g.HoldsFunction(Prop(7)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public class HelperExitTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    [Fact]
    public void An_apps_own_helper_that_ends_the_request_is_an_exit_where_it_is_returned()
    {
        var exits = shop.Graph.AllFacts.Where(f => f.Kind == "exit" && f.Owner.Contains("PageHelpers.Show", StringComparison.Ordinal))
            .Select(f => $"{f["kind"]} {f["status"]} {f["result"]}").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Forbid  Refuse", "StatusCode 404 Gone"], exits);
    }

    [Fact]
    public void A_filter_setting_its_contexts_result_to_a_refusal_is_an_exit()
    {
        var exits = shop.Graph.AllFacts.Where(f => f.Kind == "exit" && f.Owner.Contains("OpenStoreFilter.OnAuthorization", StringComparison.Ordinal)).ToList();
        var exit = Assert.Single(exits);
        Assert.Equal("Challenge", exit["kind"]);
        Assert.Contains("IsAuthenticated", exit["conditions"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_into_a_package_is_a_call_site_with_no_target_and_not_one_into_system()
    {
        var packages = shop.Graph.AllFacts.Where(f => f.Kind == "call-site" && f["kind"] == "package").ToList();
        Assert.All(packages, f => Assert.Equal("", f["target"]));
        Assert.DoesNotContain(packages, f => f["package"].StartsWith("System", StringComparison.Ordinal));
        Assert.Contains(packages, f => f["package"].StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void A_redirect_links_to_the_flow_of_the_screen_it_opens()
    {
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        var welcome = set.Flows.Single(f => f.Kind == "screen-opens" && f.Start.Point.EndsWith("AccountController.Welcome", StringComparison.Ordinal));
        var links = set.Flows.SelectMany(f => f.Links).Where(l => l.Kind == "screen" && l.At.Code.Contains("RedirectToAction(nameof(Welcome))", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, l => { Assert.Equal(welcome.Id, l.ToFlow); Assert.False(l.Guessed); Assert.Equal("AccountController.Welcome", l.To); });
        // Layer 2 keeps where it goes: the action and, by default, the controller it's written in.
        Assert.Contains(shop.Graph.AllFacts, f => f.Kind == "exit" && f["text"] == "RedirectToAction(nameof(Welcome))" && f["action"] == "Welcome" && f["controller"] == "Account" && f["area"] == "");
    }

    [Fact]
    public void A_navigation_written_as_a_constant_path_fits_the_pages_whose_route_matches()
    {
        var g = new FlowGraph(shop.Graph, shop.Root);
        const string file = "Shop.Web/ClientApp/src/app/order-form.component.ts";
        var orders = g.PagesAt("'/orders'", file);
        Assert.Single(orders);
        Assert.EndsWith("|/orders", orders[0], StringComparison.Ordinal);
        Assert.Equal(g.PagesAt("'/orders/7'", file), g.PagesAt("['/orders', order.id]", file));
        Assert.Single(g.PagesAt("`/orders/${id}`", file));
        Assert.EndsWith("|/orders/:id", g.PagesAt("`/orders/${id}`", file)[0], StringComparison.Ordinal);
        Assert.Empty(g.PagesAt("returnUrl", file));          // made at runtime
        Assert.Empty(g.PagesAt("'orders'", file));           // relative to where it is
        Assert.Empty(g.PagesAt("'/nowhere'", file));
        // A path joined with `+`; a router path read from the app's root (D204).
        Assert.EndsWith("|/orders/:id", g.PagesAt("'/orders/' + order.id", file).Single(), StringComparison.Ordinal);
        Assert.EndsWith("|/orders/:id", g.PagesAt("['orders', order.id]", file, fromRoot: true).Single(), StringComparison.Ordinal);
        Assert.Empty(g.PagesAt("['orders', order.id]", file));
        Assert.Empty(g.PagesAt("this.paths(order)", file, fromRoot: true));
                // Another frontend's pages never fit: the React app's /orders/new isn't the Angular app's.
        Assert.Single(g.PagesAt("'/orders/new'", "Shop.Web/react-app/src/App.tsx"));
        Assert.EndsWith("|/orders/:id", g.PagesAt("'/orders/new'", file).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_through_a_value_another_function_returned_is_a_guess_that_says_whose_result_it_is()
    {
        // D210: `const load = priceLoader('eur')` then `load(n)`: what runs is whatever priceLoader handed back, so priceLoader
        // stands for it, 🟡 with its reason, never a certain call to priceLoader.
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        var steps = set.Flows.SelectMany(f => f.Steps).Where(s => s.At.File.EndsWith("PriceTag.tsx", StringComparison.Ordinal) && s.At.Line == 11 && s.What.Contains("priceLoader", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, s =>
        {
            Assert.True(s.Guessed);
            Assert.Equal("calls a value taken from priceLoader's result; what it runs isn't known, so priceLoader stands for it", s.GuessReason);
        });
    }

    [Fact]
    public void A_call_inside_a_timer_runs_later_a_guess_that_says_so()
    {
        // D212: `setInterval(() => refreshPrices(), 60000)` runs refreshPrices after its delay, not when the click runs.
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        var steps = set.Flows.SelectMany(f => f.Steps).Where(s => s.At.File.EndsWith("PriceTag.tsx", StringComparison.Ordinal) && s.At.Line == 12 && s.What.Contains("refreshPrices", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, s =>
        {
            Assert.True(s.Guessed);
            Assert.Equal("runs later: a function given to setInterval(…), after its delay, not when this code runs", s.GuessReason);
        });
    }

    [Fact]
    public void An_element_inside_a_component_that_draws_nothing_until_a_prop_is_set_is_a_guess_that_says_so()
    {
        // D213: Drawer returns null while `!isOpen`, so PriceTag inside it is shown only when it's open.
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        var steps = set.Flows.SelectMany(f => f.Steps).Where(s => s.At.File.EndsWith("PriceModal.tsx", StringComparison.Ordinal) && s.What == "shows PriceTag").ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, s =>
        {
            Assert.True(s.Guessed);
            Assert.Equal("shown only when Drawer draws it: Drawer draws nothing while `!isOpen` (here isOpen=open)", s.GuessReason);
        });
    }

    [Fact]
    public void A_call_in_a_function_given_to_a_cache_runs_only_when_nothing_is_stored_and_the_cache_call_itself_has_no_such_condition()
    {
        // D215: PriceCache.cs — Load inside `cache.Get(…, () => …)` and `seen.GetOrAdd(…, id => …)` runs only on a miss; the
        // call to cache.Get on the same line doesn't borrow that condition, and Load on its own line has none.
        var set = FlowBuilder.Build(shop.Graph, shop.Root);
        var job = set.Flows.Single(f => f.Kind == "job" && f.Start.Point.Contains("PriceCacheTask.ExecuteAsync", StringComparison.Ordinal));
        string Conds(int line, string what) => string.Join(" ← ", job.Steps.Single(s => s.At.Line == line && s.What.Contains(what, StringComparison.Ordinal)).Conditions.Select(c => $"{c.Kind} {c.Code}"));
        Assert.Equal("cache-miss cache.Get", Conds(14, "PriceLoader.Load"));
        Assert.Equal("", Conds(14, "IPriceCache.Get"));
        Assert.Equal("cache-miss seen.GetOrAdd", Conds(15, "PriceLoader.Load"));
        Assert.Equal("", Conds(16, "PriceLoader.Load"));
        // D216: `loader?.Reload(PriceLoader.Load(price))` — both run only when loader is set.
        Assert.Equal("set loader", Conds(17, "PriceLoader.Reload"));
        Assert.Equal("set loader", Conds(17, "PriceLoader.Load"));
        Assert.Contains("only if `loader` is set", FlowFiles.Markdown(job, set), StringComparison.Ordinal);
        Assert.Contains("only if `cache.Get` finds nothing stored for the key", FlowFiles.Markdown(job, set), StringComparison.Ordinal);
    }
}
