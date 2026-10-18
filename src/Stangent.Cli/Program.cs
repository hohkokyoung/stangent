using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Phases;
using Stangent.Core.Store;
using Stangent.Core.Telemetry;
using Stangent.Graph;
using Stangent.Graph.Impact;

// The `stangent` CLI: the store's only door (D23). Output follows docs/cli-conventions.md:
// one JSON envelope on stdout, exit codes from ExitCodes, warnings on stderr.

var repoOption = new Option<string?>("--repo") { Description = "Repo to work on (default: the nearest stangent.yaml at or above the current folder).", Recursive = true };
var prettyOption = new Option<bool>("--pretty") { Description = "Indent the JSON output for people.", Recursive = true };
var fileOption = new Option<string?>("--file") { Description = "Read JSON from this file instead of stdin." };

var root = new RootCommand("stangent: specs, code, tests, PRs and tickets, linked through one store.");
root.Options.Add(repoOption);
root.Options.Add(prettyOption);

var init = new Command("init", "Set up stangent in a repo: stangent.yaml, the store folder, .gitignore lines.");
init.SetAction(pr => Cli.Run(pr, "init", ctx =>
{
    var (repo, created) = StangentRepo.Init(pr.GetValue(repoOption) ?? Directory.GetCurrentDirectory());
    ctx.UseRepo(repo);
    return new JsonObject { ["root"] = repo.Root, ["created"] = created, ["store"] = Path.GetRelativePath(repo.Root, repo.StoreRoot) };
}));
root.Subcommands.Add(init);

var status = new Command("status", "What's in the store, whether AI is on, and where telemetry goes.");
status.SetAction(pr => Cli.Run(pr, "status", ctx =>
{
    var entries = ctx.Store.Query(new ArtifactQuery());
    ctx.Span.Attributes["stangent.artifact.count"] = entries.Count;
    var byType = new JsonObject();
    foreach (var group in entries.GroupBy(e => e.Ref[..e.Ref.IndexOf('/')]).OrderBy(g => g.Key, StringComparer.Ordinal))
        byType[group.Key] = group.Count();
    return new JsonObject
    {
        ["root"] = ctx.Repo.Root,
        ["version"] = TelemetryWriter.ServiceVersion,
        ["ai"] = new JsonObject { ["enabled"] = ctx.Repo.Config.Ai.Enabled },
        ["artifacts"] = new JsonObject { ["total"] = entries.Count, ["byType"] = byType },
        ["detectors"] = new JsonArray([.. ctx.Repo.Config.Detectors.Select(d => (JsonNode)d)]),
        ["telemetryDir"] = ctx.Telemetry.Dir,
    };
}));
root.Subcommands.Add(status);

var store = new Command("store", "Read and write artifacts.");
root.Subcommands.Add(store);

var refArg = new Argument<string>("ref") { Description = "The artifact, as type/id." };
var get = new Command("get", "Print one artifact.") { refArg };
get.SetAction(pr => Cli.Run(pr, "store get", ctx => ctx.Store.Get(ArtifactRef.Parse(pr.GetValue(refArg)!))));
store.Subcommands.Add(get);

var put = new Command("put", "Validate and write one artifact (JSON on stdin or --file).") { fileOption };
put.SetAction(pr => Cli.Run(pr, "store put", ctx =>
{
    var artifact = Cli.ReadJson(pr.GetValue(fileOption)) as JsonObject
        ?? throw new StangentException("invalid_json", "Expected one JSON object.", ExitCodes.Usage);
    ctx.Span.Attributes["stangent.artifact.count"] = 1;
    return StoreBatch.ToJson(ctx.Store.Put(artifact));
}));
store.Subcommands.Add(put);

var sourceArg = new Argument<string>("source") { Description = "Artifact to link from, as type/id." };
var relArg = new Argument<string>("rel") { Description = "Relationship, e.g. implements." };
var targetArg = new Argument<string>("target") { Description = "Artifact to link to, as type/id. Must exist." };
var link = new Command("link", "Add a link between two existing artifacts.") { sourceArg, relArg, targetArg };
link.SetAction(pr => Cli.Run(pr, "store link", ctx => StoreBatch.ToJson(ctx.Store.Link(
    ArtifactRef.Parse(pr.GetValue(sourceArg)!), pr.GetValue(relArg)!, ArtifactRef.Parse(pr.GetValue(targetArg)!)))));
store.Subcommands.Add(link);

var typeOption = new Option<string?>("--type") { Description = "Only this artifact type." };
var linksToOption = new Option<string?>("--links-to") { Description = "Only artifacts linking to this type/id." };
var relOption = new Option<string?>("--rel") { Description = "Only links with this relationship." };
var query = new Command("query", "List artifacts from the index.") { typeOption, linksToOption, relOption };
query.SetAction(pr => Cli.Run(pr, "store query", ctx =>
{
    var entries = ctx.Store.Query(new ArtifactQuery(pr.GetValue(typeOption), pr.GetValue(linksToOption), pr.GetValue(relOption)));
    ctx.Span.Attributes["stangent.artifact.count"] = entries.Count;
    return StoreBatch.QueryJson(entries);
}));
store.Subcommands.Add(query);

