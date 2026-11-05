using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Loading;
using Stangent.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Xunit;

namespace Stangent.Core.Tests;

/// <summary>Render routes and shared components as candidates (route-render, shared-candidates, D127): the extractor's own cases are in its tests.</summary>
public class SharedCandidateTests
{
    static string App()
    {
        var root = Directory.CreateTempSubdirectory("stangent-shared-").FullName;
        void Write(string name, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name))!);
            File.WriteAllText(Path.Combine(root, name), text);
        }
        Write("package.json", """{ "name": "shop-ui" }""");
        Write("tsconfig.json", """{ "compilerOptions": { "jsx": "preserve", "allowJs": true, "module": "esnext", "moduleResolution": "bundler" }, "include": ["src"] }""");
        Write("src/App.tsx", """
            import { Route } from "react-router-dom"; import Orders from "./Orders"; import Cart from "./Cart"; import Billing from "./Billing"; import Notice from "./Notice";
            export default function App() { return <div>
              <Route path="/orders" element={<Orders />} /><Route path="/cart" element={<Cart />} /><Route path="/billing" element={<Billing />} />
              <Route path="/notice" render={(props) => <Notice {...props} />} />
            </div>; }
            """);
        // Orders renders both directly, Cart through a helper inside it, Billing only through Panel, which isn't shared.
        Write("src/Orders.tsx", """import Popup from "./Popup"; import Tip from "./Tip"; export default function Orders() { return <div><Popup /><Tip /></div>; }""");
        Write("src/Cart.tsx", """import Popup from "./Popup"; import Tip from "./Tip"; export default function Cart() { const inner = () => <Popup />; return <div>{inner()}<Tip /></div>; }""");
        Write("src/Billing.tsx", """import Panel from "./Panel"; export default function Billing() { return <Panel />; }""");
        Write("src/Panel.tsx", """import Popup from "./Popup"; import Tip from "./Tip"; export default function Panel() { return <div><Popup /><Tip /></div>; }""");
        Write("src/Popup.tsx", """export default function Popup() { return <p>popup</p>; }""");
        Write("src/Tip.tsx", """export default function Tip() { return <p>tip</p>; }""");
        Write("src/Notice.tsx", """export default function Notice() { return <p>notice</p>; }""");
        return root;
    }

    static PageSet Pages(string root, bool shared = true, bool routes = false, Func<SpecSettings, SpecSettings>? settings = null)
    {
        var project = FrontendScanner.Find(root).Single();
        var (fragment, _, error) = FrontendScanner.Extract(root, project, "print", new Dictionary<string, Dictionary<string, List<string>>>(), routeRender: routes);
        Assert.Null(error);
        var graph = new CodeGraph { Commit = "c" };
        graph.Fragments[project.Id] = fragment;
        var config = new StangentConfig();
        config.Components["shared-candidates"] = shared;
        var made = SpecSettings.From(config);
        return PageList.Build(graph, root, settings?.Invoke(made) ?? made);
    }

    const string Popup = "ts|src/Popup.tsx#Popup";
    const string Tip = "ts|src/Tip.tsx#Tip";

    static JsonObject Section(string title) => new() { ["title"] = title, ["text"] = "What it does." };

    static readonly Dictionary<string, JsonObject> None = [];

    [Fact]
    public void A_candidate_screen_brings_the_shared_components_it_renders_itself_labelled()
    {
        var set = Pages(App());
        var found = Candidates.Find(Section("Orders"), set, None);
        Assert.Empty(found.LeftOut);
        Assert.Equal("shared component of page:/orders", found.Kept.Single(c => c.Page == Popup).Why);
        Assert.Equal("shared component of page:/orders", found.Kept.Single(c => c.Page == Tip).Why);
    }

    [Fact]
    public void A_function_inside_the_screen_is_the_screen()
    {
        Assert.Contains(Candidates.Find(Section("Cart"), Pages(App()), None).Kept, c => c.Page == Popup);
    }

    [Fact]
    public void Nothing_further_than_the_screen_itself_and_nothing_on_a_name_alone()
    {
        var set = Pages(App());
        var billing = Candidates.Find(Section("Billing"), set, None);
        Assert.DoesNotContain(billing.Kept, c => c.Why.StartsWith("shared component", StringComparison.Ordinal));
        Assert.Empty(billing.LeftOut);
        // Popup's own name finds its page as it always did, and no screen comes with it.
        Assert.Equal(Popup, Assert.Single(Candidates.Find(Section("Popup"), set, None).Kept).Page);
    }

    [Fact]
    public void Off_the_list_is_what_it_was()
    {
        var root = App();
        var off = Candidates.Find(Section("Orders"), Pages(root, shared: false), None);
        Assert.Empty(off.LeftOut);
        Assert.Equal(["page:/orders"], off.Kept.Select(c => c.Page));
        Assert.False(StangentComponents.IsOn(new StangentConfig(), "shared-candidates"));
        Assert.False(StangentComponents.IsOn(new StangentConfig(), "route-render"));
    }

    static JsonObject Recorded(int chars) => new()
    {
        ["kind"] = "component", ["title"] = "t", ["codeTitle"] = "c",
        ["shows"] = new JsonArray(new JsonObject { ["text"] = new string('x', chars) }),
    };

    [Fact]
    public void What_doesnt_fit_the_bundle_budget_is_listed_not_dropped()
    {
        var recorded = new Dictionary<string, JsonObject>(StringComparer.Ordinal) { [Popup] = Recorded(50), [Tip] = Recorded(50) };
        var whole = LinkRun.PageBlock(Popup, recorded[Popup]).Length;
        var set = Pages(App(), settings: s => s with { MaxBundleChars = whole + "What it does.".Length });
        var found = Candidates.Find(Section("Orders"), set, recorded);
        Assert.Contains(found.Kept, c => c.Page == Popup);
        var left = Assert.Single(found.LeftOut);
        Assert.Equal(Tip, left.Page);
        Assert.Equal("shared component of page:/orders", left.Why);
        Assert.Contains(Tip, LinkRun.LeftOutBlock(found.LeftOut));
        Assert.Equal("", LinkRun.LeftOutBlock([]));
    }

    [Fact]
    public void A_render_route_gets_a_page_only_with_its_switch_on()
    {
        var root = App();
        Assert.Null(Pages(root).Find("page:/notice"));
        var set = Pages(root, routes: true);
        Assert.Equal("/notice · Notice", set.Find("page:/notice")!.Title);
    }

    [Fact]
    public async Task A_plain_react_repo_with_no_python_is_read_only_when_it_asks_for_a_route_rule()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("stangent-plain-").FullName;
        File.WriteAllText(Path.Combine(root, "package.json"), """{ "dependencies": { "react": "18" } }""");
        File.WriteAllText(Path.Combine(root, "App.jsx"), """export default function App() { return <p>hi</p>; }""");
        var repo = StangentRepo.Init(root).Repo;
        var (_, off) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
        Assert.Equal(0, off.Frontends);
        Assert.Null(off.RouteRender);
        repo.Config.Components["route-render"] = true;
        var (_, on) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
        Assert.Equal(1, on.Frontends);
        Assert.Equal(new RouteRenderStats(0, 0), on.RouteRender);
    }
}
