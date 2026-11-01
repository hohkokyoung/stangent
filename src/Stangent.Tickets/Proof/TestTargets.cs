using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Tickets.Proof;

/// <summary>
/// Where a reproduction test may go (D76) and how stangent builds and runs it. <see cref="Dir"/> is the only folder the
/// reproducer may write in; <see cref="Patterns"/> are the files it may create or edit there.
/// </summary>
public sealed record TestTarget(string Id, string Kind, string Runner, string Dir, string? Project, IReadOnlyList<string> Patterns, IReadOnlyList<string> Covers)
{
    /// <summary>Extra <c>dotnet</c> arguments (<c>tickets.dotnetArgs</c>, already with the repo's path in).</summary>
    public string Args { get; init; } = "";
    /// <summary>The repo's unit-test filter for whole-project runs (<c>tickets.testFilter</c>).</summary>
    public string? Filter { get; init; }

    public const string Unit = "unit";
    public const string Playwright = "playwright";

    public string? BuildCommand => Runner == "dotnet" ? $"dotnet build {Quote(Project!)}{Extra}" : null;

    string Extra => Args.Length == 0 ? "" : " " + Args;

    /// <summary>The command that runs one test (or, with no name, the whole target), writing its report to <paramref name="report"/>.</summary>
    public string TestCommand(string? file, string? name, string report, bool vstest = true) => Runner switch
    {
        "dotnet" => vstest
            ? $"dotnet test {Quote(Project!)}{Extra}{(name is not null ? $" --filter {Quote("FullyQualifiedName~" + FilterEscape(name))}" : Filter is { Length: > 0 } f ? $" --filter {Quote(f)}" : "")} --logger {Quote("trx;LogFileName=" + report)}"
            // MTP: the build command already ran with the args (and dotnet test --project would rebuild without them).
            : $"dotnet test --project {Quote(Project!)} --no-build{(name is null ? "" : $" --filter-method {Quote(name)}")} -- --report-trx --report-trx-filename {Quote(Path.GetFileName(report))} --results-directory {Quote(Path.GetDirectoryName(report)!)}",
        "vitest" => $"npx vitest run{(file is null ? "" : " " + Quote(file))}{(name is null ? "" : $" -t {Quote(Anchored(name))}")} --reporter=json --outputFile={Quote(report)}",
        "jest" => $"npx jest{(file is null ? "" : " " + Quote(file))}{(name is null ? "" : $" -t {Quote(Anchored(name))}")} --json --outputFile={Quote(report)}",
        "playwright" => $"npx playwright test --config {AppSession.ConfigFile} --project={AppSession.Project}{(file is null ? "" : " " + Quote(file))}{(name is null ? "" : $" -g {Quote(RegexEscape(name))}")} --retries=0 --reporter=json",
        _ => throw new StangentException("usage", $"Unknown runner {Runner}."),
    };

    /// <summary>Lists the tests a .NET target's runner finds, without building (D90: a runner that finds none at all is
    /// broken, whatever test name was asked for). Null for other runners.</summary>
    public string? ListCommand(bool vstest = true) => Runner != "dotnet" ? null
        : vstest ? $"dotnet test {Quote(Project!)}{Extra} --no-build --list-tests" : $"dotnet test --project {Quote(Project!)} --no-build --list-tests";

