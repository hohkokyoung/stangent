using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>A section as read from the chapter files (phase fs layer 1, D377, D382, D384). <c>Number</c> is the one written on
/// the heading, or none; <c>Id</c> and <c>IdKind</c> are what <c>document.json</c> calls the section (its number, or its heading
/// path); <c>PermanentId</c> is the heading's marker, or none for a heading typed without one; <c>Line</c> counts in the whole
/// document, <c>FileLine</c> in its chapter file.</summary>
public sealed record ChapterSection(
    string Id, string IdKind, string? Number, string Title, int Level, string? Parent, string Text, string TextHash,
    string? PermanentId, int Line, string File, int FileLine);

/// <summary>What the chapter files say: the whole document as one Markdown text (no markers, as <c>document.md</c> was), its
/// sections, the headings typed with no marker, and the ids that sit on more than one heading.</summary>
public sealed record ChapterRead(string Markdown, IReadOnlyList<ChapterSection> Sections, IReadOnlyList<ChapterSection> Unmarked, IReadOnlyList<string> Repeated);

/// <summary>
/// The FSD as the PM edits it (phase fs layer 1, D377, D382, D384): <c>specs/fsd/&lt;doc&gt;/chapters/</c>, one Markdown file per
/// top-level chapter, cut at its sub-headings when over <c>fsd.exportMaxLines</c>, named with its section's permanent id
/// (<c>01-02-s0003-series-details.md</c>). Text above the first heading is <c>00-front-matter.md</c>. Every heading ends with
/// its permanent id, hidden: <c>### 3.1 Series page &lt;!-- S-0012 --&gt;</c>. The files hold the document's lines exactly, so
/// joining them with a line break and taking the markers off gives back the converter's Markdown byte for byte; <see cref="Write"/>
/// checks that before it writes anything. Two things are added only so the text reads back the same: a line that isn't a
/// heading but looks like one gets a backslash (<c>\# of retries</c>), and a heading whose words don't show the number the
/// import gave it says so in its marker: <c>&lt;!-- S-0012 no-number --&gt;</c> where the words start with a number the import
/// didn't take ("2023 Roadmap" isn't section 2023), <c>&lt;!-- S-0012 number=7 --&gt;</c> where the import numbered a heading
/// that shows none (the attached-files section). No converter writes code fences, so none are read: a line is a heading
/// exactly when the converter's was (D391).
/// </summary>
public static partial class FsdChapters
{
    public const string FrontMatter = "00-front-matter.md";
    const int MaxSlug = 60;
    const string NoNumber = "no-number";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();

    /// <summary>A heading line: one or more <c>#</c>, then a space and its words (or nothing).</summary>
    [GeneratedRegex(@"^(#+)(?:[ \t]+(.*))?$")]
    private static partial Regex HeadingLine();

    /// <summary>The marker at the end of a heading. The space before it is the one the writer adds.</summary>
    [GeneratedRegex(@" ?<!--[ \t]*(S-\d+)(?:[ \t]+(no-number|number=(\d+(?:\.\d+)*)))?[ \t]*-->[ \t]*$")]
    private static partial Regex Marker();

    /// <summary>A line made not to read as a heading: one more backslash than it had (<c>\# x</c>, or <c>\\# x</c> for a line that was <c>\# x</c>).</summary>
    [GeneratedRegex(@"^\\(\\*#+(?:[ \t].*)?)$")]
    private static partial Regex Escaped();

    /// <summary>A line that reads as a heading, or as one made not to.</summary>
    [GeneratedRegex(@"^\\*#+(?:[ \t].*)?$")]
    private static partial Regex HeadingLike();

    [GeneratedRegex(@"^((?:\d+-)+)")]
    private static partial Regex Prefix();

    public static string Dir(string docDir) => Path.Combine(docDir, "chapters");

    public static bool Has(string docDir) => Directory.Exists(Dir(docDir));

    /// <summary>The whole document as one Markdown text, markers off: from the chapters, or from <c>document.md</c> for a
    /// document not migrated yet (<c>stangent fsd migrate</c>). Null when there is neither.</summary>
    public static string? Markdown(string docDir)
    {
        if (Has(docDir)) return Read(Files(docDir)).Markdown;
        var md = Path.Combine(docDir, "document.md");
        return File.Exists(md) ? File.ReadAllText(md) : null;
    }

