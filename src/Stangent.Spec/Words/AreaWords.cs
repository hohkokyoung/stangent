using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Words;

/// <summary>
/// Layer 4's AI step (D60, D254): one call per section of an area's page. <c>pages prepare</c> writes the section's bundle (its
/// items from <see cref="PageSummary"/>, the writing rules, the schema); the batch driver runs the writer; <c>pages check</c>
/// runs D251/D255's checks (every sentence cites an item, a 🟡/❓ is hedged, every condition of a cited item is marked, every
/// must-cite item covered), gives one retry, and otherwise keeps the code-made list. "In short" runs last, from the checked
/// sections. Results go to <c>specs/pages-runs.json</c>, and the page's words between D233's two lines are rebuilt from it.
/// A section whose hash (items, rules, model, effort) is unchanged makes no call. Off unless <c>components.pages</c> is on.
/// </summary>
public static class AreaWords
{
    public const string Writer = "stangent-area-writer";
    public const string OutputSchema = "area-section-output.v1.json";
    public const string Instructions = "area-writer.md";
    public const string RunsFile = "specs/pages-runs.json";
    public const string WordsMarker = "<!-- written by `stangent pages check` (D254): every sentence cites the code's items in a hidden note; rebuilt from specs/pages-runs.json -->";

    /// <summary>D255 item 1's fixed hedge list: a sentence on a 🟡/❓ item uses one of these.</summary>
    public static readonly IReadOnlyList<string> Hedges =
        ["probably", "likely", "may", "might", "appears", "seems", "could", "perhaps", "possibly", "not known", "unknown", "isn't known", "unclear", "can't tell", "at run time"];

    static readonly StringComparer O = StringComparer.Ordinal;
    static string Key(string area, string section) => $"{area}#{section}";

    // ---- what's pending ---------------------------------------------------------------------------------------------------

    /// <summary>Each section's state against its current hash: new, changed, checked, fallback. "In short" is last.</summary>
    public static JsonObject Sections(StangentRepo repo, AreaSummary s)
    {
        var settings = AiSettings.Resolve(repo.Config, Writer);
        var runs = LoadRuns(repo.Root);
        var list = new JsonArray();
        foreach (var sec in s.Sections)
        {
            var run = runs[Key(s.Area, sec.Id)] as JsonObject;
            var hash = Hash(s, sec, runs, settings);
            var state = run is null ? "new" : (string?)run["hash"] != hash ? "changed" : (string)run["words"]!;
            list.Add(new JsonObject { ["area"] = s.Area, ["section"] = sec.Id, ["title"] = sec.Title, ["items"] = sec.Items.Count, ["state"] = state });
        }
        // The batch driver runs "In short" alone, after the others.
        var shortRow = list[0]!;
        list.RemoveAt(0);
        list.Add(shortRow);
        return new JsonObject { ["area"] = s.Area, ["model"] = settings.Model, ["effort"] = settings.Effort, ["sections"] = list };
    }

    // ---- prepare ----------------------------------------------------------------------------------------------------------

