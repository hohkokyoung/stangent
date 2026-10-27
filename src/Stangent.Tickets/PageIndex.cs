using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Tickets;

/// <summary>
/// Words, normalised the same way on both sides of a match (D87): camelCase and snake_case split, lower case, plural and
/// "-ing" folded, stop words, single letters and bare numbers dropped. Order is kept, so phrases can be matched.
/// </summary>
public static partial class Terms
{
    static readonly HashSet<string> Stop =
    [
        "a", "about", "above", "after", "again", "all", "also", "am", "an", "and", "any", "are", "as", "at", "be", "because", "been",
        "before", "being", "below", "between", "both", "but", "by", "can", "cannot", "could", "did", "didn", "do", "doe", "doesn", "don",
        "down", "during", "each", "even", "ever", "few", "for", "from", "further", "get", "got", "had", "has", "have", "having", "he",
        "her", "here", "him", "his", "how", "however", "if", "in", "into", "is", "isn", "it", "its", "just", "let", "like", "may",
        "me", "might", "more", "most", "much", "must", "my", "no", "nor", "not", "now", "of", "off", "on", "once", "one", "only", "or",
        "other", "our", "out", "over", "own", "same", "she", "should", "so", "some", "such", "than", "that", "the", "their", "them",
        "then", "there", "these", "they", "thi", "this", "those", "through", "to", "too", "under", "until", "up", "us", "use", "very",
        "via", "wa", "was", "wasn", "we", "were", "weren", "what", "when", "where", "which", "while", "who", "why", "will", "with",
        "won", "would", "yet", "you", "your", "still", "seem", "seems", "though", "since", "etc", "eg", "ie", "ll", "ve", "re",
        // Words every page and every bug report shares.
        "page", "screen", "api", "v1", "v2", "v3", "v4", "v5", "http", "https", "www", "com",
    ];

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelBreak();

    [GeneratedRegex(@"[A-Za-z0-9]+")]
    private static partial Regex Word();

    /// <summary>A term, where it sits in the text it came from (for quoting the ticket's own words), and its segment: the
    /// run of words between punctuation (<c>: , ; ( )</c>, a full stop before a space) a phrase can't cross.</summary>
    public readonly record struct Token(string Term, int Start, int End, int Segment);

    public static string[] Of(string? text) => [.. Tokens(text).Select(t => t.Term)];

    public static List<Token> Tokens(string? text)
    {
        var list = new List<Token>();
        if (string.IsNullOrEmpty(text)) return list;
        var segment = 0;
        var last = 0;
        foreach (Match m in Word().Matches(text))
        {
            if (Breaks(text.AsSpan(last, m.Index - last))) segment++;
            last = m.Index + m.Length;
            // Split inside the word (QualityProfileItem → quality profile item) and keep each part's position.
            var offset = 0;
            foreach (var part in CamelBreak().Split(m.Value))
            {
                var at = m.Index + offset;
                offset += part.Length;
                if (part.Length < 2 || part.All(char.IsAsciiDigit)) continue;
                var term = Fold(part.ToLowerInvariant());
                if (!Stop.Contains(term)) list.Add(new Token(term, at, at + part.Length, segment));
            }
        }
        return list;
    }

    static bool Breaks(ReadOnlySpan<char> gap) =>
        gap.IndexOfAny(",;:!?()[]{}|\"<>=\n") >= 0 || (gap.Contains('.') && gap.IndexOfAny(" \t") >= 0);

    /// <summary>Plural first, then "-ing", so a word and its plural always meet: settings → setting → sett, setting → sett.</summary>
    static string Fold(string w)
    {
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) w = w[..^3] + "y";
        else if (w.Length > 4 && (w.EndsWith("sses", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal)
                                  || w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal))) w = w[..^2];
        else if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal)
                 && !w.EndsWith("is", StringComparison.Ordinal)) w = w[..^1];
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) w = w[..^3];
        return w;
    }
}

/// <summary>Where a piece of a page's indexed text came from (D87).</summary>
public static class IndexSource
{
    public const string Title = "title";
    public const string Route = "route";
    public const string Statement = "statement";
    public const string Fsd = "fsd";
    public const string Name = "name";
    public const string Text = "text";
}

