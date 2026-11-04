using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Stangent.Spec.Fsd;

/// <summary>A Word comment as a <c>problems</c> line: one line with a fixed shape, so a later step can turn it into a question for the BA.</summary>
public sealed record DocxComment(string Section, string Author, bool Resolved, string Text)
{
    /// <summary>The <c>section</c> of a comment whose anchor isn't in the imported text.</summary>
    public const string NoAnchor = "(no anchor)";

    static readonly JsonSerializerOptions Quoted = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly Regex Shape = new(@"^comment: section (""(?:[^""\\]|\\.)*"") by (""(?:[^""\\]|\\.)*"") (open|resolved): (""(?:[^""\\]|\\.)*"")$");

    static string Quote(string s) => JsonSerializer.Serialize(s, Quoted);

    public string Line() => $"comment: section {Quote(Section)} by {Quote(Author)} {(Resolved ? "resolved" : "open")}: {Quote(Text)}";

    /// <summary>The comment in a <c>problems</c> line, or null if the line isn't one.</summary>
    public static DocxComment? Parse(string line)
    {
        var m = Shape.Match(line);
        if (!m.Success) return null;
        string Read(int group) => JsonSerializer.Deserialize<string>(m.Groups[group].Value)!;
        return new DocxComment(Read(1), Read(2), m.Groups[3].Value == "resolved", Read(4));
    }
}

/// <summary>What a <c>.docx</c> holds besides what the converter renders: tracked changes, and every word in it, read apart from the converter so one can check the other (D116).</summary>
static class DocxReview
{
    const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly string[] Tracked = ["ins", "del", "moveFrom", "moveTo", "cellIns", "cellDel", "cellMerge"];

    static bool Is(OpenXmlElement e, params ReadOnlySpan<string> names) => e.NamespaceUri == W && names.Contains(e.LocalName);

    /// <summary>A mark on a paragraph mark or a row, not on content.</summary>
    static bool IsMark(OpenXmlElement e) => e.Parent?.LocalName is "rPr" or "trPr";

    /// <summary>Tracked changes by element. Format changes aren't counted: they change no words, and their old properties may hold marks of their own.</summary>
    public static Dictionary<string, int> Count(IEnumerable<OpenXmlElement> roots)
    {
        var found = new Dictionary<string, int>();
        foreach (var e in roots.SelectMany(r => r.Descendants()))
            if (Is(e, Tracked) && !e.Ancestors().Any(a => a.LocalName.EndsWith("Change", StringComparison.Ordinal)))
                found[e.LocalName] = found.GetValueOrDefault(e.LocalName) + 1;
        return found;
    }

    public static string Summary(Dictionary<string, int> found)
    {
        string? Part(int n, string what) => n > 0 ? $"{n} {what}" : null;
        int Get(string k) => found.GetValueOrDefault(k);
        return string.Join(", ", new[]
        {
            Part(Get("ins") + Get("cellIns"), "inserted"), Part(Get("del") + Get("cellDel"), "deleted"),
            Part(Get("moveFrom"), "moved from"), Part(Get("moveTo"), "moved to"), Part(Get("cellMerge"), "cell merges"),
        }.OfType<string>());
    }

    /// <summary>
    /// Word's "accept all", on the in-memory tree only: deleted and moved-away content goes, inserted and moved-in content stays, once.
    /// A paragraph whose mark is deleted joins the next paragraph, as it does in Word.
    /// </summary>
    public static void Accept(OpenXmlElement root)
    {
        static bool Gone(OpenXmlElement? props) => props?.ChildElements.Any(c => Is(c, "del", "moveFrom")) == true;

        foreach (var row in root.Descendants<TableRow>().Where(r => Gone(r.TableRowProperties)).ToList()) row.Remove();
        foreach (var cell in root.Descendants<TableCell>().Where(c => c.TableCellProperties?.ChildElements.Any(x => Is(x, "cellDel")) == true).ToList()) cell.Remove();
        foreach (var e in root.Descendants().Where(e => Is(e, "del", "moveFrom") && !IsMark(e)).ToList()) e.Remove();
        foreach (var p in root.Descendants<Paragraph>().Where(p => Gone(p.ParagraphProperties?.ParagraphMarkRunProperties)).ToList())
        {
            var next = p.ElementsAfter().FirstOrDefault(e => e is Paragraph or Table or SdtBlock or SectionProperties);
            if (next is not Paragraph into) continue;
            var at = (OpenXmlElement?)into.ParagraphProperties;
            foreach (var child in p.ChildElements.Where(c => c is not ParagraphProperties).ToList())
            {
                child.Remove();
                if (at is null) into.PrependChild(child); else at.InsertAfterSelf(child);
                at = child;
            }
            p.Remove();
        }
        foreach (var e in root.Descendants().Where(e => Is(e, "ins", "moveTo") && !IsMark(e)).ToList())
        {
            foreach (var child in e.ChildElements.ToList())
            {
                child.Remove();
                e.InsertBeforeSelf(child);
            }
            e.Remove();
        }
    }

    /// <summary>Children of type <typeparamref name="T"/>, looking through content controls and custom XML wrapped around them.</summary>
    public static IEnumerable<T> Unwrapped<T>(OpenXmlElement parent) where T : OpenXmlElement
    {
        foreach (var c in parent.ChildElements)
            if (c is T t) yield return t;
            else if (c.LocalName is "sdt" or "sdtContent" or "customXml")
                foreach (var inner in Unwrapped<T>(c)) yield return inner;
    }

    /// <summary>
    /// Every word in an element's text-bearing elements, by element name in any namespace, so it doesn't depend on what the converter
    /// knows how to read. Word stores a text box twice (the shape and a fallback for old readers): read once, like the converter.
    /// </summary>
    public static string[] Words(OpenXmlElement e) => Plain(e).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string[] Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string Plain(OpenXmlElement e)
    {
        var sb = new StringBuilder();
        Visit(e, sb);
        return sb.ToString();
    }

    /// <summary>A fallback is only a copy for old readers when there's a choice beside it. With no choice, it's what the file holds.</summary>
    public static bool ForOldReaders(OpenXmlElement e) => e.LocalName == "Fallback" && e.Parent?.ChildElements.Any(c => c.LocalName == "Choice") == true;

    static void Visit(OpenXmlElement e, StringBuilder sb)
    {
        switch (e.LocalName)
        {
            case "Fallback" when ForOldReaders(e): return;
            case "t": sb.Append(e.InnerText); return;
            case "textpath": sb.Append(' ').Append(e.GetAttributes().FirstOrDefault(a => a.LocalName == "string").Value).Append(' '); return;
            case "noBreakHyphen": sb.Append('-'); return;
            case "tab" or "ptab" or "br" or "cr": sb.Append(' '); return;
        }
        var boundary = e.LocalName is "p" or "tc" or "tr" or "pt";
        if (boundary) sb.Append(' ');
        foreach (var c in e.ChildElements) Visit(c, sb);
        if (boundary) sb.Append(' ');
    }
}
