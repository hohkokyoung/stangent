using System.Text.Json;
using Stangent.Graph;
using Stangent.Graph.Detectors;

namespace Stangent.Flows;

/// <summary>The settings a flow is built with (D172, D176); written into every flow file, so a rebuild with others differs.</summary>
public sealed record FlowSettings(int HubCallers = 10, int Depth = 8);

/// <summary>A condition around a call or exit, quoted exactly (D174): its kind, its code text and its line.</summary>
public sealed record Condition(string Kind, string Code, string File, int Line, string? Case = null);

/// <summary>The graph, indexed for the walk. Read only; built once per build.</summary>
internal sealed partial class FlowGraph
{
    public CodeGraph Graph { get; }
    public string Root { get; }
    public Dictionary<string, Node> Nodes { get; }
    readonly ILookup<(string, string), Edge> outEdges;
    readonly ILookup<(string, string), Edge> inEdges;
    readonly ILookup<(string, string), Fact> factsByOwner;
    readonly Dictionary<string, HashSet<string>> callerSets;
    public ILookup<string, BlindSpot> BlindByOwner { get; }
    /// <summary>Event type → the methods handling it (<c>handle</c> facts).</summary>
    public ILookup<string, string> HandlersByEvent { get; }
    /// <summary>D201: every method that handles an event (<c>handle</c> facts).</summary>
    public HashSet<string> HandlerPoints { get; }
    /// <summary>D201: the interfaces handlers implement (<c>handle</c> facts' <c>interface</c>: <c>IConsumer`1</c>, <c>IHandleAsync`1</c>).</summary>
    public HashSet<string> HandlerInterfaces { get; }
    /// <summary>Command type → the job methods running it (<c>is-job</c> facts of kind <c>command</c>).</summary>
    public ILookup<string, string> JobsByCommand { get; }
    /// <summary>D191: a script's own functions by their global name (<c>AjaxCart.send_ajax</c>), for a view's onclick to call.</summary>
    public ILookup<string, string> ScriptPoints { get; }
    /// <summary>Entity type → the table that maps it.</summary>
    public Dictionary<string, string> TableOf { get; }
    readonly Dictionary<string, string[]> fileLines = new(StringComparer.Ordinal);
    readonly ILookup<string, Fact> rulesByType;

