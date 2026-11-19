using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>What's left of writing trace.yaml once the link route was cut (D342): table facts and the frozen links go through it.</summary>
public class TraceStoreTests
{
    static readonly JsonObject Section = new() { ["id"] = "1", ["title"] = "Orders" };

    static JsonObject Agree() => TraceStore.Entry("agree", "shop", Section, "Orders need approval.", "page:/orders", "A1", "The Place button submits the order.", "judge", "confirmed", null);

    static void Decide(string root, string id, string decision)
    {
        var trace = TraceFile.Load(root);
        var e = trace["entries"]!.AsArray().OfType<JsonObject>().Single(x => (string)x["id"]! == id);
        e["decision"] = decision;
        e["decided_by"] = "Pat";
        TraceFile.Save(root, trace);
    }

    [Fact]
    public void An_entry_written_again_keeps_the_persons_decision_and_one_that_doesnt_come_back_drops_it_on_the_module()
    {
        using var repo = new TempRepo();
        var link = Agree();
        var id = (string)link["id"]!;
        TraceStore.ApplyEntries(repo.Root, "shop", "1", [link]);
        Decide(repo.Root, id, "agree");

        TraceStore.ApplyEntries(repo.Root, "shop", "1", [Agree()]);
        var kept = Assert.Single(TraceFile.Load(repo.Root)["entries"]!.AsArray())!;
        Assert.Equal(("agree", "Pat"), ((string?)kept["decision"], (string?)kept["decided_by"]));

        // A decided agree link nobody writes again goes, and its decision's drop is on the history, not lost.
        TraceStore.ApplyEntries(repo.Root, "shop", "1", []);
        Assert.Empty(TraceFile.Load(repo.Root)["entries"]!.AsArray());
        var drop = StateChanges.History(repo.Root, StateKinds.TraceDecision, id).Last();
        Assert.Equal(("agree", "undecided"), (drop.From, drop.To));
    }

    [Fact]
    public void Another_sections_entries_are_left_alone()
    {
        using var repo = new TempRepo();
        var other = TraceStore.Entry("gap", "shop", new JsonObject { ["id"] = "2", ["title"] = "Stock" }, "Stock is counted.", null, null, null, "judge", null, "n");
        TraceStore.ApplyEntries(repo.Root, "shop", "2", [other]);
        TraceStore.ApplyEntries(repo.Root, "shop", "1", [Agree()]);
        TraceStore.ApplyEntries(repo.Root, "shop", "1", []);
        Assert.Equal((string)other["id"]!, (string)Assert.Single(TraceFile.Load(repo.Root)["entries"]!.AsArray())!["id"]!);
    }

    [Theory]
    [InlineData("fsd: { disprove: true }\n", "disprove")]
    [InlineData("spec: { ask: true }\n", "ask")]
    public void A_config_holding_a_cut_setting_says_it_was_cut(string yaml, string key)
    {
        using var repo = new TempRepo();
        File.AppendAllText(Path.Combine(repo.Root, StangentRepo.ConfigFile), yaml);
        var e = Assert.Throws<StangentException>(() => StangentRepo.Discover(repo.Root));
        Assert.Equal("invalid_config", e.Code);
        Assert.Contains($"`{key}` was cut with the old link route (D342)", e.Message);
    }
}
