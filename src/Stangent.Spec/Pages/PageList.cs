using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Stangent.Graph;

namespace Stangent.Spec.Pages;

public static class PageKind
{
    /// <summary>A frontend route, or a server-rendered action that returns a view (D54).</summary>
    public const string Screen = "screen";
    /// <summary>An entry point no screen reaches: an endpoint only other services call, an event or command handler.</summary>
    public const string Backend = "backend";
    /// <summary>A component used by 3 or more screens; the screens link to it instead of describing it again.</summary>
    public const string Component = "component";
}

/// <summary>
/// One reference page (D54). <see cref="Id"/> is stable and comes from the graph: <c>page:&lt;route&gt;</c> for a screen,
/// the entry point's graph id otherwise. <see cref="Path"/> is where its files live under <c>specs/reference/</c>,
/// without the extension.
/// </summary>
public sealed record PageDef(string Id, string Kind, string Entry, string Title, string Group, string Path);

/// <summary>The page list and each page's slice, worked out together: which components are shared and which entry
/// points a screen reaches both depend on the screens' slices.</summary>
public sealed class PageSet
{
    public required IReadOnlyList<PageDef> Pages { get; init; }
    public required IReadOnlyDictionary<string, Slice> Slices { get; init; }
    public required SliceBuilder Builder { get; init; }
    public required SourceReader Source { get; init; }
    public required SpecSettings Settings { get; init; }
    /// <summary>The commit the graph was built from: what a recorded page says it describes.</summary>
    public required string Commit { get; init; }

    Dictionary<string, PageDef>? byKey;

    /// <summary>A page by its id, its path, or its entry point's graph id.</summary>
    public PageDef? Find(string idOrPath)
    {
        byKey ??= Pages.SelectMany(p => new[] { (p.Path, p), (p.Entry, p), (p.Id, p) })
            .GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.Last().p, StringComparer.Ordinal);
        return byKey.GetValueOrDefault(idOrPath);
    }
}

public static partial class PageList
{
    /// <summary>A component in this many screens' slices or more gets its own page (D54).</summary>
    public const int SharedComponentScreens = 3;

    [GeneratedRegex(@"\b(?:View|PartialView)\s*\(")]
    private static partial Regex ReturnsView();

    public static PageSet Build(CodeGraph graph, string repoRoot, SpecSettings settings)
    {
        var builder = new SliceBuilder(graph, settings.HubCallers);
        var source = new SourceReader(repoRoot);
        var depth = settings.Depth;
        var nodes = builder.AllNodes.ToList();

        // Screens: frontend routes, plus server-rendered actions (GET endpoints whose action returns a view).
        var screens = new List<(string Id, Node Entry, string Group)>();
        var routes = nodes.Where(n => n.Kind == NodeKind.Page).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        var routeCounts = routes.GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.Count());
        foreach (var r in routes)
        {
            var frontend = (r.Project ?? "").Replace("project:", "");
            screens.Add((routeCounts[r.Name] > 1 ? $"page:{frontend}|{r.Name}" : $"page:{r.Name}", r, frontend));
        }
        var routesTo = graph.AllEdges.Where(e => e.Kind == EdgeKind.RoutesTo).ToLookup(e => e.Source, StringComparer.Ordinal);
        foreach (var ep in nodes.Where(n => n.Kind == NodeKind.Endpoint).OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            var (verb, route) = SplitEndpoint(ep.Name);
            if (verb is not ("GET" or "ANY")) continue;
            if (!routesTo[ep.Id].Any(e => builder.Find(e.Target) is { } action && ReturnsView().IsMatch(source.Text(action)))) continue;
            screens.Add(($"page:{route}", ep, Project(builder, ep)));
        }
        // Two server-rendered screens on one route (GET and ANY): keep the first by id.
        screens = [.. screens.GroupBy(s => s.Id).Select(g => g.First())];

        // Components shared by 3 or more screens get their own page; screens link to them instead of entering them.
        // Split at the API boundary: screens stop at the endpoints they call, which have pages of their own.
        var split = settings.SplitAtEndpoints;
        IReadOnlySet<string>? frontStop = split ? new HashSet<string> { NodeKind.Endpoint } : null;
        var plain = screens.ToDictionary(s => s.Id, s => builder.Build(s.Entry.Id, depth, null, frontStop), StringComparer.Ordinal);
        var shared = plain.Values.SelectMany(s => s.Nodes.Where(n => n.Node.Kind == NodeKind.Component).Select(n => n.Node.Id).Distinct())
            .GroupBy(id => id).Where(g => g.Count() >= SharedComponentScreens).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        // A screen's own routed component is the screen, not a shared component.
        var routed = screens.SelectMany(s => routesTo[s.Entry.Id].Select(e => e.Target)).ToHashSet(StringComparer.Ordinal);
        shared.ExceptWith(routed);

