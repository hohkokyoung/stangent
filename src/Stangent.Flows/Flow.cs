namespace Stangent.Flows;

/// <summary>A place in the code a flow item rests on (D173's proof line): <c>path:line</c> and the code written there.</summary>
public sealed record At(string File, int Line, string Code)
{
    public override string ToString() => $"{File}:{Line}";
}

/// <summary>Where a branch stops (D172, D174): database, outside-call, message, screen, hub, unknown, depth.</summary>
public sealed record End(string Kind, string Detail);

/// <summary>One step of a flow: a real call, an HTTP hop to an endpoint's action, a save, a send (D172).</summary>
public sealed class Step
{
    public int N { get; set; }
    public int? From { get; init; }
    /// <summary>What happens, in a fixed sentence filled by code (D173).</summary>
    public required string What { get; init; }
    public required At At { get; init; }
    /// <summary>The point the step runs, when it walks into one.</summary>
    public string? Point { get; init; }
    public bool Guessed { get; init; }
    public string? GuessReason { get; init; }
    public List<Condition> Conditions { get; init; } = [];
    public End? End { get; set; }
    /// <summary>A point already on this branch: "goes back to step N" (D176).</summary>
    public int? BackTo { get; set; }
    /// <summary>A point already walked elsewhere in this flow: its steps are under step N.</summary>
    public int? SameAs { get; set; }
    /// <summary>What kind of step: call, http, renders, on-load, database, outside-call, message, queue.</summary>
    public required string Kind { get; init; }
    public string? Runs { get; init; }
    internal int Depth { get; init; }
    internal List<Step> Children { get; } = [];
    internal Step? Parent { get; init; }
}

/// <summary>An early exit (a stop). D300: <see cref="CaughtBy"/> is the catch that takes a thrown error, which makes it handled, not a
/// stop; <see cref="Unsure"/> says why it can't be told whether a catch on the way takes it (❓).</summary>
public sealed record Exit(string Kind, string What, At At, List<Condition> Conditions, int? Step, string Point)
{
    public At? CaughtBy { get; init; }
    public string? Unsure { get; init; }
    /// <summary>D304(7): what the catch returns (its own <c>return</c> or the one right after its <c>try</c>); null when not known.</summary>
    public string? CatchEnds { get; init; }
}

/// <summary>A place the flow leaves its bounds (D173): message, runs-later, screen, continues. A guessed link carries its own
/// reason (step 6, D237 item 2): a redirect to no known screen says so, not "a queued command matched to its job".</summary>
public sealed record Link(string Kind, string To, string? ToFlow, At At, string Sentence, int? Step, bool Guessed, string? Reason = null)
{
    /// <summary>D310: while building, a <c>continues</c> link made through a call on <c>this</c>: the class of the object the shared code
    /// runs on, so it is walked for that class. Not written.</summary>
    internal string? Receiver { get; init; }
    /// <summary>D316: while building, a <c>continues</c> link made where no mutation was started: the shared code is walked so, its
    /// mutation hooks' requests only declared. Not written.</summary>
    internal bool Declared { get; init; }
}

public sealed record Check(string What, string Rule, string Property, At At, string Message, bool OnlyWarns);

public sealed record Guess(int? Step, string What, string Reason);

public sealed record Unknown(int? Step, string Point, At At, string Why);

public sealed record Actor(string Who, string Why, At? At, bool Guessed);

public sealed record StartInfo(string What, At At, string Point, IReadOnlyList<string> Triggers);

public sealed record Scenario(string Name, IReadOnlyList<Condition> Conditions, int? Exit);

/// <summary>One flow (D179): everything shown, all by code.</summary>
public sealed class Flow
{
    public required string Id { get; set; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required StartInfo Start { get; init; }
    public required Actor Actor { get; set; }
    public List<string> Inputs { get; } = [];
    public List<Check> Checks { get; } = [];
    public List<Step> Steps { get; } = [];
    public List<Exit> Exits { get; } = [];
    /// <summary>D300: thrown errors a catch on the way takes; the flow carries on.</summary>
    public List<Exit> Handled { get; } = [];
    public List<Link> Links { get; } = [];
    public List<Guess> Guesses { get; } = [];
    public List<Unknown> Unknowns { get; } = [];
    public List<Scenario> Scenarios { get; } = [];
    public List<string> DisplayOnly { get; } = [];
    public string? Runs { get; init; }
    public string Home { get; init; } = "";
    /// <summary>D310: a shared flow walked for one receiving class: that class (not written; its id and name say it).</summary>
    internal string? Receiver { get; set; }
    /// <summary>D316: a shared flow walked for flows that enter it where no mutation was started (not written; its id and name say it).</summary>
    internal bool Declared { get; set; }
    /// <summary>Every point the walk went into, for <c>flows check</c>'s "no silent skips" (not written).</summary>
    internal HashSet<string> Reached { get; } = new(StringComparer.Ordinal);
}

/// <summary>A start code found that isn't a flow, and why (D177 item 5: no silent skips).</summary>
public sealed record Skipped(string Kind, string Point, At At, string Reason);
