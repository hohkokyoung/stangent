using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Stangent.Graph;

namespace Stangent.Spec.Checks;

/// <summary>
/// T.5: did a run change the app's files? Before and after, by git: the commit, and every changed or untracked file with a
/// hash of its content, so an edit to a file that was already dirty shows too. Stangent's own folders are left out, as a
/// run is meant to write there. Git-ignored files (build output) aren't seen. Outside git it says it didn't check.
/// </summary>
public static class AppFiles
{
    const string Ours = "-- . \":(exclude).stangent\" \":(exclude)specs\" \":(exclude)stangent.yaml\"";

    public static JsonObject Snapshot(string root)
    {
        if (Git.Run(root, "rev-parse --show-toplevel")?.Trim() is not { Length: > 0 } top)
            return new JsonObject { ["checked"] = false, ["why"] = "not a git repo" };
        var files = new JsonObject();
        foreach (var path in Changed(root))
        {
            var full = Path.Combine(top, path);
            files[path] = File.Exists(full) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full))) : "(deleted)";
        }
        return new JsonObject { ["checked"] = true, ["commit"] = Git.Head(root), ["files"] = files };
    }

    public static JsonObject Compare(string root, JsonObject before)
    {
        if ((bool?)before["checked"] != true) return new JsonObject { ["ok"] = true, ["checked"] = false, ["why"] = (string?)before["why"] };
        var after = Snapshot(root);
        if ((bool?)after["checked"] != true) return new JsonObject { ["ok"] = true, ["checked"] = false, ["why"] = (string?)after["why"] };
        var (was, now) = (before["files"]!.AsObject(), after["files"]!.AsObject());
        var changed = was.Select(f => f.Key).Union(now.Select(f => f.Key), StringComparer.Ordinal)
            .Where(path => (string?)was[path] != (string?)now[path]).Order(StringComparer.Ordinal).ToList();
        var moved = (string?)before["commit"] != (string?)after["commit"];
        return new JsonObject
        {
            ["ok"] = changed.Count == 0 && !moved,
            ["checked"] = true,
            ["commitMoved"] = moved ? new JsonObject { ["from"] = (string?)before["commit"], ["to"] = (string?)after["commit"] } : null,
            ["changed"] = new JsonArray([.. changed.Select(p => (JsonNode)p)]),
        };
    }

    static IEnumerable<string> Changed(string root)
    {
        var parts = (Git.Run(root, $"status --porcelain=v1 -z --untracked-files=all {Ours}") ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            // "XY path", and a rename or copy has its old path as the next part.
            yield return parts[i][3..];
            if (parts[i][0] is 'R' or 'C' || parts[i][1] is 'R' or 'C') yield return parts[++i];
        }
    }
}
