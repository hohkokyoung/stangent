using System.Text;
using System.Text.RegularExpressions;

namespace Stangent.Spec.Fsd;

/// <summary>A heading already written to the Markdown. <c>Typed</c>: its number is part of its text, so it's only a candidate.</summary>
public sealed record FsdHeading(string? Number, string Title, int Level, int Line, int Start, int TextStart, bool Typed, string Full);

/// <summary>The Markdown being built: paragraphs are separated by a blank line, list items by a single line break.</summary>
sealed class MarkdownText
{
    readonly StringBuilder md = new();
    bool lastTight;

    public int Line { get; private set; } = 1;
    public int Length => md.Length;

    public void Emit(string block, bool tight = false)
    {
        if (md.Length > 0) { md.Append(tight && lastTight ? "\n" : "\n\n"); Line += tight && lastTight ? 1 : 2; }
        md.Append(block);
        Line += block.Count(c => c == '\n');
        lastTight = tight;
    }

    public override string ToString() => md.ToString();
}

/// <summary>Headings → sections, for every converter: numbers kept when they fit, ids, parents, and what's reported (D56).</summary>
static partial class FsdSections
{
    /// <summary>The one section of a <c>--flat</c> import. In brackets, so it can't be mistaken for a heading of the document.</summary>
    public const string FlatId = "(whole document, no sections)";

    public const string NoStructureAdvice = "supply a copy with headings or bookmarks, or use --flat to import the whole document as one section with no structure";

    /// <summary>How far a typed chapter number can run on from the one before. Further than this reads as words that start with a number; it's reported either way, so a person sees it.</summary>
    const int MaxChapterGap = 10;

    [GeneratedRegex(@"^(\d+(?:\.\d+)*)\.?\s+(\S.*)$")]
    private static partial Regex TypedNumber();

    /// <summary>A number typed at the start of a heading's text, as a candidate: "4.2 Tags" → 4.2, "Tags".</summary>
    public static (string? Number, string Shown) Typed(string text) =>
        TypedNumber().Match(text) is { Success: true } m ? (m.Groups[1].Value, m.Groups[2].Value) : (null, text);