var batch = new Command("batch", "Run many get/put/link/query ops in one call; all checked before anything is written.") { fileOption };
batch.SetAction(pr => Cli.Run(pr, "store batch", ctx =>
{
    var ops = Cli.ReadJson(pr.GetValue(fileOption)) as JsonArray
        ?? throw new StangentException("invalid_json", "Expected a JSON array of ops.", ExitCodes.Usage);
    ctx.Span.Attributes["stangent.artifact.count"] = ops.Count;
    return StoreBatch.Run(ctx.Store, ops);
}));
store.Subcommands.Add(batch);

var index = new Command("index", "The SQLite index derived from the store files.");
root.Subcommands.Add(index);
var rebuild = new Command("rebuild", "Rebuild the index from the store files (validates every file).");
rebuild.SetAction(pr => Cli.Run(pr, "index rebuild", ctx =>
{
    var count = ctx.Store.RebuildIndex();
    ctx.Span.Attributes["stangent.artifact.count"] = count;
    return new JsonObject { ["artifacts"] = count };
}));
index.Subcommands.Add(rebuild);
var dump = new Command("dump", "Print the index contents in a stable order.");
dump.SetAction(pr => Cli.Run(pr, "index dump", ctx => ctx.Store.DumpIndex()));
index.Subcommands.Add(dump);

var phases = new Command("phases", "The roadmap's phase files.");
root.Subcommands.Add(phases);
var dirOption = new Option<string>("--dir") { Description = "Folder of phase files.", DefaultValueFactory = _ => "phases" };
var roadmapOption = new Option<string?>("--roadmap") { Description = "Roadmap to compare statuses with (default: roadmap.md next to the phases folder)." };
var check = new Command("check", "Flag phases consuming an old contract version, and statuses that disagree with the roadmap (D20).") { dirOption, roadmapOption };
check.SetAction(pr => Cli.Run(pr, "phases check", ctx =>
{
    var dir = Path.GetFullPath(pr.GetValue(dirOption)!);
    var roadmap = pr.GetValue(roadmapOption) ?? Path.Combine(Path.GetDirectoryName(dir)!, "roadmap.md");
    var report = PhaseCheck.Run(dir, roadmap);
    if (report["ok"]!.GetValue<bool>()) return report;
    throw new CheckFailed("phases_need_attention", "Some phases need attention; see result.problems.", report);
}));
phases.Subcommands.Add(check);

var graph = new Command("graph", "The code graph (D25–D27): build it, then ask what a change affects.");
root.Subcommands.Add(graph);
var noRestoreOption = new Option<bool>("--no-restore") { Description = "Skip `dotnet restore` (packages must already be restored)." };
var fullOption = new Option<bool>("--full") { Description = "Re-extract every project instead of only those that changed." };
var build = new Command("build", "Build or update .stangent/graph.sqlite from the code.") { noRestoreOption, fullOption };
build.SetAction(pr => Cli.Run(pr, "graph build", ctx =>
{
    var (_, stats) = GraphBuilder.BuildAsync(ctx.Repo, new BuildOptions(!pr.GetValue(noRestoreOption), pr.GetValue(fullOption), Console.Error))
        .GetAwaiter().GetResult();
    ctx.Span.Attributes["stangent.artifact.count"] = stats.Nodes;
    return JsonSerializer.SerializeToNode(stats, Cli.Json);
}));
graph.Subcommands.Add(build);

