using System.Text.RegularExpressions;
using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>One inventory line (D326, D334 item 6): a control a user can touch, named by the label lookup.</summary>
public sealed record InventoryLine(
    string Kind, Named Name, Choices Choices, string File, int Line, string Code, string Handler, string Part, Fact Fact)
{
    public string At => $"{File}:{Line}";
    /// <summary>Display-name server fields this field shows (D334 item 4), merged into this line rather than listed again.</summary>
    public List<Fact> Merged { get; } = [];
}

/// <summary>A line left out by D326's fixed list (D334 item 5), with its reason: <c>close</c>, <c>table</c> or <c>expand</c>.</summary>
public sealed record SkippedLine(InventoryLine Line, string Reason);

/// <summary>A server-defined field that is a label, not a control (D334 item 4): <c>unbound-model-label</c>.</summary>
public sealed record SetAsideLine(Fact Fact, Named Name, string Reason)
{
    public string At => $"{Fact.File}:{Fact.Line}";
}

/// <summary>
/// A group of lines (D328): a part (component with its template, view, partial, view component), a dialog opened from code, a data
/// menu, or a server class's generic-form fields. <see cref="TitleFromCode"/> when no title is written for it (D327: marked <c>code</c>).
/// </summary>
public sealed class InventoryGroup
{
    public required string Id { get; init; }
    public required string Kind { get; init; }   // part | dialog | menu | server-class
    public required string Title { get; init; }
    public required bool TitleFromCode { get; init; }
    public List<InventoryLine> Lines { get; } = [];
    /// <summary>Screens reaching it, by id, ordinal.</summary>
    public List<string> Screens { get; set; } = [];

    /// <summary>"14 actions", "3 fields", "5 actions, 3 fields".</summary>
    public string Size()
    {
        var actions = Lines.Count(l => l.Kind is "button" or "menu-entry");
        var fields = Lines.Count - actions;
        static string N(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";
        return actions > 0 && fields > 0 ? $"{N(actions, "action")}, {N(fields, "field")}" : actions > 0 ? N(actions, "action") : N(fields, "field");
    }
}

/// <summary>A screen's section (D328): its own lines, the dialogs and menus it opens, and one pointer per shared part it uses.</summary>
public sealed class InventoryScreen
{
    public required string Id { get; init; }
    public required Named Title { get; init; }
    public string Line => Labels.ScreenLine(Title);
    public List<InventoryLine> Lines { get; } = [];
    public List<InventoryGroup> Dialogs { get; } = [];
    public List<InventoryGroup> Menus { get; } = [];
    public List<InventoryGroup> Pointers { get; } = [];
}

/// <summary>The whole inventory of one graph (D325–D328, D334), deterministic: the same graph gives the same lists in the same order.</summary>
public sealed class InventorySet
{
    public required string Commit { get; init; }
    public List<InventoryScreen> Screens { get; } = [];
    public List<InventoryGroup> Shared { get; } = [];
    public List<InventoryGroup> NotPlaced { get; } = [];
    /// <summary>D362 (3): what the app's root component draws or opens, on every screen; the roots found, by id.</summary>
    public List<InventoryGroup> AppFrame { get; } = [];
    public List<string> Roots { get; } = [];
    public List<SkippedLine> Skipped { get; } = [];
    public List<SetAsideLine> SetAside { get; } = [];
    /// <summary>Data menus found, and how many are placed under a screen or shared.</summary>
    public int Menus { get; set; }
    public int MenusPlaced { get; set; }
    /// <summary>Dialogs found (components opened from code, D334 item 1).</summary>
    public int Dialogs { get; set; }

    public IEnumerable<InventoryLine> AllLines =>
        Screens.SelectMany(s => s.Lines.Concat(s.Dialogs.SelectMany(d => d.Lines)).Concat(s.Menus.SelectMany(m => m.Lines)))
            .Concat(Shared.SelectMany(g => g.Lines)).Concat(AppFrame.SelectMany(g => g.Lines)).Concat(NotPlaced.SelectMany(g => g.Lines));
}

/// <summary>
/// Builds the PM inventory (D325–D328) from the code graph by code, no AI, by the calls D334 fixed before any run: lines from
/// <c>ui-event</c> buttons, <c>menu-item</c>s and <c>form-field</c>s outside tests, named by <see cref="Labels"/>; parts reached from
/// each screen through <c>renders</c>, dialogs opened from code (a 4-hop frontend <c>calls</c> walk to a <c>uses</c> line onto a
/// component not rendered there) and data menus (reached by the same walk); a part rendered by exactly one other folded into it;
/// placed under one screen, "Shared parts" or "Not placed"; D326's skip list applied, its reasons kept.
/// </summary>
public static partial class Inventory
{
    /// <summary>D334 item 1: how many <c>calls</c> lines the walk from a part's own methods follows.</summary>
    public const int Hops = 4;

