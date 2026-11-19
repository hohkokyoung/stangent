using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Stangent.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace Stangent.Spec.Fsd;

/// <summary>
/// One word of a page's own text. Positions come from the baseline, never from a letter's glyph box: for some embedded
/// fonts PdfPig reports a glyph box many points wide and shifted off the letter, so a box's centre can land in the next column.
/// </summary>
sealed class PdfWord(string text, double left, double right, double firstLetterMiddle, double y, double size)
{
    public string Text { get; } = text;
    public double Left { get; } = left;
    public double Right { get; } = right;
    public double FirstLetterMiddle { get; } = firstLetterMiddle;
    public double Y { get; } = y;
    public double Size { get; } = size;
    public PdfTable? Table { get; set; }
}

/// <summary>What a page emits. <c>Text</c> holds the document's words; <c>Prefix</c> only Markdown; a table's words are in its cells.</summary>
sealed record PdfBlock(string Prefix, string Text, bool Tight = false, PdfBlockKind Kind = PdfBlockKind.Paragraph, IReadOnlyList<IReadOnlyList<string>>? Rows = null, int Level = 0, bool StartsAtBody = false, bool EndsAtBody = false, bool EndsPage = false, int Page = 0);

enum PdfBlockKind { Paragraph, Heading, Image, Table }

/// <summary>
/// <c>.pdf</c> → Markdown, deterministically and by code alone (D111). The text comes from <see cref="ContentOrderTextExtractor"/>,
/// or its own rule run over just the page's main-direction, visible letters when a page has others, and this adds structure around
/// its words. The reading rule: the drawing order is used, unless it goes against position (up the page, or back along a line), and
/// then that page is read by position (columns, then top to bottom, left to right) and listed. Three checks hold it to that: cells
/// word for word, each page's words as emitted, and the same words read a second way, by position. All of them rest on PdfPig's
/// letters, so none can catch a wrong letter. Fonts are never used to guess headings. When the code can't tell, it doesn't join and
/// doesn't drop: it keeps the text apart and lists it, since a false sentence is worse than a broken one.
/// </summary>
public static partial class PdfConverter
{
    /// <summary>Lines further apart than this many times the document's usual line pitch start a new paragraph.</summary>
    const double ParagraphGapFactor = 1.25;

    /// <summary>The width of a space, as a share of the font size, in common fonts.</summary>
    const double SpaceShare = 0.25;

    /// <summary>A label set this many spaces from its text was laid out as a list; one space is how a sentence reads.</summary>
    const double LabelApartSpaces = 2;

    /// <summary>Words in neighbouring cells this many spaces apart or less are one run of text going through a rule: cell padding is wider.</summary>
    const double OverrunSpaces = 1.5;

    /// <summary>Bucket for line pitch as a share of font size: finer than a line-spacing setting, coarser than rounding noise.</summary>
    const double PitchBucket = 0.05;

    /// <summary>Letters closer in height than this share of their size are on one line: the share the extractor itself uses.</summary>
    const double SameLineShare = 0.9;

    /// <summary>A column gap is at least this many times the text size; word gaps are far narrower.</summary>
    const double ColumnGapFactor = 1;

    /// <summary>A running header or footer is rarely more than this many lines from the edge of the page.</summary>
    const int RunningLinesFromEdge = 3;

    /// <summary>"Most pages": a repeat on more than this share of the pages with text (of the odd or even pages, when they differ).</summary>
    const double RunningShare = 0.5;

    /// <summary>Fewer repeats than this are chance, whatever their share of a very short document.</summary>
    const int RunningMinPages = 3;

    /// <summary>A contents page starts with at least this many entries: one dotted line alone proves nothing.</summary>
    const int ContentsMinEntries = 3;

    /// <summary>"Near the front": the first quarter of the document, or its first pages if it's short.</summary>
    const double ContentsFrontShare = 0.25;

    const int ContentsFrontMinPages = 10;

    /// <summary>A row of a table ruled only across is rarely taller than this many lines; rules further apart are underlined headings over prose.</summary>
    const int RuleRowLines = 3;

    /// <summary>Text more than this many degrees off the page's main direction is another direction; a slightly skewed page isn't.</summary>
    const double DirectionSlack = 2;

    /// <summary>An image over this share of the page is the page, as a scan is.</summary>
    const double FullPageImageShare = 0.8;

    /// <summary>Enough of a cell's first words to find it again.</summary>
    const int CellWordsShown = 6;

    /// <summary>Deeper than any real outline. PdfPig walks outlines recursively, and a stack overflow can't be caught.</summary>
    const int OutlineMaxDepth = 64;

    const string BadStructureAdvice = "Re-export the PDF with bookmarks or supply the Word file, or use --flat to import the whole document as one section with no structure";

    static readonly ContentOrderTextExtractor.Options ExtractOptions = new() { NegativeGapAsWhitespace = true };

    [GeneratedRegex(@"^(.*?\S)\s*(?:[.…]\s*){3,}\s*(\d{1,5})$")]
    private static partial Regex ContentsEntry();

    [GeneratedRegex(@"^(?:\d{1,3}[.)]|\(\d{1,3}\)|\(?[a-zA-Z]\))$")]
    private static partial Regex ListLabel();

