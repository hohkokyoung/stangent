using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Tickets.Proof;

/// <summary>
/// The app while the reproducer works (D86): the repo's existing Playwright specs that open the ticket's screens, a
/// fixed script the reproducer runs its own spec with, and <c>stangent ticket serve</c>, which keeps the app up for the
/// attempt and runs the related specs once before the AI starts.
/// </summary>
public static partial class AppSession
{
    static readonly string[] Skip = ["node_modules", "test-results", "playwright-report", ".git", ".claude", ".stangent", "bin", "obj"];

    [GeneratedRegex(@"\.goto\(\s*['""`]([^'""`$]+)['""`]")]
    private static partial Regex GotoPath();

    /// <summary>
    /// Specs outside the reproducer's own folder whose <c>goto</c> path is one of the bundle's screens (a screen page's
    /// id is <c>page:&lt;route&gt;</c>; <c>:param</c> segments match any segment). Repo-relative, sorted.
    /// </summary>
    public static List<string> RelatedSpecs(StangentRepo repo, JsonObject bundle)
    {
        if (repo.Config.Tickets.App is not { } app) return [];
        var routes = (bundle["pages"]?.AsArray() ?? []).OfType<JsonObject>()
            .Where(p => (string?)p["kind"] == "screen" && ((string?)p["id"])?.StartsWith("page:", StringComparison.Ordinal) == true)
            .Select(p => ((string)p["id"]!)["page:".Length..]).Distinct(StringComparer.Ordinal).ToList();
        if (routes.Count == 0) return [];
        var dir = Path.GetFullPath(Path.Combine(repo.Root, app.PlaywrightDir ?? "."));
        if (!Directory.Exists(dir)) return [];
        var own = Path.GetFullPath(Path.Combine(repo.Root, app.SpecDir)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var found = new List<string>();
        foreach (var file in Specs(dir))
        {
            if (file.StartsWith(own, StringComparison.Ordinal)) continue;
            var paths = GotoPath().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value);
            if (paths.Any(p => routes.Any(r => RouteMatches(r, p))))
                found.Add(Path.GetRelativePath(repo.Root, file).Replace('\\', '/'));
        }
        return [.. found.Order(StringComparer.Ordinal)];
    }

    static IEnumerable<string> Specs(string dir)
    {
        var stack = new Stack<string>([dir]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            foreach (var f in Directory.EnumerateFiles(d))
                if (Regex.IsMatch(Path.GetFileName(f), @"\.spec\.(ts|js)$")) yield return Path.GetFullPath(f);
            // Not into links (another checkout, a loop) or tooling folders.
            foreach (var sub in Directory.EnumerateDirectories(d)
                         .Where(s => !Skip.Contains(Path.GetFileName(s)) && !new DirectoryInfo(s).Attributes.HasFlag(FileAttributes.ReparsePoint)))
                stack.Push(sub);
        }
    }

