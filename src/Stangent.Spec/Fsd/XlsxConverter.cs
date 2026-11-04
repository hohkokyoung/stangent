using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>One sheet of a workbook as a CSV file: line N is row N of the sheet, so a citation's row can be read off it. A hidden row is an empty line.</summary>
public sealed record SheetCsv(string Sheet, string Csv);

/// <summary>
/// <c>.xlsx</c> and <c>.xlsm</c> → Markdown, deterministically, with the Open XML SDK (D111, D123). Each visible sheet is a level-1 section titled with its name, in workbook order, and
/// holds one table. Headings inside a sheet are never guessed. What a cell stores is what's shown: a number exactly as stored, a date by a built-in date format as an ISO date,
/// a formula's cached value. What isn't shown is listed in <c>problems</c>, and each cell's text is checked against the table by a second pass.
/// </summary>
public static partial class XlsxConverter
{
    /// <summary>A sheet whose rows times columns come to more than this stops the import: it takes a hostile or broken file to hold one, and each row of a table is written out whole. A stated limit, not a measured one.</summary>
    public const long MaxSheetCells = 20_000_000;

    [GeneratedRegex(@"^\$?([A-Za-z]{1,3})\$?(\d{1,7})$")]
    private static partial Regex CellRef();

    [GeneratedRegex(@"_x([0-9A-Fa-f]{4})_")]
    private static partial Regex Escaped();

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex Bar();

    static readonly HashSet<string> RevisionElements = ["rrc", "rm", "rcv", "rsnm", "ris", "rcc", "rfmt", "raf", "rdn", "rcmt", "rqt", "rcft"];

    static StangentException Corrupt(string detail) =>
        new("invalid_xlsx", "This workbook can't be opened: it may be damaged. Supply a copy saved again from Excel.", ExitCodes.Failed, [detail]);

    /// <summary>A workbook locked with a password is an encrypted package inside an OLE container, not a zip. Protection is never worked around, so this says how to get a copy that can be read.</summary>
    public static void CheckLocked(byte[] file)
    {
        if (!OleContainer.IsOle(file) || file.AsSpan().IndexOf(Encoding.Unicode.GetBytes("EncryptedPackage")) < 0) return;
        throw new StangentException("xlsx_locked",
            "This workbook is locked with a password, so it can't be read, and stangent never gets round protection. To import it: open it in Excel, choose File > Info > Protect Workbook > Encrypt with Password, clear the password, save a copy, and import that copy. If you can't open it, ask whoever sent it for an unlocked copy.",
            ExitCodes.Failed);
    }

    static T Open<T>(byte[] file, Func<SpreadsheetDocument, T> read)
    {
        try
        {
            using var stream = new MemoryStream(file);
            using var doc = SpreadsheetDocument.Open(stream, false);
            return read(doc);
        }
        catch (Exception e) when (e is FileFormatException or OpenXmlPackageException or InvalidDataException or XmlException or FormatException or ArgumentException)
        {
            throw Corrupt(e.Message);
        }
    }

    /// <summary>The import's safety step: a file that can't be opened, or holds revisions nobody settled, stops here. Gives how many revisions it holds.</summary>
    public static int CheckSafe(byte[] file, bool acceptChanges) => Open(file, doc =>
    {
        var part = doc.WorkbookPart ?? throw Corrupt("It has no workbook part.");
        var revisions = Revisions(part).Count;
        if (revisions > 0 && !acceptChanges)
            throw new StangentException("xlsx_tracked_changes",
                $"This is a shared workbook holding {revisions} recorded revisions, and nobody has settled them. Its cells hold every revision as saved, with no earlier state to compare them to. In Excel, use Review > Track Changes > Accept/Reject Changes and save a copy, or use --accept-changes to read the cells as saved.",
                ExitCodes.Failed);
        return revisions;
    });

    /// <summary>The recorded changes in a shared workbook's revision logs.</summary>
    static List<XElement> Revisions(WorkbookPart part) =>
        [.. (part.WorkbookRevisionHeaderPart?.WorkbookRevisionLogParts ?? []).SelectMany(l => Read(l).Elements().Where(e => RevisionElements.Contains(e.Name.LocalName)))];

    static XElement Read(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root ?? throw Corrupt($"{part.Uri} is empty.");
    }

