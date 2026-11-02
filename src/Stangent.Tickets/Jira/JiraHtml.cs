using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Stangent.Tickets.Jira;

/// <summary>
/// Turns a body Jira sent as HTML (it does when an issue holds what Markdown can't) into the Markdown it would otherwise
/// have sent, in its shape but without its backslash escapes (D102), so a ticket reads the same whichever format Jira
/// picked: links <c>[text](url)</c>, images
/// <c>![alt](src)</c>, numbered lists, task items <c>- [x]</c>, code in backticks or fences with their language, quotes,
/// headings, bold/italic/strike, rules and tables. Jira's media blocks become <c>[attachment: name]</c>. Unknown tags
/// keep their text. Jira's HTML is a small, known set of tags, so a tolerant tag reader is enough (no HTML library).
/// </summary>
public static partial class JiraHtml
{
    sealed class El(string tag, Dictionary<string, string> attrs)
    {
        public string Tag { get; } = tag;
        public Dictionary<string, string> Attrs { get; } = attrs;
        public List<object> Children { get; } = []; // El or string (raw, not yet decoded)
        public string? Attr(string name) => Attrs.TryGetValue(name, out var v) ? v : null;
    }

    static readonly HashSet<string> Void = new(StringComparer.Ordinal) { "br", "hr", "img", "input", "wbr", "col", "area", "source", "meta", "link" };

    [GeneratedRegex(@"<!--.*?-->|<(?<close>/?)(?<tag>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:[^<>""']|""[^""]*""|'[^']*')*)>|[^<]+|<", RegexOptions.Singleline)]
    private static partial Regex Token();

