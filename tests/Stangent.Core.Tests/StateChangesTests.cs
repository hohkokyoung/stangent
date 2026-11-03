using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Core.State;

namespace Stangent.Core.Tests;

/// <summary>The state module (D114): every table, and the history.</summary>
public partial class StateChangesTests
{
    public static TheoryData<string, string, string, string> EveryMove()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var kind in StateKinds.All)
            foreach (var rule in kind.Moves)
                foreach (var who in rule.Who)
                    data.Add(kind.Name, rule.From, rule.To, who);
        return data;
    }

    public static TheoryData<string> EveryKind() => [.. StateKinds.All.Select(k => k.Name)];

    [Theory]
    [MemberData(nameof(EveryMove))]
    public void A_legal_move_passes_and_records_history(string kind, string from, string to, string who)
    {
        using var temp = new TempRepo();
        Assert.True(StateChanges.Move(temp.Root, kind, "X-1", from, to, new StateActor(who, "someone"), "because"));

        var move = Assert.Single(StateChanges.History(temp.Root, kind, "X-1"));
        Assert.Equal((kind, "X-1", from, to, who, "someone", "because"), (move.Kind, move.Id, move.From, move.To, move.Who.Kind, move.Who.Name, move.Reason));
        Assert.True(DateTimeOffset.TryParse(move.At, out _));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void An_illegal_move_is_refused_with_the_legal_ones_and_leaves_no_history(string name)
    {
        using var temp = new TempRepo();
        var kind = StateKinds.Get(name);
        var from = kind.Moves[0].From;
        var e = Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, name, "X-1", from, "no-such-state", StateActor.Code("t"), "r"));

        Assert.Equal("illegal_transition", e.Code);
        Assert.Equal(ExitCodes.Failed, e.ExitCode);
        Assert.Equal(kind.Moves.Count(m => m.From == from), e.Details.Count);
        Assert.Contains(e.Details, d => d.StartsWith($"{from} -> {kind.Moves[0].To} by ", StringComparison.Ordinal));
        Assert.Empty(StateChanges.History(temp.Root, name, "X-1"));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void No_table_lets_an_agent_move_anything(string name)
    {
        using var temp = new TempRepo();
        var kind = StateKinds.Get(name);
        Assert.DoesNotContain(kind.Moves, m => m.Who.Contains(StateActor.AgentKind));
        foreach (var rule in kind.Moves)
            Assert.Equal("illegal_transition", Assert.Throws<StangentException>(() =>
                StateChanges.Move(temp.Root, name, "X-1", rule.From, rule.To, StateActor.Agent("writer"), "r")).Code);
        if (kind.NullState is null)
            Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, name, "X-1", null, kind.Initial[0], StateActor.Agent("writer"), "r"));
    }

    [Fact]
    public void Who_may_move_is_held_by_the_table()
    {
        using var temp = new TempRepo();
        // Only a person approves (D99) or decides (D58); only a person confirms a bug (D96) or posts (D74).
        foreach (var (kind, from, to) in new[] { (StateKinds.Requirement, "proposed", "approved"), (StateKinds.TraceDecision, "undecided", "agree"),
            (StateKinds.TraceDecision, "agree", "defer"), (StateKinds.ProofConfirmation, "unconfirmed", "confirmed"), (StateKinds.DraftPosted, "unposted", "posted") })
        {
            Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, kind, "X-1", from, to, StateActor.Code("t"), "r"));
            Assert.True(StateChanges.Move(temp.Root, kind, "X-1", from, to, StateActor.Person("Pat"), "r"));
        }
        // Code alone starts an item, and a person doesn't run a step.
        Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, StateKinds.Requirement, "R-1", null, "proposed", StateActor.Person("Pat"), "r"));
        Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, StateKinds.PageRun, "p", "write", "verify", StateActor.Person("Pat"), "r"));
        // A retired id never comes back.
        Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, StateKinds.Requirement, "R-1", "retired", "proposed", StateActor.Code("t"), "r"));
    }

    [Fact]
    public void A_first_state_and_a_stay_are_judged_too()
    {
        using var temp = new TempRepo();
        Assert.True(StateChanges.Move(temp.Root, StateKinds.Requirement, "R-1", null, "proposed", StateActor.Code("t"), "r"));
        Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, StateKinds.Requirement, "R-1", null, "approved", StateActor.Code("t"), "r"));
        // Staying put changes nothing and adds no line, unless the table lists it (a second confirmation names a new person).
        Assert.False(StateChanges.Move(temp.Root, StateKinds.Requirement, "R-1", "approved", "approved", StateActor.Code("t"), "r"));
        Assert.Throws<StangentException>(() => StateChanges.Move(temp.Root, StateKinds.Requirement, "R-1", "bad", "bad", StateActor.Code("t"), "r"));
        Assert.True(StateChanges.Move(temp.Root, StateKinds.ProofConfirmation, "K-1", "confirmed", "confirmed", StateActor.Person("Lee"), "r"));
        Assert.Single(StateChanges.History(temp.Root, StateKinds.Requirement, "R-1"));
    }

    [Fact]
    public void Apply_writes_the_field_and_a_null_state_clears_it()
    {
        using var temp = new TempRepo();
        var entry = new JsonObject { ["decision"] = "agree", ["decided_by"] = "pm-anna" };
        StateChanges.Apply(temp.Root, entry, StateKinds.TraceDecision, "L-1", "undecided", StateActor.Code("t"), "stale");
        Assert.Null(entry["decision"]);
        Assert.Null(entry["decided_by"]);
        Assert.True(entry.ContainsKey("decision"));
        Assert.Equal("agree", StateChanges.History(temp.Root, StateKinds.TraceDecision, "L-1")[0].From);

        // A new object replacing an old one moves from the old one's state.
        var item = new JsonObject();
        StateChanges.Apply(temp.Root, item, StateKinds.Requirement, "R-1", "proposed", StateActor.Code("t"), "changed", previous: new JsonObject { ["status"] = "approved" });
        Assert.Equal("proposed", (string)item["status"]!);
        Assert.Equal("approved", StateChanges.History(temp.Root, StateKinds.Requirement, "R-1")[0].From);

        // A lifecycle held elsewhere can't be written as a field.
        Assert.Equal("state_has_no_field", Assert.Throws<StangentException>(() =>
            StateChanges.Apply(temp.Root, item, StateKinds.DraftPosted, "d", "posted", StateActor.Person("Pat"), "r")).Code);
    }

    [Fact]
    public void An_unknown_kind_is_a_usage_error_and_a_damaged_line_stops_the_read()
    {
        using var temp = new TempRepo();
        var e = Assert.Throws<StangentException>(() => StateChanges.History(temp.Root, "nope", "x"));
        Assert.Equal(("unknown_state_kind", ExitCodes.Usage), (e.Code, e.ExitCode));
        Assert.Empty(StateChanges.History(temp.Root, StateKinds.Question, "Q-1"));

        var path = Path.Combine(temp.Root, StateKinds.Get(StateKinds.Question).HistoryFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json\n");
        Assert.Equal("bad_state_history", Assert.Throws<StangentException>(() => StateChanges.History(temp.Root, StateKinds.Question, "Q-1")).Code);
    }

    [Fact]
    public async Task Concurrent_appends_do_not_interleave()
    {
        using var temp = new TempRepo();
        var longReason = new string('x', 20_000);
        var writers = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 40; i++)
                StateChanges.Move(temp.Root, StateKinds.TraceDecision, $"L-{w}", "undecided", "agree", StateActor.Person("Pat"), longReason);
        }, TestContext.Current.CancellationToken));
        await Task.WhenAll(writers);

        var lines = File.ReadAllLines(Path.Combine(temp.Root, StateKinds.Get(StateKinds.TraceDecision).HistoryFile));
        Assert.Equal(320, lines.Length);
        Assert.All(lines, l => Assert.Equal(longReason, (string)JsonNode.Parse(l)!["reason"]!));
        foreach (var w in Enumerable.Range(0, 8))
            Assert.Equal(40, StateChanges.History(temp.Root, StateKinds.TraceDecision, $"L-{w}").Count);
    }

    /// <summary>A lifecycle in committed files needs committed history; one in run files must not reach a commit.</summary>
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void History_lives_where_its_state_lives(string name)
    {
        using var temp = new TempRepo();
        var git = Process.Start(new ProcessStartInfo("git", ["init", "-q"]) { WorkingDirectory = temp.Root })!;
        git.WaitForExit();
        var check = Process.Start(new ProcessStartInfo("git", ["check-ignore", "-q", StateKinds.Get(name).HistoryFile]) { WorkingDirectory = temp.Root })!;
        check.WaitForExit();
        var committed = name is StateKinds.Requirement or StateKinds.Question or StateKinds.TraceDecision;
        Assert.Equal(committed ? 1 : 0, check.ExitCode);   // check-ignore: 0 ignored, 1 not
    }
}
