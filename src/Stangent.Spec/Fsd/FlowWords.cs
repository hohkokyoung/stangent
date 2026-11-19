using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Flows;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// D416: the PM view's actor, journey and edge cases in plain words. <b>Code picks</b> from each flow a numbered question
/// links to, by D273's categories read straight from the flow file: the start, who may start it, each thing that changes
/// the world (a save, work queued, a file written, a push, an outside call, a navigation; the same change once) and each
/// way it stops early with its condition, at most <see cref="MaxChanges"/> and <see cref="MaxStops"/> (the rest counted,
/// never dropped silently). <b>An AI words them</b> (D60: <c>fsd view</c> prepares, <c>stangent-view-writer</c> writes one
/// line per pick and a title, <c>fsd view-check</c> checks): every line names one pick, every pick has a line, no line is
/// extra, a line is one sentence and names no code name its step didn't. One retry; a pick still failing keeps the code's
/// own label, marked. The words are stored in <c>specs/fsd/&lt;doc&gt;/flow-words.json</c> (AI output is kept, D18) and
/// reused while a flow's picks are unchanged. Trusted by one frozen measurement (D416 (4)), not a checker on each run.
/// </summary>
public static partial class FlowWords
{
    public const string Contract = "flow-words";
    public const int Version = 1;
    public const string Schema = "flow-words.v1.json";
    public const string OutputSchema = "fsd-view-output.v1.json";
    public const string Writer = "stangent-view-writer";
    /// <summary>Kavita's 475 flows (2026-11-19): distinct changes per flow p95 8, distinct stops p90 8 (D416 (2)).</summary>
    public const int MaxChanges = 8;
    public const int MaxStops = 8;
    public const double DefaultUsd = 2.0;
    public const int MaxTurns = 4;
    public const int MaxLine = 200;
    public const int MaxTitle = 80;
    const string InputFile = "input.md";
    const string OutputFile = "output.json";

    public static string WordsPath(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "flow-words.json");
    public static string RunDir(string root, string doc) => Path.Combine(root, ".stangent", "fsd", doc, "view");
    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    public sealed record Item(string Id, string Kind, string Code, string At, IReadOnlyList<string> Conditions);
    public sealed record Picked(string Flow, string Name, string File, IReadOnlyList<Item> Items, string Hash, int MoreChanges, int MoreStops);
    public sealed record Worded(string Title, IReadOnlyDictionary<string, string> Lines, IReadOnlySet<string> Failed);

    // ---- picking (code) ----------------------------------------------------------------------------------------------

