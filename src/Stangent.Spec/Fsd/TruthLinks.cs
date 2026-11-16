using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>What one evidence line, or a whole finding, links to (D362 (1)): <c>flow</c>, <c>screen</c> (an inventory
/// screen, shared part or App frame group), <c>node</c> (the smallest graph node holding the line) or <c>file</c>.
/// <see cref="Hash"/> is the fingerprint of what it links to, so a ruling is flagged when that changes (D362 (2)).</summary>
public sealed record TruthLink(string Kind, string Id, string Title, string Hash, bool Exact = false)
{
    public static readonly string[] Kinds = ["flow", "screen", "node", "file"];
    public int Rank => Array.IndexOf(Kinds, Kind);
    /// <summary>Within a kind, a line that is the flow step or inventory control itself beats one found by a walked method or
    /// a nearby control (D368: an exact route step beat by a shared helper's walk, on Kavita).</summary>
    public int Precision => Rank * 2 + (Exact ? 0 : 1);
    /// <summary>A node or file link: the finding's code is in neither a flow nor a screen (D362 (4)).</summary>
    public bool Fallback => Rank >= 2;
    public string Key => $"{Kind}:{Id}";

    public JsonObject ToJson() => new() { ["kind"] = Kind, ["id"] = Id, ["title"] = Title, ["hash"] = Hash, ["exact"] = Exact };
    public static TruthLink FromJson(JsonNode n) => new((string)n["kind"]!, (string)n["id"]!, (string)n["title"]!, (string)n["hash"]!, (bool?)n["exact"] == true);
}

/// <summary>A finding's link: its best evidence line's (ties to the first), and every line's.</summary>
public sealed record FindingLinks(TruthLink Link, IReadOnlyList<(string Evidence, TruthLink Link)> Lines);

/// <summary>
/// The truth map's linker (D362): each FSD-review finding's evidence lines against the committed flows
/// (<c>specs/flows/</c>), the inventory (<c>specs/inventory.json</c>, its screens, shared parts and App frame) and the
/// code graph. Reads files only; never builds them. The same files give the same links.
/// </summary>
public sealed partial class TruthLinks
{
    readonly string root;
    readonly Dictionary<(string File, int Line), SortedSet<string>> stepAt = [];
    readonly Dictionary<string, SortedSet<string>> flowsByPoint = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<(int Line, string Flow)>> stepLines = new(StringComparer.Ordinal);
    readonly Dictionary<string, (string File, string Name, string Kind)> flows = new(StringComparer.Ordinal);
    readonly Dictionary<(string File, int Line), string> inventoryAt = [];
    readonly Dictionary<string, List<(int Line, string Group)>> inventoryFiles = new(StringComparer.Ordinal);
    readonly Dictionary<string, (string Title, string Json)> groups = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> screenByRoute = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<Node>> nodesByFile = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> hashes = new(StringComparer.Ordinal);

    public bool HasFlows => flows.Count > 0;
    public bool HasInventory => groups.Count > 0;

    public TruthLinks(string root, CodeGraph? graph)
    {
        this.root = root;
        var index = PageRun.LoadJson(Path.Combine(root, FlowFiles.IndexFile)) as JsonObject;
        foreach (var f in index?["flows"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var id = (string)f["id"]!;
            var file = (string)f["file"]!;
            flows[id] = (file, (string?)f["name"] ?? id, (string?)f["kind"] ?? "");
            if (PageRun.LoadJson(Path.Combine(root, file)) is JsonNode flow) Walk(flow, id);
        }
        if (PageRun.LoadJson(Path.Combine(root, InventoryFiles.JsonFile)) is JsonObject inv)
        {
            foreach (var s in inv["screens"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var id = (string)s["id"]!;
                AddGroup(id, (string)s["title"]!, s);
                if (id.LastIndexOf('|') is var bar and >= 0) screenByRoute.TryAdd(id[(bar + 1)..].TrimEnd('/'), id);
            }
            foreach (var g in inv["sharedParts"]?.AsArray().OfType<JsonObject>() ?? []) AddGroup((string)g["id"]!, (string)g["title"]!, g);
            foreach (var g in inv["appFrame"]?["groups"]?.AsArray().OfType<JsonObject>() ?? [])
                AddGroup((string)g["id"]!, $"{InventoryFiles.AppFrameTitle}: {(string)g["title"]!}", g);
        }
        foreach (var n in graph?.AllNodes ?? [])
            if (n.File is { } file && n.Kind != NodeKind.File && n.Line > 0 && n.EndLine >= n.Line)
                (nodesByFile.TryGetValue(file, out var list) ? list : nodesByFile[file] = []).Add(n);
    }

    void Walk(JsonNode node, string flow)
    {
        if (node is JsonObject o)
        {
            if (o["at"] is JsonValue av && av.TryGetValue<string>(out var at) && Split(at) is ({ } file, { } line))
            {
                (stepAt.TryGetValue((file, line), out var set) ? set : stepAt[(file, line)] = new(StringComparer.Ordinal)).Add(flow);
                (stepLines.TryGetValue(file, out var list) ? list : stepLines[file] = []).Add((line, flow));
            }
            if (o["point"] is JsonValue pv && pv.TryGetValue<string>(out var point))
                (flowsByPoint.TryGetValue(point, out var set) ? set : flowsByPoint[point] = new(StringComparer.Ordinal)).Add(flow);
            foreach (var (_, v) in o) if (v is not null) Walk(v, flow);
        }
        else if (node is JsonArray a) foreach (var v in a) if (v is not null) Walk(v, flow);
    }

    void AddGroup(string id, string title, JsonObject group)
    {
        groups[id] = (title, CanonicalJson.SerializeCompact(group));
        foreach (var at in Ats(group))
            if (Split(at) is ({ } file, { } line))
            {
                inventoryAt.TryAdd((file, line), id);
                (inventoryFiles.TryGetValue(file, out var c) ? c : inventoryFiles[file] = []).Add((line, id));
            }
    }

    static IEnumerable<string> Ats(JsonNode? n) => n switch
    {
        JsonObject o => (o["at"] is JsonValue v && v.TryGetValue<string>(out var at) ? [at] : Enumerable.Empty<string>()).Concat(o.SelectMany(kv => Ats(kv.Value))),
        JsonArray a => a.SelectMany(Ats),
        _ => [],
    };

    static (string? File, int? Line) Split(string at)
    {
        var m = PathLine().Match(at.Trim());
        return m.Success && int.TryParse(m.Groups["from"].Value, out var line) ? (m.Groups["path"].Value, line) : (null, null);
    }

    /// <summary><c>path:10</c>, <c>path:10-12</c>; anything after the first number (<c>path:10-</c>, <c>path:10,12</c>) is
    /// ignored, so the line still counts (D368).</summary>
    [GeneratedRegex(@"^(?<path>.+?):(?<from>\d+)(?:-(?<to>\d+))?(?:[^0-9].*)?$")]
    private static partial Regex PathLine();

    // ---- linking ------------------------------------------------------------------------------------------------------

    /// <summary>One finding: each evidence item's link, the best (flow, screen, node, file; ties to the first item) as the
    /// finding's. An undocumented screen naming its route links to that screen when no line reaches a flow.</summary>
    public FindingLinks Link(JsonObject finding)
    {
        var lines = new List<(string, TruthLink)>();
        foreach (var e in finding["evidence"]?.AsArray().Select(x => ((string)x!).Trim()) ?? []) lines.Add((e, LinkEvidence(e)));
        var best = lines.Count == 0 ? null : lines.Select(x => x.Item2).OrderBy(l => l.Precision).First();
        if ((string?)finding["state"] == "undocumented" && (string?)finding["screen"] is { } route
            && screenByRoute.TryGetValue(route.TrimEnd('/'), out var screen) && (best is null || best.Rank > 1))
            best = Group(screen, exact: true);
        best ??= new TruthLink("file", "", "(no evidence)", CanonicalJson.Hash(""));
        return new FindingLinks(best, lines);
    }

    /// <summary>One evidence item, <c>path:line</c>, <c>path:from-to</c> or a path alone (a file or folder).</summary>
    public TruthLink LinkEvidence(string evidence)
    {
        var m = PathLine().Match(evidence);
        var path = (m.Success ? m.Groups["path"].Value : evidence).Replace('\\', '/').TrimEnd('/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        if (!m.Success || !int.TryParse(m.Groups["from"].Value, out var from)) return FileLink(path);
        var to = m.Groups["to"].Success && int.TryParse(m.Groups["to"].Value, out var t) && t >= from ? t : from;
        // Every line of a range counts; the best one wins.
        TruthLink? best = null;
        for (var line = from; line <= Math.Min(to, from + 200); line++)
        {
            var l = LinkLine(path, line);
            if (best is null || l.Precision < best.Precision) best = l;
            if (best.Precision == 0) break;
        }
        return best ?? FileLink(path);
    }

    TruthLink LinkLine(string file, int line)
    {
        // 1. Flow: a step's own line, else inside a method a flow walks (a step's point, or holding a step's line).
        if (stepAt.TryGetValue((file, line), out var exact)) return Flow(Pick(exact), exact: true);
        var method = Smallest(file, line, n => n.Kind is NodeKind.Method or NodeKind.Endpoint);
        if (method is not null)
        {
            var walked = new SortedSet<string>(StringComparer.Ordinal);
            if (flowsByPoint.TryGetValue(method.Id, out var byPoint)) walked.UnionWith(byPoint);
            foreach (var (l, f) in stepLines.GetValueOrDefault(file) ?? []) if (l >= method.Line && l <= method.EndLine) walked.Add(f);
            if (walked.Count > 0) return Flow(Pick(walked));
        }
        // 2. Screen: an inventory line, else the nearest inventory line in the same file (a tie goes to the one before): a
        // file can hold several groups (Kavita's action factory, 11 menus), and the nearest is the one the line belongs to;
        // a menu's own opening line sits just above its first entry (D368).
        if (inventoryAt.TryGetValue((file, line), out var g)) return Group(g, exact: true);
        if (inventoryFiles.TryGetValue(file, out var near))
            return Group(near.OrderBy(x => Math.Abs(x.Line - line)).ThenBy(x => x.Line > line ? 1 : 0).ThenBy(x => x.Group, StringComparer.Ordinal).First().Group);
        // 3. Node: the smallest one holding the line. 4. The file.
        return Smallest(file, line, _ => true) is { } node ? NodeLink(node) : FileLink(file);
    }

    /// <summary>A start flow (click, screen opening, job) before a shared one; then by id.</summary>
    string Pick(IEnumerable<string> ids) =>
        ids.OrderBy(id => flows.TryGetValue(id, out var f) && f.Kind == "shared" ? 1 : 0).ThenBy(id => id, StringComparer.Ordinal).First();

    Node? Smallest(string file, int line, Func<Node, bool> where) =>
        (nodesByFile.GetValueOrDefault(file) ?? []).Where(n => n.Line <= line && n.EndLine >= line && where(n))
            .OrderBy(n => n.EndLine - n.Line).ThenBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault();

    TruthLink Flow(string id, bool exact = false)
    {
        var (file, name, _) = flows[id];
        return new("flow", id, name, Hashed("flow:" + id, () => Read(file)), exact);
    }

    TruthLink Group(string id, bool exact = false)
    {
        var (title, json) = groups[id];
        return new("screen", id, title, Hashed("screen:" + id, () => json), exact);
    }

    TruthLink NodeLink(Node n) => new("node", n.Id, n.Name, Hashed("node:" + n.Id, () =>
    {
        var lines = Read(n.File!).Split('\n');
        return string.Join('\n', lines.Skip(n.Line - 1).Take(n.EndLine - n.Line + 1));
    }));

    TruthLink FileLink(string path) => new("file", path, path, Hashed("file:" + path, () =>
    {
        var full = Path.Combine(root, path);
        if (Directory.Exists(full))
            return string.Join('\n', Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/') + " " + CanonicalJson.Hash(File.ReadAllText(f))));
        return Read(path);
    }));

    string Hashed(string key, Func<string> text) => hashes.TryGetValue(key, out var h) ? h : hashes[key] = CanonicalJson.Hash(text());

    string Read(string rel)
    {
        var full = Path.Combine(root, rel);
        return File.Exists(full) ? File.ReadAllText(full).Replace("\r\n", "\n") : "";
    }

    // ---- fsd links <doc> --------------------------------------------------------------------------------------------------

    /// <summary><c>fsd links &lt;doc&gt;</c>: each finding of <c>review.json</c> with its link, and the share linked by
    /// fallback (D362 (4)). Reads only.</summary>
    public static JsonObject Run(StangentRepo repo, string doc)
    {
        var review = PageRun.LoadJson(FsdReview.ReviewJson(repo.Root, doc)) as JsonObject
            ?? throw new StangentException("not_reviewed", $"{doc} has no merged review yet. Run `stangent fsd review-merge {doc}` first.", ExitCodes.NotFound);
        var linker = new TruthLinks(repo.Root, GraphStore.Load(GraphStore.PathFor(repo)));
        var rows = new JsonArray();
        var counts = TruthLink.Kinds.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        foreach (var f in review["findings"]!.AsArray().OfType<JsonObject>())
        {
            var links = linker.Link(f);
            counts[links.Link.Kind]++;
            rows.Add(new JsonObject
            {
                ["id"] = (string)f["id"]!, ["state"] = (string)f["state"]!, ["section"] = (string?)f["section"], ["link"] = links.Link.ToJson(),
                ["lines"] = new JsonArray([.. links.Lines.Select(l => (JsonNode)new JsonObject { ["evidence"] = l.Evidence, ["kind"] = l.Link.Kind, ["id"] = l.Link.Id })]),
            });
        }
        var total = rows.Count;
        var fallback = counts["node"] + counts["file"];
        var warnings = new JsonArray();
        if (!linker.HasFlows) warnings.Add((JsonNode)$"No flows in {FlowFiles.Folder}: run `stangent flows build`; nothing can link to a flow.");
        if (!linker.HasInventory) warnings.Add((JsonNode)$"No {InventoryFiles.JsonFile}: run `stangent inventory`; nothing can link to a screen.");
        return new JsonObject
        {
            ["doc"] = doc, ["findings"] = total,
            ["counts"] = new JsonObject([.. TruthLink.Kinds.Select(k => KeyValuePair.Create(k, (JsonNode?)counts[k]))]),
            ["fallback"] = fallback, ["fallbackShare"] = total == 0 ? 0 : Math.Round(100.0 * fallback / total, 1),
            ["summary"] = Summary(total, counts),
            ["warnings"] = warnings, ["links"] = rows,
        };
    }

    /// <summary>"51 findings: flow 12, screen 34, node 5, file 0 (fallback 9.8%)".</summary>
    public static string Summary(int total, IReadOnlyDictionary<string, int> counts)
    {
        var fallback = counts.GetValueOrDefault("node") + counts.GetValueOrDefault("file");
        var share = total == 0 ? 0 : Math.Round(100.0 * fallback / total, 1);
        var sb = new StringBuilder($"{total} finding{(total == 1 ? "" : "s")}: ");
        sb.Append(string.Join(", ", TruthLink.Kinds.Select(k => $"{k} {counts.GetValueOrDefault(k)}")));
        sb.Append(System.FormattableString.Invariant($" (fallback {share:0.0}%)"));
        return sb.ToString();
    }
}
