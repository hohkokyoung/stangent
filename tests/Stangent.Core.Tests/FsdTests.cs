using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

public class FsdTests
{
    const string Outline = """
        % Shop FSD
        #! Orders
        Customers place orders from the cart.
        ##! Placing an order
        The order screen shows the cart total.
        1. Pick a delivery address.
        2. Confirm the order.
        - Orders over 100 need approval.
          - Approval comes from a manager.
        | Field | Rule |
        |---|---|
        | Total | at most 100 |
        ##! Cancelling
        An order can be cancelled until it ships.
        1. Open the order.
        ![The order screen](order.png)
        # 2 Settings
        ## 2.1 Tags
        At most 3 tags per order.
        # Calendar
        ## Views
        The calendar shows a week or a month.
        """;

    static string Png(string dir)
    {
        var path = Path.Combine(dir, "order.png");
        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAwS2OUAAAAABJRU5ErkJggg=="));
        return dir;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_docx_converts_to_markdown_with_its_own_numbers_or_heading_paths(bool numberingOnStyle)
    {
        using var temp = new TempRepo();
        var doc = DocxConverter.Convert(DocxWriter.Write(Outline, Png(temp.Root), numberingOnStyle));
        Assert.Equal(
            ["1", "1.1", "1.2", "2", "2.1", "Calendar", "Calendar / Views"],
            doc.Sections.Select(s => s.Id));
        Assert.Equal(["number", "number", "number", "number", "number", "path", "path"], doc.Sections.Select(s => s.IdKind));
        Assert.Equal("Shop FSD", doc.Title);
        Assert.Equal("1", doc.Sections.Single(s => s.Id == "1.1").Parent);
        Assert.Contains("# 1 Orders", doc.Markdown);
        Assert.Contains("## 1.1 Placing an order", doc.Markdown);
        Assert.Contains("1. Pick a delivery address.\n2. Confirm the order.", doc.Markdown);
        Assert.Contains("1. Open the order.", doc.Markdown);                            // a new list starts again at 1
        Assert.Contains("- Orders over 100 need approval.\n  - Approval comes from a manager.", doc.Markdown);
        Assert.Contains("| Field | Rule |\n|---|---|\n| Total | at most 100 |", doc.Markdown);
        Assert.Equal("At most 3 tags per order.", doc.Sections.Single(s => s.Id == "2.1").Text);
        var image = Assert.Single(doc.Images);
        Assert.Equal("The order screen", image.Caption);
        Assert.Contains($"![]({image.Name})", doc.Markdown);
        Assert.Empty(doc.Problems);
        var lines = doc.Markdown.Split('\n');
        Assert.All(doc.Sections, s => Assert.EndsWith(s.Title, lines[s.Line - 1]));
    }

    [Fact]
    public void Duplicate_and_missing_numbers_are_reported_not_guessed()
    {
        var doc = DocxConverter.Convert(DocxWriter.Write("""
            # 1 Series
            ## 1.1 List
            ## 1.1 Details
            ## Editing
            ## 1.4 Deleting
            """));
        Assert.Equal(["1", "1.1", "1.1 (2)", "Series / Editing", "1.4"], doc.Sections.Select(s => s.Id));
        Assert.Contains(doc.Problems, p => p.Contains("section id \"1.1\" is used again"));
        Assert.Contains(doc.Problems, p => p.Contains("heading \"Editing\" has no number among numbered headings"));
    }

    [Fact]
    public void A_number_at_the_start_of_a_heading_is_kept_only_when_it_fits_the_numbering()
    {
        var doc = DocxConverter.Convert(DocxWriter.Write("""
            # 1 Series
            ## 1.1 List
            ## 2.4 Out of place
            # 2023 Roadmap
            # 2 Calendar
            # 24 hour clock
            """));
        Assert.Equal(["1", "1.1", "Series / 2.4 Out of place", "2023 Roadmap", "2", "24 hour clock"], doc.Sections.Select(s => s.Id));
        Assert.Contains(doc.Problems, p => p.Contains("\"2023 Roadmap\" starts with 2023, which doesn't fit"));
        Assert.Contains(doc.Problems, p => p.Contains("\"24 hour clock\" starts with 24"));
        Assert.Contains("# 2023 Roadmap", doc.Markdown);
    }

    [Fact]
    public void Import_archives_the_original_and_the_same_file_always_gives_the_same_bytes()
    {
        using var archive = new TempRepo();
        string Config() => $"version: 1\nfsd: {{ archive: {{ name: client-docs, path: '{archive.Root}', git: false }} }}\n";
        using var a = new TempRepo(Config());
        using var b = new TempRepo(Config());
        var file = Path.Combine(Png(archive.Root), "Shop FSD.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline, archive.Root));

        var first = FsdImport.Import(a.Repo, file);
        Assert.Equal(("shop-fsd", "imported", 7), ((string)first["id"]!, (string)first["status"]!, (int)first["sections"]!));
        FsdImport.Import(b.Repo, file);
        foreach (var name in new[] { "document.md", "document.json" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(FsdImport.DocDir(a.Root, "shop-fsd"), name)), File.ReadAllBytes(Path.Combine(FsdImport.DocDir(b.Root, "shop-fsd"), name)));

        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(FsdImport.DocDir(a.Root, "shop-fsd"), "document.json")))!;
        Assert.Empty(SchemaRegistry.Embedded.Validate(FsdImport.Schema, doc.ToJsonString()));
        Assert.Equal("client-docs", (string)doc["original"]!["archive"]!);
        Assert.True(File.Exists(Path.Combine(archive.Root, (string)doc["original"]!["path"]!)));
        Assert.True(File.Exists(Path.Combine(FsdImport.DocDir(a.Root, "shop-fsd"), (string)doc["images"]![0]!["file"]!)));

        var again = FsdImport.Import(a.Repo, file);
        Assert.Equal("unchanged", (string)again["status"]!);
    }

    [Fact]
    public void A_git_archive_commits_the_original()
    {
        using var archive = new TempRepo();
        ShopCopy.Git(archive.Root, "init -q");
        ShopCopy.Git(archive.Root, "config user.name test");
        ShopCopy.Git(archive.Root, "config user.email test@example.invalid");
        using var repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive.Root}', git: true }} }}\n");
        var file = Path.Combine(repo.Root, "fsd.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# 1 Series\nText."));
        FsdImport.Import(repo.Repo, file);
        Assert.Contains("stangent: archive fsd/", Graph.Git.Run(archive.Root, "log --oneline"));
    }

    [Fact]
    public void Import_needs_an_archive()
    {
        using var repo = new TempRepo();
        var file = Path.Combine(repo.Root, "fsd.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# 1 Series"));
        Assert.Equal("archive_not_set", Assert.Throws<StangentException>(() => FsdImport.Import(repo.Repo, file)).Code);
    }
}
