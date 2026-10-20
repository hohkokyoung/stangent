using Stangent.Graph;

namespace Stangent.Spec.Pages;

/// <summary>
/// A point in a page's slice. <see cref="Role"/> is <c>walk</c> for points reached by the walk, <c>stop</c> for a shared
/// component the walk links to but doesn't enter (it has its own page), and <c>handler</c> or <c>table</c> for side
/// effects: event handlers triggered by, and tables mapped to, what the walk reached (one step back, never walked).
/// </summary>
public sealed record SliceNode(Node Node, Label Label, int Depth, string Role);

public sealed record SliceEdge(string Source, string Target, string Kind, Label Label);

/// <summary>What a page is written from (D54): the walk from its entry point, within the depth limit.</summary>
public sealed class Slice
{
    public required string Entry { get; init; }
    /// <summary>How many lines deep the walk went.</summary>
    public required int Depth { get; init; }
    public required IReadOnlyList<SliceNode> Nodes { get; init; }
    public required IReadOnlyList<SliceEdge> Edges { get; init; }
    /// <summary>❓ blind spots in the slice's points: what the page couldn't see.</summary>
    public required IReadOnlyList<BlindSpot> Unknowns { get; init; }
    /// <summary>Points one line past the depth limit: what the slice was cut off from.</summary>
    public required int CutOff { get; init; }

    HashSet<string>? ids;
    public IReadOnlySet<string> Ids => ids ??= Nodes.Select(n => n.Node.Id).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Walks forward from an entry point: from the dependent to what it depends on, the opposite of an impact walk, along
/// the same lines (<see cref="EdgeKind.Propagating"/>) except those that point back at callers (<c>tests</c>,
/// <c>triggered-by</c>, <c>maps</c>). Pure code over the graph: the same graph gives the same slice.
/// </summary>
public sealed class SliceBuilder
{
    /// <summary>Lines followed forward. <c>triggered-by</c> and <c>maps</c> point from a handler or table back at the
    /// code, so they're read one step back as side effects instead.</summary>
    public static readonly HashSet<string> Forward =
        [.. EdgeKind.Propagating.Except([EdgeKind.Tests, EdgeKind.TriggeredBy, EdgeKind.Maps])];

    readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<Edge>> outgoing;
    readonly ILookup<string, Edge> triggeredBy;
    readonly ILookup<string, Edge> mappedBy;
    readonly ILookup<string, BlindSpot> blindByOwner;

    /// <param name="hubCallers">Points with this many distinct callers or more are leaves: included, not walked into (0: off).</param>
    public SliceBuilder(CodeGraph graph, int hubCallers = 0)
    {
        foreach (var n in graph.AllNodes) nodes.TryAdd(n.Id, n);
        var edges = GraphStore.SortEdges(graph.AllEdges).ToList();
        outgoing = edges.Where(e => Forward.Contains(e.Kind)).GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        triggeredBy = edges.Where(e => e.Kind == EdgeKind.TriggeredBy).ToLookup(e => e.Target, StringComparer.Ordinal);
        mappedBy = edges.Where(e => e.Kind == EdgeKind.Maps).ToLookup(e => e.Target, StringComparer.Ordinal);
        blindByOwner = graph.AllBlindSpots.ToLookup(b => b.Owner, StringComparer.Ordinal);
        var hub = hubCallers > 0 ? hubCallers : int.MaxValue;
        // Widely shared helpers (logging, translate(), base services) would pull half the code base into every slice;
        // the code calling them says what they're for.
        Hubs = edges.Where(e => Forward.Contains(e.Kind) && e.Kind != EdgeKind.Uses).GroupBy(e => e.Target)
            .Where(g => g.Select(e => e.Source).Distinct().Count() >= hub).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        Handlers = triggeredBy.SelectMany(g => g).Select(e => e.Source).Where(nodes.ContainsKey).ToHashSet(StringComparer.Ordinal);
    }

    public Node? Find(string id) => nodes.GetValueOrDefault(id);
    public IEnumerable<Node> AllNodes => nodes.Values;

    /// <summary>Event and command handlers: the sources of <c>triggered-by</c> lines (backend entry points, D54).</summary>
    public IReadOnlySet<string> Handlers { get; }
    /// <summary>Points with many callers: included in a slice but never walked into.</summary>
    public IReadOnlySet<string> Hubs { get; }

