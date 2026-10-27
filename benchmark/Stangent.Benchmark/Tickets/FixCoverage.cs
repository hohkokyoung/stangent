using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Tickets;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// How much of each past fix a bundle could reach once its starting page is right, and what kinds of files it misses.
/// No AI. <c>--fix-coverage &lt;clone&gt; &lt;tickets dir | cases.json&gt; [--out file]</c>.
/// <para>The start: the page index's best-ranked right page (D87's rule); a bug with no right page starts from the index's
/// first page and is reported apart. Each file the fix changed is put in the first layer that reaches it:</para>
/// <list type="bullet">
/// <item><c>page</c>: the page's slice (what its written page describes).</item>
/// <item><c>impact</c>: what depends on the slice, within 2 lines back (the bundle's impact answer, D71), uncapped.</item>
/// <item><c>history</c>: 📜 files that usually change with the slice's files, all of them.</item>
/// <item><c>past the page</c>: 2 lines further than the page goes (the right-page rule's margin, not in a bundle today).</item>
/// <item>missed, by kind of file.</item>
/// </list>
/// The whole slice stands in for the written statements' citations, both as a layer and as the impact walk's starting
/// points, so <c>reached</c> is an upper bound. <c>shown</c> (the slice, the impact answer's 5 per group, its top 10 📜) is
/// only an estimate: a bundle started from citations fills those capped lists differently.
/// </summary>
public static partial class FixCoverage
{
    public const int ImpactDepth = 2;

