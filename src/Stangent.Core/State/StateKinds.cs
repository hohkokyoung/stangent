using Stangent.Core.Evidence;

namespace Stangent.Core.State;

/// <summary>Who made a move. <c>code</c> and <c>person</c> may; an <c>agent</c> never decides state (D18, D60).</summary>
public sealed record StateActor(string Kind, string Name)
{
    public const string CodeKind = "code", PersonKind = "person", AgentKind = "agent";
    public static StateActor Code(string name) => new(CodeKind, name);
    public static StateActor Person(string name) => new(PersonKind, name);
    public static StateActor Agent(string name) => new(AgentKind, name);
}

/// <summary>One legal move and who may make it.</summary>
public sealed record StateRule(string From, string To, IReadOnlyList<string> Who);

/// <summary>
/// One lifecycle: its first states and its legal moves. <see cref="Field"/> is the JSON field that holds the state, when
/// the field holds the state's own name; <see cref="NullState"/> is the state a null or missing field means.
/// </summary>
public sealed record StateKind(string Name, string HistoryFile, IReadOnlyList<string> Initial, IReadOnlyList<StateRule> Moves,
    string? Field = null, string? NullState = null, IReadOnlyList<string>? ClearedOnNull = null)
{
    public IEnumerable<string> States => Initial.Concat(Moves.SelectMany(m => new[] { m.From, m.To })).Distinct();
}

/// <summary>
/// The tables (D114). Only real lifecycles are here: a stored thing moving over time, with code or a person deciding.
/// What one command returned isn't (<c>docs/foundation.md</c>). No table lets an agent move anything.
/// </summary>
public static class StateKinds
{
    public const string Requirement = "requirement", Question = "question", TraceDecision = "trace-decision", FindingSeverity = "finding-severity",
        Proof = "proof", ProofConfirmation = "proof-confirmation", DraftPosted = "draft-posted",
        PageRun = "page-run", LinkRun = "link-run", ReqRun = "req-run", TicketMatch = "ticket-match", TicketStep = "ticket-step",
        FsdCandidate = "fsd-candidate", FsdSectionMatch = "fsd-section-match", FindingVerdict = "finding-verdict", FindingReview = "finding-review", DisproveRun = "disprove-run";

    // Committed state gets committed history; run state in .stangent/ gets history beside it, ignored the same way.
    const string RequirementHistory = "specs/requirements/history.jsonl", TraceHistory = "specs/trace-history.jsonl",
        TicketHistory = ".stangent/tickets/state-history.jsonl", FsdHistory = "specs/fsd/history.jsonl";

    static readonly string[] Code = [StateActor.CodeKind], Person = [StateActor.PersonKind];
    static StateRule By(string[] who, string from, string to) => new(from, to, who);
    static StateRule[] Each(string[] who, string from, params string[] to) => [.. to.Select(t => By(who, from, t))];

    static readonly string[] Decisions = ["agree", "code-wrong", "fsd-wrong", "page-wrong", "defer"];
    public static readonly string[] Verdicts = ["stands", "refuted", "page-gap", "unreachable", "cant-tell"];
    static readonly string[] VerdictStates = ["unverified", .. Verdicts];
    static readonly string[] Outcomes = ["reproduced", "not-reproduced", "not-reproduced-flaky", "not-provable", "failed"];

