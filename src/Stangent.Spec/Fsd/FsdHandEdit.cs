using System.Text.Json.Nodes;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Whether the chapter files (phase fs layer 1), or <c>document.md</c> for a document not migrated yet, still say what <c>document.json</c> does (D52). Every step reads the sections from the JSON, so an edit to the Markdown
/// reaches nothing and, before this, nothing said so. Compares by section, so documents already imported are covered with no new field. Code only.
/// Text above the first heading has no section, so it isn't compared.
/// </summary>
public static class FsdHandEdit
{
    /// <summary>Sections whose Markdown differs from what was imported, by number; empty when it agrees or there's no file to compare.</summary>
    public static List<string> Sections(string dir, JsonObject document)
    {
        return FsdChapters.Markdown(dir) is { } text ? Compare(text, document) : [];
    }

    /// <summary>Sections whose text in <paramref name="text"/> (the whole document) differs from what was imported, by number.</summary>
    public static List<string> Compare(string text, JsonObject document)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sections = document["sections"]!.AsArray().OfType<JsonObject>().OrderBy(s => (int)s["line"]!).ToList();
        var edited = new List<string>();
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var at = (int)s["line"]!; // the heading's line, counted from 1
            var end = i + 1 < sections.Count ? (int)sections[i + 1]["line"]! - 1 : lines.Length;
            var same = at >= 1 && at <= lines.Length && end >= at
                && lines[at - 1].Contains((string)s["title"]!, StringComparison.Ordinal)
                && string.Join('\n', lines[at..end]).Trim('\n') == (string)s["text"]!;
            if (!same) edited.Add((string)s["id"]!);
        }
        return edited;
    }

    public static string? Warning(string doc, IReadOnlyList<string> edited, bool chapters = false) => edited.Count == 0 ? null : chapters
        ? $"specs/fsd/{doc}/chapters don't match document.json at {Name(edited)}. Steps still read document.json's sections until phase fs layer 2 reads the committed chapters, so the edit reaches no step yet."
        : $"specs/fsd/{doc}/document.md doesn't match the import at {Name(edited)}. Nothing reads that file for its sections, so the edit reaches no step: change the FSD itself and import it again.";

    static string Name(IReadOnlyList<string> edited) =>
        edited.Count == 1 ? $"section {edited[0]}" : $"{edited.Count} sections, first {edited[0]}";

    /// <summary>The warning for a command that reads the document, on stderr with everything else that warns, and in its result.</summary>
    public static string? Note(string root, string doc, JsonObject document, JsonObject? result)
    {
        var edited = Sections(FsdImport.DocDir(root, doc), document);
        if (Warning(doc, edited, FsdChapters.Has(FsdImport.DocDir(root, doc))) is not { } warning) return null;
        Console.Error.WriteLine("warning: " + warning);
        if (result is not null)
            result["documentEdited"] = new JsonObject { ["sections"] = new JsonArray([.. edited.Select(e => (JsonNode)e)]), ["warning"] = warning };
        return warning;
    }
}