/// <summary>The ranking's knobs (D87): tuned on Sonarr's 40 bugs, then frozen before bitwarden was measured.</summary>
/// <param name="DepthDecay">Text deeper in a slice says less about the page: weight × 1 / (1 + DepthDecay × depth).</param>
/// <param name="PhraseBoost">A phrase's words count this much more than the same words found apart.</param>
/// <param name="WinnerRatio">A clear winner shares a phrase with the ticket and scores at least this many times the runner-up.</param>
/// <param name="SummaryWeight">Summary words count this much more than words in the description and comments.</param>
/// <param name="LengthNorm">0: a page's size doesn't matter; 1: scores are divided by its size against the average.</param>
public sealed record RankSettings(double DepthDecay = 0.35, double PhraseBoost = 2.0, double WinnerRatio = 1.5, double SummaryWeight = 2.0,
    double LengthNorm = 0.1)
{
    public static readonly RankSettings Default = new();
}

/// <summary>One piece of text a page is known by: a title, a statement, a message in the code. Phrases match inside one unit.</summary>
public sealed record IndexUnit(string Source, string Text, string[] Terms, int[] Segments);

/// <summary>A page's line in a ranking: its score, the longest phrase it shares with the ticket and the words it matched.</summary>
public sealed record RankedPage(PageDef Page, double Score, PhraseMatch? Phrase, IReadOnlyList<string> Words);

/// <summary>Ticket words that appear in a row, in this order, inside one unit of the page's text.</summary>
public sealed record PhraseMatch(string Quote, string Where, string[] Terms, IndexUnit Unit);

/// <summary>A ranking of every page for one ticket: never shorter than the page list, best first (D87).</summary>
public sealed record PageRanking(IReadOnlyList<RankedPage> Pages, RankedPage? Winner)
{
    /// <summary>The pages listed after a clear winner: the next code-ranked pages.</summary>
    public IEnumerable<RankedPage> AlsoPossibly => Winner is null ? [] : Pages.Skip(1).Take(PageIndex.AlsoPossible).Where(p => p.Score > 0);
}

/// <summary>
/// The page index (D87), built by code from the graph, the page list, the written pages and the FSD links: for each page
/// its title, its route or endpoint, the names of the code in its slice, its written statements, its linked FSD sections,
/// and the text its code holds (string literals: messages, validation, logs; markup text in TSX/JSX/HTML/Razor; <c>.resx</c>
/// values the code names; the texts of a project's own text files, e.g. <c>translate('Key')</c> → <c>en.json</c>, through <c>tickets.textFiles</c>).
/// <see cref="Rank"/> orders every page for a ticket: shared phrases first, rare words over common. Deterministic.
/// </summary>
public sealed partial class PageIndex
{
    /// <summary>The pages listed as "also possibly" beside a clear winner.</summary>
    public const int AlsoPossible = 4;
    /// <summary>How many pages the point picker sees in detail when there's no clear winner.</summary>
    public const int Detailed = 20;

    /// <summary>How much a source counts, before the slice depth discount.</summary>
    public static readonly IReadOnlyDictionary<string, double> SourceWeight = new Dictionary<string, double>
    {
        [IndexSource.Title] = 3.0, [IndexSource.Route] = 3.0, [IndexSource.Statement] = 1.5, [IndexSource.Fsd] = 1.5,
        [IndexSource.Name] = 2.0, [IndexSource.Text] = 1.5,
    };

    public RankSettings Settings { get; }

    readonly List<IndexUnit> units = [];
    readonly Dictionary<string, int> unitIds = new(StringComparer.Ordinal);
    readonly List<PageDef> pages;
    readonly Dictionary<string, int> pageIds;
    /// <summary>Per page: unit → weight (the best weight that unit has in the page).</summary>
    readonly List<Dictionary<int, double>> pageUnits;
    /// <summary>Unit → the pages it belongs to, with its weight there.</summary>
    readonly List<List<(int Page, double Weight)>> unitPages = [];
    readonly Dictionary<string, int> df = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<int>> unitsByTerm = new(StringComparer.Ordinal);
    /// <summary>Per page: how many distinct terms it holds.</summary>
    double[] length = [];
    double averageLength = 1;

