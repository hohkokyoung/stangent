using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Trace;

/// <summary>
/// What a finding carries beyond its link (2.4, D118): a permanent <c>X-0001</c>, a severity a person sets, and <c>evidence</c> records
/// filled by code from what's known. All of it is issued by <c>trace check</c>, never the AI, and none of it reaches a hash or a prompt.
/// </summary>
public static partial class TraceFindings
{
    const string CheckerName = "stangent trace check";

    [GeneratedRegex(@"\AX-\d+")]
    private static partial Regex FindingId();

    public static bool IsFinding(JsonObject e) => (string?)e["state"] != "agree";

    public static string LedgerFile(string root) => Path.Combine(root, "specs", "trace-ids.json");

    /// <summary>The lock stays out of specs/, which is committed.</summary>
    static PermanentIds Ledger(string root) => new(LedgerFile(root), Path.Combine(root, ".stangent", "fsd", "trace-ids.lock"));

    /// <summary>
    /// Gives every finding what it lacks: an id, its first severity, and any evidence record it doesn't have yet. A record already
    /// there isn't rewritten, so one that's been damaged is seen by <see cref="Check"/>. Ids nothing holds any more are marked removed.
    /// Returns whether the file needs saving.
    /// </summary>
    public static bool Stamp(string root, JsonObject trace, SpecIndex index)
    {
        var entries = TraceFile.Sorted(trace["entries"]!.AsArray()).ToList();
        var findings = entries.Where(IsFinding).ToList();
        if (findings.Count == 0 && !File.Exists(LedgerFile(root))) return false;
        var before = trace.ToJsonString();
        var ledger = Ledger(root);

        // A ledger behind the file, or deleted, catches up first, so a live id is never given again.
        var held = findings.Select(e => (string?)e["finding"]).OfType<string>().ToList();
        if (held.Count > 0) ledger.Raise("X", "X-{0:D4}", held.Max(Number));
        foreach (var e in findings.Where(e => e["finding"] is null)) e["finding"] = ledger.Issue("X", "X-{0:D4}");
        var live = findings.Select(e => (string)e["finding"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in ledger.Active("X").Where(id => !live.Contains(id)).ToList()) ledger.Remove("X", gone);

        var documents = FsdImport.Documents(root).ToDictionary(d => (string)d["id"]!, StringComparer.Ordinal);
        var runs = LinkRun.LoadRuns(root);
        var repo = new Lazy<string>(() => SymbolLines.RepoName(root));
        foreach (var e in findings)
        {
            var id = (string)e["finding"]!;
            if (e["severity"] is null)
                StateChanges.Apply(root, e, StateKinds.FindingSeverity, id, Severity.Ungraded, StateActor.Code(CheckerName), "a new finding starts ungraded");
            var evidence = e["evidence"] as JsonArray ?? [];
            var have = evidence.OfType<JsonObject>().Select(r => (string?)r["id"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var checkers = Checkers(e, runs);
            foreach (var record in Records(e, id, documents, index, repo, checkers).Where(r => !have.Contains((string)r["id"]!)))
                evidence.Add((JsonNode)record);
            foreach (var record in evidence.OfType<JsonObject>()) SyncPerson(record, (string?)e["decided_by"]);
            e["evidence"] = evidence;
        }
        return trace.ToJsonString() != before;
    }

    static int Number(string id) => int.TryParse(id.Split('-').Last(), out var n) ? n : 0;

    /// <summary>The code that checked it; the models that judged it; and the person who decided it.</summary>
    static List<JsonObject> Checkers(JsonObject e, JsonObject runs)
    {
        var list = new List<JsonObject> { Checker("code", CheckerName) };
        if ((string?)e["by"] == "judge")
        {
            var run = (string?)e["fsd"]?["doc"] is { } doc && (string?)e["fsd"]?["section"] is { } section ? runs[LinkRun.Key(doc, section)] : null;
            list.Add(Checker("agent", (string?)run?["judge"]?["model"] ?? LinkRun.Judge));
            if (e["check"] is not null) list.Add(Checker("agent", (string?)run?["checker"]?["model"] ?? LinkRun.Checker));
        }
        return list;
    }

    static JsonObject Checker(string kind, string name) => new() { ["kind"] = kind, ["name"] = name };

    /// <summary>A decision that's dropped back takes its person with it.</summary>
    static void SyncPerson(JsonObject record, string? decidedBy)
    {
        if (record["checkedBy"] is not JsonArray checkers) return;
        foreach (var stale in checkers.OfType<JsonObject>().Where(c => (string?)c["kind"] == "person" && (string?)c["name"] != decidedBy).ToList()) checkers.Remove(stale);
        if (decidedBy is not null && !checkers.OfType<JsonObject>().Any(c => (string?)c["kind"] == "person" && (string?)c["name"] == decidedBy))
            checkers.Add((JsonNode)Checker("person", decidedBy));
    }

    static IEnumerable<JsonObject> Records(JsonObject e, string id, Dictionary<string, JsonObject> documents, SpecIndex index, Lazy<string> repo, List<JsonObject> checkers)
    {
        JsonArray Checked() => [.. checkers.Select(c => (JsonNode)c.DeepClone())];
        if (e["fsd"] is JsonObject fsd && (string?)fsd["quote"] is { } fsdQuote && documents.TryGetValue((string)fsd["doc"]!, out var doc))
            yield return new JsonObject
            {
                ["id"] = id + ".fsd", ["citation"] = new JsonObject { ["kind"] = "document", ["ref"] = (string)fsd["doc"]!, ["quote"] = fsdQuote },
                ["contentHash"] = (string?)doc["original"]?["hash"], ["strength"] = "stated", ["checkedBy"] = Checked(),
            };
        if (e["page"] is not JsonObject page) yield break;

        var pageId = (string)page["id"]!;
        var citation = new JsonObject { ["kind"] = "page", ["ref"] = pageId };
        if ((string?)page["statement"] is { } statementId) citation["symbol"] = statementId;
        if ((string?)page["quote"] is { } pageQuote) citation["quote"] = pageQuote;
        var record = new JsonObject { ["id"] = id + ".page", ["citation"] = citation };
        if (index.Pages.TryGetValue(pageId, out var recorded) && (string?)recorded["generated"]?["commit"] is { Length: 40 } commit && commit.All(Uri.IsHexDigit))
            record["commit"] = commit.ToLowerInvariant();
        record["strength"] = "read";
        record["checkedBy"] = Checked();
        yield return record;

        // The code the quoted statement cites, where the page has line citations (2.3).
        if ((string?)page["statement"] is not { } st || index.Statement(pageId, st) is not { } statement || !index.PageFiles.TryGetValue(pageId, out var file)) yield break;
        if (PageLines.Load(file) is not { } lines || (string?)lines["commit"] is not { } at) yield break;
        var n = 0;
        foreach (var cite in statement["cites"]!.AsArray().Select(c => (string)c!))
        {
            n++;
            if (lines["symbols"]?[cite] is not JsonObject at_ || (string?)at_["file"] is not { } path) continue;
            var code = new JsonObject { ["kind"] = "code", ["ref"] = $"{repo.Value}/{path}", ["symbol"] = cite };
            if (at_["start"] is not null) code["lines"] = new JsonObject { ["start"] = (int)at_["start"]!, ["end"] = (int)at_["end"]! };
            yield return new JsonObject { ["id"] = $"{id}.code.{n}", ["citation"] = code, ["commit"] = at, ["strength"] = "read", ["checkedBy"] = Checked() };
        }
    }

    /// <summary>
    /// Everything wrong with a finding's evidence, as one line each: the contract's own validator, then what it can't know. Evidence
    /// mustn't cite a finding, because an opinion can't back up a claim about today's behaviour (D53).
    /// </summary>
    public static IEnumerable<string> Check(JsonObject e, IReadOnlySet<string>? nodes)
    {
        var id = (string?)e["finding"] ?? (string)e["id"]!;
        if (e["evidence"] is not JsonArray { Count: > 0 } evidence) { yield return $"{id}: a finding needs evidence, and code found nothing to cite"; yield break; }
        foreach (var node in evidence)
        {
            var problems = EvidenceValidator.Validate(node?.ToJsonString() ?? "null");
            var name = (node as JsonObject)?["id"]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string?)node!["id"] : null;
            if (problems.Count > 0)
            {
                foreach (var p in problems) yield return $"{id}: evidence {name ?? "(no id)"}: {p}";
                continue;
            }
            foreach (var p in Rules((JsonObject)node!, e, id, nodes)) yield return $"{id}: evidence {name}: {p}";
        }
    }

    static IEnumerable<string> Rules(JsonObject record, JsonObject e, string id, IReadOnlySet<string>? nodes)
    {
        if (!((string)record["id"]!).StartsWith(id + ".", StringComparison.Ordinal)) yield return $"its id doesn't belong to {id}";
        var citation = record["citation"]!.AsObject();
        var kind = (string)citation["kind"]!;
        var cited = (string)citation["ref"]!;
        var symbol = (string?)citation["symbol"];
        var strength = (string)record["strength"]!;

        if (FindingId().IsMatch(cited) || symbol is not null && FindingId().IsMatch(symbol) || cited.Contains("trace.yaml", StringComparison.Ordinal))
        {
            yield return "it cites a finding; only the FSD, a page statement, code or a test can back up a finding (D53)";
            yield break;
        }
        var want = kind switch { "document" => "stated", "code" or "page" => "read", "test" => "ran", _ => null };
        if (want is null) yield return $"a finding's evidence can't be a {kind} citation";
        else if (want != strength) yield return $"a {kind} citation's strength is {want}, not {strength}";

        var same = (string? a, string? b) => a is not null && b is not null && Quotes.Normalize(a) == Quotes.Normalize(b);
        switch (kind)
        {
            case "document":
                if (cited != (string?)e["fsd"]?["doc"]) yield return $"it cites document {cited}, and this finding is on {(string?)e["fsd"]?["doc"] ?? "no document"}";
                if (!same((string?)citation["quote"], (string?)e["fsd"]?["quote"])) yield return "its quote isn't the finding's FSD quote";
                break;
            case "page":
                if (cited != (string?)e["page"]?["id"]) yield return $"it cites page {cited}, and this finding is on {(string?)e["page"]?["id"] ?? "no page"}";
                if (symbol != (string?)e["page"]?["statement"]) yield return $"it cites statement {symbol ?? "(none)"}, and this finding is on {(string?)e["page"]?["statement"] ?? "(none)"}";
                if (citation["quote"] is not null && !same((string?)citation["quote"], (string?)e["page"]?["quote"])) yield return "its quote isn't the finding's page quote";
                break;
            case "code":
                if (symbol is null) yield return "a code citation names its symbol";
                else if (nodes is not null && !nodes.Contains(symbol)) yield return $"it cites `{symbol}`, which isn't in the code graph";
                break;
        }
    }

    /// <summary>The findings as <c>trace check</c> lists them.</summary>
    public static JsonArray Listing(IEnumerable<JsonObject> entries) => new([.. entries.Where(IsFinding).Select(e => (JsonNode)new JsonObject
    {
        ["id"] = (string?)e["finding"], ["entry"] = (string?)e["id"], ["state"] = (string?)e["state"], ["severity"] = (string?)e["severity"] ?? Severity.Ungraded,
        ["doc"] = (string?)e["fsd"]?["doc"], ["section"] = (string?)e["fsd"]?["section"], ["page"] = (string?)e["page"]?["id"], ["statement"] = (string?)e["page"]?["statement"],
        ["decision"] = (string?)e["decision"],
    })]);
}