    public static RunStep Prepare(StangentRepo repo, AreaSummary s, string sectionId, bool retryFailed = false)
    {
        if (!StangentComponents.IsOn(repo.Config, "pages"))
            return new RunStep("off", "none", Errors: ["layer 4's pages are off (components.pages: true turns them on)"]);
        var sec = s.Find(sectionId) ?? throw new StangentException("not_found", $"No section '{sectionId}' in {s.Area}. `stangent pages sections \"{s.Area}\"` lists them.", ExitCodes.NotFound);
        var settings = AiSettings.Resolve(repo.Config, Writer);
        var runs = LoadRuns(repo.Root);
        if (sec.Id == PageSummary.InShort)
        {
            var waiting = s.Sections.Where(x => x.Id != PageSummary.InShort && (runs[Key(s.Area, x.Id)] as JsonObject)?["hash"]?.GetValue<string>() != Hash(s, x, runs, settings)).Select(x => x.Id).ToList();
            if (waiting.Count > 0) return new RunStep("waiting", "none", Errors: [$"\"In short\" is written last, from the checked sections; not written yet: {string.Join(", ", waiting)}"]);
        }
        var bundle = Bundle(s, sec, runs);
        var hash = HashOf(bundle, settings);
        var run = runs[Key(s.Area, sec.Id)] as JsonObject;
        if ((string?)run?["hash"] == hash && ((string?)run!["words"] == "checked" || !retryFailed))
            return new RunStep((string)run["words"]! == "checked" ? "current" : "fallback", "none",
                Errors: (string)run["words"]! == "checked" ? null : ["kept the code's list with these same items (--retry-failed tries again)"]);
        AiPolicy.EnsureAllowed(repo.Config, "pages prepare");
        var work = Work(repo.Root, s.Folder, sec.Id);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "writer.md"), bundle);
        Save(work, new JsonObject { ["area"] = s.Area, ["section"] = sec.Id, ["hash"] = hash, ["retryUsed"] = false, ["model"] = settings.Model, ["effort"] = settings.Effort, ["usage"] = new JsonObject() });
        return Step(repo.Root, work);
    }

    static RunStep Step(string root, string work) =>
        new("write", "write", Writer, $"Area section: {Rel(root, Path.Combine(work, "writer.md"))}\nWrite your JSON to: {Rel(root, Path.Combine(work, "writer.json"))}",
            Rel(root, Path.Combine(work, "writer.json")), Schema: OutputSchema);

    // ---- check ------------------------------------------------------------------------------------------------------------

    public static RunStep Check(StangentRepo repo, AreaSummary s, string sectionId, Usage? usage)
    {
        var work = Work(repo.Root, s.Folder, sectionId);
        if (PageRun.LoadJson(Path.Combine(work, "state.json")) is not JsonObject state)
            throw new StangentException("not_prepared", $"No run for {s.Area} § {sectionId}. Run `stangent pages prepare` first.", ExitCodes.Usage);
        var sec = s.Find(sectionId) ?? throw new StangentException("not_found", $"No section '{sectionId}' in {s.Area} any more.", ExitCodes.NotFound);
        var settings = AiSettings.Resolve(repo.Config, Writer);
        var runs = LoadRuns(repo.Root);
        var bundle = Bundle(s, sec, runs);
        if ((string?)state["hash"] != HashOf(bundle, settings))
            throw new StangentException("section_changed", $"{s.Area} § {sectionId}'s items changed during this run. Run `stangent pages prepare` to start again.", ExitCodes.Usage);
        state["usage"] = Usage.FromJson(state["usage"]).Add(usage ?? Usage.None).ToJson();

        var errors = new List<string>();
        var text = File.Exists(Path.Combine(work, "writer.json")) ? File.ReadAllText(Path.Combine(work, "writer.json")) : "";
        var output = PageChecks.Parse(text, errors);
        if (errors.Count == 0 && output is null) errors.Add("The output is empty.");
        if (errors.Count == 0) errors.AddRange(WordChecks.Check(output!, Citable(s, sec, runs)));
        if (errors.Count > 0)
        {
            if (!(bool)state["retryUsed"]!)
            {
                state["retryUsed"] = true;
                Save(work, state);
                var sb = new StringBuilder("# The code check failed\n\nYour output wasn't accepted. Fix every problem below and write the whole output again (one more failure and this section keeps the code's list instead of your words).\n\n");
                foreach (var e in errors.Take(40)) sb.Append($"- {e}\n");
                File.WriteAllText(Path.Combine(work, "feedback.md"), sb.ToString());
                if (File.Exists(Path.Combine(work, "writer.json"))) File.Move(Path.Combine(work, "writer.json"), Path.Combine(work, "previous-writer.json"), overwrite: true);
                else File.WriteAllText(Path.Combine(work, "previous-writer.json"), "(no output)");
                var step = Step(repo.Root, work);
                return step with
                {
                    Status = "retry", Errors = errors,
                    Prompt = step.Prompt + $"\nYour previous attempt: {Rel(repo.Root, Path.Combine(work, "previous-writer.json"))}\nWhat to fix: {Rel(repo.Root, Path.Combine(work, "feedback.md"))}",
                };
            }
            Record(repo, s, state, "fallback", null, errors);
            return new RunStep("fallback", "none", Errors: errors);
        }
        Record(repo, s, state, "checked", (JsonObject)output!, null);
        return new RunStep("checked", "none");
    }

    static void Record(StangentRepo repo, AreaSummary s, JsonObject state, string status, JsonObject? output, List<string>? errors)
    {
        var root = repo.Root;
        using (Lock(root))
        {
            var runs = LoadRuns(root);
            var key = Key(s.Area, (string)state["section"]!);
            var old = runs[key] as JsonObject;
            var usage = Usage.FromJson(state["usage"]);
            var run = new JsonObject
            {
                ["words"] = status, ["hash"] = state["hash"]!.DeepClone(), ["model"] = state["model"]!.DeepClone(), ["effort"] = state["effort"]!.DeepClone(),
                ["retried"] = (bool)state["retryUsed"]!, ["writer"] = usage.ToJson(),
                ["spentUsd"] = Math.Round(((double?)old?["spentUsd"] ?? 0) + usage.CostUsd, 6),
            };
            if (output is not null) run["paragraphs"] = output["paragraphs"]!.DeepClone();
            if (errors is not null) run["errors"] = new JsonArray([.. errors.Take(10).Select(e => (JsonNode)e)]);
            runs[key] = run;
            Quotes.WriteAtomic(Path.Combine(root, RunsFile), CanonicalJson.Serialize(runs));
            WritePage(repo, s, runs);
        }
        Save(Work(root, s.Folder, (string)state["section"]!), state);
    }

    /// <summary>Rewrites the words between D233's two lines from the recorded runs; the rest of the page stays byte for byte.</summary>
    public static void WritePage(StangentRepo repo, AreaSummary s, JsonObject runs)
    {
        var root = repo.Root;
        var settings = AiSettings.Resolve(repo.Config, Writer);
        var hashes = s.Sections.ToDictionary(x => x.Id, x => Hash(s, x, runs, settings), O);
        var path = Path.Combine(root, AreaLayout.Root, s.Folder, AreaLayout.Page);
        if (!File.Exists(path))
            throw new StangentException("not_found", $"No {AreaLayout.Root}/{s.Folder}/{AreaLayout.Page}: run `stangent areas check` first.", ExitCodes.NotFound);
        var page = File.ReadAllText(path);
        var start = page.IndexOf(AreaPages.WordsStart + "\n", StringComparison.Ordinal);
        var end = page.IndexOf(AreaPages.WordsEnd, StringComparison.Ordinal);
        if (start < 0 || end < start)
            throw new StangentException("page_markers_missing", $"{AreaLayout.Root}/{s.Folder}/{AreaLayout.Page} has lost its layer 4 lines: run `stangent areas check`.", ExitCodes.Usage);
        var from = start + AreaPages.WordsStart.Length + 1;
        var text = page[..from] + Render(s, runs, hashes) + page[end..];
        if (text != page) Quotes.WriteAtomic(path, text);
    }

    // ---- the page's words -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The words between the two lines (D253): a contents list, "In short", each section in page order with "On its own" over the
    /// handlers and jobs, then "Can't tell from the code". A section the writer passed shows its sentences, each followed by its items
    /// in a hidden note; one that fell back, or isn't written for its current items, shows the code's list and says so.
    /// </summary>
    public static string Render(AreaSummary s, JsonObject runs, IReadOnlyDictionary<string, string> hashes)
    {
        var sb = new StringBuilder(WordsMarker).Append("\n\n");
        sb.Append("**On this page:** ");
        var titles = new List<string>();
        string? lastGroup = null;
        foreach (var sec in s.Sections)
        {
            if (sec.Group != "" && sec.Group != lastGroup) titles.Add(sec.Group);
            lastGroup = sec.Group;
            if (sec.Group == "") titles.Add(sec.Title);
        }
        var cant = s.Sections.SelectMany(x => x.Items.Where(i => i.Label == "unknown").Select(i => (Sec: x, Item: i))).ToList();
        if (cant.Count > 0) titles.Add("Can't tell from the code");
        sb.Append(string.Join(" · ", titles.Select(Esc))).Append("\n\n");
        string? group = null;
        foreach (var sec in s.Sections)
        {
            if (sec.Group != "" && sec.Group != group) sb.Append($"## {sec.Group}\n\n");
            group = sec.Group;
            sb.Append($"{(sec.Group != "" ? "###" : "##")} {Esc(sec.Title)}\n\n");
            var run = runs[Key(s.Area, sec.Id)] as JsonObject;
            if (run is not null && (string?)run["hash"] != hashes.GetValueOrDefault(sec.Id)) run = null;   // written for items that changed since
            if (run is not null && (string)run["words"]! == "checked" && run["paragraphs"] is JsonArray ps)
            {
                foreach (var p in ps.OfType<JsonObject>())
                {
                    var line = new StringBuilder();
                    if ((string?)p["lead"] is { Length: > 0 } lead) line.Append($"**{Esc(lead.TrimEnd('.', ':'))}.** ");
                    line.Append(string.Join(" ", p["sentences"]!.AsArray().OfType<JsonObject>().Select(x =>
                        $"{Esc((string)x["text"]!)} <!-- {string.Join(" ", x["items"]!.AsArray().Select(i => (string)i!))} -->")));
                    sb.Append(line).Append("\n\n");
                }
                continue;
            }
            if (sec.Id == PageSummary.InShort && sec.Items.Count == 0) { sb.Append("*Not written yet: \"In short\" is written last, from the other sections.*\n\n"); continue; }
            sb.Append(run is not null && (string)run["words"]! == "fallback"
                ? "*The words for this section couldn't be checked against the code, so here is the code's list instead.*\n\n"
                : "*Not written yet: here is the code's list.*\n\n");
            foreach (var i in sec.Items) sb.Append(PageSummary.ItemLine(i, false));
            sb.Append('\n');
        }
        if (cant.Count > 0)
        {
            sb.Append("## Can't tell from the code\n\n");
            // One line per place and section: the same ❓ reached along two paths is one line here.
            foreach (var line in cant.Select(x => PageSummary.ItemLine(x.Item, false).TrimEnd('\n').Split('\n')[0] + $" (in {Esc(x.Sec.Title)})").Distinct(O)) sb.Append(line).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static string Esc(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c is '\\' or '[' or ']' or '*' or '_' or '`' or '<') sb.Append('\\');
            sb.Append(c is '\n' or '\r' ? ' ' : c);
        }
        return sb.ToString();
    }

    // ---- bundles and hashes -----------------------------------------------------------------------------------------------

    /// <summary>D254's hash: the bundle (items, rules, schema), the model and the effort. A model change re-runs every section (D256).</summary>
    static string HashOf(string bundle, AiSettings settings) => CanonicalJson.Hash($"{bundle}\n{settings.Model}\n{settings.Effort}");

    static string Hash(AreaSummary s, SummarySection sec, JsonObject runs, AiSettings settings) => HashOf(Bundle(s, sec, runs), settings);

    /// <summary>
    /// What a section's sentences may cite: its own items, and for "In short" also the items the other sections' checked sentences
    /// cite (or a fallen-back section's must-cite items), each as <c>§n:ID</c>.
    /// </summary>
    static Dictionary<string, SummaryItem> Citable(AreaSummary s, SummarySection sec, JsonObject runs)
    {
        var map = sec.Items.ToDictionary(i => i.Id, i => i, O);
        if (sec.Id != PageSummary.InShort) return map;
        // The other sections' items are there to cite, never required: WordChecks only requires ids without a section.
        foreach (var (_, n, items) in Others(s, runs))
            foreach (var i in items) map[$"§{n}:{i.Id}"] = i;
        return map;
    }

    static IEnumerable<(SummarySection Section, int N, List<SummaryItem> Items)> Others(AreaSummary s, JsonObject runs)
    {
        var n = 0;
        foreach (var other in s.Sections.Where(x => x.Id != PageSummary.InShort))
        {
            n++;
            var run = runs[Key(s.Area, other.Id)] as JsonObject;
            var cited = run?["paragraphs"] is JsonArray ps
                ? ps.OfType<JsonObject>().SelectMany(p => p["sentences"]!.AsArray().OfType<JsonObject>()).SelectMany(x => x["items"]!.AsArray().Select(i => (string)i!)).ToHashSet(O)
                : null;
            yield return (other, n, [.. other.Items.Where(i => cited is null ? i.MustCite : cited.Contains(i.Id))]);
        }
    }

    public static string Bundle(AreaSummary s, SummarySection sec, JsonObject runs)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions(Instructions));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(OutputSchema).TrimEnd('\n')).Append("\n```\n\n");
        sb.Append($"# The section\n\n- area: {s.Area}\n- section: {sec.Title}{(sec.Group != "" ? $" (under \"{sec.Group}\")" : "")}\n");
        if (sec.Id == PageSummary.InShort)
        {
            sb.Append("\nThis is the page's first section, \"In short\": three to six sentences on what this part of the app is for, what people do in it, ");
            sb.Append("and when it refuses, drawn from the sections below. Cite their items as written (`§2:A4`). Its own items, if any, must be cited.\n");
        }
        sb.Append("\n# The items\n\nEach item: its id, ✅ certain / 🟡 guessed / ❓ not known, what the code does, where, and the conditions on the way to it, numbered. ");
        sb.Append("**must cite** marks the ones every answer must cite.\n\n");
        foreach (var i in sec.Items) sb.Append(Line(i, i.Id));
        if (sec.Id == PageSummary.InShort)
            foreach (var (other, n, items) in Others(s, runs))
            {
                sb.Append($"\n## §{n} {other.Title}{(other.Group != "" ? $" (under \"{other.Group}\")" : "")}\n\n");
                var run = runs[Key(s.Area, other.Id)] as JsonObject;
                if (run?["paragraphs"] is JsonArray ps)
                {
                    sb.Append("Its checked words:\n\n");
                    foreach (var p in ps.OfType<JsonObject>())
                        sb.Append("> ").Append(string.Join(" ", p["sentences"]!.AsArray().OfType<JsonObject>().Select(x => $"{(string)x["text"]!} [{string.Join(" ", x["items"]!.AsArray().Select(i => $"§{n}:{(string)i!}"))}]"))).Append('\n');
                    sb.Append("\nThe items they cite:\n\n");
                }
                else sb.Append("Its words couldn't be checked; its must-cite items:\n\n");
                foreach (var i in items) sb.Append(Line(i, $"§{n}:{i.Id}", optional: true));
            }
        return sb.ToString();
    }

    static string Line(SummaryItem i, string id, bool optional = false)
    {
        var sb = new StringBuilder($"- **{id}** {i.Mark} {i.Text}");
        if (i.Reason is not null && i.Label != "certain") sb.Append($" ({(i.Label == "guessed" ? "guessed" : "not known")}: {i.Reason})");
        if (i.MustCite && !optional) sb.Append(" — **must cite**");
        sb.Append($"\n  - at `{i.At}`{(i.Code != "" ? $": `{i.Code.Replace("`", "'")}`" : "")}{(i.AlsoAt.Count > 0 ? $" (and {i.AlsoAt.Count} other place{(i.AlsoAt.Count == 1 ? "" : "s")} like it)" : "")}\n");
        for (var n = 0; n < i.Conditions.Count; n++) sb.Append($"  - condition {n + 1}: {i.Conditions[n].Text}\n");
        return sb.ToString();
    }

    // ---- files ------------------------------------------------------------------------------------------------------------

    public static JsonObject LoadRuns(string root) => PageRun.LoadJson(Path.Combine(root, RunsFile)) as JsonObject ?? new JsonObject();

    static string Work(string root, string folder, string section) => Path.Combine(root, ".stangent", "pages", folder, section);
    static void Save(string work, JsonObject state) { Directory.CreateDirectory(work); File.WriteAllText(Path.Combine(work, "state.json"), CanonicalJson.Serialize(state)); }
    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    static FileStream Lock(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".stangent", "pages"));
        var path = Path.Combine(root, ".stangent", "pages", "files.lock");
        for (var i = 0; ; i++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(50); }
        }
    }
}

