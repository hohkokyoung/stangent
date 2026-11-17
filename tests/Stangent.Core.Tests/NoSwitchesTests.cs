using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Graph.Detectors;
using Stangent.Graph.History;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>No component switches (D381, D388): every feature is on, a stangent.yaml from before still loads and is told
/// what no longer does anything, and <c>ai.enabled</c> stays, since it is a client's "no external AI" rule (D24).</summary>
public class NoSwitchesTests
{
    const string OldConfig = """
        version: 1
        components: { pages: false, truth-verdict: false }
        detectors: [none]
        history: { enabled: false, window: 7 }
        fsd: { findings: true, attachmentHome: false, compareTables: false, exportMaxLines: 900 }
        tickets: { aiPoints: false, bundleChars: 1234 }
        """;

    [Fact]
    public void An_old_config_loads_names_what_it_still_holds_and_keeps_the_rest()
    {
        using var app = new SpecApp(OldConfig);
        var c = app.Repo.Config;
        Assert.Equal(["components", "detectors", "history.enabled", "fsd.findings", "fsd.attachmentHome", "fsd.compareTables", "tickets.aiPoints"], c.RemovedSettings);
        Assert.Equal((7, 900, 1234), (c.History.Window, c.Fsd.ExportMaxLines, c.Tickets.BundleChars));
    }

    [Fact]
    public void A_config_with_none_of_them_names_nothing_and_a_new_one_writes_none()
    {
        using var app = new SpecApp("version: 1\nhistory: { window: 7 }\n");
        Assert.Empty(app.Repo.Config.RemovedSettings);
        using var fresh = new TempRepo();
        var written = File.ReadAllText(Path.Combine(fresh.Root, "stangent.yaml"));
        foreach (var key in new[] { "components:", "detectors:", "findings:", "attachmentHome:", "compareTables:", "aiPoints:" })
            Assert.DoesNotContain(key, written);
    }

    [Fact]
    public void A_config_that_isnt_yaml_is_still_refused_with_its_line()
    {
        using var temp = new TempRepo();
        File.WriteAllText(Path.Combine(temp.Root, "stangent.yaml"), "version: 1\ncomponents: [\n");
        var e = Assert.Throws<StangentException>(() => StangentRepo.Discover(temp.Root));
        Assert.Equal("invalid_config", e.Code);
    }

    [Fact]
    public void Every_detector_runs_whatever_an_old_list_said()
    {
        using var app = new SpecApp(OldConfig);
        Assert.Equal(DetectorRegistry.BuiltIn, DetectorRegistry.Enabled(app.Repo.Config));
    }

    [Fact]
    public void Git_history_runs_whatever_an_old_setting_said()
    {
        using var app = new SpecApp(OldConfig);
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        HistoryStep.Apply(app.Repo, graph, null, () => null, []);
        Assert.NotEqual("off", graph.History!.Status);
    }

    [Fact]
    public void Ai_enabled_false_still_stops_an_ai_step()
    {
        var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
        using var app = new SpecApp($"version: 1\nai: {{ enabled: false }}\nfsd: {{ archive: {{ name: docs, path: '{archive}' }} }}\n");
        var file = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# 1 Orders\n\nOrders over 50 need approval.\n"));
        FsdImport.Import(app.Repo, file);
        Assert.Equal("ai_disabled", Assert.Throws<StangentException>(() => FsdReview.Prepare(app.Repo, "shop", 1, false)).Code);
    }

    [Fact]
    public void The_cli_warns_about_old_settings_and_neither_envelope_nor_telemetry_names_components()
    {
        using var app = new SpecApp(OldConfig);
        var run = WholeFlow.RunIn(app.Root, "status");
        Assert.Equal(0, run.Exit);
        Assert.Null(run.Envelope["components"]);
        Assert.Null(run.Envelope["result"]!["detectors"]);
        Assert.Contains("components, detectors, history.enabled", run.Err);
        Assert.Contains("D381", run.Err);
        var events = Directory.GetFiles(Path.Combine(app.Root, ".stangent", "telemetry"), "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.DoesNotContain("stangent.components", e));
        Assert.All(events, e => Assert.Contains("\"schemaVersion\":3", e));
    }
}
