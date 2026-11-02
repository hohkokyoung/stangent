using System.Text.Json.Nodes;
using Stangent.Benchmark.Tickets;
using Stangent.Graph;
using Stangent.Spec.Trace;

namespace Stangent.Benchmark.Tests;

/// <summary>Phase 3's scoring pieces (D79): starting points' files, the keyword baseline, the 95% range.</summary>
public class TicketScoreTests
{
    [Fact]
    public void Proof_is_real_counts_only_a_pass_and_shows_the_unclear_ones_as_a_range_beside_it()
    {
        var real = TicketScore.ProofIsReal([.. Enumerable.Range(1, 17).Select(i => ($"P-{i}", (string?)"passed")),
            ("S-1", "same-failure"), ("S-2", "same-failure"), ("S-3", "same-failure"), ("D-1", "different-failure"), ("N-1", "not-compiled")]);
        Assert.Equal(17, (int)real["passOnFix"]!);
        Assert.Equal(0.773, (double)real["share"]!);
        Assert.False((bool)real["passed"]!);   // D79's bar: the unclear ones don't lift it
        Assert.Equal(0.864, (double)real["shareIfUnclearReal"]!);
        Assert.Equal(["D-1"], real["onFix"]!["different-failure"]!.AsArray().Select(k => (string)k!));
        Assert.Equal(3, real["onFix"]!["same-failure"]!.AsArray().Count);

        Assert.True((bool)TicketScore.ProofIsReal([.. Enumerable.Range(1, 9).Select(i => ($"P-{i}", (string?)"passed")), ("N-1", "not-run")])["passed"]!);
        var unverified = TicketScore.ProofIsReal([("X-1", null)]);
        Assert.Equal(1.0, (double)unverified["shareIfUnclearReal"]!);
        Assert.Equal(["X-1"], unverified["onFix"]!["none"]!.AsArray().Select(k => (string)k!));
    }

    [Theory]
    [InlineData("abc123-dirty", "abc123", "same-failure")]   // the run's own commit, dirty only with its SDK pin
    [InlineData("def456", "abc123", null)]                   // a hand-run --verify elsewhere doesn't count
    [InlineData("abc123", null, null)]
    public void On_fix_reads_only_the_answer_keys_commit(string commit, string? fix, string? want)
    {
        var proof = new JsonObject { ["verification"] = new JsonObject { ["commit"] = commit, ["result"] = "same-failure" } };
        Assert.Equal(want, TicketScore.OnFix(proof, fix));
    }

    [Fact]
    public void On_fix_doesnt_count_a_test_edited_after_its_proof()
    {
        var proof = new JsonObject { ["verification"] = new JsonObject { ["commit"] = "abc123", ["result"] = "passed", ["testChanged"] = true } };
        Assert.Null(TicketScore.OnFix(proof, "abc123"));
    }

    [Fact]
    public void On_fix_classifies_a_record_from_before_D105_from_its_saved_runs()
    {
        var proof = JsonNode.Parse("""
            {"attempts":[{"runs":[{"result":"failed-on-assertion","message":"found Imported"}]}],
             "verification":{"commit":"abc123","passed":false,"newFailures":[],"runs":[{"result":"failed-on-assertion","message":"found Imported"}]}}
            """)!.AsObject();
        Assert.Equal("same-failure", TicketScore.OnFix(proof, "abc123"));
        Assert.Null(TicketScore.OnFix(new JsonObject { ["verification"] = null }, "abc123"));
    }

    [Fact]
    public void Starting_files_are_the_cited_symbols_files_of_starting_pages_plus_matched_symbols()
    {
        var page = JsonNode.Parse("""{"id":"page:/series","sections":{"shows":[{"text":"Shows the series.","cites":["A|M:A.Series.Show"]}],"actions":[],"rules":[],"sideEffects":[]}}""")!.AsObject();
        var index = new SpecIndex { Sections = new Dictionary<(string, string), JsonObject>(), Pages = new Dictionary<string, JsonObject> { ["page:/series"] = page } };
        var nodes = new Dictionary<string, Node>
        {
            ["A|M:A.Series.Show"] = new("A|M:A.Series.Show", NodeKind.Method, "Series.Show", null, "src/A/Series.cs", 1, 2),
            ["A|M:A.Tv.Update"] = new("A|M:A.Tv.Update", NodeKind.Method, "Tv.Update", null, "src/A/Tv.cs", 1, 2),
        };
        var points = JsonNode.Parse("""{"points":[{"kind":"page","id":"page:/series"},{"kind":"symbol","id":"A|M:A.Tv.Update"},{"kind":"page","id":"page:/unwritten"}]}""")!;
        Assert.Equal(["src/A/Series.cs", "src/A/Tv.cs"], TicketScore.StartingFiles(points, index, nodes).Order());
    }

    [Fact]
    public void The_keyword_baseline_takes_the_top_five_files_by_shared_words()
    {
        var words = new HashSet<string> { "episode", "monitor", "season" };
        var files = new Dictionary<string, List<string>>
        {
            ["src/Tv/EpisodeService.cs"] = ["EpisodeService.SetEpisodeMonitored"],
            ["src/Tv/SeasonMonitor.cs"] = ["SeasonMonitor.Season"],
            ["src/Indexers/Torznab.cs"] = ["Torznab.Fetch"],
            ["frontend/src/Episode/EpisodeRow.tsx"] = ["EpisodeRow"],
        };
        var top = TicketScore.KeywordTop5(words, files);
        Assert.Contains("src/Tv/EpisodeService.cs", top);
        Assert.Contains("src/Tv/SeasonMonitor.cs", top);
        Assert.DoesNotContain("src/Indexers/Torznab.cs", top);
    }

    [Fact]
    public void The_range_is_repeatable_and_brackets_the_share()
    {
        double[] v = [1, 1, 1, 0, 1, 0, 1, 1, 0, 1];
        var (lo, hi) = TicketScore.Range(v);
        Assert.Equal((lo, hi), TicketScore.Range(v));
        Assert.True(lo <= 0.7 && 0.7 <= hi);
        Assert.Equal((0.0, 0.0), TicketScore.Range([]));
    }
}
