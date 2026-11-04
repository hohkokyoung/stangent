using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Graph;

public sealed record CodeEnumMember(string Name, string? Value, int Line);

/// <summary>An enum as the code declares it (C.2). <see cref="Id"/> is its point on the graph.</summary>
public sealed record CodeEnum(string Id, string Name, string Project, string File, int Line, int EndLine, IReadOnlyList<CodeEnumMember> Members);

/// <summary>
/// The enums beside the graph, in a file of their own, as 📜 lines are kept from the walk (C.2, D124): the graph's store and export are the same bytes with this on or off,
/// so the <c>code-graph</c> contract doesn't move and nothing that walks the graph can see them. Written only with <c>table-facts</c> on, and removed with it off.
/// </summary>
public static class CodeEnums
{
    public static string PathFor(string root) => Path.Combine(root, ".stangent", "graph-enums.json");

    /// <summary>What's on file, with the commit it was read at. Null when nothing is.</summary>
    public static (string Commit, List<CodeEnum> Enums)? Load(string root)
    {
        if (!File.Exists(PathFor(root))) return null;
        var o = JsonNode.Parse(File.ReadAllText(PathFor(root)))!.AsObject();
        return ((string)o["commit"]!, [.. o["enums"]!.AsArray().OfType<JsonObject>().Select(e => new CodeEnum((string)e["id"]!, (string)e["name"]!, (string)e["project"]!, (string)e["file"]!, (int)e["line"]!, (int)e["endLine"]!,
            [.. e["members"]!.AsArray().OfType<JsonObject>().Select(m => new CodeEnumMember((string)m["name"]!, (string?)m["value"], (int)m["line"]!))]))]);
    }

    /// <summary>What this build read, and for a project it reused, what was on file for it.</summary>
    internal static void Save(string root, bool on, CodeGraph graph, CodeGraph? previous, (string Commit, List<CodeEnum> Enums)? before)
    {
        if (!on)
        {
            if (File.Exists(PathFor(root))) File.Delete(PathFor(root));
            return;
        }
        var reused = (before?.Enums ?? []).Where(e => previous?.Fragments.GetValueOrDefault(e.Project) is { } f && ReferenceEquals(f, graph.Fragments.GetValueOrDefault(e.Project)));
        var all = reused.Concat(graph.Fragments.Values.SelectMany(f => f.Enums)).DistinctBy(e => e.Id).OrderBy(e => e.Id, StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(PathFor(root))!);
        File.WriteAllText(PathFor(root), CanonicalJson.Serialize(new JsonObject
        {
            ["commit"] = graph.Commit,
            ["enums"] = new JsonArray([.. all.Select(e => (JsonNode)new JsonObject
            {
                ["id"] = e.Id, ["name"] = e.Name, ["project"] = e.Project, ["file"] = e.File, ["line"] = e.Line, ["endLine"] = e.EndLine,
                ["members"] = new JsonArray([.. e.Members.Select(m => (JsonNode)new JsonObject { ["name"] = m.Name, ["value"] = m.Value, ["line"] = m.Line })]),
            })]),
        }));
    }
}