var symbolOption = new Option<string?>("--symbol") { Description = "A graph id, or a name like OrderProcessingService.PlaceOrderAsync." };
var fileImpactOption = new Option<string?>("--file") { Description = "Every point declared in this file." };
var diffOption = new Option<string?>("--diff") { Description = "A git range (base..head), or one ref compared with the working tree." };
var expandOption = new Option<string?>("--expand") { Description = "Show every item of one group: " + string.Join(", ", ImpactAnalyzer.Groups) + "." };
var formatOption = new Option<string>("--format") { Description = "json (default) or md (prints Markdown instead of the JSON envelope).", DefaultValueFactory = _ => "json" };
var impact = new Command("impact", "What a change affects, grouped and labelled (D27).") { symbolOption, fileImpactOption, diffOption, expandOption, formatOption };
impact.SetAction(pr =>
{
    var given = new[] { ("symbol", pr.GetValue(symbolOption)), ("file", pr.GetValue(fileImpactOption)), ("diff", pr.GetValue(diffOption)) }
        .Where(g => g.Item2 is not null).ToList();
    var markdown = pr.GetValue(formatOption) == "md";
    return Cli.Run(pr, "graph impact", ctx =>
    {
        if (given.Count != 1) throw new StangentException("usage", "Give exactly one of --symbol, --file or --diff.", ExitCodes.Usage);
        var report = Cli.Analyzer(ctx).Analyze(new ChangeSpec(given[0].Item1, given[0].Item2!), ctx.Repo.Root, pr.GetValue(expandOption));
        var errors = SchemaRegistry.Embedded.Validate("impact-report.v1.json", report.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_report", "The impact report broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        ctx.Span.Attributes["stangent.artifact.count"] = report["groups"]!.AsArray().Sum(g => (int)g!["total"]!);
        return report;
    }, markdown ? r => ImpactAnalyzer.Markdown(r!.AsObject()) : null);
});
graph.Subcommands.Add(impact);

var idArg = new Argument<string>("symbol") { Description = "A graph id, or a name like OrderProcessingService.PlaceOrderAsync." };
var show = new Command("show", "A point and the lines in and out of it.") { idArg };
show.SetAction(pr => Cli.Run(pr, "graph show", ctx => Cli.Show(ctx, pr.GetValue(idArg)!)));
graph.Subcommands.Add(show);

var exportArg = new Argument<string>("path") { Description = "File to write (gzip'd JSON lines)." };
var export = new Command("export", "Write the graph to one file, the unit CI shares (D25).") { exportArg };
export.SetAction(pr => Cli.Run(pr, "graph export", ctx =>
{
    var g = Cli.LoadGraph(ctx);
    GraphStore.Export(g, pr.GetValue(exportArg)!);
    return new JsonObject { ["path"] = Path.GetFullPath(pr.GetValue(exportArg)!), ["commit"] = g.Commit, ["hash"] = CanonicalJson.Hash(Convert.ToBase64String(File.ReadAllBytes(pr.GetValue(exportArg)!))) };
}));
graph.Subcommands.Add(export);
var importArg = new Argument<string>("path") { Description = "A file written by graph export." };
var import = new Command("import", "Replace .stangent/graph.sqlite with an exported graph.") { importArg };
import.SetAction(pr => Cli.Run(pr, "graph import", ctx =>
{
    var g = GraphStore.Import(pr.GetValue(importArg)!);
    GraphStore.Save(g, GraphStore.PathFor(ctx.Repo));
    return new JsonObject { ["commit"] = g.Commit, ["nodes"] = g.AllNodes.Count(), ["head"] = Cli.Head(ctx) };
}));
graph.Subcommands.Add(import);

return Cli.Execute(root, args, prettyOption);

/// <summary>A check that ran and found problems: exit 1, with the report as the result.</summary>
sealed class CheckFailed(string code, string message, JsonNode result) : StangentException(code, message)
{
    public JsonNode Result { get; } = result;
}

sealed class CliContext(string? repoPath, TelemetryWriter telemetry, Span span)
{
    StangentRepo? repo;
    FileArtifactStore? store;

    public TelemetryWriter Telemetry { get; private set; } = telemetry;
    public Span Span { get; } = span;
    public StangentRepo Repo => repo ??= UseRepo(StangentRepo.Discover(repoPath ?? Directory.GetCurrentDirectory()));
    public FileArtifactStore Store => store ??= new FileArtifactStore(Repo, SchemaRegistry.Embedded);

    public StangentRepo UseRepo(StangentRepo r)
    {
        repo = r;
        if (Environment.GetEnvironmentVariable(TelemetryWriter.DirVariable) is null)
            Telemetry = new TelemetryWriter(r.TelemetryDir, SchemaRegistry.Embedded, TimeProvider.System, Console.Error);
        Span.Attributes["stangent.repo.id"] = r.Id;
        Span.Attributes["stangent.ai.enabled"] = r.Config.Ai.Enabled;
        return r;
    }
}

static class Cli
{
    static bool pretty;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int Execute(RootCommand root, string[] args, Option<bool> prettyOption)
    {
        var parsed = root.Parse(args);
        pretty = parsed.GetValue(prettyOption);
        if (parsed.Errors.Count > 0)
            return Write(Envelope(false, null, "usage", string.Join(" ", parsed.Errors.Select(e => e.Message)), []), ExitCodes.Usage);
        return parsed.Invoke();
    }

    public static CodeGraph LoadGraph(CliContext ctx) => GraphStore.Load(GraphStore.PathFor(ctx.Repo))
        ?? throw new StangentException("no_graph", "No code graph yet. Run `stangent graph build` (or `graph import`).", ExitCodes.NotFound);

    public static ImpactAnalyzer Analyzer(CliContext ctx) => new(LoadGraph(ctx));

    public static string Head(CliContext ctx)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = ctx.Repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var head = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return head;
    }

    public static JsonNode Show(CliContext ctx, string symbol)
    {
        var g = LoadGraph(ctx);
        var analyzer = new ImpactAnalyzer(g);
        var matches = analyzer.Resolve(symbol);
        if (matches.Count == 0) throw new StangentException("not_found", $"Nothing in the graph matches '{symbol}'.", ExitCodes.NotFound);
        var edges = g.AllEdges.ToList();
        var spots = g.AllBlindSpots.ToList();
        JsonArray Lines(IEnumerable<Edge> list, Func<Edge, string> other) => new([.. GraphStore.SortEdges(list).Take(50).Select(e => (JsonNode)new JsonObject
        {
            ["kind"] = e.Kind, ["label"] = e.Label == Label.Certain ? "certain" : "guessed", ["detector"] = e.Detector,
            ["id"] = other(e), ["name"] = analyzer.Find(other(e))?.Name,
        })]);
        return new JsonArray([.. matches.Take(10).Select(n => (JsonNode)new JsonObject
        {
            ["id"] = n.Id, ["name"] = n.Name, ["kind"] = n.Kind, ["file"] = n.File, ["line"] = n.Line, ["isTest"] = n.IsTest,
            ["dependsOn"] = Lines(edges.Where(e => e.Source == n.Id), e => e.Target),
            ["dependedOnBy"] = Lines(edges.Where(e => e.Target == n.Id), e => e.Source),
            ["unresolved"] = new JsonArray([.. spots.Where(b => b.Owner == n.Id).Select(b => (JsonNode)$"{b.File}:{b.Line} {b.Reason}")]),
        })]);
    }

    public static int Run(ParseResult pr, string command, Func<CliContext, JsonNode?> body) => Run(pr, command, body, null);

    /// <summary><paramref name="render"/>, when given, prints its text instead of the JSON envelope on success.</summary>
    public static int Run(ParseResult pr, string command, Func<CliContext, JsonNode?> body, Func<JsonNode?, string>? render)
    {
        var repoPath = pr.GetValue<string?>("--repo");
        var span = DefaultTelemetry().Start("stangent." + command.Replace(' ', '.'));
        span.Attributes["stangent.command"] = command;
        var ctx = new CliContext(repoPath, DefaultTelemetry(), span);
        int exit;
        JsonObject output;
        try
        {
            // Find the repo up front so telemetry lands in its folder even if the command fails.
            // Commands that need a repo fail inside body() when there is none.
            if (command != "init" && StangentRepo.TryDiscover(repoPath ?? Directory.GetCurrentDirectory()) is { } found)
                ctx.UseRepo(found);
            output = Envelope(true, body(ctx), null, null, []);
            exit = ExitCodes.Ok;
        }
        catch (CheckFailed e)
        {
            output = Envelope(false, e.Result, e.Code, e.Message, e.Details);
            exit = e.ExitCode;
            span.Attributes["stangent.error.code"] = e.Code;
        }
        catch (StangentException e)
        {
            output = Envelope(false, null, e.Code, e.Message, e.Details);
            exit = e.ExitCode;
            span.Attributes["stangent.error.code"] = e.Code;
        }
        span.Attributes["stangent.exit_code"] = exit;
        ctx.Telemetry.End(span, exit == ExitCodes.Ok);
        if (render is not null && exit == ExitCodes.Ok)
        {
            Console.Out.Write(render(output["result"]));
            return exit;
        }
        return Write(output, exit);
    }

    public static JsonNode? ReadJson(string? file)
    {
        var text = file is null ? Console.In.ReadToEnd() : File.ReadAllText(file);
        try { return JsonNode.Parse(text); }
        catch (JsonException e) { throw new StangentException("invalid_json", e.Message, ExitCodes.Usage); }
    }

    static TelemetryWriter DefaultTelemetry()
    {
        var dir = Environment.GetEnvironmentVariable(TelemetryWriter.DirVariable)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".stangent", "telemetry");
        return new TelemetryWriter(dir, SchemaRegistry.Embedded, TimeProvider.System, Console.Error);
    }

    static JsonObject Envelope(bool ok, JsonNode? result, string? code, string? message, IReadOnlyList<string> details)
    {
        var envelope = new JsonObject { ["ok"] = ok };
        if (result is not null) envelope["result"] = result.DeepClone();
        if (!ok)
            envelope["error"] = new JsonObject
            {
                ["code"] = code, ["message"] = message,
                ["details"] = new JsonArray([.. details.Select(d => (JsonNode)d)]),
            };
        return envelope;
    }

    static int Write(JsonObject output, int exit)
    {
        Console.Out.WriteLine(pretty ? CanonicalJson.Serialize(output).TrimEnd('\n') : CanonicalJson.SerializeCompact(output));
        return exit;
    }
}
