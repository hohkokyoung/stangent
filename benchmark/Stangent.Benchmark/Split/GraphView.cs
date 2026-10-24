using Stangent.Graph;

namespace Stangent.Benchmark.Split;

/// <summary>What owns a changed line: the innermost member covering it, or, outside members, the innermost type.</summary>
sealed record LineOwner(IReadOnlyList<string> Members, string? Type);

/// <summary>
/// One graph (the change's or its parent's) seen the way the split check needs it (D48): which point owns a
/// line, which type contains a point, and which points are within N lines of another, in either direction.
/// Points are keyed without their project, so a file compiled into several projects is one member.
/// </summary>
sealed class GraphView
{
    static readonly HashSet<string> MemberKinds = [NodeKind.Method, NodeKind.Property, NodeKind.Field, NodeKind.Component, NodeKind.Template];

    readonly Dictionary<string, List<Node>> pointsByFile = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> idsByKey = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> neighbours = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> containerOf = new(StringComparer.Ordinal);
    readonly HashSet<string> typeIds = new(StringComparer.Ordinal);
    readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);

    public GraphView(CodeGraph graph)
    {
        foreach (var n in graph.AllNodes.DistinctBy(n => n.Id))
        {
            Add(idsByKey, KeyOf(n.Id), n.Id);
            nodes[n.Id] = n;
            if (n.Kind == NodeKind.Type) typeIds.Add(n.Id);
            if (n.File is null || n.Line <= 0 || !(MemberKinds.Contains(n.Kind) || n.Kind == NodeKind.Type)) continue;
            Add(pointsByFile, n.File, n);
        }
        foreach (var e in graph.AllEdges)
        {
            if (e.Kind == EdgeKind.Contains) { if (typeIds.Contains(e.Source)) containerOf.TryAdd(e.Target, e.Source); continue; }
            // ✅ and 🟡 lines a change travels along (📜 lines live apart from these and are never walked, D40).
            if (e.Label > Label.Guessed || !(EdgeKind.Propagating.Contains(e.Kind) || EdgeKind.FirstStepOnly.Contains(e.Kind))) continue;
            Add(neighbours, e.Source, e.Target);
            Add(neighbours, e.Target, e.Source);
        }
        foreach (var list in neighbours.Values) { list.Sort(StringComparer.Ordinal); }
    }

    static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(value);
    }

    /// <summary><c>Asm|M:Ns.T.Run(…)</c> → <c>M:Ns.T.Run(…)</c>; <c>ts|a.ts#F</c> → <c>a.ts#F</c>.</summary>
    public static string KeyOf(string id) => id.IndexOf('|') is var i and >= 0 ? id[(i + 1)..] : id;

    public bool Has(string key) => idsByKey.ContainsKey(key);

    /// <summary>The owner of 1-based line <paramref name="line"/> of <paramref name="file"/>.</summary>
    public LineOwner Owner(string file, int line)
    {
        if (!pointsByFile.TryGetValue(file, out var points)) return new([], null);
        var covering = points.Where(p => p.Line <= line && line <= Math.Max(p.Line, p.EndLine)).ToList();
        var types = covering.Where(p => p.Kind == NodeKind.Type).ToList();
        var type = types.Count == 0 ? null : types.Where(p => p.EndLine - p.Line == types.Min(t => t.EndLine - t.Line)).MinBy(p => KeyOf(p.Id), StringComparer.Ordinal);
        var members = covering.Where(p => MemberKinds.Contains(p.Kind)).ToList();
        if (members.Count > 0)
        {
            var span = members.Min(p => p.EndLine - p.Line);
            var innermost = members.Where(p => p.EndLine - p.Line == span).ToList();
            // A positional record's properties are declared on its header line: that line is the type's.
            if (!(type is not null && line == type.Line && innermost.All(p => p.Line == type.Line)))
                return new([.. innermost.Select(p => KeyOf(p.Id)).Distinct().Order(StringComparer.Ordinal)], null);
        }
        return new([], type is null ? null : KeyOf(type.Id));
    }

    /// <summary>
    /// A name that survives a move: a type's name without its namespace, a member's <c>Type.Member</c>. Null when the key
    /// isn't a type or member point.
    /// </summary>
    public string? MoveName(string key)
    {
        if (!idsByKey.TryGetValue(key, out var ids)) return null;
        var node = nodes[ids[0]];
        if (node.Kind == NodeKind.Type)
        {
            var name = node.Name.Split('<')[0];
            return "type " + name[(name.LastIndexOf('.') + 1)..] + node.Name[name.Length..];
        }
        return MemberKinds.Contains(node.Kind) ? node.Kind + " " + node.Name : null;
    }

    /// <summary>The type that declares this point (by key), if any.</summary>
    public string? ContainerType(string key) =>
        idsByKey.TryGetValue(key, out var ids) ? ids.Select(id => containerOf.GetValueOrDefault(id)).OfType<string>().Select(KeyOf).Order(StringComparer.Ordinal).FirstOrDefault() : null;

    /// <summary>Keys of every point within <paramref name="depth"/> lines of this one, either direction, through any point.</summary>
    public HashSet<string> Reach(string key, int depth)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        if (!idsByKey.TryGetValue(key, out var starts)) return reached;
        var seen = new HashSet<string>(starts, StringComparer.Ordinal);
        var frontier = starts.ToList();
        for (var d = 0; d < depth && frontier.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var id in frontier)
                foreach (var n in neighbours.GetValueOrDefault(id) ?? [])
                    if (seen.Add(n)) { next.Add(n); reached.Add(KeyOf(n)); }
            frontier = next;
        }
        reached.Remove(key);
        return reached;
    }
}