    public static ConvertedDocument Convert(byte[] file, bool acceptChanges = false, int largeSheets = 50, int largeRows = 100000) => Open(file, doc =>
    {
        var workbook = doc.WorkbookPart ?? throw Corrupt("It has no workbook part.");
        return new Reader(workbook, largeSheets, largeRows).Run(acceptChanges);
    });

    /// <summary>A cell's letters and number: <c>B12</c> → column 2, row 12. Null for anything else.</summary>
    internal static (int Row, int Column)? ParseRef(string text)
    {
        var m = CellRef().Match(text);
        if (!m.Success || !int.TryParse(m.Groups[2].Value, out var row) || row < 1) return null;
        var column = 0;
        foreach (var c in m.Groups[1].Value) column = column * 26 + char.ToUpperInvariant(c) - 'A' + 1;
        return (row, column);
    }

    internal static string Name(int row, int column)
    {
        var letters = "";
        for (var c = column; c > 0; c = (c - 1) / 26) letters = (char)('A' + (c - 1) % 26) + letters;
        return letters + row;
    }

    /// <summary>
    /// A stored serial number as an ISO date, the way the workbook's calendar counts it. Null when it isn't a real date: negative, too large, or day 60 of the 1900 calendar, which is
    /// the 29 February 1900 that Excel counts and no calendar has. A time-only format with no day part gives the time alone.
    /// </summary>
    internal static string? IsoDate(string stored, bool date1904, bool timeOnly)
    {
        if (!decimal.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) || serial < 0) return null;
        var days = decimal.Floor(serial);
        var seconds = (int)decimal.Round((serial - days) * 86400m, MidpointRounding.AwayFromZero);
        if (seconds == 86400) { days++; seconds = 0; }
        if (days > 3_000_000) return null;
        if (timeOnly && days == 0) return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        if (!date1904 && days == 60) return null;
        if (!date1904 && days == 0) return null;
        var start = date1904 ? new DateTime(1904, 1, 1) : days < 60 ? new DateTime(1899, 12, 31) : new DateTime(1899, 12, 30);
        var date = start.AddDays((double)days);
        if (date.Year > 9999) return null;
        return seconds == 0 ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.AddSeconds(seconds).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    static bool IsDateFormat(int id) => id is >= 14 and <= 17 or 22 or >= 27 and <= 36 or >= 50 and <= 58;
    static bool IsTimeFormat(int id) => id is >= 18 and <= 21 or 45 or 47;

