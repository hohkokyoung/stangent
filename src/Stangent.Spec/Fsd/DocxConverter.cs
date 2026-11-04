using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace Stangent.Spec.Fsd;

/// <summary>A heading and the text under it, up to the next heading of any level.</summary>
public sealed record FsdSection(string Id, string IdKind, string Title, int Level, string? Parent, string Text, int Line);

/// <summary>An image kept from the document, named by its content (Phase 2b: no AI reads it).</summary>
public sealed record FsdImage(string Name, byte[] Bytes, string? Caption);

public sealed record ConvertedDocument(string Markdown, IReadOnlyList<FsdSection> Sections, IReadOnlyList<FsdImage> Images, IReadOnlyList<string> Problems, string? Title)
{
    /// <summary>Files held inside the document, as the converter found them.</summary>
    public IReadOnlyList<FileInDocument> Files { get; init; } = [];
    /// <summary>Those files once they've been read (<see cref="EmbeddedFiles"/>).</summary>
    public IReadOnlyList<AttachmentRecord> Attachments { get; init; } = [];
    /// <summary>A workbook's sheets as CSV files, used when it's an attachment.</summary>
    public IReadOnlyList<SheetCsv> Sheets { get; init; } = [];
}

/// <summary>
/// <c>.docx</c> → Markdown, deterministically, in C# with the Open XML SDK (Phase 2b step 1, D13). Keeps headings (with
/// Word's own automatic numbering worked out, or numbers typed into the heading), numbered and bulleted lists, tables,
/// and images with their captions. Inline formatting is dropped on purpose: quotes are matched against this text.
/// </summary>
public static partial class DocxConverter
{
    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyle();

    public static ConvertedDocument Convert(byte[] docx, bool flat = false, bool acceptChanges = false) => Open(docx, main =>
    {
        var ctx = new Context(main, flat);
        ctx.Settle(acceptChanges);
        foreach (var block in main.Document?.Body?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
            ctx.Block(block);
        return ctx.Finish();
    });

    /// <summary>The import's safety step (D121): a file that can't be opened, or isn't settled, stops here. Gives how many tracked changes it holds.</summary>
    public static int CheckSafe(byte[] docx, bool acceptChanges) => Open(docx, main =>
    {
        var found = DocxReview.Count(TrackedRoots(main));
        if (found.Count > 0 && !acceptChanges) throw NotSettled(found);
        return found.Values.Sum();
    });

    /// <summary>A Word file locked with a password is an encrypted package inside an OLE container, not a zip. Protection is never worked around, so this says how to get a copy that can be read.</summary>
    public static void CheckLocked(byte[] docx)
    {
        byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        if (docx.Length < ole.Length || !docx.AsSpan(0, ole.Length).SequenceEqual(ole)) return;
        if (docx.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("EncryptedPackage")) < 0) return;
        throw new Core.StangentException("docx_locked",
            "This Word file is locked with a password, so it can't be read, and stangent never gets round protection. To import it: open it in Word, choose File > Info > Protect Document > Encrypt with Password, clear the password, save a copy and import that copy. If you aren't allowed to, ask the document's owner for an unlocked copy.",
            Core.ExitCodes.Failed);
    }

    static T Open<T>(byte[] docx, Func<MainDocumentPart, T> read)
    {
        try
        {
            using var stream = new MemoryStream(docx);
            using var doc = WordprocessingDocument.Open(stream, false);
            return read(doc.MainDocumentPart ?? throw new Core.StangentException("invalid_docx", "The file has no document body."));
        }
        catch (Exception e) when (e is FileFormatException or OpenXmlPackageException or InvalidDataException or System.Xml.XmlException)
        {
            // A password-protected Word file is an OLE container, not a zip, and lands here too.
            throw new Core.StangentException("invalid_docx", "This .docx can't be opened: it may be damaged or password-protected. Supply an unprotected copy.", Core.ExitCodes.Failed, [e.Message]);
        }
    }

    static List<OpenXmlElement> TrackedRoots(MainDocumentPart main) =>
        new OpenXmlElement?[] { main.Document?.Body, main.FootnotesPart?.Footnotes, main.EndnotesPart?.Endnotes }.OfType<OpenXmlElement>().ToList();

    static Core.StangentException NotSettled(Dictionary<string, int> found) =>
        new("docx_tracked_changes", $"This .docx has {found.Values.Sum()} tracked changes ({DocxReview.Summary(found)}), so it isn't settled. Accept or reject them in Word, or use --accept-changes to import the accepted view.");

    sealed class Context(MainDocumentPart main, bool flat)
    {
        /// <summary>How many bold lines a <c>no_structure</c> stop lists, and how long a line can be and still read as a heading: stated settings (D122).</summary>
        const int MaxBoldLines = 20, MaxHeadingWords = 15;