    public IReadOnlyList<PageDef> Pages => pages;
    public int UnitCount => units.Count;

    PageIndex(List<PageDef> pages, RankSettings settings)
    {
        this.pages = pages;
        Settings = settings;
        pageIds = pages.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        pageUnits = [.. pages.Select(_ => new Dictionary<int, double>())];
    }

    /// <summary>The units a page is indexed with, heaviest first (for tests and the picker's detail).</summary>
    public IEnumerable<(IndexUnit Unit, double Weight)> UnitsOf(string pageId) =>
        pageUnits[pageIds[pageId]].OrderByDescending(u => u.Value).ThenBy(u => u.Key).Select(u => (units[u.Key], u.Value));

    double Idf(string term) => df.TryGetValue(term, out var n) ? Math.Log(1 + (pages.Count - n + 0.5) / (n + 0.5)) : 0;

    // ---- building ----

    public static PageIndex Build(TicketContext ctx, string repoRoot, StangentConfig config, RankSettings? settings = null)
    {
        var index = new PageIndex([.. ctx.Set.Pages], settings ?? RankSettings.Default);
        var text = new CodeText(repoRoot, ctx.Set.Source, config.Tickets.TextFiles);
        var fsdByPage = ctx.Trace.Where(e => (string?)e["page"]?["id"] is not null && (string?)e["fsd"]?["doc"] is not null && (string?)e["fsd"]?["section"] is not null)
            .GroupBy(e => (string)e["page"]!["id"]!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => ((string)e["fsd"]!["doc"]!, (string)e["fsd"]!["section"]!)).Distinct().ToList(), StringComparer.Ordinal);