    /// <summary>One line of a table cell's text, as it's rendered and as a quote will match it.</summary>
    static string Clean(string text)
    {
        var decoded = Escaped().Replace(text, m => ((char)System.Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        return LineBreaks().Replace(decoded, " ").Trim();
    }

    static string TextOf(XElement e) =>
        string.Concat(e.Descendants().Where(d => d.Name.LocalName == "t" && d.Ancestors().All(a => a.Name.LocalName != "rPh")).Select(d => d.Value));

    static string? Attr(XElement e, string name) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(c => c.Name.LocalName == name);
    static IEnumerable<XElement> Children(XElement? e, string name) => e?.Elements().Where(c => c.Name.LocalName == name) ?? [];
    static bool On(string? value) => value is "1" or "true";

    /// <summary>Which of a section's stored cells the rendered table doesn't hold where it should: anything listed as left out is accounted for (D123). Each row is read back from the rendered text, so this checks the table, not the walk that made it.</summary>
    internal static List<string> Unaccounted(IReadOnlyDictionary<(int Row, int Column), string> stored, IReadOnlySet<(int Row, int Column)> leftOut, IReadOnlyList<int> rows, IReadOnlyList<int> columns, string rendered)
    {
        var lines = rendered.Length == 0 ? [] : rendered.Split('\n').Where((_, i) => i != 1).ToList();
        string At(int row, int column)
        {
            var r = IndexOf(rows, row);
            var c = IndexOf(columns, column);
            if (r < 0 || c < 0 || r >= lines.Count) return "";
            var cells = Bar().Split(lines[r]);
            return c + 1 < cells.Length ? cells[c + 1].Trim() : "";
        }
        return [.. stored.Where(s => s.Value.Length > 0 && !leftOut.Contains(s.Key) && At(s.Key.Row, s.Key.Column) != s.Value.Replace("|", "\\|", StringComparison.Ordinal))
            .OrderBy(s => s.Key).Select(s => Name(s.Key.Row, s.Key.Column))];
    }

    static int IndexOf(IReadOnlyList<int> list, int value)
    {
        var (low, high) = (0, list.Count - 1);
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (list[mid] == value) return mid;
            if (list[mid] < value) low = mid + 1; else high = mid - 1;
        }
        return -1;
    }

    sealed class Reader(WorkbookPart workbook, int largeSheets, int largeRows)
    {
        readonly List<string> problems = [];
        readonly List<FileInDocument> files = [];
        readonly List<SheetCsv> csvs = [];
        readonly List<FsdHeading> headings = [];
        readonly MarkdownText md = new();
        List<string> shared = [];
        readonly Dictionary<int, string> customFormats = [];
        List<int> cellFormats = [];
        bool date1904;
        int textCells;

        public ConvertedDocument Run(bool acceptChanges)
        {
            var book = Read(workbook);
            date1904 = On(Attr(Child(book, "workbookPr") ?? new XElement("none"), "date1904"));
            LoadStrings();
            LoadStyles();
            Revisions(acceptChanges);
            if (Child(book, "workbookProtection") is not null) problems.Add("workbook: its structure is protected; it's readable, and read as stored");

            var sheets = Children(Child(book, "sheets"), "sheet").ToList();
            var worksheets = 0;
            foreach (var sheet in sheets)
            {
                var name = Attr(sheet, "name") ?? throw Corrupt("A sheet has no name.");
                var state = Attr(sheet, "state");
                var part = Attr(sheet, "id") is { } rid ? workbook.Parts.FirstOrDefault(p => p.RelationshipId == rid).OpenXmlPart : null;
                if (part is WorksheetPart worksheet)
                {
                    worksheets++;
                    if (state is "hidden" or "veryHidden") HiddenSheet(name, state, worksheet);
                    else Sheet(name, worksheet);
                }
                else problems.Add(part is ChartsheetPart
                    ? $"workbook: chart sheet \"{name}\" not read{(state is "hidden" or "veryHidden" ? $" ({state})" : "")}"
                    : $"workbook: sheet \"{name}\" isn't a worksheet (a macro or dialog sheet, or missing), so it's not read");
            }
            if (worksheets > largeSheets) problems.Add($"workbook: {worksheets} sheets, over fsd.excel.largeSheets ({largeSheets})");
            Extras();
            if (textCells == 0)
                throw new StangentException("no_text", "This workbook has no visible text: no visible sheet holds a cell with anything in it. Whatever hidden sheets, rows or columns hold is never read in their place.", ExitCodes.Failed);

            var body = md.ToString();
            var sections = FsdSections.Build(body, headings, problems);
            return new ConvertedDocument(body + "\n", sections, [], problems, null) { Files = files, Sheets = csvs };
        }

        void LoadStrings()
        {
            if (workbook.SharedStringTablePart is not { } part) return;
            shared = [.. Children(Read(part), "si").Select(TextOf)];
        }

        void LoadStyles()
        {
            if (workbook.WorkbookStylesPart is not { } part) return;
            var styles = Read(part);
            foreach (var f in Children(Child(styles, "numFmts"), "numFmt"))
                if (int.TryParse(Attr(f, "numFmtId"), out var id)) customFormats[id] = Attr(f, "formatCode") ?? "";
            cellFormats = [.. Children(Child(styles, "cellXfs"), "xf").Select(x => int.TryParse(Attr(x, "numFmtId"), out var id) ? id : 0)];
        }

        void Revisions(bool acceptChanges)
        {
            if (workbook.WorkbookRevisionHeaderPart is null) return;
            var count = XlsxConverter.Revisions(workbook).Count;
            problems.Add(count > 0
                ? $"--accept-changes: {count} revisions in a shared workbook read as saved; this is the cells as they stand, not an agreed document"
                : "shared workbook: it has a revision history with no change the file can show, so there's no accepted state to compare; its cells are read as saved");
            _ = acceptChanges;
        }

        void HiddenSheet(string name, string state, WorksheetPart part)
        {
            var count = Part(Read(part)).Count(c => c.Value.Text.Length > 0);
            problems.Add($"hidden sheet \"{name}\" ({state}): left out, with {count} cells holding text");
            Embedded(part, null, $"hidden sheet \"{name}\"");
        }

        void Sheet(string name, WorksheetPart part)
        {
            var root = Read(part);
            var cells = Part(root);
            var stored = cells.ToDictionary(c => c.Key, c => c.Value.Text);
            var grid = new Dictionary<(int Row, int Column), string>(stored);
            var hiddenRows = Children(Child(root, "sheetData"), "row").Where(r => On(Attr(r, "hidden"))).Select(RowNumber).OfType<int>().ToHashSet();
            var hiddenColumns = HiddenColumns(root);
            var lines = new List<string>();
            var leftOut = new HashSet<(int Row, int Column)>();
            Merges(root, name, stored, grid, leftOut, lines);

            foreach (var c in stored.Keys.Where(c => hiddenRows.Contains(c.Row) || hiddenColumns.Contains(c.Column))) leftOut.Add(c);
            var visible = grid.Where(g => g.Value.Length > 0 && !hiddenRows.Contains(g.Key.Row) && !hiddenColumns.Contains(g.Key.Column)).ToList();
            textCells += visible.Count;

            var rows = new List<int>();
            var columns = new List<int>();
            var text = "";
            if (visible.Count > 0)
            {
                var (top, bottom) = (visible.Min(v => v.Key.Row), visible.Max(v => v.Key.Row));
                var (left, right) = (visible.Min(v => v.Key.Column), visible.Max(v => v.Key.Column));
                if ((long)(bottom - top + 1) * (right - left + 1) > MaxSheetCells)
                    throw new StangentException("xlsx_too_large", $"Sheet \"{name}\" spans {bottom - top + 1} rows by {right - left + 1} columns, over the limit of {MaxSheetCells} cells, so it wasn't read.", ExitCodes.Failed);
                rows = [.. Enumerable.Range(top, bottom - top + 1).Where(r => !hiddenRows.Contains(r))];
                columns = [.. Enumerable.Range(left, right - left + 1).Where(c => !hiddenColumns.Contains(c))];
                text = Table(grid, rows, columns);
                csvs.Add(new SheetCsv(name, Csv(grid, bottom, hiddenRows, columns)));
                if (rows.Count > largeRows) lines.Add($"{rows.Count} rows, over fsd.excel.largeRows ({largeRows})");
            }
            else lines.Add("no visible text");

            var start = md.Length > 0 ? md.Length + 2 : 0;
            var heading = $"# {name}";
            md.Emit(heading);
            headings.Add(new FsdHeading(null, name, 1, md.Line, start, start + heading.Length, false, name));
            if (text.Length > 0) md.Emit(text);

            Formulas(root, hiddenRows, hiddenColumns, lines);
            CustomFormats(cells, hiddenRows, hiddenColumns, lines);
            lines.AddRange(cells.Where(c => c.Value.Note is not null).Select(c => $"{Name(c.Key.Row, c.Key.Column)}: {c.Value.Note}"));
            Hidden(hiddenRows, hiddenColumns, stored, lines);
            if (Child(root, "sheetProtection") is { } lockSheet && Attr(lockSheet, "sheet") is not ("0" or "false"))
                lines.Add("is protected; it's readable, and read as stored");
            NotRead(root, part, lines);
            problems.AddRange(lines.Select(l => $"sheet \"{name}\": {l}"));
            Comments(name, part);
            Embedded(part, name, $"sheet \"{name}\"");

            foreach (var cell in Unaccounted(stored, leftOut, rows, columns, text))
                problems.Add($"completeness: sheet \"{name}\": cell {cell}'s text isn't in the table, and no reason is listed");
        }

        static int? RowNumber(XElement row) => int.TryParse(Attr(row, "r"), out var n) ? n : null;

        /// <summary>Every cell of a sheet, by row and column. A row or cell with no address follows the one before it, which the format allows.</summary>
        Dictionary<(int Row, int Column), Cell> Part(XElement root)
        {
            var cells = new Dictionary<(int, int), Cell>();
            var rowAt = 0;
            foreach (var row in Children(Child(root, "sheetData"), "row"))
            {
                rowAt = RowNumber(row) ?? rowAt + 1;
                var columnAt = 0;
                foreach (var c in Children(row, "c"))
                {
                    (int Row, int Column) at = Attr(c, "r") is { } r && ParseRef(r) is { } parsed ? parsed : (rowAt, columnAt + 1);
                    columnAt = at.Column;
                    cells[(rowAt, at.Column)] = Value(c);
                }
            }
            return cells;
        }

        Cell Value(XElement c)
        {
            var type = Attr(c, "t");
            var v = Child(c, "v")?.Value;
            string? format = null;
            string? note = null;
            string text;
            switch (type)
            {
                case "s":
                    if (int.TryParse(v, out var index) && index >= 0 && index < shared.Count) text = shared[index];
                    else { text = ""; if (v is not null) note = "points at a shared string that isn't there; shown empty"; }
                    break;
                case "inlineStr": text = Child(c, "is") is { } inline ? TextOf(inline) : ""; break;
                case "b": text = v is null ? "" : On(v) ? "TRUE" : "FALSE"; break;
                case "str" or "e" or "d": text = v ?? ""; break;
                default:
                    text = v ?? "";
                    if (v is not null)
                    {
                        var id = int.TryParse(Attr(c, "s"), out var style) && style >= 0 && style < cellFormats.Count ? cellFormats[style] : 0;
                        if (IsDateFormat(id) || IsTimeFormat(id))
                        {
                            if (IsoDate(v, date1904, IsTimeFormat(id)) is { } iso) text = iso;
                            else note = "isn't a real date in its date format; the raw number is shown";
                        }
                        else if (id >= 164 || customFormats.ContainsKey(id)) format = customFormats.GetValueOrDefault(id, $"format {id}");
                    }
                    break;
            }
            return new Cell(Clean(text), format, note);
        }

        static HashSet<int> HiddenColumns(XElement root)
        {
            var hidden = new HashSet<int>();
            foreach (var col in Children(Child(root, "cols"), "col").Where(c => On(Attr(c, "hidden"))))
                if (int.TryParse(Attr(col, "min"), out var min) && int.TryParse(Attr(col, "max"), out var max))
                    for (var i = Math.Max(min, 1); i <= Math.Min(max, 16384); i++) hidden.Add(i);
            return hidden;
        }

        /// <summary>A merged range shows one value: it repeats in each of its cells, and the range is listed. A cell's own text under a merge isn't shown by Excel, so it's left out and listed.</summary>
        static void Merges(XElement root, string name, Dictionary<(int Row, int Column), string> stored, Dictionary<(int Row, int Column), string> grid, HashSet<(int Row, int Column)> leftOut, List<string> lines)
        {
            long area = 0;
            foreach (var merge in Children(Child(root, "mergeCells"), "mergeCell"))
            {
                var parts = (Attr(merge, "ref") ?? "").Split(':');
                if (parts.Length != 2 || ParseRef(parts[0]) is not { } from || ParseRef(parts[1]) is not { } to || from.Row > to.Row || from.Column > to.Column) continue;
                if (!grid.TryGetValue(from, out var value) || value.Length == 0) continue;
                area += (long)(to.Row - from.Row + 1) * (to.Column - from.Column + 1);
                if (area > MaxSheetCells)
                    throw new StangentException("xlsx_too_large", $"Sheet \"{name}\" merges more than {MaxSheetCells} cells, so it wasn't read.", ExitCodes.Failed);
                var buried = 0;
                for (var r = from.Row; r <= to.Row; r++)
                    for (var c = from.Column; c <= to.Column; c++)
                    {
                        if ((r, c) == from) continue;
                        if (stored.TryGetValue((r, c), out var own) && own.Length > 0) { leftOut.Add((r, c)); buried++; }
                        grid[(r, c)] = value;
                    }
                lines.Add($"merged cells {parts[0]}:{parts[1]} repeat their value{(buried > 0 ? $"; {buried} cells under the merge had text of their own, left out" : "")}");
            }
        }

        static string Table(Dictionary<(int Row, int Column), string> grid, List<int> rows, List<int> columns)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                sb.Append('|');
                foreach (var c in columns)
                    sb.Append(' ').Append(grid.GetValueOrDefault((rows[i], c), "").Replace("|", "\\|", StringComparison.Ordinal)).Append(" |");
                sb.Append('\n');
                if (i == 0) sb.Append('|').Append(string.Concat(Enumerable.Repeat("---|", columns.Count))).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string Csv(Dictionary<(int Row, int Column), string> grid, int bottom, HashSet<int> hiddenRows, List<int> columns)
        {
            static string Field(string s) => s.AsSpan().IndexOfAny(",\"") >= 0 ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;
            var sb = new StringBuilder();
            for (var r = 1; r <= bottom; r++)
            {
                if (!hiddenRows.Contains(r) && columns.Any(c => grid.GetValueOrDefault((r, c), "").Length > 0))
                    sb.AppendJoin(',', columns.Select(c => Field(grid.GetValueOrDefault((r, c), ""))));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Formulas in what's shown: cached value in the text, the formula here. A shared formula is one line for its whole range; one with no cached value isn't computed.</summary>
        static void Formulas(XElement root, HashSet<int> hiddenRows, HashSet<int> hiddenColumns, List<string> lines)
        {
            var masters = new Dictionary<string, string>(StringComparer.Ordinal);
            var rowAt = 0;
            foreach (var row in Children(Child(root, "sheetData"), "row"))
            {
                rowAt = RowNumber(row) ?? rowAt + 1;
                var columnAt = 0;
                foreach (var c in Children(row, "c"))
                {
                    (int Row, int Column) at = Attr(c, "r") is { } r && ParseRef(r) is { } parsed ? parsed : (rowAt, columnAt + 1);
                    columnAt = at.Column;
                    if (Child(c, "f") is not { } f || hiddenRows.Contains(rowAt) || hiddenColumns.Contains(at.Column)) continue;
                    var kind = Attr(f, "t");
                    var name = Name(rowAt, at.Column);
                    var cached = Child(c, "v") is not null;
                    var written = f.Value.Trim();
                    if (kind == "shared" && Attr(f, "si") is { } si)
                    {
                        if (written.Length > 0) masters.TryAdd(si, written);
                        else if (!cached) written = masters.GetValueOrDefault(si, "(a shared formula whose first cell wasn't found)");
                        else if (masters.ContainsKey(si)) continue;
                        else written = "(a shared formula whose first cell wasn't found)";
                    }
                    var place = Attr(f, "ref") is { } range && range != name && kind != null ? range : name;
                    var how = kind is "shared" or "array" ? $" ({kind})" : kind == "dataTable" ? " (data table)" : "";
                    lines.Add(cached ? $"formula at {place}{how}: {written}; its cached value is shown" : $"formula at {name}{how} has no cached value and wasn't computed: {written}");
                }
            }
        }

        static void CustomFormats(Dictionary<(int Row, int Column), Cell> cells, HashSet<int> hiddenRows, HashSet<int> hiddenColumns, List<string> lines)
        {
            foreach (var g in cells.Where(c => c.Value.Format is not null && c.Value.Text.Length > 0 && !hiddenRows.Contains(c.Key.Row) && !hiddenColumns.Contains(c.Key.Column))
                         .GroupBy(c => c.Value.Format!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var first = g.Min(c => c.Key);
                lines.Add($"{g.Count()} cells use the custom number format \"{g.Key}\", from {Name(first.Row, first.Column)}; the raw number is shown");
            }
        }

        static void Hidden(HashSet<int> hiddenRows, HashSet<int> hiddenColumns, Dictionary<(int Row, int Column), string> stored, List<string> lines)
        {
            foreach (var (from, to) in Ranges(hiddenRows))
            {
                var count = stored.Count(s => s.Value.Length > 0 && s.Key.Row >= from && s.Key.Row <= to);
                lines.Add($"hidden row{(from == to ? $" {from}" : $"s {from}-{to}")} left out, with {count} cells holding text");
            }
            foreach (var (from, to) in Ranges(hiddenColumns))
            {
                var count = stored.Count(s => s.Value.Length > 0 && s.Key.Column >= from && s.Key.Column <= to && !hiddenRows.Contains(s.Key.Row));
                lines.Add($"hidden column{(from == to ? $" {Name(1, from)[..^1]}" : $"s {Name(1, from)[..^1]}-{Name(1, to)[..^1]}")} left out, with {count} cells holding text");
            }
        }

        static IEnumerable<(int From, int To)> Ranges(HashSet<int> numbers)
        {
            int? start = null, last = null;
            foreach (var n in numbers.Order())
            {
                if (start is null) { start = last = n; continue; }
                if (n == last + 1) { last = n; continue; }
                yield return (start.Value, last!.Value);
                start = last = n;
            }
            if (start is not null) yield return (start.Value, last!.Value);
        }

        static void NotRead(XElement root, WorksheetPart part, List<string> lines)
        {
            void Named(int count, string one, string many) { if (count > 0) lines.Add(count == 1 ? $"a {one} not read" : $"{count} {many} not read"); }
            if (part.DrawingsPart is { } drawing)
            {
                var shapes = Read(drawing);
                Named(drawing.ChartParts.Count() + drawing.ExtendedChartParts.Count(), "chart", "charts");
                Named(shapes.Descendants().Count(e => e.Name.LocalName == "pic"), "picture", "pictures");
                Named(shapes.Descendants().Count(e => e.Name.LocalName == "sp" && e.Descendants().Any(t => t.Name.LocalName == "t" && t.Value.Trim().Length > 0)), "shape with text", "shapes with text");
            }
            Named(part.PivotTableParts.Count(), "pivot table", "pivot tables");
            if (Child(root, "headerFooter") is { } hf && hf.Value.Trim().Length > 0) lines.Add("page headers and footers not read");
        }

        /// <summary>Notes and threaded comments, in the shape a Word comment has (S.2). Excel keeps a note for each threaded comment, for older readers, and that note isn't listed again.</summary>
        void Comments(string name, WorksheetPart part)
        {
            var threaded = part.Parts.Select(p => p.OpenXmlPart).Where(p => p.Uri.ToString().Contains("threadedComment", StringComparison.OrdinalIgnoreCase)).ToList();
            var people = workbook.Parts.Select(p => p.OpenXmlPart).Where(p => p.Uri.ToString().Contains("/persons/", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => Read(p).Elements().Where(e => e.Name.LocalName == "person"))
                .GroupBy(p => Attr(p, "id") ?? "").ToDictionary(g => g.Key, g => Attr(g.First(), "displayName") ?? "unknown", StringComparer.Ordinal);
            if (part.WorksheetCommentsPart is { } legacy)
            {
                var root = Read(legacy);
                var authors = Children(Child(root, "authors"), "author").Select(a => a.Value).ToList();
                foreach (var c in Children(Child(root, "commentList"), "comment"))
                {
                    var author = int.TryParse(Attr(c, "authorId"), out var i) && i >= 0 && i < authors.Count ? authors[i] : "unknown";
                    if (author.StartsWith("tc=", StringComparison.Ordinal)) continue;
                    problems.Add(new DocxComment(name, author, false, $"{Attr(c, "ref")}: {Clean(TextOf(c))}").Line());
                }
            }
            foreach (var p in threaded)
                foreach (var c in Read(p).Elements().Where(e => e.Name.LocalName == "threadedComment"))
                    problems.Add(new DocxComment(name, people.GetValueOrDefault(Attr(c, "personId") ?? "", "unknown"), On(Attr(c, "done")), $"{Attr(c, "ref")}: {Clean(Child(c, "text")?.Value ?? "")}").Line());
        }

        void Embedded(WorksheetPart part, string? section, string where)
        {
            foreach (var pair in part.Parts.Where(p => p.OpenXmlPart is EmbeddedPackagePart or EmbeddedObjectPart).OrderBy(p => p.OpenXmlPart.Uri.ToString(), StringComparer.Ordinal))
            {
                using var stream = pair.OpenXmlPart.GetStream(FileMode.Open, FileAccess.Read);
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                files.Add(new FileInDocument(Path.GetFileName(pair.OpenXmlPart.Uri.ToString()), bytes.ToArray(), section, $"{where}, {pair.OpenXmlPart.Uri}"));
            }
        }

        void Extras()
        {
            foreach (var link in workbook.ExternalWorkbookParts)
                problems.Add($"workbook: an external link not read ({link.ExternalRelationships.FirstOrDefault()?.Uri.ToString() ?? "no target"})");
            if (workbook.VbaProjectPart is not { } vba) return;
            using var stream = vba.GetStream(FileMode.Open, FileAccess.Read);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var modules = OleContainer.TryOpen(bytes.ToArray())?.Streams
                .Where(s => s.StartsWith("VBA/", StringComparison.Ordinal))
                .Select(s => s["VBA/".Length..])
                .Where(s => s is not ("dir" or "_VBA_PROJECT" or "PROJECTwm") && !s.StartsWith("__SRP_", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToList();
            problems.Add(modules is { Count: > 0 }
                ? $"workbook: macros not read, and never run: {string.Join(", ", modules)}"
                : "workbook: macros not read, and never run; their module names can't be read");
        }

        sealed record Cell(string Text, string? Format, string? Note);
    }
}