    /// <summary>A spec's <c>goto</c> path against a screen's route: the same segments, a route's <c>:param</c> or <c>*</c> matching any.</summary>
    public static bool RouteMatches(string route, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var abs) && abs.Scheme.StartsWith("http", StringComparison.Ordinal)) path = abs.AbsolutePath;
        path = path.Split('?', '#')[0].TrimEnd('/');
        route = route.TrimEnd('/');
        var a = route.Split('/');
        var b = path.Split('/');
        return a.Length == b.Length && a.Zip(b).All(x => x.First.StartsWith(':') || x.First == "*" || string.Equals(x.First, x.Second, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The script the reproducer runs its own spec with: only a spec in its folder or a related one, against the
    /// running app. Outside the folders it may edit, so it can't change what the script runs.</summary>
    public static string ScriptPath(TicketPaths paths) => Path.Combine(paths.Dir, "tools", "playwright.sh");

    public static string ReadyPath(TicketPaths paths) => Path.Combine(paths.Dir, "tools", "app-ready.json");

    public static string RelatedRunPath(TicketPaths paths) => Path.Combine(AiStep.Work(paths, StepSpec.Prove), "related-run.md");

    public static void WriteScript(StangentRepo repo, TicketPaths paths, IReadOnlyList<string> related)
    {
        var app = repo.Config.Tickets.App!;
        var own = app.SpecDir.TrimEnd('/');
        var sb = new StringBuilder("#!/usr/bin/env bash\n# Written by stangent (D86): runs one Playwright spec against the app stangent started.\n# usage: playwright.sh <spec, relative to the repo>\nset -euo pipefail\n");
        sb.Append("spec=\"${1:?usage: playwright.sh <spec>}\"\n");
        sb.Append("case \"$spec\" in\n");
        sb.Append($"  {Sh(own)}/*.spec.ts|{Sh(own)}/*.spec.js) ;;\n");
        foreach (var r in related) sb.Append($"  {Sh(r)}) ;;\n");
        sb.Append($"  *) echo \"only a spec in {own}/ or a related one\" >&2; exit 2 ;;\nesac\n");
        sb.Append("case \"$spec\" in *..*) echo \"no .. in the path\" >&2; exit 2 ;; esac\n");
        // Playwright reads its file argument as a pattern: only plain path characters, so it can only name this file.
        sb.Append("case \"$spec\" in *[!A-Za-z0-9._/-]*) echo \"only letters, digits and . _ / - in the path\" >&2; exit 2 ;; esac\n");
        sb.Append($"cd {Sh(Path.Combine(repo.Root, app.PlaywrightDir ?? "."))}\n");
        sb.Append($"BASE_URL={Sh(app.Url ?? "")} exec npx playwright test {Sh(PatternEscape(repo.Root.TrimEnd('/')))}/\"$spec\" --retries=0 --reporter=line\n");
        var file = ScriptPath(paths);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, sb.ToString());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    static string Sh(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>A path as a literal in Playwright's file pattern (a JavaScript regular expression).</summary>
    static string PatternEscape(string s) => Regex.Replace(s, @"[.*+?^${}()|\[\]\\]", m => "\\" + m.Value);

    /// <summary>A backstop if the driver dies without stopping it: serve never runs longer than this.</summary>
    public static readonly TimeSpan MaxServe = TimeSpan.FromMinutes(90);

    /// <summary>
    /// Starts the app for the attempt, runs the related specs once (their results go in the reproducer's input folder),
    /// writes the ready file, then keeps the app up until <paramref name="ct"/> is cancelled (the driver stops it before
    /// stangent's own check starts the app again).
    /// </summary>
    public static async Task<JsonObject> ServeAsync(StangentRepo repo, string key, ICommandRunner runner, CancellationToken ct)
    {
        var paths = new TicketPaths(repo.Root, key);
        var state = AiStep.State(paths, StepSpec.Prove);
        var target = state["targets"]?.AsArray().Select(t => TestTarget.FromJson(t!)).FirstOrDefault(t => t.Kind == TestTarget.Playwright)
            ?? throw new StangentException("no_playwright_target", $"{key}'s attempt has no Playwright target: nothing to serve.", ExitCodes.Usage);
        var related = state["related"]?.AsArray().Select(r => (string)r!).ToList() ?? [];
        var ready = ReadyPath(paths);
        if (File.Exists(ready)) File.Delete(ready);

        await using var app = await BugProof.StartApp(repo, runner, ct);
        var report = new StringBuilder("# Related Playwright tests, run against the app before you started\n\n");
        if (related.Count == 0) report.Append("None: no existing spec opens this ticket's screens.\n");
        foreach (var spec in related)
        {
            var file = Path.GetRelativePath(target.WorkingDir(repo.Root), Path.Combine(repo.Root, spec)).Replace('\\', '/');
            var r = await runner.RunAsync($"npx playwright test '{file.Replace("'", "'\\''")}' --retries=0 --reporter=line", target.WorkingDir(repo.Root),
                BugProof.CommandTimeout, new Dictionary<string, string> { ["BASE_URL"] = repo.Config.Tickets.App?.Url ?? "" }, ct);
            var tail = r.Output.Length > 3000 ? r.Output[^3000..] : r.Output;
            report.Append($"## `{spec}`: {(r.TimedOut ? "timed out" : r.ExitCode == 0 ? "passed" : "failed")}\n\n````text\n{tail.Trim()}\n````\n\n");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(RelatedRunPath(paths))!);
        File.WriteAllText(RelatedRunPath(paths), report.ToString());
        File.WriteAllText(ready, new JsonObject { ["url"] = repo.Config.Tickets.App?.Url, ["related"] = related.Count }.ToJsonString());

        try { await Task.Delay(MaxServe, ct); }
        catch (OperationCanceledException) { }
        if (File.Exists(ready)) File.Delete(ready);
        return new JsonObject { ["key"] = key, ["served"] = true, ["related"] = new JsonArray([.. related.Select(r => (JsonNode)r)]) };
    }
}
