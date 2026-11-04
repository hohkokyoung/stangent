using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd import</c> (Phase 2b step 1, D111): converts a <c>.docx</c> or <c>.pdf</c> to Markdown by code, archives the original
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

    public static JsonObject Import(StangentRepo repo, string file, string? id = null, bool flat = false, bool acceptChanges = false)
    {
        if (!File.Exists(file)) throw new StangentException("not_found", $"No file at {file}.", ExitCodes.NotFound);
        var options = new ImportOptions(flat, acceptChanges);
        var format = ImportPipeline.ForFile(file) ?? throw (Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? new StangentException("unsupported_format", "Excel import is decided (D111) but isn't built yet. Only .docx and .pdf are imported for now.", ExitCodes.Usage)
            : new StangentException("unsupported_format", "Only .docx and .pdf are imported; other formats get their own converter when a real FSD needs one.", ExitCodes.Usage));
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
            var same = new JsonObject { ["id"] = id, ["status"] = "unchanged", ["hash"] = hash };
            if (added > 0) same["permanentIdsAdded"] = added;
            if (FsdVersions.PendingHash(repo.Root, id) is { } pending)
            {
                same["candidate"] = pending;
                same["note"] = $"This file is already the current version, and candidate {pending[..12]} is still waiting: `stangent fsd promote {id}` would apply it. To throw it away, run `stangent fsd discard {id}`.";
            }
            return same;
        }

        var import = ImportPipeline.Run(format, bytes, options);
        var converted = import.Document;
        var archivedPath = ArchiveOriginal(repo, archive, id, file, bytes, hash);
        var doc = Document(id, file, converted, archive.Name, archivedPath, hash);
        var archived = new JsonObject { ["name"] = archive.Name, ["path"] = archivedPath };
        if (current is not null)
        {
            var offered = FsdVersions.Offer(repo.Root, id, current, doc, converted, hash, flat, archived);
            offered["steps"] = import.StepsJson();
            return offered;
        }

        FsdVersions.IssueIds(repo.Root, id, doc, doc["sections"]!.AsArray().OfType<JsonObject>());
        ImportProblems.Attach(doc);
        var errors = SchemaRegistry.Embedded.Validate(Schema, doc.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_document", "The imported document broke source-document v3 (a stangent bug).", ExitCodes.Failed, errors);
        Directory.CreateDirectory(dir);
        WriteText(dir, converted);
        File.WriteAllText(jsonPath, CanonicalJson.Serialize(doc));
        return new JsonObject
        {
            ["id"] = id, ["status"] = "imported", ["hash"] = hash, ["sections"] = converted.Sections.Count,
            ["images"] = converted.Images.Count, ["problems"] = doc["problems"]!.DeepClone(), ["archive"] = archived, ["steps"] = import.StepsJson(),
        };
    }

    /// <summary>The document's record, every section's permanent id still to come.</summary>
    static JsonObject Document(string id, string file, ConvertedDocument converted, string archive, string archivedPath, string hash) => new()
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

    /// <summary>The Markdown and the images into a folder.</summary>
    internal static void WriteText(string dir, ConvertedDocument converted)
    {
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
