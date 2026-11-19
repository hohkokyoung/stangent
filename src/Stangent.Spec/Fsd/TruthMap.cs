using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd rulings-check &lt;doc&gt;</c> (D363, D366 (3), contract <c>truth-map</c> v1): checks <c>rulings.yaml</c>
/// (every value one of its state's, restated with a statement, no entry without a key), takes who ruled and when from
/// <c>git blame</c> on each <c>ruling</c> or <c>matches</c> line, lists the medium and low matches first, counts, and
/// writes <c>truth-map.json</c> (for agents) and the PM's view (D415: <c>specs/fsd/index.md</c>, one summary per area and one
/// file per module, <see cref="TruthView"/>), the same bytes from the same inputs. Code only.
/// </summary>
public static partial class TruthMap
{
    public const string Contract = "truth-map";
    public const int Version = 3;
    public const string Schema = "truth-map.v3.json";
    public static readonly string[] Standings = ["blank", "ruled", "flagged", "not-found", "confirmed", "unconfirmed", "not-a-match"];
    public static string JsonPath(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "truth-map.json");
    /// <summary>The single PM file before D415; removed when the view is written, so nobody reads a stale one.</summary>
    public static string OldMarkdownPath(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "truth-map.md");

    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Where an entry stands for a ticket (layer 4): not found again and flagged first, since either needs the PM.</summary>
    public static string Standing(RulingEntry e, RulingSection? section)
    {
        if (e.NotFound is not null) return "not-found";
        if (e.Flagged is not null) return "flagged";
        if (e.IsMatch)
            return e.Ruling == "not-a-match" ? "not-a-match" : MatchConfirmed(e, section) ? "confirmed" : "unconfirmed";
        // D417: a keep with no sentence says nothing about how the kept code should work, so it counts as not answered.
        return e.Ruling is null || e.NeedsWords ? "blank" : "ruled";
    }

    static bool MatchConfirmed(RulingEntry e, RulingSection? section) =>
        e.Ruling == "confirmed" || (e.Ruling is null && section is { MatchesConfirmed: true } && !e.New);