        readonly Dictionary<string, Style> styles = main.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Where(s => s.StyleId?.Value is not null).GroupBy(s => s.StyleId!.Value!).ToDictionary(g => g.Key, g => g.First()) ?? [];
        readonly Numbering? numbering = main.NumberingDefinitionsPart?.Numbering;
        readonly Dictionary<int, int[]> counters = [];
        readonly MarkdownText md = new();
        readonly List<FsdHeading> headings = [];
        readonly HashSet<int> instancesSeen = [];
        readonly List<FsdImage> images = [];
        readonly List<string> problems = [];
        string? title;
        string? pendingImage;
        // What the file's own words say document.md should hold, per heading (-1: before the first): the completeness check (D116).
        readonly Dictionary<int, Dictionary<string, int>> expected = [];
        // What's listed against a section, in the order found; a block's section is known only once it's been read.
        readonly List<(int Section, string Kind, string Unit)> marks = [];
        List<(string Kind, int Count, string Unit)> pendingMarks = [];
        readonly List<string> pendingAnchors = [];
        readonly Dictionary<string, int> commentSection = [];
        readonly List<Note> notes = [];
        int footnotes, endnotes;
        readonly Lazy<string?> defaultParagraphStyle = new(() => main.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .FirstOrDefault(s => s.Default?.Value == true && s.Type?.Value == StyleValues.Paragraph)?.StyleId?.Value);

        sealed record Note(string Label, OpenXmlElement? Content);

        /// <summary>Tracked changes stop the import unless the accepted view is asked for (D116). Comments are always read as accepted: they're listed, not imported.</summary>
        public void Settle(bool accept)
        {
            var roots = TrackedRoots(main);
            var found = DocxReview.Count(roots);
            if (found.Count > 0)
            {
                var total = found.Values.Sum();
                var summary = DocxReview.Summary(found);
                if (!accept) throw NotSettled(found);
                foreach (var root in roots) DocxReview.Accept(root);
                problems.Add($"--accept-changes: {total} tracked changes accepted ({summary}); this is the accepted view, not an agreed document");
            }
            if (main.WordprocessingCommentsPart?.Comments is { } comments) DocxReview.Accept(comments);
        }

        public void Block(OpenXmlElement block)
        {
            switch (block)
            {
                case SdtBlock sdt:
                    Placeholder(sdt);
                    foreach (var child in sdt.SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>()) Block(child);
                    break;
                case CustomXmlBlock:
                    foreach (var child in block.ChildElements.Where(c => c is not CustomXmlProperties)) Block(child);
                    break;
                // With no choice beside it, a fallback's blocks are the document's.
                case { LocalName: "AlternateContent" } when !block.ChildElements.Any(c => c.LocalName == "Choice"):
                    foreach (var child in block.ChildElements.Where(c => c.LocalName == "Fallback").SelectMany(f => f.ChildElements)) Block(child);
                    break;
                default: Leaf(block); break;
            }
        }

        /// <summary>One block: rules first, then the words left in it are what the render must hold. A kind of block with no case here renders nothing, so the check catches it.</summary>
        void Leaf(OpenXmlElement block)
        {
            Prepare(block);
            var words = DocxReview.Words(block);
            switch (block)
            {
                case Paragraph p:
                    Paragraph(p);
                    SmartArt(p);
                    break;
                case Table t:
                    Table(t);
                    words = [.. words.Select(w => w.Replace("|", "\\|"))];
                    break;
            }
            Expect(words);
            Settled();
        }

        void Expect(IEnumerable<string> words)
        {
            var section = headings.Count - 1;
            if (!expected.TryGetValue(section, out var counts)) expected[section] = counts = new(StringComparer.Ordinal);
            foreach (var w in words) counts[w] = counts.GetValueOrDefault(w) + 1;
        }

        void Expect(string added) => Expect(DocxReview.Words(added));

        /// <summary>The block just read belongs to the section now open.</summary>
        void Settled()
        {
            var section = headings.Count - 1;
            foreach (var (kind, count, unit) in pendingMarks) Record(section, kind, count, unit);
            pendingMarks.Clear();
            foreach (var id in pendingEmbeds) embeds.Add((section, id));
            pendingEmbeds.Clear();
            foreach (var id in pendingAnchors) commentSection.TryAdd(id, section);
            pendingAnchors.Clear();
        }

        readonly Dictionary<(int, string), int> markCounts = [];
        readonly List<string> pendingEmbeds = [];
        readonly List<(int Section, string Id)> embeds = [];

        void Record(int section, string kind, int count, string unit)
        {
            if (markCounts.TryGetValue((section, kind), out var n)) markCounts[(section, kind)] = n + count;
            else { markCounts[(section, kind)] = count; marks.Add((section, kind, unit)); }
        }

        void Mark(string kind, int count = 1, string unit = "")
        {
            if (count > 0) pendingMarks.Add((kind, count, unit));
        }

