using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>One row by its place: in a section's table the header is row 1 and the rule isn't counted; in a sheet's CSV, row N is line N.</summary>
public sealed record FsdTableRow(int Row, IReadOnlyList<string> Cells);

/// <summary>A table read into rows: a section's Markdown table, or one sheet of an attachment.</summary>
/// <param name="Section">The section it sits in, or null for an attachment with no section (a PDF's).</param>
public sealed record FsdTable(string Id, string? Sheet, JsonObject? Section, string? AttachmentFile, IReadOnlyList<string> Columns, IReadOnlyList<FsdTableRow> Rows)
{
    public int HeaderRow { get; init; } = 1;
    public string Name => Sheet is null ? Id : $"{Id} sheet {Sheet}";
}

/// <summary>A document's tables as rows, by code (C.2, D124). Nothing here reads anything an AI wrote, and nothing reads it back into a hash.</summary>
public static class FsdTables
{
    /// <summary>A Markdown row's cells: a bar with a backslash before it is part of the cell. The backslash stays, so a cell is still quotable word for word.</summary>
    static List<string> Cells(string line)
    {
        var inner = line.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|') && !inner.EndsWith("\\|", StringComparison.Ordinal)) inner = inner[..^1];
        var cells = new List<string>();
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] != '|' || (i > 0 && inner[i - 1] == '\\')) continue;
            cells.Add(inner[start..i].Trim());
            start = i + 1;
        }
        cells.Add(inner[start..].Trim());
        return cells;
    }

    /// <summary>Each Markdown table in a section's text, as <see cref="FsdObjects.TablesIn"/> finds them: its header, then its rows.</summary>
    static IEnumerable<(List<string> Columns, List<FsdTableRow> Rows)> Read(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!lines[i].StartsWith('|') || !FsdObjects.RuleLine().IsMatch(lines[i + 1])) continue;
            var end = i + 2;
            while (end < lines.Length && lines[end].StartsWith('|')) end++;
            yield return (Cells(lines[i]), [.. Enumerable.Range(i + 2, end - i - 2).Select(k => new FsdTableRow(k - i, Cells(lines[k])))]);
            i = end - 1;
        }
    }

    public static List<FsdTable> All(string root, string doc)
    {
        var document = FsdOutline.Load(root, doc);
        var sections = document["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var tables = new List<FsdTable>();
        foreach (var t in (document["tables"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            var section = sections.First(s => (string)s["permanentId"]! == (string)t["section"]!);
            var (columns, rows) = Read((string)section["text"]!).ElementAt((int)t["position"]! - 1);
            tables.Add(new FsdTable((string)t["id"]!, null, section, null, columns, rows));
        }
        foreach (var a in Sheets(document))
        {
            var path = Path.Combine(FsdAttachments.Folder(FsdImport.DocDir(root, doc), a.Id), a.File);
            if (!File.Exists(path)) continue;
            var lines = File.ReadAllText(path).TrimEnd('\n').Split('\n').Select((l, i) => new FsdTableRow(i + 1, AttachmentChecks.Cells(l))).Where(r => r.Cells.Any(c => c.Trim().Length > 0)).ToList();
            if (lines.Count == 0) continue;
            tables.Add(new FsdTable(a.Id, a.Sheet, sections.FirstOrDefault(s => (string)s["id"]! == a.Section), a.File, lines[0].Cells, lines.Skip(1).ToList()) { HeaderRow = lines[0].Row });
        }
        return tables;
    }

    static IEnumerable<(string Id, string? Sheet, string File, string? Section)> Sheets(JsonObject document) =>
        (document["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Where(a => a["id"] is not null).SelectMany(a => (a["files"]?.AsArray() ?? []).OfType<JsonObject>()
            .Where(f => (string?)f["role"] == "csv").Select(f => ((string)a["id"]!, (string?)f["sheet"], (string)f["file"]!, (string?)a["sectionNumber"])));

    /// <summary>Attachments with no sheet to read: their tables, if any, can't be listed, so they're named instead of passed over.</summary>
    public static JsonArray NotRead(string root, string doc)
    {
        var document = FsdOutline.Load(root, doc);
        var read = Sheets(document).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        return new JsonArray([.. (document["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Where(a => (string?)a["id"] is { } id && !read.Contains(id))
            .Select(a => (JsonNode)new JsonObject { ["id"] = (string)a["id"]!, ["fileName"] = (string)a["fileName"]!, ["section"] = (string?)a["sectionNumber"] })]);
    }

    public static JsonObject Listing(string root, string doc) => new()
    {
        ["doc"] = doc,
        ["tables"] = new JsonArray([.. All(root, doc).Select(t => (JsonNode)Describe(t))]),
        ["attachmentsNotRead"] = NotRead(root, doc),
    };

    internal static JsonObject Describe(FsdTable t)
    {
        var o = new JsonObject { ["id"] = t.Id };
        if (t.Sheet is not null) o["sheet"] = t.Sheet;
        o["section"] = (string?)t.Section?["id"];
        o["sectionId"] = (string?)t.Section?["permanentId"];
        o["title"] = (string?)t.Section?["title"];
        o["columns"] = new JsonArray([.. t.Columns.Select(c => (JsonNode)c)]);
        o["rows"] = t.Rows.Count;
        return o;
    }
}