    /// <summary>The chapter files of a folder, in document order: by the numbers their names start with, then by name.</summary>
    public static List<(string Name, string Text)> Files(string docDir) =>
        [.. Order(Directory.EnumerateFiles(Dir(docDir), "*.md").Select(Path.GetFileName).OfType<string>())
            .Select(n => (n, File.ReadAllText(Path.Combine(Dir(docDir), n)).Replace("\r\n", "\n")))];

    public static IEnumerable<string> Order(IEnumerable<string> names) => names
        .Select(n => (Name: n, Key: Prefix().Match(n) is { Success: true } m ? m.Groups[1].Value.TrimEnd('-').Split('-').Select(int.Parse).ToList() : [int.MaxValue]))
        .OrderBy(x => x.Key, Comparer<List<int>>.Create(CompareParts)).ThenBy(x => x.Name, StringComparer.Ordinal).Select(x => x.Name);

    static int CompareParts(List<int> a, List<int> b)
    {
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Count.CompareTo(b.Count);
    }

    sealed class Node(JsonObject section, int start)
    {
        public JsonObject Section { get; } = section;
        public int Start { get; } = start;
        public int OwnEnd { get; set; }
        public int End { get; set; }
        public List<Node> Children { get; } = [];
    }

    /// <summary>
    /// The chapter files for a document: <paramref name="markdown"/> as the converter wrote it, and its sections as
    /// <c>document.json</c> holds them (each with its <c>line</c>, <c>level</c>, <c>title</c>, <c>id</c>, <c>idKind</c> and
    /// <c>permanentId</c>). A chapter over <paramref name="maxLines"/> is cut at its next heading level, and so on down; one
    /// section over the limit stays whole, because there's no heading to cut at, and is listed in <c>OverLimit</c>.
    /// </summary>
    public static (List<(string Name, string Body)> Files, List<string> OverLimit) Cut(string markdown, IReadOnlyList<JsonObject> sections, int maxLines)
    {
        if (maxLines < 1) throw new StangentException("usage", "fsd.exportMaxLines must be at least 1.", ExitCodes.Usage);
        if (sections.Count == 0) throw new StangentException("invalid_document", "A document with no sections has no chapters (a stangent bug).", ExitCodes.Failed);
        var lines = markdown.Split('\n');
        var marked = Mark(lines, sections);
        var tops = Tree(sections, lines);
        var files = new List<(string, string)>();
        var over = new List<string>();
        if (tops[0].Start > 0) files.Add((FrontMatter, string.Join('\n', marked[..tops[0].Start])));
        for (var i = 0; i < tops.Count; i++) Emit(tops[i], $"{i + 1:D2}", marked, maxLines, files, over);
        return (files, over);
    }

    /// <summary>Throws as <see cref="Write"/> would, writing nothing: for a step to call before it changes anything else.</summary>
    public static void Verify(string markdown, IReadOnlyList<JsonObject> sections, int maxLines) => Check(Cut(markdown, sections, maxLines).Files, markdown, sections);

