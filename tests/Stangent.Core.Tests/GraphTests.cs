using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Graph.Detectors;
using Stangent.Graph.Impact;

namespace Stangent.Core.Tests;

/// <summary>A fresh copy of <c>tests/fixtures/shop</c> committed to its own git repo, with its graph built once.</summary>
public sealed class ShopGraph : IAsyncLifetime
{
    public string Root { get; } = ShopCopy.Create();
    public StangentRepo Repo { get; private set; } = null!;
    public CodeGraph Graph { get; private set; } = null!;
    public BuildStats Stats { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Repo = StangentRepo.Discover(Root);
        (Graph, Stats) = await GraphBuilder.BuildAsync(Repo, new BuildOptions(), TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}

static class ShopCopy
{
    public static string Create()
    {
        var source = Path.Combine(RepoRoot.Find(), "tests", "fixtures", "shop");
        var root = Directory.CreateTempSubdirectory("stangent-shop-").FullName;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (rel.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, rel))!);
            File.Copy(file, Path.Combine(root, rel));
        }
        Git(root, "init -q");
        Git(root, "add -A");
        Git(root, "-c user.name=test -c user.email=test@example.invalid commit -q -m fixture");
        return root;
    }

    public static void Git(string root, string args)
    {
        using var p = Process.Start(new ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {args}: {p.StandardError.ReadToEnd()}");
    }
}

