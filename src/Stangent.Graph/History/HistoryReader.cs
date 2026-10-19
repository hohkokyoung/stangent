using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Graph.History;

/// <summary>One commit on main's own line, its files already mapped to the paths they have today.</summary>
public sealed record HistoryCommit(string Hash, string Author, string Email, string Message, IReadOnlyList<string> Files);

/// <summary>Reads main's own line (D36), newest first, following renames and moves (D38).</summary>
public static class HistoryLog
{
    const char CommitStart = '\u001e', Field = '\u001f';

    /// <summary>
    /// Null when git fails or this isn't a git repo. Only paths with the map's file types are listed: rename
    /// detection then compares source files only, not every image and script (7 s → 0.8 s on nopCommerce).
    /// </summary>
    public static List<HistoryCommit>? Read(string root, bool followRenames, IEnumerable<string> extensions)
    {
        var pathspec = string.Join(" ", extensions.Order(StringComparer.Ordinal).Select(e => $"\"*{e}\""));
        var output = Git.Run(root,
            $"-c core.quotePath=false log --first-parent --diff-merges=first-parent {(followRenames ? "-M" : "--no-renames")} " +
            $"--name-status --format=%x1e%H%x1f%an%x1f%ae%x1f%B%x1f HEAD -- {pathspec}");
        return output is null ? null : Parse(output);
    }

    /// <summary>
    /// Parses <c>git log --name-status</c> output (newest first). A rename or move maps the old path to
    /// today's path for every older commit; a deleted path is dead for older commits, so a file created
    /// later with the same name doesn't inherit the old one's history.
    /// </summary>
    public static List<HistoryCommit> Parse(string output)
    {
        var alias = new Dictionary<string, string>(StringComparer.Ordinal);
        string Resolve(string path) => alias.TryGetValue(path, out var now) ? now : path;
        var commits = new List<HistoryCommit>();
        foreach (var record in output.Split(CommitStart, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = record.Split(Field);
            if (parts.Length < 5) continue;
            var files = new List<string>();
            var after = new List<(string Old, string Now)>();
            foreach (var line in parts[4].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = line.Split('\t');
                if (cols.Length < 2) continue;
                switch (cols[0][0])
                {
                    case 'R' when cols.Length >= 3:
                        var now = Resolve(cols[2]);
                        files.Add(now);
                        after.Add((cols[1], now));
                        break;
                    case 'D':
                        files.Add(Resolve(cols[1]));
                        after.Add((cols[1], "\0deleted:" + cols[1]));
                        break;
                    default:
                        files.Add(Resolve(cols[^1]));
                        break;
                }
            }
            // Only older commits see the new names: this commit's own paths are already resolved.
            foreach (var (old, now) in after) alias[old] = now;
            commits.Add(new HistoryCommit(parts[0].Trim(), parts[1], parts[2], parts[3], [.. files.Distinct(StringComparer.Ordinal)]));
        }
        return commits;
    }
}

/// <summary>Counts how often files change together, after the noise filters (D35, D37).</summary>
public static class HistoryCounter
{
    static readonly Regex Reverts = new(@"This reverts commit ([0-9a-f]{7,40})", RegexOptions.CultureInvariant);

    public sealed record Result(List<HistoryLine> Lines, int CommitsRead, int CommitsUsed, SortedDictionary<string, int> Dropped);

    /// <param name="commits">Newest first, paths as they are today.</param>
    /// <param name="ignoreRevs">Hashes (or prefixes) from <c>.git-blame-ignore-revs</c>.</param>
    public static Result Count(IReadOnlyList<HistoryCommit> commits, IReadOnlySet<string> mapFiles,
        StangentConfig.HistoryConfig settings, IReadOnlyCollection<string> ignoreRevs)
    {
        var reverted = commits.SelectMany(c => Reverts.Matches(c.Message).Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        var bots = settings.BotAuthors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["reverts"] = 0, ["formatting"] = 0, ["bots"] = 0, ["huge"] = 0 };
        var used = new List<string[]>();
        var read = 0;
        foreach (var c in commits)
        {
            var files = c.Files.Where(mapFiles.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (files.Length == 0) continue;
            read++;
            // A revert and the commit it reverts both go: the change didn't stick, and keeping both counts it twice.
            if (Reverts.IsMatch(c.Message) || reverted.Any(r => c.Hash.StartsWith(r, StringComparison.Ordinal))) dropped["reverts"]++;
            else if (ignoreRevs.Any(r => c.Hash.StartsWith(r, StringComparison.Ordinal))) dropped["formatting"]++;
            else if (IsBot(c, bots)) dropped["bots"]++;
            else if (files.Length > settings.MaxFiles) dropped["huge"]++;
            else used.Add(files);
        }

        // Each file's window: its last N surviving commits. Anything older counts zero (D35).
        var windows = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < used.Count; i++)
            foreach (var f in used[i])
            {
                if (!windows.TryGetValue(f, out var w)) windows[f] = w = [];
                if (w.Count < settings.Window) w.Add(i);
            }

        var lines = new List<HistoryLine>();
        foreach (var (target, window) in windows)
        {
            var shared = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in window)
                foreach (var f in used[i])
                    if (f != target) shared[f] = shared.GetValueOrDefault(f) + 1;
            foreach (var (source, n) in shared)
                if (n >= settings.MinShared && n >= settings.MinShare * window.Count)
                    lines.Add(new HistoryLine(source, target, n, window.Count));
        }
        lines.Sort((a, b) => string.CompareOrdinal(a.Target, b.Target) is var t and not 0 ? t : string.CompareOrdinal(a.Source, b.Source));
        return new Result(lines, read, used.Count, dropped);
    }

