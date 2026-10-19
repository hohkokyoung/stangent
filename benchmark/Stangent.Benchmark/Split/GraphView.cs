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

    public GraphView(CodeGraph graph)
    {
        foreach (var n in graph.AllNodes.DistinctBy(n => n.Id))
        {
            Add(idsByKey, KeyOf(n.Id), n.Id);
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
        var members = covering.Where(p => MemberKinds.Contains(p.Kind)).ToList();
        if (members.Count > 0)
        {
            var span = members.Min(p => p.EndLine - p.Line);
            return new([.. members.Where(p => p.EndLine - p.Line == span).Select(p => KeyOf(p.Id)).Distinct().Order(StringComparer.Ordinal)], null);
        }
        var types = covering.Where(p => p.Kind == NodeKind.Type).ToList();
        if (types.Count == 0) return new([], null);
        var typeSpan = types.Min(p => p.EndLine - p.Line);
        return new([], types.Where(p => p.EndLine - p.Line == typeSpan).Select(p => KeyOf(p.Id)).Order(StringComparer.Ordinal).First());
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
