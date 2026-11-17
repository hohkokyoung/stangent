using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// A new file for a document already here is compared, never imported over (phase fs layer 5, D385, D388). <c>fsd import</c> (and
/// <c>fsd reread</c>) write <c>.stangent/fsd/&lt;doc&gt;/compare/</c> and change no chapter. Each section of the new file is matched to
/// a permanent id (<see cref="FsdSectionDiff.Compare"/>: first by the text hash and heading path the last imported file gave, kept
/// in <c>document.json</c>'s <c>fileSections</c>, then by the repo's text), a doubtful one settled by a person (<c>fsd resolve</c>).
/// Then three ways, per section, against the last file: only the file changed it (offered), only the repo did (kept), both did
/// (shown side by side, the repo's kept), new in the file (offered), gone from the file (listed). <c>fsd take</c> copies an
/// offered section's text into its chapter, uncommitted, and moves its <c>fileSections</c> entry; the commit approves (D380).
/// Code only. Nothing here writes a chapter except <see cref="Take"/>, and it writes only a section whose text in the working
/// tree is HEAD's and, at HEAD, the last file's: a section the PM changed is never offered.
/// </summary>
public static partial class FsdCompare
{
    public const int Version = 1;
    const string Figures = "figures", FigureFormat = "F-{0:D4}";
    static readonly StateActor Code = StateActor.Code("stangent fsd import");

    public static string Dir(string root, string doc) => Path.Combine(root, ".stangent", "fsd", doc, "compare");
    static string JsonFile(string root, string doc) => Path.Combine(Dir(root, doc), "comparison.json");
    static string MdFile(string root, string doc) => Path.Combine(Dir(root, doc), "comparison.md");
    static string Rel(string doc) => $".stangent/fsd/{doc}/compare";

    [GeneratedRegex(@"\((images/[^)\s]+)\)")]
    private static partial Regex ImageRef();

    // ---- the last file's sections -----------------------------------------------------------------------------------

    /// <summary><c>document.json</c>'s <c>fileSections</c> for these sections: each one's heading and text hash as the file gave them, by permanent id.</summary>
    public static JsonArray FileSections(IReadOnlyList<JsonObject> sections)
    {
        var paths = FsdSectionDiff.Paths(sections);
        return new JsonArray([.. sections.Where(s => s["permanentId"] is not null).Select(s => (JsonNode)Record(s, paths[(string)s["id"]!]))]);
    }

    static JsonObject Record(JsonObject s, string path)
    {
        var o = new JsonObject
        {
            ["section"] = (string)s["permanentId"]!, ["title"] = (string)s["title"]!,
            ["path"] = new JsonArray([.. path.Split(FsdSectionDiff.PathMark).Select(p => (JsonNode)p)]),
            ["textHash"] = (string?)s["textHash"] ?? CanonicalJson.Hash((string)s["text"]!),
        };
        if ((string?)s["idKind"] == "number") o["number"] = (string)s["id"]!;
        return o;
    }

    sealed record Base(List<JsonObject> Records, string From);

