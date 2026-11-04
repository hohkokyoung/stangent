using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Graph.Impact;

namespace Stangent.Graph;

/// <summary>
/// Where a point is declared, at the commit its graph was built from (1.1). Lines come only from what the extractors stored:
/// a missing line stays null, and lines that may not match the commit say so and can't be cited.
/// </summary>
public sealed record SymbolLines(
    string Id, string Kind, string Name, string? File, int? StartLine, int? EndLine, string? Commit,
    bool LinesMatchCommit, bool Generated, IReadOnlyList<string> AlsoIn, string? Note)
{
    /// <summary>
    /// Every point the symbol names: all overloads, and one per project for a file compiled into several. Empty when none does.
    /// Never picks one.
    /// </summary>
    public static IReadOnlyList<SymbolLines> Where(CodeGraph graph, string symbol)
    {
        var matches = new ImpactAnalyzer(graph).Resolve(symbol);
        if (matches.Count == 0) return [];
        var nodes = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var members = graph.AllEdges.Where(e => e.Kind == EdgeKind.Contains).ToLookup(e => e.Source, StringComparer.Ordinal);
        return [.. matches.Select(n => Of(graph, n, n.Kind == NodeKind.Type ? PartsElsewhere(n, members, nodes) : []))];
    }

    /// <summary>
    /// Many symbols, by exact graph id, for a caller citing a whole set: the same answer as <see cref="Where"/> without walking the
    /// graph for each. Null when the graph has no such point.
    /// </summary>
    public static Func<string, SymbolLines?> Lookup(CodeGraph graph)
    {
        var nodes = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var members = graph.AllEdges.Where(e => e.Kind == EdgeKind.Contains).ToLookup(e => e.Source, StringComparer.Ordinal);
        return id => nodes.GetValueOrDefault(id) is { } n ? Of(graph, n, n.Kind == NodeKind.Type ? PartsElsewhere(n, members, nodes) : []) : null;
    }

    /// <summary>The commit if it's a real one: a graph built outside git has none.</summary>
    public static string? CommitOf(CodeGraph graph) =>
        graph.Commit.Length == 40 && graph.Commit.All(Uri.IsHexDigit) ? graph.Commit.ToLowerInvariant() : null;

    /// <summary>Why a file's lines can't be said to match the graph's commit, or null when they can.</summary>
    public static string? NotAtCommit(CodeGraph graph, string? file)
    {
        if (CommitOf(graph) is null) return "the graph wasn't built at a git commit";
        var name = file?.Normalize();
        if (name is not null && graph.DirtyFiles?.Contains(name, StringComparer.Ordinal) == true)
            return "this file differed from the commit when the graph was built, so its lines don't match the commit";
        if (name is not null && graph.NotInCommit?.Contains(name, StringComparer.Ordinal) == true)
            return "the commit doesn't contain this file (git ignores it, or a build generated it), so its lines can't match the commit";
        return graph.DirtyFiles is null || graph.NotInCommit is null ? "the graph doesn't say whether its tree was clean; rebuild it" : null;
    }

    /// <summary>A repo's name for <c>repo/path</c>: its origin's last path part, else its folder.</summary>
    public static string RepoName(string root)
    {
        var origin = Git.Run(root, "remote get-url origin")?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(origin)) return Path.GetFileName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar));
        var name = origin[(origin.LastIndexOfAny(['/', ':', '\\']) + 1)..];
        return name.EndsWith(".git", StringComparison.Ordinal) ? name[..^4] : name;
    }

    /// <summary>The citation for an <c>evidence</c> record, or null when this can't honestly cite anything: no file, or a file that may not match the commit.</summary>
    public Citation? ToCitation(string repoName) => File is null || !LinesMatchCommit ? null : new Citation
    {
        Kind = CitationKind.Code, Ref = $"{repoName}/{File}", Symbol = Id,
        Lines = StartLine is { } start && EndLine is { } end ? new LineRange(start, end) : null,
    };

    public JsonObject ToJson(string repoName) => new()
    {
        ["id"] = Id, ["kind"] = Kind, ["name"] = Name, ["file"] = File, ["startLine"] = StartLine, ["endLine"] = EndLine, ["commit"] = Commit,
        ["linesMatchCommit"] = LinesMatchCommit, ["generated"] = Generated,
        ["alsoIn"] = new JsonArray([.. AlsoIn.Select(f => (JsonNode)f)]), ["note"] = Note,
        ["citation"] = ToCitation(repoName) is { } c ? CitationJson(c) : null,
    };

    /// <summary>The same shape <c>evidence</c> stores: enum names as the schema spells them, nulls left out.</summary>
    public static JsonObject CitationJson(Citation c)
    {
        var o = new JsonObject { ["kind"] = JsonNamingPolicy.CamelCase.ConvertName(c.Kind.ToString()), ["ref"] = c.Ref };
        if (c.Lines is { } lines) o["lines"] = new JsonObject { ["start"] = lines.Start, ["end"] = lines.End };
        if (c.Symbol is { } symbol) o["symbol"] = symbol;
        return o;
    }

    static SymbolLines Of(CodeGraph graph, Node n, IReadOnlyList<string> alsoIn)
    {
        var commit = CommitOf(graph);
        var start = n.File is not null && n.Line > 0 ? n.Line : (int?)null;
        var end = start is not null && n.EndLine >= n.Line ? n.EndLine : (int?)null;
        var notes = new List<string>();
        if (n.File is null) notes.Add("this point has no place in a file");
        else if (start is null) notes.Add("the graph holds this point's file, not its lines");
        else if (end is null) notes.Add("the graph holds only this point's first line, so no lines are cited");
        var notAtCommit = NotAtCommit(graph, n.File);
        if (notAtCommit is not null) notes.Add(notAtCommit);
        if (alsoIn.Count > 0) notes.Add("a partial type: these lines are its first part, and it has members outside them");
        return new SymbolLines(n.Id, n.Kind, n.Name, n.File, start, end, commit,
            notAtCommit is null, n.File is not null && LooksGenerated(n.File), alsoIn,
            notes.Count == 0 ? null : string.Join("; ", notes));
    }

    /// <summary>
    /// A partial type's node holds only its first part (the first file by name). Its members hold their own places, so the files
    /// of those outside that span show the other parts. A part with no members leaves no trace.
    /// </summary>
    static IReadOnlyList<string> PartsElsewhere(Node type, ILookup<string, Edge> members, Dictionary<string, Node> nodes) =>
        [.. members[type.Id].Select(e => nodes.GetValueOrDefault(e.Target)).OfType<Node>()
            .Where(m => m.File is not null && m.Line > 0 && (m.File != type.File || m.Line < type.Line || m.Line > type.EndLine))
            .Select(m => m.File!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>By name only: a checked-in generated file's lines are real at the commit, but they're not what a person wrote.</summary>
    static bool LooksGenerated(string file)
    {
        var name = Path.GetFileName(file);
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) || name.Contains(".generated.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".gen.", StringComparison.OrdinalIgnoreCase);
    }
}
