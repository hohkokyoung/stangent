using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Pages;

/// <summary>
/// Every saved AI answer's settings (D161), found by code: each usage record under <c>specs/</c>, the ticket folders and saved
/// <c>spec ask</c> answers (all reused while their inputs match),
/// with the model and effort its calls ran with. With the settings pinned now, it says which were made under others.
/// It changes nothing: re-running them is a person's call, since settings are in no input hash.
/// </summary>
public static class AiRuns
{
    public static JsonObject List(StangentRepo repo, bool differsOnly)
    {
        var rows = new List<JsonObject>();
        foreach (var file in Files(repo.Root))
        {
            JsonNode? json;
            try { json = JsonNode.Parse(File.ReadAllText(file)); } catch (System.Text.Json.JsonException) { continue; }
            Walk(json, "$", (path, usage) => rows.Add(Row(repo, Path.GetRelativePath(repo.Root, file).Replace('\\', '/'), path, usage)));
        }
        var shown = rows.Where(r => !differsOnly || (bool)r["differs"]!).ToList();
        return new JsonObject
        {
            ["records"] = rows.Count,
            ["unknown"] = rows.Count(r => (string?)r["effort"] == RunSettings.UnknownValue),
            ["differs"] = rows.Count(r => (bool)r["differs"]!),
            ["runs"] = new JsonArray([.. shown]),
        };
    }

    static IEnumerable<string> Files(string root)
    {
        foreach (var dir in new[] { Path.Combine(root, "specs"), Path.Combine(root, ".stangent", "tickets"), Path.Combine(root, ".stangent", "spec-ask") })
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                    yield return f;
    }

    /// <summary>A usage record is an object with <c>calls</c> and <c>costUsd</c>, as <see cref="Usage.ToJson"/> writes it.</summary>
    static void Walk(JsonNode? node, string path, Action<string, JsonObject> found)
    {
        switch (node)
        {
            case JsonObject o when o["calls"] is JsonValue && o["costUsd"] is JsonValue:
                if (Usage.FromJson(o).Calls > 0) found(path, o);
                break;
            case JsonObject o:
                foreach (var (k, v) in o) Walk(v, $"{path}.{k}", found);
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) Walk(a[i], $"{path}[{i}]", found);
                break;
        }
    }

    static JsonObject Row(StangentRepo repo, string file, string path, JsonObject o)
    {
        var usage = Usage.FromJson(o);
        var run = usage.Run ?? RunSettings.Unknown;
        string? why = null;
        if (run.Agent is RunSettings.UnknownValue or RunSettings.MixedValue) why = $"agent {run.Agent}";
        else
        {
            try
            {
                var now = AiSettings.Resolve(repo.Config, run.Agent);
                var parts = new List<string>();
                if (run.Model != now.Model) parts.Add($"model {run.Model}, pinned {now.Model}");
                if (run.Effort != now.Effort) parts.Add($"effort {run.Effort}, pinned {now.Effort}");
                if (parts.Count > 0) why = string.Join("; ", parts);
            }
            catch (StangentException e) { why = e.Code; }
        }
        var row = new JsonObject
        {
            ["file"] = file, ["path"] = path, ["calls"] = usage.Calls, ["agent"] = run.Agent,
            ["model"] = run.Model, ["modelReported"] = usage.Model, ["effort"] = run.Effort, ["mode"] = run.Mode, ["claudeVersion"] = run.ClaudeVersion,
            ["differs"] = why is not null,
        };
        if (why is not null) row["why"] = why;
        return row;
    }
}
