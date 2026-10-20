using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>A page proposed for an FSD section, and why: <c>name</c> (titles match), <c>fields</c> (field labels match
/// properties in the page's slice), <c>endpoint</c> (an endpoint a candidate screen calls), <c>ai</c> (proposed by the
/// candidate finder from page titles only).</summary>
public sealed record Candidate(string Page, string Why, double Score);

/// <summary>
/// Candidate pairs by code (D53, Phase 2b step 2): names that match across the two sides. Screen names ↔ routes,
/// components and page titles; field labels ↔ properties; endpoint names. Case and word order are normalised, plurals
/// and "-ing" folded. Deterministic: the same FSD and pages give the same candidates.
/// </summary>
public static partial class Candidates
{
    public const int MaxPerSection = 10;
    public const int MaxByName = 5;
    public const int MaxEndpointsPerScreen = 4;

    static readonly HashSet<string> Stop =
    [
        "a", "an", "the", "of", "for", "to", "and", "or", "in", "on", "with", "by", "from", "at", "as", "is", "are", "be", "this",
        "that", "it", "its", "page", "screen", "view", "api", "v1", "v2", "v3", "v4", "v5", "index", "modal", "content",
        "component", "get", "post", "put", "delete", "any", "head", "id", "new", "s",
    ];

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelBreak();

    /// <summary>Normalised words: camelCase split, lower case, plural and -ing folded, route parameters and stop words dropped.</summary>
    public static HashSet<string> Words(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var s = Regex.Replace(text, @"[:{][A-Za-z0-9_]+\}?", " ");
        s = CamelBreak().Replace(s, " ");
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(s.ToLowerInvariant(), "[a-z0-9]+"))
        {
            var w = Fold(m.Value);
            if (w.Length > 0 && !Stop.Contains(w)) words.Add(w);
        }
        return words;
    }

    static string Fold(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) w = w[..^3];
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        return w;
    }

    /// <summary>The names a page goes by: its route or entry point, its routed components, and its titles.</summary>
    public static List<HashSet<string>> PageNames(PageDef page, JsonObject? recorded)
    {
        var names = new List<HashSet<string>>();
        var parts = page.Title.Split(" · ");
        names.Add(Words(parts[0].Contains(' ') && page.Kind == PageKind.Backend ? parts[0][(parts[0].IndexOf(' ') + 1)..] : parts[0]));
        if (parts.Length > 1) names.AddRange(parts[1].Split(", ").Select(Words));
        if ((string?)recorded?["title"] is { } title) names.Add(Words(title));
        return [.. names.Where(n => n.Count > 0)];
    }

    /// <summary>Field labels a section names: first cells of its table rows (not the header), and "Label: …" list items.</summary>
    public static List<HashSet<string>> FieldLabels(string text)
    {
        var labels = new List<HashSet<string>>();
        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            // A table row below the header and its |---| line.
            if (lines[i].StartsWith('|') && i >= 2 && lines[i - 1].StartsWith('|') && !lines[i].StartsWith("|---", StringComparison.Ordinal)
                && lines[..i].Reverse().TakeWhile(l => l.StartsWith('|')).Any(l => l.StartsWith("|---", StringComparison.Ordinal)))
                labels.Add(Words(lines[i].Trim('|').Split('|')[0]));
            else if (Regex.Match(lines[i], @"^- ([^:]{2,40}):") is { Success: true } m)
                labels.Add(Words(m.Groups[1].Value));
        }
        return [.. labels.Where(w => w.Count > 0).DistinctBy(w => string.Join(" ", w.Order()))];
    }

    /// <param name="parent">The chapter the section sits in: a subsection is about part of its chapter's screen, so the
    /// chapter's screens are its candidates too.</param>
    public static List<Candidate> ForSection(JsonObject section, PageSet set, IReadOnlyDictionary<string, JsonObject> recorded, JsonObject? parent = null)
    {
        var title = Words((string)section["title"]!);
        var text = (string)section["text"]!;
        var byPage = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        void Add(string page, string why, double score)
        {
            if (!byPage.TryGetValue(page, out var have) || have.Score < score) byPage[page] = new Candidate(page, why, score);
        }

        // Names: one side's words all in the other's (word order and case don't matter).
        if (title.Count > 0)
        {
            var named = new List<Candidate>();
            foreach (var page in set.Pages)
            {
                var best = 0.0;
                foreach (var name in PageNames(page, recorded.GetValueOrDefault(page.Id)))
                {
                    var shared = name.Intersect(title).Count();
                    if (shared == 0 || !(name.IsSubsetOf(title) || title.IsSubsetOf(name))) continue;
                    best = Math.Max(best, (double)shared / name.Union(title).Count());
                }
                if (best > 0) named.Add(new Candidate(page.Id, "name", best + (page.Kind == PageKind.Screen ? 0.01 : 0)));
            }
            foreach (var c in named.OrderByDescending(c => c.Score).ThenBy(c => c.Page, StringComparer.Ordinal).Take(MaxByName)) Add(c.Page, c.Why, c.Score);
        }

        // Field labels ↔ properties and fields in a screen's or component's slice: at least two labels, or every label when there's one.
        var labels = FieldLabels(text);
        if (labels.Count > 0)
            foreach (var page in set.Pages.Where(p => p.Kind != PageKind.Backend))
            {
                var props = set.Slices[page.Id].Nodes.Where(n => n.Node.Kind is NodeKind.Property or NodeKind.Field)
                    .Select(n => Words(n.Node.Name.Split('.')[^1])).Where(w => w.Count > 0).ToList();
                var hits = labels.Count(l => props.Any(p => l.IsSubsetOf(p)));
                if (hits >= Math.Min(2, labels.Count)) Add(page.Id, "fields", 0.5 * hits / labels.Count);
            }

        // A subsection inherits its chapter's screens, found the same way, at a lower score.
        if (parent is not null)
            foreach (var c in ForSection(new JsonObject { ["title"] = parent["title"]!.DeepClone(), ["text"] = "" }, set, recorded)
                         .Where(c => c.Why == "name" && set.Find(c.Page)?.Kind == PageKind.Screen))
                Add(c.Page, "chapter", c.Score * 0.6);

        // The endpoints a candidate screen calls carry the rules behind its actions (pages split at endpoints): those
        // named like the section first, then those that save (not GET), which carry the validation.
        var words = Words(text).Union(title).ToHashSet();
        foreach (var screen in byPage.Values.Where(c => set.Find(c.Page)?.Kind == PageKind.Screen).ToList())
            foreach (var ep in set.Slices[screen.Page].Nodes.Where(n => n.Node.Kind == NodeKind.Endpoint && n.Role == "stop")
                         .Select(n => (Node: n.Node, Shared: Words(PageList.SplitEndpoint(n.Node.Name).Route).Intersect(words).Count(),
                             Writes: PageList.SplitEndpoint(n.Node.Name).Verb is not "GET"))
                         .Where(x => (x.Shared > 0 || x.Writes) && set.Find(x.Node.Id) is not null)
                         .OrderByDescending(x => x.Shared).ThenByDescending(x => x.Writes).ThenBy(x => x.Node.Id, StringComparer.Ordinal)
                         .Take(MaxEndpointsPerScreen))
                Add(ep.Node.Id, "endpoint", screen.Score * 0.5);

        return [.. byPage.Values.OrderByDescending(c => c.Score).ThenBy(c => c.Page, StringComparer.Ordinal).Take(MaxPerSection)];
    }
}