        /// <summary>A content control still showing its prompt ("Click here to enter text") has no content: the prompt isn't the document's.</summary>
        void Placeholder(SdtElement sdt)
        {
            if (sdt.SdtProperties?.ChildElements.Any(c => c.LocalName == "showingPlcHdr") != true) return;
            Mark("a content control's placeholder text left out", DocxReview.Words(sdt).Length, " words");
            foreach (var run in sdt.Descendants<Run>().ToList()) run.Remove();
        }

        /// <summary>The rules: what's left out on purpose goes from the tree, so the words left are what the render must hold; the rest is listed.</summary>
        void Prepare(OpenXmlElement block)
        {
            foreach (var e in block.Descendants())
                if (e is CommentRangeStart { Id.Value: { } start }) pendingAnchors.Add(start);
                else if (e is CommentReference { Id.Value: { } reference }) pendingAnchors.Add(reference);
            foreach (var sdt in block.Descendants<SdtElement>().ToList()) Placeholder(sdt);
            foreach (var row in block.Descendants<TableRow>().Where(r => Flag(r.TableRowProperties, "hidden") == true).ToList())
            {
                Mark("a hidden table row left out", DocxReview.Words(row).Length, " words");
                row.Remove();
            }
            foreach (var cell in block.Descendants<TableCell>().Where(IsMergedDown).ToList())
            {
                Mark("text Word doesn't show in a cell merged down left out", DocxReview.Words(cell).Length, " words");
                foreach (var run in cell.Descendants<Run>().ToList()) run.Remove();
            }
            foreach (var run in block.Descendants<Run>().ToList())
            {
                if (Applies(run, "vanish"))
                {
                    Mark("hidden text left out", DocxReview.Words(run).Length, " words");
                    run.Remove();
                }
                else if (Applies(run, "strike") || Applies(run, "dstrike"))
                    Mark("struck-through text kept as ordinary text", DocxReview.Words(run).Length, " words");
            }
            foreach (var p in (block is Paragraph own ? new[] { own } : []).Concat(block.Descendants<Paragraph>()))
                if (Flag(p.ParagraphProperties?.ParagraphMarkRunProperties, "vanish") == true && DocxReview.Words(p).Length > 0)
                    Mark("a hidden paragraph mark: its text is visible and kept, as its own paragraph, though Word shows it joined to the next");
            foreach (var math in block.Descendants().Where(e => e.LocalName is "oMathPara" or "oMath" && !e.Ancestors().Any(a => a.LocalName == "oMathPara")).ToList())
            {
                Mark("an equation not read");
                math.Remove();
            }
            if (block.LocalName == "altChunk") Mark("an embedded document (altChunk) not read");
            var fieldOpen = false;
            foreach (var e in Live(block))
                switch (e)
                {
                    case Table _: Mark("a nested table flattened: its words are kept, its rows and columns aren't"); break;
                    case SymbolChar: Mark("a symbol not read"); break;
                    case FieldChar { FieldCharType.Value: var type } when type == FieldCharValues.Begin: fieldOpen = true; break;
                    case FieldCode code when fieldOpen:
                        fieldOpen = false;
                        Field(code.Text);
                        break;
                    case SimpleField { Instruction.Value: { } instruction }: Field(instruction); break;
                    case Drawing.GraphicData { Uri.Value: { } uri } when uri.Contains("chart", StringComparison.OrdinalIgnoreCase): Mark("a chart not read"); break;
                    case Drawing.GraphicData { Uri.Value: { } uri } when uri.Contains("diagram", StringComparison.OrdinalIgnoreCase) && block is not Paragraph _: Mark("SmartArt not read"); break;
                    case Drawing.Blip { Embed: null }: Mark("a linked picture not kept"); break;
                    case { LocalName: "OLEObject" }:
                        // With its bytes in the file it's read (A.7); it comes in by its relationship, with this section. With none, it's only named.
                        if (e.GetAttributes().FirstOrDefault(a => a.LocalName == "id").Value is { } embed && EmbeddedPart(embed) is not null) pendingEmbeds.Add(embed);
                        else Mark($"an embedded file not read ({e.GetAttributes().FirstOrDefault(a => a.LocalName == "ProgID").Value ?? "unknown kind"})");
                        break;
                    case { LocalName: "control" }: Mark("an ActiveX control not read"); break;
                    case { LocalName: "txbxContent" }: Mark("a text box: its text is kept where it's anchored, which may not be where it reads"); break;
                    case { LocalName: "imagedata" }: Mark("an old-style picture not kept"); break;
                }
        }

        /// <summary>Descendants, leaving out what only old readers see. A table found under a block is nested, or sits in a text box.</summary>
        static IEnumerable<OpenXmlElement> Live(OpenXmlElement block) =>
            block.Descendants().Where(e => !e.Ancestors().Any(DocxReview.ForOldReaders));