    public static JsonObject Run(string clone, string cases, TextWriter log)
    {
        var repo = StangentRepo.Discover(clone);
        var ctx = TicketContext.Load(repo);
        var index = PageIndex.Build(ctx, repo.Root, repo.Config);
        var list = IndexScore.Load(clone, cases);
        var right = IndexScore.WalkFiles(ctx, -2);
        var graphFiles = ctx.Graph.AllNodes.Where(n => n.File is not null).Select(n => n.File!).ToHashSet(StringComparer.Ordinal);
        log.WriteLine($"{ctx.Set.Pages.Count} pages, {list.Count} bugs, history {ctx.Graph.History?.Status ?? "none"}");

        var rows = new JsonArray();
        var layers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var apartLayers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int shownFiles = 0, fixFiles = 0;
        foreach (var c in list)
        {
            var ranking = index.Rank(c.Text).Pages;
            var start = ranking.FirstOrDefault(r => right[r.Page.Id].Overlaps(c.Fix)) ?? ranking[0];
            var covered = right[start.Page.Id].Overlaps(c.Fix);
            var page = start.Page;
            var slice = ctx.Set.Slices[page.Id];
            // What the page writer is shown (Spec Bundle): the walk and the handlers it triggers, not shared components or tables.
            var sliceFiles = slice.Nodes.Where(n => n.Role is "walk" or "handler" && n.Node.File is not null).Select(n => n.Node.File!).ToHashSet(StringComparer.Ordinal);
            var starts = slice.Nodes.Where(n => n.Role == "walk" && n.Node.File is not null && !n.Node.IsTest
                && n.Node.Kind is not (NodeKind.Project or NodeKind.File)).Select(n => n.Node).ToList();
            var (reached, history) = starts.Count == 0 ? ([], []) : ctx.Analyzer.Reach(starts, ImpactDepth);
            var impactFiles = reached.Where(n => n.File is not null).Select(n => n.File!).ToHashSet(StringComparer.Ordinal);
            var historyFiles = history.ToHashSet(StringComparer.Ordinal);
            var shown = new HashSet<string>(sliceFiles, StringComparer.Ordinal);
            if (starts.Count > 0)
            {
                var answer = ctx.Analyzer.AnalyzeStarts(starts, "starting-points", page.Id, repo.Root, ImpactDepth);
                foreach (var g in answer["groups"]!.AsArray())
                    foreach (var i in g!["items"]!.AsArray())
                        if ((string?)i!["file"] is { } f) shown.Add(f);
                foreach (var h in answer["history"]!["items"]!.AsArray()) shown.Add((string)h!["file"]!);
            }

            var files = new JsonArray();
            foreach (var f in c.Fix.Order(StringComparer.Ordinal))
            {
                var layer = sliceFiles.Contains(f) ? "page" : impactFiles.Contains(f) ? "impact" : historyFiles.Contains(f) ? "history"
                    : right[page.Id].Contains(f) ? "past the page" : "missed";
                var kind = Kind(f, graphFiles);
                if (covered)
                {
                    layers[layer] = layers.GetValueOrDefault(layer) + 1;
                    fixFiles++;
                    if (shown.Contains(f)) shownFiles++;
                    if (layer == "missed") kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                }
                else apartLayers[layer] = apartLayers.GetValueOrDefault(layer) + 1;
                files.Add(new JsonObject { ["file"] = f, ["layer"] = layer, ["kind"] = kind, ["shown"] = shown.Contains(f) });
            }
            rows.Add(new JsonObject
            {
                ["id"] = c.Id, ["start"] = page.Id, ["rank"] = ranking.ToList().FindIndex(r => r.Page.Id == page.Id) + 1, ["rightPage"] = covered,
                ["reached"] = files.Count(f => (string)f!["layer"]! != "missed"), ["of"] = files.Count,
                ["offered"] = new JsonObject
                {
                    ["page"] = sliceFiles.Count, ["impact"] = impactFiles.Except(sliceFiles).Count(),
                    ["history"] = historyFiles.Except(sliceFiles).Except(impactFiles).Count(), ["shown"] = shown.Count,
                },
                ["files"] = files,
            });
            log.WriteLine($"{c.Id,-14} {(covered ? "right" : "apart"),-5} {files.Count(f => (string)f!["layer"]! != "missed")}/{files.Count}  "
                + string.Join(", ", files.Select(f => $"{Path.GetFileName((string)f!["file"]!)}:{f["layer"]}")));
        }

        var scored = rows.Where(r => (bool)r!["rightPage"]!).ToList();
        double Mean(IEnumerable<double> v) { var a = v.ToArray(); return a.Length == 0 ? 0 : Math.Round(a.Average(), 3); }
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["clone"] = Path.GetFileName(clone.TrimEnd('/')), ["commit"] = ctx.Graph.Commit,
            ["history"] = ctx.Graph.History?.Status ?? "none", ["impactDepth"] = ImpactDepth,
            ["bugs"] = list.Count, ["withRightPage"] = scored.Count,
            ["fixFiles"] = fixFiles,
            ["byLayer"] = Obj(layers),
            ["shown"] = shownFiles,
            ["offeredMedian"] = new JsonObject([.. new[] { "page", "impact", "history", "shown" }.Select(k => KeyValuePair.Create(k,
                (JsonNode?)Median(scored.Select(r => (int)r!["offered"]![k]!))))]),
            ["missedByKind"] = Obj(kinds),
            ["meanShareReached"] = Mean(scored.Select(r => (double)(int)r!["reached"]! / (int)r["of"]!)),
            ["meanShareReachedNoTests"] = Mean(scored.Select(r =>
            {
                var nonTest = r!["files"]!.AsArray().Where(f => (string)f!["kind"]! != "test").ToList();
                return nonTest.Count == 0 ? 1.0 : (double)nonTest.Count(f => (string)f!["layer"]! != "missed") / nonTest.Count;
            })),
            ["apart"] = new JsonObject { ["bugs"] = list.Count - scored.Count, ["byLayer"] = Obj(apartLayers) },
            ["cases"] = rows,
        };
    }

    static double Median(IEnumerable<int> v)
    {
        var a = v.Order().ToArray();
        return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    static JsonObject Obj(IDictionary<string, int> d) => new([.. d.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))]);

    [GeneratedRegex(@"(^|/)(tests?|e2e|__tests__)/|[^/]*\.(Test|Tests|IntegrationTest)(\.[^/]*)?/|(Tests?|Fixture)\.cs$|\.(spec|test)\.[jt]sx?$")]
    private static partial Regex TestPath();

    /// <summary>What kind of file a missed fix file is: tests, migrations, SQL, UI text, styles, frontend code, config and
    /// build files, C# the graph has, C# the graph doesn't have.</summary>
    public static string Kind(string f, IReadOnlySet<string> graphFiles)
    {
        var ext = Path.GetExtension(f).ToLowerInvariant();
        if (TestPath().IsMatch(f)) return "test";
        if (Regex.IsMatch(f, @"(^|/)Migrations?/", RegexOptions.IgnoreCase)) return "migration";
        if (ext == ".sql") return "sql";
        if (ext == ".resx" || Regex.IsMatch(f, @"(^|/)(Localization|i18n|locales?)/", RegexOptions.IgnoreCase)) return "ui text";
        if (ext is ".css" or ".scss" or ".less") return "styles";
        if (ext is ".ts" or ".tsx" or ".js" or ".jsx" or ".vue" or ".html") return graphFiles.Contains(f) ? "frontend code" : "frontend (not in graph)";
        if (ext is ".cs" or ".cshtml" or ".razor") return graphFiles.Contains(f) ? "C#" : "C# (not in graph)";
        if (ext is ".json" or ".yml" or ".yaml" or ".xml" or ".config" or ".csproj" or ".props" or ".targets" or ".sh" or ".md" or ".lock" or ""
            || Path.GetFileName(f).StartsWith("Dockerfile", StringComparison.Ordinal)) return "config / build";
        return "other";
    }
}
