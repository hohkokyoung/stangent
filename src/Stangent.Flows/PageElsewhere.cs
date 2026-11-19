using System.Text;
using System.Text.Json.Nodes;

namespace Stangent.Flows;

/// <summary>D316: one start flow that calls an endpoint, as the "Elsewhere" list names it: its area, readable name and link from the page.</summary>
public sealed record ElsewhereCaller(string Area, string Name, string Kind, string? Link);

/// <summary>D316: an endpoint of a controller this area lands on that none of its start flows calls, and who does.</summary>
public sealed record ElsewhereEndpoint(string Route, string At, IReadOnlyList<ElsewhereCaller> Callers);

/// <summary>D316: a controller this area's start flows land on, with its endpoints they don't call (up to <see cref="PageElsewhere.Cap"/>).</summary>
public sealed record ElsewhereController(string Name, string File, int Total, IReadOnlyList<ElsewhereEndpoint> Endpoints, IReadOnlyList<string> More)
{
    /// <summary>Endpoints no start flow calls, up to the cap, said on one line; and how many more there are.</summary>
    public IReadOnlyList<ElsewhereEndpoint> Uncalled { get; init; } = [];
    public int UncalledMore { get; init; }
}

/// <summary>
/// D316: an area page's "Elsewhere" list. For the backend controllers the area's start flows land on (an endpoint a request of
/// theirs, or of shared code they continue into, is matched to; or the MVC action a screen starts in), the controllers' other
/// endpoints that no start flow of this area calls, each with the areas and start flows that do, or "no start flow calls it".
/// Code only, from the flow files and the graph's endpoints.
/// </summary>
public static class PageElsewhere
{
    static readonly StringComparer O = StringComparer.Ordinal;
    /// <summary>Endpoints listed per controller; the rest are counted and named by route on one line.</summary>
    public const int Cap = 15;
    /// <summary>Callers named per endpoint; the rest are counted.</summary>
    public const int CallerCap = 3;
    public const string Heading = "## Elsewhere";

    /// <summary>Each start flow's endpoints: its own requests, those of shared flows it reaches, and the action it starts in.</summary>
    internal sealed class Index(FlowReader reader, Areas a)
    {
        readonly Dictionary<string, HashSet<string>> own = new(O);
        Dictionary<string, HashSet<string>>? byFlow;
        Dictionary<string, List<string>>? byEndpoint;

        HashSet<string> Own(string id)
        {
            if (own.TryGetValue(id, out var have)) return have;
            var set = new HashSet<string>(O);
            if (reader.Load(id) is { } flow)
                foreach (var st in (flow["steps"] as JsonArray ?? []).OfType<JsonObject>())
                    if ((string?)st["kind"] == "http" && (string?)st["point"] is { } p && p.StartsWith("endpoint:", StringComparison.Ordinal)) set.Add(p);
            return own[id] = set;
        }

        void Build()
        {
            if (byFlow is not null) return;
            byFlow = new Dictionary<string, HashSet<string>>(O);
            byEndpoint = new Dictionary<string, List<string>>(O);
            var byAction = a.Endpoints().GroupBy(e => e.Action, O).ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList(), O);
            foreach (var f in a.Flows.Values.Where(f => f.Kind != "shared").OrderBy(f => f.Id, O))
            {
                var set = new HashSet<string>(Own(f.Id), O);
                foreach (var s in a.Reach(f.Id, sharedOnly: true)) set.UnionWith(Own(s));
                if (byAction.TryGetValue(f.Point, out var mine)) set.UnionWith(mine);
                byFlow[f.Id] = set;
                foreach (var e in set)
                {
                    if (!byEndpoint.TryGetValue(e, out var list)) byEndpoint[e] = list = [];
                    list.Add(f.Id);
                }
            }
        }

        public IReadOnlySet<string> Of(string flow) { Build(); return byFlow!.TryGetValue(flow, out var s) ? s : new HashSet<string>(O); }

        /// <summary>The endpoints a start flow lands on itself: its own requests and the action it starts in (not shared code's).</summary>
        public IReadOnlySet<string> Lands(string flow)
        {
            var set = new HashSet<string>(Own(flow), O);
            if (a.Flows.TryGetValue(flow, out var f)) set.UnionWith(a.Endpoints().Where(e => e.Action == f.Point).Select(e => e.Id));
            return set;
        }

