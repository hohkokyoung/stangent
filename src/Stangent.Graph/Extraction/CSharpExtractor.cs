using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stangent.Graph.Extraction;

/// <summary>
/// The compiler's part of the map (D26): points for declarations and ✅ lines for what the
/// compiler can prove. Detectors add the rest.
/// </summary>
public static class CSharpExtractor
{
    static readonly HashSet<string> TestAttributes = ["Fact", "Theory", "Test", "TestCase", "TestCaseSource", "TestMethod", "DataTestMethod"];

    // Calls that load or invoke code by name, so whatever they reach is invisible (❓).
    static readonly Dictionary<string, string> Reflection = new()
    {
        ["System.Activator.CreateInstance"] = "reflection: Activator.CreateInstance",
        ["System.Type.GetType"] = "reflection: Type.GetType",
        ["System.Type.GetMethod"] = "reflection: Type.GetMethod",
        ["System.Type.GetProperty"] = "reflection: Type.GetProperty",
        ["System.Type.InvokeMember"] = "reflection: Type.InvokeMember",
        ["System.Reflection.MethodBase.Invoke"] = "reflection: MethodBase.Invoke",
        ["System.Reflection.Assembly.GetTypes"] = "reflection: Assembly.GetTypes",
        ["System.Reflection.Assembly.GetExportedTypes"] = "reflection: Assembly.GetExportedTypes",
        ["System.Reflection.Assembly.Load"] = "reflection: Assembly.Load",
        ["System.Reflection.Assembly.LoadFrom"] = "reflection: Assembly.LoadFrom",
    };

    public static void Extract(ExtractContext ctx)
    {
        var fragment = ctx.Fragment;
        fragment.Nodes.Add(new Node(ctx.ProjectId, NodeKind.Project, ctx.AssemblyName, ctx.ProjectId));
        var declared = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var file in ctx.Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            var fileId = "file:" + file.RelativePath;
            fragment.Nodes.Add(new Node(fileId, NodeKind.File, file.RelativePath, ctx.ProjectId, file.RelativePath));
            ctx.AddEdge(ctx.ProjectId, fileId, EdgeKind.Contains, Label.Certain);
            var root = file.Tree.GetRoot();

            foreach (var decl in root.DescendantNodes().Where(IsDeclaration))
                Declare(ctx, file, decl, fileId, declared);

            foreach (var node in root.DescendantNodes())
                Reference(ctx, file, node);
        }

