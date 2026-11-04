using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>A sensitivity label on a file. <c>Name</c> is missing where the file only holds its id, as <c>LabelInfo.xml</c> does.</summary>
public sealed record ImportLabel(string? Name, string Id)
{
    public string Shown => Name ?? Id;
    public bool Named(string asked) => string.Equals(Name, asked, StringComparison.OrdinalIgnoreCase) || string.Equals(Id, asked.Trim('{', '}'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Sensitivity labels (D122): Word's <c>MSIP_Label_*</c> custom properties and <c>docMetadata/LabelInfo.xml</c>, and the same properties in a PDF's metadata.
/// A label stops the import unless a person names it with <c>--allow-label</c>. This reads the label; it never removes one or gets round any protection.
/// </summary>
public static partial class ImportLabels
{
    [GeneratedRegex(@"^MSIP_Label_(?<id>[0-9A-Fa-f-]+)_(?<property>[A-Za-z]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LabelProperty();

    /// <summary>Labels from name and value pairs, the shape Word's custom properties and a PDF's metadata both have. One switched off doesn't count.</summary>
    public static List<ImportLabel> FromProperties(IEnumerable<KeyValuePair<string, string>> properties)
    {
        var found = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in properties)
        {
            if (LabelProperty().Match(name) is not { Success: true } m) continue;
            var id = m.Groups["id"].Value.ToLowerInvariant();
            if (!found.TryGetValue(id, out var label)) found[id] = label = new(StringComparer.OrdinalIgnoreCase);
            label[m.Groups["property"].Value] = value;
        }
        return [.. found.Where(l => !l.Value.TryGetValue("Enabled", out var on) || !IsOff(on))
            .Select(l => new ImportLabel(l.Value.GetValueOrDefault("Name"), l.Key)).OrderBy(l => l.Id, StringComparer.Ordinal)];
    }

    static bool IsOff(string value) => value.Trim() is "0" || value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Word file's labels, read from the zip itself. A file that isn't a zip has none here, and the next step says what's wrong with it.</summary>
    public static List<ImportLabel> OfWord(byte[] docx)
    {
        var properties = new List<KeyValuePair<string, string>>();
        var listed = new List<ImportLabel>();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            if (Xml(zip, "docProps/custom.xml") is { } custom)
                foreach (var p in custom.Descendants().Where(e => e.Name.LocalName == "property"))
                    if ((string?)p.Attribute("name") is { } name) properties.Add(KeyValuePair.Create(name, p.Value));
            if (Xml(zip, "docMetadata/LabelInfo.xml") is { } info)
                foreach (var l in info.Descendants().Where(e => e.Name.LocalName == "label"))
                    if ((string?)l.Attribute("id") is { } id && !IsOff((string?)l.Attribute("enabled") ?? "1") && !IsOn((string?)l.Attribute("removed")))
                        listed.Add(new ImportLabel(null, id.Trim('{', '}').ToLowerInvariant()));
        }
        catch (InvalidDataException) { return []; }
        var labels = FromProperties(properties);
        labels.AddRange(listed.Where(l => labels.TrueForAll(k => !k.Id.Equals(l.Id, StringComparison.OrdinalIgnoreCase))).DistinctBy(l => l.Id));
        return labels;
    }

    static bool IsOn(string? value) => value is not null && value.Trim() is "1" || value?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Damaged metadata is no label found: the file itself may be fine, and it can't be told what the words were meant to say.</summary>
    static XDocument? Xml(ZipArchive zip, string path)
    {
        if (zip.GetEntry(path) is not { } entry) return null;
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            return XDocument.Load(reader);
        }
        catch (XmlException) { return null; }
    }

    /// <summary>Stops on a label nobody named. A file can hold several, and each has to be named.</summary>
    public static void Check(IReadOnlyList<ImportLabel> labels, IReadOnlyList<string>? allowed)
    {
        var stopped = labels.Where(l => !(allowed ?? []).Any(l.Named)).ToList();
        if (stopped.Count == 0) return;
        var names = string.Join(", ", stopped.Select(l => l.Shown));
        throw new StangentException("document_labelled",
            $"This file has a sensitivity label ({names}), so it isn't imported by default: what's in it would go on to AI steps and into this repo. If that's allowed for this label, run the import again with --allow-label \"{stopped[0].Shown}\"; the label and your name are kept in the document's record.",
            ExitCodes.Failed, [.. stopped.Select(l => l.Name is null ? $"label {l.Id}" : $"label {l.Name} ({l.Id})")]);
    }
}
