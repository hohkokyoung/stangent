namespace Stangent.Graph;

/// <summary>How a line was found (D26). Ordered from strongest to weakest.</summary>
public enum Label
{
    /// <summary>✅ The compiler proved it.</summary>
    Certain = 0,
    /// <summary>🟡 A detector inferred it from a pattern.</summary>
    Guessed = 1,
    /// <summary>📜 Usually changes together, from git history (code-graph v3). A hint only, never walked.</summary>
    History = 2,
}

/// <summary>Kinds of points. Strings are stored, so renaming a member here is a contract change.</summary>
public static class NodeKind
{
    public const string Project = "project";
    public const string File = "file";
    public const string Type = "type";
    public const string Method = "method";
    public const string Property = "property";
    public const string Field = "field";
    public const string Endpoint = "endpoint";
    public const string Table = "table";
    public const string Column = "column";
    public const string Config = "config";
    /// <summary>An Angular or React component (code-graph v2).</summary>
    public const string Component = "component";
    /// <summary>An Angular component's template (code-graph v2).</summary>
    public const string Template = "template";
    /// <summary>A frontend route: the frontend's entry point (code-graph v2).</summary>
    public const string Page = "page";
}

/// <summary>
/// Kinds of lines. Every line points from the dependent to what it depends on, so "what does a
/// change to X affect" is everything that can reach X by following lines backwards.
/// </summary>
public static class EdgeKind
{
    public const string Contains = "contains";
    public const string Calls = "calls";
    public const string Uses = "uses";
    public const string Inherits = "inherits";
    public const string Implements = "implements";
    public const string Overrides = "overrides";
    /// <summary>An interface or virtual member depends on each implementation behind it.</summary>
    public const string DispatchesTo = "dispatches-to";
    public const string WiredTo = "wired-to";
    public const string RoutesTo = "routes-to";
    /// <summary>A handler depends on every method that publishes the event it handles.</summary>
    public const string TriggeredBy = "triggered-by";
    public const string Maps = "maps";
    public const string ReadsConfig = "reads-config";
    public const string Binds = "binds";
    public const string CallsHttp = "calls-http";
    public const string Tests = "tests";
    /// <summary>A component (or template) renders a child component or its template (code-graph v2).</summary>
    public const string Renders = "renders";
    /// <summary>A frontend field depends on the backend property it's read from (code-graph v2).</summary>
    public const string MapsField = "maps-field";
    /// <summary>A Python file imports a file or a symbol (D127). Structure, like <c>contains</c>: what changes behaviour are its calls and uses, so it's never walked.</summary>
    public const string Imports = "imports";
    /// <summary>
    /// 📜 A file usually changes when another does (code-graph v3). Stored apart from the other lines,
    /// as <see cref="HistoryLine"/>s, so the impact walk never follows them (D40).
    /// </summary>
    public const string ChangesWith = "changes-with";
    /// <summary>
    /// 🟡 A Playwright spec's <c>goto</c> opens a screen (code-graph v4). Stored apart from the other lines, as
    /// <see cref="ScreenTest"/>s, so nothing that reads the graph's lines ever follows them (D115).
    /// </summary>
    public const string OpensScreen = "opens-screen";

    /// <summary>
    /// 🟡/✅ A method saves to a table (code-graph v5, G3): <c>DbSet&lt;T&gt;.Add/Update/Remove</c>, <c>Add/Remove</c> on an
    /// entity's navigation collection, or a write-named method of a generic repository over <c>T</c>. Not walked by impact.
    /// </summary>
    public const string Writes = "writes";
    /// <summary>A method reads a table (code-graph v5): a <c>DbSet&lt;T&gt;</c> used for anything but a write, or a read-named repository method. Not walked.</summary>
    public const string Reads = "reads";

    /// <summary>Lines a change travels back along: behaviour flows through these.</summary>
    public static readonly HashSet<string> Propagating =
        [Calls, Uses, DispatchesTo, RoutesTo, TriggeredBy, Maps, ReadsConfig, Binds, CallsHttp, Tests, WiredTo, Renders, MapsField];

    /// <summary>Lines followed only from the changed points themselves (a base change affects what derives from it).</summary>
    public static readonly HashSet<string> FirstStepOnly = [Inherits, Implements, Overrides];
}

public sealed record Node(
    string Id,
    string Kind,
    string Name,
    string? Project = null,
    string? File = null,
    int Line = 0,
    int EndLine = 0,
    bool IsTest = false);

public sealed record Edge(string Source, string Target, string Kind, Label Label, string Detector = "compiler");

