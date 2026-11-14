using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>Layer 3 step 5, areas (D218–D228): a small graph and flow index laid out so each rule shows once.</summary>
public sealed class AreasTests : IDisposable
{
    const string Cfg = "web/tsconfig.json";
    readonly string root = Directory.CreateTempSubdirectory("stangent-areas-").FullName;
    readonly CodeGraph graph = new() { Commit = "abc" };
    readonly List<JsonObject> flows = [];

    public void Dispose() => Directory.Delete(root, true);

    static string Ts(string file, string member) => $"ts|web/src/{file}#{member}";

    void Node(string id, string kind, string? file, string? project = null) =>
        graph.LinkedNodes.Add(new Node(id, kind, id.Split('#').Last(), project ?? $"project:{Cfg}", file));
    void Edge(string from, string to, string kind) => graph.LinkedEdges.Add(new Edge(from, to, kind, Label.Certain));
    void Fact(string kind, string owner, string? file, params (string, string)[] data) =>
        graph.LinkedFacts.Add(new Fact("test", kind, owner, file, 1, data.ToDictionary(d => d.Item1, d => d.Item2)));

    /// <summary>A screen: a page routing to a component in <paramref name="file"/>, with its opening flow.</summary>
    string Screen(string route, string file, string component)
    {
        var page = $"page:{Cfg}|{route}";
        var id = Ts(file, component);
        Node(page, NodeKind.Page, null);
        Node(id, NodeKind.Component, $"web/src/{file}");
        Edge(page, id, EdgeKind.RoutesTo);
        Flow($"screen{route.Replace('/', '-').Replace(':', '_').Replace('*', 'x')}", "screen-opens", page, $"web/src/App.tsx:1");
        return id;
    }

    /// <summary>A click on a method of <paramref name="component"/>.</summary>
    string Click(string id, string component, string method, params string[] links)
    {
        var file = graph.LinkedNodes.First(n => n.Id == component).File!;
        var point = $"{component}.{method}";
        Node(point, NodeKind.Method, file);
        Edge(component, point, EdgeKind.Contains);
        Flow(id, "click", point, $"{file}:5", links);
        return point;
    }

    void Flow(string id, string kind, string point, string start, params string[] links) =>
        flows.Add(new JsonObject { ["id"] = id, ["kind"] = kind, ["point"] = point, ["start"] = start, ["links"] = new JsonArray([.. links.Select(l => (JsonNode)l)]) });

    void Item(string menu, string path, string name, string address) =>
        Fact("menu-item", "ts|web/src/Nav.tsx#Nav", "web/src/Nav.tsx", ("menu", menu), ("path", path), ("name", name), ("key", ""), ("address", address), ("via", "data"));

