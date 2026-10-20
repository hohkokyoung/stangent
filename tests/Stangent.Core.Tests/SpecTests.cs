using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>
/// A small app written to a temp repo with a hand-made graph: three screens sharing a button, an endpoint whose action
/// has a rule, an event handler, a server-rendered screen, and code no page reaches. Fast, and exact about what each
/// slice holds. Subagent outputs are canned (no LLM, Phase 2 done-when).
/// </summary>
sealed class SpecApp : IDisposable
{
    public const string Route = "page:web/tsconfig.json|/orders";
    public const string OrdersPage = "ts|web/src/Orders.tsx#OrdersPage";
    public const string Save = "ts|web/src/Orders.tsx#OrdersPage.save";
    public const string Button = "ts|web/src/Button.tsx#Button";
    public const string Post = "endpoint:Shop.Api|POST /api/orders";
    public const string Create = "Shop.Api|M:Shop.Api.OrdersController.Create(System.Decimal)";
    public const string Place = "Shop.Core|M:Shop.Core.OrderService.Place(System.Decimal)";
    public const string Receipt = "Shop.Core|M:Shop.Core.ReceiptHandler.Handle(Shop.Core.OrderPlaced)";
    public const string Home = "endpoint:Shop.Api|GET /home";
    public const string HomeIndex = "Shop.Api|M:Shop.Api.HomeController.Index";
    public const string Charge = "Shop.Core|M:Shop.Core.Billing.Charge";

    public TempRepo Temp { get; }
    public string Root => Temp.Root;
    public StangentRepo Repo { get; private set; }
    public CodeGraph Graph { get; }