        OpenXmlPart? EmbeddedPart(string relationship) =>
            main.Parts.FirstOrDefault(p => p.RelationshipId == relationship).OpenXmlPart is { } part and (EmbeddedPackagePart or EmbeddedObjectPart) ? part : null;

        /// <summary>The files in the document, by the order they're met. One nothing in the body points to has no section, and it's still read.</summary>
        List<FileInDocument> Files(Func<int, string> sectionId)
        {
            var files = new List<FileInDocument>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(OpenXmlPart part, string? section)
            {
                using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                files.Add(new FileInDocument(Path.GetFileName(part.Uri.ToString()), bytes.ToArray(), section, part.Uri.ToString()));
            }
            foreach (var (section, id) in embeds)
                if (seen.Add(id)) Add(EmbeddedPart(id)!, section >= 0 || flat ? sectionId(section) : null);
            foreach (var part in main.Parts.Where(p => !seen.Contains(p.RelationshipId) && p.OpenXmlPart is EmbeddedPackagePart or EmbeddedObjectPart).OrderBy(p => p.OpenXmlPart.Uri.ToString(), StringComparer.Ordinal))
                Add(part.OpenXmlPart, null);
            return files;
        }

        void Field(string instruction)
        {
            var kind = DocxReview.Words(instruction).FirstOrDefault()?.ToUpperInvariant() ?? "";
            if (kind is not ("" or "HYPERLINK" or "PAGE" or "NUMPAGES" or "PAGEREF" or "TOC")) Mark($"a {kind} field: its text is what Word last saved");
        }

        static bool IsMergedDown(TableCell c) => c.TableCellProperties?.VerticalMerge is { } vm && vm.Val?.Value != MergedCellValues.Restart;

        static bool? Flag(OpenXmlElement? props, string name)
        {
            if (props?.ChildElements.FirstOrDefault(c => c.LocalName == name) is not { } e) return null;
            return e.GetAttributes().FirstOrDefault(a => a.LocalName == "val").Value is not ("0" or "false" or "off");
        }

        bool? StyleFlag(string? id, string name)
        {
            for (var depth = 0; id is not null && depth < 32 && styles.TryGetValue(id, out var s); depth++, id = s.BasedOn?.Val?.Value)
                if (Flag(s.StyleRunProperties, name) is { } found) return found;
            return null;
        }

        /// <summary>A toggle on a run: its own value wins; otherwise the character and paragraph styles, which cancel if both set it (Word's rule), then the document's defaults.</summary>
        bool Applies(Run run, string name)
        {
            if (Flag(run.RunProperties, name) is { } own) return own;
            var paragraphStyle = run.Ancestors<Paragraph>().FirstOrDefault()?.ParagraphProperties?.ParagraphStyleId?.Val?.Value
                ?? defaultParagraphStyle.Value;
            var (character, paragraph) = (StyleFlag(run.RunProperties?.RunStyle?.Val?.Value, name), StyleFlag(paragraphStyle, name));
            if (character is null && paragraph is null)
                return Flag(main.StyleDefinitionsPart?.Styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle, name) ?? false;
            return (character ?? false) ^ (paragraph ?? false);
        }

        /// <summary>SmartArt's words live in their own part: kept as a list after the paragraph, one item per node. Listed, because the file's order isn't always the drawing's.</summary>
        void SmartArt(Paragraph p)
        {
            foreach (var data in Live(p).OfType<Drawing.GraphicData>().Where(g => g.Uri?.Value?.Contains("diagram", StringComparison.OrdinalIgnoreCase) == true))
            {
                var dm = data.Descendants().FirstOrDefault(e => e.LocalName == "relIds")?.GetAttributes().FirstOrDefault(a => a.LocalName == "dm").Value;
                if (main.Parts.FirstOrDefault(x => x.RelationshipId == dm).OpenXmlPart is not DiagramDataPart part || part.DataModelRoot is null) { Mark("SmartArt not read"); continue; }
                foreach (var pt in part.DataModelRoot.Descendants().Where(e => e.LocalName == "pt"))
                    if (DocxReview.Words(pt) is { Length: > 0 } node)
                    {
                        var text = string.Join(' ', node);
                        Expect("-");
                        Expect(node);
                        Emit($"- {text}", tight: true);
                    }
                Mark("SmartArt: its words are kept as a list, its shape and order aren't");
            }
        }

        /// <summary>Footnotes and endnotes go at the end of their section, labelled, with the words they're attached to.</summary>
        void FlushNotes(int count)
        {
            var outer = pendingMarks;
            pendingMarks = [];
            foreach (var note in notes.Take(count))
            {
                if (note.Content is not { } content) { Mark($"{note.Label} has no text in the file"); continue; }
                Prepare(content);
                var (words, text) = (DocxReview.Words(content), Text(content).Trim());
                if (words.Length == 0) continue;
                Expect(note.Label);
                Expect(words);
                Emit($"{note.Label} {text}");
                if (content.Descendants<Drawing.Blip>().Any()) Mark("a picture in a footnote or endnote not kept");
            }
            notes.RemoveRange(0, count);
            foreach (var (kind, marked, unit) in pendingMarks) Record(headings.Count - 1, kind, marked, unit);
            pendingMarks = outer;
        }