    public static readonly IReadOnlyList<StateKind> All =
    [
        // Approval is a person's (D99); anything that changes an item drops it back (D11). A retired id never returns.
        new(Requirement, RequirementHistory, ["proposed"],
        [
            By(Person, "proposed", "approved"), By(Code, "approved", "proposed"),
            By(Code, "proposed", "retired"), By(Code, "approved", "retired"),
        ], Field: "status"),

        // A question that goes away is closed, never deleted; the same words on the same text open it again.
        new(Question, RequirementHistory, ["open"], [By(Code, "open", "dropped"), By(Code, "dropped", "open")], Field: "status"),

        // D58: a person decides and can change their mind. Code only ever drops a decision back. A person removing one
        // by hand in a PR is seen when the merge is attributed.
        new(TraceDecision, TraceHistory, ["undecided"],
        [
            .. Decisions.Select(d => By(Person, "undecided", d)),
            .. Decisions.SelectMany(from => Decisions.Where(to => to != from).Select(to => By(Person, from, to))),
            .. Decisions.Select(d => new StateRule(d, "undecided", [StateActor.CodeKind, StateActor.PersonKind])),
        ], Field: "decision", NullState: "undecided", ClearedOnNull: ["decided_by", "decided_on"]),

        // D118: code starts a finding ungraded and a person grades it, and can change their mind. An agent never does, and code can't judge the effect on users.
        new(FindingSeverity, TraceHistory, [Severity.Ungraded],
            [.. Severity.All.SelectMany(from => Severity.All.Where(to => to != from).Select(to => By(Person, from, to)))], Field: "severity"),

        // D119: an agent proposes a verdict, and only code writes it, once its quote passes. Code drops it back when what it was judged on changes.
        new(FindingVerdict, TraceHistory, ["unverified"],
            [.. VerdictStates.SelectMany(from => VerdictStates.Where(to => to != from).Select(to => By(Code, from, to)))],
            Field: "verdict", NullState: "unverified", ClearedOnNull: ["disproof", "disproof_hash"]),

        // D119 (fix round): a quote proves the text exists, not that it supports the verdict, so a person confirms a refuted or unreachable one, or sends it back to the BA.
        // Code sends one back when a person's decision on it drops, and lets go of a review when the verdict it was about moves. An agent never does.
        new(FindingReview, TraceHistory, ["waiting"],
        [
            .. Each(Person, "waiting", "confirmed"), By(Person, "sent-back", "confirmed"),
            new StateRule("waiting", "sent-back", [StateActor.CodeKind, StateActor.PersonKind]), new StateRule("confirmed", "sent-back", [StateActor.CodeKind, StateActor.PersonKind]),
            .. Each(Code, "confirmed", "waiting"), By(Code, "sent-back", "waiting"),
        ], Field: "review", NullState: "waiting", ClearedOnNull: ["review_note", "review_hash"]),

        // The latest attempt's outcome: any attempt can follow any other, so the table holds the values and who moves them.
        new(Proof, TicketHistory, Outcomes,
            [.. Outcomes.SelectMany(from => Outcomes.Where(to => to != from).Select(to => By(Code, from, to)))], Field: "outcome"),

        // D96: only a developer makes a reproduced failure a bug. A new attempt clears it. Confirming again names a new person.
        new(ProofConfirmation, TicketHistory, ["unconfirmed"],
        [
            By(Person, "unconfirmed", "confirmed"), By(Person, "confirmed", "confirmed"), By(Code, "confirmed", "unconfirmed"),
        ], NullState: "unconfirmed"),

        // D74: posted under a person's own login. --again posts twice. Making the draft again starts it over.
        new(DraftPosted, TicketHistory, ["unposted"],
        [
            By(Person, "unposted", "posted"), By(Person, "posted", "posted"), By(Code, "posted", "unposted"),
        ], NullState: "unposted"),

        // The AI steps' runs (D60). `prepare` starts a new run from nothing, so a restart is a first state, not a move.
        new(PageRun, ".stangent/spec/state-history.jsonl", ["write"],
        [
            .. Each(Code, "write", "verify", "failed"), .. Each(Code, "verify", "fix", "done", "failed"), .. Each(Code, "fix", "done", "failed"),
        ], Field: "stage"),

        new(LinkRun, ".stangent/fsd/state-history.jsonl", ["find", "judge"],
        [
            By(Code, "find", "failed"), .. Each(Code, "judge", "confirm", "done", "failed"), .. Each(Code, "confirm", "done", "failed"),
        ], Field: "stage"),

        // D119: one finding at a time, each from nothing.
        new(DisproveRun, ".stangent/fsd/state-history.jsonl", ["disprove"], [By(Code, "disprove", "done")], Field: "stage"),

        new(ReqRun, ".stangent/req/state-history.jsonl", ["write"],
        [
            .. Each(Code, "write", "confirm", "failed"), .. Each(Code, "confirm", "fix", "done", "failed"),
            .. Each(Code, "fix", "reconfirm", "done", "failed"), .. Each(Code, "reconfirm", "done", "failed"),
        ], Field: "stage"),

        new(TicketMatch, TicketHistory, ["pick"], [.. Each(Code, "pick", "confirm", "done"), By(Code, "confirm", "done")], Field: "stage"),

        // Points, prove and drafts: a run with no stage yet is running. `check` has no guard on these, so a second check
        // can move a finished run either way (listed as questionable in phases/0-foundation.md).
        new(TicketStep, TicketHistory, ["running"],
        [
            .. Each(Code, "running", "done", "failed"), By(Code, "failed", "done"), By(Code, "done", "failed"),
        ], Field: "stage", NullState: "running"),

        // D117: a new version of an imported document waits beside the current one. Only a person promotes or discards it; a newer file replaces it.
        new(FsdCandidate, FsdHistory, ["candidate"], [.. Each(Person, "candidate", "promoted", "discarded"), By(Code, "candidate", "replaced")], Field: "state"),

        // D117: a section code won't match on its own. A person settles it and can change their mind. Code only lets go of a
        // claim when the section that claimed this id is settled another way.
        new(FsdSectionMatch, FsdHistory, ["doubtful"],
        [
            .. Each(Person, "doubtful", "same", "new", "removed"), By(Person, "same", "same"), By(Person, "same", "new"), By(Person, "new", "same"),
            By(Person, "removed", "same"), By(Code, "same", "doubtful"),
        ], Field: "state"),
    ];

    public static StateKind Get(string name) => All.FirstOrDefault(k => k.Name == name)
        ?? throw new StangentException("unknown_state_kind", $"No lifecycle named '{name}'. There are: {string.Join(", ", All.Select(k => k.Name))}.", ExitCodes.Usage);
}