    public SpecApp(string? config = null)
    {
        Temp = new TempRepo(config);
        Repo = Temp.Repo;
        Write("web/src/Orders.tsx", """
            export function OrdersPage() {
              const save = () => fetch('/api/orders', { method: 'POST' });
              return <div><h1>Orders</h1><Button label="Place order" onClick={save} /></div>;
            }
            """);
        Write("web/src/Other.tsx", """
            export function CartPage() { return <Button label="Cart" />; }
            export function AccountPage() { return <Button label="Account" />; }
            """);
        Write("web/src/Button.tsx", """
            export function Button({ label, onClick }) {
              return <button onClick={onClick}>{label}</button>;
            }
            """);
        Write("Api/OrdersController.cs", """
            public class OrdersController(OrderService orders)
            {
                [HttpPost("api/orders")]
                public Order Create(decimal total) => orders.Place(total);
            }
            public class HomeController : Controller
            {
                [HttpGet("home")]
                public IActionResult Index() { return View(); }
            }
            """);
        Write("Core/OrderService.cs", """
            public class OrderService
            {
                public Order Place(decimal total)
                {
                    if (total > 100) throw new ArgumentException("Orders over 100 need approval");
                    events.Publish(new OrderPlaced());
                    return new Order { Total = total };
                }
            }
            public class ReceiptHandler
            {
                public void Handle(OrderPlaced e) => mail.Send("receipt");
            }
            public static class Billing
            {
                public static void Charge() => gateway.Charge();
            }
            """);

        Graph = new CodeGraph { Commit = "c0ffee" };
        var f = new ProjectFragment { ProjectId = "project:all", Fingerprint = "x" };
        f.Nodes.AddRange([
            new Node(Route, NodeKind.Page, "/orders", "project:web/tsconfig.json", "web/src/Orders.tsx", 1, 1),
            new Node("page:web/tsconfig.json|/cart", NodeKind.Page, "/cart", "project:web/tsconfig.json", "web/src/Other.tsx", 1, 1),
            new Node("page:web/tsconfig.json|/account", NodeKind.Page, "/account", "project:web/tsconfig.json", "web/src/Other.tsx", 2, 2),
            new Node(OrdersPage, NodeKind.Component, "OrdersPage", "project:web/tsconfig.json", "web/src/Orders.tsx", 1, 4),
            new Node(Save, NodeKind.Method, "OrdersPage.save", "project:web/tsconfig.json", "web/src/Orders.tsx", 2, 2),
            new Node("ts|web/src/Other.tsx#CartPage", NodeKind.Component, "CartPage", "project:web/tsconfig.json", "web/src/Other.tsx", 1, 1),
            new Node("ts|web/src/Other.tsx#AccountPage", NodeKind.Component, "AccountPage", "project:web/tsconfig.json", "web/src/Other.tsx", 2, 2),
            new Node(Button, NodeKind.Component, "Button", "project:web/tsconfig.json", "web/src/Button.tsx", 1, 3),
            new Node(Post, NodeKind.Endpoint, "POST /api/orders", "project:Api/Shop.Api.csproj", "Api/OrdersController.cs", 3, 3),
            new Node(Create, NodeKind.Method, "OrdersController.Create", "project:Api/Shop.Api.csproj", "Api/OrdersController.cs", 4, 4),
            new Node(Home, NodeKind.Endpoint, "GET /home", "project:Api/Shop.Api.csproj", "Api/OrdersController.cs", 8, 8),
            new Node(HomeIndex, NodeKind.Method, "HomeController.Index", "project:Api/Shop.Api.csproj", "Api/OrdersController.cs", 9, 9),
            new Node(Place, NodeKind.Method, "OrderService.Place", "project:Core/Shop.Core.csproj", "Core/OrderService.cs", 3, 8),
            new Node(Receipt, NodeKind.Method, "ReceiptHandler.Handle", "project:Core/Shop.Core.csproj", "Core/OrderService.cs", 12, 12),
            new Node(Charge, NodeKind.Method, "Billing.Charge", "project:Core/Shop.Core.csproj", "Core/OrderService.cs", 16, 16),
            new Node("project:Api/Shop.Api.csproj", NodeKind.Project, "Shop.Api"),
            new Node("project:Core/Shop.Core.csproj", NodeKind.Project, "Shop.Core"),
            new Node("project:web/tsconfig.json", NodeKind.Project, "web"),
        ]);
        f.Edges.AddRange([
            new Edge(Route, OrdersPage, EdgeKind.RoutesTo, Label.Certain, "routes"),
            new Edge("page:web/tsconfig.json|/cart", "ts|web/src/Other.tsx#CartPage", EdgeKind.RoutesTo, Label.Certain, "routes"),
            new Edge("page:web/tsconfig.json|/account", "ts|web/src/Other.tsx#AccountPage", EdgeKind.RoutesTo, Label.Certain, "routes"),
            new Edge(OrdersPage, Save, EdgeKind.Calls, Label.Certain),
            new Edge(OrdersPage, Button, EdgeKind.Renders, Label.Certain),
            new Edge("ts|web/src/Other.tsx#CartPage", Button, EdgeKind.Renders, Label.Certain),
            new Edge("ts|web/src/Other.tsx#AccountPage", Button, EdgeKind.Renders, Label.Certain),
            new Edge(Save, Post, EdgeKind.CallsHttp, Label.Guessed, "frontend-http"),
            new Edge(Post, Create, EdgeKind.RoutesTo, Label.Certain, "aspnetcore-routes"),
            new Edge(Home, HomeIndex, EdgeKind.RoutesTo, Label.Certain, "aspnetcore-routes"),
            new Edge(Create, Place, EdgeKind.Calls, Label.Certain),
            new Edge(Receipt, Place, EdgeKind.TriggeredBy, Label.Guessed, "publish-handle"),
        ]);
        f.BlindSpots.Add(new BlindSpot(Place, "Core/OrderService.cs", 6, "events.Publish: target resolved at run time"));
        Graph.Fragments[f.ProjectId] = f;
        GraphStore.Save(Graph, GraphStore.PathFor(Repo));
    }

    public void Write(string rel, string text)
    {
        var path = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.Replace("\r\n", "\n") + "\n");
    }

    public PageSet Pages() => PageList.Build(GraphStore.Load(GraphStore.PathFor(Repo))!, Root, SpecSettings.From(Repo.Config));

    public void Dispose() => Temp.Dispose();
}

public class SpecTests
{
    static JsonObject Statement(string text, params string[] cites) =>
        new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    /// <summary>A correct page for POST /api/orders, the way a writer would return it.</summary>
    static JsonObject GoodEndpointPage(JsonArray? resolutions = null)
    {
        var page = new JsonObject
        {
            ["title"] = "Place an order",
            ["shows"] = new JsonArray(Statement("Placing an order returns the new order with its total.", SpecApp.Create, SpecApp.Place)),
            ["actions"] = new JsonArray(new JsonObject
            {
                ["text"] = "A caller places an order with a total.", ["cites"] = new JsonArray(SpecApp.Create),
                ["endpoints"] = new JsonArray(SpecApp.Post),
            }),
            ["rules"] = new JsonArray(Statement("Orders over 100 are refused with \"Orders over 100 need approval\".", SpecApp.Place)),
            ["sideEffects"] = new JsonArray(Statement("Placing an order publishes OrderPlaced, which sends a receipt.", SpecApp.Place, SpecApp.Receipt)),
        };
        if (resolutions is not null) page["resolutions"] = resolutions;
        return page;
    }

