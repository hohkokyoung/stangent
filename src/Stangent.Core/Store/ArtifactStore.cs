using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Stangent.Core.Store;

/// <summary>An artifact's address, written <c>type/id</c>.</summary>
public readonly record struct ArtifactRef(string Type, string Id)
{
    public override string ToString() => $"{Type}/{Id}";

    public static ArtifactRef Parse(string text)
    {
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash == text.Length - 1 || text.IndexOf('/', slash + 1) >= 0)
            throw new StangentException("invalid_ref", $"'{text}' is not a reference of the form type/id.", ExitCodes.Usage);
        return new ArtifactRef(text[..slash], text[(slash + 1)..]);
    }

    public static ArtifactRef Of(JsonObject artifact) =>
        new((string?)artifact["type"] ?? "", (string?)artifact["id"] ?? "");
}

public sealed record IndexEntry(string Ref, int SchemaVersion, string? Title, string Path, string Hash);

public sealed record PutResult(string Ref, string Hash, bool Changed);

public sealed record ArtifactQuery(string? Type = null, string? LinksTo = null, string? Rel = null);

/// <summary>
/// The store interface (D8). Every product reads and writes artifacts through this, via the
/// CLI (D23); backends can change without the products noticing.
/// </summary>
public interface IArtifactStore
{
    JsonObject Get(ArtifactRef artifact);
    bool Exists(ArtifactRef artifact);
    /// <summary>Errors that would make <see cref="Put"/> fail. <paramref name="pending"/> counts as existing link targets.</summary>
    IReadOnlyList<string> Validate(JsonObject artifact, IReadOnlySet<ArtifactRef>? pending = null);
    PutResult Put(JsonObject artifact);
    PutResult Link(ArtifactRef source, string rel, ArtifactRef target);
    IReadOnlyList<IndexEntry> Query(ArtifactQuery query);
    int RebuildIndex();
    /// <summary>The index's full contents in a stable order, to prove a rebuild reproduces it.</summary>
    JsonObject DumpIndex();
}

/// <summary>
/// First backend (D7): one canonical JSON file per artifact at <c>&lt;store root&gt;/&lt;type&gt;/&lt;id&gt;.json</c>,
/// plus a SQLite index that is purely derived from those files. The index is rebuilt whenever
/// the files changed since it was built, so it can never disagree with them.
/// </summary>
public sealed class FileArtifactStore(StangentRepo repo, SchemaRegistry schemas) : IArtifactStore
{
    const string BaseSchema = "artifact-base.v1.json";

    public JsonObject Get(ArtifactRef artifact)
    {
        var path = PathOf(artifact);
        if (!File.Exists(path))
            throw new StangentException("not_found", $"No artifact {artifact}.", ExitCodes.NotFound);
        return Parse(File.ReadAllText(path), path);
    }

    public bool Exists(ArtifactRef artifact) => IsValidRef(artifact) && File.Exists(PathOf(artifact));

    public IReadOnlyList<string> Validate(JsonObject artifact, IReadOnlySet<ArtifactRef>? pending = null)
    {
        var errors = schemas.Validate(BaseSchema, artifact.ToJsonString()).ToList();
        if (errors.Count > 0) return errors;

        var type = (string)artifact["type"]!;
        var typeSchema = $"types/{type}.v{(int)artifact["schemaVersion"]!}.json";
        if (schemas.Has(typeSchema))
            errors.AddRange(schemas.Validate(typeSchema, (artifact["data"] ?? new JsonObject()).ToJsonString())
                .Select(e => "data" + e.TrimStart('#')));

        var seen = new HashSet<(string, string)>();
        foreach (var link in artifact["links"]?.AsArray() ?? [])
        {
            var rel = (string)link!["rel"]!;
            var target = (string)link["target"]!;
            if (!seen.Add((rel, target))) errors.Add($"links: duplicate {rel} → {target}");
            var targetRef = ArtifactRef.Parse(target);
            if (!Exists(targetRef) && pending?.Contains(targetRef) != true)
                errors.Add($"links: target {target} does not exist");
        }
        return errors;
    }