        void Reference(string kind, OpenXmlElement? content, string before)
        {
            var words = DocxReview.Words(before);
            var number = kind == "Footnote" ? ++footnotes : ++endnotes;
            notes.Add(new Note($"[{kind} {number}, after \"{string.Join(' ', words[Math.Max(0, words.Length - 5)..])}\"]", content));
        }

        void Paragraph(Paragraph p)
        {
            var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var styleName = styleId is not null && styles.TryGetValue(styleId, out var st) ? st.StyleName?.Val?.Value ?? styleId : styleId ?? "";
            var notesBefore = notes.Count;
            var text = Text(p).Trim();
            var imagesHere = Images(p);
            foreach (var img in imagesHere)
            {
                Expect($"![]({img})");
                Emit($"![]({img})");
            }
            if (imagesHere.Count > 0) pendingImage = imagesHere[^1];
            if (text.Length == 0) return;

            if (styleName.Equals("Title", StringComparison.OrdinalIgnoreCase)) { title ??= text; Emit(text); return; }
            if (styleName.Equals("Caption", StringComparison.OrdinalIgnoreCase) && pendingImage is not null)
            {
                var i = images.FindIndex(x => x.Name == pendingImage);
                images[i] = images[i] with { Caption = text };
                pendingImage = null;
                Emit(text);
                return;
            }
            pendingImage = null;

            var level = flat ? 0 : HeadingLevel(p, styleId);
            var (label, listLevel, bullet) = NumberLabel(p, styleId);
            if (level > 0)
            {
                string? number = label is null ? null : label.TrimEnd('.', ')', ' ');
                if (number is not null && !Regex.IsMatch(number, @"^\d+(\.\d+)*$")) number = null;
                var shown = text;
                var typed = false;
                // A number typed into the heading is a candidate only; Finish keeps it when it fits the numbering.
                if (number is null && FsdSections.Typed(text) is { Number: not null } t) { (number, shown) = t; typed = true; }
                // The notes before this heading end the section before it; one on the heading itself is in its own section.
                FlushNotes(notesBefore);
                var start = md.Length;
                Emit($"{new string('#', level)} {(number is null || typed ? "" : number + " ")}{(typed ? text : shown)}");
                headings.Add(new FsdHeading(number, shown, level, md.Line, start, md.Length, typed, text));
                Expect(new string('#', level));
                if (number is not null && !typed) Expect(number);
                return;
            }
            if (label is not null || bullet)
            {
                var indent = new string(' ', 2 * listLevel);
                if (bullet || label is null || !Regex.IsMatch(label, @"^\d+\.$")) Expect("-");
                if (!bullet && label is not null) Expect(label);
                Emit(bullet || label is null ? $"{indent}- {text}" : Regex.IsMatch(label, @"^\d+\.$") ? $"{indent}{label} {text}" : $"{indent}- {label} {text}", tight: true);
                return;
            }
            Emit(text);
        }

        void Table(Table t)
        {
            var rows = new List<List<string>>();
            var above = new Dictionary<int, string>();
            var repeated = 0;
            foreach (var r in DocxReview.Unwrapped<TableRow>(t))
            {
                // A row can start part-way across the grid; blanks keep its cells under the right columns.
                var row = Enumerable.Repeat("", r.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0).ToList();
                foreach (var c in DocxReview.Unwrapped<TableCell>(r))
                {
                    var column = row.Count;
                    string cell;
                    if (IsMergedDown(c))
                    {
                        // A cell merged down rows repeats its value, so a row read alone still says what it says.
                        cell = above.GetValueOrDefault(column, "");
                        Expect(cell);
                        repeated++;
                    }
                    else
                    {
                        // Nested tables are flattened to their text; images keep their place as image links.
                        // A paragraph inside a text box is read with the paragraph holding it.
                        cell = string.Join(" ", c.Descendants<Paragraph>().Where(x => !x.Ancestors<Paragraph>().Any()).Select(x =>
                        {
                            var pictures = Images(x).Select(i => $"![]({i})").ToList();
                            foreach (var picture in pictures) Expect(picture);
                            return string.Join(" ", pictures.Append(Text(x).Trim())).Trim();
                        }).Where(x => x.Length > 0)).Replace("|", "\\|");
                        if (c.TableCellProperties?.VerticalMerge is null) above.Remove(column); else above[column] = cell;
                    }
                    row.Add(cell);
                    row.AddRange(Enumerable.Repeat("", (c.TableCellProperties?.GridSpan?.Val?.Value ?? 1) - 1));
                }
                if (row.Count > 0) rows.Add(row);
            }
            if (repeated > 0) Mark("a cell merged down rows repeats its value in each row", repeated, " cells");
            if (rows.Count == 0) return;
            var width = rows.Max(r => r.Count);
            var sb = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                var cells = rows[i].Concat(Enumerable.Repeat("", width - rows[i].Count));
                sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                if (i == 0) sb.Append('|').Append(string.Concat(Enumerable.Repeat("---|", width))).Append('\n');
            }
            Expect(Enumerable.Repeat("|", rows.Count * (width + 1)).Append("|" + string.Concat(Enumerable.Repeat("---|", width))));
            Emit(sb.ToString().TrimEnd('\n'));
        }

