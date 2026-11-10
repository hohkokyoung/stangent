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
/// first, each with its code quoted exactly.</item>
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
        if (Exit(method, value, file.Model) is not var (kind, status)) return;
        ctx.AddFact(Name, "exit", owner, set, ("kind", kind), ("result", value is InvocationExpressionSyntax ? method.Name : method.ContainingType.Name),
            ("status", status), ("text", set.ToString()), ("exception", ""), ("conditions", Json(Around(set, ctx))));
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
        if (Returned(call) && Exit(method, call, file.Model) is var (kind, status))
            ctx.AddFact(Name, "exit", owner, call, ("kind", kind), ("result", call is InvocationExpressionSyntax ? method.Name : method.ContainingType.Name),
                ("status", status), ("text", call.ToString()), ("exception", ""), ("conditions", Json(Around(call, ctx))));
        if (target is null && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) && !ns.StartsWith("System.Net.Http", StringComparison.Ordinal)) return;
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
            ("exception", exception), ("conditions", Json(conditions)));
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
            }
        }
        return list;
    }

    /// <summary>X1: an earlier guard among the same statements (`if (x) return …;`, or a branch of an `if … else if` chain that leaves, D198) means this runs only unless x; nearest first.</summary>
    static void Guards(SyntaxList<StatementSyntax> statements, StatementSyntax stmt, List<Condition> list, ExtractContext ctx)
    {
        for (var i = statements.IndexOf(stmt) - 1; i >= 0; i--)
        {
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
