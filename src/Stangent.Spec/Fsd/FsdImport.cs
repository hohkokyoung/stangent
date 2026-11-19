using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd import</c> (Phase 2b step 1, D111): converts a <c>.docx</c>, <c>.pdf</c>, <c>.xlsx</c> or <c>.xlsm</c> to Markdown by code, archives the original
/// (D16) and records the import's facts as a <c>source-document</c> (v4, D383) and the text as chapter files (D377) in <c>specs/fsd/&lt;doc-id&gt;/</c>.
/// The same file always gives the same bytes. A file for a document whose FSD is committed is compared with it and imported over
/// nothing (<see cref="FsdCompare"/>, phase fs layer 5, D385); before that first commit, the same file again is left alone.
/// </summary>
public static class FsdImport
{
    public const string Schema = "source-document.v4.json";

    public static string DocDir(string root, string id) => Path.Combine(root, "specs", "fsd", id);

    public static string DefaultId(string file) =>
        Regex.Replace(Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') is { Length: > 0 } s ? s : "document";

    public static JsonObject Import(StangentRepo repo, string file, string? id = null, bool flat = false, bool acceptChanges = false, IReadOnlyList<string>? allowLabels = null)
    {
        if (!File.Exists(file)) throw new StangentException("not_found", $"No file at {file}.", ExitCodes.NotFound);
        var options = ImportOptions.From(repo.Config, flat, acceptChanges, allowLabels);
        var format = ImportPipeline.ForFile(file) ?? throw (Path.GetExtension(file).Equals(".xls", StringComparison.OrdinalIgnoreCase)
            ? new StangentException("unsupported_format", "An old Excel file (.xls) isn't read. Save it as .xlsx in Excel, then import that.", ExitCodes.Usage)
            : new StangentException("unsupported_format", "Only .docx, .pdf, .xlsx and .xlsm are imported; other formats get their own converter when a real FSD needs one.", ExitCodes.Usage));
        format.CheckOptions(options);
        var archive = repo.Config.Fsd.Archive is { Name.Length: > 0, Path.Length: > 0 } a ? a
            : throw new StangentException("archive_not_set", "Set fsd.archive in stangent.yaml first: { name: <archive name>, path: <folder outside this repo>, git: false } (D16).", ExitCodes.Usage);
        id ??= DefaultId(file);
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,63}$"))
            throw new StangentException("usage", $"Document id '{id}' must be lower-case letters, digits and dashes.", ExitCodes.Usage);

        var bytes = File.ReadAllBytes(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var dir = DocDir(repo.Root, id);
        var current = FsdDocument.Facts(repo.Root, id);
        if (current is not null && !FsdDocument.Migrated(current))
            throw new StangentException("fsd_not_migrated", $"{id} was imported before the FSD became chapter files. Run `stangent fsd migrate {id}` and commit specs/fsd/{id}/ first (D383).", ExitCodes.Usage);
        // A first import not committed yet: the same file again is left alone, and another file waits for that commit (D380).
        if (current is not null && FsdDocument.Blob(repo.Root, $"specs/fsd/{id}/document.json") is null && Git.Run(repo.Root, "rev-parse --git-dir") is not null)
        {
            if ((string?)current["original"]?["hash"] == hash && flat == IsFlat(current)) return Unchanged(repo.Root, id, dir, current, hash);
            throw new StangentException("fsd_not_committed", $"specs/fsd/{id}/ isn't committed yet: read the first import and commit it (the commit approves it, D380), then import the new file to compare it with that.", ExitCodes.Usage);
        }

        var import = ImportPipeline.Run(format, bytes, options);
        var converted = AttachmentHome.Apply(import.Document, true);
        var labels = LabelRecord(repo.Root, import.Labels, null);
        var archivedPath = ArchiveOriginal(repo, archive, id, file, bytes, hash);
        var doc = Document(id, file, converted, archive.Name, archivedPath, hash, options.AttachmentChars);
        ArchiveEmbedded(repo, archive, id, converted, doc);
        if (labels is not null) doc["labels"] = labels;
        var archived = new JsonObject { ["name"] = archive.Name, ["path"] = archivedPath };
        if (current is not null)
        {
            // Compared with the committed FSD (D380, D385): no chapter changes; `fsd take` copies in what a person picks.
            var compared = FsdCompare.Compare(repo, id, doc, converted, import, hash, archived, "a new file came in");
            compared["steps"] = import.StepsJson();
            return compared;
        }
        // Committing the chapters approves a first import (D385, D388).
        FsdVersions.IssueIds(repo.Root, id, doc, doc["sections"]!.AsArray().OfType<JsonObject>());
        FsdObjects.Assign(repo.Root, id, null, doc);
        ImportProblems.Attach(doc);
        // What the file said, section by section, for the next file's comparison (D385), and which converter read it.
        doc["fileSections"] = FsdCompare.FileSections([.. doc["sections"]!.AsArray().OfType<JsonObject>()]);
        doc["converter"] = import.ConverterJson();
        FsdDocument.Validate(doc);
        FsdChapters.Verify(converted.Markdown, [.. doc["sections"]!.AsArray().OfType<JsonObject>()], repo.Config.Fsd.ExportMaxLines);
        Directory.CreateDirectory(dir);
        WriteText(dir, converted, doc);
        // The FSD as the PM edits it (phase fs layer 1, D377, D378): chapter files with each heading's id, and no document.md.
        var chapters = FsdChapters.Write(dir, converted.Markdown, [.. doc["sections"]!.AsArray().OfType<JsonObject>()], repo.Config.Fsd.ExportMaxLines);
        FsdDocument.Save(repo.Root, id, doc, newImport: true);
        ImportReport.Write(dir, doc);
        return ImportReport.AddTo(new JsonObject
        {
            ["id"] = id, ["status"] = "imported", ["hash"] = hash, ["sections"] = converted.Sections.Count,
            ["images"] = converted.Images.Count, ["problems"] = doc["problems"]!.DeepClone(), ["archive"] = archived, ["steps"] = import.StepsJson(),
            ["chapters"] = Chapters(chapters),
            ["next"] = $"Read specs/fsd/{id}/chapters/ and commit specs/fsd/{id}/: the commit approves the import, and steps read only the committed FSD (D380).",
        }, doc, $"specs/fsd/{id}");
    }

    /// <summary>The chapter files written, and any one section too long to cut, for an import's result.</summary>
    internal static JsonObject Chapters((List<string> Files, List<string> OverLimit) written) => new()
    {
        ["dir"] = "chapters", ["files"] = new JsonArray([.. written.Files.Select(f => (JsonNode)f)]),
        ["overLimit"] = new JsonArray([.. written.OverLimit.Select(f => (JsonNode)f)]),
    };

    /// <summary>Imported with <c>--flat</c>: the import says so in its problems (v4 keeps no sections to look at).</summary>
    internal static bool IsFlat(JsonObject document) =>
        (document["problems"]?.AsArray() ?? []).OfType<JsonObject>().Any(p => (string?)p["kind"] == "flat");

    static JsonObject Unchanged(string root, string id, string dir, JsonObject current, string hash)
    {
        // The report counts sections: the committed ones once there are, the facts alone before the first commit.
        JsonObject shown;
        try { shown = FsdDocument.Load(root, id); }
        catch (StangentException e) when (e.Code is "fsd_not_committed" or "not_git") { shown = current.DeepClone().AsObject(); shown["sections"] = new JsonArray(); }
        ImportReport.Write(dir, shown);
        return ImportReport.AddTo(new JsonObject { ["id"] = id, ["status"] = "unchanged", ["hash"] = hash }, shown, $"specs/fsd/{id}");
    }

    /// <summary>The labels a person allowed, for the document's record, each with who said so: from git, never typed. Null for a file with none. A reread keeps who allowed what's already recorded.</summary>
    static JsonArray? LabelRecord(string root, IReadOnlyList<ImportLabel> labels, JsonObject? current)
    {
        if (labels.Count == 0) return null;
        var kept = (current?["labels"]?.AsArray() ?? []).OfType<JsonObject>().ToDictionary(l => (string)l["id"]!, l => (string)l["allowedBy"]!, StringComparer.OrdinalIgnoreCase);
        return new JsonArray([.. labels.Select(l =>
        {
            var o = new JsonObject { ["id"] = l.Id, ["allowedBy"] = kept.GetValueOrDefault(l.Id) ?? FsdVersions.GitPerson(root) };
            if (l.Name is not null) o["name"] = l.Name;
            return (JsonNode)o;
        })]);
    }

    /// <summary>
    /// The same original, read again with today's converter (D122). Different text is compared as a new file is (<see cref="FsdCompare"/>, D385), so a converter's
    /// fixes reach earlier imports only as sections a person takes. The same text reads <c>unchanged</c>, whatever the file hash says. What was asked of the first
    /// reading holds: flat stays flat, tracked changes stay accepted, and a label already allowed stays allowed, with who allowed it.
    /// </summary>
    public static JsonObject Reread(StangentRepo repo, string id, IReadOnlyList<string>? allowLabels = null)
    {
        var dir = DocDir(repo.Root, id);
        var jsonPath = Path.Combine(dir, "document.json");
        if (!File.Exists(jsonPath)) throw new StangentException("not_found", $"No imported document {id}. A first import still waiting for approval has no current version to read again.", ExitCodes.NotFound);
        var current = FsdDocument.Load(repo.Root, id);
        var original = current["original"]!;
        var hash = (string)original["hash"]!;
        var archive = repo.Config.Fsd.Archive is { Name.Length: > 0, Path.Length: > 0 } a ? a
            : throw new StangentException("archive_not_set", "Set fsd.archive in stangent.yaml first: the original is read from there (D16).", ExitCodes.Usage);
        if (archive.Name != (string)original["archive"]!)
            throw new StangentException("archive_not_set", $"The original is in archive {(string)original["archive"]!}, and fsd.archive here is {archive.Name}.", ExitCodes.Usage);
        var path = Path.Combine(Path.GetFullPath(Path.Combine(repo.Root, archive.Path)), (string)original["path"]!);
        if (!File.Exists(path)) throw new StangentException("archive_file_missing", $"The original isn't at {path} in archive {archive.Name}.", ExitCodes.NotFound);
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash)
            throw new StangentException("archive_file_changed", $"The file at {path} isn't the original this document was imported from: its hash isn't {hash[..12]}. Nothing was read.");

        var fileName = (string)original["fileName"]!;
        var format = ImportPipeline.ForFile(fileName) ?? throw new StangentException("unsupported_format", $"No format reads {fileName} any more.", ExitCodes.Usage);
        var flat = IsFlat(current);
        ImportProblems.Upgrade(current);
        var accepted = current["problems"]!.AsArray().OfType<JsonObject>().Any(p => (string?)p["kind"] == "tracked-changes");
        var allowed = (current["labels"]?.AsArray() ?? []).OfType<JsonObject>().Select(l => (string)l["id"]!).Concat(allowLabels ?? []).ToList();
        var options = ImportOptions.From(repo.Config, flat, accepted, allowed);
        format.CheckOptions(options);
        var import = ImportPipeline.Run(format, bytes, options);
        var converted = AttachmentHome.Apply(import.Document, true);
        var doc = Document(id, fileName, converted, archive.Name, (string)original["path"]!, hash, options.AttachmentChars);
        ArchiveEmbedded(repo, archive, id, converted, doc);
        doc["title"] = current["title"]?.DeepClone();
        if (LabelRecord(repo.Root, import.Labels, current) is { } labels) doc["labels"] = labels;
        var archived = new JsonObject { ["name"] = archive.Name, ["path"] = (string)original["path"]! };

        if (converted.Markdown != FsdDocument.Markdown(repo.Root, id) || !SameSections(current, doc) || !FsdAttachments.Same(current, doc))
        {
            var compared = FsdCompare.Compare(repo, id, doc, converted, import, hash, archived, "the same file, read again by this build, gives other text");
            compared["reread"] = true;
            compared["steps"] = import.StepsJson();
            return compared;
        }

        // The same text. A newer build can still have more to say about it, and problems are in no hash.
        ImportProblems.Attach(doc);
        ImportProblems.Attach(current);
        var problemsChanged = !JsonNode.DeepEquals(doc["problems"], current["problems"]);
        if (problemsChanged)
        {
            current["problems"] = doc["problems"]!.DeepClone();
            // Only the problems move, onto the working document.json: an uncommitted `fsd take` there keeps what it wrote (fresh check, layer 5).
            var working = FsdDocument.Facts(repo.Root, id)!;
            working["problems"] = doc["problems"]!.DeepClone();
            FsdDocument.Save(repo.Root, id, working);
        }
        var same = Unchanged(repo.Root, id, dir, current, hash);
        same["reread"] = true;
        same["problemsChanged"] = problemsChanged;
        same["steps"] = import.StepsJson();
        return same;
    }

