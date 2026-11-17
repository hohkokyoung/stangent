using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd rule &lt;doc&gt; &lt;n&gt; [answer]</c> (D371): the PM's answer to question <c>n</c> of
/// <c>truth-map.md</c>, written into <c>rulings.yaml</c> so no person edits it. The number is read back to its entry's key
/// through <c>truth-map.json</c>, the map the PM read, and the key is looked up in today's <c>rulings.yaml</c>; the truth map
/// isn't rebuilt, so every number keeps its meaning until <c>fsd rulings-check</c> runs. Code checks the answer fits the item
/// (D18). Nothing is committed: who answered comes from git when it is.
/// </summary>
public static class PmAnswers
{
    static readonly string[] Yes = ["yes", "y", "confirmed", "confirm", "right", "correct", "ok"];
    static readonly string[] No = ["no", "n", "not-a-match", "not a match", "wrong"];
    static readonly string[] Code = ["code", "code-right", "code right", "the code", "the code is right"];
    static readonly string[] Fsd = ["fsd", "fsd-right", "fsd right", "the fsd", "the fsd is right"];
    static readonly string[] Keep = ["keep", "keep it"];
    static readonly string[] Remove = ["remove", "remove it"];

    static string Norm(string s) => s.Trim().TrimEnd('.', '!').Trim().ToLowerInvariant();

    static StangentException Refuse(string message) => new("bad_answer", message, ExitCodes.Failed);

