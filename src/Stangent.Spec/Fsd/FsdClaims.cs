using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>One claim in a section: its words as written, its place, and the nearest text before it that introduces its steps. <c>Problem</c> says why it isn't clean.</summary>
public sealed record FsdClaim(int Position, string? Flow, string Kind, string Words, string? Problem = null, string? SetAside = null, FsdJoin? Join = null)
{
    /// <summary>What a link judge does with it (D145): <c>claim</c> is judged; anything else is set aside by where it sits, and only given as context.</summary>
    public string JudgeKind => SetAside ?? (Kind is FsdClaims.Kinds.FlowLabel or FsdClaims.Kinds.Figure ? Kind : FsdClaims.JudgeKinds.Claim);
}

/// <summary>A step a page break cut in two, joined (D146). <c>TailAt</c> is where its second part starts in <c>Words</c>; in the section that part sits after the claim at position <c>TailAfter</c>, which may be another claim's.</summary>
public sealed record FsdJoin(int TailAt, int TailAfter);

/// <summary>Where the claims' words first differ from the section's. Null from <see cref="FsdClaims.CheckWords"/> means they match.</summary>
public sealed record FsdWordMismatch(int At, string? Section, string? Claims, int SectionWords, int ClaimWords);

/// <summary>
/// Cuts a section's text into claims, by code with no AI (D143): a sentence, a list item, a table row, or a numbered step inside a table cell. Reads and prints only. Nothing else
/// reads this yet, so no id, hash, bundle or saved file moves. What can't be cut cleanly is kept whole or marked, and listed: code never guesses.
/// </summary>
public static partial class FsdClaims
{
    public const string Component = "fsd-claims";

    public static class Kinds
    {
        public const string Sentence = "sentence", ListItem = "list-item", TableRow = "table-row", Step = "step", FlowLabel = "flow-label", Figure = "figure", Block = "block";
    }

    /// <summary>A claim's <c>kind</c> in <c>claims.json</c> (D145). Everything but <c>claim</c> comes from where the words sit, never what they say.</summary>
    public static class JudgeKinds
    {
        public const string Claim = "claim", ColumnHeading = "column-heading", LabelRow = "label-row";
    }

    /// <summary>Why a claim isn't clean. The first three are what a table broken across a page leaves.</summary>
    public static class Problems
    {
        public const string StartsMidStep = "starts-mid-step", EndsOpen = "ends-open", ContinuesAbove = "continues-above",
            NumbersOutOfOrder = "numbers-out-of-order", MaybeSentenceEnd = "maybe-sentence-end", CodeBlock = "code-block", NoTableRule = "no-table-rule", RowNotClosed = "row-not-closed";
    }

