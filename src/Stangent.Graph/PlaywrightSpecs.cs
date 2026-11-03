using System.Text;
using System.Text.RegularExpressions;

namespace Stangent.Graph;

/// <summary>A <c>goto</c> call in a spec. <see cref="Path"/> is its URL when that's one whole string; null when it's built at runtime.</summary>
public sealed record SpecGoto(int Line, string Receiver, string? Path);

/// <summary>
/// Reading a repo's Playwright specs, for bug proof (D86) and the graph (D115): the one place that finds spec files, reads their
/// <c>goto</c> calls and matches a path to a route.
/// </summary>
public static partial class PlaywrightSpecs
{
    static readonly string[] Skip = ["node_modules", "test-results", "playwright-report", ".git", ".claude", ".stangent", "bin", "obj"];

    [GeneratedRegex(@"\.goto\(\s*['""`]([^'""`$]+)['""`]")]
    private static partial Regex GotoPath();

    [GeneratedRegex(@"\??\.\s*goto\s*\(")]
    private static partial Regex GotoCall();

    /// <summary>Spec files under a folder, by the name bug proof has always looked for: <c>*.spec.ts</c> and <c>*.spec.js</c>.</summary>
    public static IEnumerable<string> Files(string dir) => Files(dir, name => Regex.IsMatch(name, @"\.spec\.(ts|js)$"));

    /// <summary>Files under a folder whose name passes, as full paths. Not into links (another checkout, a loop) or tooling folders.</summary>
    public static IEnumerable<string> Files(string dir, Func<string, bool> name)
    {
        var stack = new Stack<string>([dir]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            foreach (var f in Directory.EnumerateFiles(d))
                if (name(Path.GetFileName(f))) yield return Path.GetFullPath(f);
            foreach (var sub in Directory.EnumerateDirectories(d)
                         .Where(s => !Skip.Contains(Path.GetFileName(s)) && !new DirectoryInfo(s).Attributes.HasFlag(FileAttributes.ReparsePoint)))
                stack.Push(sub);
        }
    }

    /// <summary>The paths bug proof reads: any <c>.goto(</c> starting with a string, on any receiver, comments included.</summary>
    public static IEnumerable<string> LiteralGotos(string text) => GotoPath().Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>A spec's <c>goto</c> path against a screen's route: the same segments, a route's <c>:param</c> or <c>*</c> matching any.</summary>
    public static bool RouteMatches(string route, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var abs) && abs.Scheme.StartsWith("http", StringComparison.Ordinal)) path = abs.AbsolutePath;
        path = path.Split('?', '#')[0].TrimEnd('/');
        route = route.TrimEnd('/');
        var a = route.Split('/');
        var b = path.Split('/');
        return a.Length == b.Length && a.Zip(b).All(x => x.First.StartsWith(':') || x.First == "*" || string.Equals(x.First, x.Second, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every real <c>goto</c> call, for the graph, which has to say what it can't read: none in a comment or a string, each with its
    /// receiver, and a URL only when the whole argument is one string (<c>"/a" + id</c> has none).
    /// </summary>
    public static IReadOnlyList<SpecGoto> Gotos(string text)
    {
        var (code, bare) = Mask(text);
        var found = new List<SpecGoto>();
        foreach (var call in GotoCall().Matches(bare).Cast<Match>())
        {
            var line = 1 + bare.AsSpan(0, call.Index).Count('\n');
            found.Add(new SpecGoto(line, Receiver(bare, call.Index), WholeString(code, call.Index + call.Length)));
        }
        return found;
    }

    /// <summary>The name just before the dot: <c>page</c> in <c>this.page.goto</c>. Empty for anything else, like <c>(await open()).goto</c>.</summary>
    static string Receiver(string bare, int dot)
    {
        var end = dot;
        while (end > 0 && char.IsWhiteSpace(bare[end - 1])) end--;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(bare[start - 1]) || bare[start - 1] is '_' or '$')) start--;
        return bare[start..end];
    }

    static string? WholeString(string code, int at)
    {
        while (at < code.Length && char.IsWhiteSpace(code[at])) at++;
        if (at >= code.Length || code[at] is not ('\'' or '"' or '`')) return null;
        var quote = code[at];
        var close = at + 1;
        while (close < code.Length && code[close] != quote)
        {
            if (code[close] == '\\') return null;
            close++;
        }
        if (close >= code.Length) return null;
        var value = code[(at + 1)..close];
        if (quote == '`' && value.Contains("${", StringComparison.Ordinal)) return null;
        var next = close + 1;
        while (next < code.Length && char.IsWhiteSpace(code[next])) next++;
        return next < code.Length && code[next] is ',' or ')' ? value : null;
    }

    /// <summary>
    /// The text with comments blanked, and again with strings blanked too. Same length and same newlines, so places carry over. A
    /// <c>'</c> or <c>"</c> string ends at its line, so a stray quote in a regex can't swallow the rest of the file.
    /// </summary>
    static (string Code, string Bare) Mask(string text)
    {
        var code = new StringBuilder(text);
        var bare = new StringBuilder(text);
        void Blank(int i, bool comment)
        {
            if (text[i] == '\n') return;
            bare[i] = ' ';
            if (comment) code[i] = ' ';
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                for (; i < text.Length && text[i] != '\n'; i++) Blank(i, true);
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                for (; i < end; i++) Blank(i, true);
                i--;
            }
            else if (c is '\'' or '"' or '`')
            {
                Blank(i, false);
                for (i++; i < text.Length && text[i] != c && (c == '`' || text[i] != '\n'); i++)
                {
                    Blank(i, false);
                    if (text[i] == '\\' && i + 1 < text.Length) Blank(++i, false);
                }
                if (i < text.Length) Blank(i, false);
            }
        }
        return (code.ToString(), bare.ToString());
    }
}
