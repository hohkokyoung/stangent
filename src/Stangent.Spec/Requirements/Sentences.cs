using System.Text.RegularExpressions;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Requirements;

/// <summary>One thing an FSD section says: a sentence, or a table row with its cells.</summary>
public sealed record Sentence(string Text, IReadOnlyList<string>? Cells = null);

/// <summary>
/// The sentences of an FSD section, by code (D99): every one must be cited by an item or marked <c>background</c>.
/// Table rules and images aren't sentences. A table's first row is one: Word doesn't say whether it's a header, and a row of
/// labels costs one background line, where skipping it could lose a rule. A wrong split only asks for one more citation,
/// since a sentence counts as cited only when quotes cover all its words.
/// </summary>
public static partial class Sentences
{
    [GeneratedRegex(@"^(?:>\s*)*(?:#{1,6}\s+)?(?:(?:[-*+]|\d+[.)])\s+)*")]
    private static partial Regex LineMark();

    [GeneratedRegex(@"(?<=[.!?。！？])\s+(?=[\p{Lu}0-9""'(“‘])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^\|[\s:|-]*\|$")]
    private static partial Regex TableRule();

    public static List<Sentence> Of(string text)
    {
        var sentences = new List<Sentence>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith("![", StringComparison.Ordinal) || TableRule().IsMatch(line)) continue;
            if (line.StartsWith('|'))
            {
                var cells = line.Trim('|').Split('|').Select(Quotes.Normalize).Where(c => c.Length > 0).ToList();
                if (cells.Count > 0) sentences.Add(new Sentence(string.Join(" | ", cells), cells));
                continue;
            }
            sentences.AddRange(SentenceEnd().Split(LineMark().Replace(line, "")).Select(Quotes.Normalize).Where(s => s.Length > 0).Select(s => new Sentence(s)));
        }
        return sentences;
    }

    /// <summary>
    /// Whether quotes cite a sentence. A sentence: it's one of the quotes' own sentences, or quotes of at least 4 words
    /// together cover every word of it, matching whole words. A fragment leaves the rest unread ("Orders over 50 need"
    /// hides "unless the customer is a VIP"), and a short sentence inside a longer one isn't that sentence. A table row:
    /// a quote holds it whole, cell by cell. Any one cell could be the rule ("Refunds need a director's sign-off | ✓"), and
    /// a cell's words can be in another row too.
    /// </summary>
    public static bool Cited(Sentence s, IEnumerable<string> quotes)
    {
        if (s.Cells is not null) return quotes.Any(q => $" | {Trimmed(q)} | ".Contains($" | {s.Text} | ", StringComparison.Ordinal));
        var pieces = quotes.SelectMany(Pieces).Distinct(StringComparer.Ordinal).ToList();
        return pieces.Contains(s.Text, StringComparer.Ordinal) || Covered(s.Text, pieces.Where(p => p.Split(' ').Length >= 4));
    }

    /// <summary>Background is one whole sentence as listed, so nothing else can be hidden in it.</summary>
    public static bool IsExactly(string background, Sentence s) => Trimmed(background) == s.Text;

    static string Trimmed(string quote)
    {
        var q = Quotes.Normalize(quote);
        return q.StartsWith('|') ? string.Join(" | ", q.Trim('|').Split('|').Select(Quotes.Normalize).Where(c => c.Length > 0)) : LineMark().Replace(q, "");
    }

    /// <summary>A quote may span sentences or cells: each part is matched on its own.</summary>
    static IEnumerable<string> Pieces(string quote) =>
        SentenceEnd().Split(Trimmed(quote)).SelectMany(p => p.Split('|')).Select(Quotes.Normalize).Where(p => p.Length > 0).Append(Trimmed(quote));

    static bool Covered(string text, IEnumerable<string> pieces)
    {
        var covered = new bool[text.Length];
        foreach (var p in pieces)
            foreach (var at in WholeWordsAt(text, p)) Array.Fill(covered, true, at, p.Length);
        return Enumerable.Range(0, text.Length).All(i => covered[i] || !char.IsLetterOrDigit(text[i]));
    }

    /// <summary>Where <paramref name="part"/> appears in <paramref name="text"/> without cutting a word in two.</summary>
    static IEnumerable<int> WholeWordsAt(string text, string part)
    {
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
        {
            var starts = at == 0 || !char.IsLetterOrDigit(text[at - 1]) || !char.IsLetterOrDigit(part[0]);
            var ends = at + part.Length == text.Length || !char.IsLetterOrDigit(text[at + part.Length]) || !char.IsLetterOrDigit(part[^1]);
            if (starts && ends) yield return at;
        }
    }
}
