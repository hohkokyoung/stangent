using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// G9: constants' values as facts (<c>value</c>) on the field: <c>const</c> fields, and <c>static readonly</c> fields whose
/// initializer the compiler or the string evaluator knows. Enums aren't here (they have their own reader, C.2).
/// </summary>
public sealed class ConstantsDetector : IDetector
{
    public string Name => "constants";

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var declarator in file.Tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Parent?.Parent is not FieldDeclarationSyntax) continue;
                if (file.Model.GetDeclaredSymbol(declarator) is not IFieldSymbol field || field.ContainingType.TypeKind == TypeKind.Enum) continue;
                if (ctx.NodeId(field) is not { } fieldId) continue;
                string? value = null;
                if (field.IsConst && field.HasConstantValue) value = Text(field.ConstantValue);
                else if (field.IsStatic && field.IsReadOnly && declarator.Initializer?.Value is { } init)
                    value = file.Model.GetConstantValue(init) is { HasValue: true } c ? Text(c.Value) : ExtractContext.StringValue(init, file.Model);
                if (value is null || value.Contains("{*}", StringComparison.Ordinal)) continue;
                ctx.AddFact(Name, "value", fieldId, declarator, ("value", value), ("type", field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
                    ("kind", field.IsConst ? "const" : "static readonly"));
            }
    }

    static string? Text(object? v) => v switch { null => "null", bool b => b ? "true" : "false", _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) };

    public void Link(LinkContext ctx) { }
}

/// <summary>
/// G10: SignalR messages a server sends, as points <c>message:signalr|&lt;name&gt;</c> with a <c>sends</c> line from each
/// method that sends one; the TypeScript extractor's <c>signalr-on</c> facts become <c>listens</c> lines from frontend code.
/// Sent directly (<c>Clients.All.SendAsync("receiveMessage", …)</c>) or through an app's own wrapper whose method-name
/// parameter reaches <c>SendAsync</c> (Kavita's <c>IEventHub.SendMessageAsync(MessageFactory.ScanSeries, …)</c>). A name
/// known only at runtime is ❓. Neither line is walked by impact.
/// </summary>
public sealed class SignalRDetector : IDetector
{
    public string Name => "signalr";
    const string Frontend = "frontend-http";

    /// <summary>Only calls to methods with these words are looked at as possible wrapper calls; the wrapper match decides.</summary>
    static readonly string[] WrapperWords = ["Send", "Publish", "Broadcast", "Notify", "Emit", "Message", "Push"];

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var call in file.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method || ctx.OwnerId(call, file.Model) is not { } owner) continue;
                var args = call.ArgumentList.Arguments;
                if (method.Name is "SendAsync" or "SendCoreAsync" && IsClientProxy(method.ReducedFrom is not null ? method.ReceiverType : method.ContainingType) && args.Count > 0)
                {
                    var first = args[0].Expression;
                    if (ExtractContext.StringValue(first, file.Model) is { } name && !name.Contains("{*}", StringComparison.Ordinal))
                        ctx.AddFact(Name, "send", owner, call, ("name", name));
                    else if (first is IdentifierNameSyntax id && file.Model.GetSymbolInfo(id).Symbol is IParameterSymbol p
                             && p.ContainingSymbol is IMethodSymbol wrapper && ctx.NodeId(wrapper) is { } wrapperId)
                        ctx.AddFact(Name, "wrapper", wrapperId, call, ("index", p.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    else ctx.AddBlindSpot(owner, call, "SignalR message name built at runtime", Name);
                    continue;
                }
                if (!WrapperWords.Any(w => method.Name.Contains(w, StringComparison.Ordinal)) || ctx.NodeId(method.OriginalDefinition) is not { } targetId) continue;
                for (var i = 0; i < args.Count && i < method.Parameters.Length; i++)
                    if (method.Parameters[i].Type.SpecialType == SpecialType.System_String
                        && ExtractContext.StringValue(args[i].Expression, file.Model) is { } value && !value.Contains("{*}", StringComparison.Ordinal))
                        ctx.AddFact(Name, "send-arg", owner, call, ("target", targetId), ("index", i.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("value", value));
            }
    }

    static bool IsClientProxy(ITypeSymbol? type) =>
        type is not null && (type.Name is "IClientProxy" or "ISingleClientProxy" || type.AllInterfaces.Any(i => i.Name is "IClientProxy" or "ISingleClientProxy"));

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var wrappers = facts.Where(f => f.Kind == "wrapper").Select(f => (f.Owner, f["index"])).ToHashSet();
        // An interface member's call counts when the member dispatches to a wrapper (IEventHub → EventHub).
        var dispatch = ctx.Graph.AllEdges.Where(e => e.Kind == EdgeKind.DispatchesTo).ToLookup(e => e.Source, e => e.Target, StringComparer.Ordinal);
        bool IsWrapper(string target, string index) => wrappers.Contains((target, index)) || dispatch[target].Any(t => wrappers.Contains((t, index)));

        var sends = facts.Where(f => f.Kind == "send").Select(f => (f.Owner, Name: f["name"]))
            .Concat(facts.Where(f => f.Kind == "send-arg" && IsWrapper(f["target"], f["index"])).Select(f => (f.Owner, Name: f["value"])));
        foreach (var (owner, name) in sends.Distinct().OrderBy(s => s.Owner, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal))
            ctx.AddEdge(owner, Message(ctx, name), EdgeKind.Sends, Label.Guessed, Name);

        foreach (var on in ctx.Facts(Frontend).Where(f => f.Kind == "signalr-on").OrderBy(f => f.Owner, StringComparer.Ordinal))
            if (on["name"].Length > 0) ctx.AddEdge(on.Owner, Message(ctx, on["name"]), EdgeKind.Listens, Label.Guessed, Name);
            else ctx.AddBlindSpot(on.Owner, on.File, on.Line, "SignalR listener for a message name known only at runtime", Name);
    }

    static string Message(LinkContext ctx, string name)
    {
        var id = $"message:signalr|{name}";
        if (!ctx.HasNode(id)) ctx.AddNode(new Node(id, NodeKind.Message, name));
        return id;
    }
}

