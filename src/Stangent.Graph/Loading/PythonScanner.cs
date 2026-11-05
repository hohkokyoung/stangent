using System.Diagnostics;
using System.Text;
using Stangent.Core;

namespace Stangent.Graph.Loading;

/// <summary>A Python project: the files under a dependency file (or under the repo, when there's none). <see cref="Files"/> are relative to the repo; notebooks are listed so they can be counted, never read.</summary>
public sealed record PythonProject(string Id, string Name, string Label, string Directory, IReadOnlyList<string> Files);

/// <summary>What <c>graph build</c> says about Python (D127): files read, what it couldn't, and how calls and routes came out.</summary>
public sealed record PythonStats(
    int Projects, int Files, int Unparsed, int NotebooksOmitted,
    int CallsCertain, int CallsGuessed, int CallsOutside, int CallsNotInProject, int CallsUnresolved,
    int ImportsResolved, int ImportsOutside, int ImportsUnresolved, int Routes, int RouteProblems,
    int FrontendCalls, int FrontendCallsMatched, int FrontendCallsUnmatched, int FrontendCallsRuntimeUrl)
{
    public static PythonStats From(CodeGraph graph, int projects)
    {
        var summaries = graph.AllFacts.Where(f => f.Detector == PythonScanner.Detector && f.Kind == "summary").ToList();
        int Sum(string key) => summaries.Sum(f => int.TryParse(f[key], out var n) ? n : 0);
        var calls = graph.AllFacts.Count(f => f.Detector == "frontend-http" && f.Kind == "call");
        var unmatched = graph.AllBlindSpots.Count(b => b.Detector == "frontend-http" && b.Reason.EndsWith("matched no backend endpoint", StringComparison.Ordinal));
        var runtime = graph.AllBlindSpots.Count(b => b.Detector == "frontend-http" && b.Reason.EndsWith("URL built at runtime", StringComparison.Ordinal));
        var routes = graph.AllNodes.Count(n => n.Kind == NodeKind.Endpoint && n.Id.StartsWith("endpoint:python", StringComparison.Ordinal));
        return new PythonStats(projects, Sum("files"), Sum("unparsed"), Sum("notebooks"),
            Sum("callsCertain"), Sum("callsGuessed"), Sum("callsOutside"), Sum("callsNotInProject"), Sum("callsUnresolved"),
            Sum("importsResolved"), Sum("importsOutside"), Sum("importsUnresolved"), routes, Sum("routeProblems"),
            calls, calls - unmatched, unmatched, runtime);
    }
}

/// <summary>
/// Finds Python projects, fingerprints them and runs the bundled extractor on them (D127). Only ever parses: the extractor is run with
/// <c>-I -B</c> (no environment, no current folder, no bytecode written) and never imports or runs what it reads.
/// </summary>
public static class PythonScanner
{
    public const string Detector = "python";

    static readonly HashSet<string> SkipDirs = new(StringComparer.Ordinal)
    {
        "node_modules", ".git", ".stangent", "__pycache__", "venv", ".venv", "site-packages", ".tox", ".nox", ".eggs",
        ".mypy_cache", ".pytest_cache", ".ruff_cache", ".ipynb_checkpoints", "dist", "build", "obj",
    };

    static readonly string[] Markers = ["pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile"];

    /// <summary>A virtualenv whatever it's called, packaging leftovers, and a C# project's build output.</summary>
    static bool Skip(string dir)
    {
        var name = Path.GetFileName(dir);
        if (SkipDirs.Contains(name) || name.EndsWith(".egg-info", StringComparison.Ordinal)) return true;
        if (File.Exists(Path.Combine(dir, "pyvenv.cfg")) || File.Exists(Path.Combine(dir, "bin", "activate")) || File.Exists(Path.Combine(dir, "Scripts", "activate"))) return true;
        if (name == "bin" && Directory.EnumerateFiles(Path.GetDirectoryName(dir)!, "*.csproj").Any()) return true;
        return new DirectoryInfo(dir).LinkTarget is not null;
    }

