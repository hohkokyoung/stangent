using System.Text.Json.Nodes;
using Stangent.Benchmark.Tickets;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Tickets;

namespace Stangent.Benchmark.Tests;

/// <summary>Phase 2c's match score (D99), by the rule frozen in the key, on made-up tickets.</summary>
public sealed class MatchScoreTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-match-").FullName;
    readonly List<JsonNode> tickets = [];
    public void Dispose() => Directory.Delete(root, recursive: true);

    string Key()
    {
        var path = Path.Combine(root, "key.json");
        File.WriteAllText(path, new JsonObject { ["tickets"] = new JsonArray([.. tickets.Select(t => t.DeepClone())]) }.ToJsonString());
        return path;
    }

    void Requirement(string id, string quote)
    {
        var q = new JsonObject { ["doc"] = "d", ["section"] = "1", ["text"] = quote, ["hash"] = "h" };
        var existing = RequirementFile.Features(root, "d").FirstOrDefault();
        var feature = existing ?? new JsonObject
        {
            ["version"] = 1, ["id"] = "F-0001", ["doc"] = "d", ["section"] = "1", ["title"] = "t", ["parent"] = null, ["children"] = new JsonArray(), ["textHash"] = null,
            ["requirements"] = new JsonArray(), ["definitions"] = new JsonArray(), ["journeys"] = new JsonArray(), ["background"] = new JsonArray(), ["retired"] = new JsonArray(),
        };
        feature["requirements"]!.AsArray().Add((JsonNode)new JsonObject
        {
            ["id"] = id, ["title"] = "t", ["kind"] = "rule", ["statement"] = "s", ["quotes"] = new JsonArray((JsonNode)q), ["acceptance"] = new JsonArray(),
            ["refers"] = new JsonArray(), ["status"] = "proposed", ["check"] = "confirmed", ["note"] = null, ["hash"] = "h",
        });
        RequirementFile.Save(root, feature);
    }

    void Ticket(string id, string group, string? quote, string outcome, string? pick = null, bool skipped = true, string finder = "found")
    {
        tickets.Add(new JsonObject { ["id"] = id, ["group"] = group, ["quotes"] = quote is null ? new JsonArray() : new JsonArray((JsonNode)quote) });
        var match = new JsonObject
        {
            ["outcome"] = outcome, ["approvalSkipped"] = skipped, ["finder"] = new JsonObject { ["status"] = finder, ["points"] = 1 }, ["considered"] = new JsonArray(),
            ["usage"] = new JsonObject { ["matcher"] = new Usage("m", 1, 1, 1, 0, 0, 0.5, 1).ToJson(), ["checker"] = Usage.None.ToJson() },
        };
        if (pick is not null) match["requirement"] = new JsonObject { ["id"] = pick, ["codeFound"] = true };
        var paths = new TicketPaths(root, id);
        Directory.CreateDirectory(paths.Dir);
        File.WriteAllText(Path.Combine(paths.Dir, "match.json"), match.ToJsonString());
    }

    [Fact]
    public void Right_means_the_quotes_overlap_one_way_or_the_other()
    {
        Assert.True(MatchScore.Overlap(["One clause of it"], ["One clause of it, and another."]));
        Assert.True(MatchScore.Overlap(["The whole  sentence, and more."], ["The whole sentence"]));
        Assert.False(MatchScore.Overlap(["Something else entirely."], ["The whole sentence"]));
    }

    [Fact]
    public void A_suggestion_on_a_none_ticket_is_wrong_and_each_bar_is_counted_by_the_frozen_rule()
    {
        Requirement("R-0001", "The right sentence here.");
        Requirement("R-0002", "A different sentence here.");
        Ticket("MK-1", "requirement-code", "The right sentence here.", "suggested", "R-0001");
        Ticket("MK-2", "requirement-no-code", "The right sentence here.", "suggested", "R-0002");
        Ticket("MK-3", "none", null, "none");
        Ticket("MK-4", "none", null, "suggested", "R-0001");
        var score = MatchScore.Run(root, Key());
        Assert.Equal((1, 3), ((int)score["suggestions"]!["right"]!, (int)score["suggestions"]!["of"]!));
        Assert.Equal(1, (int)score["none"]!["right"]!);
        Assert.Equal(1, (int)score["found"]!["right"]!);
        Assert.Equal(2.0, (double)score["matchSpentUsd"]!);
    }

    [Fact]
    public void A_failed_run_scores_nothing_and_is_counted_apart()
    {
        Ticket("MK-1", "none", null, "failed", finder: "failed");
        tickets.Add(new JsonObject { ["id"] = "MK-2", ["group"] = "none", ["quotes"] = new JsonArray() }); // never run
        var score = MatchScore.Run(root, Key());
        Assert.Equal(0, (int)score["none"]!["right"]!);
        Assert.Equal(2, (int)score["failedRuns"]!);
        Assert.False((bool)score["suggestions"]!["pass"]!);   // no suggestions at all fails this bar
    }

    [Fact]
    public void What_ships_follows_the_bars_and_the_note_on_approval_comes_from_the_records()
    {
        Requirement("R-0001", "The right sentence here.");
        for (var i = 1; i <= 14; i++) Ticket($"MK-{i}", "requirement-code", "The right sentence here.", "suggested", "R-0001");
        for (var i = 15; i <= 22; i++) Ticket($"MK-{i}", "none", null, "none", skipped: false);
        var score = MatchScore.Run(root, Key());
        Assert.Equal("ships on", (string)score["outcome"]!);
        Assert.Equal(14, (int)score["approvalSkipped"]!);

        tickets.RemoveAll(t => (string)t["id"]! == "MK-14");
        Assert.Equal("ships on, and the report says it finds too few", (string)MatchScore.Run(root, Key())["outcome"]!);
        tickets.RemoveAll(t => (string)t["id"]! == "MK-22");
        Assert.StartsWith("ships off", (string)MatchScore.Run(root, Key())["outcome"]!);
    }

    [Fact]
    public void The_score_holds_no_words_from_a_ticket()
    {
        tickets.Add(new JsonObject { ["id"] = "MK-1", ["group"] = "none", ["summary"] = "SECRET SUMMARY", ["description"] = "SECRET BODY", ["why"] = "SECRET WHY", ["quotes"] = new JsonArray() });
        Assert.DoesNotContain("SECRET", MatchScore.Run(root, Key()).ToJsonString());
    }
}