        var pages = new List<PageDef>();
        var slices = new Dictionary<string, Slice>(StringComparer.Ordinal);
        foreach (var (id, entry, group) in screens)
        {
            pages.Add(new PageDef(id, PageKind.Screen, entry.Id, ScreenTitle(builder, entry, routesTo), group, "screens/" + Slug(id["page:".Length..], id)));
            slices[id] = builder.Build(entry.Id, depth, shared, frontStop);
        }
        foreach (var c in shared.Order(StringComparer.Ordinal))
        {
            var node = builder.Find(c)!;
            pages.Add(new PageDef(c, PageKind.Component, c, node.Name, Project(builder, node), "components/" + Slug(node.Name, c)));
            slices[c] = builder.Build(c, depth, shared, frontStop);
        }

        // Backend pages: endpoints no screen or shared component reaches, and every event or command handler.
        var reached = slices.Values.SelectMany(s => s.Nodes.Where(n => n.Role == "walk").Select(n => n.Node.Id)).ToHashSet(StringComparer.Ordinal);
        var screenEntries = screens.Select(s => s.Entry.Id).ToHashSet(StringComparer.Ordinal);
        var backend = nodes.Where(n => (n.Kind == NodeKind.Endpoint && (split || !reached.Contains(n.Id)) && !screenEntries.Contains(n.Id))
                                       || builder.Handlers.Contains(n.Id))
            .Where(n => !n.IsTest)
            .OrderBy(n => n.Id, StringComparer.Ordinal);
        foreach (var b in backend)
        {
            var group = Project(builder, b);
            var title = b.Kind == NodeKind.Endpoint ? b.Name : HandlerTitle(b);
            pages.Add(new PageDef(b.Id, PageKind.Backend, b.Id, title, group, $"backend/{Slug(group, group)}/{Slug(title, b.Id)}"));
            slices[b.Id] = builder.Build(b.Id, split ? settings.BackendDepth : depth, shared);
        }

        return new PageSet
        {
            Pages = [.. pages.OrderBy(p => KindOrder(p.Kind)).ThenBy(p => p.Id, StringComparer.Ordinal)],
            Slices = slices, Builder = builder, Source = source, Settings = settings, Commit = graph.Commit,
        };
    }

    static int KindOrder(string kind) => kind switch { PageKind.Screen => 0, PageKind.Component => 1, _ => 2 };

    public static (string Verb, string Route) SplitEndpoint(string name)
    {
        var space = name.IndexOf(' ');
        return space < 0 ? ("ANY", name) : (name[..space], name[(space + 1)..]);
    }

    /// <summary>The project a point belongs to, by its display name (<c>Sonarr.Api.V3</c>, <c>frontend</c>).</summary>
    static string Project(SliceBuilder builder, Node n)
    {
        if (n.Project is { } p) return builder.Find(p)?.Name ?? p.Replace("project:", "");
        if (n.Id.StartsWith("endpoint:", StringComparison.Ordinal)) return n.Id["endpoint:".Length..].Split('|')[0];
        var bar = n.Id.IndexOf('|');
        return bar > 0 ? n.Id[..bar] : "other";
    }

    static string ScreenTitle(SliceBuilder builder, Node entry, ILookup<string, Edge> routesTo)
    {
        var shows = routesTo[entry.Id].Select(e => builder.Find(e.Target)?.Name).Where(n => n is not null).Order(StringComparer.Ordinal).ToList();
        var route = entry.Kind == NodeKind.Endpoint ? SplitEndpoint(entry.Name).Route : entry.Name;
        return shows.Count == 0 ? route : $"{route} · {string.Join(", ", shows.Select(s => s!.Split('.')[0]))}";
    }

    /// <summary><c>SeriesAddedHandler.Handle(SeriesAddedEvent)</c>: the class and the event it handles.</summary>
    static string HandlerTitle(Node n)
    {
        var id = n.Id;
        var open = id.IndexOf('(');
        var parameters = open < 0 ? "" : id[(open + 1)..id.LastIndexOf(')')];
        var evt = parameters.Split(',')[0];
        evt = evt[(evt.LastIndexOf('.') + 1)..];
        return evt.Length == 0 ? n.Name : $"{n.Name}({evt})";
    }

    /// <summary>A readable, file-safe name plus 6 hex of the id's hash, so two ids never share a file.</summary>
    public static string Slug(string readable, string id)
    {
        var s = Regex.Replace(readable.ToLowerInvariant(), "[^a-z0-9.]+", "-").Trim('-', '.');
        if (s.Length > 80) s = s[..80].Trim('-', '.');
        if (s.Length == 0) s = "root";
        return $"{s}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..6]}";
    }
}

/// <summary>Reads the source text of graph points from the working tree, one file at a time.</summary>
public sealed class SourceReader(string root)
{
    readonly Dictionary<string, string[]?> files = new(StringComparer.Ordinal);

    public string[]? Lines(string file)
    {
        if (files.TryGetValue(file, out var cached)) return cached;
        var path = Path.Combine(root, file);
        var lines = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n").Split('\n') : null;
        files[file] = lines;
        return lines;
    }

    /// <summary>The point's lines, or "" when it has no file or the file has gone.</summary>
    public string Text(Node n)
    {
        if (n.File is null || n.Line <= 0 || Lines(n.File) is not { } lines) return "";
        var from = Math.Min(n.Line, lines.Length);
        var to = Math.Min(Math.Max(n.Line, n.EndLine), lines.Length);
        return string.Join("\n", lines[(from - 1)..to]);
    }
}
