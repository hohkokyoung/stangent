namespace Stangent.Graph;

/// <summary>How a line was found (D26). Ordered from strongest to weakest.</summary>
public enum Label
{
    /// <summary>✅ The compiler proved it.</summary>
    Certain = 0,
    /// <summary>🟡 A detector inferred it from a pattern.</summary>
    Guessed = 1,
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

    public IEnumerable<Node> AllNodes => Fragments.Values.SelectMany(f => f.Nodes).Concat(LinkedNodes);
    public IEnumerable<Edge> AllEdges => Fragments.Values.SelectMany(f => f.Edges).Concat(LinkedEdges);
    public IEnumerable<BlindSpot> AllBlindSpots => Fragments.Values.SelectMany(f => f.BlindSpots).Concat(LinkedBlindSpots);
    public IEnumerable<Fact> AllFacts => Fragments.Values.SelectMany(f => f.Facts);
}
