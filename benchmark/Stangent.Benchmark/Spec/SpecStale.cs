using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec;
using Stangent.Spec.Pages;

namespace Stangent.Benchmark.Spec;

/// <summary>
/// Phase 2a, step 7: how many reference pages each main-line commit makes stale (D55). For the last N commits, oldest
/// first, it builds the graph at each one and computes every page's input hash, exactly as `stangent spec stale` does,
/// then counts the pages whose hash changed since the commit before (and pages that appear or disappear). No AI.
/// </summary>
public static class SpecStale
{
    public static async Task<JsonObject> RunAsync(string root, StangentRepo repo, int count, Action applySdkOverride, TextWriter log)
    {
        var startFile = Path.Combine(root, ".stangent", "spec-stale-start");
        var start = File.Exists(startFile) ? File.ReadAllText(startFile).Trim() : Git.Head(root);
        File.WriteAllText(startFile, start);
        var hashes = (Git.Run(root, $"log --first-parent --format=%H -n {count + 1} {start}") ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse().ToList();
        var settings = SpecSettings.From(repo.Config);
        var rows = new JsonArray();
        Dictionary<string, string>? before = null;
        string? previous = null;
        try
        {
            foreach (var hash in hashes)
            {
                Git.Run(root, $"checkout -q -f {hash}");
                applySdkOverride();
                // Restore only when package inputs changed; otherwise the packages already on disk serve.
                var packages = previous is not null && (Git.Run(root, $"diff --name-only {previous} {hash}") ?? "").Split('\n')
                    .Any(f => f.EndsWith(".csproj") || f.EndsWith(".props") || f.EndsWith("package.json") || f.EndsWith("yarn.lock") || f.EndsWith("global.json"));
                var watch = Stopwatch.StartNew();
                var (graph, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: previous is null || packages));
                var set = PageList.Build(graph, root, settings);
                var now = set.Pages.ToDictionary(p => p.Id, p => Bundles.Build(set, p, null).InputHash, StringComparer.Ordinal);
                if (before is not null)
                {
                    var stale = now.Count(p => before.TryGetValue(p.Key, out var h) && h != p.Value);
                    var added = now.Keys.Count(k => !before.ContainsKey(k));
                    var removed = before.Keys.Count(k => !now.ContainsKey(k));
                    var subject = (Git.Run(root, $"log -1 --format=%s {hash}") ?? "").Trim();
                    var files = (Git.Run(root, $"diff --name-only {previous} {hash}") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                    rows.Add(new JsonObject
                    {
                        ["commit"] = hash, ["subject"] = subject, ["filesChanged"] = files, ["pages"] = now.Count,
                        ["stale"] = stale, ["new"] = added, ["removed"] = removed, ["flagged"] = stale + added + removed,
                        ["extracted"] = stats.Extracted, ["ms"] = watch.ElapsedMilliseconds,
                    });
                    log.WriteLine($"spec-stale: {hash[..8]} {stale} stale, {added} new, {removed} removed of {now.Count} ({files} files changed; {watch.ElapsedMilliseconds} ms)");
                }
                before = now;
                previous = hash;
            }
        }
        finally
        {
            Git.Run(root, $"checkout -q -f {start}");
            applySdkOverride();
            await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: true));
            File.Delete(startFile);
        }

        var flagged = rows.Select(r => (int)r!["flagged"]!).Order().ToList();
        var staleOnly = rows.Select(r => (int)r!["stale"]!).Order().ToList();
        return new JsonObject
        {
            ["repo"] = Path.GetFileName(root),
            ["note"] = "Phase 2a step 7 (D55): pages each main-line commit flags as stale (input hash changed) or adds/removes, graph rebuilt at every commit. No AI.",
            ["settings"] = settings.Describe() + $", cap {settings.MaxBundleChars}",
            ["commits"] = rows.Count,
            ["flagged"] = Summary(flagged), ["staleOnly"] = Summary(staleOnly),
            ["commitsFlaggingNothing"] = flagged.Count(f => f == 0),
            ["perCommit"] = rows,
        };
    }

    static JsonObject Summary(List<int> sorted) => sorted.Count == 0 ? new JsonObject() : new JsonObject
    {
        ["median"] = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0,
        ["p90"] = sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(sorted.Count * 0.9))],
        ["worst"] = sorted[^1], ["total"] = sorted.Sum(),
    };
}
