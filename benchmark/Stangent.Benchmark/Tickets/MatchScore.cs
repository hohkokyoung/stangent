using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Tickets;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// Phase 2c's matching score (D99), by the rule frozen inside <c>benchmark/fsd/sonarr/match-key.json</c>. The key's tickets go
/// through the same steps as any ticket; only this code reads the key, and it prints ids, never a ticket's words, so the person
/// building matching stays blind to them.
/// </summary>
public static class MatchScore
{
    /// <summary>A tracker holding the key's tickets, as if they'd been pulled.</summary>
    sealed class KeyTracker(JsonArray tickets) : IIssueTracker
    {
        public string Source => "jira";
        public bool Replaying => true;

        public Task<Ticket> GetAsync(string key, CancellationToken ct = default)
        {
            var t = tickets.First(x => (string)x!["id"]! == key)!;
            return Task.FromResult(new Ticket
            {
                Key = key, Id = key, Type = (string)t["type"]!, Status = "To Do", Summary = (string)t["summary"]!,
                Description = (string)t["description"]!, DescriptionFormat = "markdown",
            });
        }

        public Task<SavedFile> DownloadAsync(Ticket ticket, TicketAttachment attachment, string destination, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PostedComment> PostCommentAsync(string key, string markdown, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public static async Task<List<string>> SeedAsync(string clone, string keyPath)
    {
        var tickets = JsonNode.Parse(File.ReadAllText(keyPath))!["tickets"]!.AsArray();
        var repo = StangentRepo.Discover(clone);
        await using var tracker = new KeyTracker(tickets);
        var keys = new List<string>();
        foreach (var t in tickets)
        {
            await TicketPull.RunAsync(repo, tracker, (string)t!["id"]!);
            keys.Add((string)t["id"]!);
        }
        return keys;
    }

    /// <summary>One suggestion is right when one of its FSD quotes and one of the ticket's listed quotes overlap: whitespace collapsed,
    /// case kept, one holds the other.</summary>
    public static bool Overlap(IEnumerable<string> quotes, IEnumerable<string> listed) =>
        quotes.Select(Quotes.Normalize).Any(q => listed.Select(Quotes.Normalize).Any(l => q.Contains(l, StringComparison.Ordinal) || l.Contains(q, StringComparison.Ordinal)));

    public static JsonObject Run(string clone, string keyPath)
    {
        var key = JsonNode.Parse(File.ReadAllText(keyPath))!.AsObject();
        var rows = new JsonArray();
        int suggestions = 0, right = 0, none = 0, found = 0, failed = 0;
        double spent = 0;
        var quotesOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var doc in RequirementFile.Docs(clone))
            foreach (var f in RequirementFile.Features(clone, doc))
                foreach (var r in f["requirements"]!.AsArray().OfType<JsonObject>())
                    quotesOf[(string)r["id"]!] = [.. r["quotes"]!.AsArray().Select(q => (string)q!["text"]!)];
        foreach (var t in key["tickets"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string)t["id"]!;
            var group = (string)t["group"]!;
            var listed = t["quotes"]!.AsArray().Select(q => (string)q!).ToList();
            var match = TicketMatch.Load(clone, id);
            var outcome = (string?)match?["outcome"] ?? "not run";
            var isFailed = outcome is "failed" or "not run";
            if (isFailed) failed++;
            spent += match is null ? 0 : Usage.FromJson(match["usage"]!["matcher"]).CostUsd + Usage.FromJson(match["usage"]!["checker"]).CostUsd;
            var pick = outcome == "suggested" ? (string)match!["requirement"]!["id"]! : null;
            var isRight = pick is not null && group != "none" && Overlap(quotesOf.GetValueOrDefault(pick) ?? [], listed);
            if (pick is not null) { suggestions++; if (isRight) right++; }
            if (group == "none" && outcome == "none") none++;
            if (group != "none" && isRight) found++;
            // Whether the right requirement was among those the AI could pick: says where a miss happened.
            var considered = match?["considered"]?.AsArray().Select(c => (string)c!).ToList() ?? [];
            rows.Add(new JsonObject
            {
                ["id"] = id, ["group"] = group, ["outcome"] = outcome, ["pick"] = pick, ["right"] = group == "none" ? outcome == "none" : isRight,
                ["codeFound"] = match?["requirement"]?["codeFound"]?.DeepClone(), ["reporter"] = match?["reporter"]?.DeepClone(),
                ["refused"] = match?["refused"]?["pick"]?.DeepClone(),
                ["rightWasCandidate"] = group == "none" ? null : considered.Any(c => Overlap(quotesOf.GetValueOrDefault(c) ?? [], listed)),
                ["candidates"] = match?["candidates"]?.DeepClone(),
            });
        }
        var share = suggestions == 0 ? 0 : (double)right / suggestions;
        var passRight = suggestions > 0 && share >= 0.9;
        var passNone = none >= 8;
        var passFound = found >= 14;
        return new JsonObject
        {
            ["note"] = "Phase 2c (D99): matching scored on the frozen key by its own rule, with approval skipped (the benchmark setting). It's our own test set: a real FSD stays the judge (D56).",
            ["suggestions"] = new JsonObject { ["right"] = right, ["of"] = suggestions, ["share"] = Math.Round(share, 3), ["bar"] = 0.9, ["pass"] = passRight },
            ["none"] = new JsonObject { ["right"] = none, ["of"] = 10, ["bar"] = 8, ["pass"] = passNone },
            ["found"] = new JsonObject { ["right"] = found, ["of"] = 20, ["bar"] = 14, ["pass"] = passFound },
            ["failedRuns"] = failed,
            ["outcome"] = !(passRight && passNone) ? "ships off: requirements stay context" : passFound ? "ships on" : "ships on, and the report says it finds too few",
            ["matchSpentUsd"] = Math.Round(spent, 3),
            ["tickets"] = rows,
        };
    }
}