/// <summary>
/// G11: work queued to run later (Hangfire <c>BackgroundJob.Enqueue/Schedule</c>, <c>RecurringJob.AddOrUpdate</c>,
/// <c>IBackgroundJobClient</c>), as a <c>job</c> fact on the method that queues it: the method the job runs, how it's queued,
/// and a recurring job's id and schedule. The plain <c>calls</c> line to that method stays; the fact says it doesn't run now.
/// </summary>
public sealed class BackgroundJobsDetector : IDetector
{
    public string Name => "background-jobs";

    static readonly HashSet<string> Methods = ["Enqueue", "Schedule", "AddOrUpdate", "ContinueJobWith", "ContinueWith", "Requeue"];

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var call in file.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (ExtractContext.InvokedName(call) is not { } name || !Methods.Contains(name)) continue;
                if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method
                    || method.ContainingNamespace?.ToDisplayString().StartsWith("Hangfire", StringComparison.Ordinal) != true) continue;
                if (ctx.OwnerId(call, file.Model) is not { } owner) continue;
                var lambda = call.ArgumentList.Arguments.Select(a => a.Expression).OfType<LambdaExpressionSyntax>().FirstOrDefault();
                var body = lambda?.ExpressionBody is AwaitExpressionSyntax aw ? aw.Expression : lambda?.ExpressionBody;
                var target = body is InvocationExpressionSyntax inner && file.Model.GetSymbolInfo(inner).Symbol is IMethodSymbol runs ? ctx.NodeId(runs.OriginalDefinition) : null;
                if (target is null) { ctx.AddBlindSpot(owner, call, $"background job ({name}) whose method isn't followed", Name); continue; }
                var recurring = name == "AddOrUpdate";
                var strings = call.ArgumentList.Arguments.Select(a => ExtractContext.StringValue(a.Expression, file.Model)).ToList();
                ctx.AddFact(Name, "job", owner, call, ("runs", target), ("via", $"{method.ContainingType.Name}.{name}"),
                    ("id", recurring ? strings.FirstOrDefault() ?? "" : ""),
                    ("schedule", recurring ? strings.Skip(1).FirstOrDefault(s => s is not null) ?? "" : name == "Schedule" ? "delayed" : "now, in the background"));
            }
    }

    public void Link(LinkContext ctx) { }
}