    /// <summary>
    /// The last file's sections. From <c>document.json</c>'s <c>fileSections</c> (written by the import, <c>fsd migrate</c> and
    /// <c>fsd take</c> since layer 5); for a document written before them, from the chapters in the commit that approved its import:
    /// the first commit after <c>importCommit</c> that touched them, which an import or migration wrote from the file.
    /// </summary>
    static Base BaseOf(string root, string doc, JsonObject facts)
    {
        if (facts["fileSections"] is JsonArray held) return new Base([.. held.OfType<JsonObject>()], "document.json");
        var chapters = $"specs/fsd/{doc}/chapters";
        var range = (string?)facts["importCommit"] is { } at ? $"{at}..HEAD" : "HEAD";
        var commit = Git.Run(root, $"log --reverse --format=%H {range} -- \"{chapters}\"")?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? throw new StangentException("no_base", $"{doc}'s document.json holds no fileSections, and no commit after its import touched {chapters}: what the last file said can't be told, so nothing can be compared. Import the file it came from into a new document id.", ExitCodes.Failed);
        var tree = Git.Run(root, $"rev-parse --verify --quiet \"{commit}:./{chapters}\"")?.Trim()
            ?? throw new StangentException("no_base", $"{chapters} isn't in commit {commit[..12]}.", ExitCodes.Failed);
        var names = FsdChapters.Order((Git.Run(root, $"ls-tree -z --name-only {tree}") ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
        var read = FsdChapters.Read([.. names.Select(n => (n, (Git.Run(root, $"cat-file blob \"{tree}:{n}\"") ?? "").Replace("\r\n", "\n")))]);
        var sections = read.Sections.Select(FsdChapters.Json).ToList();
        return new Base([.. FileSections(sections).OfType<JsonObject>()], $"commit {commit[..12]}");
    }

    static FsdSectionDiff.BaseSection Matchable(JsonObject r) =>
        new((string)r["section"]!, string.Join(FsdSectionDiff.PathMark, r["path"]!.AsArray().Select(p => (string)p!)), (string)r["textHash"]!);

    static string Heading(string? number, string title) => (number ?? "") + "\u0002" + title;
    /// <summary>A section's heading as written: its number (without the " (2)" a repeated one's id gets) and its title.</summary>
    static string Heading(JsonObject s) => Heading((string?)s["idKind"] == "number" ? Regex.Replace((string)s["id"]!, @" \(\d+\)$", "") : null, (string)s["title"]!);
    static string BaseHeading(JsonObject r) => Heading((string?)r["number"], (string)r["title"]!);

    // ---- compare ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A new file for a document whose FSD is committed: the comparison, and no chapter changed. <paramref name="document"/> is the
    /// file's record (sections with no permanent ids), <paramref name="converted"/> its text. The same file compared again against
    /// the same commit keeps what people settled; anything else starts over.
    /// </summary>
    internal static JsonObject Compare(StangentRepo repo, string doc, JsonObject document, ConvertedDocument converted, ImportResult import, string hash, JsonObject archived, string why)
    {
        var root = repo.Root;
        FsdDocument.Load(root, doc); // refuses a document not committed, not migrated or not readable
        var facts = FsdDocument.CommittedFacts(root, doc);
        var lines = converted.Markdown.Split('\n');
        var fileSections = document["sections"]!.AsArray().OfType<JsonObject>().Select(s => s.DeepClone().AsObject()).ToList();
        for (var i = 0; i < fileSections.Count; i++)
        {
            var start = (int)fileSections[i]["line"]!;
            var end = i + 1 < fileSections.Count ? (int)fileSections[i + 1]["line"]! - 1 : lines.Length;
            fileSections[i]["heading"] = lines[start - 1];
            fileSections[i]["body"] = string.Join('\n', lines[start..end].Select(FsdChapters.Escape));
        }
        var cmp = new JsonObject
        {
            ["version"] = Version, ["doc"] = doc, ["why"] = why,
            ["file"] = new JsonObject
            {
                ["hash"] = hash, ["fileName"] = (string)document["original"]!["fileName"]!, ["archive"] = archived.DeepClone(),
                ["converter"] = import.ConverterJson(),
            },
            ["fileSections"] = new JsonArray([.. fileSections.Select(s => (JsonNode)s)]),
            ["fileAttachments"] = document["attachments"]?.DeepClone() ?? new JsonArray(),
            ["fileProblems"] = document["problems"]!.DeepClone(), ["verdict"] = ImportReport.Verdict(document),
            ["images"] = new JsonArray([.. converted.Images.Select(i =>
            {
                var o = new JsonObject { ["file"] = i.Name };
                if (i.Caption is not null) o["caption"] = i.Caption;
                return (JsonNode)o;
            })]),
        };
        if (Load(root, doc) is { } before && (string?)before["file"]?["hash"] == hash) cmp["settled"] = before["settled"]?.DeepClone();
        var dir = Dir(root, doc);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var img in converted.Images)
        {
            Directory.CreateDirectory(Path.Combine(dir, "images"));
            File.WriteAllBytes(Path.Combine(dir, img.Name), img.Bytes);
        }
        // The new file's own import report, as a first import has one, for reading before anything is taken.
        ImportReport.Write(dir, document);
        Build(root, doc, cmp, facts, fresh: true);
        Save(root, doc, cmp);
        return Result(cmp, "compared");
    }

    /// <summary>The entries from the file's sections, HEAD and the last file's: matching, what people settled, and each section's case.</summary>
    static void Build(string root, string doc, JsonObject cmp, JsonObject facts, bool fresh)
    {
        var head = FsdDocument.Load(root, doc);
        var repoSections = head["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var last = BaseOf(root, doc, facts);
        // A base read from a commit's chapters matches sections, and says nothing about who changed what: that commit can hold
        // the PM's edits made before it approved the import (fresh check, layer 5). So no section is offered from it: every one
        // that differs is shown side by side with the repo's kept.
        var basis = last.From == "document.json" ? last.Records.ToDictionary(r => (string)r["section"]!, StringComparer.Ordinal) : [];
        cmp["head"] = Git.Run(root, "rev-parse --verify --quiet HEAD")?.Trim();
        cmp["chaptersTree"] = ChaptersTree(root, doc);
        cmp["factsHash"] = CanonicalJson.Hash(CanonicalJson.Serialize(facts));
        cmp["base"] = new JsonObject
        {
            ["from"] = last.From, ["fileHash"] = (string)facts["original"]!["hash"]!, ["fileName"] = (string)facts["original"]!["fileName"]!,
            ["converter"] = facts["converter"]?.DeepClone(),
        };
        cmp["converterChanged"] = ConverterNote(facts, cmp["file"]!["converter"]!.AsObject());
        if (last.From == "document.json") cmp.Remove("baseNote");
        else cmp["baseNote"] = $"{doc}'s document.json doesn't record what the last file said per section (it was written before phase fs layer 5), so its sections were matched by the chapters of {last.From}, which may hold the PM's own edits: no update is offered, and each difference is shown side by side with the repo's text kept. New sections can still be taken; copy anything else by hand.";

        var fileSections = cmp["fileSections"]!.AsArray().OfType<JsonObject>().ToList();
        var matched = FsdSectionDiff.Compare(new JsonArray([.. repoSections.Select(s => s.DeepClone())]), new JsonArray([.. fileSections.Select(Matching)]), [.. last.Records.Select(Matchable)]);
        var settled = cmp["settled"] as JsonObject ?? new JsonObject();
        cmp["settled"] = settled;
        var key = $"{doc}@{((string)cmp["file"]!["hash"]!)[..12]}";
        var repoById = repoSections.Where(s => s["permanentId"] is not null).ToDictionary(s => (string)s["permanentId"]!, StringComparer.Ordinal);
        var fileById = fileSections.ToDictionary(s => (string)s["id"]!, StringComparer.Ordinal);

        // A person's word on a doubtful section: a file section is an id (same) or new; a repo section is gone from the file.
        foreach (var e in matched.Where(e => (string)e["kind"]! == "doubtful"))
        {
            var name = Name(e);
            if (settled[name] is JsonObject said && Valid(said, e, repoById))
            {
                e["state"] = (string)said["state"]!;
                e["decided_by"] = (string)said["by"]!;
                if ((string?)said["sameAs"] is { } same) e["same_as"] = same;
            }
            else
            {
                settled.Remove(name);
                e["state"] = "doubtful";
                // Recorded once per comparison: settling builds the entries again, and that adds no line.
                if (fresh) StateChanges.Move(root, StateKinds.FsdSectionMatch, $"{key}#{name}", null, "doubtful", Code, "shares a sentence or a heading, and nothing exact matches");
            }
        }
        // A repo section another one claimed is settled with it.
        var claimed = matched.Select(e => (string?)e["same_as"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var e in matched.Where(e => (string)e["kind"]! == "doubtful" && (string?)e["side"] == "current" && (string?)e["state"] == "doubtful" && claimed.Contains((string)e["permanentId"]!)))
            e["state"] = "same";

        var entries = new List<JsonObject>();
        foreach (var e in matched)
        {
            var kind = (string)e["kind"]!;
            if (kind == "doubtful" && (string?)e["side"] == "current")
            {
                var pid = (string)e["permanentId"]!;
                if ((string?)e["state"] == "same") continue; // claimed: its pair says what it is
                entries.Add((string?)e["state"] == "removed" ? Gone(repoById[pid], e) : Doubtful(e, repoById[pid], null));
                continue;
            }
            if (kind == "removed") { entries.Add(Gone(repoById[(string)e["permanentId"]!], null)); continue; }
            var file = fileById[(string)e["to"]!];
            if (kind == "added" || (kind == "doubtful" && (string?)e["state"] == "new")) { entries.Add(New(file, e)); continue; }
            if (kind == "doubtful" && (string?)e["state"] != "same") { entries.Add(Doubtful(e, null, file)); continue; }
            var pidOf = kind == "doubtful" ? (string)e["same_as"]! : (string)e["permanentId"]!;
            if ((bool?)e["repoGone"] == true || !repoById.TryGetValue(pidOf, out var repoSection))
            {
                entries.Add(new JsonObject { ["case"] = "removed-here", ["section"] = pidOf, ["file"] = (string)file["id"]!, ["title"] = (string)file["title"]!, ["note"] = "the last file had it and the repo took it out; the new file still has it, and nothing here brings it back" });
                continue;
            }
            entries.Add(Pair(file, repoSection, basis.GetValueOrDefault(pidOf), e));
        }
        cmp["sections"] = new JsonArray([.. entries.Select(x => (JsonNode)x)]);
        // The new file's import problems, each on its section's permanent id where the file's section has one here.
        var pidOfFile = entries.Where(x => (string?)x["file"] is not null && (string?)x["section"] is not null).ToDictionary(x => (string)x["file"]!, x => (string)x["section"]!, StringComparer.Ordinal);
        cmp["problems"] = new JsonArray([.. (cmp["fileProblems"]?.AsArray() ?? []).OfType<JsonObject>().Select(p =>
        {
            var c = p.DeepClone().AsObject();
            c.Remove("section");
            if ((string?)c["sectionNumber"] is { } n && pidOfFile.TryGetValue(n, out var pid)) c["section"] = pid;
            return (JsonNode)c;
        })]);
        cmp["counts"] = Counts(entries);
        if (WholeDocumentFiles(facts, cmp) is { Count: > 0 } whole) cmp["attachmentsWithNoSection"] = whole;
    }

    /// <summary>The file's section as the matcher reads it: its id, title, parent and text.</summary>
    static JsonNode Matching(JsonObject s)
    {
        var o = new JsonObject
        {
            ["id"] = (string)s["id"]!, ["idKind"] = (string)s["idKind"]!, ["title"] = (string)s["title"]!, ["level"] = (int)s["level"]!,
            ["text"] = (string)s["text"]!, ["parent"] = (string?)s["parent"],
        };
        // A section's files, so one that changed with the words the same is named (D123).
        if (s["contentHash"] is not null) o["contentHash"] = s["contentHash"]!.DeepClone();
        if (s["attachments"] is not null) o["attachments"] = s["attachments"]!.DeepClone();
        return o;
    }

    static string Name(JsonObject doubtful) => (string?)doubtful["side"] == "candidate" ? (string)doubtful["to"]! : (string)doubtful["permanentId"]!;

    /// <summary>A settled answer still fits the entry: the id it names is still one of the repo's, and still on the entry's side.</summary>
    static bool Valid(JsonObject said, JsonObject e, Dictionary<string, JsonObject> repoById) => (string?)said["state"] switch
    {
        "same" => (string?)e["side"] == "candidate" && (string?)said["sameAs"] is { } id && repoById.ContainsKey(id),
        "new" => (string?)e["side"] == "candidate",
        "removed" => (string?)e["side"] == "current",
        _ => false,
    };

    static JsonObject Pair(JsonObject file, JsonObject repo, JsonObject? last, JsonObject match)
    {
        var (fileHash, repoHash) = ((string)file["textHash"]!, (string)repo["textHash"]!);
        var fileChanged = last is null || fileHash != (string)last["textHash"]! || Heading(file) != BaseHeading(last);
        var repoChanged = last is null || repoHash != (string)last["textHash"]! || Heading(repo) != BaseHeading(last);
        var same = fileHash == repoHash && Heading(file) == Heading(repo);
        var @case = same ? "same" : !fileChanged ? "repo-changed" : !repoChanged ? "file-changed" : "both-changed";
        var o = new JsonObject
        {
            ["case"] = @case, ["section"] = (string)repo["permanentId"]!, ["file"] = (string)file["id"]!, ["repo"] = (string)repo["id"]!,
            ["title"] = (string)repo["title"]!,
        };
        if ((string)file["title"]! != (string)repo["title"]!) o["fileTitle"] = (string)file["title"]!;
        if (@case != "same")
        {
            o["textChanged"] = fileHash != repoHash;
            o["headingChanged"] = Heading(file) != Heading(repo);
        }
        if (last is null) o["noBase"] = true;   // no entry for it, or a base read from a commit: never offered
        if ((int)file["level"]! != (int)repo["level"]!) o["levelChanged"] = new JsonObject { ["repo"] = (int)repo["level"]!, ["file"] = (int)file["level"]! };
        if (match["fromPath"] is not null) o["fileMovedIt"] = new JsonObject { ["from"] = match["fromPath"]!.DeepClone(), ["to"] = match["toPath"]!.DeepClone() };
        if (match["attachmentsChanged"] is JsonArray files) o["attachmentsChanged"] = files.DeepClone();
        if ((string?)match["decided_by"] is { } by) o["settledBy"] = by;
        return o;
    }

    static JsonObject New(JsonObject file, JsonObject match)
    {
        var o = new JsonObject { ["case"] = "new", ["section"] = null, ["file"] = (string)file["id"]!, ["title"] = (string)file["title"]! };
        if ((string?)match["decided_by"] is { } by) o["settledBy"] = by;
        return o;
    }

    static JsonObject Gone(JsonObject repo, JsonObject? match)
    {
        var o = new JsonObject { ["case"] = "gone", ["section"] = (string)repo["permanentId"]!, ["repo"] = (string)repo["id"]!, ["title"] = (string)repo["title"]! };
        if ((string?)match?["decided_by"] is { } by) o["settledBy"] = by;
        return o;
    }

    static JsonObject Doubtful(JsonObject e, JsonObject? repo, JsonObject? file)
    {
        var o = new JsonObject
        {
            ["case"] = "doubtful", ["side"] = file is not null ? "file" : "repo", ["section"] = repo is null ? null : (string)repo["permanentId"]!,
            ["file"] = file is null ? null : (string)file["id"]!, ["repo"] = repo is null ? null : (string)repo["id"]!,
            ["title"] = (string)(file ?? repo)!["title"]!, ["path"] = e["path"]?.DeepClone(),
            ["candidates"] = e["candidates"]!.DeepClone(), ["state"] = "doubtful",
        };
        if (e["suggestions"] is JsonArray s) o["suggestions"] = s.DeepClone();
        if (e["splitInto"] is JsonArray split) o["splitInto"] = split.DeepClone();
        if (e["splitFrom"] is not null) o["splitFrom"] = e["splitFrom"]!.DeepClone();
        return o;
    }

    static string? ConverterNote(JsonObject facts, JsonObject now)
    {
        if (facts["converter"] is not JsonObject was)
            return $"The last import of this document didn't record its converter, so whether {(string)now["format"]!} converter version {(int)now["version"]!} reads the same file the same way can't be told: a section the file seems to have changed may be the converter's doing. Read each update before taking it.";
        if ((string)was["format"]! == (string)now["format"]! && (int)was["version"]! == (int)now["version"]!) return null;
        return $"The converter differs from the last import's ({(string)was["format"]!} v{(int)was["version"]!} then, {(string)now["format"]!} v{(int)now["version"]!} now): a section the file seems to have changed may be the converter's doing, not the client's. Read each update before taking it.";
    }

    /// <summary>A file with no section is in no section's hash, so a change to one is said here instead.</summary>
    static JsonArray WholeDocumentFiles(JsonObject facts, JsonObject cmp)
    {
        static List<(string Name, string Hash)> Of(JsonNode? a) =>
            [.. (a?.AsArray() ?? []).OfType<JsonObject>().Where(x => x["sectionNumber"] is null).Select(x => ((string)x["fileName"]!, (string)x["hash"]!))];
        return new JsonArray([.. FsdSectionDiff.Changes(Of(facts["attachments"]), Of(cmp["fileAttachments"])).Select(c => (JsonNode)c)]);
    }

    static readonly string[] Cases = ["file-changed", "new", "both-changed", "repo-changed", "gone", "removed-here", "doubtful", "same"];

    static JsonObject Counts(List<JsonObject> entries)
    {
        var o = new JsonObject();
        foreach (var c in Cases) o[c] = entries.Count(e => (string)e["case"]! == c);
        return o;
    }

    static string? ChaptersTree(string root, string doc) => Git.Run(root, $"rev-parse --verify --quiet \"HEAD:./specs/fsd/{doc}/chapters\"")?.Trim();

    public static JsonObject? Load(string root, string doc) =>
        File.Exists(JsonFile(root, doc)) ? JsonNode.Parse(File.ReadAllText(JsonFile(root, doc)))!.AsObject() : null;

    static JsonObject Need(string root, string doc) => Load(root, doc)
        ?? throw new StangentException("no_comparison", $"{doc} has no comparison: run `stangent fsd import <file> --id {doc}` with the new file.", ExitCodes.NotFound);

    static void Save(string root, string doc, JsonObject cmp)
    {
        Quotes.WriteAtomic(JsonFile(root, doc), CanonicalJson.Serialize(cmp));
        Quotes.WriteAtomic(MdFile(root, doc), Markdown(root, cmp));
    }

    /// <summary>The comparison, read again (<c>fsd diff</c>): the same JSON that's on disk.</summary>
    public static JsonObject Show(string root, string doc, bool markdown = false)
    {
        var cmp = Need(root, doc);
        if (markdown) cmp["markdown"] = File.Exists(MdFile(root, doc)) ? File.ReadAllText(MdFile(root, doc)) : Markdown(root, cmp);
        return cmp;
    }

    static JsonArray Ids(JsonObject cmp, string @case) => new([.. Entries(cmp).Where(e => (string)e["case"]! == @case).Select(e => (JsonNode)((string?)e["section"] ?? (string)e["file"]!))]);

    static List<JsonObject> Entries(JsonObject cmp) => [.. cmp["sections"]!.AsArray().OfType<JsonObject>()];

    static JsonObject Result(JsonObject cmp, string status)
    {
        var doc = (string)cmp["doc"]!;
        var doubtful = new JsonArray([.. Entries(cmp).Where(e => (string)e["case"]! == "doubtful").Select(e => (JsonNode)((string?)e["file"] ?? (string)e["section"]!))]);
        var offered = (int)cmp["counts"]!["file-changed"]! + (int)cmp["counts"]!["new"]!;
        var r = new JsonObject
        {
            ["id"] = doc, ["status"] = status, ["hash"] = (string)cmp["file"]!["hash"]!, ["from"] = (string)cmp["base"]!["fileHash"]!,
            ["counts"] = cmp["counts"]!.DeepClone(), ["doubtful"] = doubtful, ["updates"] = Ids(cmp, "file-changed"), ["new"] = Ids(cmp, "new"),
            ["bothChanged"] = Ids(cmp, "both-changed"), ["comparison"] = $"{Rel(doc)}/comparison.md",
            ["verdict"] = (string?)cmp["verdict"], ["report"] = $"{Rel(doc)}/{ImportReport.FileName}", ["problems"] = cmp["problems"]?.DeepClone(),
            ["next"] = doubtful.Count > 0 ? $"Settle each doubtful section with `stangent fsd resolve {doc} …` (comparison.md says how), then read the comparison."
                : offered > 0 ? $"Read {Rel(doc)}/comparison.md. Take what you want with `stangent fsd take {doc} <section>` or `--all-updates`, then `stangent fsd check {doc}` and commit: the commit approves (D380). No chapter changed."
                : "Nothing in the new file is offered. No chapter changed.",
        };
        if ((string?)cmp["converterChanged"] is { } note) r["converterChanged"] = note;
        if ((string?)cmp["baseNote"] is { } baseNote) r["baseNote"] = baseNote;
        return r;
    }

    // ---- settle -----------------------------------------------------------------------------------------------------

    /// <summary>A person settles one doubtful section (<c>fsd resolve</c>): a file section is the repo's section with this id, or new;
    /// a repo section is gone from the file (listed, never deleted). Who comes from git. Refused when HEAD moved since the comparison.</summary>
    public static JsonObject Resolve(string root, string doc, string section, string? sameAs, bool isNew, bool removed, string who)
    {
        if ((sameAs is null ? 0 : 1) + (isNew ? 1 : 0) + (removed ? 1 : 0) != 1)
            throw new StangentException("usage", "Say one of --same-as <S-id>, --new or --removed.", ExitCodes.Usage);
        var cmp = Need(root, doc);
        Current(root, doc, cmp);
        var key = $"{doc}@{((string)cmp["file"]!["hash"]!)[..12]}";
        var entry = Entries(cmp).FirstOrDefault(e => (string)e["case"]! == "doubtful" && ((string?)e["file"] == section || (string?)e["section"] == section))
            ?? throw new StangentException("not_doubtful", $"Section {section} isn't doubtful in {doc}'s comparison.", ExitCodes.NotFound,
                [.. Entries(cmp).Where(e => (string)e["case"]! == "doubtful").Select(e => (string?)e["file"] ?? (string)e["section"]!)]);
        var fileSide = (string)entry["side"]! == "file";
        if (removed == fileSide)
            throw new StangentException("usage", fileSide ? $"{section} is in the new file: say --same-as <S-id> or --new." : $"{section} is in the repo: say --removed (it's listed as gone from the file, never deleted), or claim it from a file section with --same-as.", ExitCodes.Usage);
        var settled = cmp["settled"] as JsonObject ?? new JsonObject();
        if (sameAs is not null)
        {
            if (FsdVersions.Ledger(root, doc).Removed("sections").Contains(sameAs))
                throw new StangentException("section_id_removed", $"{sameAs} was removed earlier. A removed id never comes back: say --new.", ExitCodes.Failed);
            if (FsdDocument.Load(root, doc)["sections"]!.AsArray().OfType<JsonObject>().All(s => (string?)s["permanentId"] != sameAs))
                throw new StangentException("not_found", $"The committed FSD of {doc} has no section {sameAs}.", ExitCodes.NotFound);
            var holder = Entries(cmp).FirstOrDefault(e => !ReferenceEquals(e, entry) && (string?)e["section"] == sameAs && (string)e["case"]! is not "doubtful" and not "gone");
            if (holder is not null || settled.Any(kv => kv.Key != section && (string?)kv.Value!["sameAs"] == sameAs))
                throw new StangentException("section_id_taken", $"{sameAs} already matches another section of the new file. Settle that one another way first if this is wrong.", ExitCodes.Failed);
        }
        var to = sameAs is not null ? "same" : isNew ? "new" : "removed";
        var item = new JsonObject { ["state"] = (string?)settled[section]?["state"] ?? "doubtful" };
        StateChanges.Apply(root, item, StateKinds.FsdSectionMatch, $"{key}#{section}", to, StateActor.Person(who), sameAs is not null ? $"the same section as {sameAs}" : isNew ? "a new section" : "gone from the file");
        var said = new JsonObject { ["state"] = to, ["by"] = who };
        if (sameAs is not null) said["sameAs"] = sameAs;
        settled[section] = said;
        cmp["settled"] = settled;
        Build(root, doc, cmp, FsdDocument.CommittedFacts(root, doc), fresh: false);
        Save(root, doc, cmp);
        var result = Result(cmp, "compared");
        result["section"] = section;
        result["state"] = to;
        result["same_as"] = sameAs;
        result["decided_by"] = who;
        return result;
    }

    /// <summary>The comparison was made against the committed FSD as it is now, or it's refused: compare again.</summary>
    static void Current(string root, string doc, JsonObject cmp)
    {
        var facts = FsdDocument.CommittedFacts(root, doc);
        if ((string?)cmp["chaptersTree"] != ChaptersTree(root, doc) || (string?)cmp["factsHash"] != CanonicalJson.Hash(CanonicalJson.Serialize(facts)))
            throw new StangentException("comparison_out_of_date", $"{doc}'s comparison was made against an FSD that isn't the committed one any more. Import the file again (`stangent fsd import <file> --id {doc}`) to compare it with today's.", ExitCodes.Failed);
    }

    // ---- take -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>fsd take &lt;doc&gt; &lt;section&gt;… | --all-updates</c>: the file's heading and text copied into the chapter files, uncommitted, for
    /// each section the comparison offers (changed only by the file, or new in it). A section the repo changed is never taken: its
    /// text must read in the working tree as HEAD has it, and HEAD's must be the last file's. A new section goes after the one it
    /// follows in the file, which must be in the repo. Each taken section's <c>fileSections</c> entry moves to the file's, and
    /// <c>original</c>, <c>converter</c> and <c>importCommit</c> with it; a commit approves it all (D380). Anything not taken is listed, with why.
    /// </summary>
    public static JsonObject Take(StangentRepo repo, string doc, IReadOnlyList<string> sections, bool allUpdates)
    {
        var root = repo.Root;
        if (sections.Count == 0 && !allUpdates) throw new StangentException("usage", "Name the sections to take (their S- id, or a new section's number in the file), or say --all-updates.", ExitCodes.Usage);
        var cmp = Need(root, doc);
        Current(root, doc, cmp);
        var entries = Entries(cmp);
        var fileById = cmp["fileSections"]!.AsArray().OfType<JsonObject>().ToDictionary(s => (string)s["id"]!, StringComparer.Ordinal);
        var notTaken = new JsonArray();
        void Skip(string name, string why) => notTaken.Add(new JsonObject { ["section"] = name, ["why"] = why });

        var chosen = new List<JsonObject>();
        if (allUpdates) chosen.AddRange(entries.Where(e => (string)e["case"]! == "file-changed"));
        foreach (var name in sections)
        {
            var e = entries.FirstOrDefault(x => (string?)x["section"] == name) ?? entries.FirstOrDefault(x => (string?)x["section"] is null && (string?)x["file"] == name)
                ?? entries.FirstOrDefault(x => (string?)x["file"] == name);
            if (e is null) { Skip(name, "the comparison has no such section"); continue; }
            var why = (string)e["case"]! switch
            {
                "file-changed" or "new" => null,
                "both-changed" => (bool?)e["noBase"] == true
                    ? "the last file's words for it aren't known, so whether the repo changed it can't be told: the repo's text is kept. Copy the file's words by hand from comparison.md if you want them"
                    : "the repo changed it too: the repo's text is kept (D385). Copy the file's words by hand from comparison.md if you want them",
                "repo-changed" => "only the repo changed it: there's nothing in the file to take",
                "same" => "the file and the repo already say the same",
                "doubtful" => $"it's doubtful: settle it first with `stangent fsd resolve {doc} {name} …`",
                "gone" => "the file no longer has it: it's listed, and nothing here deletes it",
                "removed-here" => "the repo took it out; nothing here brings it back",
                _ => "it isn't offered",
            };
            if (why is not null) { Skip(name, why); continue; }
            if (!chosen.Contains(e)) chosen.Add(e);
        }

        var dir = FsdImport.DocDir(root, doc);
        var files = FsdChapters.Files(dir);
        var read = FsdChapters.Read(files);
        if (read.Damaged.Count > 0 || read.Repeated.Count > 0 || read.Unmarked.Count > 0)
            throw new StangentException("fsd_check_first", $"The chapter files of {doc} have headings with no id, a damaged id or one id twice: run `stangent fsd check {doc}` first (nothing was written).", ExitCodes.Usage);
        var head = FsdDocument.Load(root, doc)["sections"]!.AsArray().OfType<JsonObject>().Where(s => s["permanentId"] is not null)
            .ToDictionary(s => (string)s["permanentId"]!, StringComparer.Ordinal);
        var ledgerText = File.Exists(Path.Combine(dir, "ids.json")) ? File.ReadAllText(Path.Combine(dir, "ids.json")) : null;
        var ledger = FsdDocument.LedgerOf(ledgerText);
        var next = ledger.Last;

        var edit = new Edit(files);
        var taken = new List<(JsonObject Entry, JsonObject File, string PermanentId, bool IsNew)>();
        // Where each file section is in the repo, by permanent id: matched ones, and new ones as this run places them.
        var placed = entries.Where(e => (string?)e["section"] is not null && (string?)e["file"] is not null && (string)e["case"]! is not "removed-here" and not "doubtful")
            .ToDictionary(e => (string)e["file"]!, e => (string)e["section"]!, StringComparer.Ordinal);
        var order = cmp["fileSections"]!.AsArray().OfType<JsonObject>().Select(s => (string)s["id"]!).ToList();
        foreach (var e in chosen.OrderBy(e => (string)e["case"]! == "new" ? 1 : 0).ThenBy(e => order.IndexOf((string)e["file"]!)))
        {
            var file = fileById[(string)e["file"]!];
            var name = (string?)e["section"] ?? (string)e["file"]!;
            if ((string)e["case"]! == "file-changed")
            {
                var pid = (string)e["section"]!;
                // Judged on the working tree as it was before this run: an edit made here moves other sections' numbers, not their words.
                var was = read.Sections.FirstOrDefault(x => x.PermanentId == pid);
                var now = edit.Section(pid);
                if (was is null || now is null) { Skip(name, "it isn't in the working tree's chapter files"); continue; }
                if (was.TextHash == (string)file["textHash"]! && Heading(FsdChapters.Json(was)) == Heading(file))
                { Skip(name, "it already reads as the file has it (taken, not committed yet): read it and commit"); continue; }
                if (!head.TryGetValue(pid, out var committed) || was.TextHash != (string)committed["textHash"]! || was.Title != (string)committed["title"]! || was.Id != (string)committed["id"]!)
                { Skip(name, "it was edited since HEAD (an uncommitted change): the edit is kept; commit or undo it, import the file again, then take"); continue; }
                if (e["levelChanged"] is not null) { Skip(name, "the file moved it to another heading level: move it by hand"); continue; }
                if (edit.Update(now, file) is { } bad) { Skip(name, bad); continue; }
                taken.Add((e, file, pid, false));
            }
            else
            {
                var at = order.IndexOf((string)file["id"]!);
                var after = at > 0 ? placed.GetValueOrDefault(order[at - 1]) : null;
                if (after is null || edit.Section(after) is not { } previous)
                { Skip(name, at == 0 ? "it's the file's first section, so where it goes here can't be told: add it by hand" : $"the section before it in the file ({order[at - 1]}) isn't in the repo, so where it goes can't be told: take that one first, or add it by hand"); continue; }
                var pid = string.Format(System.Globalization.CultureInfo.InvariantCulture, ledger.Format, ++next);
                if (edit.Insert(previous, file, pid) is { } bad) { next--; Skip(name, bad); continue; }
                placed[(string)file["id"]!] = pid;
                taken.Add((e, file, pid, true));
            }
        }
        if (taken.Count == 0)
            throw new StangentException("nothing_taken", $"Nothing was taken from {doc}'s comparison; nothing was written.", ExitCodes.Failed, [.. notTaken.Select(n => $"{(string)n!["section"]!}: {(string)n["why"]!}")]);

        // Every other section reads as before; each taken one reads as the file's; no number used twice that wasn't before.
        var after2 = FsdChapters.Read(edit.Files);
        var parents = ParentIds(after2);
        var beforeParents = ParentIds(read);
        var takenIds = taken.Select(t => t.PermanentId).ToHashSet(StringComparer.Ordinal);
        foreach (var s in read.Sections)
        {
            var a = after2.Sections.FirstOrDefault(x => x.PermanentId == s.PermanentId);
            if (a is null || a.Level != s.Level || parents.GetValueOrDefault(a.PermanentId!) != beforeParents.GetValueOrDefault(s.PermanentId!)
                || (!takenIds.Contains(s.PermanentId!) && (a.TextHash != s.TextHash || a.Title != s.Title)))
                throw new StangentException("take_out_of_step", $"Taking these would change {s.PermanentId} ({s.Title}), which wasn't taken, or where it sits: nothing was written. Take fewer at once, or make the change by hand.", ExitCodes.Failed);
        }
        foreach (var t in taken)
        {
            var a = after2.Sections.FirstOrDefault(x => x.PermanentId == t.PermanentId);
            var wantParent = (string?)t.File["parent"] is { } fp ? placed.GetValueOrDefault(fp) : null;
            if (a is null || a.TextHash != (string)t.File["textHash"]! || a.Title != (string)t.File["title"]! || a.Level != (int)t.File["level"]!
                || (t.IsNew && parents.GetValueOrDefault(t.PermanentId) != wantParent))
                throw new StangentException("take_out_of_step", $"{(string?)t.Entry["section"] ?? (string)t.File["id"]!} wouldn't read back as the file has it, or not under the same heading: nothing was written. Make the change by hand.", ExitCodes.Failed);
        }
        // A number used twice is warned, as `fsd check` warns (D384): often a section gone from the file still holds it here.
        var repeated = after2.Sections.Select(s => s.Id).Where(id => Regex.IsMatch(id, @" \(\d+\)$")).Except(read.Sections.Select(s => s.Id))
            .Select(id => Regex.Replace(id, @" \(\d+\)$", "")).Distinct().ToList();

        // Write: the ledger first (new ids), then the chapters, the images, document.json and the trace's numbers.
        edit.CheckWritable(FsdChapters.Dir(dir));
        var issued = new List<string>();
        if (taken.Any(t => t.IsNew))
        {
            var issuer = FsdVersions.Ledger(root, doc);
            foreach (var t in taken.Where(t => t.IsNew))
            {
                var id = issuer.Issue("sections", ledger.Format);
                if (id != t.PermanentId)
                    throw new StangentException("ledger_moved", $"The ledger gave {id} where {t.PermanentId} was planned (another command gave an id meanwhile). Run `stangent fsd check {doc}`, look at the chapters, and take again.", ExitCodes.Failed);
                issued.Add(id);
            }
        }
        edit.Save(FsdChapters.Dir(dir));
        var facts = FsdDocument.Facts(root, doc)!;
        // Only what the file said is recorded: a section never taken from a file keeps no entry rather than a guessed one.
        var fileSections = facts["fileSections"] as JsonArray ?? [];
        var paths = FsdSectionDiff.Paths(cmp["fileSections"]!.AsArray().OfType<JsonObject>().ToList());
        var images = facts["images"]!.AsArray();
        var brought = new JsonArray();
        foreach (var t in taken)
        {
            var record = Record(new JsonObject
            {
                ["permanentId"] = t.PermanentId, ["title"] = (string)t.File["title"]!, ["idKind"] = (string)t.File["idKind"]!, ["id"] = (string)t.File["id"]!,
                ["textHash"] = (string)t.File["textHash"]!,
            }, paths[(string)t.File["id"]!]);
            var at = fileSections.OfType<JsonObject>().ToList().FindIndex(r => (string?)r["section"] == t.PermanentId);
            if (at >= 0) fileSections[at] = record; else fileSections.Add(record);
            foreach (var img in ImageRef().Matches((string)t.File["text"]!).Select(m => m.Groups[1].Value).Distinct())
            {
                var from = Path.Combine(Dir(root, doc), img);
                var to = Path.Combine(dir, img);
                if (!File.Exists(to) && File.Exists(from)) { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to); brought.Add(img); }
                if (images.OfType<JsonObject>().All(i => (string?)i["file"] != img))
                {
                    var o = new JsonObject { ["file"] = img, ["id"] = FsdVersions.Ledger(root, doc).Issue(Figures, FigureFormat), ["section"] = t.PermanentId };
                    if ((string)t.File["idKind"]! == "number") o["sectionNumber"] = (string)t.File["id"]!;
                    if (cmp["images"]!.AsArray().OfType<JsonObject>().FirstOrDefault(i => (string?)i["file"] == img)?["caption"] is { } caption) o["caption"] = caption.DeepClone();
                    images.Add(o);
                }
            }
        }
        facts["fileSections"] = fileSections;
        var archive = cmp["file"]!["archive"]!.AsObject();
        facts["original"] = new JsonObject
        {
            ["archive"] = (string)archive["name"]!, ["path"] = (string)archive["path"]!, ["hash"] = (string)cmp["file"]!["hash"]!, ["fileName"] = (string)cmp["file"]!["fileName"]!,
        };
        facts["converter"] = cmp["file"]!["converter"]!.DeepClone();
        // The new file's problems on the sections taken replace the last file's there; elsewhere the last file's stay. Tables
        // are worked out again from the text, as an import does (D122).
        var takenFile = taken.Select(t => (string)t.File["id"]!).ToHashSet(StringComparer.Ordinal);
        var whole = new JsonObject { ["sections"] = new JsonArray([.. after2.Sections.Select(x => (JsonNode)FsdChapters.Json(x))]) };
        var keptProblems = (facts["problems"]?.AsArray() ?? []).OfType<JsonObject>().Where(p => (string?)p["section"] is not { } on || !takenIds.Contains(on));
        var newProblems = (cmp["fileProblems"]?.AsArray() ?? []).OfType<JsonObject>().Where(p => (string?)p["sectionNumber"] is { } n && takenFile.Contains(n));
        whole["problems"] = new JsonArray([.. keptProblems.Concat(newProblems).Select(p => p.DeepClone())]);
        ImportProblems.Attach(whole);
        facts["problems"] = whole["problems"]!.DeepClone();
        whole["tables"] = facts["tables"]?.DeepClone();
        FsdObjects.AssignTables(root, doc, facts, whole);
        facts["tables"] = whole["tables"]!.DeepClone();
        FsdDocument.Save(root, doc, facts, newImport: true);
        foreach (var (k, v) in facts) if (k is not "sections") whole[k] ??= v?.DeepClone();
        ImportReport.Write(dir, whole);

        // A section's number or heading path moved: trace entries follow it by permanent id (D117, FsdCarry).
        var pairs = read.Sections.Select(s => (Old: s, New: after2.Sections.FirstOrDefault(x => x.PermanentId == s.PermanentId)))
            .Where(p => p.New is not null && p.New.Id != p.Old.Id)
            .Select(p => new FsdCarry.Pair(WithId(p.Old), WithId(p.New!), "modified")).ToList();
        var carried = pairs.Count > 0 ? FsdCarry.Apply(root, doc, pairs, [], after2.Sections.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)) : null;

        var attachments = new JsonArray([.. taken.Where(t => t.Entry["attachmentsChanged"] is not null).Select(t => (JsonNode)new JsonObject
        {
            ["section"] = t.PermanentId, ["changes"] = t.Entry["attachmentsChanged"]!.DeepClone(),
        })]);
        var result = new JsonObject
        {
            ["doc"] = doc,
            ["taken"] = new JsonArray([.. taken.Select(t => (JsonNode)new JsonObject
            {
                ["section"] = t.PermanentId, ["file"] = (string)t.File["id"]!, ["title"] = (string)t.File["title"]!, ["new"] = t.IsNew,
            })]),
            ["notTaken"] = notTaken, ["idsGiven"] = new JsonArray([.. issued.Select(i => (JsonNode)i)]), ["images"] = brought,
            ["attachmentsNotTaken"] = attachments,
            ["changed"] = new JsonArray([.. edit.Changed.Order(StringComparer.Ordinal).Select(n => (JsonNode)$"specs/fsd/{doc}/chapters/{n}"), (JsonNode)$"specs/fsd/{doc}/document.json"]),
            ["next"] = $"Read the change (`git diff specs/fsd/{doc}`), run `stangent fsd check {doc}`, and commit: the commit approves it (D380). Nothing is read by a step until then.",
        };
        if (carried is not null) result["carried"] = carried;
        if (repeated.Count > 0)
        {
            result["numbersRepeated"] = new JsonArray([.. repeated.Select(n => (JsonNode)n)]);
            Console.Error.WriteLine($"warning: two sections are now numbered {string.Join(", ", repeated)} in specs/fsd/{doc}: a section gone from the new file may still hold the number here. Take it out by hand (`stangent fsd check {doc} --removed <id>`) or renumber, before you commit.");
        }
        if (attachments.Count > 0) result["note"] = "Files attached to these sections changed in the new file; take copies text and pictures only, so the attachments stay as the last import brought them (listed in attachmentsNotTaken).";
        return result;
    }

    static JsonObject WithId(ChapterSection s) => FsdChapters.Json(s);

    /// <summary>Each section's parent, by permanent id.</summary>
    static Dictionary<string, string?> ParentIds(ChapterRead read)
    {
        var byId = read.Sections.GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().PermanentId, StringComparer.Ordinal);
        return read.Sections.Where(s => s.PermanentId is not null).GroupBy(s => s.PermanentId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Parent is { } p ? byId.GetValueOrDefault(p) : null, StringComparer.Ordinal);
    }

