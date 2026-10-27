using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Tickets;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// The page index's measure (D87), no AI: for each past bug, is a right page among the top <see cref="PageIndex.Detailed"/>
/// the point picker sees in detail? (Before 2026-10-28 a clear winner showed only itself and its "also possibly" pages;
/// winners are still counted, as the record of why that shortcut was dropped.) A page
/// is right when its slice's own walk reaches a file the fix changed. Bugs no page can be right for are reported apart.
/// <c>--score-index &lt;clone&gt; &lt;cases&gt; [--out file]</c>: cases are a tickets dir (Sonarr's frozen set: the pulled Jira
/// tickets in the clone and the answer key) or a JSON file of <c>{ id, summary, description, files }</c>.
/// </summary>
public static class IndexScore
{
    public sealed record Case(string Id, TicketText Text, HashSet<string> Fix);

    /// <param name="walkDepth">Right pages by a walk this deep from each page's entry instead of the page's own slice (0: the slice;
    /// negative: the page's own depth plus that many lines, e.g. -2 → 8 for a screen, 6 for a backend page).</param>
    public static JsonObject Run(string clone, string cases, TextWriter log, int walkDepth = 0, RankSettings? settings = null)
    {
        var repo = StangentRepo.Discover(clone);
        var clock = Stopwatch.StartNew();
        var ctx = TicketContext.Load(repo);
        var loaded = clock.Elapsed.TotalSeconds;
        var index = PageIndex.Build(ctx, repo.Root, repo.Config, settings);
        var built = clock.Elapsed.TotalSeconds - loaded;
        log.WriteLine($"{ctx.Set.Pages.Count} pages, {index.UnitCount} units; graph {loaded:F1}s, index {built:F1}s");

        var list = Load(clone, cases);
        var walkFiles = WalkFiles(ctx, walkDepth);

        var rows = new JsonArray();
        int reachable = 0, hit = 0, top20 = 0, winners = 0, winnerRight = 0, winnerHit = 0;
        var rankTimes = new List<double>();
        foreach (var c in list)
        {
            var right = ctx.Set.Pages.Where(p => walkFiles[p.Id].Overlaps(c.Fix)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var t = Stopwatch.StartNew();
            var ranking = index.Rank(c.Text);
            rankTimes.Add(t.Elapsed.TotalMilliseconds);
            var order = ranking.Pages.Select(r => r.Page.Id).ToList();
            var firstRight = order.FindIndex(right.Contains);
            // The picker always sees the top 20 in detail (D87, 2026-10-28: no clear-winner shortcut). Winners are still
            // counted below, as a record of why the shortcut was dropped.
            var shown = PageIndex.Detailed;
            var isHit = firstRight >= 0 && firstRight < shown;
            if (right.Count > 0) reachable++;
            if (isHit) hit++;
            if (firstRight is >= 0 and < PageIndex.Detailed) top20++;
            if (ranking.Winner is not null)
            {
                winners++;
                if (right.Contains(ranking.Winner.Page.Id)) winnerRight++;
                if (isHit) winnerHit++;
            }
            var row = new JsonObject
            {
                ["id"] = c.Id, ["rightPages"] = right.Count, ["firstRight"] = firstRight < 0 ? null : firstRight + 1, ["hit"] = isHit,
                ["winner"] = ranking.Winner is { } w ? new JsonObject
                {
                    ["page"] = w.Page.Id, ["right"] = right.Contains(w.Page.Id), ["phrase"] = w.Phrase!.Quote, ["unit"] = Short(w.Phrase.Unit.Text),
                } : null,
                ["top5"] = new JsonArray([.. ranking.Pages.Take(5).Select(r => (JsonNode)new JsonObject
                {
                    ["page"] = r.Page.Id, ["score"] = Math.Round(r.Score, 2), ["right"] = right.Contains(r.Page.Id),
                    ["phrase"] = r.Phrase?.Quote,
                })]),
                ["firstRightPage"] = firstRight < 0 ? null : new JsonObject
                {
                    ["page"] = order[firstRight], ["score"] = Math.Round(ranking.Pages[firstRight].Score, 2), ["phrase"] = ranking.Pages[firstRight].Phrase?.Quote,
                    ["words"] = string.Join(' ', ranking.Pages[firstRight].Words.Take(15)),
                },
            };
            rows.Add(row);
            log.WriteLine($"{c.Id,-8} right {right.Count,3}  first {(firstRight < 0 ? "-" : (firstRight + 1).ToString(CultureInfo.InvariantCulture)),5}  {(isHit ? "HIT " : "miss")}"
                + (ranking.Winner is { } win ? $"  winner {(right.Contains(win.Page.Id) ? "right" : "WRONG")} \"{win.Phrase!.Quote}\"" : ""));
        }

        double Share(int n, int of) => of == 0 ? 0 : Math.Round((double)n / of, 3);
        var hits = rows.Select(r => (bool)r!["hit"]! ? 1.0 : 0.0).ToArray();
        var reachableHits = rows.Where(r => (int)r!["rightPages"]! > 0).Select(r => (bool)r!["hit"]! ? 1.0 : 0.0).ToArray();
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["clone"] = Path.GetFileName(clone.TrimEnd('/')), ["commit"] = ctx.Graph.Commit,
            ["pages"] = ctx.Set.Pages.Count, ["rightBy"] = walkDepth > 0 ? $"walk depth {walkDepth}" : walkDepth < 0 ? $"the page's depth + {-walkDepth}" : "slice", ["units"] = index.UnitCount, ["buildSeconds"] = Math.Round(built, 1),
            ["rankMs"] = rankTimes.Count == 0 ? null : new JsonObject { ["median"] = Math.Round(rankTimes.Order().ElementAt(rankTimes.Count / 2), 1), ["max"] = Math.Round(rankTimes.Max(), 1) },
            ["settings"] = new JsonObject
            {
                ["depthDecay"] = index.Settings.DepthDecay, ["phraseBoost"] = index.Settings.PhraseBoost, ["winnerRatio"] = index.Settings.WinnerRatio,
                ["summaryWeight"] = index.Settings.SummaryWeight, ["lengthNorm"] = index.Settings.LengthNorm,
                ["alsoPossible"] = PageIndex.AlsoPossible, ["detailed"] = PageIndex.Detailed,
            },
            ["bugs"] = list.Count, ["reachable"] = reachable,
            ["hit"] = new JsonObject { ["n"] = hit, ["share"] = Share(hit, list.Count), ["range"] = R(TicketScore.Range(hits)) },
            ["hitOfReachable"] = new JsonObject { ["n"] = hit, ["share"] = Share(hit, reachable), ["range"] = R(TicketScore.Range(reachableHits)) },
            ["inTop20"] = top20,
            ["winners"] = new JsonObject { ["n"] = winners, ["right"] = winnerRight, ["hit"] = winnerHit },
            ["cases"] = rows,
        };
    }

