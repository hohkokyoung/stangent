using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// Phase 3's frozen set (D77, D78), chosen by code from git history and GitHub, once, then committed:
/// <list type="bullet">
/// <item><b>Bugs:</b> newest first on the main line before the pinned commit, issues labelled <c>bug</c> that exactly one
/// main-line commit closes ("Closes #N", "Fixes #N", "Resolves #N"), that commit changing at most 15 files. Labelled
/// backend, frontend or both by the files it changed.</item>
/// <item><b>Not bugs:</b> newest first, issues filed with the bug template, closed as "not planned" before the pinned
/// commit, that no main-line commit references, not labelled <c>enhancement</c>, and not closed by a stale bot.</item>
/// </list>
/// Each issue's text and its comments from before the fix (bugs) or the closing (not bugs), and before the first
/// maintainer comment, are frozen with their attachments, so nothing that holds the answer reaches a bundle. The answer key (fix commit and files)
/// goes in its own file, never read by the run.
/// </summary>
public static partial class TicketSelection
{
    public const int MaxFixFiles = 15;

    [GeneratedRegex(@"(?i)\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s*:?\s*(?:Sonarr/Sonarr)?#(\d+)\b")]
    private static partial Regex Reference();

    [GeneratedRegex(@"https://(?:github\.com/user-attachments/(?:assets|files)/[^\s)""'<>\]]+|user-images\.githubusercontent\.com/[^\s)""'<>\]]+|github\.com/Sonarr/Sonarr/assets/[^\s)""'<>\]]+)")]
    private static partial Regex AttachmentUrl();