    /// <summary>The chapter files being edited, in memory: an update replaces one section's heading words and own lines; an insert adds a section's lines after another's.</summary>
    sealed class Edit(List<(string Name, string Text)> files)
    {
        public List<(string Name, string Text)> Files { get; } = [.. files];
        public HashSet<string> Changed { get; } = new(StringComparer.Ordinal);
        ChapterRead read = FsdChapters.Read(files);

        public ChapterSection? Section(string pid) => read.Sections.FirstOrDefault(s => s.PermanentId == pid);

        (int File, string[] Lines, int End) Lines(ChapterSection s)
        {
            var f = Files.FindIndex(x => x.Name == s.File);
            var lines = Files[f].Text.Split('\n');
            var end = read.Sections.Where(x => x.File == s.File && x.FileLine > s.FileLine).Select(x => x.FileLine - 1).DefaultIfEmpty(lines.Length).Min();
            return (f, lines, end);
        }

        static string[] Body(JsonObject file) => ((string)file["body"]!).Split('\n');

        public string? Update(ChapterSection s, JsonObject file)
        {
            var (f, lines, end) = Lines(s);
            var heading = FsdChapters.HeadingWithMarker((string)file["heading"]!, (string)file["idKind"]!, (string)file["id"]!, s.PermanentId!);
            var body = Body(file);
            // The repo's own blank lines after the text stay, so the next heading keeps its place.
            var text = lines[..(s.FileLine - 1)].Append(heading).Concat(Trimmed(body)).Concat(Tail(lines[s.FileLine..end])).Concat(lines[end..]);
            return Put(f, string.Join('\n', text));
        }

