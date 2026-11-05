using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Loading;
using Stangent.Spec;
using Stangent.Spec.Pages;
using Xunit;

namespace Stangent.Core.Tests;

/// <summary>Screens a React app's root shows with no route (root-screens, D127): the extractor's own cases are in its tests; these hold the path through to the page list.</summary>
public class RootScreenTests
{
    static string App()
    {
        var root = Directory.CreateTempSubdirectory("stangent-root-screens-").FullName;
        void Write(string name, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name))!);
            File.WriteAllText(Path.Combine(root, name), text);
        }
        Write("package.json", """{ "name": "shop-ui" }""");
        Write("tsconfig.json", """{ "compilerOptions": { "jsx": "preserve", "allowJs": true, "module": "esnext", "moduleResolution": "bundler" }, "include": ["src"] }""");
        Write("src/index.tsx", """import { createRoot } from "react-dom/client"; import App from "./App"; createRoot(document.body).render(<App />);""");
        Write("src/App.tsx", """import { Routes, Route } from "react-router-dom"; import Door from "./Door"; import Tasks from "./Tasks"; export default function App({ who }: any) { return who ? <Routes><Route path="/tasks" element={<Tasks />} /></Routes> : <Door />; }""");
        Write("src/Door.tsx", """import Help from "./Help"; export default function Door() { return <Help />; }""");
        Write("src/Help.tsx", """export default function Help() { return <p>help</p>; }""");
        Write("src/Tasks.tsx", """export default function Tasks() { return <p>tasks</p>; }""");
        return root;
    }

    static PageSet Pages(string root, bool on)
    {
        var project = FrontendScanner.Find(root).Single();
        var (fragment, _, error) = FrontendScanner.Extract(root, project, "print", new Dictionary<string, Dictionary<string, List<string>>>(), rootScreens: on);
        Assert.Null(error);
        var graph = new CodeGraph { Commit = "c" };
        graph.Fragments[project.Id] = fragment;
        return PageList.Build(graph, root, SpecSettings.From(new StangentConfig()));
    }

    [Fact]
    public void Off_the_page_list_has_only_routes()
    {
        Assert.Equal(["page:/tasks"], Pages(App(), false).Pages.Where(p => p.Kind == PageKind.Screen).Select(p => p.Id));
    }

    [Fact]
    public void On_the_root_screen_has_a_labelled_page_holding_what_it_renders()
    {
        var set = Pages(App(), true);
        Assert.Equal(["page:(no route) Door", "page:/tasks"], set.Pages.Where(p => p.Kind == PageKind.Screen).Select(p => p.Id));
        var page = set.Find("page:(no route) Door")!;
        Assert.Equal("(no route) Door · Door", page.Title);
        Assert.Contains(set.Slices[page.Id].Nodes, n => n.Node.Id == "ts|src/Help.tsx#Help");
    }

    [Fact]
    public void The_switch_is_off_until_a_repo_turns_it_on()
    {
        Assert.False(StangentComponents.IsOn(new StangentConfig(), "root-screens"));
        Assert.True(StangentComponents.IsOn(new StangentConfig { Components = { ["root-screens"] = true } }, "root-screens"));
    }
}
