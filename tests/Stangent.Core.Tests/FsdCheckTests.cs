using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary><c>stangent fsd check</c> (phase fs layer 3, D380, D382, D384, D393): ids on new headings are the only write; each
/// refusal writes nothing; a rename and a move keep their ids; a marker deleted from a retyped heading is refused, not read as a
/// new section; a damaged marker is caught, by the check and by the reader. No AI.</summary>
public sealed class FsdCheckTests : IDisposable
{
    readonly TempRepo repo = new();

    public void Dispose() => repo.Dispose();

    string Root => repo.Root;
    string Dir => FsdImport.DocDir(Root, "shop");
    string Chapter => Path.Combine(FsdChapters.Dir(Dir), "01-document.md");
    string Ledger => Path.Combine(Dir, "ids.json");

    /// <summary>S-0001 "1 Orders", S-0002 "1.1 Refunds", S-0003 "2 Glossary", committed.</summary>
    void Shop(params (string, string, string)[] sections) => FsdFixture.Write(Root, "shop", "Shop", sections.Length > 0 ? sections :
        [("1", "Orders", "Orders are placed from the cart."), ("1.1", "Refunds", "Refunds take five days."), ("2", "Glossary", "Terms.")]);

    JsonObject Check(params string[] removed) => Check(removed, []);

    JsonObject Check(string[] removed, string[] same)
    {
        FsdDocument.Forget();
        return FsdCheck.Run(repo.Repo, "shop", removed, same);
    }

    void Edit(string from, string to) => Chapters.Edit(Dir, from, to);

    static List<string> Kinds(JsonObject result, string list = "refused") =>
        [.. result[list]!.AsArray().OfType<JsonObject>().Select(r => (string)r["kind"]!)];

    static List<string> Ids(JsonObject result, string change) =>
        [.. result["changes"]![change]!.AsArray().OfType<JsonObject>().Select(c => (string)c["id"]!)];

    Dictionary<string, byte[]> Snapshot() => new(Chapters.Files(Dir)) { ["ids.json"] = File.ReadAllBytes(Ledger) };

