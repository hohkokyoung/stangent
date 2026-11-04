using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// A document's attachments, once it has them (A.6, A.8, D123): their ids and sections, where their files go, and each section's content hash.
/// None of it touches <c>document.md</c> or a section's <c>text</c>.
/// </summary>
public static class FsdAttachments
{
    const string Scope = "attachments", Format = "A-{0:D4}";

    public static string Folder(string docDir, string name) => Path.Combine(docDir, "attachments", name);
    static string Slot(int index) => $"pending-{index + 1:D4}";

    /// <summary>Puts the attachments and each section's hash into a new record. Ids come later, as a section's do.</summary>
    internal static void AddTo(JsonObject document, ConvertedDocument converted, int attachmentChars)
    {
        var bySection = converted.Attachments.Where(a => a.Section is not null).ToLookup(a => a.Section!, StringComparer.Ordinal);
        foreach (var s in document["sections"]!.AsArray().OfType<JsonObject>())
        {
            var held = bySection[(string)s["id"]!].ToList();
            s["contentHash"] = ContentHash((string)s["text"]!, held.Select(a => a.Hash));
            if (held.Count > 0) s["attachments"] = new JsonArray([.. held.Select(a => (JsonNode)new JsonObject { ["fileName"] = a.FileName, ["hash"] = a.Hash })]);
        }
        if (converted.Attachments.Count > 0)
            document["attachments"] = new JsonArray([.. converted.Attachments.Select(a => (JsonNode)EmbeddedFiles.Json(a, attachmentChars))]);
    }

    /// <summary>The section's text and its attachments' hashes. With none, it's the text's own hash, so a section with no attachments reads the same both ways.</summary>
    public static string ContentHash(string text, IEnumerable<string> hashes)
    {
        var sorted = hashes.Order(StringComparer.Ordinal).ToList();
        return CanonicalJson.Hash(sorted.Count == 0 ? text : text + "\u0001" + string.Join('\u0001', sorted));
    }