        public string? Insert(ChapterSection after, JsonObject file, string pid)
        {
            var (f, lines, end) = Lines(after);
            // After the section's own lines (before any section under it), with a blank line before the new heading.
            var own = lines[after.FileLine..end];
            var keep = own.Length - Tail(own).Count();
            var heading = FsdChapters.HeadingWithMarker((string)file["heading"]!, (string)file["idKind"]!, (string)file["id"]!, pid);
            var added = new List<string> { "", heading };
            added.AddRange(Trimmed(Body(file)));
            var tail = Tail(own).ToList();
            if (tail.Count == 0 && end < lines.Length) tail.Add("");
            var text = lines[..(after.FileLine + keep)].Concat(added).Concat(tail).Concat(lines[end..]);
            return Put(f, string.Join('\n', text));
        }

        /// <summary>A section's own lines with the blank lines at its end left off, a blank one first under the heading.</summary>
        static IEnumerable<string> Trimmed(string[] body)
        {
            var list = body.ToList();
            while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
            while (list.Count > 0 && list[0].Length == 0) list.RemoveAt(0);
            return list.Count == 0 ? [] : list.Prepend("");
        }

        static IEnumerable<string> Tail(string[] own)
        {
            var n = 0;
            while (n < own.Length && own[^(n + 1)].Length == 0) n++;
            return own[(own.Length - n)..];
        }