    /// <summary>Each file belongs to the nearest folder above it with a dependency file, else to the repo. None when the repo has no Python.</summary>
    public static List<PythonProject> Find(string root)
    {
        var byHome = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var markerOf = new Dictionary<string, string?>(StringComparer.Ordinal);
        var stack = new Stack<(string Dir, string Home)>([(root, Marker(root) is null ? "" : root)]);
        markerOf[root] = Marker(root);
        markerOf[""] = null;
        while (stack.Count > 0)
        {
            var (dir, home) = stack.Pop();
            IEnumerable<string> files, dirs;
            try { files = Directory.EnumerateFiles(dir); dirs = Directory.EnumerateDirectories(dir); }
            catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException or IOException) { continue; }
            foreach (var f in files)
                if (Path.GetExtension(f) is ".py" or ".wsgi" or ".ipynb")
                {
                    if (!byHome.TryGetValue(home, out var list)) byHome[home] = list = [];
                    list.Add(ProjectScanner.Rel(root, f));
                }
            foreach (var d in dirs)
            {
                if (Skip(d)) continue;
                var marker = Marker(d);
                if (marker is not null) markerOf[d] = marker;
                stack.Push((d, marker is not null ? d : home));
            }
        }
        var projects = new List<PythonProject>();
        foreach (var (home, list) in byHome)
        {
            // Notebooks alone aren't a Python project to read, but the omission still has to be said, so they make one.
            list.Sort(StringComparer.Ordinal);
            var marker = markerOf[home];
            var rel = home.Length == 0 ? "" : ProjectScanner.Rel(root, home);
            var dirRel = rel == "." ? "" : rel;
            var label = marker is null ? "python" : dirRel.Length == 0 ? marker : $"{dirRel}/{marker}";
            var name = dirRel.Length == 0 ? "python" : dirRel;
            projects.Add(new PythonProject("project:" + label, name, label, home.Length == 0 ? root : home, list));
        }
        return projects;
    }

    static string? Marker(string dir) => Markers.FirstOrDefault(m => File.Exists(Path.Combine(dir, m)));

    /// <summary>Changes when a file's contents change. A notebook counts by its path: it's never read.</summary>
    public static string Fingerprint(string root, PythonProject p) =>
        CanonicalJson.Hash(string.Join("\n", p.Files.Select(f => f.EndsWith(".ipynb", StringComparison.Ordinal) ? f
            : $"{f}\t{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, f))))}")));

    /// <summary>The extractor shipped beside the stangent assemblies, or STANGENT_PY_EXTRACTOR.</summary>
    public static string? ExtractorPath()
    {
        var env = Environment.GetEnvironmentVariable("STANGENT_PY_EXTRACTOR");
        if (!string.IsNullOrEmpty(env)) return File.Exists(env) ? env : null;
        var shipped = Path.Combine(AppContext.BaseDirectory, "py-extractor", "stangent_py_extract.py");
        return File.Exists(shipped) ? shipped : null;
    }

    /// <summary>So a new extractor re-reads every Python project.</summary>
    public static string ExtractorHash() => ExtractorPath() is { } p ? CanonicalJson.Hash(Convert.ToBase64String(File.ReadAllBytes(p))) : "none";

    /// <summary>Python 3 and its version, which decides what syntax parses. Null when there's none.</summary>
    public static (string Command, string Version)? Interpreter()
    {
        foreach (var command in new[] { "python3", "python" })
        {
            var (code, output) = Run(command, ["-I", "-B", "-c", "import sys; print('.'.join(map(str, sys.version_info[:3])))"], null, Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(20));
            var version = output.Trim();
            if (code == 0 && version.StartsWith("3.", StringComparison.Ordinal)) return (command, version);
        }
        return null;
    }

    public static (ProjectFragment Fragment, List<string> Warnings, string? Error) Extract(string root, PythonProject p, string fingerprint, string python, bool flask)
    {
        var fragment = new ProjectFragment { ProjectId = p.Id, Fingerprint = fingerprint };
        var warnings = new List<string>();
        if (ExtractorPath() is not { } extractor)
            return (fragment, warnings, $"{p.Label}: the Python extractor isn't installed with this stangent build");
        List<string> args = ["-I", "-B", extractor, "--root", root, "--project", p.Id, "--name", p.Name, "--dir", p.Directory];
        if (!flask) args.Add("--no-flask");
        var (code, output) = Run(python, args, string.Join("\n", p.Files) + "\n", root, TimeSpan.FromMinutes(10));
        if (code != 0) return (fragment, warnings, $"{p.Label}: Python extractor failed (exit {code}): {Tail(output)}");
        FrontendScanner.ReadFragment(output, fragment, warnings, p.Label);
        // It always reports the project itself. Nothing at all means it didn't really run: never leave a project silently empty.
        if (!fragment.Nodes.Any(n => n.Id == p.Id))
            return (fragment, warnings, $"{p.Label}: the Python extractor produced no output ({extractor})");
        return (fragment, warnings, null);
    }

    static (int Code, string Output) Run(string file, IEnumerable<string> args, string? stdin, string dir, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            if (!process.WaitForExit(timeout)) { process.Kill(entireProcessTree: true); return (-1, "timed out"); }
            return (process.ExitCode, process.ExitCode == 0 ? stdout.Result : stdout.Result + "\n" + stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, $"{file} not found: {e.Message}");
        }
    }

    static string Tail(string text)
    {
        var last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return last.Length > 300 ? last[..300] + "…" : last;
    }
}