    /// <summary>English abbreviations a capital often follows. Only ever stops a cut, and the sentence is listed, so a word missing here costs one wrong cut at worst and one here costs one listed sentence.</summary>
    static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal) { "al", "Dr", "Eq", "etc", "Fig", "Figs", "Mr", "Mrs", "Ms", "No", "pp", "Prof", "vs", "Vol" };

    const string Closers = "\"'”’)]*_`", Openers = "\"'“‘([*_`";

    [GeneratedRegex(@"^\|(\s*:?-+:?\s*\|)+$")]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"^\s*[-*+]\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*\d+[.)]\s+\S")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"^!\[[^\]]*\]\([^)]*\)$")]
    private static partial Regex FigureLine();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellBar();

    [GeneratedRegex(@"(?<!\S)(\d+)[.)](?=\s+\S)")]
    private static partial Regex StepNumber();

    [GeneratedRegex(@"^(\*\*[^*]+\*\*|__[^_]+__)$")]
    private static partial Regex AllBold();

    // ---- reading the text into blocks ----

    abstract record Block;
    sealed record Paragraph(string Text) : Block;
    sealed record ListItem(string Text) : Block;
    sealed record Whole(string Text, string Kind, string? Problem) : Block;
    sealed record Table(List<Row> Rows) : Block;

    /// <summary>A table row: its non-empty cells with their columns, in order.</summary>
    sealed record Row(string Raw, List<(int Column, string Text)> Cells, bool Closed)
    {
        public List<CellParts>? Steps { get; init; }
        public bool HasSteps => Steps is not null && Steps.Any(c => c.Steps.Count > 0);
        /// <summary>How many values sit outside the first column. A first column often only labels the row.</summary>
        public int Values => Cells.Count(c => c.Column > 0);
        /// <summary>The column its first numbered step sits in.</summary>
        public int StepColumn => Cells[Steps!.FindIndex(c => c.Steps.Count > 0)].Column;
        /// <summary>Numbered steps, and nothing else but a first column to label them: <c>| Name: | 1. … | 2. … |</c>.</summary>
        public bool LabelledByFirst => HasSteps && First is not null && Steps![0].Steps.Count == 0 && !Steps[0].OutOfOrder && Steps.Skip(1).All(c => c.Steps.Count > 0);
        public string? First => Cells.Count > 0 && Cells[0].Column == 0 ? Cells[0].Text : null;
        public string Joined => string.Join(" | ", Cells.Select(c => c.Text));
    }

    /// <summary>A cell, cut. <c>Lead</c> is text before its first number. No <c>Steps</c>: no numbers, or, with <c>OutOfOrder</c>, numbers that couldn't be trusted.</summary>
    sealed record CellParts(string Text, string? Lead, List<string> Steps, bool OutOfOrder);

    static List<Block> Blocks(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<Block>();
        for (var i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) { i++; continue; }
            if (IsFence(line))
            {
                var end = i + 1;
                while (end < lines.Length && !IsFence(lines[end])) end++;
                end = Math.Min(end, lines.Length - 1);
                blocks.Add(new Whole(string.Join('\n', lines[i..(end + 1)]), Kinds.Block, Problems.CodeBlock));
                i = end + 1;
                continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                if (i + 1 >= lines.Length || !RuleLine().IsMatch(lines[i + 1].Trim()))
                {
                    blocks.Add(new Whole(line.Trim(), Kinds.Block, Problems.NoTableRule));
                    i++;
                    continue;
                }
                // Tables with only blank lines between them are read as one, since a table broken across a page comes in like that.
                if (blocks.LastOrDefault() is not Table table) blocks.Add(table = new Table([]));
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                    if (!RuleLine().IsMatch(lines[i].Trim())) table.Rows.Add(ReadRow(lines[i].Trim()));
                continue;
            }
            if (IsListLine(line))
            {
                var item = new StringBuilder(Bullet().Replace(line, "").Trim());
                for (i++; i < lines.Length && lines[i].Trim().Length > 0 && !StartsBlock(lines[i]); i++) item.Append(' ').Append(lines[i].Trim());
                blocks.Add(new ListItem(item.ToString()));
                continue;
            }
            if (FigureLine().IsMatch(line.Trim()))
            {
                blocks.Add(new Whole(line.Trim(), Kinds.Figure, null));
                i++;
                continue;
            }
            var paragraph = new StringBuilder(line.Trim());
            for (i++; i < lines.Length && lines[i].Trim().Length > 0 && !StartsBlock(lines[i]); i++) paragraph.Append(' ').Append(lines[i].Trim());
            blocks.Add(new Paragraph(paragraph.ToString()));
        }
        return blocks;
    }

    static bool IsFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal);

    static bool IsListLine(string line) => Bullet().IsMatch(line) || NumberedLine().IsMatch(line);

    static bool StartsBlock(string line) => IsFence(line) || line.TrimStart().StartsWith('|') || IsListLine(line) || FigureLine().IsMatch(line.Trim());

    static Row ReadRow(string line)
    {
        var closed = line.Length > 1 && CellBar().IsMatch(line, line.Length - 1);
        var inner = line[1..(closed ? line.Length - 1 : line.Length)];
        var cells = CellBar().Split(inner).Select((c, column) => (Column: column, Text: c.Trim())).Where(c => c.Text.Length > 0).ToList();
        return new Row(line, cells, closed) { Steps = closed ? [.. cells.Select(c => Cut(c.Text))] : null };
    }

    // ---- a numbered step inside a cell ----

    /// <summary>
    /// Finds the numbers that start steps. One at the start of a cell counts, and later ones count when the text before them ends a sentence, or they're the next number after
    /// the last ("1. Enter name 2. Save"). So "the limit is 5. Then" isn't a step. Numbers may skip, since steps alternate between columns, but must rise; if not, the cell stays whole.
    /// </summary>
    static CellParts Cut(string cell)
    {
        var found = new List<(int At, int Number)>();
        foreach (Match m in StepNumber().Matches(cell))
        {
            if (!int.TryParse(m.Groups[1].Value, out var number)) continue;
            if (m.Index == 0 || EndsSentence(cell[..m.Index]) || (found.Count > 0 && number == found[^1].Number + 1)) found.Add((m.Index, number));
        }
        if (found.Count == 0) return new CellParts(cell, null, [], false);
        for (var k = 1; k < found.Count; k++)
            if (found[k].Number <= found[k - 1].Number) return new CellParts(cell, null, [], true);
        var steps = new List<string>();
        for (var k = 0; k < found.Count; k++)
            steps.Add(cell[found[k].At..(k + 1 < found.Count ? found[k + 1].At : cell.Length)].Trim());
        return new CellParts(cell, found[0].At > 0 ? cell[..found[0].At].Trim() : null, steps, false);
    }

    static bool EndsSentence(string text)
    {
        var t = text.TrimEnd().TrimEnd(Closers.ToCharArray());
        return t.Length > 0 && t[^1] is '.' or '!' or '?' or ':' or ';';
    }

    // ---- sentences ----

    /// <summary>
    /// Cuts at a stop, then a space, then a capital. Doesn't cut inside a code span, or at a dotted run. A stop after one letter, an abbreviation or a dotted token before a capital,
    /// and any stop before a digit, might or might not end a sentence, so it isn't cut, and the sentence says so.
    /// </summary>
    static IEnumerable<(string Text, bool Doubt)> Sentences(string paragraph)
    {
        var start = 0;
        var doubt = false;
        var inCode = false;
        for (var i = 0; i < paragraph.Length; i++)
        {
            var c = paragraph[i];
            if (c == '`') inCode = !inCode;
            if (inCode || c is not ('.' or '!' or '?')) continue;
            if (i > 0 && paragraph[i - 1] == '.') continue;
            var after = i + 1;
            while (after < paragraph.Length && Closers.Contains(paragraph[after])) after++;
            if (after >= paragraph.Length || !char.IsWhiteSpace(paragraph[after])) continue;
            var next = after;
            while (next < paragraph.Length && char.IsWhiteSpace(paragraph[next])) next++;
            var first = next;
            while (first < paragraph.Length && Openers.Contains(paragraph[first])) first++;
            if (first >= paragraph.Length) continue;
            var ch = paragraph[first];
            var capital = char.IsLetter(ch) && !char.IsLower(ch);
            if (char.IsDigit(ch) || (capital && c == '.' && ShortForm(paragraph, start, i))) { doubt = true; continue; }
            if (!capital) continue;
            yield return (paragraph[start..after], doubt);
            start = next;
            doubt = false;
        }
        if (start < paragraph.Length) yield return (paragraph[start..].TrimEnd(), doubt);
    }

    static bool ShortForm(string text, int start, int stop)
    {
        var from = stop;
        while (from > start && !char.IsWhiteSpace(text[from - 1])) from--;
        var token = text[from..stop].TrimStart(Openers.ToCharArray());
        return token.Length == 1 && char.IsLetter(token[0]) || token.Contains('.') || Abbreviations.Contains(token);
    }

    /// <summary>A paragraph introduces what follows when its last sentence ends in a colon or is all bold: nothing else about it, and never particular words.</summary>
    static bool Introduces(string sentence) => sentence.TrimEnd().TrimEnd(Closers.ToCharArray()).EndsWith(':') || AllBold().IsMatch(sentence.Trim());

    // ---- flows in a table ----

    /// <summary>
    /// For each row of a table, the flow its claims fall under, and which row is a label. Flows only exist where a table has numbered steps. A steps row whose only other cell is its first
    /// column is labelled by that cell. Otherwise look at the plain rows just above the steps, from the bottom, using only where cells sit: a first-column value is a label when the steps below
    /// leave that column empty, and a row with one value elsewhere is a label when it sits in the column the steps start in, since a value anywhere else belongs to its own column.
    /// Plain rows after the last steps belong to no flow. A row with one value between column headings and their steps, or starting in lower case, continues something above: it's never a label, and it's listed.
    /// </summary>
    static RowFlow[] Flows(List<Row> rows, string? flow)
    {
        var result = new RowFlow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            if (i > 0 && rows[i].Closed && rows[i].First is null && rows[i].Values == 1 && !rows[i].HasSteps && char.IsLower(rows[i].Cells[0].Text[0])) result[i].Continues = true;
        for (var i = 0; i < rows.Count;)
        {
            if (rows[i].HasSteps)
            {
                if (rows[i].LabelledByFirst) { flow = rows[i].First; result[i] = result[i] with { Flow = flow, Label = true }; }
                else result[i] = result[i] with { Flow = flow };
                i++;
                continue;
            }
            var end = i;
            while (end < rows.Count && !rows[end].HasSteps) end++;
            if (end == rows.Count) break;
            var labelAt = -1;
            string? label = null;
            if (!rows[end].LabelledByFirst)
            {
                var stepsEnd = end;
                while (stepsEnd < rows.Count && rows[stepsEnd].HasSteps) stepsEnd++;
                var stepColumn = rows[end..stepsEnd].Min(r => r.StepColumn);
                for (var k = end - 1; k >= i && labelAt < 0; k--)
                {
                    var row = rows[k];
                    if (!row.Closed || row.Cells.Count == 0 || !row.Cells.Any(c => c.Text.Any(char.IsLetter))) continue;
                    if (row.First is { } first && (rows[end].First is null || (row.Values == 0 && stepColumn == 0)))
                    {
                        (labelAt, label) = (k, row.Values > 1 ? first : string.Join(' ', row.Cells.Select(c => c.Text)));
                        continue;
                    }
                    if (row.First is not null || row.Values != 1 || result[k].Continues) continue;
                    if (k == end - 1 && k > i && rows[k - 1].Values > 1) { result[k].Continues = true; continue; }
                    if (row.Cells[0].Column == stepColumn) (labelAt, label) = (k, row.Cells[0].Text);
                }
            }
            for (var k = i; k < end; k++)
                result[k] = labelAt < 0
                    ? result[k] with { Flow = rows[end].LabelledByFirst ? null : flow }
                    : result[k] with { Flow = k > labelAt ? label : null, Label = k == labelAt };
            if (labelAt >= 0) flow = label;
            i = end;
        }
        for (var i = 0; i < rows.Count; i++)
            if (!result[i].Label && !result[i].Continues) result[i].SetAside = SetAsideRow(rows, i);
        return result;
    }

    /// <summary>
    /// A row nothing can be compared with, told by where it sits (D145). A column heading: no steps of its own, directly above a steps row, with a value for each cell holding steps there.
    /// A label: two cells, the first ending in a colon, the second a single token, as in <c>Ref: | A12</c>. A row that's neither stays a claim, even if a person would call it a heading.
    /// </summary>
    static string? SetAsideRow(List<Row> rows, int at)
    {
        var row = rows[at];
        if (row.HasSteps || !row.Closed || row.Steps!.Any(c => c.OutOfOrder)) return null;
        if (row.Values >= 2 && at + 1 < rows.Count && rows[at + 1].HasSteps && row.Values == rows[at + 1].Steps!.Count(c => c.Steps.Count > 0)) return JudgeKinds.ColumnHeading;
        if (row.Cells.Count == 2 && row.First is { } first && first.TrimEnd(Closers.ToCharArray()).EndsWith(':') && !row.Cells[1].Text.Any(char.IsWhiteSpace)) return JudgeKinds.LabelRow;
        return null;
    }

    record struct RowFlow(string? Flow, bool Label, bool Continues)
    {
        public string? SetAside { get; set; }
    }

    // ---- the claims ----

    /// <summary>A section's text as claims, in order, with positions from 1.</summary>
    public static List<FsdClaim> Split(string text)
    {
        var claims = new List<FsdClaim>();
        void Add(string? flow, string kind, string words, string? problem = null, string? setAside = null) => claims.Add(new FsdClaim(claims.Count + 1, flow, kind, words, problem, setAside));
        var blocks = Blocks(text);
        string? flow = null;
        for (var b = 0; b < blocks.Count; b++)
        {
            switch (blocks[b])
            {
                case Paragraph p:
                    var sentences = Sentences(p.Text).ToList();
                    var next = b + 1 < blocks.Count ? blocks[b + 1] : null;
                    var leads = next is ListItem || (next is Table t && t.Rows.Any(r => r.HasSteps));
                    // Starting in lower case, it's the rest of something above, such as a table cell a page break pushed out, so the flow goes on, and it's listed.
                    var continues = char.IsLower(p.Text[0]);
                    if (!continues) flow = null;
                    for (var s = 0; s < sentences.Count; s++)
                    {
                        var label = leads && s == sentences.Count - 1 && Introduces(sentences[s].Text);
                        Add(continues ? flow : null, label ? Kinds.FlowLabel : Kinds.Sentence, sentences[s].Text, sentences[s].Doubt ? Problems.MaybeSentenceEnd : continues && s == 0 ? Problems.ContinuesAbove : null);
                        if (label) flow = sentences[s].Text;
                    }
                    break;
                case ListItem item:
                    Add(flow, Kinds.ListItem, item.Text);
                    break;
                case Whole whole:
                    Add(null, whole.Kind, whole.Text, whole.Problem);
                    break;
                case Table table:
                    var flows = Flows(table.Rows, flow);
                    var tails = Tails(table.Rows, flows);
                    var open = new Dictionary<int, int>();
                    for (var r = 0; r < table.Rows.Count; r++)
                    {
                        var above = open;
                        open = [];
                        AddRow(table.Rows[r], flows[r], Add, tails[r], column => open[column] = claims.Count - 1, (column, tail) =>
                        {
                            var head = claims[above[column]];
                            claims[above[column]] = head with { Words = $"{head.Words} {tail}", Problem = EndsSentence(tail) ? null : Problems.EndsOpen, Join = new FsdJoin(head.Words.Length + 1, claims.Count) };
                        });
                    }
                    flow = table.Rows[^1].HasSteps ? flows[^1].Flow : null;
                    break;
            }
        }
        return claims;
    }

    /// <summary>
    /// Which cells of each row finish a step the row above left open (D146), by position and punctuation only. A step with no end stop at the end of its cell, and in the same column of the next row, text with
    /// no number before it, starting in lower case. Strict: every such start in the row has to sit under an open step, or a start that belongs to nothing says the columns can't be trusted, and the whole row stays
    /// apart. A row of plain values goes only if all its cells do, and a row that labels a flow never goes. Anything left apart keeps its tags.
    /// </summary>
    static HashSet<int>[] Tails(List<Row> rows, RowFlow[] flows)
    {
        var tails = new HashSet<int>[rows.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            tails[r] = [];
            if (r == 0 || !rows[r - 1].Closed || !rows[r].Closed || flows[r].Label) continue;
            var open = OpenColumns(rows[r - 1]);
            var starts = rows[r].Cells.Where((_, i) => StartsLower(rows[r].Steps![i])).Select(c => c.Column).ToHashSet();
            if (open.Count == 0 || starts.Count == 0 || !starts.IsSubsetOf(open)) continue;
            if (!rows[r].HasSteps && starts.Count != rows[r].Cells.Count) continue;
            tails[r] = starts;
        }
        return tails;
    }

    /// <summary>The columns of a row whose last step ends with no end stop.</summary>
    static HashSet<int> OpenColumns(Row row) =>
        [.. row.Cells.Where((_, i) => row.Steps![i].Steps.Count > 0 && !EndsSentence(row.Steps[i].Steps[^1])).Select(c => c.Column)];

    /// <summary>The cell opens with text that has no number before it, in lower case.</summary>
    static bool StartsLower(CellParts cell) => !cell.OutOfOrder && (cell.Steps.Count > 0 ? cell.Lead : cell.Text) is { Length: > 0 } start && char.IsLower(start[0]);

    static void AddRow(Row row, RowFlow at, Action<string?, string, string, string?, string?> add, HashSet<int> tails, Action<int> opened, Action<int, string> takeTail)
    {
        if (row.Cells.Count == 0) return;
        if (!row.Closed) { add(at.Flow, Kinds.Block, row.Raw, Problems.RowNotClosed, null); return; }
        if (!row.HasSteps)
        {
            if (tails.Count > 0)
            {
                foreach (var (column, text) in row.Cells) takeTail(column, text);
                return;
            }
            var outOfOrder = row.Steps!.Any(c => c.OutOfOrder);
            var problem = outOfOrder ? Problems.NumbersOutOfOrder : at.Continues ? Problems.ContinuesAbove : null;
            add(at.Flow, at.Label ? Kinds.FlowLabel : Kinds.TableRow, row.Joined, problem, at.SetAside);
            return;
        }
        // Left to right, as written. Steps alternating between columns stay in that order, each with its own number, so the words read as the section does.
        var plain = new List<string>();
        void FlushPlain()
        {
            if (plain.Count > 0) add(at.Label ? null : at.Flow, at.Label ? Kinds.FlowLabel : Kinds.TableRow, string.Join(" | ", plain), null, null);
            plain.Clear();
        }
        for (var i = 0; i < row.Steps!.Count; i++)
        {
            var cell = row.Steps[i];
            var column = row.Cells[i].Column;
            if (cell.OutOfOrder) { FlushPlain(); add(at.Flow, Kinds.TableRow, cell.Text, Problems.NumbersOutOfOrder, null); continue; }
            if (cell.Steps.Count == 0)
            {
                if (!tails.Contains(column)) { plain.Add(cell.Text); continue; }
                FlushPlain();
                takeTail(column, cell.Text);
                continue;
            }
            FlushPlain();
            if (cell.Lead is not null)
            {
                if (tails.Contains(column)) takeTail(column, cell.Lead);
                else add(at.Flow, Kinds.Step, cell.Lead, Problems.StartsMidStep, null);
            }
            for (var s = 0; s < cell.Steps.Count; s++)
                add(at.Flow, Kinds.Step, cell.Steps[s], s == cell.Steps.Count - 1 && !EndsSentence(cell.Steps[s]) ? Problems.EndsOpen : null, null);
            if (!EndsSentence(cell.Steps[^1])) opened(column);
        }
        FlushPlain();
    }

    // ---- the words check (D18) ----

    /// <summary>The section's words, read straight from its text and not through <see cref="Split"/>: table rules, cell bars and list bullets aside.</summary>
    public static List<string> SectionWords(string text)
    {
        var words = new List<string>();
        var fenced = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (IsFence(raw)) { fenced = !fenced; words.AddRange(Words(raw)); continue; }
            if (fenced) { words.AddRange(Words(raw)); continue; }
            if (RuleLine().IsMatch(raw.Trim())) continue;
            words.AddRange(Words(Bullet().Replace(raw, "")));
        }
        return words;
    }

    static string[] Words(string text) => CellBar().Replace(text, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether the claims' words, together and in order, are the section's words. Null when they are. A joined claim's second part is read where the section has it, after the claim its join names,
    /// so a join can't hide a word that's moved, lost or added: it's the claim's own words that get cut and put back.
    /// </summary>
    public static FsdWordMismatch? CheckWords(string text, IEnumerable<FsdClaim> claims)
    {
        var expected = SectionWords(text);
        var all = claims.ToList();
        var got = new List<string>();
        foreach (var c in all)
        {
            got.AddRange(Words(c.Join is null ? c.Words : c.Words[..c.Join.TailAt]));
            foreach (var joined in all.Where(j => j.Join?.TailAfter == c.Position)) got.AddRange(Words(joined.Words[joined.Join!.TailAt..]));
        }
        for (var i = 0; i < Math.Max(expected.Count, got.Count); i++)
        {
            var e = i < expected.Count ? expected[i] : null;
            var g = i < got.Count ? got[i] : null;
            if (e != g) return new FsdWordMismatch(i + 1, e, g, expected.Count, got.Count);
        }
        return null;
    }

    // ---- the command ----

    public static JsonObject Off() => new()
    {
        ["split"] = false, ["off"] = $"{Component} is off (components.{Component}), so nothing was split. That isn't a pass.",
    };

    /// <summary>A document's claims, or one section's. Sections with no text aren't counted. Writes nothing.</summary>
    /// <param name="claimLinks">The switch is on (D145): the file is brought up to date first, and each claim says its id and kind.</param>
    public static JsonObject Report(string root, string doc, string? section, bool claimLinks = false)
    {
        var synced = claimLinks ? ClaimFile.Sync(root, doc) : null;
        var held = claimLinks ? ClaimFile.Load(root, doc) : null;
        var document = FsdOutline.Load(root, doc);
        var all = document["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var chosen = section is not null ? [FsdOutline.Find(document, section)] : all.Where(s => ((string)s["text"]!).Trim().Length > 0).ToList();

        var sections = new JsonArray();
        var notClean = new JsonArray();
        var mismatches = new JsonArray();
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        var joined = 0;
        foreach (var s in chosen)
        {
            var text = (string)s["text"]!;
            var claims = Split(text);
            var id = (string?)s["permanentId"];
            var number = (string)s["id"]!;
            total += claims.Count;
            foreach (var c in claims)
            {
                kinds[c.Kind] = kinds.GetValueOrDefault(c.Kind) + 1;
                if (c.Problem is null) continue;
                reasons[c.Problem] = reasons.GetValueOrDefault(c.Problem) + 1;
                notClean.Add(new JsonObject { ["section"] = id, ["number"] = number, ["position"] = c.Position, ["reason"] = c.Problem, ["words"] = c.Words });
            }
            if (CheckWords(text, claims) is { } m)
                mismatches.Add(new JsonObject { ["section"] = id, ["number"] = number, ["at"] = m.At, ["sectionWord"] = m.Section, ["claimWord"] = m.Claims, ["sectionWords"] = m.SectionWords, ["claimWords"] = m.ClaimWords });
            sections.Add(new JsonObject
            {
                ["section"] = id, ["number"] = number, ["title"] = (string)s["title"]!,
                ["claims"] = new JsonArray([.. claims.Select(c => (JsonNode)ClaimJson(id, c, held?.At(id, c.Position)))]),
            });
            joined += claims.Count(c => c.Join is not null);
        }
        var result = new JsonObject
        {
            ["doc"] = doc, ["split"] = true,
            ["sectionCount"] = chosen.Count, ["claimCount"] = total, ["joinedCount"] = joined, ["notCleanCount"] = notClean.Count, ["mismatchCount"] = mismatches.Count,
            ["kinds"] = Counts(kinds), ["notCleanByReason"] = Counts(reasons),
            ["notClean"] = notClean, ["mismatches"] = mismatches, ["sections"] = sections,
        };
        if (section is not null) result["only"] = section;
        if (synced is not null) result["ids"] = synced;
        FsdHandEdit.Note(root, doc, document, result);
        return result;
    }

    static JsonObject ClaimJson(string? section, FsdClaim c, StoredClaim? held = null)
    {
        var o = new JsonObject { ["section"] = section, ["position"] = c.Position, ["flow"] = c.Flow, ["kind"] = c.Kind, ["words"] = c.Words };
        if (held is not null) { o["id"] = held.Id; o["judgeKind"] = held.Kind; }
        if (c.Problem is not null) o["problem"] = c.Problem;
        if (c.Join is not null) o["joined"] = true;
        return o;
    }

    static JsonObject Counts(SortedDictionary<string, int> counts)
    {
        var o = new JsonObject();
        foreach (var (name, count) in counts) o[name] = count;
        return o;
    }

    /// <summary><c>flow | words</c>, one a line, for a person. A whole document also has a line for each section. Counts are in the JSON.</summary>
    public static string Text(JsonNode? result)
    {
        if ((bool?)result!["split"] != true) return $"{(string)result["off"]!}\n";
        var sb = new StringBuilder();
        var one = result["only"] is not null;
        foreach (var s in result["sections"]!.AsArray().OfType<JsonObject>())
        {
            if (!one) sb.Append($"# {(string)s["number"]!}  {(string)s["title"]!}\n");
            foreach (var c in s["claims"]!.AsArray().OfType<JsonObject>())
            {
                var words = ((string)c["words"]!).Replace('\n', ' ');
                var flow = (string?)c["flow"] ?? "-";
                if ((string?)c["id"] is { } id) sb.Append(id).Append(' ').Append((string)c["judgeKind"]!).Append(" | ");
                sb.Append(flow).Append(" | ").Append(words);
                if ((bool?)c["joined"] == true) sb.Append(" [joined]");
                if ((string?)c["problem"] is { } problem) sb.Append(" [").Append(problem).Append(']');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }
}
