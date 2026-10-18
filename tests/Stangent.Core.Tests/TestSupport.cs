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
