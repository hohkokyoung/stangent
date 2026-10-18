using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// ASP.NET Core endpoints: attribute routes and minimal APIs (✅), conventional routes given as
/// strings (🟡). An endpoint depends on the action or handler it routes to.
/// </summary>
public sealed class RoutesDetector : IDetector
{
    public string Name => "aspnetcore-routes";

    static readonly Dictionary<string, string> HttpAttributes = new()
    {
        ["HttpGetAttribute"] = "GET", ["HttpPostAttribute"] = "POST", ["HttpPutAttribute"] = "PUT",
        ["HttpDeleteAttribute"] = "DELETE", ["HttpPatchAttribute"] = "PATCH", ["HttpHeadAttribute"] = "HEAD",
    };

    static readonly Dictionary<string, string> MapMethods = new()
    {
        ["MapGet"] = "GET", ["MapPost"] = "POST", ["MapPut"] = "PUT", ["MapDelete"] = "DELETE", ["MapPatch"] = "PATCH", ["Map"] = "ANY",
    };

    public static string EndpointId(string assembly, string method, string template) => $"endpoint:{assembly}|{method} {template}";

    public static string Normalize(string template)
    {
        var t = template.Trim().TrimStart('~');
        t = "/" + t.Trim('/');
        while (t.Contains("//")) t = t.Replace("//", "/");
        return t;
    }

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
        {
            var root = file.Tree.GetRoot();
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                if (file.Model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) Controller(ctx, type, cls);
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = ExtractContext.InvokedName(call);
                if (name is null) continue;
                if (MapMethods.ContainsKey(name) || name == "MapMethods") MinimalApi(ctx, file, call, name);
                else if (name is "MapControllerRoute" or "MapAreaControllerRoute" or "MapDefaultControllerRoute" or "MapRoute") Conventional(ctx, file, call, name);
                else if (name.StartsWith("MapDynamicControllerRoute", StringComparison.Ordinal) || name == "MapFallbackToController")
                    ctx.AddBlindSpot(ctx.OwnerId(call, file.Model), call, $"route chosen at runtime: {name}", Name);
            }
        }
    }

    void Controller(ExtractContext ctx, INamedTypeSymbol type, ClassDeclarationSyntax syntax)
    {
        if (type.IsAbstract || type.DeclaredAccessibility != Accessibility.Public) return;
        var isController = ExtractContext.IsOrDerives(type, "Microsoft.AspNetCore.Mvc.ControllerBase")
            || type.GetAttributes().Any(a => a.AttributeClass?.Name == "ApiControllerAttribute");
        if (!isController) return;
        if (type.GetAttributes().Any(a => a.AttributeClass?.Name == "NonControllerAttribute")) return;
        // Partial controllers: only the first declaration speaks for the class.
        if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() != syntax) return;

        var controllerName = type.Name.EndsWith("Controller", StringComparison.Ordinal) ? type.Name[..^"Controller".Length] : type.Name;
        var area = Attr(type, "AreaAttribute");
        var classRoutes = RouteTemplates(type);

        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            if (!ctx.InRepo(t) && !SymbolEqualityComparer.Default.Equals(t, type)) break;
            foreach (var method in t.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public || method.IsStatic) continue;
                if (method.GetAttributes().Any(a => a.AttributeClass?.Name == "NonActionAttribute")) continue;
                var methodId = ctx.NodeId(method);
                if (methodId is null) continue;
                var actionName = Attr(method, "ActionNameAttribute") ?? (method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^5] : method.Name);
                var verbs = method.GetAttributes()
                    .Select(a => a.AttributeClass?.Name is { } n && HttpAttributes.TryGetValue(n, out var v) ? (v, Arg(a)) : (null, null))
                    .Where(v => v.Item1 is not null).ToList();
                var methodRoutes = RouteTemplates(method);
                var location = method.Locations.FirstOrDefault(l => l.IsInSource);
                var line = location is null ? 0 : location.GetLineSpan().StartLinePosition.Line + 1;
                var file = location?.SourceTree is { } tree ? ctx.RelPath(tree) : null;

                var templates = verbs.Where(v => v.Item2 is not null).Select(v => (v.Item1!, v.Item2!))
                    .Concat(methodRoutes.SelectMany(r => verbs.Count > 0 ? verbs.Select(v => (v.Item1!, r)) : [("ANY", r)]))
                    .ToList();
                if (templates.Count == 0 && classRoutes.Count > 0)
                    templates = verbs.Count > 0 ? [.. verbs.Select(v => (v.Item1!, ""))] : [("ANY", "")];

                if (templates.Count > 0)
                {
                    foreach (var (verb, template) in templates)
                        foreach (var full in Combine(classRoutes, template))
                        {
                            var path = Normalize(full.Replace("[controller]", controllerName).Replace("[action]", actionName).Replace("[area]", area ?? ""));
                            AddEndpoint(ctx, verb, path, methodId, file, line, Label.Certain);
                        }
                }
                else
                {
                    // Not attribute-routed: reachable only through conventional routes, linked later.
                    ctx.AddFact(Name, "action", methodId, null,
                        ("controller", controllerName), ("action", actionName), ("area", area ?? ""),
                        ("verbs", string.Join(",", verbs.Select(v => v.Item1).Distinct().Order())),
                        ("assembly", ctx.AssemblyName), ("file", file ?? ""), ("line", line.ToString()));
                }
            }
        }
    }

    static IEnumerable<string> Combine(List<string> classRoutes, string template)
    {
        if (template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal) || classRoutes.Count == 0)
            return [template];
        return classRoutes.Select(c => template.Length == 0 ? c : $"{c}/{template}");
    }

    static List<string> RouteTemplates(ISymbol symbol) =>
        [.. symbol.GetAttributes().Where(a => a.AttributeClass?.Name == "RouteAttribute").Select(Arg).OfType<string>()];

    static string? Attr(ISymbol symbol, string attribute) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == attribute) is { } a ? Arg(a) : null;

    static string? Arg(AttributeData a) =>
        a.ConstructorArguments.FirstOrDefault().Value as string
        ?? a.NamedArguments.FirstOrDefault(n => n.Key is "Template" or "Name").Value.Value as string;

    void AddEndpoint(ExtractContext ctx, string verb, string path, string target, string? file, int line, Label label)
    {
        var id = EndpointId(ctx.AssemblyName, verb, path);
        if (!ctx.Fragment.Nodes.Any(n => n.Id == id))
            ctx.Fragment.Nodes.Add(new Node(id, NodeKind.Endpoint, $"{verb} {path}", ctx.ProjectId, file, line, line));
        ctx.AddEdge(id, target, EdgeKind.RoutesTo, label, Name);
    }

    void MinimalApi(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, string name)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) return;
        var receiverType = method.ReducedFrom is not null ? method.ReceiverType : method.Parameters.FirstOrDefault()?.Type;
        if (!ExtractContext.IsOrDerives(receiverType, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder")) return;

        var args = call.ArgumentList.Arguments;
        var templateIndex = 0;
        var handlerIndex = name == "MapMethods" ? 2 : 1;
        if (args.Count <= handlerIndex) return;
        var template = ExtractContext.StringValue(args[templateIndex].Expression, file.Model);
        var owner = ctx.OwnerId(call, file.Model);
        if (template is null)
        {
            ctx.AddBlindSpot(owner, call, $"{name} with a route built at runtime", Name);
            return;
        }

        var prefix = call.Expression is MemberAccessExpressionSyntax ma ? GroupPrefix(ma.Expression, file.Model, 0) : "";
        if (prefix is null)
        {
            ctx.AddBlindSpot(owner, call, "route group prefix not known here (passed in as a parameter)", Name);
            prefix = "{*}";
        }
        var verbs = name == "MapMethods"
            ? (args[1].Expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Select(l => l.Token.ValueText.ToUpperInvariant()).ToList() is { Count: > 0 } v ? v : ["ANY"])
            : [MapMethods[name]];

        var handler = args[handlerIndex].Expression;
        var target = handler is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax
            ? owner
            : ctx.NodeId(file.Model.GetSymbolInfo(handler).Symbol ?? file.Model.GetSymbolInfo(handler).CandidateSymbols.FirstOrDefault());
        if (target is null) return;
        foreach (var verb in verbs)
            AddEndpoint(ctx, verb, Normalize($"{prefix}/{template}"), target, file.RelativePath, ctx.Line(call), Label.Certain);
    }

    /// <summary>
    /// The path a route group adds, following <c>MapGroup("…")</c> calls and local variables
    /// within the method. Null when the group comes from a parameter.
    /// </summary>
    static string? GroupPrefix(ExpressionSyntax expr, SemanticModel model, int depth)
    {
        if (depth > 8) return null;
        switch (expr)
        {
            case InvocationExpressionSyntax inv when ExtractContext.InvokedName(inv) == "MapGroup" && inv.Expression is MemberAccessExpressionSyntax m:
                var outer = GroupPrefix(m.Expression, model, depth + 1);
                var own = ExtractContext.StringValue(inv.ArgumentList.Arguments.FirstOrDefault()?.Expression, model) ?? "{*}";
                return outer is null ? null : $"{outer}/{own}";
            case InvocationExpressionSyntax inv when inv.Expression is MemberAccessExpressionSyntax m:
                return GroupPrefix(m.Expression, model, depth + 1);
            case IdentifierNameSyntax id:
                var symbol = model.GetSymbolInfo(id).Symbol;
                if (symbol is ILocalSymbol local
                    && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } init })
                    return GroupPrefix(init, model, depth + 1);
                if (symbol is IParameterSymbol p && !ExtractContext.IsOrDerives(p.Type, "Microsoft.AspNetCore.Routing.RouteGroupBuilder")
                    && p.Type.Name != "RouteGroupBuilder")
                    return ""; // the app or endpoint builder itself: no prefix
                return symbol is IParameterSymbol ? null : "";
            default:
                return "";
        }
    }

    void Conventional(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, string name)
    {
        var owner = ctx.OwnerId(call, file.Model);
        if (owner is null) return;
        string? pattern, area = null;
        AnonymousObjectCreationExpressionSyntax? defaults = null;
        if (name == "MapDefaultControllerRoute")
            pattern = "{controller=Home}/{action=Index}/{id?}";
        else
        {
            var args = call.ArgumentList.Arguments;
            ExpressionSyntax? Named(string argName, int position) =>
                args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == argName)?.Expression
                ?? (args.Count > position && args[position].NameColon is null ? args[position].Expression : null);
            var offset = name == "MapAreaControllerRoute" ? 1 : 0;
            if (name == "MapAreaControllerRoute") area = ExtractContext.StringValue(Named("areaName", 1), file.Model);
            pattern = ExtractContext.StringValue(Named("pattern", 1 + offset) ?? Named("template", 1 + offset), file.Model);
            defaults = Named("defaults", 2 + offset) as AnonymousObjectCreationExpressionSyntax;
        }
        if (pattern is null)
        {
            ctx.AddBlindSpot(owner, call, $"{name} with a pattern built at runtime", Name);
            return;
        }

        string? Default(string key)
        {
            var member = defaults?.Initializers.FirstOrDefault(i => i.NameEquals?.Name.Identifier.ValueText == key);
            if (member is not null) return ExtractContext.StringValue(member.Expression, file.Model);
            var inline = System.Text.RegularExpressions.Regex.Match(pattern, "\\{" + key + "=([^}?]+)\\??\\}");
            return inline.Success ? inline.Groups[1].Value : null;
        }

        var controller = Default("controller");
        var action = Default("action");
        var generic = pattern.Contains("{controller", StringComparison.Ordinal) || pattern.Contains("{action", StringComparison.Ordinal);
        ctx.AddFact(Name, "conventional", owner, call,
            ("pattern", pattern), ("controller", generic ? "" : controller ?? ""), ("action", generic ? "" : action ?? ""),
            ("area", area ?? Default("area") ?? ""), ("assembly", ctx.AssemblyName), ("project", ctx.ProjectId));
    }

    public void Link(LinkContext ctx)
    {
        var actions = ctx.Facts(Name).Where(f => f.Kind == "action").ToList();
        var explicitlyRouted = new HashSet<string>(StringComparer.Ordinal);
        var conventional = ctx.Facts(Name).Where(f => f.Kind == "conventional")
            .OrderBy(f => f["controller"].Length == 0 ? 1 : 0).ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();

        foreach (var route in conventional)
        {
            var assembly = route["assembly"];
            var matches = actions.Where(a => Same(a["area"], route["area"])
                && (route["controller"].Length == 0 || string.Equals(a["controller"], route["controller"], StringComparison.OrdinalIgnoreCase))
                && (route["action"].Length == 0 || string.Equals(a["action"], route["action"], StringComparison.OrdinalIgnoreCase)))
                .ToList();
            // Prefer actions in the assembly that declares the route; plugins carry their own routes.
            var local = matches.Where(a => a["assembly"] == assembly).ToList();
            if (local.Count > 0) matches = local;

            if (route["controller"].Length > 0)
            {
                if (matches.Count == 0)
                {
                    ctx.AddBlindSpot(route.Owner, route.File, route.Line,
                        $"route to {route["controller"]}.{route["action"]} matched no controller action", Name);
                    continue;
                }
                var path = Normalize(System.Text.RegularExpressions.Regex.Replace(route["pattern"], "\\{(\\w+)=([^}]+)\\}", "{$1}"));
                foreach (var verb in matches.SelectMany(Verbs).Distinct().Order(StringComparer.Ordinal))
                {
                    var id = EndpointId(assembly, verb, path);
                    ctx.AddNode(new Node(id, NodeKind.Endpoint, $"{verb} {path}", route["project"], route.File, route.Line, route.Line));
                    foreach (var a in matches.Where(a => Verbs(a).Contains(verb)))
                    {
                        ctx.AddEdge(id, a.Owner, EdgeKind.RoutesTo, Label.Guessed, Name);
                        explicitlyRouted.Add(a.Owner);
                    }
                }
            }
            else
            {
                // {controller}/{action}: every action not already given its own route.
                foreach (var a in matches.Where(a => !explicitlyRouted.Contains(a.Owner)))
                {
                    var path = Normalize(route["pattern"]
                        .Replace("{controller=Home}", a["controller"]).Replace("{controller}", a["controller"])
                        .Replace("{action=Index}", a["action"]).Replace("{action}", a["action"]));
                    path = System.Text.RegularExpressions.Regex.Replace(path, "\\{(\\w+)=([^}]+)\\}", "{$1}");
                    path = System.Text.RegularExpressions.Regex.Replace(path, "\\{area(:[^}]*)?(=[^}]*)?\\}", a["area"]);
                    foreach (var verb in Verbs(a))
                    {
                        var id = EndpointId(assembly, verb, Normalize(path));
                        ctx.AddNode(new Node(id, NodeKind.Endpoint, $"{verb} {Normalize(path)}", route["project"], a["file"], int.Parse(a["line"]), int.Parse(a["line"])));
                        ctx.AddEdge(id, a.Owner, EdgeKind.RoutesTo, Label.Guessed, Name);
                    }
                }
            }
        }
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> Verbs(Fact action) => action["verbs"].Length == 0 ? ["ANY"] : action["verbs"].Split(',');
}
