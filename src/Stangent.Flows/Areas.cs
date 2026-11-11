using System.Text.Json;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Detectors;

namespace Stangent.Flows;

/// <summary>A start flow as <c>specs/flows/index.json</c> lists it: what areas read (D218).</summary>
public sealed record IndexFlow(string Id, string Kind, string Point, string Start, IReadOnlyList<string> Links);

/// <summary>
/// Where a start flow sits, for <c>areas.yaml</c> to place it (D225): the menu item address its screen matched (null when none) and a
/// folder. <see cref="Frame"/>: a click only the app's frame shows (D222).
/// </summary>
public sealed record Place(string? Address, string Folder, bool Frame = false);

/// <summary>A screen (D219): a routed component or an MVC action that shows a page, with its home folder and the menu item it matched.</summary>
public sealed record Screen(string Id, string? Target, string Folder, string Home, string HomeName, bool CatchAll, string Frontend)
{
    public MenuItemInfo? Item { get; set; }
}

/// <summary>One kept menu item (D221, D226): its menu, place, display name, address, children.</summary>
public sealed class MenuItemInfo
{
    public required string Menu { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required int Rank { get; init; }
    public MenuItemInfo? Parent { get; set; }
    public List<MenuItemInfo> Children { get; } = [];
    public List<string> Screens { get; } = [];
    public int Depth => Path.Count(c => c == '.');
    /// <summary>The MVC area its addresses lead into (nopCommerce's Admin), to name its area <c>Admin › Catalog</c>.</summary>
    public string AppArea { get; set; } = "";
    public IEnumerable<MenuItemInfo> Subtree() => Children.SelectMany(c => c.Subtree()).Prepend(this);
}

/// <summary>What a start flow is, for placing it: its places, or for a job or handler the flows leading to it and its backend folder.</summary>
/// <summary><see cref="Backend"/>: the folder of the class a job runs in (an interface's, its one implementation's); <see cref="BackendFolder"/> and
/// <see cref="BackendName"/>: D223's first folder below its project, and that folder's name.</summary>
public sealed record StartFlow(string Id, string Kind, IReadOnlyList<Place> Places, bool Any, string? Backend, string? BackendFolder, string? BackendName, string OwnFolder);

/// <summary>An area as <c>areas.yaml</c> describes it (D225).</summary>
public sealed class AreaSpec
{
    public required string Name { get; set; }
    public List<string> Menu { get; init; } = [];
    public List<string> Folders { get; init; } = [];
}

public sealed record Tie(string Flow, IReadOnlyList<string> Choose, string Answer);
public sealed record Large(string Area, int Flows, string Why);

/// <summary>The whole <c>areas.yaml</c> (D225).</summary>
public sealed class AreasFile
{
    public List<AreaSpec> Areas { get; init; } = [];
    public List<Large> Large { get; init; } = [];
    public List<Tie> Ties { get; init; } = [];
}

/// <summary>Every start flow placed (D218): its area, or a tie still open, or no area at all.</summary>
public sealed class Placement
{
    public SortedDictionary<string, string> AreaOf { get; } = new(StringComparer.Ordinal);
    /// <summary>Flows two or more areas could hold, with the areas, in <c>areas.yaml</c>'s order.</summary>
    public SortedDictionary<string, List<string>> Open { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Uncovered { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Areas (layer 3 step 5, D218–D226): code reads the screens, the app's menu and the flows, proposes <c>areas.yaml</c> and checks it.
/// No AI (D156); the same graph and flows give the same bytes.
/// </summary>
public sealed class Areas
{
    public const int Big = 40;
    public const string FrameName = "App frame";
    static readonly StringComparer O = StringComparer.Ordinal;

    readonly FlowGraph g;
    public string Commit { get; }
    public IReadOnlyDictionary<string, IndexFlow> Flows { get; }
    public Dictionary<string, Screen> Screens { get; } = new(StringComparer.Ordinal);
    /// <summary>Screen targets (routed components, views an MVC screen shows, the actions themselves) → their screens.</summary>
    readonly Dictionary<string, List<Screen>> byTarget = new(StringComparer.Ordinal);
    /// <summary>Home folder path → its display name (D219).</summary>
    public SortedDictionary<string, string> Homes { get; } = new(StringComparer.Ordinal);
    public List<MenuItemInfo> TopItems { get; } = [];
    public List<string> KeptMenus { get; } = [];
    public SortedDictionary<string, StartFlow> Starts { get; } = new(StringComparer.Ordinal);
    readonly Dictionary<string, (string Root, string Name)> frontends = new(StringComparer.Ordinal);

    Areas(CodeGraph graph, string root, string commit, IReadOnlyDictionary<string, IndexFlow> flows)
    {
        g = new FlowGraph(graph, root);
        Commit = commit;
        Flows = flows;
        ReadScreens();
        ReadMenus(new Translations(root, resources: true));
        ReadStarts();
    }

    /// <summary>Reads <c>specs/flows/index.json</c> (contract <c>flows</c> v1) and lays areas' facts over the graph.</summary>
    public static Areas Load(CodeGraph graph, string root)
    {
        var path = Path.Combine(root, FlowFiles.Folder, "index.json");
        if (!File.Exists(path)) throw new StangentException("not_found", $"No {FlowFiles.Folder}/index.json: run `stangent flows build` first.", ExitCodes.NotFound);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var flows = new SortedDictionary<string, IndexFlow>(StringComparer.Ordinal);
        foreach (var f in doc.RootElement.GetProperty("flows").EnumerateArray())
        {
            var id = f.GetProperty("id").GetString()!;
            flows[id] = new IndexFlow(id, f.GetProperty("kind").GetString()!, f.GetProperty("point").GetString()!, f.GetProperty("start").GetString()!,
                [.. f.GetProperty("links").EnumerateArray().Select(l => l.GetString()!)]);
        }
        return new Areas(graph, root, doc.RootElement.GetProperty("commit").GetString() ?? "", flows);
    }

    // ---- Screens and homes (D219) ----------------------------------------------------------------------------------------------

    static string Dir(string file) => file.Contains('/') ? file[..file.LastIndexOf('/')] : "";
    static string Base(string path) => path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
    static bool Under(string path, string folder) => folder == "" || path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);

    string ProjectDir(string? project) =>
        project is not null && project.StartsWith("project:", StringComparison.Ordinal) && project.EndsWith(".csproj", StringComparison.Ordinal) ? Dir(project["project:".Length..]) : "";

    void ReadScreens()
    {
        // Frontends: every page's routed components; the deepest folder holding all one frontend's screens is its root.
        var pages = g.Nodes.Values.Where(n => n.Kind == NodeKind.Page).Select(n => n.Id).Order(O).ToList();
        var tsScreens = new List<(string Page, string Config, string? Target, string File, bool CatchAll)>();
        foreach (var p in pages)
        {
            var bar = p.IndexOf('|');
            var config = p["page:".Length..bar];
            var route = p[(bar + 1)..];
            var last = route.TrimEnd('/').Split('/')[^1];
            var catchAll = last is "*" or "**";
            var target = g.Out(p, EdgeKind.RoutesTo).Select(e => e.Target).Where(t => g.Nodes.TryGetValue(t, out var n) && n.File is not null).Order(O).FirstOrDefault();
            if (target is null) continue;
            tsScreens.Add((p, config, target, g.Nodes[target].File!, catchAll));
        }
        foreach (var grp in tsScreens.GroupBy(s => s.Config).OrderBy(x => x.Key, O))
        {
            var dirs = grp.Select(s => Dir(s.File)).Distinct(O).ToList();
            var rootDir = dirs.Count == 1 ? dirs[0] : Common(dirs);
            frontends[grp.Key] = (rootDir, Base(rootDir) is { Length: > 0 } b ? b : grp.Key);
            foreach (var (page, config, target, file, catchAll) in grp)
            {
                var home = TsHome(rootDir, file);
                Add(new Screen(page, target, Dir(file), catchAll ? "" : home, catchAll ? "" : HomeNameTs(rootDir, home), catchAll, config));
            }
        }
        // MVC: every action that shows a page when requested (D171's on-load facts), with the views it renders.
        var actions = g.Graph.AllFacts.Where(f => f.Kind == "on-load" && f["framework"] == "mvc").Select(f => f["handler"]).Where(h => h != "").Distinct(O).Order(O).ToList();
        var mvcAction = g.Graph.AllFacts.Where(f => f.Kind == "mvc-action").GroupBy(f => f.Owner, O).ToDictionary(x => x.Key, x => x.First(), O);
        foreach (var a in actions)
        {
            var views = g.Out(a, EdgeKind.Renders).Select(e => e.Target).Where(t => t.StartsWith("view:", StringComparison.Ordinal)).Distinct(O).Order(O).ToList();
            var project = g.Nodes.TryGetValue(a, out var an) ? an.Project : null;
            var projectDir = ProjectDir(project);
            if (views.Count > 0)
            {
                var file = views[0]["view:".Length..];
                var (home, name) = RazorHome(file, ProjectDir(g.Nodes.TryGetValue(views[0], out var vn) ? vn.Project : project));
                Add(new Screen(a, views[0], Dir(file), home, name, false, projectDir));
            }
            else
            {
                // A view path known only at runtime: MVC's own convention, the controller's name in its project (D219).
                var controller = mvcAction.TryGetValue(a, out var mf) ? mf["controller"] : ControllerOf(a);
                var folder = $"{projectDir}/Views/{controller}".TrimStart('/');
                Add(new Screen(a, null, folder, folder, $"{Base(projectDir)} › {controller}", false, projectDir));
            }
        }
        foreach (var s in Screens.Values.Where(s => !s.CatchAll && s.Home != "").OrderBy(s => s.Id, O)) Homes.TryAdd(s.Home, s.HomeName);
    }

    void Add(Screen s)
    {
        Screens[s.Id] = s;
        foreach (var t in new[] { s.Target, s.Id }.Where(t => t is not null).Distinct(O))
        {
            if (!byTarget.TryGetValue(t!, out var list)) byTarget[t!] = list = [];
            list.Add(s);
        }
    }

    static string Common(List<string> dirs)
    {
        var parts = dirs[0].Split('/').ToList();
        foreach (var d in dirs.Skip(1))
        {
            var p = d.Split('/');
            var n = 0;
            while (n < parts.Count && n < p.Length && parts[n] == p[n]) n++;
            parts = parts.Take(n).ToList();
        }
        return string.Join('/', parts);
    }

    static string TsHome(string root, string file)
    {
        var dir = Dir(file);
        if (dir == root || !Under(dir, root)) return dir;
        var rest = root == "" ? dir : dir[(root.Length + 1)..];
        return (root == "" ? "" : root + "/") + rest.Split('/')[0];
    }

    static string HomeNameTs(string root, string home) => home == root ? (Base(root) is { Length: > 0 } b ? b : "(root)") : Base(home);

    /// <summary>D219's Razor home: the folder after <c>Views/</c>, named with its project and the folders between, <c>Areas/</c> dropped.</summary>
    static (string Home, string Name) RazorHome(string file, string projectDir)
    {
        var at = file.IndexOf("/Views/", StringComparison.Ordinal);
        if (at < 0) return (Dir(file), Base(Dir(file)));
        var pre = file[..at];
        var rest = file[(at + "/Views/".Length)..].Split('/');
        var home = rest.Length > 1 ? $"{pre}/Views/{rest[0]}" : $"{pre}/Views";
        var between = (projectDir != "" && Under(pre, projectDir) ? pre[projectDir.Length..] : "").Split('/', StringSplitOptions.RemoveEmptyEntries).Where(x => x != "Areas");
        var name = string.Join(" › ", new[] { Base(projectDir != "" ? projectDir : pre) }.Concat(between).Concat(rest.Length > 1 ? [rest[0]] : []));
        return (home, name);
    }

    static string ControllerOf(string action)
    {
        var body = action.Contains("|M:", StringComparison.Ordinal) ? action[(action.IndexOf("|M:", StringComparison.Ordinal) + 3)..] : action;
        if (body.Contains('(')) body = body[..body.IndexOf('(')];
        var parts = body.Split('.');
        var c = parts.Length >= 2 ? parts[^2] : body;
        return c.EndsWith("Controller", StringComparison.Ordinal) ? c[..^"Controller".Length] : c;
    }

    /// <summary>The home folder a path sits in, if any.</summary>
    public string? HomeOf(string folder)
    {
        string? best = null;
        foreach (var h in Homes.Keys) if (Under(folder, h) && (best is null || h.Length > best.Length)) best = h;
        return best;
    }

    // ---- The menu (D221, D226) -------------------------------------------------------------------------------------------------

    void ReadMenus(Translations names)
    {
        var routeNames = g.Graph.AllFacts.Where(f => f.Kind == "conventional" && f["name"] != "")
            .GroupBy(f => f["name"], StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var mvc = g.Graph.AllFacts.Where(f => f.Kind == "mvc-action").GroupBy(f => f.Owner, O).ToDictionary(x => x.Key, x => x.First(), O);
        var mvcScreens = Screens.Values.Where(s => mvc.ContainsKey(s.Id))
            .ToLookup(s => $"{mvc[s.Id]["area"]}/{mvc[s.Id]["controller"]}/{mvc[s.Id]["action"]}".ToLowerInvariant(), O);
        var routes = Screens.Values.Where(s => s.Id.StartsWith("page:", StringComparison.Ordinal) && !s.CatchAll)
            .Select(s => (Screen: s, Segs: Segments(s.Id[(s.Id.IndexOf('|') + 1)..]))).ToList();

        List<string> Match(string address)
        {
            if (address.StartsWith("mvc:", StringComparison.Ordinal)) return [.. mvcScreens[address[4..].ToLowerInvariant()].Select(s => s.Id).Order(O)];
            if (address.StartsWith("route:", StringComparison.Ordinal))
                return routeNames.TryGetValue(address[6..], out var r) ? [.. mvcScreens[$"{r["area"]}/{r["controller"]}/{r["action"]}".ToLowerInvariant()].Select(s => s.Id).Order(O)] : [];
            if (address.Contains("://", StringComparison.Ordinal) || address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return [];
            var segs = Segments(address);
            var hits = routes.Where(r => r.Segs.Length == segs.Length && r.Segs.Zip(segs).All(p => p.First.StartsWith(':') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
                .Select(r => (r.Screen.Id, Params: r.Segs.Count(x => x.StartsWith(':')))).ToList();
            if (hits.Count == 0) return [];
            var fewest = hits.Min(h => h.Params);
            return [.. hits.Where(h => h.Params == fewest).Select(h => h.Id).Order(O)];
        }

        var facts = g.Graph.AllFacts.Where(f => f.Kind == "menu-item").ToList();
        var menus = new List<(string Menu, List<MenuItemInfo> Items, HashSet<string> Screens, int Leading)>();
        foreach (var grp in facts.GroupBy(f => f["menu"], O).OrderBy(x => x.Key, O))
        {
            var items = new Dictionary<string, MenuItemInfo>(O);
            foreach (var f in grp.OrderBy(f => PathKey(f["path"]), O))
            {
                var words = names.Text(f["key"]) ?? (f["name"] != "" ? f["name"] : f["key"]);
                // A translation that is only another key's reference (transloco's "{{settings.customize}}") reads that key.
                for (var hop = 0; hop < 3 && System.Text.RegularExpressions.Regex.Match(words, @"^\{\{\s*([\w.-]+)\s*\}\}$") is { Success: true } r; hop++)
                    words = names.Text(r.Groups[1].Value) ?? words;
                var item = new MenuItemInfo { Menu = grp.Key, Path = f["path"], Name = words.Trim(), Address = f["address"], Rank = 0, AppArea = f["area"] };
                if (item.Address != "") item.Screens.AddRange(Match(item.Address));
                items.TryAdd(item.Path, item);
            }
            foreach (var it in items.Values)
                if (it.Path.Contains('.') && items.TryGetValue(it.Path[..it.Path.LastIndexOf('.')], out var parent)) { it.Parent = parent; parent.Children.Add(it); }
            var all = items.Values.ToList();
            menus.Add((grp.Key, all, all.SelectMany(i => i.Screens).ToHashSet(O), all.Count(i => i.Screens.Count > 0)));
        }
        // D226: a menu needs three or more items leading to screens; the one reaching most screens first; a menu more than half of
        // whose screens a kept menu already reaches (a settings page's own index of the sidebar's settings) adds nothing.
        var reached = new HashSet<string>(O);
        var rank = 0;
        foreach (var m in menus.Where(m => m.Leading >= 3).OrderByDescending(m => m.Screens.Count).ThenBy(m => m.Menu, O))
        {
            if (m.Screens.Count(reached.Contains) * 2 > m.Screens.Count) continue;
            reached.UnionWith(m.Screens);
            KeptMenus.Add(m.Menu);
            rank++;
            foreach (var it in m.Items.Where(i => i.Parent is null).OrderBy(i => PathKey(i.Path), O))
            {
                var top = Rerank(it, rank);
                var area = top.Subtree().Select(x => x.AppArea).FirstOrDefault(a => a != "") ?? "";
                foreach (var x in top.Subtree()) x.AppArea = area;
                TopItems.Add(top);
            }
        }
        // Each screen's item: the deepest that matches it, then the first kept menu, then the first in its menu.
        foreach (var it in TopItems.SelectMany(t => t.Subtree()).OrderByDescending(i => i.Depth).ThenBy(i => i.Rank).ThenBy(i => PathKey(i.Path), O))
            foreach (var s in it.Screens) if (Screens[s].Item is null) Screens[s].Item = it;
    }

    static MenuItemInfo Rerank(MenuItemInfo it, int rank)
    {
        var copy = new MenuItemInfo { Menu = it.Menu, Path = it.Path, Name = it.Name, Address = it.Address, Rank = rank, AppArea = it.AppArea };
        copy.Screens.AddRange(it.Screens);
        foreach (var c in it.Children.OrderBy(c => PathKey(c.Path), O)) { var cc = Rerank(c, rank); cc.Parent = copy; copy.Children.Add(cc); }
        return copy;
    }

    static string PathKey(string path) => string.Join('.', path.Split('.').Select(p => p.PadLeft(4, '0')));

    static string[] Segments(string address)
    {
        var a = address;
        var cut = a.IndexOfAny(['?', '#']);
        if (cut >= 0) a = a[..cut];
        return a.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The name an item's area takes: nopCommerce's Admin › Catalog when its addresses lead into an MVC area.</summary>
    public static string AreaName(MenuItemInfo top) => top.AppArea != "" ? $"{top.AppArea} › {top.Name}" : top.Name;

    // ---- Start flows and their places (D219, D222) -----------------------------------------------------------------------------

    static readonly HashSet<string> StartKinds = new(["click", "screen-opens", "job", "handler"], StringComparer.Ordinal);
    static readonly string[] Up = [EdgeKind.Renders, EdgeKind.Uses, EdgeKind.Calls];

    void ReadStarts()
    {
        foreach (var f in Flows.Values.Where(f => StartKinds.Contains(f.Kind)))
        {
            var file = f.Start.Contains(':') ? f.Start[..f.Start.LastIndexOf(':')] : f.Start;
            var own = Dir(file);
            switch (f.Kind)
            {
                case "screen-opens":
                {
                    var s = Screens.GetValueOrDefault(f.Point);
                    Starts[f.Id] = s is null ? new StartFlow(f.Id, f.Kind, [new Place(null, own)], false, null, null, null, own)
                        : s.CatchAll ? new StartFlow(f.Id, f.Kind, [new Place(null, s.Folder, Frame: true)], false, null, null, null, own)
                        : new StartFlow(f.Id, f.Kind, [new Place(s.Item?.Address, s.Folder)], false, null, null, null, own);
                    break;
                }
                case "click":
                    Starts[f.Id] = Click(f, own);
                    break;
                default:
                    var (dir, folder, name) = Backend(f.Point, file);
                    Starts[f.Id] = new StartFlow(f.Id, f.Kind, [], false, dir, folder, name, own);
                    break;
            }
        }
    }

    StartFlow Click(IndexFlow f, string own)
    {
        var start = g.Nodes.ContainsKey(f.Point) ? f.Point : f.Point.Contains('#') && g.Nodes.ContainsKey(f.Point[..f.Point.IndexOf('#')]) ? f.Point[..f.Point.IndexOf('#')] : f.Point;
        var home = HomeOf(own);
        var (screens, homeNodes, root) = WalkUp(start, home);
        var places = new List<Place>();
        if (home is not null)
        {
            places.AddRange(screens.Where(s => s.Home == home).Select(s => new Place(s.Item?.Address, own)));
            if (places.Count == 0) places.Add(new Place(null, own));
        }
        else
        {
            places.AddRange(screens.Select(s => new Place(s.Item?.Address, s.Folder)));
            places.AddRange(homeNodes.Select(d => new Place(null, d)));
            if (places.Count == 0 && root) places.Add(new Place(null, own, Frame: true));
        }
        var distinct = places.Distinct().OrderBy(p => p.Address ?? "", O).ThenBy(p => p.Folder, O).ThenBy(p => p.Frame).ToList();
        return new StartFlow(f.Id, f.Kind, distinct, distinct.Count == 0, null, null, null, own);
    }

    /// <summary>
    /// D219, D222: up from a click through what shows it (renders), opens it (uses) or calls it, and from a member to its component, until a
    /// screen, or code in another home folder. <c>Root</c>: the walk reached code nothing shows, the app's frame.
    /// </summary>
    (List<Screen> Screens, List<string> HomeDirs, bool Root) WalkUp(string start, string? ownHome)
    {
        var seen = new HashSet<string>(O) { start };
        var queue = new Queue<string>([start]);
        var screens = new List<Screen>();
        var homeDirs = new SortedSet<string>(O);
        var root = false;
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (n != start)
            {
                if (byTarget.TryGetValue(n, out var hit) && hit.Any(s => !s.CatchAll)) { screens.AddRange(hit.Where(s => !s.CatchAll)); continue; }
                if (g.Nodes.TryGetValue(n, out var node) && node.File is not null && HomeOf(Dir(node.File)) is { } h && h != ownHome && IsShown(node)) { homeDirs.Add(Dir(node.File)); continue; }
            }
            var parents = Up.SelectMany(k => g.In(n, k)).Select(e => e.Source).ToList();
            if (g.Nodes.TryGetValue(n, out var me) && me.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Field or NodeKind.Template)
                parents.AddRange(g.In(n, EdgeKind.Contains).Select(e => e.Source));
            var next = parents.Where(p => !Skip(p)).Distinct(O).Order(O).ToList();
            if (next.Count == 0 && n != start && g.Nodes.TryGetValue(n, out var top) && top.Kind is NodeKind.Component or NodeKind.View or NodeKind.Template or NodeKind.Type or NodeKind.Method) root = true;
            foreach (var p in next) if (seen.Add(p)) queue.Enqueue(p);
        }
        return ([.. screens.Distinct().OrderBy(s => s.Id, O)], [.. homeDirs], root);
    }

    static bool IsShown(Node n) => n.Kind is NodeKind.Component or NodeKind.View or NodeKind.Template or NodeKind.Method or NodeKind.Type or NodeKind.Property or NodeKind.Field;

    bool Skip(string id) =>
        id.StartsWith("file:", StringComparison.Ordinal) || id.StartsWith("project:", StringComparison.Ordinal) || id.StartsWith("page:", StringComparison.Ordinal)
        || id.StartsWith("endpoint:", StringComparison.Ordinal) || g.IsTest(id);

    /// <summary>D223's backend folder: the first folder below the class's project, a class at the project's root taking its name; an interface's job, its one implementation's.</summary>
    (string Dir, string Folder, string Name) Backend(string point, string file)
    {
        var at = point;
        var impls = g.Out(point, EdgeKind.DispatchesTo).Select(e => e.Target).Distinct(O).Order(O).ToList();
        if (impls.Count == 1 && g.Nodes.TryGetValue(point, out var pn) && g.Nodes.TryGetValue(DocIds.ContainingType(point) ?? "", out var t) && IsInterface(t)) at = impls[0];
        if (!g.Nodes.TryGetValue(at, out var node) || node.File is null) return (Dir(file), Dir(file), Base(Dir(file)));
        var projectDir = ProjectDir(node.Project);
        var rel = projectDir != "" && Under(node.File, projectDir) ? node.File[(projectDir.Length + 1)..] : node.File;
        var first = rel.Contains('/') ? rel[..rel.IndexOf('/')] : null;
        var project = Base(projectDir != "" ? projectDir : Dir(node.File));
        return first is null ? (Dir(node.File), projectDir, project) : (Dir(node.File), $"{projectDir}/{first}".TrimStart('/'), $"{project} › {first}");
    }

    bool IsInterface(Node type) => g.In(type.Id, EdgeKind.Implements).Any() || g.Out(type.Id, EdgeKind.DispatchesTo).Any() || type.Name.StartsWith('I') && type.Name.Length > 1 && char.IsUpper(type.Name[1]);

    // ---- Placing flows by an areas.yaml (D225) ---------------------------------------------------------------------------------

    /// <summary>The area a place falls in: its menu address's area, unless an area lists a folder holding it deeper than any the menu's area lists.</summary>
    static string? AreaOfPlace(Place p, List<AreaSpec> areas)
    {
        AreaSpec? byMenu = p.Address is null ? null : areas.FirstOrDefault(a => a.Menu.Contains(p.Address, O));
        AreaSpec? byFolder = null;
        var folderDepth = -1;
        foreach (var a in areas)
            foreach (var f in a.Folders)
                if (Under(p.Folder, f) && f.Length > folderDepth) { byFolder = a; folderDepth = f.Length; }
        if (byMenu is null) return byFolder?.Name;
        var menuDepth = byMenu.Folders.Where(f => Under(p.Folder, f)).Select(f => f.Length).DefaultIfEmpty(-1).Max();
        return byFolder is not null && byFolder != byMenu && menuDepth >= 0 && folderDepth > menuDepth ? byFolder.Name : byMenu.Name;
    }

    /// <summary>Places every start flow by the file (D223 for jobs and handlers). Answered ties hold.</summary>
    public Placement Place(AreasFile file)
    {
        var result = new Placement();
        var names = file.Areas.Select(a => a.Name).Distinct(O).ToList();
        // An area's parent: the longest other area whose name it starts with, then " › " (a split's parts, D221, D224).
        var parentOf = names.ToDictionary(n => n, n => names.Where(p => p != n && n.StartsWith(p + " › ", StringComparison.Ordinal)).OrderByDescending(p => p.Length).FirstOrDefault(), O);
        int Depth(string n) { var d = 0; for (var x = parentOf[n]; x is not null; x = parentOf[x]) d++; return d; }
        List<string> Line(string n) { var l = new List<string>(); for (string? x = n; x is not null; x = parentOf[x]) l.Add(x); return l; }
        var answers = file.Ties.Where(t => t.Answer != "" && names.Contains(t.Answer, O)).GroupBy(t => t.Flow, O).ToDictionary(x => x.Key, x => x.First().Answer, O);
        void Decide(string id, List<string> found)
        {
            if (found.Count == 1) result.AreaOf[id] = found[0];
            else if (found.Count == 0) result.Uncovered.Add(id);
            else if (answers.TryGetValue(id, out var a)) result.AreaOf[id] = a;
            // D227: parts of one area (Settings › Indexers, Settings › Connect) give that area, not a tie.
            else if (found.Select(Line).Aggregate((x, y) => [.. x.Intersect(y, O)]).FirstOrDefault() is { } common) result.AreaOf[id] = common;
            else result.Open[id] = found;
        }
        foreach (var s in Starts.Values.Where(s => s.Kind is not ("job" or "handler")))
        {
            if (s.Any) { Decide(s.Id, names.Count < 2 ? names : [.. names]); continue; }   // D222: code can't see which screen shows it
            var found = s.Places.Select(p => AreaOfPlace(p, file.Areas)).Where(a => a is not null).Select(a => a!).Distinct(O).ToList();
            Decide(s.Id, [.. names.Where(found.Contains)]);
        }
        // D223: jobs and handlers go where more than half the start flows leading to them sit, through shared flows; one placed so counts
        // for those it leads to, repeated until nothing changes; an even split between two areas is a tie; else their backend folder.
        var jobs = Starts.Values.Where(s => s.Kind is "job" or "handler").Select(s => s.Id).ToList();
        var leads = new Dictionary<string, HashSet<string>>(O);
        foreach (var s in Starts.Keys) leads[s] = Reach(s);
        var placedByMajority = new Dictionary<string, string>(O);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var j in jobs)
            {
                if (placedByMajority.ContainsKey(j)) continue;
                var counts = Counts(j);
                var total = counts.Values.Sum();
                if (total == 0) continue;
                // D227: an area split into parts counts its parts' flows too; the deepest area holding more than half takes it.
                var held = new Dictionary<string, int>(O);
                foreach (var (area, n) in counts) for (var x = area; x is not null; x = parentOf[x]) held[x] = held.GetValueOrDefault(x) + n;
                var top = held.Where(h => h.Value * 2 > total).OrderByDescending(h => Depth(h.Key)).ThenBy(h => names.IndexOf(h.Key)).Select(h => h.Key).FirstOrDefault();
                if (top is not null) { placedByMajority[j] = top; result.AreaOf[j] = top; changed = true; }
            }
        }
        foreach (var j in jobs.Where(j => !placedByMajority.ContainsKey(j)))
        {
            var counts = Counts(j);
            if (counts.Count == 2 && counts.Values.Distinct().Count() == 1) { Decide(j, [.. names.Where(counts.ContainsKey)]); continue; }
            var s = Starts[j];
            var area = s.Backend is null ? null : AreaOfPlace(new Place(null, s.Backend), file.Areas);
            Decide(j, area is null ? [] : [area]);
        }
        return result;

        Dictionary<string, int> Counts(string job)
        {
            var c = new Dictionary<string, int>(O);
            foreach (var (from, reach) in leads)
            {
                if (from == job || !reach.Contains(job)) continue;
                var area = Starts[from].Kind is "job" or "handler" ? placedByMajority.GetValueOrDefault(from) : result.AreaOf.GetValueOrDefault(from);
                if (area is not null) c[area] = c.GetValueOrDefault(area) + 1;
            }
            return c;
        }
    }

    /// <summary>The start flows and shared flows a flow leads to, directly or through shared flows (D189, D218).</summary>
    public HashSet<string> Reach(string id, bool sharedOnly = false)
    {
        var found = new HashSet<string>(O);
        var seen = new HashSet<string>(O) { id };
        var stack = new Stack<string>([id]);
        while (stack.Count > 0)
            foreach (var l in Flows.TryGetValue(stack.Pop(), out var f) ? f.Links : [])
            {
                if (!Flows.TryGetValue(l, out var to) || !seen.Add(l)) continue;
                if (to.Kind == "shared") { if (sharedOnly) found.Add(l); stack.Push(l); }
                else if (!sharedOnly) found.Add(l);
            }
        return found;
    }
}
