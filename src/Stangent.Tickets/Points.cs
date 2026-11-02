using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Tickets;

/// <summary>What the ticket steps read besides the ticket: the graph, the page list, the recorded pages and the links.</summary>
public sealed class TicketContext
{
    public required CodeGraph Graph { get; init; }
    public required ImpactAnalyzer Analyzer { get; init; }
    public required PageSet Set { get; init; }
    public required SpecIndex Index { get; init; }
    public required List<JsonObject> Trace { get; init; }

    public static TicketContext Load(StangentRepo repo)
    {
        var graph = GraphStore.Load(GraphStore.PathFor(repo))
            ?? throw new StangentException("no_graph", "No code graph yet. Run `stangent graph build` (or `graph import`).", ExitCodes.NotFound);
        return new TicketContext
        {
            Graph = graph, Analyzer = new ImpactAnalyzer(graph),
            Set = PageList.Build(graph, repo.Root, SpecSettings.From(repo.Config)),
            Index = SpecIndex.Load(repo.Root),
            Trace = [.. TraceFile.Load(repo.Root)["entries"]!.AsArray().OfType<JsonObject>()],
        };
    }

    /// <summary>A page's title as people know it: the written page's title, else the one from the code.</summary>
    public string Title(PageDef p) => (string?)Index.Pages.GetValueOrDefault(p.Id)?["title"] ?? p.Title;

}

/// <summary>A place in the code a ticket's work starts from: a file the file finder named (D101), or (code's signals,
/// D70, and the developer) a reference page or a graph symbol; and why. A file point carries the line from the file the
/// finder quoted and its one-sentence reason.</summary>
public sealed record StartingPoint(string Kind, string Id, string Title, string Signal, string Evidence, string Where, string By,
    string? Line = null, string? Why = null)
{
    public const string Page = "page";
    public const string Symbol = "symbol";
    public const string File = "file";

    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["kind"] = Kind, ["id"] = Id, ["title"] = Title, ["signal"] = Signal, ["evidence"] = Evidence, ["where"] = Where, ["by"] = By,
        };
        if (Line is not null) o["line"] = Line;
        if (Why is not null) o["why"] = Why;
        return o;
    }

    public static StartingPoint FromJson(JsonNode n) => new((string)n["kind"]!, (string)n["id"]!, (string)n["title"]!, (string)n["signal"]!,
        (string)n["evidence"]!, (string)n["where"]!, (string)n["by"]!, (string?)n["line"], (string?)n["why"]);
}

/// <summary>A part of a ticket whose cause lives outside the code (D83): the server, network, deployment, live data or a
/// third party. <c>Quote</c> is the ticket's words (from the AI, checked word for word) or the developer's note.</summary>
public sealed record OutsidePart(string Area, string Quote, string By)
{
    public static readonly string[] Areas = ["server", "network", "deployment", "data", "third-party", "other"];

    public JsonObject ToJson() => new() { ["area"] = Area, ["quote"] = Quote, ["by"] = By };

    public static OutsidePart FromJson(JsonNode n) => new((string)n["area"]!, (string)n["quote"]!, (string)n["by"]!);

    /// <summary>The bundle's outside parts (none in a bundle built before D83).</summary>
    public static List<OutsidePart> Of(JsonObject bundleOrPoints) =>
        bundleOrPoints["outside"]?.AsArray().Select(o => FromJson(o!)).ToList() ?? [];

    /// <summary>Every part is outside the code and nothing points into it: bug proof is skipped (D83, settled 2026-10-31).
    /// Not when the file finder named files that failed their checks (D101): it did point into the code.</summary>
    public static bool OnlyOutside(JsonObject bundleOrPoints) =>
        bundleOrPoints["points"]!.AsArray().Count == 0 && Of(bundleOrPoints).Count > 0
        && ((bundleOrPoints["rejected"] ?? bundleOrPoints["ai"]?["rejected"]) as JsonArray ?? []).Count == 0;

    /// <summary><c>--add-outside "network: times out behind the proxy"</c>.</summary>
    public static (string Area, string Note) Parse(string text)
    {
        var colon = text.IndexOf(':');
        var area = colon < 0 ? "" : text[..colon].Trim().ToLowerInvariant();
        var note = colon < 0 ? "" : text[(colon + 1)..].Trim();
        if (!Areas.Contains(area) || note.Length == 0)
            throw new StangentException("usage", $"--add-outside takes '<area>: <what>', area one of {string.Join(", ", Areas)}; got '{text}'.", ExitCodes.Usage);
        return (area, note);
    }
}

