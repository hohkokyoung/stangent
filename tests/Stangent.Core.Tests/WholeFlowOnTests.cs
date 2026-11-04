using System.Text.Json.Nodes;

namespace Stangent.Core.Tests;

/// <summary>The whole flow with D124's switches on: a fake AI answers each variant, and the code checks run on it. Real <c>claude</c> never runs.</summary>
public sealed class WholeFlowOn : IDisposable
{
    public WholeFlow Flow { get; } = WholeFlow.AllSwitchedOn();

    public void Dispose() => Flow.Dispose();
}

public class WholeFlowOnTests(WholeFlowOn on) : IClassFixture<WholeFlowOn>
{
    readonly WholeFlow flow = on.Flow;

    [Fact]
    public void The_flow_gets_through_with_every_switch_on_and_every_file_fits_its_contract()
    {
        Assert.Equal(int.MaxValue, flow.BrokeAt);
        Assert.Empty(flow.SchemaErrors);
    }

    [Fact]
    public void Table_facts_compare_real_fields_and_enums_from_both_extractors()
    {
        flow.Reached(4);
        var compared = flow.Seen("compared");
        var counts = compared["counts"]!;
        var byKind = compared["tables"]!.AsArray().OfType<JsonObject>().ToList();
        // The order's fields come from the endpoint's response type on the graph.
        Assert.Equal("weight", (string)byKind[0]["fsdOnly"]![0]!["fsd"]!);
        Assert.Contains(byKind[0]["matches"]!.AsArray(), m => (string)m!["code"]! == "total");
        Assert.Contains(byKind[0]["codeOnly"]!.AsArray(), m => (string)m!["code"]! == "id");
        // C#: Closed is 5 in the code and 1 in the FSD. TypeScript: both names match.
        Assert.Equal("5", (string)byKind[1]["differs"]![0]!["codeValue"]!);
        Assert.Equal(2, byKind[2]["matches"]!.AsArray().Count);
        Assert.Equal(1, (int)counts["unmapped"]!);
        Assert.Equal("Other", (string)compared["unmapped"]![0]!["title"]!);
    }

    [Fact]
    public void Table_facts_pass_trace_check_with_code_lines_as_evidence()
    {
        flow.Reached(4);
        var check = flow.Seen("compared.check");
        Assert.True((bool)check["ok"]!, string.Join("; ", check["problems"]!.AsArray().Select(p => (string)p!)));
        var differs = flow.Seen("compared.trace")["entries"]!.AsArray().OfType<JsonObject>().Single(e => (string?)e["table_fact"]?["result"] == "differs");
        Assert.EndsWith("Shop.Core/Status.cs", (string)differs["table_fact"]!["path"]!);
        Assert.Equal("read", (string)differs["evidence"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["citation"]!["kind"]! == "code")["strength"]!);
    }

    [Fact]
    public void Switched_off_the_graph_export_is_the_same_bytes_and_compare_says_off()
    {
        flow.Reached(4);
        Assert.True((bool)flow.Seen("export.same"));
        Assert.True((bool)flow.Seen("enums.removed"));
        Assert.True((bool)flow.Seen("enums.back"));
        Assert.False((bool)flow.Seen("compare.off")["compared"]!);
    }
}
