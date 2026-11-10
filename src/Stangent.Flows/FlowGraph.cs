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
