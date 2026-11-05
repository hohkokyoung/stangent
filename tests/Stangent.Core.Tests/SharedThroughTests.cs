using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Graph.Loading;
using Stangent.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using Xunit;

namespace Stangent.Core.Tests;

/// <summary>A screen's shared components reached through its unshared ones become candidates (shared-through, D140). Fakes only: no judge runs.</summary>
public class SharedThroughTests
{
    const string Popup = "ts|src/Popup.tsx#Popup";
    const string Tip = "ts|src/Tip.tsx#Tip";
    const string DoorForm = "ts|src/DoorForm.tsx#DoorForm";
    const string NoticeForm = "ts|src/NoticeForm.tsx#NoticeForm";
    static readonly Dictionary<string, JsonObject> None = [];

    /// <summary>Three routes render Popup, and Popup renders Tip, so both are shared. Door has no route and Notice a render= route: each reaches Popup only through a form of its own.</summary>
    static string App()
    {
        var root = Directory.CreateTempSubdirectory("stangent-shared-through-").FullName;
        void Write(string name, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name))!);
            File.WriteAllText(Path.Combine(root, name), text);
        }
        Write("package.json", """{ "name": "shop-ui" }""");
        Write("tsconfig.json", """{ "compilerOptions": { "jsx": "preserve", "allowJs": true, "module": "esnext", "moduleResolution": "bundler" }, "include": ["src"] }""");
        Write("src/index.tsx", """import { createRoot } from "react-dom/client"; import App from "./App"; createRoot(document.body).render(<App />);""");
        Write("src/App.tsx", """
            import { Routes, Route } from "react-router-dom"; import Door from "./Door"; import Orders from "./Orders"; import Cart from "./Cart"; import Billing from "./Billing"; import Notice from "./Notice";
            export default function App({ who }: any) { return who ? <Routes>
              <Route path="/orders" element={<Orders />} /><Route path="/cart" element={<Cart />} /><Route path="/billing" element={<Billing />} />
              <Route path="/notice" render={(props) => <Notice {...props} />} />
            </Routes> : <Door />; }
            """);
        foreach (var screen in new[] { "Orders", "Cart", "Billing" })
            Write($"src/{screen}.tsx", $$"""import Popup from "./Popup"; export default function {{screen}}() { return <Popup />; }""");
        Write("src/Door.tsx", """import DoorForm from "./DoorForm"; export default function Door() { return <DoorForm />; }""");
        Write("src/DoorForm.tsx", """import Popup from "./Popup"; export default function DoorForm() { return <Popup />; }""");
        Write("src/Notice.tsx", """import NoticeForm from "./NoticeForm"; export default function Notice() { return <NoticeForm />; }""");
        Write("src/NoticeForm.tsx", """import Popup from "./Popup"; export default function NoticeForm() { return <Popup />; }""");
        Write("src/Popup.tsx", """import Tip from "./Tip"; export default function Popup() { return <Tip />; }""");
        Write("src/Tip.tsx", """export default function Tip() { return <p>tip</p>; }""");
        return root;
    }

    static PageSet Pages(string root, bool on = true, Func<SpecSettings, SpecSettings>? settings = null)
    {
        var project = FrontendScanner.Find(root).Single();
        var (fragment, _, error) = FrontendScanner.Extract(root, project, "print", new Dictionary<string, Dictionary<string, List<string>>>(), rootScreens: true, routeRender: true);
        Assert.Null(error);
        var graph = new CodeGraph { Commit = "c" };
        graph.Fragments[project.Id] = fragment;
        var config = new StangentConfig();
        config.Components["shared-through"] = on;
        var made = SpecSettings.From(config);
        return PageList.Build(graph, root, settings?.Invoke(made) ?? made);
    }

    static JsonObject Section(string title) => new() { ["title"] = title, ["text"] = "What it does." };

    static JsonObject Recorded(int chars) => new()
    {
        ["kind"] = "component", ["title"] = "t", ["codeTitle"] = "c",
        ["shows"] = new JsonArray(new JsonObject { ["text"] = new string('x', chars) }),
    };

    [Fact]
    public void A_screen_with_no_route_reaches_a_shared_component_through_an_unshared_one()
    {
        var set = Pages(App());
        Assert.Equal(PageKind.Component, set.Find(Popup)!.Kind);
        Assert.Null(set.Find(DoorForm));
        var found = Candidates.Find(Section("Door"), set, None);
        Assert.Empty(found.LeftOut);
        Assert.Equal("shared component reached from page:(no route) Door", found.Kept.Single(c => c.Page == Popup).Why);
    }

    [Fact]
    public void A_render_route_does_too()
    {
        var set = Pages(App());
        Assert.Null(set.Find(NoticeForm));
        Assert.Equal("shared component reached from page:/notice", Candidates.Find(Section("Notice"), set, None).Kept.Single(c => c.Page == Popup).Why);
    }

    [Fact]
    public void It_stops_at_the_first_shared_component_on_each_path()
    {
        var set = Pages(App());
        Assert.Equal(PageKind.Component, set.Find(Tip)!.Kind);
        Assert.Contains(set.Slices[Popup].Nodes, n => n.Node.Id == Tip);
        Assert.DoesNotContain(Candidates.Find(Section("Door"), set, None).Kept, c => c.Page == Tip);
        Assert.DoesNotContain(Candidates.Find(Section("Orders"), set, None).Kept, c => c.Page == Tip);
    }

    [Fact]
    public void A_shared_components_own_name_brings_nothing_new()
    {
        Assert.Equal([(Popup, "name")], Candidates.Find(Section("Popup"), Pages(App()), None).Kept.Select(c => (c.Page, c.Why)));
    }

    [Fact]
    public void Off_the_list_is_what_it_was()
    {
        var off = Candidates.Find(Section("Door"), Pages(App(), on: false), None);
        Assert.Empty(off.LeftOut);
        Assert.Equal(["page:(no route) Door"], off.Kept.Select(c => c.Page));
        var component = StangentComponents.All.Single(c => c.Name == "shared-through");
        Assert.False(component.OnByDefault);
        Assert.Equal("components.shared-through", component.Setting);
        Assert.Contains("fsd candidates", component.Commands);
        Assert.False(StangentComponents.IsOn(new StangentConfig(), "shared-through"));
    }

    [Fact]
    public void What_doesnt_fit_the_bundle_budget_is_listed_not_dropped()
    {
        var recorded = new Dictionary<string, JsonObject>(StringComparer.Ordinal) { [Popup] = Recorded(50) };
        var set = Pages(App(), settings: s => s with { MaxBundleChars = 10 });
        var found = Candidates.Find(Section("Door"), set, recorded);
        Assert.DoesNotContain(found.Kept, c => c.Page == Popup);
        Assert.Equal(new LeftOutPage(Popup, "shared component reached from page:(no route) Door", LeftOutPage.NoRoom), Assert.Single(found.LeftOut));
    }

    /// <summary>Three routes each render every shared component directly, except that /alpha reaches the first by name only through Mid.</summary>
    static PageSet Crowded()
    {
        var root = Directory.CreateTempSubdirectory("stangent-crowded-").FullName;
        var f = new ProjectFragment { ProjectId = "project:web", Fingerprint = "x" };
        f.Nodes.Add(new Node("project:web", NodeKind.Project, "web"));
        Node Component(string name) => new($"ts|{name}", NodeKind.Component, name, "project:web", "a.tsx", 1, 1);
        var shared = Enumerable.Range(0, 12).Select(i => $"C{i:00}").ToList();
        f.Nodes.AddRange(shared.Select(Component));
        f.Nodes.AddRange([Component("Afar"), Component("Mid")]);
        foreach (var screen in new[] { "alpha", "beta", "gamma" })
        {
            f.Nodes.Add(new Node($"page:{screen}", NodeKind.Page, $"/{screen}", "project:web", "a.tsx", 1, 1));
            f.Nodes.Add(Component(screen + "Page"));
            f.Edges.Add(new Edge($"page:{screen}", $"ts|{screen}Page", EdgeKind.RoutesTo, Label.Certain, "routes"));
            f.Edges.AddRange(shared.Select(c => new Edge($"ts|{screen}Page", $"ts|{c}", EdgeKind.Renders, Label.Certain)));
            f.Edges.Add(screen == "alpha" ? new Edge("ts|alphaPage", "ts|Mid", EdgeKind.Renders, Label.Certain) : new Edge($"ts|{screen}Page", "ts|Afar", EdgeKind.Renders, Label.Certain));
        }
        f.Edges.Add(new Edge("ts|Mid", "ts|Afar", EdgeKind.Renders, Label.Certain));
        var graph = new CodeGraph { Commit = "c" };
        graph.Fragments[f.ProjectId] = f;
        var config = new StangentConfig();
        config.Components["shared-through"] = true;
        return PageList.Build(graph, root, SpecSettings.From(config));
    }

    [Fact]
    public void The_page_limit_holds_nearest_first_and_the_rest_are_listed()
    {
        var found = Candidates.Find(Section("Alpha"), Crowded(), None);
        Assert.Equal(Candidates.MaxPerSection, found.Kept.Count);
        Assert.Equal("page:/alpha", found.Kept[0].Page);
        Assert.Equal(Enumerable.Range(0, 9).Select(i => $"ts|C{i:00}"), found.Kept.Skip(1).Select(c => c.Page));
        Assert.Equal(["ts|C09", "ts|C10", "ts|C11", "ts|Afar"], found.LeftOut.Select(c => c.Page));
        Assert.All(found.LeftOut, c => Assert.Equal(LeftOutPage.NoRoom, c.Reason));
    }

    // Through the run: the small app's Orders screen renders its shared Button, whose page isn't recorded.

    static JsonObject State(SpecApp app, RunStep step) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "state.json")))!;

    static string JudgeText(SpecApp app, RunStep step) =>
        SharedHead.Assemble(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md")));

    static void RecordButton(SpecApp app)
    {
        var def = app.Pages().Find(SpecApp.Button)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["title"] = "Button",
            ["shows"] = new JsonArray(new JsonObject { ["text"] = "The button shows its label.", ["cites"] = new JsonArray((JsonNode)SpecApp.Button) }),
            ["actions"] = new JsonArray(), ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray(),
        }.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
    }

    const string On = "components: { shared-through: true }\n";

    [Fact]
    public void A_page_not_written_yet_is_needed_told_to_the_judge_and_kept_on_the_run()
    {
        using var app = FsdLinkTests.App(On);
        var index = SpecIndex.Load(app.Root);
        var needed = PagesNeeded.ForDocument(app.Pages(), index, app.Root, "shop", null)["pages"]!.AsArray();
        Assert.Contains(needed, p => (string)p!["id"]! == SpecApp.Button);

        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Equal("judge", step.Action);
        Assert.Contains(SpecApp.Button, step.Warning);
        Assert.Contains("--for-fsd shop", step.Warning);
        var bundle = JudgeText(app, step);
        Assert.Contains("# Pages left out", bundle);
        Assert.Contains($"- `{SpecApp.Button}` (shared component reached from page:/orders): no page written yet", bundle);
        var state = State(app, step);
        Assert.DoesNotContain(state["candidates"]!.AsArray(), c => (string)c!["page"]! == SpecApp.Button);

        // The move-a-section hash reads the same left-out list from the run, so a renumbered section keeps its links.
        Assert.Equal((string)state["inputs"]!, LinkRun.JudgeInputs(index.Sections[("shop", "1")], "shop",
            [.. state["candidates"]!.AsArray().Select(c => (string)c!["page"]!)], index, null, true, leftOut: LinkRun.LeftOutOf(state)));

        // The judge can't cite what it wasn't given.
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        FsdLinkTests.Write(app, step, FsdLinkTests.Links(FsdLinkTests.Link("agree", "Every order sends a receipt.", SpecApp.Button, "S1", "The button shows its label.")));
        var retry = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Contains(retry.Errors!, e => e.Contains("isn't one of the candidate pages"));
        FsdLinkTests.Write(app, retry, FsdLinkTests.Links(FsdLinkTests.Link("conflict", "Orders over 50 need approval.", SpecApp.Post, "R1", "Orders over 100 are refused")));
        Assert.Equal("recorded", LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null).Status);
        var run = LinkRun.LoadRuns(app.Root)["shop#1"]!;
        Assert.Equal(SpecApp.Button, (string)run["candidatesLeftOut"]![0]!["page"]!);
        Assert.Equal(LeftOutPage.NotWritten, (string)run["candidatesLeftOut"]![0]!["reason"]!);
    }

    [Fact]
    public void Once_the_page_is_written_the_judge_reads_it_and_the_section_runs_again()
    {
        using var app = FsdLinkTests.App(On);
        var before = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        var inputs = (string)State(app, before)["inputs"]!;
        RecordButton(app);

        var after = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        var state = State(app, after);
        Assert.NotEqual(inputs, (string)state["inputs"]!);
        Assert.Null(after.Warning);
        Assert.Null(state["candidatesLeftOut"]);
        var button = state["candidates"]!.AsArray().Single(c => (string)c!["page"]! == SpecApp.Button)!;
        Assert.Equal("shared component reached from page:/orders", (string)button["why"]!);
        var bundle = JudgeText(app, after);
        Assert.Contains("The button shows its label.", bundle);
        Assert.DoesNotContain("# Pages left out", bundle);
    }

    [Fact]
    public void A_group_of_sections_gets_them_too()
    {
        using var app = FsdLinkTests.App(On);
        app.Repo.Config.Fsd.GroupSections = true;
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Equal("group", (string)State(app, step)["level"]!);
        Assert.Equal(SpecApp.Button, (string)State(app, step)["candidatesLeftOut"]![0]!["page"]!);
        Assert.Contains(SpecApp.Button, JudgeText(app, step));
    }

    [Fact]
    public void Off_the_judge_reads_and_hashes_what_it_always_did()
    {
        using var app = FsdLinkTests.App();
        var index = SpecIndex.Load(app.Root);
        Assert.DoesNotContain(PagesNeeded.ForDocument(app.Pages(), index, app.Root, "shop", null)["pages"]!.AsArray(), p => (string)p!["id"]! == SpecApp.Button);
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Null(step.Warning);
        Assert.DoesNotContain("# Pages left out", JudgeText(app, step));
        var state = State(app, step);
        Assert.Null(state["candidatesLeftOut"]);
        Assert.Equal((string)state["inputs"]!, LinkRun.JudgeInputs(index.Sections[("shop", "1")], "shop", [.. state["candidates"]!.AsArray().Select(c => (string)c!["page"]!)], index, null, true));
    }
}
