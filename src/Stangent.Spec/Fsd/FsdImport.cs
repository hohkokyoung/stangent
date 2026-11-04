using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd import</c> (Phase 2b step 1, D111): converts a <c>.docx</c>, <c>.pdf</c>, <c>.xlsx</c> or <c>.xlsm</c> to Markdown by code, archives the original
/// (D16) and records the document's sections as a <c>source-document</c> (v3) in <c>specs/fsd/&lt;doc-id&gt;/</c>.
/// The same file always gives the same bytes; a file already imported (same content hash) is left alone. A different file
/// for a document already here comes in as a candidate beside it (<see cref="FsdVersions"/>, D117).
/// </summary>
public static class FsdImport
{
    public const string Schema = "source-document.v3.json";

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
        var jsonPath = Path.Combine(dir, "document.json");
        var current = File.Exists(jsonPath) ? JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject : null;
        // The same file is imported once, unless --flat asks for a different shape than what's recorded.
        if (current is not null && (string?)current["original"]?["hash"] == hash
            && flat == (current["sections"]?.AsArray() is { Count: 1 } only && (string?)only[0]?["id"] == FsdSections.FlatId))
        {
            var added = FsdVersions.EnsureIds(repo.Root, id, current);
            return Unchanged(repo.Root, id, dir, current, hash, added);
        }

        var import = ImportPipeline.Run(format, bytes, options);
        var converted = import.Document;
        var labels = LabelRecord(repo.Root, import.Labels, null);
        var archivedPath = ArchiveOriginal(repo, archive, id, file, bytes, hash);
        var doc = Document(id, file, converted, archive.Name, archivedPath, hash, options.AttachmentChars);
        if (labels is not null) doc["labels"] = labels;
        var archived = new JsonObject { ["name"] = archive.Name, ["path"] = archivedPath };
        if (current is not null)
        {
            var offered = FsdVersions.Offer(repo.Root, id, current, doc, converted, hash, flat, archived);
            offered["steps"] = import.StepsJson();
            return offered;
        }
        if (StangentComponents.IsOn(repo.Config, FsdVersions.FirstImportApproval))
        {
            var waiting = FsdVersions.OfferFirst(repo.Root, id, doc, converted, hash, flat, archived);
            waiting["steps"] = import.StepsJson();
            return waiting;
        }
        // The switch went off with a first import still waiting: this one goes straight in, and that one is replaced.
        FsdVersions.ReplaceFirst(repo.Root, id, hash);

        FsdVersions.IssueIds(repo.Root, id, doc, doc["sections"]!.AsArray().OfType<JsonObject>());
        FsdObjects.Assign(repo.Root, id, null, doc);
        ImportProblems.Attach(doc);
        var errors = SchemaRegistry.Embedded.Validate(Schema, doc.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_document", "The imported document broke source-document v3 (a stangent bug).", ExitCodes.Failed, errors);
        Directory.CreateDirectory(dir);
        WriteText(dir, converted, doc);
        File.WriteAllText(jsonPath, CanonicalJson.Serialize(doc));
        ImportReport.Write(dir, doc);
        return ImportReport.AddTo(new JsonObject
        {
            ["id"] = id, ["status"] = "imported", ["hash"] = hash, ["sections"] = converted.Sections.Count,
            ["images"] = converted.Images.Count, ["problems"] = doc["problems"]!.DeepClone(), ["archive"] = archived, ["steps"] = import.StepsJson(),
        }, doc, $"specs/fsd/{id}");
    }

