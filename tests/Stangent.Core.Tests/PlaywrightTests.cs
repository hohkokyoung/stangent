using Stangent.Core.Evidence;
using Stangent.Graph;
using Stangent.Graph.Detectors;
using Stangent.Graph.Extraction;
using Stangent.Graph.Impact;

namespace Stangent.Core.Tests;

public class PlaywrightTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-pw-").FullName;

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    void Write(string rel, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, rel))!);
        File.WriteAllText(Path.Combine(root, rel), text);
    }

    const string Import = "import { test } from '@playwright/test';\n";

    static Node Page(string route, string config = "web/tsconfig.json") =>
        new($"page:{config}|{route}", NodeKind.Page, route, "project:" + config, "web/src/routes.ts", 3, 3);

    CodeGraph Link(params Node[] pages)
    {
        var graph = new CodeGraph { Commit = "0123456789abcdef0123456789abcdef01234567", DirtyFiles = [] };
        graph.LinkedNodes.AddRange(pages);
        new PlaywrightDetector().Link(new LinkContext(graph, new Dictionary<string, List<string>>()) { Root = root });
        return graph;
    }

    [Theory]
    [InlineData("await page.goto('/orders');", "page", "/orders")]
    [InlineData("await page.goto(\"/orders\", { waitUntil: 'load' });", "page", "/orders")]
    [InlineData("await page.goto(`/orders`);", "page", "/orders")]
    [InlineData("await this.page.goto('/orders');", "page", "/orders")]
    [InlineData("await page\n  .goto(\n    '/orders'\n  );", "page", "/orders")]
    [InlineData("await page?.goto('/orders');", "page", "/orders")]
    [InlineData("await page.goto(url);", "page", null)]
    [InlineData("await page.goto(`/orders/${id}`);", "page", null)]
    [InlineData("await page.goto('/orders/' + id);", "page", null)]
    [InlineData("await login.goto();", "login", null)]
    [InlineData("await (await open()).goto('/x');", "", "/x")]
    public void A_goto_call_is_read_for_its_receiver_and_whole_url(string code, string receiver, string? path)
    {
        var call = Assert.Single(PlaywrightSpecs.Gotos(code));
        Assert.Equal(receiver, call.Receiver);
        Assert.Equal(path, call.Path);
    }

    [Theory]
    [InlineData("// await page.goto('/orders');")]
    [InlineData("/* await page.goto('/orders');\n await page.goto('/b'); */")]
    [InlineData("test('page.goto(\"/orders\") works', () => {});")]
    [InlineData("const s = `await page.goto('/orders')`;")]
    public void A_goto_in_a_comment_or_a_string_isnt_a_call(string code) => Assert.Empty(PlaywrightSpecs.Gotos(code));

    [Fact]
    public void A_call_keeps_its_line_past_a_comment_and_a_stray_quote()
    {
        var call = Assert.Single(PlaywrightSpecs.Gotos("// it's\nconst r = /it's/;\n/* a\nb */\nawait page.goto('/x');\n"));
        Assert.Equal(5, call.Line);
    }

    [Fact]
    public void A_spec_opening_a_screen_becomes_a_guessed_line_at_its_goto()
    {
        Write("e2e/orders.spec.ts", Import + "test('x', async ({ page }) => {\n  await page.goto('/orders/5?tab=1');\n});\n");
        var graph = Link(Page("/orders/:id"), Page("/settings"));

        var line = Assert.Single(graph.ScreenTests);
        Assert.Equal(("e2e/orders.spec.ts", 3, "/orders/5?tab=1", Label.Guessed), (line.Spec, line.Line, line.Goto, line.Label));
        Assert.Equal("page:web/tsconfig.json|/orders/:id", line.Page);
        Assert.Empty(graph.AllBlindSpots);
    }

    [Theory]
    [InlineData("await page.goto(base + '/orders');", "a built URL")]
    [InlineData("await page.goto(`${base}/orders`);", "a built URL")]
    [InlineData("await ordersPage.goto();", "on ordersPage")]
    [InlineData("await ordersPage.goto('/orders');", "on ordersPage")]
    [InlineData("await page.goto('https://example.com/orders');", "isn't the app's own address")]
    [InlineData("await page.goto('about:blank');", "isn't the app's own address")]
    [InlineData("await page.goto('#/orders');", "hash route")]
    [InlineData("await page.goto('/#/orders');", "hash route")]
    [InlineData("await page.goto('orders');", "relative to a base URL")]
    [InlineData("await page.goto('/nothing/here');", "no screen on the map")]
    public void A_goto_the_code_cant_resolve_is_a_blind_spot_never_a_guess(string code, string reason)
    {
        Write("e2e/orders.spec.ts", Import + code + "\n");
        var graph = Link(Page("/orders"));

        Assert.Empty(graph.ScreenTests);
        var spot = Assert.Single(graph.AllBlindSpots);
        Assert.Contains(reason, spot.Reason);
        Assert.Equal(("e2e/orders.spec.ts", 2, PlaywrightDetector.Detector), (spot.File, spot.Line, spot.Detector));
    }

    [Fact]
    public void A_local_absolute_url_counts_as_the_app()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('http://127.0.0.1:4200/orders');\n");
        Assert.Single(Link(Page("/orders")).ScreenTests);
    }

    [Fact]
    public void Screens_with_no_page_points_send_every_goto_to_a_blind_spot()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('/orders');\n");
        var graph = Link();
        Assert.Empty(graph.ScreenTests);
        Assert.Contains("no screen on the map", Assert.Single(graph.AllBlindSpots).Reason);
    }

    [Fact]
    public void A_spec_with_no_goto_says_so()
    {
        Write("e2e/a.spec.ts", Import + "test('x', async ({ home }) => { await home.open(); });\n");
        Assert.Contains("no goto", Assert.Single(Link(Page("/orders")).AllBlindSpots).Reason);
    }

    [Fact]
    public void The_exact_route_beats_a_param_and_the_specs_own_frontend_beats_another()
    {
        Write("web/e2e/a.spec.ts", Import + "await page.goto('/settings');\n");
        var graph = Link(Page("/settings"), Page("/:id"), Page("/settings", "admin/tsconfig.json"));
        Assert.Equal("page:web/tsconfig.json|/settings", Assert.Single(graph.ScreenTests).Page);
    }

    [Fact]
    public void Outside_any_frontend_every_frontend_with_the_route_is_linked()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('/settings');\n");
        Assert.Equal(2, Link(Page("/settings"), Page("/settings", "admin/tsconfig.json")).ScreenTests.Count);
    }

    [Fact]
    public void Another_runners_test_is_left_alone()
    {
        Write("web/src/a.spec.ts", "describe('x', () => it('y', () => { page.goto('/orders'); }));\n");
        Write("web/src/b.spec.ts", "describe('x', () => it('y', () => {}));\n");
        var graph = Link(Page("/orders"));
        Assert.Empty(graph.ScreenTests);
        Assert.Empty(graph.AllBlindSpots);
    }

    [Fact]
    public void A_playwright_config_above_a_spec_is_enough_and_a_plain_unit_test_beside_it_says_nothing()
    {
        Write("playwright.config.ts", "export default {};\n");
        Write("e2e/a.test.ts", "import { test } from '../fixtures';\nawait page.goto('/orders');\n");
        Write("web/src/unit.spec.ts", "describe('x', () => it('y', () => {}));\n");
        var graph = Link(Page("/orders"));
        Assert.Equal("e2e/a.test.ts", Assert.Single(graph.ScreenTests).Spec);
        Assert.Empty(graph.AllBlindSpots);
    }

    [Fact]
    public void A_repo_with_no_specs_gets_nothing_and_its_export_doesnt_change()
    {
        Write("web/src/app.ts", "export const a = 1;\n");
        var graph = Link(Page("/orders"));
        Assert.Empty(graph.ScreenTests);
        Assert.Empty(graph.AllBlindSpots);
        Assert.DoesNotContain("screenTests", GraphStore.CanonicalLines(graph).First());
    }

    [Fact]
    public void Lines_survive_export_import_and_the_database()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('/orders');\n");
        var graph = Link(Page("/orders"));
        var export = Path.Combine(root, "g.gz");
        var db = Path.Combine(root, "g.sqlite");
        GraphStore.Export(graph, export);
        GraphStore.Save(graph, db);
        Assert.Equal(graph.ScreenTests, GraphStore.Import(export).ScreenTests);
        Assert.Equal(graph.ScreenTests, GraphStore.Load(db)!.ScreenTests);
        Assert.Equal(GraphStore.CanonicalLines(graph), GraphStore.CanonicalLines(GraphStore.Import(export)));
    }

    [Fact]
    public void The_impact_walk_never_sees_these_lines()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('/orders');\nawait page.goto(url);\n");
        var page = Page("/orders");
        var graph = Link(page);
        Assert.Single(graph.ScreenTests);
        Assert.DoesNotContain(graph.AllEdges, e => e.Kind == EdgeKind.OpensScreen);
        Assert.DoesNotContain(graph.AllNodes, n => n.File == "e2e/a.spec.ts");
        Assert.DoesNotContain(EdgeKind.OpensScreen, EdgeKind.Propagating);
        Assert.DoesNotContain(EdgeKind.OpensScreen, EdgeKind.FirstStepOnly);

        var analyzer = new ImpactAnalyzer(graph);
        Assert.Empty(analyzer.Affected([page]));
        Assert.Empty(analyzer.InFile("e2e/a.spec.ts"));
    }

    [Fact]
    public void An_answer_cites_each_line_as_a_test_read_not_ran()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('/orders');\nawait page.goto(url);\n");
        var graph = Link(Page("/orders"), Page("/settings"));

        var answer = PlaywrightLinks.Answer(graph, "shop");
        Assert.Equal((1, 1, 1), ((int)answer["specs"]!, (int)answer["gotosResolved"]!, (int)answer["blindSpots"]!));
        var link = answer["links"]!.AsArray().Single()!;
        Assert.Equal("read", (string)link["strength"]!);
        Assert.Equal("guessed", (string)link["label"]!);
        Assert.Equal("shop/e2e/a.spec.ts", (string)link["citation"]!["ref"]!);

        var evidence = new EvidenceRecord
        {
            Id = "E-1", Commit = graph.Commit, Strength = EvidenceStrength.Read,
            Citation = new Citation { Kind = CitationKind.Test, Ref = (string)link["citation"]!["ref"]!, Lines = new LineRange(2, 2) },
            CheckedBy = [new EvidenceChecker { Kind = CheckerKind.Code, Name = "graph screen-tests" }],
        };
        Assert.Empty(evidence.Validate());
        Assert.Contains("\"kind\":\"test\"", link["citation"]!.ToJsonString());

        Assert.Empty(PlaywrightLinks.Answer(graph, "shop", "/settings")["links"]!.AsArray());
        Assert.Single(PlaywrightLinks.Answer(graph, "shop", "/orders")["links"]!.AsArray());
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => PlaywrightLinks.Answer(graph, "shop", "/none")).Code);
    }

    [Fact]
    public void A_spec_that_differs_from_the_commit_isnt_cited()
    {
        Write("e2e/a.spec.ts", Import + "await page.goto('/orders');\n");
        var graph = Link(Page("/orders"));
        graph.DirtyFiles = ["e2e/a.spec.ts"];
        var link = PlaywrightLinks.Answer(graph, "shop")["links"]!.AsArray().Single()!;
        Assert.False((bool)link["linesMatchCommit"]!);
        Assert.Null(link["citation"]);
    }

    [Fact]
    public async Task On_a_real_build_a_spec_links_to_a_real_page_and_impact_stays_the_same()
    {
        var clean = ShopCopy.Create();
        var withSpec = ShopCopy.Create();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var (before, _) = await GraphBuilder.BuildAsync(StangentRepo.Discover(clean), new BuildOptions(), ct);
            var page = before.AllNodes.First(n => n.Kind == NodeKind.Page && !n.Name.Contains(':'));
            File.WriteAllText(Path.Combine(withSpec, "orders.spec.ts"), $"{Import}await page.goto('{page.Name}');\nawait page.goto(url);\n");
            var (after, _) = await GraphBuilder.BuildAsync(StangentRepo.Discover(withSpec), new BuildOptions(), ct);

            Assert.Contains(after.ScreenTests, t => t.Spec == "orders.spec.ts" && t.Line == 2 && t.Page == page.Id);
            Assert.Equal(["orders.spec.ts"], after.DirtyFiles!);
            Assert.Equal(before.AllEdges.Count(), after.AllEdges.Count());
            Assert.Equal(before.AllNodes.Count(), after.AllNodes.Count());

            foreach (var start in before.AllNodes.Where(n => n.Kind is NodeKind.Page or NodeKind.Component).Select(n => n.Id).Distinct().Order(StringComparer.Ordinal).Take(10))
            {
                var a = new ImpactAnalyzer(before).Analyze(new ChangeSpec("symbol", start), clean);
                var b = new ImpactAnalyzer(after).Analyze(new ChangeSpec("symbol", start), withSpec);
                Assert.Equal(a["groups"]!.ToJsonString(), b["groups"]!.ToJsonString());
            }
        }
        finally
        {
            Directory.Delete(clean, recursive: true);
            Directory.Delete(withSpec, recursive: true);
        }
    }
}
