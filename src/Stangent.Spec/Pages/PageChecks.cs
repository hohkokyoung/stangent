using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Spec.Pages;

/// <summary>A verifier finding that survived the evidence check (D62).</summary>
public sealed record Finding(string Id, string Statement, string StatementText, string Kind, string? Quote, string Explanation);

/// <summary>
/// The code checks on AI output (D18, D60): the schema, every citation in the graph and in the page's slice, and the
/// evidence behind verifier findings. Each failure is one specific line the writer can act on.
/// </summary>
public static class PageChecks
{
    public static readonly string[] Sections = ["shows", "actions", "rules", "sideEffects"];
    static readonly Dictionary<string, string> Prefix = new() { ["shows"] = "S", ["actions"] = "A", ["rules"] = "R", ["sideEffects"] = "E" };

    public static JsonNode? Parse(string text, List<string> errors)
    {
        try { return JsonNode.Parse(Unfence(text)); }
        catch (JsonException e) { errors.Add(text.Trim().Length == 0 ? "No output: the result file is missing or empty." : $"Not valid JSON: {e.Message}"); return null; }
    }

    /// <summary>A model sometimes wraps its JSON in a Markdown fence; that's the only repair code makes.</summary>
    static string Unfence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;
        var first = t.IndexOf('\n');
        var last = t.LastIndexOf("```", StringComparison.Ordinal);
        return first > 0 && last > first ? t[(first + 1)..last] : t;
    }

    /// <summary>The page's statements with the ids code gives them: S1… shows, A1… actions, R1… rules, E1… side effects.</summary>
    public static IEnumerable<(string Section, string Id, JsonObject Statement)> Statements(JsonObject page)
    {
        foreach (var section in Sections)
        {
            var list = page[section] as JsonArray ?? page["sections"]?[section] as JsonArray ?? [];
            var i = 0;
            foreach (var s in list.OfType<JsonObject>())
                yield return (section, Prefix[section] + ++i, s);
        }
    }

    /// <summary>Checks a writer's page. <paramref name="findings"/> is set when the writer was fixing a verifier's findings.</summary>
    public static List<string> CheckWriter(JsonNode? output, Slice slice, SliceBuilder graph, Bundle bundle,
        IReadOnlyList<Finding>? findings = null)
    {
        var errors = new List<string>();
        if (output is null) return ["No output: the result file is missing or empty."];
        errors.AddRange(SchemaRegistry.Embedded.Validate(Bundles.WriterSchema, output.ToJsonString()).Select(e => $"Schema: {e}"));
        if (output is not JsonObject page) return errors;

        foreach (var (_, id, s) in Statements(page))
        {
            foreach (var cite in (s["cites"] as JsonArray ?? []).Select(c => c?.GetValueKind() == JsonValueKind.String ? (string)c! : null))
            {
                if (cite is null) continue;
                if (graph.Find(cite) is null) errors.Add($"{id} cites `{cite}`, which isn't in the code graph. Cite ids from the Symbols table exactly.");
                else if (!slice.Ids.Contains(cite)) errors.Add($"{id} cites `{cite}`, which is outside this page's slice. Cite only ids from the Symbols table.");
            }
            foreach (var ep in (s["endpoints"] as JsonArray ?? []).Select(c => c?.GetValueKind() == JsonValueKind.String ? (string)c! : null))
            {
                if (ep is null) continue;
                if (!slice.Ids.Contains(ep) || graph.Find(ep)?.Kind != NodeKind.Endpoint)
                    errors.Add($"{id} lists endpoint `{ep}`, which isn't an endpoint in this page's slice.");
            }
        }

        var resolutions = (page["resolutions"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (findings is null)
        {
            if (resolutions.Count > 0) errors.Add("`resolutions` is only for answering a verifier's findings; leave it out.");
            return errors;
        }

        // Fixing: every finding answered once; "fixed" must change the statement; "rejected" needs a reason, and
        // rejecting "unsupported" needs the supporting line from the slice.
        var texts = Statements(page).Select(s => Quotes.Normalize((string?)s.Statement["text"] ?? "")).ToHashSet(StringComparer.Ordinal);
        foreach (var f in findings)
        {
            var answers = resolutions.Where(r => (string?)r["finding"] == f.Id).ToList();
            if (answers.Count != 1) { errors.Add($"Finding {f.Id} needs exactly one resolution (it has {answers.Count})."); continue; }
            var a = answers[0];
            switch ((string?)a["outcome"])
            {
                case "fixed" when texts.Contains(Quotes.Normalize(f.StatementText)):
                    errors.Add($"Finding {f.Id} is marked fixed, but statement {f.Statement} is unchanged. Change or remove it, or reject the finding with a reason.");
                    break;
                case "rejected" when f.Kind == "unsupported" && (a["quote"] is null || !Quotes.InSource((string)a["quote"]!, bundle.SourceLines)):
                    errors.Add($"Finding {f.Id} is rejected, but its `quote` isn't a line from the slice's source. Copy the line that supports {f.Statement}, or fix it.");
                    break;
            }
        }
        foreach (var r in resolutions.Where(r => findings.All(f => f.Id != (string?)r["finding"])))
            errors.Add($"Resolution for {(string?)r["finding"]}: there's no such finding.");
        return errors;
    }

    /// <summary>
    /// Checks a verifier's output (D62): schema, then each finding's evidence. A "contradicted" finding whose quote isn't
    /// in the slice's source, or a finding about a statement that doesn't exist, is dropped.
    /// </summary>
    public static (List<string> Errors, List<Finding> Kept, int Dropped) CheckVerifier(JsonNode? output, JsonObject staged, Bundle bundle)
    {
        if (output is null) return (["No output: the result file is missing or empty."], [], 0);
        var errors = SchemaRegistry.Embedded.Validate(Bundles.VerifierSchema, output.ToJsonString()).Select(e => $"Schema: {e}").ToList();
        if (errors.Count > 0) return (errors, [], 0);
        var statements = Statements(staged).ToDictionary(s => s.Id, s => (string)s.Statement["text"]!, StringComparer.Ordinal);
        var kept = new List<Finding>();
        var dropped = 0;
        foreach (var f in output["findings"]!.AsArray().OfType<JsonObject>())
        {
            var statement = (string)f["statement"]!;
            var kind = (string)f["kind"]!;
            var quote = (string?)f["quote"];
            var hasEvidence = kind == "unsupported" || (quote is not null && Quotes.InSource(quote, bundle.SourceLines));
            if (!statements.TryGetValue(statement, out var text) || !hasEvidence) { dropped++; continue; }
            kept.Add(new Finding($"F{kept.Count + 1}", statement, text, kind, kind == "contradicted" ? quote : null, (string)f["explanation"]!));
        }
        return ([], kept, dropped);
    }
}
