using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stangent.Spec.Fsd;

/// <summary>A file held inside a document, and the section it sits in. Null is a file with no section: one attached to a whole PDF, or on a hidden sheet.</summary>
public sealed record FileInDocument(string Name, byte[] Bytes, string? Section, string From);

/// <summary>One file an attachment comes to, by its path under the attachment's folder. <c>Role</c>: <c>original</c>, <c>csv</c>, <c>text</c>, or <c>kept</c> for a file unpacked and not read.</summary>
public sealed record AttachmentOutput(string Path, byte[] Bytes, string Role, string? Sheet = null);

/// <summary>An attachment, read as far as it can be. <c>Status</c>: <c>converted</c>, <c>kept</c> (saved and listed, with why), or <c>withheld</c> (a label nobody allowed, so nothing's saved).</summary>
public sealed record AttachmentRecord(string FileName, string From, string? Section, string Hash, long Bytes, string Kind, string Status, string? Why, IReadOnlyList<string> Problems, IReadOnlyList<AttachmentOutput> Outputs)
{
    public int Chars => Outputs.Where(o => o.Role is "csv" or "text").Sum(o => Encoding.UTF8.GetCharCount(o.Bytes));
}

/// <summary>
/// What an embedded file may come to (D123). Stated limits, not measured ones: anything past them is refused and listed, and the import goes on. A real file should come nowhere near
/// them; a zip bomb reaches them at once. <c>MaxRatio</c> only counts above <c>RatioFrom</c> unpacked bytes, as small files compress unpredictably.
/// </summary>
public sealed record ImportLimits(long MaxFileBytes = 256L << 20, long MaxUnpackedBytes = 1L << 30, int MaxEntries = 10_000, int MaxDepth = 3, long RatioFrom = 10L << 20, int MaxRatio = 500)
{
    public static readonly ImportLimits Default = new();
}

/// <summary>What's left of one attachment's <see cref="ImportLimits.MaxUnpackedBytes"/>, across everything nested in it.</summary>
public sealed class UnpackBudget(long bytes)
{
    long left = bytes;
    public bool Take(long count)
    {
        if (count > left) return false;
        left -= count;
        return true;
    }
}

/// <summary>
/// Files inside a document, by code (A.7, D123). Excel gives one CSV per sheet, Word and PDF text through the same import steps, and a zip is unpacked and each file read. Anything else, and anything locked,
/// scanned or unreadable, is kept and listed. Nothing in here stops an import. The text never goes into <c>document.md</c>.
/// </summary>
public static partial class EmbeddedFiles
{
    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex DriveLetter();

    /// <summary>The document with its files read, and a line for each in its problems. A file stays out of the text, so a reader has to be told it's there.</summary>
    public static ConvertedDocument Attach(ConvertedDocument document, ImportOptions options)
    {
        if (document.Files.Count == 0) return document;
        var limits = options.Limits ?? ImportLimits.Default;
        var records = document.Files.Select(f => One(f, options, options.Depth + 1, options.Budget ?? new UnpackBudget(limits.MaxUnpackedBytes))).ToList();
        var lines = records.Select(r => Line(r, options.AttachmentChars)).ToList();
        return document with { Attachments = records, Problems = [.. document.Problems, .. lines] };
    }

    static string Line(AttachmentRecord r, int attachmentChars)
    {
        var where = r.Section is null ? "with no section" : $"in section {r.Section}";
        var outcome = r.Status switch
        {
            "converted" => $"read into {r.Outputs.Count(o => o.Role is "csv" or "text")} files, not into the document's text",
            "withheld" => $"withheld, not saved here: {r.Why}",
            _ => $"kept, not read: {r.Why}",
        };
        var more = r.Problems.Count > 0 ? $"; {r.Problems.Count} things listed in its record" : "";
        var late = r.Chars > attachmentChars ? $"; not checked: its {r.Chars} characters are over fsd.attachmentChars ({attachmentChars})" : "";
        return $"attachment \"{r.FileName}\" {where}: {outcome}{more}{late}";
    }

