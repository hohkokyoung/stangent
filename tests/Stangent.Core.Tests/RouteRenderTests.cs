using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Loading;
using Stangent.Spec;
using Stangent.Spec.Pages;
using Xunit;

namespace Stangent.Core.Tests;

/// <summary>Render routes (route-render, D127): the extractor's own cases are in its tests.</summary>
public class RouteRenderTests
{
    static string App()
    {
        var root = Directory.CreateTempSubdirectory("stangent-routes-").FullName;
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

    static PageSet Pages(string root, bool routes = false)
    {
        var project = FrontendScanner.Find(root).Single();
        var (fragment, _, error) = FrontendScanner.Extract(root, project, "print", new Dictionary<string, Dictionary<string, List<string>>>(), routeRender: routes);
        Assert.Null(error);
        var graph = new CodeGraph { Commit = "c" };
        graph.Fragments[project.Id] = fragment;
        return PageList.Build(graph, root, SpecSettings.From(new StangentConfig()));
    }

    [Fact]
    public void Route_render_is_off_by_default() => Assert.False(StangentComponents.IsOn(new StangentConfig(), "route-render"));

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
