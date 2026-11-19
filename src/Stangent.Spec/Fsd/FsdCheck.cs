using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// <c>stangent fsd check &lt;doc&gt;</c> (phase fs layer 3, D380, D382, D384, D393): the check before a commit. Unlike every other
/// step it reads the working tree's chapter files, and compares them with <c>HEAD</c>'s by permanent id. It gives each heading
/// typed with no marker the next id from the ledger, which is the only chapter write. It refuses, writing nothing: a marker
/// that doesn't read as one, or sits off its heading; an id on two headings; an id the ledger never gave, marks removed, or gave
/// before <c>HEAD</c> and no committed heading holds; a ledger that went back; a section gone from <c>HEAD</c> while a heading has
/// no marker (it may be that section retyped: never guessed, the person puts the marker back or says <c>--removed</c>); an
/// unmarked heading that looks like a section whose marker now sits on a heading that doesn't; a heading that skips a level,
/// where <c>HEAD</c>'s didn't; and a section removed while <c>rulings.yaml</c> holds a person's work on it, unless the person
/// says <c>--removed</c> (its entries are listed). It warns about repeated or out-of-order numbers (D384) and lists every section
/// added, removed, renamed, moved or changed against <c>HEAD</c>: that list is what the commit approves. When it succeeds, a removed
/// section's entries in <c>specs/trace.yaml</c> take its mark, <c>(removed S-0004) 4</c>, decision kept, and are listed under
/// <c>trace</c> (D396); a removal undone before the commit puts them back. Code only.
/// </summary>
public static partial class FsdCheck
{
    const string Scope = "sections";

