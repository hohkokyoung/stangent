using Stangent.Core;
using Stangent.Graph;
using Xunit;

namespace Stangent.Core.Tests;

/// <summary>A fresh copy of <c>tests/fixtures/flask-shop</c> committed to its own git repo, with its graph built once.</summary>
public sealed class FlaskShop : IAsyncLifetime
{
    public string Root { get; } = Create();
    public StangentRepo Repo { get; private set; } = null!;
    public CodeGraph Graph { get; private set; } = null!;
    public BuildStats Stats { get; private set; } = null!;

    public static string Create(string? config = null)
    {
        var source = Path.Combine(RepoRoot.Find(), "tests", "fixtures", "flask-shop");
        var root = Directory.CreateTempSubdirectory("stangent-flask-").FullName;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        // What must be skipped: a virtualenv under any name, and a bytecode cache.
        Write(root, "backend/myenv/pyvenv.cfg", "home = /usr/bin\n");
        Write(root, "backend/myenv/lib/skipped_env.py", "def skipped_env():\n    pass\n");
        Write(root, "backend/shop/__pycache__/skipped_cache.py", "def skipped_cache():\n    pass\n");
        if (config is not null) File.AppendAllText(Path.Combine(root, "stangent.yaml"), config);
        ShopCopy.Git(root, "init -q");
        ShopCopy.Git(root, "add -A");
        ShopCopy.Git(root, "-c user.name=test -c user.email=test@example.invalid commit -q -m fixture");
        return root;
    }

