using System.Text.Json.Nodes;
using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>
/// D327 item 3's floor check (D333): the share of buttons, fields, server-defined fields, choices and screens named by visible text in one
/// graph, where the words came from, and the lines left ❓. Never measured against a planted key.
/// </summary>
public static class LabelReport
{
    public static JsonObject Report(CodeGraph graph, string root, int top = 20, FlowSet? flows = null, int sample = 0)
    {
        var g = new FlowGraph(graph, root);
        var labels = new Labels(g, root);
        var kinds = new JsonObject();
        var missing = new JsonObject();
        var sampled = new JsonObject();

        void Count(string kind, IEnumerable<(Named Name, string At)> lines)
        {
            var all = lines.ToList();
            var named = all.Count(x => x.Name.Visible);
            kinds[kind] = new JsonObject
            {
                ["named"] = named, ["total"] = all.Count, ["share"] = Share(named, all.Count),
                ["bySource"] = new JsonObject([.. all.Where(x => x.Name.Visible).GroupBy(x => x.Name.Source).OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Count()))]),
            };
            missing[kind] = new JsonArray([.. all.Where(x => !x.Name.Visible).Take(top).Select(x => (JsonNode)$"{x.At} {x.Name.Line}")]);
            // A fixed pick of named lines (by a hash of where they are, so the same graph gives the same sample) for a person to check.
            if (sample > 0)
                sampled[kind] = new JsonArray([.. all.Where(x => x.Name.Visible).OrderBy(x => Hash(x.At)).ThenBy(x => x.At, StringComparer.Ordinal).Take(sample)
                    .Select(x => (JsonNode)$"{x.At} {x.Name.Code} → \"{x.Name.Words}\" ({x.Name.Source}){(x.Name.Help is { } h ? $" help \"{h}\"" : "")}")]);
        }
        string At(Fact f) => $"{f.File}:{f.Line}";

        var events = g.AllFacts("ui-event").Where(f => !g.IsTest(f.Owner) && Labels.IsButton(f)).OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
        Count("buttons", events.Select(f => (labels.Button(f), At(f))));
        var fields = g.AllFacts("form-field").Where(f => f["via"] != "server" && f["inside"] != "wrapper" && !g.IsTest(f.Owner)).OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
        Count("fields", fields.Select(f => (labels.Field(f), At(f))));
        var server = g.AllFacts("form-field").Where(f => f["via"] == "server" && !g.IsTest(f.Owner)).OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
        Count("serverFields", server.Select(f => (labels.Field(f), At(f))));
        // Choices: each field with a list or enum of choices counts once, named when every choice is.
        var withChoices = fields.Concat(server).Select(f => (Fact: f, Choices: labels.ChoicesOf(f))).Where(x => x.Choices.Kind is "enum" or "list").ToList();
        Count("choices", withChoices.Select(x => (x.Choices.Visible ? new Named(string.Join(" | ", x.Choices.Names.Select(n => n.Words)), null, x.Fact["via"] == "server" ? x.Fact["bind"] : x.Fact["to"], x.Choices.Kind)
            : new Named(null, null, x.Fact["via"] == "server" ? x.Fact["bind"] : x.Fact["to"], "", x.Choices.Note != "" ? x.Choices.Note : $"{x.Choices.Names.Count(n => !n.Visible)} of {x.Choices.Names.Count} choices have no visible name"), At(x.Fact))));
        var screens = Screens(g).ToList();
        Count("screens", screens.Select(p => (labels.Screen(p), g.Nodes[p].File is { } file ? $"{file}:{g.Nodes[p].Line}" : p)));

        if (flows is not null)
        {
            // Layer 3's names from the same lookup (D327 item 4): a click flow named by words, not its handler's name; a screen flow by its title.
            var clicks = flows.Flows.Where(f => f.Kind == "click").ToList();
            Count("clickFlows", clicks.Select(f => (g.Nodes.TryGetValue(f.Start.Point, out var n) && f.Name == FlowGraph.ShortName(n)
                ? new Named(null, null, f.Name, "") : new Named(f.Name, null, f.Name, "key"), f.Start.At.ToString())));
            var opens = flows.Flows.Where(f => f.Kind == "screen-opens").ToList();
            Count("screenFlows", opens.Select(f => (labels.Screen(f.Start.Point) is { Visible: true } t ? t : new Named(null, null, f.Name, ""), f.Start.At.ToString())));
        }
        var report = new JsonObject { ["commit"] = graph.Commit, ["kinds"] = kinds, ["missing"] = missing };
        if (sample > 0) report["sample"] = sampled;
        return report;
    }

    /// <summary>Screens as layer 3 counts them: every page, and every MVC action that returns a view (D171, D182).</summary>
    internal static IEnumerable<string> Screens(FlowGraph g) =>
        g.Nodes.Values.Where(n => n.Kind == NodeKind.Page && !n.IsTest).Select(n => n.Id)
            .Concat(g.AllFacts("on-load").Where(f => f["framework"] == "mvc" && !g.IsTest(f.Owner) && g.Nodes.ContainsKey(f.Owner)).Select(f => f.Owner))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    static ulong Hash(string text)
    {
        var h = 14695981039346656037UL;   // FNV-1a: stable across runs, unlike string.GetHashCode
        foreach (var c in text) { h ^= c; h *= 1099511628211UL; }
        h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;   // spread nearby places apart
        return h;
    }

    static double Share(int n, int total) => total == 0 ? 0 : Math.Round(100.0 * n / total, 1);
}
