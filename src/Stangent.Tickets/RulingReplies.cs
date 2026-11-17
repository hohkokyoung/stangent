using System.Globalization;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Tickets.Proof;

namespace Stangent.Tickets;

/// <summary>
/// Truth map layer 5 (D364, D374), run by <c>ticket pull</c> with <c>truth-verdict</c> on: the comments after the posted
/// questions draft that has the PM's question, read for a line starting <c>Ruling:</c>. The entry the question was about is
/// the latest proof attempt's <c>verdict.entry</c>, or none ("not covered"), checked against the question that was posted;
/// the ruling is drafted into <c>rulings.yaml</c> uncommitted (<see cref="JiraRulings"/>), and a person confirms it by
/// committing. Code only. A reply that doesn't parse, doesn't fit, or disagrees with another person's is listed, never guessed.
/// </summary>
public static class RulingReplies
{
    /// <summary>The words <see cref="TicketDrafts.TruthQuestion"/> ends with: a posted draft holding them asked the PM.</summary>
    public const string Asked = "Reply with a line starting `Ruling:`";

    /// <summary>The replies drafted so far, by comment id (git-ignored, beside the ticket): one a person took out isn't drafted again.</summary>
    public const string DraftedFile = "rulings-drafted.json";

    public static JsonObject Read(StangentRepo repo, string key, JsonObject pulled)
    {
        if (!TruthList.On(repo.Config)) return new JsonObject { ["state"] = "off" };
        var paths = new TicketPaths(repo.Root, key);
        var dir = TicketDrafts.Dir(paths);
        var drafts = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "questions-*.json").Order(StringComparer.Ordinal).Select(f => PageRun.LoadJson(f) as JsonObject).OfType<JsonObject>().ToList()
            : [];
        // Every posted draft holding the question (an internal and a client one can both be posted): replies count from the first.
        var askers = drafts.Where(d => d["posted"] is JsonObject && ((string?)d["text"])?.Contains(Asked, StringComparison.Ordinal) == true)
            .OrderBy(d => (string?)d["posted"]!["at"], StringComparer.Ordinal).ToList();
        if (askers.Count == 0) return new JsonObject { ["state"] = "not-asked", ["summary"] = "No question for the PM has been posted on this ticket." };
        var ours = drafts.Select(d => (string?)d["posted"]?["commentId"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var question = new JsonObject
        {
            ["drafts"] = new JsonArray([.. askers.Select(d => (JsonNode)new JsonObject { ["draft"] = $"{d["kind"]}-{d["tone"]}", ["comment"] = (string)d["posted"]!["commentId"]!, ["at"] = (string)d["posted"]!["at"]! })]),
        };

        // The comments after it: by their place after the posted comment, or, when Jira no longer lists it, by time.
        var comments = pulled["comments"]!.AsArray().OfType<JsonObject>().ToList();
        var places = askers.Select(d => comments.FindIndex(c => (string?)c["id"] == (string)d["posted"]!["commentId"]!)).Where(i => i >= 0).ToList();
        var after = places.Count > 0 ? comments.Skip(places.Min() + 1).ToList()
            : comments.Where(c => Later((string?)c["created"], (string)askers[0]["posted"]!["at"]!)).ToList();
        var replies = new JsonArray();
        var read = new List<(JsonObject Comment, JiraRulings.Said Said)>();
        foreach (var c in after.Where(c => !ours.Contains((string)c["id"]!)))
        {
            var lines = ((string?)c["text"] ?? "").Replace("\r\n", "\n").Split('\n').Select(JiraRulings.RulingLine).OfType<string>().ToList();
            if (lines.Count == 0) continue;
            var saids = lines.Select(JiraRulings.Read).ToList();
            var reply = new JsonObject { ["comment"] = (string)c["id"]!, ["author"] = (string?)c["author"], ["created"] = (string?)c["created"], ["line"] = lines[0] };
            if (saids.Any(s => s is null)) reply["notRead"] = "nothing follows `Ruling:`";
            else if (saids.Distinct().Count() > 1) reply["notRead"] = $"it has {lines.Count} `Ruling:` lines that say different things";
            else if (saids[0]!.Unclear is { } unclear) reply["notRead"] = unclear;
            else
            {
                reply["said"] = saids[0]!.ToString();
                read.Add((c, saids[0]!));
            }
            replies.Add(reply);
        }
        var result = new JsonObject { ["state"] = "waiting", ["question"] = question, ["replies"] = replies };
        if (read.Count == 0)
        {
            result["summary"] = replies.Count == 0 ? "The PM's question is posted; no `Ruling:` reply yet." : "No `Ruling:` reply could be read: see `replies`.";
            if (replies.Count > 0) result["state"] = "listed";
            return result;
        }

        // Each person's latest reply replaces their earlier ones; when people's latest replies differ, a person settles it.
        var latest = read.GroupBy(r => (string?)r.Comment["author"] ?? "").Select(g => g.Last()).ToList();
        if (latest.Select(r => r.Said).Distinct().Count() > 1)
            return Listed(result, "Replies from different people say different things: settle it in Jira (each person's latest reply counts), then pull again. Nothing was drafted.");
        var (chosen, said) = read.Last(r => latest.Contains(r));
        foreach (var r in replies.OfType<JsonObject>().Where(r => r["said"] is not null && (string)r["comment"]! != (string)chosen["id"]!))
            r["replacedBy"] = (string)chosen["id"]!;
        var commentId = (string)chosen["id"]!;
        var jiraReply = new JiraRulings.Reply(paths.Key, commentId, (string?)chosen["author"] ?? "", (string?)chosen["created"] ?? "", said.Words ?? said.Word!);
        result["reply"] = commentId;

        if (JiraRulings.Recorded(repo.Root, paths.Key, commentId) is { } done)
        {
            result["state"] = "already";
            result["drafted"] = new JsonObject { ["doc"] = done.Doc, ["entry"] = done.Entry };
            result["summary"] = $"Comment {commentId}'s ruling is already in specs/fsd/{done.Doc}/rulings.yaml ({done.Entry}).";
            return result;
        }
        // Drafted before and taken out of rulings.yaml since (a person turned it down): not drafted again.
        var record = Path.Combine(paths.Dir, DraftedFile);
        var draftedBefore = PageRun.LoadJson(record) as JsonObject ?? [];
        if (draftedBefore[commentId] is JsonNode when)
            return Listed(result, $"Comment {commentId}'s ruling was drafted on {(string?)when} and has been taken out of rulings.yaml since, so it isn't drafted again (delete {paths.Relative(record)} to draft it again).");

        // Which entry: the proof that stopped, as long as it is still the one a posted question was written from.
        var stop = BugProof.Load(paths)?["attempts"]?.AsArray().LastOrDefault() as JsonObject;
        if (stop is null || BugProof.NoProof(stop) is not ("contradicted" or "unclear") || stop["verdict"] is not JsonObject verdict)
            return Listed(result, "The latest bug proof no longer stops on the truth map, so it can't tell which entry the reply was about. Nothing was drafted.");
        if (!askers.Any(d => ((string)d["text"]!).EndsWith(TicketDrafts.TruthQuestion(verdict, (string)d["tone"]!), StringComparison.Ordinal)))
            return Listed(result, "The bug proof has changed since the question was posted (it now stops on another entry or for another reason): post the new question. Nothing was drafted.");
        var contradicted = (string?)verdict["verdict"] == "contradicted";

        JsonObject drafted;
        if (verdict["entry"] is JsonObject entry)
            drafted = JiraRulings.Draft(repo, (string)entry["doc"]!, (JsonObject)entry["key"]!, (string)entry["state"]!, (string?)entry["sectionId"],
                [.. entry["evidence"]!.AsArray().Select(v => (string)v!)], jiraReply, contradicted);
        else
        {
            var docs = JiraRulings.Docs(repo.Root);
            if (docs.Count != 1)
                return Listed(result, docs.Count == 0 ? "This repo has no rulings.yaml to draft into. Nothing was drafted."
                    : $"Nothing covered the ticket, and this repo has {docs.Count} FSDs ({string.Join(", ", docs)}): which one the rule belongs to is for a person (`stangent fsd rule`). Nothing was drafted.");
            drafted = JiraRulings.Draft(repo, docs[0], null, null, null, TicketFiles(repo, paths.Key), jiraReply, contradicted);
        }
        result["drafted"] = drafted;
        if ((string)drafted["result"]! != "drafted")
            return Listed(result, $"Comment {commentId}'s `Ruling:` doesn't fit: {(string)drafted["why"]!}. Nothing was drafted.");
        var doc = (string)drafted["doc"]!;
        draftedBefore[commentId] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        Quotes.WriteAtomic(record, CanonicalJson.Serialize(draftedBefore));
        result["state"] = "drafted";
        result["summary"] = $"Drafted from comment {commentId} by {jiraReply.Author}: {(string)drafted["entry"]!} → {RulingWords(drafted)}{((string?)drafted["replaced"] is { } was ? $" (was {was})" : "")}, "
            + $"in {(string)drafted["file"]!}, not committed. A person confirms it by committing that file; until then it backs no ticket.";
        result["next"] = $"Read the change (`git diff {(string)drafted["file"]!}`), commit it to confirm, then `stangent fsd rulings-check {doc}` and prove the ticket again (`stangent ticket bundle {paths.Key}`, `stangent ticket prove {paths.Key}`).";
        return result;
    }

    static string RulingWords(JsonObject d) => (string)d["ruling"]! == "restated" ? $"in the PM's words: “{(string?)d["statement"]}”" : TruthMap.RulingWords((string)d["ruling"]!);

    static JsonObject Listed(JsonObject result, string why)
    {
        result["state"] = "listed";
        result["summary"] = why;
        return result;
    }

    /// <summary>A comment's time after the post's, when both read as times; otherwise not after (the safe side).</summary>
    static bool Later(string? created, string postedAt) =>
        DateTimeOffset.TryParse(Offset(created), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var c)
        && DateTimeOffset.TryParse(Offset(postedAt), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p) && c > p;

    /// <summary>Jira's <c>2026-11-17T10:00:00.000+0800</c> with the colon .NET reads (<c>+08:00</c>).</summary>
    static string? Offset(string? t) => t is null ? null : System.Text.RegularExpressions.Regex.Replace(t, @"([+-]\d{2})(\d{2})$", "$1:$2");

    /// <summary>The code a "not covered" rule is tied to: the ticket's file points, and its symbol points as <c>file:line</c>.</summary>
    static List<string> TicketFiles(StangentRepo repo, string key)
    {
        var bundle = TicketBundle.LoadOrFail(repo.Root, key);
        var points = bundle["points"]!.AsArray().OfType<JsonObject>().ToList();
        CodeGraph? graph = points.Any(p => (string?)p["kind"] == "symbol") ? GraphStore.Load(GraphStore.PathFor(repo)) : null;
        var nodes = graph?.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (var p in points)
        {
            var id = (string)p["id"]!;
            if ((string?)p["kind"] == "file") lines.Add(id);
            else if ((string?)p["kind"] == "symbol" && nodes?.GetValueOrDefault(id) is { File: { } f } n) lines.Add(n.Line > 0 ? $"{f}:{n.Line}" : f);
        }
        return [.. lines.Distinct(StringComparer.Ordinal)];
    }
}
