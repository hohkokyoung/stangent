using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary><c>fsd status</c> (E.1, D122): where a document stands and the next thing to do, with the exact command. Code only, and it writes nothing.</summary>
public static class FsdStatus
{
    public static JsonObject Of(string root, string doc)
    {
        var waiting = FsdVersions.PendingHash(root, doc) is null ? null : FsdVersions.Diff(root, doc);
        var path = Path.Combine(FsdImport.DocDir(root, doc), "document.json");
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : null;
        if (document is null && waiting is null) throw new StangentException("not_found", $"No imported document {doc}.", ExitCodes.NotFound);

        var doubtful = waiting is null ? [] : waiting["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful").Select(e => (string?)e["to"] ?? (string)e["permanentId"]!).ToList();
        var result = new JsonObject { ["doc"] = doc };
        if (document is not null)
            result["current"] = new JsonObject
            {
                ["hash"] = (string)document["original"]!["hash"]!, ["title"] = (string)document["title"]!, ["sections"] = document["sections"]!.AsArray().Count, ["verdict"] = ImportReport.Verdict(document),
            };
        if (waiting is not null)
            result["candidate"] = new JsonObject { ["hash"] = (string)waiting["to"]!, ["state"] = (string?)waiting["state"], ["first"] = (bool?)waiting["first"] == true };
        result["doubtful"] = new JsonArray([.. doubtful.Select(d => (JsonNode)d)]);

        var trace = TraceFile.Load(root);
        var coverage = document is null ? null : TraceCoverage.Of(root, document, trace);
        var uncovered = coverage?["uncovered"]!.AsArray().OfType<JsonObject>().ToList() ?? [];
        if (coverage is not null)
            result["coverage"] = new JsonObject
            {
                ["linked"] = coverage["linked"]!.AsArray().Count, ["notInCode"] = coverage["notInCode"]!.AsArray().Count, ["empty"] = coverage["empty"]!.AsArray().Count,
                ["uncovered"] = new JsonArray([.. uncovered.Select(u => (JsonNode)(string)u["section"]!)]),
            };

        var entries = trace["entries"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["fsd"]?["doc"] == doc).ToList();
        var findings = entries.Where(TraceFindings.IsFinding).ToList();
        List<string> Queue(params string[] who) => [.. findings.Where(e => who.Contains(TraceFindings.GoesTo(e))).Select(e => (string?)e["finding"] ?? (string)e["id"]!)];
        var ba = Queue("ba");
        var confirm = Queue("developer-confirm");
        var developer = Queue("developer");
        var pageOwner = Queue("page-owner");
        var toApprove = entries.Count(e => (string?)e["state"] == "agree" && e["decision"] is null);
        static JsonArray Ids(IEnumerable<string> ids) => new([.. ids.Select(i => (JsonNode)i)]);
        result["queues"] = new JsonObject
        {
            ["ba"] = Ids(ba), ["unclear"] = findings.Count(e => (string?)e["state"] == "unclear" && !TraceFindings.IsGone(e)), ["agreeLinksToApprove"] = toApprove, ["developer"] = Ids(developer), ["developerConfirm"] = Ids(confirm), ["pageOwner"] = Ids(pageOwner),
            ["recheck"] = findings.Count(e => TraceFindings.Standing(e) == "recheck"), ["gone"] = Queue("gone").Count, ["closed"] = Queue("closed").Count, ["decided"] = Queue("decided").Count,
        };
        result["next"] = Next(doc, waiting, doubtful, uncovered, ba, confirm, developer, pageOwner, toApprove);
        return result;
    }

    /// <summary>A heading path has spaces in it, and the command should still run as written.</summary>
    static string Arg(string section) => section.Contains(' ', StringComparison.Ordinal) ? $"\"{section}\"" : section;

    static JsonObject Next(string doc, JsonObject? waiting, List<string> doubtful, List<JsonObject> uncovered, List<string> ba, List<string> confirm, List<string> developer, List<string> pageOwner, int toApprove)
    {
        static JsonObject Do(string who, string what, string command) => new() { ["who"] = who, ["what"] = what, ["command"] = command };
        // What changes the document comes first: nothing else is worth deciding on words about to change.
        if (waiting is not null && (bool?)waiting["first"] == true) return Do("a person", "Approve this first import, once its report has been read.", $"stangent fsd approve {doc}");
        if (doubtful.Count > 0)
        {
            // A candidate's section is settled by naming what it is. One that's only left on the current side is settled by saying it's gone.
            var inCandidate = doubtful.FirstOrDefault(d => !d.StartsWith("S-", StringComparison.Ordinal));
            return Do("a person", $"Settle {doubtful.Count} doubtful section(s) of the waiting candidate (`stangent fsd diff {doc} --format md` shows them), starting with this one.",
                inCandidate is not null ? $"stangent fsd resolve {doc} {Arg(inCandidate)} --same-as <S-id>" : $"stangent fsd resolve {doc} {doubtful[0]} --removed");
        }
        if (waiting is not null) return Do("a person", "Make the waiting candidate the current version.", $"stangent fsd promote {doc}");
        if (uncovered.Count > 0) return Do("a person or the AI", $"{uncovered.Count} section(s) have no link and no mark. Link them (this is a paid AI run), or mark one that isn't about the code: `stangent fsd not-in-code {doc} {Arg((string)uncovered[0]["section"]!)} --why <why>`.", $"plugin/scripts/spec-run.sh --fsd {doc}");
        if (ba.Count > 0) return Do("the BA", $"Decide {ba.Count} finding(s), by editing `decision` in specs/trace.yaml in a PR. `stangent trace check` lists them.", "stangent trace check");
        if (confirm.Count > 0) return Do("a developer", $"Read what the AI called refuted or unreachable, starting with {confirm[0]}, and confirm or send it back.", $"stangent trace confirm {confirm[0]} --why <why>");
        if (developer.Count > 0) return Do("a developer", $"Look at {developer.Count} finding(s) the AI reads as gaps in a page. `stangent trace check` lists them.", "stangent trace check");
        if (pageOwner.Count > 0) return Do("the page's owner", $"Write again the pages behind {pageOwner.Count} finding(s) a person said the page has wrong (a paid AI run).", "plugin/scripts/spec-run.sh --stale");
        if (toApprove > 0) return Do("the BA", $"Approve {toApprove} agree link(s), a chapter at a time.", $"stangent trace approve {doc} <chapter>");
        return Do("nobody", "Nothing is waiting.", "");
    }
}
