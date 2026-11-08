using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;
using Stangent.Graph.Loading;

namespace Stangent.Graph.Detectors;

/// <summary>
/// N1, N2: server-rendered views (Razor <c>.cshtml</c>), read from the C# the SDK's Razor compiler makes for each one, so the
/// markup, the tag helpers' values and the C# in the view are what the build itself sees. Each view is a <c>view</c> point:
/// its <c>@model</c> (<c>uses</c>), what its code calls, its forms (<c>form</c> points, linked to the endpoint they post to),
/// its buttons (<c>ui-event</c> facts with their text keys), its partials and view components (<c>renders</c>). An action's or
/// view component's <c>View(…)</c> reaches its view by MVC's lookup rules (<c>renders</c>). A view the compiler made nothing
/// for, and a Razor Page (whose handlers aren't read as endpoints), is a blind spot.
/// </summary>
public sealed class RazorViewsDetector : IDetector
{
    public string Name => "razor-views";

    /// <summary>The view a generated file was made from, from its <c>[assembly: RazorCompiledItem(…, "/Views/X/Y.cshtml")]</c>.</summary>
    public static string? ViewPath(SyntaxTree tree) => CompiledItem(tree)?.Path;

    static (string Kind, string Path)? CompiledItem(SyntaxTree tree)
    {
        if (tree.GetRoot() is not CompilationUnitSyntax unit) return null;
        foreach (var a in unit.AttributeLists.SelectMany(l => l.Attributes))
            if (a.Name.ToString().EndsWith("RazorCompiledItemAttribute", StringComparison.Ordinal) && a.ArgumentList?.Arguments is { Count: >= 3 } args
                && args[1].Expression is LiteralExpressionSyntax kind && args[2].Expression is LiteralExpressionSyntax path)
                return (kind.Token.ValueText, path.Token.ValueText);
        return null;
    }