    Areas Build()
    {
        // The menu: Series; Activity › Queue; Settings › A, B. A settings page's own index of two items is no menu.
        Item("web/src/Nav.tsx:1", "1", "Series", "/series");
        Item("web/src/Nav.tsx:1", "2", "Activity", "");
        Item("web/src/Nav.tsx:1", "2.1", "Queue", "/activity/queue");
        Item("web/src/Nav.tsx:1", "3", "Settings", "");
        Item("web/src/Nav.tsx:1", "3.1", "A", "/settings/a");
        Item("web/src/Nav.tsx:1", "3.2", "B", "/settings/b");
        Item("web/src/Settings/Index.tsx:1", "1", "A", "/settings/a");
        Item("web/src/Settings/Index.tsx:1", "2", "B", "/settings/b");

        var series = Screen("/series", "Series/SeriesIndex.tsx", "SeriesIndex");
        var details = Screen("/series/:id", "Series/Details.tsx", "Details");
        var queue = Screen("/activity/queue", "Activity/Queue.tsx", "Queue");
        var a = Screen("/settings/a", "Settings/A.tsx", "A");
        var b = Screen("/settings/b", "Settings/B.tsx", "B");
        var reader = Screen("/reader", "Reader/Reader.tsx", "Reader");
        Screen("/*", "Components/NotFound.tsx", "NotFound");

        // A click in its screen's folder; one on a dialog both Series details and the queue open (a tie); one only the frame shows.
        Click("click-save", details, "save", "shared-load");
        Click("click-series", series, "refresh");
        Click("click-queue", queue, "remove", "handler-even", "shared-two");
        Click("click-read", reader, "turn", "shared-two", "job-y");
        var modal = Ts("Components/Modal.tsx", "Modal");
        Node(modal, NodeKind.Component, "web/src/Components/Modal.tsx");
        Edge(details, modal, EdgeKind.Uses);
        Edge(queue, modal, EdgeKind.Uses);
        Click("click-modal", modal, "ok", "job-y");
        var header = Ts("Components/Header.tsx", "Header");
        var app = Ts("App.tsx", "App");
        Node(header, NodeKind.Component, "web/src/Components/Header.tsx");
        Node(app, NodeKind.Component, "web/src/App.tsx");
        Edge(app, header, EdgeKind.Renders);
        Click("click-restart", header, "restart");
        // An input both settings screens show: parts of one area give that area (D227).
        var input = Ts("Components/Input.tsx", "Input");
        Node(input, NodeKind.Component, "web/src/Components/Input.tsx");
        Edge(a, input, EdgeKind.Renders);
        Edge(b, input, EdgeKind.Renders);
        Click("click-input", input, "change", "job-settings");
        // 45 clicks on settings screen A: Settings is over 40, so it splits by its menu children; A stays over 40, flagged.
        for (var i = 0; i < 45; i++) Click($"click-a{i:00}", a, $"m{i:00}");
        Click("click-b", b, "go", "job-settings");

        // Jobs: one led by Series flows through a shared flow; one by nobody (its backend folder); one led evenly by two areas.
        flows.Add(new JsonObject { ["id"] = "shared-load", ["kind"] = "shared", ["point"] = "x", ["start"] = "x:1", ["links"] = new JsonArray("job-refresh") });
        flows.Add(new JsonObject { ["id"] = "shared-two", ["kind"] = "shared", ["point"] = "y", ["start"] = "y:1", ["links"] = new JsonArray() });
        // job-y: led by two Series clicks and the reader's, so Series by majority, until the dialog's tie is answered Activity (D231).
        foreach (var (id, file) in new[] { ("job-refresh", "src/Core/Tv/Refresh.cs"), ("job-lonely", "src/Core/Jobs/Lonely.cs"), ("handler-even", "src/Core/Events/Even.cs"), ("job-settings", "src/Core/Config/Save.cs"), ("job-y", "src/Core/Sync/Y.cs") })
        {
            var point = $"Core|M:Core.{id}.Run";
            Node(point, NodeKind.Method, file, "project:src/Core/Core.csproj");
            Flow(id, id.StartsWith("job", StringComparison.Ordinal) ? "job" : "handler", point, $"{file}:3");
        }
        flows.First(f => (string)f["id"]! == "click-series")!["links"] = new JsonArray("handler-even", "shared-load", "job-y");
        flows.First(f => (string)f["id"]! == "click-save")!["links"] = new JsonArray("shared-load", "job-y");

        Directory.CreateDirectory(Path.Combine(root, FlowFiles.Folder));
        File.WriteAllText(Path.Combine(root, FlowFiles.Folder, "index.json"),
            new JsonObject { ["commit"] = "abc", ["flows"] = new JsonArray([.. flows.Select(f => (JsonNode)f.DeepClone())]) }.ToJsonString());
        return Areas.Load(graph, root);
    }