    public static InventorySet Build(CodeGraph graph, string root) => new Builder(new FlowGraph(graph, root), root).Build();

    // ---- D334 item 5: the fixed skip list --------------------------------------------------------------------------------

    static readonly Dictionary<string, string> SkipWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["close"] = "close", ["cancel"] = "close", ["back"] = "close", ["go back"] = "close", ["dismiss"] = "close", ["×"] = "close",
        ["expand"] = "expand", ["collapse"] = "expand", ["expand all"] = "expand", ["collapse all"] = "expand",
        ["show more"] = "expand", ["show less"] = "expand", ["read more"] = "expand", ["read less"] = "expand", ["see more"] = "expand", ["see less"] = "expand",
        ["next page"] = "table", ["previous page"] = "table", ["first page"] = "table", ["last page"] = "table",
        ["select all"] = "table", ["deselect all"] = "table", ["unselect all"] = "table",
    };

    static readonly Dictionary<string, string> SkipHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["close"] = "close", ["cancel"] = "close", ["dismiss"] = "close", ["back"] = "close", ["goBack"] = "close",
        ["expand"] = "expand", ["collapse"] = "expand", ["toggleExpand"] = "expand", ["toggleCollapse"] = "expand", ["showMore"] = "expand", ["showLess"] = "expand",
        ["sort"] = "table", ["sortBy"] = "table", ["changeSort"] = "table", ["updateSort"] = "table", ["pageChange"] = "table", ["changePage"] = "table",
        ["nextPage"] = "table", ["previousPage"] = "table", ["prevPage"] = "table", ["firstPage"] = "table", ["lastPage"] = "table",
        ["setPageSize"] = "table", ["pageSizeChange"] = "table", ["selectAll"] = "table", ["toggleAll"] = "table", ["deselectAll"] = "table",
    };

    static readonly HashSet<string> SkipFieldWords = new(StringComparer.OrdinalIgnoreCase) { "page size", "items per page", "per page" };

    /// <summary>The set-aside reasons: D334 item 4's unbound model label, D336's hidden server field.</summary>
    public static readonly IReadOnlyList<string> SetAsideReasons = ["hidden-field", "unbound-model-label"];

    /// <summary>D336: an attribute argument <c>Hidden =</c> whose value is <c>Hidden</c>, <c>X.Hidden</c> or <c>true</c>.</summary>
    public static bool IsHidden(string args) => HiddenArg().IsMatch(args);

    [System.Text.RegularExpressions.GeneratedRegex(@"(?:^|[,(\s])Hidden\s*=\s*(?:(?:[A-Za-z_]\w*\.)*Hidden|true)\s*(?:$|[,)])")]
    private static partial System.Text.RegularExpressions.Regex HiddenArg();

    /// <summary>The reasons D334 item 5 allows; <c>inventory check</c> fails on any other.</summary>
    public static readonly IReadOnlyList<string> SkipReasons = ["close", "expand", "table"];

    /// <summary>
    /// A button's or menu entry's skip reason, or null: its visible words, else its handler's short name (D334 item 5). D335: a close-type
    /// handler name counts only when the line shows no words of its own (a "Download" button whose handler is <c>close</c> stays); bare
    /// next/previous/first/last are no longer skip words (a checkout's "Next" is a real step).
    /// </summary>
    public static string? SkipAction(string? shown, string? handler)
    {
        var hasWords = shown is not null && shown.Any(char.IsLetter);
        if (shown is not null)
        {
            var w = shown.Trim().TrimEnd('…', ':').Trim();
            if (w.EndsWith("...", StringComparison.Ordinal)) w = w[..^3].Trim();
            if (SkipWords.TryGetValue(w, out var reason)) return reason;
        }
        if (handler is null) return null;
        var h = handler;
        foreach (var prefix in new[] { "handle", "on" })
            if (h.Length > prefix.Length && h.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(h[prefix.Length])) { h = h[prefix.Length..]; break; }
        foreach (var suffix in new[] { "Clicked", "Click", "Press", "Modal", "Dialog" })
            if (h.Length > suffix.Length && h.EndsWith(suffix, StringComparison.Ordinal)) { h = h[..^suffix.Length]; break; }
        return SkipHandlers.TryGetValue(h, out var r) && !(hasWords && r == "close") ? r : null;
    }

    /// <summary>
    /// A field's skip reason, or null: label words page size, items per page, per page, select all; or bound to <c>selectAll</c>. D335: the
    /// page-size words skip only a field bound to no server model property (<paramref name="property"/> empty): one that saves to a model is a
    /// setting (nopCommerce's category "Page size"), not table mechanics.
    /// </summary>
    public static string? SkipField(string? words, string boundTo, string property = "")
    {
        var w = words?.Trim().TrimEnd('…', ':').Trim();
        if (w is not null && ((SkipFieldWords.Contains(w) && property == "") || string.Equals(w, "select all", StringComparison.OrdinalIgnoreCase))) return "table";
        var last = boundTo[(boundTo.LastIndexOf('.') + 1)..].Trim();
        return string.Equals(last, "selectAll", StringComparison.OrdinalIgnoreCase) ? "table" : null;
    }

    // ---- names said readable (D327: a code name, marked `code`) ------------------------------------------------------------

    /// <summary><c>SeriesIndexBody</c> → "Series Index Body"; <c>edit-reading-list</c> → "Edit Reading List"; <c>GROUPS</c> → "Groups".</summary>
    public static string Readable(string name)
    {
        var allUpper = name.Any(char.IsLetter) && !name.Any(char.IsLower);
        var words = Hump().Matches(name).Select(m => m.Value).ToList();
        if (words.Count == 0) return name;
        return string.Join(" ", words.Select(w => allUpper || w.Length == 1 || !w.All(char.IsUpper) ? char.ToUpperInvariant(w[0]) + (allUpper ? w[1..].ToLowerInvariant() : w[1..]) : w));
    }

    /// <summary>A data menu's title (D334 item 2): its holder's name said readable, a leading get/build/create taken off.</summary>
    public static string MenuTitle(string holderName)
    {
        var n = MenuVerb().Replace(holderName.TrimStart('_'), "");
        return Readable(n.Length == 0 ? holderName : n);
    }

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Hump();

    [GeneratedRegex(@"^(?:get|build|create)(?=[A-Z_])")]
    private static partial Regex MenuVerb();

    [GeneratedRegex(@"(?:this\.)?([A-Za-z_$][\w$]*)\s*(?::[^=]*)?=\s*\[")]
    internal static partial Regex ListAssigned();

    // ---- the builder ------------------------------------------------------------------------------------------------------

    sealed partial class Builder(FlowGraph g, string root)
    {
        readonly Labels labels = new(g, root);
        readonly Dictionary<string, string> partOf = new(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> renderChildren = new(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> walks = new(StringComparer.Ordinal);
        ILookup<string, string>? members;

        static int ByAt(InventoryLine a, InventoryLine b)
        {
            var c = string.CompareOrdinal(a.File, b.File);
            if (c != 0) return c;
            c = a.Line.CompareTo(b.Line);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Kind, b.Kind);
            return c != 0 ? c : string.CompareOrdinal(a.Fact.Owner, b.Fact.Owner);
        }

        public InventorySet Build()
        {
            var set = new InventorySet { Commit = g.Graph.Commit };
            bool Out(Fact f) => !g.IsTest(f.Owner);
            static IEnumerable<Fact> Ordered(IEnumerable<Fact> facts) => facts.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal);

            var buttons = Ordered(g.AllFacts("ui-event").Where(f => Out(f) && Labels.IsButton(f))).ToList();
            var allFields = g.AllFacts("form-field").Where(Out).ToList();
            var fields = Ordered(allFields.Where(f => f["via"] != "server" && f["inside"] != "wrapper")).ToList();
            var server = Ordered(allFields.Where(f => f["via"] == "server")).ToList();
            var menuItems = Ordered(g.AllFacts("menu-item").Where(Out)).ToList();

            var partLines = new Dictionary<string, List<InventoryLine>>(StringComparer.Ordinal);
            void AddPartLine(InventoryLine line)
            {
                if (!partLines.TryGetValue(line.Part, out var list)) partLines[line.Part] = list = [];
                list.Add(line);
            }

            foreach (var f in buttons)
            {
                var name = labels.Button(f);
                var handlerName = Labels.HandlerName(f["handler"]);
                var line = new InventoryLine("button", name, Choices.None, f.File ?? "", f.Line, name.Code, f["handler"], PartOf(f.Owner), f);
                if (SkipAction(Labels.Shown(name, f["text"]), handlerName) is { } reason) set.Skipped.Add(new(line, reason));
                else AddPartLine(line);
            }

            // D334 item 4: a display-name server field shows only through a view field bound to its property (merged there).
            var fieldLines = new List<InventoryLine>();
            foreach (var f in fields)
            {
                var name = labels.Field(f);
                var line = new InventoryLine("field", name, labels.ChoicesOf(f), f.File ?? "", f.Line, name.Code, "", PartOf(f.Owner), f);
                if (SkipField(name.Words, f["to"], f["property"]) is { } reason) set.Skipped.Add(new(line, reason));
                else AddPartLine(line);
                fieldLines.Add(line);
            }
            var byProperty = fieldLines.Where(l => l.Fact["property"] != "").GroupBy(l => l.Fact["property"], StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var serverClasses = new SortedDictionary<string, List<InventoryLine>>(StringComparer.Ordinal);
            foreach (var f in server)
            {
                var name = labels.Field(f);
                if (IsDisplayName(f["attribute"]))
                {
                    var bar = f.Owner.IndexOf('|', StringComparison.Ordinal);
                    var property = bar >= 0 ? f.Owner[(bar + 1)..] : f.Owner;
                    if (byProperty.TryGetValue(property, out var bound)) bound.Merged.Add(f);
                    else set.SetAside.Add(new(f, name, "unbound-model-label"));
                    continue;
                }
                // D336: a server-defined field its attribute marks hidden (`Hidden = Hidden`, `Hidden = HiddenType.Hidden`, `Hidden = true`) is drawn for
                // no user: set aside, like an unbound label.
                if (IsHidden(f["args"])) { set.SetAside.Add(new(f, name, "hidden-field")); continue; }
                var line = new InventoryLine("server-field", name, labels.ChoicesOf(f), f.File ?? "", f.Line, name.Code, "", f["to"], f);
                if (!serverClasses.TryGetValue(f["to"], out var list)) serverClasses[f["to"]] = list = [];
                list.Add(line);
            }

            // Menu entries: written in markup → lines of their part; written as data → one menu per `menu` key (D334 item 2).
            var dataMenus = new SortedDictionary<string, List<InventoryLine>>(StringComparer.Ordinal);
            var menuOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in menuItems)
            {
                var name = labels.MenuEntry(f);
                var data = f["via"] == "data";
                var menuId = "menu:" + (f["menu"] != "" ? f["menu"] : f.Owner);
                var line = new InventoryLine("menu-entry", name, Choices.None, f.File ?? "", f.Line, name.Code, f["handler"], data ? menuId : PartOf(f.Owner), f);
                if (SkipAction(name.Words, Labels.HandlerName(f["handler"])) is { } reason) { set.Skipped.Add(new(line, reason)); continue; }
                if (!data) { AddPartLine(line); continue; }
                if (!dataMenus.TryGetValue(menuId, out var list)) { dataMenus[menuId] = list = []; menuOwner[menuId] = f.Owner; }
                list.Add(line);
            }

            // ---- parts, dialogs, menus, reach -------------------------------------------------------------------------------
            var partIds = g.Nodes.Values.Where(n => !n.IsTest && n.Kind is NodeKind.Component or NodeKind.View).Select(n => n.Id)
                .Concat(partLines.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var dialogsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var dialogs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in partIds)
            {
                var opened = Dialogs(p);
                dialogsOf[p] = opened;
                dialogs.UnionWith(opened);
            }
            // Data menus: the holder (the member the list is assigned to, else the member holding the facts) and who reads it.
            var menuTitle = new Dictionary<string, string>(StringComparer.Ordinal);
            var menuReaders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (menuId, lines) in dataMenus)
            {
                var (holder, readers) = Holder(menuOwner[menuId], lines.Min(l => l.Line), lines[0].File);
                menuReaders[menuId] = readers;
                menuTitle[menuId] = MenuTitle(g.Nodes.TryGetValue(holder, out var hn) ? FlowGraph.ShortName(hn) : ShortOf(holder));
            }
            var menusOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var p in partIds)
            {
                var walk = Walk(p);
                menusOf[p] = [.. menuReaders.Where(m => m.Value.Overlaps(walk)).Select(m => m.Key).Order(StringComparer.Ordinal)];
            }

            List<string> Children(string u) => u.StartsWith("menu:", StringComparison.Ordinal) ? []
                : [.. RenderChildren(u).Concat(dialogsOf.GetValueOrDefault(u) ?? []).Concat(menusOf.GetValueOrDefault(u) ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

            var screenIds = LabelReport.Screens(g).ToList();
            var starts = screenIds.ToDictionary(s => s, Starts, StringComparer.Ordinal);
            var startParts = starts.Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);
            var screensOf = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var ownOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var s in screenIds)
            {
                foreach (var u in Reach(starts[s], Children))
                {
                    if (!screensOf.TryGetValue(u, out var ss)) screensOf[u] = ss = new(StringComparer.Ordinal);
                    ss.Add(s);
                }
                ownOf[s] = Reach(starts[s], RenderChildren);
            }

            // ---- folding (D334 item 3): a part rendered by exactly one other part folds into it, recursively --------------------
            var parents = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var p in partIds)
                foreach (var c in RenderChildren(p))
                {
                    if (!parents.TryGetValue(c, out var ps)) parents[c] = ps = new(StringComparer.Ordinal);
                    ps.Add(p);
                }
            string? FoldParent(string p) => !dialogs.Contains(p) && !startParts.Contains(p) && parents.TryGetValue(p, out var ps) && ps.Count == 1 ? ps.Min : null;
            string RootOf(string p)
            {
                var chain = new List<string> { p };
                var seen = new HashSet<string>(StringComparer.Ordinal) { p };
                for (var up = FoldParent(p); up is not null; up = FoldParent(up))
                {
                    if (!seen.Add(up)) return chain[chain.IndexOf(up)..].Min(StringComparer.Ordinal)!;   // a loop of single parents: its least id
                    chain.Add(up);
                }
                return chain[^1];
            }

            var groups = new Dictionary<string, InventoryGroup>(StringComparer.Ordinal);
            foreach (var (part, lines) in partLines.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var rootPart = RootOf(part);
                if (!groups.TryGetValue(rootPart, out var group))
                {
                    var (title, fromCode) = PartTitle(rootPart);
                    groups[rootPart] = group = new InventoryGroup { Id = rootPart, Kind = dialogs.Contains(rootPart) ? "dialog" : "part", Title = title, TitleFromCode = fromCode };
                }
                group.Lines.AddRange(lines);
            }
            foreach (var (menuId, lines) in dataMenus)
            {
                var group = new InventoryGroup { Id = menuId, Kind = "menu", Title = menuTitle[menuId], TitleFromCode = true };
                group.Lines.AddRange(lines);
                groups[menuId] = group;
            }
            foreach (var group in groups.Values)
            {
                group.Lines.Sort(ByAt);
                group.Screens = [.. screensOf.GetValueOrDefault(group.Id) ?? []];
            }
            set.Dialogs = dialogs.Count;
            set.Menus = dataMenus.Count;
            set.MenusPlaced = dataMenus.Keys.Count(m => groups[m].Screens.Count > 0);

            // ---- placement (D328, D334 item 3) -------------------------------------------------------------------------------
            var screens = screenIds.Select(s => new InventoryScreen { Id = s, Title = labels.Screen(s) })
                .OrderBy(s => s.Line, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
            var byId = screens.ToDictionary(s => s.Id, StringComparer.Ordinal);
            static int ByTitle(InventoryGroup a, InventoryGroup b) { var c = string.CompareOrdinal(a.Title, b.Title); return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id); }
            var ordered = groups.Values.ToList();
            ordered.Sort(ByTitle);
            // A part reached from one screen only through a dialog sits in that dialog's section.
            var dialogClosure = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            HashSet<string> InDialog(string d) => dialogClosure.TryGetValue(d, out var c) ? c : dialogClosure[d] = Reach([d], RenderChildren);
            var placedIn = new Dictionary<string, InventoryGroup>(StringComparer.Ordinal);
            foreach (var group in ordered)
            {
                if (group.Screens.Count == 0) { set.NotPlaced.Add(group); continue; }
                if (group.Screens.Count >= 2)
                {
                    set.Shared.Add(group);
                    foreach (var s in group.Screens) byId[s].Pointers.Add(group);
                    continue;
                }
                var screen = byId[group.Screens[0]];
                if (group.Kind == "menu") screen.Menus.Add(group);
                else if (group.Kind == "dialog") screen.Dialogs.Add(group);
            }
            foreach (var group in ordered.Where(x => x.Screens.Count == 1 && x.Kind == "part"))
            {
                var screen = byId[group.Screens[0]];
                if (ownOf[screen.Id].Contains(group.Id)) { screen.Lines.AddRange(group.Lines); continue; }
                var host = screen.Dialogs.FirstOrDefault(d => InDialog(d.Id).Contains(group.Id));
                if (host is null && dialogs.Where(d => screensOf.TryGetValue(d, out var ds) && ds.Count == 1 && ds.Min == screen.Id).Order(StringComparer.Ordinal)
                        .FirstOrDefault(d => InDialog(d).Contains(group.Id)) is { } bare)
                {
                    // A dialog with no lines of its own whose parts hold some: it gets its section for them.
                    var (title, fromCode) = PartTitle(bare);
                    host = new InventoryGroup { Id = bare, Kind = "dialog", Title = title, TitleFromCode = fromCode, Screens = [screen.Id] };
                    screen.Dialogs.Add(host);
                    screen.Dialogs.Sort(ByTitle);
                }
                if (host is not null) { host.Lines.AddRange(group.Lines); host.Lines.Sort(ByAt); }
                else screen.Lines.AddRange(group.Lines);
            }
            foreach (var s in screens) s.Lines.Sort(ByAt);
            set.Screens.AddRange(screens);

            // ---- App frame (D362 (3)) ------------------------------------------------------------------------------------------
            // The root: a part no other part renders and no screen starts at, that code outside any class mounts (a file's
            // top level: Angular's bootstrapApplication, React's createRoot(...).render), or the server's layout view
            // (`_Layout`, the framework's own name). What it draws or opens, unplaced so far, is on every screen.
            var rendered = partIds.SelectMany(RenderChildren).ToHashSet(StringComparer.Ordinal);
            // A file mounts what it uses only when it calls the framework's mount function (React files use components at
            // their top level all the time: Sonarr had 50 "roots" without this), or, for an NgModule, names it in `bootstrap: [..]`.
            var mountFiles = new Dictionary<string, string?>(StringComparer.Ordinal);
            string? MountText(string file)
            {
                if (mountFiles.TryGetValue(file, out var t)) return t;
                var path = Path.Combine(root, file);
                var text = File.Exists(path) ? File.ReadAllText(path) : "";
                return mountFiles[file] = MountCall().IsMatch(text) || BootstrapList().IsMatch(text) ? text : null;
            }
            bool Mounted(Node n)
            {
                if (n.Kind == NodeKind.View) return Path.GetFileName(n.File ?? "") == "_Layout.cshtml";
                var name = FlowGraph.ShortName(n);
                foreach (var e in g.In(n.Id, EdgeKind.Uses).Concat(g.In(n.Id, EdgeKind.Renders)))
                {
                    if (!g.Nodes.TryGetValue(e.Source, out var src) || src.File is not { } file || MountText(file) is not { } text) continue;
                    if (src.Kind == NodeKind.File && MountCall().IsMatch(text)) return true;
                    if (BootstrapList().Matches(text).Any(m => Regex.IsMatch(m.Groups["list"].Value, $@"\b{Regex.Escape(name)}\b"))) return true;
                }
                return false;
            }
            set.Roots.AddRange(partIds.Where(p => !rendered.Contains(p) && !startParts.Contains(p) && !dialogs.Contains(p)
                && g.Nodes.TryGetValue(p, out var n) && !n.IsTest && n.Kind is NodeKind.Component or NodeKind.View && Mounted(n)));
            if (set.Roots.Count > 0)
            {
                // A part in the root's own file that nothing renders is the frame too: a router's layout element
                // (Sonarr's `<Route element={<PageLayout />}>` in App.tsx) is reached through the router, not a render line.
                var rootFiles = set.Roots.Select(r => g.Nodes[r].File).OfType<string>().ToHashSet(StringComparer.Ordinal);
                var layouts = partIds.Where(p => !rendered.Contains(p) && !startParts.Contains(p) && !dialogs.Contains(p) && !set.Roots.Contains(p)
                    && g.Nodes.TryGetValue(p, out var n) && !n.IsTest && n.Kind == NodeKind.Component && n.File is { } f && rootFiles.Contains(f)).ToList();
                var frame = Reach(set.Roots.Concat(layouts), Children);
                foreach (var group in set.NotPlaced.Where(x => frame.Contains(x.Id)).ToList())
                {
                    set.NotPlaced.Remove(group);
                    set.AppFrame.Add(group);
                }
            }

            // Server-defined fields drawn by a generic form from the server's list (D334 item 4): one group per class, Not placed.
            foreach (var (cls, lines) in serverClasses)
            {
                var group = new InventoryGroup { Id = "server:" + cls, Kind = "server-class", Title = Readable(ShortOf(cls)), TitleFromCode = true };
                group.Lines.AddRange(lines);
                group.Lines.Sort(ByAt);
                set.NotPlaced.Add(group);
            }
            set.NotPlaced.Sort((a, b) => { var c = string.CompareOrdinal(a.Kind == "server-class" ? "1" : "0", b.Kind == "server-class" ? "1" : "0"); return c != 0 ? c : ByTitle(a, b); });
            set.Skipped.Sort((a, b) => ByAt(a.Line, b.Line));
            set.SetAside.Sort((a, b) => { var c = string.CompareOrdinal(a.Fact.File, b.Fact.File); return c != 0 ? c : a.Fact.Line.CompareTo(b.Fact.Line); });
            return set;
        }

        [GeneratedRegex(@"\b(?:bootstrapApplication|bootstrapModule|createRoot|hydrateRoot|ReactDOM\.render)\s*\(")]
        private static partial Regex MountCall();
        [GeneratedRegex(@"\bbootstrap\s*:\s*\[(?<list>[^\]]*)\]")]
        private static partial Regex BootstrapList();

        static bool IsDisplayName(string attribute)
        {
            var a = attribute.EndsWith("Attribute", StringComparison.Ordinal) ? attribute[..^"Attribute".Length] : attribute;
            a = a[(a.LastIndexOf('.') + 1)..];
            return a is "DisplayName" or "Display" || a.EndsWith("DisplayName", StringComparison.Ordinal);
        }

        static string ShortOf(string id)
        {
            var hash = id.LastIndexOf('#');
            var s = hash >= 0 ? id[(hash + 1)..] : id;
            var bar = s.LastIndexOf('|');
            if (bar >= 0) s = s[(bar + 1)..];
            var cut = s.IndexOfAny(['(', '<', '`']);
            if (cut >= 0) s = s[..cut];
            if (s.Length > 2 && s[1] == ':') s = s[2..];
            return s[(s.LastIndexOf('.') + 1)..];
        }

        /// <summary>A part's title (D327): its own title facts, else its class or view name said readable, marked code.</summary>
        (string Title, bool FromCode) PartTitle(string part)
        {
            var code = CodeTitle(part);
            var named = labels.PartTitle(part, code);
            return named.Visible ? (named.Words!, false) : (Readable(code), true);
        }

        string CodeTitle(string part)
        {
            if (part.StartsWith("view:", StringComparison.Ordinal))
            {
                var file = Path.GetFileNameWithoutExtension(part["view:".Length..].Split('#')[0]).TrimStart('_');
                // A view component's default view is named after its folder (Components/<Name>/Default.cshtml).
                var dir = Path.GetFileName(Path.GetDirectoryName(part["view:".Length..]) ?? "");
                return file == "Default" && dir != "" ? dir : file;
            }
            var name = g.Nodes.TryGetValue(part, out var n) ? FlowGraph.ShortName(n) : ShortOf(part);
            if (name.EndsWith(".template", StringComparison.Ordinal)) name = name[..^".template".Length];
            return name.Length > "Component".Length && name.EndsWith("Component", StringComparison.Ordinal) ? name[..^"Component".Length] : name;
        }

        /// <summary>
        /// The part a fact's owner belongs to: a template → the component rendering it; a component or view → itself; a member of a
        /// component → that component; anything else → the owner itself (a part of its own, titled by its code name).
        /// </summary>
        string PartOf(string owner)
        {
            if (partOf.TryGetValue(owner, out var p)) return p;
            p = owner;
            if (g.Nodes.TryGetValue(owner, out var node))
            {
                if (node.Kind == NodeKind.Template)
                    p = g.In(owner, EdgeKind.Renders).Select(e => e.Source).Where(s => g.Nodes.TryGetValue(s, out var c) && c.Kind == NodeKind.Component).Order(StringComparer.Ordinal).FirstOrDefault() ?? owner;
                else if (node.Kind is not (NodeKind.Component or NodeKind.View))
                {
                    var container = g.In(owner, EdgeKind.Contains).Select(e => e.Source).FirstOrDefault(s => g.Nodes.TryGetValue(s, out var c) && c.Kind is NodeKind.Component)
                        ?? (Parent(owner) is { } up && g.Nodes.TryGetValue(up, out var c2) && c2.Kind == NodeKind.Component ? up : null);
                    if (container is not null) p = container;
                }
            }
            return partOf[owner] = p;
        }

        /// <summary>A TypeScript member's holder by its id (<c>ts|a.ts#A.f</c> → <c>ts|a.ts#A</c>); null for a top-level one or C#.</summary>
        static string? Parent(string id)
        {
            var hash = id.IndexOf('#', StringComparison.Ordinal);
            if (!id.StartsWith("ts|", StringComparison.Ordinal) || hash < 0) return null;
            var dot = id.LastIndexOf('.');
            return dot > hash ? id[..dot] : null;
        }

        /// <summary>
        /// The parts a part renders (D334 item 1): through its own template, child components and views; a view component (or the
        /// method rendering a view) stands for the views its methods render.
        /// </summary>
        List<string> RenderChildren(string part)
        {
            if (part.StartsWith("menu:", StringComparison.Ordinal)) return [];
            if (renderChildren.TryGetValue(part, out var list)) return list;
            var own = new List<string> { part };
            own.AddRange(g.Out(part, EdgeKind.Renders).Select(e => e.Target).Where(t => g.Nodes.TryGetValue(t, out var n) && n.Kind == NodeKind.Template));
            var result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var source in own)
                foreach (var t in g.Out(source, EdgeKind.Renders).Select(e => e.Target))
                {
                    if (t == part || own.Contains(t, StringComparer.Ordinal) || !g.Nodes.TryGetValue(t, out var n) || n.IsTest) continue;
                    if (n.Kind is NodeKind.Component or NodeKind.View) result.Add(t);
                    else if (n.Kind is NodeKind.Type or NodeKind.Method)
                        foreach (var m in g.Out(t, EdgeKind.Contains).Select(e => e.Target).Prepend(t))
                            foreach (var v in g.Out(m, EdgeKind.Renders).Select(e => e.Target))
                                if (v != part && g.Nodes.TryGetValue(v, out var vn) && vn.Kind == NodeKind.View && !vn.IsTest) result.Add(v);
                }
            return renderChildren[part] = [.. result];
        }

        /// <summary>The parts a screen starts at: a page's routed components; an MVC action's views.</summary>
        List<string> Starts(string screen)
        {
            if (!g.Nodes.TryGetValue(screen, out var node)) return [];
            var kind = node.Kind == NodeKind.Page ? EdgeKind.RoutesTo : EdgeKind.Renders;
            return [.. g.Out(screen, kind).Select(e => e.Target).Where(t => g.Nodes.TryGetValue(t, out var n) && !n.IsTest && n.Kind is NodeKind.Component or NodeKind.View)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        }

        static HashSet<string> Reach(IEnumerable<string> from, Func<string, List<string>> next)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var todo = new Stack<string>(from.Reverse());
            while (todo.TryPop(out var u))
                if (seen.Add(u))
                    foreach (var c in next(u)) if (!seen.Contains(c)) todo.Push(c);
            return seen;
        }

        /// <summary>
        /// D334 item 1's walk: the part itself, its own members, and the frontend points they reach through <c>calls</c> lines within
        /// <see cref="Hops"/> hops. Only frontend code (<c>ts|</c>): a server-rendered view has no walk.
        /// </summary>
        HashSet<string> Walk(string part)
        {
            if (walks.TryGetValue(part, out var seen)) return seen;
            seen = new HashSet<string>(StringComparer.Ordinal);
            if (part.StartsWith("ts|", StringComparison.Ordinal) && g.Nodes.TryGetValue(part, out var node) && node.Kind == NodeKind.Component)
            {
                members ??= g.Nodes.Values.Where(n => !n.IsTest && Parent(n.Id) is not null).ToLookup(n => Parent(n.Id)!, n => n.Id, StringComparer.Ordinal);
                var frontier = members[part].Concat(g.Out(part, EdgeKind.Contains).Select(e => e.Target)).Append(part)
                    .Where(id => !g.IsTest(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                seen.UnionWith(frontier);
                for (var hop = 1; hop <= Hops && frontier.Count > 0; hop++)
                {
                    frontier = [.. frontier.SelectMany(id => g.Out(id, EdgeKind.Calls)).Select(e => e.Target)
                        .Where(t => t.StartsWith("ts|", StringComparison.Ordinal) && !g.IsTest(t) && !seen.Contains(t)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                    seen.UnionWith(frontier);
                }
            }
            return walks[part] = seen;
        }

        /// <summary>
        /// Dialogs opened from code (D334 item 1): components a method on the walk holds a <c>uses</c> line to, which the part doesn't
        /// render itself.
        /// </summary>
        List<string> Dialogs(string part)
        {
            var walk = Walk(part);
            if (walk.Count == 0) return [];
            var rendered = RenderChildren(part).ToHashSet(StringComparer.Ordinal);
            return [.. walk.Where(id => g.Nodes.TryGetValue(id, out var n) && n.Kind == NodeKind.Method)
                .SelectMany(id => g.Out(id, EdgeKind.Uses)).Select(e => e.Target)
                .Where(t => t != part && !rendered.Contains(t) && g.Nodes.TryGetValue(t, out var n) && n.Kind == NodeKind.Component && !n.IsTest)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        }

        /// <summary>
        /// A data menu's holder and readers (D334 item 2): the member the list is assigned to when its code says so (<c>this.seriesActions = [</c>,
        /// <c>const GROUPS = [</c>) and that member is in the graph, else the member holding the facts. A method holder is its own reader;
        /// any other is read by whatever holds a <c>uses</c> line to it, its builder aside.
        /// </summary>
        (string Holder, HashSet<string> Readers) Holder(string owner, int firstLine, string file)
        {
            var holder = owner;
            if (owner.StartsWith("ts|", StringComparison.Ordinal))
            {
                var lines = g.Lines(file);
                for (var l = firstLine; l >= Math.Max(1, firstLine - 3) && l <= lines.Length; l--)
                {
                    var m = ListAssigned().Match(lines[l - 1]);
                    if (!m.Success) continue;
                    var name = m.Groups[1].Value;
                    var hash = owner.IndexOf('#', StringComparison.Ordinal);
                    var candidates = new[] { Parent(owner) is { } up ? $"{up}.{name}" : null, hash >= 0 ? $"{owner[..(hash + 1)]}{name}" : null };
                    if (candidates.FirstOrDefault(c => c is not null && g.Nodes.ContainsKey(c)) is { } found) holder = found;
                    break;
                }
            }
            var readers = new HashSet<string>(StringComparer.Ordinal);
            if (!g.Nodes.TryGetValue(holder, out var hn) || hn.Kind == NodeKind.Method) readers.Add(holder);
            else
            {
                readers.UnionWith(g.In(holder, EdgeKind.Uses).Select(e => e.Source).Where(s => s != owner && !g.IsTest(s)));
                if (readers.Count == 0) readers.Add(holder);
            }
            return (holder, readers);
        }
    }
}
