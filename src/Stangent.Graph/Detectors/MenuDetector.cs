using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// D221: a menu built in C# as data, as <c>menu-item</c> facts (the TypeScript extractor reads frontends' menus, <see cref="RazorViewsDetector"/>
/// views' links). An item is an object initialiser with a name-like property (<c>Title</c>, <c>Name</c>…) and an address-like one (<c>Url</c>,
/// <c>Href</c>…), or a name and a list of items under it (a heading). The address is a constant URL, a call to ASP.NET's URL builders with
/// constant arguments (<c>Url.Action("List", "Product")</c>), or a call to the repo's own helper whose body returns such a call built from its
/// parameters, given constants (nopCommerce's <c>GetMenuItemUrl("Product", "List")</c> → <c>LinkGenerator.GetPathByAction(…, new { area = AreaNames.ADMIN })</c>).
/// A name is a constant, or the constant a call is given (<c>GetResourceAsync("Admin.Catalog")</c>), kept as a translation key. A list of three or
/// more items is a menu; the outermost such list is its top level.
/// </summary>
public sealed class MenuDetector : IDetector
{
    public string Name => "menu";

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
        {
            var seen = new HashSet<SyntaxNode>();
            foreach (var list in file.Tree.GetRoot().DescendantNodes().Where(IsList))
            {
                if (seen.Contains(list)) continue;
                var local = new HashSet<SyntaxNode>();
                var items = ListItems(ctx, file.Model, list, local);
                if (items.Count < 3) continue;
                seen.UnionWith(local);
                MenuTree.Emit(ctx.Fragment.Facts, Name, [new MenuTree.ListUnit(items)]);
            }
        }
    }

    public void Link(LinkContext ctx) { }

    static bool IsList(SyntaxNode n) =>
        n is InitializerExpressionSyntax { RawKind: (int)SyntaxKind.CollectionInitializerExpression or (int)SyntaxKind.ArrayInitializerExpression }
        || n is CollectionExpressionSyntax;

    static IEnumerable<ExpressionSyntax> Elements(SyntaxNode list) => list switch
    {
        InitializerExpressionSyntax i => i.Expressions,
        CollectionExpressionSyntax c => c.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression),
        _ => [],
    };

    /// <summary>The list an expression holds: <c>new List&lt;T&gt; { … }</c>, <c>new[] { … }</c>, <c>[ … ]</c>.</summary>
    static SyntaxNode? ListOf(ExpressionSyntax e) => e switch
    {
        BaseObjectCreationExpressionSyntax { Initializer: { } i } when IsList(i) => i,
        ArrayCreationExpressionSyntax { Initializer: { } i } => i,
        ImplicitArrayCreationExpressionSyntax { Initializer: var i } => i,
        CollectionExpressionSyntax c => c,
        _ => null,
    };

    static List<MenuTree.Item> ListItems(ExtractContext ctx, SemanticModel model, SyntaxNode list, HashSet<SyntaxNode> seen)
    {
        seen.Add(list);
        var items = new List<MenuTree.Item>();
        foreach (var e in Elements(list))
        {
            if (e is not BaseObjectCreationExpressionSyntax { Initializer: { RawKind: (int)SyntaxKind.ObjectInitializerExpression } init } obj) continue;
            var (item, inner) = ObjectItem(ctx, model, obj, init, seen);
            if (item is not null) items.Add(item);
            else items.AddRange(inner);
        }
        return items;
    }

    static (MenuTree.Item? Item, List<MenuTree.Item> Inner) ObjectItem(ExtractContext ctx, SemanticModel model, ExpressionSyntax obj, InitializerExpressionSyntax init, HashSet<SyntaxNode> seen)
    {
        var props = init.Expressions.OfType<AssignmentExpressionSyntax>()
            .Select(a => (Name: (a.Left as IdentifierNameSyntax)?.Identifier.ValueText, Value: a.Right)).Where(p => p.Name is not null).ToList();
        string name = "", key = "";
        foreach (var wanted in MenuTree.NameProps)
            foreach (var (n, v) in props)
            {
                if (!string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                var (pn, pk) = Label(model, v);
                if (key == "" && pk != "") key = pk;
                if (name == "" && pn != "") name = pn;
            }
        MenuTree.Address? address = null;
        var addressSeen = false;
        var inner = new List<MenuTree.Item>();
        foreach (var (n, v) in props)
        {
            if (MenuTree.IsAddress(n!))
            {
                addressSeen = true;
                address ??= AddressOf(model, v);
                continue;
            }
            if (MenuTree.IsName(n!)) continue;
            if (ListOf(v) is { } list && !seen.Contains(list)) inner.AddRange(ListItems(ctx, model, list, seen));
        }
        var named = name != "" || key != "";
        if (named && (address is not null || !addressSeen && inner.Count > 0))
            return (new MenuTree.Item(name, key, address ?? MenuTree.Address.None, "data", ctx.OwnerId(obj, model) ?? ctx.ProjectId, ctx.RelPath(obj.SyntaxTree), ctx.Line(obj)) { Children = inner }, []);
        return (null, inner);
    }

    static (string Name, string Key) Label(SemanticModel model, ExpressionSyntax e)
    {
        if (e is AwaitExpressionSyntax aw) e = aw.Expression;
        if (model.GetConstantValue(e) is { HasValue: true, Value: string s }) return (s, "");
        if (e is InvocationExpressionSyntax call && call.ArgumentList.Arguments.FirstOrDefault() is { } first
            && model.GetConstantValue(first.Expression) is { HasValue: true, Value: string k }) return ("", k);
        return ("", "");
    }

    static readonly HashSet<string> Builders = new(StringComparer.Ordinal) { "Action", "RouteUrl", "GetPathByAction", "GetUriByAction", "GetPathByRouteValues", "GetUriByRouteValues" };

    /// <summary>Where an address-like value leads, when the code fixes it; null otherwise.</summary>
    static MenuTree.Address? AddressOf(SemanticModel model, ExpressionSyntax e)
    {
        if (e is AwaitExpressionSyntax aw) e = aw.Expression;
        if (model.GetConstantValue(e) is { HasValue: true, Value: string url }) return MenuTree.Address.Url(url);
        if (e is not InvocationExpressionSyntax call || model.GetSymbolInfo(call).Symbol is not IMethodSymbol m) return null;
        if (IsBuilder(m)) return Built(model, call, m, _ => null);
        // The repo's own helper: its body returns a builder's call, made from its parameters; the caller gives them.
        var decl = m.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        if (decl is null) return null;
        var returned = decl.ExpressionBody?.Expression
            ?? decl.Body?.Statements.OfType<ReturnStatementSyntax>().Select(r => r.Expression).LastOrDefault(x => x is InvocationExpressionSyntax);
        if (returned is not InvocationExpressionSyntax inner) return null;
        var innerModel = model.Compilation.GetSemanticModel(inner.SyntaxTree);
        if (innerModel.GetSymbolInfo(inner).Symbol is not IMethodSymbol b || !IsBuilder(b)) return null;
        var args = call.ArgumentList.Arguments;
        string? Given(string parameter)
        {
            var index = m.Parameters.ToList().FindIndex(p => p.Name == parameter);
            var arg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == parameter) ?? (index >= 0 && index < args.Count && args[index].NameColon is null ? args[index] : null);
            return arg is not null && model.GetConstantValue(arg.Expression) is { HasValue: true, Value: string v } ? v : null;
        }
        return Built(innerModel, inner, b, x => innerModel.GetSymbolInfo(x).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p.ContainingSymbol, m.OriginalDefinition) ? Given(p.Name) : null);
    }

    static bool IsBuilder(IMethodSymbol m) =>
        Builders.Contains(m.Name) && (m.ContainingType.Name.Contains("UrlHelper", StringComparison.Ordinal) || m.ContainingType.Name.Contains("LinkGenerator", StringComparison.Ordinal)
            || (m.ReducedFrom ?? m).ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true);

    /// <summary>A builder call's action, controller, area and route name, each a constant or a value the helper's caller gives; null when any is made at runtime.</summary>
    static MenuTree.Address? Built(SemanticModel model, InvocationExpressionSyntax call, IMethodSymbol m, Func<ExpressionSyntax, string?> given)
    {
        var parameters = m.Parameters.ToList();   // a reduced extension's parameters already leave out its receiver
        string? action = null, controller = null, area = null, route = null;
        string? Value(ExpressionSyntax x) => model.GetConstantValue(x) is { HasValue: true, Value: string v } ? v : given(x);
        var args = call.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var p = args[i].NameColon is { } nc ? parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.ValueText) : i < parameters.Count ? parameters[i] : null;
            switch (p?.Name)
            {
                case "action" or "actionName": action = Value(args[i].Expression); if (action is null) return null; break;
                case "controller" or "controllerName": controller = Value(args[i].Expression); if (controller is null) return null; break;
                case "routeName": route = Value(args[i].Expression); if (route is null) return null; break;
                case "values" when args[i].Expression is AnonymousObjectCreationExpressionSyntax anon:
                    if (anon.Initializers.FirstOrDefault(x => (x.NameEquals?.Name.Identifier.ValueText ?? (x.Expression as IdentifierNameSyntax)?.Identifier.ValueText) == "area") is { } a)
                    {
                        area = Value(a.Expression);
                        if (area is null) return null;
                    }
                    break;
            }
        }
        if (action is null && route is null) return null;
        return route is not null && action is null ? MenuTree.Address.Route(route) : MenuTree.Address.Mvc(area ?? "", controller ?? "", action!);
    }
}