/// <summary>
/// 📜 <see cref="Source"/> changed in <see cref="Shared"/> of the last <see cref="Of"/> commits that touched
/// <see cref="Target"/> (D35). Both are file paths on the map. Like every line, it points from the dependent
/// (the file that usually has to change too) to what it depends on (the file that changed).
/// </summary>
public sealed record HistoryLine(string Source, string Target, int Shared, int Of);

/// <summary>
/// What 📜 was computed from (<c>meta.history</c>, D42). <see cref="Status"/>: <c>full</c>, <c>shallow</c>
/// (a shallow clone's partial history), <c>none</c> (no history) or <c>off</c> (<c>history.enabled: false</c>).
/// <see cref="Commit"/> is the commit the lines were computed at; it's older than the graph's own commit
/// when a shallow clone kept the lines it downloaded.
/// </summary>
public sealed record HistoryInfo(
    string Status, string Commit, string Oldest, int CommitsRead, int CommitsUsed,
    IReadOnlyDictionary<string, int> Dropped, bool RenamesFollowed, string Fingerprint, string? Note = null);

/// <summary>
/// 🟡 The <c>goto</c> at <see cref="Spec"/>:<see cref="Line"/> opens the screen <see cref="Page"/> (a <c>page</c> point). Like every
/// line, it points from the dependent (the test) to what it depends on (the screen). It says a test exists, not that it ran.
/// </summary>
public sealed record ScreenTest(string Spec, int Line, string Goto, string Page, Label Label = Label.Guessed);

/// <summary>❓ A place where a line couldn't be resolved (D17).</summary>
public sealed record BlindSpot(string Owner, string File, int Line, string Reason, string Detector = "compiler");

/// <summary>
/// Something a detector noticed inside one project, kept so links between projects can be made
/// later without Roslyn. Values are plain strings so facts can be stored and compared.
/// </summary>
public sealed record Fact(string Detector, string Kind, string Owner, string? File, int Line, IReadOnlyDictionary<string, string> Data)
{
    public string this[string key] => Data.TryGetValue(key, out var v) ? v : "";
}

/// <summary>Everything extracted from one project. Incremental builds replace whole fragments.</summary>
public sealed class ProjectFragment
{
    public required string ProjectId { get; init; }
    public required string Fingerprint { get; init; }
    public List<Node> Nodes { get; } = [];
    public List<Edge> Edges { get; } = [];
    public List<BlindSpot> BlindSpots { get; } = [];
    public List<Fact> Facts { get; } = [];
    /// <summary>Enums this build read (C.2). Never stored with the fragment, so the walk and the graph's bytes can't see them: <see cref="CodeEnums"/> holds them.</summary>
    public List<CodeEnum> Enums { get; } = [];
}

/// <summary>The whole graph after linking: fragments plus what detectors linked across them.</summary>
public sealed class CodeGraph
{
    public string Commit { get; set; } = "";
    public SortedDictionary<string, ProjectFragment> Fragments { get; } = new(StringComparer.Ordinal);
    public List<Node> LinkedNodes { get; } = [];
    public List<Edge> LinkedEdges { get; } = [];
    public List<BlindSpot> LinkedBlindSpots { get; } = [];
    public List<string> LoadErrors { get; } = [];
    /// <summary>📜 lines (code-graph v3): kept apart from <see cref="AllEdges"/>, so the walk never sees them.</summary>
    public List<HistoryLine> HistoryLines { get; } = [];
    public HistoryInfo? History { get; set; }
    /// <summary>
    /// Files that differed from <see cref="Commit"/> when the graph was built, so their lines don't match it (1.1). Empty: a
    /// clean tree. Null: not known (no git, or a graph from before this was recorded).
    /// </summary>
    public IReadOnlyList<string>? DirtyFiles { get; set; }
    /// <summary>
    /// Files on the map that <see cref="Commit"/> doesn't contain, such as a gitignored generated file, so their lines can't match it.
    /// Empty: none. Null: not known.
    /// </summary>
    public IReadOnlyList<string>? NotInCommit { get; set; }
    /// <summary>🟡 <c>opens-screen</c> lines (code-graph v4): kept apart from <see cref="AllEdges"/>, as 📜 lines are (D115).</summary>
    public List<ScreenTest> ScreenTests { get; } = [];

    public IEnumerable<Node> AllNodes => Fragments.Values.SelectMany(f => f.Nodes).Concat(LinkedNodes);
    public IEnumerable<Edge> AllEdges => Fragments.Values.SelectMany(f => f.Edges).Concat(LinkedEdges);
    public IEnumerable<BlindSpot> AllBlindSpots => Fragments.Values.SelectMany(f => f.BlindSpots).Concat(LinkedBlindSpots);
    public IEnumerable<Fact> AllFacts => Fragments.Values.SelectMany(f => f.Facts);
}