    public PutResult Put(JsonObject artifact)
    {
        var errors = Validate(artifact);
        var reference = ArtifactRef.Of(artifact);
        if (errors.Count > 0)
            throw new StangentException("invalid_artifact", $"{reference} is not valid.", ExitCodes.Failed, errors);

        var text = CanonicalJson.Serialize(artifact);
        var path = PathOf(reference);
        var changed = !File.Exists(path) || File.ReadAllText(path) != text;
        if (changed)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        return new PutResult(reference.ToString(), CanonicalJson.Hash(text), changed);
    }

    public PutResult Link(ArtifactRef source, string rel, ArtifactRef target)
    {
        var artifact = Get(source);
        var links = artifact["links"]?.AsArray() ?? [];
        if (!links.Any(l => (string?)l!["rel"] == rel && (string?)l["target"] == target.ToString()))
            links.Add(new JsonObject { ["rel"] = rel, ["target"] = target.ToString() });
        artifact["links"] = links.DeepClone();
        return Put(artifact);
    }

    public IReadOnlyList<IndexEntry> Query(ArtifactQuery query)
    {
        EnsureIndexFresh();
        using var db = Open(repo.IndexPath);
        using var cmd = db.CreateCommand();
        var where = new List<string>();
        if (query.Type is not null) { where.Add("a.type = $type"); cmd.Parameters.AddWithValue("$type", query.Type); }
        if (query.LinksTo is not null || query.Rel is not null)
        {
            var link = "EXISTS (SELECT 1 FROM links l WHERE l.source = a.ref";
            if (query.LinksTo is not null) { link += " AND l.target = $target"; cmd.Parameters.AddWithValue("$target", query.LinksTo); }
            if (query.Rel is not null) { link += " AND l.rel = $rel"; cmd.Parameters.AddWithValue("$rel", query.Rel); }
            where.Add(link + ")");
        }
        cmd.CommandText = "SELECT ref, schema_version, title, path, hash FROM artifacts a"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY ref";
        using var reader = cmd.ExecuteReader();
        var results = new List<IndexEntry>();
        while (reader.Read())
            results.Add(new IndexEntry(reader.GetString(0), reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return results;
    }

    public int RebuildIndex()
    {
        var files = StoreFiles();
        var artifacts = new List<(ArtifactRef Ref, JsonObject Artifact, string Path, string Hash)>();
        var errors = new List<string>();
        foreach (var path in files)
        {
            var rel = Relative(path);
            try
            {
                var artifact = Parse(File.ReadAllText(path), path);
                var reference = ArtifactRef.Of(artifact);
                if (PathOf(reference) != path)
                    errors.Add($"{rel}: holds {reference}, which belongs at {Relative(PathOf(reference))}");
                artifacts.Add((reference, artifact, rel, CanonicalJson.Hash(CanonicalJson.Serialize(artifact))));
            }
            catch (StangentException e)
            {
                errors.Add($"{rel}: {e.Message}");
            }
        }

        var all = artifacts.Select(a => a.Ref).ToHashSet();
        foreach (var (reference, artifact, rel, _) in artifacts)
            errors.AddRange(Validate(artifact, all).Select(e => $"{rel}: {e}"));
        if (errors.Count > 0)
            throw new StangentException("invalid_store", $"{errors.Count} problem(s) in the store; the index was not rebuilt.", ExitCodes.Failed, errors);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(repo.IndexPath)!);
        var temp = repo.IndexPath + ".tmp";
        File.Delete(temp);
        using (var db = Open(temp))
        {
            Exec(db, """
                CREATE TABLE artifacts (ref TEXT PRIMARY KEY, type TEXT NOT NULL, id TEXT NOT NULL,
                  schema_version INTEGER NOT NULL, title TEXT, path TEXT NOT NULL, hash TEXT NOT NULL, json TEXT NOT NULL);
                CREATE TABLE links (source TEXT NOT NULL, rel TEXT NOT NULL, target TEXT NOT NULL,
                  PRIMARY KEY (source, rel, target));
                CREATE INDEX links_target ON links (target);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                """);
            using var tx = db.BeginTransaction();
            foreach (var (reference, artifact, rel, hash) in artifacts)
            {
                Exec(db, "INSERT INTO artifacts VALUES ($ref, $type, $id, $version, $title, $path, $hash, $json)",
                    ("$ref", reference.ToString()), ("$type", reference.Type), ("$id", reference.Id),
                    ("$version", (int)artifact["schemaVersion"]!), ("$title", (string?)artifact["title"]),
                    ("$path", rel), ("$hash", hash), ("$json", CanonicalJson.SerializeCompact(artifact)));
                foreach (var link in artifact["links"]?.AsArray() ?? [])
                    Exec(db, "INSERT INTO links VALUES ($source, $rel, $target)",
                        ("$source", reference.ToString()), ("$rel", (string)link!["rel"]!), ("$target", (string)link["target"]!));
            }
            Exec(db, "INSERT INTO meta VALUES ('fingerprint', $fp)", ("$fp", Fingerprint(files)));
            tx.Commit();
        }
        File.Move(temp, repo.IndexPath, overwrite: true);
        return artifacts.Count;
    }

    public JsonObject DumpIndex()
    {
        EnsureIndexFresh();
        using var db = Open(repo.IndexPath);
        var artifacts = new JsonArray();
        foreach (var row in Rows(db, "SELECT ref, schema_version, title, path, hash FROM artifacts ORDER BY ref"))
            artifacts.Add(new JsonObject
            {
                ["ref"] = row.GetString(0), ["schemaVersion"] = row.GetInt32(1),
                ["title"] = row.IsDBNull(2) ? null : row.GetString(2), ["path"] = row.GetString(3), ["hash"] = row.GetString(4),
            });
        var links = new JsonArray();
        foreach (var row in Rows(db, "SELECT source, rel, target FROM links ORDER BY source, rel, target"))
            links.Add(new JsonObject { ["source"] = row.GetString(0), ["rel"] = row.GetString(1), ["target"] = row.GetString(2) });
        return new JsonObject { ["artifacts"] = artifacts, ["links"] = links };
    }

    void EnsureIndexFresh()
    {
        if (File.Exists(repo.IndexPath))
        {
            using var db = Open(repo.IndexPath);
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'fingerprint'";
            if (cmd.ExecuteScalar() as string == Fingerprint(StoreFiles())) return;
        }
        RebuildIndex();
    }

    /// <summary>Changes whenever a store file is added, removed or rewritten.</summary>
    string Fingerprint(IEnumerable<string> files) => CanonicalJson.Hash(string.Join("\n",
        files.Select(f => { var info = new FileInfo(f); return $"{Relative(f)}\t{info.Length}\t{info.LastWriteTimeUtc.Ticks}"; })));

    List<string> StoreFiles() => Directory.Exists(repo.StoreRoot)
        ? [.. Directory.EnumerateFiles(repo.StoreRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
        : [];

    string PathOf(ArtifactRef artifact)
    {
        if (!IsValidRef(artifact))
            throw new StangentException("invalid_ref", $"'{artifact}' is not a valid reference.", ExitCodes.Usage);
        return System.IO.Path.Combine(repo.StoreRoot, artifact.Type, artifact.Id + ".json");
    }

    // Same rules as artifact-base.v1.json; checked here too because ids become file paths.
    static bool IsValidRef(ArtifactRef r) =>
        System.Text.RegularExpressions.Regex.IsMatch(r.Type, "^[a-z][a-z0-9-]{0,63}$")
        && System.Text.RegularExpressions.Regex.IsMatch(r.Id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");

    string Relative(string path) => System.IO.Path.GetRelativePath(repo.Root, path).Replace('\\', '/');

    static JsonObject Parse(string text, string source)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new StangentException("invalid_json", $"{source} is not a JSON object.");
        }
        catch (JsonException e)
        {
            throw new StangentException("invalid_json", $"{source}: {e.Message}");
        }
    }

    static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    static void Exec(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static IEnumerable<SqliteDataReader> Rows(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) yield return reader;
    }
}
