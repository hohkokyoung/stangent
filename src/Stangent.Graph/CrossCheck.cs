using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stangent.Graph;

/// <summary>What the cross-check compared and how many disagreements it found (D164).</summary>
public sealed record CrossCheckStats(int Sources, int Disagreements);

/// <summary>
/// D164: the framework's own answer as the key. Every build compares the graph's endpoints with each OpenAPI file in the
/// repo (an operation no endpoint fits; an endpoint of the file's own project it doesn't list), and the tables our rules
/// find with EF's snapshot. Every disagreement is
/// written to <c>.stangent/graph-crosscheck.json</c>; nothing here changes the graph, since a stale file would mislead it.
/// </summary>
public static class CrossCheck
{
    public static string PathFor(string root) => Path.Combine(root, ".stangent", "graph-crosscheck.json");

    static readonly HashSet<string> Skip = ["node_modules", "bin", "obj", ".git", ".stangent", "dist", "artifacts"];
    static readonly string[] Verbs = ["get", "post", "put", "delete", "patch", "head", "options"];

    public static CrossCheckStats? Run(string root, CodeGraph graph)
    {
        var report = new JsonObject { ["commit"] = graph.Commit };
        var sources = 0;
        var disagreements = 0;

        var openapi = new JsonArray();
        var endpoints = graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint).ToList();
        foreach (var file in OpenApiFiles(root))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            JsonObject? entry;
            try { entry = CompareOpenApi(root, file, rel, endpoints); }
            catch (Exception e) when (e is not OperationCanceledException) { entry = new JsonObject { ["file"] = rel, ["ignored"] = $"couldn't be read: {e.GetType().Name}" }; }
            if (entry is null) continue;
            openapi.Add(entry);
            if (entry.ContainsKey("ignored")) continue;
            sources++;
            disagreements += entry["missingFromGraph"]!.AsArray().Count + entry["notInOpenapi"]!.AsArray().Count;
        }
        report["openapi"] = openapi;

        var ef = new JsonArray();
        var entities = graph.AllFacts.Where(f => f.Detector == "entity-tables" && f.Kind == "entity").ToList();
        // Per DbContext its snapshot names (R3); a snapshot without [DbContext] speaks for its assembly.
        static string Key(Fact f) => f["context"].Length > 0 ? f["context"] : "assembly:" + f["assembly"];
        foreach (var key in entities.Where(f => f["source"] == "snapshot").Select(Key).Distinct().Order(StringComparer.Ordinal))
        {
            var snapshot = entities.Where(f => f["source"] == "snapshot" && Key(f) == key).GroupBy(f => f["entity"]).ToDictionary(g => g.Key, g => g.First()["table"], StringComparer.Ordinal);
            var byRules = entities.Where(f => f["source"] is "dbset" or "navigation" && Key(f) == key).Select(f => f["entity"]).ToHashSet(StringComparer.Ordinal);
            var rulesOnly = byRules.Where(e => !snapshot.ContainsKey(e)).Order(StringComparer.Ordinal).ToList();
            var snapshotOnly = snapshot.Keys.Where(e => !byRules.Contains(e)).Order(StringComparer.Ordinal).Select(e => $"{e} ({snapshot[e]})").ToList();
            sources++;
            disagreements += rulesOnly.Count + snapshotOnly.Count;
            ef.Add(new JsonObject
            {
                ["context"] = key, ["tables"] = snapshot.Count,
                ["foundByRulesOnly"] = new JsonArray([.. rulesOnly.Select(e => (JsonNode)e)]),
                ["inSnapshotOnly"] = new JsonArray([.. snapshotOnly.Select(e => (JsonNode)e)]),
            });
        }
        report["efSnapshot"] = ef;

        var path = PathFor(root);
        if (sources == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return new CrossCheckStats(sources, disagreements);
    }

    /// <summary>One OpenAPI file against the endpoints; null when it isn't one. R5: a file almost none of whose operations fit any
    /// endpoint documents someone else's API (a client's spec), so it's set aside, not counted.</summary>
    static JsonObject? CompareOpenApi(string root, string file, string rel, List<Node> endpoints)
    {
        if (Read(file) is not { } doc || doc["paths"] is not JsonObject paths) return null;
        var scope = ProjectOf(root, file);
        var inScope = endpoints.Where(e => scope is null || e.Project == scope).ToList();
        var basePath = doc["basePath"] is JsonValue v && v.TryGetValue<string>(out var b) ? b : "";
        var documented = new HashSet<(string Verb, string Path)>();
        foreach (var (route, item) in paths)
            if (item is JsonObject ops)
                foreach (var verb in Verbs.Where(ops.ContainsKey))
                    documented.Add((verb.ToUpperInvariant(), Normalize(basePath.TrimEnd('/') + route)));
        static (string Verb, string Path, string Name) Shape(Node e) => (e.Name[..e.Name.IndexOf(' ')], Normalize(e.Name[(e.Name.IndexOf(' ') + 1)..]), e.Name);
        // A host's OpenAPI documents every project it serves (Sonarr's lists Sonarr.Http's login and ping): an operation is
        // missing only when no endpoint anywhere fits it; an endpoint is undocumented only when it's the file's own project's.
        var everywhere = endpoints.Select(Shape).ToList();
        var mapped = inScope.Select(Shape).ToList();
        bool Fits((string Verb, string Path) d) => everywhere.Any(m => m.Path == d.Path && (m.Verb == "ANY" || m.Verb == d.Verb));
        var missing = documented.Where(d => !Fits(d)).Select(d => $"{d.Verb} {d.Path}").Order(StringComparer.Ordinal).ToList();
        if (documented.Count >= 5 && (documented.Count - missing.Count) * 5 < documented.Count)
            return new JsonObject { ["file"] = rel, ["ignored"] = $"only {documented.Count - missing.Count} of {documented.Count} operations fit any endpoint: not this repo's API" };
        var notInOpenapi = mapped.Where(m => !documented.Any(d => d.Path == m.Path && (m.Verb == "ANY" || d.Verb == m.Verb))).Select(m => m.Name).Distinct().Order(StringComparer.Ordinal).ToList();
        return new JsonObject
        {
            ["file"] = rel, ["scope"] = scope ?? "(whole repo)", ["operations"] = documented.Count, ["endpoints"] = inScope.Count,
            ["missingFromGraph"] = new JsonArray([.. missing.Select(m => (JsonNode)m)]),
            ["notInOpenapi"] = new JsonArray([.. notInOpenapi.Select(m => (JsonNode)m)]),
        };
    }

    /// <summary>A path compared by shape: lower case, no trailing slash, every <c>{…}</c> the same.</summary>
    public static string Normalize(string path)
    {
        // Split at slashes outside braces: {**path:regex(^(?!api/).*)} is one segment.
        var segments = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var c in path)
        {
            if (c == '{') depth++;
            else if (c == '}') depth--;
            if (c == '/' && depth == 0) { if (current.Length > 0) segments.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) segments.Add(current.ToString());
        return "/" + string.Join("/", segments.Select(s => s.StartsWith('{') && s.EndsWith('}') ? "{}" : s.ToLowerInvariant()));
    }

    static IEnumerable<string> OpenApiFiles(string root)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            foreach (var entry in entries.Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry)) { if (!Skip.Contains(name)) stack.Push(entry); continue; }
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && (name.Contains("openapi", StringComparison.OrdinalIgnoreCase) || name.Contains("swagger", StringComparison.OrdinalIgnoreCase)))
                    yield return entry;
            }
        }
    }

    static JsonObject? Read(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 50_000_000) return null;
            return JsonNode.Parse(File.ReadAllText(file)) is JsonObject o && (o.ContainsKey("openapi") || o.ContainsKey("swagger")) ? o : null;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>The project whose folder holds the file (its nearest .csproj up to the repo root), or null for the whole repo.</summary>
    static string? ProjectOf(string root, string file)
    {
        var full = Path.GetFullPath(root);
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(file)); dir is not null && dir.Length > full.Length; dir = Path.GetDirectoryName(dir))
            if (Directory.EnumerateFiles(dir, "*.csproj").Order(StringComparer.Ordinal).FirstOrDefault() is { } csproj)
                return "project:" + Path.GetRelativePath(root, csproj).Replace('\\', '/');
        return null;
    }
}