/// <summary>D251/D255 item 1's checks on one section's answer, by code. Problems, empty when it passes.</summary>
public static class WordChecks
{
    static readonly StringComparer O = StringComparer.Ordinal;

    public static List<string> Check(JsonNode output, IReadOnlyDictionary<string, SummaryItem> items)
    {
        var errors = SchemaRegistry.Embedded.Validate(AreaWords.OutputSchema, output.ToJsonString()).Select(e => $"schema: {e}").ToList();
        if (errors.Count > 0) return errors;
        var cited = new HashSet<string>(O);
        var n = 0;
        foreach (var p in output["paragraphs"]!.AsArray().OfType<JsonObject>())
        {
            if ((string?)p["lead"] is { } lead && Markup(lead) is { } bad) errors.Add($"a lead \"{lead}\" {bad}");
            foreach (var sentence in p["sentences"]!.AsArray().OfType<JsonObject>())
            {
                n++;
                var text = (string)sentence["text"]!;
                var label = $"sentence {n} (\"{(text.Length > 60 ? text[..60] + "…" : text)}\")";
                if (Markup(text) is { } why) errors.Add($"{label} {why}");
                var ids = sentence["items"]!.AsArray().Select(i => (string)i!).Distinct(O).ToList();
                if (ids.Count == 0) { errors.Add($"{label} cites no item"); continue; }
                foreach (var id in ids.Where(id => !items.ContainsKey(id))) errors.Add($"{label} cites {id}, which isn't one of this section's items");
                var known = ids.Where(items.ContainsKey).ToList();
                cited.UnionWith(known);
                if (known.Any(id => items[id].Label != "certain") && !Hedged(text))
                    errors.Add($"{label} cites {string.Join(", ", known.Where(id => items[id].Label != "certain"))} (guessed or not known) without a hedge word ({string.Join(", ", AreaWords.Hedges)})");
                var marks = sentence["conditions"]!.AsArray().OfType<JsonObject>().Select(m => ((string)m["item"]!, (int)m["condition"]!)).ToList();
                foreach (var (item, c) in marks)
                {
                    if (!known.Contains(item)) errors.Add($"{label} marks a condition of {item}, which it doesn't cite");
                    else if (c > items[item].Conditions.Count) errors.Add($"{label} marks condition {c} of {item}, which has {items[item].Conditions.Count}");
                }
                foreach (var id in known)
                    for (var c = 1; c <= items[id].Conditions.Count; c++)
                        if (!marks.Contains((id, c))) errors.Add($"{label} cites {id} but doesn't mark its condition {c} ({items[id].Conditions[c - 1].Text}) as said or always");
            }
        }
        foreach (var (id, i) in items.Where(kv => !kv.Key.Contains(':') && kv.Value.MustCite && !cited.Contains(kv.Key))) errors.Add($"{id} must be cited and no sentence cites it ({i.Text})");
        return errors;
    }

    static string? Markup(string text) =>
        text.Contains('\n') || text.Contains('\r') ? "holds a line break"
        : text.Contains("<!--", StringComparison.Ordinal) || text.Contains("-->", StringComparison.Ordinal) || text.Contains('<') ? "holds HTML"
        : text.Contains("](", StringComparison.Ordinal) || text.Contains("**", StringComparison.Ordinal) || text.Contains('`') ? "holds Markdown"
        : null;

    public static bool Hedged(string text)
    {
        var lower = text.ToLowerInvariant().Replace('’', '\'');
        return AreaWords.Hedges.Any(h => Regex.IsMatch(lower, $@"(?<![a-z']){Regex.Escape(h)}(?![a-z])"));
    }
}
