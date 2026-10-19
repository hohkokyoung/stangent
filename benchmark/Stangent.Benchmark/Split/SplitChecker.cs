using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Stangent.Graph.Loading;

namespace Stangent.Benchmark.Split;

/// <summary>How one repo is built and unit-tested for the split check (<c>benchmark/configs/&lt;name&gt;.split.json</c>).</summary>
sealed record SplitConfig
{
    /// <summary>The solution to build, relative to the repo root (<c>.sln</c> or <c>.slnx</c>).</summary>
    public string Solution { get; init; } = "";
    /// <summary>Test projects: project names matching this pattern.</summary>
    public string TestProjects { get; init; } = @"\.Tests?$";
    /// <summary>Test projects whose name contains one of these are left out (integration tests, D46).</summary>
    public List<string> IntegrationMarkers { get; init; } = ["Integration"];
    /// <summary>A <c>dotnet test --filter</c> expression for the repo's own markers (e.g. NUnit categories).</summary>
    public string TestFilter { get; init; } = "";
    /// <summary>Projects this machine can't build (e.g. they need Rust): left out with every project that references them.</summary>
    public List<string> BuildExclude { get; init; } = [];
    /// <summary>Path prefixes of the frontend: a change touching them runs <see cref="FrontendCommands"/> (tsc).</summary>
    public List<string> FrontendPaths { get; init; } = [];
    public List<string> FrontendCommands { get; init; } = [];
    public int BuildTimeoutMinutes { get; init; } = 30;
    public int TestTimeoutMinutes { get; init; } = 30;

    public static SplitConfig Load(string path) =>
        JsonSerializer.Deserialize<SplitConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip })
        ?? throw new InvalidOperationException($"{path}: empty");

    public bool IsFrontend(string path) => FrontendPaths.Any(p => path.StartsWith(p, StringComparison.Ordinal));

    public JsonObject ToJson() => JsonSerializer.SerializeToNode(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
}

sealed record BuildResult(bool Ok, IReadOnlyList<string> Errors);

/// <summary>Builds and tests whatever is in the working tree. The real one runs dotnet and tsc; tests use a fake.</summary>
interface ISplitChecker
{
    BuildResult Build(bool dotnet, bool frontend);
    /// <summary>Each test's full name → whether it passed. A project that can't be run gives nothing.</summary>
    Dictionary<string, bool> Test(IReadOnlyList<string> testProjects);
}

sealed partial class DotnetChecker(string root, SplitConfig config, string workDir, TextWriter log) : ISplitChecker
{
    [GeneratedRegex(@": error [A-Z]+[0-9]+:|error TS[0-9]+:|: error :")]
    private static partial Regex ErrorLine();

    public BuildResult Build(bool dotnet, bool frontend)
    {
        var errors = new List<string>();
        var ok = true;
        if (dotnet)
        {
            var (code, output) = Run("dotnet", ["build", BuildTarget(), "-nologo", "-v", "q", "-clp:NoSummary"], TimeSpan.FromMinutes(config.BuildTimeoutMinutes));
            ok &= code == 0;
            errors.AddRange(Errors(output));
            if (code != 0 && errors.Count == 0) errors.Add($"dotnet build exited {code}: {Tail(output)}");
        }
        if (frontend)
            foreach (var command in config.FrontendCommands)
            {
                var (code, output) = Run("/bin/sh", ["-c", command], TimeSpan.FromMinutes(config.BuildTimeoutMinutes));
                if (code == 0) continue;
                ok = false;
                var found = Errors(output);
                errors.AddRange(found.Count > 0 ? found : [$"{command} exited {code}: {Tail(output)}"]);
                break;
            }
        return new BuildResult(ok, [.. errors.Distinct().Take(10)]);
    }

    List<string> Errors(string output) =>
        [.. output.Split('\n').Where(l => ErrorLine().IsMatch(l)).Select(l => l.Replace(root + Path.DirectorySeparatorChar, "").Trim()).Distinct()];