    /// <param name="stops">Points linked to but not entered (shared components with their own pages).</param>
    /// <param name="stopKinds">Kinds of points linked to but not entered (endpoints, when they have pages of their own).</param>
    public Slice Build(string entry, int maxDepth, IReadOnlySet<string>? stops = null, IReadOnlySet<string>? stopKinds = null)
    {
        stops ??= new HashSet<string>();
        if (stopKinds is { Count: > 0 })
            stops = stops.Concat(nodes.Values.Where(n => stopKinds.Contains(n.Kind)).Select(n => n.Id)).ToHashSet(StringComparer.Ordinal);
        var certain = Bfs(entry, maxDepth, stops, certainOnly: true);
        var all = Bfs(entry, maxDepth, stops, certainOnly: false);
        var result = new Dictionary<string, SliceNode>(StringComparer.Ordinal);
        foreach (var (id, depth) in all)
        {
            var label = certain.ContainsKey(id) ? Label.Certain : Label.Guessed;
            var role = id != entry && stops.Contains(id) ? "stop" : "walk";
            result[id] = new SliceNode(nodes[id], label, certain.TryGetValue(id, out var d) ? d : depth, role);
        }

        // Side effects, one step back from what the walk reached: handlers of events it publishes, tables it maps to.
        foreach (var walked in result.Values.Where(n => n.Role == "walk").OrderBy(n => n.Node.Id, StringComparer.Ordinal).ToList())
        {
            foreach (var e in triggeredBy[walked.Node.Id])
                if (!result.ContainsKey(e.Source) && nodes.TryGetValue(e.Source, out var h))
                    result[e.Source] = new SliceNode(h, e.Label, walked.Depth + 1, "handler");
            foreach (var e in mappedBy[walked.Node.Id])
                if (!result.ContainsKey(e.Source) && nodes.TryGetValue(e.Source, out var t))
                    result[e.Source] = new SliceNode(t, e.Label, walked.Depth + 1, "table");
        }

        var ids = result.Keys.ToHashSet(StringComparer.Ordinal);
        var sliceEdges = new List<SliceEdge>();
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            if (result[id].Role != "walk") continue;
            foreach (var e in outgoing.GetValueOrDefault(id) ?? [])
                if (ids.Contains(e.Target)) sliceEdges.Add(new SliceEdge(e.Source, e.Target, e.Kind, e.Label));
            foreach (var e in triggeredBy[id].Concat(mappedBy[id]))
                if (ids.Contains(e.Source)) sliceEdges.Add(new SliceEdge(e.Source, e.Target, e.Kind, e.Label));
        }

        var cutOff = result.Values.Where(n => n.Role == "walk" && n.Depth == maxDepth)
            .SelectMany(n => outgoing.GetValueOrDefault(n.Node.Id) ?? [])
            .Select(e => e.Target).Where(t => !ids.Contains(t) && nodes.ContainsKey(t)).Distinct().Count();

        return new Slice
        {
            Entry = entry,
            Depth = maxDepth,
            Nodes = [.. result.Values.OrderBy(n => n.Depth).ThenBy(n => n.Node.Id, StringComparer.Ordinal)],
            Edges = [.. sliceEdges.Distinct().OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal)
                .ThenBy(e => e.Kind, StringComparer.Ordinal)],
            Unknowns = [.. result.Keys.Order(StringComparer.Ordinal).SelectMany(id => blindByOwner[id])
                .DistinctBy(b => (b.Owner, b.File, b.Line, b.Reason))
                .OrderBy(b => b.File, StringComparer.Ordinal).ThenBy(b => b.Line).ThenBy(b => b.Reason, StringComparer.Ordinal)],
            CutOff = cutOff,
        };
    }

    Dictionary<string, int> Bfs(string entry, int maxDepth, IReadOnlySet<string> stops, bool certainOnly)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal) { [entry] = 0 };
        var queue = new Queue<string>([entry]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var depth = seen[id];
            if (depth >= maxDepth || (id != entry && (stops.Contains(id) || Hubs.Contains(id)))) continue;
            foreach (var edge in outgoing.GetValueOrDefault(id) ?? [])
            {
                if (certainOnly && edge.Label != Label.Certain) continue;
                if (seen.ContainsKey(edge.Target) || !nodes.ContainsKey(edge.Target)) continue;
                seen[edge.Target] = depth + 1;
                queue.Enqueue(edge.Target);
            }
        }
        return seen;
    }
}
