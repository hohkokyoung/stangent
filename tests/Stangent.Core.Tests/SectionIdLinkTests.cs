using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Links store a section's permanent id beside its number (D121).</summary>
public sealed class SectionIdLinkTests : IDisposable
{
    const string Apples = "Apples are sorted by size before they are packed.";
    const string Pears = "Pears wait three days in the cold room first.";
    const string Figs = "Figs are only sold to trade customers on request.";

    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public SectionIdLinkTests() => repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    JsonObject Import(string body)
    {
        var file = Path.Combine(archive.Root, "shop.docx");
        File.WriteAllBytes(file, FsdDocxTests.Docx(body));
        return FsdImport.Import(repo.Repo, file, "shop");
    }

    static string Chapter(string title, string text) => FsdDocxTests.H(title) + FsdDocxTests.P(text);

    JsonObject Section(string number) => FsdImport.Documents(repo.Root).Single()["sections"]!.AsArray().OfType<JsonObject>().Single(s => (string)s["id"]! == number);

    /// <summary>A link as an older build wrote it: the number alone.</summary>
    void OldLink(string number, string quote)
    {
        var entry = LinkRun.Entry("gap", "shop", Section(number), quote, null, null, null, "code", null, null);
        entry["fsd"]!.AsObject().Remove("sectionId");
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add(entry);
        TraceFile.Save(repo.Root, trace);
    }

    JsonObject Link() => (JsonObject)TraceFile.Load(repo.Root)["entries"]!.AsArray().Single()!["fsd"]!;

    [Fact]
    public void A_new_link_has_it_and_an_old_one_gets_it_at_a_plain_check_with_its_entry_id_unchanged()
    {
        Import(Chapter("1 Apples", Apples) + Chapter("2 Pears", Pears));
        Assert.Equal("S-0002", (string)LinkRun.Entry("gap", "shop", Section("2"), Pears, null, null, null, "code", null, null)["fsd"]!["sectionId"]!);

        OldLink("2", Pears);
        var id = (string)TraceFile.Load(repo.Root)["entries"]![0]!["id"]!;
        Assert.Null(Link()["sectionId"]);
        Assert.True((bool)TraceCheck.Run(repo.Root, null, null, false)["ok"]!);
        Assert.Equal(("2", "S-0002"), ((string)Link()["section"]!, (string)Link()["sectionId"]!));
        Assert.Equal(id, (string)TraceFile.Load(repo.Root)["entries"]![0]!["id"]!);
    }

    [Fact]
    public void A_typed_id_is_put_right_by_the_next_check()
    {
        Import(Chapter("1 Apples", Apples) + Chapter("2 Pears", Pears));
        OldLink("2", Pears);
        var trace = TraceFile.Load(repo.Root);
        trace["entries"]![0]!["fsd"]!["sectionId"] = "S-0001";
        TraceFile.Save(repo.Root, trace);
        TraceCheck.Run(repo.Root, null, null, false);
        Assert.Equal("S-0002", (string)Link()["sectionId"]!);
    }

    [Fact]
    public void A_renumbered_section_keeps_its_id_and_the_number_moves()
    {
        Import(Chapter("1 Apples", Apples) + Chapter("2 Pears", Pears));
        OldLink("2", Pears);
        TraceCheck.Run(repo.Root, null, null, false);

        Assert.Equal("candidate", (string)Import(Chapter("1 Figs", Figs) + Chapter("2 Apples", Apples) + Chapter("3 Pears", Pears))["status"]!);
        var promoted = FsdVersions.Promote(repo.Repo, "shop", "Pat Example");
        Assert.Empty(promoted["carried"]!["sectionIdsDisagree"]!.AsArray());
        Assert.Equal(("3", "S-0002"), ((string)Link()["section"]!, (string)Link()["sectionId"]!));
        Assert.Equal("S-0002", (string)Section("3")["permanentId"]!);
    }

    [Fact]
    public void A_removed_section_keeps_its_id_beside_the_mark()
    {
        Import(Chapter("1 Apples", Apples) + Chapter("2 Pears", Pears));
        OldLink("2", Pears);
        Import(Chapter("1 Apples", Apples));
        FsdVersions.Promote(repo.Repo, "shop", "Pat Example");
        Assert.Equal(("(removed S-0002) 2", "S-0002"), ((string)Link()["section"]!, (string)Link()["sectionId"]!));
    }

    [Fact]
    public void Promotion_lists_a_link_whose_number_and_id_name_different_sections()
    {
        Import(Chapter("1 Apples", Apples) + Chapter("2 Pears", Pears));
        OldLink("2", Pears);
        var trace = TraceFile.Load(repo.Root);
        trace["entries"]![0]!["fsd"]!["sectionId"] = "S-0001";
        TraceFile.Save(repo.Root, trace);

        Import(Chapter("1 Figs", Figs) + Chapter("2 Apples", Apples) + Chapter("3 Pears", Pears));
        var bad = Assert.Single(FsdVersions.Promote(repo.Repo, "shop", "Pat Example")["carried"]!["sectionIdsDisagree"]!.AsArray())!;
        Assert.Equal(("S-0001", "S-0002"), ((string)bad["sectionId"]!, (string)bad["numberNames"]!));
    }
}
