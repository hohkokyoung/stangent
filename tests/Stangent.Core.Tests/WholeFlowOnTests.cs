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
    public void The_finer_judge_is_answered_and_its_verdicts_reach_trace_check_and_status()
    {
        flow.Reached(5);
        Assert.Equal("link-judge-group-finer-output.v1.json", (string)flow.Seen("on.judge")["next"]!["schema"]!);
        Assert.Equal("recorded", (string)flow.Seen("on.recorded")["status"]!);
        var check = flow.Seen("on.check");
        Assert.True((bool)check["ok"]!, string.Join("; ", check["problems"]!.AsArray().Select(p => (string)p!)));
        Assert.Equal(1, (int)check["counts"]!["byState"]!["partial"]!);
        Assert.Equal(1, (int)check["counts"]!["unclear"]!);
        Assert.Equal(1, (int)flow.Seen("on.status")["queues"]!["unclear"]!);
    }

    [Fact]
    public void A_group_of_sections_is_judged_once_each_link_on_its_own_section()
    {
        flow.Reached(5);
        Assert.Equal("grouped", (string)flow.Seen("on.grouped")["status"]!);
        Assert.Equal("current", (string)flow.Seen("on.current")["status"]!);
        var runs = flow.Seen("on.runs");
        Assert.Equal("1", (string)runs["shop#2"]!["unit"]!["leader"]!);
        Assert.Contains("2", runs["shop#1"]!["unit"]!["sections"]!.AsArray().Select(s => (string)s!));
        Assert.Contains(flow.Seen("on.trace")["entries"]!.AsArray(), e => (string?)e!["fsd"]?["section"] == "2" && (string)e!["state"]! == "conflict");
    }

    [Fact]
    public void A_question_is_answered_from_the_pages_with_code_lines_and_labelled()
    {
        flow.Reached(6);
        Assert.Equal(("stangent-page-asker", "page-asker-output.v1.json"), ((string)flow.Seen("ask.prepare")["next"]!["agent"]!, (string)flow.Seen("ask.prepare")["next"]!["schema"]!));
        Assert.Contains(WholeFlow.Effect, flow.Seen("ask.bundle").GetValue<string>());
        var answer = flow.Seen("ask.answer");
        Assert.Equal("answered", (string)answer["status"]!);
        Assert.Equal("as the pages describe it", (string)answer["answer"]!["label"]!);
        var cite = answer["answer"]!["sentences"]![0]!["cites"]![0]!;
        Assert.Equal(WholeFlow.Effect, (string)cite["says"]!);
        Assert.Contains(cite["lines"]!.AsArray(), l => ((string?)l!["at"] ?? "").Contains("Shop.Core/Orders.cs:", StringComparison.Ordinal));
        // The same pages and the same question: the same answer, with no call.
        Assert.Equal("none", (string)flow.Seen("ask.again")["next"]!["action"]!);
        Assert.Equal("answered", (string)flow.Seen("ask.again")["status"]!);
    }

    [Fact]
    public void A_question_nothing_answers_says_the_pages_dont_cover_it()
    {
        flow.Reached(6);
        Assert.Equal(("not-covered", "none"), ((string)flow.Seen("ask.unknown")["status"]!, (string)flow.Seen("ask.unknown")["next"]!["action"]!));
        Assert.Equal("not-covered", (string)flow.Seen("ask.nothing")["status"]!);
        Assert.Equal("The pages don't cover this.", (string)flow.Seen("ask.nothing")["answer"]!["text"]!);
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