/// <summary>The shared shape of D221's menus: items, lists, and how markup's nesting folds into a tree.</summary>
public static class MenuTree
{
    /// <summary>Property names that name an item, in the order they're preferred (as the TypeScript extractor's).</summary>
    public static readonly string[] NameProps = ["title", "label", "text", "name", "caption", "displayName", "header", "heading", "systemName"];
    static readonly string[] AddressProps = ["to", "href", "link", "url", "path", "route", "routerLink"];
    public static bool IsName(string n) => NameProps.Any(p => string.Equals(p, n, StringComparison.OrdinalIgnoreCase));
    public static bool IsAddress(string n) => AddressProps.Any(p => string.Equals(p, n, StringComparison.OrdinalIgnoreCase));

    public sealed record Address(string Text, string Controller = "", string Action = "", string Area = "", string RouteName = "")
    {
        public static readonly Address None = new("");
        /// <summary>A constant URL; a page anchor or script (<c>#</c>, <c>javascript:</c>) leads nowhere, so it's none.</summary>
        public static Address? Url(string url) => url.Trim().Length == 0 || url.TrimStart().StartsWith('#') || url.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ? null : new(url);
        public static Address Mvc(string area, string controller, string action) => new($"mvc:{area}/{controller}/{action}", controller, action, area);
        public static Address Route(string name) => new($"route:{name}", RouteName: name);
    }

