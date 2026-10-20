using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Writes a real Word document from a small outline, so tests and the benchmark's planted FSD exercise what Word does:
/// automatic heading numbering (on the paragraph or through the heading style), numbers typed into headings, plain
/// headings, bulleted and numbered lists, tables and captioned images. Not a general Markdown → Word converter.
/// </summary>
/// <remarks>
/// Outline lines: <c># Title</c> (heading 1–3 by the count of #), <c>#! Title</c> (Word numbers it), <c>% Title</c>
/// (document title), <c>- item</c>, <c>  - item</c>, <c>1. item</c> (Word numbers it), <c>| a | b |</c> (a table row;
/// the first row is the header), <c>![caption](path.png)</c>, anything else a paragraph. Blank lines end a block.
/// </remarks>
public static class DocxWriter
{
    const int HeadingNumbers = 1, Bullets = 2, Ordered = 3;

    /// <param name="numberingOnStyle">Link automatic heading numbering to the heading styles (as Word's multilevel lists
    /// do), instead of putting it on each heading paragraph.</param>
    public static byte[] Write(string outline, string? imageDir = null, bool numberingOnStyle = false)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            AddStyles(main, numberingOnStyle);
            AddNumbering(main, numberingOnStyle);
            var orderedRestart = true;
            var orderedInstance = Ordered;
            var nextInstance = 10;
            var table = new List<string[]>();
            uint imageId = 1;