    /// <summary>Where commands run: the package folder for Jest and Vitest, Playwright's own folder (its <see cref="Project"/>),
    /// the repo root otherwise.</summary>
    public string WorkingDir(string root) => Runner is "vitest" or "jest" ? Path.Combine(root, Dir)
        : Runner == "playwright" && Project is { Length: > 0 } p ? Path.Combine(root, p) : root;

    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["kind"] = Kind, ["runner"] = Runner, ["dir"] = Dir, ["project"] = Project, ["args"] = Args, ["filter"] = Filter,
        ["patterns"] = new JsonArray([.. Patterns.Select(p => (JsonNode)p)]), ["covers"] = new JsonArray([.. Covers.Select(c => (JsonNode)c)]),
    };

    public static TestTarget FromJson(JsonNode n) => new((string)n["id"]!, (string)n["kind"]!, (string)n["runner"]!, (string)n["dir"]!, (string?)n["project"],
        [.. n["patterns"]!.AsArray().Select(p => (string)p!)], [.. n["covers"]!.AsArray().Select(c => (string)c!)])
    { Args = (string?)n["args"] ?? "", Filter = (string?)n["filter"] };

    /// <summary>The repo root is written <c>.</c>.</summary>
    public bool AtRoot => Dir is "." or "";

    /// <summary>A path the reproducer wrote: inside <see cref="Dir"/> and matching one of <see cref="Patterns"/>.</summary>
    public bool Allows(string relativeFile)
    {
        var f = Normalize(relativeFile);
        if (f.Length == 0 || f.StartsWith('/') || f.Split('/').Contains("..")) return false;
        if (!AtRoot && !f.StartsWith(Dir.TrimEnd('/') + "/", StringComparison.Ordinal)) return false;
        var name = Path.GetFileName(f);
        return Patterns.Any(p => Regex.IsMatch(name, "^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase));
    }

    /// <summary>A repo-relative path as the reproducer may write it: slashes forward, one leading <c>./</c> dropped.</summary>
    public static string Normalize(string path)
    {
        var f = path.Replace('\\', '/');
        while (f.StartsWith("./", StringComparison.Ordinal)) f = f[2..];
        return f;
    }

    /// <summary>The edit rules for the driver (D60): each file pattern under the target's folder, by absolute path (D89).</summary>
    public IEnumerable<string> EditRules(string root) =>
        Patterns.Select(p => $"Edit({ClaudeRules.Absolute(AtRoot ? root : Path.Combine(root, Dir))}/**/{p})");

    /// <summary>VSTest's --filter treats <c>\ ( ) &amp; | = ! ~</c> as syntax; a backslash makes them literal.</summary>
    static string FilterEscape(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"[\\()&|=!~]", m => "\\" + m.Value);

    /// <summary>Jest's -t, Vitest's -t and Playwright's -g are regular expressions: a title is matched literally.</summary>
    static string RegexEscape(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"[\\^$.*+?()\[\]{}|/]", m => "\\" + m.Value);

    static string Anchored(string name) => RegexEscape(name) + "$";

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}

/// <summary>
/// Claude Code permission paths (D89). A rule without a leading <c>//</c> is read from the agent's current folder, so
/// once its shell runs <c>cd src</c>, <c>Edit(src/Tests/**)</c> means <c>src/src/Tests/**</c> and every allowed write
/// is refused. <c>//</c> and an absolute path name the same folder wherever the shell is.
/// </summary>
public static class ClaudeRules
{
    /// <summary><c>/repo/x</c> → <c>//repo/x</c>; <c>C:\repo\x</c> → <c>//c/repo/x</c>.</summary>
    public static string Absolute(string path)
    {
        var p = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        if (p.Length >= 2 && p[1] == ':') p = "/" + char.ToLowerInvariant(p[0]) + p[2..];
        return "/" + p;
    }
}

/// <summary>
/// Finds the test targets for a ticket's code (D76): .NET test projects (xUnit, NUnit, MSTest) that reference the
/// projects the starting points live in; the Jest or Vitest package holding their frontend files; and Playwright when
/// <c>tickets.app</c> is set. Code only, from the repo's files and the graph.
/// </summary>
public static partial class TestTargets
{
    public const int MaxUnitTargets = 3;
    static readonly string[] Skip = ["bin", "obj", "node_modules", ".git", ".stangent", "dist", "artifacts"];

    [GeneratedRegex(@"Microsoft\.NET\.Test\.Sdk|""xunit|Include=""xunit|NUnit|MSTest|<IsTestProject>\s*true", RegexOptions.IgnoreCase)]
    private static partial Regex TestSdk();

    [GeneratedRegex(@"<ProjectReference\s+Include=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectReference();

    /// <param name="files">Repo-relative files of the code the starting points reach (their symbols and pages' citations).</param>
    /// <param name="projects">Repo-relative project files those symbols belong to.</param>
    public static List<TestTarget> Find(StangentRepo repo, IReadOnlyCollection<string> projects, IReadOnlyCollection<string> files)
    {
        var root = repo.Root;
        var targets = new List<TestTarget>();

        // .NET: test projects referencing a starting point's project, directly or through any chain of references
        // (audit: a Domain project reached only via Web → Infrastructure → Application had no target at two levels).
        var csprojs = Files(root, "*.csproj").ToList();
        // Paths compare ignoring case: a reference may spell a folder's case differently from the disk, and on Windows
        // and macOS both name the same folder.
        var refs = csprojs.ToDictionary(p => p, p => References(root, p), StringComparer.OrdinalIgnoreCase);
        var direct = new Dictionary<string, int>(StringComparer.Ordinal);
        string? args = null;   // the graph build's properties, worked out only when a .NET test target is found
        foreach (var test in csprojs)
        {
            var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(refs[test]);
            while (queue.TryDequeue(out var r))
                if (reached.Add(r)) foreach (var next in refs.GetValueOrDefault(r) ?? []) queue.Enqueue(next);
            var covers = projects.Where(reached.Contains).Order(StringComparer.Ordinal).ToList();
            if (covers.Count == 0) continue;
            args ??= DotnetArgs(repo);
            if (!IsTestProject(repo, test, args)) continue;
            direct["dotnet:" + test] = projects.Count(p => refs[test].Contains(p, StringComparer.OrdinalIgnoreCase));
            var dir = Path.GetDirectoryName(test)!.Replace('\\', '/') is { Length: > 0 } d ? d : ".";
            targets.Add(new TestTarget("dotnet:" + test, TestTarget.Unit, "dotnet", dir, test, ["*.cs"], covers)
            {
                Args = args, Filter = repo.Config.Tickets.TestFilter,
            });
        }

        // Jest / Vitest: the nearest package.json above a starting point's frontend file that uses one of them.
        foreach (var pkg in Files(root, "package.json"))
        {
            var dir = Path.GetDirectoryName(pkg)!.Replace('\\', '/');
            var text = File.ReadAllText(Path.Combine(root, pkg));
            var runner = text.Contains("\"vitest\"", StringComparison.Ordinal) ? "vitest" : text.Contains("\"jest\"", StringComparison.Ordinal) ? "jest" : null;
            if (runner is null) continue;
            var covers = files.Where(f => Regex.IsMatch(f, @"\.(ts|tsx|js|jsx)$") && (dir.Length == 0 || f.StartsWith(dir + "/", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal).ToList();
            if (covers.Count == 0) continue;
            targets.Add(new TestTarget($"{runner}:{(dir.Length == 0 ? "." : dir)}", TestTarget.Unit, runner, dir.Length == 0 ? "." : dir, null,
                ["*.test.ts", "*.test.tsx", "*.test.js", "*.test.jsx", "*.spec.ts", "*.spec.tsx", "*.spec.js", "*.spec.jsx"], covers));
        }

        // Most covered projects referenced directly first: in Sonarr every test project reaches Core through a shared
        // helper library, so reach alone would rank Automation.Test or Integration.Test above Core.Test.
        // When some reference it directly, one that only reaches it through another project isn't offered.
        if (targets.Any(t => direct.GetValueOrDefault(t.Id) > 0))
            targets.RemoveAll(t => t.Id.StartsWith("dotnet:", StringComparison.Ordinal) && direct.GetValueOrDefault(t.Id) == 0);
        var unit = targets.OrderByDescending(t => direct.GetValueOrDefault(t.Id)).ThenByDescending(t => t.Covers.Count)
            .ThenBy(t => t.Id, StringComparer.Ordinal).Take(MaxUnitTargets).ToList();
        // Playwright only when the repo says how to start and seed the app (D76).
        if (repo.Config.Tickets.App is { Start: not null, Url: not null } app)
            unit.Add(new TestTarget("playwright:" + app.SpecDir, TestTarget.Playwright, "playwright", app.SpecDir.TrimEnd('/'), app.PlaywrightDir?.TrimEnd('/'), ["*.spec.ts", "*.spec.js"], []));
        return unit;
    }

    /// <summary>
    /// The properties the graph build loads projects with (audit item 4): the main solution's folder (projects that
    /// build relative to $(SolutionDir)), NuGet audit off (old commits pin packages with advisories published since)
    /// and Windows-only projects loadable anywhere, each unless <c>tickets.dotnetArgs</c> sets it, then those args.
    /// </summary>
    static string DotnetArgs(StangentRepo repo)
    {
        var own = repo.Config.Tickets.DotnetArgs.Replace("{root}", repo.Root.TrimEnd('/'));
        var props = new List<string>();
        if (!own.Contains("SolutionDir", StringComparison.OrdinalIgnoreCase)
            && Stangent.Graph.Loading.ProjectScanner.MainSolutionDir(repo.Root, Stangent.Graph.Loading.ProjectScanner.Find(repo.Root)) is { } dir)
            props.Add(Quote("-p:SolutionDir=" + dir.TrimEnd('/', '\\') + "/"));
        if (!own.Contains("NuGetAudit", StringComparison.OrdinalIgnoreCase)) props.Add("-p:NuGetAudit=false");
        if (!own.Contains("EnableWindowsTargeting", StringComparison.OrdinalIgnoreCase)) props.Add("-p:EnableWindowsTargeting=true");
        if (own.Length > 0) props.Add(own);
        return string.Join(' ', props);
    }

    static string Quote(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z0-9_./:=@+,-]+$") ? s : "'" + s.Replace("'", "'\\''") + "'";

    [GeneratedRegex(@"""(Microsoft\.NET\.Test\.Sdk|MSTest\.Sdk|MSTest\.TestAdapter|NUnit3TestAdapter|xunit\.runner\.visualstudio|MSTest|xunit\.v3|xunit\.v3\.mtp-v\d+|TUnit)""|""IsTestProject"":\s*""true""", RegexOptions.IgnoreCase)]
    private static partial Regex TestRunner();

    [GeneratedRegex(@"""IsTestProject"":\s*""false""", RegexOptions.IgnoreCase)]
    private static partial Regex NotTestProject();

    /// <summary>
    /// A project that runs tests: what MSBuild evaluates it to (packages and properties from Directory.Build.props
    /// included, as Sonarr adds its test packages there), with a test runner or <c>IsTestProject</c>. A helper library
    /// that only references NUnit or xUnit has no runner, so it isn't one. When MSBuild can't evaluate it, the project
    /// file's own text decides.
    /// </summary>
    static bool IsTestProject(StangentRepo repo, string project, string args)
    {
        var evaluated = Evaluate(repo, project, args);
        if (evaluated is null)
        {
            Console.Error.WriteLine($"stangent: couldn't evaluate {project} with dotnet msbuild; deciding from the project file's text.");
            return TestSdk().IsMatch(File.ReadAllText(Path.Combine(repo.Root, project)));
        }
        return !NotTestProject().IsMatch(evaluated) && TestRunner().IsMatch(evaluated);
    }

    static string? Evaluate(StangentRepo repo, string project, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "msbuild", project, "-getItem:PackageReference", "-getProperty:IsTestProject", "-nologo" }) psi.ArgumentList.Add(a);
            // Only the properties of tickets.dotnetArgs: switches like -c or --no-restore are dotnet build's, not msbuild's.
            // Only the properties of the proof's args (D94: the graph build's, then tickets.dotnetArgs): switches like -c or
            // --no-restore are dotnet build's, not msbuild's. A quoted one loses its quotes here (no shell in between).
            foreach (var a in System.Text.RegularExpressions.Regex.Matches(args, @"'(?:[^']|'\\'')*'|\S+").Select(m => m.Value)
                         .Select(a => a.StartsWith('\'') && a.EndsWith('\'') && a.Length > 1 ? a[1..^1].Replace("'\\''", "'") : a)
                .Where(a => a.StartsWith("-p:", StringComparison.Ordinal) || a.StartsWith("-property:", StringComparison.Ordinal))) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromSeconds(60))) { p.Kill(entireProcessTree: true); return null; }
            return p.ExitCode == 0 && stdout.Wait(TimeSpan.FromSeconds(10)) ? stdout.Result : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    static IEnumerable<string> Files(string root, string pattern)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var f in Directory.EnumerateFiles(dir, pattern)) yield return Path.GetRelativePath(root, f).Replace('\\', '/');
            foreach (var d in Directory.EnumerateDirectories(dir).Where(d => !Skip.Contains(Path.GetFileName(d))).Order(StringComparer.Ordinal))
                stack.Push(d);
        }
    }

    static List<string> References(string root, string project)
    {
        var dir = Path.GetDirectoryName(Path.Combine(root, project))!;
        return [.. ProjectReference().Matches(File.ReadAllText(Path.Combine(root, project)))
            .Select(m => Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(dir, m.Groups[1].Value.Replace('\\', '/')))).Replace('\\', '/'))];
    }
}
