using System.Text.Json.Nodes;
using Stangent.Benchmark.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Benchmark.Tests;

/// <summary>The scorer on a hand-written key and a hand-written output, so it's known to work before anyone pays for a run (D119). The numbers below are worked out by hand.</summary>
public sealed class DisproveScoreTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-disprove-score").FullName;
    public void Dispose() => Directory.Delete(root, true);

    static JsonObject Section(string id) => new() { ["id"] = id, ["title"] = "T" + id };

    static JsonObject Finding(string state, string section, string quote, string page, string? verdict)
    {
        var e = LinkRun.Entry(state, "d", Section(section), quote, page, state == "conflict" ? "R1" : null, state == "conflict" ? "the page says something else here" : null, "judge", null, "note");
        if (verdict is not null) e["verdict"] = verdict;
        return e;
    }

    string Key()
    {
        var path = Path.Combine(root, "key.json");
        JsonObject Item(string id, string kind, string section, string text) => new() { ["id"] = id, ["kind"] = kind, ["section"] = section, ["text"] = text };
        File.WriteAllText(path, new JsonObject
        {
            ["doc"] = "d",
            ["items"] = new JsonArray(
                Item("P1", "gap", "1", "Alpha orders are checked every single day."),
                Item("P2", "conflict", "2", "Beta orders may wait for thirty days."),
                Item("P3", "gap", "3", "Gamma orders always send one receipt."),
                new JsonObject { ["id"] = "P4", ["kind"] = "undocumented", ["pages"] = new JsonArray("page:/none") }),
        }.ToJsonString());
        return path;
    }

    [Fact]
    public void Before_and_after_are_counted_from_the_key_and_the_hand_check()
    {
        var unplantedFalse = Finding("gap", "4", "Delta orders are sent by carrier pigeon.", "page:/a", "refuted");
        var unplantedFalse2 = Finding("gap", "5", "Epsilon orders are sent by slow boat.", "page:/a", "unreachable");
        var unplantedFalse3 = Finding("gap", "6", "Zeta orders are sent by fast train.", "page:/a", "cant-tell");
        var unplantedReal = Finding("gap", "7", "Eta orders are sent by the night mail.", "page:/a", "stands");
        var unplantedGap = Finding("gap", "8", "Theta orders are sent by rolling them.", "page:/a", "page-gap");
        var trace = TraceFile.Empty();
        foreach (var e in new[]
        {
            Finding("gap", "1", "Alpha orders are checked every single day.", "page:/a", "stands"),          // P1 found, stays
            Finding("conflict", "2", "Beta orders may wait for thirty days.", "page:/a", "refuted"),         // P2 found, lost: refuted
            Finding("gap", "3", "Gamma orders always send one receipt.", "page:/a", "page-gap"),             // P3 found, lost: to developers
            unplantedFalse, unplantedFalse2, unplantedFalse3, unplantedReal, unplantedGap,
        }) trace["entries"]!.AsArray().Add((JsonNode)e);
        TraceFile.Save(root, trace);

        var hand = Path.Combine(root, "hand.json");
        File.WriteAllText(hand, new JsonObject
        {
            ["_checker"] = "by hand",
            [(string)unplantedFalse["id"]!] = new JsonObject { ["verdict"] = "false" },
            [(string)unplantedFalse2["id"]!] = new JsonObject { ["verdict"] = "false" },
            [(string)unplantedFalse3["id"]!] = new JsonObject { ["verdict"] = "false" },
            [(string)unplantedReal["id"]!] = new JsonObject { ["verdict"] = "real" },
            [(string)unplantedGap["id"]!] = new JsonObject { ["verdict"] = "false" },
        }.ToJsonString());

        var score = DisproveScore.Run(root, Key(), hand);

        Assert.Equal(3, (int)score["before"]!["found"]!["found"]!);
        Assert.Equal(1, (int)score["after"]!["found"]!["overall"]!["found"]!);
        Assert.Equal(8, (int)score["before"]!["noise"]!["findings"]!);
        Assert.Equal(3, (int)score["after"]!["findings"]!);                 // P1, the cant-tell, the stands

        Assert.Equal(new[] { "P2", "P3" }, score["plantedLost"]!.AsArray().Select(l => (string)l!["item"]!));
        Assert.Equal("refuted", (string)score["plantedLost"]![0]!["verdict"]!);
        Assert.Equal("developer", (string)score["plantedLost"]![1]!["goesTo"]!);

        Assert.Equal(2, (int)score["byVerdict"]!["refuted"]!);              // P2 and the first false alarm
        Assert.Equal(2, (int)score["byVerdict"]!["stands"]!);
        Assert.Equal(1, (int)score["byVerdict"]!["cant-tell"]!);
        Assert.Equal(2, (int)score["byVerdict"]!["page-gap"]!);
        Assert.Equal(1, (int)score["byVerdict"]!["unreachable"]!);
        Assert.Equal(0, (int)score["byVerdict"]!["unverified"]!);
        Assert.Equal((3, 3, 2), ((int)score["byQueue"]!["ba"]!, (int)score["byQueue"]!["closed"]!, (int)score["byQueue"]!["developer"]!));

        Assert.Equal((4, 3, 1), ((int)score["handChecked"]!["false"]!["checked"]!, (int)score["handChecked"]!["false"]!["leftTheQueue"]!, (int)score["handChecked"]!["false"]!["stillInTheQueue"]!));
        Assert.Equal((1, 0, 1), ((int)score["handChecked"]!["real"]!["checked"]!, (int)score["handChecked"]!["real"]!["leftTheQueue"]!, (int)score["handChecked"]!["real"]!["stillInTheQueue"]!));
        // 2 of 3 queued findings are unplanted (0.667 as the score rounds it); of the hand-checked ones still queued, 1 of 2 is false: 0.667 / 2.
        Assert.Equal(0.334, (double)score["after"]!["falseAlarmShare"]!);
        Assert.False((bool)score["bars"]!["found27of30"]!);
        Assert.False((bool)score["bars"]!["falseAlarmsAtMost30Percent"]!);
        Assert.Equal(24, (int)score["before2c"]!["found"]!);
    }

    [Fact]
    public void With_no_verdicts_after_is_before()
    {
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add((JsonNode)Finding("gap", "1", "Alpha orders are checked every single day.", "page:/a", null));
        TraceFile.Save(root, trace);
        var score = DisproveScore.Run(root, Key(), null);
        Assert.Equal((1, 1), ((int)score["before"]!["found"]!["found"]!, (int)score["after"]!["found"]!["overall"]!["found"]!));
        Assert.Empty(score["plantedLost"]!.AsArray());
        Assert.Null(score["bars"]!["falseAlarmsAtMost30Percent"]);
    }
}
