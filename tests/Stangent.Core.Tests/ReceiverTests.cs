using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>
/// D310: a virtual call on <c>this</c> in a base class runs the override of the class the flow came in through (Sonarr's
/// <c>RestController.TypedCreated</c> calling <c>GetResourceById</c>). Built from C# added to a copy of the Shop fixture: a base class
/// whose method calls an abstract member on <c>this</c>, two subclasses overriding it, and a caller that holds only the base class.
/// </summary>
public sealed class ReceiverTests : IAsyncLifetime
{
    const string Code = """
        namespace Shop.Core;

        public abstract class NarrowBase
        {
            protected abstract string Load(int id);
            protected string Created(int id)
            {
                var found = this.Load(id);
                return found;
            }
            public string Open(int id) => Load(id);
        }

        public class NarrowSeries : NarrowBase
        {
            protected override string Load(int id) => SeriesLoad(id);
            static string SeriesLoad(int id) => id.ToString();
            public string Create(int id) => Created(id);
        }

        public class NarrowNaming : NarrowBase
        {
            protected override string Load(int id) => NamingLoad(id);
            static string NamingLoad(int id) => id.ToString();
            public string Create(int id) => Created(id);
        }

        public static class NarrowCaller
        {
            public static string Call(NarrowBase any) => any.Open(1);
        }
        """;

    const string M = "Shop.Core|M:Shop.Core.";
    readonly string root = ShopCopy.Create();
    CodeGraph graph = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(root, "Shop.Core", "Narrow.cs"), Code, ct);
        (graph, _) = await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(Restore: false), ct);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(root, recursive: true);
        return ValueTask.CompletedTask;
    }

    Flow Walk(string start)
    {
        var flow = new Flow
        {
            Id = "job-test", Name = "test", Kind = "job", Start = new StartInfo("job", new At("Shop.Core/Narrow.cs", 1, ""), M + start, []),
            Actor = new Actor("system", "", null, false),
        };
        var w = new Walker(new FlowGraph(graph, root), new FlowSettings(), flow) { ActorFixed = true };
        w.Root(M + start);
        w.Finish();
        return flow;
    }

    [Fact]
    public void A_call_on_this_or_on_the_object_itself_is_marked_and_one_on_another_object_is_not()
    {
        string? Self(string owner, string target) =>
            graph.AllFacts.Single(f => f.Kind == "call-site" && f.Owner == M + owner && f["target"] == M + target)["self"];
        Assert.Equal("1", Self("NarrowBase.Created(System.Int32)", "NarrowBase.Load(System.Int32)"));      // this.Load(id)
        Assert.Equal("1", Self("NarrowBase.Open(System.Int32)", "NarrowBase.Load(System.Int32)"));         // Load(id)
        Assert.Equal("1", Self("NarrowSeries.Create(System.Int32)", "NarrowBase.Created(System.Int32)"));  // an inherited method
        Assert.Equal("", Self("NarrowCaller.Call(Shop.Core.NarrowBase)", "NarrowBase.Open(System.Int32)")); // another object
        Assert.Equal("", Self("NarrowSeries.Load(System.Int32)", "NarrowSeries.SeriesLoad(System.Int32)"));  // static
    }

    [Fact]
    public void Entered_through_one_subclass_a_virtual_call_on_this_in_the_base_runs_only_that_subclass_override()
    {
        var flow = Walk("NarrowSeries.Create(System.Int32)");
        var load = Assert.Single(flow.Steps, s => s.Point?.Contains(".Load(", StringComparison.Ordinal) == true);
        Assert.Equal(M + "NarrowSeries.Load(System.Int32)", load.Point);
        Assert.False(load.Guessed);
        Assert.DoesNotContain(flow.Steps, s => s.Point?.Contains("Naming", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void A_receiver_the_walk_cannot_tell_keeps_every_override_each_a_guess_saying_why()
    {
        var flow = Walk("NarrowCaller.Call(Shop.Core.NarrowBase)");
        var loads = flow.Steps.Where(s => s.Point?.Contains(".Load(", StringComparison.Ordinal) == true).ToList();
        Assert.Equal([M + "NarrowNaming.Load(System.Int32)", M + "NarrowSeries.Load(System.Int32)"], loads.Select(s => s.Point).Order(StringComparer.Ordinal));
        Assert.All(loads, s => Assert.True(s.Guessed));
        Assert.All(loads, s => Assert.Contains("called on `this`, and which class the object is isn't known here", s.GuessReason, StringComparison.Ordinal));
    }

    [Fact]
    public void A_shared_flow_with_such_a_call_is_walked_once_per_receiving_class_with_its_name_in_the_id()
    {
        // Two jobs enter the same base method; at 2 callers it is shared code (a hub), so each continues into its own walk of it.
        var jobs = new ProjectFragment { ProjectId = "Narrow", Fingerprint = "" };
        foreach (var c in new[] { "NarrowSeries", "NarrowNaming" })
            jobs.Facts.Add(new Fact("test", "is-job", $"{M}{c}.Create(System.Int32)", "Shop.Core/Narrow.cs", 1, new Dictionary<string, string> { ["kind"] = "recurring", ["id"] = c }));
        graph.Fragments["Narrow"] = jobs;
        var set = FlowBuilder.Build(graph, root, new FlowSettings(HubCallers: 2));
        var series = set.Flows.Single(f => f.Name == "NarrowSeries");
        var naming = set.Flows.Single(f => f.Name == "NarrowNaming");
        Assert.Equal("shared-narrowbase-created--narrowseries", Assert.Single(series.Links, l => l.Kind == "continues").ToFlow);
        Assert.Equal("shared-narrowbase-created--narrownaming", Assert.Single(naming.Links, l => l.Kind == "continues").ToFlow);
        var shared = set.Flows.Single(f => f.Id == "shared-narrowbase-created--narrowseries");
        Assert.Equal("NarrowBase.Created on NarrowSeries", shared.Name);
        var load = Assert.Single(shared.Steps, s => s.Point?.Contains(".Load(", StringComparison.Ordinal) == true);
        Assert.Equal(M + "NarrowSeries.Load(System.Int32)", load.Point);
        // No flow enters it without a receiving class, so the walk over every override isn't written.
        Assert.DoesNotContain(set.Flows, f => f.Id == "shared-narrowbase-created");
        // Byte for byte the same on a rebuild.
        Assert.Equal(FlowFiles.Render(set), FlowFiles.Render(FlowBuilder.Build(graph, root, new FlowSettings(HubCallers: 2))));
    }
}
