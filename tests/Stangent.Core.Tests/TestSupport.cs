using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Store;

namespace Stangent.Core.Tests;

/// <summary>A throwaway folder with <c>stangent init</c> run in it.</summary>
sealed class TempRepo : IDisposable
{
    public TempRepo(string? config = null)
    {
        Root = Directory.CreateTempSubdirectory("stangent-test-").FullName;
        if (config is not null) File.WriteAllText(Path.Combine(Root, StangentRepo.ConfigFile), config);
        Repo = StangentRepo.Init(Root).Repo;
    }

    public string Root { get; }
    public StangentRepo Repo { get; }

    public FileArtifactStore Store(SchemaRegistry? schemas = null) => new(Repo, schemas ?? SchemaRegistry.Embedded);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

static class Artifacts
{
    public static JsonObject Note(string id, string? title = null, params (string Rel, string Target)[] links)
    {
        var artifact = new JsonObject { ["id"] = id, ["type"] = "note", ["schemaVersion"] = 1 };
        if (title is not null) artifact["title"] = title;
        if (links.Length > 0)
            artifact["links"] = new JsonArray([.. links.Select(l => (JsonNode)new JsonObject { ["rel"] = l.Rel, ["target"] = l.Target })]);
        return artifact;
    }
}

/// <summary>A new version taken all the way in, as a person would: imported, anything doubtful settled, promoted (D117).</summary>
static class FsdNewVersion
{
    public static JsonObject Take(StangentRepo repo, string file, string doc = "shop")
    {
        var result = Stangent.Spec.Fsd.FsdImport.Import(repo, file);
        if ((string)result["status"]! != "candidate") return result;
        foreach (var e in Stangent.Spec.Fsd.FsdVersions.Diff(repo.Root, doc)["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful").ToList())
            Stangent.Spec.Fsd.FsdVersions.Resolve(repo.Root, doc, (string?)e["to"] ?? (string)e["permanentId"]!, null, e["to"] is not null, e["to"] is null, "Pat");
        return Stangent.Spec.Fsd.FsdVersions.Promote(repo, doc, "Pat");
    }
}

sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

static class RepoRoot
{
    /// <summary>The stangent repo this test assembly was built from.</summary>
    public static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "roadmap.md"))) return dir.FullName;
        throw new InvalidOperationException("roadmap.md not found above the test binaries");
    }
}

/// <summary>A document's chapter files (phase fs layer 1), as tests read and edit them.</summary>
static class Chapters
{
    /// <summary>The whole document as document.md held it: the chapters joined, markers off.</summary>
    public static string Text(string docDir) => Stangent.Spec.Fsd.FsdChapters.Markdown(docDir)!;

    public static byte[] Bytes(string docDir) => System.Text.Encoding.UTF8.GetBytes(Text(docDir));

    /// <summary>Every chapter file, name and bytes, for "nothing changed" checks.</summary>
    public static Dictionary<string, byte[]> Files(string docDir) =>
        Directory.EnumerateFiles(Stangent.Spec.Fsd.FsdChapters.Dir(docDir), "*.md").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);

    /// <summary>Replaces <paramref name="from"/> in the one chapter file that holds it.</summary>
    public static void Edit(string docDir, string from, string to)
    {
        var file = Assert.Single(Directory.EnumerateFiles(Stangent.Spec.Fsd.FsdChapters.Dir(docDir), "*.md"), f => File.ReadAllText(f).Contains(from, StringComparison.Ordinal));
        File.WriteAllText(file, File.ReadAllText(file).Replace(from, to, StringComparison.Ordinal));
    }
}