    static bool SameSections(JsonObject a, JsonObject b)
    {
        static IEnumerable<string> Shape(JsonObject d) => d["sections"]!.AsArray().OfType<JsonObject>().Select(s =>
            string.Join("\u0001", (string)s["id"]!, (string)s["idKind"]!, (string)s["title"]!, (int)s["level"]!, (string?)s["parent"] ?? "", (int)s["line"]!, (string)s["textHash"]!));
        return Shape(a).SequenceEqual(Shape(b));
    }

    /// <summary>The document's record, every section's permanent id still to come.</summary>
    static JsonObject Document(string id, string file, ConvertedDocument converted, string archive, string archivedPath, string hash, int attachmentChars)
    {
        var doc = Record(id, file, converted, archive, archivedPath, hash);
        FsdAttachments.AddTo(doc, converted, attachmentChars);
        return doc;
    }

    static JsonObject Record(string id, string file, ConvertedDocument converted, string archive, string archivedPath, string hash) => new()
    {
        ["schemaVersion"] = FsdDocument.SchemaVersion,
        ["id"] = id,
        ["title"] = converted.Title ?? Path.GetFileNameWithoutExtension(file),
        ["original"] = new JsonObject { ["archive"] = archive, ["path"] = archivedPath, ["hash"] = hash, ["fileName"] = Path.GetFileName(file) },
        ["sections"] = new JsonArray([.. converted.Sections.Select(s => (JsonNode)SectionJson(s))]),
        ["images"] = new JsonArray([.. converted.Images.Select(i =>
        {
            var o = new JsonObject { ["file"] = i.Name };
            if (i.Caption is not null) o["caption"] = i.Caption;
            return (JsonNode)o;
        })]),
        ["problems"] = ImportProblems.Of(converted.Problems, [.. converted.Sections.Select(SectionJson)]),
    };

