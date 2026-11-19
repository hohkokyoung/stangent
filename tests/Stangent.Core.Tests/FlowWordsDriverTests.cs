namespace Stangent.Core.Tests;

/// <summary>D416's driver, <c>plugin/scripts/fsd-view-run.sh</c>: no cost choice, no call; with one, <c>fsd view</c> →
/// the writer with no tools → <c>fsd view-check</c>, until nothing is left. Fake claude and stangent only (D112).</summary>
public sealed class FlowWordsDriverTests : IDisposable
{
    const string Driver = "plugin/scripts/fsd-view-run.sh";
    const string Step = """{"ok":true,"result":{"status":"prepared","next":{"action":"view","agent":"stangent-view-writer","prompt":"Input: .stangent/fsd/shop/view/input.md\nWrite your JSON to: .stangent/fsd/shop/view/output.json","output":".stangent/fsd/shop/view/output.json","schema":"fsd-view-output.v1.json","limits":{"tools":"","permissionMode":"dontAsk","maxTurns":4,"maxBudgetUsd":1}},"summary":"1 of 1 linked flows prepared for the writer."}}""";
    const string Done = """{"ok":true,"result":{"status":"checked","next":{"action":"none"},"summary":"1 flow in plain words"}}""";

    readonly DriverRig rig = new();

    public FlowWordsDriverTests() => rig.Put(".stangent/fsd/shop/view/input.md", "FLOWS-TO-WORD");
    public void Dispose() => rig.Dispose();

    void Fake() => rig.Stangent($$"""
        "fsd view shop --max-cost "*) echo '{{Step}}' ;;
        "fsd view-check shop --usage "*) echo '{{Done}}' ;;
        """);

    [Fact]
    public void No_cost_choice_makes_no_call()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Env["STANGENT_MAX_COST"] = "";
        Fake();
        var run = rig.Run(Driver, "shop");
        Assert.Equal(2, run.Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.DoesNotContain(rig.Calls(), c => c.StartsWith("stangent fsd view", StringComparison.Ordinal));
    }

    [Fact]
    public void The_writer_runs_with_no_tools_and_its_answer_is_checked()
    {
        if (OperatingSystem.IsWindows()) return;
        Fake();
        rig.ClaudeAnswers(DriverRig.GoodAnswer);
        var run = rig.Run(Driver, "--max-cost", "1", "shop");
        Assert.Equal(0, run.Exit);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--tools  ", claude + " ");   // an empty tool list: it reads nothing
        Assert.Contains("--max-turns 4", claude);
        Assert.Contains("FLOWS-TO-WORD", rig.ClaudeStdin);
        Assert.Equal(["stangent fsd view shop --max-cost 1"], rig.StangentCalls().Where(c => c.StartsWith("stangent fsd view ", StringComparison.Ordinal)));
        Assert.Contains(rig.StangentCalls(), c => c.StartsWith("stangent fsd view-check shop --usage ", StringComparison.Ordinal));
        Assert.Contains("worded\tshop\tview\t1 flow in plain words", run.Out);
    }
}