public class GraphTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    const string PlaceOrder = "Shop.Core|M:Shop.Core.OrderService.PlaceOrder(System.String,System.Decimal)";
    const string IPlaceOrder = "Shop.Core|M:Shop.Core.IOrderService.PlaceOrder(System.String,System.Decimal)";

    IEnumerable<Edge> Edges(string kind) => shop.Graph.AllEdges.Where(e => e.Kind == kind);
    Node? NodeById(string id) => shop.Graph.AllNodes.FirstOrDefault(n => n.Id == id);

    [Fact]
    public void Every_project_loads_and_is_on_the_map()
    {
        Assert.Empty(shop.Stats.LoadErrors);
        Assert.Equal(4, shop.Stats.Projects);
        Assert.Equal(4, shop.Graph.AllNodes.Count(n => n.Kind == NodeKind.Project));
    }

    [Fact]
    public void The_compiler_part_has_methods_calls_and_dispatch_through_interfaces()
    {
        Assert.Equal(NodeKind.Method, NodeById(PlaceOrder)?.Kind);
        Assert.Contains(Edges(EdgeKind.Calls), e => e.Source.EndsWith("Endpoints.CreateOrder(Shop.Core.IOrderService,Microsoft.Extensions.Configuration.IConfiguration)", StringComparison.Ordinal)
            && e.Target == IPlaceOrder && e.Label == Label.Certain);
        Assert.Contains(Edges(EdgeKind.DispatchesTo), e => e.Source == IPlaceOrder && e.Target == PlaceOrder);
        Assert.True(NodeById("Shop.Tests|M:Shop.Tests.OrderTests.Placing_an_order_keeps_the_customer")?.IsTest);
    }

    [Fact]
    public void Calls_from_top_level_statements_belong_to_the_generated_entry_point()
    {
        // Shop.Web/Program.cs uses top-level statements; its calls must still be on the map.
        Assert.Contains(shop.Graph.AllNodes, n => n.Id == "Shop.Web|M:Program.<Main>$(System.String[])" && n.File == "Shop.Web/Program.cs");
        Assert.Contains(Edges(EdgeKind.Uses), e => e.Source == "Shop.Web|M:Program.<Main>$(System.String[])" && e.Target == "Shop.Core|T:Shop.Core.PaymentOptions");
    }

    [Fact]
    public void Routes_are_found_for_minimal_apis_attributes_and_conventional_routes()
    {
        var endpoints = shop.Graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint).Select(n => n.Name).ToList();
        Assert.Contains("POST /api/orders", endpoints);           // MapGroup("api/orders").MapPost("/")
        Assert.Contains("POST /api/legacy/place", endpoints);     // [Route] + [HttpPost("place")]
        Assert.Contains("ANY /checkout", endpoints);              // MapControllerRoute defaults → CheckoutController.Index
        Assert.Contains("GET /api/stock/{id:int}", endpoints);
        Assert.Contains(Edges(EdgeKind.RoutesTo), e => e.Source == "endpoint:Shop.Web|ANY /checkout"
            && e.Target == "Shop.Web|M:Shop.Web.Controllers.CheckoutController.Index" && e.Label == Label.Guessed);
    }

    [Fact]
    public void Di_events_tables_config_and_http_links_are_found()
    {
        Assert.Contains(Edges(EdgeKind.WiredTo), e => e.Source == "Shop.Core|T:Shop.Core.IOrderService" && e.Target == "Shop.Core|T:Shop.Core.OrderService");
        Assert.Contains(Edges(EdgeKind.TriggeredBy), e => e.Source == "Shop.Core|M:Shop.Core.SendReceiptOnOrderPlaced.HandleAsync(Shop.Core.OrderPlaced)"
            && e.Target == PlaceOrder);
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "table:Shop.Core|Order" && e.Target == "Shop.Core|T:Shop.Core.Order");
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "column:Shop.Core|Order.Total" && e.Target == "Shop.Core|P:Shop.Core.Order.Total");
        Assert.Contains(Edges(EdgeKind.ReadsConfig), e => e.Target == "config:Shop.Web|Shop:Region");
        Assert.Contains(Edges(EdgeKind.Binds), e => e.Source == "Shop.Core|P:Shop.Core.PaymentOptions.ApiKey" && e.Target == "config:Shop.Web|Payment:ApiKey");
        Assert.Equal("Shop.Web/appsettings.json", NodeById("config:Shop.Web|Payment:ApiKey")?.File);
        Assert.Contains(Edges(EdgeKind.CallsHttp), e => e.Source == "Shop.Web|M:Shop.Web.StockClient.GetStock(System.Int32)"
            && e.Target == "endpoint:Shop.Stock|GET /api/stock/{id:int}");
        Assert.DoesNotContain(Edges(EdgeKind.CallsHttp), e => e.Target == "endpoint:Shop.Stock|GET /api/stock/summary");
    }

    [Fact]
    public void Blind_spots_are_recorded_where_links_cannot_be_followed()
    {
        var spots = shop.Graph.AllBlindSpots.Select(b => b.Reason).ToList();
        Assert.Contains("reflection: Activator.CreateInstance", spots);
        Assert.Contains(spots, r => r.Contains("'Shop:NotInAppSettings' is in no appsettings file", StringComparison.Ordinal));
    }

    [Fact]
    public void Impact_of_a_method_stops_at_endpoints_handlers_and_tests_and_matches_its_contract()
    {
        var report = new ImpactAnalyzer(shop.Graph).Analyze(new ChangeSpec("symbol", "OrderService.PlaceOrder"), shop.Root);

        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v2.json", report.ToJsonString()));
        var groups = report["groups"]!.AsArray().ToDictionary(g => (string)g!["name"]!, g => g!);
        var endpoints = Names(groups["endpoints"]);
        Assert.Contains("POST /api/orders", endpoints);
        Assert.Contains("POST /api/legacy/place", endpoints);
        Assert.Contains("ANY /checkout", endpoints);
        Assert.DoesNotContain(endpoints, e => e.Contains("stock", StringComparison.Ordinal));
        Assert.Equal(2, (int)groups["endpoints"]["certain"]!);  // the minimal API and the attribute route
        Assert.Equal(["SendReceiptOnOrderPlaced.HandleAsync"], Names(groups["eventHandlers"]));
        Assert.Equal(["OrderTests.Placing_an_order_keeps_the_customer"], Names(groups["tests"]));

        var md = ImpactAnalyzer.Markdown(report);
        Assert.Contains("✅", md);
        Assert.Contains("🟡 `ANY /checkout`", md);
    }

    [Fact]
    public void Impact_lists_the_nearest_first_and_can_stop_at_a_depth()
    {
        var analyzer = new ImpactAnalyzer(shop.Graph);
        var report = analyzer.Analyze(new ChangeSpec("symbol", PlaceOrder), shop.Root);
        var code = report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!;
        var depths = code["items"]!.AsArray().Select(i => (int)i!["depth"]!).ToList();
        Assert.Equal(depths.Order(), depths);
        Assert.Equal(1, depths[0]);   // IOrderService.PlaceOrder dispatches straight to it
        var bands = code["bands"]!;
        Assert.Equal((int)code["total"]!, (int)bands["direct"]! + (int)bands["near"]! + (int)bands["far"]!);

        // POST /api/orders is 3 lines away (endpoint → CreateOrder → IOrderService → PlaceOrder).
        var direct = analyzer.Analyze(new ChangeSpec("symbol", PlaceOrder), shop.Root, maxDepth: 1);
        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v2.json", direct.ToJsonString()));
        Assert.Equal(0, (int)direct["groups"]![0]!["total"]!);
        Assert.Equal(1, (int)direct["maxDepth"]!);
        Assert.Contains("1 direct", ImpactAnalyzer.Markdown(report));
    }

    [Fact]
    public void Impact_follows_event_chains_but_not_the_dispatcher_behind_a_handler()
    {
        var analyzer = new ImpactAnalyzer(shop.Graph);
        var report = analyzer.Analyze(new ChangeSpec("symbol", PlaceOrder), shop.Root);
        // The handler is affected, but nothing that merely calls handlers in general is.
        Assert.Equal(1, (int)report["groups"]![1]!["total"]!);
        Assert.DoesNotContain("PluginLoader.Load", Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!));
    }

    [Fact]
    public void Impact_reaches_another_service_through_its_http_endpoint()
    {
        var report = new ImpactAnalyzer(shop.Graph).Analyze(new ChangeSpec("symbol", "endpoint:Shop.Stock|GET /api/stock/{id:int}"), shop.Root);
        var code = Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!);
        Assert.Contains("StockClient.GetStock", code);
    }

    [Fact]
    public void A_changed_config_key_affects_the_settings_class_bound_to_it()
    {
        var analyzer = new ImpactAnalyzer(shop.Graph);
        var line = NodeById("config:Shop.Web|Payment:ApiKey")!.Line;
        Assert.Contains(analyzer.AtLines("Shop.Web/appsettings.json", line, line), n => n.Id == "config:Shop.Web|Payment:ApiKey");

        var report = analyzer.Analyze(new ChangeSpec("symbol", "config:Shop.Web|Payment:ApiKey"), shop.Root);
        Assert.Contains("PaymentOptions.ApiKey", Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!));
    }

    [Fact]
    public void A_git_diff_starts_from_the_methods_whose_lines_changed()
    {
        var file = Path.Combine(shop.Root, "Shop.Core", "Orders.cs");
        var original = File.ReadAllText(file);
        try
        {
            File.WriteAllText(file, original.Replace("Publish(new OrderPlaced(order.Id));", "Publish(new OrderPlaced(order.Id)); // changed"));
            var starts = new ImpactAnalyzer(shop.Graph).StartNodes(new ChangeSpec("diff", "HEAD"), shop.Root);
            Assert.Equal([PlaceOrder], starts.Select(s => s.Id));
        }
        finally
        {
            File.WriteAllText(file, original);
        }
    }

    static List<string> Names(JsonNode group) => [.. group["items"]!.AsArray().Select(i => (string)i!["name"]!)];
}

