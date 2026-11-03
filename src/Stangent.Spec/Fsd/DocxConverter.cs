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

public sealed record ConvertedDocument(string Markdown, IReadOnlyList<FsdSection> Sections, IReadOnlyList<FsdImage> Images, IReadOnlyList<string> Problems, string? Title);

/// <summary>
/// <c>.docx</c> → Markdown, deterministically, in C# with the Open XML SDK (Phase 2b step 1, D13). Keeps headings (with
/// Word's own automatic numbering worked out, or numbers typed into the heading), numbered and bulleted lists, tables,
/// and images with their captions. Inline formatting is dropped on purpose: quotes are matched against this text.
/// </summary>
public static partial class DocxConverter
{
    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyle();

    public static ConvertedDocument Convert(byte[] docx, bool flat = false)
    {
        try
        {
            using var stream = new MemoryStream(docx);
            using var doc = WordprocessingDocument.Open(stream, false);
            var main = doc.MainDocumentPart ?? throw new Core.StangentException("invalid_docx", "The file has no document body.");
            var ctx = new Context(main, flat);
            foreach (var block in main.Document?.Body?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
                ctx.Block(block);
            return ctx.Finish();
        }
        catch (Exception e) when (e is FileFormatException or OpenXmlPackageException or InvalidDataException or System.Xml.XmlException)
        {
            // A password-protected Word file is an OLE container, not a zip, and lands here too.
            throw new Core.StangentException("invalid_docx", "This .docx can't be opened: it may be damaged or password-protected. Supply an unprotected copy.", Core.ExitCodes.Failed, [e.Message]);
        }
    }

    sealed class Context(MainDocumentPart main, bool flat)
    {
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

        public void Block(OpenXmlElement block)
        {
            switch (block)
            {
                case Paragraph p: Paragraph(p); break;
                case Table t: Table(t); break;
                case SdtBlock sdt:
                    foreach (var child in sdt.SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>()) Block(child);
                    break;
            }
        }

        void Paragraph(Paragraph p)
        {
            var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var styleName = styleId is not null && styles.TryGetValue(styleId, out var st) ? st.StyleName?.Val?.Value ?? styleId : styleId ?? "";
            var text = Text(p).Trim();
            var imagesHere = Images(p);
            foreach (var img in imagesHere) Emit($"![]({img})");
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
                var start = md.Length;
                Emit($"{new string('#', level)} {(number is null || typed ? "" : number + " ")}{(typed ? text : shown)}");
                headings.Add(new FsdHeading(number, shown, level, md.Line, start, md.Length, typed, text));
                return;
            }
            if (label is not null || bullet)
            {
                var indent = new string(' ', 2 * listLevel);
                Emit(bullet || label is null ? $"{indent}- {text}" : Regex.IsMatch(label, @"^\d+\.$") ? $"{indent}{label} {text}" : $"{indent}- {label} {text}", tight: true);
                return;
            }
            Emit(text);
        }

        void Table(Table t)
        {
            var rows = t.Elements<TableRow>().Select(r => r.Elements<TableCell>().SelectMany(c =>
            {
                // Nested tables are flattened to their text; images keep their place as image links.
                var cell = string.Join(" ", c.Descendants<Paragraph>().Select(x => string.Join(" ", Images(x).Select(i => $"![]({i})").Append(Text(x).Trim())).Trim())
                    .Where(x => x.Length > 0)).Replace("|", "\\|");
                var span = c.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                return new[] { cell }.Concat(Enumerable.Repeat("", span - 1));
            }).ToList()).Where(r => r.Count > 0).ToList();
            if (rows.Count == 0) return;
            var width = rows.Max(r => r.Count);
            var sb = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                var cells = rows[i].Concat(Enumerable.Repeat("", width - rows[i].Count));
                sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                if (i == 0) sb.Append('|').Append(string.Concat(Enumerable.Repeat("---|", width))).Append('\n');
            }
            Emit(sb.ToString().TrimEnd('\n'));
        }

        void Emit(string block, bool tight = false) => md.Emit(block, tight);

        static string Text(OpenXmlElement p)
        {
            var sb = new StringBuilder();
            // Word stores a text box twice (the shape and a fallback for old readers): read it once.
            foreach (var e in p.Descendants().Where(d => !d.Ancestors().Any(a => a.LocalName == "Fallback")))
                switch (e)
                {
                    case Text t when t.Parent is Run: sb.Append(t.Text); break;
                    case TabChar: sb.Append(' '); break;
                    case Break or CarriageReturn: sb.Append(' '); break;
                    case NoBreakHyphen: sb.Append('-'); break;
                }
            return Regex.Replace(sb.ToString().Replace(' ', ' '), " {2,}", " ");
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
            var body = md.ToString();
            if (flat)
            {
                var (markdown, whole) = FsdSections.Flat(body, problems);
                return new ConvertedDocument(markdown, whole, images, problems, title);
            }
            if (headings.Count == 0)
                throw new Core.StangentException("no_structure", $"This .docx has no headings, so it has no sections. Apply heading styles, {FsdSections.NoStructureAdvice}.");
            var sections = FsdSections.Build(body, headings, problems);
            return new ConvertedDocument(body + "\n", sections, images, problems, title);
        }
    }
}