    /// <summary>One section as <c>document.json</c> holds it, its permanent id still to come.</summary>
    internal static JsonObject SectionJson(FsdSection s)
    {
        var o = new JsonObject
        {
            ["id"] = s.Id, ["idKind"] = s.IdKind, ["title"] = s.Title, ["level"] = s.Level, ["line"] = s.Line,
            ["text"] = s.Text, ["textHash"] = CanonicalJson.Hash(s.Text),
        };
        if (s.Parent is not null) o["parent"] = s.Parent;
        return o;
    }

    /// <summary>The images and the attachments into the document's folder (its text is chapter files, <see cref="FsdChapters"/>).</summary>
    internal static void WriteText(string dir, ConvertedDocument converted, JsonObject document)
    {
        FsdAttachments.Write(dir, converted, document);
        foreach (var img in converted.Images)
        {
            Directory.CreateDirectory(Path.Combine(dir, "images"));
            File.WriteAllBytes(Path.Combine(dir, img.Name), img.Bytes);
        }
    }

    /// <summary>Copies the original into the archive as <c>&lt;doc-id&gt;/&lt;hash 12&gt;-&lt;file name&gt;</c>; a git archive also commits it.</summary>
    static string ArchiveOriginal(StangentRepo repo, StangentConfig.ArchiveConfig archive, string id, string file, byte[] bytes, string hash) =>
        ToArchive(repo, archive, $"{id}/{hash[..12]}-{Path.GetFileName(file)}", bytes);

