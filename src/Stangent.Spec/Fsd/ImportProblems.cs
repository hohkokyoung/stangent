using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Import problems as data (<c>source-document</c> v3, D121): <c>kind</c>, the section if the line names one, and <c>detail</c>, the line a person
/// reads. Converters keep writing lines, so v1 and v2 documents and new imports go through this one reader: an old document's lines
/// come out exactly as a new one's would. A line no rule knows is <c>other</c>, never dropped.
/// </summary>
public static partial class ImportProblems
{
    [GeneratedRegex(@"^page (\d+): ")] private static partial Regex PageLine();
    [GeneratedRegex(@"^line (\d+): ")] private static partial Regex MarkdownLine();
    [GeneratedRegex(@"^section (.+?) follows ")] private static partial Regex Follows();
    [GeneratedRegex(@"^line \d+: section (.+?) is out of step")] private static partial Regex OutOfStep();

    /// <summary>Longest first, so <c>4.2.1</c> and a heading path holding a colon are read whole.</summary>
    static List<string> Ids(IEnumerable<JsonObject> sections) =>
        [.. sections.Select(s => (string)s["id"]!).OrderByDescending(s => s.Length).ThenBy(s => s, StringComparer.Ordinal)];

    /// <summary>One line, taken apart. <paramref name="sections"/> are the document's; a line's section is found among them.</summary>
    public static JsonObject Of(string line, IReadOnlyList<JsonObject> sections)
    {
        var ids = Ids(sections);
        var o = new JsonObject { ["kind"] = KindOf(line), ["detail"] = line };
        if (DocxComment.Parse(line) is { } comment)
        {
            o["author"] = comment.Author;
            o["state"] = comment.Resolved ? "resolved" : "open";
            o["text"] = comment.Text;
            if (ids.Contains(comment.Section)) o["sectionNumber"] = comment.Section;
            return o;
        }
        if (PageLine().Match(line) is { Success: true } page) o["page"] = int.Parse(page.Groups[1].Value);
        if (MarkdownLine().Match(line) is { Success: true } at)
        {
            var number = int.Parse(at.Groups[1].Value);
            o["line"] = number;
            if (sections.LastOrDefault(s => (int)s["line"]! <= number) is { } holder) o["sectionNumber"] = (string)holder["id"]!;
        }
        if (line.StartsWith("attachment \"", StringComparison.Ordinal))
        {
            if (ids.FirstOrDefault(id => line.Contains($"\" in section {id}: ", StringComparison.Ordinal)) is { } held) o["sectionNumber"] = held;
            return o;
        }
        if (line.StartsWith("sheet \"", StringComparison.Ordinal))
        {
            if (ids.FirstOrDefault(id => line.StartsWith($"sheet \"{id}\": ", StringComparison.Ordinal)) is { } sheet) o["sectionNumber"] = sheet;
            return o;
        }
        if (Follows().Match(line) is { Success: true } follows) o["sectionNumber"] = follows.Groups[1].Value;
        else if (OutOfStep().Match(line) is { Success: true } step) o["sectionNumber"] = step.Groups[1].Value;
        else
        {
            var body = line.StartsWith("completeness: ", StringComparison.Ordinal) ? line["completeness: ".Length..] : line;
            if (ids.FirstOrDefault(id => body.StartsWith($"section {id}: ", StringComparison.Ordinal)) is { } named) o["sectionNumber"] = named;
        }
        return o;
    }

    static string KindOf(string line)
    {
        if (DocxComment.Parse(line) is not null) return "comment";
        bool Starts(string s) => line.StartsWith(s, StringComparison.Ordinal);
        bool Has(string s) => line.Contains(s, StringComparison.Ordinal);
        if (Starts("completeness: ")) return "completeness";
        if (Starts("attachment \"")) return "attachment";
        if (Starts("hidden sheet \"")) return "hidden";
        if (Starts("shared workbook: ")) return "shared-workbook";
        if (Starts("sheet \"") || Starts("workbook: ")) return SheetKind(line[(Starts("workbook: ") ? "workbook: ".Length : line.IndexOf("\": ", StringComparison.Ordinal) + 3)..]);
        if (Starts("--accept-changes: ")) return "tracked-changes";
        if (Starts("imported with --flat")) return "flat";
        if (Starts("page headers and footers")) return "outside-body";
        if (Starts("printed contents: ") || Starts("sections came from the printed contents")) return "contents";
        if (Starts("bookmark ")) return "bookmark";
        if (Starts("removed a running ") || Starts("a repeated line was kept")) return "running-line";
        if (Starts("text running in another direction")) return "other-direction";
        if (Has(" words are text in white")) return "white-text";
        if (Has(" characters are symbol-font")) return "symbols";
        if (Has(" words come before the first section")) return "before-first-section";
        if (Has(" sentences were joined across a page break")) return "page-join";
        if (Regex.IsMatch(line, @"^\d+ images in this document")) return "image";
        if (MarkdownLine().IsMatch(line) || Follows().IsMatch(line)) return "numbering";
        if (PageLine().IsMatch(line))
        {
            if (Has("the section title")) return "heading";
            if (Has("an image")) return "image";
            if (Has("table") || Has("grid")) return "table";
            return "page-text";
        }
        if (Starts("section ") || Starts("before the first heading: ")) return "element";
        return "other";
    }

    /// <summary>A workbook's line, by what follows the sheet's name, as a name can say anything.</summary>
    static string SheetKind(string rest)
    {
        bool Has(string s) => rest.Contains(s, StringComparison.Ordinal);
        if (rest.StartsWith("merged cells ", StringComparison.Ordinal)) return "merged";
        if (rest.StartsWith("formula at ", StringComparison.Ordinal)) return "formula";
        if (rest.StartsWith("hidden ", StringComparison.Ordinal)) return "hidden";
        if (Has("custom number format") || Has("isn't a real date")) return "number-format";
        if (Has("fsd.excel.")) return "large-workbook";
        if (Has("protected")) return "protected";
        if (Has(" not read") || Has("not a worksheet") || Has("isn't a worksheet")) return "not-read";
        return "sheet";
    }

    /// <summary>Every line of a conversion, in the order the converter wrote them.</summary>
    public static JsonArray Of(IEnumerable<string> lines, IReadOnlyList<JsonObject> sections) =>
        new([.. lines.Select(l => (JsonNode)Of(l, sections))]);

    /// <summary>A document's <c>problems</c> as v3, whatever version wrote them: a v1 or v2 line is read, an object stays.</summary>
    public static void Upgrade(JsonObject doc)
    {
        var sections = doc["sections"]!.AsArray().OfType<JsonObject>().ToList();
        doc["problems"] = new JsonArray([.. doc["problems"]!.AsArray().Select(p =>
            p is JsonObject o ? o.DeepClone() : Of((string)p!, sections))]);
    }

    /// <summary>Puts each problem's permanent id beside its number, once the sections have theirs.</summary>
    public static void Attach(JsonObject doc)
    {
        var ids = doc["sections"]!.AsArray().OfType<JsonObject>()
            .Where(s => s["permanentId"] is not null).ToDictionary(s => (string)s["id"]!, s => (string)s["permanentId"]!, StringComparer.Ordinal);
        foreach (var p in doc["problems"]!.AsArray().OfType<JsonObject>())
        {
            p.Remove("section");
            if ((string?)p["sectionNumber"] is { } number && ids.TryGetValue(number, out var id)) p["section"] = id;
        }
    }
}
