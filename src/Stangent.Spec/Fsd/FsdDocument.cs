using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// The one reader of a document (phase fs layer 2, D380, D383): <c>document.json</c> holds only the import's facts
/// (<c>source-document</c> v4), and the sections come from the chapter files <b>as of <c>HEAD</c></b>, read through git, so an
/// uncommitted edit is never read and a commit is the approval. What a step gets has the shape v3's <c>document.json</c> had:
/// the facts with a <c>sections</c> array, each section's <c>attachments</c>, <c>attachmentLines</c> and <c>contentHash</c> worked
/// out from the document's own <c>attachments</c> by permanent id. The facts (<c>document.json</c>), the ledger (<c>ids.json</c>) and the
/// attachment files a step reads are HEAD's too (<see cref="Blob"/>), so an import, a promote or a hand edit counts only once committed. The chapter files read are cached in <c>.stangent/fsd/&lt;doc&gt;/</c> by the
/// git tree hash of <c>specs/fsd/&lt;doc&gt;/chapters</c>, which changes with every commit that touches them and with nothing else.
/// Refused: a document whose sections are still in <c>document.json</c> (<c>fsd migrate</c> first), a folder that isn't a git
/// checkout, chapters not committed yet, and chapters where one id sits on two headings or names an id the ledger never gave
/// (<c>invalid_fsd</c>, D389 call 1). A heading committed with no marker reads with no id and a warning naming <c>fsd check</c>.
/// </summary>
public static class FsdDocument
{
    public const int SchemaVersion = 4;
    const string Scope = "sections";

    /// <summary>The files read for a tree hash, in this process: a tree hash names its content, so it never goes stale.</summary>
    static readonly ConcurrentDictionary<string, ChapterRead> Reads = new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, bool> Warned = new(StringComparer.Ordinal);

    public static string JsonPath(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "document.json");

    static string Rel(string doc) => $"specs/fsd/{doc}/chapters";

    /// <summary><c>document.json</c> as it is on disk: the import's facts. Null when there's none.</summary>
    public static JsonObject? Facts(string root, string doc) =>
        File.Exists(JsonPath(root, doc)) ? JsonNode.Parse(File.ReadAllText(JsonPath(root, doc)))!.AsObject() : null;

    /// <summary>A document imported before phase fs layer 2 still holds its sections in <c>document.json</c>.</summary>
    public static bool Migrated(JsonObject facts) => facts["sections"] is null;

    internal static StangentException NotMigrated(string doc) => new("fsd_not_migrated",
        $"{doc} was imported before the FSD became chapter files: its sections are still in document.json. Run `stangent fsd migrate {doc}` and commit specs/fsd/{doc}/ (D383).", ExitCodes.Usage);

    /// <summary>The document as every step reads it: HEAD's facts, and the sections of HEAD's chapters.</summary>
    public static JsonObject Load(string root, string doc)
    {
        var working = Facts(root, doc) ?? throw new StangentException("not_found", $"No imported document {doc}.", ExitCodes.NotFound);
        if (!Migrated(working)) throw NotMigrated(doc);
        var facts = CommittedFacts(root, doc);
        if (!Migrated(facts)) throw NotMigrated(doc);
        var read = Committed(root, doc);
        facts["sections"] = Sections(facts, read);
        return facts;
    }

    /// <summary><c>document.json</c> as HEAD holds it: the import's facts a commit approved.</summary>
    public static JsonObject CommittedFacts(string root, string doc)
    {
        Git(root);
        return Blob(root, $"specs/fsd/{doc}/document.json") is { } text ? JsonNode.Parse(text)!.AsObject()
            : throw new StangentException("fsd_not_committed",
                $"specs/fsd/{doc}/document.json isn't committed, and steps read only the committed FSD (D380). Read the import and commit specs/fsd/{doc}/: the commit approves it.", ExitCodes.Usage);
    }

    /// <summary>A file as HEAD holds it, by its path from the stangent root; null when HEAD has none (or this isn't git).</summary>
    public static string? Blob(string root, string rel) =>
        global::Stangent.Graph.Git.Run(root, $"cat-file blob \"HEAD:./{rel.Replace('\\', '/')}\"");