    /// <summary>
    /// The sections under <paramref name="headings"/>. <paramref name="outline"/> is for documents whose levels come from an
    /// outline rather than heading styles (PDF): a parent with no number ("Chapter 7: …") doesn't stop its children keeping
    /// theirs, and a number out of step with its parent (8.3.2 under 8.2) keeps its id with a problem.
    /// </summary>
    public static List<FsdSection> Build(string md, IReadOnlyList<FsdHeading> headings, List<string> problems, bool outline = false)
    {
        var sections = new List<FsdSection>();
        var path = new List<(int Level, string Title, string Id, string? Number)>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        string? lastTop = null;
        for (var i = 0; i < headings.Count; i++)
        {
            var h = headings[i];
            if (h.Typed)
            {
                // "4.2 Tags" under chapter 4 is a number; "2023 Roadmap" or "24 hour clock" isn't. A typed number
                // must have one part per heading level and continue its parent's number, or be a next chapter's (ChapterNumber).
                var parts = h.Number!.Split('.');
                var above = path.Where(x => x.Level < h.Level).ToList();
                var prefix = string.Join(".", parts[..^1]);
                bool fits;
                if (outline && h.Level > 1 && parts.Length == h.Level)
                {
                    fits = true;
                    if (above.Count > 0 && above[^1].Number is { } own && own != prefix)
                        problems.Add($"line {h.Line}: section {h.Number} is out of step with its parent {own}; it keeps its number");
                }
                else
                {
                    var parentNumber = above.Select(x => x.Id).LastOrDefault();
                    fits = parts.Length == h.Level && (h.Level == 1
                        ? ChapterNumber(parts[0], lastTop, headings, i)
                        : parentNumber is not null && prefix == parentNumber);
                }
                if (!fits)
                {
                    problems.Add($"line {h.Line}: heading \"{h.Full}\" starts with {h.Number}, which doesn't fit the numbering around it; read as a plain heading");
                    h = h with { Number = null, Title = h.Full, Typed = false };
                }
            }
            if (h.Level == 1 && h.Number is not null) lastTop = h.Number;
            while (path.Count > 0 && path[^1].Level >= h.Level) path.RemoveAt(path.Count - 1);
            var parent = path.Count > 0 ? path[^1].Id : null;
            var (id, kind) = h.Number is not null ? (h.Number, "number") : (string.Join(" / ", path.Select(x => x.Title).Append(h.Title)), "path");
            if (seen.TryGetValue(id, out var count))
            {
                problems.Add($"line {h.Line}: section id \"{id}\" is used again (first at line {sections.First(s => s.Id == id).Line}); this one is \"{id} ({count + 1})\"");
                seen[id] = count + 1;
                id = $"{id} ({count + 1})";
            }
            else seen[id] = 1;
            var end = i + 1 < headings.Count ? headings[i + 1].Start : md.Length;
            var body = md[h.TextStart..end].Trim('\n');
            sections.Add(new FsdSection(id, kind, h.Title, h.Level, parent, body, h.Line));
            path.Add((h.Level, h.Title, id, h.Number));
        }
        // A plain heading among numbered siblings inside a numbered chapter: its number is missing, and it isn't
        // guessed. Plain chapters beside numbered ones are fine: FSDs follow no standard (D56).
        var numbered = sections.Where(s => s.IdKind == "number").Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in sections.Where(s => s.Parent is not null && numbered.Contains(s.Parent))
                     .GroupBy(s => (s.Parent, s.Level)).Where(g => g.Any(s => s.IdKind == "number")))
            foreach (var s in group.Where(s => s.IdKind == "path"))
                problems.Add($"line {s.Line}: heading \"{s.Title}\" has no number among numbered headings; its id is its heading path");
        // Numbers out of order within a chapter (4.1 then 4.3): reported, never renumbered.
        foreach (var group in sections.Where(s => s.IdKind == "number").GroupBy(s => s.Parent ?? ""))
        {
            var nums = group.Select(s => s.Id.Split('.')[^1]).Select(n => int.TryParse(n, out var v) ? v : -1).ToList();
            for (var k = 1; k < nums.Count; k++)
                if (nums[k] != nums[k - 1] + 1 && nums[k] > 0 && nums[k - 1] > 0)
                    problems.Add($"section {group.ElementAt(k).Id} follows {group.ElementAt(k - 1).Id}: a number is skipped or out of order");
        }
        return sections;
    }

    /// <summary>
    /// Whether a number typed on a top-level heading is a chapter's. The next number always is. A gap is normal, since it's what taking a chapter out
    /// leaves, and the skip is reported. So a higher number stays a number too, unless what's around it says it's words: too far on to be chapters taken
    /// out ("2023 Roadmap"), a lower-case word next ("24 hour clock"), a later chapter at or below it ("3 Ways to pay" before "2 Pricing"), or, with no
    /// numbered chapter before it, no next number after it. "1 Intro" then "3 Ways to pay" has the same shape as "1 Orders" then "3 Checkout" and reads as a number.
    /// </summary>
    static bool ChapterNumber(string number, string? lastTop, IReadOnlyList<FsdHeading> headings, int at)
    {
        var last = 0;
        if (lastTop is not null && !int.TryParse(lastTop, out last)) return false;
        if (number == (last + 1).ToString()) return true;
        if (!int.TryParse(number, out var n) || n.ToString() != number || n <= last || n - last > MaxChapterGap || char.IsLower(headings[at].Title[0])) return false;
        var later = headings.Skip(at + 1).Where(x => x.Level == 1 && x.Number is not null).Select(x => int.TryParse(x.Number, out var v) ? v : int.MaxValue).ToList();
        return later.All(v => v > n) && (lastTop is not null || later.FirstOrDefault() == n + 1);
    }

    /// <summary>The whole body as one labelled section (<c>--flat</c>), with the problem line saying it has no structure.</summary>
    public static (string Markdown, List<FsdSection> Sections) Flat(string body, List<string> problems)
    {
        var heading = $"# {FlatId}";
        problems.Insert(0, "imported with --flat: the whole document is one section, with no structure");
        var trimmed = body.Trim('\n');
        return ($"{heading}\n\n{trimmed}\n", [new FsdSection(FlatId, "path", FlatId, 1, null, trimmed, 1)]);
    }
}