    /// <summary>The flows' index (<c>specs/flows/index.json</c>) by id; empty when there are no flows.</summary>
    public static Dictionary<string, JsonObject> FlowIndex(string root) =>
        ((PageRun.LoadJson(Path.Combine(root, FlowFiles.IndexFile)) as JsonObject)?["flows"]?.AsArray().OfType<JsonObject>() ?? [])
            .Where(f => (string?)f["id"] is not null && (string?)f["file"] is not null)
            .GroupBy(f => (string)f["id"]!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    static bool IsChange(string kind, string what) =>
        kind is "queue" or "file" or "push" or "outside-call" or "navigate" || (kind == "database" && what.StartsWith("saves", StringComparison.Ordinal));

    /// <summary>A flow's picks, or null when its file can't be read.</summary>
    public static Picked? Pick(string root, JsonObject indexEntry)
    {
        var file = (string)indexEntry["file"]!;
        if (PageRun.LoadJson(Path.Combine(root, file)) is not JsonObject flow) return null;
        // Each condition as the flow's own Markdown reads it (D173, D240): an `unless` is an earlier guard that must be false,
        // never the bare code, which reads as its opposite (the 2026-11-19 measurement: 22 of 22 false lines).
        static List<string> Conds(JsonNode? n) => [.. (n as JsonArray ?? []).OfType<JsonObject>().Select(FlowFiles.ConditionText).OfType<string>()];
        var id = (string)indexEntry["id"]!;
        var name = (string?)flow["name"] ?? (string?)indexEntry["name"] ?? id;
        var start = flow["start"] as JsonObject;
        var startAt = (string?)start?["at"] ?? "";
        var items = new List<Item> { new("S", "start", $"{name}: {(string?)start?["code"] ?? ""}".Trim().TrimEnd(':'), startAt, []) };
        if (flow["actor"] is JsonObject a && (string?)a["who"] is { Length: > 0 } who)
            items.Add(new Item("A", "actor", (string?)a["why"] is { Length: > 0 } why ? $"{who} ({why})" : who, startAt, []));
        var changes = new List<Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in (flow["steps"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var what = (string?)s["what"] ?? "";
            if (!IsChange((string?)s["kind"] ?? "", what) || !seen.Add(what)) continue;
            changes.Add(new Item($"C{changes.Count + 1}", "change", $"{what} — {((string?)s["code"] ?? "").Trim()}", (string?)s["at"] ?? "", Conds(s["conditions"])));
        }
        var stops = new List<Item>();
        var stopKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in (flow["exits"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var conds = Conds(x["conditions"]);
            var kind = (string?)x["kind"] ?? "stop";
            if (!stopKeys.Add(kind + "\u0001" + string.Join("\u0001", conds))) continue;
            stops.Add(new Item($"X{stops.Count + 1}", "stop", $"{kind}: {((string?)x["code"] ?? "").Trim()}", (string?)x["at"] ?? "", conds));
        }
        items.AddRange(changes.Take(MaxChanges));
        items.AddRange(stops.Take(MaxStops));
        // Not the lines: an edit above the flow moves them without changing what the words describe, and must not cost a re-run.
        var hash = CanonicalJson.Hash(new JsonArray([.. items.Select(i => (JsonNode)new JsonObject
        {
            ["id"] = i.Id, ["kind"] = i.Kind, ["code"] = i.Code, ["conditions"] = new JsonArray([.. i.Conditions.Select(c => (JsonNode)c)]),
        })]).ToJsonString());
        return new Picked(id, name, file, items, hash, Math.Max(changes.Count - MaxChanges, 0), Math.Max(stops.Count - MaxStops, 0));
    }

    // ---- the stored words ------------------------------------------------------------------------------------------------

    /// <summary><c>flow-words.json</c>'s <c>flows</c>, or an empty object.</summary>
    public static JsonObject LoadWords(string root, string doc) =>
        (PageRun.LoadJson(WordsPath(root, doc)) as JsonObject)?["flows"] as JsonObject ?? [];

    /// <summary>A flow's words, only when they were checked against these very picks.</summary>
    public static Worded? WordsFor(JsonObject words, Picked p)
    {
        if (words[p.Flow] is not JsonObject w || (string?)w["itemsHash"] != p.Hash) return null;
        var lines = (w["lines"] as JsonObject ?? []).Where(kv => kv.Value is JsonValue).ToDictionary(kv => kv.Key, kv => (string)kv.Value!, StringComparer.Ordinal);
        var failed = (w["failed"] as JsonArray ?? []).Select(x => (string)x!).ToHashSet(StringComparer.Ordinal);
        return new Worded((string?)w["title"] ?? "", lines, failed);
    }

    /// <summary>The flows the view's numbered questions link to (never a listed control's, D414), picked.</summary>
    static List<Picked> Linked(string root, JsonObject map)
    {
        var index = FlowIndex(root);
        var ids = TruthView.Areas(map).SelectMany(a => a.Modules).SelectMany(TruthView.Questions)
            .Where(e => (string)e["link"]!["kind"]! == "flow").Select(e => (string)e["link"]!["id"]!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return [.. ids.Select(id => index.TryGetValue(id, out var entry) ? Pick(root, entry) : null).OfType<Picked>()];
    }

    // ---- fsd view <doc>: prepare ---------------------------------------------------------------------------------------

    /// <summary><c>fsd view &lt;doc&gt; [--max-cost N|none]</c>: the linked flows whose picks have no checked words yet, prepared
    /// for <c>stangent-view-writer</c>. Without a cost choice they are counted and the command named (rule 9).</summary>
    public static (JsonObject Result, RunStep? Step) Run(StangentRepo repo, string doc, string? maxCost)
    {
        var root = repo.Root;
        double? cap = maxCost switch
        {
            null => null,
            "none" => DefaultUsd,
            var v when double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 => d,
            _ => throw new StangentException("usage", "--max-cost takes a dollar figure (1.5) or `none`.", ExitCodes.Usage),
        };
        var map = PageRun.LoadJson(TruthMap.JsonPath(root, doc)) as JsonObject
            ?? throw new StangentException("no_truth_map", $"{doc} has no truth map yet: run `stangent fsd rulings-check {doc}` first.", ExitCodes.NotFound);
        var words = LoadWords(root, doc);
        var linked = Linked(root, map);
        var waiting = linked.Where(p => WordsFor(words, p) is null).ToList();
        var result = new JsonObject
        {
            ["doc"] = doc, ["linkedFlows"] = linked.Count, ["worded"] = linked.Count - waiting.Count, ["waiting"] = waiting.Count,
            ["flows"] = new JsonArray([.. waiting.Select(p => (JsonNode)p.Flow)]),
        };
        if (waiting.Count == 0)
        {
            result["summary"] = $"{linked.Count} linked flow{(linked.Count == 1 ? " is" : "s are")} in plain words already; nothing to word.";
            return (result, null);
        }
        if (cap is null)
        {
            result["summary"] = $"{waiting.Count} of {linked.Count} linked flows wait for plain words, which an AI writes (paid, rule 9).";
            result["next"] = $"Choose a cost cap: `plugin/scripts/fsd-view-run.sh --max-cost 1 {doc}` (or `--max-cost none` for the writer's own ${DefaultUsd.ToString(CultureInfo.InvariantCulture)}).";
            return (result, null);
        }
        var step = Prepare(repo, doc, waiting, cap.Value, maxCost == "none");
        result["summary"] = $"{waiting.Count} of {linked.Count} linked flows prepared for the writer.";
        return (result, step);
    }

    static RunStep Prepare(StangentRepo repo, string doc, List<Picked> flows, double cap, bool none)
    {
        var root = repo.Root;
        var dir = RunDir(root, doc);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("fsd-view-writer.md")).Append("\n# The flows to word\n\n");
        var list = new JsonArray();
        var k = 0;
        foreach (var p in flows)
        {
            var id = $"V{++k}";
            list.Add(new JsonObject { ["id"] = id, ["flow"] = p.Flow, ["hash"] = p.Hash });
            sb.Append($"## {id} · {p.Name}\n\n");
            foreach (var i in p.Items)
            {
                sb.Append($"- **{i.Id}** ({i.Kind}): `{i.Code.Replace("`", "'", StringComparison.Ordinal)}`");
                if (i.Conditions.Count > 0) sb.Append($" — {string.Join("; ", i.Conditions)}");
                sb.Append('\n');
            }
            sb.Append('\n');
        }
        sb.Append("# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(OutputSchema).TrimEnd('\n')).Append("\n```\n");
        File.WriteAllText(Path.Combine(dir, InputFile), sb.ToString());
        var state = new JsonObject
        {
            ["doc"] = doc, ["commit"] = Git.Head(root), ["capUsd"] = cap, ["capChosen"] = none ? "none" : "usd",
            ["flows"] = list, ["retryUsed"] = false, ["usage"] = Usage.None.ToJson(),
        };
        MoveStage(root, state, "prepared", $"{flows.Count} flows to word");
        Save(dir, state);
        return Next(root, dir, "prepared");
    }

    static RunStep Next(string root, string dir, string status) => new(status, "view", Writer,
        $"Input: {Rel(root, Path.Combine(dir, InputFile))}\nWrite your JSON to: {Rel(root, Path.Combine(dir, OutputFile))}",
        Rel(root, Path.Combine(dir, OutputFile)), Schema: OutputSchema);

    static void MoveStage(string root, JsonObject state, string to, string reason) =>
        StateChanges.Apply(root, state, StateKinds.FsdViewRun, $"{(string)state["doc"]!}/view", to, StateActor.Code("stangent fsd view"), reason);

    static void Save(string dir, JsonObject state) => Quotes.WriteAtomic(Path.Combine(dir, "state.json"), CanonicalJson.Serialize(state));

    /// <summary>The writer's limits: no tools at all (every step it words is in its prompt), structured output, a few turns, and
    /// what is left of the run's cap.</summary>
    public static JsonObject DriverLimits(StangentRepo repo, string doc)
    {
        var state = PageRun.LoadJson(Path.Combine(RunDir(repo.Root, doc), "state.json")) as JsonObject;
        var cap = (double?)state?["capUsd"] ?? DefaultUsd;
        var spent = state is null ? 0 : Usage.FromJson(state["usage"]).CostUsd;
        return new JsonObject
        {
            ["tools"] = "", ["permissionMode"] = "dontAsk", ["maxTurns"] = MaxTurns,
            ["maxBudgetUsd"] = Math.Round(Math.Max(cap - spent, 0.01), 4),
        };
    }

    // ---- fsd view-check <doc> --------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>fsd view-check &lt;doc&gt;</c>: each flow's words checked by code against its picks (D416 (2)); passing words kept in
    /// <c>flow-words.json</c> and the view rewritten. One retry for the flows with problems; after it a failing line keeps
    /// the code's own label, marked, and a flow with no answer stays unworded.
    /// </summary>
    public static (JsonObject Result, RunStep Step) Check(StangentRepo repo, string doc, Usage? usage)
    {
        var root = repo.Root;
        var dir = RunDir(root, doc);
        var state = PageRun.LoadJson(Path.Combine(dir, "state.json")) as JsonObject
            ?? throw new StangentException("not_prepared", $"No view run for {doc}: run `stangent fsd view {doc} --max-cost <usd>` first.", ExitCodes.Usage);
        var stage = (string?)state["stage"];
        if (stage is "checked" or "failed") return (new JsonObject { ["doc"] = doc, ["run"] = stage }, new RunStep("current", "none"));
        var outPath = Path.Combine(dir, OutputFile);
        if (stage == "retry" && usage is null && !File.Exists(outPath))
            throw new StangentException("no_output", $"The retry has no answer yet ({Rel(root, outPath)}). Run the writer with the retry's prompt, or pass --usage for a call that returned nothing.", ExitCodes.Usage);
        state["usage"] = Usage.FromJson(state["usage"]).Add(usage ?? Usage.None).ToJson();

        var errors = new List<string>();
        var output = PageChecks.Parse(File.Exists(outPath) ? File.ReadAllText(outPath) : "", errors) as JsonObject;
        if (errors.Count == 0 && output is null) errors.Add("The output isn't a JSON object.");
        if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.ValidateDeep(OutputSchema, output!.ToJsonString()).Select(e => $"Schema: {e}"));
        if (output is null && (!File.Exists(outPath) || new FileInfo(outPath).Length == 0))
            errors.Insert(0, "You returned no answer (most likely you ran out of turns or budget). This time, answer with every flow.");

        var index = FlowIndex(root);
        var asked = (state["flows"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(f => (Id: (string)f["id"]!, Pick: index.TryGetValue((string)f["flow"]!, out var entry) ? Pick(root, entry) : null, Hash: (string)f["hash"]!))
            .ToList();
        var answers = errors.Count == 0 ? output!["flows"]!.AsArray().OfType<JsonObject>().ToList() : [];
        var problems = new List<string>(errors);
        var results = new Dictionary<string, (string Title, Dictionary<string, string> Lines, HashSet<string> Failed)>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in answers)
        {
            var id = (string)a["flow"]!;
            var ask = asked.FirstOrDefault(x => x.Id == id);
            if (ask.Id is null) { problems.Add($"{id}: no flow {id} was asked for"); continue; }
            if (!seen.Add(id)) { problems.Add($"{id}: two answers for {id}; give one"); continue; }
            if (ask.Pick is not { } p || p.Hash != ask.Hash) { problems.Add($"{id}: its flow changed since the run was prepared; it is left unworded"); continue; }
            var (title, lines, failed, why) = CheckFlow(p, a);
            results[id] = (title, lines, failed);
            problems.AddRange(why.Select(w => $"{id}: {w}"));
        }
        foreach (var x in asked.Where(x => !seen.Contains(x.Id))) problems.Add($"{x.Id}: no answer was given for it");

        var retrying = problems.Count > 0 && (bool?)state["retryUsed"] != true;
        // Words that passed are kept now; on the retry the writer answers again for the flows with problems only.
        var kept = Store(root, doc, asked, results, final: !retrying);
        // The view follows the words kept, also before a retry the driver may never get to (a cost cap, a failed call).
        if (kept.Count > 0 && PageRun.LoadJson(TruthMap.JsonPath(root, doc)) is JsonObject kmap) TruthView.Write(root, doc, kmap);
        if (retrying)
        {
            state["retryUsed"] = true;
            var redo = asked.Where(x => !results.TryGetValue(x.Id, out var r) || r.Failed.Count > 0 || r.Title.Length == 0).Select(x => (JsonNode)new JsonObject { ["id"] = x.Id, ["flow"] = x.Pick?.Flow ?? "", ["hash"] = x.Hash }).ToList();
            state["flows"] = new JsonArray([.. redo]);
            MoveStage(root, state, "retry", "the code check failed");
            Save(dir, state);
            var sb = new StringBuilder("# The code check failed\n\nAnswer again for these flows only (the others are kept), fixing every problem below; after one more check, a line that still fails keeps the code's own label:\n\n");
            foreach (var e in problems.Take(80)) sb.Append($"- {e}\n");
            File.WriteAllText(Path.Combine(dir, "feedback.md"), sb.ToString());
            var previous = Path.Combine(dir, "previous-" + OutputFile);
            if (File.Exists(outPath)) File.Move(outPath, previous, overwrite: true); else File.WriteAllText(previous, "(no output)");
            var next = Next(root, dir, "retry");
            return (Report(root, doc, kept, problems, "retry"), next with { Errors = problems, Prompt = next.Prompt + $"\nYour previous attempt: {Rel(root, previous)}\nWhat to fix: {Rel(root, Path.Combine(dir, "feedback.md"))}" });
        }
        MoveStage(root, state, "checked", problems.Count > 0 ? $"checked, {problems.Count} problems left" : "checked");
        Save(dir, state);
        var report = Report(root, doc, kept, problems, "checked");
        if (kept.Count > 0) report["view"] = Rel(root, TruthView.ViewDir(root, doc));
        return (report, new RunStep("checked", "none", Errors: problems.Count > 0 ? problems : null));
    }

    /// <summary>One flow's answer against its picks: every line names one pick, every pick has a line, one sentence each, no
    /// code name its step doesn't have; the title one line naming none either.</summary>
    static (string Title, Dictionary<string, string> Lines, HashSet<string> Failed, List<string> Why) CheckFlow(Picked p, JsonObject a)
    {
        var why = new List<string>();
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var byId = p.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var l in a["lines"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string)l["item"]!;
            var text = ((string)l["text"]!).Trim();
            if (!byId.TryGetValue(id, out var item)) { why.Add($"line for {id}: there is no step {id} (the steps are {string.Join(", ", byId.Keys)})"); continue; }
            if (lines.ContainsKey(id) || failed.Contains(id)) { why.Add($"{id}: two lines; give one"); failed.Add(id); lines.Remove(id); continue; }
            if (LineProblem(text, item.Code + " " + string.Join(" ", item.Conditions)) is { } wp) { why.Add($"{id}: {wp}"); failed.Add(id); continue; }
            lines[id] = text;
        }
        foreach (var i in p.Items.Where(i => !lines.ContainsKey(i.Id) && !failed.Contains(i.Id))) { why.Add($"{i.Id}: no line was given for it"); failed.Add(i.Id); }
        var title = ((string?)a["title"] ?? "").Trim();
        var all = string.Join(" ", p.Items.Select(i => i.Code + " " + string.Join(" ", i.Conditions)));
        if (LineProblem(title, all, MaxTitle) is { } tp) { why.Add($"title: {tp}"); title = ""; }
        return (title, lines, failed, why);
    }

    /// <summary>Why a line isn't one plain sentence about its step, or null: empty, too long, a line break or backtick, or a
    /// code name (a word with an inner capital, a dotted name, a call, snake_case) its step doesn't have.</summary>
    internal static string? LineProblem(string text, string step, int max = MaxLine)
    {
        if (text.Length == 0) return "the line is empty";
        if (text.Length > max) return $"the line is {text.Length} characters; keep it to {max}, one plain sentence";
        if (text.Contains('\n') || text.Contains('\r')) return "the line has a line break; give one sentence";
        if (text.Contains('`')) return "the line has a backtick: plain words, no code marks";
        foreach (Match m in CodeName().Matches(text))
        {
            var name = m.Value.TrimEnd('(');
            if (name is "e.g" or "i.e" or "E.g" or "I.e") continue;
            if (!step.Contains(name, StringComparison.Ordinal)) return $"it names `{name}`, which its step doesn't: say what happens in plain words";
        }
        return null;
    }

    // A dotted name, a call, an inner capital, snake_case, SCREAMING_SNAKE, or a path or address (/api/x, http://…).
    [GeneratedRegex(@"\b(?=[\w.]*[A-Za-z])\w+(?:\.\w+)+\b|\b\w+\(|\b[A-Za-z_]*[a-z][A-Z]\w*\b|\b[a-z]+_[a-z0-9_]+\b|\b[A-Z][A-Z0-9]*_[A-Z0-9_]+\b|(?:https?://|(?<![\w/])/)[\w\-.~]+(?:/[\w\-.~:{}]*)*")]
    private static partial Regex CodeName();

    /// <summary>Merges checked words into <c>flow-words.json</c>: a flow keeps its passing lines; on the last check (no retry
    /// left) its failing picks are recorded as failed so the view shows their code labels, marked. Returns the flows kept.</summary>
    static JsonArray Store(string root, string doc, List<(string Id, Picked? Pick, string Hash)> asked,
        Dictionary<string, (string Title, Dictionary<string, string> Lines, HashSet<string> Failed)> results, bool final)
    {
        var path = WordsPath(root, doc);
        var file = PageRun.LoadJson(path) as JsonObject ?? new JsonObject { ["contract"] = Contract, ["version"] = Version, ["doc"] = doc, ["flows"] = new JsonObject() };
        var flows = file["flows"] as JsonObject ?? [];
        file["flows"] = flows;
        var kept = new JsonArray();
        foreach (var (id, pick, hash) in asked)
        {
            if (pick is null || !results.TryGetValue(id, out var r)) continue;
            var whole = r.Failed.Count == 0 && r.Title.Length > 0;
            if (!whole && !final) continue;   // asked again on the retry
            flows[pick.Flow] = new JsonObject
            {
                ["itemsHash"] = hash, ["title"] = r.Title,
                ["lines"] = new JsonObject([.. r.Lines.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))]),
                ["failed"] = new JsonArray([.. r.Failed.Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
            };
            kept.Add((JsonNode)pick.Flow);
        }
        if (kept.Count == 0) return kept;
        var errors = SchemaRegistry.Embedded.Validate(Schema, file.ToJsonString());
        if (errors.Count > 0) throw new StangentException("bad_flow_words", $"flow-words.json doesn't match its contract: {errors[0]}");
        Quotes.WriteAtomic(path, CanonicalJson.Serialize(file));
        return kept;
    }

    static JsonObject Report(string root, string doc, JsonArray kept, List<string> problems, string stage) => new()
    {
        ["doc"] = doc, ["run"] = stage, ["worded"] = kept, ["words"] = Rel(root, WordsPath(root, doc)),
        ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        ["summary"] = $"{kept.Count} flow{(kept.Count == 1 ? "" : "s")} in plain words" + (stage == "retry" ? ", the rest back to the writer once" : problems.Count > 0 ? $", {problems.Count} problems left (those lines keep the code's labels)" : ""),
        ["next"] = $"Commit {Rel(root, WordsPath(root, doc))} with the view: the words are AI output, kept (D18).",
    };
}
