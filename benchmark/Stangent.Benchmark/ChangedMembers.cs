using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph;

/// <summary>
/// Finds which methods an old commit changed, by name, so they can be looked up in today's graph
/// even after the code has moved (line-level scoring). Needs only a syntax parse of the old file:
/// milliseconds, no project load.
/// </summary>
static class ChangedMembers
{
    /// <summary>
    /// A member as the graph names it: <c>Type.Member</c> (constructors <c>Type.ctor</c>), with a
    /// parameter count for methods; or a type's full name when the line is outside any member.
    /// </summary>
    public sealed record Key(string Name, int? Parameters, bool IsType);

    public static HashSet<Key> At(string source, IEnumerable<(int From, int To)> ranges)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var text = tree.GetText();
        var root = tree.GetRoot();
        var spans = root.DescendantNodes()
            .Where(n => n is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or BaseFieldDeclarationSyntax or BaseTypeDeclarationSyntax)
            .Select(n => (Node: n, Span: text.Lines.GetLinePositionSpan(n.Span)))
            .ToList();
        var keys = new HashSet<Key>();
        foreach (var (from, to) in ranges)
            for (var line = from; line <= to; line++)
            {
                var zero = line - 1;
                var covering = spans.Where(s => s.Span.Start.Line <= zero && zero <= s.Span.End.Line).ToList();
                if (covering.Count == 0)
                {
                    // Top-level statements belong to the compiler's generated entry point.
                    if (root is CompilationUnitSyntax cu && cu.Members.OfType<GlobalStatementSyntax>().Any(g =>
                            text.Lines.GetLinePositionSpan(g.Span) is var gs && gs.Start.Line <= zero && zero <= gs.End.Line))
                        keys.Add(new Key("Program.<Main>$", 1, false));
                    continue;
                }
                var innermost = covering.MinBy(s => s.Span.End.Line - s.Span.Start.Line).Node;
                foreach (var key in KeysOf(innermost)) keys.Add(key);
            }
        return keys;
    }

    static IEnumerable<Key> KeysOf(SyntaxNode node)
    {
        var type = node as BaseTypeDeclarationSyntax ?? node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        if (type is null) yield break;
        switch (node)
        {
            case BaseTypeDeclarationSyntax t:
                yield return new Key(TypeFullName(t), null, true);
                break;
            case ConstructorDeclarationSyntax c:
                yield return new Key($"{type.Identifier.ValueText}.ctor", c.ParameterList.Parameters.Count, false);
                break;
            case MethodDeclarationSyntax m:
                yield return new Key($"{type.Identifier.ValueText}.{m.Identifier.ValueText}", m.ParameterList.Parameters.Count, false);
                break;
            case PropertyDeclarationSyntax p:
                yield return new Key($"{type.Identifier.ValueText}.{p.Identifier.ValueText}", null, false);
                break;
            case IndexerDeclarationSyntax:
                yield return new Key($"{type.Identifier.ValueText}.this[]", null, false);
                break;
            case EventDeclarationSyntax e:
                yield return new Key($"{type.Identifier.ValueText}.{e.Identifier.ValueText}", null, false);
                break;
            case BaseFieldDeclarationSyntax f:
                foreach (var v in f.Declaration.Variables) yield return new Key($"{type.Identifier.ValueText}.{v.Identifier.ValueText}", null, false);
                break;
        }
    }

    /// <summary>Same form as the graph's type names: <c>Ns.Outer.Inner</c>, with type parameters as <c>&lt;T&gt;</c>.</summary>
    static string TypeFullName(BaseTypeDeclarationSyntax t)
    {
        var parts = new List<string>();
        foreach (var node in t.AncestorsAndSelf())
        {
            switch (node)
            {
                case TypeDeclarationSyntax td:
                    parts.Add(td.Identifier.ValueText + (td.TypeParameterList is { } tp ? "<" + string.Join(", ", tp.Parameters.Select(p => p.Identifier.ValueText)) + ">" : ""));
                    break;
                case BaseTypeDeclarationSyntax bt:
                    parts.Add(bt.Identifier.ValueText);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    parts.Add(ns.Name.ToString());
                    break;
            }
        }
        parts.Reverse();
        return string.Join(".", parts);
    }

    /// <summary>Number of parameters in a graph method id, e.g. <c>…M:Ns.T.Run(System.Int32,System.String)</c> → 2.</summary>
    public static int ParameterCount(string id)
    {
        var open = id.IndexOf('(');
        if (open < 0) return 0;
        var inner = id[(open + 1)..id.LastIndexOf(')')];
        if (inner.Length == 0) return 0;
        int depth = 0, count = 1;
        foreach (var c in inner)
        {
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        return count;
    }
}

/// <summary>Looks changed members up in today's graph: same file first, then a unique match elsewhere (the code moved).</summary>
sealed class MemberIndex(CodeGraph graph)
{
    readonly ILookup<(string File, string Name), Node> byFileAndName = graph.AllNodes
        .Where(n => n.File is not null && n.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Field or NodeKind.Type)
        .ToLookup(n => (n.File!, n.Name));
    readonly ILookup<string, Node> byName = graph.AllNodes
        .Where(n => n.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Field or NodeKind.Type)
        .ToLookup(n => n.Name, StringComparer.Ordinal);

    public enum Match { SameFile, Moved, None }

    public (List<Node> Nodes, Match How) Find(string file, ChangedMembers.Key key)
    {
        var here = Filter(byFileAndName[(file, key.Name)], key);
        if (here.Count > 0) return (here, Match.SameFile);
        var elsewhere = Filter(byName[key.Name], key);
        // Moved code: accept only an unambiguous match (one declaration, possibly in several projects).
        return elsewhere.Select(n => n.File).Distinct().Count() == 1 ? (elsewhere, Match.Moved) : ([], Match.None);
    }

    static List<Node> Filter(IEnumerable<Node> nodes, ChangedMembers.Key key)
    {
        var list = nodes.ToList();
        if (key.Parameters is not { } count) return list;
        var exact = list.Where(n => ChangedMembers.ParameterCount(n.Id) == count).ToList();
        return exact.Count > 0 ? exact : list; // a changed signature still names the same method
    }
}