    static void Write(string root, string rel, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, rel))!);
        File.WriteAllText(Path.Combine(root, rel), text);
    }

    public async ValueTask InitializeAsync()
    {
        Repo = StangentRepo.Discover(Root);
        (Graph, Stats) = await GraphBuilder.BuildAsync(Repo, new BuildOptions(Restore: false), TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}

public class PythonGraphTests(FlaskShop shop) : IClassFixture<FlaskShop>
{
    CodeGraph G => shop.Graph;

    static string Py(string file, string name) => $"py|backend/shop/{file}#{name}";

    bool Line(string source, string target, string kind, Label? label = null) =>
        G.AllEdges.Any(e => e.Source == source && e.Target == target && e.Kind == kind && (label is null || e.Label == label));

    IEnumerable<string> Reasons(string owner) => G.AllBlindSpots.Where(b => b.Owner == owner).Select(b => b.Reason);

    IEnumerable<string> Targets(string verbAndPath) => G.AllEdges.Where(e => e.Kind == EdgeKind.RoutesTo && e.Source == Endpoint(verbAndPath)).Select(e => e.Target);

    static string Endpoint(string verbAndPath) => "endpoint:python|" + verbAndPath;

    [Fact]
    public void Nothing_is_run_and_what_is_left_out_is_counted()
    {
        Assert.False(File.Exists(Path.Combine(shop.Root, "ran.txt")));
        Assert.False(File.Exists(Path.Combine(shop.Root, "backend", "ran.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(shop.Root, "backend", "shop"), "__pycache__").SelectMany(d => Directory.GetFiles(d, "*.pyc")));
        var python = shop.Stats.Python!;
        Assert.Equal(1, python.NotebooksOmitted);
        Assert.Equal(1, python.Unparsed);
        Assert.Contains(G.AllBlindSpots, b => b.File == "analysis.ipynb" && b.Reason.Contains("notebook isn't read"));
        Assert.Contains(G.AllBlindSpots, b => b.File == "backend/shop/legacy.py" && b.Reason.Contains("couldn't be parsed"));
        Assert.Contains(shop.Stats.LoadWarnings, w => w.Contains("legacy.py"));
        Assert.DoesNotContain(G.AllNodes, n => (n.File ?? "").Contains("skipped_") || n.Name.Contains("skipped_"));
        Assert.Contains(G.AllNodes, n => n.Id == "project:requirements.txt" && n.Kind == NodeKind.Project);
    }

    [Fact]
    public void Points_have_their_kinds_lines_and_test_marks()
    {
        var method = G.AllNodes.Single(n => n.Id == Py("service.py", "OrderService.place"));
        Assert.Equal((NodeKind.Method, "OrderService.place", "backend/shop/service.py"), (method.Kind, method.Name, method.File!));
        Assert.True(method.Line > 0 && method.EndLine > method.Line);
        Assert.Equal(NodeKind.Type, G.AllNodes.Single(n => n.Id == Py("service.py", "OrderService")).Kind);
        Assert.Equal(NodeKind.Property, G.AllNodes.Single(n => n.Id == Py("service.py", "OrderService.size")).Kind);
        Assert.Equal(NodeKind.Field, G.AllNodes.Single(n => n.Id == Py("service.py", "log")).Kind);
        Assert.True(G.AllNodes.Single(n => n.Id == Py("test_orders.py", "test_list_orders")).IsTest);
        Assert.True(Line("file:backend/shop/service.py", Py("service.py", "OrderService"), EdgeKind.Contains));
        Assert.True(Line(Py("service.py", "OrderService"), Py("service.py", "OrderService.place"), EdgeKind.Contains));
    }

    [Fact]
    public void Calls_are_resolved_only_as_far_as_the_text_proves()
    {
        // Same module, and through a relative import: ✅.
        Assert.True(Line(Py("app.py", "item"), Py("app.py", "load_name"), EdgeKind.Calls, Label.Certain));
        Assert.True(Line(Py("service.py", "OrderService.check"), Py("service.py", "audit"), EdgeKind.Calls, Label.Certain));
        Assert.True(Line(Py("orders.py", "cancel"), Py("service.py", "audit"), EdgeKind.Calls, Label.Certain)); // `import … as`
        // self. on a known class, an attribute set in __init__, and super().
        Assert.True(Line(Py("service.py", "OrderService.place"), Py("service.py", "OrderService.check"), EdgeKind.Calls, Label.Certain));
        Assert.True(Line(Py("service.py", "OrderService.all"), Py("store.py", "Store.read_all"), EdgeKind.Calls, Label.Certain));
        Assert.True(Line(Py("service.py", "FastOrderService.place"), Py("service.py", "OrderService.place"), EdgeKind.Calls, Label.Certain));
        // An absolute import rests on how the app is started: 🟡. A local holding an instance of it, too.
        Assert.True(Line(Py("orders.py", "list_orders"), Py("service.py", "OrderService.place"), EdgeKind.Calls, Label.Guessed));
        // Inheritance and overriding.
        Assert.True(Line(Py("service.py", "FastOrderService"), Py("service.py", "OrderService"), EdgeKind.Inherits, Label.Certain));
        Assert.True(Line(Py("service.py", "FastOrderService.place"), Py("service.py", "OrderService.place"), EdgeKind.Overrides));
        Assert.True(Line(Py("service.py", "OrderService.place"), Py("service.py", "FastOrderService.place"), EdgeKind.DispatchesTo));
        // Imports are lines, and they're never walked.
        Assert.True(Line("file:backend/shop/app.py", Py("orders.py", "orders_bp"), EdgeKind.Imports, Label.Certain));
        Assert.DoesNotContain(EdgeKind.Imports, EdgeKind.Propagating);
    }

    [Fact]
    public void What_cant_be_proved_is_a_blind_spot_not_a_guess()
    {
        Assert.Contains(Reasons(Py("orders.py", "cancel")), r => r.Contains("on_done()"));
        Assert.Contains(Reasons(Py("orders.py", "cancel")), r => r.Contains("dynamic: getattr()"));
        Assert.Contains(Reasons(Py("orders.py", "unknown_receiver")), r => r.Contains(".place()"));
        Assert.DoesNotContain(G.AllEdges, e => e.Source == Py("orders.py", "unknown_receiver") && e.Kind == EdgeKind.Calls);
        Assert.DoesNotContain(Reasons(Py("orders.py", "unknown_receiver")), r => r.Contains("frobnicate"));
        Assert.Contains(Reasons(Py("service.py", "Hidden")), r => r.Contains("base class UnknownBase"));
        Assert.Contains(Reasons(Py("service.py", "Hidden.run")), r => r.Contains(".inherited()"));
        // A file off the importer's path isn't followed, however well its name fits.
        Assert.Contains(Reasons("file:backend/shop/app.py"), r => r.Contains("import helpers could be backend/tools/helpers.py"));
        Assert.DoesNotContain(G.AllEdges, e => e.Target == "py|backend/tools/helpers.py#shout" && e.Kind == EdgeKind.Calls);
        var python = shop.Stats.Python!;
        Assert.True(python.CallsUnresolved > 0 && python.CallsCertain > 0 && python.CallsGuessed > 0 && python.CallsOutside > 0 && python.CallsNotInProject > 0);
    }

    [Fact]
    public void Flask_routes_become_endpoints_with_their_real_urls()
    {
        Assert.Equal([Py("app.py", "health")], Targets("GET /health"));
        Assert.Equal([Py("app.py", "item")], Targets("GET /items/{item_id}"));
        Assert.Equal([Py("app.py", "item")], Targets("DELETE /items/{item_id}"));
        Assert.Equal([Py("app.py", "files")], Targets("GET /files/{*name}"));
        Assert.Equal([Py("app.py", "ping")], Targets("GET /ping"));
        Assert.Empty(Targets("POST /ping"));
        Assert.Equal([Py("app.py", "report")], Targets("GET /report")); // methods= followed to its constant
        Assert.Equal([Py("app.py", "report")], Targets("PATCH /report"));
        // Blueprints: their own prefix, a registration's prefix instead, and one inside another.
        Assert.Equal([Py("orders.py", "list_orders")], Targets("POST /api/orders"));
        Assert.Equal([Py("orders.py", "cancel")], Targets("POST /api/orders/{order_id}/cancel"));
        Assert.Equal([Py("stock.py", "levels")], Targets("GET /api/stock/levels"));
        Assert.Equal([Py("admin.py", "daily")], Targets("GET /admin/reports/daily"));
        // A class view: its handlers.
        Assert.Equal([Py("admin.py", "AccountView.get")], Targets("GET /accounts"));
        Assert.Equal([Py("admin.py", "AccountView.post")], Targets("POST /accounts"));
        Assert.Contains(G.AllEdges, e => e.Kind == EdgeKind.RoutesTo && e.Source == Endpoint("GET /health") && e.Label == Label.Certain);
    }

    [Fact]
    public void A_route_that_cant_be_proved_says_so()
    {
        Assert.Contains(Reasons(Py("app.py", "built")), r => r.Contains("URL is built at runtime"));
        Assert.DoesNotContain(G.AllEdges, e => e.Kind == EdgeKind.RoutesTo && e.Target == Py("app.py", "built"));
        Assert.Contains(Reasons(Py("admin.py", "register.on_a_parameter")), r => r.Contains("isn't provably a Flask app"));
        // Served at its own prefix, but only 🟡 and with a ❓: nothing registers it.
        Assert.Contains(G.AllEdges, e => e.Kind == EdgeKind.RoutesTo && e.Source == Endpoint("GET /forgotten/page") && e.Label == Label.Guessed);
        Assert.Contains(Reasons(Py("admin.py", "forgotten_page")), r => r.Contains("isn't seen registered"));
    }

    [Fact]
    public void Reacts_calls_in_plain_js_reach_flasks_routes()
    {
        Assert.Contains(G.AllNodes, n => n.Id == "project:frontend/package.json");
        Assert.Contains(G.AllNodes, n => n.Kind == NodeKind.Page && n.Name == "/orders");
        var app = "ts|frontend/src/App.js#";
        Assert.True(Line(app + "loadOrders", Endpoint("POST /api/orders"), EdgeKind.CallsHttp));
        Assert.True(Line(app + "cancelOrder", Endpoint("POST /api/orders/{order_id}/cancel"), EdgeKind.CallsHttp));
        Assert.True(Line(app + "loadItem", Endpoint("GET /items/{item_id}"), EdgeKind.CallsHttp));
        Assert.True(Line(app + "loadLevels", Endpoint("GET /api/stock/levels"), EdgeKind.CallsHttp));
        Assert.Contains(Reasons(app + "loadNothing"), r => r.Contains("matched no backend endpoint"));
        Assert.Contains(Reasons(app + "loadBuilt"), r => r.Contains("built at runtime"));
        var python = shop.Stats.Python!;
        Assert.Equal((5, 4, 1, 1), (python.FrontendCalls, python.FrontendCallsMatched, python.FrontendCallsUnmatched, python.FrontendCallsRuntimeUrl));
    }

    [Fact]
    public async Task A_second_build_reuses_python_and_gives_the_same_bytes()
    {
        var first = Path.Combine(shop.Root, ".stangent", "a.gz");
        var second = Path.Combine(shop.Root, ".stangent", "b.gz");
        GraphStore.Export(G, first);
        var (again, stats) = await GraphBuilder.BuildAsync(shop.Repo, new BuildOptions(Restore: false), TestContext.Current.CancellationToken);
        Assert.Equal(0, stats.Extracted);
        GraphStore.Export(again, second);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Theory]
    [InlineData("components:\n  python: false\n", false, false)]
    [InlineData("components:\n  detector-flask-routes: false\n", true, false)]
    public async Task The_switches_take_python_or_its_routes_out(string config, bool python, bool routes)
    {
        var root = FlaskShop.Create(config);
        try
        {
            var repo = StangentRepo.Discover(root);
            var (graph, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), TestContext.Current.CancellationToken);
            Assert.Equal(python, graph.AllNodes.Any(n => n.Id.StartsWith("py|", StringComparison.Ordinal)));
            Assert.Equal(python, stats.Python is not null);
            Assert.Equal(python, graph.AllNodes.Any(n => n.Id == "project:frontend/package.json"));
            Assert.Equal(routes, graph.AllNodes.Any(n => n.Kind == NodeKind.Endpoint));
            Assert.Equal(routes, graph.AllBlindSpots.Any(b => b.Detector == "flask-routes"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_repo_with_no_python_keeps_its_plain_react_app_off_the_map()
    {
        var root = FlaskShop.Create();
        try
        {
            foreach (var f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) is ".py" or ".ipynb"))
                File.Delete(f);
            var repo = StangentRepo.Discover(root);
            var (graph, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), TestContext.Current.CancellationToken);
            Assert.Null(stats.Python);
            Assert.Equal(0, stats.Frontends);
            Assert.DoesNotContain(graph.AllNodes, n => n.Id.StartsWith("py|", StringComparison.Ordinal) || n.Id.StartsWith("ts|", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
