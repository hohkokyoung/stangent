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
        // Route attributes are inherited (Kavita puts [Route("api/[controller]")] on a base controller).
        var classRoutes = new List<string>();
        for (var t = type; t is not null && classRoutes.Count == 0; t = t.BaseType) classRoutes = RouteTemplates(t);

        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            if (!ctx.InRepo(t) && !SymbolEqualityComparer.Default.Equals(t, type)) break;
            foreach (var method in t.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public || method.IsStatic) continue;
                if (method.GetAttributes().Any(a => a.AttributeClass?.Name == "NonActionAttribute")) continue;
                var methodId = ctx.NodeId(method);
                if (methodId is null) continue;
                ApiShapes.Record(ctx, Name, method, methodId, ctx.ShapesSeen);
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

    /// <summary>
    /// [Route("…")], and custom attributes implementing IRouteTemplateProvider whose constructor sets
    /// Template (Sonarr's [V3ApiController] builds $"api/v{version}/{resource}").
    /// </summary>
    static List<string> RouteTemplates(ISymbol symbol) =>
        [.. symbol.GetAttributes().Select(a => a.AttributeClass?.Name == "RouteAttribute" ? Arg(a)
            : !HttpAttributes.ContainsKey(a.AttributeClass?.Name ?? "") && a.AttributeClass is { } c && Implements(c, "IRouteTemplateProvider") ? CustomTemplate(a) : null)
            .OfType<string>()];

    static bool Implements(INamedTypeSymbol type, string name) => type.AllInterfaces.Any(i => i.Name == name);

    /// <summary>Evaluates the Template a custom route attribute's constructor chain assigns, from the arguments it was given.</summary>
    static string? CustomTemplate(AttributeData attribute)
    {
        if (attribute.AttributeConstructor is not { } ctor) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            var p = ctor.Parameters[i];
            var arg = i < attribute.ConstructorArguments.Length ? attribute.ConstructorArguments[i].Value : p.HasExplicitDefaultValue ? p.ExplicitDefaultValue : null;
            if (arg is not null) values[p.Name] = Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture)!;
        }
        for (var depth = 0; ctor is not null && depth < 5; depth++)
        {
            if (ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not ConstructorDeclarationSyntax syntax) return null;
            var assignment = syntax.Body?.Statements.OfType<ExpressionStatementSyntax>().Select(e => e.Expression).OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "Template" } or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Template" });
            if (assignment is not null) return Evaluate(assignment.Right, values);
            if (syntax.Initializer is not { } init || ctor.ContainingType.BaseType is not { } baseType) return null;
            var next = baseType.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length >= init.ArgumentList.Arguments.Count
                && c.Parameters.Skip(init.ArgumentList.Arguments.Count).All(p => p.HasExplicitDefaultValue));
            if (next is null) return null;
            var nextValues = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < next.Parameters.Length; i++)
            {
                var value = i < init.ArgumentList.Arguments.Count ? Evaluate(init.ArgumentList.Arguments[i].Expression, values)
                    : next.Parameters[i].HasExplicitDefaultValue ? Convert.ToString(next.Parameters[i].ExplicitDefaultValue, System.Globalization.CultureInfo.InvariantCulture) : null;
                if (value is not null) nextValues[next.Parameters[i].Name] = value;
            }
            (ctor, values) = (next, nextValues);
        }
        return null;
    }

    /// <summary>Literals, parameters, constants, interpolation and + concatenation; null when any part isn't known.</summary>
    static string? Evaluate(ExpressionSyntax expr, IReadOnlyDictionary<string, string> values)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                return lit.Token.ValueText;
            case IdentifierNameSyntax id:
                return values.GetValueOrDefault(id.Identifier.ValueText);
            case InterpolatedStringExpressionSyntax interp:
                var sb = new System.Text.StringBuilder();
                foreach (var c in interp.Contents)
                {
                    if (c is InterpolatedStringTextSyntax t) sb.Append(t.TextToken.ValueText);
                    else if (c is InterpolationSyntax h && Evaluate(h.Expression, values) is { } v) sb.Append(v);
                    else return null;
                }
                return sb.ToString();
            case BinaryExpressionSyntax bin when bin.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression):
                return Evaluate(bin.Left, values) is { } l && Evaluate(bin.Right, values) is { } r ? l + r : null;
            default:
                return null;
        }
    }

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

        // Bind arguments by the method's parameters, not position: in-house wrappers such as
        // CleanArchitecture's MapGet(Delegate handler, string pattern = "") put them in another order.
        var parameters = method.ReducedFrom is not null || !method.IsExtensionMethod ? method.Parameters : method.Parameters.RemoveAt(0);
        var args = call.ArgumentList.Arguments.Skip(method.ReducedFrom is null && method.IsExtensionMethod ? 1 : 0).ToList();
        var bound = new Dictionary<IParameterSymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);
        for (var i = 0; i < args.Count; i++)
        {
            var p = args[i].NameColon is { } nc ? parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.ValueText) : i < parameters.Length ? parameters[i] : null;
            if (p is not null) bound[p] = args[i].Expression;
        }
        var templateParam = parameters.FirstOrDefault(p => p.Type.SpecialType == SpecialType.System_String && p.Name is "pattern" or "template" or "route" or "path")
            ?? parameters.FirstOrDefault(p => p.Type.SpecialType == SpecialType.System_String);
        var handlerParam = parameters.FirstOrDefault(p => p.Type.Name == "Delegate" || p.Name is "handler" or "requestDelegate");
        if (handlerParam is null || !bound.TryGetValue(handlerParam, out var handler)) return;

        var owner = ctx.OwnerId(call, file.Model);
        string? template;
        if (templateParam is not null && bound.TryGetValue(templateParam, out var templateExpr))
        {
            // A wrapper forwarding its own parameters (builder.MapGet(pattern, handler)) isn't an endpoint itself.
            if (templateExpr is IdentifierNameSyntax tid && file.Model.GetSymbolInfo(tid).Symbol is IParameterSymbol
                && handler is IdentifierNameSyntax hid && file.Model.GetSymbolInfo(hid).Symbol is IParameterSymbol) return;
            template = ExtractContext.StringValue(templateExpr, file.Model);
        }
        else template = templateParam is { HasExplicitDefaultValue: true, ExplicitDefaultValue: string d } ? d : templateParam is null ? "" : null;
        if (template is null)
        {
            ctx.AddBlindSpot(owner, call, $"{name} with a route built at runtime", Name);
            return;
        }

        var prefix = call.Expression is MemberAccessExpressionSyntax ma ? GroupPrefix(ma.Expression, file.Model, 0) : "";
        prefix ??= ConventionPrefix(ctx, file, call);
        if (prefix is null)
        {
            ctx.AddBlindSpot(owner, call, "route group prefix not known here (passed in as a parameter)", Name);
            prefix = "{*}";
        }
        var methodsParam = parameters.FirstOrDefault(p => p.Name == "httpMethods");
        var verbs = name == "MapMethods" && methodsParam is not null && bound.TryGetValue(methodsParam, out var verbsExpr)
            ? (verbsExpr.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Select(l => l.Token.ValueText.ToUpperInvariant()).ToList() is { Count: > 0 } v ? v : ["ANY"])
            : [MapMethods.GetValueOrDefault(name, "ANY")];

        var handlerSymbol = file.Model.GetSymbolInfo(handler).Symbol ?? file.Model.GetSymbolInfo(handler).CandidateSymbols.FirstOrDefault();
        var target = handler is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax ? owner : ctx.NodeId(handlerSymbol);
        if (target is null) return;
        if (handlerSymbol is IMethodSymbol handlerMethod) ApiShapes.Record(ctx, Name, handlerMethod, target, ctx.ShapesSeen);
        foreach (var verb in verbs)
            AddEndpoint(ctx, verb, Normalize($"{prefix}/{template}"), target, file.RelativePath, ctx.Line(call), Label.Certain);
    }

    /// <summary>
    /// A group mapped by convention rather than in code, e.g. CleanArchitecture registers every
    /// IEndpointGroup at "/api/{ClassName}" by reflection:
    /// detectorSettings: { aspnetcore-routes: { groupConventions: ["CleanArchitecture.Web.Infrastructure.IEndpointGroup=/api/{type}"] } }.
    /// </summary>
    static string? ConventionPrefix(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call)
    {
        var type = file.Model.GetEnclosingSymbol(call.SpanStart)?.ContainingType;
        if (type is null) return null;
        foreach (var entry in ctx.Settings.GetValueOrDefault("groupConventions") ?? [])
        {
            if (entry.Split('=', 2) is not [var iface, var pattern]) continue;
            var matches = type.AllInterfaces.Any(i => ExtractContext.FullName(i) == iface.Trim() || i.Name == iface.Trim())
                || ExtractContext.IsOrDerives(type.BaseType, iface.Trim());
            if (matches) return pattern.Trim().Replace("{type}", type.Name);
        }
        return null;
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