            void FlushTable()
            {
                if (table.Count == 0) return;
                var t = new Table(new TableProperties(new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
                foreach (var row in table)
                    t.Append(new TableRow(row.Select(c => new TableCell(new Paragraph(new Run(new Text(c) { Space = SpaceProcessingModeValues.Preserve }))))));
                body.Append(t);
                body.Append(new Paragraph());
                table.Clear();
            }

            foreach (var raw in outline.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (!line.TrimStart().StartsWith('|')) FlushTable();
                if (!Regex.IsMatch(line, @"^\s*\d+\.\s")) orderedRestart = true;
                if (line.Length == 0) continue;

                if (line.StartsWith('|'))
                {
                    var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (cells.All(c => Regex.IsMatch(c, "^-+$"))) continue;
                    table.Add(cells);
                }
                else if (Regex.Match(line, @"^(#{1,3})(!?)\s+(.*)$") is { Success: true } h)
                {
                    var level = h.Groups[1].Length;
                    var props = new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{level}" });
                    if (h.Groups[2].Length > 0 && !numberingOnStyle)
                        props.Append(new NumberingProperties(new NumberingLevelReference { Val = level - 1 }, new NumberingId { Val = HeadingNumbers }));
                    if (h.Groups[2].Length == 0 && numberingOnStyle)
                        props.Append(new NumberingProperties(new NumberingId { Val = 0 })); // this heading opts out
                    body.Append(new Paragraph(props, TextRun(h.Groups[3].Value)));
                }
                else if (line.StartsWith("% "))
                    body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Title" }), TextRun(line[2..])));
                else if (Regex.Match(line, @"^(\s*)-\s+(.*)$") is { Success: true } b)
                    body.Append(ListItem(Bullets, b.Groups[1].Length / 2, b.Groups[2].Value));
                else if (Regex.Match(line, @"^(\s*)\d+\.\s+(.*)$") is { Success: true } o)
                {
                    if (orderedRestart)
                    {
                        // Each numbered list starts again at 1: a new list instance over the same definition.
                        orderedInstance = nextInstance++;
                        main.NumberingDefinitionsPart!.Numbering!.Append(new NumberingInstance(new AbstractNumId { Val = Ordered },
                            new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 }) { NumberID = orderedInstance });
                        orderedRestart = false;
                    }
                    body.Append(ListItem(orderedInstance, o.Groups[1].Length / 2, o.Groups[2].Value));
                }
                else if (Regex.Match(line, @"^!\[(.*)\]\((.*)\)$") is { Success: true } img)
                {
                    var path = Path.Combine(imageDir ?? ".", img.Groups[2].Value);
                    var part = main.AddImagePart(ImagePartType.Png);
                    using (var f = File.OpenRead(path)) part.FeedData(f);
                    body.Append(new Paragraph(new Run(Image(main.GetIdOfPart(part), imageId++, img.Groups[2].Value))));
                    if (img.Groups[1].Length > 0)
                        body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Caption" }), TextRun(img.Groups[1].Value)));
                }
                else body.Append(new Paragraph(TextRun(line)));
            }
            FlushTable();
            main.Document.Save();
        }
        return ms.ToArray();
    }

    static Run TextRun(string text) => new(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

    static Paragraph ListItem(int numId, int level, string text) => new(
        new ParagraphProperties(new ParagraphStyleId { Val = "ListParagraph" },
            new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = numId })),
        TextRun(text));

    static void AddStyles(MainDocumentPart main, bool numberingOnStyle)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles(
            new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            new Style(new StyleName { Val = "Title" }, new BasedOn { Val = "Normal" },
                new StyleRunProperties(new Bold(), new FontSize { Val = "48" })) { Type = StyleValues.Paragraph, StyleId = "Title" },
            new Style(new StyleName { Val = "caption" }, new BasedOn { Val = "Normal" },
                new StyleRunProperties(new Italic())) { Type = StyleValues.Paragraph, StyleId = "Caption" },
            new Style(new StyleName { Val = "List Paragraph" }, new BasedOn { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "ListParagraph" });
        for (var level = 1; level <= 3; level++)
        {
            var ppr = new StyleParagraphProperties(new KeepNext(), new OutlineLevel { Val = level - 1 });
            if (numberingOnStyle) ppr.InsertAfter(new NumberingProperties(new NumberingId { Val = HeadingNumbers }), ppr.GetFirstChild<KeepNext>());
            styles.Append(new Style(new StyleName { Val = $"heading {level}" }, new BasedOn { Val = "Normal" }, ppr,
                new StyleRunProperties(new Bold(), new FontSize { Val = (36 - 4 * level).ToString() })) { Type = StyleValues.Paragraph, StyleId = $"Heading{level}" });
        }
        part.Styles = styles;
    }

    static void AddNumbering(MainDocumentPart main, bool numberingOnStyle)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        Level Lvl(int i, NumberFormatValues fmt, string text, string? style = null)
        {
            var l = new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = fmt }, new LevelText { Val = text },
                new PreviousParagraphProperties(new Indentation { Left = (360 * (i + 1)).ToString(), Hanging = "360" })) { LevelIndex = i };
            if (style is not null) l.InsertAfter(new ParagraphStyleIdInLevel { Val = style }, l.GetFirstChild<NumberingFormat>());
            return l;
        }
        part.Numbering = new Numbering(
            new AbstractNum(Lvl(0, NumberFormatValues.Decimal, "%1.", numberingOnStyle ? "Heading1" : null),
                Lvl(1, NumberFormatValues.Decimal, "%1.%2.", numberingOnStyle ? "Heading2" : null),
                Lvl(2, NumberFormatValues.Decimal, "%1.%2.%3.", numberingOnStyle ? "Heading3" : null)) { AbstractNumberId = HeadingNumbers },
            new AbstractNum(Lvl(0, NumberFormatValues.Bullet, "•"), Lvl(1, NumberFormatValues.Bullet, "◦")) { AbstractNumberId = Bullets },
            new AbstractNum(Lvl(0, NumberFormatValues.Decimal, "%1."), Lvl(1, NumberFormatValues.LowerLetter, "%2)")) { AbstractNumberId = Ordered },
            new NumberingInstance(new AbstractNumId { Val = HeadingNumbers }) { NumberID = HeadingNumbers },
            new NumberingInstance(new AbstractNumId { Val = Bullets }) { NumberID = Bullets },
            new NumberingInstance(new AbstractNumId { Val = Ordered }) { NumberID = Ordered });
    }

    static Drawing Image(string relId, uint id, string name)
    {
        const long cx = 3_000_000, cy = 1_800_000;
        return new Drawing(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.DocProperties { Id = id, Name = name },
            new A.Graphic(new A.GraphicData(new PIC.Picture(
                new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = id, Name = name }, new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                    new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle }))) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });
    }
}
