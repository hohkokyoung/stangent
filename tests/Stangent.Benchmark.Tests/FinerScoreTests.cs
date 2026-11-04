using System.Text.Json.Nodes;
using Stangent.Benchmark.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Benchmark.Tests;

/// <summary>The scorer counts <c>partial</c> and <c>unclear</c> out loud (C.4, D124), on a hand-written key.</summary>
public sealed class FinerScoreTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-finer-score").FullName;
    public void Dispose() => Directory.Delete(root, true);

    static JsonObject Section(string id) => new() { ["id"] = id, ["title"] = "s", ["permanentId"] = "S-000" + id };

    static JsonObject Entry(string state, string section, string quote) =>
        LinkRun.Entry(state, "d", Section(section), quote, state == "partial" ? "page:/a" : null, state == "partial" ? "R1" : null, state == "partial" ? "the page says part of this here" : null, "judge", null, "n");

    string Key()
    {
        var path = Path.Combine(root, "key.json");
        File.WriteAllText(path, new JsonObject
        {
            ["doc"] = "d",
            ["items"] = new JsonArray(
                new JsonObject { ["id"] = "P1", ["kind"] = "gap", ["section"] = "1", ["text"] = "Alpha orders are checked every single day." },
                new JsonObject { ["id"] = "P2", ["kind"] = "conflict", ["section"] = "2", ["text"] = "Beta orders may wait for thirty days." },
                new JsonObject { ["id"] = "P3", ["kind"] = "undocumented", ["pages"] = new JsonArray("page:/none") }),
        }.ToJsonString());
        return path;
    }

    void Trace(params JsonObject[] entries)
    {
        var trace = TraceFile.Empty();
        foreach (var e in entries) trace["entries"]!.AsArray().Add((JsonNode)e);
        TraceFile.Save(root, trace);
    }

    [Fact]
    public void A_partial_on_a_planted_item_is_found_not_as_its_kind_and_an_unclear_one_is_neither()
    {
        Trace(Entry("partial", "1", "Alpha orders are checked every single day."), Entry("unclear", "2", "Beta orders may wait for thirty days."));
        var score = FsdScore.Run(root, Key(), null);
        var items = score["items"]!.AsArray();
        Assert.True((bool)items[0]!["found"]!);
        Assert.False((bool)items[0]!["foundAsKind"]!);
        Assert.False((bool)items[1]!["found"]!);
        Assert.Equal(1, (int)score["found"]!["overall"]!["found"]!);
        // The unclear one isn't noise either: only the partial is a finding.
        Assert.Equal(1, (int)score["noise"]!["findings"]!);
        Assert.Equal(0, (int)score["noise"]!["unplanted"]!);
        var finer = score["finerVerdicts"]!;
        Assert.Equal((1, 1), ((int)finer["partial"]!, (int)finer["unclear"]!));
        Assert.Equal("P1", (string)finer["partialFoundPlanted"]![0]!);
    }

    [Fact]
    public void A_table_fact_is_not_scored_against_the_key()
    {
        var fact = Entry("table-fact", "1", "Alpha orders are checked every single day.");
        Trace(fact);
        var score = FsdScore.Run(root, Key(), null);
        Assert.False((bool)score["items"]![0]!["found"]!);
        Assert.Equal(0, (int)score["noise"]!["findings"]!);
    }

    [Fact]
    public void With_neither_it_says_nothing()
    {
        Trace(Entry("gap", "1", "Alpha orders are checked every single day."));
        Assert.Null(FsdScore.Run(root, Key(), null)["finerVerdicts"]);
    }
}
