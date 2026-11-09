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
}