    /// <summary>Refused, and nothing written: chapter files and the ledger as they were.</summary>
    JsonObject Refused(string kind, params string[] removed)
    {
        var before = Snapshot();
        var result = Check(removed, []);
        Assert.False((bool)result["ok"]!);
        Assert.Contains(kind, Kinds(result));
        Assert.Empty(result["idsGiven"]!.AsArray());
        var after = Snapshot();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before) Assert.Equal(bytes, after[name]);
        return result;
    }

    static JsonObject Section(JsonObject document, string permanentId) =>
        document["sections"]!.AsArray().OfType<JsonObject>().Single(s => (string?)s["permanentId"] == permanentId);

    [Fact]
    public void A_new_heading_gets_the_next_id_and_nothing_else_changes()
    {
        Shop();
        File.AppendAllText(Chapter, "\n## 1.2 Returns\nReturns are free.");
        var before = File.ReadAllText(Chapter);

        var result = Check();
        Assert.True((bool)result["ok"]!);
        var given = Assert.Single(result["idsGiven"]!.AsArray())!;
        Assert.Equal("S-0004", (string)given["id"]!);
        Assert.Equal(before.Replace("## 1.2 Returns", "## 1.2 Returns <!-- S-0004 -->", StringComparison.Ordinal), File.ReadAllText(Chapter));
        Assert.Equal(["S-0004"], Ids(result, "added"));
        Assert.Empty(Ids(result, "removed"));
        Assert.Empty(Ids(result, "renamed"));
        Assert.Contains("specs/fsd/shop/ids.json", result["uncommittedFiles"]!.AsArray().Select(f => (string)f!));

        // A second run gives nothing more, and still lists the section as added: HEAD doesn't have it yet.
        var again = Check();
        Assert.Empty(again["idsGiven"]!.AsArray());
        Assert.Equal(["S-0004"], Ids(again, "added"));

        Committed.All(Root, "the PM's new section");
        FsdDocument.Forget();
        Assert.Equal("Returns are free.", (string)Section(FsdDocument.Load(Root, "shop"), "S-0004")["text"]!);
        Assert.Equal("unchanged", (string)Check()["status"]!);
    }

    [Fact]
    public void A_rename_and_a_renumber_keep_the_id()
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->", "## 1.2 Refunds and returns <!-- S-0002 -->");
        Edit("Refunds take five days.", "Refunds take ten days.");

        var result = Check();
        Assert.True((bool)result["ok"]!);
        var renamed = Assert.Single(result["changes"]!["renamed"]!.AsArray())!;
        Assert.Equal(("S-0002", "1.1 Refunds", "1.2 Refunds and returns"), ((string)renamed["id"]!, (string)renamed["was"]!, (string)renamed["now"]!));
        Assert.Equal(["S-0002"], Ids(result, "changed"));
        Assert.Empty(Ids(result, "added"));
        Assert.Empty(Ids(result, "removed"));
        Assert.Empty(result["idsGiven"]!.AsArray());
    }

    [Fact]
    public void A_move_between_chapters_keeps_the_id_and_its_text()
    {
        Shop();
        Edit("\n# 2 Glossary <!-- S-0003 -->\nTerms.", "");
        File.WriteAllText(Path.Combine(FsdChapters.Dir(Dir), "02-s0003-glossary.md"), "# 2 Glossary <!-- S-0003 -->\nTerms.");

        var result = Check();
        Assert.True((bool)result["ok"]!);
        var moved = Assert.Single(result["changes"]!["moved"]!.AsArray())!;
        Assert.Equal(("S-0003", "01-document.md", "02-s0003-glossary.md"), ((string)moved["id"]!, (string)moved["fromFile"]!, (string)moved["toFile"]!));
        Assert.Empty(Ids(result, "added"));
        Assert.Empty(Ids(result, "removed"));
        Assert.Empty(Ids(result, "changed"));
        // A new chapter file is named: `git commit -a` would leave it out.
        Assert.Contains("specs/fsd/shop/chapters/02-s0003-glossary.md", result["uncommittedFiles"]!.AsArray().Select(f => (string)f!));

        Committed.All(Root, "move the glossary");
        FsdDocument.Forget();
        Assert.Equal("Terms.", (string)Section(FsdDocument.Load(Root, "shop"), "S-0003")["text"]!);
    }

    [Fact]
    public void A_marker_deleted_on_a_retyped_heading_is_refused_not_read_as_a_new_section()
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->", "## 1.1 Refund rules");

        var result = Refused("marker-removed");
        var refusal = result["refused"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["kind"]! == "marker-removed");
        Assert.Equal("S-0002", (string)refusal["id"]!);
        Assert.Contains("<!-- S-0002 -->", (string)refusal["message"]!, StringComparison.Ordinal);
        // Its words are the same, so it is named as the heading that looks like S-0002.
        Assert.Equal("1.1 Refund rules", (string)Assert.Single(refusal["looksLike"]!.AsArray())!["heading"]!);

        // Said on purpose, the old section goes and the heading is a new one.
        var confirmed = Check("S-0002");
        Assert.True((bool)confirmed["ok"]!);
        Assert.Equal(["S-0002"], Ids(confirmed, "removed"));
        Assert.Equal(["S-0004"], Ids(confirmed, "added"));
    }

    [Fact]
    public void A_heading_retyped_with_new_words_and_no_marker_is_refused_too()
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "## 1.1 Money back\nMoney comes back in ten days.");

        var result = Refused("marker-removed");
        Assert.Empty(result["refused"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["kind"]! == "marker-removed")["looksLike"]!.AsArray());
    }

    [Fact]
    public void A_section_removed_with_no_heading_unmarked_is_listed()
    {
        Shop();
        Edit("\n## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "");

        var result = Check();
        Assert.True((bool)result["ok"]!);
        Assert.Equal(["S-0002"], Ids(result, "removed"));
    }

    [Theory]
    [InlineData("<!--S-0002 nonumber-->")]
    [InlineData("<!-- S-0002")]
    [InlineData("<!-- s-0002 -->")]
    [InlineData("S-0002 -->")]
    public void A_damaged_marker_is_refused_by_the_check_and_by_the_reader(string damaged)
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->", $"## 1.1 Refunds {damaged}");

        Refused("damaged-marker");

        // Committed without the check, every step refuses the document rather than read the heading with no id.
        Committed.All(Root, "a damaged marker");
        FsdDocument.Forget();
        Assert.Equal("invalid_fsd", Assert.Throws<StangentException>(() => FsdDocument.Load(Root, "shop")).Code);
    }

    [Fact]
    public void Repairing_a_damaged_marker_that_was_committed_passes()
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->", "## 1.1 Refunds <!--S-0002 nonumber-->");
        Committed.All(Root, "a damaged marker");
        Edit("## 1.1 Refunds <!--S-0002 nonumber-->", "## 1.1 Refunds <!-- S-0002 -->");

        var result = Check();
        Assert.True((bool)result["ok"]!, string.Join("; ", Kinds(result)));

        // Put on another heading instead, its rulings would land there: refused.
        Edit("## 1.1 Refunds <!-- S-0002 -->", "## 1.1 Refunds");
        Edit("# 2 Glossary <!-- S-0003 -->", "# 2 Glossary <!-- S-0003 -->\n## 2.1 Billing <!-- S-0002 -->");
        Refused("old-id");
    }

    [Fact]
    public void A_marker_off_its_heading_is_refused()
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->\nRefunds", "## 1.1 Refunds\n<!-- S-0002 --> Refunds");

        var result = Refused("marker-off-heading");
        Assert.Contains("marker-removed", Kinds(result));
    }

    [Fact]
    public void A_text_arrow_is_not_a_marker()
    {
        var read = FsdChapters.Read([("01.md", "# 1 States <!-- S-0001 -->\nS-1 --> S-2 when paid.")]);
        Assert.Empty(read.Damaged);
    }

    [Fact]
    public void A_copied_heading_with_its_marker_is_refused()
    {
        Shop();
        File.AppendAllText(Chapter, "\n## 2.1 Refunds <!-- S-0002 -->\nA copy.");
        Refused("repeated-id");
    }

    [Theory]
    [InlineData("unknown-id")]
    [InlineData("removed-id")]
    public void An_id_the_ledger_never_gave_or_marks_removed_is_refused(string kind)
    {
        Shop();
        if (kind == "unknown-id") File.AppendAllText(Chapter, "\n# 3 Notes <!-- S-0099 -->\nNotes.");
        else
        {
            FsdVersions.Ledger(Root, "shop").Remove("sections", "S-0003");
            Committed.All(Root, "the ledger marks S-0003 removed");
        }
        Refused(kind);
    }

    [Fact]
    public void An_old_id_typed_onto_a_new_heading_is_refused()
    {
        Shop();
        Edit("\n# 2 Glossary <!-- S-0003 -->\nTerms.", "");
        Committed.All(Root, "the glossary goes");
        File.AppendAllText(Chapter, "\n# 2 Billing <!-- S-0003 -->\nInvoices go out monthly.");

        Refused("old-id");
    }

    [Fact]
    public void A_ledger_that_went_back_is_refused()
    {
        Shop();
        var ledger = JsonNode.Parse(File.ReadAllText(Ledger))!;
        ledger["scopes"]!["sections"]!["last"] = 1;
        File.WriteAllText(Ledger, ledger.ToJsonString());
        File.AppendAllText(Chapter, "\n# 3 Notes\nNotes.");

        Refused("ledger-went-back");
    }

    [Fact]
    public void A_marker_moved_onto_a_new_heading_is_refused()
    {
        Shop();
        // The PM typed a new section and put 1.1's marker on it, leaving 1.1 with none.
        Edit("## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "## 1.1 Refunds\nRefunds take five days.\n## 1.2 Returns <!-- S-0002 -->\nReturns are free.");

        var result = Refused("marker-moved");
        Assert.Equal("S-0002", (string)result["refused"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["kind"]! == "marker-moved")["id"]!);
    }

    [Fact]
    public void Markers_swapped_between_headings_are_refused()
    {
        Shop();
        Edit("## 1.1 Refunds <!-- S-0002 -->", "## 1.1 Refunds <!-- S-XXXX -->");
        Edit("# 2 Glossary <!-- S-0003 -->", "# 2 Glossary <!-- S-0002 -->");
        Edit("<!-- S-XXXX -->", "<!-- S-0003 -->");

        Refused("marker-on-other-section");
    }

    [Fact]
    public void Two_headings_of_one_title_swapping_markers_are_refused_by_their_words()
    {
        Shop(("1", "Orders", "Orders."), ("1.1", "Overview", "Orders go through the cart."), ("2", "Billing", "Billing."), ("2.1", "Overview", "Invoices go out monthly."));
        Edit("## 1.1 Overview <!-- S-0002 -->", "## 1.1 Overview <!-- S-XXXX -->");
        Edit("## 2.1 Overview <!-- S-0004 -->", "## 2.1 Overview <!-- S-0002 -->");
        Edit("<!-- S-XXXX -->", "<!-- S-0004 -->");

        Refused("marker-on-other-section");
    }

    [Fact]
    public void A_marker_laid_over_another_heading_whose_section_goes_is_refused()
    {
        Shop();
        Ruled("S-0002", "fsd-right");
        // 1.1 and its words deleted, and its marker typed over the glossary's: S-0002's ruling would judge the glossary.
        Edit("\n## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "");
        Edit("# 2 Glossary <!-- S-0003 -->", "# 2 Glossary <!-- S-0002 -->");

        Refused("marker-on-other-section");
    }

    [Fact]
    public void Two_sections_sharing_placeholder_words_stay_apart()
    {
        Shop(("1", "Orders", "TBD."), ("2", "Billing", "TBD."), ("3", "Glossary", "Terms."));
        Edit("Terms.", "TBD.");

        var result = Check();
        Assert.True((bool)result["ok"]!, string.Join("; ", Kinds(result)));
        Assert.Equal(["S-0003"], Ids(result, "changed"));
    }

    [Fact]
    public void A_new_level_skip_is_refused_and_a_committed_one_only_warned()
    {
        Shop();
        File.AppendAllText(Chapter, "\n#### Deep\nToo deep.");
        Refused("level-skip");

        using var other = new TempRepo();
        FsdFixture.Write(other.Root, "shop", "Shop", ("1", "Orders", "Orders."), ("1.1.1", "Deep", "Deep."));
        FsdDocument.Forget();
        var result = FsdCheck.Run(other.Repo, "shop");
        Assert.True((bool)result["ok"]!);
        Assert.Contains("level-skip", Kinds(result, "warnings"));
    }

    [Fact]
    public void Repeated_and_out_of_order_numbers_are_warnings()
    {
        Shop();
        Edit("# 2 Glossary", "# 1 Glossary");
        File.AppendAllText(Chapter, "\n# 0 Notes\nNotes.");

        var result = Check();
        Assert.True((bool)result["ok"]!);
        var warnings = Kinds(result, "warnings");
        Assert.Contains("repeated-number", warnings);
        Assert.Contains("number-out-of-order", warnings);
    }

    void Ruled(string permanentId, string ruling)
    {
        var file = new RulingsFile { Doc = "shop", ReviewCommit = "abc", FsdVersion = "v", ReviewHash = "h", Runs = 2 };
        var section = new RulingSection { Section = "1.1", SectionId = permanentId, Title = "Refunds", TextHash = "t" };
        section.Entries.Add(new RulingEntry { KeySection = permanentId, KeyQuote = "Refunds take five days.", State = "conflict", Ruling = ruling, Runs = [1, 2] });
        file.Sections.Add(section);
        File.WriteAllText(Rulings.PathFor(Root, "shop"), Rulings.Write(file));
        Committed.All(Root, "the PM's ruling");
    }

    [Fact]
    public void A_section_removed_while_it_holds_a_ruling_is_refused_with_its_entries_unless_said_on_purpose()
    {
        Shop();
        Ruled("S-0002", "fsd-right");
        Edit("\n## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "");

        var result = Refused("rulings-on-removed");
        var entry = Assert.Single(result["refused"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["kind"]! == "rulings-on-removed")["entries"]!.AsArray())!;
        Assert.Equal("fsd-right", (string)entry["ruling"]!);

        // Deleting the entry from rulings.yaml in the same edit doesn't hide it: HEAD's file still has it.
        File.WriteAllText(Rulings.PathFor(Root, "shop"), Rulings.Write(new RulingsFile { Doc = "shop", ReviewCommit = "abc", FsdVersion = "v", ReviewHash = "h", Runs = 2 }));
        Refused("rulings-on-removed");

        var confirmed = Check("S-0002");
        Assert.True((bool)confirmed["ok"]!);
        var removed = Assert.Single(confirmed["changes"]!["removed"]!.AsArray())!;
        Assert.Single(removed["rulingsLeft"]!.AsArray());
    }

    [Fact]
    public void A_ruled_section_given_a_new_heading_and_new_words_is_refused_unless_said_the_same()
    {
        Shop();
        Ruled("S-0002", "code-right");
        Edit("## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "## 1.1 Store credit <!-- S-0002 -->\nCredit never expires.");

        Refused("rewritten-with-rulings");
        var same = Check([], ["S-0002"]);
        Assert.True((bool)same["ok"]!, string.Join("; ", Kinds(same)));
        Assert.Equal(["S-0002"], Ids(same, "renamed"));
    }

    [Fact]
    public void A_new_heading_taking_a_ruled_sections_marker_is_refused()
    {
        Shop();
        Ruled("S-0002", "fsd-right");
        // The old heading retitled and rewritten with no marker, and a new heading took its marker.
        Edit("## 1.1 Refunds <!-- S-0002 -->\nRefunds take five days.", "## 1.1 Shipping\nParcels ship in two days.\n## 1.2 Other <!-- S-0002 -->\nAnything else.");

        Refused("rewritten-with-rulings");
    }

    [Fact]
    public void Removed_names_only_a_section_gone_since_head()
    {
        Shop();
        Assert.Equal("usage", Assert.Throws<StangentException>(() => Check("S-0002")).Code);
    }

    [Fact]
    public void A_first_import_not_committed_lists_every_section_as_added()
    {
        using var archive = new TempRepo();
        using var fresh = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive.Root}', git: false }} }}\n");
        Committed.All(fresh.Root, "the repo before the import");
        var file = Path.Combine(fresh.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# 1 Orders\nOrders.\n### 1.1.1 Deep\nDeep."));
        FsdImport.Import(fresh.Repo, file);

        FsdDocument.Forget();
        var result = FsdCheck.Run(fresh.Repo, "shop");
        Assert.True((bool)result["ok"]!, string.Join("; ", Kinds(result)));
        Assert.Equal(2, Ids(result, "added").Count);
        Assert.Contains("level-skip", Kinds(result, "warnings"));
    }
}
