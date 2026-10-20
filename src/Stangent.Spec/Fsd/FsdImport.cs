using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd import</c> (Phase 2b step 1): converts a <c>.docx</c> to Markdown by code, archives the original
/// (D16) and records the document's sections as a <c>source-document</c> (v1) in <c>specs/fsd/&lt;doc-id&gt;/</c>.
/// The same file always gives the same bytes; a file already imported (same content hash) is left alone.
/// </summary>
public static class FsdImport
{
    public const string Schema = "source-document.v1.json";

    public static string DocDir(string root, string id) => Path.Combine(root, "specs", "fsd", id);

    public static string DefaultId(string file) =>
        Regex.Replace(Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') is { Length: > 0 } s ? s : "document";

    public static JsonObject Import(StangentRepo repo, string file, string? id = null)
    {
        if (!File.Exists(file)) throw new StangentException("not_found", $"No file at {file}.", ExitCodes.NotFound);
        if (!file.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            throw new StangentException("unsupported_format", "Only .docx is imported for now (Phase 2b); other formats get their own converter when a real FSD needs one.", ExitCodes.Usage);
        var archive = repo.Config.Fsd.Archive is { Name.Length: > 0, Path.Length: > 0 } a ? a
            : throw new StangentException("archive_not_set", "Set fsd.archive in stangent.yaml first: { name: <archive name>, path: <folder outside this repo>, git: false } (D16).", ExitCodes.Usage);
        id ??= DefaultId(file);
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,63}$"))
            throw new StangentException("usage", $"Document id '{id}' must be lower-case letters, digits and dashes.", ExitCodes.Usage);

        var bytes = File.ReadAllBytes(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var dir = DocDir(repo.Root, id);
        var jsonPath = Path.Combine(dir, "document.json");
        if (File.Exists(jsonPath) && JsonNode.Parse(File.ReadAllText(jsonPath))?["original"]?["hash"]?.GetValue<string>() == hash)
            return new JsonObject { ["id"] = id, ["status"] = "unchanged", ["hash"] = hash };
        var replaced = File.Exists(jsonPath);

        var converted = DocxConverter.Convert(bytes);
        var archivedPath = ArchiveOriginal(repo, archive, id, file, bytes, hash);

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "document.md"), converted.Markdown);
        foreach (var img in converted.Images)
        {
            Directory.CreateDirectory(Path.Combine(dir, "images"));
            File.WriteAllBytes(Path.Combine(dir, img.Name), img.Bytes);
        }
        var doc = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["title"] = converted.Title ?? Path.GetFileNameWithoutExtension(file),
            ["original"] = new JsonObject { ["archive"] = archive.Name, ["path"] = archivedPath, ["hash"] = hash, ["fileName"] = Path.GetFileName(file) },
            ["markdown"] = "document.md",
            ["sections"] = new JsonArray([.. converted.Sections.Select(s =>
            {
                var o = new JsonObject
                {
                    ["id"] = s.Id, ["idKind"] = s.IdKind, ["title"] = s.Title, ["level"] = s.Level, ["line"] = s.Line,
                    ["text"] = s.Text, ["textHash"] = CanonicalJson.Hash(s.Text),
                };
                if (s.Parent is not null) o["parent"] = s.Parent;
                return (JsonNode)o;
            })]),
            ["images"] = new JsonArray([.. converted.Images.Select(i =>
            {
                var o = new JsonObject { ["file"] = i.Name };
                if (i.Caption is not null) o["caption"] = i.Caption;
                return (JsonNode)o;
            })]),
            ["problems"] = new JsonArray([.. converted.Problems.Select(p => (JsonNode)p)]),
        };
        var errors = SchemaRegistry.Embedded.Validate(Schema, doc.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_document", "The imported document broke source-document v1 (a stangent bug).", ExitCodes.Failed, errors);
        File.WriteAllText(jsonPath, CanonicalJson.Serialize(doc));
        return new JsonObject
        {
            ["id"] = id, ["status"] = replaced ? "replaced" : "imported", ["hash"] = hash, ["sections"] = converted.Sections.Count,
            ["images"] = converted.Images.Count, ["problems"] = doc["problems"]!.DeepClone(),
            ["archive"] = new JsonObject { ["name"] = archive.Name, ["path"] = archivedPath },
        };
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