public class GraphBuildTests
{
    [Fact]
    public async Task Builds_are_deterministic_and_an_incremental_build_equals_a_full_one()
    {
        var root = ShopCopy.Create();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var repo = StangentRepo.Discover(root);
            var (first, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(), ct);
            var (second, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: true), ct);
            Assert.Equal(Export(first, root, "a"), Export(second, root, "b"));

            File.AppendAllText(Path.Combine(root, "Shop.Stock", "Extra.cs"), "namespace Shop.Stock;\npublic static class Extra { public static int Twice(int x) => x * 2; }\n");
            var (incremental, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
            Assert.Equal(1, stats.Extracted);   // only Shop.Stock: nothing references it
            Assert.Equal(3, stats.Reused);
            var (full, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: true), ct);
            Assert.Equal(Export(full, root, "c"), Export(incremental, root, "d"));
            Assert.Contains(incremental.AllNodes, n => n.Name == "Extra.Twice");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Changing_detector_settings_re_extracts_every_project()
    {
        var root = ShopCopy.Create();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(), ct);
            var config = Path.Combine(root, StangentRepo.ConfigFile);
            File.WriteAllText(config, File.ReadAllText(config).Replace("entityBaseTypes: [Shop.Core.Entity]", "entityBaseTypes: []"));

