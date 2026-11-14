using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// D171 + D173: which code is a system job, and which code queues one.
/// <list type="bullet">
/// <item>An <c>is-job</c> fact on each method a job runs: a class implementing a job interface (built in: Quartz's <c>IJob</c>,
/// Coravel's <c>IInvocable</c>; an app's own in <c>jobInterfaces</c>, e.g. Sonarr's <c>IExecute`1</c>, nopCommerce's
/// <c>IScheduleTask</c>), or a .NET <c>BackgroundService</c>'s <c>ExecuteAsync</c>. A generic interface names the command
/// type it runs (<c>command</c>). Hangfire's recurring registrations are added by <c>background-jobs</c>.</item>
/// <item>A <c>queue</c> fact where code hands a command to a method that takes any command (a parameter typed as the job
/// interface's constraint, <c>Push&lt;TCommand&gt;(TCommand) where TCommand : Command</c>) and makes it there:
/// <c>Push(new RefreshSeriesCommand(…))</c>. Linking draws a 🟡 <c>queues</c> line to the handler of that type. A command
/// whose type is made at runtime is ❓; one passed along from a parameter or a type parameter isn't this call's to say.</item>
/// </list>
/// </summary>
public sealed class JobsDetector : IDetector
{
    public string Name => "jobs";

    static readonly string[] BuiltIn = ["Quartz.IJob", "Coravel.Invocable.IInvocable"];
    const string HostedService = "Microsoft.Extensions.Hosting.BackgroundService";

    public void Extract(ExtractContext ctx)
    {
        var names = BuiltIn.Concat(ctx.Settings.GetValueOrDefault("jobInterfaces") ?? []).ToHashSet(StringComparer.Ordinal);
        // The command base of each generic job interface: its type parameter's constraint (IExecute<T> where T : Command).
        var bases = names.Select(n => ctx.Compilation.GetTypeByMetadataName(n)).OfType<INamedTypeSymbol>()
            .Where(t => t.IsGenericType).Select(t => t.TypeParameters[0].ConstraintTypes.OfType<INamedTypeSymbol>().FirstOrDefault())
            .OfType<INamedTypeSymbol>().Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();

        // A test hands commands to whatever it checks (a comparer, a converter): that isn't queuing work.
        var tests = ctx.Fragment.Nodes.Where(n => n.IsTest).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var file in ctx.Files)
        {
            var root = file.Tree.GetRoot();
            foreach (var decl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type || type.IsAbstract) continue;
                if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() != decl) continue;
                foreach (var iface in type.AllInterfaces)
                {
                    var name = ExtractContext.FullName(iface.OriginalDefinition);
                    if (!names.Contains(name)) continue;
                    var arg = iface.TypeArguments.FirstOrDefault();
                    if (arg is ITypeParameterSymbol)
                    {
                        ctx.AddBlindSpot(ctx.NodeId(type), decl, $"job {type.Name} runs a command type chosen by whoever uses it ({iface.Name}<{arg.Name}>)", Name);
                        continue;
                    }
                    foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl || ctx.NodeId(impl) is not { } runs) continue;
                        ctx.AddFact(Name, "is-job", runs, decl, ("interface", name), ("kind", arg is null ? "scheduled" : "command"),
                            ("command", arg is null ? "" : ctx.NodeId(arg) ?? ""), ("id", ""), ("schedule", ""));
                    }
                }
                if (ExtractContext.IsOrDerives(type.BaseType, HostedService))
                    for (var t = type; t is not null && ctx.InRepo(t); t = t.BaseType)
                        if (t.GetMembers("ExecuteAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsOverride) is { } run && ctx.NodeId(run) is { } runs)
                        {
                            ctx.AddFact(Name, "is-job", runs, decl, ("interface", HostedService), ("kind", "hosted"), ("command", ""), ("id", ""), ("schedule", "while the app runs"));
                            break;
                        }
            }
            if (bases.Count == 0) continue;
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                Queue(ctx, file, call, bases, tests);
        }
    }

    void Queue(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, List<INamedTypeSymbol> bases, HashSet<string> tests)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method || ctx.OwnerId(call, file.Model) is not { } owner || tests.Contains(owner)) return;
        var parameters = method.OriginalDefinition.Parameters;
        var args = call.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var p = arg.NameColon is { } nc ? parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.ValueText) : i < parameters.Length ? parameters[i] : parameters.LastOrDefault(x => x.IsParams);
            if (p is null || AnyCommand(p.Type, bases) is not { } commandBase) continue;
            var via = $"{method.ContainingType.Name}.{method.Name}";
            var made = arg.Expression.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>()
                .Select(c => file.Model.GetTypeInfo(c).Type).OfType<INamedTypeSymbol>()
                .Where(t => !t.IsAbstract && ExtractContext.IsOrDerives(t, ExtractContext.FullName(commandBase)))
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
            if (made.Count > 0)
            {
                foreach (var t in made)
                    if (ctx.NodeId(t) is { } typeId) ctx.AddFact(Name, "queue", owner, call, ("type", typeId), ("via", via), ("arg", Short(arg.Expression)));
                continue;
            }
            // Not made here: passed along from a parameter, field or property, or a type parameter's value, is the caller's to say.
            var symbol = file.Model.GetSymbolInfo(arg.Expression).Symbol;
            if (symbol is IParameterSymbol or IFieldSymbol or IPropertySymbol) continue;
            var type = Element(file.Model.GetTypeInfo(arg.Expression).Type);
            if (type is null or ITypeParameterSymbol) continue;
            if (type is INamedTypeSymbol named && !named.IsAbstract && !SymbolEqualityComparer.Default.Equals(named, commandBase) && ctx.NodeId(named) is { } id)
                ctx.AddFact(Name, "queue", owner, call, ("type", id), ("via", via), ("arg", Short(arg.Expression)));
            else
                ctx.AddBlindSpot(owner, call, $"queues work through {via} whose type is known only at runtime: {Short(arg.Expression)}", Name);
        }
    }

    /// <summary>The command base a parameter takes any command of: the base itself, a type parameter constrained to it, or a list of either.</summary>
    static INamedTypeSymbol? AnyCommand(ITypeSymbol type, List<INamedTypeSymbol> bases)
    {
        var element = Element(type) ?? type;
        foreach (var b in bases)
        {
            if (SymbolEqualityComparer.Default.Equals(element.OriginalDefinition, b)) return b;
            // The method's own type parameter (Push<TCommand>(TCommand)): a handler's Execute(TCommand) takes its class's, and runs now.
            if (element is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } tp
                && tp.ConstraintTypes.Any(c => ExtractContext.IsOrDerives(c, ExtractContext.FullName(b)))) return b;
        }
        return null;
    }

    /// <summary>A collection's element type (<c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, <c>T[]</c>), or the type itself.</summary>
    static ITypeSymbol? Element(ITypeSymbol? type) => type switch
    {
        IArrayTypeSymbol a => a.ElementType,
        INamedTypeSymbol { IsGenericType: true } n when n.SpecialType != SpecialType.System_Nullable_T
            && (n.Name is "IEnumerable" or "List" or "IList" or "ICollection" or "IReadOnlyList" or "IReadOnlyCollection") => n.TypeArguments[0],
        _ => type,
    };

    static string Short(SyntaxNode node)
    {
        var text = string.Join(' ', node.ToString().Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        return text.Length > 120 ? text[..117] + "…" : text;
    }

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var handlers = facts.Where(f => f.Kind == "is-job" && f["command"].Length > 0).GroupBy(f => f["command"])
            .ToDictionary(g => g.Key, g => g.Select(f => f.Owner).Distinct().Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        foreach (var q in facts.Where(f => f.Kind == "queue"))
        {
            if (handlers.GetValueOrDefault(q["type"]) is { } runs)
                foreach (var h in runs) ctx.AddEdge(q.Owner, h, EdgeKind.Queues, Label.Guessed, Name);
            else ctx.AddBlindSpot(q.Owner, q.File, q.Line, $"queues {q["type"][(q["type"].LastIndexOf('.') + 1)..]} through {q["via"]}, which no job the graph knows runs", Name);
        }
    }
}

