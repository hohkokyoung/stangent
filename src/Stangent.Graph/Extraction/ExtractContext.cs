using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stangent.Graph.Extraction;

/// <summary>One parsed source file of the project being extracted.</summary>
public sealed record SourceFile(SyntaxTree Tree, SemanticModel Model, string RelativePath);

/// <summary>What the extractor and every detector see while working on one project.</summary>
public sealed class ExtractContext(
    string repoRoot,
    string projectId,
    string projectDir,
    Compilation compilation,
    IReadOnlyList<SourceFile> files,
    ProjectFragment fragment,
    IReadOnlyDictionary<string, List<string>> settings)
{
    public string RepoRoot { get; } = repoRoot;
    public string ProjectId { get; } = projectId;
    public string ProjectDir { get; } = projectDir;
    public Compilation Compilation { get; } = compilation;
    public string AssemblyName => Compilation.AssemblyName ?? "";
    public IReadOnlyList<SourceFile> Files { get; } = files;
    public ProjectFragment Fragment { get; } = fragment;
    /// <summary>This detector's settings from <c>stangent.yaml</c> (empty when none).</summary>
    public IReadOnlyDictionary<string, List<string>> Settings { get; set; } = settings;

    readonly HashSet<(string, string, string, string)> edgeKeys = [];

    /// <summary>Types whose JSON shape was already recorded for this project.</summary>
    public HashSet<string> ShapesSeen { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The graph id of a symbol declared in this repo: <c>&lt;assembly&gt;|&lt;documentation id&gt;</c>.
    /// Null for symbols from packages or the framework, which aren't on the map.
    /// </summary>
    public string? NodeId(ISymbol? symbol)
    {
        symbol = Normalize(symbol);
        if (symbol is null || !InRepo(symbol)) return null;
        // Top-level statements compile into a generated entry point with no documentation id;
        // give it the id it would have, so code in Program.cs is on the map.
        if (symbol is IMethodSymbol { Name: "<Main>$", ContainingType: { } program })
            return $"{symbol.ContainingAssembly?.Name}|M:{program.ToDisplayString().Replace("global::", "")}.<Main>$(System.String[])";
        var docId = symbol.GetDocumentationCommentId();
        return docId is null ? null : $"{symbol.ContainingAssembly?.Name}|{docId}";
    }

    /// <summary>Generic instances, reduced extensions and accessors map onto their declarations.</summary>
    public static ISymbol? Normalize(ISymbol? symbol) => symbol switch
    {
        null => null,
        IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet, AssociatedSymbol: { } prop } => prop.OriginalDefinition,
        IMethodSymbol { MethodKind: MethodKind.EventAdd or MethodKind.EventRemove, AssociatedSymbol: { } evt } => evt.OriginalDefinition,
        IMethodSymbol { ReducedFrom: { } reduced } => reduced.OriginalDefinition,
        IMethodSymbol m => m.OriginalDefinition,
        IPropertySymbol p => p.OriginalDefinition,
        IFieldSymbol f => f.OriginalDefinition,
        IEventSymbol e => e.OriginalDefinition,
        INamedTypeSymbol t => t.OriginalDefinition,
        _ => symbol,
    };

    public bool InRepo(ISymbol symbol) => symbol.Locations.Any(l =>
        l.IsInSource && l.SourceTree?.FilePath is { } path && path.StartsWith(RepoRoot, StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// The map point that "owns" code at this syntax node: the enclosing method, property, field or
    /// type. Lambdas and local functions belong to the member they're written in.
    /// </summary>
    public string? OwnerId(SyntaxNode node, SemanticModel model)
    {
        var symbol = model.GetEnclosingSymbol(node.SpanStart);
        while (symbol is not null)
        {
            switch (symbol)
            {
                case IMethodSymbol { MethodKind: MethodKind.LambdaMethod or MethodKind.LocalFunction or MethodKind.AnonymousFunction }:
                    symbol = symbol.ContainingSymbol;
                    continue;
                case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol or INamedTypeSymbol:
                    return NodeId(symbol);
                default:
                    symbol = symbol.ContainingSymbol;
                    continue;
            }
        }
        return null;
    }

    public int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    public string RelPath(SyntaxTree tree) => Path.GetRelativePath(RepoRoot, tree.FilePath).Replace('\\', '/');

    public void AddEdge(string? source, string? target, string kind, Label label, string detector = "compiler")
    {
        if (source is null || target is null || source == target) return;
        if (edgeKeys.Add((source, target, kind, detector)))
            Fragment.Edges.Add(new Edge(source, target, kind, label, detector));
    }

    public void AddBlindSpot(string? owner, SyntaxNode at, string reason, string detector = "compiler") =>
        Fragment.BlindSpots.Add(new BlindSpot(owner ?? ProjectId, RelPath(at.SyntaxTree), Line(at), reason, detector));

    public void AddFact(string detector, string kind, string owner, SyntaxNode? at, params (string Key, string Value)[] data) =>
        Fragment.Facts.Add(new Fact(detector, kind, owner, at is null ? null : RelPath(at.SyntaxTree), at is null ? 0 : Line(at),
            data.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal)));

    /// <summary>A string literal, a constant, or an interpolated string whose holes are constants or become <c>{*}</c>.</summary>
    public static string? StringValue(ExpressionSyntax? expr, SemanticModel model, int depth = 0)
    {
        if (depth > 4) return null;
        switch (expr)
        {
            case null:
                return null;
            case IdentifierNameSyntax or MemberAccessExpressionSyntax when model.GetConstantValue(expr) is { HasValue: false }:
                // A local or readonly field assigned once from a string expression: follow it.
                return model.GetSymbolInfo(expr).Symbol switch
                {
                    ILocalSymbol local => SingleInitializer(local.DeclaringSyntaxReferences, model, depth),
                    IFieldSymbol { IsReadOnly: true } field => SingleInitializer(field.DeclaringSyntaxReferences, model, depth),
                    _ => null,
                };
            case InterpolatedStringExpressionSyntax interpolated:
                var parts = new System.Text.StringBuilder();
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text) parts.Append(text.TextToken.ValueText.Replace("{{", "{").Replace("}}", "}"));
                    else if (content is InterpolationSyntax hole)
                        parts.Append(model.GetConstantValue(hole.Expression) is { HasValue: true, Value: string s } ? s
                            : SameTree(hole.Expression, model) && StringValue(hole.Expression, model, depth + 1) is { } followed && !followed.Contains("{*}") ? followed : "{*}");
                }
                return parts.ToString();
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                var left = StringValue(binary.Left, model, depth + 1);
                var right = StringValue(binary.Right, model, depth + 1);
                return left is null && right is null ? null : (left ?? "{*}") + (right ?? "{*}");
            default:
                return model.GetConstantValue(expr) is { HasValue: true, Value: string value } ? value : null;
        }
    }

    static string? SingleInitializer(IEnumerable<SyntaxReference> refs, SemanticModel model, int depth)
    {
        var declarators = refs.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>().ToList();
        if (declarators is not [{ Initializer.Value: { } init } declarator]) return null;
        // Only follow initializers in the same file: a semantic model answers for its own tree.
        if (declarator.SyntaxTree != model.SyntaxTree) return null;
        return StringValue(init, model, depth + 1);
    }

    static bool SameTree(SyntaxNode node, SemanticModel model) => node.SyntaxTree == model.SyntaxTree;

    /// <summary>The named method being invoked, e.g. "AddScoped" for <c>services.AddScoped&lt;A, B&gt;()</c>.</summary>
    public static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax i => i.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
        _ => null,
    };

    public static string FullName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.MetadataName}" : type.MetadataName;

    /// <summary>True when the type is, derives from or implements one named <paramref name="fullName"/> (metadata form, e.g. <c>Ns.IConsumer`1</c>).</summary>
    public static bool IsOrDerives(ITypeSymbol? type, string fullName)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (FullName(t.OriginalDefinition) == fullName) return true;
        return type?.AllInterfaces.Any(i => FullName(i.OriginalDefinition) == fullName) == true;
    }
}

