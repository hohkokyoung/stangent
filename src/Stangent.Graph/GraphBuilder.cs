using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Stangent.Core;
using Stangent.Graph.Detectors;
using Stangent.Graph.Extraction;
using Stangent.Graph.Loading;

namespace Stangent.Graph;

public sealed record BuildOptions(bool Restore = true, bool Full = false, TextWriter? Log = null);

public sealed record BuildStats(
    string Commit, int Projects, int Extracted, int Reused, int Nodes, int Edges, int BlindSpots,
    IReadOnlyList<string> LoadErrors, IReadOnlyList<string> LoadWarnings, IReadOnlyList<string> Detectors, long LoadMs, long ExtractMs, long LinkMs, long TotalMs);

/// <summary>
/// Builds the graph (D25): scan projects, re-extract only those whose fingerprint changed, then
/// run every detector's link step over the whole graph and save it.
/// </summary>
public static class GraphBuilder
{
    public static async Task<(CodeGraph Graph, BuildStats Stats)> BuildAsync(StangentRepo repo, BuildOptions options, CancellationToken ct = default)
    {
        var log = options.Log ?? TextWriter.Null;
        var total = Stopwatch.StartNew();
        EnsureSdk(repo.Root);
        var detectors = DetectorRegistry.Enabled(repo.Config);
        var projects = ProjectScanner.Find(repo.Root);
        // A fragment is only reusable if it was made by this extractor with these detectors and
        // settings: an upgrade or a config change must re-extract, not reuse stale facts.
        var salt = Salt(repo.Config, detectors);
        var fingerprints = ProjectScanner.Fingerprints(repo.Root, projects)
            .ToDictionary(kv => kv.Key, kv => CanonicalJson.Hash(kv.Value + "\n" + salt), StringComparer.Ordinal);
        var previous = options.Full ? null : GraphStore.Load(GraphStore.PathFor(repo));

        var changed = projects.Where(p => previous?.Fragments.GetValueOrDefault(p.Id)?.Fingerprint != fingerprints[p.Id]).ToList();
        var graph = new CodeGraph { Commit = Git.Head(repo.Root) };
        foreach (var p in projects.Except(changed))
            graph.Fragments[p.Id] = previous!.Fragments[p.Id];
        // Load errors of projects that weren't re-extracted still apply.
        if (previous is not null)
            graph.LoadErrors.AddRange(previous.LoadErrors.Where(e => projects.Except(changed).Any(p => e.StartsWith(p.RelativePath + ":"))));

        var load = Stopwatch.StartNew();
        var extract = new Stopwatch();
        var warnings = new List<string>();
        if (changed.Count > 0)
        {
            if (options.Restore)
                graph.LoadErrors.AddRange(ProjectScanner.Restore(repo.Root, changed.Count == projects.Count ? projects : changed, log));

            using var workspace = CreateWorkspace(repo.Root, projects);
            foreach (var p in changed)
            {
                if (workspace.CurrentSolution.Projects.Any(x => x.FilePath == p.FullPath)) continue;
                log.WriteLine($"stangent: loading {p.RelativePath}");
                try { await workspace.OpenProjectAsync(p.FullPath, cancellationToken: ct); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    graph.LoadErrors.Add($"{p.RelativePath}: {ProjectScanner.Shorten(repo.Root, e.Message.Split('\n')[0])}");
                }
            }
            // MSBuild reports warnings (e.g. NuGet audit) as "failures" even when the project loaded;
            // only projects that produce no compilation count as load errors (below).
            warnings.AddRange(workspace.Diagnostics.Select(d => ErrorFor(repo.Root, changed, d.Message)).Distinct().Order(StringComparer.Ordinal));

            extract.Start();
            var fragments = new ProjectFragment[changed.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, changed.Count), ct, async (i, token) =>
            {
                var p = changed[i];
                var fragment = new ProjectFragment { ProjectId = p.Id, Fingerprint = fingerprints[p.Id] };
                var project = workspace.CurrentSolution.Projects
                    .Where(x => x.FilePath == p.FullPath).OrderBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
                if (project is not null && await project.GetCompilationAsync(token) is { } compilation)
                {
                    var files = project.Documents
                        .Where(d => d.FilePath is { } f && f.StartsWith(repo.Root, StringComparison.Ordinal)
                            && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                        .Select(d => compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath == d.FilePath))
                        .OfType<SyntaxTree>()
                        .Select(t => new SourceFile(t, compilation.GetSemanticModel(t), ProjectScanner.Rel(repo.Root, t.FilePath)))
                        .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
                        .ToList();
                    var ctx = new ExtractContext(repo.Root, p.Id, p.Directory, compilation, files, fragment, new Dictionary<string, List<string>>());
                    CSharpExtractor.Extract(ctx);
                    foreach (var detector in detectors)
                    {
                        ctx.Settings = DetectorRegistry.Settings(repo.Config, detector.Name);
                        detector.Extract(ctx);
                    }
                }
                else
                {
                    lock (graph.LoadErrors) graph.LoadErrors.Add($"{p.RelativePath}: could not be loaded; its code is ❓");
                    fragment.Nodes.Add(new Node(p.Id, NodeKind.Project, p.Name, p.Id));
                    fragment.BlindSpots.Add(new BlindSpot(p.Id, p.RelativePath, 0, "project could not be loaded"));
                }
                fragments[i] = fragment;
            });
            foreach (var f in fragments) graph.Fragments[f.ProjectId] = f;
            extract.Stop();
        }
        load.Stop();
        var loadMs = load.ElapsedMilliseconds - extract.ElapsedMilliseconds;