    /// <summary>
    /// Gives each attachment its <c>A-</c> id, keeping what <paramref name="previous"/> gave: the same bytes in the same section, then the same bytes anywhere if that's unique, then the same
    /// name in the same section, as an edited file is still that file. Anything else is new, and an id nothing claims is marked removed. Sections must have their permanent ids by now.
    /// </summary>
    internal static void Assign(string root, string doc, JsonObject? previous, JsonObject document)
    {
        var old = (previous?["attachments"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var ledger = FsdVersions.Ledger(root, doc);
        var highest = old.Select(o => (string?)o["id"]).OfType<string>().Select(s => int.Parse(s[2..])).DefaultIfEmpty(0).Max();
        if (highest > 0) ledger.Raise(Scope, Format, highest);
        if (document["attachments"] is not JsonArray list)
        {
            foreach (var gone in old) ledger.Remove(Scope, (string)gone["id"]!);
            return;
        }
        var found = list.OfType<JsonObject>().ToList();
        var sections = document["sections"]!.AsArray().OfType<JsonObject>().ToDictionary(s => (string)s["id"]!, StringComparer.Ordinal);
        foreach (var a in found)
            if ((string?)a["sectionNumber"] is { } number && sections.TryGetValue(number, out var s)) a["section"] = (string)s["permanentId"]!;

        void Claim(Func<JsonObject, IEnumerable<JsonObject>, JsonObject?> pick)
        {
            foreach (var a in found.Where(a => a["id"] is null))
                if (pick(a, old) is { } match) { a["id"] = (string)match["id"]!; old.Remove(match); }
        }
        Claim((a, left) => left.FirstOrDefault(o => (string)o["hash"]! == (string)a["hash"]! && (string?)o["section"] == (string?)a["section"]));
        foreach (var a in found.Where(a => a["id"] is null))
        {
            var hash = (string)a["hash"]!;
            var here = old.Where(o => (string)o["hash"]! == hash).ToList();
            if (here.Count == 1 && found.Count(f => f["id"] is null && (string)f["hash"]! == hash) == 1) { a["id"] = (string)here[0]["id"]!; old.Remove(here[0]); }
        }
        Claim((a, left) => left.FirstOrDefault(o => (string)o["fileName"]! == (string)a["fileName"]! && (string?)o["section"] == (string?)a["section"] && a["section"] is not null));
        foreach (var gone in old) ledger.Remove(Scope, (string)gone["id"]!);
        foreach (var a in found.Where(a => a["id"] is null)) a["id"] = ledger.Issue(Scope, Format);

        foreach (var s in sections.Values)
        {
            var held = found.Where(a => (string?)a["sectionNumber"] == (string)s["id"]!).ToList();
            if (held.Count == 0) continue;
            s["attachments"] = new JsonArray([.. held.Select(a => (JsonNode)new JsonObject { ["id"] = (string)a["id"]!, ["fileName"] = (string)a["fileName"]!, ["hash"] = (string)a["hash"]! })]);
            s["attachmentLines"] = new JsonArray([.. held.Select(a => (JsonNode)$"{(string)a["id"]!} {(string)a["fileName"]!} is attached to this section, at attachments/{(string)a["id"]!}/. This line is generated by stangent; it isn't the document's own text.")]);
        }
    }

    /// <summary>Writes the files, each under its id, or for a candidate, under a place that holds until it has one.</summary>
    internal static void Write(string dir, ConvertedDocument converted, JsonObject document)
    {
        var records = (document["attachments"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        for (var i = 0; i < converted.Attachments.Count; i++)
            EmbeddedFiles.Write(Folder(dir, (string?)records[i]["id"] ?? Slot(i)), converted.Attachments[i]);
    }

    /// <summary>A waiting candidate's files become the document's, each under the id it's been given. The old ones go: they're in the archive's original, as ever.</summary>
    internal static void Move(string candidate, string dir, JsonObject document)
    {
        if (Directory.Exists(Path.Combine(dir, "attachments"))) Directory.Delete(Path.Combine(dir, "attachments"), recursive: true);
        var records = (document["attachments"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        for (var i = 0; i < records.Count; i++)
        {
            var from = Folder(candidate, Slot(i));
            if (!Directory.Exists(from)) continue;
            Directory.CreateDirectory(Path.Combine(dir, "attachments"));
            Directory.Move(from, Folder(dir, (string)records[i]["id"]!));
        }
    }

    /// <summary>Whether two records of a document hold the same files, sections and what they came to.</summary>
    internal static bool Same(JsonObject a, JsonObject b)
    {
        static IEnumerable<string> Shape(JsonObject d) => (d["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Select(x =>
            string.Join('\u0001', [(string)x["hash"]!, (string?)x["sectionNumber"] ?? "", (string)x["reading"]!, .. x["files"]!.AsArray().Select(f => (string)f!["hash"]!)]));
        return Shape(a).SequenceEqual(Shape(b));
    }

    /// <summary>The folder a record's files are in, in order: under its id, or for a candidate not yet moved, its holding place.</summary>
    internal static string FolderName(JsonObject record, int index, bool candidate) => candidate ? Slot(index) : (string)record["id"]!;
}

/// <summary>
/// A section's attachments in the checks (A.7, D123), behind <c>fsd.attachmentsInChecks</c>, off by default. When it's on, a section read for a check carries its attachments' text under <see cref="Key"/>,
/// which is never written back. The link judge's and the disprove step's bundles show it, and a quote can come from it. Off, nothing carries it, so every bundle and hash is what it was.
/// </summary>
public static class AttachmentChecks
{
    public const string Key = "checkAttachments";

    /// <summary>Every section in the index that has attachments, read for a check. Does nothing with the switch off.</summary>
    public static void Load(SpecIndex index, StangentRepo repo)
    {
        if (!repo.Config.Fsd.AttachmentsInChecks) return;
        foreach (var document in FsdImport.Documents(repo.Root).Where(d => d["attachments"] is not null))
        {
            var id = (string)document["id"]!;
            foreach (var s in document["sections"]!.AsArray().OfType<JsonObject>())
                if (index.Sections.TryGetValue((id, (string)s["id"]!), out var section))
                    Fill(section, document, FsdImport.DocDir(repo.Root, id), false, repo.Config.Fsd.AttachmentChars);
        }
    }

    /// <summary>One section's attachments, from a document's own folder or a candidate's. What's over the limit is named, with no text, so a bundle can say it wasn't checked.</summary>
    internal static void Fill(JsonObject section, JsonObject document, string docDir, bool candidate, int limit)
    {
        var records = (document["attachments"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var items = new JsonArray();
        for (var i = 0; i < records.Count; i++)
        {
            var a = records[i];
            if ((string?)a["sectionNumber"] != (string)section["id"]!) continue;
            var (id, name) = ((string?)a["id"] ?? "", (string)a["fileName"]!);
            var chars = (int?)a["chars"] ?? 0;
            if (chars > limit)
            {
                items.Add((JsonNode)new JsonObject { ["id"] = id, ["fileName"] = name, ["notChecked"] = $"its {chars} characters are over fsd.attachmentChars ({limit})" });
                continue;
            }
            foreach (var f in a["files"]!.AsArray().OfType<JsonObject>().Where(f => (string)f["role"]! is "csv" or "text"))
            {
                var path = Path.Combine(FsdAttachments.Folder(docDir, FsdAttachments.FolderName(a, i, candidate)), (string)f["file"]!);
                if (!File.Exists(path)) continue;
                var item = new JsonObject { ["id"] = id, ["fileName"] = name, ["file"] = (string)f["file"]!, ["role"] = (string)f["role"]!, ["text"] = Readable((string)f["role"]!, File.ReadAllText(path)) };
                if ((string?)f["sheet"] is { } sheet) item["sheet"] = sheet;
                items.Add((JsonNode)item);
            }
        }
        if (items.Count > 0) section[Key] = items;
    }

    /// <summary>A CSV's rows in the shape quotes match, a row per line, so a line of this is a row of the sheet. Other text as it is.</summary>
    static string Readable(string role, string text) =>
        role != "csv" ? text : string.Join('\n', text.TrimEnd('\n').Split('\n').Select(l => l.Length == 0 ? "" : "| " + string.Join(" | ", Cells(l).Select(c => c.Replace("|", "\\|", StringComparison.Ordinal))) + " |"));

    /// <summary>One CSV line's cells. Our own files have no line breaks in a cell, as a cell's text is one line.</summary>
    static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    /// <summary>The block a bundle adds, after the section's text. Empty for a section carrying nothing, so a bundle stays byte for byte what it was.</summary>
    public static string Block(JsonObject section)
    {
        if (section[Key] is not JsonArray items) return "";
        var sb = new StringBuilder("\n# This section's attachments\n\nFiles attached to the section, read by code. They're part of what it says: a quote from one counts as a quote from the section, word for word.\n");
        foreach (var i in items.OfType<JsonObject>())
        {
            if ((string?)i["notChecked"] is { } why)
            {
                sb.Append($"\n## {(string)i["id"]!} {(string)i["fileName"]!}: not checked\n\nIt isn't in this bundle: {why}.\n");
                continue;
            }
            var sheet = (string?)i["sheet"] is { } s ? $", sheet {s}" : "";
            sb.Append($"\n## {(string)i["id"]!} {(string)i["fileName"]!}, `{(string)i["file"]!}`{sheet}\n\n````text\n{(string)i["text"]!}\n````\n");
        }
        return sb.ToString();
    }

    /// <summary>Whether a quote can be taken from what the section carries.</summary>
    public static bool Holds(JsonObject section, string quote) => Find(section, quote) is not null;

    /// <summary>Where a quote the section's own text doesn't hold came from: the attachment, file, sheet and row. Null when its own text holds it, or nothing does.</summary>
    public static JsonObject? SourceOf(JsonObject section, string quote) => section[Key] is null || Quotes.InText(quote, (string)section["text"]!) ? null : Find(section, quote);

    static JsonObject? Find(JsonObject section, string quote)
    {
        foreach (var i in (section[Key]?.AsArray() ?? []).OfType<JsonObject>().Where(i => i["text"] is not null))
        {
            var text = (string)i["text"]!;
            if (!Quotes.InText(quote, text)) continue;
            var source = new JsonObject { ["attachment"] = (string)i["id"]!, ["attachmentFile"] = (string)i["file"]! };
            if ((string?)i["sheet"] is { } sheet)
            {
                source["attachmentSheet"] = sheet;
                if (Row(text, quote) is { } row) source["attachmentRow"] = row.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return source;
        }
        return null;
    }

    static int? Row(string text, string quote)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Length > 0 && Quotes.InText(quote, lines[i])) return i + 1;
        return null;
    }

    /// <summary>A link's quote from an attachment, against the file as it is now. The file is looked up in the document's record, so a hand-edited link can't name a path. Gives the row it's on, which may have moved, or null when it isn't there any more.</summary>
    internal static (bool Holds, string? Row) Check(string root, JsonObject fsd, string quote)
    {
        var doc = (string)fsd["doc"]!;
        var path = Path.Combine(FsdImport.DocDir(root, doc), "document.json");
        if (!File.Exists(path)) return (false, null);
        var record = (JsonNode.Parse(File.ReadAllText(path))!["attachments"]?.AsArray() ?? []).OfType<JsonObject>().FirstOrDefault(a => (string?)a["id"] == (string?)fsd["attachment"]);
        var file = record?["files"]!.AsArray().OfType<JsonObject>().FirstOrDefault(f => (string?)f["file"] == (string?)fsd["attachmentFile"] && (string)f["role"]! is "csv" or "text");
        if (file is null) return (false, null);
        var full = Path.Combine(FsdAttachments.Folder(FsdImport.DocDir(root, doc), (string)record!["id"]!), (string)file["file"]!);
        if (!File.Exists(full)) return (false, null);
        var text = Readable((string)file["role"]!, File.ReadAllText(full));
        if (!Quotes.InText(quote, text)) return (false, null);
        return (true, (string)file["role"]! == "csv" ? Row(text, quote)?.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    /// <summary>Brings each link's row up to where its quote is now, as <c>trace check</c> fills a section's id: by code, with nothing typed surviving.</summary>
    internal static bool StampRows(string root, JsonObject trace)
    {
        var changed = false;
        foreach (var fsd in trace["entries"]!.AsArray().OfType<JsonObject>().Select(e => e["fsd"]).OfType<JsonObject>().Where(f => f["attachment"] is not null && f["quote"] is not null))
        {
            var (holds, row) = Check(root, fsd, (string)fsd["quote"]!);
            if (!holds || row is null || (string?)fsd["attachmentRow"] == row) continue;
            fsd["attachmentRow"] = row;
            changed = true;
        }
        return changed;
    }
}