    static bool IsBot(HistoryCommit c, HashSet<string> bots) =>
        c.Author.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) || c.Email.Contains("[bot]", StringComparison.OrdinalIgnoreCase)
        || bots.Contains(c.Author) || bots.Contains(c.Email);
}

/// <summary>The 📜 step of <c>graph build</c>: runs after linking, needs neither Roslyn nor Node.</summary>
public static class HistoryStep
{
    /// <summary>Bumped whenever the counting changes, so stored lines are recomputed.</summary>
    const int AlgorithmVersion = 2;

    public static void Apply(StangentRepo repo, CodeGraph graph, CodeGraph? previous, Func<CodeGraph?> loadStored, List<string> warnings)
    {
        var settings = repo.Config.History;
        var empty = new Dictionary<string, int>();
        if (!settings.Enabled)
        {
            graph.History = new HistoryInfo("off", "", "", 0, 0, empty, false, "", "history.enabled is false");
            return;
        }
        if (graph.Commit.Length == 0)
        {
            graph.History = new HistoryInfo("none", "", "", 0, 0, empty, false, "", "not a git repository");
            warnings.Add("history_unavailable: not a git repository, so there's no 📜 evidence");
            return;
        }

        var mapFiles = graph.AllNodes.Where(n => n.Kind is NodeKind.File or NodeKind.Template && n.File is not null)
            .Select(n => n.File!).ToHashSet(StringComparer.Ordinal);
        var ignorePath = Path.Combine(repo.Root, ".git-blame-ignore-revs");
        var ignoreRevs = File.Exists(ignorePath)
            ? File.ReadAllLines(ignorePath).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length >= 7).Select(l => l.Split(' ')[0]).ToList()
            : [];
        // Rename detection needs old file contents. A blobless clone fetches them over the network on every
        // build (34 s on nopCommerce); a clone with --filter=blob:limit=200k has them (D38, D42).
        var followRenames = settings.FollowRenames;
        if (followRenames && Git.Run(repo.Root, "config --get remote.origin.partialclonefilter")?.Trim() is { } filter
            && (filter == "blob:none" || filter.StartsWith("tree:", StringComparison.Ordinal)))
        {
            followRenames = false;
            warnings.Add($"history_renames_skipped: this clone has no file contents ({filter}), so moved files lose their older 📜 history; clone with --filter=blob:limit=200k instead");
        }
        // Recomputed in full only when something it depends on changed (D41).
        var fingerprint = CanonicalJson.Hash(string.Join("\n",
            AlgorithmVersion, graph.Commit, settings.Enabled, settings.Window, settings.MinShared, settings.MinShare.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settings.MaxFiles, followRenames, string.Join(",", settings.BotAuthors.Order(StringComparer.Ordinal)),
            string.Join(",", ignoreRevs), string.Join("\n", mapFiles.Order(StringComparer.Ordinal))));
        if (previous?.History is { Status: "full" or "shallow" } same && same.Fingerprint == fingerprint)
        {
            graph.History = same;
            graph.HistoryLines.AddRange(previous.HistoryLines);
            return;
        }

        var shallow = Git.Run(repo.Root, "rev-parse --is-shallow-repository")?.Trim() == "true";
        if (shallow && (previous ?? loadStored()) is { History: { Status: "full" } stored } storedGraph)
        {
            // A shallow clone can't recompute history: keep the lines it downloaded, a few commits stale (D42).
            graph.History = stored;
            graph.HistoryLines.AddRange(storedGraph.HistoryLines.Where(l => mapFiles.Contains(l.Source) && mapFiles.Contains(l.Target)));
            warnings.Add($"history_shallow: this clone is shallow, so the 📜 lines computed at {stored.Commit[..Math.Min(10, stored.Commit.Length)]} were kept");
            return;
        }

        var extensions = mapFiles.Select(Path.GetExtension).Where(e => !string.IsNullOrEmpty(e)).Distinct(StringComparer.Ordinal).Select(e => e!);
        var commits = HistoryLog.Read(repo.Root, followRenames, extensions);
        if (commits is null)
        {
            graph.History = new HistoryInfo("none", "", "", 0, 0, empty, false, "", "git log failed");
            warnings.Add("history_unavailable: git log failed, so there's no 📜 evidence");
            return;
        }
        var result = HistoryCounter.Count(commits, mapFiles, settings, ignoreRevs);
        graph.HistoryLines.AddRange(result.Lines);
        graph.History = new HistoryInfo(shallow ? "shallow" : "full", graph.Commit, commits.Count > 0 ? commits[^1].Hash : "",
            result.CommitsRead, result.CommitsUsed, result.Dropped, followRenames, fingerprint,
            shallow ? "shallow clone: only the history it has" : null);
        if (shallow) warnings.Add("history_shallow: this clone is shallow, so 📜 uses only the history it has (fetch with --unshallow for all of it)");
    }
}