    public static int Run(string clone, string outDir, string repo, int bugs, int notBugs, TextWriter log)
    {
        var pinned = Git(clone, "rev-parse HEAD").Trim();
        var pinnedDate = DateTimeOffset.Parse(Git(clone, "log -1 --format=%cI HEAD").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var commits = Git(clone, "log --first-parent --format=%H%x1f%cI%x1f%B%x1e HEAD").Split('\u001e', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim().Split('\u001f')).Where(p => p.Length == 3)
            .Select(p => (Hash: p[0], Date: DateTimeOffset.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), Message: p[2])).ToList();
        var refs = new Dictionary<int, List<int>>();
        for (var i = 0; i < commits.Count; i++)
            foreach (Match m in Reference().Matches(commits[i].Message))
            {
                var n = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (!refs.TryGetValue(n, out var list)) refs[n] = list = [];
                if (!list.Contains(i)) list.Add(i);
            }
        var anyRef = Git(clone, "log --format=%B HEAD").Let(all => Regex.Matches(all, @"#(\d+)\b").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet());

        var selected = new JsonArray();
        var key = new JsonObject();
        var reasons = new JsonObject();
        void Skip(int n, string why) { reasons[n.ToString(System.Globalization.CultureInfo.InvariantCulture)] = why; }

        // Bugs, newest fix first.
        var picked = 0;
        foreach (var (n, idx) in refs.Where(r => r.Value.Count == 1).Select(r => (r.Key, r.Value[0])).OrderBy(x => x.Item2).ThenBy(x => x.Key))
        {
            if (picked >= bugs) break;
            var c = commits[idx];
            var parent = Git(clone, $"rev-parse {c.Hash}^1").Trim();
            var files = Git(clone, $"diff --name-only {parent} {c.Hash}").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (files.Count == 0 || files.Count > MaxFixFiles) { Skip(n, $"fix changes {files.Count} files"); continue; }
            var issue = Gh($"repos/{repo}/issues/{n}") as JsonObject;
            if (issue is null || issue["pull_request"] is not null) { Skip(n, "not an issue"); continue; }
            if ((string?)issue["state"] != "closed") { Skip(n, "not closed"); continue; }
            if (!Labels(issue).Contains("bug")) { Skip(n, "not labelled bug"); continue; }
            var area = files.Any(f => f.StartsWith("frontend/", StringComparison.Ordinal)) ? files.Any(f => f.StartsWith("src/", StringComparison.Ordinal)) ? "both" : "frontend" : "backend";
            var cutBug = Freeze(outDir, repo, issue, c.Date, log);
            selected.Add(new JsonObject
            {
                ["issue"] = n, ["kind"] = "bug", ["area"] = area, ["runCommit"] = parent, ["title"] = (string?)issue["title"],
                ["createdAt"] = (string?)issue["created_at"], ["frozenBefore"] = cutBug.ToUniversalTime().ToString("O"),
            });
            key[n.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["fixCommit"] = c.Hash, ["parent"] = parent, ["files"] = new JsonArray([.. files.Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
            };
            picked++;
            log.WriteLine($"bug #{n} ({area}, {files.Count} files)");
        }

        // Not bugs, newest closed first.
        var taken = 0;
        for (var page = 1; taken < notBugs && page <= 10; page++)
        {
            var q = Uri.EscapeDataString($"repo:{repo} is:issue is:closed reason:\"not planned\" closed:<{pinnedDate:yyyy-MM-dd}");
            var found = Gh($"search/issues?q={q}&sort=updated&order=desc&per_page=50&page={page}")?["items"]?.AsArray() ?? [];
            if (found.Count == 0) break;
            foreach (var issue in found.OfType<JsonObject>().OrderByDescending(i => (string?)i["closed_at"], StringComparer.Ordinal))
            {
                if (taken >= notBugs) break;
                var n = (int)issue["number"]!;
                if (selected.Any(s => (int)s!["issue"]! == n)) continue;
                var body = (string?)issue["body"] ?? "";
                if (!body.Contains("### Current Behavior", StringComparison.Ordinal) || !body.Contains("### Expected Behavior", StringComparison.Ordinal)) { Skip(n, "not the bug template"); continue; }
                if (anyRef.Contains(n)) { Skip(n, "a commit references it"); continue; }
                if (Labels(issue).Contains("enhancement")) { Skip(n, "enhancement"); continue; }
                var closed = DateTimeOffset.Parse((string)issue["closed_at"]!, System.Globalization.CultureInfo.InvariantCulture);
                var comments = Comments(repo, n);
                if (comments.LastOrDefault() is { } last && ((string?)last["user"]?["type"] == "Bot" || ((string?)last["body"] ?? "").Contains("stale", StringComparison.OrdinalIgnoreCase)))
                { Skip(n, "closed by a bot or as stale"); continue; }
                if (!comments.Any(c => (string?)c["author_association"] is "MEMBER" or "OWNER" or "COLLABORATOR")) { Skip(n, "no maintainer said why"); continue; }
                var runCommit = commits.First(c => c.Date <= closed).Hash;
                var cut = Freeze(outDir, repo, issue, closed, log);
                selected.Add(new JsonObject
                {
                    ["issue"] = n, ["kind"] = "not-bug", ["area"] = null, ["runCommit"] = runCommit, ["title"] = (string?)issue["title"],
                    ["createdAt"] = (string?)issue["created_at"], ["frozenBefore"] = cut.ToUniversalTime().ToString("O"),
                });
                taken++;
                log.WriteLine($"not a bug #{n}");
            }
        }

        var selection = new JsonObject
        {
            ["schemaVersion"] = 1, ["repo"] = repo, ["pinned"] = pinned, ["rule"] = "D78; benchmark/Stangent.Benchmark/Tickets/TicketSelection.cs",
            ["maxFixFiles"] = MaxFixFiles, ["issues"] = selected, ["skipped"] = reasons,
        };
        Write(Path.Combine(outDir, "selection.json"), selection);
        Write(Path.Combine(outDir, "answer-key.json"), new JsonObject { ["note"] = "Never read by the run: the scoring only (D77).", ["issues"] = key });
        log.WriteLine($"{picked} bugs, {taken} not bugs → {outDir}");
        return picked == bugs && taken == notBugs ? 0 : 1;
    }

    /// <summary>The issue's text and its comments from before <paramref name="before"/>, bots left out, with every
    /// attachment they link downloaded beside them.</summary>
    static DateTimeOffset Freeze(string outDir, string repo, JsonObject issue, DateTimeOffset before, TextWriter log)
    {
        var n = (int)issue["number"]!;
        var dir = Path.Combine(outDir, "issues", n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        // A maintainer's comment can hold the answer ("that's by design", "the bug is in X"): freeze only what came before
        // the first one, as well as before the fix or the closing (decided before the first run).
        var all = Comments(repo, n);
        var firstMaintainer = all.Where(c => (string?)c["author_association"] is "MEMBER" or "OWNER" or "COLLABORATOR")
            .Select(c => DateTimeOffset.Parse((string)c["created_at"]!, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
        if (firstMaintainer < before) before = firstMaintainer;
        var comments = all
            .Where(c => DateTimeOffset.Parse((string)c["created_at"]!, System.Globalization.CultureInfo.InvariantCulture) < before && (string?)c["user"]?["type"] != "Bot")
            .Select(c => (JsonNode)new JsonObject
            {
                ["id"] = (long)c["id"]!, ["author"] = (string?)c["user"]?["login"], ["createdAt"] = (string?)c["created_at"], ["body"] = (string?)c["body"] ?? "",
            }).ToList();
        var frozen = new JsonObject
        {
            ["number"] = n, ["title"] = (string?)issue["title"], ["body"] = (string?)issue["body"] ?? "", ["author"] = (string?)issue["user"]?["login"],
            ["createdAt"] = (string?)issue["created_at"], ["comments"] = new JsonArray([.. comments]),
        };
        var urls = AttachmentUrl().Matches((string)frozen["body"]! + "\n" + string.Join("\n", comments.Select(c => (string)c!["body"]!)))
            .Select(m => m.Value.TrimEnd('.', ',')).Distinct().ToList();
        var files = new JsonArray();
        var i = 0;
        foreach (var url in urls)
        {
            var name = $"{++i:D2}-{Regex.Replace(Path.GetFileName(new Uri(url).AbsolutePath), "[^A-Za-z0-9._-]", "_")}";
            var path = Path.Combine(dir, "attachments", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var ok = Process("curl", ["-sSL", "--fail", "--max-time", "60", "-o", path, url]).Code == 0;
            if (!ok) { log.WriteLine($"  #{n}: couldn't download {url}"); if (File.Exists(path)) File.Delete(path); continue; }
            files.Add(new JsonObject { ["url"] = url, ["file"] = $"attachments/{name}", ["bytes"] = new FileInfo(path).Length });
        }
        frozen["attachments"] = files;
        Write(Path.Combine(dir, "issue.json"), frozen);
        return before;
    }

    static HashSet<string> Labels(JsonNode issue) => (issue["labels"]?.AsArray() ?? []).Select(l => (string?)l?["name"]).OfType<string>().ToHashSet(StringComparer.Ordinal);

    static List<JsonObject> Comments(string repo, int n)
    {
        var all = new List<JsonObject>();
        for (var page = 1; page <= 10; page++)
        {
            var list = Gh($"repos/{repo}/issues/{n}/comments?per_page=100&page={page}")?.AsArray() ?? [];
            all.AddRange(list.OfType<JsonObject>());
            if (list.Count < 100) break;
        }
        return all;
    }

    static JsonNode? Gh(string path)
    {
        var (code, output) = Process("gh", ["api", path]);
        return code == 0 ? JsonNode.Parse(output) : null;
    }

    static string Git(string dir, string args)
    {
        var (code, output) = Process("git", ["-C", dir, .. SplitArgs(args)]);
        return code == 0 ? output : throw new InvalidOperationException($"git {args} failed");
    }

    static IEnumerable<string> SplitArgs(string args) => args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    static (int Code, string Output) Process(string file, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output.Result);
    }

    static void Write(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }

    static T Let<TIn, T>(this TIn value, Func<TIn, T> f) => f(value);
}