    public static JsonObject Apply(StangentRepo repo, string doc, int n, string? answer, string? words, string? note, string? mapId = null)
    {
        var root = repo.Root;
        var mapPath = TruthMap.JsonPath(root, doc);
        if (!File.Exists(mapPath))
            throw new StangentException("no_truth_map", $"{doc} has no truth map yet: run `stangent fsd rulings-check {doc}`, then read truth-map.md.", ExitCodes.NotFound);
        var map = JsonNode.Parse(File.ReadAllText(mapPath))!.AsObject();
        var path = Rulings.PathFor(root, doc);
        if (!File.Exists(path))
            throw new StangentException("no_rulings", $"{doc} has no rulings.yaml: run `stangent fsd rulings {doc}` first.", ExitCodes.NotFound);
        // The numbers the PM read: a map rebuilt since (by `rulings-check`) numbers differently, so an answer by an old
        // number is refused rather than put on whatever holds that number now.
        var builtId = MapId(map);
        if (mapId is { Length: > 0 } && !string.Equals(mapId.Trim(), builtId, StringComparison.OrdinalIgnoreCase))
            throw new StangentException("map_rebuilt", $"truth-map.md was rebuilt since you read it (your numbers are from map {mapId.Trim()}, it is now map {builtId}): read it again; nothing was written.", ExitCodes.Failed);
        var yaml = File.ReadAllText(path);
        var (file, parseProblems) = Rulings.Parse(yaml);
        var problems = TruthMap.Problems(file, parseProblems);
        if (problems.Count > 0)
            throw new StangentException("invalid_rulings", "rulings.yaml needs fixing before an answer is written (nothing was written).", ExitCodes.Failed, problems);
        answer = string.IsNullOrWhiteSpace(answer) ? null : Norm(answer);
        words = string.IsNullOrWhiteSpace(words) ? null : words.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (answer is null && words is null && note is null)
            throw Refuse($"Say the answer to {n}: for example `yes`, `fsd`, `keep`, or `--words \"the rule in your words\"`.");

        var (section, entry) = Find(map, n);
        string said;
        if (entry is null)
        {
            // A section's question: are all its matches right?
            var sid = (string)section!["sectionId"]!;
            var block = file.Sections.FirstOrDefault(b => b.SectionId == sid)
                ?? throw Refuse($"{n} ({Heading(section)}) isn't in rulings.yaml any more: a review refresh changed it. Run `stangent fsd rulings-check {doc}` and read truth-map.md again.");
            if (words is not null || note is not null) throw Refuse($"{n} asks whether {Heading(section)}'s matches are all right: answer `yes`, or answer `no` on the one that isn't. Words and notes go on an entry's own number.");
            if (answer is "clear") { (block.Matches, block.MatchesRaw) = (null, null); said = "not confirmed (cleared)"; }
            else if (Yes.Contains(answer))
            {
                (block.Matches, block.MatchesRaw) = ("confirmed", null);
                foreach (var e in block.Entries.Where(e => e.IsMatch && e.Ruling is null)) { e.New = false; e.Flagged = null; }
                said = "yes, all its matches are right";
            }
            else if (No.Contains(answer!)) throw Refuse($"Which match in {Heading(section)} isn't right? Answer `no` on its own number; the others can then be confirmed with `{n} yes`.");
            else throw Refuse($"{n} asks whether {Heading(section)}'s matches are all right: the answer is `yes` (or `clear` to undo it), not \"{answer}\".");
            Save(path, yaml, file);
            return Report(doc, n, Heading(section), said, path, root, "matches", block.Matches, null, null, builtId);
        }

        var key = entry["key"]!.AsObject();
        var probe = new RulingEntry { KeySection = (string?)key["section"], KeyQuote = (string?)key["quote"], KeyLink = (string?)key["link"], State = (string)entry["state"]! };
        var found = file.WithSections.FirstOrDefault(x => x.Entry.Problem is null && x.Entry.Key == probe.Key);
        var what = Describe(section, entry);
        if (found.Entry is not { } target)
            throw Refuse($"{n} ({what}) isn't in rulings.yaml any more: a review refresh changed it. Run `stangent fsd rulings-check {doc}` and read truth-map.md again.");
        if (target.State != probe.State)
            throw Refuse($"{n} ({what}) has changed since truth-map.md was made: the review now calls it a {target.State}. Run `stangent fsd rulings-check {doc}` and read it again.");

        if (answer is "drop")
        {
            if (target.NotFound is null && !JiraRulings.FromTicket(target)) throw Refuse($"Only an item the last review didn't find again, or one a ticket's Jira reply added, can be dropped; {n} was found. Answer it instead.");
            if (words is not null || note is not null) throw Refuse("`drop` removes the item, so it takes no words or note.");
            if (found.Section is { } sb)
            {
                sb.Entries.Remove(target);
                if (sb.Entries.Count == 0 && sb.Matches is null) file.Sections.Remove(sb);
            }
            else file.Undocumented.Remove(target);
            Save(path, yaml, file);
            return Report(doc, n, what, "dropped: removed from the rulings", path, root, "drop", null, null, null, builtId);
        }

        if (target.IsMatch && words is not null)
            throw Refuse($"{n} is a match: the answer is `yes` or `no`. To say what the rule really is, answer `no` and add a note.");
        if (answer is "clear" && (words is not null || note is not null))
            throw Refuse("`clear` undoes the answer, so it takes no words or note.");
        string? ruling = answer is null ? null : answer == "clear" ? "" : Ruling(target.State, answer, n);
        if (ruling is null && words is not null) ruling = "restated";
        if (ruling == "restated" && words is null && target.Statement is null)
            throw Refuse($"For \"in my words\", say the rule: `--words \"…\"`.");
        if (ruling is not null)
        {
            var oldStatement = target.Statement;
            if (ruling.Length == 0) { (target.Ruling, target.RulingRaw, target.Statement) = (null, null, null); target.InFsd = false; }
            else
            {
                (target.Ruling, target.RulingRaw) = (ruling, null);
                target.Statement = words ?? (ruling == "restated" ? oldStatement : null);
                target.InFsd = target.InFsd && ruling == "restated" && target.Statement == oldStatement;
            }
            // Answered means looked at: a flag or "new" mark is done with (D369's "delete the line once you have looked").
            target.Flagged = null;
            // The answer is now this person's, not the Jira reply's: its jira line goes, so nobody reads it as the PM's (D374).
            target.Jira = null;
            target.New = false;
        }
        var replacedNote = note is not null && target.PmNote is not null && target.PmNote != note;
        if (note is not null) target.PmNote = note;
        // Code checks what the answer leaves (D18): an answer never makes the file one `rulings-check` refuses.
        var after = TruthMap.Problems(file, []);
        if (after.Count > 0) throw new StangentException("bad_answer", $"That answer would leave rulings.yaml invalid (nothing was written): {after[0]}", ExitCodes.Failed, after);
        Save(path, yaml, file);
        said = ruling switch
        {
            null => "note added",
            "" => "cleared: not answered",
            _ => RulingWordsWith(ruling, target.Statement) + (note is not null ? ", with your note" : ""),
        };
        if (replacedNote) said += " (your earlier note was replaced)";
        if (target.NotFound is not null && ruling is { Length: > 0 }) said += $" (still marked: the review of `{target.NotFound}` didn't find it again)";
        return Report(doc, n, what, said, path, root, "ruling", target.Ruling, target.Statement, target.PmNote, builtId);
    }

