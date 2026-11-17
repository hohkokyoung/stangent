using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Phase fs layer 4 (D379, D386, D387): <c>stangent fsd write &lt;doc&gt;</c> turns the rulings not yet written into chapter
/// edits, uncommitted, for a person to read and commit (D380). The PM's own words (<c>restated</c>) are placed by code: they
/// replace the quoted sentence of a gap or conflict, or, for code no section covers, go at the end of the section named
/// with <c>--section</c> (without one it is listed, never guessed). A <c>code-right</c> ruling goes to an AI step (D60):
/// <c>fsd write</c> prepares the drafter's input under a cost cap (rule 9), <see cref="Writer"/> drafts the replacing
/// sentence with the code lines it describes, and <c>fsd write-check</c> checks each draft by code (the sentence it
/// replaces is the ruled quote word for word, it replaces only that, every cited <c>path:line</c> is real and inside the
/// entry's linked code); one retry for the drafts that failed, then they are listed. Each written entry becomes a confirmed
/// match on its new sentence with <c>writtenFrom</c> (D386), and its section's recorded hash moves to the new text, so
/// <c>fsd rulings</c> doesn't flag the edit it made. Only the replaced words change: a section edited since its rulings
/// were recorded (by hand, or a newer FSD) is listed, not written.
/// </summary>
public static partial class FsdWrite
{
    public const string Writer = "stangent-fsd-writer";
    public const string OutputSchema = "fsd-write-output.v1.json";
    /// <summary>The drafter's own cap when the cost choice is <c>none</c>: one call for all the document's drafts.</summary>
    public const double DefaultUsd = 2.0;
    public const int MaxTurns = 40;
    const int MaxSentence = 1000;

    const string InputFile = "input.md";
    const string OutputFile = "output.json";

    public static string RunDir(string root, string doc) => Path.Combine(root, ".stangent", "fsd", doc, "write");

    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    // ---- what a written entry is (D386) ---------------------------------------------------------------------------------

    /// <summary>The key of the finding a written entry replaced: a review that finds it again mustn't bring it back.</summary>
    public static string FromKey(RulingEntry e)
    {
        var w = e.WrittenFrom!;
        return new RulingEntry { KeySection = (string?)w["section"], KeyQuote = (string?)w["quote"], KeyLink = (string?)w["link"], State = (string?)w["state"] ?? "" }.Key;
    }

