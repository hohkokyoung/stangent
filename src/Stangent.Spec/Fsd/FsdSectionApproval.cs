using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.State;

namespace Stangent.Spec.Fsd;

/// <summary>
/// D150 (D128 at section level), behind <c>section-approval</c>: each section a new version changes waits for a person's approval or
/// rejection, and promote applies only what was approved. An answer's id holds the exact text on both sides, so it drops by itself when
/// that text changes. Answers are read from the history only: nothing typed into a file counts.
/// </summary>
public static partial class FsdSectionApproval
{
    public const string Component = "section-approval";

    public static bool On(StangentConfig config) => StangentComponents.IsOn(config, Component);

    /// <summary>One change a person answers: its kind, where it is on each side, and the id its answer is recorded under.</summary>
    public sealed record Change(string Key, string Label, string Kind, string? To, string? PermanentId, string? From, string Title, JsonObject Entry);

    static string Hash(JsonObject? s) => s is null ? "-" : (string?)s["contentHash"] ?? (string)s["textHash"]!;

    static bool Partial(JsonObject diff) => (bool?)diff["partial"] == true;

    /// <summary>In a changes-only file, a heading with no text of its own under the same path is there for its place, not a change.</summary>
    internal static bool Context(JsonObject diff, JsonObject entry, JsonObject? candidate) =>
        Partial(diff) && (string)entry["kind"]! == "modified" && candidate is not null
        && ((string)candidate["text"]!).Trim().Length == 0 && (candidate["attachments"]?.AsArray().Count ?? 0) == 0;

    /// <summary>The changes in a candidate, in diff order. A doubtful section still open isn't one yet: it's settled first with <c>fsd resolve</c>.</summary>
    public static List<Change> Changes(string doc, JsonObject diff, JsonObject current, JsonObject pending)
    {
        var oldById = current["sections"]!.AsArray().OfType<JsonObject>().ToDictionary(s => (string)s["permanentId"]!, StringComparer.Ordinal);
        var newById = pending["sections"]!.AsArray().OfType<JsonObject>().ToDictionary(s => (string)s["id"]!, StringComparer.Ordinal);
        var changes = new List<Change>();
        foreach (var e in diff["sections"]!.AsArray().OfType<JsonObject>())
        {
            var kind = (string)e["kind"]!;
            var to = (string?)e["to"];
            var candidate = to is null ? null : newById.GetValueOrDefault(to);
            string? pid = (string?)e["permanentId"];
            string change;
            switch (kind)
            {
                case "unchanged": continue;
                case "modified" or "moved" or "renamed":
                    if (Context(diff, e, candidate)) continue;
                    change = kind;
                    break;
                case "added": change = "added"; break;
                case "removed": change = "removed"; break;
                case "doubtful" when (string)e["side"]! == "candidate":
                    if ((string?)e["state"] == "same") { change = "same"; pid = (string)e["same_as"]!; }
                    else if ((string?)e["state"] == "new") change = "added";
                    else continue;
                    break;
                case "doubtful":
                    if ((string?)e["state"] != "removed") continue;
                    change = "removed";
                    break;
                default: continue;
            }
            var old = pid is null ? null : oldById.GetValueOrDefault(pid);
            var label = to ?? pid!;
            // The exact text on both sides, the title on both sides, and where it goes: any of them changing is another change.
            var sig = string.Join("\u0001", change, pid ?? "-", to ?? "-", Hash(old), Hash(candidate), (string?)old?["title"] ?? "-", (string?)candidate?["title"] ?? "-");
            changes.Add(new Change($"{doc}#{label}@{CanonicalJson.Hash(sig)[..12]}", label, change, to, pid, (string?)e["from"], (string)e["title"]!, e));
        }
        return changes;
    }

    /// <summary>What a person said about a change, from the history: <c>unanswered</c>, <c>approved</c> or <c>rejected</c>, with who.</summary>
    public static (string Answer, string? By) AnswerOf(string root, Change c) =>
        StateChanges.History(root, StateKinds.FsdSectionAnswer, c.Key).LastOrDefault() is { } m && m.Who.Kind == StateActor.PersonKind ? (m.To, m.Who.Name) : ("unanswered", null);