    static void WriteOutput(SpecApp app, RunStep step, JsonNode output) =>
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());

    [Fact]
    public void Pages_come_from_the_graph_screens_shared_components_and_backend_entry_points()
    {
        using var app = new SpecApp();
        var set = app.Pages();
        Assert.Equal(
            new[] { "page:/account", "page:/cart", "page:/home", "page:/orders", SpecApp.Button, SpecApp.Post, SpecApp.Receipt }.Order(StringComparer.Ordinal),
            set.Pages.Select(p => p.Id).Order(StringComparer.Ordinal));
        Assert.Equal(PageKind.Screen, set.Find("page:/home")!.Kind);         // server-rendered: the action returns View()
        Assert.Equal(PageKind.Component, set.Find(SpecApp.Button)!.Kind);    // rendered by 3 screens
        Assert.Equal(PageKind.Backend, set.Find(SpecApp.Post)!.Kind);        // every endpoint has a page (screens stop at it)

        var orders = set.Slices["page:/orders"];
        Assert.Contains(SpecApp.Post, orders.Ids);                           // linked, not entered
        Assert.DoesNotContain(SpecApp.Place, orders.Ids);
        Assert.Equal("stop", orders.Nodes.Single(n => n.Node.Id == SpecApp.Button).Role);

        var post = set.Slices[SpecApp.Post];
        Assert.Contains(SpecApp.Place, post.Ids);
        Assert.Equal("handler", post.Nodes.Single(n => n.Node.Id == SpecApp.Receipt).Role);   // a side effect, one step back
        Assert.DoesNotContain(SpecApp.Charge, post.Ids);
        Assert.Single(post.Unknowns);
    }

    [Fact]
    public void Without_the_split_screens_walk_into_the_backend_and_only_unreached_endpoints_get_pages()
    {
        using var app = new SpecApp("version: 1\nspec: { splitAtEndpoints: false }\n");
        var set = app.Pages();
        Assert.Contains(SpecApp.Place, set.Slices["page:/orders"].Ids);
        Assert.Null(set.Find(SpecApp.Post));
    }

    [Fact]
    public void A_bundle_is_the_same_bytes_every_time_and_its_hash_ignores_the_previous_version()
    {
        using var app = new SpecApp();
        var set = app.Pages();
        var page = set.Find(SpecApp.Post)!;
        var a = Bundles.Build(set, page, null);
        var b = Bundles.Build(app.Pages(), page, null);
        var withPrevious = Bundles.Build(set, page, new JsonObject { ["title"] = "old" });
        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.InputHash, withPrevious.InputHash);
        Assert.Contains("\"title\": \"old\"", withPrevious.Text);
        Assert.Contains("if (total > 100) throw", a.Text);
        Assert.Contains("events.Publish: target resolved at run time", a.Text);
    }

    [Fact]
    public void A_made_up_citation_and_an_out_of_slice_citation_are_both_rejected()
    {
        using var app = new SpecApp();
        var set = app.Pages();
        var page = set.Find(SpecApp.Post)!;
        var output = GoodEndpointPage();
        output["rules"]!.AsArray().Add(Statement("Orders are charged at once.", "Shop.Core|M:Shop.Core.Payments.ChargeNow"));
        output["rules"]!.AsArray().Add(Statement("Orders are billed.", SpecApp.Charge));
        var errors = PageChecks.CheckWriter(output, set.Slices[page.Id], set.Builder, Bundles.Build(set, page, null));
        Assert.Equal(2, errors.Count);
        Assert.Contains("R2 cites `Shop.Core|M:Shop.Core.Payments.ChargeNow`, which isn't in the code graph", errors[0]);
        Assert.Contains("R3 cites `Shop.Core|M:Shop.Core.Billing.Charge`, which is outside this page's slice", errors[1]);
        Assert.Empty(PageChecks.CheckWriter(GoodEndpointPage(), set.Slices[page.Id], set.Builder, Bundles.Build(set, page, null)));
    }

    [Fact]
    public void A_page_runs_write_check_verify_then_is_recorded_and_a_rerun_makes_no_AI_call()
    {
        using var app = new SpecApp();
        var step = PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo);
        Assert.Equal(("write", PageRun.Writer), (step.Action, step.Agent));
        WriteOutput(app, step, GoodEndpointPage());
        step = PageRun.Check(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo, Usage.FromClaudeResult(Canned(1000, 200, "claude-sonnet-5-5")));
        Assert.Equal(("verify", PageRun.Verifier), (step.Action, step.Agent));
        WriteOutput(app, step, new JsonObject { ["findings"] = new JsonArray() });
        step = PageRun.Check(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo, Usage.FromClaudeResult(Canned(800, 50, "claude-sonnet-5-5")));
        Assert.Equal("recorded", step.Status);

        var paths = new SpecPaths(app.Root);
        var page = app.Pages().Find(SpecApp.Post)!;
        var json = File.ReadAllText(paths.PageJson(page));
        var md = File.ReadAllText(paths.PageMarkdown(page));
        var recorded = JsonNode.Parse(json)!;
        Assert.Equal("R1", (string)recorded["sections"]!["rules"]![0]!["id"]!);
        Assert.Equal(1000, (int)recorded["generated"]!["writer"]!["inputTokens"]!);
        Assert.Equal("claude-sonnet-5-5", (string)recorded["generated"]!["verifier"]!["model"]!);
        Assert.Single(recorded["unknowns"]!.AsArray());
        Assert.Contains("❓ `Core/OrderService.cs:6`", md);

        // Repeatable: unchanged inputs → no AI step, and the files stay byte for byte.
        var again = PageRun.Prepare(app.Pages(), page, app.Repo);
        Assert.Equal(("current", "none"), (again.Status, again.Action));
        Assert.Equal(json, File.ReadAllText(paths.PageJson(page)));
        Assert.Equal(md, File.ReadAllText(paths.PageMarkdown(page)));

        // A code change inside the slice makes it stale; the bundle then carries the previous version.
        app.Write("Core/OrderService.cs", File.ReadAllText(Path.Combine(app.Root, "Core/OrderService.cs")).Replace("total > 100", "total > 200").TrimEnd('\n'));
        var stale = Staleness.Report(app.Pages(), paths, null);
        Assert.Equal(SpecApp.Post, (string)stale["stale"]![0]!["id"]!);
        Assert.DoesNotContain(stale["stale"]!.AsArray(), s => (string)s!["id"]! == "page:/orders"); // the screen stops at the endpoint
        var rerun = PageRun.Prepare(app.Pages(), page, app.Repo);
        Assert.Equal("write", rerun.Action);
        Assert.Contains("Orders over 100 are refused", File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(rerun.Output!)!, "bundle.md")));
    }

    [Fact]
    public void A_failed_check_gets_one_retry_then_the_page_is_listed_as_failed_and_not_retried_with_the_same_inputs()
    {
        using var app = new SpecApp();
        var page = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), page, app.Repo);
        WriteOutput(app, step, new JsonObject { ["title"] = "x" });                       // breaks the schema
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.Equal(("retry", "write"), (step.Status, step.Action));
        Assert.Contains(step.Errors!, e => e.StartsWith("Schema:"));
        Assert.Contains("What to fix:", step.Prompt);
        WriteOutput(app, step, "not json at all");
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.Equal(("failed", "none"), (step.Status, step.Action));

        var failed = PageRun.Failed(new SpecPaths(app.Root));
        Assert.Equal(SpecApp.Post, (string)failed.Single()!["id"]!);
        Assert.Equal("failed the code check twice", (string)failed.Single()!["reason"]!);
        Assert.Equal("failed", PageRun.Prepare(app.Pages(), page, app.Repo).Status);
        Assert.Equal("write", PageRun.Prepare(app.Pages(), page, app.Repo, retryFailed: true).Action);
    }

    [Fact]
    public void A_planted_wrong_statement_is_caught_by_the_verifier_and_findings_without_evidence_are_dropped()
    {
        using var app = new SpecApp();
        var page = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), page, app.Repo);
        var wrong = GoodEndpointPage();
        wrong["rules"]![0]!["text"] = "Orders over 500 are refused.";                     // planted: the code says 100
        WriteOutput(app, step, wrong);
        step = PageRun.Check(app.Pages(), page, app.Repo, null);

        // Canned verifier: one real contradiction, one with a quote that isn't in the slice, one about no statement.
        WriteOutput(app, step, new JsonObject
        {
            ["findings"] = new JsonArray(
                new JsonObject { ["statement"] = "R1", ["kind"] = "contradicted", ["quote"] = "if (total > 100) throw new ArgumentException(\"Orders over 100 need approval\");", ["explanation"] = "The limit is 100." },
                new JsonObject { ["statement"] = "S1", ["kind"] = "contradicted", ["quote"] = "return Order.Empty;", ["explanation"] = "Made up." },
                new JsonObject { ["statement"] = "R9", ["kind"] = "unsupported", ["explanation"] = "No such statement." }),
        });
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.Equal(("fix", "write"), (step.Status, step.Action));
        var feedback = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "feedback-2.md"));
        Assert.Contains("**F1** (contradicted) on R1", feedback);
        Assert.DoesNotContain("F2", feedback);

        // Marking it fixed without changing it is caught; the one retry then fixes it.
        WriteOutput(app, step, Fixing(wrong, "fixed"));
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("is marked fixed, but statement R1 is unchanged"));
        WriteOutput(app, step, Fixing(GoodEndpointPage(), "fixed"));
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.Equal("recorded", step.Status);

        var recorded = JsonNode.Parse(File.ReadAllText(new SpecPaths(app.Root).PageJson(page)))!;
        var finding = recorded["verification"]!["findings"]!.AsArray().Single()!;
        Assert.Equal(("F1", "fixed", "Orders over 500 are refused."), ((string)finding["id"]!, (string)finding["outcome"]!, (string)finding["statementText"]!));
        Assert.Equal(2, (int)recorded["verification"]!["dropped"]!);
        Assert.Equal(1, (int)recorded["generated"]!["checkRetries"]!);
    }

    [Fact]
    public void Rejecting_an_unsupported_finding_needs_a_supporting_line_from_the_slice()
    {
        using var app = new SpecApp();
        var page = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), page, app.Repo);
        WriteOutput(app, step, GoodEndpointPage());
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        WriteOutput(app, step, new JsonObject
        {
            ["findings"] = new JsonArray(new JsonObject { ["statement"] = "E1", ["kind"] = "unsupported", ["explanation"] = "No receipt is visible." }),
        });
        step = PageRun.Check(app.Pages(), page, app.Repo, null);

        WriteOutput(app, step, Fixing(GoodEndpointPage(), "rejected", quote: "mail.Send(\"nothing like this\")"));
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.Contains(step.Errors!, e => e.Contains("its `quote` isn't a line from the slice's source"));
        WriteOutput(app, step, Fixing(GoodEndpointPage(), "rejected", quote: "public void Handle(OrderPlaced e) => mail.Send(\"receipt\");"));
        Assert.Equal("recorded", PageRun.Check(app.Pages(), page, app.Repo, null).Status);
    }

    [Fact]
    public void The_verifier_gets_one_retry_and_a_code_fragment_is_not_evidence()
    {
        using var app = new SpecApp();
        var page = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), page, app.Repo);
        WriteOutput(app, step, GoodEndpointPage());
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        step = PageRun.Check(app.Pages(), page, app.Repo, null);           // the verifier wrote nothing
        Assert.Equal(("retry", "verify"), (step.Status, step.Action));
        WriteOutput(app, step, new JsonObject
        {
            ["findings"] = new JsonArray(new JsonObject { ["statement"] = "R1", ["kind"] = "contradicted", ["quote"] = "return", ["explanation"] = "The code says otherwise." }),
        });
        var final = PageRun.Check(app.Pages(), page, app.Repo, null);
        Assert.True(final.Status == "recorded", string.Join("; ", final.Errors ?? []));   // "return" is no evidence: dropped
        Assert.False(Quotes.InSource("return", ["return new Order { Total = total };"]));
    }

    [Fact]
    public void A_slice_over_the_cap_is_listed_as_failed_without_an_AI_call()
    {
        using var app = new SpecApp("version: 1\nspec: { maxBundleChars: 100 }\n");
        var step = PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo);
        Assert.Equal(("failed", "none"), (step.Status, step.Action));
        Assert.StartsWith("slice over the size cap", step.Errors![0]);
    }

    [Fact]
    public void With_AI_switched_off_prepare_stops_but_stale_still_works()
    {
        using var app = new SpecApp("version: 1\nai: { enabled: false }\n");
        var e = Assert.Throws<StangentException>(() => PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo));
        Assert.Equal(ExitCodes.AiDisabled, e.ExitCode);
        Assert.Equal(7, (int)Staleness.Report(app.Pages(), new SpecPaths(app.Root), null)["counts"]!["new"]!);
    }

    static JsonObject Fixing(JsonObject page, string outcome, string? quote = null)
    {
        var resolution = new JsonObject { ["finding"] = "F1", ["outcome"] = outcome, ["reason"] = "Checked against the source." };
        if (quote is not null) resolution["quote"] = quote;
        var copy = (JsonObject)page.DeepClone();
        copy["resolutions"] = new JsonArray(resolution);
        return copy;
    }

    static JsonObject Canned(int input, int output, string model) => new()
    {
        ["duration_ms"] = 1234, ["total_cost_usd"] = 0.01,
        ["usage"] = new JsonObject { ["input_tokens"] = input, ["output_tokens"] = output, ["cache_read_input_tokens"] = 0, ["cache_creation_input_tokens"] = 0 },
        ["modelUsage"] = new JsonObject { [model] = new JsonObject { ["outputTokens"] = output } },
    };
}