    static string RulingWordsWith(string ruling, string? statement) =>
        ruling == "restated" ? $"in your words: \"{statement}\"" : TruthMap.RulingWords(ruling) + (statement is not null ? $", in your words: \"{statement}\"" : "");

    /// <summary>The answer as a ruling of the entry's kind, or a refusal naming the answers it takes.</summary>
    static string Ruling(string state, string answer, int n)
    {
        if (state != "match" && answer is "restated" or "my words" or "in my words" or "neither") return "restated";
        switch (state)
        {
            case "match":
                if (Yes.Contains(answer)) return "confirmed";
                if (No.Contains(answer)) return "not-a-match";
                throw Refuse($"{n} asks whether the code does what the FSD says: `yes` or `no`, not \"{answer}\".");
            case "undocumented":
                if (Keep.Contains(answer)) return "keep";
                if (Remove.Contains(answer)) return "remove";
                throw Refuse($"{n} asks whether to keep code the FSD doesn't mention: `keep`, `remove`, or the rule in your words (`--words \"…\"`), not \"{answer}\".");
            default:
                if (Code.Contains(answer)) return "code-right";
                if (Fsd.Contains(answer)) return "fsd-right";
                throw Refuse($"{n} asks which is right: `code` (the FSD needs updating), `fsd` (the code has a bug), or the rule in your words (`--words \"…\"`), not \"{answer}\".");
        }
    }

    static (JsonObject? Section, JsonObject? Entry) Find(JsonObject map, int n)
    {
        foreach (var s in map["sections"]!.AsArray().OfType<JsonObject>())
        {
            if ((int?)s["matches"]?["n"] == n) return (s, null);
            foreach (var e in s["entries"]!.AsArray().OfType<JsonObject>())
                if ((int?)e["n"] == n) return (s, e);
        }
        foreach (var e in map["undocumented"]!.AsArray().OfType<JsonObject>())
            if ((int?)e["n"] == n) return (null, e);
        throw Refuse($"truth-map.md has no question {n}.");
    }

    static string Heading(JsonObject s) => (string?)s["idKind"] == "number" ? $"{(string)s["section"]!} {(string)s["title"]!}" : (string)s["title"]!;

    static string Describe(JsonObject? section, JsonObject e)
    {
        var where = section is null ? "code no section covers" : Heading(section);
        if ((string?)e["key"]!["quote"] is { } q)
        {
            var t = Quotes.Normalize(q);
            return $"{where}, “{(t.Length > 70 ? t[..70] + "…" : t)}”";
        }
        var ev = e["evidence"]!.AsArray().Select(v => (string)v!).FirstOrDefault();
        return $"{where}, {TruthMap.StateWords((string)e["state"]!).ToLowerInvariant()} at `{ev ?? (string)e["link"]!["id"]!}`";
    }

    /// <summary>Written as <c>fsd rulings</c> writes it, so every other line stays byte for byte and git blame keeps naming
    /// whoever answered it; the file's line endings are kept.</summary>
    internal static void Save(string path, string before, RulingsFile file)
    {
        var text = Rulings.Write(file);
        if (before.Contains("\r\n", StringComparison.Ordinal)) text = text.Replace("\n", "\r\n");
        Quotes.WriteAtomic(path, text);
    }

    /// <summary>The map's short id, shown at the top of truth-map.md: its rulings.yaml hash's first 8 characters.</summary>
    public static string MapId(JsonObject map) => ((string?)map["rulingsHash"] ?? "").Replace("sha256:", "", StringComparison.Ordinal) is var h && h.Length >= 8 ? h[..8] : h;

    static JsonObject Report(string doc, int n, string what, string said, string path, string root, string field, string? ruling, string? statement, string? pmNote, string mapId) => new()
    {
        ["doc"] = doc, ["n"] = n, ["map"] = mapId, ["item"] = what, ["answer"] = said, ["field"] = field,
        ["ruling"] = ruling, ["statement"] = statement, ["pmNote"] = pmNote,
        ["file"] = Path.GetRelativePath(root, path).Replace('\\', '/'), ["committed"] = false,
        ["summary"] = $"{n} · {what}: {said}.",
        ["next"] = $"More answers by number, then `stangent fsd rulings-check {doc}` rebuilds the truth map (its numbers can change then), and a commit makes the answers count.",
    };
}