        void Emit(string block, bool tight = false) => md.Emit(block, tight);

        string Text(OpenXmlElement p)
        {
            var sb = new StringBuilder();
            Read(p, sb);
            return Regex.Replace(sb.ToString().Replace(' ', ' '), " {2,}", " ");
        }

        void Read(OpenXmlElement parent, StringBuilder sb)
        {
            foreach (var e in parent.ChildElements)
                switch (e)
                {
                    // Word stores a text box twice (the shape and a fallback for old readers): read it once.
                    case { LocalName: "Fallback" } when DocxReview.ForOldReaders(e): break;
                    case Text t when t.Parent is Run: sb.Append(t.Text); break;
                    case TabChar or PositionalTab: sb.Append(' '); break;
                    case Break or CarriageReturn: sb.Append(' '); break;
                    case NoBreakHyphen: sb.Append('-'); break;
                    case FootnoteReference f:
                        Reference("Footnote", main.FootnotesPart?.Footnotes?.Elements<Footnote>().FirstOrDefault(n => n.Id?.Value == f.Id?.Value), sb.ToString());
                        break;
                    case EndnoteReference n:
                        Reference("Endnote", main.EndnotesPart?.Endnotes?.Elements<Endnote>().FirstOrDefault(x => x.Id?.Value == n.Id?.Value), sb.ToString());
                        break;
                    // A paragraph inside a text box ends a word, like any other paragraph.
                    case Paragraph _:
                        sb.Append(' ');
                        Read(e, sb);
                        sb.Append(' ');
                        break;
                    default: Read(e, sb); break;
                }
        }

        List<string> Images(Paragraph p)
        {
            var names = new List<string>();
            foreach (var blip in p.Descendants<Drawing.Blip>())
            {
                if (blip.Embed?.Value is not { } rel || main.GetPartById(rel) is not ImagePart part) continue;
                using var s = part.GetStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = ms.ToArray();
                var ext = part.ContentType switch { "image/png" => "png", "image/jpeg" => "jpg", "image/gif" => "gif", "image/svg+xml" => "svg", _ => "bin" };
                var name = $"images/{System.Convert.ToHexStringLower(SHA256.HashData(bytes))[..12]}.{ext}";
                if (images.All(i => i.Name != name)) images.Add(new FsdImage(name, bytes, null));
                names.Add(name);
            }
            return names;
        }