    [Fact]
    public void Areas_come_from_the_menu_and_folders_split_over_40_and_place_clicks_jobs_and_ties_by_the_rules()
    {
        var areas = Build();
        Assert.Equal(["web/src/Nav.tsx:1"], areas.KeptMenus);   // the two-item settings index isn't a menu (D226)
        var (file, placement) = AreaFiles.Propose(areas);
        Assert.Equal(["Series", "Activity", "Settings", "Settings › A", "Settings › B", "Reader", "Core › Jobs", Areas.FrameName], file.Areas.Select(a => a.Name));
        var series = file.Areas.Single(a => a.Name == "Series");
        Assert.Equal(("/series", "web/src/Series"), (string.Join(",", series.Menu), string.Join(",", series.Folders)));
        Assert.Equal("Series", placement.AreaOf["click-save"]);            // its own folder is its screen's home
        Assert.Equal("Series", placement.AreaOf["screen-series-_id"]);     // no menu item: joins the menu area holding its home's screen (D221)
        Assert.Equal("Reader", placement.AreaOf["click-read"]);            // no menu reaches it: its folder is an area
        Assert.Equal(Areas.FrameName, placement.AreaOf["click-restart"]);  // only the app's frame shows it (D222)
        Assert.Equal(Areas.FrameName, placement.AreaOf["screen-x"]);       // the catch-all route makes no home
        Assert.Equal("Settings", placement.AreaOf["click-input"]);         // two parts of Settings (D227)
        Assert.Equal("Settings › A", placement.AreaOf["click-a00"]);
        Assert.Equal("Series", placement.AreaOf["job-refresh"]);           // through a shared flow, all its leaders in Series (D223)
        Assert.Equal("Settings", placement.AreaOf["job-settings"]);        // its leaders in two parts of Settings (D227)
        Assert.Equal("Core › Jobs", placement.AreaOf["job-lonely"]);       // no leaders: its backend folder
        Assert.Equal(["Series", "Activity"], file.Ties.Single(t => t.Flow == "click-modal").Choose);
        Assert.Equal(["Series", "Activity"], file.Ties.Single(t => t.Flow == "handler-even").Choose);
        Assert.Equal([new Large("Settings › A", 46, "over 40, nothing to split by")], file.Large);
    }

    [Fact]
    public void The_yaml_reads_back_as_written_and_check_wants_every_tie_answered()
    {
        var areas = Build();
        var (file, placement) = AreaFiles.Propose(areas);
        var text = AreaFiles.Render(file, areas.Commit, placement);
        var again = AreaFiles.Propose(Areas.Load(graph, root));
        Assert.Equal(text, AreaFiles.Render(again.File, areas.Commit, again.Placement));   // the same bytes twice
        var back = AreaFiles.Parse(text);
        Assert.Equal(file.Areas.Select(a => (a.Name, string.Join(",", a.Menu), string.Join(",", a.Folders))), back.Areas.Select(a => (a.Name, string.Join(",", a.Menu), string.Join(",", a.Folders))));
        var (report, _) = AreaFiles.Check(areas, back);
        Assert.False((bool)report["ok"]!);
        Assert.Equal(2, report["problems"]!.AsArray().Count(p => ((string)p!).StartsWith("tie unanswered", StringComparison.Ordinal)));

        // Answered, merged and renamed by a person: the check passes and areas.json holds every start flow and its shared flows.
        var answered = AreaFiles.Parse(text.Replace("answer: ''", "answer: 'Series'").Replace("'Reader'", "'Reading'"));
        // D230: until flows build has put the files where the file says, the check says to run it.
        Assert.Contains(AreaFiles.Check(areas, answered).Report["problems"]!.AsArray(), p => ((string)p!).EndsWith("run `stangent flows build`", StringComparison.Ordinal));
        areas = Built(AreaLayout.ByAreas(areas, answered));
        var (ok, json) = AreaFiles.Check(areas, answered);
        Assert.True((bool)ok["ok"]!, ok.ToJsonString());
        Assert.Equal("Reading", (string)json["flows"]!["click-read"]!);
        Assert.Equal("Series", (string)json["flows"]!["click-modal"]!);
        var seriesArea = json["areas"]!.AsArray().Single(a => (string)a!["name"]! == "Series")!;
        Assert.Contains("shared-load", seriesArea["shared"]!.AsArray().Select(x => (string)x!));
    }

    [Fact]
    public void Check_reports_flows_no_area_holds_two_areas_holding_one_folder_and_answers_whose_flow_is_gone()
    {
        var areas = Build();
        var file = AreaFiles.Parse("""
            areas:
            - name: Series
              menu: ['/series']
              folders: ['web/src/Series']
            - name: Also series
              folders: ['web/src/Series']
            ties:
            - flow: click-gone
              choose: ['Series']
              answer: 'Series'
            """);
        var problems = AreaFiles.Check(areas, file).Report["problems"]!.AsArray().Select(p => (string)p!).ToList();
        Assert.Contains("folder 'web/src/Series' is in 'Series' and 'Also series'", problems);
        Assert.Contains("start flow 'click-read' is in no area", problems);
        Assert.Contains("tie answered for 'click-gone', which is no longer a start flow", problems);
        Assert.Contains("menu item '/activity/queue' is in no area", problems);
        Assert.Contains("folder 'web/src/Reader' is in no area", problems);
    }

