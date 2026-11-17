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
        var facts = FsdDocument.Facts(root, doc);
        if (facts is null && waiting is null) throw new StangentException("not_found", $"No imported document {doc}.", ExitCodes.NotFound);
        // The sections are the committed chapters' (phase fs layer 2): an import not committed yet, or chapters refused, read none.
        JsonObject? document = null;
        StangentException? unread = null;
        if (facts is not null)
            try { document = FsdDocument.Load(root, doc); }
            catch (StangentException e) when (e.Code is "fsd_not_committed" or "fsd_not_migrated" or "not_git" or "invalid_fsd") { unread = e; }

        var doubtful = waiting is null ? [] : waiting["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful").Select(e => (string?)e["to"] ?? (string)e["permanentId"]!).ToList();
        var result = new JsonObject { ["doc"] = doc };
        if (facts is not null)
        {
            FsdDocument.Note(root, doc, result);
            result["current"] = new JsonObject
            {
                ["hash"] = (string)facts["original"]!["hash"]!, ["title"] = (string)facts["title"]!, ["sections"] = document?["sections"]!.AsArray().Count, ["verdict"] = ImportReport.Verdict(facts),
            };
            if (unread is not null) result["unread"] = new JsonObject { ["code"] = unread.Code, ["message"] = unread.Message };
        }
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
        var reviewed = document is not null && File.Exists(FsdReview.ReviewJson(root, doc));
        result["reviewed"] = reviewed;
        result["next"] = unread is not null && waiting is null ? Unread(doc, unread) : Next(doc, waiting, doubtful, document is not null && !reviewed, ba, confirm, developer, pageOwner, toApprove);
        return result;
    }

    /// <summary>What to do when no step can read the document yet: commit it, migrate it, or put its ids right.</summary>
    static JsonObject Unread(string doc, StangentException e) => e.Code switch
    {
        "fsd_not_committed" => new() { ["who"] = "a person", ["what"] = $"Read specs/fsd/{doc}/chapters/ and commit specs/fsd/{doc}/: the commit approves the import, and until then no step reads it (D380).", ["command"] = $"git add specs/fsd/{doc} && git commit" },
        "fsd_not_migrated" => new() { ["who"] = "a person", ["what"] = "Turn the document into chapter files, then commit them (D383).", ["command"] = $"stangent fsd migrate {doc}" },
        _ => new() { ["who"] = "a person", ["what"] = e.Message, ["command"] = "" },
    };

    /// <summary>A heading path has spaces in it, and the command should still run as written.</summary>
    static string Arg(string section) => section.Contains(' ', StringComparison.Ordinal) ? $"\"{section}\"" : section;

    static JsonObject Next(string doc, JsonObject? waiting, List<string> doubtful, bool notReviewed, List<string> ba, List<string> confirm, List<string> developer, List<string> pageOwner, int toApprove)
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
        // The old link route is cut (D342): the review is how a document is checked against the code now.
        if (notReviewed) return Do("a person or the AI", "Review the document against the source: two reviewer runs, checked and merged by code (D341). This is a paid AI run; `--estimate` shows the cost first.", $"plugin/scripts/fsd-review-run.sh --max-cost 8 {doc}");
        if (ba.Count > 0) return Do("the BA", $"Decide {ba.Count} finding(s), by editing `decision` in specs/trace.yaml in a PR. `stangent trace check` lists them.", "stangent trace check");
        // `trace confirm` went with the disprover (D342): a person settles these by their decision, as any finding.
        if (confirm.Count > 0) return Do("a developer", $"Read what the old disprover called refuted, unreachable or a strict page gap, starting with {confirm[0]}, and set its `decision` in specs/trace.yaml in a PR. `stangent trace findings {doc}` lists them.", $"stangent trace findings {doc}");
        if (developer.Count > 0) return Do("a developer", $"Look at {developer.Count} finding(s) the AI reads as gaps in a page. `stangent trace check` lists them.", "stangent trace check");
        if (pageOwner.Count > 0) return Do("the page's owner", $"Write again the pages behind {pageOwner.Count} finding(s) a person said the page has wrong (a paid AI run).", "plugin/scripts/spec-run.sh --stale");
        if (toApprove > 0) return Do("the BA", $"Approve {toApprove} agree link(s), a chapter at a time.", $"stangent trace approve {doc} <chapter>");
        return Do("nobody", "Nothing is waiting.", "");
    }
}
