using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

public sealed record ImportOptions(bool Flat = false, bool AcceptChanges = false, IReadOnlyList<string>? AllowLabels = null)
{
    /// <summary>When a workbook is flagged as very large (<c>fsd.excel</c>). The defaults are the settings'.</summary>
    public int LargeSheets { get; init; } = 50;
    public int LargeRows { get; init; } = 100000;
    /// <summary>Over this an attachment reads "not checked" (<c>fsd.attachmentChars</c>).</summary>
    public int AttachmentChars { get; init; } = 40000;
    /// <summary>How far an embedded file may go. Null is <see cref="ImportLimits.Default"/>.</summary>
    public ImportLimits? Limits { get; init; }
    /// <summary>How many files this one is inside, and what's left to unpack, for files nested in files.</summary>
    public int Depth { get; init; }
    public UnpackBudget? Budget { get; init; }

    public static ImportOptions From(StangentConfig config, bool flat = false, bool acceptChanges = false, IReadOnlyList<string>? allowLabels = null) => new(flat, acceptChanges, allowLabels)
    {
        LargeSheets = config.Fsd.Excel.LargeSheets, LargeRows = config.Fsd.Excel.LargeRows, AttachmentChars = config.Fsd.AttachmentChars,
    };
}

/// <summary>What one step did. <c>not-for-format</c> says, with a reason, that a format has no such step, so a missing step can't pass for an empty one.</summary>
public sealed record ImportStepResult(string Step, string Status, string Note, int Count = 0)
{
    public const string Ran = "ran", NotForFormat = "not-for-format";

    public JsonObject ToJson() => new() { ["step"] = Step, ["result"] = Status, ["note"] = Note, ["count"] = Count };
}

public sealed record ImportResult(string Format, ConvertedDocument Document, IReadOnlyList<ImportStepResult> Steps, IReadOnlyList<ImportLabel> Labels)
{
    public JsonArray StepsJson() => new([.. Steps.Select(s => (JsonNode)s.ToJson())]);
}

/// <summary>
/// The steps every document format fills in (D121): safety checks, convert, comments, extract files, completeness check. <see cref="ImportPipeline"/>
/// runs them in order and gives the result. Safety and convert can stop an import; the other three report. A format without a step
/// returns <see cref="ImportStepResult.NotForFormat"/> and says why. Word and PDF read comments and check completeness in the same walk
/// that converts, so their three report what that walk found; a new format can do the work here instead.
/// </summary>
public interface IImportFormat
{
    string Name { get; }
    IReadOnlyList<string> Extensions { get; }
    /// <summary>A request this format can't do at all, refused before anything else is looked at.</summary>
    void CheckOptions(ImportOptions options);
    /// <summary>The sensitivity labels on the file. Safety stops on any nobody named.</summary>
    IReadOnlyList<ImportLabel> Labels(byte[] file);
    /// <summary>Stops a file that shouldn't be read, by throwing.</summary>
    ImportStepResult Safety(byte[] file, ImportOptions options);
    ConvertedDocument Convert(byte[] file, ImportOptions options);
    ImportStepResult Comments(ConvertedDocument document);
    ImportStepResult ExtractFiles(ConvertedDocument document);
    ImportStepResult Completeness(ConvertedDocument document);
}

/// <summary>A ticket's attachment through the import steps: its text, or why it has none.</summary>
public sealed record ConvertedAttachment(string Format, string? Text, string? NotConverted, JsonArray Problems, JsonArray Steps)
{
    /// <summary>The stop's code, when it stopped on one.</summary>
    public string? Code { get; init; }
    public ConvertedDocument? Document { get; init; }
}

public static class ImportPipeline
{
    public static readonly IReadOnlyList<IImportFormat> Formats = [new WordFormat(), new PdfFormat(), new ExcelFormat()];

