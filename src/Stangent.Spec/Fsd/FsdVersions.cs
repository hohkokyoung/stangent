using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// A document's versions (D117). Each section has a permanent id beside its number, from a ledger per document. A changed file
/// waits in <c>candidate/</c> with its section diff while the current version keeps serving; a person settles what code won't
/// match, and promoting makes it current and carries saved work across (<see cref="FsdCarry"/>).
/// </summary>
public static class FsdVersions
{
    const string Scope = "sections", Format = "S-{0:D4}";
    public const string FirstImportApproval = "first-import-approval";
    static readonly StateActor Code = StateActor.Code("stangent fsd import");

    static string JsonPath(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "document.json");
    public static string CandidateDir(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "candidate");
    static string DiffJson(string root, string doc) => Path.Combine(CandidateDir(root, doc), "section-diff.json");
    static string DiffMd(string root, string doc) => Path.Combine(CandidateDir(root, doc), "SECTION_DIFF.md");
    static string Pending(string root, string doc) => Path.Combine(CandidateDir(root, doc), "pending.json");
    static string CandidateKey(string doc, string hash) => $"{doc}@{hash[..12]}";

    /// <summary>The document's ledger. The lock stays out of <c>specs/</c>, which is committed.</summary>
    public static PermanentIds Ledger(string root, string doc) =>
        new(Path.Combine(FsdImport.DocDir(root, doc), "ids.json"), Path.Combine(root, ".stangent", "fsd", doc, "ids.lock"));

    /// <summary>Gives each section its next id. A ledger deleted or behind catches up with the document first, so a live id is never given again.</summary>
    internal static void IssueIds(string root, string doc, JsonObject current, IEnumerable<JsonObject> sections)
    {
        var ledger = Ledger(root, doc);
        var highest = current["sections"]!.AsArray().OfType<JsonObject>().Select(s => (string?)s["permanentId"]).OfType<string>().Select(s => int.Parse(s[2..])).DefaultIfEmpty(0).Max();
        if (highest > 0) ledger.Raise(Scope, Format, highest);
        foreach (var s in sections) s["permanentId"] = ledger.Issue(Scope, Format);
    }

    /// <summary>A document from before gets what later versions added on its next import: permanent ids (v2), in document order, and structured
    /// problems (v3). Only <c>document.json</c> changes: <c>document.md</c>, numbers, text and hashes stay, so saved AI work stays valid.</summary>
    public static int EnsureIds(string root, string doc, JsonObject current)
    {
        var missing = current["sections"]!.AsArray().OfType<JsonObject>().Where(s => s["permanentId"] is null).ToList();
        if (missing.Count == 0 && (int?)current["schemaVersion"] >= 3 && current["tables"] is not null) return 0;
        if (missing.Count > 0) IssueIds(root, doc, current, missing);
        current["schemaVersion"] = 3;
        if (current["tables"] is null) FsdObjects.Assign(root, doc, null, current);
        ImportProblems.Upgrade(current);
        ImportProblems.Attach(current);
        Validate(current);
        File.WriteAllText(JsonPath(root, doc), CanonicalJson.Serialize(current));
        return missing.Count;
    }