            // Reload: the config is read when the repo is discovered.
            var (graph, stats) = await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(Restore: false), ct);
            Assert.Equal(4, stats.Extracted);
            Assert.DoesNotContain(graph.AllNodes, n => n.Id == "table:Shop.Core|Order");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string Export(CodeGraph graph, string root, string name)
    {
        var path = Path.Combine(root, $"graph-{name}.jsonl.gz");
        GraphStore.Export(graph, path);
        var hash = CanonicalJson.Hash(Convert.ToBase64String(File.ReadAllBytes(path)));
        Assert.Equal(GraphStore.CanonicalLines(graph).Count(), GraphStore.CanonicalLines(GraphStore.Import(path)).Count());
        return hash;
    }
}

public class GraphUnitTests
{
    [Theory]
    [InlineData("/api/stock/{id:int}", "/api/stock/{*}", 9)]
    [InlineData("/api/stock/summary", "/api/stock/{*}", 7)]
    [InlineData("/api/stock/{id:int}", "/api/stock/7", 8)]
    [InlineData("/api/stock/summary", "/api/stock/other", -1)]
    [InlineData("/items/{id?}", "/items", 3)]
    [InlineData("/files/{*path}", "/files/a/b", 5)]
    public void Http_paths_score_by_how_closely_they_fit(string template, string path, int score) =>
        Assert.Equal(score, HttpServicesDetector.Score(template, path));

    [Fact]
    public void Appsettings_keys_are_flattened_with_their_lines()
    {
        var json = "{\n  // comment\n  \"A\": {\n    \"B\": 1,\n    \"C\": [\"x\", { \"D\": true }]\n  },\n  \"E\": null\n}\n"u8.ToArray();
        Assert.Equal([("A:B", 4), ("A:C:0", 5), ("A:C:1:D", 5), ("E", 7)], ConfigKeysDetector.Flatten(json));
    }

    [Fact]
    public void Diff_hunks_become_line_ranges_on_the_new_side()
    {
        var diff = "diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -3,0 +4,2 @@\n+a\n+b\n@@ -10 +12 @@\n-c\n+d\n@@ -20,3 +23,0 @@\n-e\n+++ /dev/null\n@@ -1 +0,0 @@\n";
        Assert.Equal([new ChangedLines("x.cs", 4, 5), new ChangedLines("x.cs", 12, 12), new ChangedLines("x.cs", 23, 23)], DiffMapper.Parse(diff));
    }

    [Fact]
    public void Doc_ids_give_the_containing_and_constructed_types()
    {
        Assert.Equal("A|T:Ns.T", DocIds.ContainingType("A|M:Ns.T.Run(System.Int32)"));
        Assert.Equal("A|T:Ns.Outer.Inner", DocIds.ConstructedType("A|M:Ns.Outer.Inner.#ctor"));
        Assert.Null(DocIds.ConstructedType("A|M:Ns.T.Run"));
        Assert.Null(DocIds.ContainingType("A|T:Ns.T"));
    }
}