        foreach (var page in index.pages)
        {
            var i = index.pageIds[page.Id];
            foreach (var t in page.Title.Split(" · "))
                index.Add(i, IndexSource.Title, t, 1.0);
            if (ctx.Index.Pages.GetValueOrDefault(page.Id) is { } written)
            {
                if ((string?)written["title"] is { } title) index.Add(i, IndexSource.Title, title, 1.0);
                foreach (var (_, _, s) in PageChecks.Statements(written)) index.Add(i, IndexSource.Statement, (string?)s["text"], 1.0);
            }
            foreach (var key in fsdByPage.GetValueOrDefault(page.Id) ?? [])
                if (ctx.Index.Sections.GetValueOrDefault(key) is { } section)
                {
                    index.Add(i, IndexSource.Fsd, (string?)section["title"], 1.0);
                    foreach (var para in ((string?)section["text"] ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        index.Add(i, IndexSource.Fsd, para, 1.0);
                }
            if (!ctx.Set.Slices.TryGetValue(page.Id, out var slice)) continue;
            foreach (var n in slice.Nodes)
            {
                var near = 1.0 / (1 + index.Settings.DepthDecay * n.Depth);
                if (n.Node.Kind is NodeKind.Endpoint or NodeKind.Page)
                    index.Add(i, IndexSource.Route, Route(n.Node), near);
                if (n.Node.Kind is not (NodeKind.Project or NodeKind.File))
                    index.Add(i, IndexSource.Name, n.Node.Name, near);
                // Only what the page's own walk reached: shared components (stop), handlers and tables are named, not read.
                if (n.Role == "walk")
                    foreach (var s in text.Of(n.Node))
                        index.Add(i, IndexSource.Text, s, near);
            }
            // A server-rendered screen: the views its actions (one line from the endpoint) return.
            if (page.Kind == PageKind.Screen && slice.Nodes.Any(n => n.Depth == 0 && n.Node.Kind == NodeKind.Endpoint))
                foreach (var action in slice.Nodes.Where(n => n.Depth == 1 && n.Node.Kind == NodeKind.Method && n.Role == "walk"))
                    foreach (var s in text.RazorView(action.Node))
                        index.Add(i, IndexSource.Text, s, 1.0);
        }

        // Each page counts a term once, however many units hold it.
        index.length = new double[index.pages.Count];
        for (var p = 0; p < index.pages.Count; p++)
        {
            var terms = index.pageUnits[p].Keys.SelectMany(u => index.units[u].Terms).Distinct(StringComparer.Ordinal).ToList();
            index.length[p] = terms.Count;
            foreach (var term in terms) index.df[term] = index.df.GetValueOrDefault(term) + 1;
        }
        index.averageLength = index.length.Length == 0 ? 1 : Math.Max(1, index.length.Average());
        return index;
    }

    static string Route(Node n) => n.Kind == NodeKind.Endpoint ? PageList.SplitEndpoint(n.Name).Route : n.Name;

    void Add(int page, string source, string? text, double near)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.Length > 400) text = text[..400];
        var key = source + "\u0001" + text;
        if (!unitIds.TryGetValue(key, out var u))
        {
            var tokens = Terms.Tokens(text);
            if (tokens.Count == 0) return;
            var terms = tokens.Select(t => t.Term).ToArray();
            u = units.Count;
            units.Add(new IndexUnit(source, text, terms, [.. tokens.Select(t => t.Segment)]));
            unitIds[key] = u;
            unitPages.Add([]);
            foreach (var t in terms.Distinct(StringComparer.Ordinal))
                (unitsByTerm.TryGetValue(t, out var list) ? list : unitsByTerm[t] = []).Add(u);
        }
        var weight = SourceWeight[source] * near;
        var mine = pageUnits[page];
        if (mine.TryGetValue(u, out var had) && had >= weight) return;
        if (mine.ContainsKey(u)) unitPages[u].RemoveAll(x => x.Page == page);
        mine[u] = weight;
        unitPages[u].Add((page, weight));
    }

    // ---- ranking ----

    /// <summary>
    /// Every page, best first: for each ticket word, its rarity (fewer pages hold it → counts more) × the best weight it has
    /// in the page; plus, for each phrase of 2+ ticket words in a row found in one unit, its words' rarity × the unit's
    /// weight × <see cref="RankSettings.PhraseBoost"/>; divided by the page's size against the average
    /// (<see cref="RankSettings.LengthNorm"/>). Summary words count <see cref="RankSettings.SummaryWeight"/>×. Ties by page id.
    /// A clear winner shares a phrase and scores <see cref="RankSettings.WinnerRatio"/>× the runner-up.
    /// </summary>
    public PageRanking Rank(TicketText ticket)
    {
        var lines = TicketLines(ticket, Settings.SummaryWeight);
        var ask = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var l in lines)
            foreach (var t in l.Tokens)
                ask[t.Term] = Math.Max(ask.GetValueOrDefault(t.Term), l.Weight);

        // Words: the best weight each asked term has in each page.
        var scores = new double[pages.Count];
        var words = new Dictionary<int, SortedSet<string>>();
        foreach (var (term, w) in ask)
        {
            if (!unitsByTerm.TryGetValue(term, out var holding)) continue;
            var idf = Idf(term);
            var best = new Dictionary<int, double>();
            foreach (var u in holding)
                foreach (var (p, weight) in unitPages[u])
                    if (idf * weight > best.GetValueOrDefault(p)) best[p] = idf * weight;
            foreach (var (p, value) in best)
            {
                scores[p] += value * w;
                (words.TryGetValue(p, out var set) ? set : words[p] = new SortedSet<string>(StringComparer.Ordinal)).Add(term);
            }
        }

        // Phrases: 2+ ticket terms in a row, in one unit. Each distinct phrase counts once per page, at its best unit.
        var phraseScore = new Dictionary<(int Page, string Phrase), double>();
        var bestPhrase = new Dictionary<int, (double Score, PhraseMatch Match)>();
        foreach (var (u, match, terms) in Phrases(lines))
        {
            var value = terms.Sum(Idf) * Settings.PhraseBoost * match.Weight;
            var phrase = string.Join(' ', terms);
            foreach (var (p, weight) in unitPages[u])
            {
                var s = value * weight;
                if (s <= phraseScore.GetValueOrDefault((p, phrase))) continue;
                phraseScore[(p, phrase)] = s;
                if (!bestPhrase.TryGetValue(p, out var b) || s > b.Score || (s == b.Score && string.CompareOrdinal(phrase, string.Join(' ', b.Match.Terms)) < 0))
                    bestPhrase[p] = (s, new PhraseMatch(match.Quote, match.Where, terms, units[u]));
            }
        }
        foreach (var ((p, _), s) in phraseScore) scores[p] += s;
        // A page holding many words matches many by chance: scale by its size against the average (BM25's b).
        for (var p = 0; p < scores.Length; p++)
            scores[p] /= 1 - Settings.LengthNorm + Settings.LengthNorm * length[p] / averageLength;

        var ranked = Enumerable.Range(0, pages.Count)
            .Select(p => new RankedPage(pages[p], Math.Round(scores[p], 6), bestPhrase.TryGetValue(p, out var b) ? b.Match : null,
                words.TryGetValue(p, out var set) ? [.. set] : []))
            .OrderByDescending(r => r.Score).ThenBy(r => r.Page.Id, StringComparer.Ordinal).ToList();

        RankedPage? winner = null;
        if (ranked.Count > 0 && ranked[0].Score > 0 && ranked[0].Phrase is not null
            && (ranked.Count == 1 || ranked[0].Score >= Settings.WinnerRatio * ranked[1].Score))
            winner = ranked[0];
        return new PageRanking(ranked, winner);
    }

    sealed record TicketLine(string Where, string Text, List<Terms.Token> Tokens, double Weight);

    /// <summary>The ticket's lines with their terms. Markdown headings and ticked boxes are a template's, not the reporter's.</summary>
    static List<TicketLine> TicketLines(TicketText ticket, double summaryWeight)
    {
        var lines = new List<TicketLine>();
        foreach (var (where, body) in ticket.Prose)
            foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('#') || TemplateBox().IsMatch(line)) continue;
                // A form field in a list ("- Using Reverse Proxy: No"): the key is the template's, the value the reporter's.
                if (FormField().Match(line) is { Success: true } field) line = field.Groups["value"].Value;
                var tokens = Terms.Tokens(line);
                if (tokens.Count > 0) lines.Add(new TicketLine(where, line, tokens, where == "summary" ? summaryWeight : 1.0));
            }
        return lines;
    }

    [GeneratedRegex(@"^[-*]\s*\[[ xX]\]")]
    private static partial Regex TemplateBox();

    [GeneratedRegex(@"^[-*]\s+(?:\*\*)?[A-Za-z][\w .()/-]{0,40}?(?:\*\*)?:\s+(?<value>.*)$")]
    private static partial Regex FormField();

    sealed record LineMatch(string Quote, string Where, double Weight);

    /// <summary>Maximal runs of 2+ terms a ticket line shares, in order, with a unit: one per (unit, run).</summary>
    IEnumerable<(int Unit, LineMatch Match, string[] Terms)> Phrases(List<TicketLine> lines)
    {
        var seen = new HashSet<(int, string)>();
        foreach (var line in lines)
        {
            var t = line.Tokens;
            for (var i = 0; i + 1 < t.Count; i++)
            {
                if (t[i].Segment != t[i + 1].Segment || !unitsByTerm.TryGetValue(t[i].Term, out var first) || !unitsByTerm.ContainsKey(t[i + 1].Term)) continue;
                foreach (var u in first)
                {
                    var ut = units[u].Terms;
                    var us = units[u].Segments;
                    bool Follows(int a, int b) => a + 1 < t.Count && b + 1 < ut.Length && ut[b + 1] == t[a + 1].Term
                        && t[a + 1].Segment == t[a].Segment && us[b + 1] == us[b];
                    for (var j = 0; j + 1 < ut.Length; j++)
                    {
                        if (ut[j] != t[i].Term || !Follows(i, j)) continue;
                        // Only maximal runs: skip a start the previous term would have extended.
                        if (i > 0 && j > 0 && ut[j - 1] == t[i - 1].Term && Follows(i - 1, j - 1)) continue;
                        var k = 2;
                        while (Follows(i + k - 1, j + k - 1)) k++;
                        var terms = t.Skip(i).Take(k).Select(x => x.Term).ToArray();
                        if (!seen.Add((u, string.Join(' ', terms)))) continue;
                        var quote = line.Text[t[i].Start..t[i + k - 1].End];
                        yield return (u, new LineMatch(quote, line.Where, line.Weight), terms);
                    }
                }
            }
        }
    }

    // ---- the text the code holds ----

    /// <summary>
    /// Reads the text a graph point's code holds: string literals (C#, TS/JS), markup text between tags (TSX/JSX, HTML,
    /// Angular templates), the values of <c>.resx</c> entries the code names (<c>Resources.Key</c>), and the texts of the
    /// project's text files for strings that are their keys (<c>translate('Key')</c> → <c>en.json</c>). Types are not read:
    /// their members are.
    /// </summary>
    sealed partial class CodeText
    {
        readonly string root;
        readonly SourceReader source;
        readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
        readonly Dictionary<string, Dictionary<string, string>>? resx;
        readonly Dictionary<string, List<string>> cache = new(StringComparer.Ordinal);

        public CodeText(string root, SourceReader source, List<string> textFiles)
        {
            this.root = root;
            this.source = source;
            foreach (var file in textFiles)
            {
                var path = Path.Combine(root, file);
                if (!File.Exists(path))
                    throw new StangentException("bad_config", $"tickets.textFiles: '{file}' doesn't exist.", ExitCodes.Usage);
                try { foreach (var (k, v) in LoadValues(path)) texts.TryAdd(k, v); }
                catch (Exception e) when (e is JsonException or System.Xml.XmlException)
                { throw new StangentException("bad_config", $"tickets.textFiles: '{file}' isn't valid: {e.Message}", ExitCodes.Usage); }
            }
            resx = LoadResx(root);
            // Resource keys are looked up by string too (localizer["Key"]), so they read like the text files' keys.
            foreach (var entries in resx?.Values ?? Enumerable.Empty<Dictionary<string, string>>())
                foreach (var (k, v) in entries) texts.TryAdd(k, v);
        }

        public List<string> Of(Node n)
        {
            if (n.Kind is NodeKind.Type or NodeKind.Project or NodeKind.File || n.File is null || n.Line <= 0) return [];
            var key = $"{n.File}:{n.Line}:{n.EndLine}";
            if (cache.TryGetValue(key, out var hit)) return hit;
            var code = source.Text(n);
            var found = Read(code, Path.GetExtension(n.File).ToLowerInvariant());
            cache[key] = found;
            return found;
        }

        List<string> Read(string code, string ext)
        {
            var found = new List<string>();
            var markup = ext is ".tsx" or ".jsx" or ".html" or ".htm" or ".cshtml" or ".razor";
            foreach (Match m in (ext is ".cs" or ".cshtml" or ".razor" ? CsString() : ScriptString()).Matches(code))
            {
                var literal = m.Groups["s"].Value;
                // A key into the project's texts reads as its text: translate('Key'), GetLocalizedString("Key"), Label = "Key".
                if (texts.TryGetValue(literal, out var text)) found.Add(text);
                if (Readable(literal) is { } s) found.Add(s);
            }
            if (markup)
                foreach (Match m in MarkupText().Matches(code))
                    if (Readable(m.Groups["t"].Value) is { } s) found.Add(s);
            if (resx is not null)
                foreach (Match m in ResourceRef().Matches(code))
                    if (resx.TryGetValue(m.Groups["c"].Value, out var entries) && entries.TryGetValue(m.Groups["k"].Value, out var v)) found.Add(v);
            return found;
        }

        /// <summary>A literal people might read: holds a word of 2+ letters and isn't a path, a URL template or a format.</summary>
        static string? Readable(string s)
        {
            s = Hole().Replace(s, " ").Trim();
            if (s.Length < 3 || !Letters().IsMatch(s)) return null;
            return s;
        }

        /// <summary>A server-rendered action's Razor view, by convention: <c>Views/&lt;Controller&gt;/&lt;View("Name") or the
        /// action&gt;.cshtml</c>, else <c>Views/Shared/…</c>, in the action's folder or the nearest folder above it that has one.</summary>
        public List<string> RazorView(Node action)
        {
            if (action.File is null || !action.Name.Contains('.')) return [];
            var controller = action.Name.Split('.')[0];
            if (controller.EndsWith("Controller", StringComparison.Ordinal)) controller = controller[..^"Controller".Length];
            var named = ViewName().Match(source.Text(action));
            var view = named.Success ? named.Groups["v"].Value : action.Name.Split('.')[^1];
            for (var dir = Path.GetDirectoryName(action.File); dir is not null; dir = dir.Length == 0 ? null : Path.GetDirectoryName(dir))
                foreach (var candidate in new[] { Path.Combine(dir, "Views", controller, view + ".cshtml"), Path.Combine(dir, "Views", "Shared", view + ".cshtml") })
                    if (File.Exists(Path.Combine(root, candidate)))
                        return Read(File.ReadAllText(Path.Combine(root, candidate)), ".cshtml");
            return [];
        }

        static Dictionary<string, string> LoadValues(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (k, v) in ResxEntries(path)) values[k] = v;
                return values;
            }
            void Walk(JsonNode? node, string prefix)
            {
                if (node is JsonObject o)
                    foreach (var (k, v) in o) Walk(v, prefix.Length == 0 ? k : $"{prefix}.{k}");
                else if (node is JsonValue v && v.TryGetValue<string>(out var s)) values[prefix] = s;
            }
            Walk(JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }), "");
            return values;
        }

        /// <summary>Each resource class's <c>.resx</c>, by class name (the file name before any culture suffix): the neutral file
        /// (<c>Resources.resx</c>), else the English one (<c>SharedResources.en.resx</c>, <c>.en-US</c>). Classes with the same
        /// name in different projects are merged; the first path in ordinal order wins a key both hold.</summary>
        static Dictionary<string, Dictionary<string, string>>? LoadResx(string root)
        {
            var sep = Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(root, "*.resx", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => !f.Contains($"{sep}node_modules{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}.git{sep}", StringComparison.Ordinal)
                            && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
                .Select(f => (Path: f, Name: Path.GetFileNameWithoutExtension(f)))
                .Select(f => (f.Path, Class: f.Name.Split('.')[0], Culture: f.Name.Contains('.') ? f.Name[(f.Name.IndexOf('.') + 1)..] : ""))
                .Where(f => f.Culture is "" or "en" or "en-US" or "en-GB")
                .GroupBy(f => (Path.GetDirectoryName(f.Path), f.Class))
                .Select(g => g.OrderBy(f => f.Culture.Length).ThenBy(f => f.Path, StringComparer.Ordinal).First())
                .OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
            if (files.Count == 0) return null;
            var all = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                var entries = all.TryGetValue(f.Class, out var e) ? e : all[f.Class] = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (k, v) in ResxEntries(f.Path)) entries.TryAdd(k, v);
            }
            return all;
        }

        static IEnumerable<(string Key, string Value)> ResxEntries(string path)
        {
            XDocument doc;
            try { doc = XDocument.Load(path); }
            catch (System.Xml.XmlException) { yield break; }
            foreach (var d in doc.Root?.Elements("data") ?? [])
                if (d.Attribute("name")?.Value is { } k && d.Attribute("type") is null && d.Element("value")?.Value is { } v)
                    yield return (k, v);
        }

        [GeneratedRegex(@"(?<![\w""])(?:\$@|@\$|\$|@)?""(?<s>(?:[^""\\\n]|\\.|"""")*)""", RegexOptions.CultureInvariant)]
        private static partial Regex CsString();

        [GeneratedRegex(@"'(?<s>(?:[^'\\\n]|\\.)*)'|""(?<s>(?:[^""\\\n]|\\.)*)""|`(?<s>(?:[^`\\]|\\.)*)`", RegexOptions.CultureInvariant)]
        private static partial Regex ScriptString();

        [GeneratedRegex(@">(?<t>[^<>{}=;]*[A-Za-z]{2}[^<>{}=;]*)<", RegexOptions.CultureInvariant)]
        private static partial Regex MarkupText();

        [GeneratedRegex(@"\b(?<c>[A-Z]\w*)\.(?<k>[A-Z]\w*)\b", RegexOptions.CultureInvariant)]
        private static partial Regex ResourceRef();

        [GeneratedRegex(@"\b(?:View|PartialView)\s*\(\s*""(?<v>[\w]+)""")]
        private static partial Regex ViewName();

        [GeneratedRegex(@"\{[^{}]*\}|\$\{[^{}]*\}")]
        private static partial Regex Hole();

        [GeneratedRegex(@"[A-Za-z]{2}")]
        private static partial Regex Letters();
    }
}