    /// <summary>The past bugs: a tickets dir (Sonarr's frozen set) or a cases JSON file.</summary>
    internal static List<Case> Load(string clone, string cases) => Directory.Exists(cases) ? Sonarr(clone, cases) : FromFile(cases);

    /// <summary>Per page, the files its walk reaches: at <paramref name="walkDepth"/> (negative: the page's depth plus that
    /// many lines; 0: its slice), stopping where its slice stops.</summary>
    internal static Dictionary<string, HashSet<string>> WalkFiles(TicketContext ctx, int walkDepth)
    {
        // The walk stops where the page's own slice stops (PageList): at shared components, which have pages of their own,
        // and, for screens and components, at endpoints.
        IReadOnlySet<string>? endpoints = ctx.Set.Settings.SplitAtEndpoints ? new HashSet<string> { Stangent.Graph.NodeKind.Endpoint } : null;
        var shared = ctx.Set.Pages.Where(p => p.Kind == PageKind.Component).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        return ctx.Set.Pages.ToDictionary(p => p.Id, p =>
            (walkDepth != 0 ? ctx.Set.Builder.Build(p.Entry, walkDepth > 0 ? walkDepth : OwnDepth(ctx, p) - walkDepth,
                    shared, p.Kind == PageKind.Backend ? null : endpoints)
                : ctx.Set.Slices.GetValueOrDefault(p.Id))?
                .Nodes.Where(n => n.Role == "walk" && n.Node.File is not null).Select(n => n.Node.File!).ToHashSet(StringComparer.Ordinal) ?? [],
            StringComparer.Ordinal);
    }

    static JsonArray R((double, double) r) => [r.Item1, r.Item2];

    /// <summary>How deep the page's own slice goes (Phase 2, D54).</summary>
    internal static int OwnDepth(TicketContext ctx, PageDef p) =>
        p.Kind == PageKind.Backend && ctx.Set.Settings.SplitAtEndpoints ? ctx.Set.Settings.BackendDepth : ctx.Set.Settings.Depth;

    static string Short(string s) => s.Length > 120 ? s[..120] + "…" : s;

    /// <summary>Sonarr's 40 bugs (D78): the ticket as pulled from Jira in the clone, the fix's files from the answer key.</summary>
    static List<Case> Sonarr(string clone, string ticketsDir)
    {
        var selection = Load(Path.Combine(ticketsDir, "selection.json"));
        var key = Load(Path.Combine(ticketsDir, "answer-key.json"))["issues"]!.AsObject();
        var keys = Load(Path.Combine(ticketsDir, "jira-keys.json"));
        var list = new List<Case>();
        foreach (var i in selection["issues"]!.AsArray().OfType<JsonObject>().Where(i => (string)i["kind"]! == "bug"))
        {
            var number = ((int)i["issue"]!).ToString(CultureInfo.InvariantCulture);
            var jira = (string)keys[number]!;
            var pulled = Load(Path.Combine(clone, ".stangent", "tickets", jira, "ticket.json"));
            var fix = key[number]!["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal);
            list.Add(new Case($"{jira}/#{number}", TicketText.From(pulled), fix));
        }
        return list;
    }

    /// <summary><c>[{ "id", "summary", "description", "comments": [..]?, "files": [..] }]</c></summary>
    static List<Case> FromFile(string path) =>
        [.. JsonNode.Parse(File.ReadAllText(path))!.AsArray().OfType<JsonObject>().Select(c => new Case((string)c["id"]!,
            new TicketText([("summary", (string)c["summary"]!), ("description", (string?)c["description"] ?? ""),
                .. (c["comments"]?.AsArray() ?? []).Select((x, i) => ($"comment {i + 1}", (string)x!))], []),
            c["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal)))];

    static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