/// <summary>A link detector (D17): finds one kind of link the compiler can't see.</summary>
public interface IDetector
{
    string Name { get; }

    /// <summary>Per project, with Roslyn: record edges inside the project and facts for linking.</summary>
    void Extract(ExtractContext ctx);

    /// <summary>Once per build, without Roslyn: turn facts from all projects into nodes and edges.</summary>
    void Link(LinkContext ctx);
}

/// <summary>What detectors see when linking across the whole graph.</summary>
public sealed class LinkContext(CodeGraph graph, IReadOnlyDictionary<string, List<string>> settings)
{
    public CodeGraph Graph { get; } = graph;
    public IReadOnlyDictionary<string, List<string>> Settings { get; set; } = settings;
    readonly HashSet<string> nodeIds = [.. graph.AllNodes.Select(n => n.Id)];
    readonly HashSet<(string, string, string, string)> edgeKeys = [];

    public IEnumerable<Fact> Facts(string detector) => Graph.AllFacts.Where(f => f.Detector == detector);

    public bool HasNode(string id) => nodeIds.Contains(id);

    public void AddNode(Node node)
    {
        if (nodeIds.Add(node.Id)) Graph.LinkedNodes.Add(node);
    }

    public void AddEdge(string source, string target, string kind, Label label, string detector)
    {
        if (source == target || !HasNode(source) || !HasNode(target)) return;
        if (edgeKeys.Add((source, target, kind, detector)))
            Graph.LinkedEdges.Add(new Edge(source, target, kind, label, detector));
    }

    public void AddBlindSpot(string owner, string? file, int line, string reason, string detector) =>
        Graph.LinkedBlindSpots.Add(new BlindSpot(owner, file ?? "", line, reason, detector));
}
