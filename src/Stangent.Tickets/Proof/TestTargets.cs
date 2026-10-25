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
            : $"dotnet test --project {Quote(Project!)}{(name is null ? "" : $" --filter-method {Quote(name)}")} -- --report-trx --report-trx-filename {Quote(Path.GetFileName(report))} --results-directory {Quote(Path.GetDirectoryName(report)!)}",
        "vitest" => $"npx vitest run{(file is null ? "" : " " + Quote(file))}{(name is null ? "" : $" -t {Quote(Anchored(name))}")} --reporter=json --outputFile={Quote(report)}",
        "jest" => $"npx jest{(file is null ? "" : " " + Quote(file))}{(name is null ? "" : $" -t {Quote(Anchored(name))}")} --json --outputFile={Quote(report)}",
        "playwright" => $"npx playwright test{(file is null ? "" : " " + Quote(file))}{(name is null ? "" : $" -g {Quote(RegexEscape(name))}")} --retries=0 --reporter=json",
        _ => throw new StangentException("usage", $"Unknown runner {Runner}."),
    };

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

    /// <summary>The edit rules for the driver (D60): each file pattern under the target's folder.</summary>
    public IEnumerable<string> EditRules() => Patterns.Select(p => AtRoot ? $"Edit(**/{p})" : $"Edit({Dir.TrimEnd('/')}/**/{p})");

    /// <summary>VSTest's --filter treats <c>\ ( ) &amp; | = ! ~</c> as syntax; a backslash makes them literal.</summary>
    static string FilterEscape(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"[\\()&|=!~]", m => "\\" + m.Value);

    /// <summary>Jest's -t, Vitest's -t and Playwright's -g are regular expressions: a title is matched literally.</summary>
    static string RegexEscape(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"[\\^$.*+?()\[\]{}|/]", m => "\\" + m.Value);

    static string Anchored(string name) => RegexEscape(name) + "$";

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
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

        // .NET: test projects referencing a starting point's project, directly or through one project in between.
        var csprojs = Files(root, "*.csproj").ToList();
        var refs = csprojs.ToDictionary(p => p, p => References(root, p), StringComparer.Ordinal);
        foreach (var test in csprojs.Where(p => TestSdk().IsMatch(File.ReadAllText(Path.Combine(root, p)))))
        {
            var reached = new HashSet<string>(refs[test], StringComparer.Ordinal);
            foreach (var r in refs[test].ToList()) reached.UnionWith(refs.GetValueOrDefault(r) ?? []);
            var covers = projects.Where(reached.Contains).Order(StringComparer.Ordinal).ToList();
            if (covers.Count == 0) continue;
            var dir = Path.GetDirectoryName(test)!.Replace('\\', '/') is { Length: > 0 } d ? d : ".";
            targets.Add(new TestTarget("dotnet:" + test, TestTarget.Unit, "dotnet", dir, test, ["*.cs"], covers)
            {
                Args = repo.Config.Tickets.DotnetArgs.Replace("{root}", root.TrimEnd('/')), Filter = repo.Config.Tickets.TestFilter,
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

        var unit = targets.OrderByDescending(t => t.Covers.Count).ThenBy(t => t.Id, StringComparer.Ordinal).Take(MaxUnitTargets).ToList();
        // Playwright only when the repo says how to start and seed the app (D76).
        if (repo.Config.Tickets.App is { Start: not null, Url: not null } app)
            unit.Add(new TestTarget("playwright:" + app.SpecDir, TestTarget.Playwright, "playwright", app.SpecDir.TrimEnd('/'), app.PlaywrightDir?.TrimEnd('/'), ["*.spec.ts", "*.spec.js"], []));
        return unit;
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
