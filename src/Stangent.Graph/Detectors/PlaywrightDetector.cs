using System.Text.RegularExpressions;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Existing Playwright specs (D115): a <c>page.goto</c> whose whole URL is a string and matches a screen's route is a 🟡
/// <c>opens-screen</c> line, stored apart and never walked. Anything else is ❓, with why. Routes are never guessed.
/// </summary>
public sealed partial class PlaywrightDetector : IDetector
{
    public const string Detector = "playwright-specs";

    /// <summary>What Playwright's own default test match finds.</summary>
    [GeneratedRegex(@"\.(spec|test)\.(ts|tsx|js|jsx|mts|mjs|cts|cjs)$")]
    private static partial Regex TestName();

    [GeneratedRegex(@"(from\s+|require\(\s*|import\s+)['""](@playwright/[\w-]+|playwright(/[\w-]+)?)['""]")]
    private static partial Regex PlaywrightImport();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:")]
    private static partial Regex HasScheme();

    public string Name => Detector;

    public void Extract(ExtractContext ctx) { }

    public void Link(LinkContext ctx)
    {
        if (ctx.Root.Length == 0) return;
        var pages = ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Page).DistinctBy(n => n.Id).ToList();
        var listed = Git.Run(ctx.Root, "ls-files --cached --others --exclude-standard -z")?.Split('\0').ToHashSet(StringComparer.Ordinal);
        foreach (var full in PlaywrightSpecs.Files(ctx.Root, name => TestName().IsMatch(name)).Order(StringComparer.Ordinal))
        {
            var spec = Path.GetRelativePath(ctx.Root, full).Replace('\\', '/');
            if (listed?.Contains(spec) == false) continue; // built output or anything else git ignores
            var text = File.ReadAllText(full);
            var imports = PlaywrightImport().IsMatch(text);
            if (!imports && !HasConfigAbove(ctx.Root, full)) continue; // another runner's test
            var gotos = PlaywrightSpecs.Gotos(text);
            var owner = "browser-test:" + spec;
            // A plain unit test beside a config has no goto and says nothing; one that imports Playwright is a browser test.
            if (gotos.Count == 0 && imports) ctx.AddBlindSpot(owner, spec, 1, "playwright spec: no goto, so the screen it opens isn't known", Detector);
            foreach (var call in gotos)
            {
                if (Unreadable(call) is { } why)
                {
                    ctx.AddBlindSpot(owner, spec, call.Line, why, Detector);
                    continue;
                }
                var screens = Screens(pages, spec, Local(call.Path!));
                if (screens.Count == 0)
                    ctx.AddBlindSpot(owner, spec, call.Line, $"playwright goto: no screen on the map has the route {call.Path}", Detector);
                foreach (var page in screens) ctx.Graph.ScreenTests.Add(new ScreenTest(spec, call.Line, call.Path!, page.Id));
            }
        }
    }

    /// <summary>Why a call can't be matched to a route, or null when its URL can be.</summary>
    static string? Unreadable(SpecGoto call)
    {
        if (call.Receiver != "page")
            return $"playwright goto: on {(call.Receiver.Length == 0 ? "an expression" : call.Receiver)}, not a plain page, so it may be a page object and its URL isn't read";
        if (call.Path is not { } path) return "playwright goto: a built URL";
        if (HasScheme().IsMatch(path) && !IsLocal(path)) return $"playwright goto: {path} isn't the app's own address";
        var local = Local(path);
        if (local.Contains("#/", StringComparison.Ordinal) || local.Contains("#!/", StringComparison.Ordinal))
            return $"playwright goto: {path} is a hash route";
        return local.StartsWith('/') ? null : $"playwright goto: {path} is relative to a base URL the spec doesn't show";
    }

    static bool IsLocal(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http", StringComparison.Ordinal)
        && (u.IsLoopback || u.Host == "0.0.0.0");

    static string Local(string path) => HasScheme().IsMatch(path) && Uri.TryCreate(path, UriKind.Absolute, out var u) ? u.PathAndQuery + u.Fragment : path;

    /// <summary>
    /// The screens a path opens: the spec's own frontend's if it sits inside one with a match, else any frontend's. A route with
    /// fewer <c>:param</c> and <c>*</c> parts beats one with more, as routers do: <c>/settings</c> isn't also <c>/:id</c>.
    /// </summary>
    static List<Node> Screens(List<Node> pages, string spec, string path)
    {
        var matches = pages.Where(p => PlaywrightSpecs.RouteMatches(p.Name, path)).ToList();
        var own = matches.Where(p => InFrontend(p, spec)).ToList();
        if (own.Count > 0) matches = own;
        if (matches.Count == 0) return matches;
        var fewest = matches.Min(Wild);
        return [.. matches.Where(p => Wild(p) == fewest).OrderBy(p => p.Id, StringComparer.Ordinal)];
    }

    static int Wild(Node page) => page.Name.Split('/').Count(s => s.StartsWith(':') || s == "*");

    static bool InFrontend(Node page, string spec)
    {
        var config = page.Project?.Replace("project:", "", StringComparison.Ordinal) ?? "";
        var dir = Path.GetDirectoryName(config)?.Replace('\\', '/') ?? "";
        return dir.Length > 0 && spec.StartsWith(dir + "/", StringComparison.Ordinal);
    }

    static bool HasConfigAbove(string root, string file)
    {
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (var dir = Path.GetDirectoryName(file); dir is not null && dir.Length >= top.Length; dir = Path.GetDirectoryName(dir))
            if (Directory.EnumerateFiles(dir, "playwright.config.*").Any()) return true;
        return false;
    }
}