/// <summary>The ticket's text, by where it came from: what code searches and what quotes must come from.</summary>
public sealed record TicketText(IReadOnlyList<(string Where, string Text)> Prose, IReadOnlyList<(string Where, string Text)> Attachments)
{
    public static TicketText From(JsonObject pulled)
    {
        var prose = new List<(string, string)> { ("summary", (string)pulled["ticket"]!["summary"]!), ("description", (string?)pulled["description"] ?? "") };
        prose.AddRange(pulled["comments"]!.AsArray().Select(c => ($"comment {(string)c!["id"]!}", (string?)c["text"] ?? "")));
        var files = pulled["attachments"]!.AsArray().Where(a => a!["text"] is not null)
            .Select(a => ($"attachment {(string)a!["fileName"]!}", (string)a["text"]!)).ToList();
        return new TicketText(prose, files);
    }

    /// <summary>Everything a quote may come from (D70): summary, description and comments.</summary>
    public string Quotable => string.Join("\n\n", Prose.Select(p => p.Text));

    /// <summary>What a file finder quote may come from (D101): the prose and the text attachments (logs, traces).</summary>
    public string QuotableWithAttachments => string.Join("\n\n", Prose.Concat(Attachments).Select(p => p.Text));
}

/// <summary>
/// Starting points by code (D70 step 1), used when the file finder is off (D101: <c>tickets.aiPoints</c> false or
/// <c>ai.enabled</c> false): stack-trace frames → methods; URL paths → route pages and endpoint pages;
/// quoted screen names, page titles and FSD section titles → pages (sections through <c>specs/trace.yaml</c>); symbol
/// names that exist in the graph. Each point records its signal and the text it came from. Deterministic.
/// </summary>
public static partial class PointFinder
{
    public const int MaxFrames = 8;
    public const int MaxPerSignal = 3;

    public static List<StartingPoint> Find(TicketText text, TicketContext ctx)
    {
        var points = new List<StartingPoint>();
        points.AddRange(Frames(text, ctx));
        points.AddRange(Urls(text, ctx));
        points.AddRange(Titles(text, ctx));
        points.AddRange(Symbols(text, ctx));
        return [.. points.DistinctBy(p => (p.Kind, p.Id))];
    }

    // ---- stack traces ----