/// <summary>Stale detection on the shop fixture (Phase 2 done-when): a real graph, a real commit, a real diff.</summary>
public class SpecStaleTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    [Fact]
    public async Task A_commit_makes_stale_exactly_the_pages_whose_slices_it_changes()
    {
        var settings = SpecSettings.From(shop.Repo.Config);
        var set = PageList.Build(shop.Graph, shop.Root, settings);
        var paths = new SpecPaths(shop.Root);
        // Record every page as written at this commit (only its input hash matters to stale detection).
        foreach (var p in set.Pages)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.PageJson(p))!);
            File.WriteAllText(paths.PageJson(p), CanonicalJson.Serialize(new JsonObject
            {
                ["schemaVersion"] = 1, ["id"] = p.Id, ["title"] = p.Title,
                ["generated"] = new JsonObject { ["bundleHash"] = Bundles.Build(set, p, null).InputHash, ["commit"] = shop.Graph.Commit },
            }));
        }
        Assert.Equal(0, (int)Staleness.Report(set, paths, null)["counts"]!["stale"]!);

        // Change OrderService.PlaceOrder's body, commit, rebuild the graph.
        var file = Path.Combine(shop.Root, "Shop.Core", "Orders.cs");
        var before = File.ReadAllText(file);
        try
        {
            File.WriteAllText(file, before.Replace("Total = total };", "Total = total * 2 };"));
            ShopCopy.Git(shop.Root, "-c user.name=test -c user.email=test@example.invalid commit -q -am change");
            var (graph, _) = await GraphBuilder.BuildAsync(shop.Repo, new BuildOptions(Restore: false), TestContext.Current.CancellationToken);
            var after = PageList.Build(graph, shop.Root, settings);
            var report = Staleness.Report(after, paths, "HEAD~1..HEAD");

            var stale = report["stale"]!.AsArray().Select(s => (string)s!["id"]!).ToList();
            var expected = after.Pages.Where(p => after.Slices[p.Id].Nodes.Any(n => n.Node.File == "Shop.Core/Orders.cs" && n.Role == "walk"
                    && n.Node.Kind == NodeKind.Method && n.Node.Name.Contains("PlaceOrder")))
                .Select(p => p.Id).ToList();
            Assert.NotEmpty(stale);
            Assert.Equal(expected.Order(StringComparer.Ordinal), stale.Order(StringComparer.Ordinal));
            Assert.All(report["stale"]!.AsArray(), s => Assert.Contains("Shop.Core/Orders.cs", s!["changedFiles"]!.AsArray().Select(f => (string)f!)));
            Assert.Contains("**Reference pages:**", Staleness.Markdown(report));
        }
        finally
        {
            File.WriteAllText(file, before);
            ShopCopy.Git(shop.Root, "-c user.name=test -c user.email=test@example.invalid commit -q -am revert");
            await GraphBuilder.BuildAsync(shop.Repo, new BuildOptions(Restore: false), TestContext.Current.CancellationToken);
            Directory.Delete(paths.ReferenceDir, recursive: true);
        }
    }
}
