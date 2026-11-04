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

    /// <summary>Every queue a finding can be in, in the order they're listed.</summary>
    public static readonly string[] Queues = ["ba", "developer", "developer-confirm", "closed", "page-owner", "decided"];

    /// <summary>Who's next with this finding (D119): <c>ba</c> by default, including one that stands, can't be told, or was sent back; <c>developer</c> for a page gap;
    /// <c>developer-confirm</c> when the AI says refuted or unreachable, because code proved its quote exists and only a person can say it supports the verdict (D18);
    /// <c>closed</c> once a person confirmed that, kept and listed; <c>page-owner</c> when a person said the page is wrong; <c>decided</c> otherwise.</summary>
    public static string GoesTo(JsonObject e) => (string?)e["decision"] switch
    {
        "page-wrong" => "page-owner",
        not null => "decided",
        _ => ((string?)e["verdict"], ReviewHolds(e) ? (string?)e["review"] : null) switch
        {
            (_, "sent-back") => "ba",
            ("refuted" or "unreachable", "confirmed") => "closed",
            ("refuted" or "unreachable", _) => "developer-confirm",
            ("page-gap", _) => "developer",
            _ => "ba",
        },
    };

    /// <summary>A finding a person agreed the code took apart, that nobody has decided: out of every list a person works from, and kept on file.
    /// A verdict nobody has confirmed closes nothing, so it stays in impact answers, ticket bundles and drafts.</summary>
    public static bool Closed(JsonObject e) => GoesTo(e) == "closed";

    /// <summary>Whether the disprove step's verdict is what took this finding away from the BA, as opposed to a person's decision or sending it back.</summary>
    public static bool TakenOutByDisprove(JsonObject e) => GoesTo(e) is "developer" or "developer-confirm" or "closed";

    /// <summary>The words a queue shows beside a finding. A verdict is the AI's reading: code found its quote, and nobody has said the quote supports it.</summary>
    static string Label(JsonObject e)
    {
        var verdict = (string?)e["verdict"];
        return (ReviewHolds(e) ? (string?)e["review"] : null) switch
        {
            "sent-back" => $"sent back to the BA: {(string?)e["review_note"]?["why"]}",
            "confirmed" => $"{verdict}, confirmed by {(string?)e["review_note"]?["by"]}",
            _ => verdict switch { null => "unverified", "cant-tell" => "cant-tell", _ => $"the AI's reading: {verdict}" },
        };
    }

    public static bool ReviewHolds(JsonObject e) => (string?)e["review_hash"] == TraceFile.ReviewHash((string?)e["review"], e["review_note"] as JsonObject, (string?)e["disproof_hash"]);

    /// <summary>A person's word on a verdict, from <c>trace confirm</c> and <c>trace send-back</c>, on the state module. Returns where the finding went.</summary>
    /// <param name="to"><c>confirmed</c> or <c>sent-back</c>.</param>
    /// <param name="who">From git, never typed.</param>
    public static JsonObject Review(string root, string id, string to, string who, string? why)
    {
        using var _ = LinkRun.Lock(root);
        var trace = TraceFile.Load(root);
        var e = trace["entries"]!.AsArray().OfType<JsonObject>().FirstOrDefault(x => IsFinding(x) && ((string?)x["finding"] == id || (string?)x["id"] == id))
            ?? throw new StangentException("not_found", $"No finding {id} in specs/trace.yaml.", ExitCodes.NotFound);
        var now = GoesTo(e);
        var allowed = to == "confirmed"
            ? (string?)e["verdict"] is "refuted" or "unreachable" && now is "developer-confirm" or "ba"
            : now is "developer-confirm" or "closed";
        if (!allowed)
            throw new StangentException("not_reviewable", $"{id} is with {now}{((string?)e["verdict"] is { } v ? $", verdict {v}" : "")}: only a refuted or unreachable verdict nobody has decided can be {to}.", ExitCodes.Usage);
        var note = new JsonObject { ["by"] = who, ["why"] = why ?? (to == "confirmed" ? "the quote supports the verdict" : $"{who} sent it back") };
        if (to == "confirmed" && DecidedOn(e, SpecIndex.Load(root)) is { } on) note["on"] = on;
        WriteReview(root, e, to, note, StateActor.Person(who), why ?? (to == "confirmed" ? "confirmed by a person" : "sent back by a person"));
        TraceFile.Save(root, trace);
        return new JsonObject { ["finding"] = (string?)e["finding"], ["entry"] = (string?)e["id"], ["review"] = to, ["by"] = who, ["goesTo"] = GoesTo(e) };
    }

    static void WriteReview(string root, JsonObject e, string to, JsonObject note, StateActor who, string reason)
    {
        StateChanges.Apply(root, e, StateKinds.FindingReview, (string)e["id"]!, to, who, reason);
        e["review_note"] = note;
        e["review_hash"] = TraceFile.ReviewHash(to, note, (string?)e["disproof_hash"]);
    }

    /// <summary>A person's word no longer stands, so the verdict it covered mustn't take over again: the finding goes to the BA, saying why. It holds with the disprove step off, because nothing has to run for it.</summary>
    public static void SendBack(string root, JsonObject e, StateActor who, string why)
    {
        if (e["verdict"] is null || (string?)e["review"] == "sent-back" && ReviewHolds(e)) return;
        WriteReview(root, e, "sent-back", new JsonObject { ["why"] = why }, who, why);
    }

    /// <summary>Code drops a person's decision back, and the verdict underneath it doesn't get to decide instead.</summary>
    public static void DropDecision(string root, JsonObject e, StateActor who, string reason)
    {
        var had = e["decision"] is not null;
        StateChanges.Apply(root, e, StateKinds.TraceDecision, (string)e["id"]!, "undecided", who, reason);
        if (had) SendBack(root, e, who, $"a person's decision was dropped because {reason}");
    }

    /// <summary>The verdict a review was about is moving, so a new verdict starts with nobody's word on it.</summary>
    public static void ClearReview(string root, JsonObject e, StateActor who, string reason)
    {
        if (e["review"] is not null || e["review_note"] is not null || e["review_hash"] is not null)
            StateChanges.Apply(root, e, StateKinds.FindingReview, (string)e["id"]!, "waiting", who, reason);
    }

    /// <summary>The hash of the page statement's text this entry rests on, or null when it rests on none (D118).</summary>
    public static string? DecidedOn(JsonObject e, SpecIndex index) =>
        (string?)e["page"]?["id"] is { } page && (string?)e["page"]?["statement"] is { } statement && index.Statement(page, statement) is { } found
            ? TraceFile.QuoteHash((string)found["text"]!) : null;

    public static string LedgerFile(string root) => Path.Combine(root, "specs", "trace-ids.json");

    /// <summary>The lock stays out of specs/, which is committed.</summary>
    static PermanentIds Ledger(string root) => new(LedgerFile(root), Path.Combine(root, ".stangent", "fsd", "trace-ids.lock"));

    /// <summary>
    /// Gives every finding what it lacks, an id and its first severity, and derives its evidence again from what's current. Evidence
    /// is code's own, so a finding's records are replaced when each is what code derives now, or what code wrote before: the base's
    /// copy when there is a base, else the file's own <c>evidence_hash</c>. Any other record is left where <see cref="Check"/> sees it,
    /// and named in <paramref name="refused"/>. Ids nothing holds any more are marked removed. Returns whether the file needs saving.
    /// </summary>
    /// <param name="baseFindings">The base file's findings by id, or null when the check has no base.</param>
    /// <param name="refused">Filled with each finding's records that nobody but a person could have written.</param>
    public static bool Stamp(string root, JsonObject trace, SpecIndex index, IReadOnlyDictionary<string, JsonObject>? baseFindings, Dictionary<string, List<string>> refused)
    {
        refused.Clear();
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
            var derived = Records(e, id, documents, index, repo, Checkers(e, runs)).ToList();
            foreach (var record in derived) SyncPerson(record, (string?)e["decided_by"]);
            var filed = e["evidence"] as JsonArray ?? [];
            var typed = Typed(e, filed, derived, baseFindings is null ? null : baseFindings.GetValueOrDefault(id)?["evidence"] as JsonArray ?? [], baseFindings is null);
            if (typed.Count > 0)
            {
                refused[id] = [.. typed.Select(r => (r as JsonObject)?["id"]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string)r!["id"]! : "(no id)")];
                // Stays as it is, hash and all, so the refusal holds until a person deletes it. A record they deleted comes back.
                var have = filed.OfType<JsonObject>().Select(r => r["id"]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string?)r["id"] : null).OfType<string>().ToHashSet(StringComparer.Ordinal);
                foreach (var record in derived.Where(r => !have.Contains((string)r["id"]!))) filed.Add((JsonNode)record);
                e["evidence"] = filed;
                continue;
            }
            var evidence = new JsonArray([.. derived.Select(r => (JsonNode)r)]);
            e["evidence"] = evidence;
            e["evidence_hash"] = TraceFile.EvidenceHash(evidence);
        }
        return trace.ToJsonString() != before;
    }

    /// <summary>The records code can't vouch for: not what it derives now, and not what it wrote before.</summary>
    static List<JsonNode?> Typed(JsonObject e, JsonArray filed, List<JsonObject> derived, JsonArray? baseEvidence, bool useHash)
    {
        var hashHolds = useHash && (string?)e["evidence_hash"] == TraceFile.EvidenceHash(filed);
        return [.. filed.Where(r => !derived.Exists(d => JsonNode.DeepEquals(d, r))
            && !(hashHolds || baseEvidence is not null && baseEvidence.Any(b => JsonNode.DeepEquals(b, r))))];
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
        // The disprove step's quote: the agent said it, then code found it word for word at these lines (D119). Code read, so `read`, never `ran`.
        if (e["disproof"] is JsonObject disproof && (string?)disproof["quote"] is { } quote && (string?)disproof["commit"] is { } quotedAt)
            yield return new JsonObject
            {
                ["id"] = id + ".disprove",
                ["citation"] = new JsonObject
                {
                    ["kind"] = "code", ["ref"] = $"{repo.Value}/{(string)disproof["path"]!}", ["lines"] = new JsonObject { ["start"] = (int)disproof["start"]!, ["end"] = (int)disproof["end"]! },
                    ["symbol"] = (string)disproof["symbol"]!, ["quote"] = quote,
                },
                ["commit"] = quotedAt, ["strength"] = "read",
                ["checkedBy"] = new JsonArray(
                    new JsonObject { ["kind"] = "agent", ["name"] = (string?)disproof["agent"] ?? DisproveRun.Disprover, ["inputsHash"] = (string)disproof["inputs"]! },
                    Checker("code", "stangent fsd disprove")),
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
            // Whatever limits these lines goes with them, so a partial type's first part isn't read as the whole type.
            if (((string?)at_["note"] ?? (string?)at_["why"]) is { } limit) code["note"] = limit;
            yield return new JsonObject { ["id"] = $"{id}.code.{n}", ["citation"] = code, ["commit"] = at, ["strength"] = "read", ["checkedBy"] = Checked() };
        }
    }

    /// <summary>
    /// Everything wrong with a finding's evidence, as one line each: the contract's own validator, then what it can't know. Evidence
    /// mustn't cite a finding, because an opinion can't back up a claim about today's behaviour (D53).
    /// </summary>
    /// <param name="refused">This finding's records that aren't code's, from <see cref="Stamp"/>.</param>
    public static IEnumerable<string> Check(JsonObject e, IReadOnlySet<string>? nodes, IEnumerable<string>? refused = null)
    {
        var id = (string?)e["finding"] ?? (string)e["id"]!;
        if ((e["verdict"] is not null || e["disproof"] is not null || e["disproof_hash"] is not null)
            && (string?)e["disproof_hash"] != TraceFile.DisproofHash((string?)e["verdict"], e["disproof"] as JsonObject))
            yield return $"{id}: its verdict isn't what code wrote; only `stangent fsd disprove` writes one, because it takes a finding out of a queue";
        if ((e["review"] is not null || e["review_note"] is not null || e["review_hash"] is not null) && !ReviewHolds(e))
            yield return $"{id}: its review isn't what code wrote; only `stangent trace confirm` and `trace send-back` write one, because it closes a finding";
        if (e["evidence"] is not JsonArray { Count: > 0 } evidence) { yield return $"{id}: a finding needs evidence, and code found nothing to cite"; yield break; }
        foreach (var record in refused ?? [])
            yield return $"{id}: evidence {record}: it isn't what code derives from the current inputs, and evidence is never typed; delete it and the next check writes what's true";
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
        ["goesTo"] = GoesTo(e), ["verdict"] = (string?)e["verdict"] ?? "unverified", ["label"] = Label(e),
        ["review"] = (string?)e["review"], ["reviewBy"] = (string?)e["review_note"]?["by"], ["reviewWhy"] = (string?)e["review_note"]?["why"],
        ["why"] = (string?)e["disproof"]?["why"], ["quote"] = (string?)e["disproof"]?["quote"],
        ["at"] = (string?)e["disproof"]?["path"] is { } path ? $"{path}:{(int)e["disproof"]!["start"]!}-{(int)e["disproof"]!["end"]!}" : null,
        ["suggestedSeverity"] = (string?)e["disproof"]?["suggestedSeverity"],
    })]);

    /// <summary>The same findings by who has them next, so each queue reads on its own (D119).</summary>
    public static JsonObject Groups(IEnumerable<JsonObject> entries)
    {
        var findings = entries.Where(IsFinding).ToList();
        return new JsonObject(Queues.Select(g => KeyValuePair.Create(g,
            (JsonNode?)new JsonArray([.. findings.Where(e => GoesTo(e) == g).Select(e => (JsonNode?)((string?)e["finding"] ?? (string)e["id"]!))]))));
    }
}