    /// <summary>Writes the chapters into <c>chapters/</c>, after checking they read back as <paramref name="markdown"/> and
    /// its sections; any other <c>.md</c> file there is removed. Returns the names written.</summary>
    public static (List<string> Files, List<string> OverLimit) Write(string docDir, string markdown, IReadOnlyList<JsonObject> sections, int maxLines)
    {
        var (files, over) = Cut(markdown, sections, maxLines);
        Check(files, markdown, sections);
        var dir = Dir(docDir);
        Directory.CreateDirectory(dir);
        var names = files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var old in Directory.EnumerateFiles(dir, "*.md").Where(f => !names.Contains(Path.GetFileName(f)))) File.Delete(old);
        foreach (var (name, body) in files)
        {
            var target = Path.Combine(dir, name);
            if (!File.Exists(target) || File.ReadAllText(target) != body) Quotes.WriteAtomic(target, body);
        }
        return ([.. files.Select(f => f.Name)], over);
    }

    /// <summary>The round trip: what the files read back as must be what they were written from, or nothing is written.</summary>
    static void Check(List<(string Name, string Body)> files, string markdown, IReadOnlyList<JsonObject> sections)
    {
        // Read as Files reads them from disk, so a line break the disk would change is caught here.
        var read = Read([.. files.Select(f => (f.Name, f.Body.Replace("\r\n", "\n")))]);
        string? wrong = null;
        if (read.Markdown != markdown) wrong = "the text";
        else if (read.Sections.Count != sections.Count) wrong = $"{read.Sections.Count} sections where there are {sections.Count}";
        else
            for (var i = 0; i < sections.Count && wrong is null; i++)
            {
                var (a, b) = (read.Sections[i], sections[i]);
                if (a.Id != (string)b["id"]! || a.IdKind != (string)b["idKind"]! || a.Title != (string)b["title"]! || a.Level != (int)b["level"]!
                    || a.Parent != (string?)b["parent"] || a.Text != (string)b["text"]! || a.Line != (int)b["line"]! || a.PermanentId != (string?)b["permanentId"])
                    wrong = $"section {(string)b["id"]!}";
            }
        if (wrong is not null)
            throw new StangentException("chapters_out_of_step", $"The chapter files wouldn't read back as the document: {wrong} differs (a stangent bug). Nothing was written.", ExitCodes.Failed);
    }

    /// <summary>Each line as a chapter file holds it: a section's heading gets its marker, and a line that would read as a
    /// heading and isn't one gets a backslash. A line inside a code fence is left as it is.</summary>
    static string[] Mark(string[] lines, IReadOnlyList<JsonObject> sections)
    {
        var heads = sections.ToDictionary(s => (int)s["line"]! - 1);
        var marked = (string[])lines.Clone();
        for (var i = 0; i < lines.Length; i++)
        {
            if (heads.TryGetValue(i, out var s))
            {
                if (!HeadingLine().IsMatch(lines[i]))
                    throw new StangentException("chapters_out_of_step", $"Section {(string)s["id"]!} says its heading is at line {i + 1}, and it isn't one (a stangent bug).", ExitCodes.Failed);
                var id = (string?)s["permanentId"] ?? throw new StangentException("chapters_out_of_step", $"Section {(string)s["id"]!} has no permanent id yet (a stangent bug).", ExitCodes.Failed);
                // The import read no number here, so the reader mustn't take the heading's first word for one; or it gave a
                // number the heading doesn't show (the attached-files section, AttachmentHome), so the marker carries it.
                var typed = FsdSections.Typed(HeadingWords(lines[i])).Number;
                var flag = (string)s["idKind"]! == "path" ? (typed is not null ? " " + NoNumber : "")
                    : typed != (string)s["id"]! ? $" number={(string)s["id"]!}" : "";
                marked[i] = $"{lines[i]} <!-- {id}{flag} -->";
            }
            else if (HeadingLike().IsMatch(lines[i])) marked[i] = "\\" + lines[i];
        }
        return marked;
    }

    static string HeadingWords(string line) => HeadingLine().Match(line).Groups[2].Value;

    /// <summary>
    /// Reads chapter files (name, text) in document order: the whole document with markers and added backslashes off, and its
    /// sections. A heading with no marker has no permanent id and is listed in <c>Unmarked</c>; an id on two headings is listed
    /// in <c>Repeated</c>. Nothing here refuses: what a reader does about those is its call (phase fs layers 2 and 3).
    /// </summary>
    public static ChapterRead Read(IReadOnlyList<(string Name, string Text)> files)
    {
        var all = new List<(string Line, string File, int FileLine)>();
        foreach (var (name, text) in files)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++) all.Add((lines[i], name, i + 1));
        }
        var md = new StringBuilder();
        var headings = new List<FsdHeading>();
        var meta = new List<(string? Id, string File, int FileLine)>();
        for (var i = 0; i < all.Count; i++)
        {
            if (i > 0) md.Append('\n');
            var (line, file, fileLine) = all[i];
            if (HeadingLine().IsMatch(line))
            {
                var m = Marker().Match(line);
                var plain = m.Success ? line[..m.Index] : line;
                var start = md.Length;
                md.Append(plain);
                var words = HeadingWords(plain);
                var level = HeadingLine().Match(plain).Groups[1].Length;
                var (number, title) = !m.Success || !m.Groups[2].Success ? FsdSections.Typed(words)
                    : m.Groups[3].Success ? (m.Groups[3].Value, words) : (null, words);
                // Trusted as written (D384): never typed-only, so the numbering rules that judge a converter's guess don't run.
                headings.Add(new FsdHeading(number, title, level, i + 1, start, md.Length, false, words));
                meta.Add((m.Success ? m.Groups[1].Value : null, file, fileLine));
            }
            else md.Append(Escaped().Match(line) is { Success: true } e ? e.Groups[1].Value : line);
        }
        var markdown = md.ToString();
        var built = FsdSections.Build(markdown, headings, []);
        var sections = built.Select((s, i) => new ChapterSection(s.Id, s.IdKind, headings[i].Number, s.Title, s.Level, s.Parent, s.Text,
            CanonicalJson.Hash(s.Text), meta[i].Id, s.Line, meta[i].File, meta[i].FileLine)).ToList();
        var repeated = sections.Where(s => s.PermanentId is not null).GroupBy(s => s.PermanentId!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).Order(StringComparer.Ordinal).ToList();
        return new ChapterRead(markdown, sections, [.. sections.Where(s => s.PermanentId is null)], repeated);
    }

    /// <summary>The tree of sections by heading level; each node's lines run to the next section's heading.</summary>
    static List<Node> Tree(IReadOnlyList<JsonObject> sections, string[] lines)
    {
        var tops = new List<Node>();
        var path = new List<(int Level, Node Node)>();
        var nodes = new List<Node>();
        foreach (var s in sections)
        {
            var node = new Node(s, (int)s["line"]! - 1);
            nodes.Add(node);
            var level = (int)s["level"]!;
            while (path.Count > 0 && path[^1].Level >= level) path.RemoveAt(path.Count - 1);
            (path.Count > 0 ? path[^1].Node.Children : tops).Add(node);
            path.Add((level, node));
        }
        for (var i = 0; i < nodes.Count; i++) nodes[i].OwnEnd = i + 1 < nodes.Count ? nodes[i + 1].Start : lines.Length;
        void SetEnd(Node n)
        {
            foreach (var c in n.Children) SetEnd(c);
            n.End = n.Children.Count > 0 ? n.Children[^1].End : n.OwnEnd;
        }
        foreach (var t in tops) SetEnd(t);
        return tops;
    }

    static void Emit(Node node, string prefix, string[] lines, int maxLines, List<(string, string)> files, List<string> over)
    {
        var whole = string.Join('\n', lines[node.Start..node.End]);
        if (Count(whole) <= maxLines || node.Children.Count == 0)
        {
            files.Add((Name(prefix, node.Section), whole));
            if (Count(whole) > maxLines) over.Add(Name(prefix, node.Section));
            return;
        }
        // The text before its first sub-heading goes in its own file, heading included, so an empty section keeps its heading.
        var own = string.Join('\n', lines[node.Start..node.OwnEnd]);
        files.Add((Name($"{prefix}-00", node.Section), own));
        if (Count(own) > maxLines) over.Add(Name($"{prefix}-00", node.Section));
        for (var i = 0; i < node.Children.Count; i++) Emit(node.Children[i], $"{prefix}-{i + 1:D2}", lines, maxLines, files, over);
    }

    /// <summary>Lines in a file's text: a final line break ends a line, it doesn't start one.</summary>
    static int Count(string text) => text.TrimEnd('\n').Count(c => c == '\n') + 1;

    /// <summary><c>01-s0001-introduction.md</c>. The permanent id keeps two sections with one title apart.</summary>
    public static string Name(string prefix, JsonObject section)
    {
        var slug = NotSlug().Replace(((string)section["title"]!).ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > MaxSlug) slug = slug[..MaxSlug].TrimEnd('-');
        if (slug.Length == 0) slug = "section";
        return $"{prefix}-{((string?)section["permanentId"] ?? "").Replace("-", "", StringComparison.Ordinal).ToLowerInvariant()}-{slug}.md";
    }

    /// <summary>A section as a <c>document.json</c> section reads it (what the import recorded, before layer 2 reads the chapters).</summary>
    public static JsonObject Json(ChapterSection s)
    {
        var o = new JsonObject
        {
            ["id"] = s.Id, ["idKind"] = s.IdKind, ["title"] = s.Title, ["level"] = s.Level, ["line"] = s.Line, ["text"] = s.Text, ["textHash"] = s.TextHash,
        };
        if (s.Parent is not null) o["parent"] = s.Parent;
        if (s.PermanentId is not null) o["permanentId"] = s.PermanentId;
        return o;
    }
}