        var link = Stopwatch.StartNew();
        var linkCtx = new LinkContext(graph, new Dictionary<string, List<string>>());
        foreach (var detector in detectors)
        {
            linkCtx.Settings = DetectorRegistry.Settings(repo.Config, detector.Name);
            detector.Link(linkCtx);
        }
        link.Stop();

        GraphStore.Save(graph, GraphStore.PathFor(repo));
        total.Stop();
        var stats = new BuildStats(graph.Commit, projects.Count, changed.Count, projects.Count - changed.Count,
            graph.AllNodes.Count(), graph.AllEdges.Count(), graph.AllBlindSpots.Count(),
            [.. graph.LoadErrors.Distinct().Order(StringComparer.Ordinal)], warnings, [.. detectors.Select(d => d.Name)],
            loadMs, extract.ElapsedMilliseconds, link.ElapsedMilliseconds, total.ElapsedMilliseconds);
        return (graph, stats);
    }

    static string Salt(StangentConfig config, IReadOnlyList<IDetector> detectors)
    {
        var settings = string.Join("\n", detectors.Select(d => d.Name + "=" + string.Join(";",
            DetectorRegistry.Settings(config, d.Name).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + ":" + string.Join(",", kv.Value)))));
        // The module version id changes whenever the extractor's code does (builds are deterministic).
        return $"{typeof(GraphBuilder).Assembly.ManifestModule.ModuleVersionId}\n{GraphStore.SchemaVersion}\n{settings}";
    }

    /// <summary>An MSBuild workspace set up the way stangent loads projects (also used by the benchmark).</summary>
    public static MSBuildWorkspace CreateWorkspace(string root, IReadOnlyList<RepoProject> projects)
    {
        var properties = new Dictionary<string, string> { ["Configuration"] = "Debug", ["NuGetAudit"] = "false", ["TreatWarningsAsErrors"] = "false" };
        // Projects that build relative to $(SolutionDir) need it set when loaded on their own.
        if (ProjectScanner.MainSolutionDir(root, projects) is { } solutionDir) properties["SolutionDir"] = solutionDir;
        return MSBuildWorkspace.Create(properties);
    }

    /// <summary>
    /// The repo's global.json may ask for a .NET SDK that isn't installed; MSBuild then fails deep
    /// inside project loading. Check first and say what to do.
    /// </summary>
    public static void EnsureSdk(string root)
    {
        var psi = new ProcessStartInfo("dotnet", "--version")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode == 0) return;
        var requested = System.Text.RegularExpressions.Regex.Match(output, @"Requested SDK version: (\S+)");
        throw new StangentException("sdk_missing",
            requested.Success
                ? $"This repo's global.json asks for .NET SDK {requested.Groups[1].Value}, which isn't installed. Install it, or relax global.json's version or rollForward."
                : $"`dotnet --version` fails in {root}: {ProjectScanner.Shorten(root, output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "")}",
            ExitCodes.Failed);
    }

    static string ErrorFor(string root, IEnumerable<RepoProject> projects, string message)
    {
        var project = projects.FirstOrDefault(p => message.Contains(p.FullPath, StringComparison.Ordinal));
        var text = ProjectScanner.Shorten(root, message);
        return project is null ? text : $"{project.RelativePath}: {text.Replace(project.RelativePath + ": ", "")}";
    }
}

/// <summary>Runs git in a repo; null when git fails or isn't installed.</summary>
public static class Git
{
    public static string Head(string root) => Run(root, "rev-parse HEAD")?.Trim() ?? "";

    public static string? Run(string root, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