        public IReadOnlyList<string> Callers(string endpoint) { Build(); return byEndpoint!.TryGetValue(endpoint, out var l) ? l : []; }
    }

    /// <summary>The "Elsewhere" controllers of one area, in the order of their first endpoint's file.</summary>
    internal static List<ElsewhereController> Build(Index index, Areas a, Placement placement, AreaLayout? layout, string area, IEnumerable<string> starts)
    {
        var here = layout?.SlugOf.ContainsKey(area) == true ? layout.AreaFolder(area) : null;
        var reached = new HashSet<string>(O);
        var lands = new HashSet<string>(O);
        foreach (var s in starts) { reached.UnionWith(index.Of(s)); lands.UnionWith(index.Lands(s)); }
        var all = a.Endpoints();
        // The controllers its start flows land on themselves; one reached only through shared code (a helper's lookup) isn't the area's.
        var controllers = all.Where(e => lands.Contains(e.Id)).Select(e => e.Controller).ToHashSet(O);
        var result = new List<ElsewhereController>();
        foreach (var group in all.Where(e => controllers.Contains(e.Controller)).GroupBy(e => e.Controller, O).OrderBy(g => g.First().File, O).ThenBy(g => g.Key, O))
        {
            var mine = group.GroupBy(e => e.Id, O).Select(g => g.First()).ToList();
            var others = mine.Where(e => !reached.Contains(e.Id)).ToList();
            if (others.Count == 0) continue;
            var each = others.Select(e => new ElsewhereEndpoint(e.Route, $"{e.File}:{e.Line}", [.. index.Callers(e.Id)
                .Where(id => a.Flows.ContainsKey(id) && placement.AreaOf.ContainsKey(id))
                .Select(id => Caller(a.Flows[id], placement.AreaOf[id], layout, here))
                .OrderBy(c => c.Area, O).ThenBy(c => PageChart.KindRank(c.Kind)).ThenBy(c => c.Name.ToLowerInvariant(), O).ThenBy(c => c.Link, O)])).ToList();
            var called = each.Where(e => e.Callers.Count > 0).ToList();
            var uncalled = each.Where(e => e.Callers.Count == 0).ToList();
            result.Add(new ElsewhereController(group.First().ControllerName, group.First().File, mine.Count, [.. called.Take(Cap)], [.. called.Skip(Cap).Select(e => e.Route)])
                { Uncalled = [.. uncalled.Take(Cap)], UncalledMore = uncalled.Count - Math.Min(uncalled.Count, Cap) });
        }
        return result;
    }

    static ElsewhereCaller Caller(IndexFlow f, string area, AreaLayout? layout, string? here) =>
        new(area, f.Name == "" ? f.Id : f.Name, f.Kind, here is not null && layout!.FolderOf.ContainsKey(f.Id) ? AreaLayout.Relative(here, layout.Markdown(f.Id)) : null);

    /// <summary>The section's text: a heading, one line saying what it lists, then a line per controller and per endpoint.</summary>
    internal static string Render(IReadOnlyList<ElsewhereController> list)
    {
        if (list.Count == 0) return "";
        static string Code(string text) => $"`{text.Replace('`', '\'')}`";
        static string Short(string at) => at.Contains('/') ? at[(at.LastIndexOf('/') + 1)..] : at;
        static string Say(ElsewhereCaller c)
        {
            var name = c.Kind == "click" ? $"\"{c.Name}\"" : Code(PageChart.Readable(c.Name));
            return c.Link is null ? name : $"[{name.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)}]({c.Link})";
        }
        var sb = new StringBuilder($"\n{Heading}\n\n");
        sb.Append("Other endpoints of the controllers this area's start flows land on, which none of them calls, and the start flows that do.\n\n");
        foreach (var c in list)
        {
            var shown = c.Endpoints.Count + c.More.Count + c.Uncalled.Count + c.UncalledMore;
            sb.Append($"- {Code(c.Name)} ({Code(c.File)}): {shown} of its {c.Total} endpoints not called here\n");
            // Endpoints called by the very same start flows share one line (four cover uploads from one chooser's clicks).
            foreach (var same in c.Endpoints.GroupBy(e => string.Join("\u0001", e.Callers.Select(x => $"{x.Area}\u0002{x.Name}\u0002{x.Link}")), O))
            {
                var e = same.First();
                var named = e.Callers.Take(CallerCap).GroupBy(x => x.Area, O).Select(g => $"{g.Key}: {string.Join(", ", g.Select(Say))}");
                var routes = same.Count() == 1 ? $"{Code(e.Route)} ({Code(Short(e.At))})" : string.Join(", ", same.Select(x => $"{Code(x.Route)} ({Code(":" + x.At[(x.At.LastIndexOf(':') + 1)..])})"));
                sb.Append($"  - {routes}: called by {string.Join("; ", named)}{(e.Callers.Count > CallerCap ? $"; +{e.Callers.Count - CallerCap} more" : "")}\n");
            }
            if (c.More.Count > 0) sb.Append($"  - +{c.More.Count} more called elsewhere: {string.Join(", ", c.More.Select(Code))}\n");
            if (c.Uncalled.Count > 0)
                sb.Append($"  - no start flow calls: {string.Join(", ", c.Uncalled.Select(e => $"{Code(e.Route)} ({Code(":" + e.At[(e.At.LastIndexOf(':') + 1)..])})"))}{(c.UncalledMore > 0 ? $", +{c.UncalledMore} more" : "")}\n");
        }
        return sb.ToString();
    }
}
