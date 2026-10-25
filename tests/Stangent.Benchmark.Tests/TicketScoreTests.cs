using System.Text.Json.Nodes;
using Stangent.Benchmark.Tickets;
using Stangent.Graph;
using Stangent.Spec.Trace;

namespace Stangent.Benchmark.Tests;

/// <summary>Phase 3's scoring pieces (D79): starting points' files, the keyword baseline, the 95% range.</summary>
public class TicketScoreTests
{
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