    /// <summary>One file, and everything in it. It's kept whole first, so whatever happens next, nothing's lost.</summary>
    public static AttachmentRecord One(FileInDocument file, ImportOptions options, int depth, UnpackBudget? budget = null)
    {
        var limits = options.Limits ?? ImportLimits.Default;
        budget ??= new UnpackBudget(limits.MaxUnpackedBytes);
        var safe = Sniffed(SafeSegment(file.Name), file.Bytes);
        var ext = Path.GetExtension(safe).ToLowerInvariant();
        var hash = System.Convert.ToHexStringLower(SHA256.HashData(file.Bytes));
        var problems = new List<string>();
        var outputs = new List<AttachmentOutput> { new($"original/{SafeSegment(file.Name)}", file.Bytes, "original") };
        var format = ImportPipeline.ForFile(safe);
        var ole = OleContainer.IsOle(file.Bytes);
        var kind = ole ? "ole" : ext switch
        {
            ".xlsx" or ".xlsm" => "excel",
            ".docx" => "word",
            ".pdf" => "pdf",
            ".zip" => "zip",
            ".pptx" or ".ppt" or ".pptm" => "powerpoint",
            _ => "other",
        };
        AttachmentRecord Done(string status, string? why) => new(file.Name, file.From, file.Section, hash, file.Bytes.Length, kind, status, why, problems, status == "withheld" ? [] : outputs);

        if (depth > limits.MaxDepth) return Done("kept", $"it's nested more than {limits.MaxDepth} files deep, the limit");
        if (ole) return Ole(file, safe, options, depth, budget, problems, outputs, Done);
        if (kind == "powerpoint") return Done("kept", "PowerPoint isn't decided: the file is kept, and its slides aren't read");
        if (kind == "zip") return Zip(file, options, depth, budget, problems, outputs, Done);
        if (format is null) return Done("kept", "no format reads it");

        if (format.Name != "pdf" && ZipGuard(file.Bytes, limits) is { } refused) return Done("kept", refused);
        var child = options with { Depth = depth, Budget = budget, AcceptChanges = true, Flat = false };
        var converted = ImportPipeline.Attachment(safe, file.Bytes, child)!;
        if (converted.Code == "document_labelled") return Done("withheld", converted.NotConverted);
        if (converted.NotConverted is { } why) return Done("kept", why);

        var document = converted.Document!;
        problems.AddRange(document.Problems.Where(p => !p.StartsWith("attachment \"", StringComparison.Ordinal)));
        if (format.Name == "xlsx")
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in document.Sheets)
                outputs.Add(new($"{Unique(names, SheetFile(sheet.Sheet))}.csv", Encoding.UTF8.GetBytes(sheet.Csv), "csv", sheet.Sheet));
        }
        else outputs.Add(new($"{Path.GetFileNameWithoutExtension(safe)}.md", Encoding.UTF8.GetBytes(document.Markdown), "text"));
        foreach (var (inner, i) in document.Attachments.Select((a, i) => (a, i + 1)))
            Nest($"embedded/{i}-{SafeSegment(inner.FileName)}/", inner, problems, outputs);
        return Done("converted", null);
    }

    static AttachmentRecord Ole(FileInDocument file, string safe, ImportOptions options, int depth, UnpackBudget budget, List<string> problems, List<AttachmentOutput> outputs, Func<string, string?, AttachmentRecord> done)
    {
        if (OleContainer.TryOpen(file.Bytes) is not { } container) return done("kept", "an OLE object that can't be read");
        if (container.Streams.Contains("EncryptedPackage")) return done("kept", "it's locked with a password");
        if (container.Package() is { } package)
        {
            if (!budget.Take(package.Bytes.Length)) return done("kept", "the file inside it goes over the limit on what one attachment can unpack");
            var inner = One(new FileInDocument(package.Name, package.Bytes, file.Section, file.From), options, depth + 1, budget);
            Nest($"package/{SafeSegment(package.Name)}/", inner, problems, outputs);
            return inner.Status == "converted" ? done("converted", null) : done("kept", $"the file inside it, {package.Name}: {inner.Why}");
        }
        var wrapped = container.Read("Package");
        if (wrapped is not null && OfficeExtension(wrapped) is { } ext)
        {
            if (!budget.Take(wrapped.Length)) return done("kept", "the file inside it goes over the limit on what one attachment can unpack");
            var name = Path.GetFileNameWithoutExtension(safe) + ext;
            var inner = One(new FileInDocument(name, wrapped, file.Section, file.From), options, depth + 1, budget);
            Nest($"package/{name}/", inner, problems, outputs);
            return inner.Status == "converted" ? done("converted", null) : done("kept", $"the file inside it, {name}: {inner.Why}");
        }
        var old = container.Streams.Contains("Workbook") || container.Streams.Contains("Book") ? "an old Excel file (.xls), which isn't read"
            : container.Streams.Contains("WordDocument") ? "an old Word file (.doc), which isn't read"
            : container.Streams.Contains("PowerPoint Document") ? "an old PowerPoint file; PowerPoint isn't decided"
            : "an OLE object that can't be read";
        return done("kept", old);
    }

    /// <summary>A name that says what the file is. A part inside an Office file can be called anything, <c>package.bin</c> included, so one whose name doesn't say goes by what it holds. It's only used to choose a reader and name what comes out.</summary>
    static string Sniffed(string name, byte[] bytes)
    {
        if (ImportPipeline.ForFile(name) is not null || Path.GetExtension(name).ToLowerInvariant() is ".zip" or ".pptx" or ".ppt" or ".pptm") return name;
        var found = bytes.AsSpan().StartsWith("%PDF-"u8) ? ".pdf" : OfficeExtension(bytes);
        return found is null ? name : Path.GetFileNameWithoutExtension(name) + found;
    }

    /// <summary>What a zip is, by the parts it holds. Null for anything else.</summary>
    static string? OfficeExtension(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("PK"u8)) return null;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return zip.GetEntry("xl/workbook.xml") is not null ? ".xlsx" : zip.GetEntry("word/document.xml") is not null ? ".docx" : zip.GetEntry("ppt/presentation.xml") is not null ? ".pptx" : null;
        }
        catch (InvalidDataException) { return null; }
    }

    static void Nest(string prefix, AttachmentRecord inner, List<string> problems, List<AttachmentOutput> outputs)
    {
        outputs.AddRange(inner.Outputs.Select(o => o with { Path = prefix + o.Path, Role = o.Role == "original" ? "kept" : o.Role }));
        if (inner.Status != "converted") problems.Add($"{inner.FileName}: {inner.Status}: {inner.Why}");
        problems.AddRange(inner.Problems.Select(p => $"{inner.FileName}: {p}"));
    }

    static AttachmentRecord Zip(FileInDocument file, ImportOptions options, int depth, UnpackBudget budget, List<string> problems, List<AttachmentOutput> outputs, Func<string, string?, AttachmentRecord> done)
    {
        var limits = options.Limits ?? ImportLimits.Default;
        ZipArchive archive;
        try { archive = new ZipArchive(new MemoryStream(file.Bytes), ZipArchiveMode.Read); }
        catch (InvalidDataException e) { return done("kept", $"a zip that can't be opened: {e.Message}"); }
        using (archive)
        {
            if (archive.Entries.Count > limits.MaxEntries)
                return done("kept", $"it holds {archive.Entries.Count} entries, over the limit of {limits.MaxEntries}, so none were unpacked");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var read = 0;
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                if (UnsafeName(entry.FullName) is { } bad) { problems.Add($"refused \"{entry.FullName}\": {bad}"); continue; }
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) { problems.Add($"refused \"{entry.FullName}\": a symbolic link"); continue; }
                var bytes = Unpack(entry, limits, budget, out var why);
                if (bytes is null) { problems.Add($"refused \"{entry.FullName}\": {why}"); continue; }
                var path = Unique(used, SafePath(entry.FullName));
                var inner = One(new FileInDocument(entry.FullName, bytes, file.Section, file.From), options, depth + 1, budget);
                Nest($"unpacked/{path}/", inner, problems, outputs);
                read++;
            }
            return read > 0 || problems.Count == 0 ? done("converted", null) : done("kept", "nothing in it could be unpacked");
        }
    }

    /// <summary>An entry's bytes, counted as they come rather than by what its header claims. Null, with why, past a limit or when it won't open.</summary>
    static byte[]? Unpack(ZipArchiveEntry entry, ImportLimits limits, UnpackBudget budget, out string? why)
    {
        why = null;
        try
        {
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int got;
            while ((got = stream.Read(buffer)) > 0)
            {
                if (bytes.Length + got > limits.MaxFileBytes) { why = $"it unpacks past the limit of {limits.MaxFileBytes} bytes for one file"; return null; }
                if (!budget.Take(got)) { why = $"it goes over the limit of {limits.MaxUnpackedBytes} bytes one attachment can unpack"; return null; }
                bytes.Write(buffer, 0, got);
                if (bytes.Length > limits.RatioFrom && bytes.Length / Math.Max(entry.CompressedLength, 1) > limits.MaxRatio)
                {
                    why = $"it unpacks to more than {limits.MaxRatio} times its packed size, the limit";
                    return null;
                }
            }
            return bytes.ToArray();
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
        {
            why = $"it can't be unpacked: {e.Message}";
            return null;
        }
    }

    /// <summary>Why an Office file, which is a zip, shouldn't be opened, or null. Every part is really unpacked and counted, since a header can say anything. Not a zip at all is for the format's own step to say.</summary>
    public static string? ZipGuard(byte[] file, ImportLimits limits)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            if (zip.Entries.Count > limits.MaxEntries) return $"it holds {zip.Entries.Count} parts, over the limit of {limits.MaxEntries}";
            var budget = new UnpackBudget(limits.MaxUnpackedBytes);
            foreach (var entry in zip.Entries)
                if (Unpack(entry, limits, budget, out var why) is null) return $"part {entry.FullName}: {why}";
            return null;
        }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>Why a zip entry's name can't be used, or null: anything that could write outside the folder it's unpacked into.</summary>
    public static string? UnsafeName(string name)
    {
        if (name.Any(char.IsControl)) return "a control character in its name";
        var path = name.Replace('\\', '/');
        if (path.StartsWith('/')) return "an absolute path";
        if (DriveLetter().IsMatch(path)) return "a drive letter";
        if (path.Split('/').Contains("..")) return "it climbs out of its folder";
        return path.Length > 200 ? "its name is over 200 characters" : null;
    }

    static string SafePath(string name) => string.Join('/', name.Replace('\\', '/').Split('/').Where(s => s.Length > 0 && s != ".").Select(SafeSegment));

    static string SafeSegment(string name)
    {
        var last = name.Replace('\\', '/').Split('/')[^1];
        var clean = string.Concat(last.Select(c => char.IsControl(c) || "<>:\"|?*".Contains(c) ? '_' : c)).Trim().TrimEnd('.').TrimStart('.');
        return clean.Length == 0 ? "file" : clean;
    }

    /// <summary>A sheet's file name: lower case, so <c>Rates</c> is <c>rates.csv</c>.</summary>
    static string SheetFile(string sheet)
    {
        var slug = Regex.Replace(sheet.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "sheet" : slug;
    }

    /// <summary>The name, or the name with a number, so two can't land on one file on a disk that ignores case.</summary>
    static string Unique(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++) candidate = $"{name}-{i}";
        return candidate;
    }

    /// <summary>The record <c>document.json</c> holds, its id and section's permanent id still to come.</summary>
    /// <summary>I.7, D16: a client's file as it came, at any depth. It goes to the archive; what it came to stays in the repo.</summary>
    public static bool IsOriginal(AttachmentOutput o) => o.Path.StartsWith("original/", StringComparison.Ordinal) || o.Path.Contains("/original/", StringComparison.Ordinal);

    public static JsonObject Json(AttachmentRecord r, int attachmentChars)
    {
        var o = new JsonObject
        {
            ["fileName"] = r.FileName, ["from"] = r.From, ["hash"] = r.Hash, ["bytes"] = r.Bytes, ["kind"] = r.Kind, ["reading"] = r.Status, ["chars"] = r.Chars,
            ["files"] = new JsonArray([.. r.Outputs.Select(f =>
            {
                var file = new JsonObject { ["file"] = f.Path, ["role"] = f.Role, ["hash"] = System.Convert.ToHexStringLower(SHA256.HashData(f.Bytes)) };
                if (f.Sheet is not null) file["sheet"] = f.Sheet;
                return (JsonNode)file;
            })]),
            ["problems"] = new JsonArray([.. r.Problems.Select(p => (JsonNode)p)]),
        };
        if (r.Why is not null) o["why"] = r.Why;
        if (r.Section is not null) o["sectionNumber"] = r.Section;
        if (r.Chars > attachmentChars) o["notChecked"] = true;
        return o;
    }

    /// <summary>Writes an attachment's files into its folder. Paths came from code that refused anything unsafe, and this checks once more that each stays inside.</summary>
    public static void Write(string folder, AttachmentRecord record)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var output in record.Outputs.Where(o => !IsOriginal(o)))
        {
            var target = Path.GetFullPath(Path.Combine(root, output.Path));
            if (!target.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException($"An attachment's file {output.Path} would land outside its folder (a stangent bug).");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, output.Bytes);
        }
    }
}
