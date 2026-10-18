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
/// Built in: nopCommerce <c>IConsumer&lt;T&gt;</c>, eShop <c>IIntegrationEventHandler&lt;T&gt;</c>,
/// MediatR notification and request handlers, MassTransit consumers. Others via
/// <c>detectorSettings: { publish-handle: { handlerInterfaces: [Namespace.IHandler`1] } }</c>.
/// </remarks>
public sealed class PublishHandleDetector : IDetector
{
    public string Name => "publish-handle";

    static readonly string[] BuiltInHandlers =
    [
        "Nop.Services.Events.IConsumer`1",
        "eShop.EventBus.Abstractions.IIntegrationEventHandler`1",
        "MediatR.INotificationHandler`1",
        "MediatR.IRequestHandler`1",
        "MediatR.IRequestHandler`2",
        "MassTransit.IConsumer`1",
    ];

    public void Extract(ExtractContext ctx)
    {
        var handlers = BuiltInHandlers.Concat(ctx.Settings.GetValueOrDefault("handlerInterfaces") ?? []).ToHashSet(StringComparer.Ordinal);
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
                        ctx.AddFact(Name, "handle", handlerId, decl, ("event", eventId), ("interface", ExtractContext.FullName(iface.OriginalDefinition)));
                    }
                }
            }
    }

    public void Link(LinkContext ctx)
    {
        var handles = ctx.Facts(Name).Where(f => f.Kind == "handle").ToList();
        if (handles.Count == 0) return;
        var byEvent = handles.GroupBy(f => f["event"]).ToDictionary(g => g.Key, g => g.Select(f => f.Owner).Distinct().Order(StringComparer.Ordinal).ToList());
        // Services often declare their own copy of an integration event and the bus routes by type
        // name, so a publisher in one service reaches same-named handlers in others.
        var byName = handles.GroupBy(f => SimpleName(f["event"])).ToDictionary(g => g.Key, g => g.ToList());

        var publishers = ctx.Graph.AllEdges
            .Where(e => e.Kind == EdgeKind.Calls)
            .Select(e => (e.Source, Event: DocIds.ConstructedType(e.Target)))
            .Where(p => p.Event is not null && byName.ContainsKey(SimpleName(p.Event)))
            .Distinct()
            .OrderBy(p => p.Event, StringComparer.Ordinal).ThenBy(p => p.Source, StringComparer.Ordinal);

        foreach (var (publisher, evt) in publishers)
        {
            foreach (var handler in byEvent.GetValueOrDefault(evt!) ?? [])
                ctx.AddEdge(handler, publisher, EdgeKind.TriggeredBy, Label.Guessed, Name);
            foreach (var other in byName[SimpleName(evt!)].Where(f => f["event"] != evt && DocIds.Assembly(f["event"]) != DocIds.Assembly(evt!)))
                ctx.AddEdge(other.Owner, publisher, EdgeKind.TriggeredBy, Label.Guessed, Name);
        }
    }

    static string SimpleName(string typeId) => typeId[(typeId.LastIndexOf('.') + 1)..];
}
