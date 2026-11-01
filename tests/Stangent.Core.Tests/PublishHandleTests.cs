using Stangent.Graph;
using Stangent.Graph.Detectors;
using Stangent.Graph.Extraction;

namespace Stangent.Core.Tests;

/// <summary>D94 item 8: same-named events in other assemblies link only through a message bus's handlers.</summary>
public class PublishHandleTests
{
    const string Publisher = "Orders|M:Orders.Checkout.Place";
    const string OrdersEvent = "Orders|T:Orders.Events.ItemAdded";
    const string BasketEvent = "Basket|T:Basket.Events.ItemAdded";
    const string BusHandler = "Basket|M:Basket.OnItemAdded.Handle(Basket.Events.ItemAdded)";
    const string LocalHandler = "Orders|M:Orders.OnItemAdded.Handle(Orders.Events.ItemAdded)";

    static List<Edge> Link(bool bus)
    {
        var graph = new CodeGraph { Commit = "c" };
        var f = new ProjectFragment { ProjectId = "project:all", Fingerprint = "x" };
        f.Nodes.AddRange([new Node(Publisher, NodeKind.Method, "Place"), new Node(BusHandler, NodeKind.Method, "Handle"), new Node(LocalHandler, NodeKind.Method, "Handle")]);
        f.Edges.Add(new Edge(Publisher, "Orders|M:Orders.Events.ItemAdded.#ctor", EdgeKind.Calls, Label.Certain));
        f.Facts.Add(new Fact("publish-handle", "handle", BusHandler, null, 1, new Dictionary<string, string> { ["event"] = BasketEvent, ["bus"] = bus ? "true" : "false" }));
        f.Facts.Add(new Fact("publish-handle", "handle", LocalHandler, null, 1, new Dictionary<string, string> { ["event"] = OrdersEvent, ["bus"] = "false" }));
        graph.Fragments[f.ProjectId] = f;
        new PublishHandleDetector().Link(new LinkContext(graph, new Dictionary<string, List<string>>()));
        return [.. graph.LinkedEdges.Where(e => e.Kind == EdgeKind.TriggeredBy)];
    }

    [Fact]
    public void A_handler_of_the_same_event_type_is_always_triggered_by_its_publisher() =>
        Assert.Contains(Link(bus: false), e => e.Source == LocalHandler && e.Target == Publisher);

    [Fact]
    public void A_same_named_event_in_another_assembly_links_only_on_a_bus()
    {
        Assert.Contains(Link(bus: true), e => e.Source == BusHandler && e.Target == Publisher);
        Assert.DoesNotContain(Link(bus: false), e => e.Source == BusHandler);   // a modular monolith: two different events
    }
}