/// <summary>
/// D174 + D176: the conditions around each call and the early exits of each method, read from C# syntax.
/// <list type="bullet">
/// <item><c>condition</c>: a call (to the repo's code, or to a framework's outside <c>System.*</c>) under at least one
/// <c>if</c>, <c>else</c>, <c>?:</c>, <c>switch</c> case or arm, <c>catch</c> or loop of its own member, or after a guard
/// (<c>unless</c>: an earlier <c>if</c> in the same block that always returns, throws, breaks or continues): those, innermost
/// first, each with its code quoted exactly; inside a function given to a cache's get-or-create call, <c>cache-miss</c>
/// (D215); after a <c>?.</c>, <c>set</c> (D216).</item>
/// <item><c>exit</c>: a return of the framework's own <c>BadRequest</c>, <c>NotFound</c>, <c>Unauthorized</c>, <c>Forbid</c>,
/// <c>Challenge</c>, <c>Redirect…</c>/<c>LocalRedirect…</c> or <c>StatusCode(4xx/5xx)</c> (a call or a <c>new …Result</c>), and
/// every <c>throw</c>, with its conditions. Anything else returned isn't an exit (D176).</item>
/// </list>
/// </summary>
public sealed class ConditionsDetector : IDetector
{
    public string Name => "conditions";

