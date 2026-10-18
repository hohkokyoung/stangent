using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Database tables and columns. EF Core: <c>DbSet&lt;T&gt;</c> properties (✅), table names from
/// <c>ToTable("…")</c> or <c>[Table]</c>. Other ORMs: a configurable "entity base class → table
/// named after the class" convention (🟡), e.g. nopCommerce's <c>Nop.Core.BaseEntity</c>:
/// <c>detectorSettings: { entity-tables: { entityBaseTypes: [Nop.Core.BaseEntity] } }</c>.
/// A table depends on its entity type; a column on its property.
/// </summary>
public sealed class EntityTablesDetector : IDetector
{
    public string Name => "entity-tables";

    public void Extract(ExtractContext ctx)
    {
        var baseTypes = ctx.Settings.GetValueOrDefault("entityBaseTypes") ?? [];
        foreach (var file in ctx.Files)
        {
            var root = file.Tree.GetRoot();
            foreach (var decl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type) continue;
                if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() != decl) continue;

                if (ExtractContext.IsOrDerives(type, "Microsoft.EntityFrameworkCore.DbContext"))
                    foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
                        if (prop.Type is INamedTypeSymbol { TypeArguments: [INamedTypeSymbol entity] } set
                            && ExtractContext.FullName(set.OriginalDefinition) == "Microsoft.EntityFrameworkCore.DbSet`1")
                            Entity(ctx, entity, prop.Name, "dbset", decl);

                if (!type.IsAbstract && baseTypes.Any(b => ExtractContext.IsOrDerives(type.BaseType, b)))
                    Entity(ctx, type, type.Name, "convention", decl);

                if (Attr(type, "TableAttribute") is { } tableName && ctx.NodeId(type) is { } typeId)
                    ctx.AddFact(Name, "table-name", typeId, decl, ("entity", typeId), ("table", tableName));
            }

            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (ExtractContext.InvokedName(call) != "ToTable" || call.Expression is not MemberAccessExpressionSyntax m) continue;
                if (file.Model.GetTypeInfo(m.Expression).Type is not INamedTypeSymbol { TypeArguments: [INamedTypeSymbol entity] } builder
                    || !ExtractContext.FullName(builder.OriginalDefinition).EndsWith("EntityTypeBuilder`1", StringComparison.Ordinal)) continue;
                var args = call.ArgumentList.Arguments;
                var table = ExtractContext.StringValue(args.FirstOrDefault()?.Expression, file.Model);
                var schema = args.Count > 1 ? ExtractContext.StringValue(args[1].Expression, file.Model) : null;
                if (table is not null && ctx.NodeId(entity) is { } entityId)
                    ctx.AddFact(Name, "table-name", entityId, call, ("entity", entityId), ("table", schema is null ? table : $"{schema}.{table}"));
            }
        }
    }

    void Entity(ExtractContext ctx, INamedTypeSymbol entity, string defaultTable, string source, SyntaxNode at)
    {
        if (ctx.NodeId(entity) is not { } entityId) return;
        ctx.AddFact(Name, "entity", entityId, at, ("entity", entityId), ("table", defaultTable), ("source", source),
            ("assembly", ctx.AssemblyName), ("project", ctx.ProjectId));
        for (var t = entity; t is not null && ctx.InRepo(t); t = t.BaseType)
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
                if (!prop.IsStatic && IsScalar(prop.Type) && ctx.NodeId(prop) is { } propId
                    && !prop.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                    ctx.AddFact(Name, "column", propId, null, ("entity", entityId), ("column", Attr(prop, "ColumnAttribute") ?? prop.Name));
    }

    static bool IsScalar(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var inner] }) type = inner;
        return type.SpecialType is not SpecialType.None and not SpecialType.System_Object
            || type.TypeKind == TypeKind.Enum
            || type.Name is "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid" or "Decimal";
    }

    static string? Attr(ISymbol symbol, string name) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == name)?.ConstructorArguments.FirstOrDefault().Value as string;

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var names = facts.Where(f => f.Kind == "table-name").GroupBy(f => f["entity"])
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).First()["table"]);
        var columns = facts.Where(f => f.Kind == "column").GroupBy(f => f["entity"]).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var entity in facts.Where(f => f.Kind == "entity")
                     .OrderBy(f => f["source"] == "dbset" ? 0 : 1).ThenBy(f => f["entity"], StringComparer.Ordinal))
        {
            var entityId = entity["entity"];
            var table = names.GetValueOrDefault(entityId) ?? entity["table"];
            var tableId = $"table:{entity["assembly"]}|{table}";
            var label = entity["source"] == "dbset" ? Label.Certain : Label.Guessed;
            ctx.AddNode(new Node(tableId, NodeKind.Table, table, entity["project"], entity.File, entity.Line, entity.Line));
            ctx.AddEdge(tableId, entityId, EdgeKind.Maps, label, Name);
            foreach (var column in columns.GetValueOrDefault(entityId) ?? [])
            {
                var columnId = $"column:{entity["assembly"]}|{table}.{column["column"]}";
                ctx.AddNode(new Node(columnId, NodeKind.Column, $"{table}.{column["column"]}", entity["project"], entity.File, entity.Line, entity.Line));
                ctx.AddEdge(tableId, columnId, EdgeKind.Contains, label, Name);
                ctx.AddEdge(columnId, column.Owner, EdgeKind.Maps, label, Name);
            }
        }
    }
}