    /// <summary>The index as <c>flows build</c> would leave it for a layout: each flow's <c>file</c> where the layout puts it.</summary>
    Areas Built(AreaLayout layout)
    {
        File.WriteAllText(Path.Combine(root, FlowFiles.IndexFile), new JsonObject
        {
            ["commit"] = "abc",
            ["flows"] = new JsonArray([.. flows.Select(f => { var c = f.DeepClone().AsObject(); c["file"] = layout.File((string)f["id"]!); return (JsonNode)c; })]),
        }.ToJsonString());
        return Areas.Load(graph, root);
    }

    static AreasFile Answered(AreasFile file, params (string Flow, string Answer)[] answers) => new()
    {
        Areas = file.Areas, Large = file.Large,
        Ties = [.. file.Ties.Select(t => answers.FirstOrDefault(a => a.Flow == t.Flow) is { Flow: not null } a ? t with { Answer = a.Answer } : t)],
    };

    [Fact]
    public void Start_flows_sit_under_their_area_shared_flows_with_their_one_area_or_in_shared_and_ties_in_unplaced()
    {
        var areas = Build();
        var (file, _) = AreaFiles.Propose(areas);
        var layout = AreaLayout.ByAreas(areas, file);
        Assert.Equal("specs/areas/series", layout.FolderOf["click-save"]);
        Assert.Equal("specs/areas/settings-a", layout.FolderOf["click-a00"]);
        Assert.Equal("specs/areas/app-frame", layout.FolderOf["click-restart"]);
        Assert.Equal("specs/areas/core-jobs", layout.FolderOf["job-lonely"]);
        Assert.Equal("specs/areas/series/click-save.json", layout.File("click-save"));
        // A tie still open: in unplaced/, and said so.
        Assert.Equal("specs/areas/unplaced", layout.FolderOf["click-modal"]);
        Assert.Equal(["click-modal", "handler-even"], layout.NotPlaced);
        // Only Series' starts reach shared-load: it sits with them. Activity's and Reader's both reach shared-two: shared/.
        Assert.Equal("specs/areas/series", layout.FolderOf["shared-load"]);
        Assert.Equal("specs/areas/shared", layout.FolderOf["shared-two"]);
        // The same areas give the same layout.
        Assert.Equal(layout.FolderOf, AreaLayout.ByAreas(Areas.Load(graph, root), AreaFiles.Parse(AreaFiles.Render(file, "abc", layout.Placement!))).FolderOf);
    }

    [Fact]
    public void An_areas_folder_is_its_name_as_a_slug_and_two_names_never_share_one()
    {
        var slugs = AreaLayout.Slugs(["Settings › Indexers", "settings indexers", "Shared", "Unplaced", "日本", "Admin › Catalog › Products"]);
        Assert.Equal("settings-indexers", slugs["Settings › Indexers"]);   // ordinal first keeps the bare slug
        Assert.Equal("settings-indexers-2", slugs["settings indexers"]);
        Assert.Equal("shared-2", slugs["Shared"]);                          // shared/ and unplaced/ are never an area's
        Assert.Equal("unplaced-2", slugs["Unplaced"]);
        Assert.Equal("area", slugs["日本"]);
        Assert.Equal("admin-catalog-products", slugs["Admin › Catalog › Products"]);
        Assert.Equal("../shared/x.md", AreaLayout.Relative("specs/areas/series", "specs/areas/shared/x.md"));
        Assert.Equal("x.md", AreaLayout.Relative("specs/flows", "specs/flows/x.md"));
    }

    /// <summary>A person's words added under the counts, between layer 4's two lines (D313(3): a page without words has neither).</summary>
    static string WithWords(string page) =>
        page.Replace("\n\n## Screens", $"\n\n{AreaPages.WordsStart}\nSeries is where people add shows.\n{AreaPages.WordsEnd}\n\n## Screens", StringComparison.Ordinal);