        string? Put(int f, string text)
        {
            var copy = Files.ToList();
            copy[f] = (copy[f].Name, text);
            var after = FsdChapters.Read(copy);
            if (after.Damaged.Count > 0 || after.Repeated.Count > 0)
                return "its words would read as a damaged or repeated id marker: copy it by hand";
            Files[f] = copy[f];
            read = after;
            Changed.Add(copy[f].Name);
            return null;
        }

        /// <summary>Refuses before anything is written: a chapter file with both line endings can't be written back with only its words changed.</summary>
        public void CheckWritable(string chaptersDir)
        {
            foreach (var name in Changed)
            {
                var before = File.ReadAllText(Path.Combine(chaptersDir, name));
                if (before.Contains("\r\n", StringComparison.Ordinal) && before.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n'))
                    throw new StangentException("mixed_line_endings", $"{name} mixes CRLF and LF line endings: make them one kind and commit, then take (nothing was written).", ExitCodes.Failed);
            }
        }

        public void Save(string chaptersDir)
        {
            foreach (var (name, text) in Files.Where(x => Changed.Contains(x.Name)))
            {
                var path = Path.Combine(chaptersDir, name);
                var before = File.ReadAllText(path);
                Quotes.WriteAtomic(path, before.Contains("\r\n", StringComparison.Ordinal) ? text.Replace("\n", "\r\n") : text);
            }
        }
    }