    public FlowGraph(CodeGraph graph, string root)
    {
        Graph = graph;
        Root = root;
        Nodes = graph.AllNodes.GroupBy(n => n.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var edges = graph.AllEdges.ToList();
        outEdges = edges.ToLookup(e => (e.Source, e.Kind));
        inEdges = edges.ToLookup(e => (e.Target, e.Kind));
        var facts = graph.AllFacts.ToList();
        factsByOwner = facts.ToLookup(f => (f.Owner, f.Kind));
        // A hub is counted by the distinct points outside tests that call it (D172).
        callerSets = edges.Where(e => e.Kind == EdgeKind.Calls && !IsTest(e.Source))
            .GroupBy(e => e.Target, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(e => e.Source).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        BlindByOwner = graph.AllBlindSpots.ToLookup(b => b.Owner, StringComparer.Ordinal);
        HandlersByEvent = facts.Where(f => f.Kind == "handle").OrderBy(f => f.Owner, StringComparer.Ordinal).ToLookup(f => f["event"], f => f.Owner, StringComparer.Ordinal);
        HandlerPoints = facts.Where(f => f.Kind == "handle").Select(f => f.Owner).ToHashSet(StringComparer.Ordinal);
        HandlerInterfaces = facts.Where(f => f.Kind == "handle" && f["interface"] != "").Select(f => f["interface"]).ToHashSet(StringComparer.Ordinal);
        JobsByCommand = facts.Where(f => f.Kind == "is-job" && f["kind"] == "command" && f["command"] != "").ToLookup(f => f["command"], f => f.Owner, StringComparer.Ordinal);
        rulesByType = facts.Where(f => f.Kind == "rule" && DocIds.ContainingType(f.Owner) is not null)
            .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal)
            .ToLookup(f => DocIds.ContainingType(f.Owner)!, StringComparer.Ordinal);
        ScriptPoints = Nodes.Values.Where(n => n.Kind == NodeKind.Method && n.Id.StartsWith("ts|", StringComparison.Ordinal) && !n.IsTest && n.File is not null && n.File.EndsWith(".js", StringComparison.Ordinal))
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToLookup(n => n.Name, n => n.Id, StringComparer.Ordinal);
        TableOf = edges.Where(e => e.Kind == EdgeKind.Maps && Nodes.TryGetValue(e.Source, out var t) && t.Kind == NodeKind.Table)
            .GroupBy(e => e.Target, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Nodes[g.Select(e => e.Source).Order(StringComparer.Ordinal).First()].Name, StringComparer.Ordinal);
    }

    public bool IsTest(string id) => Nodes.TryGetValue(id, out var n) && n.IsTest;
    public IEnumerable<Edge> Out(string id, string kind) => outEdges[(id, kind)];
    public IEnumerable<Edge> In(string id, string kind) => inEdges[(id, kind)];
    public IEnumerable<Fact> Facts(string owner, string kind) => factsByOwner[(owner, kind)];
    public IEnumerable<Fact> AllFacts(string kind) => Graph.AllFacts.Where(f => f.Kind == kind);

    // ---- screens a redirect or navigation opens (D204) ----------------------------------------------------------------------

    List<(string Id, string[] Segments)>? pages;
    ILookup<string, Fact>? routesByName;
    ILookup<(string, string), Fact>? actionsByName;

    /// <summary>A screen's name in a link: a page's route, or a controller action's own name.</summary>
    public string ScreenName(string id) => id.StartsWith("page:", StringComparison.Ordinal) ? id[(id.IndexOf('|') + 1)..] : Name(id);

    /// <summary>
    /// The frontend pages a navigation's target fits, read from its code: a quoted path (<c>'/home'</c>, a template's <c>${…}</c>
    /// fitting any one segment), or an array of segments (<c>['/library', id, 'series', seriesId]</c>, a value fitting any one).
    /// A page's <c>:param</c> fits any segment. A target made at runtime (a variable), or a relative one: none. Only
    /// the pages of the frontend whose folder holds <paramref name="file"/>.
    /// </summary>
    public IReadOnlyList<string> PagesAt(string code, string file, bool fromRoot = false)
    {
        pages ??= [.. Nodes.Keys.Where(k => k.StartsWith("page:", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(k => (k, k[(k.IndexOf('|') + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries)))];
        var segments = PathSegments(code.Trim(), fromRoot);
        if (segments is null) return [];
        // Only the pages of the navigating code's own frontend: the one whose project folder holds its file.
        var own = pages.Where(p => Frontend(p.Id) is var dir && (dir == "" || file.StartsWith(dir + "/", StringComparison.Ordinal))).ToList();
        return [.. own.Where(p => p.Segments.Length == segments.Count
            && p.Segments.Zip(segments).All(x => x.First.StartsWith(':') || x.Second is null || x.First == x.Second)).Select(p => p.Id)];
    }

    static string Frontend(string page)
    {
        var config = page["page:".Length..page.IndexOf('|')];
        var slash = config.LastIndexOf('/');
        return slash < 0 ? "" : config[..slash];
    }

    /// <summary>
    /// A path's segments from its code, null for a value fitting any one segment: a quoted path, a template (<c>${…}</c>), quoted
    /// parts joined with <c>+</c>, or an array of such parts. Not a constant path (a variable, a call), or not from the root
    /// (no leading slash, unless <paramref name="fromRoot"/>): null.
    /// </summary>
    static List<string?>? PathSegments(string code, bool fromRoot)
    {
        const char Any = '\u0001';
        static List<string> Split(string code, char by)
        {
            var depth = 0; var start = 0; var quote = '\0'; var items = new List<string>();
            for (var i = 0; i < code.Length; i++)
            {
                var c = code[i];
                if (quote != '\0') { if (c == quote && code[i - 1] != '\\') quote = '\0'; continue; }
                if (c is '\'' or '"' or '`') quote = c;
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == by && depth == 0) { items.Add(code[start..i].Trim()); start = i + 1; }
            }
            items.Add(code[start..].Trim());
            return items;
        }
        // A part's text, a value made at runtime standing as `Any`; null when the part holds no quoted text at all.
        static string? Text(string part)
        {
            var pieces = Split(part, '+');
            var text = "";
            var quoted = false;
            foreach (var piece in pieces)
                if (piece.Length >= 2 && piece[0] is '\'' or '"' or '`' && piece[^1] == piece[0])
                {
                    quoted = true;
                    text += piece[0] == '`' ? System.Text.RegularExpressions.Regex.Replace(piece[1..^1], @"\$\{[^}]*\}", Any.ToString()) : piece[1..^1];
                }
                else text += Any;
            return quoted ? text : null;
        }
        string path;
        if (code.Length >= 2 && code[0] == '[' && code[^1] == ']')
        {
            var items = Split(code[1..^1], ',');
            if (items.Count == 0 || items[0] == "" || Text(items[0]) is not { } first) return null;
            path = string.Join("/", new[] { first }.Concat(items.Skip(1).Select(i => Text(i) ?? Any.ToString())));
        }
        else if (Text(code) is { } t) path = t;
        else return null;
        if (!path.StartsWith('/') && !fromRoot) return null;
        return [.. path.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Contains(Any) ? null : x)];
    }

    /// <summary>
    /// The actions a server-side redirect opens, from its exit fact's <c>route</c>, or <c>controller</c> and <c>action</c> (D204): a
    /// named conventional route's controller, action and area, then the actions serving GET with those names in that area (a
    /// redirect to an action: the area layer 2 read), those in the redirecting
    /// code's own project first. The text naming the target, and the actions (none when the target is made at runtime).
    /// </summary>
    public (string To, IReadOnlyList<string> Screens) RedirectTarget(Fact exit, string from)
    {
        routesByName ??= AllFacts("conventional").Where(f => f["name"] != "").ToLookup(f => f["name"], StringComparer.Ordinal);
        actionsByName ??= AllFacts("mvc-action").ToLookup(f => (f["controller"], f["action"]));
        var (controller, action, area) = (exit["controller"], exit["action"], exit.Data.TryGetValue("area", out var a) ? a : null);
        var to = exit["route"] != "" ? $"the route {exit["route"]}" : action != "" ? $"{controller}/{action}" : exit["url"] != "" ? exit["url"] : "";
        if (exit["route"] is { Length: > 0 } route)
        {
            var named = routesByName[route].Select(f => (f["controller"], f["action"], f["area"])).Distinct().ToList();
            if (named.Count != 1) return (to, []);
            (controller, action, area) = named[0];
        }
        if (controller == "" || action == "") return (to, []);
        var gets = actionsByName[(controller, action)].Where(f => f["verbs"] == "" || f["verbs"].Split('|', ',', ' ').Contains("GET", StringComparer.Ordinal))
            .Where(f => area is null || f["area"] == area).Select(f => f.Owner).Where(Nodes.ContainsKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var project = Nodes.TryGetValue(from, out var n) ? n.Project : null;
        var own = gets.Where(id => Nodes[id].Project == project).ToList();
        return (to, own.Count > 0 ? own : gets);
    }
    /// <summary>The validation rules on a type's own properties.</summary>
    public IEnumerable<Fact> AllRules(string type) => rulesByType[type];
    /// <summary>
    /// How many places call it (D172's hub test): the distinct points outside tests calling it, or calling an interface or base
    /// member that runs it, since a call written to the interface is a call to each implementation.
    /// </summary>
    public int Callers(string id)
    {
        if (callerCount.TryGetValue(id, out var n)) return n;
        var all = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Up(string m)
        {
            if (!seen.Add(m)) return;
            if (callerSets.TryGetValue(m, out var set)) all.UnionWith(set);
            foreach (var e in In(m, EdgeKind.DispatchesTo)) Up(e.Source);
        }
        Up(id);
        return callerCount[id] = all.Count;
    }

    readonly Dictionary<string, int> callerCount = new(StringComparer.Ordinal);

    /// <summary>A type's own name, without its namespace: <c>SeriesAddedEvent</c>.</summary>
    public string TypeName(string id)
    {
        var name = Name(id);
        var lt = name.IndexOf('<');
        var head = lt >= 0 ? name[..lt] : name;
        var dot = head.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    public string Name(string id) => Nodes.TryGetValue(id, out var n) ? n.Name : id;

    /// <summary>A file's text lines at the build's commit, read once; empty when the file isn't there.</summary>
    public string[] Lines(string? file)
    {
        if (string.IsNullOrEmpty(file)) return [];
        if (fileLines.TryGetValue(file, out var lines)) return lines;
        var path = Path.Combine(Root, file);
        lines = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n").Split('\n') : [];
        fileLines[file] = lines;
        return lines;
    }

    /// <summary>The code on one line, trimmed: the proof a flow quotes (D173).</summary>
    public string Code(string? file, int line)
    {
        var lines = Lines(file);
        return line >= 1 && line <= lines.Length ? lines[line - 1].Trim() : "";
    }

    /// <summary>
    /// The line a declaration is named on, inside its span: the first line holding its name followed by <c>(</c> (a method), or
    /// its name (anything else). The stored span starts at its attributes, which aren't what a reader quotes.
    /// </summary>
    public int NameLine(Node node)
    {
        if (node.File is null || node.Line <= 0) return node.Line;
        var name = ShortName(node);
        var lines = Lines(node.File);
        var end = Math.Min(node.EndLine > 0 ? node.EndLine : node.Line, lines.Length);
        for (var l = node.Line; l <= end; l++)
            if (lines[l - 1].Contains(name + "(", StringComparison.Ordinal) || lines[l - 1].Contains(name + "<", StringComparison.Ordinal)) return l;
        for (var l = node.Line; l <= end; l++)
            if (lines[l - 1].Contains(name, StringComparison.Ordinal) && !lines[l - 1].TrimStart().StartsWith('[')) return l;
        return node.Line;
    }

    /// <summary>The member's own name: <c>SeriesService.AddSeries</c> → <c>AddSeries</c>; a constructor → its type's name.</summary>
    public static string ShortName(Node node)
    {
        var name = node.Name;
        if (name.EndsWith(".ctor", StringComparison.Ordinal)) name = name[..^5];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    /// <summary>The conditions a fact carries (D174), innermost first, in the owner's file.</summary>
    public static List<Condition> ConditionsOf(Fact fact)
    {
        var json = fact["conditions"];
        if (json == "") return [];
        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.EnumerateArray().Select(c => new Condition(
            c.GetProperty("kind").GetString()!, c.GetProperty("text").GetString()!, fact.File ?? "",
            c.TryGetProperty("line", out var l) ? l.GetInt32() : fact.Line,
            c.TryGetProperty("case", out var k) ? k.GetString() : null))];
    }

    /// <summary>
    /// Where a call written to <paramref name="target"/> runs (D172's hops, which don't count as calls): an interface or virtual
    /// member → each implementation, the member itself too when it has a body of its own. Several → one branch each, 🟡.
    /// </summary>
    public List<string> Dispatch(string target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        void Go(string id)
        {
            if (!seen.Add(id)) return;
            var impls = Out(id, EdgeKind.DispatchesTo).Select(e => e.Target).Where(t => !IsTest(t)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (impls.Count == 0 || HasBody(id)) result.Add(id);
            foreach (var i in impls) Go(i);
        }
        Go(target);
        return [.. result.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Code of its own: anything it calls or does is written in it.</summary>
    public bool HasBody(string id) =>
        Facts(id, "call-site").Any() || Facts(id, "access").Any() || Facts(id, "exit").Any() || Facts(id, "call").Any() || Facts(id, "http-match").Any()
        || Facts(id, "queue").Any() || Facts(id, "job").Any() || Facts(id, "on-load").Any();

    public static string? ConstructedType(string id) => DocIds.ConstructedType(id);

    /// <summary>A field or property whose value is a function written in place: <c>x = (a) =&gt; …</c>, <c>x = async a =&gt; …</c>, <c>x = function …</c>.</summary>
    public bool HoldsFunction(Node node)
    {
        var lines = Lines(node.File);
        if (node.Line < 1 || node.Line > lines.Length) return false;
        var end = Math.Min(Math.Max(node.EndLine, node.Line), Math.Min(lines.Length, node.Line + 3));
        return FunctionValue().IsMatch(string.Join(" ", lines[(node.Line - 1)..end]));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"=\s*(async\s+)?(function\b|(\((?:[^()]|\([^()]*\))*\)|[A-Za-z_$][\w$]*)\s*(:[^=;]*?)?=>)")]
    private static partial System.Text.RegularExpressions.Regex FunctionValue();
}
