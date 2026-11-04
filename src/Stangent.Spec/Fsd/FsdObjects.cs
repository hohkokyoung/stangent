using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Permanent ids for a document's tables (<c>T-0001</c>) and pictures (<c>F-0001</c>), from the document's ledger, with the section each sits in (D122).
/// They're worked out from what the conversion already gave, so <c>document.md</c> and a section's fields don't change, and no hash reads them.
/// </summary>
public static partial class FsdObjects
{
    const string Tables = "tables", TableFormat = "T-{0:D4}", Figures = "figures", FigureFormat = "F-{0:D4}";

    [GeneratedRegex(@"^\|(-+\|)+$")]
    private static partial Regex RuleLine();

    /// <summary>A section's Markdown tables: a line of cells, then a rule, then its rows. A paragraph that starts with a bar has no rule under it.</summary>
    public static IEnumerable<(int Rows, int Columns, string Hash)> TablesIn(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!lines[i].StartsWith('|') || !RuleLine().IsMatch(lines[i + 1])) continue;
            var end = i + 2;
            while (end < lines.Length && lines[end].StartsWith('|')) end++;
            yield return (end - i - 1, lines[i + 1].Count(c => c == '|') - 1, CanonicalJson.Hash(string.Join('\n', lines[i..end])));
            i = end - 1;
        }
    }

    /// <summary>
    /// Gives every table and picture its id, keeping what <paramref name="previous"/> gave. A picture is its file, named by content. A table keeps its id by the same
    /// words in the same section, then the same words anywhere if that's unique, then the same place in the same section, since an edited table is still that table.
    /// Anything else is new, and an id nothing claimed is marked removed. Sections must have their permanent ids by now.
    /// </summary>
    public static void Assign(string root, string doc, JsonObject? previous, JsonObject document)
    {
        var ledger = FsdVersions.Ledger(root, doc);
        Catch(ledger, Tables, TableFormat, previous?["tables"]);
        Catch(ledger, Figures, FigureFormat, previous?["images"]);
        var sections = document["sections"]!.AsArray().OfType<JsonObject>().ToList();

        var found = sections.SelectMany(s => TablesIn((string)s["text"]!).Select((t, i) => (Section: s, Position: i + 1, t.Rows, t.Columns, t.Hash))).ToList();
        var old = (previous?["tables"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var ids = new string?[found.Count];
        void Claim(Func<(JsonObject Section, int Position, int Rows, int Columns, string Hash), IEnumerable<JsonObject>, JsonObject?> pick)
        {
            for (var i = 0; i < found.Count; i++)
                if (ids[i] is null && pick(found[i], old) is { } match) { ids[i] = (string)match["id"]!; old.Remove(match); }
        }
        static string Place(JsonObject s) => (string)s["permanentId"]!;
        Claim((t, left) => left.FirstOrDefault(o => (string)o["section"]! == Place(t.Section) && (string)o["hash"]! == t.Hash));
        for (var i = 0; i < found.Count; i++)
        {
            if (ids[i] is not null) continue;
            var hash = found[i].Hash;
            var here = old.Where(o => (string)o["hash"]! == hash).ToList();
            if (here.Count == 1 && Enumerable.Range(0, found.Count).Count(k => ids[k] is null && found[k].Hash == hash) == 1) { ids[i] = (string)here[0]["id"]!; old.Remove(here[0]); }
        }
        Claim((t, left) => left.FirstOrDefault(o => (string)o["section"]! == Place(t.Section) && (int)o["position"]! == t.Position));
        foreach (var gone in old) ledger.Remove(Tables, (string)gone["id"]!);
        document["tables"] = new JsonArray([.. found.Select((t, i) => (JsonNode)new JsonObject
        {
            ["id"] = ids[i] ?? ledger.Issue(Tables, TableFormat), ["section"] = Place(t.Section), ["sectionNumber"] = (string)t.Section["id"]!,
            ["position"] = t.Position, ["rows"] = t.Rows, ["columns"] = t.Columns, ["hash"] = t.Hash,
        })]);

        var pictures = (previous?["images"]?.AsArray() ?? []).OfType<JsonObject>().Where(o => o["id"] is not null).ToDictionary(o => (string)o["file"]!, o => (string)o["id"]!, StringComparer.Ordinal);
        foreach (var image in document["images"]!.AsArray().OfType<JsonObject>())
        {
            var file = (string)image["file"]!;
            image["id"] = pictures.Remove(file, out var kept) ? kept : ledger.Issue(Figures, FigureFormat);
            if (sections.FirstOrDefault(s => ((string)s["text"]!).Contains($"({file})", StringComparison.Ordinal)) is not { } holder) continue;
            image["section"] = Place(holder);
            image["sectionNumber"] = (string)holder["id"]!;
        }
        foreach (var gone in pictures.Values) ledger.Remove(Figures, gone);
        FsdAttachments.Assign(root, doc, previous, document);
    }

    /// <summary>A ledger behind the document, or deleted, catches up first, so a live id is never given again.</summary>
    static void Catch(Stangent.Core.Evidence.PermanentIds ledger, string scope, string format, JsonNode? held)
    {
        var highest = (held?.AsArray() ?? []).OfType<JsonObject>().Select(o => (string?)o["id"]).OfType<string>().Select(s => int.Parse(s[2..])).DefaultIfEmpty(0).Max();
        if (highest > 0) ledger.Raise(scope, format, highest);
    }

    /// <summary>The ids in one section, for reading it on its own.</summary>
    public static (JsonArray Tables, JsonArray Figures) In(JsonObject document, string permanentId) => (
        new JsonArray([.. (document["tables"]?.AsArray() ?? []).OfType<JsonObject>().Where(t => (string?)t["section"] == permanentId).Select(t => (JsonNode)(string)t["id"]!)]),
        new JsonArray([.. document["images"]!.AsArray().OfType<JsonObject>().Where(i => (string?)i["section"] == permanentId && i["id"] is not null).Select(i => (JsonNode)(string)i["id"]!)]));
}
