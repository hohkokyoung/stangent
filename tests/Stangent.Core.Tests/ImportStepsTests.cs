using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Tickets;

namespace Stangent.Core.Tests;

/// <summary>The steps every format fills in, and a ticket attachment through them (D121).</summary>
public sealed class ImportStepsTests
{
    static byte[] OnePagePdf(params string[] lines)
    {
        using var pdf = new PdfFixture();
        pdf.Page(lines);
        return pdf.Build(PdfFixture.Mark(lines[0], 0, 1));
    }

    static (string, string)[] Steps(JsonArray steps) => [.. steps.Select(s => ((string)s!["step"]!, (string)s["result"]!))];

    [Fact]
    public void Word_and_pdf_fill_in_every_step_and_say_which_they_dont_have()
    {
        var word = ImportPipeline.Run(ImportPipeline.ForFile("a.docx")!, FsdDocxTests.Docx(FsdDocxTests.H("Rules") + FsdDocxTests.P("Text.")), new());
        Assert.Equal([("safety", "ran"), ("convert", "ran"), ("comments", "ran"), ("extract-files", "ran"), ("completeness", "ran")], Steps(word.StepsJson()));
        var pdf = ImportPipeline.Run(ImportPipeline.ForFile("a.PDF")!, OnePagePdf("Rules", "Text."), new());
        Assert.Equal([("safety", "ran"), ("convert", "ran"), ("comments", "not-for-format"), ("extract-files", "ran"), ("completeness", "ran")], Steps(pdf.StepsJson()));
        Assert.Contains("annotations", pdf.Steps[2].Note);
    }

    [Fact]
    public void The_steps_give_what_the_converter_gives()
    {
        var bytes = FsdDocxTests.Docx(FsdDocxTests.H("Rules") + FsdDocxTests.P("Text."));
        var run = ImportPipeline.Run(ImportPipeline.ForFile("a.docx")!, bytes, new()).Document;
        var direct = DocxConverter.Convert(bytes);
        Assert.Equal(direct.Markdown, run.Markdown);
        Assert.Equal(direct.Problems, run.Problems);
    }

    [Fact]
    public void The_safety_step_stops_a_file_with_tracked_changes_a_damaged_word_file_and_a_damaged_pdf()
    {
        var tracked = FsdDocxTests.Docx(FsdDocxTests.H("Rules") + """<w:p><w:ins w:id="1" w:author="a"><w:r><w:t>new</w:t></w:r></w:ins></w:p>""");
        var word = ImportPipeline.ForFile("a.docx")!;
        Assert.Equal("docx_tracked_changes", Assert.Throws<StangentException>(() => word.Safety(tracked, new())).Code);
        Assert.Equal(1, word.Safety(tracked, new(AcceptChanges: true)).Count);
        Assert.Equal("invalid_docx", Assert.Throws<StangentException>(() => word.Safety([1, 2, 3], new())).Code);
        Assert.Equal("invalid_pdf", Assert.Throws<StangentException>(() => ImportPipeline.ForFile("a.pdf")!.Safety([1, 2, 3], new())).Code);
    }

    [Fact]
    public void A_format_with_no_import_has_no_steps()
    {
        Assert.Null(ImportPipeline.ForFile("a.xlsx"));
        Assert.Null(ImportPipeline.Attachment("a.xlsx", [1]));
    }

    [Fact]
    public void An_attachment_is_read_flat_when_it_has_no_structure_and_listed_when_it_cant_be_read()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Refunds are paid within five days.");
        var flat = ImportPipeline.Attachment("a.pdf", pdf.Build())!;
        Assert.Contains("Refunds are paid within five days.", flat.Text);
        Assert.Equal("flat", (string)flat.Problems[0]!["kind"]!);

        var broken = ImportPipeline.Attachment("a.pdf", [1, 2, 3])!;
        Assert.Null(broken.Text);
        Assert.Contains("can't be read", broken.NotConverted);
    }

    [Fact]
    public async Task A_pdf_on_a_ticket_converts_and_a_broken_one_doesnt_stop_the_pull_and_neither_reaches_the_bundle_as_problems()
    {
        using var pdf = new PdfFixture();
        pdf.Page("Refunds are paid within five days.");
        using var ticketRepo = TicketFixtures.Repo();
        TicketFixtures.Make(TicketFixtures.ReplayDir(ticketRepo), "SON-7", "Task", "x", "See spec.pdf", files:
        [
            new("1", "spec.pdf", "application/pdf", pdf.Build()),
            new("2", "broken.pdf", "application/pdf", [37, 80, 68, 70]),
        ]);
        await using var tracker = Trackers.Open(ticketRepo.Repo, new Stangent.Tickets.Secrets.MemorySecretStore(), TextWriter.Null, new FolderRawSink(TicketPaths.TicketsDir(ticketRepo.Root)));
        var t = await TicketPull.RunAsync(ticketRepo.Repo, tracker, "SON-7", TestContext.Current.CancellationToken);
        var a = t["attachments"]!.AsArray().Select(x => x!.AsObject()).ToDictionary(x => (string)x["fileName"]!);
        Assert.Contains("Refunds are paid", (string)a["spec.pdf"]["text"]!);
        Assert.Equal(("other", "pdf"), ((string)a["spec.pdf"]["kind"]!, (string)a["spec.pdf"]["format"]!));
        Assert.Equal("flat", (string)a["spec.pdf"]["problems"]![0]!["kind"]!);
        Assert.Null(a["broken.pdf"]["text"]);
        Assert.NotNull(a["broken.pdf"]["notConverted"]);
    }
}