    [GeneratedRegex(@"^(\(?)(\d+|[a-zA-Z])([.)])$")]
    private static partial Regex LabelCore();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:[ivxlcdm]+|[IVXLCDM]+)(?![\p{L}\p{N}])")]
    private static partial Regex RomanToken();

    public static ConvertedDocument Convert(byte[] pdf, bool flat = false) => Convert(pdf, flat, null);

    /// <summary>Files attached to the PDF (A.7). The file names no place in the text for them, so they have no section, and none is guessed.</summary>
    static List<FileInDocument> Attached(PdfDocument doc)
    {
        try
        {
            return doc.Advanced.TryGetEmbeddedFiles(out var files)
                ? [.. files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => new FileInDocument(f.Name, [.. f.Bytes], null, $"attached to the PDF as {f.Name}"))]
                : [];
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NullReferenceException or InvalidCastException) { return []; }
    }

    /// <summary><paramref name="tamper"/> lets a test break a page's layout: no PDF can reach the check that catches it, since a block's words all come from the page.</summary>
    internal static ConvertedDocument Convert(byte[] pdf, bool flat, Func<int, List<PdfBlock>, List<PdfBlock>>? tamper) =>
        Open(pdf, doc => new Reader(doc, flat, tamper).Run());

    /// <summary>The import's safety step (D121): damaged, protected or empty stops here. Gives the page count.</summary>
    public static int CheckSafe(byte[] pdf) => Open(pdf, doc => doc.NumberOfPages);

    /// <summary>Sensitivity labels in the document's information and its XMP metadata (D122). Metadata that can't be read shows no label; that's a known gap.</summary>
    public static IReadOnlyList<ImportLabel> Labels(byte[] pdf) => Open(pdf, doc =>
    {
        var properties = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in doc.Information.DocumentInformationDictionary?.Data ?? new Dictionary<string, UglyToad.PdfPig.Tokens.IToken>())
            if (value is UglyToad.PdfPig.Tokens.IDataToken<string> text) properties.Add(KeyValuePair.Create(key, text.Data));
        try
        {
            if (doc.TryGetXmpMetadata(out var xmp))
                foreach (var e in xmp.GetXDocument().Descendants())
                {
                    if (!e.HasElements) properties.Add(KeyValuePair.Create(e.Name.LocalName, e.Value));
                    properties.AddRange(e.Attributes().Select(a => KeyValuePair.Create(a.Name.LocalName, a.Value)));
                }
        }
        catch (System.Xml.XmlException) { }
        return (IReadOnlyList<ImportLabel>)ImportLabels.FromProperties(properties);
    });

    static T Open<T>(byte[] pdf, Func<PdfDocument, T> read)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (doc.IsEncrypted) throw Locked();
            if (doc.NumberOfPages == 0) throw new StangentException("invalid_pdf", "This PDF has no pages.");
            return read(doc);
        }
        catch (PdfDocumentEncryptedException)
        {
            throw Locked();
        }
        // PdfPig throws many kinds of exception on a damaged file, and nothing has been written yet.
        catch (Exception e) when (e is not StangentException)
        {
            throw new StangentException("invalid_pdf", "This file can't be read as a PDF: it may be damaged. Supply a fresh copy.", ExitCodes.Failed, [e.Message]);
        }
    }

    /// <summary>Any protection counts, even one a viewer opens without a password: someone chose to restrict this file, and that isn't worked around.</summary>
    static StangentException Locked() => new("pdf_locked", "This PDF is protected, so it isn't imported, even where it opens without a password. Supply an unprotected copy.");

    /// <summary>A line of the page's own text, as the extractor split it. A removed line is a running header or footer.</summary>
    sealed class Line(List<PdfWord> words)
    {
        public List<PdfWord> Words { get; } = words;
        public string Text { get; } = string.Join(" ", words.Select(w => w.Text));
        public string Key { get; } = Squash(string.Concat(words.Select(w => w.Text)));
        public double Y => Words[0].Y;
        public double Size => Words[0].Size;
        public double Left => Words.Min(w => w.Left);
        public double Right => Words.Max(w => w.Right);
        public bool Removed { get; set; }
        public bool OwnParagraph { get; set; }
        public bool BreakBefore { get; set; }
        public bool InCell { get; set; }
    }

    /// <summary>A letter in the coordinates its text reads in: level, and left to right, whatever direction the page draws it.</summary>
    internal sealed record Glyph(string Value, double Start, double End, double Y, double Size, Letter Source);

    sealed record Image(string Name, double Left, double Bottom, double Right, double Top) { public PdfCell? Cell { get; set; } }

    sealed record Target(string Title, int Level, int Page, double? Top);

    /// <summary>A section heading on a page: found on lines <c>Start..End</c>, or not found and placed at <c>Start</c>.</summary>
    sealed record Anchor(Target Target, int Start, int End, string? Found);

    sealed class PageModel(int number, double height, string own, List<Line> lines, PdfGrids grids, List<Image> images, bool aligned, bool readingsAgree, bool turned)
    {
        public bool Turned { get; } = turned;
        public int Number { get; } = number;
        public double Height { get; } = height;
        public string Own { get; } = own;
        public List<Line> Lines { get; set; } = lines;
        public PdfGrids Grids { get; } = grids;
        public List<Image> Images { get; } = images;
        public bool Aligned { get; } = aligned;
        public bool ReadingsAgree { get; } = readingsAgree;
        public List<Anchor> Anchors { get; set; } = [];
        public PageModel? Before { get; set; }
        public PageModel? After { get; set; }
    }

    internal sealed class Reader(PdfDocument doc, bool flat, Func<int, List<PdfBlock>, List<PdfBlock>>? tamper)
    {
        readonly List<string> problems = [];
        readonly List<FsdImage> images = [];
        readonly Dictionary<string, string> imageNames = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> otherDirection = new(StringComparer.Ordinal);
        readonly List<(int Page, int Words)> whitePages = [];
        int imageCount;
        int unmapped;
        double bodySize;
        double usualPitch;

        public ConvertedDocument Run()
        {
            var pages = new List<PageModel>();
            for (var n = 1; n <= doc.NumberOfPages; n++) pages.Add(ReadPage(doc.GetPage(n), n));
            if (pages.All(p => p.Lines.Count == 0))
                throw new StangentException("no_text", "No page of this PDF has any text: it looks scanned or made of pictures, and text isn't read from images. Supply a copy with real text, or the Word file.");
            foreach (var p in pages.Where(p => p.Lines.Count == 0))
                problems.Add($"page {p.Number}: no text (scanned or picture-only); nothing was read from it");

            var bookmarks = flat ? null : Bookmarks(pages.Count);
            usualPitch = UsualPitch(pages);
            bodySize = UsualSize([.. pages.SelectMany(p => p.Lines).SelectMany(l => l.Words)]);
            RemoveRunningLines(pages, TitleTest(pages, bookmarks), usualPitch);
            foreach (var p in pages) FillTables(p);
            foreach (var p in pages) SettleOrder(p);
            if (!flat) PlaceHeadings(pages, bookmarks ?? PrintedContents(pages) ?? throw NoStructure());

            for (var n = 1; n < pages.Count; n++) (pages[n].Before, pages[n - 1].After) = (pages[n - 1], pages[n]);
            var pitch = UsualPitch(pages);
            var blocks = pages.SelectMany(p =>
            {
                var own = CheckedBlocks(p, pitch);
                if (own.Count > 0) own[^1] = own[^1] with { EndsPage = true, Page = p.Number };
                return own;
            }).ToList();
            var body = new MarkdownText();
            var headings = new List<FsdHeading>();
            var joins = Write(blocks, body, headings);
            if (joins.Count > 0) problems.Add($"{joins.Count} sentences were joined across a page break, after pages {string.Join(", ", joins)}");

            var before = EmittedWords(blocks.TakeWhile(b => b.Kind != PdfBlockKind.Heading)).Count();
            if (!flat && before > 0) problems.Add($"{before} words come before the first section, so they're in the document but in no section");
            if (imageCount > 0)
                problems.Add($"{imageCount} images in this document ({images.Count} different): no AI or code reads them, and rules can sit inside diagrams");
            foreach (var (other, count) in otherDirection.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal))
                problems.Add($"text running in another direction on {count} pages is kept out of the text (a watermark or margin note, or a label on a diagram): \"{other}\"");
            if (whitePages.Count > 0)
                problems.Add($"{whitePages.Sum(w => w.Words)} words are text in white, may not be visible; kept, on pages {string.Join(", ", whitePages.Select(w => w.Page))}");
            if (unmapped > 0)
                problems.Add($"{unmapped} characters are symbol-font or unmapped glyphs with no standard letter; they're kept as drawn, and a quote must match them exactly");
            var title = string.IsNullOrWhiteSpace(doc.Information.Title) ? null : doc.Information.Title.Trim();
            if (flat)
            {
                var (markdown, whole) = FsdSections.Flat(body.ToString(), problems);
                return new ConvertedDocument(markdown, whole, images, problems, title) { Files = Attached(doc) };
            }
            var text = body.ToString();
            return new ConvertedDocument(text + "\n", FsdSections.Build(text, headings, problems, outline: true), images, problems, title) { Files = Attached(doc) };
        }

        static StangentException NoStructure() =>
            new("no_structure", $"This PDF has no usable bookmarks and no printed contents page, so it has no sections, and headings aren't guessed from fonts. {BadStructureAdvice}.");

        PageModel ReadPage(Page page, int number)
        {
            var images = page.GetImages().Select(i => i.BoundingBox).ToList();
            var (kept, angle, others, hidden, ocr, white) = Sort(page, images);
            if (ocr > 0) problems.Add($"page {number}: {ocr} words are text from an OCR layer over a full-page image, not checked");
            if (hidden > 0) problems.Add($"page {number}: {hidden} words are hidden (invisible or off the page); removed from the text");
            if (white > 0) whitePages.Add((number, white));
            foreach (var text in others.Distinct(StringComparer.Ordinal)) otherDirection[text] = otherDirection.GetValueOrDefault(text) + 1;

            var turn = Turn(page, angle);
            var glyphs = kept.Select(turn).ToList();
            // Nothing set aside and nothing turned: the extractor itself reads the page.
            var own = kept.Count == page.Letters.Count(l => !string.IsNullOrEmpty(l.Value)) && angle == 0 ? ContentOrderTextExtractor.GetText(page, ExtractOptions) : ReadText(glyphs);
            var (lines, aligned) = ReadLines(glyphs, own);
            if (!aligned) problems.Add($"page {number}: its words couldn't be placed on the page; kept as plain text");
            var words = lines.SelectMany(l => l.Words).ToList();
            var map = angle == 0 ? null : Map(page, angle);
            var (width, height) = angle == 0 ? (page.Width, page.Height) : Extent(page, angle);
            var grids = aligned && words.Count > 0
                ? PdfTables.Find(page.Paths, width, height, UsualSize(words), map)
                : new PdfGrids([], [], []);
            unmapped += CountUnmapped(words.SelectMany(w => w.Text));
            var agree = SameWords(OwnWords(own), WordsByPosition(glyphs));
            return new PageModel(number, height, own, lines, grids, ReadImages(page, number, map), aligned, agree, angle != 0);
        }

        /// <summary>
        /// Which letters this page reads: those in its main direction that a person can see. Others are set aside: other directions by
        /// their text, hidden ones by a word count. Invisible text over a page that's one big image is an OCR layer, kept and counted.
        /// </summary>
        static (List<Letter> Kept, double Angle, List<string> Others, int Hidden, int Ocr, int White) Sort(Page page, List<PdfRectangle> images)
        {
            var letters = page.Letters.Where(l => !string.IsNullOrEmpty(l.Value)).ToList();
            var pageArea = page.Width * page.Height;
            var scanned = images.Any(i => i.Width * i.Height >= pageArea * FullPageImageShare);
            static bool Invisible(Letter l) => l.RenderingMode == TextRenderingMode.Neither;
            bool OffPage(Letter l) => !Within(page.CropBox.Bounds, l.StartBaseLine);
            // Only what's certainly never painted goes. White may sit on anything, so it stays and is said.
            bool Hidden(Letter l) => (Invisible(l) && !scanned) || OffPage(l);

            var seen = letters.Where(l => !string.IsNullOrWhiteSpace(l.Value) && !Hidden(l)).ToList();
            var angle = seen.Select(Direction).OfType<double>().GroupBy(d => d).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
            bool Main(Letter l) => Direction(l) is not { } d || Math.Abs(Math.IEEERemainder(d - angle, 360)) <= DirectionSlack;

            var kept = new List<Letter>();
            var hidden = new List<Letter>();
            var other = new List<Letter>();
            var last = kept;
            foreach (var l in letters)
            {
                // White space goes with whatever it follows.
                if (!string.IsNullOrWhiteSpace(l.Value)) last = Hidden(l) ? hidden : Main(l) ? kept : other;
                last.Add(l);
            }
            var others = other.GroupBy(l => Direction(l) ?? 0).OrderBy(g => g.Key)
                .Select(g => Normal(ReadText([.. g.Select(Turn(page, g.Key))]))).Where(t => t.Length > 0).ToList();
            var ocr = scanned ? Words(kept.Where(Invisible), page) : 0;
            var white = Words(kept.Where(l => l.RenderingMode is TextRenderingMode.Fill or TextRenderingMode.FillClip && IsWhite(l)), page);
            return (kept, angle, others, Words(hidden, page), ocr, white);
        }

        static bool Within(PdfRectangle box, PdfPoint point) => point.X >= box.Left && point.X <= box.Right && point.Y >= box.Bottom && point.Y <= box.Top;

        static int Words(IEnumerable<Letter> letters, Page page) => OwnWords(ReadText([.. letters.Select(Turn(page, 0))])).Count;

        /// <summary>A colour that can't be read, such as a pattern, counts as visible: reading one must never stop an import.</summary>
        static bool IsWhite(Letter letter)
        {
            try
            {
                var (r, g, b) = letter.FillColor.ToRGBValues();
                return r >= 1 && g >= 1 && b >= 1;
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or NotImplementedException or ArgumentException or NullReferenceException) { return false; }
        }

        /// <summary>The way a letter runs, in whole degrees; null for one with no width.</summary>
        static double? Direction(Letter l)
        {
            var (dx, dy) = (l.EndBaseLine.X - l.StartBaseLine.X, l.EndBaseLine.Y - l.StartBaseLine.Y);
            if (dx == 0 && dy == 0) return null;
            var degrees = Math.Round(Math.Atan2(dy, dx) * 180 / Math.PI);
            return degrees < 0 ? degrees + 360 : degrees;
        }

        static Func<PdfPoint, PdfPoint> Map(Page page, double angle)
        {
            var (cos, sin) = (Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180));
            PdfPoint Raw(PdfPoint p) => new(p.X * cos + p.Y * sin, p.Y * cos - p.X * sin);
            var corners = new[] { new PdfPoint(0, 0), new PdfPoint(page.Width, 0), new PdfPoint(0, page.Height), new PdfPoint(page.Width, page.Height) }.Select(Raw).ToList();
            var (minX, minY) = (corners.Min(c => c.X), corners.Min(c => c.Y));
            return p => { var r = Raw(p); return new PdfPoint(r.X - minX, r.Y - minY); };
        }

        static (double Width, double Height) Extent(Page page, double angle)
        {
            var map = Map(page, angle);
            var corners = new[] { new PdfPoint(0, 0), new PdfPoint(page.Width, 0), new PdfPoint(0, page.Height), new PdfPoint(page.Width, page.Height) }.Select(map).ToList();
            return (corners.Max(c => c.X), corners.Max(c => c.Y));
        }

        static Func<Letter, Glyph> Turn(Page page, double angle)
        {
            if (angle == 0) return l => new Glyph(l.Value, l.StartBaseLine.X, l.EndBaseLine.X, l.StartBaseLine.Y, l.PointSize, l);
            var map = Map(page, angle);
            return l =>
            {
                var (start, end) = (map(l.StartBaseLine), map(l.EndBaseLine));
                return new Glyph(l.Value, start.X, end.X, start.Y, l.PointSize, l);
            };
        }

        static double UsualSize(List<PdfWord> words) =>
            words.GroupBy(w => Math.Round(w.Size)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key is > 0 and var s ? s : 12;

        static List<string> OwnWords(string own) => [.. own.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

        /// <summary>
        /// Lines and words exactly as the extractor wrote them, with each word's letters found by walking the page's letters
        /// alongside its output: the extractor copies them in order and only adds white space, so the two can't drift apart.
        /// </summary>
        static (List<Line> Lines, bool Aligned) ReadLines(List<Glyph> letters, string own)
        {
            var glyphs = new List<(char Char, Glyph Letter)>();
            foreach (var letter in letters)
                foreach (var c in letter.Value)
                    if (!char.IsWhiteSpace(c)) glyphs.Add((c, letter));
            var at = 0;
            var aligned = true;
            var lines = new List<Line>();
            foreach (var text in own.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var words = new List<PdfWord>();
                foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var (first, last) = (at, at + word.Length - 1);
                    if (last >= glyphs.Count || !word.SequenceEqual(glyphs.Skip(first).Take(word.Length).Select(g => g.Char))) aligned = false;
                    at += word.Length;
                    if (!aligned) { words.Add(new PdfWord(word, 0, 0, 0, 0, 0)); continue; }
                    var (a, b) = (glyphs[first].Letter, glyphs[last].Letter);
                    words.Add(new PdfWord(word, a.Start, b.End, (a.Start + a.End) / 2, a.Y, a.Size));
                }
                if (words.Count > 0) lines.Add(new Line(words));
            }
            if (at != glyphs.Count) aligned = false;
            if (aligned) return (lines, true);
            // Without positions every line stands alone, in the extractor's order.
            return ([.. own.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(w => w.Length > 0)
                .Select(w => new Line([.. w.Select(x => new PdfWord(x, 0, 0, 0, 0, 0))]) { OwnParagraph = true })], false);
        }

        /// <summary>
        /// The second reading: the same letters taken by position, line by line and left to right, split by the extractor's own
        /// white-space rule. It only differs when drawing order changes the words, such as one word drawn in pieces.
        /// </summary>
        static List<string> WordsByPosition(List<Glyph> glyphs)
        {
            var letters = glyphs.OrderByDescending(l => l.Y).ThenBy(l => l.Start).ToList();
            var words = new List<string>();
            for (var i = 0; i < letters.Count;)
            {
                var j = i + 1;
                while (j < letters.Count && letters[i].Y - letters[j].Y <= SameLineShare * Math.Min(letters[i].Size, letters[j].Size)) j++;
                var word = new StringBuilder();
                Glyph? previous = null;
                foreach (var letter in letters.GetRange(i, j - i).OrderBy(l => l.Start).ThenBy(l => l.End))
                {
                    if (string.IsNullOrWhiteSpace(letter.Value))
                    {
                        if (word.Length > 0) words.Add(word.ToString());
                        word.Clear();
                        previous = null;
                        continue;
                    }
                    if (previous is not null && WhitespaceSizeStatistics.IsProbablyWhitespace(Math.Abs(letter.Start - previous.End), previous.Source))
                    {
                        words.Add(word.ToString());
                        word.Clear();
                    }
                    word.Append(letter.Value.Where(c => !char.IsWhiteSpace(c)).ToArray());
                    previous = letter;
                }
                if (word.Length > 0) words.Add(word.ToString());
                i = j;
            }
            return words;
        }

        List<Image> ReadImages(Page page, int number, Func<PdfPoint, PdfPoint>? map)
        {
            var found = new List<Image>();
            foreach (var image in page.GetImages())
            {
                imageCount++;
                var raw = image.RawMemory.ToArray();
                var rawKey = System.Convert.ToHexStringLower(SHA256.HashData(raw));
                if (!imageNames.TryGetValue(rawKey, out var name))
                {
                    var (bytes, ext) = Decoded(image, raw);
                    if (ext == "bin") problems.Add($"page {number}: an image couldn't be decoded to a known format; its raw bytes are saved");
                    name = $"images/{System.Convert.ToHexStringLower(SHA256.HashData(bytes))[..12]}.{ext}";
                    imageNames[rawKey] = name;
                    if (images.All(i => i.Name != name)) images.Add(new FsdImage(name, bytes, null));
                }
                var box = image.BoundingBox;
                var corners = new[] { new PdfPoint(box.Left, box.Bottom), new PdfPoint(box.Right, box.Top) }.Select(map ?? (point => point)).ToList();
                found.Add(new Image(name, corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y)));
            }
            return [.. found.OrderByDescending(i => (i.Top + i.Bottom) / 2).ThenBy(i => i.Left).ThenBy(i => i.Name, StringComparer.Ordinal)];
        }

        static (byte[] Bytes, string Ext) Decoded(IPdfImage image, byte[] raw)
        {
            if (raw.AsSpan().StartsWith<byte>([0xFF, 0xD8, 0xFF])) return (raw, "jpg");
            if (raw.AsSpan().StartsWith<byte>([0xFF, 0x4F, 0xFF, 0x51]) || raw.AsSpan().StartsWith<byte>([0, 0, 0, 0x0C, 0x6A, 0x50])) return (raw, "jp2");
            try { if (image.TryGetPng(out var png) && png is { Length: > 0 }) return (png, "png"); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* Kept as raw bytes and listed. */ }
            return (raw, "bin");
        }

        /// <summary>Whether a line is a section's own title: that's content, however regular it looks.</summary>
        static Func<int, string, bool> TitleTest(List<PageModel> pages, List<Target>? bookmarks)
        {
            if (bookmarks is not null)
            {
                var marked = bookmarks.Select(t => (t.Page, Squash(t.Title))).ToHashSet();
                return (page, key) => marked.Contains((page, key));
            }
            var listed = ReadContents(pages).Entries.Select(e => Squash(e.Title)).ToHashSet(StringComparer.Ordinal);
            return (_, key) => listed.Contains(key);
        }

        sealed record EdgeLine(int Page, Line Line, double FromEdge, bool Apart, bool Title);

        /// <summary>
        /// A running line stands apart from the text near the top or bottom edge, at one distance from that edge, on most pages,
        /// or most odd or even pages. Its text is identical, or differs only by a number that follows the page. Nothing else is removed.
        /// </summary>
        void RemoveRunningLines(List<PageModel> pages, Func<int, string, bool> isTitle, double pitch)
        {
            var withText = pages.Where(p => p.Aligned && p.Lines.Count > 0).ToList();
            foreach (var top in new[] { true, false })
            {
                var edge = withText.SelectMany(p => EdgeLines(p, top, isTitle, pitch)).Where(e => !e.Title).ToList();
                foreach (var group in edge.GroupBy(e => e.Line.Text, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                    Remove(top, group.ToList(), withText, found => $"\"{found[0].Line.Text}\"");

                var numbered = edge.Where(e => !e.Line.Removed)
                    .SelectMany(e => Numbers(e.Line.Text).Select(n => (Entry: e, n.Template, n.Roman, n.Value)))
                    .GroupBy(x => x.Template, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal);
                foreach (var group in numbered)
                {
                    // Arabic and Roman numbering each run at their own fixed distance from the page number.
                    var tracking = group.GroupBy(x => x.Roman).OrderBy(g => g.Key).SelectMany(kind =>
                    {
                        var offset = kind.GroupBy(x => x.Value - x.Entry.Page).OrderByDescending(o => o.Count()).ThenBy(o => o.Key).First().Key;
                        return kind.Where(x => x.Value - x.Entry.Page == offset).Select(x => x.Entry);
                    }).Where(e => !e.Line.Removed).OrderBy(e => e.Page).ToList();
                    Remove(top, tracking, withText, found => $"\"{found[0].Line.Text}\" to \"{found[^1].Line.Text}\", its number following the page");
                }
            }
        }

        static List<EdgeLine> EdgeLines(PageModel p, bool top, Func<int, string, bool> isTitle, double pitch)
        {
            // A line inside a table is the table's: a repeated header row or last row is content.
            bool InGrid(Line l) => p.Grids.Tables.Any(t => t.Spans(l.Words[0].FirstLetterMiddle, l.Y + l.Size * SpaceShare));
            var from = (top ? p.Lines.OrderByDescending(l => l.Y) : p.Lines.OrderBy(l => l.Y)).Where(l => !InGrid(l)).ToList();
            var edge = from.Take(RunningLinesFromEdge).ToList();
            // The block of lines at the edge, up to the first gap wider than the document's line spacing.
            var block = 0;
            for (var i = 0; i < edge.Count && block == 0; i++)
                if (i + 1 < from.Count && Math.Abs(from[i].Y - from[i + 1].Y) > from[i].Size * pitch * ParagraphGapFactor) block = i + 1;
            var apart = block > 0 && !edge.Take(block).Any(l => isTitle(p.Number, l.Key));
            return [.. edge.Select((l, i) => new EdgeLine(p.Number, l, top ? p.Height - l.Y : l.Y, apart && i < block, isTitle(p.Number, l.Key)))];
        }

        void Remove(bool top, List<EdgeLine> lines, List<PageModel> withText, Func<List<EdgeLine>, string> shown)
        {
            int Needed(int pageCount) => Math.Max(RunningMinPages, (int)Math.Floor(pageCount * RunningShare) + 1);
            IEnumerable<(List<EdgeLine> Lines, int Pages)> Choices()
            {
                yield return (lines, withText.Count);
                foreach (var odd in new[] { 1, 0 })
                    yield return ([.. lines.Where(e => e.Page % 2 == odd)], withText.Count(p => p.Number % 2 == odd));
            }
            foreach (var (choice, pageCount) in Choices())
            {
                var same = LargestRun(choice);
                if (same.Count(e => e.Apart) < Needed(pageCount)) continue;
                foreach (var e in same) e.Line.Removed = true;
                var ordered = same.OrderBy(e => e.Page).ToList();
                problems.Add($"removed a running {(top ? "header" : "footer")} on {same.Count} pages: {shown(ordered)}");
                return;
            }
            var repeated = LargestRun(lines);
            if (repeated.Count >= Needed(withText.Count))
                problems.Add($"a repeated line was kept, because it doesn't stand apart from the text, on {repeated.Count} pages: {shown([.. repeated.OrderBy(e => e.Page)])}");
        }

        /// <summary>Every number in a line, each with the line's text around it: "Page 7 of 9" → "Page # of 9", and "Page 7 of #".</summary>
        static IEnumerable<(string Template, bool Roman, int Value)> Numbers(string text)
        {
            foreach (Match m in Digits().Matches(text))
                if (int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    yield return (text[..m.Index] + "#" + text[(m.Index + m.Length)..], false, value);
            foreach (Match m in RomanToken().Matches(text))
                if (RomanValue(m.Value) is { } value)
                    yield return (text[..m.Index] + "#" + text[(m.Index + m.Length)..], true, value);
        }

        /// <summary>A Roman numeral's value, when it's written the standard way.</summary>
        static int? RomanValue(string text)
        {
            var (values, symbols) = (new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 }, new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" });
            var (lower, at, total) = (text.ToLowerInvariant(), 0, 0);
            for (var i = 0; i < values.Length; i++)
                while (lower.AsSpan(at).StartsWith(symbols[i])) { total += values[i]; at += symbols[i].Length; }
            return at == lower.Length && total > 0 ? total : null;
        }

        /// <summary>The most lines at about one distance from the edge: a line of the same words elsewhere on a page is the document's text.</summary>
        static List<EdgeLine> LargestRun(List<EdgeLine> found)
        {
            var sorted = found.OrderBy(x => x.FromEdge).ToList();
            var best = new List<EdgeLine>();
            var current = new List<EdgeLine>();
            for (var i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && sorted[i].FromEdge - sorted[i - 1].FromEdge > sorted[i].Line.Size * SpaceShare) current = [];
                current.Add(sorted[i]);
                if (current.Count > best.Count) best = current;
            }
            return best;
        }

        /// <summary>
        /// Puts words into cells. A table stands only if all of it holds: every cell word for word in the page's own text, no word
        /// across a rule, none in the drawn extent but outside a cell, cells read in order. Otherwise it's plain text, and listed.
        /// </summary>
        void FillTables(PageModel p)
        {
            foreach (var note in p.Grids.NotTables) problems.Add($"page {p.Number}: {note}");
            var visible = p.Lines.Where(l => !l.Removed).ToList();
            var settled = new HashSet<int>();
            var own = $" {Normal(p.Own)} ";
            foreach (var table in p.Grids.Tables)
            {
                var claimed = new List<(PdfWord Word, PdfCell Cell)>();
                var strays = false;
                var overrun = false;
                foreach (var line in visible)
                {
                    PdfCell? before = null;
                    PdfWord? last = null;
                    foreach (var word in line.Words)
                    {
                        // The word goes whole by where its first letter starts, so a word is never cut at a column.
                        var y = word.Y + word.Size * SpaceShare;
                        var cell = table.Cells.FirstOrDefault(c => c.Holds(word.FirstLetterMiddle, y));
                        if (cell is null) strays |= table.Spans(word.FirstLetterMiddle, y);
                        else
                        {
                            claimed.Add((word, cell));
                            overrun |= before is not null && !ReferenceEquals(before, cell) && last is not null && Math.Abs(word.Left - last.Right) <= word.Size * SpaceShare * OverrunSpaces;
                        }
                        (before, last) = (cell, word);
                    }
                }
                foreach (var (word, cell) in claimed) cell.Words.Add(word);

                var filled = table.Cells.Where(c => c.Words.Count > 0).ToList();
                var rowsFilled = filled.Select(c => c.Row).Distinct().Count();
                var colsFilled = filled.Select(c => c.Col).Distinct().Count();
                var isGrid = table.Rows >= 2 && table.Cols >= 2;
                if (!isGrid || rowsFilled < 2 || colsFilled < 2)
                {
                    if (filled.Count >= 2)
                    {
                        // Not a table, but its cells aren't one sentence either.
                        var start = string.Join(" ", filled[0].Words.Take(CellWordsShown).Select(w => w.Text));
                        problems.Add($"page {p.Number}: a drawn grid of {table.Rows} rows and {table.Cols} columns starting \"{start}\" isn't read as a table: its cells' text is kept apart in paragraphs, in the order drawn, and its columns aren't kept");
                        KeepCellsApart(p, filled);
                        settled.Add(table.Group);
                    }
                    else if (isGrid)
                    {
                        problems.Add($"page {p.Number}: a drawn grid of {table.Rows} rows and {table.Cols} columns has {(filled.Count == 0 ? "no text in it" : "text in only one cell")}, so it isn't read as a table");
                        settled.Add(table.Group);
                    }
                    // A single box round some text is prose, with nothing to say.
                    else if (!strays && table.Cells.Count == 1 && p.Grids.Tables.Count(t => t.Group == table.Group) == 1) settled.Add(table.Group);
                    foreach (var cell in table.Cells) cell.Words.Clear();
                    continue;
                }
                settled.Add(table.Group);
                var fault = strays ? "text inside its drawn lines is in no closed cell"
                    : overrun || claimed.Any(x => x.Word.Right > x.Cell.Right + x.Word.Size * SpaceShare) ? "text runs across a cell's rule"
                    : filled.Any(c => !InOrder(c.Words)) ? "a cell's words are drawn out of order"
                    : filled.Any(c => !own.Contains($" {c.Text} ", StringComparison.Ordinal)) ? "a cell's text isn't word for word in the page's own text"
                    : null;
                if (fault is not null)
                {
                    problems.Add($"page {p.Number}: a table wasn't brought in exactly ({fault}); kept as plain text");
                    foreach (var line in p.Lines.Where(l => l.Words.Any(w => claimed.Any(x => ReferenceEquals(x.Word, w))))) (line.OwnParagraph, line.InCell) = (true, true);
                    foreach (var cell in table.Cells) cell.Words.Clear();
                    continue;
                }
                foreach (var (word, _) in claimed) word.Table = table;
                foreach (var image in p.Images.Where(i => i.Cell is null))
                    if (table.Cells.FirstOrDefault(c => c.Holds((image.Left + image.Right) / 2, (image.Bottom + image.Top) / 2)) is { } holder)
                    {
                        image.Cell = holder;
                        holder.Images.Add(image.Name);
                    }
            }
            if (SuggestsATable(p, visible, settled))
                problems.Add($"page {p.Number}: drawn lines with text between them may be a table; not brought in as one");
        }

        /// <summary>Splits lines that run across cells, so words from two cells never read as one sentence, and starts a paragraph at each cell.</summary>
        static void KeepCellsApart(PageModel p, List<PdfCell> cells)
        {
            var cellOf = cells.SelectMany(c => c.Words.Select(w => (Word: w, Cell: c))).ToDictionary(x => x.Word, x => x.Cell);
            var lines = new List<Line>();
            foreach (var line in p.Lines)
            {
                var pieces = new List<List<PdfWord>>();
                PdfCell? current = null;
                foreach (var word in line.Words)
                {
                    var cell = cellOf.GetValueOrDefault(word);
                    if (pieces.Count == 0 || !ReferenceEquals(cell, current)) pieces.Add([]);
                    pieces[^1].Add(word);
                    current = cell;
                }
                if (pieces.Count <= 1) { lines.Add(line); continue; }
                lines.AddRange(pieces.Select(w => new Line(w) { Removed = line.Removed, OwnParagraph = true }));
            }
            p.Lines = lines;
            foreach (var line in p.Lines.Where(l => l.Words.Any(cellOf.ContainsKey))) line.InCell = true;
            foreach (var cell in cells)
                if (p.Lines.FirstOrDefault(l => ReferenceEquals(l.Words[0], cell.Words[0])) is { } first) first.BreakBefore = true;
        }

        /// <summary>Rules that aren't a table but look like one: parallel rules a row apart with text between them, or a divider with text on both sides of it.</summary>
        bool SuggestsATable(PageModel p, List<Line> visible, HashSet<int> settled)
        {
            var free = visible.SelectMany(l => l.Words).Where(w => w.Table is null).ToList();
            if (free.Count == 0) return false;
            var size = UsualSize(free);
            var tolerance = size * SpaceShare;
            var rules = p.Grids.Rules.Where(r => !settled.Contains(r.Group)).ToList();
            var rowHeight = size * usualPitch * RuleRowLines;
            // A lone rule under a line of text is an underline, and never gets here. Two with text between them are never underlines.
            var across = rules.Where(r => r.Horizontal).OrderByDescending(r => r.At).ToList();
            for (var i = 0; i < across.Count; i++)
                for (var j = i + 1; j < across.Count && across[i].At - across[j].At <= rowHeight; j++)
                    if (Math.Abs(across[i].From - across[j].From) <= tolerance && Math.Abs(across[i].To - across[j].To) <= tolerance
                        && free.Any(w => w.FirstLetterMiddle >= across[i].From && w.FirstLetterMiddle <= across[i].To && w.Y < across[i].At && w.Y > across[j].At))
                        return true;
            return rules.Where(r => !r.Horizontal).Any(v => visible.Any(l => l.Y >= v.From && l.Y <= v.To
                && l.Words.Any(w => w.Table is null && w.FirstLetterMiddle < v.At) && l.Words.Any(w => w.Table is null && w.FirstLetterMiddle > v.At)));
        }

        /// <summary>A cell's words never go up, or back along a line.</summary>
        static bool InOrder(List<PdfWord> words) =>
            words.Zip(words.Skip(1)).All(pair => !GoesBack(pair.First.Left, pair.First.Right, pair.First.Y, pair.Second.Left, pair.Second.Right, pair.Second.Y, pair.Second.Size));

        /// <summary>Whether going from one piece of text to the next goes against reading: back along a line, or up the page without moving wholly right (a new column).</summary>
        static bool GoesBack(double aLeft, double aRight, double aY, double bLeft, double bRight, double bY, double size)
        {
            var tolerance = size * SpaceShare;
            if (Math.Abs(aY - bY) <= tolerance) return bRight <= aLeft + tolerance;
            return bY > aY && bLeft < aRight - tolerance;
        }

        /// <summary>The reading rule: drawing order unless it goes against position, and then this page is read by position, and listed.</summary>
        void SettleOrder(PageModel p)
        {
            if (!p.Aligned) return;
            // Prose lines and whole tables, in the order they'd be emitted.
            var items = new List<(double Left, double Right, double Top, double Bottom, double Size)>();
            var tablesSeen = new HashSet<PdfTable>();
            var disordered = false;
            foreach (var line in p.Lines.Where(l => !l.Removed))
            {
                foreach (var table in line.Words.Select(w => w.Table).OfType<PdfTable>())
                    if (tablesSeen.Add(table)) items.Add((table.Left, table.Right, table.Top, table.Bottom, line.Size));
                var free = line.Words.Where(w => w.Table is null).ToList();
                if (free.Count == 0) continue;
                disordered |= !InOrder(free);
                items.Add((free.Min(w => w.Left), free.Max(w => w.Right), free[0].Y, free[0].Y, line.Size));
            }
            disordered |= items.Zip(items.Skip(1)).Any(s => GoesBack(s.First.Left, s.First.Right, s.First.Bottom, s.Second.Left, s.Second.Right, s.Second.Top, s.Second.Size));
            if (!disordered) return;
            problems.Add($"page {p.Number}: the order the words are drawn in and their place on the page disagree, so their order is uncertain; read by position");
            var ordered = p.Lines.Select(l => InOrder(l.Words) ? l : new Line([.. l.Words.OrderBy(w => w.Left).ThenBy(w => w.Right)]) { Removed = l.Removed, OwnParagraph = l.OwnParagraph, BreakBefore = l.BreakBefore, InCell = l.InCell }).ToList();
            p.Lines = ByPosition(ordered.Where(l => !l.Removed).ToList()).Concat(ordered.Where(l => l.Removed)).ToList();
        }

        /// <summary>Columns first, where a gap runs the full height of a stretch of text with lines on both sides; then top to bottom, left to right.</summary>
        static IEnumerable<Line> ByPosition(List<Line> lines)
        {
            if (lines.Count < 2) return lines;
            var size = lines.Max(l => l.Size);
            var byLeft = lines.OrderBy(l => l.Left).ThenByDescending(l => l.Y).ToList();
            var reach = byLeft[0].Right;
            for (var i = 1; i < byLeft.Count; i++)
            {
                if (byLeft[i].Left - reach >= size * ColumnGapFactor && i >= 2 && byLeft.Count - i >= 2)
                    return ByPosition(byLeft[..i]).Concat(ByPosition(byLeft[i..]));
                reach = Math.Max(reach, byLeft[i].Right);
            }
            // A line across the whole width stops columns being seen: split at the widest gap down the page and try each part.
            var byY = lines.OrderByDescending(l => l.Y).ThenBy(l => l.Left).ToList();
            var (widest, at) = (0d, 0);
            for (var i = 1; i < byY.Count; i++)
                if (byY[i - 1].Y - byY[i].Y > widest) (widest, at) = (byY[i - 1].Y - byY[i].Y, i);
            return widest > size && byY.Count > 2 ? ByPosition(byY[..at]).Concat(ByPosition(byY[at..])) : byY;
        }

        /// <summary>Bookmarks → targets, or null when there are none to use. Depth gives the level. Bad ones are listed; their children still count.</summary>
        List<Target>? Bookmarks(int pageCount)
        {
            CheckOutline();
            // Without allowing containers, PdfPig silently drops a bookmark with no destination and everything under it.
            if (!doc.TryGetBookmarks(out var bookmarks, true) || bookmarks.Roots.Count == 0) return null;
            var targets = OutlineTargets(bookmarks, pageCount, problems);
            return targets.Count > 0 ? targets : null;
        }

        /// <summary>
        /// An outline that loops, or nests absurdly deep, would overflow PdfPig's recursive walk and end the process, which no
        /// catch can stop. So the raw outline is walked first, by a loop.
        /// </summary>
        void CheckOutline()
        {
            var catalog = doc.Structure.Catalog.CatalogDictionary;
            if (!catalog.TryGet(NameToken.Outlines, out IToken outlines)) return;
            var seen = new HashSet<IndirectReference>();
            var pending = new Stack<(IToken Token, int Depth)>();
            pending.Push((outlines, 0));
            while (pending.TryPop(out var item))
            {
                var token = item.Token;
                if (token is IndirectReferenceToken reference)
                {
                    if (!seen.Add(reference.Data)) throw BadOutline();
                    token = doc.Structure.GetObject(reference.Data).Data;
                }
                if (token is not DictionaryToken node) continue;
                if (item.Depth > OutlineMaxDepth) throw BadOutline();
                if (node.TryGet(NameToken.First, out IToken first)) pending.Push((first, item.Depth + 1));
                if (node.TryGet(NameToken.Next, out IToken next)) pending.Push((next, item.Depth));
            }
        }

        static StangentException BadOutline() =>
            new("invalid_pdf", "This PDF's bookmarks loop back on themselves or nest impossibly deep: the file is damaged. Supply a fresh copy.");

        /// <summary>The printed contents → targets, or null when there's no contents page. Every entry must check out, on exactly one offset, or the import stops.</summary>
        List<Target>? PrintedContents(List<PageModel> pages)
        {
            var (entries, contentsPages, otherLists, unused) = ReadContents(pages);
            if (entries.Count == 0) return null;
            bool OnPage(string title, int number) => number >= 1 && number <= pages.Count && !contentsPages.Contains(number)
                && WholeLines(pages[number - 1].Lines, title).Count > 0;
            bool Fits((string Title, string? Lead, int Printed) e, int offset) =>
                OnPage(e.Title, e.Printed + offset) || (e.Lead is not null && OnPage($"{e.Lead} {e.Title}", e.Printed + offset));

            var votes = new Dictionary<int, int>();
            foreach (var e in entries)
                foreach (var p in pages.Where(p => OnPage(e.Title, p.Number) || (e.Lead is not null && OnPage($"{e.Lead} {e.Title}", p.Number))))
                    votes[p.Number - e.Printed] = votes.GetValueOrDefault(p.Number - e.Printed) + 1;
            var offsets = votes.Keys.Where(o => entries.All(e => Fits(e, o))).Order().ToList();
            if (offsets.Count > 1)
                throw new StangentException("no_structure", $"This PDF has no usable bookmarks, and its printed contents fits {offsets.Count} different page offsets, so which is right can't be known. {BadStructureAdvice}.");
            if (offsets.Count == 0)
            {
                var best = votes.Count == 0 ? 0 : votes.OrderByDescending(v => v.Value).ThenBy(v => Math.Abs(v.Key)).ThenBy(v => v.Key).First().Key;
                var failed = entries.Where(e => !Fits(e, best)).ToList();
                throw new StangentException("no_structure",
                    $"This PDF has no usable bookmarks, and {failed.Count} of {entries.Count} printed contents entries aren't a whole line on the page they point to (first: \"{failed[0].Title}\", page {failed[0].Printed + best}). {BadStructureAdvice}.");
            }
            var targets = new List<Target>();
            foreach (var e in entries)
            {
                var page = e.Printed + offsets[0];
                var joined = e.Lead is null ? null : $"{e.Lead} {e.Title}";
                if (joined is not null && OnPage(joined, page)) targets.Add(new Target(joined, Level(joined), page, null));
                else
                {
                    if (e.Lead is not null) problems.Add($"printed contents: the line \"{e.Lead}\" has no page number; not used as a section");
                    targets.Add(new Target(e.Title, Level(e.Title), page, null));
                }
            }
            foreach (var line in unused) problems.Add($"printed contents: the line \"{line}\" has no page number; not used as a section");
            if (otherLists > 0) problems.Add($"printed contents: {otherLists} dotted lines after the page numbers start again were read as another list and not used as sections");
            problems.Add("sections came from the printed contents, so they may be coarser than the document's real structure");
            return targets;
        }

        static int Level(string title) => FsdSections.Typed(title).Number is { } number ? number.Split('.').Length : 1;

        /// <summary>Dotted lines near the front. A title wrapped over two lines ends in the dots, so the line above is kept as a possible start of it.</summary>
        static (List<(string Title, string? Lead, int Printed)> Entries, HashSet<int> Pages, int OtherLists, List<string> Unused) ReadContents(List<PageModel> pages)
        {
            var entries = new List<(string, string?, int)>();
            var contentsPages = new HashSet<int>();
            var unused = new List<string>();
            var front = Math.Max(ContentsFrontMinPages, (int)(pages.Count * ContentsFrontShare));
            var last = 0;
            var other = 0;
            foreach (var p in pages.Take(front))
            {
                var lines = p.Lines.Where(l => !l.Removed).ToList();
                var count = lines.Count(l => ContentsEntry().IsMatch(l.Text));
                if (entries.Count == 0 && other == 0 && count < ContentsMinEntries) continue;
                if (count == 0) break;
                contentsPages.Add(p.Number);
                string? lead = null;
                for (var i = 0; i < lines.Count; i++)
                {
                    var match = ContentsEntry().Match(lines[i].Text);
                    if (!match.Success)
                    {
                        // The first line of a page is its title, not half an entry.
                        if (entries.Count > 0 && i > 0)
                        {
                            if (lead is not null) unused.Add(lead);
                            lead = lines[i].Text;
                        }
                        continue;
                    }
                    var printed = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    // Page numbers only ever rise in a contents list: when they fall, a list of tables or figures has begun.
                    if (other > 0 || printed < last) { other++; lead = null; continue; }
                    last = printed;
                    entries.Add((match.Groups[1].Value, lead, printed));
                    lead = null;
                }
                if (lead is not null) unused.Add(lead);
            }
            return (entries, contentsPages, other, unused);
        }

        /// <summary>
        /// Finds each heading among its page's lines: whole lines only, so a heading never cuts a line in two. The bookmark's own
        /// position chooses between several matches; without one, the first is taken and listed. One not found is listed, the text stays as it is,
        /// and the heading is placed where the page's remaining text starts.
        /// </summary>
        void PlaceHeadings(List<PageModel> pages, List<Target> targets)
        {
            foreach (var group in targets.GroupBy(t => t.Page).OrderBy(g => g.Key))
            {
                var page = pages[group.Key - 1];
                var used = new HashSet<int>();
                var anchors = new List<Anchor>();
                var next = 0;
                foreach (var target in group)
                {
                    var matches = WholeLines(page.Lines, target.Title).Where(m => !Enumerable.Range(m.Start, m.End - m.Start + 1).Any(used.Contains)).ToList();
                    if (matches.Count == 0)
                    {
                        problems.Add($"page {page.Number}: the section title \"{target.Title}\" isn't a whole line on its page; its heading is placed where the page's remaining text starts, and the text is left as it is");
                        anchors.Add(new Anchor(target, next, next, null));
                        continue;
                    }
                    var match = matches[0];
                    if (target.Top is { } top && !page.Turned)
                    {
                        // A destination names a point at or just above the heading's top, so its heading is the nearest match below that.
                        var size = page.Lines[match.Start].Size;
                        var below = matches.Where(m => page.Lines[m.Start].Y <= top + size).ToList();
                        match = (below.Count > 0 ? below : matches).OrderBy(m => Math.Abs(page.Lines[m.Start].Y - top)).ThenBy(m => m.Start).First();
                    }
                    else if (matches.Count > 1)
                        problems.Add($"page {page.Number}: the section title \"{target.Title}\" is on {matches.Count} whole lines and nothing says which is the heading; the first was taken");
                    if (anchors.Count > 0 && match.Start < next)
                        problems.Add($"page {page.Number}: the section title \"{target.Title}\" is above the one listed before it; placed where it is");
                    anchors.Add(new Anchor(target, match.Start, match.End, match.Text));
                    next = match.End + 1;
                    foreach (var i in Enumerable.Range(match.Start, match.End - match.Start + 1)) used.Add(i);
                    // A title inside a grid means the grid is layout around the document's structure, not a table.
                    var heading = page.Lines.Skip(match.Start).Take(match.End - match.Start + 1).SelectMany(l => l.Words).ToList();
                    foreach (var table in heading.Select(w => w.Table).OfType<PdfTable>().Distinct().ToList())
                    {
                        problems.Add($"page {page.Number}: a table holds the section title \"{target.Title}\"; kept as plain text");
                        foreach (var word in page.Lines.SelectMany(l => l.Words).Where(w => ReferenceEquals(w.Table, table))) word.Table = null;
                        foreach (var image in page.Images.Where(i => i.Cell is not null && table.Cells.Contains(i.Cell))) image.Cell = null;
                    }
                }
                page.Anchors = [.. anchors.OrderBy(a => a.Start)];
            }
        }

        /// <summary>The usual distance between lines, as a share of font size: spacing differs by document, so "a bigger gap" must be measured against it.</summary>
        static double UsualPitch(List<PageModel> pages)
        {
            var counts = new Dictionary<int, int>();
            foreach (var p in pages.Where(p => p.Aligned))
            {
                var lines = p.Lines.Where(l => !l.Removed).ToList();
                for (var i = 1; i < lines.Count; i++)
                {
                    var (a, b) = (lines[i - 1], lines[i]);
                    if (b.Y >= a.Y || b.Size <= 0 || Math.Abs(a.Size - b.Size) > PitchBucket) continue;
                    var bucket = (int)Math.Round((a.Y - b.Y) / b.Size / PitchBucket);
                    counts[bucket] = counts.GetValueOrDefault(bucket) + 1;
                }
            }
            return counts.Count == 0 ? 0 : counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key).First().Key * PitchBucket;
        }

        /// <summary>
        /// The emitted words must be the page's own, minus running lines, or the page drops to plain text, which is checked too.
        /// If that still fails, the page is its lines one by one, which can't repeat a word. Where the two readings of the page differ, it's plain and said so.
        /// </summary>
        List<PdfBlock> CheckedBlocks(PageModel p, double pitch)
        {
            var own = OwnWords(p.Own);
            foreach (var word in p.Lines.Where(l => l.Removed).SelectMany(l => l.Words)) own.Remove(word.Text);
            if (!p.ReadingsAgree)
                problems.Add($"page {p.Number}: reading its words by position gives different words than reading them as drawn, so words on this page may be wrong; kept as plain text as drawn");
            var plain = !p.Aligned || !p.ReadingsAgree;
            var blocks = Blocks(p, pitch, plain);
            if (tamper is not null) blocks = tamper(p.Number, blocks);
            if (SameWords(own, EmittedWords(blocks))) return blocks;
            problems.Add($"page {p.Number}: the words laid out didn't match the page's own words; kept as plain text");
            blocks = Blocks(p, pitch, plain: true);
            if (tamper is not null) blocks = tamper(p.Number, blocks);
            if (SameWords(own, EmittedWords(blocks))) return blocks;
            problems.Add($"page {p.Number}: its plain text didn't match either; only its own lines are kept, and its headings go first");
            return [.. p.Anchors.Select(a => new PdfBlock(a.Target.Title, "", Kind: PdfBlockKind.Heading, Level: a.Target.Level)),
                .. p.Lines.Where(l => !l.Removed).Select(l => new PdfBlock("", l.Text))];
        }

        static IEnumerable<string> EmittedWords(IEnumerable<PdfBlock> blocks) =>
            blocks.SelectMany(b => b.Kind == PdfBlockKind.Table ? b.Rows!.SelectMany(r => r) : b.Kind == PdfBlockKind.Image ? [] : [b.Text])
                .SelectMany(t => t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        List<PdfBlock> Blocks(PageModel p, double pitch, bool plain)
        {
            var blocks = new List<PdfBlock>();
            var visible = p.Lines.Where(l => !l.Removed).ToList();
            var rightEdge = visible.Count > 0 ? visible.Max(l => l.Right) : 0;
            var images = new Queue<Image>(p.Images.Where(i => plain || i.Cell is null));
            var tablesDone = new HashSet<PdfTable>();
            var anchors = new Queue<Anchor>(p.Anchors);
            List<PdfWord>? para = null;
            var listLevels = new List<double>();
            string prefix = "";
            double itemLeft = 0;
            Line? previous = null;
            Line? firstLine = null;
            // The body's left edge on this page: the most lines start there.
            var bodyLeft = visible.Count > 0 ? visible.GroupBy(l => Math.Round(l.Left)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key : 0;
            bool AtBody(Line l) => !l.InCell && Math.Abs(l.Size - bodySize) <= bodySize * PitchBucket && Math.Abs(l.Left - bodyLeft) <= l.Size * SpaceShare;

            void Flush()
            {
                if (para is not null)
                    blocks.Add(new PdfBlock(prefix, string.Join(" ", para.Select(w => w.Text)), prefix.Length > 0 || itemLeft > 0,
                        StartsAtBody: AtBody(firstLine!), EndsAtBody: AtBody(previous!)));
                para = null;
                previous = null;
            }
            void EmitImagesAbove(double y)
            {
                while (images.TryPeek(out var image) && (image.Top + image.Bottom) / 2 > y)
                {
                    Flush();
                    images.Dequeue();
                    blocks.Add(new PdfBlock("", image.Name, Kind: PdfBlockKind.Image));
                }
            }
            void AddHeading(Anchor a)
            {
                Flush();
                listLevels.Clear();
                // A title not found on the page isn't one of its words, so it's only Markdown.
                blocks.Add(new PdfBlock(a.Found is null ? a.Target.Title : "", a.Found ?? "", Kind: PdfBlockKind.Heading, Level: a.Target.Level));
            }

            for (var i = 0; i < p.Lines.Count; i++)
            {
                var consumed = false;
                while (anchors.TryPeek(out var a) && a.Start <= i)
                {
                    anchors.Dequeue();
                    if (!p.Lines[i].Removed) EmitImagesAbove(p.Lines[i].Y);
                    AddHeading(a);
                    if (a.Found is null) continue;
                    i = a.End;
                    consumed = true;
                    break;
                }
                if (consumed || p.Lines[i].Removed) continue;

                var line = p.Lines[i];
                EmitImagesAbove(line.Y);
                var kept = new List<PdfWord>();
                foreach (var word in line.Words)
                {
                    if (!plain && word.Table is { } table)
                    {
                        if (tablesDone.Add(table))
                        {
                            Flush();
                            listLevels.Clear();
                            blocks.Add(TableBlock(table));
                        }
                    }
                    else kept.Add(word);
                }
                if (kept.Count == 0) continue;
                var piece = new Line(kept) { OwnParagraph = plain || line.OwnParagraph, BreakBefore = line.BreakBefore, InCell = line.InCell };
                var continues = para is not null && !Breaks(previous!, piece, pitch, rightEdge);
                var label = ReadLabel(piece, InSequence(p, i), continues && !EndsSentence(previous!.Text));
                if (continues && label is null)
                {
                    para!.AddRange(kept);
                    previous = piece;
                    continue;
                }
                Flush();
                if (label is null)
                {
                    listLevels.Clear();
                    (prefix, itemLeft, para, previous, firstLine) = ("", 0, kept, piece, piece);
                    continue;
                }
                var tolerance = piece.Size * SpaceShare;
                while (listLevels.Count > 0 && listLevels[^1] > piece.Left + tolerance) listLevels.RemoveAt(listLevels.Count - 1);
                if (listLevels.Count == 0 || piece.Left > listLevels[^1] + tolerance) listLevels.Add(piece.Left);
                var indent = new string(' ', 2 * (listLevels.Count - 1));
                (prefix, itemLeft, para, previous, firstLine) = (indent + label.Value.Prefix, Math.Max(piece.Left, 1), [.. label.Value.Words], piece, piece);
            }
            while (anchors.TryDequeue(out var a)) AddHeading(a);
            Flush();
            EmitImagesAbove(double.NegativeInfinity);
            return blocks;
        }

        /// <summary>Whether the nearest marker before or after this one, at the same left edge and on this page or the next one along, continues its sequence (5 → 6, a → b).</summary>
        static bool InSequence(PageModel p, int i)
        {
            var line = p.Lines[i];
            if (line.Words.Count < 2 || !IsLabel(line.Words[0].Text)) return false;
            var tolerance = line.Size * SpaceShare;
            string? Nearest(IEnumerable<Line> from) => from
                .Where(l => !l.Removed && l.Words.Count >= 2 && IsLabel(l.Words[0].Text) && Math.Abs(l.Words[0].Left - line.Words[0].Left) <= tolerance)
                .Select(l => l.Words[0].Text).FirstOrDefault();
            var before = p.Lines.Take(i).Reverse().Concat(Enumerable.Reverse(p.Before?.Lines ?? []));
            var after = p.Lines.Skip(i + 1).Concat(p.After?.Lines ?? []);
            return Nearest(before) is { } b && Follows(b, line.Words[0].Text) || Nearest(after) is { } a && Follows(line.Words[0].Text, a);
        }

        /// <summary>"5." → "6.", "a)" → "b)", and a dash after a dash.</summary>
        internal static bool Follows(string marker, string next)
        {
            if (marker == "-" || next == "-") return marker == next;
            var (a, b) = (LabelCore().Match(marker), LabelCore().Match(next));
            if (!a.Success || !b.Success || a.Groups[1].Value != b.Groups[1].Value || a.Groups[3].Value != b.Groups[3].Value) return false;
            var (x, y) = (a.Groups[2].Value, b.Groups[2].Value);
            return int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m == n + 1
                : x.Length == 1 && y.Length == 1 && y[0] == x[0] + 1 && char.IsLetter(y[0]);
        }

        /// <summary>
        /// A list item's Markdown prefix and words, or null. A real bullet glyph becomes "- ". A typed label ("1.", "a)", "-") counts only
        /// when it continues the sequence of the marker before or after it, or it sits apart from its text and the line before isn't an unfinished sentence: a justified gap can make the end of a sentence look like a label.
        /// </summary>
        static (string Prefix, List<PdfWord> Words)? ReadLabel(Line line, bool inSequence, bool afterUnfinishedSentence)
        {
            var first = line.Words[0].Text;
            if (IsBullet(first[0]))
            {
                var rest = first.TrimStart(BulletsIn(first));
                var words = line.Words.Skip(1).ToList();
                if (rest.Length > 0) words.Insert(0, new PdfWord(rest, line.Left, line.Words[0].Right, line.Words[0].FirstLetterMiddle, line.Y, line.Size));
                return words.Count > 0 ? ("- ", words) : null;
            }
            if (line.Words.Count < 2 || !IsLabel(first)) return null;
            var tolerance = line.Size * SpaceShare;
            var apart = line.Words[1].Left - line.Words[0].Right > tolerance * LabelApartSpaces;
            if (!inSequence && (!apart || afterUnfinishedSentence)) return null;
            if (first == "-") return ("", line.Words);
            return (first[^1] == '.' ? "" : "- ", line.Words);
        }

        static bool IsLabel(string word) => word == "-" || ListLabel().IsMatch(word);

        static char[] BulletsIn(string word) => [.. word.Where(IsBullet).Distinct()];

        static bool Breaks(Line a, Line b, double pitch, double rightEdge)
        {
            if (a.OwnParagraph || b.OwnParagraph || b.BreakBefore || a.InCell != b.InCell || b.Y >= a.Y) return true;
            if (StartsWithSymbol(b.Words[0].Text)) return true;
            if (Math.Abs(a.Size - b.Size) > b.Size * PitchBucket) return true;
            if (a.Y - b.Y > b.Size * pitch * ParagraphGapFactor) return true;
            if (ContentsEntry().IsMatch(a.Text) || ContentsEntry().IsMatch(b.Text)) return true;
            // A sentence that ended on a line with room for the next word was ended on purpose. Without an end mark, room proves nothing: one wide line elsewhere on the page gives every line room.
            if (EndsSentence(a.Text) && rightEdge - a.Right > b.Words[0].Right - b.Words[0].Left + b.Size * SpaceShare) return true;
            return false;
        }

        static PdfBlock TableBlock(PdfTable table)
        {
            var rows = Enumerable.Range(0, table.Rows).Select(_ => new string[table.Cols].Select(_ => "").ToList()).ToList();
            foreach (var cell in table.Cells) rows[cell.Row][cell.Col] = cell.Text;
            var images = table.Cells.Where(c => c.Images.Count > 0).ToDictionary(c => (c.Row, c.Col), c => string.Join(" ", c.Images.Select(n => $"![]({n})")));
            var sb = new StringBuilder();
            for (var r = 0; r < rows.Count; r++)
            {
                var shown = rows[r].Select((text, c) => images.TryGetValue((r, c), out var i) ? $"{i} {text}".Trim() : text).Select(t => t.Replace("|", "\\|"));
                sb.Append("| ").Append(string.Join(" | ", shown)).Append(" |\n");
                if (r == 0) sb.Append('|').Append(string.Concat(Enumerable.Repeat("---|", table.Cols))).Append('\n');
            }
            return new PdfBlock("", sb.ToString().TrimEnd('\n'), Kind: PdfBlockKind.Table, Rows: rows);
        }

        /// <summary>Writes the pages' blocks. A paragraph runs on over a page break only on strong evidence: body text at the body's size and left edge, no end mark, and the next page starting in lower case.</summary>
        static List<int> Write(List<PdfBlock> blocks, MarkdownText md, List<FsdHeading> headings)
        {
            var joins = new List<int>();
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                switch (b.Kind)
                {
                    case PdfBlockKind.Heading:
                        var text = b.Prefix + b.Text;
                        var start = md.Length;
                        md.Emit($"{new string('#', b.Level)} {text}");
                        var (number, shown) = FsdSections.Typed(text);
                        headings.Add(new FsdHeading(number, shown, b.Level, md.Line, start, md.Length, number is not null, text));
                        break;
                    case PdfBlockKind.Image: md.Emit($"![]({b.Text})"); break;
                    case PdfBlockKind.Table: md.Emit(b.Text); break;
                    default:
                        var joined = b.Text;
                        var current = b;
                        while (!b.Tight && current.EndsPage && i + 1 < blocks.Count && blocks[i + 1] is { Kind: PdfBlockKind.Paragraph, Tight: false } next && next.Text.Length > 0
                               && current.EndsAtBody && next.StartsAtBody && !EndsSentence(joined) && !ContentsEntry().IsMatch(current.Text) && char.IsLower(next.Text[0]))
                        {
                            joins.Add(current.Page);
                            joined += " " + next.Text;
                            current = next;
                            i++;
                        }
                        md.Emit(b.Prefix + joined, b.Tight);
                        break;
                }
            }
            return joins;
        }
    }

    /// <summary>Bookmarks flattened in document order. Internal so a test can give it a page no builder would write.</summary>
    internal static List<(string Title, int Level, int Page)> OutlineTargetsForTest(Bookmarks bookmarks, int pageCount, List<string> problems) =>
        [.. OutlineTargets(bookmarks, pageCount, problems).Select(t => (t.Title, t.Level, t.Page))];

    static List<Target> OutlineTargets(Bookmarks bookmarks, int pageCount, List<string> problems)
    {
        var targets = new List<Target>();
        void Walk(BookmarkNode node, int depth)
        {
            var title = Normal(node.Title ?? "");
            if (node is DocumentBookmarkNode d && d.PageNumber >= 1 && d.PageNumber <= pageCount && title.Length > 0)
                targets.Add(new Target(title, depth + 1, d.PageNumber, d.Destination.Type == ExplicitDestinationType.XyzCoordinates ? d.Destination.Coordinates.Top : null));
            else problems.Add($"bookmark \"{title}\" has no page inside the document; not used");
            foreach (var child in node.Children) Walk(child, depth + 1);
        }
        foreach (var root in bookmarks.Roots) Walk(root, 0);
        // Sections follow the document, so a bookmark out of order goes where its page is.
        for (var i = 1; i < targets.Count; i++)
            if (targets[i].Page < targets[i - 1].Page)
                problems.Add($"bookmark \"{targets[i].Title}\" points to page {targets[i].Page}, before the one above it (page {targets[i - 1].Page}); placed by its page");
        return [.. targets.OrderBy(t => t.Page)];
    }

    /// <summary>
    /// Every place a title is one whole line, or whole lines in a row when it wraps. White space and case don't count. A title that's
    /// only the start of a line isn't there: it would cut the sentence in two.
    /// </summary>
    static List<(int Start, int End, string Text)> WholeLines(List<Line> lines, string title)
    {
        var found = new List<(int, int, string)>();
        var key = Squash(title);
        if (key.Length == 0) return found;
        for (var i = 0; i < lines.Count; i++)
        {
            var s = "";
            for (var j = i; j < lines.Count && !lines[j].Removed && s.Length < key.Length; j++)
            {
                s += lines[j].Key;
                if (!key.StartsWith(s, StringComparison.Ordinal)) break;
                if (s.Length == key.Length) found.Add((i, j, string.Join(" ", lines.Skip(i).Take(j - i + 1).Select(l => l.Text))));
            }
        }
        return found;
    }

    static string Squash(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    static string Normal(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>End marks, looking past a closing bracket or quote.</summary>
    static bool EndsSentence(string text) => text.TrimEnd(')', ']', '"', '\'', '”', '’') is { Length: > 0 } t && ".!?:;。！？".Contains(t[^1]);

    /// <summary>Only clear bullets: a tick, an empty box or an empty circle can mean something, so they stay as drawn. U+F0B7 and U+F0A7 are the Symbol and Wingdings bullets.</summary>
    internal static bool IsBullet(char c) => "•‣◦▪●∙⁃·\uF0B7\uF0A7".Contains(c);

    /// <summary>A line starting with some other symbol starts a new line: "✓ Admin" and "□ Guest" aren't one sentence.</summary>
    static bool StartsWithSymbol(string word) => !IsBullet(word[0]) && char.GetUnicodeCategory(word[0]) is UnicodeCategory.OtherSymbol or UnicodeCategory.PrivateUse;

    /// <summary>The extractor's own rule, over the letters a page reads: it only takes a whole page, with no way to leave letters out or turn them.</summary>
    internal static string ReadText(List<Glyph> letters)
    {
        static bool NewLine(Glyph a, Glyph b) =>
            Math.Abs(a.Y - b.Y) > SameLineShare * Math.Min((int)Math.Round(a.Size), (int)Math.Round(b.Size));
        var sb = new StringBuilder();
        Glyph? previous = null;
        Glyph? lastInk = null;
        var justSpaced = false;
        foreach (var letter in letters)
        {
            if (letter.Value == " " && !justSpaced)
            {
                if (previous is not null && NewLine(previous, letter)) continue;
                sb.Append(' ');
                previous = letter;
                justSpaced = true;
                continue;
            }
            justSpaced = false;
            if (previous is not null && letter.Value != " ")
            {
                if (lastInk is not null && NewLine(lastInk, letter))
                {
                    if (previous.Value == " ") sb.Remove(sb.Length - 1, 1);
                    sb.Append('\n');
                    justSpaced = true;
                }
                else if (previous.Value != " " && WhitespaceSizeStatistics.IsProbablyWhitespace(Math.Abs(letter.Start - previous.End), previous.Source))
                {
                    sb.Append(' ');
                    justSpaced = true;
                }
            }
            sb.Append(letter.Value);
            previous = letter;
            if (!string.IsNullOrWhiteSpace(letter.Value)) lastInk = letter;
        }
        return sb.ToString();
    }

    /// <summary>A whole page by <see cref="ReadText"/>, so a test can hold it to the extractor's output.</summary>
    internal static string ReadTextForTest(Page page) =>
        ReadText([.. page.Letters.Where(l => !string.IsNullOrEmpty(l.Value)).Select(l => new Glyph(l.Value, l.StartBaseLine.X, l.EndBaseLine.X, l.StartBaseLine.Y, l.PointSize, l))]);

    /// <summary>Characters from a symbol font's private range, or the replacement character: glyphs with no standard letter.</summary>
    internal static int CountUnmapped(IEnumerable<char> chars) =>
        chars.Count(c => c == '�' || (char.GetUnicodeCategory(c) == UnicodeCategory.PrivateUse && !IsBullet(c)));

    /// <summary>The same words, any order, ignoring only the bullet glyphs that list items turn into Markdown.</summary>
    internal static bool SameWords(IEnumerable<string> a, IEnumerable<string> b)
    {
        static List<string> Clean(IEnumerable<string> words)
        {
            var list = words.Select(w => w.TrimStart(BulletChars(w))).Where(w => w.Length > 0).ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }
        return Clean(a).SequenceEqual(Clean(b), StringComparer.Ordinal);
    }

    static char[] BulletChars(string word) => [.. word.Where(IsBullet).Distinct()];
}