    /// <summary>The checks on the file; empty when it is fine.</summary>
    public static List<string> Problems(RulingsFile file, List<string> parseProblems)
    {
        var problems = new List<string>(parseProblems);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in file.Sections)
        {
            if (s.Matches is { } m && m != "confirmed")
                problems.Add($"section {s.Section}: matches \"{m}\" isn't confirmed (leave it blank until the matches look right)");
            foreach (var e in s.Entries)
                if (e.Problem is null && e.KeySection != s.SectionId)
                    problems.Add($"{e.Describe()}: listed under section {s.SectionId}, but its key names {e.KeySection ?? "no section"} (entries aren't moved by hand)");
        }
        foreach (var e in file.Undocumented)
            if (e.Problem is null && e.KeySection is not null)
                problems.Add($"{e.Describe()}: listed with the code no section covers, but its key names section {e.KeySection}");
        foreach (var (_, e) in file.WithSections)
        {
            var where = e.Describe();
            if (e.Problem is { } p) { problems.Add($"{where}: {p}"); continue; }
            if (!seen.Add(e.Key)) problems.Add($"{where}: two entries have this key");
            if (!FsdReview.States.Contains(e.State)) { problems.Add($"{where}: state \"{e.State}\" isn't one of {string.Join(", ", FsdReview.States)}"); continue; }
            var allowed = Rulings.RulingsFor(e.State);
            if (e.Ruling is { } r && !allowed.Contains(r))
                problems.Add($"{where}: ruling \"{r}\" isn't one of {string.Join(", ", allowed)} (a {e.State})");
            if (e.Ruling == "restated" && e.Statement is null)
                problems.Add($"{where}: a restated ruling needs a statement, the rule in your words");
        }
        return problems;
    }

    /// <summary><c>fsd rulings-check &lt;doc&gt;</c>: the checks, then the truth map's two files. Problems write nothing.</summary>
    public static JsonObject Check(StangentRepo repo, string doc, out bool ok)
    {
        var root = repo.Root;
        var path = Rulings.PathFor(root, doc);
        if (!File.Exists(path))
            throw new StangentException("no_rulings", $"{doc} has no {Rel(root, path)} yet. Run `stangent fsd rulings {doc}` first.", ExitCodes.NotFound);
        var yaml = File.ReadAllText(path);
        var (file, parseProblems) = Rulings.Parse(yaml);
        var problems = Problems(file, parseProblems);
        var blame = Blame(root, path);
        // D417: a new bare keep is refused; one already committed counts as blank and is listed until it has the PM's words.
        var needsWords = new JsonArray();
        foreach (var (section, e) in file.WithSections.Where(x => x.Entry.Problem is null && x.Entry.NeedsWords))
        {
            if (Who(e, section, blame) is { Committed: false })
                problems.Add($"{e.Describe()}: keep needs a sentence in the PM's words, what the kept code should do (`stangent fsd rule {doc} <number> keep --words \"…\"`, D417)");
            else needsWords.Add((JsonNode)e.Describe());
        }
        var warnings = new JsonArray();
        var reviewPath = FsdReview.ReviewJson(root, doc);
        if (!File.Exists(reviewPath)) warnings.Add((JsonNode)$"{Rel(root, reviewPath)} is gone; the rulings are read as they are.");
        else if (CanonicalJson.Hash(File.ReadAllText(reviewPath)) != file.ReviewHash)
            warnings.Add((JsonNode)$"review.json changed since the last `stangent fsd rulings {doc}`; run it, so the rulings follow the review.");
        JsonObject document;
        try { document = FsdOutline.Load(root, doc); }
        catch (StangentException) { throw new StangentException("not_imported", $"{doc} isn't imported: the truth map shows each section's text, so `stangent fsd import` it first.", ExitCodes.NotFound); }

        var entries = file.WithSections.ToList();
        var standings = entries.Select(x => Standing(x.Entry, x.Section)).ToList();
        var report = new JsonObject
        {
            ["doc"] = doc, ["file"] = Rel(root, path), ["ok"] = problems.Count == 0,
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        };
        var counts = Counts(entries, standings, blame);
        var linkCounts = TruthLink.Kinds.ToDictionary(k => k, k => entries.Count(x => x.Entry.Link.Kind == k), StringComparer.Ordinal);
        report["counts"] = counts;
        report["needsWords"] = needsWords;
        if (needsWords.Count > 0) warnings.Add((JsonNode)$"{needsWords.Count} keep ruling{(needsWords.Count == 1 ? " has" : "s have")} no sentence: {(needsWords.Count == 1 ? "it counts" : "they count")} as not answered until the PM says what the kept code should do (D417).");
        report["links"] = LinkCounts(linkCounts, entries.Count);
        report["fallbackShare"] = report["links"]!["fallbackShare"]!.DeepClone();
        report["sections"] = new JsonArray([.. file.Sections.Select(s => (JsonNode)SectionCounts(s))]);
        report["verifyFirst"] = new JsonArray([.. VerifyFirst(entries).Select(x => (JsonNode)new JsonObject
        {
            ["section"] = x.Section?.Section, ["title"] = x.Section?.Title, ["confidence"] = x.Entry.Confidence, ["entry"] = x.Entry.Describe(),
            ["evidence"] = new JsonArray([.. x.Entry.Evidence.Select(v => (JsonNode)v)]),
        })]);
        var uncommitted = entries.Count(x => Who(x.Entry, x.Section, blame) is { Committed: false });
        if (uncommitted > 0) warnings.Add((JsonNode)$"{uncommitted} ruling{(uncommitted == 1 ? " isn't" : "s aren't")} committed yet: who ruled and when stay empty until {(uncommitted == 1 ? "it is" : "they are")}; commit, then run this again.");
        // D364: a ruling drafted from a Jira reply shows who wrote it there and who confirmed it in git, both when they differ.
        report["fromJira"] = new JsonArray([.. entries.Where(x => x.Entry.Jira is not null).Select(x =>
        {
            var who = Who(x.Entry, x.Section, blame);
            var author = (string?)x.Entry.Jira!["author"];
            return (JsonNode)new JsonObject
            {
                ["entry"] = x.Entry.Describe(), ["ruling"] = x.Entry.Ruling, ["ticket"] = (string?)x.Entry.Jira["key"], ["comment"] = (string?)x.Entry.Jira["comment"],
                ["jiraAuthor"] = author, ["committed"] = who?.Committed ?? false, ["committer"] = who?.By,
                ["differ"] = who is { Committed: true } && !SamePerson(who.By, author),
            };
        })]);
        // D386: an entry written into the FSD, with the ruling it was written from, who ruled, and who committed the words.
        report["written"] = new JsonArray([.. entries.Where(x => x.Entry.WrittenFrom is not null).Select(x =>
        {
            var who = Who(x.Entry, x.Section, blame);
            var w = x.Entry.WrittenFrom!;
            return (JsonNode)new JsonObject
            {
                ["entry"] = x.Entry.Describe(), ["from"] = (string?)w["ruling"], ["replaced"] = (string?)w["quote"], ["statement"] = (string?)w["statement"],
                ["ruledBy"] = (string?)w["by"] ?? (who is { Committed: true } ? who.By : null), ["committed"] = who?.Committed ?? false, ["committer"] = who?.By,
                ["jiraAuthor"] = (string?)x.Entry.Jira?["author"],
                ["words"] = Written(w, new JsonObject { ["committed"] = who?.Committed, ["by"] = who?.By, ["on"] = who?.On, ["jira"] = x.Entry.Jira?.DeepClone() }),
            };
        })]);
        var drafted = report["fromJira"]!.AsArray().Count(x => (bool)x!["committed"]! == false);
        if (drafted > 0) warnings.Add((JsonNode)$"{drafted} ruling{(drafted == 1 ? " was" : "s were")} drafted from a Jira reply and {(drafted == 1 ? "isn't" : "aren't")} committed: {(drafted == 1 ? "it backs" : "they back")} no ticket until a person confirms by committing.");
        report["warnings"] = warnings;
        // D414: a listed control (asked only when a ticket lands on it) doesn't hold up "done".
        report["done"] = problems.Count == 0 && standings.Where((_, i) => !entries[i].Entry.OnDemand).All(s => s is "ruled" or "confirmed" or "not-a-match");
        report["summary"] = Summary(counts, entries.Count) + "; links: " + TruthLinks.Summary(entries.Count, linkCounts, "entry", "entries");
        ok = problems.Count == 0;
        if (!ok) return report;

        var map = Build(root, doc, file, document, entries, blame, counts, linkCounts, CanonicalJson.Hash(yaml));
        var errors = SchemaRegistry.Embedded.Validate(Schema, map.ToJsonString());
        if (errors.Count > 0) throw new StangentException("bad_truth_map", $"truth-map.json doesn't match its contract: {errors[0]}");
        Quotes.WriteAtomic(JsonPath(root, doc), CanonicalJson.Serialize(map));
        if (File.Exists(OldMarkdownPath(root, doc))) File.Delete(OldMarkdownPath(root, doc));
        var view = TruthView.Write(root, doc, map);
        report["truthMap"] = Rel(root, JsonPath(root, doc));
        report["view"] = view;
        return report;
    }

    static JsonObject Counts(List<(RulingSection? Section, RulingEntry Entry)> entries, List<string> standings, IReadOnlyDictionary<int, BlameLine> blame)
    {
        var o = new JsonObject { ["entries"] = entries.Count };
        foreach (var s in Standings) o[s] = standings.Count(x => x == s);
        o["byState"] = new JsonObject([.. FsdReview.States.Select(s => KeyValuePair.Create(s, (JsonNode?)entries.Count(x => x.Entry.State == s)))]);
        var unconfirmed = entries.Where((x, i) => standings[i] == "unconfirmed").ToList();
        o["unconfirmedMatches"] = new JsonObject([.. new[] { "high", "medium", "low" }.Select(c => KeyValuePair.Create(c, (JsonNode?)unconfirmed.Count(x => x.Entry.Confidence == c)))]);
        o["uncommitted"] = entries.Count(x => Who(x.Entry, x.Section, blame) is { Committed: false });
        o["listed"] = entries.Count(x => x.Entry.OnDemand);
        o["needsWords"] = entries.Count(x => x.Entry.NeedsWords);
        return o;
    }

    static JsonObject LinkCounts(IReadOnlyDictionary<string, int> counts, int total)
    {
        var fallback = counts["node"] + counts["file"];
        var o = new JsonObject([.. TruthLink.Kinds.Select(k => KeyValuePair.Create(k, (JsonNode?)counts[k]))]);
        o["fallback"] = fallback;
        o["fallbackShare"] = total == 0 ? 0 : Math.Round(100.0 * fallback / total, 1);
        return o;
    }

    static JsonObject SectionCounts(RulingSection s)
    {
        var st = s.Entries.Select(e => Standing(e, s)).ToList();
        return new JsonObject
        {
            ["section"] = s.Section, ["sectionId"] = s.SectionId, ["title"] = s.Title, ["entries"] = s.Entries.Count,
            ["blank"] = st.Count(x => x == "blank"), ["ruled"] = st.Count(x => x == "ruled"), ["flagged"] = st.Count(x => x == "flagged"),
            ["notFound"] = st.Count(x => x == "not-found"), ["matches"] = s.Entries.Count(e => e.IsMatch),
            ["matchesConfirmed"] = st.Count(x => x == "confirmed"), ["unconfirmedMatches"] = st.Count(x => x == "unconfirmed"),
        };
    }

    /// <summary>"48 entries: 20 ruled, 25 blank, 2 flagged, 1 not found again; matches 0 confirmed, 3 to confirm, 0 not a match".</summary>
    static string Summary(JsonObject c, int total) =>
        $"{total} entr{(total == 1 ? "y" : "ies")}: {(int)c["ruled"]!} ruled, {(int)c["blank"]!} blank, {(int)c["flagged"]!} flagged, {(int)c["not-found"]!} not found again; "
        + $"matches {(int)c["confirmed"]!} confirmed, {(int)c["unconfirmed"]!} to confirm ({(int)c["unconfirmedMatches"]!["low"]!} low, {(int)c["unconfirmedMatches"]!["medium"]!} medium), {(int)c["not-a-match"]!} not a match";

    /// <summary>Unconfirmed matches of low, then medium confidence: the PM verifies them at setup (D366 (4)).</summary>
    static IEnumerable<(RulingSection? Section, RulingEntry Entry)> VerifyFirst(List<(RulingSection? Section, RulingEntry Entry)> entries) =>
        entries.Where(x => x.Entry.IsMatch && x.Entry.Confidence is "low" or "medium" && Standing(x.Entry, x.Section) == "unconfirmed")
            .OrderBy(x => x.Entry.Confidence == "low" ? 0 : 1);

    // ---- git blame ------------------------------------------------------------------------------------------------------

    public sealed record BlameLine(string Commit, string Author, string Date);
    public sealed record Person(bool Committed, string? By, string? On, string? Commit);

    /// <summary>Who last changed each line of the file, from <c>git blame</c> of the working file: a line not in a commit
    /// (changed or new) is left out.</summary>
    public static IReadOnlyDictionary<int, BlameLine> Blame(string root, string path)
    {
        var result = new Dictionary<int, BlameLine>();
        if (Git.Run(root, $"blame --line-porcelain -- \"{Rel(root, path)}\"") is not { } text) return result;
        string? commit = null;
        int line = 0;
        string author = "";
        long time = 0;
        foreach (var raw in text.Split('\n'))
        {
            if (raw.StartsWith('\t'))
            {
                if (commit is not null && commit.Trim('0').Length > 0)
                    result[line] = new BlameLine(commit, author, DateTimeOffset.FromUnixTimeSeconds(time).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                commit = null;
                continue;
            }
            var m = BlameHeader().Match(raw);
            if (m.Success) { commit = m.Groups["sha"].Value; line = int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture); continue; }
            if (raw.StartsWith("author ", StringComparison.Ordinal)) author = raw[7..];
            else if (raw.StartsWith("author-time ", StringComparison.Ordinal)) long.TryParse(raw[12..], CultureInfo.InvariantCulture, out time);
        }
        return result;
    }

    [GeneratedRegex(@"^(?<sha>[0-9a-f]{40}) \d+ (?<line>\d+)(?: \d+)?$")]
    private static partial Regex BlameHeader();

    /// <summary>Who made the entry's ruling: blame on its <c>ruling</c> line, or for a match its section's <c>matches</c>
    /// covers, on that line. Null when nobody has ruled.</summary>
    public static Person? Who(RulingEntry e, RulingSection? section, IReadOnlyDictionary<int, BlameLine> blame)
    {
        var line = e.Ruling is not null ? e.RulingLine : e.IsMatch && MatchConfirmed(e, section) ? section!.MatchesLine : 0;
        if (line == 0) return null;
        // A ruling drafted from Jira is committed only when its words and its jira line are too: a new statement under a committed
        // `ruling: restated` line mustn't count unconfirmed (D374).
        if (e.Jira is not null && new[] { e.StatementLine, e.JiraLine }.Any(l => l > 0 && !blame.ContainsKey(l))) return new Person(false, null, null, null);
        return blame.TryGetValue(line, out var b) ? new Person(true, b.Author, b.Date, b.Commit) : new Person(false, null, null, null);
    }

    // ---- truth-map.json ---------------------------------------------------------------------------------------------------

    static JsonObject Build(string root, string doc, RulingsFile file, JsonObject document, List<(RulingSection? Section, RulingEntry Entry)> entries,
        IReadOnlyDictionary<int, BlameLine> blame, JsonObject counts, IReadOnlyDictionary<string, int> linkCounts, string rulingsHash)
    {
        var flows = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var index = PageRun.LoadJson(Path.Combine(root, FlowFiles.IndexFile)) as JsonObject;
        var flowFiles = (index?["flows"]?.AsArray().OfType<JsonObject>() ?? []).ToDictionary(f => (string)f["id"]!, f => f, StringComparer.Ordinal);
        foreach (var (_, e) in entries)
            if (e.Link.Kind == "flow" && !flows.ContainsKey(e.Link.Id) && flowFiles.TryGetValue(e.Link.Id, out var entry))
                flows[e.Link.Id] = FlowSummary(root, entry);

        var blocks = file.Sections.ToDictionary(s => s.SectionId, StringComparer.Ordinal);
        var sections = new JsonArray();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in document["sections"]!.AsArray().OfType<JsonObject>())
        {
            var pid = (string?)s["permanentId"] ?? (string)s["id"]!;
            blocks.TryGetValue(pid, out var block);
            placed.Add(pid);
            sections.Add(SectionJson((string)s["id"]!, pid, (string)s["title"]!, (int)s["level"]!, (string?)s["idKind"] ?? "number", (string)s["text"]!, block, blame));
        }
        // A section the FSD no longer has, kept for rulings no run finds again.
        foreach (var block in file.Sections.Where(b => !placed.Contains(b.SectionId)))
            sections.Add(SectionJson(block.Section, block.SectionId, block.Title, 1, "number", null, block, blame));

        var verify = new JsonArray([.. VerifyFirst(entries).Select(x => (JsonNode)KeyJson(x.Entry))]);
        var undocumented = new JsonArray([.. file.Undocumented.Select(e => (JsonNode)EntryJson(e, null, blame))]);
        Number(sections, undocumented);
        return new JsonObject
        {
            ["contract"] = Contract, ["version"] = Version, ["doc"] = doc, ["title"] = (string?)document["title"] ?? doc,
            ["fsdVersion"] = (string?)document["original"]?["hash"] ?? "",
            ["review"] = new JsonObject { ["commit"] = file.ReviewCommit, ["fsdVersion"] = file.FsdVersion, ["runs"] = file.Runs },
            ["rulingsHash"] = rulingsHash,
            ["counts"] = counts.DeepClone(),
            ["links"] = LinkCounts(linkCounts, entries.Count),
            ["verifyFirst"] = verify,
            ["sections"] = sections,
            ["undocumented"] = undocumented,
            ["flows"] = new JsonObject([.. flows.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))]),
        };
    }

    /// <summary>The PM's numbers (D371, D414): section by section, a section's matches question first (when it has matches),
    /// then its entries; the code no section covers after them; then the listed controls (asked only when a ticket lands on
    /// one), in the same order, so every question comes before them. <c>fsd rule</c> takes a number back to its key through
    /// this file, so a number means what the PM read.</summary>
    static void Number(JsonArray sections, JsonArray undocumented)
    {
        var n = 1;
        static bool Listed(JsonObject e) => (bool?)e["onDemand"] == true;
        foreach (var s in sections.OfType<JsonObject>())
        {
            var m = s["matches"]!.AsObject();
            m["n"] = (int)m["count"]! > 0 ? n++ : null;
            foreach (var e in s["entries"]!.AsArray().OfType<JsonObject>().Where(e => !Listed(e))) e["n"] = n++;
        }
        foreach (var e in undocumented.OfType<JsonObject>().Where(e => !Listed(e))) e["n"] = n++;
        foreach (var s in sections.OfType<JsonObject>())
            foreach (var e in s["entries"]!.AsArray().OfType<JsonObject>().Where(Listed)) e["n"] = n++;
        foreach (var e in undocumented.OfType<JsonObject>().Where(Listed)) e["n"] = n++;
    }

    static JsonObject SectionJson(string id, string pid, string title, int level, string idKind, string? text, RulingSection? block, IReadOnlyDictionary<int, BlameLine> blame)
    {
        var matches = block?.Entries.Where(e => e.IsMatch).ToList() ?? [];
        var who = block is { MatchesConfirmed: true, MatchesLine: > 0 }
            ? blame.TryGetValue(block.MatchesLine, out var b) ? new Person(true, b.Author, b.Date, b.Commit) : new Person(false, null, null, null)
            : null;
        return new JsonObject
        {
            ["section"] = id, ["sectionId"] = pid, ["title"] = title, ["level"] = level, ["idKind"] = idKind, ["text"] = text,
            ["matches"] = new JsonObject
            {
                ["count"] = matches.Count, ["confirmed"] = block?.MatchesConfirmed == true,
                ["by"] = who?.By, ["on"] = who?.On, ["committed"] = who?.Committed,
            },
            ["entries"] = new JsonArray([.. (block?.Entries ?? []).Select(e => (JsonNode)EntryJson(e, block, blame))]),
        };
    }

    static JsonObject KeyJson(RulingEntry e) => e.KeyQuote is not null
        ? new JsonObject { ["section"] = e.KeySection, ["quote"] = e.KeyQuote }
        : new JsonObject { ["section"] = e.KeySection, ["link"] = e.KeyLink };

    static JsonObject EntryJson(RulingEntry e, RulingSection? section, IReadOnlyDictionary<int, BlameLine> blame)
    {
        var who = Who(e, section, blame);
        var o = new JsonObject
        {
            ["key"] = KeyJson(e), ["state"] = e.State, ["standing"] = Standing(e, section),
            ["link"] = new JsonObject { ["kind"] = e.Link.Kind, ["id"] = e.Link.Id, ["title"] = e.Link.Title, ["exact"] = e.Link.Exact },
            ["fallback"] = e.Link.Fallback,
            ["evidence"] = new JsonArray([.. e.Evidence.Select(v => (JsonNode)v)]),
            ["runs"] = new JsonArray([.. e.Runs.Select(r => (JsonNode)r)]),
            ["confidence"] = e.Confidence, ["disagrees"] = e.Disagrees,
            ["notes"] = new JsonArray([.. e.Notes.Select(n => (JsonNode)n)]),
            ["ruling"] = e.Ruling, ["statement"] = e.Statement, ["pmNote"] = e.PmNote,
            ["by"] = who?.By, ["on"] = who?.On, ["committed"] = who?.Committed,
            ["flagged"] = e.Flagged, ["notFound"] = e.NotFound, ["new"] = e.New,
            ["jira"] = e.Jira?.DeepClone(), ["writtenFrom"] = e.WrittenFrom?.DeepClone(),
        };
        // Only when true, so a map with neither reads as before (D414, D417).
        if (e.OnDemand) o["onDemand"] = true;
        if (e.NeedsWords) o["needsWords"] = true;
        return o;
    }

    /// <summary>A linked flow as the truth map shows it: its actor, steps (with their conditions), scenarios and endings, and
    /// how many of its steps are guesses (🟡) or unknown (❓).</summary>
    static JsonObject FlowSummary(string root, JsonObject entry)
    {
        var file = (string)entry["file"]!;
        var flow = PageRun.LoadJson(Path.Combine(root, file)) as JsonObject ?? [];
        // Each condition as the flow's Markdown reads it (v3, D420): the bare code read an earlier guard as its opposite.
        static JsonArray Conditions(JsonNode? n) => new([.. (n as JsonArray ?? []).OfType<JsonObject>().Select(FlowFiles.ConditionText).OfType<string>().Select(c => (JsonNode)c)]);
        return new JsonObject
        {
            ["name"] = (string?)flow["name"] ?? (string?)entry["name"] ?? (string)entry["id"]!,
            ["kind"] = (string?)flow["kind"] ?? (string?)entry["kind"] ?? "",
            ["file"] = file,
            ["actor"] = flow["actor"] is JsonObject a ? new JsonObject { ["who"] = (string?)a["who"], ["why"] = (string?)a["why"], ["label"] = (string?)a["label"] } : null,
            ["steps"] = new JsonArray([.. (flow["steps"] as JsonArray ?? []).OfType<JsonObject>().Select(s => (JsonNode)new JsonObject
            {
                ["n"] = s["n"]?.DeepClone(), ["what"] = (string?)s["what"], ["at"] = (string?)s["at"], ["label"] = (string?)s["label"],
                ["conditions"] = Conditions(s["conditions"]),
            })]),
            ["scenarios"] = new JsonArray([.. (flow["scenarios"] as JsonArray ?? []).OfType<JsonObject>().Select(s => (JsonNode)new JsonObject
            {
                ["name"] = (string?)s["name"], ["conditions"] = Conditions(s["conditions"]),
            })]),
            ["exits"] = new JsonArray([.. (flow["exits"] as JsonArray ?? []).OfType<JsonObject>().Select(x => (JsonNode)new JsonObject
            {
                ["kind"] = (string?)x["kind"], ["at"] = (string?)x["at"], ["conditions"] = Conditions(x["conditions"]),
            })]),
            ["guesses"] = (flow["guesses"] as JsonArray)?.Count ?? 0,
            ["unknowns"] = (flow["unknowns"] as JsonArray)?.Count ?? 0,
        };
    }

    // ---- words the PM's view shares (TruthView) ----------------------------------------------------------------------

    /// <summary>What the review found, in plain words.</summary>
    public static string StateWords(string state) => state switch
    {
        "gap" => "The FSD says it; the code doesn't do it",
        "conflict" => "The FSD and the code disagree",
        "undocumented" => "The code does this; the FSD doesn't say",
        _ => "The code does what the FSD says",
    };

    /// <summary>A ruling in plain words.</summary>
    public static string RulingWords(string ruling) => ruling switch
    {
        "code-right" => "the code is right (the FSD needs updating)",
        "fsd-right" => "the FSD is right (the code has a bug)",
        "restated" => "neither as written: the rule in your words",
        "keep" => "keep this code",
        "remove" => "remove this code",
        "confirmed" => "yes, the code does what the FSD says",
        "not-a-match" => "no, the code doesn't do what the FSD says",
        _ => ruling,
    };

    /// <summary>Where an entry stands, in plain words.</summary>
    internal static string StandingWords(JsonObject e)
    {
        var ruling = (string?)e["ruling"];
        var was = ruling is null ? "" : $" (answered: {RulingWords(ruling)} {By(e)})";
        return (string)e["standing"]! switch
        {
            "ruled" => $"answered: **{RulingWords(ruling!)}** {By(e)}",
            "confirmed" => ruling is null ? "confirmed with its section" : e["writtenFrom"] is JsonObject ? $"**written into the FSD** {By(e)}" : $"answered: **{RulingWords(ruling)}** {By(e)}",
            "not-a-match" => $"answered: **{RulingWords("not-a-match")}** {By(e)}",
            "unconfirmed" => "not confirmed yet",
            "blank" when (bool?)e["needsWords"] == true => $"answered **keep** {By(e)}, but with no sentence: **say what the kept code should do** (D417)",
            "blank" => "**not answered yet**",
            "flagged" => $"**look again**: {FlagWords((string)e["flagged"]!)}{was}",
            _ => $"**the review of `{(string)e["notFound"]!}` didn't find this again**{was}; answer `drop` to remove it",
        };
    }

    /// <summary>Why an item needs another look, in plain words (the reasons <c>fsd rulings</c> writes).</summary>
    internal static string FlagWords(string flag)
    {
        if (flag == Rulings.LinkChanged) return "the code it points at changed since it was answered";
        if (FlagReason().Match(flag) is { Success: true } m)
        {
            var what = m.Groups["why"].Value == "FSD section changed" ? "this FSD section changed" : "the FSD's sentence changed";
            return m.Groups["was"].Success
                ? $"{what} since it was answered (the answer was: {RulingWords(m.Groups["was"].Value)})"
                : $"{what} since this section's matches were confirmed";
        }
        if (StateFlag().Match(flag) is { Success: true } k)
            return $"the review now says \"{StateWords(k.Groups["now"].Value)}\"; it said \"{StateWords(k.Groups["was"].Value)}\", answered: {RulingWords(k.Groups["r"].Value)}";
        return flag;
    }

    [GeneratedRegex(@"^(?<why>FSD section changed|its FSD quote is no longer in the section) since (?:ruled \(was (?<was>[a-z-]+)\)|its matches were confirmed)$")]
    private static partial Regex FlagReason();

    [GeneratedRegex(@"^the review now calls this a (?<now>[a-z]+); it was a (?<was>[a-z]+), ruled (?<r>[a-z-]+)$")]
    private static partial Regex StateFlag();

    /// <summary>D386: "Written into the FSD from a code-right ruling by Pat PM on 2026-11-17, committed by Dana Lead on
    /// 2026-11-18; it replaced “…”". Who ruled is the blame recorded when it was written, else (ruled and written in one
    /// commit) the commit's author; who committed the words is blame on the entry's ruling line now.</summary>
    public static string Written(JsonObject w, JsonNode entry)
    {
        var from = (string?)w["ruling"] == "code-right" ? "a ruling that the code is right (an AI drafted the words, code checked them)" : "the PM's own words";
        var committed = (bool?)entry["committed"] == true;
        var by = (string?)w["by"] is { } b ? $" by {b} on {(string?)w["on"]}" : "";
        var jira = entry["jira"] is JsonObject j ? $" (in Jira by {(string?)j["author"]}, {(string?)j["key"]})" : "";
        var commit = committed ? (by.Length == 0 ? $" by {(string?)entry["by"]} on {(string?)entry["on"]}, in the commit that wrote it" : $", written in a commit by {(string?)entry["by"]} on {(string?)entry["on"]}") : ", written and _not committed_";
        var old = (string?)w["quote"] is { } q ? $"; it replaced: “{Quotes.Normalize(q)}”" : "; added for code no section covered";
        return $"Written into the FSD from {from}{jira}{by}{commit}{old}";
    }

    internal static string Heading(JsonObject s) => (string?)s["idKind"] == "number" ? $"{(string)s["section"]!} {(string)s["title"]!}" : (string)s["title"]!;

    internal static string Quote(JsonObject e) => (string?)e["key"]!["quote"] is { } q ? $"“{Quotes.Normalize(q)}”" : "";

    internal static string Found(JsonObject e, int runs)
    {
        var mine = e["runs"]!.AsArray().Select(r => (int)r!).ToList();
        if (mine.Count == 0 && e["jira"] is JsonObject j) return $"added from Jira {(string?)j["key"]}, not a review finding";
        var found = runs > 0 && mine.Count >= runs ? "found by every run" : $"found by run {string.Join(", ", mine)} of {runs}";
        return found + ((bool)e["fallback"]! ? ", linked by fallback" : "");
    }

    /// <summary>Who ruled: git's committer, and for a ruling drafted from a Jira reply (D364, D374) who wrote it there, both
    /// when they differ.</summary>
    internal static string By(JsonNode n)
    {
        var git = (bool?)n["committed"] == false ? "(_not committed_)" : (string?)n["by"] is { } by ? $"by {by} on {(string?)n["on"]}" : "";
        if (n["jira"] is not JsonObject j) return git;
        var jira = $"in Jira by {(string?)j["author"]} ({(string?)j["key"]}, comment {(string?)j["comment"]})";
        if ((bool?)n["committed"] == false) return $"{jira}, drafted, (_not committed_)";
        return SamePerson((string?)n["by"], (string?)j["author"]) ? $"{jira}, committed on {(string?)n["on"]}" : $"{jira}, confirmed {git}";
    }

    /// <summary>The Jira author and the git committer are the same person when their names read the same, case aside.</summary>
    public static bool SamePerson(string? git, string? jira) =>
        git is not null && jira is not null && string.Equals(git.Trim(), jira.Trim(), StringComparison.OrdinalIgnoreCase);
}