        foreach (var node in declared.Values.OrderBy(n => n.Id, StringComparer.Ordinal)) fragment.Nodes.Add(node);
    }

    static bool IsDeclaration(SyntaxNode n) => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
        or MethodDeclarationSyntax or ConstructorDeclarationSyntax or DestructorDeclarationSyntax or OperatorDeclarationSyntax
        or ConversionOperatorDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax
        or VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax or EventFieldDeclarationSyntax }
        or CompilationUnitSyntax or ParameterSyntax { Parent.Parent: TypeDeclarationSyntax };

    static void Declare(ExtractContext ctx, SourceFile file, SyntaxNode decl, string fileId, Dictionary<string, Node> declared)
    {
        ISymbol? symbol = decl switch
        {
            // Top-level statements: the compiler's generated entry point.
            CompilationUnitSyntax cu when cu.Members.OfType<GlobalStatementSyntax>().Any() => file.Model.GetDeclaredSymbol(cu),
            CompilationUnitSyntax => null,
            // Primary-constructor parameters of records become properties.
            ParameterSyntax p => file.Model.GetDeclaredSymbol(p) is IParameterSymbol ps
                ? ps.ContainingSymbol.ContainingType?.GetMembers(ps.Name).OfType<IPropertySymbol>().FirstOrDefault()
                : null,
            _ => file.Model.GetDeclaredSymbol(decl),
        };
        var id = ctx.NodeId(symbol);
        if (symbol is null || id is null || declared.ContainsKey(id)) return; // partial declarations: first file wins (sorted)

        var span = decl.GetLocation().GetLineSpan();
        var kind = symbol switch
        {
            INamedTypeSymbol => NodeKind.Type,
            IMethodSymbol => NodeKind.Method,
            IPropertySymbol or IEventSymbol => NodeKind.Property,
            _ => NodeKind.Field,
        };
        var isTest = symbol is IMethodSymbol method && method.GetAttributes().Any(a =>
            a.AttributeClass is { } c && (TestAttributes.Contains(c.Name) || TestAttributes.Contains(c.Name.Replace("Attribute", ""))));
        declared[id] = new Node(id, kind, DisplayName(symbol), ctx.ProjectId, file.RelativePath,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1, isTest);

        // Structure: file → type, type → member.
        if (symbol is INamedTypeSymbol type)
        {
            ctx.AddEdge(symbol.ContainingType is { } outer ? ctx.NodeId(outer) : fileId, id, EdgeKind.Contains, Label.Certain);
            Inheritance(ctx, type, id);
        }
        else
        {
            ctx.AddEdge(ctx.NodeId(symbol.ContainingType), id, EdgeKind.Contains, Label.Certain);
            if (symbol is IMethodSymbol { OverriddenMethod: { } baseMethod }) Override(ctx, id, baseMethod);
            if (symbol is IPropertySymbol { OverriddenProperty: { } baseProp }) Override(ctx, id, baseProp);
        }
    }

    static void Inheritance(ExtractContext ctx, INamedTypeSymbol type, string id)
    {
        if (type.BaseType is { } baseType) ctx.AddEdge(id, ctx.NodeId(baseType), EdgeKind.Inherits, Label.Certain);
        foreach (var iface in type.Interfaces) ctx.AddEdge(id, ctx.NodeId(iface), type.TypeKind == TypeKind.Interface ? EdgeKind.Inherits : EdgeKind.Implements, Label.Certain);
        if (type.TypeKind == TypeKind.Interface) return;

        // Members implementing interface members: calls through the interface reach the implementation.
        foreach (var iface in type.AllInterfaces)
        {
            if (ctx.NodeId(iface) is null) continue;
            foreach (var member in iface.GetMembers().Where(m => m is IMethodSymbol or IPropertySymbol or IEventSymbol))
            {
                if (type.FindImplementationForInterfaceMember(member) is not { } impl) continue;
                if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType?.OriginalDefinition, type.OriginalDefinition)) continue;
                var implId = ctx.NodeId(impl);
                var ifaceId = ctx.NodeId(member);
                ctx.AddEdge(implId, ifaceId, EdgeKind.Implements, Label.Certain);
                ctx.AddEdge(ifaceId, implId, EdgeKind.DispatchesTo, Label.Certain);
            }
        }
    }

    static void Override(ExtractContext ctx, string id, ISymbol baseMember)
    {
        var baseId = ctx.NodeId(baseMember);
        ctx.AddEdge(id, baseId, EdgeKind.Overrides, Label.Certain);
        ctx.AddEdge(baseId, id, EdgeKind.DispatchesTo, Label.Certain);
    }

    static void Reference(ExtractContext ctx, SourceFile file, SyntaxNode node)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                Invocation(ctx, file, invocation);
                break;
            case BaseObjectCreationExpressionSyntax creation:
                var created = file.Model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
                var owner = ctx.OwnerId(creation, file.Model);
                if (created is not null)
                {
                    ctx.AddEdge(owner, ctx.NodeId(created), EdgeKind.Calls, Label.Certain);
                    ctx.AddEdge(owner, ctx.NodeId(created.ContainingType), EdgeKind.Uses, Label.Certain);
                }
                else if (file.Model.GetTypeInfo(creation).Type is { TypeKind: not TypeKind.Error } t)
                {
                    ctx.AddEdge(owner, ctx.NodeId(t), EdgeKind.Uses, Label.Certain);
                }
                break;
            case IdentifierNameSyntax or GenericNameSyntax when node.Parent is not InvocationExpressionSyntax
                && !(node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node && ma.Parent is InvocationExpressionSyntax inv && inv.Expression == ma):
                var symbol = file.Model.GetSymbolInfo(node).Symbol;
                if (symbol is IPropertySymbol or IFieldSymbol or IEventSymbol or INamedTypeSymbol or IMethodSymbol)
                    ctx.AddEdge(ctx.OwnerId(node, file.Model), ctx.NodeId(symbol), EdgeKind.Uses, Label.Certain);
                break;
        }
    }

    static void Invocation(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax invocation)
    {
        var info = file.Model.GetSymbolInfo(invocation);
        var owner = ctx.OwnerId(invocation, file.Model);
        if (info.Symbol is IMethodSymbol method)
        {
            var target = ExtractContext.Normalize(method)!;
            ctx.AddEdge(owner, ctx.NodeId(target), EdgeKind.Calls, Label.Certain);
            var name = $"{ExtractContext.FullName(target.ContainingType)}.{target.Name}";
            if (Reflection.TryGetValue(name, out var reason)) ctx.AddBlindSpot(owner, invocation, reason);
            return;
        }
        if (file.Model.GetTypeInfo(invocation.Expression).Type is IDynamicTypeSymbol
            || (invocation.Expression is MemberAccessExpressionSyntax m && file.Model.GetTypeInfo(m.Expression).Type is IDynamicTypeSymbol))
        {
            ctx.AddBlindSpot(owner, invocation, "dynamic call");
            return;
        }
        if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }) return;
        // Overloads the compiler couldn't choose between (usually a missing package type): each
        // in-repo candidate becomes a guessed call; with none, it's a blind spot.
        var inRepo = info.CandidateSymbols.Select(ctx.NodeId).OfType<string>().ToList();
        foreach (var candidate in inRepo) ctx.AddEdge(owner, candidate, EdgeKind.Calls, Label.Guessed);
        // Candidates that are all framework or package methods lose nothing on the map; only a
        // call the compiler can't place at all is a blind spot.
        if (info.CandidateSymbols.Length == 0) ctx.AddBlindSpot(owner, invocation, $"unresolved call: {Short(invocation.Expression)}");
    }

    static string Short(SyntaxNode expr)
    {
        var text = expr is MemberAccessExpressionSyntax m ? m.Name.ToString() : expr.ToString();
        return text.Length > 60 ? text[..60] : text;
    }

    public static string DisplayName(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol t => t.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
        IMethodSymbol { MethodKind: MethodKind.Constructor } c => $"{c.ContainingType.Name}.ctor",
        _ when symbol.ContainingType is { } t => $"{t.Name}.{symbol.Name}",
        _ => symbol.Name,
    };
}
