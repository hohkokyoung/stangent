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
            if (held.Count > 0) Hold(s, held);
        }
    }

    /// <summary>A section's attachments, the line said for each and its content hash, from the document's records (each with its id).
    /// The import writes them on its sections; the reader (phase fs layer 2) works them out again on the committed chapters' sections.</summary>
    internal static void Hold(JsonObject s, IReadOnlyList<JsonObject> held)
    {
        s["contentHash"] = ContentHash((string)s["text"]!, held.Select(a => (string)a["hash"]!));
        s.Remove("attachments");
        s.Remove("attachmentLines");
        if (held.Count == 0) return;
        s["attachments"] = new JsonArray([.. held.Select(a => (JsonNode)new JsonObject { ["id"] = (string)a["id"]!, ["fileName"] = (string)a["fileName"]!, ["hash"] = (string)a["hash"]! })]);
        s["attachmentLines"] = new JsonArray([.. held.Select(a => (JsonNode)$"{(string)a["id"]!} {(string)a["fileName"]!} is attached to this section, at attachments/{(string)a["id"]!}/. This line is generated by stangent; it isn't the document's own text.")]);
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

    // ---- fsd take (D396) -----------------------------------------------------------------------------------------------

    /// <summary>The files a record's folder holds, by path under it, with their hashes. Its originals are in the archive (I.7), not here.</summary>
    internal static List<(string Path, string Hash)> Written(JsonObject record) =>
        [.. record["files"]!.AsArray().OfType<JsonObject>().Where(f => !EmbeddedFiles.IsOriginal((string)f["file"]!)).Select(f => ((string)f["file"]!, (string)f["hash"]!))];

    /// <summary>A section's attachments as the comparison weighs them: each one's file name and hash, in a fixed order. Empty with none.</summary>
    internal static string Key(JsonNode? attachments) => string.Join('\u0001', (attachments?.AsArray() ?? []).OfType<JsonObject>()
        .Select(a => (string)a["fileName"]! + "\u0002" + (string)a["hash"]!).Order(StringComparer.Ordinal));

    /// <summary>A section's attachments for <c>fileSections</c>: file name and hash, as the file gave them. Null with none.</summary>
    internal static JsonArray? Base(JsonNode? attachments) => (attachments?.AsArray() ?? []).OfType<JsonObject>().ToList() is { Count: > 0 } list
        ? new JsonArray([.. list.Select(a => (JsonNode)new JsonObject { ["fileName"] = (string)a["fileName"]!, ["hash"] = (string)a["hash"]! })
            .OrderBy(a => (string)a!["fileName"]!, StringComparer.Ordinal).ThenBy(a => (string)a!["hash"]!, StringComparer.Ordinal)])
        : null;

    static string Sha(byte[] bytes) => System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>Why a record's folder doesn't hold what its record says, or null: a file missing, changed or added by hand.</summary>
    internal static string? FolderDiffers(string folder, JsonObject record)
    {
        var written = Written(record);
        foreach (var (path, hash) in written)
        {
            var file = Path.Combine(folder, path);
            if (!File.Exists(file)) return $"{path} is missing";
            if (Sha(File.ReadAllBytes(file)) != hash) return $"{path} was edited";
        }
        if (!Directory.Exists(folder)) return null;
        var names = written.Select(w => w.Path).ToHashSet(StringComparer.Ordinal);
        var extra = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .FirstOrDefault(f => !names.Contains(f));
        return extra is null ? null : $"{extra} was added";
    }

    /// <summary>Whether HEAD's copy of a record's files differs from what the record says: the PM edited one and committed it.</summary>
    internal static bool EditedAtHead(string root, string doc, JsonObject record) =>
        Written(record).Any(w => HeadBytes(root, $"specs/fsd/{doc}/attachments/{(string)record["id"]!}/{w.Path}") is not { } bytes || Sha(bytes) != w.Hash);

    /// <summary>A file as HEAD holds it, as bytes (an attachment can be anything); null when HEAD has none.</summary>
    static byte[]? HeadBytes(string root, string rel)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "cat-file", "blob", $"HEAD:./{rel}" }) psi.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(psi)!;
            using var bytes = new MemoryStream();
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.BaseStream.CopyTo(bytes);
            process.WaitForExit();
            _ = errors.Result;
            return process.ExitCode == 0 ? bytes.ToArray() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>The section's records in a document's facts.</summary>
    static List<JsonObject> On(JsonObject facts, string section) =>
        [.. (facts["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Where(r => (string?)r["section"] == section)];

    /// <summary>The new file's attachments on one of its sections, each with the index that names its folder in the comparison.</summary>
    static List<(JsonObject Record, int Index)> Incoming(JsonArray fileAttachments, string fileSection) =>
        [.. fileAttachments.OfType<JsonObject>().Select((r, i) => (r, i)).Where(x => (string?)x.r["sectionNumber"] == fileSection)];

    /// <summary>
    /// Why a section's attachments can't be taken, or null, before anything is written: the repo's records or files for it changed since
    /// HEAD (an uncommitted edit is never overwritten), or the comparison's copy of one of the file's isn't what the file gave.
    /// </summary>
    internal static string? CannotTake(string docDir, string compareDir, JsonObject working, JsonObject committed, JsonArray fileAttachments, string? section, string fileSection)
    {
        if (section is not null)
        {
            var now = On(working, section);
            if (!JsonNode.DeepEquals(new JsonArray([.. now.Select(r => r.DeepClone())]), new JsonArray([.. On(committed, section).Select(r => r.DeepClone())])))
                return "its attachments' records in document.json were changed since HEAD (an uncommitted change): the change is kept; commit or undo it, import the file again, then take";
            foreach (var r in now)
                if (FolderDiffers(Folder(docDir, (string)r["id"]!), r) is { } why)
                    return $"its attachment {(string)r["id"]!} ({(string)r["fileName"]!}) was changed here since HEAD ({why}): the change is kept; commit or undo it, import the file again, then take";
        }
        foreach (var (r, i) in Incoming(fileAttachments, fileSection))
        {
            var folder = Folder(compareDir, Slot(i));
            foreach (var (path, hash) in Written(r))
                if (!File.Exists(Path.Combine(folder, path)) || Sha(File.ReadAllBytes(Path.Combine(folder, path))) != hash)
                    return $"the comparison's copy of its attachment {(string)r["fileName"]!} ({path}) is missing or isn't what the file gave: import the file again, then take";
        }
        return null;
    }

    /// <summary>
    /// <c>fsd take</c> (D396): a taken section's attachments become the new file's, files and records. Each of the file's attachments on
    /// the section keeps the <c>A-</c> id of the one the repo holds there with the same bytes, then with the same file name, as an
    /// edited file is still that file; any other gets a new id from the ledger; one the repo holds there that the file no longer has is
    /// marked removed (rule 11), and its folder goes (as it came, it's in the archive). The files are copied from the comparison's
    /// copies, which <see cref="CannotTake"/> checked by hash. Other sections' records stay as they are. <paramref name="number"/> is the
    /// section's number (its id) in the repo once taken. Null when neither side has any.
    /// </summary>
    internal static JsonObject? Take(string root, string doc, string docDir, string compareDir, JsonObject facts, JsonArray fileAttachments, string section, string fileSection, string number, ISet<string> changed)
    {
        var held = On(facts, section);
        var incoming = Incoming(fileAttachments, fileSection);
        if (held.Count == 0 && incoming.Count == 0) return null;
        if (facts["attachments"] is not JsonArray records) facts["attachments"] = records = [];
        var ledger = FsdVersions.Ledger(root, doc);
        var highest = records.OfType<JsonObject>().Select(o => (string?)o["id"]).OfType<string>().Select(s => int.Parse(s[2..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        if (highest > 0) ledger.Raise(Scope, Format, highest);

        var left = held.ToList();
        var ids = new string?[incoming.Count];
        void Claim(Func<JsonObject, JsonObject, bool> same)
        {
            for (var i = 0; i < incoming.Count; i++)
                if (ids[i] is null && left.FirstOrDefault(o => same(o, incoming[i].Record)) is { } match) { ids[i] = (string)match["id"]!; left.Remove(match); }
        }
        Claim((o, a) => (string)o["hash"]! == (string)a["hash"]!);
        Claim((o, a) => (string)o["fileName"]! == (string)a["fileName"]!);

        var at = held.Count > 0 ? records.IndexOf(held[0]) : records.Count;
        foreach (var h in held)
        {
            records.Remove(h);
            if (Directory.Exists(Folder(docDir, (string)h["id"]!))) Directory.Delete(Folder(docDir, (string)h["id"]!), recursive: true);
            changed.Add($"specs/fsd/{doc}/attachments/{(string)h["id"]!}");
        }
        var removed = new JsonArray();
        foreach (var gone in left)
        {
            ledger.Remove(Scope, (string)gone["id"]!);
            removed.Add((JsonNode)(string)gone["id"]!);
        }
        var kept = new JsonArray();
        var added = new JsonArray();
        for (var i = 0; i < incoming.Count; i++)
        {
            var (record, index) = incoming[i];
            var id = ids[i] ?? ledger.Issue(Scope, Format);
            (ids[i] is null ? added : kept).Add((JsonNode)id);
            var from = Path.GetFullPath(Folder(compareDir, Slot(index))) + Path.DirectorySeparatorChar;
            var to = Path.GetFullPath(Folder(docDir, id)) + Path.DirectorySeparatorChar;
            foreach (var (path, _) in Written(record))
            {
                var source = Path.GetFullPath(Path.Combine(from, path));
                var target = Path.GetFullPath(Path.Combine(to, path));
                if (!source.StartsWith(from, StringComparison.Ordinal) || !target.StartsWith(to, StringComparison.Ordinal))
                    throw new InvalidOperationException($"An attachment's file {path} would land outside its folder (a stangent bug).");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            var o = record.DeepClone().AsObject();
            o["id"] = id;
            o["section"] = section;
            // The section's number here, which can differ from the file's (fresh check, D396).
            o["sectionNumber"] = number;
            records.Insert(at++, o);
            changed.Add($"specs/fsd/{doc}/attachments/{id}");
        }
        if (records.Count == 0) facts.Remove("attachments");
        return new JsonObject { ["section"] = section, ["kept"] = kept, ["added"] = added, ["removed"] = removed };
    }
}

/// <summary>
/// A section's attachments in the checks (A.7, D123), behind <c>fsd.attachmentsInChecks</c>, off by default. When it's on, a section read for a check carries its attachments' text under <see cref="Key"/>,
/// which is never written back. The link judge's and the disprove step's bundles show it, and a quote can come from it. Off, nothing carries it, so every bundle and hash is what it was.
/// </summary>
public static class AttachmentChecks
{
    public const string Key = "checkAttachments";

    /// <summary>A CSV's rows in the shape quotes match, a row per line, so a line of this is a row of the sheet. Other text as it is.</summary>
    internal static string Readable(string role, string text) =>
        role != "csv" ? text : string.Join('\n', text.TrimEnd('\n').Split('\n').Select(l => l.Length == 0 ? "" : "| " + string.Join(" | ", Cells(l).Select(c => c.Replace("|", "\\|", StringComparison.Ordinal))) + " |"));

    /// <summary>One CSV line's cells. Our own files have no line breaks in a cell, as a cell's text is one line.</summary>
    internal static List<string> Cells(string line)
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
        // The record and the file as committed (phase fs layer 2, D380): an uncommitted edit to either isn't read.
        JsonObject facts;
        try { facts = FsdDocument.CommittedFacts(root, doc); }
        catch (StangentException) { return (false, null); }
        var record = (facts["attachments"]?.AsArray() ?? []).OfType<JsonObject>().FirstOrDefault(a => (string?)a["id"] == (string?)fsd["attachment"]);
        var file = record?["files"]!.AsArray().OfType<JsonObject>().FirstOrDefault(f => (string?)f["file"] == (string?)fsd["attachmentFile"] && (string)f["role"]! is "csv" or "text");
        if (file is null) return (false, null);
        if (FsdDocument.Blob(root, $"specs/fsd/{doc}/attachments/{(string)record!["id"]!}/{(string)file["file"]!}") is not { } held) return (false, null);
        var text = Readable((string)file["role"]!, held);
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
