using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>Reading a document by its sections, by code: its outline, one section, and one chapter's sections (A.10, E.3, D122).</summary>
public static class FsdOutline
{
    public static JsonObject Load(string root, string doc)
    {
        var path = Path.Combine(FsdImport.DocDir(root, doc), "document.json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
            : throw new StangentException("not_found", $"No imported document {doc}.", ExitCodes.NotFound);
    }

    static List<JsonObject> Sections(JsonObject document) => [.. document["sections"]!.AsArray().OfType<JsonObject>()];

    /// <summary>A section by its permanent id or its number or heading path.</summary>
    public static JsonObject Find(JsonObject document, string section) =>
        Sections(document).FirstOrDefault(s => (string?)s["permanentId"] == section || (string)s["id"]! == section)
        ?? throw new StangentException("not_found", $"Document {(string)document["id"]!} has no section {section}. `stangent fsd outline {(string)document["id"]!}` lists them.", ExitCodes.NotFound);

    static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    static int Lines(string text) => text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;

    public static JsonObject Outline(string root, string doc)
    {
        var document = Load(root, doc);
        var chapter = 0;
        var result = new JsonObject
        {
            ["doc"] = doc, ["hash"] = (string)document["original"]!["hash"]!, ["title"] = (string)document["title"]!,
            ["outline"] = new JsonArray([.. Sections(document).Select(s =>
            {
                var text = (string)s["text"]!;
                var o = new JsonObject
                {
                    ["level"] = (int)s["level"]!, ["id"] = (string?)s["permanentId"], ["number"] = (string)s["id"]!, ["title"] = (string)s["title"]!,
                    ["words"] = Words(text), ["lines"] = Lines(text),
                };
                if (s["parent"] is null) o["chapter"] = ++chapter;
                return (JsonNode)o;
            })]),
        };
        FsdHandEdit.Note(root, doc, document, result);
        return result;
    }

    public static string OutlineText(JsonNode? result)
    {
        var sb = new StringBuilder($"# {(string)result!["title"]!} (`{(string)result["doc"]!}`)\n\n");
        foreach (var s in result["outline"]!.AsArray().OfType<JsonObject>())
            sb.Append($"{new string(' ', 2 * ((int)s["level"]! - 1))}- {(string?)s["id"]}  {(string)s["number"]!}  {(string)s["title"]!}  ({(int)s["words"]!} words, {(int)s["lines"]!} lines)\n");
        return sb.ToString();
    }

    public static JsonObject Show(string root, string doc, string section)
    {
        var document = Load(root, doc);
        var s = Find(document, section);
        var (tables, figures) = FsdObjects.In(document, (string?)s["permanentId"] ?? "");
        FsdHandEdit.Note(root, doc, document, null);
        return new JsonObject
        {
            ["doc"] = doc, ["id"] = (string?)s["permanentId"], ["number"] = (string)s["id"]!, ["title"] = (string)s["title"]!, ["level"] = (int)s["level"]!,
            ["parent"] = (string?)s["parent"], ["line"] = (int)s["line"]!, ["words"] = Words((string)s["text"]!), ["tables"] = tables, ["figures"] = figures, ["attachments"] = (s["attachments"]?.AsArray() ?? []).DeepClone(), ["attachmentLines"] = (s["attachmentLines"]?.AsArray() ?? []).DeepClone(), ["text"] = (string)s["text"]!,
        };
    }

    public static string ShowText(JsonNode? result)
    {
        var lines = result!["attachmentLines"]!.AsArray().Select(l => $"\n{(string)l!}\n");
        return $"{(string)result["number"]!}  {(string)result["title"]!}\n\n{(string)result["text"]!}\n{string.Concat(lines)}";
    }

    /// <summary>
    /// One top-level heading and everything under it, in document order. <paramref name="chapter"/> is its number, its permanent id or its heading. Where no chapter has a
    /// number, it's its place in the list instead, as <c>fsd outline</c> shows. A numbered document never falls back on place, or <c>--chapter 2</c> in a document with chapters 1 and 3 would run 3.
    /// </summary>
    public static List<JsonObject> Chapter(JsonObject document, string chapter)
    {
        var sections = Sections(document);
        var tops = sections.Select((s, i) => (Section: s, At: i)).Where(x => x.Section["parent"] is null).ToList();
        var named = tops.FindIndex(t => (string)t.Section["id"]! == chapter || (string?)t.Section["permanentId"] == chapter);
        if (named < 0 && tops.TrueForAll(t => (string)t.Section["idKind"]! != "number") && int.TryParse(chapter, out var place) && place >= 1 && place <= tops.Count) named = place - 1;
        if (named < 0)
            throw new StangentException("not_found", $"Document {(string)document["id"]!} has no chapter {chapter}. `stangent fsd outline {(string)document["id"]!}` lists them.", ExitCodes.NotFound);
        var end = named + 1 < tops.Count ? tops[named + 1].At : sections.Count;
        return sections[tops[named].At..end];
    }
}
