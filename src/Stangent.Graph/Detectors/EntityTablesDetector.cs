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
        // F5: properties EF is told are columns or owned (Property(x => x.P), OwnsOne/OwnsMany, ComplexProperty) aren't navigations.
        var notNavigations = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        foreach (var file in ctx.Files)
            foreach (var call in file.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                if (ExtractContext.InvokedName(call) is "Property" or "OwnsOne" or "OwnsMany" or "ComplexProperty"
                    && call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LambdaExpressionSyntax { ExpressionBody: { } body }
                    && file.Model.GetSymbolInfo(body).Symbol is IPropertySymbol configured)
                    notNavigations.Add(configured.OriginalDefinition);
        foreach (var file in ctx.Files)
        {
            var root = file.Tree.GetRoot();
            foreach (var decl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type) continue;
                if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() != decl) continue;

                if (ExtractContext.IsOrDerives(type, "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot")) Snapshot(ctx, file, decl);

                if (ExtractContext.IsOrDerives(type, "Microsoft.EntityFrameworkCore.DbContext"))
                {
                    var sets = type.GetMembers().OfType<IPropertySymbol>()
                        .Where(p => DbSetEntity(p.Type) is not null).Select(p => (Prop: p, Entity: DbSetEntity(p.Type)!)).ToList();
                    var contextId = ctx.NodeId(type) ?? "";
                    foreach (var (prop, entity) in sets) Entity(ctx, entity, prop.Name, "dbset", decl, contextId);
                    // EF also maps every entity reached through a navigation from a mapped one, table named after the class (G3).
                    var seen = new HashSet<INamedTypeSymbol>(sets.Select(s => s.Entity), SymbolEqualityComparer.Default);
                    var queue = new Queue<INamedTypeSymbol>(seen);
                    while (queue.Count > 0)
                        foreach (var next in Navigations(ctx, queue.Dequeue(), notNavigations))
                            if (seen.Add(next))
                            {
                                Entity(ctx, next, next.Name, "navigation", decl, contextId);
                                queue.Enqueue(next);
                            }
                }

                if (!type.IsAbstract && baseTypes.Any(b => ExtractContext.IsOrDerives(type.BaseType, b)))
                    Entity(ctx, type, type.Name, "convention", decl);

                if (Attr(type, "TableAttribute") is { } tableName && ctx.NodeId(type) is { } typeId)
                    ctx.AddFact(Name, "table-name", typeId, decl, ("entity", typeId), ("table", tableName));
            }

            foreach (var set in root.DescendantNodes().OfType<AssignmentExpressionSyntax>()) EntryState(ctx, file, set);
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                Access(ctx, file, call);
                // An app's own table map: Sonarr's Mapper.Entity<Series>("Series") (G3).
                if (ExtractContext.InvokedName(call) == "Entity"
                    && file.Model.GetSymbolInfo(call).Symbol is IMethodSymbol { TypeArguments: [INamedTypeSymbol mapped] } mapper && ctx.InRepo(mapper)
                    && ExtractContext.StringValue(call.ArgumentList.Arguments.FirstOrDefault()?.Expression, file.Model) is { } mappedTable)
                    Entity(ctx, mapped, mappedTable, "mapping", call);
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

    // R4: an entity's columns once per project and build, however many sources name it. Keyed by the extraction itself:
    // this detector object is shared by every build in the process.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ExtractContext, HashSet<string>> columnsDone = new();

    void Entity(ExtractContext ctx, INamedTypeSymbol entity, string defaultTable, string source, SyntaxNode at, string context = "", string? assembly = null)
    {
        if (ctx.NodeId(entity) is not { } entityId) return;
        ctx.AddFact(Name, "entity", entityId, at, ("entity", entityId), ("table", defaultTable), ("source", source),
            ("assembly", assembly ?? ctx.AssemblyName), ("project", ctx.ProjectId), ("context", context));
        var done = columnsDone.GetOrCreateValue(ctx);
        lock (done) if (!done.Add(entityId)) return;
        for (var t = entity; t is not null && ctx.InRepo(t); t = t.BaseType)
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
                if (!prop.IsStatic && IsScalar(prop.Type) && ctx.NodeId(prop) is { } propId
                    && !prop.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                    ctx.AddFact(Name, "column", propId, null, ("entity", entityId), ("column", Attr(prop, "ColumnAttribute") ?? prop.Name));
    }

    static INamedTypeSymbol? DbSetEntity(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeArguments: [INamedTypeSymbol entity] } set
        && ExtractContext.FullName(set.OriginalDefinition) == "Microsoft.EntityFrameworkCore.DbSet`1" ? entity : null;

    /// <summary>In-repo classes an entity's properties point at, singly or as a collection, that have a key (EF's rule); not owned or configured as columns.</summary>
    IEnumerable<INamedTypeSymbol> Navigations(ExtractContext ctx, INamedTypeSymbol entity, HashSet<IPropertySymbol> notNavigations)
    {
        for (var t = entity; t is not null && ctx.InRepo(t); t = t.BaseType)
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
                if (!prop.IsStatic && !prop.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute") && !notNavigations.Contains(prop.OriginalDefinition)
                    && (CollectionElement(prop.Type) ?? prop.Type) is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } target
                    && ctx.InRepo(target) && HasKey(target) && !target.GetAttributes().Any(a => a.AttributeClass?.Name == "OwnedAttribute"))
                    yield return target;
    }

    /// <summary>
    /// D164: EF's own list of tables. Each top-level <c>modelBuilder.Entity("Ns.Type", b =&gt; { … b.ToTable("Name"); })</c> in a
    /// ModelSnapshot is a table (✅); owned types nested in it (<c>OwnsOne</c>, <c>ToJson</c>) aren't. A name that isn't a type
    /// (a many-to-many join table like <c>AppUserLibrary</c>) is a table with no entity.
    /// </summary>
    void Snapshot(ExtractContext ctx, SourceFile file, ClassDeclarationSyntax snapshot)
    {
        // R3: the snapshot belongs to the DbContext its [DbContext(typeof(X))] names, which may live in another project.
        var context = file.Model.GetDeclaredSymbol(snapshot)?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "DbContextAttribute")?.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
        var contextId = context is null ? "" : ctx.NodeId(context) ?? "";
        var assembly = context?.ContainingAssembly?.Name ?? ctx.AssemblyName;
        var blocks = snapshot.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(c => ExtractContext.InvokedName(c) == "Entity" && c.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "modelBuilder" } }
                && c.ArgumentList.Arguments.Count >= 2 && c.ArgumentList.Arguments[1].Expression is LambdaExpressionSyntax)
            .Select(c => (Call: c, Type: ExtractContext.StringValue(c.ArgumentList.Arguments[0].Expression, file.Model), Lambda: (LambdaExpressionSyntax)c.ArgumentList.Arguments[1].Expression))
            .Where(b => b.Type is not null).ToList();
        foreach (var (call, typeName, lambda) in blocks)
        {
            // ToTable said directly in this entity's block, not in a nested owned type's.
            var toTable = lambda.Body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(i => ExtractContext.InvokedName(i) == "ToTable"
                    && !i.Ancestors().TakeWhile(a => a != lambda).OfType<InvocationExpressionSyntax>().Any(a => ExtractContext.InvokedName(a) is "OwnsOne" or "OwnsMany"));
            if (toTable is null || ExtractContext.StringValue(toTable.ArgumentList.Arguments.FirstOrDefault()?.Expression, file.Model) is not { } table) continue;
            var type = file.Model.Compilation.GetTypeByMetadataName(typeName!);
            if (type is not null && ctx.NodeId(type) is not null) { Entity(ctx, type, table, "snapshot", call, contextId, assembly); continue; }
            // A join table: the types it links are the HasOne(…) of its blocks (EF writes relationships in a later block of the same name).
            var links = blocks.Where(b => b.Type == typeName).SelectMany(b => b.Lambda.Body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                .Where(i => ExtractContext.InvokedName(i) == "HasOne")
                .Select(i => ExtractContext.StringValue(i.ArgumentList.Arguments.FirstOrDefault()?.Expression, file.Model))
                .OfType<string>().Select(n => file.Model.Compilation.GetTypeByMetadataName(n) is { } t ? ctx.NodeId(t) : null).OfType<string>()
                .Distinct().Order(StringComparer.Ordinal).ToList();
            ctx.AddFact(Name, "snapshot-table", ctx.ProjectId, call, ("table", table), ("type", typeName!), ("assembly", assembly), ("project", ctx.ProjectId),
                ("context", contextId), ("links", string.Join(";", links))); // ids hold "|"
        }
    }

    static INamedTypeSymbol? CollectionElement(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeArguments: [INamedTypeSymbol element] } named && named.SpecialType != SpecialType.System_String
        && (named.Name is "ICollection" or "IList" or "List" or "HashSet" or "ISet" or "IEnumerable" or "IReadOnlyCollection" or "IReadOnlyList")
            ? element : null;

    static bool HasKey(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetMembers().OfType<IPropertySymbol>().Any(p => p.Name == "Id" || p.Name == type.Name + "Id"
                    || p.GetAttributes().Any(a => a.AttributeClass?.Name == "KeyAttribute")))
                return true;
        return false;
    }

    static readonly string[] WriteWords = ["Insert", "Add", "Update", "Upsert", "Delete", "Remove", "Save", "Purge", "Attach", "Clear", "Set"];
    static readonly string[] ReadWords = ["Get", "All", "Find", "Single", "First", "Query", "Count", "Any", "Has", "Exists", "Where", "Load", "Read", "Search", "Page"];

    /// <summary>Write or read, from a method name's first word; null when the name says neither.</summary>
    static string? Mode(string name) =>
        WriteWords.Any(w => StartsWord(name, w)) ? "write" : ReadWords.Any(w => StartsWord(name, w)) ? "read" : null;

    /// <summary>F10: <c>name</c> starts with the whole word: <c>All</c>, <c>AllAsync</c>, <c>AllSeries</c>, never <c>Allow</c>.</summary>
    static bool StartsWord(string name, string word) =>
        name.StartsWith(word, StringComparison.Ordinal) && (name.Length == word.Length || !char.IsLower(name[word.Length]));



    /// <summary>
    /// Which entity a call saves or reads (G3, G3b): DbSet&lt;T&gt; methods, Add/Remove on a navigation collection,
    /// and methods of a generic repository over T (IBasicRepository&lt;Series&gt;.Insert). Linking keeps those whose T has a table.
    /// </summary>
    void Access(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) return;
        INamedTypeSymbol? entity = null, owner = null;
        string? mode = null, via = null;
        var receiverExpr = (call.Expression as MemberAccessExpressionSyntax)?.Expression;
        var receiverType = receiverExpr is null ? null : file.Model.GetTypeInfo(receiverExpr).Type;
        if (method.Name.StartsWith("ExecuteDelete", StringComparison.Ordinal) || method.Name.StartsWith("ExecuteUpdate", StringComparison.Ordinal))
        {
            // F2: context.Set.Where(…).ExecuteDeleteAsync(): a write to the DbSet the chain starts from.
            for (var e = receiverExpr; e is not null; e = (e as InvocationExpressionSyntax)?.Expression is MemberAccessExpressionSyntax m ? m.Expression : null)
                if (DbSetEntity(file.Model.GetTypeInfo(e).Type) is { } bulk) { (entity, mode, via) = (bulk, "write", "dbset"); break; }
        }
        else if (DbSetEntity(receiverType) is { } set)
        {
            (entity, via) = (set, "dbset");
            // R1: anything on a DbSet that isn't a write reads it: LINQ, EF, and the app's own query extensions (Includes, ApplyStatsFilter).
            mode = Mode(method.Name) is "write" ? "write" : method.Name == "Entry" ? null : "read";
        }
        else if (ExtractContext.IsOrDerives(receiverType, "Microsoft.EntityFrameworkCore.DbContext")
                 && method.Name is "Add" or "AddAsync" or "AddRange" or "AddRangeAsync" or "Update" or "UpdateRange" or "Remove" or "RemoveRange" or "Attach" or "AttachRange"
                 && call.ArgumentList.Arguments.FirstOrDefault() is { } arg && file.Model.GetTypeInfo(arg.Expression).Type is INamedTypeSymbol argType)
            // F2: context.Update(series), context.Add(x).
            (entity, mode, via) = (CollectionElement(argType) ?? argType, "write", "context");
        else if (receiverExpr is not null && file.Model.GetSymbolInfo(receiverExpr).Symbol is IPropertySymbol { ContainingType: var holder } nav
                 && ctx.InRepo(holder) && CollectionElement(nav.Type) is { } element && ctx.InRepo(element)
                 && method.Name is "Add" or "AddRange" or "Remove" or "RemoveRange" or "RemoveAll" or "Clear" or "Insert")
            // Linking keeps it only when the holder is a mapped entity too (F3).
            (entity, owner, mode, via) = (element, holder, "write", Holds(element, holder) ? "join" : "navigation");
        else if (method.ContainingType is { IsGenericType: true } repo && repo.Name.Contains("Repository", StringComparison.Ordinal)
                 && repo.TypeArguments.OfType<INamedTypeSymbol>().FirstOrDefault(a => a.TypeKind == TypeKind.Class && ctx.InRepo(a)) is { } over
                 && Mode(method.Name) is { } m)
            (entity, mode, via) = (over, m, "repository");
        else if (receiverExpr is not null && IsQueryable(receiverType) && !IsQueryable(Unwrapped(method.ReturnType)) && QueryOrigin(receiverExpr, file.Model, 0) is { } read && ctx.InRepo(read))
            // D199: a call that runs a query (a queryable in, not one out: ToListAsync, FirstOrDefaultAsync, an app's
            // ToPagedListAsync) reads the table the query starts from: a repository's IQueryable<T> property or a DbSet<T>,
            // followed back through the query's local variable (`var query = _repository.Table; query = query.Where(…)`).
            (entity, mode, via) = (read, "read", "query");
        if (entity is null || mode is null || ctx.NodeId(entity) is not { } entityId || ctx.OwnerId(call, file.Model) is not { } ownerId) return;
        ctx.AddFact(Name, "access", ownerId, call, ("entity", entityId), ("mode", mode), ("via", via!), ("holder", owner is null ? "" : ctx.NodeId(owner) ?? ""));
    }

    static bool IsQueryable(ITypeSymbol? type) =>
        type is INamedTypeSymbol n && (n.OriginalDefinition.ToDisplayString() is "System.Linq.IQueryable<T>" or "System.Linq.IOrderedQueryable<T>"
            || n.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>"));

    /// <summary><c>Task&lt;T&gt;</c>, <c>ValueTask&lt;T&gt;</c> → T.</summary>
    static ITypeSymbol Unwrapped(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask", TypeArguments: [var inner] } ? inner : type;

    /// <summary>
    /// D199: the entity a query is over, where the code shows its start: back through the chain's own query calls
    /// (<c>.Where(…).OrderBy(…)</c>), through a local's initialiser, to a repository's <c>IQueryable&lt;T&gt;</c> property (its
    /// type's name holds "Repository") or a <c>DbSet&lt;T&gt;</c>.
    /// </summary>
    static INamedTypeSymbol? QueryOrigin(ExpressionSyntax expr, SemanticModel model, int depth)
    {
        if (depth > 6) return null;
        while (expr is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax inner } && IsQueryable(model.GetTypeInfo(expr).Type)) expr = inner.Expression;
        while (expr is ParenthesizedExpressionSyntax p) expr = p.Expression;
        // `from c in _customerRepository.Table … select c`: the query starts where its first `from` reads.
        if (expr is QueryExpressionSyntax q) return QueryOrigin(q.FromClause.Expression, model, depth + 1);
        var type = model.GetTypeInfo(expr).Type;
        if (DbSetEntity(type) is { } set) return set;
        switch (model.GetSymbolInfo(expr).Symbol)
        {
            case IPropertySymbol prop when IsQueryable(prop.Type) && (prop.ContainingType.Name.Contains("Repository", StringComparison.Ordinal)
                    || prop.ContainingType.AllInterfaces.Any(i => i.Name.Contains("Repository", StringComparison.Ordinal))):
                return (prop.Type as INamedTypeSymbol)?.TypeArguments.OfType<INamedTypeSymbol>().FirstOrDefault();
            case ILocalSymbol local:
                foreach (var r in local.DeclaringSyntaxReferences)
                    if (r.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: var init } && model.SyntaxTree == r.SyntaxTree)
                        return QueryOrigin(init, model, depth + 1);
                return null;
            default: return null;
        }
    }

    /// <summary>Many-to-many: the element also holds a collection of the holder, so the add writes the join table, not either side.</summary>
    static bool Holds(INamedTypeSymbol element, INamedTypeSymbol holder) =>
        element.GetMembers().OfType<IPropertySymbol>().Any(p => CollectionElement(p.Type) is { } back && SymbolEqualityComparer.Default.Equals(back, holder));

    /// <summary>F2: <c>context.Entry(series).State = EntityState.Modified</c> (or Added, Deleted) writes the entity's table.</summary>
    void EntryState(ExtractContext ctx, SourceFile file, AssignmentExpressionSyntax set)
    {
        if (set.Left is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "State", Expression: InvocationExpressionSyntax entry }
            || ExtractContext.InvokedName(entry) != "Entry" || entry.ArgumentList.Arguments.FirstOrDefault() is not { } arg
            || file.Model.GetTypeInfo(arg.Expression).Type is not INamedTypeSymbol type || ctx.NodeId(type) is not { } entityId
            || ctx.OwnerId(set, file.Model) is not { } ownerId) return;
        if (set.Right.ToString().EndsWith("Unchanged", StringComparison.Ordinal) || set.Right.ToString().EndsWith("Detached", StringComparison.Ordinal)) return;
        ctx.AddFact(Name, "access", ownerId, set, ("entity", entityId), ("mode", "write"), ("via", "entry-state"), ("holder", ""));
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

        var tables = new Dictionary<string, (string Id, Label Label)>(StringComparer.Ordinal);
        // D164: where EF's snapshot is there, it's the list; a navigation guess it doesn't hold isn't a table.
        // Per DbContext (R3): a navigation guess goes when its context's snapshot doesn't hold it. A snapshot with no
        // [DbContext] attribute speaks for the contexts in its own assembly.
        var snapshotFacts = facts.Where(f => (f.Kind == "entity" && f["source"] == "snapshot") || f.Kind == "snapshot-table").ToList();
        var held = snapshotFacts.Where(f => f.Kind == "entity").Select(f => (f["context"], f["entity"])).ToHashSet();
        var heldByAssembly = snapshotFacts.Where(f => f.Kind == "entity" && f["context"] == "").Select(f => (f["assembly"], f["entity"])).ToHashSet();
        var snapshotContexts = snapshotFacts.Select(f => f["context"]).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        var snapshotAssemblies = snapshotFacts.Where(f => f["context"] == "").Select(f => f["assembly"]).ToHashSet(StringComparer.Ordinal);
        bool Dropped(Fact f) => f["source"] == "navigation"
            && ((snapshotContexts.Contains(f["context"]) && !held.Contains((f["context"], f["entity"])))
                || (snapshotAssemblies.Contains(f["assembly"]) && !heldByAssembly.Contains((f["assembly"], f["entity"]))));
        int Rank(string source) => source switch { "snapshot" => 0, "dbset" => 1, "mapping" => 2, _ => 3 };
        foreach (var entity in facts.Where(f => f.Kind == "entity" && !Dropped(f))
                     .OrderBy(f => Rank(f["source"])).ThenBy(f => f["entity"], StringComparer.Ordinal)
                     .ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line))
        {
            var entityId = entity["entity"];
            if (tables.ContainsKey(entityId)) continue; // one table per entity: the snapshot, then a DbSet, then a map, then a navigation
            var table = entity["source"] == "snapshot" ? entity["table"] : names.GetValueOrDefault(entityId) ?? entity["table"];
            var tableId = $"table:{entity["assembly"]}|{table}";
            var label = entity["source"] is "dbset" or "snapshot" ? Label.Certain : Label.Guessed;
            tables[entityId] = (tableId, label);
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
    
        var joins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in facts.Where(f => f.Kind == "snapshot-table"))
        {
            var id = $"table:{t["assembly"]}|{t["table"]}";
            ctx.AddNode(new Node(id, NodeKind.Table, t["table"], t["project"], t.File, t.Line, t.Line));
            if (t["links"].Split(';') is [var a, var b]) joins.TryAdd($"{a};{b}", id);
        }
        foreach (var access in facts.Where(f => f.Kind == "access").OrderBy(f => f.Owner, StringComparer.Ordinal))
        {
            // F3: an Add on a list counts only when the list's holder is a mapped entity; a many-to-many add writes a join table we don't name.
            if (access["via"] == "join")
            {
                // R2: a many-to-many add writes the join table the snapshot says links the two types.
                var pair = string.Join(";", new[] { access["entity"], access["holder"] }.Order(StringComparer.Ordinal));
                if (joins.TryGetValue(pair, out var join)) ctx.AddEdge(access.Owner, join, EdgeKind.Writes, Label.Guessed, Name);
                continue;
            }
            if (access["via"] == "navigation" && !tables.ContainsKey(access["holder"])) continue;
            if (tables.TryGetValue(access["entity"], out var table))
                ctx.AddEdge(access.Owner, table.Id, access["mode"] == "write" ? EdgeKind.Writes : EdgeKind.Reads,
                    access["via"] is "dbset" or "query" ? table.Label : Label.Guessed, Name); // D199: a query over a DbSet<T> or a repository's IQueryable<T>: the types prove it
        }
    }
}