    public sealed record Condition(string Kind, string Text, int Line, string? Case = null);

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var node in file.Tree.GetRoot().DescendantNodes())
            {
                if (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax) Call(ctx, file, (ExpressionSyntax)node);
                if (node is ThrowStatementSyntax or ThrowExpressionSyntax) Throw(ctx, file, node);
                if (node is AssignmentExpressionSyntax set) FilterResult(ctx, file, set);
                if (node is TryStatementSyntax tryStatement && tryStatement.Catches.Count > 0) Try(ctx, file, tryStatement);
                if (node is IfStatementSyntax check && Rejects(check.Statement, file.Model) is { } message && ctx.OwnerId(check, file.Model) is { } owner)
                    ctx.AddFact(Name, "rejects", owner, check, ("condition", check.Condition.ToString()), ("message", message));
                // Step 6 (fresh check S21): a method returning a URL built from the current request's own path.
                if (node is MethodDeclarationSyntax m && m.Body is { } || node is MethodDeclarationSyntax { ExpressionBody: not null })
                {
                    var decl = (MethodDeclarationSyntax)node;
                    var returns = decl.ExpressionBody is { } eb ? [eb.Expression] : decl.DescendantNodes().OfType<ReturnStatementSyntax>().Select(r => r.Expression).OfType<ExpressionSyntax>().ToList();
                    if (returns.Any(r => ReadsLocal(r, decl, file.Model, 1)) && ctx.NodeId(file.Model.GetDeclaredSymbol(decl)) is { } id)
                        ctx.AddFact(Name, "request-url", id, decl);
                }
            }
    }

    /// <summary>
    /// D187 (ruling R5): a filter's <c>context.Result = …</c> stops the request before the action runs, so it's an exit there:
    /// the <c>Result</c> of an ASP.NET filter context (<c>Microsoft.AspNetCore.Mvc.Filters</c>), set to a result on D176's list or
    /// an app's helper making one (D186).
    /// </summary>
    void FilterResult(ExtractContext ctx, SourceFile file, AssignmentExpressionSyntax set)
    {
        if (set.Left is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } left
            || file.Model.GetSymbolInfo(left).Symbol is not IPropertySymbol prop
            || !Derives(prop.ContainingType, "Microsoft.AspNetCore.Mvc.Filters")
            || ctx.OwnerId(set, file.Model) is not { } owner) return;
        var value = set.Right;
        while (value is ParenthesizedExpressionSyntax or CastExpressionSyntax or AwaitExpressionSyntax)
            value = value switch { ParenthesizedExpressionSyntax p => p.Expression, CastExpressionSyntax c => c.Expression, AwaitExpressionSyntax a => a.Expression, _ => value };
        if (value is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax) || file.Model.GetSymbolInfo(value).Symbol is not IMethodSymbol method) return;
        if (Exit(method, value, file.Model) is var (kind, status))
        {
            ctx.AddFact(Name, "exit", owner, set, [("kind", kind), ("result", value is InvocationExpressionSyntax ? method.Name : method.ContainingType.Name),
                ("status", status), ("text", set.ToString()), ("exception", ""), ("conditions", Json(Around(set, ctx))), .. Target(kind, method, value, file.Model, ctx)]);
            return;
        }
        // D301: any other result set on a filter's context stops the request too (Kavita's `DisallowRoleAttribute` sets a
        // `ContentResult` with a 400): a stop answering its constant status when its initialiser sets one, else the result's type.
        var type = method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.ReturnType.Name;
        var code = value is BaseObjectCreationExpressionSyntax { Initializer: { } init }
            ? init.Expressions.OfType<AssignmentExpressionSyntax>().Where(a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "StatusCode" })
                .Select(a => file.Model.GetConstantValue(a.Right)).FirstOrDefault(c => c is { HasValue: true, Value: int })
            : default;
        var (k, st) = code is { HasValue: true, Value: int n } ? ("StatusCode", n.ToString(System.Globalization.CultureInfo.InvariantCulture)) : ("Result", type);
        ctx.AddFact(Name, "exit", owner, set, ("kind", k), ("result", type), ("status", st), ("text", set.ToString()), ("exception", ""), ("conditions", Json(Around(set, ctx))));
    }

    /// <summary>
    /// D300: a <c>try</c> with catches, on the member it's in: the lines its block covers (<c>from</c>, <c>to</c>) and each catch in
    /// order (<c>catches</c>, JSON): the type it takes (its full name and the names of the types it's built on are on the thrown
    /// side; here the full name, <c>*</c> for a bare catch, <c>?</c> when the compiler can't tell), its line, whether it has a
    /// <c>when</c> filter, and whether it throws anything (D304: the same error or a new one wrapping it; the error goes on).
    /// </summary>
    void Try(ExtractContext ctx, SourceFile file, TryStatementSyntax t)
    {
        if (ctx.OwnerId(t, file.Model) is not { } owner) return;
        var catches = new List<CatchInfo>();
        foreach (var c in t.Catches)
        {
            var type = c.Declaration is null ? "*"
                : file.Model.GetTypeInfo(c.Declaration.Type).Type is INamedTypeSymbol { TypeKind: not TypeKind.Error } named ? FullName(named) : "?";
            var variable = c.Declaration is { } d && d.Identifier.ValueText is { Length: > 0 } v ? v : null;
            // D304(1): any throw in the catch, a new wrapped error included, lets the error go on; a new one names what it becomes.
            var throws = c.Block.DescendantNodes(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).OfType<ThrowStatementSyntax>().ToList();
            var rethrows = throws.Count > 0;
            var becomes = throws.Select(x => x.Expression).OfType<ExpressionSyntax>()
                .Where(x => !(variable is not null && x is IdentifierNameSyntax id && id.Identifier.ValueText == variable))
                .Select(x => file.Model.GetTypeInfo(x).Type).FirstOrDefault(x => x is { TypeKind: not TypeKind.Error });
            catches.Add(new CatchInfo(type, ctx.Line(c), c.Filter is not null ? true : null, rethrows ? true : null,
                becomes is null ? null : TypeChain(becomes), rethrows ? null : Ends(c, t)));
        }
        var end = t.Block.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        ctx.AddFact(Name, "try", owner, t, ("from", ctx.Line(t.Block).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("to", end.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("catches", JsonSerializer.Serialize(catches, JsonOptions)));
    }

    public sealed record CatchInfo(string Type, int Line, bool? Filtered, bool? Rethrows, string? Becomes = null, string? Ends = null);

    /// <summary>
    /// D304(7): what a catch that doesn't throw returns: its own <c>return</c> (<c>new HealthCheck(…)</c>), or else the <c>return</c>
    /// right after the <c>try</c> it falls out to (<c>View(…)</c>); "" when it returns nothing, null when neither is there.
    /// </summary>
    static string? Ends(CatchClauseSyntax c, TryStatementSyntax t)
    {
        var own = c.Block.Statements.LastOrDefault() as ReturnStatementSyntax;
        if (own is not null) return Short(own.Expression);
        if (c.Block.Statements.LastOrDefault() is ContinueStatementSyntax or BreakStatementSyntax) return null;
        if (t.Finally is null && t.Parent is BlockSyntax block && block.Statements.IndexOf(t) is var i and >= 0 && i + 1 < block.Statements.Count
            && block.Statements[i + 1] is ReturnStatementSyntax next) return Short(next.Expression);
        return null;
    }

    static string Short(ExpressionSyntax? e)
    {
        while (e is AwaitExpressionSyntax or ParenthesizedExpressionSyntax or CastExpressionSyntax)
            e = e switch { AwaitExpressionSyntax a => a.Expression, ParenthesizedExpressionSyntax p => p.Expression, CastExpressionSyntax c => c.Expression, _ => e };
        return e switch
        {
            null => "",
            InvocationExpressionSyntax i => $"{(i.Expression is MemberAccessExpressionSyntax m ? m.Name.Identifier.ValueText : i.Expression is IdentifierNameSyntax n ? n.Identifier.ValueText : i.Expression.ToString())}(…)",
            ObjectCreationExpressionSyntax o => $"new {o.Type}(…)",
            _ => e.ToString().Length <= 40 ? e.ToString() : e.ToString()[..40] + "…",
        };
    }

    /// <summary>A type's full name without its assembly: <c>NzbDrone.Core.Indexers.Exceptions.IndexerException</c>.</summary>
    static string FullName(ITypeSymbol t) => t.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted).WithGenericsOptions(SymbolDisplayGenericsOptions.None).RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

    /// <summary>D300: a thrown type's full name and every type it's built on, nearest first: <c>A|B|System.Exception|System.Object</c>.</summary>
    static string TypeChain(ITypeSymbol? t)
    {
        var names = new List<string>();
        for (var x = t; x is not null && x.TypeKind != TypeKind.Error; x = x.BaseType) names.Add(FullName(x));
        return string.Join("|", names);
    }

    /// <summary>
    /// D294: a call that writes or deletes a file: .NET's <c>File</c> and <c>Directory</c> methods that write, copy, move or delete,
    /// the same names in <c>System.IO.Abstractions</c>, NetVips' <c>WriteToFile</c>, ImageSharp's <c>Save</c>/<c>SaveAs…</c> given a
    /// path, and <c>CopyTo(Async)</c> onto a <c>FileStream</c>. Null for anything else.
    /// </summary>
    internal static string? FileChange(IMethodSymbol method, ExpressionSyntax call, SemanticModel model)
    {
        var type = method.ContainingType;
        if (type is null) return null;
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        var name = method.Name;
        var bare = type.Name;
        if (ns == "System.IO.Abstractions")
        {
            if (bare.StartsWith('I') && bare.Length > 1 && char.IsUpper(bare[1])) bare = bare[1..];
            foreach (var suffix in (string[])["Base", "Wrapper"]) if (bare.EndsWith(suffix, StringComparison.Ordinal)) bare = bare[..^suffix.Length];
        }
        if (ns is "System.IO" or "System.IO.Abstractions")
        {
            if (bare == "File")
            {
                if (name == "Delete") return "deletes";
                if (FileWrites.Contains(name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name)) return "writes";
            }
            if (bare == "Directory")
            {
                if (name == "Delete") return "deletes";
                if (name is "CreateDirectory" or "Move") return "writes";
            }
            if (ns == "System.IO" && type.Name is "Stream" or "FileStream" or "MemoryStream" && name is "CopyTo" or "CopyToAsync" && OntoFile(call, model)) return "writes";
            return null;
        }
        if (ns.StartsWith("NetVips", StringComparison.Ordinal) && name == "WriteToFile") return "writes";
        if (ns.StartsWith("SixLabors.ImageSharp", StringComparison.Ordinal) && name.StartsWith("Save", StringComparison.Ordinal)
            && method.Parameters.FirstOrDefault()?.Type.SpecialType == SpecialType.System_String) return "writes";
        if (name is "CopyTo" or "CopyToAsync" && OntoFile(call, model)) return "writes";   // IFormFile.CopyToAsync(fileStream)
        return null;
    }

    static readonly HashSet<string> FileWrites = ["WriteAllText", "WriteAllBytes", "WriteAllLines", "AppendAllText", "AppendAllLines", "AppendAllBytes", "AppendText",
        "Create", "CreateText", "Copy", "Move", "Replace", "OpenWrite"];

    /// <summary>The call's first argument is a <c>FileStream</c> (a stream written to a file).</summary>
    static bool OntoFile(ExpressionSyntax call, SemanticModel model) =>
        call is InvocationExpressionSyntax { ArgumentList.Arguments: [var first, ..] } && model.GetTypeInfo(first.Expression).Type is { } t
        && (t.Name == "FileStream" || t.Name == "FileSystemStream" || t.BaseType?.Name == "FileSystemStream");

    /// <summary>
    /// D204: where a framework redirect sends the user, from the compiler's constant values: <c>route</c> (<c>RedirectToRoute</c>'s
    /// route name), <c>controller</c> and <c>action</c> (<c>RedirectToAction</c>, the controller defaulting to the one it's in), or
    /// <c>url</c> (<c>Redirect</c>, <c>LocalRedirect</c>). A value made at runtime is left out; not a framework redirect: nothing.
    /// </summary>
    static (string, string)[] Target(string kind, IMethodSymbol method, ExpressionSyntax call, SemanticModel model, ExtractContext ctx)
    {
        if (kind != "Redirect" || method.ContainingType?.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) != true) return [];
        var args = call is InvocationExpressionSyntax i ? i.ArgumentList.Arguments : ((BaseObjectCreationExpressionSyntax)call).ArgumentList?.Arguments ?? default;
        string? Arg(string name)
        {
            for (var n = 0; n < args.Count; n++)
            {
                var p = args[n].NameColon is { } nc ? method.Parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.Text) : n < method.Parameters.Length ? method.Parameters[n] : null;
                if (p?.Name == name) return model.GetConstantValue(args[n].Expression) is { HasValue: true, Value: string v } ? v : null;
            }
            return null;
        }
        var target = new List<(string, string)>();
        if (Arg("routeName") is { } route) target.Add(("route", route));
        if (method.Parameters.Any(p => p.Name == "actionName"))
        {
            var controller = Arg("controllerName");
            if (controller is null && !args.Any(a => a.NameColon?.Name.Identifier.Text == "controllerName")
                && model.GetEnclosingSymbol(call.SpanStart)?.ContainingType is { } own && own.Name.EndsWith("Controller", StringComparison.Ordinal)
                && method.Parameters.All(p => p.Name != "controllerName" || args.Count <= method.Parameters.IndexOf(p)))
                controller = own.Name[..^"Controller".Length];
            if (Arg("actionName") is { } action) target.Add(("action", action));
            if (controller is not null) target.Add(("controller", controller));
            // The area: the route values' `area`, else the one of the controller it's written in (MVC keeps it).
            var values = args.Select((a, n) => (a, p: a.NameColon is { } nc ? method.Parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.Text) : n < method.Parameters.Length ? method.Parameters[n] : null))
                .FirstOrDefault(x => x.p?.Name == "routeValues").a?.Expression as AnonymousObjectCreationExpressionSyntax;
            var given = values?.Initializers.FirstOrDefault(m => (m.NameEquals?.Name.Identifier.Text ?? (m.Expression as IdentifierNameSyntax)?.Identifier.Text) == "area");
            // Step 6 (fresh check S22): written outside a controller (a filter), no area is the request's own: any area fits.
            var enclosing = model.GetEnclosingSymbol(call.SpanStart)?.ContainingType;
            string? area = given is null ? (IsController(enclosing) ? AreaOf(enclosing) : null)
                : model.GetConstantValue(given.Expression) is { HasValue: true, Value: string a } ? a : null;
            if (area is not null) target.Add(("area", area));
        }
        if (Arg("url") is { } url) target.Add(("url", url));
        else if (args.Select((a, n) => (a, p: a.NameColon is { } nc ? method.Parameters.FirstOrDefault(x => x.Name == nc.Name.Identifier.Text) : n < method.Parameters.Length ? method.Parameters[n] : null))
                     .FirstOrDefault(x => x.p?.Name == "url").a is { } urlArg)
        {
            // Step 6 (fresh check S21): a URL built from the request's own path is the same page (an http ↔ https switch); one
            // a method returns is joined to that method's `request-url` fact at the walk.
            if (ReadsRequestUrl(urlArg.Expression, model, 2)) target.Add(("samePage", "1"));
            else if ((urlArg.Expression is AwaitExpressionSyntax aw ? aw.Expression : urlArg.Expression) is InvocationExpressionSyntax inv
                     && model.GetSymbolInfo(inv).Symbol is IMethodSymbol from && ctx.NodeId(ExtractContext.Normalize(from)) is { } fromId)
                target.Add(("urlFrom", fromId));
        }
        return [.. target];
    }

    static readonly HashSet<string> RequestUrlParts = ["Path", "PathBase", "QueryString"];
    static readonly HashSet<string> RequestUrlCalls = ["GetDisplayUrl", "GetEncodedUrl", "GetEncodedPathAndQuery", "GetUri"];

    /// <summary>Whether an expression is built from the current request's own URL: <c>Request.Path</c>, <c>GetDisplayUrl()</c>…,
    /// directly or in the returned value of an in-repo method it calls (two levels).</summary>
    static bool ReadsRequestUrl(ExpressionSyntax e, SemanticModel model, int depth)
    {
        foreach (var n in e.DescendantNodesAndSelf())
        {
            if (n is MemberAccessExpressionSyntax m && RequestUrlParts.Contains(m.Name.Identifier.ValueText)
                && model.GetTypeInfo(m.Expression).Type?.Name == "HttpRequest") return true;
            if (n is InvocationExpressionSyntax inv && model.GetSymbolInfo(inv).Symbol is IMethodSymbol called)
            {
                if (RequestUrlCalls.Contains(called.Name) && called.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true) return true;
                if (depth >= 2) continue;
                var target = called.IsAbstract || called.ContainingType?.TypeKind == TypeKind.Interface ? null : called;
                foreach (var r in (target ?? called).DeclaringSyntaxReferences)
                    if (r.GetSyntax() is BaseMethodDeclarationSyntax body && SameTree(model, body) is { } bodyModel
                        && body.DescendantNodes().OfType<ReturnStatementSyntax>().Any(ret => ret.Expression is not null && ReadsLocal(ret.Expression, body, bodyModel, depth + 1)))
                        return true;
            }
        }
        return false;
    }

    /// <summary>A returned value, and what its locals were assigned in the same method.</summary>
    static bool ReadsLocal(ExpressionSyntax ret, BaseMethodDeclarationSyntax body, SemanticModel model, int depth)
    {
        if (ReadsRequestUrl(ret, model, depth)) return true;
        if (model.GetSymbolInfo(ret).Symbol is ILocalSymbol local)
            return body.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(a => model.GetSymbolInfo(a.Left).Symbol is ILocalSymbol l && SymbolEqualityComparer.Default.Equals(l, local)).Any(a => ReadsRequestUrl(a.Right, model, depth))
                || body.DescendantNodes().OfType<VariableDeclaratorSyntax>().Any(v => v.Initializer is { } init && SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(v), local) && ReadsRequestUrl(init.Value, model, depth));
        return false;
    }

    static SemanticModel? SameTree(SemanticModel model, SyntaxNode node) =>
        model.Compilation.ContainsSyntaxTree(node.SyntaxTree) ? model.Compilation.GetSemanticModel(node.SyntaxTree) : null;

    /// <summary>A controller's MVC area: its (or a base class's) <c>[Area("…")]</c>, else "".</summary>
    static string AreaOf(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "AreaAttribute" or "Area") is { } attr
                && attr.ConstructorArguments is [{ Value: string area }]) return area;
        return "";
    }

    /// <summary>An MVC controller: named …Controller, or deriving from ASP.NET Core's ControllerBase.</summary>
    static bool IsController(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.Name.EndsWith("Controller", StringComparison.Ordinal) || t.Name == "ControllerBase") return true;
        return false;
    }

    static bool Derives(INamedTypeSymbol? type, string ns)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.ContainingNamespace?.ToDisplayString() == ns) return true;
        return false;
    }

    void Call(ExtractContext ctx, SourceFile file, ExpressionSyntax call)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method || ctx.OwnerId(call, file.Model) is not { } owner) return;
        var target = ctx.NodeId(method);
        var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
        var changesFile = false;
        if (target is null && FileChange(method, call, file.Model) is { } change)
        {
            ctx.AddFact(Name, "file-change", owner, call, ("change", change), ("method", $"{method.ContainingType.Name}.{method.Name}"));
            changesFile = true;
        }
        if (Returned(call) && Exit(method, call, file.Model) is var (kind, status))
            ctx.AddFact(Name, "exit", owner, call, [("kind", kind), ("result", call is InvocationExpressionSyntax ? method.Name : method.ContainingType.Name),
                ("status", status), ("text", call.ToString()), ("exception", ""), ("conditions", Json(Around(call, ctx))), .. Target(kind, method, call, file.Model, ctx)]);
        // D304(2): a .NET file write or delete keeps its condition like any step, so it isn't skipped with the other System.* calls.
        if (target is null && !changesFile && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) && !ns.StartsWith("System.Net.Http", StringComparison.Ordinal)) return;
        var conditions = Around(call, ctx);
        if (conditions.Count == 0) return;
        ctx.AddFact(Name, "condition", owner, call, ("call", $"{method.ContainingType.Name}.{(method.MethodKind == MethodKind.Constructor ? "new" : method.Name)}"),
            ("target", target ?? ""), ("conditions", Json(conditions)));
    }

    void Throw(ExtractContext ctx, SourceFile file, SyntaxNode node)
    {
        if (ctx.OwnerId(node, file.Model) is not { } owner) return;
        var thrown = node is ThrowStatementSyntax s ? s.Expression : ((ThrowExpressionSyntax)node).Expression;
        var exception = thrown is null ? "" : file.Model.GetTypeInfo(thrown).Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "";
        var conditions = Around(node, ctx);
        // `x ?? throw …`: only when x is null.
        if (node.Parent is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.CoalesceExpression) && b.Right == node)
            conditions.Insert(0, new Condition("null", b.Left.ToString(), ctx.Line(b)));
        ctx.AddFact(Name, "exit", owner, node, ("kind", "throw"), ("result", "throw"), ("status", ""), ("text", node.ToString()),
            ("exception", exception), ("exceptionTypes", thrown is null ? "" : TypeChain(file.Model.GetTypeInfo(thrown).Type)), ("conditions", Json(conditions)));
    }

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    static string Json(List<Condition> conditions) => JsonSerializer.Serialize(conditions, JsonOptions);

    /// <summary>The framework's own refusal or redirect result this call makes, by D176's fixed list; null for anything else.</summary>
    static (string Kind, string Status)? Exit(IMethodSymbol method, ExpressionSyntax call, SemanticModel model)
    {
        var type = method.ContainingType;
        if (type?.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) != true)
            return call is InvocationExpressionSyntax ? Helpers.GetValue(method.OriginalDefinition, m => new HelperBox(HelperExit(m, model, 0))).Value : null;
        var name = call is InvocationExpressionSyntax ? method.Name : type.Name;
        if (call is BaseObjectCreationExpressionSyntax && !name.EndsWith("Result", StringComparison.Ordinal)) return null;
        string? kind = null;
        foreach (var k in (string[])["BadRequest", "NotFound", "Unauthorized", "Forbid", "Challenge", "Redirect", "LocalRedirect"])
            if (name.StartsWith(k, StringComparison.Ordinal)) kind = k == "LocalRedirect" ? "Redirect" : k;
        if (kind is not null) return (kind, "");
        if (!name.StartsWith("StatusCode", StringComparison.Ordinal)) return null;
        var args = call is InvocationExpressionSyntax i ? i.ArgumentList.Arguments : ((BaseObjectCreationExpressionSyntax)call).ArgumentList?.Arguments ?? default;
        return args.Count > 0 && model.GetConstantValue(args[0].Expression) is { HasValue: true, Value: int code } && code >= 400 ? ("StatusCode", code.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
    }

    /// <summary>
    /// D186 (ruling R4): an app's own method that ends the request itself is an exit where it's returned. Its body sets the
    /// response's status to a constant 4xx/5xx (<c>Response.StatusCode = 404</c>) and returns, or every value it returns is a
    /// result on D176's list (through such helpers, two deep). Read from its source; a method with none isn't one.
    /// </summary>
    sealed record HelperBox((string Kind, string Status)? Value);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IMethodSymbol, HelperBox> Helpers = new();

    static (string Kind, string Status)? HelperExit(IMethodSymbol method, SemanticModel model, int depth)
    {
        if (depth > 2 || method.DeclaringSyntaxReferences is not [var reference] || reference.GetSyntax() is not MethodDeclarationSyntax decl) return null;
        if (!model.Compilation.ContainsSyntaxTree(decl.SyntaxTree)) return null;
        var own = model.Compilation.GetSemanticModel(decl.SyntaxTree);
        var body = (SyntaxNode?)decl.Body ?? decl.ExpressionBody;
        if (body is null) return null;
        foreach (var set in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            if (set.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "StatusCode" } left
                && own.GetSymbolInfo(left).Symbol is IPropertySymbol { ContainingType.Name: "HttpResponse" }
                && own.GetConstantValue(set.Right) is { HasValue: true, Value: int code } && code >= 400)
                return ("StatusCode", code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var returns = decl.ExpressionBody is { } arrow ? [arrow.Expression]
            : body.DescendantNodes(n => n is not (LocalFunctionStatementSyntax or LambdaExpressionSyntax)).OfType<ReturnStatementSyntax>().Select(r => r.Expression).ToList();
        if (returns.Count == 0 || returns.Any(r => r is null)) return null;
        (string Kind, string Status)? kind = null;
        var values = new List<ExpressionSyntax>();
        void Arms(ExpressionSyntax x)
        {
            while (x is ParenthesizedExpressionSyntax p) x = p.Expression;
            if (x is ConditionalExpressionSyntax c) { Arms(c.WhenTrue); Arms(c.WhenFalse); } else values.Add(x);
        }
        foreach (var r in returns) Arms(r!);
        foreach (var r in values)
        {
            var e = r;
            while (e is AwaitExpressionSyntax or ParenthesizedExpressionSyntax or CastExpressionSyntax)
                e = e switch { AwaitExpressionSyntax a => a.Expression, ParenthesizedExpressionSyntax p => p.Expression, CastExpressionSyntax c => c.Expression, _ => e };
            if (e is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax) || own.GetSymbolInfo(e).Symbol is not IMethodSymbol m) return null;
            var inner = m.ContainingType?.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true
                ? Exit(m, e, own) : e is InvocationExpressionSyntax ? HelperExit(m, own, depth + 1) : null;
            if (inner is null || kind is not null && kind != inner) return null;
            kind = inner;
        }
        return kind;
    }

    /// <summary>The value the member returns: under <c>return</c>, an expression body, or a lambda's body, through parentheses, <c>await</c>, casts and <c>?:</c>.</summary>
    static bool Returned(SyntaxNode node)
    {
        var e = node;
        while (e.Parent is ParenthesizedExpressionSyntax or AwaitExpressionSyntax or CastExpressionSyntax
               || e.Parent is ConditionalExpressionSyntax c && (c.WhenTrue == e || c.WhenFalse == e))
            e = e.Parent!;
        return e.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax || e.Parent is LambdaExpressionSyntax l && l.Body == e;
    }

    /// <summary>The conditions of its own member around a node, innermost first, quoted exactly.</summary>
    public static List<Condition> Around(SyntaxNode node, ExtractContext ctx)
    {
        var list = new List<Condition>();
        var child = node;
        for (var p = node.Parent; p is not null; child = p, p = p.Parent)
        {
            if (p is MemberDeclarationSyntax or AccessorDeclarationSyntax) break;
            switch (p)
            {
                case IfStatementSyntax i when child == i.Statement:
                    list.Add(new("if", i.Condition.ToString(), ctx.Line(i))); break;
                case IfStatementSyntax i when child == i.Else:
                    list.Add(new("else", i.Condition.ToString(), ctx.Line(i))); break;
                case ConditionalExpressionSyntax c when child == c.WhenTrue:
                    list.Add(new("if", c.Condition.ToString(), ctx.Line(c))); break;
                case ConditionalExpressionSyntax c when child == c.WhenFalse:
                    list.Add(new("else", c.Condition.ToString(), ctx.Line(c))); break;
                case SwitchSectionSyntax s when child is StatementSyntax inCase && s.Statements.Contains(inCase) && s.Parent is SwitchStatementSyntax sw:
                    // Z1: a case's statements are a block too: its guards first, then the case.
                    Guards(s.Statements, inCase, list, ctx);
                    list.Add(new("case", sw.Expression.ToString(), ctx.Line(s), string.Join(" | ", s.Labels.Select(Label)))); break;
                case SwitchExpressionArmSyntax arm when child == arm.Expression && arm.Parent is SwitchExpressionSyntax se:
                    list.Add(new("case", se.GoverningExpression.ToString(), ctx.Line(arm), arm.Pattern + (arm.WhenClause is { } w ? " " + w : ""))); break;
                case CatchClauseSyntax k when child == k.Block:
                    list.Add(new("catch", string.Join(" ", new[] { k.Declaration?.ToString(), k.Filter?.ToString() }.Where(x => !string.IsNullOrEmpty(x))), ctx.Line(k))); break;
                case CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax when child == Body(p):
                    list.Add(new("loop", Header(p), ctx.Line(p))); break;
                case DoStatementSyntax d when child == d.Statement:
                    list.Add(new("loop", $"do … while ({d.Condition})", ctx.Line(d))); break;
                case BlockSyntax block when child is StatementSyntax stmt:
                    Guards(block.Statements, stmt, list, ctx);
                    break;
                case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } } arg
                    when child == arg.Expression && child is AnonymousFunctionExpressionSyntax && CacheMiss(inv, ctx):
                    list.Add(new("cache-miss", inv.Expression.ToString(), ctx.Line(inv))); break;
                case ConditionalAccessExpressionSyntax ca when child == ca.WhenNotNull && child is not ConditionalAccessExpressionSyntax:
                    // D216: what comes after `?.` runs only when what's before it is set; the nearest `?.` quotes the chain up to it.
                    var top = ca;
                    while (top.Parent is ConditionalAccessExpressionSyntax up && up.WhenNotNull == top) top = up;
                    list.Add(new("set", ca.SyntaxTree.GetText().ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(top.SpanStart, ca.Expression.Span.End)), ctx.Line(top))); break;
            }
        }
        return list;
    }

    /// <summary>
    /// D215: a get-or-create call runs the function it's given only when nothing is stored for the key. Read by the method's
    /// name and the function argument, since the types differ in every repo and library: a <c>GetOr…</c> name
    /// (<c>GetOrAdd</c>, <c>GetOrCreateAsync</c>, <c>GetOrFetchAsync</c>; <c>GetOrder</c> isn't one) or
    /// <c>AddOrGetExisting</c> says it whatever the receiver; a plain <c>Get</c>/<c>GetAsync</c> says it only when the method's
    /// type (or the receiver's) names a cache (<c>ICached</c>, <c>IStaticCacheManager</c>, <c>IEasyCachingProvider</c>).
    /// </summary>
    static bool CacheMiss(InvocationExpressionSyntax inv, ExtractContext ctx)
    {
        var name = inv.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
            SimpleNameSyntax n => n.Identifier.ValueText,
            _ => "",
        };
        if (name == "AddOrGetExisting" || name.Length > 5 && name.StartsWith("GetOr", StringComparison.Ordinal) && char.IsUpper(name[5])) return true;
        if (name is not ("Get" or "GetAsync")) return false;
        var model = ctx.Compilation.GetSemanticModel(inv.SyntaxTree);
        static bool Cache(ITypeSymbol? t) => t is not null && t.Name.Contains("cach", StringComparison.OrdinalIgnoreCase);
        var method = model.GetSymbolInfo(inv).Symbol as IMethodSymbol ?? model.GetSymbolInfo(inv).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        var receiver = inv.Expression is MemberAccessExpressionSyntax ma ? model.GetTypeInfo(ma.Expression).Type : null;
        return Cache(method?.ContainingType) || Cache(receiver);
    }

    /// <summary>X1: an earlier guard among the same statements (`if (x) return …;`, or a branch of an `if … else if` chain that leaves, D198) means this runs only unless x; nearest first.</summary>
    static SyntaxList<StatementSyntax>? Inner(StatementSyntax s) => s switch
    {
        LockStatementSyntax { Statement: BlockSyntax { Statements.Count: > 0 } b } => b.Statements,
        UsingStatementSyntax { Statement: BlockSyntax { Statements.Count: > 0 } b } => b.Statements,
        BlockSyntax { Statements.Count: > 0 } b => b.Statements,
        _ => null,
    };

    static void Guards(SyntaxList<StatementSyntax> statements, StatementSyntax stmt, List<Condition> list, ExtractContext ctx, bool includeLast = false)
    {
        if (includeLast)
        {
            // Read the block's last statement as one before `stmt` too.
            var last = statements[^1];
            if (Inner(last) is { } deeper) Guards(deeper, deeper[^1], list, ctx, includeLast: true);
            else if (last is IfStatementSyntax lastIf)
            {
                var chain = new List<IfStatementSyntax>();
                for (var x = lastIf; x is not null; x = x.Else?.Statement as IfStatementSyntax) chain.Add(x);
                if (chain[^1].Else is null)
                    for (var j = chain.Count - 1; j >= 0; j--)
                        if (Exits(chain[j].Statement)) list.Add(new("unless", chain[j].Condition.ToString(), ctx.Line(chain[j])));
            }
        }
        for (var i = statements.IndexOf(stmt) - 1; i >= 0; i--)
        {
            // D201: a guard inside a block that always runs before this (`lock (x) { if (a) return; … }`, `using`, a bare
            // block) leaves the method too: its guards hold here, the block's own statements nearest first.
            if (Inner(statements[i]) is { } inner)
            {
                Guards(inner, inner[^1], list, ctx, includeLast: true);
                continue;
            }
            if (statements[i] is not IfStatementSyntax first) continue;
            // D198: each branch of an `if … else if …` chain that always leaves is a guard too; a chain ending in an `else`
            // isn't (some branch always runs). Later branches are nearer.
            var chain = new List<IfStatementSyntax>();
            for (var x = first; x is not null; x = x.Else?.Statement as IfStatementSyntax) chain.Add(x);
            if (chain[^1].Else is not null) continue;
            for (var j = chain.Count - 1; j >= 0; j--)
                if (Exits(chain[j].Statement)) list.Add(new("unless", chain[j].Condition.ToString(), ctx.Line(chain[j])));
        }
    }

    /// <summary>
    /// D203: an <c>if</c> whose branch only reports a problem and leaves rejects the input. Its last statement is <c>throw new X(…)</c>
    /// (not an <c>Argument…Exception</c>, which guards against the caller's bug), or a <c>return</c> of an error result (D176's
    /// <c>BadRequest</c>, <c>NotFound</c>, a 4xx status, or an object setting <c>success = false</c>), or a <c>return</c> after
    /// <c>….Add…(…)</c> calls (<c>result.AddError(…); return result;</c>, <c>ModelState.AddModelError(…); return View(model);</c>).
    /// Before it, only such <c>Add…</c> calls and local declarations (the message being made). The message is the branch's first
    /// string literal, or "". Anything else: null.
    /// </summary>
    static string? Rejects(StatementSyntax branch, SemanticModel model)
    {
        var statements = branch is BlockSyntax b ? [.. b.Statements] : new List<StatementSyntax> { branch };
        if (statements.Count == 0) return null;
        static bool Adds(StatementSyntax x) => x is ExpressionStatementSyntax { Expression: var e }
            && (e is AwaitExpressionSyntax a ? a.Expression : e) is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax m }
            && m.Name.Identifier.Text.StartsWith("Add", StringComparison.Ordinal);
        var before = statements.SkipLast(1).ToList();
        if (!before.All(x => Adds(x) || x is LocalDeclarationStatementSyntax)) return null;
        var rejects = statements[^1] switch
        {
            ThrowStatementSyntax { Expression: ObjectCreationExpressionSyntax o } => !Short(o.Type).StartsWith("Argument", StringComparison.Ordinal),
            ReturnStatementSyntax r => before.Any(Adds) || r.Expression is { } e && ErrorResult(e, model),
            _ => false,
        };
        if (!rejects) return null;
        var literal = branch.DescendantNodes().OfType<LiteralExpressionSyntax>().FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));
        return literal?.Token.ValueText ?? "";
    }

    /// <summary>D203: a returned value that says the request failed: D176's <c>BadRequest</c>/<c>NotFound</c>/4xx, or an object setting <c>success = false</c>.</summary>
    static bool ErrorResult(ExpressionSyntax e, SemanticModel model)
    {
        while (e is AwaitExpressionSyntax or ParenthesizedExpressionSyntax or CastExpressionSyntax)
            e = e switch { AwaitExpressionSyntax a => a.Expression, ParenthesizedExpressionSyntax p => p.Expression, CastExpressionSyntax c => c.Expression, _ => e };
        if (e is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax && model.GetSymbolInfo(e).Symbol is IMethodSymbol m
            && Exit(m, e, model) is var (kind, status) && (kind is "BadRequest" or "NotFound" || kind == "StatusCode" && status.StartsWith('4')))
            return true;
        return e.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any(a => a.Parent is InitializerExpressionSyntax or AnonymousObjectMemberDeclaratorSyntax
                   && a.Left is IdentifierNameSyntax { Identifier.Text: var n } && n.Equals("success", StringComparison.OrdinalIgnoreCase) && a.Right.IsKind(SyntaxKind.FalseLiteralExpression))
            || e.DescendantNodesAndSelf().OfType<AnonymousObjectMemberDeclaratorSyntax>().Any(a => a.NameEquals?.Name.Identifier.Text.Equals("success", StringComparison.OrdinalIgnoreCase) == true
                   && a.Expression.IsKind(SyntaxKind.FalseLiteralExpression));
    }

    static string Short(TypeSyntax t) => t switch { QualifiedNameSyntax q => q.Right.Identifier.Text, SimpleNameSyntax n => n.Identifier.Text, _ => t.ToString() };

    /// <summary>A statement that always leaves: <c>return</c>, <c>throw</c>, <c>break</c>, <c>continue</c>, or a block ending in one.</summary>
    static bool Exits(StatementSyntax s) => s switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax => true,
        YieldStatementSyntax y => y.IsKind(SyntaxKind.YieldBreakStatement),
        BlockSyntax { Statements: [.., var last] } => Exits(last),
        _ => false,
    };

    static string Label(SwitchLabelSyntax l) => l switch
    {
        CaseSwitchLabelSyntax c => c.Value.ToString(),
        CasePatternSwitchLabelSyntax c => c.Pattern + (c.WhenClause is { } w ? " " + w : ""),
        _ => "default",
    };

    static StatementSyntax? Body(SyntaxNode loop) => loop switch
    {
        CommonForEachStatementSyntax f => f.Statement,
        ForStatementSyntax f => f.Statement,
        WhileStatementSyntax w => w.Statement,
        _ => null,
    };

    /// <summary>A loop's header as written: <c>foreach (var s in series)</c>.</summary>
    static string Header(SyntaxNode loop)
    {
        var body = Body(loop)!;
        var text = loop.SyntaxTree.GetText().ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(loop.SpanStart, body.SpanStart));
        return text.Trim();
    }

    public void Link(LinkContext ctx) { }
}

