using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Graph.Impact;

/// <summary>What a change starts from: symbols, a file, or a git diff.</summary>
public sealed record ChangeSpec(string Kind, string Value);

/// <summary>
/// Impact answers (D27): walk back from the changed points along the lines a change travels,
/// stop at what people recognise (endpoints, event handlers, tables, tests, projects) and group
/// it, ✅ first. Pure code over the graph, no AI (D18): the same change gives the same answer.
/// </summary>
public sealed class ImpactAnalyzer
{
    public const int ItemsPerGroup = 5;
    /// <summary>📜 files shown before <c>--expand history</c> (D40).</summary>
    public const int HistoryItems = 10;
    public static readonly string[] Groups = ["change", "endpoints", "pages", "eventHandlers", "tables", "tests", "projects", "code", "history"];

    readonly CodeGraph graph;
    readonly Dictionary<string, Node> nodes;
    readonly Dictionary<string, List<Edge>> incoming;
    readonly ILookup<string, Node> byFile;
    readonly ILookup<string, BlindSpot> blindByOwner;
    readonly ILookup<string, HistoryLine> historyByTarget;

    public ImpactAnalyzer(CodeGraph graph)
    {
        this.graph = graph;
        nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var n in graph.AllNodes) nodes.TryAdd(n.Id, n);
        incoming = graph.AllEdges.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => GraphStore.SortEdges(g).ToList(), StringComparer.Ordinal);
        byFile = nodes.Values.Where(n => n.File is not null && n.Line > 0 && n.Kind is not NodeKind.File and not NodeKind.Project)
            .ToLookup(n => n.File!, StringComparer.Ordinal);
        blindByOwner = graph.AllBlindSpots.ToLookup(b => b.Owner, StringComparer.Ordinal);
        historyByTarget = graph.HistoryLines.ToLookup(l => l.Target, StringComparer.Ordinal);
    }

    /// <summary>The files a change touches: where 📜 is looked up from (D40).</summary>
    IReadOnlySet<string> ChangedFiles(ChangeSpec change, string repoRoot, IEnumerable<Node> starts)
    {
        var files = starts.Where(n => n.File is not null).Select(n => n.File!).ToHashSet(StringComparer.Ordinal);
        if (change.Kind == "file") files.Add(Normalize(change.Value, repoRoot));
        // A diff can touch files with no points on these lines (a template edit outside any member).
        if (change.Kind == "diff") files.UnionWith(DiffMapper.ChangedLines(repoRoot, change.Value).Select(h => h.File));
        return files;
    }

    /// <summary>
    /// 📜 partners of the changed files: one step out, never walked (D40). A partner of several changed
    /// files keeps its strongest evidence (highest share, then most shared commits).
    /// </summary>
    List<(HistoryLine Line, Label? Found)> HistoryPartners(IReadOnlySet<string> changed, IReadOnlyDictionary<string, Label> foundByFile)
    {
        var best = new Dictionary<string, HistoryLine>(StringComparer.Ordinal);
        foreach (var file in changed.Order(StringComparer.Ordinal))
            foreach (var line in historyByTarget[file])
            {
                if (changed.Contains(line.Source)) continue;
                if (!best.TryGetValue(line.Source, out var have) || Stronger(line, have)) best[line.Source] = line;
            }
        return [.. best.Values
            .OrderByDescending(l => (double)l.Shared / l.Of).ThenByDescending(l => l.Shared).ThenBy(l => l.Source, StringComparer.Ordinal)
            .Select(l => (l, foundByFile.TryGetValue(l.Source, out var found) ? found : (Label?)null))];
    }

    static bool Stronger(HistoryLine a, HistoryLine b) =>
        a.Shared * b.Of != b.Shared * a.Of ? a.Shared * b.Of > b.Shared * a.Of : a.Shared != b.Shared ? a.Shared > b.Shared : string.CompareOrdinal(a.Target, b.Target) < 0;

    public Node? Find(string id) => nodes.GetValueOrDefault(id);

    /// <summary>
    /// Points matching a symbol: an exact id, or a display name such as
    /// <c>OrderProcessingService.PlaceOrderAsync</c> (all overloads), or an id suffix.
    /// </summary>
    public List<Node> Resolve(string symbol)
    {
        if (nodes.TryGetValue(symbol, out var exact)) return [exact];
        var byName = nodes.Values.Where(n => n.Name == symbol).ToList();
        if (byName.Count > 0) return [.. byName.OrderBy(n => n.Id, StringComparer.Ordinal)];
        return [.. nodes.Values.Where(n => n.Id.EndsWith(symbol, StringComparison.Ordinal) || n.Id.Contains("|" + symbol, StringComparison.Ordinal))
            .OrderBy(n => n.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The innermost points declared on these lines of a file. A file compiled into several
    /// projects gives one point per project for the same lines.
    /// </summary>
    public IEnumerable<Node> AtLines(string file, int from, int to)
    {
        var inFile = byFile[file].ToList();
        for (var line = from; line <= Math.Max(from, to); line++)
        {
            var covering = inFile.Where(n => n.Line <= line && line <= Math.Max(n.Line, n.EndLine)).ToList();
            if (covering.Count == 0) continue;
            var span = covering.Min(n => n.EndLine - n.Line);
            foreach (var n in covering.Where(n => n.EndLine - n.Line == span).OrderBy(n => n.Id, StringComparer.Ordinal))
                yield return n;
        }
    }

    public List<Node> StartNodes(ChangeSpec change, string repoRoot) => change.Kind switch
    {
        "symbol" => Resolve(change.Value),
        "file" => [.. byFile[Normalize(change.Value, repoRoot)].OrderBy(n => n.Id, StringComparer.Ordinal)],
        "diff" => [.. DiffMapper.ChangedLines(repoRoot, change.Value)
            .SelectMany(h => AtLines(h.File, h.From, h.To)).DistinctBy(n => n.Id).OrderBy(n => n.Id, StringComparer.Ordinal)],
        _ => throw new StangentException("usage", $"Unknown change kind '{change.Kind}'.", ExitCodes.Usage),
    };

    static string Normalize(string file, string root)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(Directory.GetCurrentDirectory(), file));
        var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
        return rel.StartsWith("..", StringComparison.Ordinal) ? file.Replace('\\', '/') : rel;
    }

    sealed record Reached(Node Node, Label Label, string Via, int Depth);

    /// <summary>Every affected point with its label, without grouping (used by scoring, D28).</summary>
    public IReadOnlyList<(Node Node, Label Label)> Affected(IReadOnlyList<Node> starts) =>
        [.. AffectedWithDepth(starts).Select(a => (a.Node, a.Label))];

    /// <summary>The same, with how many lines away from the change each point is (1 = direct).</summary>
    public IReadOnlyList<(Node Node, Label Label, int Depth)> AffectedWithDepth(IReadOnlyList<Node> starts)
    {
        var startIds = starts.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return [.. Walk(starts).Values.Where(r => !startIds.Contains(r.Node.Id)).Select(r => (r.Node, r.Label, r.Depth))];
    }

    /// <summary>Every point declared in a repo-relative file.</summary>
    public IReadOnlyList<Node> InFile(string file) => [.. byFile[file]];

    /// <summary>Everything that can reach the start points by following lines backwards.</summary>
    Dictionary<string, Reached> Walk(IReadOnlyList<Node> starts)
    {
        var certain = Bfs(starts, certainOnly: true);
        var all = Bfs(starts, certainOnly: false);
        var result = new Dictionary<string, Reached>(StringComparer.Ordinal);
        foreach (var (id, (via, depth)) in all)
        {
            var label = certain.ContainsKey(id) ? Label.Certain : Label.Guessed;
            var (v, d) = certain.TryGetValue(id, out var c) ? c : (via, depth);
            result[id] = new Reached(nodes[id], label, v, d);
        }
        return result;
    }

    Dictionary<string, (string Via, int Depth)> Bfs(IReadOnlyList<Node> starts, bool certainOnly)
    {
        var seen = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        var queue = new Queue<(string Id, int Depth)>();
        foreach (var s in starts) { seen[s.Id] = ("start", 0); queue.Enqueue((s.Id, 0)); }
        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            // From an affected event handler, follow only further events it triggers (event chains).
            // What else "depends" on a handler is the generic dispatcher every publisher calls;
            // walking back through that would mark the whole application as affected.
            var viaEvent = seen[id].Item1 == EdgeKind.TriggeredBy;
            foreach (var edge in incoming.GetValueOrDefault(id) ?? [])
            {
                if (viaEvent && edge.Kind != EdgeKind.TriggeredBy) continue;
                if (certainOnly && edge.Label != Label.Certain) continue;
                var follow = EdgeKind.Propagating.Contains(edge.Kind) || (depth == 0 && EdgeKind.FirstStepOnly.Contains(edge.Kind));
                if (!follow || seen.ContainsKey(edge.Source) || !nodes.ContainsKey(edge.Source)) continue;
                seen[edge.Source] = (edge.Kind, depth + 1);
                queue.Enqueue((edge.Source, depth + 1));
            }
        }
        return seen;
    }

    /// <summary>Distance bands (D27, refined): direct dependents, near (2–3 lines away), far (4+).</summary>
    public static string Band(int depth) => depth <= 1 ? "direct" : depth <= 3 ? "near" : "far";

    /// <param name="maxDepth">Leave out everything more than this many lines from the change.</param>
    public JsonObject Analyze(ChangeSpec change, string repoRoot, string? expand = null, int? maxDepth = null)
    {
        if (maxDepth is < 1)
            throw new StangentException("usage", "--max-depth must be at least 1.", ExitCodes.Usage);
        if (expand is not null && !Groups.Contains(expand))
            throw new StangentException("usage", $"Unknown group '{expand}'. Groups: {string.Join(", ", Groups)}.", ExitCodes.Usage);
        var starts = StartNodes(change, repoRoot);
        if (starts.Count == 0)
            throw new StangentException("not_found", $"Nothing in the graph matches {change.Kind} '{change.Value}'.", ExitCodes.NotFound);

        var reached = Walk(starts);
        if (maxDepth is { } limit)
            reached = reached.Where(r => r.Value.Depth <= limit).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        var startIds = starts.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var affected = reached.Values.Where(r => !startIds.Contains(r.Node.Id)).ToList();

        var changedFiles = ChangedFiles(change, repoRoot, starts);
        var foundByFile = affected.Where(r => r.Node.File is not null).GroupBy(r => r.Node.File!)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Label), StringComparer.Ordinal);
        var partners = HistoryPartners(changedFiles, foundByFile);
        tags = partners.ToDictionary(p => p.Line.Source, p => p.Line, StringComparer.Ordinal);

        var groups = new JsonArray
        {
            Group("endpoints", affected.Where(r => r.Node.Kind == NodeKind.Endpoint), expand),
            Group("pages", affected.Where(r => r.Node.Kind == NodeKind.Page), expand),
            Group("eventHandlers", affected.Where(r => r.Via == EdgeKind.TriggeredBy), expand),
            Group("tables", affected.Where(r => r.Node.Kind is NodeKind.Table or NodeKind.Column), expand),
            Group("tests", affected.Where(r => r.Node.IsTest), expand),
            Group("projects", affected.Where(r => r.Node.Project is not null && r.Node.Kind != NodeKind.Project)
                .GroupBy(r => r.Node.Project!)
                .Select(g => new Reached(nodes.GetValueOrDefault(g.Key) ?? new Node(g.Key, NodeKind.Project, g.Key),
                    g.Any(r => r.Label == Label.Certain) ? Label.Certain : Label.Guessed, "contains", g.Min(r => r.Depth))), expand),
            Group("code", affected.Where(r => r.Node.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Field or NodeKind.Type
                or NodeKind.Component or NodeKind.Template
                && !r.Node.IsTest && r.Via != EdgeKind.TriggeredBy), expand),
        };

        var unresolved = new JsonArray();
        foreach (var b in reached.Keys.Order(StringComparer.Ordinal).SelectMany(id => blindByOwner[id])
                     .DistinctBy(b => (b.Owner, b.File, b.Line, b.Reason)).OrderBy(b => b.File, StringComparer.Ordinal).ThenBy(b => b.Line))
            unresolved.Add(new JsonObject
            {
                ["file"] = b.File, ["line"] = b.Line, ["reason"] = b.Reason, ["in"] = b.Owner, ["detector"] = b.Detector,
            });

        var shownPartners = expand == "history" ? partners : [.. partners.Take(HistoryItems)];
        var history = new JsonObject
        {
            ["status"] = graph.History?.Status ?? "none",
            ["commit"] = graph.History?.Commit ?? "",
            ["total"] = partners.Count,
            ["items"] = new JsonArray([.. shownPartners.Select(p => (JsonNode)new JsonObject
            {
                ["file"] = p.Line.Source, ["with"] = p.Line.Target, ["shared"] = p.Line.Shared, ["of"] = p.Line.Of,
                ["found"] = p.Found switch { Label.Certain => "certain", Label.Guessed => "guessed", _ => null },
            })]),
            ["more"] = partners.Count - shownPartners.Count,
        };

        return new JsonObject
        {
            ["schemaVersion"] = 4,
            ["graph"] = new JsonObject { ["commit"] = graph.Commit },
            ["change"] = new JsonObject
            {
                ["kind"] = change.Kind, ["value"] = change.Value,
                ["start"] = Group("change", starts.Select(s => new Reached(s, Label.Certain, "start", 0)), expand),
            },
            ["groups"] = groups,
            ["unresolved"] = unresolved,
            ["history"] = history,
            ["requirements"] = new JsonArray(),
            ["expanded"] = expand,
            ["maxDepth"] = maxDepth,
        };
    }

    /// <summary>📜 partners of the change being analysed, by file: graph items in them get an "also 📜" tag.</summary>
    Dictionary<string, HistoryLine> tags = [];

    JsonObject Group(string name, IEnumerable<Reached> items, string? expand)
    {
        // Nearest first: a direct dependent is far likelier to break than one several lines away (D28 scores).
        var sorted = items.DistinctBy(r => r.Node.Id)
            .OrderBy(r => r.Depth).ThenBy(r => r.Label).ThenBy(r => r.Node.Name, StringComparer.Ordinal).ThenBy(r => r.Node.Id, StringComparer.Ordinal).ToList();
        var shown = expand == name ? sorted : [.. sorted.Take(ItemsPerGroup)];
        // Overloads share a display name: add parameter types. Same-named classes in different
        // projects (every plugin's "EventConsumer") also get their project.
        var names = sorted.ToDictionary(r => r.Node.Id, r => r.Node.Name, StringComparer.Ordinal);
        foreach (var g in sorted.GroupBy(r => r.Node.Name).Where(g => g.Count() > 1))
            foreach (var r in g) names[r.Node.Id] = r.Node.Name + Parameters(r.Node.Id);
        foreach (var g in sorted.GroupBy(r => names[r.Node.Id]).Where(g => g.Count() > 1))
            foreach (var r in g)
                names[r.Node.Id] = $"{(r.Node.Project is { } p && nodes.TryGetValue(p, out var proj) ? proj.Name : "?")} › {names[r.Node.Id]}";
        // Still the same (two frontends with one package name): the project's path tells them apart.
        foreach (var g in sorted.GroupBy(r => names[r.Node.Id]).Where(g => g.Count() > 1))
            foreach (var r in g)
                names[r.Node.Id] = $"{(r.Node.Project ?? r.Node.Id).Replace("project:", "")} › {r.Node.Name}";
        return new JsonObject
        {
            ["name"] = name,
            ["total"] = sorted.Count,
            ["certain"] = sorted.Count(r => r.Label == Label.Certain),
            ["guessed"] = sorted.Count(r => r.Label == Label.Guessed),
            ["bands"] = new JsonObject
            {
                ["direct"] = sorted.Count(r => Band(r.Depth) == "direct"),
                ["near"] = sorted.Count(r => Band(r.Depth) == "near"),
                ["far"] = sorted.Count(r => Band(r.Depth) == "far"),
            },
            ["items"] = new JsonArray([.. shown.Select(r =>
            {
                var item = new JsonObject
                {
                    ["id"] = r.Node.Id, ["name"] = names[r.Node.Id], ["kind"] = r.Node.Kind,
                    ["label"] = r.Label == Label.Certain ? "certain" : "guessed",
                    ["file"] = r.Node.File, ["line"] = r.Node.Line, ["depth"] = r.Depth,
                };
                if (r.Node.File is { } f && tags.TryGetValue(f, out var h))
                    item["history"] = new JsonObject { ["shared"] = h.Shared, ["of"] = h.Of };
                return (JsonNode)item;
            })]),
            ["more"] = sorted.Count - shown.Count,
        };
    }

    /// <summary><c>…M:Ns.T.M(Ns.A,System.Collections.Generic.List{Ns.B})</c> → <c>(A, List{B})</c>.</summary>
    static string Parameters(string id)
    {
        var open = id.IndexOf('(');
        if (open < 0) return "";
        var inner = id[(open + 1)..id.LastIndexOf(')')];
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= inner.Length; i++)
        {
            if (i < inner.Length && inner[i] == '{') depth++;
            else if (i < inner.Length && inner[i] == '}') depth--;
            else if (i == inner.Length || (inner[i] == ',' && depth == 0))
            {
                parts.Add(System.Text.RegularExpressions.Regex.Replace(inner[start..i], @"[\w`]+\.", ""));
                start = i + 1;
            }
        }
        return "(" + string.Join(", ", parts) + ")";
    }

    static readonly Dictionary<string, string> Titles = new()
    {
        ["endpoints"] = "endpoints", ["pages"] = "pages", ["eventHandlers"] = "event handlers", ["tables"] = "tables and columns",
        ["tests"] = "tests that cover it", ["projects"] = "projects", ["code"] = "other methods and types",
    };

    /// <summary>The same answer as Markdown, for PRs and people (D27).</summary>
    public static string Markdown(JsonObject report)
    {
        var md = new StringBuilder();
        var change = report["change"]!;
        var start = change["start"]!;
        md.Append($"**Change** ({change["kind"]} `{change["value"]}`): ");
        md.AppendLine(string.Join(", ", start["items"]!.AsArray().Select(i => $"`{i!["name"]}`")) + More(start) + "\n");

        md.AppendLine("**Affects**\n");
        foreach (var g in report["groups"]!.AsArray())
        {
            var total = (int)g!["total"]!;
            if (total == 0) continue;
            var certain = (int)g["certain"]!;
            var guessed = (int)g["guessed"]!;
            var labels = guessed == 0 ? $"✅ {certain}" : certain == 0 ? $"🟡 {guessed}" : $"✅ {certain} · 🟡 {guessed}";
            var bands = g["bands"]!;
            var distance = string.Join(", ", new[] { "direct", "near", "far" }.Where(b => (int)bands[b]! > 0).Select(b => $"{bands[b]} {b}"));
            var counts = (string)g["name"]! == "projects" ? labels : $"{labels}; {distance}";
            md.AppendLine($"- **{total} {Titles[(string)g["name"]!]}** ({counts}): "
                + string.Join(", ", g["items"]!.AsArray().Select(i => $"{((string)i!["label"]! == "certain" ? "✅" : "🟡")} `{i["name"]}`{(i["history"] is null ? "" : " 📜")}"))
                + More(g));
        }
        var history = report["history"]!;
        if ((int)history["total"]! > 0)
        {
            md.AppendLine($"\n**📜 {history["total"]} file(s) usually change with it** (from git history; a hint, not a dependency)\n");
            foreach (var h in history["items"]!.AsArray())
            {
                var found = (string?)h!["found"] switch { "certain" => " · also ✅ above", "guessed" => " · also 🟡 above", _ => "" };
                md.AppendLine($"- `{h["file"]}`: changed together in {h["shared"]} of the last {h["of"]} commits of `{Path.GetFileName((string)h["with"]!)}`{found}");
            }
            if ((int)history["more"]! > 0) md.AppendLine($"- and {history["more"]} more");
        }
        var unresolved = report["unresolved"]!.AsArray();
        if (unresolved.Count > 0)
        {
            md.AppendLine($"\n**❓ {unresolved.Count} link(s) couldn't be followed**\n");
            foreach (var u in unresolved) md.AppendLine($"- `{u!["file"]}:{u["line"]}`: {u["reason"]}");
        }
        var depthNote = report["maxDepth"] is { } d ? $" · within {d} lines of the change" : "";
        if ((string)history["status"]! is "shallow") depthNote += " · 📜 from a shallow clone's partial history";
        else if ((string)history["commit"]! is { Length: > 0 } hc && hc != (string?)report["graph"]!["commit"]) depthNote += $" · 📜 history read at {hc[..Math.Min(7, hc.Length)]}";
        md.AppendLine($"\n<sub>graph {((string?)report["graph"]!["commit"])?[..Math.Min(7, ((string?)report["graph"]!["commit"])?.Length ?? 0)]}{depthNote} · nearest first; direct = 1 line away, near = 2–3, far = 4+ · expand a group with `--expand <group>`</sub>");
        return md.ToString();
    }

    static string More(JsonNode group) => (int)group["more"]! > 0 ? $" and {group["more"]} more" : "";
}