    [GeneratedRegex(@"(?<name>[a-zA-Z_:][\w:.-]*)(?:\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'>]+)))?")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"(?:^|[\s""'])/\s*$")]
    private static partial Regex SelfClosing();

    public static string ToMarkdown(string html)
    {
        // The two private-use marks bracket code blocks below; any in the ticket itself are invisible and dropped first.
        var root = Parse(html.Replace(Open.ToString(), "", StringComparison.Ordinal).Replace(Close.ToString(), "", StringComparison.Ordinal));
        return Normalize(Children(root, inPre: false)).Replace(Open.ToString(), "", StringComparison.Ordinal).Replace(Close.ToString(), "", StringComparison.Ordinal);
    }

    static El Parse(string html)
    {
        var root = new El("", []);
        var stack = new Stack<El>([root]);
        foreach (Match m in Token().Matches(html))
        {
            if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
            if (!m.Groups["tag"].Success) { stack.Peek().Children.Add(m.Value); continue; }
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            if (m.Groups["close"].Value == "/")
            {
                // Close the nearest open element with this tag; a stray closing tag is ignored.
                if (stack.Any(e => e.Tag == tag))
                    while (stack.Count > 1 && stack.Pop().Tag != tag) { }
                continue;
            }
            var raw = m.Groups["attrs"].Value;
            var selfClosing = SelfClosing().IsMatch(raw); // "<br/>", "<img src=\"x\" />"; not "href=http://x/"
            if (selfClosing) raw = raw.TrimEnd()[..^1];
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in Attribute().Matches(raw))
                attrs[a.Groups["name"].Value] = WebUtility.HtmlDecode(a.Groups["v"].Success ? a.Groups["v"].Value : "");
            var el = new El(tag, attrs);
            stack.Peek().Children.Add(el);
            if (!Void.Contains(tag) && !selfClosing) stack.Push(el);
        }
        return root;
    }

    static string Children(El e, bool inPre) => string.Concat(e.Children.Select(c => c is El child ? Render(child, inPre) : Text((string)c, inPre)));

    static string Text(string raw, bool inPre) => WebUtility.HtmlDecode(raw);

    static string Block(string s) => "\n\n" + s + "\n\n";

    static string Render(El e, bool inPre)
    {
        if (inPre) return e.Tag == "br" ? "\n" : Children(e, inPre: true);
        switch (e.Tag)
        {
            case "p": case "section": case "article": case "figure":
                return Block(Children(e, false));
            case "div":
                return e.Attr("data-type") == "media"
                    ? Block($"[attachment: {Normalize(Children(e, false))}]")
                    : Block(Children(e, false));
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                return Block(new string('#', e.Tag[1] - '0') + " " + Normalize(Children(e, false)).Replace('\n', ' '));
            case "em": case "i": return Wrap("_", Children(e, false));
            case "strong": case "b": return Wrap("**", Children(e, false));
            case "s": case "del": case "strike": return Wrap("~~", Children(e, false));
            case "code": return Code(Children(e, inPre: true));
            case "pre": return Pre(e);
            case "a": return Link(e);
            case "img": return string.IsNullOrEmpty(e.Attr("src")) ? e.Attr("alt") ?? "" : $"![{e.Attr("alt") ?? ""}]({Url(e.Attr("src")!)})";
            case "br": return "\n";
            case "hr": return Block("---");
            case "blockquote":
                return Block(string.Join('\n', Normalize(Children(e, false)).Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l)));
            case "ul": case "ol": return Block(List(e));
            case "input":
                return e.Attr("type")?.Equals("checkbox", StringComparison.OrdinalIgnoreCase) == true ? (e.Attrs.ContainsKey("checked") ? "[x] " : "[ ] ") : "";
            case "table": return Block(Table(e));
            case "script": case "style": return "";
            default: return Children(e, false);
        }
    }

    /// <summary>A mark around text, with the text's outer spaces kept outside it ("_ x_" isn't Markdown).</summary>
    static string Wrap(string mark, string s)
    {
        var core = s.Trim();
        if (core.Length == 0) return s;
        return s[..(s.Length - s.TrimStart().Length)] + mark + core + mark + s[s.TrimEnd().Length..];
    }

    static int LongestRun(string s, char c)
    {
        int best = 0, run = 0;
        foreach (var ch in s) { run = ch == c ? run + 1 : 0; best = Math.Max(best, run); }
        return best;
    }

    static string Code(string s)
    {
        if (s.Length == 0) return "";
        var fence = new string('`', LongestRun(s, '`') + 1);
        var pad = s.StartsWith('`') || s.EndsWith('`') ? " " : "";
        return fence + pad + s + pad + fence;
    }

    static string Pre(El e)
    {
        var code = e.Children.OfType<El>().FirstOrDefault(c => c.Tag == "code");
        var lang = code?.Attr("class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(c => c.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..] ?? "";
        var text = Children(e, inPre: true).Replace("\r\n", "\n").TrimEnd('\n');
        if (text.StartsWith('\n')) text = text[1..]; // HTML drops a line break right after <pre>
        var fence = new string('`', Math.Max(3, LongestRun(text, '`') + 1));
        return Block(Protect(fence + lang + "\n" + text + "\n" + fence));
    }

    static string Link(El e)
    {
        var href = e.Attr("href") ?? "";
        var text = Normalize(Children(e, false)).Replace('\n', ' ');
        if (href.Length == 0) return text;
        if (text.Length == 0 || text == href) return href;
        return $"[{text}]({Url(href)})";
    }

    static string Url(string u) => u.Contains(' ') || u.Contains(')') || u.Contains('(') ? $"<{u}>" : u;

    static string List(El e)
    {
        var ordered = e.Tag == "ol";
        var n = ordered && int.TryParse(e.Attr("start"), out var start) ? start : 1;
        var lines = new List<string>();
        var indent = "";
        foreach (var child in e.Children)
        {
            if (child is El li && li.Tag == "li")
            {
                var marker = ordered ? $"{n++}. " : "- ";
                var parts = TaskTick().Replace(Normalize(Children(li, false)), "[$1] ", 1).Split('\n'); // "[x]  text" → "[x] text"
                lines.Add(marker + parts[0]);
                indent = new string(' ', marker.Length);
                lines.AddRange(parts.Skip(1).Select(l => l.Length == 0 ? "" : indent + l));
                continue;
            }
            // Anything else in a list (a nested list, as Jira's task lists nest; stray text) is kept, under the item before it.
            var other = Normalize(child is El el ? Render(el, false) : Text((string)child, false));
            if (other.Length == 0) continue;
            lines.AddRange(other.Split('\n').Select(l => l.Length == 0 ? "" : indent + l));
        }
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"^\[([ x])\][ \t]+")]
    private static partial Regex TaskTick();

    static string Table(El e)
    {
        // Its own rows only (a table in a cell renders inside that cell); a caption goes above.
        var rows = e.Children.OfType<El>().SelectMany(c => c.Tag == "tr" ? [c] : c.Tag is "thead" or "tbody" or "tfoot" ? c.Children.OfType<El>().Where(r => r.Tag == "tr") : []).ToList();
        var sb = new StringBuilder();
        foreach (var caption in e.Children.OfType<El>().Where(c => c.Tag == "caption"))
            sb.Append(Normalize(Children(caption, false))).Append("\n\n");
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Children.OfType<El>().Where(c => c.Tag is "td" or "th").ToList();
            sb.Append("| ").Append(string.Join(" | ", cells.Select(c => Normalize(Children(c, false)).Replace('\n', ' ').Replace("|", "\\|", StringComparison.Ordinal)))).Append(" |\n");
            if (i == 0) sb.Append('|').Append(string.Concat(cells.Select(_ => " --- |"))).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    // A code block is bracketed by two private-use marks so tidying skips it (blank lines in code stay); it stays in place,
    // so list indentation and quote marks still reach its lines. ToMarkdown removes the marks.
    const char Open = '\uE000', Close = '\uE001';
    static string Protect(string block) => Open + block + Close;

    [GeneratedRegex("(\uE000.*?\uE001)", RegexOptions.Singleline)]
    private static partial Regex Protected();

    /// <summary>Line ends to \n, no spaces before a line end, at most one blank line, trimmed; code blocks untouched.</summary>
    static string Normalize(string s)
    {
        var parts = Protected().Split(s.Replace("\r\n", "\n"));
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith(Open)) continue;
            parts[i] = Regex.Replace(Regex.Replace(parts[i], @"[ \t]+\n", "\n"), @"\n{3,}", "\n\n");
        }
        return string.Concat(parts).Trim();
    }
}
