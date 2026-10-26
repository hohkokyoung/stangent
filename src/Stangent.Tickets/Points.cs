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

/// <summary>A place in the code a ticket's work starts from (D70): a reference page or a graph symbol, and why.</summary>
public sealed record StartingPoint(string Kind, string Id, string Title, string Signal, string Evidence, string Where, string By)
{
    public const string Page = "page";
    public const string Symbol = "symbol";

    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind, ["id"] = Id, ["title"] = Title, ["signal"] = Signal, ["evidence"] = Evidence, ["where"] = Where, ["by"] = By,
    };

    public static StartingPoint FromJson(JsonNode n) => new((string)n["kind"]!, (string)n["id"]!, (string)n["title"]!, (string)n["signal"]!,
        (string)n["evidence"]!, (string)n["where"]!, (string)n["by"]!);
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
}

/// <summary>
/// Starting points by code (D70 step 1): stack-trace frames → methods; URL paths → route pages and endpoint pages;
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
/// <c>stangent ticket points</c> and the point picker (D70): code's points, then (only when code found none and
/// <c>tickets.aiPoints</c> is on) the AI's picks from page titles, then the developer's additions and removals, replayed
/// in order and recorded with who made them. Written to <c>points.json</c>; same inputs → same file.
/// </summary>
public static class TicketPoints
{
    public static JsonObject Update(StangentRepo repo, TicketContext ctx, string key, IReadOnlyList<string> add, IReadOnlyList<string> remove, string who)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var text = TicketText.From(pulled);
        var previous = PageRun.LoadJson(paths.PointsJson) as JsonObject;

        var changes = previous?["changes"]?.AsArray().Select(c => c!.DeepClone()).ToList() ?? [];
        foreach (var a in add) changes.Add(Change("add", Resolve(ctx, a), who));
        foreach (var r in remove) changes.Add(Change("remove", Resolve(ctx, r), who));

        var code = PointFinder.Find(text, ctx);
        var ai = AiSection(repo, ctx, paths, text, code.Count, previous?["ai"] as JsonObject);
        var picked = (ai["picks"] as JsonArray ?? []).Select(p => ctx.Set.Find((string)p!["page"]!) is { } page
            ? new StartingPoint(StartingPoint.Page, page.Id, ctx.Title(page), "ai", (string)p["quote"]!, "ticket", "ai") : null).OfType<StartingPoint>();

        var points = code.Concat(picked).ToList();
        foreach (var c in changes)
        {
            var (action, kind, id) = ((string)c!["action"]!, (string)c["kind"]!, (string)c["id"]!);
            points.RemoveAll(p => p.Kind == kind && p.Id == id);
            if (action == "add") points.Add(new StartingPoint(kind, id, TitleOf(ctx, kind, id), "developer", "", "", (string)c["by"]!));
        }