/// <summary>
/// D171: an MVC action that serves GET (or any verb) and returns a view runs as its page is opened: an <c>on-load</c> fact on the
/// action, found while linking from the routes' <c>mvc-action</c> facts and <c>razor-views</c>' <c>View(…)</c> calls. The
/// frontends' own on-load facts (React effects, Angular's <c>ngOnInit</c>, route resolvers) come from the TypeScript extractor.
/// </summary>
public sealed class OnLoadDetector : IDetector
{
    public string Name => "on-load";

    public void Extract(ExtractContext ctx) { }

    public void Link(LinkContext ctx)
    {
        var actions = ctx.Facts("aspnetcore-routes").Where(f => f.Kind == "mvc-action").GroupBy(f => f.Owner)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        // Y1: a view chosen at runtime is still a page opening; viewFrom says how the view is known.
        foreach (var r in ctx.Facts("razor-views").Where(f => f.Kind == "runtime-view" || f.Kind == "renders-view" && f["partial"] != "yes" && f["controller"].Length > 0)
                     .OrderBy(f => f.Owner, StringComparer.Ordinal).ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line))
        {
            if (!actions.TryGetValue(r.Owner, out var action)) continue;
            var verbs = action["verbs"];
            if (verbs.Length > 0 && !verbs.Split(',').Contains("GET")) continue;
            var viewFrom = r.Kind == "runtime-view" ? "runtime" : r["name"].Length > 0 ? "name" : "action";
            ctx.AddFact(Name, "on-load", r.Owner, r.File, r.Line, ("framework", "mvc"), ("hook", r["via"].Length > 0 ? r["via"] : "View"),
                ("handler", r.Owner), ("deps", ""), ("runs", "when the page is requested"),
                ("view", viewFrom == "action" ? action["action"] : r["name"]), ("viewFrom", viewFrom), ("verbs", verbs.Length > 0 ? verbs : "any"));
        }
    }
}