    static string Tail(string output) => string.Join(" ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(3)).Trim();

    /// <summary>The solution, or a solution filter without the projects this machine can't build.</summary>
    string BuildTarget()
    {
        if (config.BuildExclude.Count == 0) return config.Solution;
        var excluded = Excluded(ProjectScanner.Find(root), config.BuildExclude);
        var solution = Path.Combine(root, config.Solution);
        var listed = SolutionProjects(solution).Where(p => !excluded.Contains(Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solution)!, p.Replace('\\', '/')))).Replace('\\', '/')));
        var filter = Path.Combine(root, ".stangent", "split-build.slnf");
        Directory.CreateDirectory(Path.GetDirectoryName(filter)!);
        var json = new JsonObject
        {
            ["solution"] = new JsonObject
            {
                ["path"] = Path.GetRelativePath(Path.GetDirectoryName(filter)!, solution).Replace('\\', '/'),
                ["projects"] = new JsonArray([.. listed.Select(p => (JsonNode)p)]),
            },
        };
        File.WriteAllText(filter, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return filter;
    }

    /// <summary>The excluded projects and every project that references one of them, as repo-relative paths.</summary>
    public static HashSet<string> Excluded(IReadOnlyList<RepoProject> projects, IEnumerable<string> roots)
    {
        var excluded = roots.ToHashSet(StringComparer.Ordinal);
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var p in projects)
                if (!excluded.Contains(p.RelativePath) && p.References.Any(excluded.Contains)) grew = excluded.Add(p.RelativePath) || grew;
        }
        return excluded;
    }

    static IEnumerable<string> SolutionProjects(string solution) =>
        solution.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? XDocument.Load(solution).Descendants("Project").Select(e => (string?)e.Attribute("Path")).OfType<string>().Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            : Regex.Matches(File.ReadAllText(solution), @"^Project\(""\{[^}]+\}""\) = ""[^""]*"", ""([^""]+\.csproj)""", RegexOptions.Multiline).Select(m => m.Groups[1].Value);

    public Dictionary<string, bool> Test(IReadOnlyList<string> testProjects)
    {
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);
        var dir = Path.Combine(workDir, "trx");
        Directory.CreateDirectory(dir);
        foreach (var project in testProjects)
        {
            if (!File.Exists(Path.Combine(root, project))) continue;
            var name = Path.GetFileNameWithoutExtension(project) + ".trx";
            File.Delete(Path.Combine(dir, name));
            List<string> args = ["test", project, "--no-build", "-nologo", "--logger", $"trx;LogFileName={name}", "--results-directory", dir];
            if (config.TestFilter.Length > 0) args.AddRange(["--filter", config.TestFilter]);
            var (code, output) = Run("dotnet", args, TimeSpan.FromMinutes(config.TestTimeoutMinutes));
            var trx = Path.Combine(dir, name);
            if (!File.Exists(trx)) { log.WriteLine($"split: {project}: no test results (exit {code}): {Tail(output)}"); continue; }
            foreach (var (test, passed) in ReadTrx(File.ReadAllText(trx)))
                results[project + "::" + test] = passed && results.GetValueOrDefault(project + "::" + test, true);
        }
        return results;
    }

    /// <summary>Each test result in a TRX file: class + test name (parameters included), and whether it passed.</summary>
    public static IEnumerable<(string Test, bool Passed)> ReadTrx(string xml)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root!.Name.Namespace;
        var classes = doc.Descendants(ns + "UnitTest").ToDictionary(
            u => (string)u.Attribute("id")!, u => (string?)u.Element(ns + "TestMethod")?.Attribute("className") ?? "", StringComparer.Ordinal);
        foreach (var r in doc.Descendants(ns + "UnitTestResult"))
        {
            var outcome = (string?)r.Attribute("outcome") ?? "";
            if (outcome is "NotExecuted" or "Inconclusive") continue; // skipped tests say nothing either way
            var cls = classes.GetValueOrDefault((string?)r.Attribute("testId") ?? "", "");
            yield return ($"{cls}.{(string?)r.Attribute("testName")}", outcome == "Passed");
        }
    }

    (int Code, string Output) Run(string file, IEnumerable<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(file) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return (-1, $"timed out after {timeout.TotalMinutes} min");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