    // ---- comparison.md -------------------------------------------------------------------------------------------------

    static string Markdown(string root, JsonObject cmp)
    {
        var doc = (string)cmp["doc"]!;
        var entries = Entries(cmp);
        var fileById = cmp["fileSections"]!.AsArray().OfType<JsonObject>().ToDictionary(s => (string)s["id"]!, StringComparer.Ordinal);
        Dictionary<string, JsonObject> repoById;
        try { repoById = FsdDocument.Load(root, doc)["sections"]!.AsArray().OfType<JsonObject>().Where(s => s["permanentId"] is not null).ToDictionary(s => (string)s["permanentId"]!, StringComparer.Ordinal); }
        catch (StangentException) { repoById = []; }
        var sb = new StringBuilder($"# Comparison: {doc}\n\n");
        sb.Append($"The new file `{(string)cmp["file"]!["fileName"]!}` (`{((string)cmp["file"]!["hash"]!)[..12]}`) against the committed FSD and the last imported file `{(string)cmp["base"]!["fileName"]!}` (`{((string)cmp["base"]!["fileHash"]!)[..12]}`, its sections from {(string)cmp["base"]!["from"]!}). ");
        sb.Append("No chapter changed. Only the sections you take with `stangent fsd take` change, and the commit approves them (D380, D385).\n\n");
        if ((string?)cmp["converterChanged"] is { } note) sb.Append($"> **Converter:** {note}\n\n");
        if ((string?)cmp["baseNote"] is { } baseNote) sb.Append($"> **No record of the last file:** {baseNote}\n\n");
        sb.Append("| Case | Sections | What happens |\n|---|---|---|\n");
        var what = new Dictionary<string, string>
        {
            ["file-changed"] = "only the file changed it: offered", ["new"] = "new in the file: offered", ["both-changed"] = "both changed it: the repo's kept, shown side by side",
            ["repo-changed"] = "only the repo changed it: the repo's kept", ["gone"] = "gone from the file: listed, never deleted",
            ["removed-here"] = "the repo took it out: stays out", ["doubtful"] = "a person settles which section it is", ["same"] = "nothing to do",
        };
        foreach (var c in Cases) sb.Append($"| {c} | {cmp["counts"]![c]} | {what[c]} |\n");

        string Quote(string text) => text.Trim().Length == 0 ? "> (no text)\n" : string.Concat(text.Split('\n').Select(l => $"> {l}\n"));
        string FileText(JsonObject e) => fileById.TryGetValue((string?)e["file"] ?? "", out var f) ? (string)f["text"]! : "";
        string RepoText(JsonObject e) => repoById.TryGetValue((string?)e["section"] ?? "", out var r) ? (string)r["text"]! : "";
        string FileHeading(JsonObject e) => fileById.TryGetValue((string?)e["file"] ?? "", out var f) ? $"{((string)f["idKind"]! == "number" ? (string)f["id"]! + " " : "")}{(string)f["title"]!}" : "";
        string RepoHeading(JsonObject e) => repoById.TryGetValue((string?)e["section"] ?? "", out var r) ? $"{((string)r["idKind"]! == "number" ? (string)r["id"]! + " " : "")}{(string)r["title"]!}" : "";

        var doubtful = entries.Where(e => (string)e["case"]! == "doubtful").ToList();
        if (doubtful.Count > 0)
        {
            sb.Append("\n## Doubtful: a person decides\n\nCode found a shared sentence or the same heading, and nothing exact, so it doesn't guess.\n");
            foreach (var e in doubtful)
            {
                var fileSide = (string)e["side"]! == "file";
                sb.Append(fileSide ? $"\n### §{e["file"]} {e["title"]} (in the new file)\n\n" : $"\n### {e["section"]} (§{e["repo"]}) {e["title"]} (in the repo)\n\n");
                if (e["splitInto"] is JsonArray split) sb.Append($"It came apart in the file, into {string.Join(", ", split.Select(t => $"§{(string)t!}"))}: at most one part keeps the id.\n\n");
                foreach (var c in e["candidates"]!.AsArray().OfType<JsonObject>())
                {
                    var where = fileSide ? $"{c["permanentId"]} §{c["from"]}" : $"§{c["to"]}";
                    sb.Append($"- {where} {c["title"]}{((bool)c["alreadyMatched"]! ? " (already matched)" : "")}: {((string?)c["sentence"] is { } sentence ? $"\"{sentence}\"" : "the same heading")}\n");
                }
                if (e["suggestions"] is JsonArray { Count: > 0 } suggestions)
                {
                    sb.Append("\nThe closest sections by shared words, for reading first. It's a ranking, and it decides nothing:\n\n");
                    foreach (var s in suggestions.OfType<JsonObject>())
                        sb.Append($"- {(fileSide ? $"{s["permanentId"]} §{s["from"]}" : $"§{s["to"]}")} {s["title"]} ({s["similarity"]})\n");
                }
                sb.Append(fileSide
                    ? $"\nSettle: `stangent fsd resolve {doc} \"{e["file"]}\" --same-as <S-id>` or `--new`\n"
                    : $"\nSettle: `stangent fsd resolve {doc} {e["section"]} --removed` (gone from the file: listed, never deleted), or claim it from a file section with `--same-as`\n");
            }
        }
        void Block(string @case, string title, bool sideBySide, bool fileOnly)
        {
            var some = entries.Where(e => (string)e["case"]! == @case).ToList();
            if (some.Count == 0) return;
            sb.Append($"\n## {title}\n");
            foreach (var e in some)
            {
                var id = (string?)e["section"] is { } pid ? $"{pid} " : "";
                sb.Append($"\n### {id}{(fileOnly ? "§" + e["file"] : "§" + (e["repo"] ?? e["file"]))} {e["title"]}\n\n");
                if (e["fileMovedIt"] is JsonObject moved) sb.Append($"The file moved it: {(string)moved["from"]!} → {(string)moved["to"]!}. Take changes its words only.\n\n");
                if (e["levelChanged"] is not null) sb.Append("The file put it at another heading level: take won't move it; move it by hand.\n\n");
                if (e["attachmentsChanged"] is JsonArray files) sb.Append($"Its attachments changed in the file ({string.Join(", ", files.OfType<JsonObject>().Select(c => $"{(string)c["fileName"]!} {(string)c["change"]!}"))}); take doesn't bring attachments.\n\n");
                if ((bool?)e["noBase"] == true) sb.Append("The last file's words for it aren't known, so the repo's are kept.\n\n");
                if (sideBySide)
                {
                    sb.Append($"**In the repo ({(@case == "file-changed" ? "as the last file had it" : "kept")}):** {RepoHeading(e)}\n\n{Quote(RepoText(e))}\n");
                    sb.Append($"**In the new file:** {FileHeading(e)}\n\n{Quote(FileText(e))}");
                }
                else if (fileOnly) sb.Append($"**In the new file:** {FileHeading(e)}\n\n{Quote(FileText(e))}");
                if (@case is "file-changed" or "new") sb.Append($"\nTake: `stangent fsd take {doc} {(string?)e["section"] ?? $"\"{e["file"]}\""}`\n");
            }
        }
        Block("file-changed", "Offered: changed only in the file", sideBySide: true, fileOnly: false);
        Block("new", "Offered: new in the file", sideBySide: false, fileOnly: true);
        Block("both-changed", "Changed in both: the repo's text is kept", sideBySide: true, fileOnly: false);
        void List(string @case, string title, string why)
        {
            var some = entries.Where(e => (string)e["case"]! == @case).ToList();
            if (some.Count == 0) return;
            sb.Append($"\n## {title}\n\n{why}\n\n");
            foreach (var e in some) sb.Append($"- {(string?)e["section"] ?? ""} §{(string?)e["repo"] ?? (string?)e["file"]} {e["title"]}\n");
        }
        List("repo-changed", "Changed only in the repo: kept", "The file is behind on these.");
        List("gone", "Gone from the file: listed", "The new file doesn't have these. Nothing here deletes them; take one out by hand, then `stangent fsd check --removed <id>`, if it should go.");
        List("removed-here", "Taken out of the repo, still in the file", "They stay out.");
        var moved = entries.Where(e => e["fileMovedIt"] is JsonObject && (string)e["case"]! == "same").ToList();
        if (moved.Count > 0)
        {
            sb.Append("\n## Moved in the file, the same words\n\nTake doesn't move a section: move it by hand if it should move here too.\n\n");
            foreach (var e in moved) sb.Append($"- {e["section"]} §{e["repo"]} {e["title"]}: {(string)e["fileMovedIt"]!["from"]!} → {(string)e["fileMovedIt"]!["to"]!}\n");
        }
        var attached = entries.Where(e => e["attachmentsChanged"] is JsonArray).ToList();
        if (attached.Count > 0)
        {
            sb.Append("\n## Attachments changed in the file\n\nTake copies text and pictures only: these stay as the last import brought them.\n\n");
            foreach (var e in attached)
                sb.Append($"- {e["section"]} §{e["repo"] ?? e["file"]} {e["title"]}: {string.Join(", ", e["attachmentsChanged"]!.AsArray().OfType<JsonObject>().Select(c => $"{(string)c["fileName"]!} {(string)c["change"]!}"))}\n");
        }
        if (cmp["attachmentsWithNoSection"] is JsonArray whole)
        {
            sb.Append("\n## Files attached to the whole document\n\nThey're in no section's hash, so they're said here. Take doesn't bring them.\n\n");
            foreach (var c in whole.OfType<JsonObject>()) sb.Append($"- {(string)c["fileName"]!}: {(string)c["change"]!}\n");
        }
        return sb.ToString();
    }
}
