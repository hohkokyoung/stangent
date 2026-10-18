using System.Text.Json.Nodes;

namespace Stangent.Core.Store;

/// <summary>
/// Runs many store operations in one CLI call (D23), so callers don't pay start-up time per
/// operation. The whole batch is checked first, in order; if anything would fail, nothing is
/// written.
/// </summary>
/// <remarks>
/// Ops: <c>{"op":"get","ref":"t/i"}</c>, <c>{"op":"put","artifact":{…}}</c>,
/// <c>{"op":"link","source":"t/i","rel":"r","target":"t/j"}</c>,
/// <c>{"op":"query","type":"t","linksTo":"t/j","rel":"r"}</c> (query fields optional).
/// </remarks>
public static class StoreBatch
{
    public static JsonArray Run(IArtifactStore store, JsonArray ops)
    {
        var errors = Check(store, ops);
        if (errors.Count > 0)
            throw new StangentException("invalid_batch", $"{errors.Count} problem(s); nothing was written.", ExitCodes.Failed, errors);

        var results = new JsonArray();
        foreach (var node in ops)
        {
            var op = node!.AsObject();
            results.Add((string)op["op"]! switch
            {
                "get" => store.Get(ArtifactRef.Parse((string)op["ref"]!)),
                "put" => ToJson(store.Put(op["artifact"]!.AsObject())),
                "link" => ToJson(store.Link(ArtifactRef.Parse((string)op["source"]!), (string)op["rel"]!, ArtifactRef.Parse((string)op["target"]!))),
                _ => QueryJson(store.Query(new ArtifactQuery((string?)op["type"], (string?)op["linksTo"], (string?)op["rel"]))),
            });
        }
        return results;
    }

    static List<string> Check(IArtifactStore store, JsonArray ops)
    {
        var errors = new List<string>();
        var created = new HashSet<ArtifactRef>();
        bool Available(ArtifactRef r) => created.Contains(r) || store.Exists(r);

        for (var i = 0; i < ops.Count; i++)
        {
            var at = $"ops[{i}]";
            if (ops[i] is not JsonObject op || op["op"] is not JsonValue kind)
            {
                errors.Add($"{at}: must be an object with an \"op\"");
                continue;
            }
            try
            {
                switch ((string?)kind)
                {
                    case "get":
                        var get = ArtifactRef.Parse(Required(op, "ref"));
                        if (!Available(get)) errors.Add($"{at}: {get} does not exist");
                        break;
                    case "put":
                        if (op["artifact"] is not JsonObject artifact) { errors.Add($"{at}: \"artifact\" must be an object"); break; }
                        var problems = store.Validate(artifact, created);
                        errors.AddRange(problems.Select(p => $"{at}: {p}"));
                        if (problems.Count == 0) created.Add(ArtifactRef.Of(artifact));
                        break;
                    case "link":
                        var source = ArtifactRef.Parse(Required(op, "source"));
                        var target = ArtifactRef.Parse(Required(op, "target"));
                        Required(op, "rel");
                        if (!Available(source)) errors.Add($"{at}: source {source} does not exist");
                        if (!Available(target)) errors.Add($"{at}: target {target} does not exist");
                        break;
                    case "query":
                        break;
                    default:
                        errors.Add($"{at}: unknown op '{(string?)kind}'");
                        break;
                }
            }
            catch (StangentException e)
            {
                errors.Add($"{at}: {e.Message}");
            }
        }
        return errors;
    }

    static string Required(JsonObject op, string field) =>
        (string?)op[field] ?? throw new StangentException("invalid_batch", $"missing \"{field}\"", ExitCodes.Usage);

    public static JsonObject ToJson(PutResult r) => new() { ["ref"] = r.Ref, ["hash"] = r.Hash, ["changed"] = r.Changed };

    public static JsonArray QueryJson(IEnumerable<IndexEntry> entries) => [.. entries.Select(e => (JsonNode)new JsonObject
    {
        ["ref"] = e.Ref, ["schemaVersion"] = e.SchemaVersion, ["title"] = e.Title, ["path"] = e.Path, ["hash"] = e.Hash,
    })];
}
