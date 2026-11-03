using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Tickets.Proof;

/// <summary>
/// The app while the reproducer works (D86): the repo's existing Playwright specs that open the ticket's screens, a
/// fixed script the reproducer runs its own spec with, and <c>stangent ticket serve</c>, which keeps the app up for the
/// attempt and runs the related specs once before the AI starts.
/// </summary>
public static partial class AppSession
{
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
        foreach (var file in PlaywrightSpecs.Files(dir))
        {
            if (file.StartsWith(own, StringComparison.Ordinal)) continue;
            var paths = PlaywrightSpecs.LiteralGotos(File.ReadAllText(file));
            if (paths.Any(p => routes.Any(r => RouteMatches(r, p))))
                found.Add(Path.GetRelativePath(repo.Root, file).Replace('\\', '/'));
        }
        return [.. found.Order(StringComparer.Ordinal)];
    }

    /// <summary>A spec's <c>goto</c> path against a screen's route (<see cref="PlaywrightSpecs.RouteMatches"/>).</summary>
    public static bool RouteMatches(string route, string path) => PlaywrightSpecs.RouteMatches(route, path);

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
        sb.Append($"BASE_URL={Sh(app.Url ?? "")} exec npx playwright test --config {ConfigFile} --project={Project} {Sh(PatternEscape(repo.Root.TrimEnd('/')))}/\"$spec\" --retries=0 --reporter=line\n");
        var file = ScriptPath(paths);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, sb.ToString());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    static string Sh(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>The Playwright config stangent runs with, beside the repo's own (in the Playwright folder).</summary>
    public const string ConfigFile = ".stangent.playwright.config.ts";
    static readonly string[] RepoConfigs = ["playwright.config.ts", "playwright.config.mts", "playwright.config.js", "playwright.config.mjs", "playwright.config.cjs"];

    /// <summary>The Playwright project stangent adds and runs: the repo's own projects stay as they are.</summary>
    public const string Project = "stangent";

    /// <summary>
    /// Writes <see cref="ConfigFile"/> (D94): the repo's own Playwright config, if it has one, unchanged but for its
    /// <c>webServer</c> (off: stangent starts the app), plus one project, <see cref="Project"/>, that stangent runs with
    /// <c>--project</c>: its test folder reaches stangent's spec folder and the repo's tests, it matches stangent's
    /// <c>*.spec.ts|js</c> and the repo's own patterns, and it uses the first ordinary project's settings and
    /// dependencies (a login setup project still runs first) with the base URL of the app stangent started. Beside the
    /// repo's config, so its relative paths mean what they did; kept out of git through <c>.git/info/exclude</c>.
    /// </summary>
    public static void WriteConfig(StangentRepo repo)
    {
        var app = repo.Config.Tickets.App!;
        var dir = Path.GetFullPath(Path.Combine(repo.Root, app.PlaywrightDir ?? "."));
        var specDir = Path.GetFullPath(Path.Combine(repo.Root, app.SpecDir));
        var own = RepoConfigs.FirstOrDefault(f => File.Exists(Path.Combine(dir, f)));
        string J(string v) => System.Text.Json.JsonSerializer.Serialize(v);
        var sb = new StringBuilder("// @ts-nocheck\n/* eslint-disable */\n");
        sb.Append("// Written by stangent for its reproductions; regenerated on every run (D94). The repo's own Playwright config plus\n");
        sb.Append("// one project, \"stangent\", pointed at the app stangent started; the repo's webServer is off (stangent runs the app).\n");
        sb.Append("import * as path from 'node:path';\n");
        sb.Append(own is null ? "const base: any = {};\n" : $"import * as own from {J("./" + own)};\nconst base: any = (own as any).default ?? own;\n");
        sb.Append($"const here = {J(dir)};\nconst specDir = {J(specDir)};\nconst name = {J(Project)};\n");
        sb.Append("""
            const DEFAULT = '**/*.@(spec|test).?(c|m)[jt]s?(x)';
            const list = (m: any) => (m === undefined ? [DEFAULT] : Array.isArray(m) ? m : [m]);
            const win = process.platform === 'win32';
            const same = (a: string, b: string) => (win ? a.toLowerCase() === b.toLowerCase() : a === b);
            const common = (a: string, b: string) => {
              const x = a.split(path.sep), y = b.split(path.sep);
              let i = 0;
              while (i < x.length && i < y.length && same(x[i], y[i])) i++;
              return x.slice(0, i).join(path.sep) || path.sep;
            };
            // Glob patterns use forward slashes on every platform (a backslash escapes).
            const glob = (p: string) => p.split(path.sep).join('/');
            const projects: any[] = base.projects ?? [];
            const like = projects.find((p) => !/setup|teardown/i.test(p.name ?? '')) ?? {};
            const testDir = common(path.resolve(here, like.testDir ?? base.testDir ?? '.'), specDir);
            const ours = {
              name,
              testDir,
              testMatch: [...list(like.testMatch ?? base.testMatch), glob(path.join(specDir, '**/*.spec.ts')), glob(path.join(specDir, '**/*.spec.js'))],
              testIgnore: [],
              dependencies: like.dependencies,
              use: { ...(base.use ?? {}), ...(like.use ?? {}), baseURL: process.env.BASE_URL },
            };
            export default { ...base, webServer: undefined, projects: [...projects.filter((p) => p.name !== name), ours] };

            """);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ConfigFile), sb.ToString());
        Exclude(repo.Root, Path.Combine(dir, ConfigFile));
    }

    /// <summary>Adds a file stangent writes into the repo to <c>.git/info/exclude</c> (local, never committed), once.</summary>
    static void Exclude(string root, string file)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --git-path info/exclude") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var path = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            if (p.ExitCode != 0 || path.Length == 0) return;
            path = Path.GetFullPath(Path.Combine(root, path));
            var line = "/" + Path.GetRelativePath(root, file).Replace('\\', '/');
            var lines = File.Exists(path) ? File.ReadAllLines(path) : [];
            if (lines.Contains(line)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, (lines.Length > 0 && !File.ReadAllText(path).EndsWith('\n') ? "\n" : "") + line + "\n");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { }
    }

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

        WriteConfig(repo);
        await using var app = await BugProof.StartApp(repo, runner, ct);
        var report = new StringBuilder("# Related Playwright tests, run against the app before you started\n\n");
        if (related.Count == 0) report.Append("None: no existing spec opens this ticket's screens.\n");
        foreach (var spec in related)
        {
            var file = Path.GetRelativePath(target.WorkingDir(repo.Root), Path.Combine(repo.Root, spec)).Replace('\\', '/');
            var r = await runner.RunAsync($"npx playwright test --config {ConfigFile} --project={Project} '{file.Replace("'", "'\\''")}' --retries=0 --reporter=line", target.WorkingDir(repo.Root),
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
