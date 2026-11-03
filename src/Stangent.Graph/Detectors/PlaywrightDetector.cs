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
        var frontends = ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Project && Path.GetFileName(n.Id) is "tsconfig.json" or "tsconfig.app.json").Select(n => n.Id)
            .Concat(pages.Select(p => p.Project).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        foreach (var full in PlaywrightSpecs.Files(ctx.Root, name => TestName().IsMatch(name), dir => BlindFolder(ctx, listed, dir)).Order(StringComparer.Ordinal))
        {
            var spec = Path.GetRelativePath(ctx.Root, full).Replace('\\', '/');
            if (listed?.Contains(spec) == false) continue; // built output or anything else git ignores
            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ctx.AddBlindSpot("browser-test:" + spec, spec, 1, "possible playwright spec: the file can't be read (a link with no target, or no permission), so the screens it opens aren't known", Detector);
                continue;
            }
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
                var (screens, none) = Screens(pages, frontends, spec, Local(call.Path!), call.Path!);
                if (none is not null) ctx.AddBlindSpot(owner, spec, call.Line, none, Detector);
                foreach (var page in screens) ctx.Graph.ScreenTests.Add(new ScreenTest(spec, call.Line, call.Path!, page.Id));
            }
        }
    }

    /// <summary>A folder that can't be read is a blind spot only if git says something in it counts.</summary>
    static void BlindFolder(LinkContext ctx, HashSet<string>? listed, string full)
    {
        var dir = Path.GetRelativePath(ctx.Root, full).Replace('\\', '/');
        if (listed?.Any(f => f.StartsWith(dir + "/", StringComparison.Ordinal)) == false) return;
        ctx.AddBlindSpot("browser-test:" + dir, dir, 1, "possible playwright specs: this folder can't be read, so any specs in it aren't known", Detector);
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
        if (local.Split('?', '#')[0].Contains('%')) return $"playwright goto: {path} has a percent-encoded path, which a router decodes and this read doesn't";
        return local.StartsWith('/') ? null : $"playwright goto: {path} is relative to a base URL the spec doesn't show";
    }

    static bool IsLocal(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http", StringComparison.Ordinal)
        && (u.IsLoopback || u.Host == "0.0.0.0");

    static string Local(string path) => HasScheme().IsMatch(path) && Uri.TryCreate(path, UriKind.Absolute, out var u) ? u.PathAndQuery + u.Fragment : path;

    /// <summary>
    /// The screens a path opens, or why none. A spec inside a frontend links only to that frontend: the nearest around it that has
    /// screens. A spec outside every frontend links only when exactly one frontend matches. Ports aren't read, so the app can't be
    /// told any other way. Within a frontend a route with fewer <c>:param</c> and <c>*</c> parts beats one with more, as routers
    /// do: <c>/settings</c> isn't also <c>/:id</c>.
    /// </summary>
    static (List<Node> Screens, string? None) Screens(List<Node> pages, List<string> frontends, string spec, string path, string written)
    {
        var matches = pages.Where(p => PlaywrightSpecs.RouteMatches(p.Name, path)).ToList();
        var noScreen = $"playwright goto: no screen on the map has the route {written}";
        var around = frontends.Where(f => Contains(f, spec)).OrderByDescending(f => Dir(f).Length).ToList();
        if (around.Count > 0)
        {
            if (matches.Count == 0) return ([], noScreen);
            var own = around.FirstOrDefault(f => pages.Any(p => p.Project == f));
            var mine = matches.Where(p => p.Project == own).ToList();
            return mine.Count > 0 ? (Fewest(mine), null)
                : ([], $"playwright goto: {written} matches only another frontend's screen, and this spec sits in {Dir(own ?? around[0])}");
        }
        if (matches.Count == 0) return ([], noScreen);
        var apps = matches.Select(p => p.Project ?? "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return apps.Count == 1 ? (Fewest(matches), null)
            : ([], $"playwright goto: {written} matches screens in {apps.Count} frontends ({string.Join(", ", apps.Select(Dir))}), and this spec sits in none of them");
    }

    static List<Node> Fewest(List<Node> matches)
    {
        var fewest = matches.Min(Wild);
        return [.. matches.Where(p => Wild(p) == fewest).OrderBy(p => p.Id, StringComparer.Ordinal)];
    }

    static int Wild(Node page) => page.Name.Split('/').Count(s => s.StartsWith(':') || s == "*");

    /// <summary>A frontend's folder from its project id; empty at the repo's root.</summary>
    static string Dir(string project) => Path.GetDirectoryName(project.Replace("project:", "", StringComparison.Ordinal))?.Replace('\\', '/') ?? "";

    static bool Contains(string project, string spec)
    {
        var dir = Dir(project);
        return dir.Length == 0 || spec.StartsWith(dir + "/", StringComparison.Ordinal);
    }

    static bool HasConfigAbove(string root, string file)
    {
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (var dir = Path.GetDirectoryName(file); dir is not null && dir.Length >= top.Length; dir = Path.GetDirectoryName(dir))
            if (Directory.EnumerateFiles(dir, "playwright.config.*").Any()) return true;
        return false;
    }
}