    [Fact]
    public void Each_area_has_a_page_of_links_grouped_by_kind_that_keeps_layer_4s_words_and_goes_when_its_area_does()
    {
        var areas = Build();
        var (proposed, _) = AreaFiles.Propose(areas);
        var file = Answered(proposed, ("click-modal", "Activity"), ("handler-even", "Series"));
        var layout = AreaLayout.ByAreas(areas, file);
        areas = Built(layout);
        var (report, json) = AreaFiles.Check(areas, file, layout);
        Assert.Equal("specs/areas/series", (string)json["areas"]!.AsArray().Single(a => (string)a!["name"]! == "Series")!["folder"]!);

        AreaPages.Write(root, areas, layout);
        var series = File.ReadAllText(Path.Combine(root, "specs/areas/series/page.md"));
        Assert.StartsWith(AreaPages.Marker, series, StringComparison.Ordinal);
        Assert.Contains("# Series\n", series, StringComparison.Ordinal);
        Assert.Contains("- **Start flows:** 6 (2 screens, 2 clicks, 1 job, 1 handler)\n", series, StringComparison.Ordinal);
        Assert.Contains("- **Shared flows they reach:** 1 (1 in this folder)\n", series, StringComparison.Ordinal);
        Assert.Contains("\n## Screens (2)\n", series, StringComparison.Ordinal);
        Assert.Contains("- [click-save](click-save.md) `click-save`\n", series, StringComparison.Ordinal);
        Assert.Contains("- [shared-load](shared-load.md) `shared-load`\n", series, StringComparison.Ordinal);
        var activity = File.ReadAllText(Path.Combine(root, "specs/areas/activity/page.md"));
        Assert.Contains("- [shared-two](../shared/shared-two.md) `shared-two`\n", activity, StringComparison.Ordinal);
        // D313(3): with no words, neither of layer 4's lines is written, and a rerun gives the same bytes.
        Assert.DoesNotContain(AreaPages.WordsStart, series, StringComparison.Ordinal);
        Assert.DoesNotContain(AreaPages.WordsEnd, series, StringComparison.Ordinal);
        Assert.Contains("(1 in this folder)\n\n## Screens (2)\n", series, StringComparison.Ordinal);
        Assert.Equal(layout.SlugOf.Count, AreaPages.Write(root, areas, layout).Unchanged);

        // Layer 4's words between the two lines are kept on a rewrite; the rest is rebuilt to the same bytes.
        var withWords = WithWords(series);
        File.WriteAllText(Path.Combine(root, "specs/areas/series/page.md"), withWords);
        var (_, _, unchanged, _) = AreaPages.Write(root, areas, layout);
        Assert.Equal(layout.SlugOf.Count, unchanged);
        Assert.Equal(withWords, File.ReadAllText(Path.Combine(root, "specs/areas/series/page.md")));

        // A rename: the old page goes, unless it holds layer 4's words, which are kept and reported.
        var renamed = AreaFiles.Parse(AreaFiles.Render(file, "abc", layout.Placement!).Replace("'Series'", "'Shows'").Replace("'Reader'", "'Reading'"));
        var after = AreaLayout.ByAreas(areas, renamed);
        var (_, removed, _, kept) = AreaPages.Write(root, areas, after);
        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(Path.Combine(root, "specs/areas/reader")));
        Assert.Equal(["specs/areas/series/page.md"], kept);
        Assert.True(File.Exists(Path.Combine(root, "specs/areas/shows/page.md")));
    }

    [Fact]
    public void Areas_check_writes_the_developer_page_with_the_switch_on_and_drops_it_but_keeps_the_words_with_it_off()
    {
        var areas = Build();
        var (proposed, _) = AreaFiles.Propose(areas);
        var file = Answered(proposed, ("click-modal", "Activity"), ("handler-even", "Series"));
        var layout = AreaLayout.ByAreas(areas, file);
        areas = Built(layout);
        var page = Path.Combine(root, "specs/areas/series/page.md");

        // On: the charts function `areas check` uses (D285) writes the note and the developer page, ended by layer 4's end line;
        // D313(3): the empty "words go below" line is left out, and a rerun gives the same bytes.
        var charts = PageChart.Pages(root, areas, layout, new PageSettings());
        AreaPages.Write(root, areas, layout, charts);
        var on = File.ReadAllText(page);
        Assert.StartsWith("---\narea: \"Series\"\nmadeBy: code, no AI\n", on, StringComparison.Ordinal);
        Assert.DoesNotContain(AreaPages.WordsStart, on, StringComparison.Ordinal);
        Assert.Contains("\n\n" + PageChart.BodyMarker + "\n", on, StringComparison.Ordinal);
        Assert.True(on.IndexOf(AreaPages.WordsEnd, StringComparison.Ordinal) < on.IndexOf("## Screens", StringComparison.Ordinal));
        Assert.Equal(layout.SlugOf.Count, AreaPages.Write(root, areas, layout, charts).Unchanged);
        Assert.Contains("\n## Screens (2)\n", on, StringComparison.Ordinal);

        // D314(1): a split area lists its parts, linked, with their start flows by readable name; each part links back, with its siblings.
        var settings = File.ReadAllText(Path.Combine(root, "specs/areas/settings/page.md"));
        var settingsA = File.ReadAllText(Path.Combine(root, "specs/areas/settings-a/page.md"));
        Assert.Contains("\n## Parts of this area\n\n- [Settings › A](../settings-a/page.md): ", settings, StringComparison.Ordinal);
        Assert.Contains("\n- [Settings › B](../settings-b/page.md): ", settings, StringComparison.Ordinal);
        Assert.Contains("\n**Part of [Settings](../settings/page.md).** Its other parts: [Settings › B](../settings-b/page.md).\n", settingsA, StringComparison.Ordinal);
        Assert.DoesNotContain("Part of", on, StringComparison.Ordinal);
        Assert.DoesNotContain("## Parts of this area", on, StringComparison.Ordinal);

        // Off: the developer page and its note go; the links stay.
        AreaPages.Write(root, areas, layout);
        var off = File.ReadAllText(page);
        Assert.StartsWith(AreaPages.Marker, off, StringComparison.Ordinal);
        Assert.Equal("", AreaPages.Words(off));
        Assert.DoesNotContain("madeBy", off, StringComparison.Ordinal);
        Assert.Contains("\n## Screens (2)\n", off, StringComparison.Ordinal);

        // Words a person wrote are kept with the switch off.
        var withWords = WithWords(off);
        File.WriteAllText(page, withWords);
        AreaPages.Write(root, areas, layout);
        Assert.Equal(withWords, File.ReadAllText(page));
    }

    [Fact]
    public void A_proposal_builds_on_a_confirmed_file_keeping_its_names_and_answers_and_adding_what_answering_left_uncovered()
    {
        var areas = Build();
        var (proposed, _) = AreaFiles.Propose(areas);
        Assert.DoesNotContain(proposed.Areas, a => a.Folders.Contains("src/Core/Sync"));   // job-y sits in Series by majority
        var renamed = AreaFiles.Parse(AreaFiles.Render(proposed, "abc", areas.Place(proposed)).Replace("'Reader'", "'Reading'"));
        // Answering the dialog Activity takes job-y's majority away (D231): the confirmed file leaves it in no area.
        var confirmed = Answered(renamed, ("click-modal", "Activity"), ("handler-even", "Series"));
        confirmed.Ties.Add(new Tie("click-gone", ["Series"], "Series"));
        Assert.Contains("start flow 'job-y' is in no area", AreaFiles.Check(areas, confirmed).Report["problems"]!.AsArray().Select(p => (string)p!));

        var (file, placement, added, dropped) = AreaFiles.Extend(areas, confirmed);
        Assert.Equal([.. renamed.Areas.Select(a => a.Name), "Core › Sync"], file.Areas.Select(a => a.Name));   // names kept, the backend folder added
        Assert.Equal(["area 'Core › Sync'", "folder 'src/Core/Sync' to 'Core › Sync'"], added);
        Assert.Equal([("click-modal", "Activity"), ("handler-even", "Series")], file.Ties.Select(t => (t.Flow, t.Answer)));
        Assert.Equal(["'click-gone': no longer a start flow"], dropped);
        Assert.Equal("Core › Sync", placement.AreaOf["job-y"]);
        var problems = AreaFiles.Check(areas, file).Report["problems"]!.AsArray().Select(p => (string)p!).Where(p => !p.EndsWith("flows build`", StringComparison.Ordinal));
        Assert.Empty(problems);
        // Built on itself, nothing more is added: the same file.
        var again = AreaFiles.Extend(areas, file);
        Assert.Empty(again.Added);
        Assert.Equal(AreaFiles.Render(file, "abc", placement, true), AreaFiles.Render(again.File, "abc", again.Placement, true));
    }

    /// <summary>
    /// D312's graph: Series over 40 with no menu children, its home holding Index/, Details/ and a one-click Edit/; two dialogs in a shared
    /// folder (one on Details and the queue, one on Details, Index and the queue); jobs led by one part, by two parts, by the parent, and a
    /// handler led only by a job.
    /// </summary>
    Areas BuildSplit()
    {
        Item("web/src/Nav.tsx:1", "1", "Series", "/series");
        Item("web/src/Nav.tsx:1", "2", "Activity", "/activity");
        Item("web/src/Nav.tsx:1", "3", "Other", "/other");
        var index = Screen("/series", "Series/Index/SeriesIndex.tsx", "SeriesIndex");
        var details = Screen("/series/:id", "Series/Details/Details.tsx", "Details");
        var queue = Screen("/activity", "Activity/Queue.tsx", "Queue");
        Screen("/other", "Other/Other.tsx", "Other");
        void Component(string id, string file, params string[] usedBy)
        {
            Node(id, NodeKind.Component, $"web/src/{file}");
            foreach (var u in usedBy) Edge(u, id, EdgeKind.Uses);
        }
        var edit = Ts("Series/Edit/EditModal.tsx", "EditModal");
        var toolbar = Ts("Series/Toolbar.tsx", "Toolbar");
        var modal1 = Ts("Components/Modal1.tsx", "Modal1");
        var modal2 = Ts("Components/Modal2.tsx", "Modal2");
        Component(edit, "Series/Edit/EditModal.tsx", details);
        Component(toolbar, "Series/Toolbar.tsx", index);
        Component(modal1, "Components/Modal1.tsx", details, queue);
        Component(modal2, "Components/Modal2.tsx", details, index, queue);
        for (var i = 0; i < 20; i++) Click($"click-i{i:00}", index, $"i{i:00}", i == 0 ? ["job-shared"] : []);
        for (var i = 0; i < 20; i++) Click($"click-d{i:00}", details, $"d{i:00}", i == 0 ? ["job-refresh", "job-shared"] : []);
        Click("click-e0", edit, "save", "job-parent");   // Edit/ holds one click: under 3, it stays with Series
        Click("click-t0", toolbar, "a", "job-parent");
        Click("click-t1", toolbar, "b", "job-parent");
        Click("click-m1", modal1, "ok");
        Click("click-m2", modal2, "ok");
        foreach (var (id, file, links) in new[] { ("job-refresh", "src/Core/Tv/Refresh.cs", ""), ("job-shared", "src/Core/Tv/Shared.cs", "handler-chain"),
                     ("job-parent", "src/Core/Tv/Parent.cs", ""), ("handler-chain", "src/Core/Events/Chain.cs", "") })
        {
            var point = $"Core|M:Core.{id}.Run";
            Node(point, NodeKind.Method, file, "project:src/Core/Core.csproj");
            Flow(id, id.StartsWith("job", StringComparison.Ordinal) ? "job" : "handler", point, $"{file}:3", links == "" ? [] : [links]);
        }
        Directory.CreateDirectory(Path.Combine(root, FlowFiles.Folder));
        File.WriteAllText(Path.Combine(root, FlowFiles.Folder, "index.json"),
            new JsonObject { ["commit"] = "abc", ["flows"] = new JsonArray([.. flows.Select(f => (JsonNode)f.DeepClone())]) }.ToJsonString());
        return Areas.Load(graph, root);
    }

    [Fact]
    public void An_area_over_40_with_no_menu_children_splits_by_its_subfolders_shared_jobs_going_to_background_and_answers_following_the_split()
    {
        var areas = BuildSplit();
        var confirmed = new AreasFile
        {
            Areas =
            [
                new AreaSpec { Name = "Series", Menu = ["/series"], Folders = ["web/src/Series"] },
                new AreaSpec { Name = "Activity", Menu = ["/activity"], Folders = ["web/src/Activity"] },
                new AreaSpec { Name = "Other", Menu = ["/other"], Folders = ["web/src/Other"] },
            ],
            Ties = [new Tie("click-m1", ["Series", "Activity"], "Series"), new Tie("click-m2", ["Series", "Activity"], "Series")],
        };
        Assert.Equal(51, areas.Place(confirmed).AreaOf.Values.Count(v => v == "Series"));   // over 40, nothing in the menu to split by

        var (file, placement, added, dropped) = AreaFiles.Extend(areas, confirmed);
        // Index/ and Details/ become parts after the parent; Edit/ (one click) stays with it; Background last.
        Assert.Equal(["Series", "Series › Details", "Series › Index", "Series › Background", "Activity", "Other"], file.Areas.Select(a => a.Name));
        Assert.Equal(("/series", "web/src/Series"), (string.Join(",", file.Areas[0].Menu), string.Join(",", file.Areas[0].Folders)));   // the parent keeps its address and folder
        Assert.Equal(["web/src/Series/Details"], file.Areas[1].Folders);
        Assert.Equal("Series › Index", placement.AreaOf["click-i05"]);
        Assert.Equal("Series › Details", placement.AreaOf["click-d05"]);
        Assert.Equal("Series", placement.AreaOf["click-e0"]);             // its small subfolder stays with the parent
        Assert.Equal("Series", placement.AreaOf["click-t0"]);             // directly in the parent folder
        Assert.Equal("Series › Index", placement.AreaOf["screen-series"]); // its screen's folder is deeper than the parent's (D225)
        // Condition 1: a job goes with the click that sets it off; one its parts share goes to Background, never the first part.
        Assert.Equal("Series › Details", placement.AreaOf["job-refresh"]);
        Assert.Equal("Series › Background", placement.AreaOf["job-shared"]);
        Assert.Equal("Series › Background", placement.AreaOf["handler-chain"]);   // led only by a Background job
        Assert.Equal("Series", placement.AreaOf["job-parent"]);                   // the parent's own clicks hold most of its leaders
        // D234 + D312: an answer naming the split area moves to its one part among the choices; with two parts it is asked again.
        Assert.Equal([("click-m1", "Series › Details"), ("click-m2", "")], file.Ties.Select(t => (t.Flow, t.Answer)));
        Assert.Equal(["Series › Details", "Series › Index", "Activity"], file.Ties[1].Choose);
        Assert.Contains("answer for 'click-m1' moved from 'Series' to its part 'Series › Details'", added);
        Assert.Contains(added, x => x.StartsWith("split 'Series': 'Series › Background'", StringComparison.Ordinal));
        Assert.Equal(["'click-m2': 'Series' is no longer one of its choices ('Series › Details', 'Series › Index', 'Activity')"], dropped);
        Assert.Empty(file.Large);

        // areas check reads the same from the written file, and proposing again on it splits nothing more.
        var back = AreaFiles.Parse(AreaFiles.Render(file, "abc", placement, true));
        Assert.Equal(placement.AreaOf, areas.Place(back).AreaOf);
        var again = AreaFiles.Extend(areas, back);
        Assert.DoesNotContain(again.Added, x => x.StartsWith("split", StringComparison.Ordinal));
        Assert.Equal(AreaFiles.Render(file, "abc", placement, true), AreaFiles.Render(again.File, "abc", again.Placement, true));
    }

    [Fact]
    public void Answers_still_a_choice_are_kept_through_a_split_and_folder_names_read_as_words()
    {
        var areas = BuildSplit();
        var confirmed = new AreasFile
        {
            Areas = [new AreaSpec { Name = "Series", Menu = ["/series"], Folders = ["web/src/Series"] }, new AreaSpec { Name = "Activity", Menu = ["/activity"], Folders = ["web/src/Activity"] }, new AreaSpec { Name = "Other", Menu = ["/other"], Folders = ["web/src/Other"] }],
            Ties = [new Tie("click-m1", ["Series", "Activity"], "Activity"), new Tie("click-m2", ["Series", "Activity"], "Activity")],
        };
        // Answered Activity, still one of the choices after the split: kept (D234).
        var (file, placement, _, dropped) = AreaFiles.Extend(areas, confirmed);
        Assert.Empty(dropped);
        Assert.Equal([("click-m1", "Activity"), ("click-m2", "Activity")], file.Ties.Select(t => (t.Flow, t.Answer)));
        Assert.Equal("Activity", placement.AreaOf["click-m2"]);
        Assert.Equal("Monitoring Options", AreaFiles.Readable("MonitoringOptions"));
        Assert.Equal("Manage Users", AreaFiles.Readable("manage-users"));
        Assert.Equal("Modals", AreaFiles.Readable("_modals"));
    }

    [Fact]
    public void Without_a_flow_index_areas_say_to_build_flows_first()
    {
        var e = Assert.Throws<StangentException>(() => Areas.Load(graph, root));
        Assert.Equal("not_found", e.Code);
    }
}