        int HeadingLevel(Paragraph p, string? styleId)
        {
            if (p.ParagraphProperties?.OutlineLevel?.Val?.Value is { } own && own < 9) return own + 1;
            for (var id = styleId; id is not null && styles.TryGetValue(id, out var s); id = s.BasedOn?.Val?.Value)
            {
                if (HeadingStyle().Match(s.StyleName?.Val?.Value ?? "") is { Success: true } m) return int.Parse(m.Groups[1].Value);
                if (s.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } lvl && lvl < 9) return lvl + 1;
            }
            return 0;
        }

        /// <summary>The paragraph's list label as Word would show it ("4.2.3", "a)", "•"), its list level, and whether it's a bullet.</summary>
        (string? Label, int Level, bool Bullet) NumberLabel(Paragraph p, string? styleId)
        {
            var numPr = p.ParagraphProperties?.NumberingProperties;
            int? numId = numPr?.NumberingId?.Val?.Value;
            int? ilvl = numPr?.NumberingLevelReference?.Val?.Value;
            for (var id = styleId; numId is null && id is not null && styles.TryGetValue(id, out var s); id = s.BasedOn?.Val?.Value)
            {
                numId = s.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
                ilvl ??= s.StyleParagraphProperties?.NumberingProperties?.NumberingLevelReference?.Val?.Value;
            }
            if (numId is null or 0 || numbering is null) return (null, 0, false);
            var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractId = instance?.AbstractNumId?.Val?.Value;
            var abs = numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
            if (abs is null) return (null, 0, false);
            var levels = abs.Elements<Level>().ToDictionary(l => l.LevelIndex?.Value ?? 0);
            // A heading style linked to a list carries no level; the list's level that names the style does.
            ilvl ??= levels.Values.FirstOrDefault(l => l.ParagraphStyleIdInLevel?.Val?.Value == styleId)?.LevelIndex?.Value ?? 0;
            var level = ilvl.Value;
            if (!levels.TryGetValue(level, out var def)) return (null, level, false);
            var format = def.NumberingFormat?.Val?.Value;
            if (format == NumberFormatValues.Bullet) return (null, level, true);

            int Start(int l)
            {
                var overrideStart = instance!.Elements<LevelOverride>().FirstOrDefault(o => o.LevelIndex?.Value == l)?.StartOverrideNumberingValue?.Val?.Value;
                return overrideStart ?? (levels.TryGetValue(l, out var d) ? d.StartNumberingValue?.Val?.Value ?? 1 : 1);
            }
            // Word keeps one count per list definition: list instances sharing it continue each other's numbering,
            // unless an instance restarts a level (a start override), which takes effect where it's first used.
            var key = abstractId ?? numId.Value;
            if (!counters.TryGetValue(key, out var c))
            {
                c = new int[9];
                for (var l = 0; l < 9; l++) c[l] = Start(l) - 1;
                counters[key] = c;
            }
            if (instancesSeen.Add(numId.Value))
                foreach (var o in instance!.Elements<LevelOverride>().Where(o => o.StartOverrideNumberingValue is not null))
                    c[o.LevelIndex?.Value ?? 0] = Start(o.LevelIndex?.Value ?? 0) - 1;
            c[level]++;
            for (var l = level + 1; l < 9; l++) c[l] = Start(l) - 1;
            for (var l = 0; l < level; l++) if (c[l] < Start(l)) c[l] = Start(l);
            if (format == NumberFormatValues.None) return (null, level, false);
            var text = def.LevelText?.Val?.Value ?? $"%{level + 1}.";
            var label = Regex.Replace(text, @"%([1-9])", m =>
            {
                var l = int.Parse(m.Groups[1].Value) - 1;
                var f = levels.TryGetValue(l, out var d) ? d.NumberingFormat?.Val?.Value : NumberFormatValues.Decimal;
                return Format(c[l], f);
            });
            return (label, level, false);
        }

        static string Format(int n, NumberFormatValues? f)
        {
            if (f == NumberFormatValues.LowerLetter) return Letters(n).ToLowerInvariant();
            if (f == NumberFormatValues.UpperLetter) return Letters(n);
            if (f == NumberFormatValues.LowerRoman) return Roman(n).ToLowerInvariant();
            if (f == NumberFormatValues.UpperRoman) return Roman(n);
            if (f == NumberFormatValues.DecimalZero) return n.ToString("00");
            return n.ToString();
        }

        static string Letters(int n) => n <= 0 ? "" : new string((char)('A' + (n - 1) % 26), (n - 1) / 26 + 1);

        static string Roman(int n)
        {
            var (vals, syms) = (new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 }, new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" });
            var sb = new StringBuilder();
            for (var i = 0; i < vals.Length; i++) while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
            return sb.ToString();
        }

        public ConvertedDocument Finish()
        {
            FlushNotes(notes.Count);
            var body = md.ToString();
            if (flat)
            {
                var (markdown, whole) = FsdSections.Flat(body, problems);
                Listed(body, _ => FsdSections.FlatId);
                return new ConvertedDocument(markdown, whole, images, problems, title) { Files = Files(_ => FsdSections.FlatId) };
            }
            if (headings.Count == 0)
                throw new Core.StangentException("no_structure", $"This .docx has no headings, so it has no sections. Apply heading styles, {FsdSections.NoStructureAdvice}.", Core.ExitCodes.Failed, BoldLines());
            var sections = FsdSections.Build(body, headings, problems);
            Listed(body, i => sections[i].Id);
            return new ConvertedDocument(body + "\n", sections, images, problems, title) { Files = Files(i => sections[i].Id) };
        }

        /// <summary>
        /// For a file that stops with <c>no_structure</c> (D122): the lines that look like headings, so a person knows where to apply heading styles. A line
        /// counts when all its words are bold, it has at most <see cref="MaxHeadingWords"/> words, and it doesn't end like a sentence. It's advice only: nothing is imported from it.
        /// </summary>
        List<string> BoldLines()
        {
            var body = main.Document?.Body?.Elements<Paragraph>().ToList() ?? [];
            var found = new List<string>();
            foreach (var (p, at) in body.Select((p, i) => (p, i + 1)))
            {
                var runs = p.Descendants<Run>().Where(r => r.InnerText.Trim().Length > 0).ToList();
                var text = string.Concat(runs.Select(r => r.InnerText)).Trim();
                if (runs.Count == 0 || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxHeadingWords || ".!?;,".Contains(text[^1]) || !runs.TrueForAll(r => Bold(p, r))) continue;
                found.Add($"paragraph {at}: \"{text}\"");
            }
            if (found.Count == 0) return [];
            return [$"stangent never guesses a heading from bold text. {(found.Count > MaxBoldLines ? $"The first {MaxBoldLines} of {found.Count} lines" : $"These {found.Count} lines")} are all bold and look like headings; if they are, give them heading styles in Word and import again.", .. found.Take(MaxBoldLines)];
        }

        bool Bold(Paragraph p, Run r) =>
            BoldValue(r.RunProperties?.Bold) ?? StyleBold(r.RunProperties?.RunStyle?.Val?.Value) ?? StyleBold(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value) ?? false;

        bool? StyleBold(string? id)
        {
            for (var seen = 0; id is not null && seen < 20 && styles.TryGetValue(id, out var style); seen++, id = style.BasedOn?.Val?.Value)
                if (BoldValue(style.StyleRunProperties?.Bold) is { } on) return on;
            return null;
        }

        static bool? BoldValue(Bold? bold) => bold is null ? null : bold.Val is null || bold.Val.Value;

        /// <summary>Everything listed after the sections are known: what each section holds that isn't brought in exactly, comments, page headers, and the completeness check.</summary>
        void Listed(string body, Func<int, string> sectionId)
        {
            string Where(int i) => i >= 0 || flat ? $"section {sectionId(i)}" : "before the first heading";
            foreach (var (section, kind, unit) in marks.OrderBy(m => m.Section))
            {
                var n = markCounts[(section, kind)];
                problems.Add($"{Where(section)}: {kind}{(unit.Length > 0 ? $" ({n}{unit})" : n > 1 ? $" ({n})" : "")}");
            }
            Comments(i => i >= 0 || flat ? sectionId(i) : "(before the first heading)");
            var outside = new OpenXmlElement?[0].Concat(main.HeaderParts.Select(h => (OpenXmlElement?)h.Header)).Concat(main.FooterParts.Select(f => (OpenXmlElement?)f.Footer))
                .OfType<OpenXmlElement>().Sum(e => DocxReview.Words(e).Length);
            if (outside > 0) problems.Add($"page headers and footers aren't imported ({outside} words)");
            Complete(body, Where);
        }

        void Comments(Func<int, string> sectionId)
        {
            if (main.WordprocessingCommentsPart?.Comments is not { } comments) return;
            var resolved = main.WordprocessingCommentsExPart?.CommentsEx?.Elements<DocumentFormat.OpenXml.Office2013.Word.CommentEx>()
                .Where(c => c.Done?.Value == true).Select(c => c.ParaId?.Value).ToHashSet() ?? [];
            var all = comments.Elements<Comment>().ToList();
            var order = commentSection.Keys.ToList();
            foreach (var c in all.OrderBy(c => order.IndexOf(c.Id?.Value ?? "") is var at and >= 0 ? at : int.MaxValue))
            {
                var section = commentSection.TryGetValue(c.Id?.Value ?? "", out var i) ? sectionId(i) : DocxComment.NoAnchor;
                var done = c.Elements<Paragraph>().LastOrDefault()?.ParagraphId?.Value is { } para && resolved.Contains(para);
                problems.Add(new DocxComment(section, c.Author?.Value ?? "unknown", done, string.Join(' ', DocxReview.Words(Text(c)))).Line());
            }
        }

        /// <summary>
        /// The completeness check (D18, D116): per section, the words in the file, minus the rules' removals, plus what the converter adds on
        /// purpose (labels, numbers, table rules, repeated cells), must equal the words rendered. It lists a mismatch and never fails the import.
        /// </summary>
        void Complete(string body, Func<int, string> where)
        {
            for (var i = -1; i < headings.Count; i++)
            {
                var start = i < 0 ? 0 : headings[i].Start;
                var end = i + 1 < headings.Count ? headings[i + 1].Start : body.Length;
                var left = new Dictionary<string, int>(expected.GetValueOrDefault(i) ?? [], StringComparer.Ordinal);
                var extra = new List<string>();
                foreach (var w in DocxReview.Words(body[start..end]))
                    if (left.GetValueOrDefault(w) > 0) left[w]--;
                    else extra.Add(w);
                var missing = left.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
                if (missing.Count == 0 && extra.Count == 0) continue;
                static string Sample(List<string> words) =>
                    string.Join(", ", words.Distinct().Order(StringComparer.Ordinal).Take(5).Select(w => $"\"{w}\""));
                var parts = new List<string>();
                if (missing.Count > 0) parts.Add($"{missing.Count} words in the file aren't in document.md ({Sample(missing)})");
                if (extra.Count > 0) parts.Add($"{extra.Count} words in document.md aren't in the file ({Sample(extra)})");
                problems.Add($"completeness: {where(i)}: {string.Join("; ", parts)}");
            }
        }
    }
}