    public static IImportFormat? ForFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return Formats.FirstOrDefault(f => f.Extensions.Contains(ext));
    }

    /// <summary>The steps in order, and the result. A stop is a <see cref="StangentException"/> from safety or convert, with nothing written.</summary>
    public static ImportResult Run(IImportFormat format, byte[] file, ImportOptions options)
    {
        var safety = format.Safety(file, options);
        var document = EmbeddedFiles.Attach(format.Convert(file, options), options);
        var convert = new ImportStepResult("convert", ImportStepResult.Ran, "the file's text as Markdown, the same bytes each time", document.Sections.Count);
        return new ImportResult(format.Name, document, [safety, convert, format.Comments(document), format.ExtractFiles(document), format.Completeness(document)], format.Labels(file));
    }

    /// <summary>
    /// For a ticket (D72, D121): an attachment isn't the agreed FSD, so nothing here stops the pull. A tracked change reads as accepted, no
    /// headings reads flat, and anything else is reported with why. Null for a file no format takes.
    /// </summary>
    public static ConvertedAttachment? Attachment(string fileName, byte[] file, ImportOptions? options = null)
    {
        if (ForFile(fileName) is not { } format) return null;
        var asked = (options ?? new ImportOptions()) with { AcceptChanges = true, Flat = false };
        try
        {
            ImportResult result;
            try { result = Run(format, file, asked); }
            catch (StangentException e) when (e.Code == "no_structure") { result = Run(format, file, asked with { Flat = true }); }
            var sections = result.Document.Sections.Select(FsdImport.SectionJson).ToList();
            return new ConvertedAttachment(format.Name, result.Document.Markdown, null, ImportProblems.Of(result.Document.Problems, sections), result.StepsJson()) { Document = result.Document };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new ConvertedAttachment(format.Name, null, e.Message, [], []) { Code = (e as StangentException)?.Code };
        }
    }

    sealed class WordFormat : IImportFormat
    {
        public string Name => "docx";
        public IReadOnlyList<string> Extensions { get; } = [".docx"];
        public void CheckOptions(ImportOptions options) { }

        public IReadOnlyList<ImportLabel> Labels(byte[] file) => ImportLabels.OfWord(file);

        public ImportStepResult Safety(byte[] file, ImportOptions options)
        {
            DocxConverter.CheckLocked(file);
            ImportLabels.Check(Labels(file), options.AllowLabels);
            var tracked = DocxConverter.CheckSafe(file, options.AcceptChanges);
            return new("safety", ImportStepResult.Ran, tracked > 0 ? "opens; tracked changes accepted, as asked" : "opens, with no tracked changes", tracked);
        }

        public ConvertedDocument Convert(byte[] file, ImportOptions options) => DocxConverter.Convert(file, options.Flat, options.AcceptChanges);

        public ImportStepResult Comments(ConvertedDocument document) =>
            new("comments", ImportStepResult.Ran, "listed in problems, never in the text", document.Problems.Count(p => DocxComment.Parse(p) is not null));

        public ImportStepResult ExtractFiles(ConvertedDocument document) =>
            new("extract-files", ImportStepResult.Ran, "pictures kept; embedded files saved and read, each listed; count is both", document.Images.Count + document.Attachments.Count);

        public ImportStepResult Completeness(ConvertedDocument document) =>
            new("completeness", ImportStepResult.Ran, "every word in the file against document.md, per section; count is the mismatches",
                document.Problems.Count(p => p.StartsWith("completeness: ", StringComparison.Ordinal)));
    }

    sealed class PdfFormat : IImportFormat
    {
        public string Name => "pdf";
        public IReadOnlyList<string> Extensions { get; } = [".pdf"];

        public void CheckOptions(ImportOptions options)
        {
            if (options.AcceptChanges) throw new StangentException("usage", "--accept-changes is for .docx files: a PDF has no tracked changes to accept.", ExitCodes.Usage);
        }

        public IReadOnlyList<ImportLabel> Labels(byte[] file) => PdfConverter.Labels(file);

        public ImportStepResult Safety(byte[] file, ImportOptions options)
        {
            var pages = PdfConverter.CheckSafe(file);
            ImportLabels.Check(Labels(file), options.AllowLabels);
            return new("safety", ImportStepResult.Ran, "opens, isn't protected, has pages; count is the pages", pages);
        }

        public ConvertedDocument Convert(byte[] file, ImportOptions options) => PdfConverter.Convert(file, options.Flat);

        public ImportStepResult Comments(ConvertedDocument document) =>
            new("comments", ImportStepResult.NotForFormat, "a PDF's annotations aren't read");

        public ImportStepResult ExtractFiles(ConvertedDocument document) =>
            new("extract-files", ImportStepResult.Ran, "pictures kept, named by content; files attached to the PDF saved and read, each listed; count is both", document.Images.Count + document.Attachments.Count);

        public ImportStepResult Completeness(ConvertedDocument document) =>
            new("completeness", ImportStepResult.Ran, "each page's and table's words against the page's own; count is what fell back to plain text",
                document.Problems.Count(p => p.Contains("wasn't brought in exactly", StringComparison.Ordinal) || p.Contains("didn't match", StringComparison.Ordinal) || p.Contains("gives different words", StringComparison.Ordinal)));
    }

    sealed class ExcelFormat : IImportFormat
    {
        public string Name => "xlsx";
        public IReadOnlyList<string> Extensions { get; } = [".xlsx", ".xlsm"];

        public void CheckOptions(ImportOptions options)
        {
            if (options.Flat) throw new StangentException("usage", "--flat is for documents with no headings: a workbook's sections are its sheets.", ExitCodes.Usage);
        }

        public IReadOnlyList<ImportLabel> Labels(byte[] file) => ImportLabels.OfWord(file);

        public ImportStepResult Safety(byte[] file, ImportOptions options)
        {
            XlsxConverter.CheckLocked(file);
            if (EmbeddedFiles.ZipGuard(file, options.Limits ?? ImportLimits.Default) is { } refused)
                throw new StangentException("xlsx_too_large", $"This workbook wasn't read: {refused}.", ExitCodes.Failed);
            ImportLabels.Check(Labels(file), options.AllowLabels);
            var revisions = XlsxConverter.CheckSafe(file, options.AcceptChanges);
            return new("safety", ImportStepResult.Ran, revisions > 0 ? "opens; a shared workbook's revisions read as saved, as asked" : "opens, with no revisions to settle", revisions);
        }

        public ConvertedDocument Convert(byte[] file, ImportOptions options) => XlsxConverter.Convert(file, options.AcceptChanges, options.LargeSheets, options.LargeRows);

        public ImportStepResult Comments(ConvertedDocument document) =>
            new("comments", ImportStepResult.Ran, "notes and threaded comments listed in problems, never in the text", document.Problems.Count(p => DocxComment.Parse(p) is not null));

        public ImportStepResult ExtractFiles(ConvertedDocument document) =>
            new("extract-files", ImportStepResult.Ran, "files on a sheet saved and read, each listed; charts and pictures are named, not read", document.Attachments.Count);

        public ImportStepResult Completeness(ConvertedDocument document) =>
            new("completeness", ImportStepResult.Ran, "every stored cell against its table, or a listed reason; count is the mismatches",
                document.Problems.Count(p => p.StartsWith("completeness: ", StringComparison.Ordinal)));
    }
}