    /// <summary>I.7: an embedded file as it came goes to the archive under its own hash, and its record says where. The record's hash and path are what they were, so nothing that reads them moves.</summary>
    static void ArchiveEmbedded(StangentRepo repo, StangentConfig.ArchiveConfig archive, string id, ConvertedDocument converted, JsonObject doc)
    {
        var records = (doc["attachments"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        for (var i = 0; i < converted.Attachments.Count; i++)
            foreach (var output in converted.Attachments[i].Outputs.Where(EmbeddedFiles.IsOriginal))
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(output.Bytes));
                var rel = ToArchive(repo, archive, $"{id}/attachments/{hash[..12]}-{Path.GetFileName(output.Path)}", output.Bytes);
                var file = records[i]["files"]!.AsArray().OfType<JsonObject>().First(f => (string?)f["file"] == output.Path);
                file["archived"] = rel;
            }
    }

    static string ToArchive(StangentRepo repo, StangentConfig.ArchiveConfig archive, string rel, byte[] bytes)
    {
        var root = Path.GetFullPath(Path.Combine(repo.Root, archive.Path));
        var target = Path.Combine(root, rel);
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        if (archive.Git)
        {
            if (Git.Run(root, "rev-parse --git-dir") is null)
                throw new StangentException("archive_not_git", $"fsd.archive.git is true, but {root} isn't a git repo.");
            Git.Run(root, $"add -- \"{rel}\"");
            if (Git.Run(root, "diff --cached --quiet") is null)
                Git.Run(root, $"commit -q -m \"stangent: archive {rel}\"");
        }
        return rel;
    }

    /// <summary>Every document a step can read, its sections from the committed chapters (<see cref="FsdDocument.All"/>).</summary>
    public static IEnumerable<JsonObject> Documents(string root) => FsdDocument.All(root);
}
