using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core.State;

namespace Stangent.Core.Tests;

/// <summary>The state module (D114): every table, the history, and the scan that keeps writes on the module.</summary>
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

    // ---- Writes stay on the module ----

    [GeneratedRegex("""\["(status|decision|decided_by|confirmedBy|stage|posted|outcome)"\]\s*=(?!=)""")]
    private static partial Regex LifecycleWrite();

    /// <summary>Every write of these field names outside the module, per file and field. A new one fails the test until it's
    /// moved onto the module or sorted here with its reason, and a count that's gone stale fails too.</summary>
    static readonly Dictionary<(string File, string Field), (int Count, string Why)> Exceptions = new()
    {
        // Result fields: what one command returned, or a copy of someone else's value. Not lifecycles.
        [("src/Stangent.Core/Phases/PhaseCheck.cs", "status")] = (1, "the report copies what roadmap.md and a phase file say; people edit those"),
        [("src/Stangent.Core/Telemetry/TelemetryWriter.cs", "status")] = (1, "the event's result"),
        [("src/Stangent.Graph/GraphStore.cs", "status")] = (1, "how much git history the build could read, set once per build"),
        [("src/Stangent.Graph/Impact/ImpactAnalyzer.cs", "status")] = (2, "a report's field"),
        [("src/Stangent.Spec/Trace/FsdImpact.cs", "status")] = (1, "a report's field"),
        [("src/Stangent.Spec/Fsd/FsdImport.cs", "status")] = (2, "what this import did: imported, replaced, unchanged"),
        [("src/Stangent.Spec/Pages/PageRun.cs", "status")] = (1, "RunStep's JSON: what this command returned"),
        [("src/Stangent.Spec/Pages/PageRun.cs", "outcome")] = (1, "a copy into a page's record of what the step returned"),
        [("src/Stangent.Spec/Requirements/RequirementFile.cs", "status")] = (3, "views: copies of the feature file's status"),
        [("src/Stangent.Tickets/Ticket.cs", "status")] = (2, "Jira's status, copied at pull; Jira owns it"),
        [("src/Stangent.Tickets/Bundle.cs", "status")] = (1, "a copy of Jira's status"),
        [("src/Stangent.Tickets/Bundle.cs", "decision")] = (1, "a copy of the trace decision into a bundle"),
        [("src/Stangent.Tickets/Points.cs", "status")] = (5, "the file finder's last answer, worked out again from its inputs on every pull; its run's stage is the lifecycle"),
        [("src/Stangent.Tickets/Match.cs", "status")] = (2, "copies of the finder's answer and a requirement's status"),
        [("src/Stangent.Tickets/Match.cs", "outcome")] = (1, "what this match found; a new match writes a new record"),
        [("src/Stangent.Tickets/Proof/BugProof.cs", "outcome")] = (3, "one attempt's result, appended and never changed; the proof's own outcome is on the module"),

        // Lifecycle fields written beside the module, because the value isn't the state or it isn't a move.
        [("src/Stangent.Spec/Fsd/LinkRun.cs", "decision")] = (2, "a new entry starts undecided, and a rebuilt entry carries its decision: neither is a move"),
        [("src/Stangent.Spec/Fsd/LinkRun.cs", "decided_by")] = (2, "the same two, for who decided"),
        [("src/Stangent.Spec/Requirements/RequirementRun.cs", "decided_by")] = (1, "a rebuilt item that kept its approval keeps who gave it: not a move"),
        [("src/Stangent.Spec/Trace/TraceCheck.cs", "decided_by")] = (1, "holds a name from git, not a state (D58); Attribute moves the decision on the module first"),
        [("src/Stangent.Tickets/Drafts.cs", "posted")] = (2, "null in a new draft; then who, when and the comment, right after the move on the module"),
        [("src/Stangent.Tickets/Proof/BugProof.cs", "confirmedBy")] = (2, "holds who confirmed, not a state; both follow a move on the module"),
    };

    [Fact]
    public void Lifecycle_fields_are_written_only_through_the_module()
    {
        var root = RepoRoot.Find();
        var found = new Dictionary<(string, string), int>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal) || rel.StartsWith("src/Stangent.Core/State/", StringComparison.Ordinal)) continue;
            foreach (var line in File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
                foreach (Match m in LifecycleWrite().Matches(line))
                    found[(rel, m.Groups[1].Value)] = found.GetValueOrDefault((rel, m.Groups[1].Value)) + 1;
        }
        var wrong = found.Where(f => !Exceptions.TryGetValue(f.Key, out var e) || e.Count != f.Value).Select(f => $"{f.Key}: {f.Value} write(s)")
            .Concat(Exceptions.Where(e => !found.ContainsKey(e.Key)).Select(e => $"{e.Key}: listed, no write left")).ToList();
        Assert.True(wrong.Count == 0, "Move these onto StateChanges, or sort them in Exceptions with a reason:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void The_scan_sees_a_write_and_not_a_comparison()
    {
        Assert.Matches(LifecycleWrite(), """e["decision"] = "agree";""");
        Assert.Matches(LifecycleWrite(), """new JsonObject { ["stage"]="done" }""");
        Assert.DoesNotMatch(LifecycleWrite(), """if ((string)e["stage"] == "done")""");
    }
}
