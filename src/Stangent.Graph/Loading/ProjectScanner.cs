using System.Diagnostics;
using System.Xml.Linq;
using Stangent.Core;

namespace Stangent.Graph.Loading;

/// <summary>A <c>.csproj</c> found in the repo, with what it references and its fingerprint.</summary>
public sealed record RepoProject(string Id, string FullPath, string RelativePath, string Name, IReadOnlyList<string> References)
{
    public string Directory => Path.GetDirectoryName(FullPath)!;
}

/// <summary>
/// Finds projects without loading MSBuild: fast enough to run on every build, so incremental
/// builds can tell what changed before paying for Roslyn (D4).
/// </summary>
public static class ProjectScanner
{
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", ".git", ".stangent", ".vs", ".idea", "artifacts" };

    static readonly string[] SharedBuildFiles =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.config", "nuget.config"];

    public static string ProjectId(string relativePath) => "project:" + relativePath;

    /// <summary>Every <c>.csproj</c> once, sorted, however many solutions include it.</summary>
    public static List<RepoProject> Find(string root)
    {
        var paths = Walk(root).Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
        return [.. paths.Select(p =>
        {
            var rel = Rel(root, p);
            return new RepoProject(ProjectId(rel), p, rel, Path.GetFileNameWithoutExtension(p), References(root, p));
        })];
    }

    /// <summary>
    /// A project's fingerprint changes when any of its own files, the shared build files above it,
    /// or the fingerprint of a project it references changes.
    /// </summary>
    public static Dictionary<string, string> Fingerprints(string root, IReadOnlyList<RepoProject> projects)
    {
        var byPath = projects.ToDictionary(p => p.RelativePath, StringComparer.Ordinal);
        var projectDirs = projects.Select(p => p.Directory).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        string Of(RepoProject p, HashSet<string> visiting)
        {
            if (result.TryGetValue(p.Id, out var done)) return done;
            if (!visiting.Add(p.Id)) return "cycle";
            var parts = new List<string>();
            foreach (var file in OwnFiles(p, projectDirs))
                parts.Add($"{Rel(root, file)}\t{CanonicalJson.Hash(File.ReadAllText(file))}");
            foreach (var file in SharedFilesAbove(root, p.Directory))
                parts.Add($"{Rel(root, file)}\t{CanonicalJson.Hash(File.ReadAllText(file))}");
            foreach (var reference in p.References)
                parts.Add($"ref\t{reference}\t{(byPath.TryGetValue(reference, out var r) ? Of(r, visiting) : "missing")}");
            return result[p.Id] = CanonicalJson.Hash(string.Join("\n", parts));
        }

        foreach (var p in projects) Of(p, []);
        return result;
    }

    /// <summary>The project's own source, project and settings files (not those of a project nested inside it).</summary>
    public static IEnumerable<string> OwnFiles(RepoProject p, IReadOnlySet<string> projectDirs) =>
        Walk(p.Directory, d => d == p.Directory || !projectDirs.Contains(d))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// Restores every solution, then any project still missing its assets file. Roslyn can't
    /// resolve package types without a restore (D4).
    /// </summary>
    public static List<string> Restore(string root, IReadOnlyList<RepoProject> projects, TextWriter log)
    {
        // One message per failing project, however many solutions include it.
        var errors = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var sln in Walk(root).Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            Run(root, sln, log, errors);
        foreach (var p in projects.Where(p => !File.Exists(Path.Combine(p.Directory, "obj", "project.assets.json")) && !errors.ContainsKey(p.RelativePath)))
            Run(root, p.FullPath, log, errors);
        return [.. errors.Select(e => $"{e.Key}: restore failed: {e.Value}")];
    }

    static void Run(string root, string target, TextWriter log, SortedDictionary<string, string> errors)
    {
        log.WriteLine($"stangent: dotnet restore {Rel(root, target)}");
        var psi = new ProcessStartInfo("dotnet", $"restore \"{target}\" --nologo -v q -p:NuGetAudit=false")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode == 0) return;
        foreach (var line in (stdout.Result + "\n" + stderr.Result).Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"error (\w+): (.*?) \[(.+?\.csproj)");
            if (!m.Success) continue;
            var project = Rel(root, m.Groups[3].Value.Split("::")[0]);
            errors.TryAdd(project, $"{m.Groups[1].Value} {Shorten(root, m.Groups[2].Value)}");
        }
        if (!errors.Any(e => target.EndsWith(e.Key, StringComparison.Ordinal)) && target.EndsWith(".csproj", StringComparison.Ordinal))
            errors.TryAdd(Rel(root, target), $"exit {process.ExitCode}");
    }

    /// <summary>Messages with repo-relative paths and at most 300 characters.</summary>
    public static string Shorten(string root, string message)
    {
        var text = message.Replace(root + Path.DirectorySeparatorChar, "").Replace(root, ".").Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }

    /// <summary>The folder of the solution that lists the most projects, with a trailing separator; null without one.</summary>
    public static string? MainSolutionDir(string root, IReadOnlyList<RepoProject> projects)
    {
        var best = Walk(root).Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Count: projects.Count(p => File.ReadAllText(f).Contains(Path.GetFileName(p.FullPath), StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(s => s.Count).ThenBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault();
        return best.Path is null || best.Count == 0 ? null : Path.GetDirectoryName(best.Path) + Path.DirectorySeparatorChar;
    }

    static List<string> References(string root, string csproj)
    {
        try
        {
            var dir = Path.GetDirectoryName(csproj)!;
            return [.. XDocument.Load(csproj).Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference" && e.Attribute("Include") is not null)
                .Select(e => Rel(root, Path.GetFullPath(Path.Combine(dir, e.Attribute("Include")!.Value.Replace('\\', '/')))))
                .Distinct().Order(StringComparer.Ordinal)];
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    static IEnumerable<string> SharedFilesAbove(string root, string dir)
    {
        for (var d = new DirectoryInfo(dir); d is not null && d.FullName.Length >= root.Length; d = d.Parent)
            foreach (var name in SharedBuildFiles)
            {
                var path = Path.Combine(d.FullName, name);
                if (File.Exists(path)) yield return path;
            }
    }

    public static IEnumerable<string> Walk(string dir, Func<string, bool>? enter = null)
    {
        var stack = new Stack<string>([dir]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files, dirs;
            try
            {
                files = Directory.EnumerateFiles(current);
                dirs = Directory.EnumerateDirectories(current);
            }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var f in files) yield return f;
            foreach (var d in dirs.OrderDescending(StringComparer.Ordinal))
                if (!SkipDirs.Contains(Path.GetFileName(d)) && (enter?.Invoke(d) ?? true)) stack.Push(d);
        }
    }

    public static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