    [GeneratedRegex(@"\bat\s+(?<method>[\w.`<>\[\],+$]+?)\((?<args>[^)\n]*)\)(?:\s+in\s+(?<file>[^\n]+?):line\s+(?<line>\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex DotnetFrame();

    [GeneratedRegex(@"\bat\s+(?:(?<name>[\w.$<>]+)\s+\()?(?<file>[^\s()]+?\.(?:ts|tsx|js|jsx)):(?<line>\d+):\d+\)?", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptFrame();

    static IEnumerable<StartingPoint> Frames(TicketText text, TicketContext ctx)
    {
        var files = ctx.Graph.AllNodes.Select(n => n.File).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var found = new List<StartingPoint>();
        foreach (var (where, body) in text.Prose.Concat(text.Attachments))
        {
            foreach (Match m in DotnetFrame().Matches(body))
            {
                var node = FromFile(ctx, files, m.Groups["file"].Value, m.Groups["line"].Value) ?? FromName(ctx, m.Groups["method"].Value);
                if (node is not null) found.Add(Point(ctx, node, "stack-frame", Line(body, m.Index), where, "code"));
            }
            foreach (Match m in ScriptFrame().Matches(body))
                if (FromFile(ctx, files, m.Groups["file"].Value, m.Groups["line"].Value) is { } node)
                    found.Add(Point(ctx, node, "stack-frame", Line(body, m.Index), where, "code"));
        }
        return found.DistinctBy(p => p.Id).Take(MaxFrames);
    }

    /// <summary>The innermost method on that line of a file the graph knows, matched by the longest path suffix (two
    /// segments at least) so <c>/home/build/src/App/X.cs</c> finds <c>src/App/X.cs</c>.</summary>
    static Node? FromFile(TicketContext ctx, List<string> files, string file, string line)
    {
        if (file.Length == 0 || !int.TryParse(line, out var n)) return null;
        var parts = file.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToArray();
        var best = files.Select(f => (File: f, Shared: SharedSuffix(parts, f.Split('/')))).Where(x => x.Shared >= 2)
            .OrderByDescending(x => x.Shared).ThenBy(x => x.File, StringComparer.Ordinal).ToList();
        if (best.Count == 0 || (best.Count > 1 && best[1].Shared == best[0].Shared)) return null;
        return ctx.Analyzer.AtLines(best[0].File, n, n).Where(x => x.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Component)
            .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    static int SharedSuffix(string[] a, string[] b)
    {
        var k = 0;
        while (k < a.Length && k < b.Length && string.Equals(a[^(k + 1)], b[^(k + 1)], StringComparison.OrdinalIgnoreCase)) k++;
        return k;
    }

    /// <summary><c>App.Tv.SeriesService.Update</c>, <c>App.Tv.SeriesService.&lt;Update&gt;d__5.MoveNext</c> and lambdas inside it
    /// all mean <c>App.Tv.SeriesService.Update</c>; found by its documentation-comment id. Overloads: the first by id.</summary>
    static Node? FromName(TicketContext ctx, string frame)
    {
        var name = frame;
        var angle = Regex.Match(frame, @"^(?<type>.+?)\.<(?<method>[^>]+)>");
        if (angle.Success) name = $"{angle.Groups["type"].Value}.{angle.Groups["method"].Value}";
        name = Regex.Replace(name, @"\.<>c__DisplayClass[\w]*", "").Replace("+", ".");
        if (!name.Contains('.')) return null;
        var prefix = "|M:" + name;
        return ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Method && !n.IsTest)
            .Where(n => n.Id.Contains(prefix + "(", StringComparison.Ordinal) || n.Id.EndsWith(prefix, StringComparison.Ordinal))
            .OrderBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    // ---- URL paths ----

    [GeneratedRegex(@"(?:(?<verb>\b(?:GET|POST|PUT|PATCH|DELETE)\s+)?(?:https?://[^\s/'""<>()]+)(?<path>/[^\s?#'""<>()]*)|(?<verb>\b(?:GET|POST|PUT|PATCH|DELETE)\s+)?(?<![\w.:/~-])(?<path>/[A-Za-z][\w.~%-]*(?:/[\w.~%:-]+)*/?))", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPath();

    static IEnumerable<StartingPoint> Urls(TicketText text, TicketContext ctx)
    {
        var screens = ctx.Set.Pages.Where(p => p.Kind == PageKind.Screen).Select(p => (Page: p, Segments: Segments(ScreenRoute(p.Id)), Verb: (string?)null));
        var endpoints = ctx.Set.Pages.Where(p => p.Kind == PageKind.Backend && p.Entry.StartsWith("endpoint:", StringComparison.Ordinal))
            .Select(p => { var (verb, route) = PageList.SplitEndpoint(p.Title); return (Page: p, Segments: Segments(route), Verb: (string?)verb); });
        var targets = screens.Concat(endpoints).ToList();
        foreach (var (where, body) in text.Prose)
            foreach (Match m in UrlPath().Matches(body))
            {
                var path = m.Groups["path"].Value.TrimEnd('/', '.', ',');
                if (Regex.IsMatch(path, @"\.(cs|ts|tsx|js|jsx|log|txt|png|jpe?g|gif|json|xml|dll|exe|html?)$", RegexOptions.IgnoreCase)) continue;
                var verb = m.Groups["verb"].Success ? m.Groups["verb"].Value.Trim() : null;
                var segs = Segments(path);
                if (segs.Length == 0) continue;
                var scored = targets.Where(t => verb is null || t.Verb is null || t.Verb == verb || t.Verb == "ANY")
                    .Select(t => (t.Page, Score: Match(segs, t.Segments))).Where(x => x.Score > 0).ToList();
                if (scored.Count == 0) continue;
                var top = scored.Max(x => x.Score);
                foreach (var (page, _) in scored.Where(x => x.Score == top).OrderBy(x => x.Page.Kind == PageKind.Screen ? 0 : 1).ThenBy(x => x.Page.Id, StringComparer.Ordinal).Take(MaxPerSignal))
                    yield return new StartingPoint(StartingPoint.Page, page.Id, ctx.Title(page), "url", m.Value.Trim(), where, "code");
            }
    }

    static string ScreenRoute(string id)
    {
        var route = id["page:".Length..];
        return route.Contains('|') ? route[(route.LastIndexOf('|') + 1)..] : route;
    }

    static string[] Segments(string route) => route.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Literal segments that match (case ignored), when every segment matches a literal or a parameter; else 0.</summary>
    static int Match(string[] path, string[] route)
    {
        if (path.Length != route.Length) return 0;
        var literal = 0;
        for (var i = 0; i < path.Length; i++)
        {
            var r = route[i];
            if (r.StartsWith(':') || r.StartsWith('{') || r == "*") continue;
            if (!string.Equals(r, path[i], StringComparison.OrdinalIgnoreCase)) return 0;
            literal++;
        }
        return literal;
    }

    // ---- quoted names ----

    [GeneratedRegex(@"""(?<q>[^""\n]{2,60})""|“(?<q>[^”\n]{2,60})”|(?<![\w'])'(?<q>[^'\n]{2,60})'(?![\w'])|`(?<q>[^`\n]{2,60})`", RegexOptions.CultureInvariant)]
    private static partial Regex Quoted();

    static IEnumerable<StartingPoint> Titles(TicketText text, TicketContext ctx)
    {
        var names = ctx.Set.Pages.Select(p => (Page: p, Names: Candidates.PageNames(p, ctx.Index.Pages.GetValueOrDefault(p.Id)))).ToList();
        var sections = ctx.Index.Sections.Values.Select(s => (Section: s, Words: Candidates.Words((string)s["title"]!))).Where(s => s.Words.Count > 0).ToList();
        foreach (var (where, body) in text.Prose)
            foreach (Match m in Quoted().Matches(body))
            {
                var quote = m.Groups["q"].Value.Trim();
                var words = Candidates.Words(quote);
                if (words.Count == 0) continue;
                var pages = names.Where(n => n.Names.Any(w => w.SetEquals(words))).Select(n => n.Page)
                    .OrderBy(p => p.Kind == PageKind.Screen ? 0 : p.Kind == PageKind.Component ? 1 : 2).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
                foreach (var p in pages.Take(MaxPerSignal))
                    yield return new StartingPoint(StartingPoint.Page, p.Id, ctx.Title(p), "title", m.Value, where, "code");
                foreach (var (section, _) in sections.Where(s => s.Words.SetEquals(words)))
                {
                    var linked = ctx.Trace.Where(e => (string?)e["fsd"]?["section"] == (string?)section["id"] && (string?)e["page"]?["id"] is not null)
                        .Select(e => (string)e["page"]!["id"]!).Distinct().Order(StringComparer.Ordinal).Select(ctx.Set.Find).OfType<PageDef>();
                    foreach (var p in linked.Take(MaxPerSignal))
                        yield return new StartingPoint(StartingPoint.Page, p.Id, ctx.Title(p), "fsd-section",
                            $"{m.Value} (FSD {(string)section["id"]!} {(string)section["title"]!})", where, "code");
                }
            }
    }

    // ---- symbol names ----

    [GeneratedRegex(@"`(?<s>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)(?:\(\))?`|(?<![\w.])(?<s>[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+(?:\.[A-Za-z_]\w*)?|[A-Z]\w*\.[A-Za-z_]\w*)(?:\(\))?(?!\w|\.\w)", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolName();

    static readonly HashSet<string> SymbolKinds = [NodeKind.Type, NodeKind.Method, NodeKind.Component, NodeKind.Property];

    static IEnumerable<StartingPoint> Symbols(TicketText text, TicketContext ctx)
    {
        var byName = ctx.Graph.AllNodes.Where(n => SymbolKinds.Contains(n.Kind) && !n.IsTest)
            .GroupBy(n => n.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        foreach (var (where, body) in text.Prose)
            foreach (Match m in SymbolName().Matches(body))
            {
                var s = m.Groups["s"].Value;
                if (!byName.TryGetValue(s, out var nodes) || nodes.Count > MaxPerSignal) continue;
                foreach (var n in nodes)
                    yield return Point(ctx, n, "symbol", m.Value, where, "code");
            }
    }

    static StartingPoint Point(TicketContext ctx, Node n, string signal, string evidence, string where, string by) =>
        new(StartingPoint.Symbol, n.Id, n.Name, signal, evidence, where, by);

    static string Line(string text, int at)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        var end = text.IndexOf('\n', at);
        var line = text[start..(end < 0 ? text.Length : end)].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}

/// <summary>
/// <c>stangent ticket points</c> and the file finder (D101): the files an agent found by searching the repo, each checked
/// by code (it exists, its ticket quote is in the ticket, its line is in the file); with the finder off, code's own
/// signals (D70) instead; then the developer's additions and removals, replayed in order and recorded with who made
/// them. Written to <c>points.json</c>; same inputs → same file.
/// </summary>
public static class TicketPoints
{
    /// <param name="refind">Search again even though the ticket hasn't changed (the code has, or the developer asks).</param>
    public static JsonObject Update(StangentRepo repo, TicketContext ctx, string key, IReadOnlyList<string> add, IReadOnlyList<string> remove, string who,
        IReadOnlyList<string>? addOutside = null, IReadOnlyList<string>? removeOutside = null, bool refind = false)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var text = TicketText.From(pulled);
        var previous = PageRun.LoadJson(paths.PointsJson) as JsonObject;

        var changes = previous?["changes"]?.AsArray().Select(c => c!.DeepClone()).ToList() ?? [];
        var known = (previous?["points"]?.AsArray() ?? []).Where(p => (string?)p!["kind"] == StartingPoint.File).Select(p => (string)p!["id"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var a in add) changes.Add(Change("add", Resolve(repo, ctx, a, null), who));
        foreach (var r in remove) changes.Add(Change("remove", Resolve(repo, ctx, r, known), who));
        // Removals first, so one call can replace an area's parts with the developer's own.
        foreach (var o in removeOutside ?? [])
        {
            var area = o.Trim().ToLowerInvariant();
            if (area != "all" && !OutsidePart.Areas.Contains(area))
                throw new StangentException("usage", $"--remove-outside takes an area ({string.Join(", ", OutsidePart.Areas)}) or all; got '{o}'.", ExitCodes.Usage);
            changes.Add(new JsonObject { ["action"] = "remove", ["kind"] = Outside, ["id"] = area, ["by"] = who });
        }
        foreach (var o in addOutside ?? [])
        {
            var (area, note) = OutsidePart.Parse(o);
            changes.Add(new JsonObject { ["action"] = "add", ["kind"] = Outside, ["id"] = area, ["note"] = note, ["by"] = who });
        }

        var ai = AiSection(repo, text, previous?["ai"] as JsonObject, refind);
        var finderOn = (string)ai["status"]! is not ("off" or "disabled");
        var code = finderOn ? [] : PointFinder.Find(text, ctx);
        var found = (ai["files"] as JsonArray ?? []).Select(f => new StartingPoint(StartingPoint.File, (string)f!["path"]!,
            Path.GetFileName((string)f["path"]!), "ai", (string)f["quote"]!, "ticket", "ai", (string)f["line"]!, (string)f["why"]!)).ToList();

        var points = code.Concat(found).ToList();
        var outside = (ai["outside"] as JsonArray ?? []).Select(o => new OutsidePart((string)o!["area"]!, (string)o["quote"]!, "ai")).ToList();
        foreach (var c in changes)
        {
            var (action, kind, id) = ((string)c!["action"]!, (string)c["kind"]!, (string)c["id"]!);
            if (kind == Outside)
            {
                if (action == "remove") outside.RemoveAll(o => id == "all" || o.Area == id);
                else outside.Add(new OutsidePart(id, (string?)c["note"] ?? "", (string)c["by"]!));
                continue;
            }
            points.RemoveAll(p => p.Kind == kind && p.Id == id);
            if (action == "add") points.Add(new StartingPoint(kind, id, TitleOf(ctx, kind, id), "developer", "", "", (string)c["by"]!));
        }

        outside = [.. outside.DistinctBy(o => (o.Area, o.Quote))];

        var result = new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = paths.Key,
            ["ticketHash"] = "sha256:" + CanonicalJson.Hash(CanonicalJson.Serialize(pulled)),
            ["commit"] = ctx.Graph.Commit,
            ["points"] = new JsonArray([.. points.Select(p => (JsonNode)p.ToJson())]),
            ["outside"] = new JsonArray([.. outside.Select(o => (JsonNode)o.ToJson())]),
            ["counts"] = new JsonObject
            {
                ["code"] = code.Count, ["ai"] = found.Count,
                ["developer"] = points.Count(p => p.Signal == "developer"),
                ["outside"] = outside.Count,
            },
            ["changes"] = new JsonArray([.. changes]),
            ["ai"] = ai,
        };
        Quotes.WriteAtomic(paths.PointsJson, CanonicalJson.Serialize(result));
        return result;
    }

    public static JsonObject LoadOrFail(string root, string key) => PageRun.LoadJson(new TicketPaths(root, key).PointsJson) as JsonObject
        ?? throw new StangentException("no_points", $"No starting points for {key} yet. Run `stangent ticket points {key}` first.", ExitCodes.NotFound);

    /// <summary>The change kind for an outside-the-code part (D83); its id is the area, or <c>all</c> on a removal.</summary>
    public const string Outside = "outside";

    static JsonObject Change(string action, (string Kind, string Id) target, string who) =>
        new() { ["action"] = action, ["kind"] = target.Kind, ["id"] = target.Id, ["by"] = who };

    /// <summary>A page by id, path or entry point; a symbol by graph id; a file by its path in the repo; else a symbol
    /// by a unique display name.</summary>
    /// <param name="files">On a removal: the file points already recorded, so one whose file is gone can still go.</param>
    static (string Kind, string Id) Resolve(StangentRepo repo, TicketContext ctx, string text, IReadOnlySet<string>? files)
    {
        if (ctx.Set.Find(text) is { } page) return (StartingPoint.Page, page.Id);
        if (ctx.Analyzer.Find(text) is { } node) return (StartingPoint.Symbol, node.Id);
        if (RepoFile(repo.Root, text) is { } file) return (StartingPoint.File, file);
        if (files is not null && CleanPath(text) is { } gone && files.Contains(gone)) return (StartingPoint.File, gone);
        var named = ctx.Analyzer.Resolve(text).Where(n => n.Kind is not (NodeKind.File or NodeKind.Project)).ToList();
        return named.Count switch
        {
            1 => (StartingPoint.Symbol, named[0].Id),
            0 => throw new StangentException("not_found", $"'{text}' is neither a page, a symbol in the graph nor a file in the repo.", ExitCodes.NotFound),
            _ => throw new StangentException("ambiguous", $"'{text}' matches {named.Count} symbols; give one id.", ExitCodes.Usage,
                [.. named.Take(10).Select(n => n.Id)]),
        };
    }

    static string TitleOf(TicketContext ctx, string kind, string id) => kind switch
    {
        StartingPoint.Page => ctx.Set.Find(id) is { } p ? ctx.Title(p) : id,
        StartingPoint.File => Path.GetFileName(id),
        _ => ctx.Analyzer.Find(id)?.Name ?? id,
    };

    /// <summary>The path cleaned: trimmed, forward slashes, no leading <c>./</c>; null for an absolute path or one with
    /// <c>..</c>.</summary>
    static string? CleanPath(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p.Length == 0 || Path.IsPathRooted(p) || p.Split('/').Contains("..") ? null : p;
    }

    /// <summary>A repo-relative file path when <paramref name="path"/> names a file git tracks at the commit checked out,
    /// written exactly as git stores it (D101): so never stangent's folder, build output, an untracked or ignored file,
    /// or a path in the wrong letter case. Outside a git repo (or with git missing): a file inside the repo folder,
    /// outside <c>.stangent/</c> and <c>.git</c>.</summary>
    public static string? RepoFile(string root, string path)
    {
        if (CleanPath(path) is not { } p) return null;
        var first = p.Split('/')[0];
        if (first.Equals(".stangent", StringComparison.OrdinalIgnoreCase) || first.Equals(".git", StringComparison.OrdinalIgnoreCase)) return null;
        if (Tracked(root) is { } tracked) return tracked.Contains(p) ? p : null;
        var full = Path.GetFullPath(Path.Combine(root, p));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.Ordinal) && File.Exists(full) ? p : null;
    }

    /// <summary>The files git tracks at HEAD under the repo folder (relative to it, even when stangent's root is a
    /// subfolder of the git repo), or null when the folder isn't in a git repo. Kept per folder and commit.</summary>
    static HashSet<string>? Tracked(string root)
    {
        var head = Stangent.Graph.Git.Head(root);
        if (head.Length == 0) return null;
        lock (TrackedCache)
        {
            if (TrackedCache.TryGetValue(root, out var hit) && hit.Head == head) return hit.Files;
            var list = Stangent.Graph.Git.Run(root, "ls-tree -r --name-only -z HEAD");
            if (list is null) return null;
            HashSet<string> files = [.. list.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
            TrackedCache[root] = (head, files);
            return files;
        }
    }

    static readonly Dictionary<string, (string Head, HashSet<string> Files)> TrackedCache = new(StringComparer.Ordinal);

    /// <summary>A file's text at HEAD as git stores it (what the finder searched), else as it is on disk. <c>./</c>:
    /// relative to the repo folder, which may be a subfolder of the git repo.</summary>
    static string FileText(string root, string path) =>
        Tracked(root) is not null && Stangent.Graph.Git.Run(root, $"show \"HEAD:./{path}\"") is { } text ? text : File.ReadAllText(Path.Combine(root, path));

    /// <summary>The file finder's state in points.json: <c>off</c> (tickets.aiPoints false), <c>disabled</c> (ai.enabled
    /// false), <c>needed</c>, or the recorded outcome (<c>found</c>, <c>none</c>, <c>failed</c>) when its input is unchanged.</summary>
    static JsonObject AiSection(StangentRepo repo, TicketText text, JsonObject? previous, bool refind)
    {
        if (!repo.Config.Tickets.AiPoints) return new JsonObject { ["status"] = "off" };
        if (!repo.Config.Ai.Enabled) return new JsonObject { ["status"] = "disabled" };
        var inputs = FinderInputHash(text);
        // The answer is kept while the ticket is the same, even after the code moves on: a new search would see the
        // half-done fix. It is marked stale instead, and searched again only on --refind (D101).
        if ((string?)previous?["inputs"] == inputs && (string?)previous["status"] is "found" or "none" or "failed")
        {
            var kept = previous.DeepClone().AsObject();
            var now = CodeState(repo.Root);
            if ((string?)kept["commit"] is { } then && then != now) kept["stale"] = true; else kept.Remove("stale");
            if (!refind) return kept;
            // A new search, asked for: the old answer stays (files, outside parts) until the new one is recorded, so a
            // search that fails half-way leaves the ticket as it was.
            kept["status"] = "needed";
            kept["refind"] = true;
            return kept;
        }
        // A refind already asked for and not yet answered keeps waiting, with the old answer.
        if ((string?)previous?["inputs"] == inputs && (string?)previous["status"] == "needed" && previous["refind"] is not null)
            return previous.DeepClone().AsObject();
        return new JsonObject { ["status"] = "needed", ["inputs"] = inputs };
    }

    /// <summary>The commit checked out: what the finder searched (a clean worktree of it, so local edits play no part),
    /// recorded beside its answer and compared to judge it stale.</summary>
    internal static string CodeState(string root) => Stangent.Graph.Git.Head(root);

    // ---- the file finder (D101) ----

    /// <summary>The finder's input: its instructions and schema, then the ticket (prose and text attachments). It
    /// searches the repo itself; nothing else of stangent's is given, so it starts where a developer would.</summary>
    public static string FinderInput(TicketText text)
    {
        var sb = AiStep.Header(StepSpec.Points);
        sb.Append("# The ticket\n\n");
        foreach (var (where, body) in text.Prose) sb.Append($"## {where}\n\n````text\n{body}\n````\n\n");
        foreach (var (where, body) in text.Attachments) sb.Append($"## {where}\n\n````text\n{body}\n````\n\n");
        return sb.ToString();
    }

    /// <summary>What decides whether the finder runs again: its instructions and the ticket. The code it searched is
    /// recorded beside the answer (<c>commit</c>), not hashed (D101).</summary>
    static string FinderInputHash(TicketText text) => CanonicalJson.Hash(FinderInput(text));

    /// <summary>The limits the driver runs the finder under (D101): read-only search inside the repo, never stangent's
    /// own folder (other tickets' drafts, earlier runs), structured output instead of a written file, and its cap.
    /// Paths absolute (D89).</summary>
    /// <remarks>The driver searches a throwaway git worktree of the same commit instead of the repo folder when it can
    /// (no <c>.stangent/</c>, no untracked or ignored files): it swaps <c>root</c> for the worktree in every rule. A
    /// retry gets what is left of the cap (D81).</remarks>
    public static JsonObject DriverLimits(StangentRepo repo, string? key = null)
    {
        var root = Proof.ClaudeRules.Absolute(repo.Root);
        var cap = repo.Config.Tickets.FindBudget;
        var spent = 0.0;
        if (key is not null && PageRun.LoadJson(Path.Combine(AiStep.Work(new TicketPaths(repo.Root, key), StepSpec.Points), "state.json")) is JsonObject state)
            spent = Usage.FromJson(state["usage"]).CostUsd;
        return new JsonObject
        {
            ["root"] = root,
            ["tools"] = "Read,Grep,Glob",
            ["allowedTools"] = new JsonArray($"Read({root}/**)"),
            ["disallowedTools"] = new JsonArray($"Read({root}/.stangent/**)", $"Read({root}/.git/**)"),
            ["permissionMode"] = "dontAsk",
            ["maxTurns"] = cap.Turns,
            ["maxBudgetUsd"] = Math.Round(Math.Max(cap.Usd - spent, 0.01), 4),
        };
    }

    public static RunStep Prepare(StangentRepo repo, TicketContext ctx, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var points = LoadOrFail(repo.Root, key);
        var status = (string?)points["ai"]?["status"];
        if (status is "off" or "disabled")
            return new RunStep("current", "none", Errors: [status == "off" ? "tickets.aiPoints is off: code's signals are the starting points" : "AI is switched off for this repo (ai.enabled: false)"]);
        if (status is "found" or "none") return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, "ticket prepare --step points");
        var text = TicketText.From(TicketPull.LoadOrFail(repo.Root, key));
        return AiStep.Start(paths, StepSpec.Points, FinderInput(text), FinderInputHash(text), new JsonObject { ["commit"] = CodeState(repo.Root) });
    }

    public static RunStep Check(StangentRepo repo, TicketContext ctx, string key, Usage? usage, string who)
    {
        var paths = new TicketPaths(repo.Root, key);
        var step = StepSpec.Points;
        var state = AiStep.State(paths, step);
        var (output, errors) = AiStep.Read(paths, step, state, usage);
        var text = TicketText.From(TicketPull.LoadOrFail(repo.Root, key));
        var quotable = text.QuotableWithAttachments;
        var files = new JsonArray();
        if (output is not null)
            foreach (var (f, i) in output["files"]!.AsArray().Select((f, i) => (f!, i)))
            {
                var given = (string)f["path"]!;
                var path = RepoFile(repo.Root, given);
                var before = errors.Count;
                if (path is null) errors.Add($"files[{i}]: `{Short(given)}` isn't a file in the repo. Give the path relative to the repo root, as Glob or Grep showed it.");
                if (!Quotes.InText((string)f["quote"]!, quotable))
                    errors.Add($"files[{i}]: the quote \"{Short((string)f["quote"]!)}\" isn't in the ticket word for word (at least 4 words, or a whole line).");
                if (path is not null && Boilerplate((string)f["line"]!))
                    errors.Add($"files[{i}]: the line \"{Short((string)f["line"]!)}\" is an import or namespace line, found in almost any file: give the code that connects the file to the ticket.");
                else if (path is not null && !Quotes.InSource((string)f["line"]!, FileLines(repo.Root, path)))
                    errors.Add($"files[{i}]: the line \"{Short((string)f["line"]!)}\" isn't in {path} as written (copy at least {Quotes.MinSourceChars} characters and two words exactly).");
                if (errors.Count == before && files.All(x => (string)x!["path"]! != path))
                    files.Add(new JsonObject { ["path"] = path, ["quote"] = (string)f["quote"]!, ["line"] = (string)f["line"]!, ["why"] = (string)f["why"]! });
            }
        if (output is not null)
            foreach (var (part, i) in (output["outside"]?.AsArray() ?? []).Select((p, i) => (p!, i)))
                if (!Quotes.InText((string)part["quote"]!, quotable))
                    errors.Add($"outside[{i}]: the quote \"{Short((string)part["quote"]!)}\" isn't in the ticket word for word (at least 4 words, or a whole line).");
        // No answer at all usually means it ran out of turns or budget: say so, so the retry answers with what it found.
        var outputFile = Path.Combine(AiStep.Work(paths, step), step.OutputFile);
        if (output is null && (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0))
            errors.Insert(0, "You returned no answer (most likely you ran out of turns or budget). This time, answer after a few searches with the files you are surest of.");
        if (errors.Count > 0 && AiStep.Retry(paths, step, state, errors) is { } retry) return retry;

        // After the retry, files that passed are kept and the ones that didn't are listed apart (D101), never dropped silently.
        var rejected = output is null ? new JsonArray() : new JsonArray([.. output["files"]!.AsArray()
            .Where(f => RepoFile(repo.Root, (string)f!["path"]!) is not { } p || files.All(x => (string)x!["path"]! != p))
            .Select(f => (JsonNode)new JsonObject { ["path"] = (string)f!["path"]!, ["quote"] = (string)f["quote"]!, ["line"] = (string)f["line"]! })]);
        var outside = output is null ? new JsonArray() : new JsonArray([.. (output["outside"]?.AsArray() ?? [])
            .Where(o => Quotes.InText((string)o!["quote"]!, quotable))
            .Select(o => (JsonNode)new JsonObject { ["area"] = (string)o!["area"]!, ["quote"] = (string)o["quote"]! })
            .DistinctBy(o => ((string)o["area"]!, (string)o["quote"]!))]);
        var points = LoadOrFail(repo.Root, key);
        var ai = new JsonObject
        {
            // Found: some file passed, or only parts outside the code with nothing named in it. Named files that all
            // failed their checks is a failed search, whatever else came back.
            ["status"] = output is null ? "failed" : files.Count > 0 ? "found" : rejected.Count > 0 ? "failed" : outside.Count > 0 ? "found" : "none",
            ["inputs"] = state["inputs"]!.DeepClone(), ["commit"] = (string?)state["commit"], ["usage"] = state["usage"]!.DeepClone(), ["retries"] = (int?)state["retries"] ?? 0,
            ["files"] = files, ["outside"] = outside,
        };
        if (rejected.Count > 0) ai["rejected"] = rejected;
        if (errors.Count > 0) ai["errors"] = new JsonArray([.. errors.Take(10).Select(e => (JsonNode)e)]);
        points["ai"] = ai;
        Quotes.WriteAtomic(paths.PointsJson, CanonicalJson.Serialize(points));
        Update(repo, ctx, key, [], [], who);
        state["stage"] = "done";
        AiStep.SaveState(paths, step, state);
        return (string)ai["status"]! == "failed" ? new RunStep("failed", "none", Errors: errors) : new RunStep("recorded", "none", Errors: errors.Count > 0 ? errors : null);
    }

    /// <summary>A file's lines, each with its spaces collapsed, as <see cref="Quotes.InSource"/> compares them.</summary>
    static List<string> FileLines(string root, string path) =>
        [.. FileText(root, path).Replace("\r\n", "\n").Split('\n').Select(Quotes.Normalize)];

    /// <summary>A line almost every file has (an import, a namespace), so it ties a file to nothing in the ticket.</summary>
    static bool Boilerplate(string line) => Regex.IsMatch(line.Trim(),
        @"^(using\s+[\w.=\s]+;|namespace\s+[\w.]+\s*[;{]?|import\s|from\s+\S+\s+import\s|package\s+[\w.]+;?|#include\s|export\s+\*\s+from\s|(const|let|var)\s+\w+\s*=\s*require\()", RegexOptions.CultureInvariant);

    static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
