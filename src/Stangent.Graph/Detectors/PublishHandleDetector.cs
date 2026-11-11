using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Event and message systems (D17): one configurable detector instead of one per library.
/// A handler is a class implementing a handler interface for some event type <c>E</c>; a
/// publisher is any method that creates an <c>E</c>. Each handler is 🟡 triggered by each publisher.
/// </summary>
/// <remarks>
/// Built in: MediatR notification and request handlers, MassTransit consumers (general libraries only; a project's own
/// interfaces, such as nopCommerce's <c>IConsumer&lt;T&gt;</c>, go in its settings). Others via
/// <c>detectorSettings: { publish-handle: { handlerInterfaces: [Namespace.IHandler`1], busInterfaces: [...] } }</c>.
/// <c>busInterfaces</c> (a subset) are message buses that route between services by the event's type name, so a
/// same-named event declared in another assembly reaches their handlers too; in-process handlers never link by name.
/// </remarks>
public sealed class PublishHandleDetector : IDetector
{
    public string Name => "publish-handle";

    static readonly string[] BuiltInHandlers =
    [
        "MediatR.INotificationHandler`1",
        "MediatR.IRequestHandler`1",
        "MediatR.IRequestHandler`2",
        "MassTransit.IConsumer`1",
    ];

    public void Extract(ExtractContext ctx)
    {
        var buses = ctx.Settings.GetValueOrDefault("busInterfaces") ?? [];
        var handlers = BuiltInHandlers.Concat(ctx.Settings.GetValueOrDefault("handlerInterfaces") ?? []).Concat(buses).ToHashSet(StringComparer.Ordinal);
        foreach (var file in ctx.Files)
            foreach (var decl in file.Tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type || type.IsAbstract || type.TypeKind != TypeKind.Class) continue;
                if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() != decl) continue;
                foreach (var iface in type.AllInterfaces)
                {
                    if (!handlers.Contains(ExtractContext.FullName(iface.OriginalDefinition))) continue;
                    if (iface.TypeArguments.FirstOrDefault() is not INamedTypeSymbol evt || ctx.NodeId(evt) is not { } eventId) continue;
                    foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl || ctx.NodeId(impl) is not { } handlerId) continue;
                        var name = ExtractContext.FullName(iface.OriginalDefinition);
                        ctx.AddFact(Name, "handle", handlerId, decl, ("event", eventId), ("interface", name), ("bus", buses.Contains(name) ? "true" : "false"));
                    }
                }
                AttributeHandlers(ctx, type);
            }
    }

    /// <summary>
    /// D206: a class registered as a handler by an attribute naming the event's type (<c>[CheckOn(typeof(SeriesUpdatedEvent))]</c>):
    /// a candidate here, a handler once <see cref="Link"/> finds the type is an event the message system carries. What runs is the
    /// one method the class overrides from an abstract one, else its one public method; a class with several says nothing.
    /// </summary>
    void AttributeHandlers(ExtractContext ctx, INamedTypeSymbol type)
    {
        var attributes = type.GetAttributes().Where(a => a.ConstructorArguments.Any(c => c.Kind == TypedConstantKind.Type && c.Value is INamedTypeSymbol)).ToList();
        if (attributes.Count == 0) return;
        var methods = type.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public).ToList();
        var runs = methods.Where(m => m.OverriddenMethod is { IsAbstract: true }).ToList() is [var only] ? only : methods is [var one] ? one : null;
        if (runs is null || ctx.NodeId(runs) is not { } handlerId) return;
        foreach (var a in attributes)
            foreach (var evt in a.ConstructorArguments.Where(c => c.Kind == TypedConstantKind.Type).Select(c => c.Value).OfType<INamedTypeSymbol>())
            {
                if (ctx.NodeId(evt) is not { } eventId || a.ApplicationSyntaxReference?.GetSyntax() is not { } at) continue;
                var bases = new List<INamedTypeSymbol>();
                for (var b = evt.BaseType; b is not null; b = b.BaseType) bases.Add(b);
                var kinds = bases.Concat(evt.AllInterfaces).Select(b => ctx.NodeId(b)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
                ctx.AddFact(Name, "handle-attribute", handlerId, at, ("event", eventId), ("bases", string.Join(" ", kinds)),
                    ("attribute", a.AttributeClass?.Name ?? ""), ("args", string.Join(", ", evt.TypeArguments.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))));
            }
    }

    public void Link(LinkContext ctx)
    {
        var handles = ctx.Facts(Name).Where(f => f.Kind == "handle").ToList();
        // D206: an attribute-registered handler counts when its type is an event: one a handler takes, or a kind of one (an event
        // implementing the IEvent a catch-all handler takes).
        var events = handles.Select(f => f["event"]).ToHashSet(StringComparer.Ordinal);
        foreach (var c in ctx.Facts(Name).Where(f => f.Kind == "handle-attribute").ToList())
            if (events.Contains(c["event"]) || c["bases"].Split(' ').Any(events.Contains))
            {
                var data = new[] { ("event", c["event"]), ("interface", ""), ("bus", "false"), ("attribute", c["attribute"]), ("args", c["args"]) };
                ctx.AddFact(Name, "handle", c.Owner, c.File, c.Line, data);
                handles.Add(new Fact(Name, "handle", c.Owner, c.File, c.Line, data.ToDictionary(d => d.Item1, d => d.Item2, StringComparer.Ordinal)));
            }
        if (handles.Count == 0) return;
        var byEvent = handles.GroupBy(f => f["event"]).ToDictionary(g => g.Key, g => g.Select(f => f.Owner).Distinct().Order(StringComparer.Ordinal).ToList());
        // On a message bus, services often declare their own copy of an integration event and the bus routes by type
        // name, so a publisher in one service reaches same-named handlers in others. Only handlers of a bus interface.
        var byName = handles.Where(f => f["bus"] == "true").GroupBy(f => SimpleName(f["event"])).ToDictionary(g => g.Key, g => g.ToList());

        var publishers = ctx.Graph.AllEdges
            .Where(e => e.Kind == EdgeKind.Calls)
            .Select(e => (e.Source, Event: DocIds.ConstructedType(e.Target)))
            .Where(p => p.Event is not null && (byEvent.ContainsKey(p.Event) || byName.ContainsKey(SimpleName(p.Event))))
            .Distinct()
            .OrderBy(p => p.Event, StringComparer.Ordinal).ThenBy(p => p.Source, StringComparer.Ordinal);

        foreach (var (publisher, evt) in publishers)
        {
            foreach (var handler in byEvent.GetValueOrDefault(evt!) ?? [])
                ctx.AddEdge(handler, publisher, EdgeKind.TriggeredBy, Label.Guessed, Name);
            foreach (var other in (byName.GetValueOrDefault(SimpleName(evt!)) ?? []).Where(f => f["event"] != evt && DocIds.Assembly(f["event"]) != DocIds.Assembly(evt!)))
                ctx.AddEdge(other.Owner, publisher, EdgeKind.TriggeredBy, Label.Guessed, Name);
        }
    }

    static string SimpleName(string typeId) => typeId[(typeId.LastIndexOf('.') + 1)..];
}
