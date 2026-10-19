using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Graph.Loading;

/// <summary>A frontend project: a tsconfig (tsconfig.app.json preferred) beside a package.json.</summary>
public sealed record FrontendProject(string Id, string TsConfig, string RelativeTsConfig, string Directory);

/// <summary>
/// Finds frontend projects, fingerprints them, restores their packages and runs the bundled
/// TypeScript extractor on them (D30). The extractor's output is a fragment like any C# project's.
/// </summary>
public static class FrontendScanner
{
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "dist", "build", "coverage", ".angular", "bin", "obj", ".git", ".stangent", "out", ".next", ".nuxt" };

    static readonly string[] SourceExtensions = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts", ".html", ".json"];

    public static List<FrontendProject> Find(string root)
    {
        var byDir = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Walk(root).Where(f => Path.GetFileName(f) is "tsconfig.json" or "tsconfig.app.json"))
        {
            var dir = Path.GetDirectoryName(file)!;
            if (!File.Exists(Path.Combine(dir, "package.json"))) continue; // a base config, extended by a real project
            if (!byDir.TryGetValue(dir, out var existing) || Path.GetFileName(file) == "tsconfig.app.json")
                byDir[dir] = file;
            _ = existing;
        }
        return [.. byDir.Select(kv =>
        {
            var rel = ProjectScanner.Rel(root, kv.Value);
            return new FrontendProject("project:" + rel, kv.Value, rel, kv.Key);
        })];
    }

    /// <summary>Changes when any of the frontend's own source, template, config or lock files change.</summary>
    public static string Fingerprint(string root, FrontendProject p, IEnumerable<string> otherProjectDirs)
    {
        var nested = otherProjectDirs.Where(d => d != p.Directory && d.StartsWith(p.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToHashSet();
        var parts = Walk(p.Directory, d => !nested.Contains(d))
            .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                || Path.GetFileName(f) is "yarn.lock" or "pnpm-lock.yaml")
            .Order(StringComparer.Ordinal)
            .Select(f => $"{ProjectScanner.Rel(root, f)}\t{CanonicalJson.Hash(File.ReadAllText(f))}");
        return CanonicalJson.Hash(string.Join("\n", parts));
    }

    /// <summary>
    /// Installs the frontend's packages when node_modules is missing, with the package manager its
    /// lock file names. Install scripts are not run: they're arbitrary code, and types don't need them.
    /// </summary>
    public static string? Restore(FrontendProject p, TextWriter log)
    {
        if (Directory.Exists(Path.Combine(p.Directory, "node_modules"))) return null;
        var (tool, args) = File.Exists(Path.Combine(p.Directory, "pnpm-lock.yaml")) ? ("pnpm", "install --frozen-lockfile --ignore-scripts")
            : File.Exists(Path.Combine(p.Directory, "yarn.lock")) ? ("yarn", YarnArgs(p.Directory))
            : File.Exists(Path.Combine(p.Directory, "package-lock.json")) ? ("npm", "ci --ignore-scripts --no-audit --no-fund")
            : ("npm", "install --ignore-scripts --no-audit --no-fund");
        // yarn and pnpm often aren't installed globally: run the version package.json pins through npx.
        if (tool != "npm" && Run(tool, "--version", p.Directory, TimeSpan.FromSeconds(20)).Code != 0)
            (tool, args) = ("npx", $"--yes {PinnedTool(p.Directory, tool)} {args}");
        log.WriteLine($"stangent: {tool} {args} in {p.RelativeTsConfig}");
        var (code, output) = Run(tool, args, p.Directory, TimeSpan.FromMinutes(15));
        if (code != 0 && tool == "npm" && args.StartsWith("ci", StringComparison.Ordinal))
        {
            // npm ci refuses a lock file that's out of step with package.json (Kavita's is). Install
            // without touching the repo's lock file: types are all stangent needs.
            const string fallback = "install --ignore-scripts --no-audit --no-fund --no-save --package-lock=false";
            log.WriteLine($"stangent: npm ci failed; npm {fallback} in {p.RelativeTsConfig}");
            (code, output) = Run("npm", fallback, p.Directory, TimeSpan.FromMinutes(15));
        }
        return code == 0 ? null : $"{p.RelativeTsConfig}: {tool} install failed (exit {code}): {Tail(output)}";
    }

    /// <summary>"yarn@1.22.22" from package.json's packageManager field, or the tool's latest major.</summary>
    static string PinnedTool(string dir, string tool)
    {
        try
        {
            var pkg = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json")));
            var pinned = (string?)pkg?["packageManager"];
            if (pinned is not null && pinned.StartsWith(tool + "@", StringComparison.Ordinal)) return pinned.Split('+')[0];
        }
        catch (JsonException) { }
        return tool == "yarn" ? "yarn@1" : tool;
    }

    /// <summary>Yarn 1 wants --frozen-lockfile; Yarn 2+ (a .yarnrc.yml) wants --immutable.</summary>
    static string YarnArgs(string dir) =>
        File.Exists(Path.Combine(dir, ".yarnrc.yml")) ? "install --immutable --mode=skip-build" : "install --frozen-lockfile --ignore-scripts --non-interactive";

    /// <summary>The bundled extractor shipped beside the stangent assemblies, or STANGENT_TS_EXTRACTOR.</summary>
    public static string? ExtractorPath()
    {
        var env = Environment.GetEnvironmentVariable("STANGENT_TS_EXTRACTOR");
        if (!string.IsNullOrEmpty(env)) return File.Exists(env) ? env : null;
        var shipped = Path.Combine(AppContext.BaseDirectory, "ts-extractor", "stangent-ts-extract.mjs");
        return File.Exists(shipped) ? shipped : null;
    }

    /// <summary>Hash of the extractor bundle, so a new extractor re-extracts every frontend.</summary>
    public static string ExtractorHash() => ExtractorPath() is { } p ? CanonicalJson.Hash(Convert.ToBase64String(File.ReadAllBytes(p))) : "none";

    public static bool NodeAvailable() => Run("node", "--version", Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(20)).Code == 0;

    /// <summary>Runs the extractor and reads its JSON lines into a fragment; warnings come back separately.</summary>
    public static (ProjectFragment Fragment, List<string> Warnings, string? Error) Extract(
        string root, FrontendProject p, string fingerprint, IReadOnlyDictionary<string, Dictionary<string, List<string>>> settings,
        string? extractorOverride = null)
    {
        var fragment = new ProjectFragment { ProjectId = p.Id, Fingerprint = fingerprint };
        var warnings = new List<string>();
        var extractor = extractorOverride ?? ExtractorPath();
        if (extractor is null)
            return (fragment, warnings, $"{p.RelativeTsConfig}: the TypeScript extractor isn't installed with this stangent build");
        var settingsJson = JsonSerializer.Serialize(settings);
        var args = $"\"{extractor}\" --tsconfig \"{p.TsConfig}\" --root \"{root}\" --project \"{p.Id}\" --settings {Quote(settingsJson)}";
        var (code, output) = Run("node", args, root, TimeSpan.FromMinutes(10), splitStderr: true);
        if (code != 0) return (fragment, warnings, $"{p.RelativeTsConfig}: TypeScript extractor failed (exit {code}): {Tail(output)}");

        foreach (var line in output.Split('\n'))
        {
            if (line.Length == 0 || line[0] != '{') continue;
            var o = JsonNode.Parse(line)!.AsObject();
            switch ((string?)o["t"])
            {
                case "node":
                    fragment.Nodes.Add(new Node((string)o["id"]!, (string)o["kind"]!, (string)o["name"]!, (string?)o["project"], (string?)o["file"],
                        (int)o["line"]!, (int)o["endLine"]!, (bool)o["isTest"]!));
                    break;
                case "edge":
                    fragment.Edges.Add(new Edge((string)o["source"]!, (string)o["target"]!, (string)o["kind"]!, (Label)(int)o["label"]!, (string)o["detector"]!));
                    break;
                case "blind":
                    fragment.BlindSpots.Add(new BlindSpot((string)o["node"]!, (string)o["file"]!, (int)o["line"]!, (string)o["reason"]!, (string)o["detector"]!));
                    break;
                case "fact":
                    fragment.Facts.Add(new Fact((string)o["detector"]!, (string)o["kind"]!, (string)o["node"]!, (string?)o["file"], (int)o["line"]!,
                        o["data"]!.AsObject().ToDictionary(kv => kv.Key, kv => (string)kv.Value!, StringComparer.Ordinal)));
                    break;
                case "warning":
                    warnings.Add($"{p.RelativeTsConfig}: {(string?)o["message"]}");
                    break;
            }
        }
        // The extractor always reports at least the project itself; nothing at all means it didn't
        // really run (a broken or wrong bundle). Say so, never leave the frontend silently empty.
        if (!fragment.Nodes.Any(n => n.Id == p.Id))
            return (fragment, warnings, $"{p.RelativeTsConfig}: the TypeScript extractor produced no output ({extractor})");
        return (fragment, warnings, null);
    }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static (int Code, string Output) Run(string file, string args, string dir, TimeSpan timeout, bool splitStderr = false)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout)) { process.Kill(entireProcessTree: true); return (-1, "timed out"); }
            return (process.ExitCode, splitStderr && process.ExitCode == 0 ? stdout.Result : stdout.Result + "\n" + stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, $"{file} not found: {e.Message}");
        }
    }

    static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var picked = lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(2).DefaultIfEmpty(lines.LastOrDefault() ?? "");
        var joined = string.Join(" | ", picked);
        return joined.Length > 300 ? joined[..300] + "…" : joined;
    }

    static IEnumerable<string> Walk(string dir, Func<string, bool>? enter = null)
    {
        var stack = new Stack<string>([dir]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files, dirs;
            try { files = System.IO.Directory.EnumerateFiles(current); dirs = System.IO.Directory.EnumerateDirectories(current); }
            catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException) { continue; }
            foreach (var f in files) yield return f;
            foreach (var d in dirs.OrderDescending(StringComparer.Ordinal))
                if (!SkipDirs.Contains(Path.GetFileName(d)) && (enter?.Invoke(d) ?? true)) stack.Push(d);
        }
    }
}