    public sealed record Item(string Name, string Key, Address Address, string Via, string Owner, string File, int Line)
    {
        public List<Item> Children { get; set; } = [];
    }

    public abstract record Unit;
    public sealed record ItemUnit(Item Item) : Unit;
    public sealed record ListUnit(List<Item> Items) : Unit;

    /// <summary>
    /// Markup's units under one element: an item takes the list right after it as its children (<c>&lt;li&gt;&lt;a&gt;…&lt;/a&gt;&lt;ul&gt;…&lt;/ul&gt;&lt;/li&gt;</c>);
    /// two or more items side by side are a list, sealed; one passes up unchanged, so a wrapper holding one item doesn't count.
    /// </summary>
    public static List<Unit> Fold(List<Unit> units)
    {
        var o = new List<Unit>();
        foreach (var u in units)
        {
            if (u is ListUnit l && o.Count > 0 && o[^1] is ItemUnit { Item.Children.Count: 0 } prev) { prev.Item.Children = l.Items; continue; }
            o.Add(u);
        }
        var items = o.OfType<ItemUnit>().Select(i => i.Item).ToList();
        if (items.Count < 2) return o;
        return [new ListUnit(items), .. o.OfType<ListUnit>()];
    }

    /// <summary>Each list of three or more items is a menu (<c>menu</c> = its first item's file and line); every item in it, in order, is a fact.</summary>
    public static void Emit(List<Fact> facts, string detector, IEnumerable<Unit> units)
    {
        foreach (var u in units)
        {
            if (u is not ListUnit { Items.Count: >= 3 } list) continue;
            var menu = $"{list.Items[0].File}:{list.Items[0].Line}";
            void Write(List<Item> items, string prefix)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var path = prefix == "" ? $"{i + 1}" : $"{prefix}.{i + 1}";
                    facts.Add(new Fact(detector, "menu-item", it.Owner, it.File, it.Line, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["menu"] = menu, ["path"] = path, ["name"] = it.Name, ["key"] = it.Key, ["address"] = it.Address.Text, ["via"] = it.Via,
                        ["controller"] = it.Address.Controller, ["action"] = it.Address.Action, ["area"] = it.Address.Area, ["route"] = it.Address.RouteName,
                    }));
                    Write(it.Children, path);
                }
            }
            Write(list.Items, "");
        }
    }
}
