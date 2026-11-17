using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd migrate &lt;doc&gt;</c> (phase fs layer 1, D389 call 3): a document imported before phase fs becomes chapter files.
/// Its <c>document.md</c> is cut into <c>chapters/</c> (<see cref="FsdChapters"/>), each heading with its permanent id, and removed.
/// Refused with uncommitted changes under <c>specs/fsd/&lt;doc&gt;/</c>, so the migration is one change a person can read and commit,
/// and refused when <c>document.md</c> was edited by hand, since that edit never reached a step and migrating would make it the FSD.
/// Its <c>document.json</c> keeps only the import's facts (<c>source-document</c> v4, phase fs layer 2, D383): from the commit after
/// it, every step reads the sections from the committed chapters. A document that already has chapters (imported during layer 1)
/// only drops its sections. Code only. Run twice, the second does nothing.
/// </summary>
public static class FsdMigrate
{
    public static JsonObject Run(StangentRepo repo, string doc)
    {
        var root = repo.Root;
        var dir = FsdImport.DocDir(root, doc);
        var jsonPath = Path.Combine(dir, "document.json");
        if (!File.Exists(jsonPath)) throw new StangentException("not_found", $"No imported document {doc}.", ExitCodes.NotFound);
        var md = Path.Combine(dir, "document.md");
        var facts = FsdDocument.Facts(root, doc)!;
        if (!File.Exists(md))
        {
            if (!FsdChapters.Has(dir)) throw new StangentException("not_found", $"{doc} has neither chapter files nor a document.md to make them from: import the file again.", ExitCodes.NotFound);
            if (FsdDocument.Migrated(facts)) return new JsonObject { ["doc"] = doc, ["status"] = "unchanged", ["note"] = $"{doc} is already chapter files." };
        }
        if (Git.Run(root, "rev-parse --git-dir") is null)
            throw new StangentException("not_git", "Migrating writes the FSD as files a commit approves (D380), and this folder isn't a git checkout.", ExitCodes.Usage);
        var rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
        if (Git.Run(root, $"status --porcelain --untracked-files=all -- \"{rel}\"") is { Length: > 0 } dirty)
            throw new StangentException("uncommitted_changes", $"{rel} has uncommitted changes. Commit or undo them first, so the migration is a change of its own.", ExitCodes.Usage,
                [.. dirty.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())]);

        var current = facts;
        if (!File.Exists(md))
        {
            // Chapters written while document.json still held the sections (layer 1): they are the FSD now, and only the copy goes.
            var given = FsdVersions.EnsureIds(root, doc, current);
            FsdDocument.Save(root, doc, current, newImport: true);
            var moved = new JsonObject
            {
                ["doc"] = doc, ["status"] = "migrated", ["removed"] = new JsonArray("document.json sections"),
                ["next"] = $"Commit {rel}/: from now on every step reads the sections from the committed chapter files (D383).",
            };
            if (given > 0) moved["permanentIdsAdded"] = given;
            return moved;
        }
        var text = File.ReadAllText(md);
        if (FsdHandEdit.Compare(text, current) is { Count: > 0 } edited)
            throw new StangentException("document_edited", $"{rel}/document.md was edited by hand at {(edited.Count == 1 ? $"section {edited[0]}" : $"{edited.Count} sections, first {edited[0]}")}. That edit never reached a step, and migrating would make it the FSD: undo it (`git checkout -- {rel}/document.md`) and migrate, then make the change in the chapter file.", ExitCodes.Usage);
        var added = FsdVersions.EnsureIds(root, doc, current);
        var written = FsdChapters.Write(dir, text, [.. current["sections"]!.AsArray().OfType<JsonObject>()], repo.Config.Fsd.ExportMaxLines);
        File.Delete(md);
        FsdDocument.Save(root, doc, current, newImport: true);
        var result = new JsonObject
        {
            ["doc"] = doc, ["status"] = "migrated", ["chapters"] = FsdImport.Chapters(written), ["removed"] = new JsonArray("document.md", "document.json sections"),
            ["next"] = $"Commit {rel}/: from now on the chapter files are the FSD, and you edit them (D377).",
        };
        if (added > 0) result["permanentIdsAdded"] = added;
        return result;
    }
}
