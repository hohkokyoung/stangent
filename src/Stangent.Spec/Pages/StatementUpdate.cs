using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Pages;

/// <summary>
/// I.2 (D131): when a page is stale, only the statements citing code that changed go back to the AI. Code works out which, from each point's hash in the page's shape,
/// tells the writer, and puts the rest back word for word; the verifier reads only what's new. Off by default (<c>statement-update</c>), because it changes what the writer
/// gets. The input hash doesn't change. A first page, a person's <c>page-wrong</c>, new instructions or no shape on file: the whole page, as ever.
/// </summary>
public static class StatementUpdate
{
    public const string Component = "statement-update";

    /// <summary>What code keeps, as the writer's own form: each with its section. Null when the whole page should run.</summary>
    public static JsonArray? Plan(PageSet set, StangentConfig config, SpecPaths paths, PageDef page, JsonObject? previous, bool reportedWrong)
    {
        if (!StangentComponents.IsOn(config, Component) || previous is null || reportedWrong) return null;
        if (PageMoves.LoadFile(paths, page) is not { } saved || (string?)saved["head"] != PageMoves.Head(set)) return null;
        var old = saved["symbols"]!.AsObject();
        var now = PageMoves.Shape(set, page).Symbols;
        bool Same(string cite) => now.TryGetValue(cite, out var hash) && (string?)old[cite] == hash;
        var keep = new JsonArray();
        // A statement a person corrected goes back too: its words aren't the writer's, and code puts the correction back after (T.1).
        var corrected = PageCorrections.Approved(config, paths.Root, page.Id).Select(c => (string?)c["appliedTo"]).ToHashSet(StringComparer.Ordinal);
        foreach (var (section, id, s) in PageChecks.Statements(previous))
        {
            var cites = s["cites"]!.AsArray().Select(c => (string)c!).ToList();
            if (cites.Count == 0 || !cites.All(Same) || corrected.Contains(id)) continue;
            var kept = new JsonObject { ["section"] = section, ["text"] = s["text"]!.DeepClone(), ["cites"] = s["cites"]!.DeepClone() };
            if (s["endpoints"] is JsonArray eps) kept["endpoints"] = eps.DeepClone();
            keep.Add(kept);
        }
        return keep;
    }

    /// <summary>What the writer reads, after the hashed part, so no hash moves.</summary>
    public static string Note(JsonArray? keep)
    {
        if (keep is null) return "";
        var sb = new StringBuilder("\n## Update only what changed\n\nCode has kept the statements below, because the code they cite is the same. Don't return them. ");
        sb.Append("Return the page with only the other statements from the previous version, corrected against the source above, and anything new the changed code does.\n\n");
        foreach (var k in keep.OfType<JsonObject>()) sb.Append($"- ({(string)k["section"]!}) {(string)k["text"]!}\n");
        if (keep.Count == 0) sb.Append("None: every statement cites code that changed.\n");
        return sb.ToString();
    }

    /// <summary>The kept statements, word for word, then what the writer gave, leaving out any it repeated.</summary>
    public static JsonObject Merge(JsonObject output, JsonArray keep)
    {
        var merged = (JsonObject)output.DeepClone();
        foreach (var section in PageChecks.Sections)
        {
            var kept = keep.OfType<JsonObject>().Where(k => (string)k["section"]! == section).ToList();
            if (kept.Count == 0) continue;
            var words = kept.Select(k => Quotes.Normalize((string)k["text"]!)).ToHashSet(StringComparer.Ordinal);
            var fresh = (merged[section] as JsonArray ?? []).OfType<JsonObject>().Where(s => !words.Contains(Quotes.Normalize((string?)s["text"] ?? "")));
            merged[section] = new JsonArray([.. kept.Select(k =>
            {
                var s = (JsonObject)k.DeepClone();
                s.Remove("section");
                return (JsonNode)s;
            }), .. fresh.Select(s => s.DeepClone())]);
        }
        return merged;
    }

    public static HashSet<string> KeptWords(JsonArray? keep) =>
        keep is null ? [] : keep.OfType<JsonObject>().Select(k => Quotes.Normalize((string)k["text"]!)).ToHashSet(StringComparer.Ordinal);
}
