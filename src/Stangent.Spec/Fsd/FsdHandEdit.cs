using System.Text.Json.Nodes;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Whether a document imported before phase fs still has, in <c>document.md</c>, what its <c>document.json</c> sections say (D52):
/// <c>fsd migrate</c> refuses a hand edit there, since it never reached a step. Since phase fs layer 2 every step reads the committed
/// chapters, so an edit there counts once committed (<see cref="FsdDocument.Note"/> says when one isn't). Code only.
/// Text above the first heading has no section, so it isn't compared.
/// </summary>
public static class FsdHandEdit
{
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
}