        var result = new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = paths.Key,
            ["ticketHash"] = "sha256:" + CanonicalJson.Hash(CanonicalJson.Serialize(pulled)),
            ["commit"] = ctx.Graph.Commit,
            ["points"] = new JsonArray([.. points.Select(p => (JsonNode)p.ToJson())]),
            ["counts"] = new JsonObject
            {
                ["code"] = code.Count, ["ai"] = picked.Count(),
                ["developer"] = points.Count(p => p.Signal == "developer"),
            },
            ["changes"] = new JsonArray([.. changes]),
            ["ai"] = ai,
        };
        Quotes.WriteAtomic(paths.PointsJson, CanonicalJson.Serialize(result));
        return result;
    }

    public static JsonObject LoadOrFail(string root, string key) => PageRun.LoadJson(new TicketPaths(root, key).PointsJson) as JsonObject
        ?? throw new StangentException("no_points", $"No starting points for {key} yet. Run `stangent ticket points {key}` first.", ExitCodes.NotFound);

    static JsonObject Change(string action, (string Kind, string Id) target, string who) =>
        new() { ["action"] = action, ["kind"] = target.Kind, ["id"] = target.Id, ["by"] = who };

    /// <summary>A page by id, path or entry point; else a symbol by graph id or a unique display name.</summary>
    static (string Kind, string Id) Resolve(TicketContext ctx, string text)
    {
        if (ctx.Set.Find(text) is { } page) return (StartingPoint.Page, page.Id);
        if (ctx.Analyzer.Find(text) is { } node) return (StartingPoint.Symbol, node.Id);
        var named = ctx.Analyzer.Resolve(text).Where(n => n.Kind is not (NodeKind.File or NodeKind.Project)).ToList();
        return named.Count switch
        {
            1 => (StartingPoint.Symbol, named[0].Id),
            0 => throw new StangentException("not_found", $"'{text}' is neither a page nor a symbol in the graph.", ExitCodes.NotFound),
            _ => throw new StangentException("ambiguous", $"'{text}' matches {named.Count} symbols; give one id.", ExitCodes.Usage,
                [.. named.Take(10).Select(n => n.Id)]),
        };
    }

    static string TitleOf(TicketContext ctx, string kind, string id) =>
        kind == StartingPoint.Page ? (ctx.Set.Find(id) is { } p ? ctx.Title(p) : id) : ctx.Analyzer.Find(id)?.Name ?? id;

    /// <summary>The AI step's state in points.json: <c>not-needed</c> (code found points), <c>off</c>, <c>disabled</c>
    /// (ai.enabled false), <c>needed</c>, or the recorded outcome (<c>picked</c>, <c>none</c>, <c>failed</c>) when its
    /// input is unchanged.</summary>
    static JsonObject AiSection(StangentRepo repo, TicketContext ctx, TicketPaths paths, TicketText text, int codePoints, JsonObject? previous)
    {
        if (codePoints > 0) return new JsonObject { ["status"] = "not-needed" };
        if (!repo.Config.Tickets.AiPoints) return new JsonObject { ["status"] = "off" };
        if (!repo.Config.Ai.Enabled) return new JsonObject { ["status"] = "disabled" };
        var inputs = PickerInputHash(ctx, text);
        if ((string?)previous?["inputs"] == inputs && (string?)previous["status"] is "picked" or "none" or "failed")
            return previous.DeepClone().AsObject();
        return new JsonObject { ["status"] = "needed", ["inputs"] = inputs };
    }

    // ---- the point picker (D70 step 2) ----

    public static string PickerInput(TicketContext ctx, TicketText text)
    {
        var sb = AiStep.Header(StepSpec.Points);
        sb.Append("# The ticket\n\n");
        foreach (var (where, body) in text.Prose) sb.Append($"## {where}\n\n````text\n{body}\n````\n\n");
        sb.Append("# The pages\n\n| id | kind | title | from the code |\n|---|---|---|---|\n");
        foreach (var p in ctx.Set.Pages)
            sb.Append($"| `{p.Id}` | {p.Kind} | {ctx.Title(p).Replace("|", "\\|")} | {p.Title.Replace("|", "\\|")} |\n");
        return sb.ToString();
    }

    /// <summary>What decides a pick: the instructions, the ticket and the pages with their code titles. Not the written
    /// titles shown beside them: writing a page later (the pages it picked, say) doesn't ask for a new pick.</summary>
    static string PickerInputHash(TicketContext ctx, TicketText text)
    {
        var sb = AiStep.Header(StepSpec.Points);
        foreach (var (where, body) in text.Prose) sb.Append($"## {where}\n\n{body}\n\n");
        foreach (var p in ctx.Set.Pages) sb.Append($"{p.Id}\t{p.Kind}\t{p.Title}\n");
        return CanonicalJson.Hash(sb.ToString());
    }

    public static RunStep Prepare(StangentRepo repo, TicketContext ctx, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var points = LoadOrFail(repo.Root, key);
        var status = (string?)points["ai"]?["status"];
        if (status is "not-needed" or "off" or "disabled")
            return new RunStep("current", "none", Errors: [status switch
            {
                "not-needed" => "code found starting points; the point picker isn't needed",
                "off" => "tickets.aiPoints is off",
                _ => "AI is switched off for this repo (ai.enabled: false)",
            }]);
        if (status is "picked" or "none") return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, "ticket prepare --step points");
        var text = TicketText.From(TicketPull.LoadOrFail(repo.Root, key));
        var input = PickerInput(ctx, text);
        return AiStep.Start(paths, StepSpec.Points, input, PickerInputHash(ctx, text));
    }

    public static RunStep Check(StangentRepo repo, TicketContext ctx, string key, Usage? usage, string who)
    {
        var paths = new TicketPaths(repo.Root, key);
        var step = StepSpec.Points;
        var state = AiStep.State(paths, step);
        var (output, errors) = AiStep.Read(paths, step, state, usage);
        var text = TicketText.From(TicketPull.LoadOrFail(repo.Root, key));
        if (output is not null)
            foreach (var (pick, i) in output["pages"]!.AsArray().Select((p, i) => (p!, i)))
            {
                var page = (string)pick["page"]!;
                if (ctx.Set.Find(page) is null) errors.Add($"pages[{i}]: `{page}` isn't a page in the list. Copy ids exactly.");
                if (!Quotes.InText((string)pick["quote"]!, text.Quotable))
                    errors.Add($"pages[{i}]: the quote \"{Short((string)pick["quote"]!)}\" isn't in the ticket word for word (at least 4 words, or a whole line).");
            }
        if (errors.Count > 0 && AiStep.Retry(paths, step, state, errors) is { } retry) return retry;

        var points = LoadOrFail(repo.Root, key);
        var ai = new JsonObject
        {
            ["status"] = errors.Count > 0 ? "failed" : output!["pages"]!.AsArray().Count == 0 ? "none" : "picked",
            ["inputs"] = state["inputs"]!.DeepClone(), ["usage"] = state["usage"]!.DeepClone(), ["retries"] = (int?)state["retries"] ?? 0,
            ["picks"] = errors.Count > 0 ? new JsonArray() : new JsonArray([.. output!["pages"]!.AsArray()
                .Select(p => (JsonNode)new JsonObject { ["page"] = ctx.Set.Find((string)p!["page"]!)!.Id, ["quote"] = (string)p["quote"]! })
                .DistinctBy(p => (string)p["page"]!)]),
        };
        if (errors.Count > 0) ai["errors"] = new JsonArray([.. errors.Take(10).Select(e => (JsonNode)e)]);
        points["ai"] = ai;
        Quotes.WriteAtomic(paths.PointsJson, CanonicalJson.Serialize(points));
        Update(repo, ctx, key, [], [], who);
        state["stage"] = "done";
        AiStep.SaveState(paths, step, state);
        return errors.Count > 0 ? new RunStep("failed", "none", Errors: errors) : new RunStep("recorded", "none");
    }

    static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