    public void Extract(ExtractContext ctx)
    {
        var projectDir = ProjectScanner.Rel(ctx.RepoRoot, ctx.ProjectDir);
        foreach (var file in ctx.Files) Returns(ctx, file, projectDir);
        foreach (var file in ctx.RazorFiles) View(ctx, file, projectDir);
        var read = ctx.RazorFiles.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var path in ctx.ViewFiles.Select(f => ProjectScanner.Rel(ctx.RepoRoot, f)).Where(p => !read.Contains(p)).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("_ViewImports", StringComparison.OrdinalIgnoreCase)) continue;   // never compiled on its own
            ctx.Fragment.BlindSpots.Add(new BlindSpot(ctx.ProjectId, path, 0, "view not read: the Razor compiler made nothing for it", Name));
        }
    }

    // ---- N2: what controllers and view components render ----

    void Returns(ExtractContext ctx, SourceFile file, string projectDir)
    {
        var root = file.Tree.GetRoot();
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (file.Model.GetDeclaredSymbol(cls) is not INamedTypeSymbol type || ctx.NodeId(type) is not { } typeId
                || type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() != cls) continue;
            if (ComponentName(type) is { } component) ctx.AddFact(Name, "view-component", typeId, cls, ("name", component));
            // T8: an app's own view location rule (themes): MVC's lookup is then only a guess.
            if (type.AllInterfaces.Any(i => i.Name == "IViewLocationExpander")) ctx.AddFact(Name, "view-expander", typeId, cls, ("type", type.Name));
        }
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var invoked = ExtractContext.InvokedName(call);
            if (invoked is not ("View" or "PartialView"))
            {
                Forwarded(ctx, file, call, projectDir);
                continue;
            }
            if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol m) continue;
            var isController = ExtractContext.IsOrDerives(m.ContainingType, "Microsoft.AspNetCore.Mvc.Controller");
            if (!isController && !ExtractContext.IsOrDerives(m.ContainingType, "Microsoft.AspNetCore.Mvc.ViewComponent")) continue;
            if (EnclosingMethod(file.Model, call) is not { } method || ctx.OwnerId(call, file.Model) is not { } owner) continue;
            var name = "";
            for (var i = 0; i < call.ArgumentList.Arguments.Count; i++)
            {
                var arg = call.ArgumentList.Arguments[i];
                var parameter = arg.NameColon is { } nc ? m.Parameters.FirstOrDefault(p => p.Name == nc.Name.Identifier.ValueText) : i < m.Parameters.Length ? m.Parameters[i] : null;
                if (parameter?.Name != "viewName") continue;
                if (file.Model.GetConstantValue(arg.Expression) is { HasValue: true } c) name = c.Value as string ?? "";
                else
                {
                    ctx.AddBlindSpot(owner, call, $"{m.Name} chosen at runtime: {arg.Expression}", Name);
                    name = null;
                }
            }
            if (name is null) continue;
            var host = method.ContainingType;
            // A base class's helper, or a controller's non-action: which view it means depends on the action or component calling it.
            if (name.Length == 0 && (host.IsAbstract || isController && method.DeclaredAccessibility != Accessibility.Public))
            {
                ctx.AddBlindSpot(owner, call, $"{m.Name}() in {(host.IsAbstract ? "a base class" : "a helper")}: its view depends on who calls it", Name);
                continue;
            }
            ctx.AddFact(Name, "renders-view", owner, call, ("name", name), ("partial", m.Name == "PartialView" ? "yes" : ""),
                ("controller", isController ? Short(host.Name, "Controller") : ""), ("action", isController ? ActionName(method) : ""),
                ("area", isController ? Area(host) : ""), ("component", isController ? "" : ComponentName(host) ?? Short(host.Name, "ViewComponent")),
                ("projectDir", projectDir));
        }
    }

    /// <summary>
    /// T4: a call to a base class's helper that forwards to <c>View(…)</c> (nopCommerce's <c>NopViewComponent.ViewAsync(model)</c>) renders
    /// the caller's view: the view name it passes when the helper forwards one, else the caller's default (🟡 at linking).
    /// </summary>
    void Forwarded(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, string projectDir)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol m) return;
        var isController = ExtractContext.IsOrDerives(m.ContainingType, "Microsoft.AspNetCore.Mvc.Controller");
        if (!isController && !ExtractContext.IsOrDerives(m.ContainingType, "Microsoft.AspNetCore.Mvc.ViewComponent")) return;
        if (Forwarder(m.OriginalDefinition) is not var (forwarded, constant, partial)) return;
        if (EnclosingMethod(file.Model, call) is not { } method || method.ContainingType.IsAbstract || ctx.OwnerId(call, file.Model) is not { } owner) return;
        var name = constant;
        if (forwarded is not null)
        {
            var args = call.ArgumentList.Arguments;
            var index = m.Parameters.ToList().FindIndex(p => p.Name == forwarded);
            var arg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == forwarded) ?? (index >= 0 && index < args.Count && args[index].NameColon is null ? args[index] : null);
            if (arg is not null)
            {
                if (file.Model.GetConstantValue(arg.Expression) is { HasValue: true, Value: string v }) name = v;
                else
                {
                    ctx.AddBlindSpot(owner, call, $"{m.Name} chosen at runtime: {arg.Expression}", Name);
                    return;
                }
            }
        }
        var host = method.ContainingType;
        ctx.AddFact(Name, "renders-view", owner, call, ("name", name), ("partial", partial ? "yes" : ""),
            ("controller", isController ? Short(host.Name, "Controller") : ""), ("action", isController ? ActionName(method) : ""),
            ("area", isController ? Area(host) : ""), ("component", isController ? "" : ComponentName(host) ?? Short(host.Name, "ViewComponent")),
            ("projectDir", projectDir), ("via", m.Name));
    }

    /// <summary>A helper whose body calls <c>View(…)</c>/<c>PartialView(…)</c>: the parameter it passes as the view's name, or a constant name.</summary>
    static (string? Forwarded, string Constant, bool Partial)? Forwarder(IMethodSymbol m)
    {
        if (m.Name is "View" or "PartialView" || m.MethodKind != MethodKind.Ordinary) return null;
        foreach (var r in m.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is not MethodDeclarationSyntax decl) continue;
            foreach (var inner in decl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (ExtractContext.InvokedName(inner) is not ("View" or "PartialView") || inner.Expression is MemberAccessExpressionSyntax { Expression: not BaseExpressionSyntax and not ThisExpressionSyntax }) continue;
                var partial = ExtractContext.InvokedName(inner) == "PartialView";
                foreach (var a in inner.ArgumentList.Arguments)
                {
                    if (a.Expression is LiteralExpressionSyntax lit && lit.Token.Value is string text) return (null, text, partial);
                    if (a.Expression is IdentifierNameSyntax id && m.Parameters.FirstOrDefault(p => p.Name == id.Identifier.ValueText) is { Type.SpecialType: SpecialType.System_String } p)
                        return (p.Name, "", partial);
                }
                return (null, "", partial);
            }
        }
        return null;
    }

    static IMethodSymbol? EnclosingMethod(SemanticModel model, SyntaxNode node)
    {
        for (var s = model.GetEnclosingSymbol(node.SpanStart); s is not null; s = s.ContainingSymbol)
            if (s is IMethodSymbol { MethodKind: not (MethodKind.LambdaMethod or MethodKind.LocalFunction or MethodKind.AnonymousFunction) } m) return m;
        return null;
    }

    static string ActionName(IMethodSymbol method) =>
        method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ActionNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string
        ?? (method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^5] : method.Name);

    static string Area(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "AreaAttribute")?.ConstructorArguments.FirstOrDefault().Value is string area) return area;
        return "";
    }

    /// <summary>MVC's rule: a public, non-abstract class named <c>…ViewComponent</c>, derived from <c>ViewComponent</c>, or marked <c>[ViewComponent]</c>.</summary>
    static string? ComponentName(INamedTypeSymbol type)
    {
        if (type.IsAbstract || type.DeclaredAccessibility != Accessibility.Public || type.TypeKind != TypeKind.Class) return null;
        AttributeData? marker = null;
        for (var t = type; t is not null && marker is null; t = t.BaseType)
            marker = t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ViewComponentAttribute");
        if (marker is null && !type.Name.EndsWith("ViewComponent", StringComparison.Ordinal)
            && !ExtractContext.IsOrDerives(type, "Microsoft.AspNetCore.Mvc.ViewComponent")) return null;
        return marker?.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string ?? Short(type.Name, "ViewComponent");
    }

    static string Short(string name, string suffix) => name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length ? name[..^suffix.Length] : name;

    // ---- N1: one view ----

    void View(ExtractContext ctx, SourceFile file, string projectDir)
    {
        var rel = file.RelativePath;
        var (kind, compiledPath) = CompiledItem(file.Tree) ?? ("", "");
        var pathInProject = compiledPath.TrimStart('/');
        var abs = Path.Combine(ctx.RepoRoot, rel);
        var source = File.Exists(abs) ? File.ReadAllText(abs) : "";
        var lines = new SourceLines(source);
        var viewId = "view:" + rel;
        var fileId = "file:" + rel;
        ctx.Fragment.Nodes.Add(new Node(fileId, NodeKind.File, rel, ctx.ProjectId, rel));
        ctx.AddEdge(ctx.ProjectId, fileId, EdgeKind.Contains, Label.Certain);
        ctx.Fragment.Nodes.Add(new Node(viewId, NodeKind.View, pathInProject, ctx.ProjectId, rel, 1, Math.Max(1, lines.Count)));
        ctx.AddEdge(fileId, viewId, EdgeKind.Contains, Label.Certain);
        void Fact(string factKind, string owner, int line, params (string, string)[] data) =>
            ctx.Fragment.Facts.Add(new Fact(Name, factKind, owner, rel, line, data.ToDictionary(d => d.Item1, d => d.Item2, StringComparer.Ordinal)));

        var (viewController, viewArea) = Folder(pathInProject);
        var cls = file.Tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        var type = cls is null ? null : file.Model.GetDeclaredSymbol(cls) as INamedTypeSymbol;
        ITypeSymbol? model = null;
        for (var t = type?.BaseType; t is not null && model is null; t = t.BaseType)
            if (t is { Name: "RazorPage", Arity: 1 }) model = t.TypeArguments[0];
        Fact("view", viewId, 1, ("path", pathInProject), ("projectDir", projectDir), ("model", model?.ToDisplayString() ?? ""),
            ("controller", viewController), ("area", viewArea), ("kind", kind));
        if (model is not null && ctx.NodeId(model) is { } modelId) ctx.AddEdge(viewId, modelId, EdgeKind.Uses, Label.Certain);
        if (kind.Contains("razor-page", StringComparison.Ordinal))
            ctx.Fragment.BlindSpots.Add(new BlindSpot(viewId, rel, 1, "Razor Page: its handlers aren't read as endpoints", Name));

        // What the view's code reaches in the repo: calls for methods, uses for the rest. The compiler's own plumbing isn't in the repo.
        foreach (var n in file.Tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var symbol = file.Model.GetSymbolInfo(n).Symbol;
            if (symbol is null or ILocalSymbol or IParameterSymbol or INamespaceSymbol || ctx.NodeId(symbol) is not { } target) continue;
            ctx.AddEdge(viewId, target, symbol is IMethodSymbol ? EdgeKind.Calls : EdgeKind.Uses, Label.Certain, Name);
        }

        // The markup, rebuilt from the compiler's writes in order, with its forms as points.
        var execute = cls?.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(md => md.Identifier.ValueText == "ExecuteAsync");
        var page = execute?.Body is null ? null : new Page(new Walker(file.Model, Statics(cls!, file.Model)).Block(execute.Body.Statements), lines, file.Model);
        var formIndex = 0;
        string FormPoint(Page.FormRecord f)
        {
            var id = $"{viewId}#form{++formIndex}";
            ctx.Fragment.Nodes.Add(new Node(id, NodeKind.Form, $"{pathInProject} form {formIndex}", ctx.ProjectId, rel, f.Line, f.Line));
            ctx.AddEdge(viewId, id, EdgeKind.Contains, Label.Certain);
            Fact("form", id, f.Line, ("method", f.Method), ("route", f.Route ?? ""), ("action", f.Action ?? ""), ("controller", f.Controller ?? ""),
                ("area", f.Area ?? ""), ("url", f.Url ?? ""), ("runtime", f.Runtime ? "yes" : ""), ("view", viewId),
                ("viewController", viewController), ("viewArea", viewArea), ("viewAction", Path.GetFileNameWithoutExtension(pathInProject)),
                ("assembly", ctx.AssemblyName), ("via", f.Via));
            return id;
        }
        var points = new Dictionary<Page.FormRecord, string>(ReferenceEqualityComparer.Instance);
        foreach (var f in page?.Forms ?? []) points[f] = FormPoint(f);
        string FormId(Page.FormRecord? f) => f is not null && points.TryGetValue(f, out var id) ? id : "";

        // View components and partials named in the view's code; a partial keeps the form around it (T9).
        foreach (var call in file.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol m) continue;
            var line = lines.Mapped(call);
            var first = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (m.Name == "InvokeAsync" && m.ContainingType.Name.Contains("ViewComponentHelper", StringComparison.Ordinal))
            {
                if (m.IsGenericMethod && ctx.NodeId(m.TypeArguments[0]) is { } generic) ctx.AddEdge(viewId, generic, EdgeKind.Renders, Label.Certain, Name);
                else if (first is TypeOfExpressionSyntax typeOf && file.Model.GetTypeInfo(typeOf.Type).Type is { } t && ctx.NodeId(t) is { } byType)
                    ctx.AddEdge(viewId, byType, EdgeKind.Renders, Label.Certain, Name);
                else if (first is not null && file.Model.GetConstantValue(first) is { HasValue: true, Value: string name }) Fact("renders-component", viewId, line, ("name", name));
                else ctx.Fragment.BlindSpots.Add(new BlindSpot(viewId, rel, line, $"view component chosen at runtime: {call}", Name));
            }
            else if (m.Name is "Partial" or "PartialAsync" or "RenderPartial" or "RenderPartialAsync" && m.ContainingType.Name.Contains("HtmlHelper", StringComparison.Ordinal))
            {
                if (first is not null && file.Model.GetConstantValue(first) is { HasValue: true, Value: string name })
                    Fact("renders-partial", viewId, line, ("name", name), ("form", FormId(page?.FormAround(call))));
                else ctx.Fragment.BlindSpots.Add(new BlindSpot(viewId, rel, line, $"partial view chosen at runtime: {call}", Name));
            }
        }
        if (page is null) return;

        foreach (var el in page.Elements.Where(e => e.Tag == "partial"))
            if (el.Props.GetValueOrDefault("Name") is { } name) Fact("renders-partial", viewId, page.LineOf(el), ("name", name), ("form", FormId(page.PartialForms.GetValueOrDefault(el))));
            else if (el.Props.ContainsKey("Name")) ctx.Fragment.BlindSpots.Add(new BlindSpot(viewId, rel, page.LineOf(el), "partial view chosen at runtime", Name));

        foreach (var b in page.Buttons)
        {
            var handler = b.Target is { } own ? FormPoint(own) : b.Submit && b.Form is { } form ? points[form] : "";
            Fact("ui-event", viewId, b.Line, ("element", b.Element), ("event", b.Submit ? "submit" : "click"), ("handler", handler),
                ("keys", string.Join("|", b.Keys)), ("text", b.Text), ("id", b.Id), ("name", b.NameAttr), ("type", b.Type));
        }
        foreach (var s in page.Scripts)
            Fact("script", viewId, s.Line, ("text", s.Text));
    }

    /// <summary><c>Views/Customer/Register.cshtml</c> → (Customer, ""); <c>Areas/Admin/Views/Blog/List.cshtml</c> → (Blog, Admin).</summary>
    static (string Controller, string Area) Folder(string pathInProject)
    {
        var parts = pathInProject.Split('/');
        var area = parts.Length > 2 && parts[0].Equals("Areas", StringComparison.OrdinalIgnoreCase) ? parts[1] : "";
        var views = Array.FindIndex(parts, p => p.Equals("Views", StringComparison.OrdinalIgnoreCase));
        var controller = views >= 0 && views + 2 < parts.Length && !parts[views + 1].Equals("Shared", StringComparison.OrdinalIgnoreCase) ? parts[views + 1] : "";
        return (controller, area);
    }

    /// <summary>The compiler's static attributes: <c>__tagHelperAttribute_3 = new TagHelperAttribute("method", "post", …)</c>.</summary>
    static Dictionary<string, (string Name, string? Value)> Statics(ClassDeclarationSyntax cls, SemanticModel model)
    {
        var result = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
        foreach (var v in cls.Members.OfType<FieldDeclarationSyntax>().SelectMany(f => f.Declaration.Variables))
            if (v.Initializer?.Value is ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: >= 1 } args } creation
                && creation.Type.ToString().EndsWith("TagHelperAttribute", StringComparison.Ordinal)
                && model.GetConstantValue(args[0].Expression) is { HasValue: true, Value: string name })
            {
                string? value = null;
                if (args.Count > 1)
                {
                    var e = args[1].Expression;
                    if (e is ObjectCreationExpressionSyntax { ArgumentList.Arguments: [var inner] }) e = inner.Expression;   // new HtmlString("…")
                    value = model.GetConstantValue(e) is { HasValue: true, Value: string s } ? s : null;
                }
                result[v.Identifier.ValueText] = (name, value);
            }
        return result;
    }

    // ---- Linking ----

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var views = facts.Where(f => f.Kind == "view").ToList();
        var byPath = new Dictionary<(string, string), string>();
        foreach (var v in views) byPath.TryAdd((v["projectDir"], v["path"].ToLowerInvariant()), v.Owner);
        var viewOf = views.ToDictionary(v => v.Owner, StringComparer.Ordinal);
        var partialForms = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var plainPartials = new List<(string Parent, string Child)>();
        // T8: with an app's own view location rule (nopCommerce's themes), a view by the same path under another root may win at runtime.
        var expander = facts.Any(f => f.Kind == "view-expander");
        string? Key(Fact v)
        {
            var path = v["path"];
            var at = path.StartsWith("Views/", StringComparison.OrdinalIgnoreCase) ? 0 : path.IndexOf("/Views/", StringComparison.OrdinalIgnoreCase) + 1;
            return at < 0 || at == 0 && !path.StartsWith("Views/", StringComparison.OrdinalIgnoreCase) ? null
                : $"{v["projectDir"]}|{v["area"]}|{path[at..].ToLowerInvariant()}";
        }
        var sameKey = expander ? views.Select(v => (Key: Key(v), v.Owner)).Where(x => x.Key is not null).ToLookup(x => x.Key!, x => x.Owner) : null;
        void Alternatives(string owner, string hit)
        {
            if (sameKey is null || !viewOf.TryGetValue(hit, out var v) || Key(v) is not { } key) return;
            foreach (var other in sameKey[key].Where(o => o != hit)) ctx.AddEdge(owner, other, EdgeKind.Renders, Label.Guessed, Name);
        }

        string? At(string projectDir, string path) => byPath.GetValueOrDefault((projectDir, path.TrimStart('~', '/').ToLowerInvariant()));
        // A path written for where the app is deployed (~/Plugins/X/Views/Y.cshtml): the longest tail that names a view in the project.
        string? ByPath(string projectDir, string name)
        {
            var parts = name.TrimStart('~', '/').Split('/');
            for (var i = 0; i < parts.Length - 1; i++)
                if (At(projectDir, string.Join('/', parts[i..])) is { } hit) return hit;
            return null;
        }
        static bool IsPath(string name) => name.Contains('/') || name.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);
        static string File(string name) => name.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) ? name : name + ".cshtml";

        // V1: the {controller} in MVC's lookup is the request's, not the view's folder: a view shows on the pages of these controllers.
        var pages = new Dictionary<string, HashSet<(string Controller, string Area)>>(StringComparer.Ordinal);
        HashSet<(string Controller, string Area)> Pages(string view) => pages.TryGetValue(view, out var s) ? s : pages[view] = [];
        foreach (var v in views.Where(v => v["controller"].Length > 0)) Pages(v.Owner).Add((v["controller"], v["area"]));
        string? First(string dir, IEnumerable<string> places) => places.Select(p => At(dir, p)).FirstOrDefault(h => h is not null);

        // N2: MVC's view lookup: an action's view.
        foreach (var r in facts.Where(f => f.Kind == "renders-view" && f["component"].Length == 0))
        {
            var dir = r["projectDir"];
            var name = r["name"].Length > 0 ? r["name"] : r["action"];
            var area = r["area"];
            var hit = IsPath(name) ? At(dir, name) ?? ByPath(dir, name)
                : First(dir, area.Length > 0 ? [$"Areas/{area}/Views/{r["controller"]}/{File(name)}", $"Areas/{area}/Views/Shared/{File(name)}", $"Views/Shared/{File(name)}"]
                    : [$"Views/{r["controller"]}/{File(name)}", $"Views/Shared/{File(name)}"]);
            if (hit is not null)
            {
                ctx.AddEdge(r.Owner, hit, EdgeKind.Renders, r["name"].Length > 0 && IsPath(name) && !expander ? Label.Certain : Label.Guessed, Name);
                Alternatives(r.Owner, hit);
                if (r["controller"].Length > 0) Pages(hit).Add((r["controller"], area));
            }
            else ctx.AddBlindSpot(r.Owner, r.File, r.Line, $"view {name} not found by MVC's lookup rules", Name);
        }

        // V1: the views that invoke each component, by its name: a component's view shows on their pages.
        var componentOf = facts.Where(f => f.Kind == "view-component").GroupBy(f => f.Owner).ToDictionary(g => g.Key, g => g.First()["name"], StringComparer.Ordinal);
        var invokers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Invoked(string component, string view) => (invokers.TryGetValue(component, out var s) ? s : invokers[component] = []).Add(view);
        foreach (var e in ctx.Graph.AllEdges.Where(e => e.Kind == EdgeKind.Renders && e.Source.StartsWith("view:", StringComparison.Ordinal) && componentOf.ContainsKey(e.Target)))
            Invoked(componentOf[e.Target], e.Source);
        foreach (var r in facts.Where(f => f.Kind == "renders-component")) Invoked(r["name"], r.Owner);

        // A component's view, and a partial: their hits under what's known of the pages they show on.
        var componentViews = facts.Where(f => f.Kind == "renders-view" && f["component"].Length > 0).ToList();
        var partials = facts.Where(f => f.Kind == "renders-partial" && viewOf.ContainsKey(f.Owner)).ToList();
        List<string> ComponentHits(Fact r)
        {
            var dir = r["projectDir"];
            var name = r["name"].Length > 0 ? r["name"] : "Default";
            if (IsPath(name)) return [.. new[] { At(dir, name) ?? ByPath(dir, name) }.OfType<string>()];
            var file = File($"Components/{r["component"]}/{name}");
            var shown = (invokers.GetValueOrDefault(r["component"]) ?? []).SelectMany(Pages).Distinct().ToList();
            var hits = shown.Select(p => First(dir, p.Area.Length > 0
                    ? [$"Areas/{p.Area}/Views/{p.Controller}/{file}", $"Areas/{p.Area}/Views/Shared/{file}", $"Views/Shared/{file}"]
                    : [$"Views/{p.Controller}/{file}", $"Views/Shared/{file}"]))
                .OfType<string>().Distinct().ToList();
            if (hits.Count == 0 && First(dir, [$"Views/Shared/{file}", .. views.Where(v => v["area"].Length > 0).Select(v => $"Areas/{v["area"]}/Views/Shared/{file}").Distinct()]) is { } any)
                hits.Add(any);
            return hits;
        }
        // Partials: beside the view, then the shared folders; a shared or component view's partial also in each page's controller folder (V1).
        List<string> PartialHits(Fact r)
        {
            var v = viewOf[r.Owner];
            var dir = v["projectDir"];
            var name = r["name"];
            var here = Path.GetDirectoryName(v["path"])?.Replace('\\', '/') ?? "";
            // A path not rooted with ~/ or / is relative to the view (MVC's rule for names with a folder or an extension).
            // T11: MVC adds the extension a name with a folder leaves out.
            var relative = IsPath(name) && !name.StartsWith('~') && !name.StartsWith('/') ? Relative(here, File(name)) : null;
            var hit = IsPath(name) ? (relative is null ? null : At(dir, relative)) ?? At(dir, File(name)) ?? ByPath(dir, File(name))
                : First(dir, new[] { $"{here}/{File(name)}", v["area"].Length > 0 ? $"Areas/{v["area"]}/Views/Shared/{File(name)}" : null, $"Views/Shared/{File(name)}" }.OfType<string>());
            var hits = hit is null ? new List<string>() : [hit];
            if (!IsPath(name) && v["controller"].Length == 0)
                hits.AddRange(Pages(r.Owner).Select(p => First(dir, p.Area.Length > 0
                        ? [$"Areas/{p.Area}/Views/{p.Controller}/{File(name)}", $"Areas/{p.Area}/Views/Shared/{File(name)}", $"Views/Shared/{File(name)}"]
                        : [$"Views/{p.Controller}/{File(name)}", $"Views/Shared/{File(name)}"]))
                    .OfType<string>().Where(h => !hits.Contains(h)).Distinct().ToList());
            return hits;
        }
        // Pages flow down: from a view to its partials, from a component's invokers to its view; until nothing new.
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var r in partials)
                foreach (var h in PartialHits(r))
                    foreach (var p in Pages(r.Owner).ToList()) grew |= Pages(h).Add(p);
            foreach (var r in componentViews)
            {
                var shown = (invokers.GetValueOrDefault(r["component"]) ?? []).SelectMany(Pages).Distinct().ToList();
                foreach (var h in ComponentHits(r))
                    foreach (var p in shown) grew |= Pages(h).Add(p);
            }
        }

        foreach (var r in componentViews)
        {
            var hits = ComponentHits(r);
            var name = r["name"].Length > 0 ? r["name"] : "Default";
            foreach (var hit in hits)
            {
                ctx.AddEdge(r.Owner, hit, EdgeKind.Renders, r["name"].Length > 0 && IsPath(name) && !expander ? Label.Certain : Label.Guessed, Name);
                Alternatives(r.Owner, hit);
            }
            if (hits.Count == 0) ctx.AddBlindSpot(r.Owner, r.File, r.Line, $"view {name} not found by MVC's lookup rules", Name);
        }
        foreach (var r in partials)
        {
            var hits = PartialHits(r);
            foreach (var hit in hits)
            {
                ctx.AddEdge(r.Owner, hit, EdgeKind.Renders, Label.Guessed, Name);
                Alternatives(r.Owner, hit);
                if (r["form"].Length > 0) (partialForms.TryGetValue(hit, out var list) ? list : partialForms[hit] = []).Add(r["form"]);
                else plainPartials.Add((r.Owner, hit));
            }
            if (hits.Count == 0) ctx.AddBlindSpot(r.Owner, r.File, r.Line, $"partial view {r["name"]} not found", Name);
        }

        // A partial rendered with no form around it, inside a partial that has one, sits in that form too (Create → _CreateOrUpdate → _Values).
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (parent, child) in plainPartials)
                if (partialForms.TryGetValue(parent, out var outer))
                {
                    var inner = partialForms.TryGetValue(child, out var have) ? have : partialForms[child] = [];
                    foreach (var f in outer.Where(f => !inner.Contains(f)).ToList()) { inner.Add(f); changed = true; }
                }
        }

        // V2: a button whose script binding (the view's own, else the one in the project's scripts) always cancels doesn't submit.
        var byId = ctx.Facts("ui").Where(f => f.Kind == "dom-event" && f["selector"].StartsWith('#') && f["event"] is "click" or "submit")
            .ToLookup(f => f["selector"][1..], StringComparer.Ordinal);
        bool Cancelled(Fact b)
        {
            if (b["id"].Length == 0) return false;
            var all = byId[b["id"]].ToList();
            var own = all.Where(x => x.Owner == b.Owner).ToList();
            var fit = own.Count > 0 ? own : all.Where(x => !x.Owner.StartsWith("view:", StringComparison.Ordinal)).ToList();
            return fit.Count == 1 && fit[0]["cancels"] == "yes" || own.Count > 0 && own.All(x => x["cancels"] == "yes");
        }

        // T9: a submit button with no form of its own submits the form around the <partial> that rendered its view (🟡); with none, ❓.
        foreach (var group in facts.Where(f => f.Kind == "ui-event" && f["event"] == "submit" && f["handler"].Length == 0 && !Cancelled(f)).GroupBy(f => f.Owner))
        {
            var around = partialForms.GetValueOrDefault(group.Key)?.Distinct().ToList() ?? [];
            foreach (var form in around) ctx.AddEdge(group.Key, form, EdgeKind.Calls, Label.Guessed, Name);
            if (around.Count == 0)
                foreach (var b in group) ctx.AddBlindSpot(b.Owner, b.File, b.Line, "submit button outside any form the graph can see", Name);
        }

        static string? Relative(string folder, string name)
        {
            var parts = new List<string>(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
            foreach (var part in name.Split('/'))
                if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
                else if (part != ".") parts.Add(part);
            return string.Join('/', parts);
        }

        var components = facts.Where(f => f.Kind == "view-component").ToLookup(f => f["name"], StringComparer.OrdinalIgnoreCase);
        foreach (var r in facts.Where(f => f.Kind == "renders-component"))
        {
            var hits = components[r["name"]].ToList();
            foreach (var c in hits) ctx.AddEdge(r.Owner, c.Owner, EdgeKind.Renders, hits.Count == 1 ? Label.Certain : Label.Guessed, Name);
            if (hits.Count == 0) ctx.AddBlindSpot(r.Owner, r.File, r.Line, $"view component {r["name"]} not found", Name);
        }

        Forms(ctx, facts.Where(f => f.Kind == "form").ToList(), partialForms);
        Scripts(ctx, facts);
    }

    /// <summary>
    /// N5: a button's id joined to a plain script's binding for it (<c>$('#check-email').on('click', checkEmail)</c>): the view
    /// calls the handler, 🟡. The view's own inline scripts first; then the project's script files, only when one binding fits.
    /// An inline handler's code already belongs to the view.
    /// </summary>
    void Scripts(LinkContext ctx, List<Fact> facts)
    {
        var bindings = ctx.Facts("ui").Where(f => f.Kind == "dom-event" && f["selector"].StartsWith('#')).ToLookup(f => f["selector"][1..], StringComparer.Ordinal);
        if (bindings.Count == 0) return;
        foreach (var b in facts.Where(f => f.Kind == "ui-event" && f["id"].Length > 0))
        {
            var all = bindings[b["id"]].ToList();
            var own = all.Where(x => x.Owner == b.Owner).ToList();
            var fit = own.Count > 0 ? own : all.Where(x => !x.Owner.StartsWith("view:", StringComparison.Ordinal)).ToList();
            if (own.Count == 0 && fit.Count != 1) continue;
            foreach (var x in fit)
                if (x["handler"].StartsWith("ts|", StringComparison.Ordinal)) ctx.AddEdge(b.Owner, x["handler"], EdgeKind.Calls, Label.Guessed, Name);
        }
    }

    /// <summary>A form reaches the endpoint it posts to: by route name, by action and controller (the view's own by default), or by a literal URL.</summary>
    void Forms(LinkContext ctx, List<Fact> forms, Dictionary<string, List<string>> partialForms)
    {
        if (forms.Count == 0) return;
        var formById = forms.GroupBy(f => f.Owner).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var routes = ctx.Facts("aspnetcore-routes").ToList();
        var conventional = routes.Where(f => f.Kind == "conventional" && f.Data.ContainsKey("name")).ToList();
        var actions = routes.Where(f => f.Kind == "mvc-action").ToList();
        var endpoints = ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var routedTo = ctx.Graph.AllEdges.Where(e => e.Kind == EdgeKind.RoutesTo && endpoints.ContainsKey(e.Source)).ToLookup(e => e.Target, e => e.Source);
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        bool Fits(string endpointId, string verb) => verb == "*" || endpoints[endpointId].Name.StartsWith(verb + " ", StringComparison.Ordinal) || endpoints[endpointId].Name.StartsWith("ANY ", StringComparison.Ordinal);

        foreach (var f in forms)
        {
            if (f["runtime"] == "yes")
            {
                ctx.AddBlindSpot(f.Owner, f.File, f.Line, "form target built at runtime", Name);
                continue;
            }
            // V3: a button's own target with no form in its file takes the method of each form around the partial that renders it (🟡).
            var verbs = f["method"].Length > 0 ? [f["method"]]
                : (partialForms.GetValueOrDefault(f["view"]) ?? []).Select(id => formById.GetValueOrDefault(id)?["method"]).OfType<string>().Where(m => m.Length > 0).Distinct().ToList();
            if (verbs.Count == 0)
                ctx.AddBlindSpot(f.Owner, f.File, f.Line, "button with its own target outside any form the graph can see: its method is unknown", Name);
            // V2: an onclick's page by GET first; its script may send it another way ("*": any verb the action serves).
            if (f["via"] == "onclick") { if (Reach(f, "GET", false, report: false) == 0) Reach(f, "*", false); }
            else foreach (var verb in verbs) Reach(f, verb, f["method"].Length == 0);
        }

        int Reach(Fact f, string verb, bool inherited, bool report = true)
        {
            var hits = new List<string>();
            var label = inherited || f["via"] == "onclick" ? Label.Guessed : Label.Certain;
            if (f["route"].Length > 0)
            {
                var named = conventional.Where(c => c["name"] == f["route"]).OrderBy(c => c["assembly"] == f["assembly"] ? 0 : 1).FirstOrDefault();
                if (named is not null && named["controller"].Length > 0)
                {
                    var path = RoutesDetector.Normalize(Regex.Replace(named["pattern"], "\\{(\\w+)=([^}]+)\\}", "{$1}"));
                    string[] tried = verb == "*" ? ["GET", "POST", "PUT", "PATCH", "DELETE", "ANY"] : [verb, "ANY"];
                    var ids = tried.Select(v => RoutesDetector.EndpointId(named["assembly"], v, path)).Where(endpoints.ContainsKey);
                    hits.AddRange(verb == "*" ? ids : ids.Take(1));
                }
            }
            if (f["url"].Length > 0)
            {
                var path = RoutesDetector.Normalize(f["url"]);
                hits.AddRange(endpoints.Values.Where(n => n.Name == $"{verb} {path}" || n.Name == $"ANY {path}").Select(n => n.Id));
            }
            var controller = f["controller"].Length > 0 ? f["controller"] : f["viewController"];
            var action = f["action"].Length > 0 ? f["action"] : f["viewAction"];
            if (f["route"].Length == 0 && f["url"].Length == 0)
            {
                // The form tag helper's defaults: this view's controller and action (the view's name), its area.
                var area = f["area"].Length > 0 ? f["area"] : f["viewArea"];
                if (f["controller"].Length == 0 || f["action"].Length == 0) label = Label.Guessed;
                var candidates = actions.Where(a => Same(a["controller"], controller) && Same(a["action"], action) && Same(a["area"], area)).ToList();
                var local = candidates.Where(a => a["assembly"] == f["assembly"]).ToList();
                if (local.Count > 0) candidates = local;
                hits.AddRange(candidates.SelectMany(a => routedTo[a.Owner]).Where(e => Fits(e, verb)).Distinct());
            }
            // T10: routing prefers the endpoint with the form's verb over ANY at the same path.
            var withVerb = hits.Where(h => endpoints[h].Name.StartsWith(verb + " ", StringComparison.Ordinal)).Select(h => endpoints[h].Name[(verb.Length + 1)..]).ToHashSet();
            hits = [.. hits.Distinct().Where(h => !(endpoints[h].Name.StartsWith("ANY ", StringComparison.Ordinal) && withVerb.Contains(endpoints[h].Name[4..])))];
            if (hits.Count > 1) label = Label.Guessed;
            foreach (var h in hits) ctx.AddEdge(f.Owner, h, EdgeKind.CallsHttp, label, Name);
            if (hits.Count == 0 && report)
                ctx.AddBlindSpot(f.Owner, f.File, f.Line, $"form {(verb == "*" ? "(any verb)" : verb)} {(f["route"].Length > 0 ? "route " + f["route"] : f["url"].Length > 0 ? f["url"] : $"{controller}.{action}")} matched no endpoint", Name);
            return hits.Count;
        }
    }

    // ---- The compiler's writes, as a stream of markup ----

    abstract class Seg;
    sealed class TextSeg(string value, SyntaxNode at) : Seg { public string Value { get; } = value; public SyntaxNode At { get; } = at; }
    sealed class ExprSeg(ExpressionSyntax node) : Seg { public ExpressionSyntax Node { get; } = node; }
    sealed class ElemSeg(string tag, bool selfClosing, SyntaxNode at) : Seg
    {
        public string Tag { get; } = tag.ToLowerInvariant();
        public bool SelfClosing { get; } = selfClosing;
        public SyntaxNode At { get; } = at;
        public List<Seg> Body { get; set; } = [];
        /// <summary>Tag helper properties (<c>Action</c>, <c>Route</c>, <c>Method</c>…); null is a value made at runtime.</summary>
        public Dictionary<string, string?> Props { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string?> Attrs { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>The tag helpers the compiler ran on the element (<c>FormTagHelper</c>…).</summary>
        public HashSet<string> Helpers { get; } = new(StringComparer.Ordinal);
        public bool BeginForm { get; init; }
    }

    sealed class Walker(SemanticModel model, Dictionary<string, (string Name, string? Value)> statics)
    {
        List<Seg>? buffer;
        string? bufferValue;
        readonly Stack<string> suffixes = new();
        string? attrName;
        List<string?>? attrParts;

        public List<Seg> Block(IEnumerable<StatementSyntax> statements)
        {
            var o = new List<Seg>();
            ElemSeg? current = null;
            foreach (var s in statements) Statement(s, o, ref current);
            return o;
        }

        void Statement(StatementSyntax s, List<Seg> o, ref ElemSeg? current)
        {
            switch (s)
            {
                case BlockSyntax b: foreach (var x in b.Statements) Statement(x, o, ref current); break;
                case IfStatementSyntax i:
                    Statement(i.Statement, o, ref current);
                    if (i.Else is not null) Statement(i.Else.Statement, o, ref current);
                    break;
                case ForEachStatementSyntax f: Statement(f.Statement, o, ref current); break;
                case ForStatementSyntax f: Statement(f.Statement, o, ref current); break;
                case WhileStatementSyntax w: Statement(w.Statement, o, ref current); break;
                case DoStatementSyntax d: Statement(d.Statement, o, ref current); break;
                case SwitchStatementSyntax sw: foreach (var x in sw.Sections.SelectMany(x => x.Statements)) Statement(x, o, ref current); break;
                case TryStatementSyntax t:
                    Statement(t.Block, o, ref current);
                    foreach (var c in t.Catches) Statement(c.Block, o, ref current);
                    if (t.Finally is not null) Statement(t.Finally.Block, o, ref current);
                    break;
                case UsingStatementSyntax u:
                    var begin = u.Expression as InvocationExpressionSyntax
                        ?? u.Declaration?.Variables.FirstOrDefault()?.Initializer?.Value as InvocationExpressionSyntax;
                    if (begin is not null && BeginForm(begin) is { } form)
                    {
                        form.Body = Block([u.Statement]);
                        o.Add(form);
                    }
                    else Statement(u.Statement, o, ref current);
                    break;
                case ExpressionStatementSyntax e: Expression(e.Expression, o, ref current); break;
            }
        }

        void Expression(ExpressionSyntax e, List<Seg> o, ref ElemSeg? current)
        {
            if (e is AwaitExpressionSyntax aw) e = aw.Expression;
            if (e is AssignmentExpressionSyntax a)
            {
                var right = a.Right is AwaitExpressionSyntax ra ? ra.Expression : a.Right;
                var left = a.Left.ToString();
                if (right is InvocationExpressionSyntax inv && ExtractContext.InvokedName(inv) is { } called)
                {
                    if (left == "__tagHelperExecutionContext" && called == "Begin" && inv.ArgumentList.Arguments.Count >= 1
                        && model.GetConstantValue(inv.ArgumentList.Arguments[0].Expression) is { HasValue: true, Value: string tag })
                    {
                        var mode = inv.ArgumentList.Arguments.Count > 1 ? inv.ArgumentList.Arguments[1].Expression.ToString() : "";
                        var el = new ElemSeg(tag, mode.EndsWith("SelfClosing", StringComparison.Ordinal) || mode.EndsWith("StartTagOnly", StringComparison.Ordinal), inv);
                        if (inv.ArgumentList.Arguments.Count > 3 && inv.ArgumentList.Arguments[3].Expression is ParenthesizedLambdaExpressionSyntax { Block: { } body })
                            el.Body = Block(body.Statements);
                        o.Add(el);
                        current = el;
                        return;
                    }
                    if (left == "__tagHelperExecutionContext" && called == "End") { current = null; return; }
                    if (left == "__tagHelperStringValueBuffer" && called == "EndWriteTagHelperAttribute")
                    {
                        bufferValue = Constant(buffer ?? []);
                        buffer = null;
                        return;
                    }
                }
                // __X = CreateTagHelper<FormTagHelper>(): a tag helper runs on the element.
                if (current is not null && right is InvocationExpressionSyntax { Expression: GenericNameSyntax { Identifier.ValueText: "CreateTagHelper" } created })
                {
                    var helper = created.TypeArgumentList.Arguments.LastOrDefault()?.ToString() ?? "";
                    current.Helpers.Add(helper[(helper.LastIndexOf('.') + 1)..]);
                    return;
                }
                if (current is not null && a.Left is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } ma
                    && id.Identifier.ValueText.StartsWith("__", StringComparison.Ordinal) && !left.StartsWith("__tagHelperExecutionContext", StringComparison.Ordinal))
                    current.Props[ma.Name.Identifier.ValueText] = Value(a.Right);
                return;
            }
            if (e is not InvocationExpressionSyntax call || ExtractContext.InvokedName(call) is not { } name) return;
            var args = call.ArgumentList.Arguments;
            var target = buffer ?? o;
            switch (name)
            {
                case "WriteLiteral" when args.Count == 1:
                    if (model.GetConstantValue(args[0].Expression) is { HasValue: true, Value: string text }) target.Add(new TextSeg(text, call));
                    else target.Add(new ExprSeg(args[0].Expression));
                    return;
                case "Write" when args.Count == 1:
                    target.Add(new ExprSeg(args[0].Expression));
                    return;
                case "BeginWriteTagHelperAttribute":
                    buffer = [];
                    return;
                case "BeginWriteAttribute" when args.Count >= 4:
                    target.Add(new TextSeg(Str(args[1].Expression) ?? "", call));
                    suffixes.Push(Str(args[3].Expression) ?? "");
                    return;
                case "WriteAttributeValue" when args.Count >= 3:
                    target.Add(new TextSeg(Str(args[0].Expression) ?? "", call));
                    if (Str(args[2].Expression) is { } literal) target.Add(new TextSeg(literal, call));
                    else target.Add(new ExprSeg(args[2].Expression));
                    return;
                case "EndWriteAttribute":
                    target.Add(new TextSeg(suffixes.Count > 0 ? suffixes.Pop() : "", call));
                    return;
                case "AddHtmlAttribute" or "AddTagHelperAttribute" when current is not null:
                    if (args.Count == 1 && args[0].Expression is IdentifierNameSyntax field && statics.TryGetValue(field.Identifier.ValueText, out var st))
                        current.Attrs[st.Name] = st.Value;
                    else if (args.Count >= 2 && Str(args[0].Expression) is { } attr) current.Attrs[attr] = Value(args[1].Expression);
                    return;
                case "BeginAddHtmlAttributeValues" when args.Count >= 2:
                    attrName = Str(args[1].Expression);
                    attrParts = [];
                    return;
                case "AddHtmlAttributeValue" when args.Count >= 3:
                    attrParts?.Add(Str(args[0].Expression));
                    attrParts?.Add(Str(args[2].Expression));
                    return;
                case "EndAddHtmlAttributeValues":
                    if (current is not null && attrName is not null && attrParts is not null)
                        current.Attrs[attrName] = attrParts.Any(p => p is null) ? null : string.Concat(attrParts);
                    attrName = null;
                    attrParts = null;
                    return;
            }
            // Sections (DefineSection("scripts", async() => { … })) and other bodies passed as lambdas are part of the view.
            foreach (var arg in args)
                if (arg.Expression is ParenthesizedLambdaExpressionSyntax { Block: { } body }) o.AddRange(Block(body.Statements));
        }

        /// <summary><c>Html.BeginForm("Register", "Account", FormMethod.Post)</c> / <c>Html.BeginRouteForm("Register")</c>: a form, POST unless told.</summary>
        ElemSeg? BeginForm(InvocationExpressionSyntax call)
        {
            if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol { Name: "BeginForm" or "BeginRouteForm" } m) return null;
            var el = new ElemSeg("form", false, call) { BeginForm = true };
            el.Props["Method"] = "post";
            var parameters = m.Parameters;   // a reduced extension's parameters leave out `this`
            var args = call.ArgumentList.Arguments;
            for (var i = 0; i < args.Count; i++)
            {
                var p = args[i].NameColon is { } nc ? parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.ValueText) : i < parameters.Length ? parameters[i] : null;
                switch (p?.Name)
                {
                    case "actionName": el.Props["Action"] = Value(args[i].Expression); break;
                    case "controllerName": el.Props["Controller"] = Value(args[i].Expression); break;
                    case "routeName": el.Props["Route"] = Value(args[i].Expression); break;
                    case "method":
                        el.Props["Method"] = model.GetSymbolInfo(args[i].Expression).Symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } f ? f.Name : null;
                        break;
                    case "routeValues" when args[i].Expression is AnonymousObjectCreationExpressionSyntax anon:
                        if (anon.Initializers.FirstOrDefault(x => x.NameEquals?.Name.Identifier.ValueText == "area") is { } area) el.Props["Area"] = Value(area.Expression);
                        break;
                }
            }
            return el;
        }

        string? Str(ExpressionSyntax e) => model.GetConstantValue(e) is { HasValue: true, Value: string s } ? s : null;

        /// <summary>A tag helper value: a static attribute's, the buffer just written, or a constant; null when made at runtime.</summary>
        string? Value(ExpressionSyntax e)
        {
            if (e is CastExpressionSyntax cast) e = cast.Expression;
            if (e is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax f, Name.Identifier.ValueText: "Value" } && statics.TryGetValue(f.Identifier.ValueText, out var st))
                return st.Value;
            if (e is IdentifierNameSyntax { Identifier.ValueText: "__tagHelperStringValueBuffer" }) return bufferValue;
            var c = model.GetConstantValue(e);
            return c.HasValue ? c.Value switch { string s => s, bool b => b ? "true" : "false", null => null, var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) } : null;
        }

        string? Constant(List<Seg> segs)
        {
            var sb = new StringBuilder();
            foreach (var s in segs)
                switch (s)
                {
                    case TextSeg t: sb.Append(t.Value); break;
                    case ExprSeg x when model.GetConstantValue(x.Node) is { HasValue: true, Value: var v }: sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)); break;
                    default: return null;
                }
            return sb.ToString();
        }
    }

    /// <summary>The view's source, for lines: the compiler's literal text appears verbatim, its C# through <c>#line</c>.</summary>
    sealed class SourceLines
    {
        readonly string text;
        readonly List<int> starts = [0];
        public SourceLines(string text)
        {
            this.text = text;
            for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        }
        public int Count => text.Length == 0 ? 0 : starts.Count - (text.EndsWith('\n') ? 1 : 0);
        public string Text => text;
        public int LineAt(int offset)
        {
            var i = starts.BinarySearch(Math.Clamp(offset, 0, Math.Max(0, text.Length)));
            return (i >= 0 ? i : ~i - 1) + 1;
        }
        public int Offset(int line, int character) => line < starts.Count ? starts[line] + character : text.Length;
        /// <summary>The view line a C# node maps to through <c>#line</c>; 0 when the compiler hid it.</summary>
        public int Mapped(SyntaxNode node)
        {
            var span = node.GetLocation().GetMappedLineSpan();
            return span.HasMappedPath ? span.StartLinePosition.Line + 1 : 0;
        }
    }

    /// <summary>The view's markup rebuilt as one string, C# values as markers, then read for forms, buttons and scripts.</summary>
    sealed class Page
    {
        public sealed record FormRecord(int Line, string Method, string? Route, string? Action, string? Controller, string? Area, string? Url, bool Runtime)
        {
            /// <summary>"onclick" when the target is read from a button's onclick (V2), a guess at what its script does.</summary>
            public string Via { get; init; } = "";
        }
        /// <summary>T9: the form around each place the view renders a partial (by html offset), for the partial's buttons.</summary>
        readonly List<(int Start, int End, FormRecord Form)> formSpans = [];
        readonly List<int> exprAt = [];
        public sealed record Button(int Line, string Element, string Type, bool Submit, FormRecord? Form, FormRecord? Target, IReadOnlyList<string> Keys, string Text, string Id, string NameAttr);
        public sealed record Script(int Line, string Text);

        readonly StringBuilder html = new();
        readonly List<(int Html, int Source)> map = [];
        readonly List<ExpressionSyntax> exprs = [];
        readonly SourceLines lines;
        readonly SemanticModel model;
        readonly Dictionary<ElemSeg, int> elemLines = [];
        int cursor;

        public List<ElemSeg> Elements { get; } = [];
        public List<FormRecord> Forms { get; } = [];
        public List<Button> Buttons { get; } = [];
        public List<Script> Scripts { get; } = [];

        public Page(List<Seg> segs, SourceLines lines, SemanticModel model)
        {
            this.lines = lines;
            this.model = model;
            Render(segs);
            Scan();
        }

        public int LineOf(ElemSeg el) => elemLines.GetValueOrDefault(el, 1);

        void Render(List<Seg> segs)
        {
            foreach (var seg in segs)
                switch (seg)
                {
                    case TextSeg t:
                        map.Add((html.Length, Locate(t.Value)));
                        html.Append(t.Value);
                        break;
                    case ExprSeg x:
                        var span = x.Node.GetLocation().GetMappedLineSpan();
                        if (span.HasMappedPath) cursor = Math.Max(cursor, lines.Offset(span.StartLinePosition.Line, span.StartLinePosition.Character));
                        map.Add((html.Length, cursor));
                        exprAt.Add(html.Length);
                        html.Append('\u0001').Append(exprs.Count).Append('\u0002');
                        exprs.Add(x.Node);
                        break;
                    case ElemSeg el:
                        var start = lines.Text.IndexOf("<" + el.Tag, cursor, StringComparison.OrdinalIgnoreCase);
                        if (start >= 0 && start - cursor < 2000 && !el.BeginForm) cursor = start + 1;
                        elemLines[el] = lines.LineAt(start >= 0 && !el.BeginForm ? start : cursor);
                        map.Add((html.Length, el.BeginForm ? cursor : Math.Max(0, start)));
                        html.Append('<').Append(el.Tag).Append(" \u0003").Append(Elements.Count).Append('\u0004').Append('>');
                        Elements.Add(el);
                        Render(el.Body);
                        if (!el.SelfClosing) html.Append("</").Append(el.Tag).Append('>');
                        break;
                }
        }

        /// <summary>Where a literal sits in the view: the next place its text appears.</summary>
        int Locate(string text)
        {
            var source = lines.Text;
            if (text.Trim().Length == 0)
            {
                var near = source.IndexOf(text, cursor, Math.Min(source.Length - cursor, text.Length + 200), StringComparison.Ordinal);
                if (near >= 0) cursor = near + text.Length;
                return near >= 0 ? near : cursor;
            }
            var at = source.IndexOf(text, cursor, StringComparison.Ordinal);
            if (at < 0) at = source.IndexOf(text, StringComparison.Ordinal);
            if (at < 0) return cursor;
            cursor = at + text.Length;
            return at;
        }

        int Line(int htmlOffset)
        {
            var i = map.FindLastIndex(m => m.Html <= htmlOffset);
            return i < 0 ? 1 : lines.LineAt(map[i].Source + (htmlOffset - map[i].Html));
        }

        static readonly Regex Tag = new(@"<(/?)([A-Za-z][\w:.-]*)((?:""[^""]*""|'[^']*'|[^'"">])*)>", RegexOptions.CultureInvariant);
        static readonly Regex Attr = new(@"([^\s=""'/\u0003\u0004]+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+)))?", RegexOptions.CultureInvariant);
        static readonly Regex ElemMark = new("\u0003(\\d+)\u0004", RegexOptions.CultureInvariant);
        static readonly Regex ExprMark = new("\u0001(\\d+)\u0002", RegexOptions.CultureInvariant);

        void Scan()
        {
            var text = html.ToString();
            var forms = new Stack<(FormRecord Form, int Start)>();
            var scriptEnd = -1;
            foreach (Match m in Tag.Matches(text))
            {
                // Markup in a script's text (a grid column's '<button …>' string) isn't the page's own.
                if (m.Index < scriptEnd) continue;
                var name = m.Groups[2].Value.ToLowerInvariant();
                if (m.Groups[1].Value == "/")
                {
                    if (name == "form" && forms.Count > 0)
                    {
                        var (open, start) = forms.Pop();
                        formSpans.Add((start, m.Index, open));
                    }
                    continue;
                }
                var el = ElemMark.Match(m.Groups[3].Value) is { Success: true } em ? Elements[int.Parse(em.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] : null;
                var (attrs, raw) = Attributes(m.Groups[3].Value, el);
                var line = el is not null ? LineOf(el) : Line(m.Index);
                switch (name)
                {
                    case "form":
                        var form = FormOf(el, attrs, raw, line, null);
                        Forms.Add(form);
                        forms.Push((form, m.Index));
                        break;
                    case "partial" when el is not null:
                        PartialForms[el] = forms.Count > 0 ? forms.Peek().Form : null;
                        break;
                    case "button":
                    case "input":
                        var type = (Get(attrs, "type") ?? (name == "button" ? "submit" : "text")).ToLowerInvariant();
                        if (name == "input" && type is not ("submit" or "button" or "image" or "reset")) break;
                        if (type == "reset") break;
                        var inner = name == "button" ? Inner(text, m, "button") : Get(attrs, "value") ?? "";
                        var (keys, words) = Words(inner);
                        var formNow = forms.Count > 0 ? forms.Peek().Form : null;
                        // V2: an inline handler that ends `return false` stops the submit.
                        var onclick = raw.GetValueOrDefault("onclick") ?? "";
                        var submit = type is "submit" or "image" && !CancelsSubmit.IsMatch(onclick);
                        // T3: <button asp-action="…">, formaction="…" or formmethod="…": its own target, else its form's.
                        // V2: a button that doesn't submit, with one constant @Url.Action/RouteUrl in its onclick, opens that (🟡 GET).
                        var own = el is not null && (el.Props.ContainsKey("Action") || el.Props.ContainsKey("Route") || el.Props.ContainsKey("Controller"))
                            || raw.ContainsKey("formaction") || raw.ContainsKey("formmethod")
                            ? ButtonTarget(el, attrs, raw, line, formNow) : !submit ? OnclickTarget(onclick, line) : null;
                        Buttons.Add(new Button(line, name, type, submit, formNow, own, keys, words, Get(attrs, "id") ?? "", Get(attrs, "name") ?? ""));
                        break;
                    case "script":
                        scriptEnd = text.IndexOf("</script", m.Index + m.Length, StringComparison.OrdinalIgnoreCase);
                        if (Get(attrs, "src") is not null) break;
                        var body = Inner(text, m, "script");
                        var bodyStart = m.Index + m.Length;
                        if (body.Trim().Length > 0) Scripts.Add(new Script(Line(bodyStart), ScriptText(text, bodyStart, bodyStart + body.Length)));
                        break;
                }
            }
            while (forms.Count > 0) { var (open, start) = forms.Pop(); formSpans.Add((start, text.Length, open)); }
        }

        public Dictionary<ElemSeg, FormRecord?> PartialForms { get; } = [];

        /// <summary>The form around a C# value the view writes (<c>@await Html.PartialAsync("_Row")</c>), innermost first.</summary>
        public FormRecord? FormAround(SyntaxNode node)
        {
            var i = exprs.FindIndex(e => e.Span.Contains(node.Span) && e.SyntaxTree == node.SyntaxTree);
            if (i < 0) return null;
            var at = exprAt[i];
            return formSpans.Where(f => f.Start <= at && at < f.End).OrderByDescending(f => f.Start).Select(f => f.Form).FirstOrDefault();
        }

        /// <summary>
        /// A script as the browser gets it, with each value the server writes (<c>'@Url.Action("Add")'</c>) as <c>__server_value__</c>,
        /// which the extractor reads as made at runtime inside a string or out, so a URL the server writes is ❓, never a made-up path.
        /// Lines the view's C# takes up are kept as empty lines, so the script's lines stay the view's.
        /// </summary>
        string ScriptText(string text, int start, int end)
        {
            var sb = new StringBuilder();
            var line = Line(start);
            var segments = new Dictionary<int, int>();
            foreach (var (h, src) in map) if (h >= start && h < end) segments[h] = src;
            for (var p = start; p < end; p++)
            {
                if (segments.TryGetValue(p, out var src))
                    for (var target = lines.LineAt(src); line < target; line++) sb.Append('\n');
                var c = text[p];
                if (c == '\u0001')
                {
                    var close = text.IndexOf('\u0002', p);
                    sb.Append(ServerValue);
                    p = close < 0 ? end : close;
                    continue;
                }
                if (c == '\n') line++;
                sb.Append(c);
            }
            return sb.ToString();
        }

        const string ServerValue = "__server_value__";

        static string? Get(Dictionary<string, string?> attrs, string name) => attrs.TryGetValue(name, out var v) ? v : null;

        /// <summary>Attribute values: plain (null when the server writes any of it) and raw (with the server's values as markers).</summary>
        (Dictionary<string, string?> Plain, Dictionary<string, string> Raw) Attributes(string text, ElemSeg? el)
        {
            var plain = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in Attr.Matches(ElemMark.Replace(text, " ")))
            {
                var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Success ? a.Groups[4].Value : "";
                plain[a.Groups[1].Value] = value.Contains('\u0001') ? null : value;
                raw[a.Groups[1].Value] = value;
            }
            if (el is not null)
                foreach (var (k, v) in el.Attrs)
                {
                    plain[k] = v;
                    raw[k] = v ?? "\u0001?\u0002";
                }
            return (plain, raw);
        }

        static bool Targets(ElemSeg? el) => el is not null && (el.Props.ContainsKey("Action") || el.Props.ContainsKey("Route") || el.Props.ContainsKey("Controller"));

        FormRecord FormOf(ElemSeg? el, Dictionary<string, string?> attrs, Dictionary<string, string> raw, int line, string? method)
        {
            string? Prop(string name) => el is not null && el.Props.TryGetValue(name, out var v) ? v : null;
            var runtime = el is not null && el.Props.Where(p => p.Key is "Action" or "Controller" or "Area" or "Route").Any(p => p.Value is null);
            // T1: the form tag helper writes method="post" when it builds the form (asp-* given, or no action of its own); a plain <form> is GET.
            var built = el is not null && el.Helpers.Contains("FormTagHelper") && (Targets(el) || !raw.ContainsKey("action"));
            var verb = method ?? (el is not null && el.Props.TryGetValue("Method", out var pm) ? pm : Get(attrs, "method")) ?? (built ? "post" : "get");
            string? url = null, route = Prop("Route"), action = Prop("Action"), controller = Prop("Controller"), area = Prop("Area");
            if (!Targets(el) && raw.TryGetValue("action", out var given)) (url, route, action, controller, area, runtime) = Address(given, runtime);
            return new FormRecord(line, verb.ToUpperInvariant(), route, action, controller, area, url, runtime);
        }

        /// <summary>T3: a button's own target: its tag helper's asp-*, else formaction (literal, or @Url.Action/RouteUrl), with formmethod or its form's method.</summary>
        FormRecord ButtonTarget(ElemSeg? el, Dictionary<string, string?> attrs, Dictionary<string, string> raw, int line, FormRecord? form)
        {
            // V3: with no form in this file, the method is the form's around the partial that renders it: "" until linking.
            var verb = (Get(attrs, "formmethod") ?? form?.Method ?? "").ToUpperInvariant();
            if (raw.ContainsKey("formmethod") && Get(attrs, "formmethod") is null) return new FormRecord(line, "GET", null, null, null, null, null, true);
            if (Targets(el))
            {
                string? Prop(string name) => el!.Props.TryGetValue(name, out var v) ? v : null;
                var runtime = el!.Props.Where(p => p.Key is "Action" or "Controller" or "Area" or "Route").Any(p => p.Value is null);
                return new FormRecord(line, verb, Prop("Route"), Prop("Action"), Prop("Controller"), Prop("Area"), null, runtime);
            }
            if (!raw.TryGetValue("formaction", out var given))
                return form is null ? new FormRecord(line, verb, null, null, null, null, null, false) : form with { Line = line, Method = verb };
            var (url, route, action, controller, area, rt) = Address(given, false);
            return new FormRecord(line, verb, route, action, controller, area, url, rt);
        }

        static readonly Regex CancelsSubmit = new(@"(?:^|;|javascript:)\s*return\s+false\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>V2: an onclick holding one server value, a constant <c>@Url.Action</c>/<c>@Url.RouteUrl</c>: the page it opens (🟡 GET).</summary>
        FormRecord? OnclickTarget(string onclick, int line)
        {
            var values = Regex.Matches(onclick, "\u0001\\d+\u0002");
            if (values.Count != 1) return null;
            var (_, route, action, controller, area, runtime) = Address(values[0].Value, false);
            return runtime || action is null && route is null ? null
                : new FormRecord(line, "GET", route, action, controller, area, null, false) { Via = "onclick" };
        }

        /// <summary>An action/formaction value: a literal URL; <c>@Url.Action("Add", "Cart")</c> or <c>@Url.RouteUrl("Name")</c> with constants; else made at runtime.</summary>
        (string? Url, string? Route, string? Action, string? Controller, string? Area, bool Runtime) Address(string value, bool runtime)
        {
            if (!value.Contains('\u0001'))
                return (value.Length == 0 || value.StartsWith('#') ? null : value, null, null, null, null, runtime);
            var single = Regex.Match(value.Trim(), "^\u0001(\\d+)\u0002$");
            if (single.Success && exprs[int.Parse(single.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] is InvocationExpressionSyntax call
                && model.GetSymbolInfo(call).Symbol is IMethodSymbol { Name: "Action" or "RouteUrl" } m && m.ContainingType.Name.Contains("UrlHelper", StringComparison.Ordinal))
            {
                string? route = null, action = null, controller = null, area = null;
                var known = true;
                var args = call.ArgumentList.Arguments;
                for (var i = 0; i < args.Count; i++)
                {
                    var p = args[i].NameColon is { } nc ? m.Parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.ValueText) : i < m.Parameters.Length ? m.Parameters[i] : null;
                    string? Const() => model.GetConstantValue(args[i].Expression) is { HasValue: true, Value: string v } ? v : null;
                    switch (p?.Name)
                    {
                        case "action": action = Const(); known &= action is not null; break;
                        case "controller": controller = Const(); known &= controller is not null; break;
                        case "routeName": route = Const(); known &= route is not null; break;
                        case "values" when args[i].Expression is AnonymousObjectCreationExpressionSyntax anon:
                            if (anon.Initializers.FirstOrDefault(x => x.NameEquals?.Name.Identifier.ValueText == "area") is { } a)
                            {
                                area = model.GetConstantValue(a.Expression) is { HasValue: true, Value: string v } ? v : null;
                                known &= area is not null;
                            }
                            break;
                    }
                }
                if (known && (action is not null || route is not null)) return (null, route, action, controller, area, runtime);
            }
            return (null, null, null, null, null, true);
        }

        static string Inner(string text, Match open, string tag)
        {
            var close = text.IndexOf("</" + tag, open.Index + open.Length, StringComparison.OrdinalIgnoreCase);
            return close < 0 ? "" : text[(open.Index + open.Length)..close];
        }

        /// <summary>A fragment's text keys (<c>T("Account.Register.Button")</c>, <c>Localizer["…"]</c>) and its literal words.</summary>
        (IReadOnlyList<string> Keys, string Words) Words(string fragment)
        {
            var keys = new List<string>();
            foreach (Match x in ExprMark.Matches(fragment))
                keys.AddRange(TextKeys(model, exprs[int.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]));
            var words = Regex.Replace(ExprMark.Replace(Regex.Replace(fragment, "<[^>]*>", " "), " "), "\\s+", " ").Trim();
            return (keys.Distinct().ToList(), System.Net.WebUtility.HtmlDecode(words));
        }
    }

    /// <summary>Keys passed to a localizer: a call or index on something whose type is named <c>…Localizer</c>, with a constant key.</summary>
    static IEnumerable<string> TextKeys(SemanticModel model, SyntaxNode node)
    {
        foreach (var n in node.DescendantNodesAndSelf())
        {
            ExpressionSyntax? receiver = null;
            ArgumentSyntax? first = null;
            if (n is InvocationExpressionSyntax inv) (receiver, first) = (inv.Expression, inv.ArgumentList.Arguments.FirstOrDefault());
            else if (n is ElementAccessExpressionSyntax ea) (receiver, first) = (ea.Expression, ea.ArgumentList.Arguments.FirstOrDefault());
            if (receiver is null || first is null) continue;
            var type = model.GetTypeInfo(receiver).Type;
            if (type?.Name.Contains("Localizer", StringComparison.Ordinal) != true) continue;
            if (model.GetConstantValue(first.Expression) is { HasValue: true, Value: string key }) yield return key;
        }
    }
}