    /// <summary>The entry, written: a confirmed match keyed by <paramref name="sectionId"/> and its new sentence, with the same
    /// link, evidence and notes, and <c>writtenFrom</c> naming what it replaced and who ruled.</summary>
    public static void MakeWritten(RulingEntry e, string sectionId, string sentence, TruthMap.Person? who, IReadOnlyList<string>? cites = null)
    {
        var w = new JsonObject { ["state"] = e.State, ["section"] = e.KeySection };
        if (e.KeyQuote is not null) w["quote"] = e.KeyQuote; else w["link"] = e.KeyLink;
        w["ruling"] = e.Ruling;
        if (e.Ruling == "restated") w["statement"] = e.Statement;
        if (cites is { Count: > 0 }) w["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]);
        if (who is { Committed: true }) { w["by"] = who.By; w["on"] = who.On; }
        e.WrittenFrom = w;
        e.State = "match";
        e.KeySection = sectionId;
        e.KeyQuote = sentence;
        e.KeyLink = null;
        (e.Ruling, e.RulingRaw, e.Statement) = ("confirmed", null, null);
        (e.Flagged, e.New, e.NotFound, e.Disagrees) = (null, false, null, false);
    }

    /// <summary>Waiting to be written: a restated or code-right difference, or restated undocumented code, not written,
    /// flagged or lost by the last review.</summary>
    static bool Waiting(RulingEntry e) => Writable(e) && e.NotFound is null && e.Flagged is null;

    /// <summary>A ruling that becomes FSD words, not written yet.</summary>
    static bool Writable(RulingEntry e) => e.Problem is null && e.WrittenFrom is null
        && ((e.IsDifference && e.Ruling is "restated" or "code-right") || (e.IsUndocumented && e.Ruling == "restated"));

    // ---- the chapters, in memory ------------------------------------------------------------------------------------------

    /// <summary>The working tree's chapter files as edited so far, and what each section must read as before an edit
    /// lands in it: its rulings block's recorded hash, else HEAD's text.</summary>
    sealed class Chapters
    {
        public readonly string Dir;
        public readonly List<(string Name, string Text)> Files;
        public readonly HashSet<string> Changed = new(StringComparer.Ordinal);
        /// <summary>The sections an edit landed in, by permanent id.</summary>
        public readonly HashSet<string> Edited = new(StringComparer.Ordinal);
        /// <summary>Where this run's own words sit in each section's body: a later quote found inside them isn't the FSD's.</summary>
        readonly Dictionary<string, List<(int Start, int End)>> placed = new(StringComparer.Ordinal);

        public bool Overlaps(ChapterSection s, int start, int end) =>
            placed.TryGetValue(s.PermanentId!, out var list) && list.Any(r => start < r.End && r.Start < end);

        /// <summary>Records words put at [start, start + length) in place of [start, end), moving the ranges after it.</summary>
        public void Place(ChapterSection s, int start, int end, int length)
        {
            var list = placed.TryGetValue(s.PermanentId!, out var l) ? l : placed[s.PermanentId!] = [];
            for (var i = 0; i < list.Count; i++) if (list[i].Start >= end) list[i] = (list[i].Start + length - (end - start), list[i].End + length - (end - start));
            list.Add((start, start + length));
        }
        public ChapterRead Read;
        readonly Dictionary<string, string?> expected = new(StringComparer.Ordinal);
        readonly RulingsFile rulings;
        readonly ChapterRead? head;

        public Chapters(string root, string doc, RulingsFile rulings)
        {
            Dir = FsdChapters.Dir(FsdImport.DocDir(root, doc));
            if (!Directory.Exists(Dir))
                throw new StangentException("fsd_not_migrated", $"{doc} has no chapter files: run `stangent fsd migrate {doc}` and commit first.", ExitCodes.Usage);
            Files = FsdChapters.Files(FsdImport.DocDir(root, doc));
            Read = FsdChapters.Read(Files);
            if (Read.Damaged.Count > 0 || Read.Repeated.Count > 0 || Read.Unmarked.Count > 0)
                throw new StangentException("fsd_check_first", $"The chapter files of {doc} have headings with no id, a damaged id or one id twice: run `stangent fsd check {doc}` first (nothing was written).", ExitCodes.Usage);
            this.rulings = rulings;
            head = FsdDocument.CommittedChapters(root, doc)?.Read;
        }

        public ChapterSection? Section(string id) => Read.Sections.FirstOrDefault(s => s.PermanentId == id);

        /// <summary>The section and the sections under it, in reading order.</summary>
        public List<ChapterSection> Subtree(string id)
        {
            var list = Read.Sections.ToList();
            var at = list.FindIndex(s => s.PermanentId == id);
            if (at < 0) return [];
            var result = new List<ChapterSection> { list[at] };
            for (var i = at + 1; i < list.Count && list[i].Level > list[at].Level; i++) result.Add(list[i]);
            return result;
        }

        /// <summary>Why <paramref name="s"/> can't take an edit, or null: its text must be the one its rulings were recorded on.</summary>
        public string? Unchanged(ChapterSection s)
        {
            var id = s.PermanentId!;
            if (!expected.TryGetValue(id, out var want))
                expected[id] = want = rulings.Sections.FirstOrDefault(b => b.SectionId == id) is { TextHash.Length: > 0 } block ? block.TextHash
                    : head?.Sections.FirstOrDefault(h => h.PermanentId == id)?.TextHash;
            if (want is null) return $"section {id} isn't committed yet: commit the chapter it is in, then write";
            return s.TextHash == want ? null
                : $"section {id} ({s.Title}) was edited since its rulings were recorded (an uncommitted edit, or a newer FSD the rulings haven't seen): commit or undo the edit and run `stangent fsd rulings`, then write; only the ruled words are ever replaced";
        }

        /// <summary>The section's own lines in its file: [start, end) by line index, the heading at start - 1.</summary>
        (int File, int Start, int End) Body(ChapterSection s)
        {
            var f = Files.FindIndex(x => x.Name == s.File);
            var lines = Files[f].Text.Split('\n');
            var next = Read.Sections.Where(x => x.File == s.File && x.FileLine > s.FileLine).Select(x => x.FileLine - 1).DefaultIfEmpty(lines.Length).Min();
            return (f, s.FileLine, next);
        }

        public string BodyText(ChapterSection s)
        {
            var (f, start, end) = Body(s);
            return string.Join('\n', Files[f].Text.Split('\n')[start..end]);
        }

        /// <summary>Puts the new body in; checks every other section reads as before and no heading came or went.</summary>
        public string? Replace(ChapterSection s, string newBody)
        {
            var (f, start, end) = Body(s);
            // A file with both line endings can't be written back with only the words changed.
            var disk = File.ReadAllText(Path.Combine(Dir, Files[f].Name));
            if (disk.Contains("\r\n", StringComparison.Ordinal) && disk.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n'))
                return $"{Files[f].Name} mixes CRLF and LF line endings, so writing it would change every line: make them one kind, commit, then write";
            var lines = Files[f].Text.Split('\n');
            var text = string.Join('\n', lines[..start].Concat(newBody.Split('\n')).Concat(lines[end..]));
            var files = Files.ToList();
            files[f] = (files[f].Name, text);
            var after = FsdChapters.Read(files);
            if (after.Sections.Count != Read.Sections.Count || after.Damaged.Count > 0 || after.Repeated.Count > 0
                || after.Sections.Zip(Read.Sections).Any(p => p.First.PermanentId != p.Second.PermanentId || p.First.Title != p.Second.Title || p.First.Level != p.Second.Level
                    || (p.First.PermanentId != s.PermanentId && p.First.TextHash != p.Second.TextHash)))
                return "the new words would change a heading or another section's text";
            Files[f] = files[f];
            Read = after;
            Changed.Add(files[f].Name);
            Edited.Add(s.PermanentId!);
            expected[s.PermanentId!] = Section(s.PermanentId!)!.TextHash;
            return null;
        }

        public void Save()
        {
            foreach (var (name, text) in Files.Where(x => Changed.Contains(x.Name)))
            {
                var path = Path.Combine(Dir, name);
                // A chapter file written with CRLF keeps it, so the diff shows only the words.
                var before = File.ReadAllText(path);
                Quotes.WriteAtomic(path, before.Contains("\r\n", StringComparison.Ordinal) ? text.Replace("\n", "\r\n") : text);
            }
        }
    }

    // ---- placing words --------------------------------------------------------------------------------------------------

    /// <summary>Text as quotes are compared (<see cref="FsdReview.Quoted"/>'s plain form): curly quotes and long dashes folded,
    /// <c>*</c> and <c>`</c> dropped, whitespace runs one space, trimmed; with each kept character's place in the raw text.</summary>
    static (string Norm, List<int> Map) Normalize(string raw)
    {
        var sb = new StringBuilder();
        var map = new List<int>();
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i] switch
            {
                '‘' or '’' or '‛' => '\'',
                '“' or '”' or '„' => '"',
                '–' or '—' => '-',
                ' ' => ' ',
                var x => x,
            };
            if (c is '*' or '`') continue;
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ') { sb.Append(' '); map.Add(i); }
                continue;
            }
            sb.Append(c);
            map.Add(i);
        }
        if (sb.Length > 0 && sb[^1] == ' ') { sb.Length--; map.RemoveAt(map.Count - 1); }
        return (sb.ToString(), map);
    }

    /// <summary>Where the quote sits in <paramref name="raw"/>, whole words, as a raw span widened over the bold or code marks
    /// right around it; null with why when it isn't there exactly once, or its marks or lines run past it.</summary>
    static (int Start, int End)? Locate(string raw, string quote, out string? why, out int found)
    {
        why = null;
        var (n, map) = Normalize(raw);
        var q = Normalize(quote).Norm;
        var hits = new List<int>();
        if (q.Length > 0)
            for (var at = n.IndexOf(q, StringComparison.Ordinal); at >= 0; at = n.IndexOf(q, at + 1, StringComparison.Ordinal))
            {
                var end = at + q.Length;
                var startOk = at == 0 || !char.IsLetterOrDigit(n[at - 1]) || !char.IsLetterOrDigit(q[0]);
                var endOk = end == n.Length || !char.IsLetterOrDigit(n[end]) || !char.IsLetterOrDigit(q[^1]);
                if (startOk && endOk) hits.Add(at);
            }
        found = hits.Count;
        if (hits.Count != 1) return null;
        var (s, e) = (map[hits[0]], map[hits[0] + q.Length - 1] + 1);
        while (s > 0 && raw[s - 1] is '*' or '`') s--;
        while (e < raw.Length && raw[e] is '*' or '`') e++;
        var span = raw[s..e];
        if (!Balanced(span, '*') || !Balanced(span, '`'))
        {
            why = "its bold or code marks run past the quoted words, so replacing it would leave a mark open: edit it by hand";
            return null;
        }
        if (BreakInside().IsMatch(span))
        {
            why = "it runs across paragraphs, list items or table rows: edit it by hand";
            return null;
        }
        return (s, e);
    }

    /// <summary>Each run of a mark (<c>**</c>, <c>*</c>) opens or closes: balanced when every length comes an even number of times.</summary>
    static bool Balanced(string span, char mark) =>
        Regex.Matches(span, Regex.Escape(mark.ToString()) + "+").GroupBy(m => m.Length).All(g => g.Count() % 2 == 0);

    [GeneratedRegex(@"\n[ \t]*(?:\n|[-*+][ \t]|\d+[.)][ \t]|\||>|#)")]
    private static partial Regex BreakInside();

    /// <summary>Why words can't go into the FSD as they are, or null: one line, no marker, not a heading, not too long.</summary>
    static string? WordsProblem(string words)
    {
        if (words.Trim().Length == 0) return "the words are empty";
        if (words.Contains('\n') || words.Contains('\r')) return "the words hold a line break; they go in as one sentence or paragraph";
        if (words.Contains("<!--", StringComparison.Ordinal) || words.Contains("-->", StringComparison.Ordinal)) return "the words hold `<!--` or `-->`, which reads as a section's id marker";
        if (Regex.IsMatch(words.TrimStart(), @"^\\*#")) return "the words start with #, which would read as a heading";
        if (words.Length > MaxSentence) return $"the words are over {MaxSentence} characters";
        // Words that would change the Markdown's shape, not just its sentence: a list, quote, table, rule or fence, or a mark left open.
        if (Regex.IsMatch(words.TrimStart(), @"^(?:[-*+][ \t]|\d+[.)][ \t]|>|\||[-=*_]{3,}\s*$)")) return "the words start like a list item, a quote, a table row or a rule, which would change the section's layout";
        if (words.Contains("```", StringComparison.Ordinal) || words.Contains("~~~", StringComparison.Ordinal)) return "the words hold a code fence, which would swallow the rest of the section";
        if (!Balanced(words, '*') || !Balanced(words, '`')) return "the words leave a bold or code mark open, which would restyle the text after them";
        if (Regex.IsMatch(words, @"<[A-Za-z/!?]")) return "the words hold an HTML tag";
        return null;
    }

    /// <summary>The quote, in the key section or one under it, replaced by <paramref name="words"/>; why not, or null.</summary>
    static string? ReplaceQuote(Chapters ch, string sectionId, string quote, string words, out ChapterSection? where)
    {
        where = null;
        if (WordsProblem(words) is { } wp) return wp;
        var tree = ch.Subtree(sectionId);
        if (tree.Count == 0) return $"section {sectionId} isn't in the chapter files";
        var hits = new List<(ChapterSection Section, int Start, int End)>();
        string? why = null;
        var total = 0;
        foreach (var s in tree)
        {
            var body = ch.BodyText(s);
            if (Locate(body, quote, out var w, out var count) is { } span) hits.Add((s, span.Start, span.End));
            total += count;
            why ??= w;
        }
        if (total > 1) return $"the quoted words appear {total} times in section {sectionId} and the sections under it, so which to replace can't be told: edit it by hand";
        if (hits.Count == 0) return why ?? $"the quoted words aren't word for word in section {sectionId} any more";
        var (sec, start, end) = hits[0];
        if (ch.Unchanged(sec) is { } changed) return changed;
        var raw = ch.BodyText(sec);
        var line = raw[..start].Split('\n')[^1] + raw[start..].Split('\n')[0];
        if (line.Contains('|') && words.Contains('|')) return "the words hold a `|`, and the sentence sits in a table row";
        if (ch.Overlaps(sec, start, end)) return "the quoted words now sit in words this run wrote for another ruling: write it again after a commit, or by hand";
        where = sec;
        if (ch.Replace(sec, raw[..start] + words + raw[end..]) is { } bad) return bad;
        ch.Place(sec, start, end, words.Length);
        return null;
    }

    /// <summary>The words added as a paragraph at the end of the section's own text, before any section under it.</summary>
    static string? Append(Chapters ch, ChapterSection s, string words)
    {
        if (WordsProblem(words) is { } wp) return wp;
        if (ch.Unchanged(s) is { } changed) return changed;
        var lines = ch.BodyText(s).Split('\n').ToList();
        var last = lines.FindLastIndex(l => l.Trim().Length > 0);
        // After its last line of text (blank lines before the next heading stay after it), or right under the heading.
        lines.InsertRange(last + 1, ["", words]);
        var at = string.Join('\n', lines.Take(last + 2)).Length + 1;
        if (ch.Replace(s, string.Join('\n', lines)) is { } bad) return bad;
        ch.Place(s, at, at, words.Length);
        return null;
    }

    // ---- fsd write <doc> -------------------------------------------------------------------------------------------------

    sealed record Pending(RulingEntry Entry, RulingSection? Block);

    /// <summary>
    /// <c>fsd write &lt;doc&gt;</c>: writes the restated rulings, places restated undocumented code in <paramref name="section"/>,
    /// and with a cost choice (<paramref name="maxCost"/>: a dollar figure, or <c>none</c> for the drafter's own cap)
    /// prepares the AI run for the code-right ones. A ruling drafted from a Jira reply waits until it is committed (D374).
    /// </summary>
    public static (JsonObject Result, RunStep? Step) Run(StangentRepo repo, string doc, string? maxCost, string? section, int? item, string? mapId)
    {
        var root = repo.Root;
        var path = Rulings.PathFor(root, doc);
        if (!File.Exists(path)) throw new StangentException("no_rulings", $"{doc} has no rulings.yaml: run `stangent fsd rulings {doc}` first.", ExitCodes.NotFound);
        double? cap = maxCost switch
        {
            null => null,
            "none" => DefaultUsd,
            var v when double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0 => d,
            _ => throw new StangentException("usage", "--max-cost takes a dollar figure (1.5) or `none`.", ExitCodes.Usage),
        };
        var yaml = File.ReadAllText(path);
        var (file, parseProblems) = Rulings.Parse(yaml);
        var problems = TruthMap.Problems(file, parseProblems);
        if (problems.Count > 0) throw new StangentException("invalid_rulings", "rulings.yaml needs fixing before anything is written (nothing was written).", ExitCodes.Failed, problems);
        var ch = new Chapters(root, doc, file);
        var blame = TruthMap.Blame(root, path);
        var written = new JsonArray();
        var listed = new JsonArray();
        void List(RulingEntry e, string why) => listed.Add(new JsonObject { ["entry"] = e.Describe(), ["ruling"] = e.Ruling, ["why"] = why });

        // Nothing is dropped silently (rule 10): a ruling held back by a flag or a lost finding is listed.
        foreach (var (_, e) in file.WithSections.Where(x => Writable(x.Entry) && !Waiting(x.Entry)))
            List(e, e.NotFound is not null
                ? $"the review of `{e.NotFound}` didn't find it again, so its sentence may be gone: review again, or drop it"
                : $"it is flagged ({e.Flagged}): the PM looks at it and answers again first");
        var waiting = file.WithSections.Where(x => Waiting(x.Entry)).Select(x => new Pending(x.Entry, x.Section)).ToList();
        var ready = new List<Pending>();
        foreach (var p in waiting)
        {
            // D374: a Jira comment can come from anyone; its ruling counts once a person commits it.
            if (p.Entry.Jira is not null && TruthMap.Who(p.Entry, p.Block, blame) is not { Committed: true })
                List(p.Entry, $"drafted from a Jira reply ({(string?)p.Entry.Jira["key"]}) and not committed: commit {Rel(root, path)} to confirm it, then write");
            else ready.Add(p);
        }

        // --section with --item: which undocumented code's words go where (truth-map.md's number, the map the PM read).
        RulingEntry? placing = null;
        var undocumented = ready.Where(p => p.Entry.IsUndocumented).ToList();
        if (item is { } n)
        {
            if (section is null) throw new StangentException("usage", "--item names the code whose words go in the section --section names: give both.", ExitCodes.Usage);
            var map = File.Exists(TruthMap.JsonPath(root, doc)) ? JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(root, doc)))!.AsObject()
                : throw new StangentException("no_truth_map", $"{doc} has no truth map: run `stangent fsd rulings-check {doc}`.", ExitCodes.NotFound);
            if (mapId is { Length: > 0 } && !string.Equals(mapId.Trim(), PmAnswers.MapId(map), StringComparison.OrdinalIgnoreCase))
                throw new StangentException("map_rebuilt", $"truth-map.md was rebuilt since it was read (map {mapId.Trim()}, now {PmAnswers.MapId(map)}): read it again; nothing was written.", ExitCodes.Failed);
            var (_, en) = PmAnswers.Find(map, n);
            var key = en?["key"] as JsonObject;
            placing = undocumented.Select(p => p.Entry).FirstOrDefault(e => key is not null && e.KeyLink == (string?)key["link"] && e.KeySection == (string?)key["section"])
                ?? throw new StangentException("usage", $"Item {n} isn't code no section covers with the rule in the PM's words, waiting to be written.", ExitCodes.Usage);
        }
        else if (section is not null)
        {
            if (undocumented.Count != 1)
                throw new StangentException("usage", undocumented.Count == 0
                    ? "--section places the PM's words about code no section covers, and none is waiting to be written."
                    : $"{undocumented.Count} rulings in the PM's words about code no section covers are waiting; say which goes in {section} with --item <its number in truth-map.md>.", ExitCodes.Usage,
                    [.. undocumented.Select(p => p.Entry.Describe())]);
            placing = undocumented[0].Entry;
        }
        ChapterSection? target = null;
        if (section is not null)
        {
            var matches = ch.Read.Sections.Where(s => s.PermanentId == section || s.Id == section).ToList();
            if (matches.Count != 1)
                throw new StangentException("usage", matches.Count == 0 ? $"No section {section} in the chapter files: give its id (S-0012) or its number." : $"{section} names {matches.Count} sections: give its id (S-0012).", ExitCodes.Usage);
            target = matches[0];
        }

        foreach (var p in ready.Where(p => p.Entry.Ruling == "restated"))
        {
            var e = p.Entry;
            var who = TruthMap.Who(e, p.Block, blame);
            var old = e.Describe();
            if (e.IsUndocumented)
            {
                if (!ReferenceEquals(e, placing)) { List(e, $"the PM's words about code no section covers go where they say: run `stangent fsd write {doc} --section <id>` (with `--item <n>` when more than one waits); never guessed"); continue; }
                // Found again by its id: an edit earlier in this run may have moved its lines.
                target = ch.Section(target!.PermanentId!)!;
                if (Append(ch, target, e.Statement!) is { } why) { List(e, why); continue; }
                Move(file, p, target!);
                MakeWritten(e, target!.PermanentId!, e.Statement!, who);
                written.Add(Written(old, e, target!, "added"));
            }
            else
            {
                if (ReplaceQuote(ch, e.KeySection!, e.KeyQuote!, e.Statement!, out var where) is { } why) { List(e, why); continue; }
                MakeWritten(e, e.KeySection!, e.Statement!, who);
                written.Add(Written(old, e, where!, "replaced"));
            }
            MoveHashes(file, ch);
        }

        var drafts = ready.Where(p => p.Entry.Ruling == "code-right").ToList();
        RunStep? step = null;
        JsonObject? drafting = null;
        if (drafts.Count > 0)
        {
            if (cap is null)
                drafting = new JsonObject
                {
                    ["entries"] = drafts.Count, ["prepared"] = false,
                    ["why"] = $"an AI drafts the FSD's new words for a ruling that the code is right, which is paid (rule 9): choose a cost cap, `plugin/scripts/fsd-write-run.sh --max-cost 1 {doc}` (or `--max-cost none` for the drafter's own ${DefaultUsd})",
                };
            else
            {
                AiPolicy.EnsureAllowed(repo.Config, "fsd write");
                step = Prepare(repo, doc, file, drafts, ch, cap.Value, maxCost == "none");
                drafting = new JsonObject { ["entries"] = drafts.Count, ["prepared"] = true, ["capUsd"] = cap.Value };
            }
        }

        if (written.Count > 0)
        {
            CheckWritable(file);
            ch.Save();
            PmAnswers.Save(path, yaml, file);
        }
        return (Report(root, doc, written, listed, drafting, ch), step);
    }

    /// <summary>The rulings as they would be written must pass every check a reader makes, the schema included, or nothing is written.</summary>
    static void CheckWritable(RulingsFile file)
    {
        var (_, parse) = Rulings.Parse(Rulings.Write(file));
        var after = TruthMap.Problems(file, parse);
        if (after.Count > 0) throw new StangentException("bad_write", $"Writing would leave rulings.yaml invalid (nothing was written): {after[0]}", ExitCodes.Failed, after);
    }

    static JsonObject Written(string old, RulingEntry e, ChapterSection where, string how) => new()
    {
        ["entry"] = old, ["ruling"] = (string?)e.WrittenFrom!["ruling"], ["how"] = how, ["section"] = where.PermanentId, ["title"] = where.Title,
        ["file"] = where.File, ["words"] = e.KeyQuote,
    };

    /// <summary>Undocumented code written into a section moves into that section's block (made when it has none).</summary>
    static void Move(RulingsFile file, Pending p, ChapterSection target)
    {
        if (p.Block is not null && p.Block.SectionId == target.PermanentId) return;
        if (p.Block is not null) p.Block.Entries.Remove(p.Entry); else file.Undocumented.Remove(p.Entry);
        var block = file.Sections.FirstOrDefault(b => b.SectionId == target.PermanentId);
        if (block is null)
        {
            block = new RulingSection { Section = target.Id, SectionId = target.PermanentId!, Title = target.Title, TextHash = target.TextHash };
            file.Sections.Add(block);
        }
        block.Entries.Add(p.Entry);
    }

    /// <summary>Each written section's recorded hash moves to its new text (D386), so a refresh doesn't flag the edit.</summary>
    static void MoveHashes(RulingsFile file, Chapters ch)
    {
        foreach (var block in file.Sections)
            if (ch.Edited.Contains(block.SectionId) && ch.Section(block.SectionId) is { } s) block.TextHash = s.TextHash;
    }

    static JsonObject Report(string root, string doc, JsonArray written, JsonArray listed, JsonObject? drafting, Chapters ch) => new()
    {
        ["doc"] = doc, ["written"] = written, ["listed"] = listed, ["drafting"] = drafting,
        ["chapters"] = new JsonArray([.. ch.Changed.Order(StringComparer.Ordinal).Select(f => (JsonNode)$"specs/fsd/{doc}/chapters/{f}")]),
        ["committed"] = false,
        ["summary"] = $"{written.Count} written into the FSD, {listed.Count} listed" + (drafting is null ? "" : (bool)drafting["prepared"]! ? $", {(int)drafting["entries"]!} for the AI to draft" : $", {(int)drafting["entries"]!} waiting for a cost cap"),
        ["next"] = written.Count > 0 || drafting is not null
            ? $"Run `stangent fsd check {doc}`, read the chapter diff (`git diff specs/fsd/{doc}/chapters`) with the PM, and commit the chapters with rulings.yaml: the commit approves the words (D380)."
            : "Nothing to write.",
    };

    // ---- the AI step: prepare ------------------------------------------------------------------------------------------

    /// <summary>The drafter's input: its instructions, each code-right entry (W1, W2, …) with the sentence to replace, its
    /// section's text, the review's words and the linked code, and the output schema.</summary>
    static RunStep Prepare(StangentRepo repo, string doc, RulingsFile file, List<Pending> drafts, Chapters ch, double cap, bool none)
    {
        var root = repo.Root;
        var dir = RunDir(root, doc);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var entries = new JsonArray();
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("fsd-writer.md")).Append("\n# The rulings to write\n\n");
        var i = 0;
        foreach (var p in drafts)
        {
            var id = $"W{++i}";
            var e = p.Entry;
            entries.Add(new JsonObject { ["id"] = id, ["section"] = e.KeySection, ["quote"] = e.KeyQuote, ["state"] = e.State });
            var s = ch.Section(e.KeySection!);
            sb.Append($"## {id} · section {e.KeySection}{(s is null ? "" : $" ({s.Title})")} · {TruthMap.StateWords(e.State).ToLowerInvariant()}\n\n");
            sb.Append($"The sentence to replace (copy it into `replaces` exactly):\n\n> {Quotes.Normalize(e.KeyQuote!)}\n\n");
            foreach (var note in e.Notes) sb.Append($"The review: {note}\n\n");
            if (e.PmNote is { } pm) sb.Append($"The PM's note: {pm}\n\n");
            sb.Append($"The code it is linked to: {e.Link.Kind} `{e.Link.Id}` ({e.Link.Title}); the review's lines: {string.Join(", ", e.Evidence.Select(v => $"`{v}`"))}. Cite only lines inside these.\n\n");
            if (s is not null)
            {
                var text = string.Join("\n\n", ch.Subtree(e.KeySection!).Select(x => x.Text.Trim('\n')).Where(t => t.Length > 0));
                sb.Append("The section's text, for the words around it:\n\n").Append(string.Join("\n", text.Split('\n').Select(l => "> " + l))).Append("\n\n");
            }
        }
        sb.Append("# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(OutputSchema).TrimEnd('\n')).Append("\n```\n");
        File.WriteAllText(Path.Combine(dir, InputFile), sb.ToString());
        var state = new JsonObject
        {
            ["doc"] = doc, ["commit"] = Git.Head(root), ["capUsd"] = cap, ["capChosen"] = none ? "none" : "usd",
            ["entries"] = entries, ["retryUsed"] = false, ["usage"] = Usage.None.ToJson(), ["done"] = new JsonArray(),
        };
        MoveStage(root, state, "prepared", $"{drafts.Count} to draft");
        Save(dir, state);
        return Next(root, dir, "prepared");
    }

    static RunStep Next(string root, string dir, string status) => new(status, "write", Writer,
        $"Input: {Rel(root, Path.Combine(dir, InputFile))}\nWrite your JSON to: {Rel(root, Path.Combine(dir, OutputFile))}",
        Rel(root, Path.Combine(dir, OutputFile)), Schema: OutputSchema);

    static void MoveStage(string root, JsonObject state, string to, string reason) =>
        StateChanges.Apply(root, state, StateKinds.FsdWriteRun, $"{(string)state["doc"]!}/write", to, StateActor.Code("stangent fsd write"), reason);

    static void Save(string dir, JsonObject state) => Quotes.WriteAtomic(Path.Combine(dir, "state.json"), CanonicalJson.Serialize(state));

    /// <summary>The limits the driver runs the drafter under, as the reviewer's (D101): read-only search inside the repo, never
    /// stangent's folder, <c>specs/</c> or <c>stangent.yaml</c>, structured output, and what is left of the run's cap.</summary>
    public static JsonObject DriverLimits(StangentRepo repo, string doc)
    {
        var p = Path.GetFullPath(repo.Root).Replace('\\', '/').TrimEnd('/');
        if (p.Length >= 2 && p[1] == ':') p = "/" + char.ToLowerInvariant(p[0]) + p[2..];
        var rule = "/" + p;
        var state = PageRun.LoadJson(Path.Combine(RunDir(repo.Root, doc), "state.json")) as JsonObject;
        var cap = (double?)state?["capUsd"] ?? DefaultUsd;
        var spent = state is null ? 0 : Usage.FromJson(state["usage"]).CostUsd;
        return new JsonObject
        {
            ["root"] = rule,
            ["tools"] = "Read,Grep,Glob",
            ["allowedTools"] = new JsonArray($"Read({rule}/**)"),
            ["disallowedTools"] = new JsonArray($"Read({rule}/.stangent/**)", $"Read({rule}/.git/**)", $"Read({rule}/specs/**)", $"Read({rule}/stangent.yaml)"),
            ["permissionMode"] = "dontAsk",
            ["maxTurns"] = MaxTurns,
            ["maxBudgetUsd"] = Math.Round(Math.Max(cap - spent, 0.01), 4),
            ["commit"] = (string?)state?["commit"],
        };
    }

    // ---- the AI step: check ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>fsd write-check &lt;doc&gt;</c>: each draft checked by code, and the passing ones written as <c>fsd write</c> writes
    /// the PM's words. A draft fails when its entry isn't one asked for, its <c>replaces</c> isn't the ruled quote word for
    /// word, its words can't go in as one sentence, it cites no line, or a cited <c>path:line</c> isn't real at the run's
    /// commit or is outside the entry's linked code (one of its evidence ranges, or a line that links to the same flow,
    /// screen, node or file). One retry for the failed and missing drafts; after it they are listed.
    /// </summary>
    public static (JsonObject Result, RunStep Step) Check(StangentRepo repo, string doc, Usage? usage)
    {
        var root = repo.Root;
        var dir = RunDir(root, doc);
        var state = PageRun.LoadJson(Path.Combine(dir, "state.json")) as JsonObject
            ?? throw new StangentException("not_prepared", $"No write run for {doc}: run `stangent fsd write {doc} --max-cost <usd>` first.", ExitCodes.Usage);
        var stage = (string?)state["stage"];
        if (stage is "checked" or "failed") return (new JsonObject { ["doc"] = doc, ["run"] = stage }, new RunStep("current", "none"));
        if (stage == "retry" && usage is null && !File.Exists(Path.Combine(dir, OutputFile)))
            throw new StangentException("no_output", $"The retry has no answer yet ({Rel(root, Path.Combine(dir, OutputFile))}). Run the drafter with the retry's prompt, or pass --usage for a call that returned nothing.", ExitCodes.Usage);
        state["usage"] = Usage.FromJson(state["usage"]).Add(usage ?? Usage.None).ToJson();

        var outPath = Path.Combine(dir, OutputFile);
        var errors = new List<string>();
        var output = PageChecks.Parse(File.Exists(outPath) ? File.ReadAllText(outPath) : "", errors) as JsonObject;
        if (errors.Count == 0 && output is null) errors.Add("The output isn't a JSON object.");
        if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.ValidateDeep(OutputSchema, output!.ToJsonString()).Select(e => $"Schema: {e}"));
        if (output is null && (!File.Exists(outPath) || new FileInfo(outPath).Length == 0))
            errors.Insert(0, "You returned no answer (most likely you ran out of turns or budget). This time, answer with the drafts you have backed with code so far.");

        var path = Rulings.PathFor(root, doc);
        var yaml = File.ReadAllText(path);
        var (file, parseProblems) = Rulings.Parse(yaml);
        var problems = TruthMap.Problems(file, parseProblems);
        if (problems.Count > 0) throw new StangentException("invalid_rulings", "rulings.yaml needs fixing before anything is written (nothing was written).", ExitCodes.Failed, problems);
        var ch = new Chapters(root, doc, file);
        var blame = TruthMap.Blame(root, path);
        var linker = new TruthLinks(root, GraphStore.Load(GraphStore.PathFor(repo)));
        var commit = (string?)state["commit"] ?? "";
        var lines = new LineCounts(root, commit);

        var done = (state["done"] as JsonArray ?? []).Select(x => (string)x!).ToHashSet(StringComparer.Ordinal);
        var asked = (state["entries"] as JsonArray ?? []).OfType<JsonObject>().Where(x => !done.Contains((string)x["id"]!)).ToList();
        var drafts = errors.Count == 0 ? output!["drafts"]!.AsArray().OfType<JsonObject>().ToList() : [];
        var failed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var written = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in drafts)
        {
            var id = (string)d["entry"]!;
            var why = new List<string>();
            var a = asked.FirstOrDefault(x => (string)x["id"]! == id);
            if (a is null) { errors.Add($"draft {id}: no entry {id} was asked for{(done.Contains(id) ? " (it was written already)" : "")}"); continue; }
            if (!seen.Add(id)) { errors.Add($"draft {id}: two drafts for {id}; give one"); continue; }
            var probe = new RulingEntry { KeySection = (string?)a["section"], KeyQuote = (string?)a["quote"], State = (string)a["state"]! };
            var found = file.WithSections.FirstOrDefault(x => x.Entry.Problem is null && x.Entry.Key == probe.Key);
            if (found.Entry is not { } e || !Waiting(e) || e.Ruling != "code-right")
            {
                (failed[id] = []).Add("its ruling changed since the run was prepared (answered again, flagged, or written)");
                continue;
            }
            if (e.Jira is not null && TruthMap.Who(e, found.Section, blame) is not { Committed: true })
            {
                (failed[id] = []).Add("its ruling was drafted from a Jira reply and isn't committed now: commit rulings.yaml, then write again");
                continue;
            }
            var replaces = (string)d["replaces"]!;
            var sentence = ((string)d["sentence"]!).Trim();
            var cites = d["cites"]!.AsArray().Select(c => ((string)c!).Trim()).ToList();
            if (FsdReview.NormalizeQuote(replaces) != FsdReview.NormalizeQuote(e.KeyQuote))
                why.Add("`replaces` isn't the ruled sentence word for word: copy it exactly as given, and only it");
            if (WordsProblem(sentence) is { } wp) why.Add($"`sentence`: {wp}");
            else if (FsdReview.NormalizeQuote(sentence) == FsdReview.NormalizeQuote(e.KeyQuote)) why.Add("`sentence` is the old sentence: the ruling says the FSD needs new words");
            if (cites.Count == 0) why.Add("`cites` is empty: give the code line(s) the new sentence describes");
            foreach (var c in cites) if (CiteProblem(c, e, lines, linker) is { } cp) why.Add(cp);
            if (why.Count == 0)
            {
                var old = e.Describe();
                if (ReplaceQuote(ch, e.KeySection!, e.KeyQuote!, sentence, out var where) is { } rw) why.Add(rw);
                else
                {
                    MakeWritten(e, e.KeySection!, sentence, TruthMap.Who(e, found.Section, blame), cites);
                    MoveHashes(file, ch);
                    written.Add(Written(old, e, where!, "replaced"));
                    done.Add(id);
                    continue;
                }
            }
            failed[id] = why;
        }
        foreach (var a in asked.Where(a => !done.Contains((string)a["id"]!) && !failed.ContainsKey((string)a["id"]!) && !seen.Contains((string)a["id"]!)))
            failed[(string)a["id"]!] = ["no draft was given for it"];

        if (written.Count > 0)
        {
            CheckWritable(file);
            ch.Save();
            PmAnswers.Save(path, yaml, file);
        }
        state["done"] = new JsonArray([.. done.Order(StringComparer.Ordinal).Select(x => (JsonNode)x)]);
        var all = errors.Concat(failed.SelectMany(kv => kv.Value.Select(w => $"{kv.Key}: {w}"))).ToList();
        var listed = new JsonArray();

        if ((failed.Count > 0 || errors.Count > 0) && (bool?)state["retryUsed"] != true && asked.Any(a => !done.Contains((string)a["id"]!)))
        {
            state["retryUsed"] = true;
            MoveStage(root, state, "retry", "the code check failed");
            Save(dir, state);
            var sb = new StringBuilder("# The code check failed\n\nSome drafts weren't accepted. Draft again only these entries, fixing every problem below ");
            sb.Append("(the others are written already; after one more check, drafts that still fail are left for a person):\n\n");
            foreach (var e in all.Take(60)) sb.Append($"- {e}\n");
            File.WriteAllText(Path.Combine(dir, "feedback.md"), sb.ToString());
            var previous = Path.Combine(dir, "previous-" + OutputFile);
            if (File.Exists(outPath)) File.Move(outPath, previous, overwrite: true); else File.WriteAllText(previous, "(no output)");
            var next = Next(root, dir, "retry");
            var retry = next with { Errors = all, Prompt = next.Prompt + $"\nYour previous attempt: {Rel(root, previous)}\nWhat to fix: {Rel(root, Path.Combine(dir, "feedback.md"))}" };
            return (CheckReport(doc, written, listed, all, ch, "retry"), retry);
        }
        foreach (var a in asked.Where(a => !done.Contains((string)a["id"]!)))
            listed.Add(new JsonObject
            {
                ["entry"] = new RulingEntry { KeySection = (string?)a["section"], KeyQuote = (string?)a["quote"], State = (string)a["state"]! }.Describe(),
                ["ruling"] = "code-right",
                ["why"] = $"the AI's draft failed the code check twice ({string.Join("; ", failed.GetValueOrDefault((string)a["id"]!) ?? errors)}): write the FSD's new words by hand, or answer the item again",
            });
        MoveStage(root, state, "checked", listed.Count > 0 ? $"written, {listed.Count} listed" : "written");
        Save(dir, state);
        return (CheckReport(doc, written, listed, all, ch, "checked"), new RunStep("checked", "none", Errors: listed.Count > 0 ? [.. listed.Select(l => $"listed: {(string)l!["why"]!}")] : null));
    }

    static JsonObject CheckReport(string doc, JsonArray written, JsonArray listed, List<string> problems, Chapters ch, string stage) => new()
    {
        ["doc"] = doc, ["run"] = stage, ["written"] = written, ["listed"] = listed,
        ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        ["chapters"] = new JsonArray([.. ch.Changed.Order(StringComparer.Ordinal).Select(f => (JsonNode)$"specs/fsd/{doc}/chapters/{f}")]),
        ["committed"] = false,
        ["summary"] = $"{written.Count} drafted and written into the FSD, {listed.Count} listed" + (stage == "retry" ? ", the rest back to the drafter once" : ""),
        ["next"] = $"Run `stangent fsd check {doc}`, read the chapter diff (`git diff specs/fsd/{doc}/chapters`) with the PM, and commit the chapters with rulings.yaml: the commit approves the words (D380).",
    };

    [GeneratedRegex(@"^(?<path>.+?):(?<from>\d+)(?:\s*[-–]\s*(?<to>\d+))?$")]
    private static partial Regex PathLine();

    /// <summary>Why a cited line isn't real or isn't in the entry's linked code, or null.</summary>
    static string? CiteProblem(string cite, RulingEntry e, LineCounts lines, TruthLinks linker)
    {
        var m = PathLine().Match(cite);
        if (!m.Success) return $"cite \"{cite}\" isn't path:line (a line, or path:from-to)";
        var file = m.Groups["path"].Value.Replace('\\', '/');
        while (file.StartsWith("./", StringComparison.Ordinal)) file = file[2..];
        if (file.Length == 0 || Path.IsPathRooted(file) || file.Split('/').Contains("..")) return $"cite \"{cite}\" isn't a path inside the repo";
        if (!Int(m.Groups["from"].Value, out var from) || !Int(m.Groups["to"].Success ? m.Groups["to"].Value : m.Groups["from"].Value, out var to) || from < 1 || to < from)
            return $"cite \"{cite}\": the lines aren't a range";
        if (lines.Of(file) is not { } count) return $"cite \"{cite}\": no file {file} at the commit the run was prepared on";
        if (to > count) return $"cite \"{cite}\": line {to} is past the end of {file} ({count} lines)";
        // Inside the linked code: within one of the review's evidence ranges, or every line linking to the entry's own link.
        foreach (var ev in e.Evidence)
        {
            var em = PathLine().Match(ev.Trim());
            var evFile = (em.Success ? em.Groups["path"].Value : ev.Trim()).Replace('\\', '/').TrimEnd('/');
            while (evFile.StartsWith("./", StringComparison.Ordinal)) evFile = evFile[2..];
            if (!em.Success) { if (evFile == file || file.StartsWith(evFile + "/", StringComparison.Ordinal)) return null; continue; }
            if (!Int(em.Groups["from"].Value, out var ef) || !Int(em.Groups["to"].Success ? em.Groups["to"].Value : em.Groups["from"].Value, out var et)) continue;
            if (evFile == file && from >= ef && to <= et) return null;
        }
        if (e.Link.Id.Length > 0)
        {
            var inside = true;
            for (var l = from; l <= Math.Min(to, from + 200) && inside; l++) inside = linker.LinkEvidence($"{file}:{l}").Key == e.Link.Key;
            if (inside && to - from <= 200) return null;
        }
        return $"cite \"{cite}\" is outside the code this entry is linked to ({e.Link.Kind} `{e.Link.Id}`, lines {string.Join(", ", e.Evidence)}): cite lines inside it";
    }

    static bool Int(string s, out int n) => int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n);

    /// <summary>Line counts of files at a commit (the drafter's worktree), or in the folder outside git.</summary>
    sealed class LineCounts(string root, string commit)
    {
        readonly IReadOnlySet<string>? files = commit.Length > 0 ? Git.FilesAt(root, commit) : null;
        readonly Dictionary<string, int?> counts = new(StringComparer.Ordinal);

        public int? Of(string path)
        {
            if (counts.TryGetValue(path, out var hit)) return hit;
            string? text = null;
            if (files is not null) { if (files.Contains(path)) text = Git.Run(root, $"show \"{commit}:./{path}\""); }
            else if (File.Exists(Path.Combine(root, path))) text = File.ReadAllText(Path.Combine(root, path));
            int? count = text is null ? null : text.Length == 0 ? 0 : text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);
            counts[path] = count;
            return count;
        }
    }
}