    static Change Find(List<Change> changes, JsonObject diff, string doc, string section)
    {
        foreach (var pick in new Func<Change, bool>[] { c => c.To == section, c => c.PermanentId == section, c => c.From == section })
        {
            var hits = changes.Where(pick).ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1)
                throw new StangentException("ambiguous_section", $"{section} names more than one change in {doc}'s candidate: use the label `fsd diff` shows.", ExitCodes.Usage, [.. hits.Select(h => h.Label)]);
        }
        var open = diff["sections"]!.AsArray().OfType<JsonObject>().Any(e => (string)e["kind"]! == "doubtful" && ((string?)e["to"] == section || (string?)e["permanentId"] == section));
        throw open
            ? new StangentException("not_settled", $"Section {section} is doubtful: settle it with `stangent fsd resolve` first, then answer it.", ExitCodes.Usage)
            : new StangentException("not_changed", $"Section {section} isn't a change in {doc}'s candidate, so there's nothing to answer.", ExitCodes.NotFound, [.. changes.Select(c => c.Label)]);
    }

    static void Require(StangentRepo repo)
    {
        if (!On(repo.Config))
            throw new StangentException("section_approval_off", "Approving section by section needs components.section-approval on in stangent.yaml (D150).", ExitCodes.Usage);
    }

    static (JsonObject Diff, JsonObject Current, JsonObject Pending) Load(string root, string doc)
    {
        var diff = FsdVersions.Diff(root, doc);
        if ((bool?)diff["first"] == true)
            throw new StangentException("needs_approval", $"{doc} is a first import, approved whole: run `stangent fsd approve {doc}`.", ExitCodes.Usage);
        var current = JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(root, doc), "document.json")))!.AsObject();
        var pending = JsonNode.Parse(File.ReadAllText(Path.Combine(FsdVersions.CandidateDir(root, doc), "pending.json")))!.AsObject();
        return (diff, current, pending);
    }

    /// <summary>A person approves or rejects one changed section. Who comes from git; the move goes through the state module.</summary>
    public static JsonObject Answer(StangentRepo repo, string doc, string section, bool approve, string? why, string who)
    {
        Require(repo);
        var root = repo.Root;
        var (diff, current, pending) = Load(root, doc);
        var changes = Changes(doc, diff, current, pending);
        var change = Find(changes, diff, doc, section);
        var to = approve ? "approved" : "rejected";
        var (was, _) = AnswerOf(root, change);
        StateChanges.Move(root, StateKinds.FsdSectionAnswer, change.Key, was == "unanswered" ? null : was, to, StateActor.Person(who),
            string.IsNullOrWhiteSpace(why) ? $"{to}: {change.Kind} §{change.Label}" : why.Trim());
        return new JsonObject
        {
            ["doc"] = doc, ["section"] = change.Label, ["change"] = change.Kind, ["answer"] = to, ["by"] = who,
            ["unanswered"] = new JsonArray([.. changes.Where(c => AnswerOf(root, c).Answer == "unanswered").Select(c => (JsonNode)c.Label)]),
        };
    }

    /// <summary>The diff with each change's answer and the commands to give one, for <c>fsd diff</c>: pasted into a PR description by a person. No API calls.</summary>
    public static JsonObject Review(StangentRepo repo, string doc)
    {
        var diff = FsdVersions.Diff(repo.Root, doc);
        if (!On(repo.Config) || (bool?)diff["first"] == true) return diff;
        var (_, current, pending) = Load(repo.Root, doc);
        diff["sectionAnswers"] = new JsonArray([.. Changes(doc, diff, current, pending).Select(c =>
        {
            var (answer, by) = AnswerOf(repo.Root, c);
            return (JsonNode)new JsonObject
            {
                ["section"] = c.Label, ["change"] = c.Kind, ["title"] = c.Title, ["answer"] = answer, ["by"] = by,
                ["approve"] = $"stangent fsd approve {doc} {Quote(c.Label)}", ["reject"] = $"stangent fsd reject {doc} {Quote(c.Label)} --why \"...\"",
            };
        })]);
        return diff;
    }

    static string Quote(string s) => s.Any(c => char.IsWhiteSpace(c) || c is '/' or '(' or ')' or '\'' or '"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;

    /// <summary>The review as Markdown below the section diff. Empty when there's no review, so the switch off prints what it always did.</summary>
    public static string ReviewMarkdown(JsonObject diff)
    {
        if (diff["sectionAnswers"] is not JsonArray review) return "";
        var sb = new StringBuilder("\n## Review (section-approval)\n\nEach changed section needs a person's answer before `fsd promote`. Run the command in the repo; the name comes from git.\n\n");
        sb.Append("| Section | Change | Title | Answer | Approve | Reject |\n|---|---|---|---|---|---|\n");
        foreach (var r in review.OfType<JsonObject>())
        {
            var answer = (string)r["answer"]! + ((string?)r["by"] is { } by ? $" by {by}" : "");
            sb.Append($"| {Cell((string)r["section"]!)} | {r["change"]} | {Cell((string)r["title"]!)} | {Cell(answer)} | `{Cell((string)r["approve"]!)}` | `{Cell((string)r["reject"]!)}` |\n");
        }
        if (review.Count == 0) sb.Append("| (none) | | | | | |\n");
        return sb.ToString();
    }

    static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    /// <summary>The answers at promote. Refuses while any change is unanswered, listing them.</summary>
    internal static List<(Change Change, string Answer, string By)> Answers(string root, string doc, JsonObject diff, JsonObject current, JsonObject pending)
    {
        var answered = Changes(doc, diff, current, pending).Select(c => (Change: c, A: AnswerOf(root, c))).ToList();
        var open = answered.Where(a => a.A.Answer == "unanswered").Select(a => a.Change.Label).ToList();
        if (open.Count > 0)
            throw new StangentException("unanswered_sections", $"{open.Count} changed section(s) of {doc}'s candidate have no answer. Approve or reject each with `stangent fsd approve|reject {doc} <section>`.", ExitCodes.Failed, open);
        return [.. answered.Select(a => (a.Change, a.A.Answer, a.A.By!))];
    }

    /// <summary>What a stale candidate's diff gets wrong now: the entries a fresh comparison classifies differently, by section.</summary>
    internal static List<string> Drift(JsonObject diff, JsonObject current, JsonObject pending)
    {
        static string Show(JsonObject e) => $"{e["kind"]} {(string?)e["from"] ?? "-"}>{(string?)e["to"] ?? "-"} {(string?)e["permanentId"] ?? "-"}";
        var then = diff["sections"]!.AsArray().OfType<JsonObject>().Select(Show).ToHashSet(StringComparer.Ordinal);
        var now = FsdSectionDiff.Compare(current["sections"]!.AsArray(), pending["sections"]!.AsArray()).Select(Show).ToHashSet(StringComparer.Ordinal);
        return [.. now.Except(then).Concat(then.Except(now).Select(s => $"was: {s}")).Order(StringComparer.Ordinal)];
    }

    // ---- The merge: approved changes in, everything else as it is now ----

    /// <summary>One section of the result: its record and its lines in its own version's <c>document.md</c>.</summary>
    internal sealed record Item(JsonObject Record, string[] Block, bool FromCurrent, string? PermanentId)
    {
        public int Level => (int)Record["level"]!;
    }

    internal sealed record Merged(List<JsonObject> Sections, HashSet<JsonObject> FromCurrent, string Markdown, JsonArray Problems, JsonArray Rejected, bool FrontKept);

    /// <summary>Each section's lines, from its heading to the next one; and what comes before the first heading.</summary>
    static (string[] Front, Dictionary<JsonObject, string[]> Blocks) Blocks(string md, List<JsonObject> sections)
    {
        var lines = md.Split('\n');
        var ordered = sections.OrderBy(s => (int)s["line"]!).ToList();
        var blocks = new Dictionary<JsonObject, string[]>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < ordered.Count; i++)
        {
            var start = (int)ordered[i]["line"]! - 1;
            var end = i + 1 < ordered.Count ? (int)ordered[i + 1]["line"]! - 1 : lines.Length;
            if (start < 0 || start >= lines.Length || end < start)
                throw new StangentException("merge_out_of_step", $"Section {(string)ordered[i]["id"]!} says its heading is at line {start + 1}, and it isn't (a stangent bug).", ExitCodes.Failed);
            blocks[ordered[i]] = lines[start..end];
        }
        return (ordered.Count > 0 ? lines[..((int)ordered[0]["line"]! - 1)] : lines, blocks);
    }

    [GeneratedRegex(@"\]\((images/[^)\s]+)\)")]
    private static partial Regex ImageRef();

    /// <summary>
    /// Builds the new current version from the answers. Approved changes come from the candidate; a rejected change keeps the current section,
    /// in place for a modified or renamed one and after the section it follows now for anything else; a rejected addition isn't there; a removal
    /// rejected, or a section a changes-only file doesn't hold, stays. Each record keeps its text byte for byte, and the result is checked:
    /// every section's text is what <c>document.md</c> holds under its heading, ids are unique, and each parent is the heading above it.
    /// </summary>
    internal static Merged Merge(string root, string doc, JsonObject current, JsonObject pending, JsonObject diff, IReadOnlyDictionary<string, (string Id, string Kind)> kept,
        List<(Change Change, string Answer, string By)> answers)
    {
        var dir = FsdImport.DocDir(root, doc);
        var candidateDir = FsdVersions.CandidateDir(root, doc);
        var oldSections = current["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var newSections = pending["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var oldById = oldSections.ToDictionary(s => (string)s["permanentId"]!, StringComparer.Ordinal);
        var (front, oldBlocks) = Blocks(File.ReadAllText(Path.Combine(dir, "document.md")), oldSections);
        var (candidateFront, newBlocks) = Blocks(File.ReadAllText(Path.Combine(candidateDir, "document.md")), newSections);
        var byTo = answers.Where(a => a.Change.To is not null).ToDictionary(a => a.Change.To!, StringComparer.Ordinal);
        var removals = answers.Where(a => a.Change.Kind == "removed").ToDictionary(a => a.Change.PermanentId!, StringComparer.Ordinal);
        var entryByTo = diff["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["to"] is not null).ToDictionary(e => (string)e["to"]!, StringComparer.Ordinal);

        var merged = new List<Item>();
        var skipped = new List<JsonObject>();
        foreach (var c in newSections)
        {
            var id = (string)c["id"]!;
            var keptId = kept.TryGetValue(id, out var k) ? k.Id : null;
            if (!byTo.TryGetValue(id, out var a))
            {
                // Unchanged, or a heading in a changes-only file that's there for its place: the current section where it has one.
                if (keptId is not null && entryByTo.TryGetValue(id, out var e) && Context(diff, e, c)) merged.Add(new Item(oldById[keptId], oldBlocks[oldById[keptId]], true, keptId));
                else merged.Add(new Item(c, newBlocks[c], false, keptId));
                continue;
            }
            if (a.Answer == "approved") { merged.Add(new Item(c, newBlocks[c], false, keptId)); continue; }
            skipped.Add(c);
            if (a.Change.Kind is "modified" or "renamed" && a.Change.PermanentId is { } pid) merged.Add(new Item(oldById[pid], oldBlocks[oldById[pid]], true, pid));
        }
        // What the current version holds and the result doesn't yet: it stays, after the section it follows now, past that section's deeper headings.
        for (var i = 0; i < oldSections.Count; i++)
        {
            var o = oldSections[i];
            var pid = (string)o["permanentId"]!;
            if (merged.Any(m => m.PermanentId == pid)) continue;
            if (removals.TryGetValue(pid, out var r) && r.Answer == "approved") continue;
            if (!removals.ContainsKey(pid) && !Partial(diff) && !answers.Any(x => x.Change.PermanentId == pid && x.Answer == "rejected")) continue;
            var anchor = -1;
            for (var j = i - 1; j >= 0 && anchor < 0; j--) anchor = merged.FindIndex(m => m.PermanentId == (string)oldSections[j]["permanentId"]!);
            var at = anchor + 1;
            var level = (int)o["level"]!;
            if (anchor >= 0) while (at < merged.Count && merged[at].Level > level) at++;
            merged.Insert(at, new Item(o, oldBlocks[o], true, pid));
        }

        // Files a kept section points at must be the candidate's too, since the candidate's images and attachments are what lands.
        var problems = new List<string>();
        foreach (var m in merged.Where(m => m.FromCurrent))
        {
            if ((m.Record["attachments"]?.AsArray().Count ?? 0) > 0) problems.Add($"§{m.Record["id"]}: the current section has attached files");
            foreach (Match img in ImageRef().Matches((string)m.Record["text"]!))
            {
                var now = Path.Combine(dir, img.Groups[1].Value);
                var then = Path.Combine(candidateDir, img.Groups[1].Value);
                if (!File.Exists(then) || !File.Exists(now) || !File.ReadAllBytes(now).AsSpan().SequenceEqual(File.ReadAllBytes(then)))
                    problems.Add($"§{m.Record["id"]}: its picture {img.Groups[1].Value} isn't the same in the candidate");
            }
        }
        foreach (var s in skipped.Where(s => (s["attachments"]?.AsArray().Count ?? 0) > 0)) problems.Add($"§{s["id"]}: the candidate section has attached files");
        if (problems.Count > 0)
            throw new StangentException("rejected_section_has_files", $"A rejection in {doc} touches a section with files, which section approval can't keep apart yet. Approve it, or discard the candidate and import a file without that change.", ExitCodes.Failed, problems);

        // The text: the current front matter (no section owns it, so nobody approved a change to it), then each section's own lines.
        var md = new StringBuilder();
        var frontText = string.Join('\n', front).TrimEnd('\n');
        var line = 1;
        if (frontText.Length > 0) { md.Append(frontText).Append("\n\n"); line += frontText.Count(ch => ch == '\n') + 2; }
        var records = new List<JsonObject>();
        var fromCurrent = new HashSet<JsonObject>(ReferenceEqualityComparer.Instance);
        var bodies = new List<(JsonObject Record, int Start)>();
        for (var i = 0; i < merged.Count; i++)
        {
            var record = merged[i].Record.DeepClone().AsObject();
            record["line"] = line;
            var block = string.Join('\n', merged[i].Block).TrimEnd('\n');
            md.Append(block).Append(i + 1 < merged.Count ? "\n\n" : "\n");
            line += block.Count(ch => ch == '\n') + 2;
            records.Add(record);
            if (merged[i].FromCurrent) fromCurrent.Add(record);
        }
        var text = md.ToString();
        Check(text, records);

        var takenNew = merged.Where(m => !m.FromCurrent).Select(m => (string)m.Record["id"]!).ToHashSet(StringComparer.Ordinal);
        var takenOld = merged.Where(m => m.FromCurrent).Select(m => (string)m.Record["id"]!).ToHashSet(StringComparer.Ordinal);
        static IEnumerable<JsonNode> Of(JsonObject d, HashSet<string> keep, bool whole) => (d["problems"]?.AsArray() ?? []).OfType<JsonObject>()
            .Where(p => (string?)p["sectionNumber"] is { } n ? keep.Contains(n) : whole).Select(p => p.DeepClone());
        var rejected = new JsonArray([.. answers.Where(a => a.Answer == "rejected").Select(a => (JsonNode)new JsonObject
            { ["section"] = a.Change.Label, ["change"] = a.Change.Kind, ["by"] = a.By })]);
        return new Merged(records, fromCurrent, text, new JsonArray([.. Of(pending, takenNew, true), .. Of(current, takenOld, false)]), rejected,
            string.Join('\n', candidateFront).TrimEnd('\n') != frontText);
    }

    /// <summary>
    /// The result's own check, by code: each section's text is exactly what <c>document.md</c> holds under its heading, ids are unique, and a
    /// section's parent is the nearest heading above it at a lower level. A mix of answers whose numbering doesn't fit is refused, not renumbered.
    /// </summary>
    static void Check(string md, List<JsonObject> records)
    {
        var lines = md.Split('\n');
        var problems = new List<string>();
        var stack = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var s = records[i];
            var id = (string)s["id"]!;
            if (!seen.Add(id)) problems.Add($"§{id}: two sections would have this id");
            var start = (int)s["line"]!;
            var end = i + 1 < records.Count ? (int)records[i + 1]["line"]! - 1 : lines.Length;
            var own = string.Join('\n', lines[start..end]).Trim('\n');
            if (own != (string)s["text"]!) throw new StangentException("merge_out_of_step", $"§{id}'s text isn't what document.md would hold under its heading (a stangent bug).", ExitCodes.Failed);
            while (stack.Count > 0 && (int)stack[^1]["level"]! >= (int)s["level"]!) stack.RemoveAt(stack.Count - 1);
            var parent = stack.Count > 0 ? (string)stack[^1]["id"]! : null;
            if (parent != (string?)s["parent"]) problems.Add($"§{id}: it would sit under {(parent is null ? "no heading" : $"§{parent}")}, and its record says {((string?)s["parent"] is { } p ? $"§{p}" : "no heading")}");
            stack.Add(s);
        }
        if (problems.Count > 0)
            throw new StangentException("answers_dont_fit", "With these answers the sections wouldn't fit together, and code doesn't renumber them. Change an answer so they fit, or discard the candidate and import a corrected file.", ExitCodes.Failed, problems);
    }
}