    internal static void Validate(JsonObject doc)
    {
        var errors = SchemaRegistry.Embedded.Validate(FsdImport.Schema, doc.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_document", "The document broke source-document v3 (a stangent bug).", ExitCodes.Failed, errors);
    }

    public static string? PendingHash(string root, string doc) => File.Exists(DiffJson(root, doc)) ? (string?)Load(root, doc)["to"] : null;

    static JsonObject Load(string root, string doc)
    {
        if (!File.Exists(DiffJson(root, doc)))
            throw new StangentException("no_candidate", $"FSD {doc} has no candidate waiting. Run `stangent fsd import` with the new file.", ExitCodes.NotFound);
        return JsonNode.Parse(File.ReadAllText(DiffJson(root, doc)))!.AsObject();
    }

    static void Save(string root, string doc, JsonObject diff)
    {
        diff["counts"] = FsdSectionDiff.Counts(diff["sections"]!.AsArray().OfType<JsonObject>());
        Quotes.WriteAtomic(DiffJson(root, doc), CanonicalJson.Serialize(diff));
        Quotes.WriteAtomic(DiffMd(root, doc), FsdSectionDiff.Markdown(diff));
    }

    /// <summary>A changed file for a document already here: it waits beside the current version. The same file again keeps what people settled; a different one starts over.</summary>
    internal static JsonObject Offer(string root, string doc, JsonObject current, JsonObject document, ConvertedDocument converted, string hash, bool flat, JsonObject archive, string why = "a changed file came in")
    {
        EnsureIds(root, doc, current);
        var dir = CandidateDir(root, doc);
        if (File.Exists(DiffJson(root, doc)))
        {
            var waiting = Load(root, doc);
            // The same file read the same way waits as it is. A newer build reading it differently is a newer candidate.
            if ((string)waiting["to"]! == hash && (bool)waiting["flat"]! == flat && File.ReadAllText(Path.Combine(dir, "document.md")) == converted.Markdown
                && FsdAttachments.Same(JsonNode.Parse(File.ReadAllText(Pending(root, doc)))!.AsObject(), document))
                return Result(doc, waiting, document, "waiting", archive);
            StateChanges.Apply(root, waiting, StateKinds.FsdCandidate, CandidateKey(doc, (string)waiting["to"]!), "replaced", Code, $"a newer file came in: {hash[..12]}");
        }
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        FsdImport.WriteText(dir, converted, document);
        Quotes.WriteAtomic(Pending(root, doc), CanonicalJson.Serialize(document));
        ImportReport.Write(dir, document);

        var key = CandidateKey(doc, hash);
        var entries = FsdSectionDiff.Compare(current["sections"]!.AsArray(), document["sections"]!.AsArray());
        foreach (var e in entries.Where(e => (string)e["kind"]! == "doubtful"))
            StateChanges.Apply(root, e, StateKinds.FsdSectionMatch, MatchKey(key, e), "doubtful", Code, "shares a sentence or a heading, and nothing exact matches");
        var diff = new JsonObject
        {
            ["version"] = 1, ["doc"] = doc, ["from"] = (string)current["original"]!["hash"]!, ["to"] = hash, ["flat"] = flat,
            ["sections"] = new JsonArray([.. entries.Select(e => (JsonNode)e)]),
        };
        if (WholeDocumentFiles(current, document) is { Count: > 0 } files) diff["attachmentsWithNoSection"] = files;
        StateChanges.Apply(root, diff, StateKinds.FsdCandidate, key, "candidate", Code, why);
        Save(root, doc, diff);
        return Result(doc, diff, document, "candidate", archive);
    }

    /// <summary>A file with no section is in no section's hash, so a change to one is said here instead.</summary>
    static JsonArray WholeDocumentFiles(JsonObject current, JsonObject document)
    {
        static List<(string Name, string Hash)> Of(JsonObject d) =>
            [.. (d["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Where(a => a["sectionNumber"] is null).Select(a => ((string)a["fileName"]!, (string)a["hash"]!))];
        return new JsonArray([.. FsdSectionDiff.Changes(Of(current), Of(document)).Select(c => (JsonNode)c)]);
    }

    static string MatchKey(string candidate, JsonObject entry) => $"{candidate}#{(string?)entry["to"] ?? (string)entry["permanentId"]!}";

    static JsonObject Result(string doc, JsonObject diff, JsonObject document, string status, JsonObject archive) => ImportReport.AddTo(new JsonObject
    {
        ["id"] = doc, ["status"] = status, ["hash"] = (string)diff["to"]!, ["from"] = (string)diff["from"]!, ["sections"] = document["sections"]!.AsArray().Count,
        ["images"] = document["images"]!.AsArray().Count, ["problems"] = document["problems"]!.DeepClone(), ["archive"] = archive,
        ["counts"] = diff["counts"]!.DeepClone(), ["doubtful"] = Open(diff), ["diff"] = $"specs/fsd/{doc}/candidate/SECTION_DIFF.md",
    }, document, $"specs/fsd/{doc}/candidate");

    static bool IsFirst(JsonObject diff) => (bool?)diff["first"] == true;

    /// <summary>
    /// A first import, with <c>first-import-approval</c> on (D122): it waits in <c>candidate/</c>, and with no <c>document.json</c> beside it nothing else can read it,
    /// until a person runs <c>fsd approve</c>. Stangent only holds it there; any PR around it is the team's own.
    /// </summary>
    internal static JsonObject OfferFirst(string root, string doc, JsonObject document, ConvertedDocument converted, string hash, bool flat, JsonObject archive)
    {
        var dir = CandidateDir(root, doc);
        JsonObject diff;
        if (File.Exists(DiffJson(root, doc)) && Load(root, doc) is var waiting && (string)waiting["to"]! == hash && (bool)waiting["flat"]! == flat) diff = waiting;
        else
        {
            ReplaceFirst(root, doc, hash);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            FsdImport.WriteText(dir, converted, document);
            Quotes.WriteAtomic(Pending(root, doc), CanonicalJson.Serialize(document));
            ImportReport.Write(dir, document);
            diff = new JsonObject { ["version"] = 1, ["doc"] = doc, ["first"] = true, ["from"] = null, ["to"] = hash, ["flat"] = flat, ["sections"] = new JsonArray() };
            StateChanges.Apply(root, diff, StateKinds.FsdCandidate, CandidateKey(doc, hash), "pending-approval", Code, "a first import, with first-import-approval on");
            diff["counts"] = FsdSectionDiff.Counts([]);
            Quotes.WriteAtomic(DiffJson(root, doc), CanonicalJson.Serialize(diff));
        }
        return ImportReport.AddTo(new JsonObject
        {
            ["id"] = doc, ["status"] = "pending-approval", ["hash"] = hash, ["sections"] = document["sections"]!.AsArray().Count, ["images"] = document["images"]!.AsArray().Count,
            ["problems"] = document["problems"]!.DeepClone(), ["archive"] = archive, ["next"] = $"stangent fsd approve {doc}",
        }, document, $"specs/fsd/{doc}/candidate");
    }

    /// <summary>A first import still waiting gives way to another file for the same document.</summary>
    internal static void ReplaceFirst(string root, string doc, string hash)
    {
        if (!File.Exists(DiffJson(root, doc))) return;
        var waiting = Load(root, doc);
        if (!IsFirst(waiting)) return;
        StateChanges.Apply(root, waiting, StateKinds.FsdCandidate, CandidateKey(doc, (string)waiting["to"]!), "replaced", Code, $"another file came in: {hash[..12]}");
        Directory.Delete(CandidateDir(root, doc), recursive: true);
    }

    /// <summary>A person approves a first import, and it becomes the current version. Only then does it get its ids and anything can read it.</summary>
    public static JsonObject Approve(string root, string doc, string who)
    {
        var diff = Load(root, doc);
        if (!IsFirst(diff))
            throw new StangentException("not_pending_approval", $"{doc}'s candidate is a new version, not a first import: run `stangent fsd promote {doc}`.", ExitCodes.Usage);
        var dir = FsdImport.DocDir(root, doc);
        var candidate = CandidateDir(root, doc);
        var pending = JsonNode.Parse(File.ReadAllText(Pending(root, doc)))!.AsObject();
        IssueIds(root, doc, pending, pending["sections"]!.AsArray().OfType<JsonObject>());
        FsdObjects.Assign(root, doc, null, pending);
        ImportProblems.Attach(pending);
        Validate(pending);
        if (Directory.Exists(Path.Combine(candidate, "images"))) Directory.Move(Path.Combine(candidate, "images"), Path.Combine(dir, "images"));
        FsdAttachments.Move(candidate, dir, pending);
        File.Move(Path.Combine(candidate, "document.md"), Path.Combine(dir, "document.md"), overwrite: true);
        Quotes.WriteAtomic(JsonPath(root, doc), CanonicalJson.Serialize(pending));
        ImportReport.Write(dir, pending);
        StateChanges.Apply(root, diff, StateKinds.FsdCandidate, CandidateKey(doc, (string)diff["to"]!), "approved", StateActor.Person(who), "first import approved");
        Directory.Delete(candidate, recursive: true);
        return ImportReport.AddTo(new JsonObject
        {
            ["id"] = doc, ["status"] = "imported", ["hash"] = (string)diff["to"]!, ["sections"] = pending["sections"]!.AsArray().Count, ["approvedBy"] = who,
        }, pending, $"specs/fsd/{doc}");
    }

    static JsonArray Open(JsonObject diff) => new([.. diff["sections"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == "doubtful")
        .Select(e => (JsonNode)((string?)e["to"] ?? (string)e["permanentId"]!))]);

    /// <summary>The waiting candidate's diff: the same JSON that's on disk.</summary>
    public static JsonObject Diff(string root, string doc) => Load(root, doc);

    /// <summary>Who's deciding: from git, never typed (D58). With no name set there's nothing true to record, so it stops.</summary>
    public static string GitPerson(string root)
    {
        var name = Git.Run(root, "config user.name")?.Trim();
        return !string.IsNullOrEmpty(name) ? name
            : throw new StangentException("no_git_identity", "A person decides this, and their name comes from git: set git config user.name first (D58).", ExitCodes.Usage);
    }

    /// <summary>A person settles one doubtful section: a candidate section is the same as an id, or new; a current section's id is removed.</summary>
    public static JsonObject Resolve(string root, string doc, string section, string? sameAs, bool isNew, bool removed, string who)
    {
        if ((sameAs is null ? 0 : 1) + (isNew ? 1 : 0) + (removed ? 1 : 0) != 1)
            throw new StangentException("usage", "Say one of --same-as <S-id>, --new or --removed.", ExitCodes.Usage);
        var diff = Load(root, doc);
        var key = CandidateKey(doc, (string)diff["to"]!);
        var entries = diff["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var entry = entries.FirstOrDefault(e => (string)e["kind"]! == "doubtful" && ((string?)e["to"] == section || (string?)e["permanentId"] == section))
            ?? throw new StangentException("not_doubtful", $"Section {section} isn't doubtful in {doc}'s candidate.", ExitCodes.NotFound, [.. Open(diff).Select(o => (string)o!)]);
        var candidateSide = (string)entry["side"]! == "candidate";
        if (removed == candidateSide)
            throw new StangentException("usage", candidateSide ? $"{section} is in the candidate: say --same-as <S-id> or --new." : $"{section} is in the current version: say --removed, or claim it from a candidate section with --same-as.", ExitCodes.Usage);
        if (removed && (string)entry["state"]! == "same")
            throw new StangentException("section_id_taken", $"A candidate section holds {section}. Settle that section another way first if this is wrong.", ExitCodes.Failed);
        var person = StateActor.Person(who);
        if (sameAs is not null) CheckFree(root, doc, entries, entry, sameAs);

        // Letting go of the id this section claimed puts that id back where it was.
        if ((string?)entry["same_as"] is { } held && held != sameAs) Release(root, key, entries, held);
        if (sameAs is not null)
        {
            StateChanges.Apply(root, entry, StateKinds.FsdSectionMatch, MatchKey(key, entry), "same", person, $"the same section as {sameAs}");
            entry["same_as"] = sameAs;
            if (entries.FirstOrDefault(e => (string)e["kind"]! == "doubtful" && (string?)e["side"] == "current" && (string?)e["permanentId"] == sameAs) is { } claimed)
            {
                StateChanges.Apply(root, claimed, StateKinds.FsdSectionMatch, MatchKey(key, claimed), "same", person, $"claimed by §{section}");
                Decided(claimed, who);
            }
        }
        else
        {
            entry["same_as"] = null;
            StateChanges.Apply(root, entry, StateKinds.FsdSectionMatch, MatchKey(key, entry), isNew ? "new" : "removed", person, isNew ? "a new section" : "this section is gone");
        }
        Decided(entry, who);
        Save(root, doc, diff);
        return new JsonObject { ["doc"] = doc, ["section"] = section, ["state"] = (string)entry["state"]!, ["same_as"] = (string?)entry["same_as"], ["decided_by"] = who, ["doubtful"] = Open(diff), ["counts"] = diff["counts"]!.DeepClone() };
    }

    static void Decided(JsonObject entry, string who) => entry["decided_by"] = who;

    static void Release(string root, string key, List<JsonObject> entries, string id)
    {
        if (entries.FirstOrDefault(e => (string)e["kind"]! == "doubtful" && (string?)e["side"] == "current" && (string?)e["permanentId"] == id) is not { } claimed) return;
        StateChanges.Apply(root, claimed, StateKinds.FsdSectionMatch, MatchKey(key, claimed), "doubtful", StateActor.Code("stangent fsd resolve"), "the section that claimed it was settled another way");
        claimed.Remove("decided_by");
    }

    /// <summary>An id can be claimed only if the current version holds it and nothing else has: an id removed earlier never comes back.</summary>
    static void CheckFree(string root, string doc, List<JsonObject> entries, JsonObject entry, string id)
    {
        if (Ledger(root, doc).Removed(Scope).Contains(id))
            throw new StangentException("section_id_removed", $"{id} was removed in an earlier version. A removed id never comes back: say --new.", ExitCodes.Failed);
        var current = JsonNode.Parse(File.ReadAllText(JsonPath(root, doc)))!["sections"]!.AsArray().OfType<JsonObject>();
        if (current.All(s => (string?)s["permanentId"] != id))
            throw new StangentException("not_found", $"The current version of {doc} has no section {id}.", ExitCodes.NotFound);
        var holder = entries.FirstOrDefault(e => !ReferenceEquals(e, entry) && (string?)e["to"] is not null && (((string)e["kind"]! != "doubtful" && (string?)e["permanentId"] == id) || (string?)e["same_as"] == id));
        if (holder is not null)
            throw new StangentException("section_id_taken", $"§{holder["to"]} already holds {id}. Settle that section another way first if this is wrong.", ExitCodes.Failed);
    }

    /// <summary>A person throws the waiting candidate away. The current version never changed, so nothing else does; the file stays in the archive and can be imported again.</summary>
    public static JsonObject Discard(string root, string doc, string who)
    {
        var diff = Load(root, doc);
        var hash = (string)diff["to"]!;
        StateChanges.Apply(root, diff, StateKinds.FsdCandidate, CandidateKey(doc, hash), "discarded", StateActor.Person(who), "discarded");
        Directory.Delete(CandidateDir(root, doc), recursive: true);
        return new JsonObject { ["id"] = doc, ["discarded"] = hash, ["current"] = (string)diff["from"]!, ["by"] = who };
    }

    /// <summary>Makes the candidate current. Refused while anything's doubtful. Nothing is dropped: what a removed section held is listed, and stays until a person acts.</summary>
    public static JsonObject Promote(StangentRepo repo, string doc, string who)
    {
        var root = repo.Root;
        var diff = Load(root, doc);
        if (IsFirst(diff))
            throw new StangentException("needs_approval", $"{doc} is a first import waiting for approval, with no current version to carry anything from: run `stangent fsd approve {doc}`.", ExitCodes.Usage);
        var left = Open(diff);
        if (left.Count > 0)
            throw new StangentException("doubtful_sections_remain", $"{left.Count} section(s) of {doc}'s candidate are doubtful. Settle each with `stangent fsd resolve`.", ExitCodes.Failed, [.. left.Select(o => (string)o!)]);
        var current = JsonNode.Parse(File.ReadAllText(JsonPath(root, doc)))!.AsObject();
        if ((string)current["original"]!["hash"]! != (string)diff["from"]!)
            throw new StangentException("candidate_out_of_date", $"{doc}'s candidate was compared with a version that isn't current any more. Import the file again.", ExitCodes.Failed);
        var pending = JsonNode.Parse(File.ReadAllText(Pending(root, doc)))!.AsObject();
        var entries = diff["sections"]!.AsArray().OfType<JsonObject>().ToList();

        // Who each candidate section is: an exact match or a person's word keeps the id; anything else is new.
        var oldById = current["sections"]!.AsArray().OfType<JsonObject>().ToDictionary(s => (string)s["permanentId"]!, StringComparer.Ordinal);
        var kept = new Dictionary<string, (string Id, string Kind)>(StringComparer.Ordinal);
        foreach (var e in entries.Where(e => (string?)e["to"] is not null))
        {
            if ((string)e["kind"]! == "doubtful") { if ((string)e["state"]! == "same") kept[(string)e["to"]!] = ((string)e["same_as"]!, "settled"); }
            else if ((string)e["kind"]! != "added") kept[(string)e["to"]!] = ((string)e["permanentId"]!, (string)e["kind"]!);
        }
        var sections = pending["sections"]!.AsArray().OfType<JsonObject>().ToList();
        foreach (var s in sections.Where(s => kept.ContainsKey((string)s["id"]!))) s["permanentId"] = kept[(string)s["id"]!].Id;
        IssueIds(root, doc, current, sections.Where(s => !kept.ContainsKey((string)s["id"]!)));
        FsdObjects.Assign(root, doc, current, pending);
        ImportProblems.Upgrade(pending);
        ImportProblems.Attach(pending);
        pending["schemaVersion"] = 3;
        Validate(pending);

        var claimed = kept.Values.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
        var gone = oldById.Where(o => !claimed.Contains(o.Key)).Select(o => o.Value).ToList();
        var pairs = sections.Where(s => kept.ContainsKey((string)s["id"]!))
            .Select(s => new FsdCarry.Pair(oldById[kept[(string)s["id"]!].Id], s, kept[(string)s["id"]!].Kind)).ToList();
        // With attachments in the checks, what a judge saw includes them, so a run's hash is worked out with them on both sides, and a changed file means a new run.
        if (repo.Config.Fsd.AttachmentsInChecks)
        {
            foreach (var s in oldById.Values) AttachmentChecks.Fill(s, current, FsdImport.DocDir(root, doc), false, repo.Config.Fsd.AttachmentChars);
            foreach (var s in sections) AttachmentChecks.Fill(s, pending, CandidateDir(root, doc), true, repo.Config.Fsd.AttachmentChars);
        }
        var carried = FsdCarry.Apply(root, doc, pairs, gone, sections.Select(s => (string)s["id"]!).ToHashSet(StringComparer.Ordinal), repo.Config.Fsd.Findings);
        foreach (var s in sections) s.Remove(AttachmentChecks.Key);

        var ledger = Ledger(root, doc);
        foreach (var s in gone) ledger.Remove(Scope, (string)s["permanentId"]!);
        var dir = FsdImport.DocDir(root, doc);
        var candidate = CandidateDir(root, doc);
        if (Directory.Exists(Path.Combine(dir, "images"))) Directory.Delete(Path.Combine(dir, "images"), recursive: true);
        if (Directory.Exists(Path.Combine(candidate, "images"))) Directory.Move(Path.Combine(candidate, "images"), Path.Combine(dir, "images"));
        FsdAttachments.Move(candidate, dir, pending);
        File.Move(Path.Combine(candidate, "document.md"), Path.Combine(dir, "document.md"), overwrite: true);
        Quotes.WriteAtomic(JsonPath(root, doc), CanonicalJson.Serialize(pending));
        ImportReport.Write(dir, pending);

        StateChanges.Apply(root, diff, StateKinds.FsdCandidate, CandidateKey(doc, (string)diff["to"]!), "promoted", StateActor.Person(who), "promoted");
        var log = new JsonObject
        {
            ["at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
            ["from"] = (string)diff["from"]!, ["to"] = (string)diff["to"]!, ["by"] = who, ["counts"] = diff["counts"]!.DeepClone(), ["carried"] = carried.DeepClone(),
        };
        File.AppendAllText(Path.Combine(dir, "promotions.jsonl"), CanonicalJson.SerializeCompact(log) + "\n");
        Directory.Delete(candidate, recursive: true);

        // The existing drop-back (D58): a decision whose quote is gone goes back to undecided, here rather than at the next CI run.
        var check = Trace.TraceCheck.Run(root, null, null, fix: true);
        return ImportReport.AddTo(new JsonObject
        {
            ["id"] = doc, ["hash"] = (string)diff["to"]!, ["from"] = (string)diff["from"]!, ["sections"] = sections.Count, ["counts"] = diff["counts"]!.DeepClone(),
            ["carried"] = carried, ["staleQuotes"] = check["staleQuotes"]!.DeepClone(), ["traceProblems"] = check["problems"]!.DeepClone(),
        }, pending, $"specs/fsd/{doc}");
    }
}
