using System.Globalization;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Tickets;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// Where "right place" loses each bug's fix, stage by stage, from what the run left (no AI). Part of <c>--score-tickets</c>
/// (reported, no bar), so a miss is pinned on the step that caused it before anyone changes that step.
/// <para>The first stage a bug fails, in the order the product works:</para>
/// <list type="number">
/// <item><c>no page covers the fix</c>: no page's slice holds a file the fix changed (a gap in Phase 2's pages).</item>
/// <item><c>right page not in the top 20</c>: some page covers it, but the page index ranks none of them in the 20 the
/// point picker reads in detail (D87).</item>
/// <item><c>code's points skipped the picker</c>: a right page is in the top 20, none was picked, and the AI picker never
/// ran because code found starting points of its own (D70: URLs, titles, symbols).</item>
/// <item><c>picker missed it</c>: a right page is in the top 20, the picker ran, and it picked none.</item>
/// <item><c>right page picked, not written</c>: a right page was picked but has no written page, so no statements.</item>
/// <item><c>right page picked, no cited fix file</c>: a right page was picked, but its statements cite no file the fix
/// changed (D79's "starting points' files" are the cited ones).</item>
/// <item><c>hit</c>: D79's right place.</item>
/// </list>
/// Beside the stage: whether the bundle's impact walk from those starting points (2 lines back, uncapped, and 📜) reaches a
/// fix file, and whether the bundle shows one (its pointers).
/// </summary>
public static class PlaceStages
{
    public static readonly string[] Stages =
        ["no page covers the fix", "right page not in the top 20", "code's points skipped the picker", "picker missed it",
         "right page picked, not written", "right page picked, no cited fix file", "hit"];

    public static JsonObject Run(string clone, string ticketsDir)
    {
        var repo = StangentRepo.Discover(clone);
        var ctx = TicketContext.Load(repo);
        var index = PageIndex.Build(ctx, repo.Root, repo.Config);
        var slices = IndexScore.WalkFiles(ctx, 0);
        var nodes = ctx.Graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var selection = Load(Path.Combine(ticketsDir, "selection.json"));
        var answers = Load(Path.Combine(ticketsDir, "answer-key.json"))["issues"]!.AsObject();
        var keys = Load(Path.Combine(ticketsDir, "jira-keys.json"));

        var rows = new JsonArray();
        var counts = Stages.ToDictionary(s => s, _ => 0, StringComparer.Ordinal);
        int reached = 0, shown = 0, bugs = 0;
        foreach (var bug in selection["issues"]!.AsArray().OfType<JsonObject>().Where(i => (string)i["kind"]! == "bug"))
        {
            bugs++;
            var number = ((int)bug["issue"]!).ToString(CultureInfo.InvariantCulture);
            var jira = (string)keys[number]!;
            var dir = Path.Combine(clone, ".stangent", "tickets", jira);
            var fix = answers[number]!["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal);
            var points = Load(Path.Combine(dir, "points.json"));
            var right = ctx.Set.Pages.Where(p => slices[p.Id].Overlaps(fix)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            // The pages the picker reads in detail are the top 20 that score above zero (Points: PageIndex.Detailed).
            var order = index.Rank(TicketText.From(Load(Path.Combine(dir, "ticket.json")))).Pages.Where(r => r.Score > 0).Select(r => r.Page.Id).ToList();
            var firstRight = order.FindIndex(right.Contains);
            var list = points["points"]!.AsArray().OfType<JsonObject>().ToList();
            var picked = list.Where(p => (string)p["kind"]! == "page").Select(p => (string)p["id"]!).ToList();
            var hit = TicketScore.StartingFiles(points, ctx.Index, nodes).Overlaps(fix);
            var pickerRan = (string?)points["ai"]?["status"] is not (null or "not-needed");
            var pickedRight = picked.Where(right.Contains).ToList();
            var stage = hit ? "hit"
                : right.Count == 0 ? Stages[0]
                : pickedRight.Count == 0 && firstRight is < 0 or >= PageIndex.Detailed ? Stages[1]
                : pickedRight.Count == 0 ? (pickerRan ? Stages[3] : Stages[2])
                : !pickedRight.Any(ctx.Index.Pages.ContainsKey) ? Stages[4]
                : Stages[5];
            counts[stage]++;

            // The impact walk as the bundle starts it (TicketBundle): what the written picked pages cite, and the symbols.
            var starts = picked.Where(ctx.Index.Pages.ContainsKey)
                .SelectMany(id => PageChecks.Statements(ctx.Index.Pages[id]).SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!)))
                .Concat(list.Where(p => (string)p["kind"]! == "symbol").Select(p => (string)p["id"]!))
                .Distinct(StringComparer.Ordinal).Select(id => nodes.GetValueOrDefault(id)).OfType<Node>().ToList();
            var (walk, history) = starts.Count == 0 ? ([], []) : ctx.Analyzer.Reach(starts, TicketBundle.ImpactDepth);
            var reachedFiles = walk.Select(n => n.File).OfType<string>().Concat(history).Concat(starts.Select(n => n.File).OfType<string>())
                .ToHashSet(StringComparer.Ordinal);
            var bundle = File.Exists(Path.Combine(dir, "bundle.json")) ? Load(Path.Combine(dir, "bundle.json")) : null;
            var shownFiles = bundle?["pointers"]?.AsArray().Select(p => (string?)p!["file"]).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [];
            var isReached = reachedFiles.Overlaps(fix);
            var isShown = shownFiles.Overlaps(fix);
            if (isReached) reached++;
            if (isShown) shown++;
            rows.Add(new JsonObject
            {
                ["key"] = jira, ["issue"] = (int)bug["issue"]!, ["stage"] = stage, ["rightPages"] = right.Count,
                ["firstRightRank"] = firstRight < 0 ? null : firstRight + 1, ["picked"] = picked.Count, ["pickerRan"] = pickerRan,
                ["reached"] = isReached, ["shown"] = isShown,
                // Precision: how many files each layer offers, and how many of the fix's files it holds.
                ["startingFiles"] = TicketScore.StartingFiles(points, ctx.Index, nodes).Count, ["reachedFiles"] = reachedFiles.Count,
                ["shownFiles"] = shownFiles.Count, ["fixFiles"] = fix.Count,
            });
        }
        return new JsonObject
        {
            ["bugs"] = bugs,
            ["firstStageLost"] = new JsonObject([.. Stages.Select(s => KeyValuePair.Create(s, (JsonNode?)counts[s]))]),
            ["walkReachesAFixFile"] = reached,
            ["bundleShowsAFixFile"] = shown,
            ["medianFiles"] = new JsonObject
            {
                ["starting"] = Median(rows.Select(r => (int)r!["startingFiles"]!)), ["reached"] = Median(rows.Select(r => (int)r!["reachedFiles"]!)),
                ["shown"] = Median(rows.Select(r => (int)r!["shownFiles"]!)), ["fix"] = Median(rows.Select(r => (int)r!["fixFiles"]!)),
            },
            ["note"] = "stages in product order; reached = the bundle's impact walk from its starting points (uncapped, 📜 included); shown = the bundle's pointers",
            ["cases"] = rows,
        };
    }

    static double Median(IEnumerable<int> v)
    {
        var a = v.Order().ToArray();
        return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