    internal static void Git(string root)
    {
        if (global::Stangent.Graph.Git.Run(root, "rev-parse --git-dir") is null)
            throw new StangentException("not_git", $"The FSD is read from its committed chapter files (D380, D383), and {root} isn't a git checkout.", ExitCodes.Usage);
    }

    /// <summary>For tests: forget the reads held in this process, as a new command would start.</summary>
    internal static void Forget() => Reads.Clear();

    /// <summary>The committed chapters' whole text, markers off: what <c>document.md</c> was.</summary>
    public static string Markdown(string root, string doc) => Committed(root, doc).Markdown;

    /// <summary>Every document that can be read, in id order. One that can't (not migrated, not committed, or refused) is left
    /// out with a warning that says why, so one document waiting for its commit doesn't stop the rest.</summary>
    public static IEnumerable<JsonObject> All(string root)
    {
        var dir = Path.Combine(root, "specs", "fsd");
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(d, "document.json"))) continue;
            JsonObject? document = null;
            try { document = Load(root, Path.GetFileName(d)); }
            catch (StangentException e) when (e.Code is "fsd_not_migrated" or "fsd_not_committed" or "not_git" or "invalid_fsd")
            {
                Warn($"{Path.GetFileName(d)}:{e.Code}", $"FSD {Path.GetFileName(d)} is left out: {e.Message}");
            }
            if (document is not null) yield return document;
        }
    }

    /// <summary>The chapter files as <c>HEAD</c> holds them, read once per tree hash and checked (D389 call 1).</summary>
    static ChapterRead Committed(string root, string doc)
    {
        var (tree, read) = Head(root, doc) ?? throw new StangentException("fsd_not_committed",
                $"{Rel(doc)} isn't committed, and steps read only the committed FSD (D380). Read the chapter files and commit them: the commit approves them.", ExitCodes.Usage);
        Check(root, doc, tree, read);
        return read;
    }

    /// <summary>The chapter files as <c>HEAD</c> holds them, not checked: what <c>fsd check</c> compares the working tree with
    /// (phase fs layer 3), and its raw files (name, text). Null when <c>HEAD</c> has no chapters for the document.</summary>
    public static (ChapterRead Read, List<(string Name, string Text)> Files)? CommittedChapters(string root, string doc)
    {
        Git(root);
        var tree = global::Stangent.Graph.Git.Run(root, $"rev-parse --verify --quiet \"HEAD:./{Rel(doc)}\"")?.Trim();
        if (string.IsNullOrEmpty(tree)) return null;
        var files = Files(root, doc, tree);
        return (Reads.GetOrAdd(tree, _ => FsdChapters.Read(files)), files);
    }

    static (string Tree, ChapterRead Read)? Head(string root, string doc)
    {
        Git(root);
        var tree = global::Stangent.Graph.Git.Run(root, $"rev-parse --verify --quiet \"HEAD:./{Rel(doc)}\"")?.Trim();
        if (string.IsNullOrEmpty(tree)) return null;
        return (tree, Reads.GetOrAdd(tree, t => FsdChapters.Read(Files(root, doc, t))));
    }

    /// <summary>A section ledger (<c>ids.json</c>) as text, read as the ledger keeps it: ids 1..last in the scope's
    /// format (<see cref="Given"/>), and those marked removed. Empty for no text, or no sections scope.</summary>
    public sealed record SectionLedger(int Last, string Format, IReadOnlySet<string> Given, IReadOnlySet<string> Removed);

    public static SectionLedger LedgerOf(string? text)
    {
        var ledger = text is null ? null : JsonNode.Parse(text)?["scopes"]?[Scope] as JsonObject;
        var last = (int?)ledger?["last"] ?? 0;
        var format = (string?)ledger?["format"] ?? "S-{0:D4}";
        var given = Enumerable.Range(1, last).Select(n => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, n)).ToHashSet(StringComparer.Ordinal);
        var removed = (ledger?["removed"]?.AsArray() ?? []).Select(r => (string?)r).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return new SectionLedger(last, format, given, removed);
    }

    /// <summary>The <c>.md</c> files of a chapters tree, in document order, from the cache or from git.</summary>
    static List<(string Name, string Text)> Files(string root, string doc, string tree)
    {
        var cache = Path.Combine(root, ".stangent", "fsd", doc, "chapters-cache.json");
        if (File.Exists(cache))
            try
            {
                if (JsonNode.Parse(File.ReadAllText(cache)) is JsonObject c && (string?)c["tree"] == tree && c["files"] is JsonArray held
                    && held.All(f => f is JsonObject o && o["name"]?.GetValueKind() == System.Text.Json.JsonValueKind.String && o["text"]?.GetValueKind() == System.Text.Json.JsonValueKind.String))
                    return [.. held.OfType<JsonObject>().Select(f => ((string)f["name"]!, (string)f["text"]!))];
            }
            catch (System.Text.Json.JsonException) { /* a broken cache is read again from git */ }
        var names = (global::Stangent.Graph.Git.Run(root, $"ls-tree -z --name-only {tree}") ?? throw new StangentException("git_failed", $"git couldn't list {Rel(doc)} at HEAD.", ExitCodes.Failed))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        var files = FsdChapters.Order(names).Select(n => (n, (global::Stangent.Graph.Git.Run(root, $"cat-file blob \"{tree}:{n}\"")
            ?? throw new StangentException("git_failed", $"git couldn't read {Rel(doc)}/{n} at HEAD.", ExitCodes.Failed)).Replace("\r\n", "\n"))).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        Quotes.WriteAtomic(cache, CanonicalJson.Serialize(new JsonObject
        {
            ["tree"] = tree, ["files"] = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject { ["name"] = f.n, ["text"] = f.Item2 })]),
        }));
        return files;
    }

    /// <summary>D389 call 1: an id on two headings, or one the ledger never gave, could put a ruling on the wrong section, so the
    /// document is refused; a heading with no marker reads with no id, and a warning says to run <c>fsd check</c>.</summary>
    static void Check(string root, string doc, string tree, ChapterRead read)
    {
        if (read.Repeated.Count > 0)
            throw new StangentException("invalid_fsd", $"In the committed {Rel(doc)}, {string.Join(", ", read.Repeated)} sits on more than one heading. Each id names one section: put the right id back on each heading and commit.", ExitCodes.Failed, [.. read.Repeated]);
        // A marker that doesn't read as one hides which section its heading is, and so its rulings and attachments (D393).
        if (read.Damaged.Count > 0)
            throw new StangentException("invalid_fsd", $"In the committed {Rel(doc)}, {read.Damaged.Count} line(s) hold a marker that doesn't read as one ({string.Join(", ", read.Damaged.Take(3).Select(d => $"{d.File} line {d.FileLine}"))}): which section it names can't be told. Write it as `<!-- S-0012 -->` at the end of its heading, run `stangent fsd check {doc}` and commit.", ExitCodes.Failed,
                [.. read.Damaged.Select(d => $"{d.File}:{d.FileLine}")]);
        // The ledger as committed, read as PermanentIds keeps it: ids 1..last in the scope's format, and those removed.
        var (given, removed) = LedgerOf(Blob(root, $"specs/fsd/{doc}/ids.json")) is var l ? (l.Given, l.Removed) : default;
        var unknown = read.Sections.Select(s => s.PermanentId).OfType<string>().Where(id => !given.Contains(id)).Distinct().ToList();
        var reused = read.Sections.Select(s => s.PermanentId).OfType<string>().Where(removed.Contains).Distinct().ToList();
        if (reused.Count > 0)
            throw new StangentException("invalid_fsd", $"The committed {Rel(doc)} names {string.Join(", ", reused)}, which specs/fsd/{doc}/ids.json marks removed. A removed id never comes back (rule 11): remove the marker and run `stangent fsd check {doc}`, which gives the heading a new id.", ExitCodes.Failed, [.. reused]);
        if (unknown.Count > 0 && given.Count == 0)
            throw new StangentException("invalid_fsd", $"specs/fsd/{doc}/ids.json holds no section ids, and the committed {Rel(doc)} names {unknown.Count}: the ledger was deleted or emptied. Restore it from git (`git checkout HEAD -- specs/fsd/{doc}/ids.json`, or from an older commit): ids are never given twice (rule 11).", ExitCodes.Failed, [.. unknown]);
        if (unknown.Count > 0)
            throw new StangentException("invalid_fsd", $"The committed {Rel(doc)} names {string.Join(", ", unknown)}, which specs/fsd/{doc}/ids.json never gave. An id is never typed: remove the marker and run `stangent fsd check {doc}`, which gives the heading its id.", ExitCodes.Failed, [.. unknown]);
        if (read.Unmarked.Count > 0)
            Warn($"{doc}:{tree}:unmarked", $"{read.Unmarked.Count} heading(s) in the committed {Rel(doc)} have no id ({string.Join(", ", read.Unmarked.Take(3).Select(s => $"\"{s.Title}\" in {s.File}"))}): they read with none, and nothing keyed on an id sees them. Run `stangent fsd check {doc}` and commit.");
    }

    /// <summary>The sections as v3's <c>document.json</c> held them, with their attachments from the facts.</summary>
    static JsonArray Sections(JsonObject facts, ChapterRead read)
    {
        var bySection = (facts["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Where(a => a["section"] is not null && a["id"] is not null)
            .ToLookup(a => (string)a["section"]!, StringComparer.Ordinal);
        return new JsonArray([.. read.Sections.Select(s =>
        {
            var o = FsdChapters.Json(s);
            FsdAttachments.Hold(o, s.PermanentId is null ? [] : [.. bySection[s.PermanentId]]);
            return (JsonNode)o;
        })]);
    }

    /// <summary>Whether the working tree's chapters, facts, ledger or attachment files differ from <c>HEAD</c>'s: the files that do, or empty. Null outside git.</summary>
    public static IReadOnlyList<string>? Uncommitted(string root, string doc) =>
        global::Stangent.Graph.Git.Run(root, $"status --porcelain --untracked-files=all -- \"{Rel(doc)}\" \"specs/fsd/{doc}/document.json\" \"specs/fsd/{doc}/ids.json\" \"specs/fsd/{doc}/attachments\"") is not { } status ? null
            : [.. status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Length > 3 ? l[3..].Trim() : l.Trim())];

    /// <summary>For a command that shows the document: says on stderr, and in its result, that an edit isn't committed and so isn't read.</summary>
    public static void Note(string root, string doc, JsonObject? result)
    {
        if (Uncommitted(root, doc) is not { Count: > 0 } files) return;
        var warning = $"specs/fsd/{doc} has uncommitted changes ({(files.Count == 1 ? files[0] : $"{files.Count} files")}). Steps read only the committed FSD (D380): what's shown here is HEAD's, and the edit counts once it's committed: run `stangent fsd check {doc}`, read its list, then commit.";
        Console.Error.WriteLine("warning: " + warning);
        if (result is null) return;
        result["uncommitted"] = true;
        result["uncommittedFiles"] = new JsonArray([.. files.Select(f => (JsonNode)f)]);
    }

    static void Warn(string key, string text)
    {
        if (Warned.TryAdd(key, true)) Console.Error.WriteLine("warning: " + text);
    }

    /// <summary>What <c>document.json</c> holds (v4): the record without its sections or its Markdown's name.</summary>
    public static JsonObject Stripped(JsonObject document, string? importCommit = null)
    {
        var facts = document.DeepClone().AsObject();
        facts.Remove("sections");
        facts.Remove("markdown");
        facts["schemaVersion"] = SchemaVersion;
        if (importCommit is { Length: > 0 }) facts["importCommit"] = importCommit;
        return facts;
    }

    public static void Validate(JsonObject document)
    {
        var errors = SchemaRegistry.Embedded.Validate(FsdImport.Schema, Stripped(document).ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_document", "The document broke source-document v4 (a stangent bug).", ExitCodes.Failed, errors);
    }

    /// <summary>Writes <c>document.json</c> from a whole record: only the facts go in. <paramref name="newImport"/> records the
    /// commit the import was made on (<c>HEAD</c> now; the commit that approves its chapters comes after it).</summary>
    public static void Save(string root, string doc, JsonObject document, bool newImport = false)
    {
        var head = newImport ? global::Stangent.Graph.Git.Run(root, "rev-parse --verify --quiet HEAD")?.Trim() : null;
        var facts = Stripped(document, head);
        if (newImport && string.IsNullOrEmpty(head)) facts.Remove("importCommit");
        Validate(facts);
        Directory.CreateDirectory(FsdImport.DocDir(root, doc));
        Quotes.WriteAtomic(JsonPath(root, doc), CanonicalJson.Serialize(facts));
    }
}