    [GeneratedRegex(@"S-\d+")]
    private static partial Regex IdText();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    public static JsonObject Run(StangentRepo repo, string doc, IReadOnlyCollection<string>? removedOnPurpose = null, IReadOnlyCollection<string>? stillSame = null)
    {
        var root = repo.Root;
        var dir = FsdImport.DocDir(root, doc);
        var rel = $"specs/fsd/{doc}";
        var facts = FsdDocument.Facts(root, doc) ?? throw new StangentException("not_found", $"No imported document {doc}.", ExitCodes.NotFound);
        if (!FsdDocument.Migrated(facts) || !FsdChapters.Has(dir)) throw FsdDocument.NotMigrated(doc);
        FsdDocument.Git(root);

        // .md in any case, as the HEAD reader takes them, so the two read the same files on any file system.
        var names = FsdChapters.Order(Directory.EnumerateFiles(FsdChapters.Dir(dir)).Select(Path.GetFileName).OfType<string>()
            .Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))).ToList();
        var raw = names.ToDictionary(n => n, n => File.ReadAllText(Path.Combine(FsdChapters.Dir(dir), n)), StringComparer.Ordinal);
        var work = FsdChapters.Read([.. names.Select(n => (n, raw[n].Replace("\r\n", "\n")))]);
        var head = FsdDocument.CommittedChapters(root, doc);
        var headRead = head?.Read;
        var ledgerPath = Path.Combine(dir, "ids.json");
        var ledger = Ledger(File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : null, $"{rel}/ids.json");
        var headLedger = Ledger(FsdDocument.Blob(root, $"{rel}/ids.json"), $"{rel}/ids.json at HEAD");
        var onPurpose = (removedOnPurpose ?? []).ToHashSet(StringComparer.Ordinal);
        var same = (stillSame ?? []).ToHashSet(StringComparer.Ordinal);

        var headById = ById(headRead);
        var workIds = work.Sections.Select(s => s.PermanentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var missing = (headRead?.Sections ?? []).Where(s => s.PermanentId is not null && !workIds.Contains(s.PermanentId))
            .DistinctBy(s => s.PermanentId).ToList();
        foreach (var id in onPurpose.Where(id => missing.All(m => m.PermanentId != id)))
            throw new StangentException("usage", $"--removed {id}: {(workIds.Contains(id) ? $"{id} is still on a heading in {rel}/chapters" : $"HEAD's {rel}/chapters has no section {id}")}, so there's no removal to confirm.", ExitCodes.Usage);

        foreach (var id in same.Where(id => !workIds.Contains(id) || !headById.ContainsKey(id)))
            throw new StangentException("usage", $"--same {id}: {id} has to be on a heading both at HEAD and now, so there's no section to keep it on.", ExitCodes.Usage);

        var refused = new JsonArray();
        var warnings = new JsonArray();
        void Refuse(string kind, string message, JsonObject? more = null)
        {
            var o = new JsonObject { ["kind"] = kind, ["message"] = message };
            foreach (var (k, v) in more ?? []) o[k] = v?.DeepClone();
            refused.Add(o);
        }
        void Warn(string kind, string message) => warnings.Add(new JsonObject { ["kind"] = kind, ["message"] = message });

        // Markers that don't read as one: which section the heading is can't be told.
        foreach (var d in work.Damaged)
            Refuse(d.Heading ? "damaged-marker" : "marker-off-heading", d.Heading
                ? $"{d.File} line {d.FileLine}: this heading's marker doesn't read as one. Write it as `<!-- S-0012 -->` at the very end of the heading line (a flag after the id is only `no-number` or `number=7`)."
                : $"{d.File} line {d.FileLine}: a marker sits on a line that isn't a heading. Put it back at the end of its heading line, or take it out if it was copied in with text.",
                new JsonObject { ["file"] = d.File, ["line"] = d.FileLine, ["text"] = d.Text });

        foreach (var id in work.Repeated)
        {
            var on = work.Sections.Where(s => s.PermanentId == id).ToList();
            Refuse("repeated-id", $"{id} sits on {on.Count} headings ({string.Join("; ", on.Select(Where))}). Each id names one section: a copied heading keeps its marker, so take it off the copy, and this check gives that heading a new id.",
                new JsonObject { ["id"] = id, ["places"] = Places(on) });
        }

        // An id is only ever given by the ledger, once (rule 11).
        // A damaged marker committed before the check existed may be repaired: the id goes back on the heading that held it
        // (same chapter file, same words), and nowhere else.
        bool Repaired(ChapterSection s) => (headRead?.Damaged ?? []).Any(d => d.Heading && d.File == s.File
            && IdText().Matches(d.Text).Any(m => m.Value == s.PermanentId) && Norm(s.Title).Length > 0 && Norm(d.Text).Contains(Norm(s.Title), StringComparison.Ordinal));
        foreach (var s in work.Sections.Where(s => s.PermanentId is not null).DistinctBy(s => s.PermanentId))
        {
            var id = s.PermanentId!;
            if (ledger.Removed.Contains(id))
                Refuse("removed-id", $"{Where(s)}: {id} is marked removed in {rel}/ids.json, and a removed id never comes back (rule 11). Take the marker off; this check gives the heading a new id.", Id(id, s));
            else if (!ledger.Given.Contains(id))
                Refuse("unknown-id", $"{Where(s)}: {id} was never given by {rel}/ids.json. An id is never typed: take the marker off, and this check gives the heading its id.", Id(id, s));
            else if (headRead is not null && headLedger.Given.Contains(id) && !headById.ContainsKey(id) && !Repaired(s))
                Refuse("old-id", $"{Where(s)}: {id} was given before HEAD and no committed heading holds it: it is an id from a section removed earlier, or copied from somewhere else. Rulings on it would land on this heading. Take the marker off; this check gives the heading a new id.", Id(id, s));
        }
        if (headLedger.Last > ledger.Last || headLedger.Removed.Any(r => !ledger.Removed.Contains(r)) || (headLedger.Last > 0 && headLedger.Format != ledger.Format))
            Refuse("ledger-went-back", $"{rel}/ids.json gives fewer ids than HEAD's (last {ledger.Last}, HEAD's {headLedger.Last}), or lost a removed id, or changed its format: ids would be given twice (rule 11). Restore it: `git checkout HEAD -- {rel}/ids.json`, then run this check again.");

        // A section gone from HEAD while a heading has no marker may be that section retyped: never guessed.
        var unmarked = work.Unmarked;
        var removed = new List<ChapterSection>();
        foreach (var m in missing)
        {
            if (unmarked.Count == 0 || onPurpose.Contains(m.PermanentId!)) { removed.Add(m); continue; }
            var like = unmarked.Where(u => Looks(u, m)).ToList();
            var marker = HeadMarker(head, m) ?? $"<!-- {m.PermanentId} -->";
            Refuse("marker-removed", like.Count > 0
                ? $"{m.PermanentId} ({Heading(m)}, in {m.File} at HEAD) is gone, and {string.Join("; ", like.Select(u => $"\"{Heading(u)}\" ({Where(u)})"))} looks like it with no marker. If it is that section, put `{marker}` back at the end of its heading, so its rulings and links stay with it. If {m.PermanentId} is removed on purpose, run again with `--removed {m.PermanentId}`."
                : $"{m.PermanentId} ({Heading(m)}, in {m.File} at HEAD) is gone, and {unmarked.Count} heading(s) have no marker ({string.Join("; ", unmarked.Take(3).Select(u => $"\"{Heading(u)}\" ({Where(u)})"))}). If one of them is {m.PermanentId} retyped, put `{marker}` back at the end of that heading. If {m.PermanentId} is removed on purpose, run again with `--removed {m.PermanentId}`.",
                new JsonObject { ["id"] = m.PermanentId, ["was"] = Heading(m), ["looksLike"] = Places(like) });
        }

        // A marker moved off its heading onto another: the unmarked heading looks like the committed section, and the heading
        // that now holds the id doesn't. Its rulings would follow the id to the wrong heading.
        foreach (var u in unmarked)
            foreach (var h in headRead?.Sections ?? [])
            {
                if (h.PermanentId is null || !Looks(u, h) || work.Sections.FirstOrDefault(w => w.PermanentId == h.PermanentId) is not { } now || Looks(now, h)) continue;
                Refuse("marker-moved", $"{h.PermanentId} was on \"{Heading(h)}\" at HEAD and now sits on \"{Heading(now)}\" ({Where(now)}), while \"{Heading(u)}\" ({Where(u)}) has no marker and looks like the committed section. If the marker moved by mistake, put it back on \"{Heading(u)}\". If both are right (renamed, and a new heading of the old name), commit the rename first, then add the new heading.",
                    new JsonObject { ["id"] = h.PermanentId, ["was"] = Heading(h), ["now"] = Where(now), ["unmarked"] = Where(u) });
            }

        // An id on another section's words: the heading holding it reads like a different committed section, by its words (or,
        // where neither has the same words, by its title while its own title changed). Its rulings would land on that text.
        // Covers a marker laid over another heading, two markers swapped, and two "Overview" headings swapping markers.
        var workById = ById(work);
        foreach (var w in workById.Values)
        {
            if (!headById.TryGetValue(w.PermanentId!, out var own) || Words(w, own)) continue;
            // The other section counts only when it no longer holds its own words (or title) itself: two sections may share a
            // placeholder ("TBD.") or a title ("Overview") and both stay where they are.
            ChapterSection? Now(ChapterSection o) => workById.GetValueOrDefault(o.PermanentId!);
            var other = headById.Values.FirstOrDefault(o => o.PermanentId != w.PermanentId && Words(w, o) && (Now(o) is not { } n1 || !Words(n1, o)))
                ?? (Norm(w.Title).Length > 0 && Norm(w.Title) != Norm(own.Title)
                    ? headById.Values.FirstOrDefault(o => o.PermanentId != w.PermanentId && Norm(o.Title) == Norm(w.Title) && (Now(o) is not { } n2 || !Looks(n2, o))) : null);
            if (other is null) continue;
            var where = workById.TryGetValue(other.PermanentId!, out var otherNow) ? $"{other.PermanentId} now sits on \"{Heading(otherNow)}\" ({Where(otherNow)})" : $"{other.PermanentId} is gone";
            Refuse("marker-on-other-section", $"\"{Heading(w)}\" ({Where(w)}) holds {w.PermanentId} (\"{Heading(own)}\" at HEAD) but reads like {other.PermanentId} (\"{Heading(other)}\" at HEAD), and {where}. {w.PermanentId}'s rulings would land on {other.PermanentId}'s text: put each marker back on its own heading. If the words really moved, commit the move of the words on its own, then any change of headings.",
                new JsonObject { ["id"] = w.PermanentId, ["readsLike"] = other.PermanentId, ["file"] = w.File, ["line"] = w.FileLine });
        }

        // Headings nest (D380): a skip the committed FSD already has is the import's, and only warned about.
        var headSkips = Skips(headRead).Select(s => s.PermanentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var s in Skips(work))
        {
            var committed = headRead is null || (s.PermanentId is not null && headSkips.Contains(s.PermanentId));
            var text = $"{Where(s)}: \"{Heading(s)}\" is level {s.Level}, more than one below the heading before it.";
            if (committed) Warn("level-skip", text + " The committed FSD has it too.");
            else Refuse("level-skip", text + " Give it one more or one fewer `#`, so it nests under the section before it.", Id(s.PermanentId, s));
        }

        // Rulings on a section that goes: refused unless the person said so; their entries stay in rulings.yaml either way.
        var rulings = new Lazy<(List<(RulingSection? Section, RulingEntry Entry)>? Entries, string? Problem)>(() => Rulings(root, doc));
        var rulingsLeft = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        foreach (var m in removed)
        {
            var (entries, problem) = rulings.Value;
            if (entries is null)
            {
                Refuse("rulings-unreadable", $"{m.PermanentId} ({Heading(m)}) is removed, and {rel}/rulings.yaml can't be read to see whether it holds rulings ({problem}). Fix the file, then run this check again.", Id(m.PermanentId, m));
                continue;
            }
            var on = On(entries, m.PermanentId!);
            if (on.Count == 0) continue;
            if (onPurpose.Contains(m.PermanentId!)) { rulingsLeft[m.PermanentId!] = on; continue; }
            Refuse("rulings-on-removed", $"{m.PermanentId} ({Heading(m)}) is removed, and {rel}/rulings.yaml holds {on.Count} entr{(on.Count == 1 ? "y" : "ies")} with a person's work on it: they'd be left with no section. Keep the heading and its marker (its words can change), or, if it goes on purpose, run again with `--removed {m.PermanentId}`: the entries stay in rulings.yaml, listed here, until they're ruled again or dropped.",
                new JsonObject { ["id"] = m.PermanentId, ["was"] = Heading(m), ["entries"] = on });
        }

        // A ruled section whose heading and words both changed may be a different section under the old id: the person says.
        foreach (var w in workById.Values)
        {
            if (!headById.TryGetValue(w.PermanentId!, out var own) || Heading(own) == Heading(w) || own.TextHash == w.TextHash || same.Contains(w.PermanentId!)) continue;
            if (rulings.Value.Entries is not { } entries || On(entries, w.PermanentId!) is not { Count: > 0 } on) continue;
            Refuse("rewritten-with-rulings", $"{w.PermanentId} has a new heading (\"{Heading(w)}\", was \"{Heading(own)}\") and new words ({Where(w)}), and {rel}/rulings.yaml holds {on.Count} entr{(on.Count == 1 ? "y" : "ies")} with a person's work on it. If it is still that section, run again with `--same {w.PermanentId}` (its rulings stay on it and come up again once its words are reviewed). If it is a different section, take its marker off so it gets a new id, and say whether {w.PermanentId} is removed.",
                new JsonObject { ["id"] = w.PermanentId, ["was"] = Heading(own), ["now"] = Heading(w), ["entries"] = on });
        }

        Numbers(work, Warn);

        var idsGiven = new JsonArray();
        if (refused.Count == 0 && unmarked.Count > 0)
        {
            var issuer = FsdVersions.Ledger(root, doc);
            var lines = names.ToDictionary(n => n, n => raw[n].Split('\n'), StringComparer.Ordinal);
            var given = new Dictionary<ChapterSection, string>();
            foreach (var u in unmarked)
            {
                var id = issuer.Issue(Scope, ledger.Format);
                given[u] = id;
                lines[u.File][u.FileLine - 1] = FsdChapters.WithMarker(lines[u.File][u.FileLine - 1], id);
                idsGiven.Add(new JsonObject { ["id"] = id, ["heading"] = Heading(u), ["file"] = u.File, ["line"] = u.FileLine });
            }
            var texts = names.ToDictionary(n => n, n => string.Join('\n', lines[n]), StringComparer.Ordinal);
            var after = FsdChapters.Read([.. names.Select(n => (n, texts[n].Replace("\r\n", "\n")))]);
            Same(work, after, given);
            foreach (var n in names.Where(n => texts[n] != raw[n])) Quotes.WriteAtomic(Path.Combine(FsdChapters.Dir(dir), n), texts[n]);
            work = after;
        }

        var changes = Changes(headRead, work, removed, rulingsLeft);

        var ok = refused.Count == 0;
        var counts = new JsonObject();
        foreach (var (k, v) in changes) counts[k] = v!.AsArray().Count;
        // A removed section's trace entries take its mark, decision and decided_by kept (D117, D396), as promote did; one whose
        // section is on a heading again (the removal undone before a commit) goes back to it. Only on success: a refusal writes nothing.
        JsonObject? trace = null;
        if (ok)
        {
            // Back on the number it has now, which may not be HEAD's (fresh check, D396).
            var back = workById.Values.Where(w => headById.ContainsKey(w.PermanentId!)).Select(FsdChapters.Json).ToList();
            if (removed.Count > 0 || Marked(root, doc, back))
                trace = FsdCarry.Apply(root, doc, [], [.. removed.Select(FsdChapters.Json)], work.Sections.Select(s => s.Id).ToHashSet(StringComparer.Ordinal), "stangent fsd check", back);
        }
        var traceMoved = trace?["traceIds"]?.AsArray().Count > 0;
        var uncommitted = ok ? Uncommitted(root, rel) : [];
        var nothing = ok && idsGiven.Count == 0 && changes.All(c => c.Value!.AsArray().Count == 0) && uncommitted.Count == 0 && !traceMoved;
        var result = new JsonObject
        {
            ["doc"] = doc,
            ["ok"] = ok,
            ["status"] = !ok ? "refused" : nothing ? "unchanged" : "ready",
            ["idsGiven"] = idsGiven,
            ["changes"] = changes,
            ["counts"] = counts,
            ["warnings"] = warnings,
            ["refused"] = refused,
            ["next"] = !ok ? $"Nothing was written. Put right what's under refused, then run `stangent fsd check {doc}` again."
                : nothing ? $"Nothing in {rel} differs from HEAD."
                : traceMoved ? $"Read the list under changes: it is what your commit approves (D380). The trace entries under trace.removed now name their removed section: commit them with it (`git add {rel} specs/trace.yaml && git commit`)."
                : $"Read the list under changes: it is what your commit approves (D380). Then commit {rel}/ (`git add {rel} && git commit`).",
        };
        if (trace is not null) result["trace"] = trace;
        if (traceMoved) uncommitted.Add("specs/trace.yaml");
        if (uncommitted.Count > 0) result["uncommittedFiles"] = new JsonArray([.. uncommitted.Select(f => (JsonNode)f)]);
        return result;
    }

    /// <summary>Whether trace.yaml holds a removed mark on one of these sections, which are on a heading again: only then is it rewritten.</summary>
    static bool Marked(string root, string doc, List<JsonObject> back)
    {
        if (back.Count == 0 || !File.Exists(Trace.TraceFile.PathIn(root))) return false;
        var ids = back.Select(b => (string)b["permanentId"]!).ToHashSet(StringComparer.Ordinal);
        return Trace.TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>().Select(e => e["fsd"]).OfType<JsonObject>()
            .Any(f => (string?)f["doc"] == doc && Trace.TraceFile.IsRemovedSection((string?)f["section"]) && (string?)f["sectionId"] is { } id && ids.Contains(id));
    }

    static FsdDocument.SectionLedger Ledger(string? text, string what)
    {
        try { return FsdDocument.LedgerOf(text); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            throw new StangentException("bad_id_ledger", $"{what} can't be read; restore it from git, ids must not be given twice (rule 11).", ExitCodes.Failed);
        }
    }

    static Dictionary<string, ChapterSection> ById(ChapterRead? read) =>
        (read?.Sections ?? []).Where(s => s.PermanentId is not null).DistinctBy(s => s.PermanentId).ToDictionary(s => s.PermanentId!, StringComparer.Ordinal);

    static string Heading(ChapterSection s) => s.Number is null ? s.Title.Trim() : $"{s.Number} {s.Title.Trim()}";

    static string Where(ChapterSection s) => $"{s.File} line {s.FileLine}";

    static JsonArray Places(IEnumerable<ChapterSection> sections) =>
        [.. sections.Select(s => (JsonNode)new JsonObject { ["heading"] = Heading(s), ["file"] = s.File, ["line"] = s.FileLine })];

    static JsonObject Id(string? id, ChapterSection s) => new() { ["id"] = id, ["heading"] = Heading(s), ["file"] = s.File, ["line"] = s.FileLine };

    static string Norm(string title) => Space().Replace(title, " ").Trim().ToLowerInvariant();

    /// <summary>Whether a heading looks like a committed section: the same title, or the same words when it has some.</summary>
    static bool Words(ChapterSection a, ChapterSection committed) => committed.Text.Trim().Length > 0 && a.TextHash == committed.TextHash;

    static bool Looks(ChapterSection a, ChapterSection committed) =>
        (Norm(a.Title).Length > 0 && Norm(a.Title) == Norm(committed.Title)) || (committed.Text.Trim().Length > 0 && a.TextHash == committed.TextHash);

    /// <summary>The marker as HEAD wrote it, flags and all, so putting it back reads the same.</summary>
    static string? HeadMarker((ChapterRead Read, List<(string Name, string Text)> Files)? head, ChapterSection s) =>
        head?.Files.FirstOrDefault(f => f.Name == s.File).Text?.Split('\n') is { } lines && s.FileLine - 1 < lines.Length ? FsdChapters.MarkerOf(lines[s.FileLine - 1]) : null;

    /// <summary>Headings more than one level below the heading before them (the first below level 1).</summary>
    static IEnumerable<ChapterSection> Skips(ChapterRead? read)
    {
        var before = 0;
        foreach (var s in read?.Sections ?? [])
        {
            if (s.Level > before + 1) yield return s;
            before = s.Level;
        }
    }

    /// <summary>D384: a number is the PM's text, so a repeat or a step back is only a warning.</summary>
    static void Numbers(ChapterRead work, Action<string, string> warn)
    {
        foreach (var g in work.Sections.Where(s => s.Number is not null).GroupBy(s => s.Number!, StringComparer.Ordinal).Where(g => g.Count() > 1))
            warn("repeated-number", $"{g.Key} is the number of {g.Count()} headings ({string.Join("; ", g.Select(s => $"\"{s.Title.Trim()}\" ({Where(s)})"))}). Numbers are yours to keep right (D384); ids, rulings and links don't depend on them.");
        // Siblings: the sections under one parent at one level, in order.
        var path = new List<(int Level, string Key)>();
        var last = new Dictionary<string, (List<int> Parts, ChapterSection Section)>(StringComparer.Ordinal);
        foreach (var s in work.Sections)
        {
            while (path.Count > 0 && path[^1].Level >= s.Level) path.RemoveAt(path.Count - 1);
            var key = $"{(path.Count > 0 ? path[^1].Key : "")}/{s.Level}";
            path.Add((s.Level, $"{key}#{s.Line}"));
            if (s.Number is null || Parts(s.Number) is not { } parts) continue;
            if (last.TryGetValue(key, out var prev) && Compare(parts, prev.Parts) < 0)
                warn("number-out-of-order", $"{s.Number} \"{s.Title.Trim()}\" ({Where(s)}) comes after {prev.Section.Number} \"{prev.Section.Title.Trim()}\". Numbers are yours to keep right (D384).");
            last[key] = (parts, s);
        }
    }

    static List<int>? Parts(string number)
    {
        var parts = new List<int>();
        foreach (var p in number.Split('.'))
            if (int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v)) parts.Add(v);
            else return null;
        return parts;
    }

    static int Compare(List<int> a, List<int> b)
    {
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Count.CompareTo(b.Count);
    }

    /// <summary>The entries of the working tree's and HEAD's <c>rulings.yaml</c>, once each by key: an entry deleted from the
    /// file in the same edit still counts.</summary>
    static (List<(RulingSection? Section, RulingEntry Entry)>? Entries, string? Problem) Rulings(string root, string doc)
    {
        var all = new List<(RulingSection?, RulingEntry)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var path = Fsd.Rulings.PathFor(root, doc);
        foreach (var text in new[] { File.Exists(path) ? File.ReadAllText(path) : null, FsdDocument.Blob(root, $"specs/fsd/{doc}/rulings.yaml") })
        {
            if (text is null) continue;
            try
            {
                var (file, _) = Fsd.Rulings.Parse(text);
                foreach (var (section, e) in file.WithSections)
                    if (keys.Add(e.Key)) all.Add((section, e));
            }
            catch (StangentException e) { return (null, e.Message); }
        }
        return (all, null);
    }

    /// <summary>A section's entries holding a person's work: those under its block, and code entries keyed on it.</summary>
    static JsonArray On(List<(RulingSection? Section, RulingEntry Entry)> entries, string id) =>
        [.. entries.Where(x => (x.Section is { SectionId.Length: > 0 } block ? block.SectionId : x.Entry.KeySection) == id && Fsd.Rulings.PersonWork(x.Entry, x.Section))
            .Select(x => (JsonNode)new JsonObject { ["entry"] = x.Entry.Describe(), ["state"] = x.Entry.State, ["ruling"] = x.Entry.Ruling ?? (x.Entry.IsMatch && x.Section is { MatchesConfirmed: true } ? "confirmed" : null) })];

    /// <summary>Giving ids must change nothing else the reader reads: a stangent bug otherwise, caught before a file is written.</summary>
    static void Same(ChapterRead before, ChapterRead after, Dictionary<ChapterSection, string> given)
    {
        var same = before.Markdown == after.Markdown && before.Sections.Count == after.Sections.Count
            && before.Sections.Zip(after.Sections).All(p => p.First with { PermanentId = given.GetValueOrDefault(p.First) ?? p.First.PermanentId } == p.Second);
        if (!same)
            throw new StangentException("chapters_out_of_step", "Adding the new ids would change how the chapters read (a stangent bug). No chapter file was written; the ids given are left unused.", ExitCodes.Failed);
    }

    /// <summary>What the commit approves, by permanent id against HEAD.</summary>
    static JsonObject Changes(ChapterRead? head, ChapterRead work, List<ChapterSection> removed, Dictionary<string, JsonArray> rulingsLeft)
    {
        var headById = ById(head);
        var headParents = Parents(head);
        var workParents = Parents(work);
        var (added, renamed, moved, changed) = (new JsonArray(), new JsonArray(), new JsonArray(), new JsonArray());
        foreach (var w in work.Sections)
        {
            if (w.PermanentId is null || !headById.TryGetValue(w.PermanentId, out var h))
            {
                added.Add(Id(w.PermanentId, w));
                continue;
            }
            var id = w.PermanentId;
            if (Heading(h) != Heading(w)) renamed.Add(new JsonObject { ["id"] = id, ["was"] = Heading(h), ["now"] = Heading(w), ["file"] = w.File, ["line"] = w.FileLine });
            var (hp, wp) = (headParents.GetValueOrDefault(h), workParents.GetValueOrDefault(w));
            if (h.File != w.File || h.Level != w.Level || hp != wp)
            {
                var o = new JsonObject { ["id"] = id, ["heading"] = Heading(w) };
                if (h.File != w.File) (o["fromFile"], o["toFile"]) = (h.File, w.File);
                if (h.Level != w.Level) (o["fromLevel"], o["toLevel"]) = (h.Level, w.Level);
                if (hp != wp) (o["fromParent"], o["toParent"]) = (hp, wp);
                moved.Add(o);
            }
            if (h.TextHash != w.TextHash) changed.Add(new JsonObject { ["id"] = id, ["heading"] = Heading(w), ["file"] = w.File, ["line"] = w.FileLine });
        }
        var gone = new JsonArray([.. removed.Select(m =>
        {
            var o = new JsonObject { ["id"] = m.PermanentId, ["heading"] = Heading(m), ["file"] = m.File };
            if (rulingsLeft.TryGetValue(m.PermanentId!, out var left)) o["rulingsLeft"] = left.DeepClone();
            return (JsonNode)o;
        })]);
        return new JsonObject { ["added"] = added, ["removed"] = gone, ["renamed"] = renamed, ["moved"] = moved, ["changed"] = changed };
    }

    /// <summary>Each section's parent by permanent id (or its heading where it has none), so a renumbered parent isn't a move.</summary>
    static Dictionary<ChapterSection, string?> Parents(ChapterRead? read)
    {
        var result = new Dictionary<ChapterSection, string?>();
        var path = new List<ChapterSection>();
        foreach (var s in read?.Sections ?? [])
        {
            while (path.Count > 0 && path[^1].Level >= s.Level) path.RemoveAt(path.Count - 1);
            result[s] = path.Count > 0 ? path[^1].PermanentId ?? Heading(path[^1]) : null;
            path.Add(s);
        }
        return result;
    }

    /// <summary>The document's files that differ from HEAD, new chapter files included (a <c>git commit -a</c> would leave those out):
    /// the commit approves them all.</summary>
    static List<string> Uncommitted(string root, string rel) =>
        global::Stangent.Graph.Git.Run(root, $"status --porcelain --untracked-files=all -- \"{rel}\"") is not { } status ? []
            : [.. status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Length > 3 ? l[3..].Trim() : l.Trim()).Order(StringComparer.Ordinal)];
}