    static JsonObject Unchanged(string root, string id, string dir, JsonObject current, string hash, int added)
    {
        ImportReport.Write(dir, current);
        var same = ImportReport.AddTo(new JsonObject { ["id"] = id, ["status"] = "unchanged", ["hash"] = hash }, current, $"specs/fsd/{id}");
        if (added > 0) same["permanentIdsAdded"] = added;
        if (FsdVersions.PendingHash(root, id) is { } pending)
        {
            same["candidate"] = pending;
            same["note"] = $"This file is already the current version, and candidate {pending[..12]} is still waiting: `stangent fsd promote {id}` would apply it. To throw it away, run `stangent fsd discard {id}`.";
        }
        return same;
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
    /// The same original, read again with today's converter (D122). Different text comes in as a candidate through the same flow as a new file, so 2.1's fixes reach
    /// earlier imports and saved work stays until a person promotes. The same text reads <c>unchanged</c>, whatever the file hash says. What was asked of the first
    /// reading holds: flat stays flat, tracked changes stay accepted, and a label already allowed stays allowed, with who allowed it.
    /// </summary>
    public static JsonObject Reread(StangentRepo repo, string id, IReadOnlyList<string>? allowLabels = null)
    {
        var dir = DocDir(repo.Root, id);
        var jsonPath = Path.Combine(dir, "document.json");
        if (!File.Exists(jsonPath)) throw new StangentException("not_found", $"No imported document {id}. A first import still waiting for approval has no current version to read again.", ExitCodes.NotFound);
        var current = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
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
        if (FsdVersions.PendingHash(repo.Root, id) is { } waiting && waiting != hash)
            throw new StangentException("candidate_waiting", $"Candidate {waiting[..12]} of {id}, from another file, is waiting. Promote or discard it first (`stangent fsd promote {id}`, `stangent fsd discard {id}`): reading the original again would replace it.");

        var fileName = (string)original["fileName"]!;
        var format = ImportPipeline.ForFile(fileName) ?? throw new StangentException("unsupported_format", $"No format reads {fileName} any more.", ExitCodes.Usage);
        var flat = current["sections"]?.AsArray() is { Count: 1 } only && (string?)only[0]?["id"] == FsdSections.FlatId;
        ImportProblems.Upgrade(current);
        var accepted = current["problems"]!.AsArray().OfType<JsonObject>().Any(p => (string?)p["kind"] == "tracked-changes");
        var allowed = (current["labels"]?.AsArray() ?? []).OfType<JsonObject>().Select(l => (string)l["id"]!).Concat(allowLabels ?? []).ToList();
        var options = ImportOptions.From(repo.Config, flat, accepted, allowed);
        format.CheckOptions(options);
        var import = ImportPipeline.Run(format, bytes, options);
        var converted = import.Document;
        var doc = Document(id, fileName, converted, archive.Name, (string)original["path"]!, hash, options.AttachmentChars);
        doc["title"] = current["title"]?.DeepClone();
        if (LabelRecord(repo.Root, import.Labels, current) is { } labels) doc["labels"] = labels;
        var archived = new JsonObject { ["name"] = archive.Name, ["path"] = (string)original["path"]! };

        if (converted.Markdown != File.ReadAllText(Path.Combine(dir, "document.md")) || !SameSections(current, doc) || !FsdAttachments.Same(current, doc))
        {
            var offered = FsdVersions.Offer(repo.Root, id, current, doc, converted, hash, flat, archived, "the same file, read again by a newer build, gives other text");
            offered["reread"] = true;
            offered["steps"] = import.StepsJson();
            return offered;
        }

        // The same text. A newer build can still have more to say about it, and problems are in no hash.
        var added = FsdVersions.EnsureIds(repo.Root, id, current);
        ImportProblems.Attach(doc);
        ImportProblems.Attach(current);
        var problemsChanged = !JsonNode.DeepEquals(doc["problems"], current["problems"]);
        if (problemsChanged)
        {
            current["problems"] = doc["problems"]!.DeepClone();
            FsdVersions.Validate(current);
            File.WriteAllText(jsonPath, CanonicalJson.Serialize(current));
        }
        var same = Unchanged(repo.Root, id, dir, current, hash, added);
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
        ["schemaVersion"] = 3,
        ["id"] = id,
        ["title"] = converted.Title ?? Path.GetFileNameWithoutExtension(file),
        ["original"] = new JsonObject { ["archive"] = archive, ["path"] = archivedPath, ["hash"] = hash, ["fileName"] = Path.GetFileName(file) },
        ["markdown"] = "document.md",
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

    /// <summary>The Markdown, the images and the attachments into a folder.</summary>
    internal static void WriteText(string dir, ConvertedDocument converted, JsonObject document)
    {
        FsdAttachments.Write(dir, converted, document);
        File.WriteAllText(Path.Combine(dir, "document.md"), converted.Markdown);
        foreach (var img in converted.Images)
        {
            Directory.CreateDirectory(Path.Combine(dir, "images"));
            File.WriteAllBytes(Path.Combine(dir, img.Name), img.Bytes);
        }
    }

    /// <summary>Copies the original into the archive as <c>&lt;doc-id&gt;/&lt;hash 12&gt;-&lt;file name&gt;</c>; a git archive also commits it.</summary>
    static string ArchiveOriginal(StangentRepo repo, StangentConfig.ArchiveConfig archive, string id, string file, byte[] bytes, string hash)
    {
        var root = Path.GetFullPath(Path.Combine(repo.Root, archive.Path));
        var rel = $"{id}/{hash[..12]}-{Path.GetFileName(file)}";
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

    public static IEnumerable<JsonObject> Documents(string root)
    {
        var dir = Path.Combine(root, "specs", "fsd");
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
            if (File.Exists(Path.Combine(d, "document.json")) && JsonNode.Parse(File.ReadAllText(Path.Combine(d, "document.json"))) is JsonObject o)
                yield return o;
    }
}
