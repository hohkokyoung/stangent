using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>Cutting a section into claims (D143), on small hand-written text. Every test also runs the words check, since a cut that loses a word is worse than no cut.</summary>
public class FsdClaimTests
{
    static List<FsdClaim> Split(string text)
    {
        var claims = FsdClaims.Split(text);
        Assert.Null(FsdClaims.CheckWords(text, claims));
        Assert.Equal(Enumerable.Range(1, claims.Count), claims.Select(c => c.Position));
        return claims;
    }

    static string[] Words(IEnumerable<FsdClaim> claims) => [.. claims.Select(c => c.Words)];

    // ---- sentences ----

    [Fact]
    public void A_paragraph_is_cut_into_sentences_with_their_words_kept()
    {
        var claims = Split("Orders over 50 need approval. Is it paid? Then ship it!\n\nA second paragraph\nruns on a line.");
        Assert.Equal(["Orders over 50 need approval.", "Is it paid?", "Then ship it!", "A second paragraph runs on a line."], Words(claims));
        Assert.All(claims, c => { Assert.Equal(FsdClaims.Kinds.Sentence, c.Kind); Assert.Null(c.Problem); Assert.Null(c.Flow); });
    }

    [Fact]
    public void A_stop_that_may_not_end_a_sentence_is_not_cut_and_is_listed()
    {
        foreach (var text in new[] { "See Dr. Smith for this.", "Written by A. Jones today.", "Use e.g. Firefox here.", "The limit is 5. 10 are left." })
        {
            var claim = Assert.Single(Split(text));
            Assert.Equal(text, claim.Words);
            Assert.Equal(FsdClaims.Problems.MaybeSentenceEnd, claim.Problem);
        }
    }

    [Fact]
    public void A_stop_inside_a_word_a_code_span_or_before_lower_case_is_no_cut_and_no_doubt()
    {
        foreach (var text in new[] { "Open config.json and stop.", "Run `foo. Bar` first.", "Stop. and go on.", "Page one ........ 12" })
        {
            var claim = Assert.Single(Split(text));
            Assert.Null(claim.Problem);
        }
    }

    [Fact]
    public void A_paragraph_starting_in_lower_case_is_listed_as_the_rest_of_something_above()
    {
        var claims = Split("The first half of a thought.\n\nand the second half. Then a new one.");
        Assert.Equal(FsdClaims.Problems.ContinuesAbove, claims[1].Problem);
        Assert.Null(claims[2].Problem);
    }

    // ---- list items ----

    [Fact]
    public void Each_list_item_is_one_claim_even_with_two_sentences_and_a_bullet_is_not_a_word()
    {
        var claims = Split("- One thing. And more.\n- Two things\n  run on.\n* Three.\n1. Four.\n2) Five.");
        Assert.Equal(["One thing. And more.", "Two things run on.", "Three.", "1. Four.", "2) Five."], Words(claims));
        Assert.All(claims, c => Assert.Equal(FsdClaims.Kinds.ListItem, c.Kind));
    }

    [Fact]
    public void A_sub_label_gives_a_list_its_flow_and_a_plain_paragraph_does_not()
    {
        var claims = Split("Some text. What happens when it rains:\n\n- Stay in.\n- Read.\n\nSomething else.\n\n- Not under it.");
        Assert.Equal(FsdClaims.Kinds.FlowLabel, claims[1].Kind);
        Assert.Equal("What happens when it rains:", claims[2].Flow);
        Assert.Equal("What happens when it rains:", claims[3].Flow);
        Assert.Null(claims[4].Flow);
        Assert.Null(claims[5].Flow);
        Assert.Equal("**Rain**", Split("**Rain**\n\n- Stay in.")[1].Flow);
    }

    [Fact]
    public void A_colon_with_no_list_after_it_labels_nothing()
    {
        var claims = Split("It says this:\n\nA plain paragraph.");
        Assert.Equal(FsdClaims.Kinds.Sentence, claims[0].Kind);
        Assert.Null(claims[1].Flow);
    }

    // ---- table rows ----

    [Fact]
    public void A_table_row_is_one_claim_and_a_rule_is_not_words()
    {
        var claims = Split("| Field | Rule. And more. |\n|---|---|\n| Name |  |\n|  |  |\n| Fee \\| rate | 5 |");
        Assert.Equal(["Field | Rule. And more.", "Name", "Fee \\| rate | 5"], Words(claims));
        Assert.All(claims, c => { Assert.Equal(FsdClaims.Kinds.TableRow, c.Kind); Assert.Null(c.Flow); });
    }

    // ---- numbered steps in a cell ----

    [Fact]
    public void Numbered_steps_in_a_cell_become_claims()
    {
        var claims = Split("| Head | Other |\n|---|---|\n| 1. The user enters a name. 2. The user saves it. |  |");
        Assert.Equal(["Head | Other", "1. The user enters a name.", "2. The user saves it."], Words(claims));
        Assert.Equal(FsdClaims.Kinds.Step, claims[1].Kind);
    }

    [Fact]
    public void Steps_alternating_between_columns_stay_as_written_with_their_own_numbers()
    {
        var claims = Split("| Who | User | Server |\n|---|---|---|\n|  | 1. Enter it. 2. Send it. 5. Read it. | 3. Check it. 4. Reply. |\n|  | 6. Close it. | 7. Log it. |");
        Assert.Equal(["1. Enter it.", "2. Send it.", "5. Read it.", "3. Check it.", "4. Reply.", "6. Close it.", "7. Log it."], Words(claims.Where(c => c.Kind == FsdClaims.Kinds.Step)));
        Assert.All(claims.Where(c => c.Kind == FsdClaims.Kinds.Step), c => Assert.Null(c.Problem));
    }

    [Fact]
    public void Steps_with_no_stop_between_them_split_only_on_the_next_number()
    {
        var claims = Split("| A | B |\n|---|---|\n|  | 1. Enter name 2. Save 7. Close. |");
        Assert.Equal(["A | B", "1. Enter name", "2. Save 7. Close."], Words(claims));
    }

    [Fact]
    public void A_number_in_the_middle_of_a_sentence_is_not_a_step()
    {
        var claims = Split("| A | B |\n|---|---|\n| The limit is 5. Then stop | x |\n| It costs 3. 50 more | y |");
        Assert.All(claims, c => Assert.Equal(FsdClaims.Kinds.TableRow, c.Kind));
    }

    [Fact]
    public void A_step_cut_across_two_rows_is_joined_and_its_words_still_check()
    {
        const string text = "| A | B |\n|---|---|\n|  | 1. It checks the password matches the |\n\n|  | hashed one on file. 2. It answers. |\n|---|---|\n";
        var claims = Split(text);
        Assert.Equal(("1. It checks the password matches the hashed one on file.", (string?)null), (claims[1].Words, claims[1].Problem));
        Assert.NotNull(claims[1].Join);
        Assert.Equal(("2. It answers.", (string?)null), (claims[2].Words, claims[2].Problem));
        Assert.Equal(3, claims.Count);
        Assert.Null(FsdClaims.CheckWords(text, claims));
    }

    [Fact]
    public void A_joined_part_read_after_another_claim_goes_back_where_the_section_has_it()
    {
        const string text = "| User | System |\n|---|---|\n| 1. Enter it. | 2. It confirms the value matches the |\n| 3. Press save. | hashed one on file. |\n";
        var claims = Split(text);
        Assert.Equal(["User | System", "1. Enter it.", "2. It confirms the value matches the hashed one on file.", "3. Press save."], Words(claims));
        Assert.Equal(4, claims[2].Join!.TailAfter);
        Assert.Null(claims[2].Problem);
        Assert.Null(FsdClaims.CheckWords(text, claims));
    }

    [Fact]
    public void A_join_with_a_word_changed_or_the_wrong_place_fails_the_words_check()
    {
        const string text = "| User | System |\n|---|---|\n| 1. Enter it. | 2. It confirms the value matches the |\n| 3. Press save. | hashed one on file. |\n";
        var claims = Split(text);
        var wrongPlace = claims.Select((c, i) => i == 2 ? c with { Join = c.Join! with { TailAfter = 3 } } : c).ToList();
        Assert.NotNull(FsdClaims.CheckWords(text, wrongPlace));
        var changed = claims.Select((c, i) => i == 2 ? c with { Words = c.Words.Replace("hashed", "salted") } : c).ToList();
        Assert.NotNull(FsdClaims.CheckWords(text, changed));
    }

    [Fact]
    public void Two_steps_cut_in_one_row_each_join_their_own_column()
    {
        const string text = "| User | System |\n|---|---|\n| 1. It enters the | 2. It stores the |\n| name of the user. | name in a table |\n";
        var claims = Split(text);
        Assert.Equal(["User | System", "1. It enters the name of the user.", "2. It stores the name in a table"], Words(claims));
        Assert.Equal(FsdClaims.Problems.EndsOpen, claims[2].Problem);
        Assert.Null(FsdClaims.CheckWords(text, claims));
    }

    [Theory]
    [InlineData("| A | B |\n|---|---|\n|  | 1. It checks the |\n|  | Hashed one on file. |\n", "a capital isn't a continuation")]
    [InlineData("| A | B | C |\n|---|---|---|\n|  | 1. It checks the |  |\n|  |  | hashed one on file. |\n", "another column")]
    [InlineData("| A | B | C |\n|---|---|---|\n|  | 1. It checks the |  |\n|  | hashed one | other value. |\n", "a plain row that isn't all continuation")]
    [InlineData("| A | B | C |\n|---|---|---|\n|  | 1. It checks the |  |\n|  | hashed one on file. | and more |\n", "two starts for one open step")]
    [InlineData("| A | B |\n|---|---|\n|  | 1. It checks the |\n|  | 2. hashed one on file. |\n", "numbered")]
    [InlineData("| A | B |\n|---|---|\n|  | 1. It checks it. |\n|  | hashed one on file. |\n", "the step above is closed")]
    [InlineData("| A | B |\n|---|---|\n|  | 1. It checks the |\n|  |  |\n|  | hashed one on file. |\n", "a row between")]
    public void What_is_not_clear_stays_apart(string text, string why)
    {
        var claims = Split(text);
        Assert.True(claims.All(c => c.Join is null), why);
        Assert.Contains(claims, c => c.Words.StartsWith("1. It checks", StringComparison.Ordinal) && !c.Words.Contains("hashed", StringComparison.Ordinal));
        Assert.Null(FsdClaims.CheckWords(text, claims));
    }

    // ---- flows ----

    [Fact]
    public void The_nearest_label_gives_the_flow_and_the_same_sentence_under_two_flows_stays_two_claims()
    {
        var claims = Split("""
            | Other paths: | Bad input. |  |
            |---|---|---|
            |  | Person | Server |
            |  | 1. Press save. | 2. Refuse it. |
            |  | Wrong key. |  |
            |  | Person | Server |
            |  | 1. Press save. | 2. Reject it. |
            | Afterwards: | Saved. |  |
            """.Replace("            ", ""));
        var press = claims.Where(c => c.Words == "1. Press save.").ToList();
        Assert.Equal(["Other paths: Bad input.", "Wrong key."], press.Select(c => c.Flow));
        Assert.NotEqual(press[0].Position, press[1].Position);
        Assert.Equal([FsdClaims.Kinds.FlowLabel, FsdClaims.Kinds.FlowLabel], claims.Where(c => c.Words is "Other paths: | Bad input." or "Wrong key.").Select(c => c.Kind));
        Assert.Equal("Other paths: Bad input.", claims.First(c => c.Words == "Person | Server").Flow);
        Assert.Null(claims[^1].Flow);
    }

    [Fact]
    public void A_first_column_labels_the_steps_on_its_own_row_or_under_its_column_headings()
    {
        var same = Split("| Name: | x |  |\n|---|---|---|\n| Usual way: | 1. Do it. | 2. Log it. |\n| Other way: | - |  |");
        Assert.Equal("Usual way:", same.First(c => c.Words == "1. Do it.").Flow);
        Assert.Equal(FsdClaims.Kinds.FlowLabel, same.First(c => c.Words == "Usual way:").Kind);

        var under = Split("| Usual way: | Person | Server |\n|---|---|---|\n|  | 1. Do it. | 2. Log it. |");
        Assert.Equal("Usual way:", under.First(c => c.Words == "1. Do it.").Flow);
    }

    [Fact]
    public void A_row_of_records_has_no_flow_and_a_value_in_another_column_is_no_label()
    {
        var claims = Split("""
            | Id | Case | Needs | Steps |
            |---|---|---|---|
            | T1 | Save it | Logged in | 1. Press save. |
            |  |  | Has a file |  |
            | T2 | Load it | Logged in | 1. Press load. |
            """.Replace("            ", ""));
        Assert.All(claims.Where(c => c.Kind == FsdClaims.Kinds.Step), c => Assert.Null(c.Flow));
        Assert.DoesNotContain(claims, c => c.Kind == FsdClaims.Kinds.FlowLabel);
    }

    [Fact]
    public void A_row_cut_between_headings_and_steps_is_listed_and_the_label_above_stands()
    {
        var claims = Split("""
            | Head |  |  |
            |---|---|---|
            |  | Wrong key. |  |
            |  | Person, | Server |

            |  | Other person |  |
            |---|---|---|
            |  | 1. Press save. | 2. Refuse it. |
            """.Replace("            ", ""));
        Assert.Equal(FsdClaims.Problems.ContinuesAbove, claims.First(c => c.Words == "Other person").Problem);
        Assert.Equal("Wrong key.", claims.First(c => c.Words == "1. Press save.").Flow);
    }

    [Fact]
    public void A_row_starting_in_lower_case_is_never_a_label()
    {
        var claims = Split("| Head |  |\n|---|---|\n|  | Wrong key. |\n|  | 1. Press save. |\n|  | and then more. |\n|  | 2. Close it. |");
        Assert.Equal(FsdClaims.Problems.ContinuesAbove, claims.First(c => c.Words == "and then more.").Problem);
        Assert.Equal("Wrong key.", claims.First(c => c.Words == "2. Close it.").Flow);
    }

    [Fact]
    public void A_paragraph_pushed_out_of_a_table_keeps_the_flow_and_a_new_one_ends_it()
    {
        var kept = Split("| Head |  |\n|---|---|\n|  | Wrong key. |\n|  | 1. Press. |\n\nthe rest of it.\n\n2. A step pushed out.\n\n| Head |  |\n|---|---|\n|  | 3. Close. |");
        Assert.Equal("Wrong key.", kept.First(c => c.Words == "the rest of it.").Flow);
        Assert.Equal("Wrong key.", kept.First(c => c.Words == "2. A step pushed out.").Flow);

        var ended = Split("| Head |  |\n|---|---|\n|  | Wrong key. |\n|  | 1. Press. |\n\nA new paragraph.\n\n- An item.");
        Assert.Null(ended[^1].Flow);
    }

    [Fact]
    public void No_rule_depends_on_the_words_used()
    {
        var one = Split("| Exception Sequence: | Invalid. |\n|---|---|\n|  | 1. Do. |");
        var two = Split("| Zebra Crossing: | Purple. |\n|---|---|\n|  | 1. Do. |");
        Assert.Equal("Exception Sequence: Invalid.", one[^1].Flow);
        Assert.Equal("Zebra Crossing: Purple.", two[^1].Flow);
    }

    // ---- what doesn't split ----

    [Fact]
    public void An_unsplittable_block_stays_whole_and_is_listed()
    {
        var fence = Split("Before.\n\n```\n- One. Two.\n|---|\n| a |\n```\n\nAfter.");
        Assert.Equal(("```\n- One. Two.\n|---|\n| a |\n```", FsdClaims.Problems.CodeBlock), (fence[1].Words, fence[1].Problem));
        Assert.Equal("After.", fence[2].Words);

        Assert.Equal(FsdClaims.Problems.CodeBlock, Assert.Single(Split("```\nNever closed. Still whole.")).Problem);
        Assert.Equal(FsdClaims.Problems.NoTableRule, Assert.Single(Split("| a bar line with no rule. And more. |")).Problem);

        var open = Split("| A | B |\n|---|---|\n| no closing bar. And more.");
        Assert.Equal(FsdClaims.Problems.RowNotClosed, open[^1].Problem);
    }

    [Fact]
    public void Numbers_that_do_not_rise_keep_the_cell_whole_and_listed()
    {
        var claims = Split("| A | B |\n|---|---|\n|  | 1. One. 3. Three. 2. Two. |");
        Assert.Equal(("1. One. 3. Three. 2. Two.", FsdClaims.Problems.NumbersOutOfOrder), (claims[^1].Words, claims[^1].Problem));

        var mixed = Split("| A | B |\n|---|---|\n|  | 1. One. 1. Again. | 2. Two. |");
        Assert.Equal(("1. One. 1. Again.", FsdClaims.Problems.NumbersOutOfOrder), (mixed[1].Words, mixed[1].Problem));
        Assert.Equal(("2. Two.", (string?)null), (mixed[2].Words, mixed[2].Problem));
    }

    [Fact]
    public void A_picture_is_a_claim_of_its_own_and_an_empty_section_has_none()
    {
        var claims = Split("Before.\n\n![](images/a.png)");
        Assert.Equal((FsdClaims.Kinds.Figure, "![](images/a.png)"), (claims[1].Kind, claims[1].Words));
        Assert.Empty(Split(""));
        Assert.Empty(Split("\n\n"));
    }

    // ---- the words check ----

    const string Checked = "One here. Two here.\n\n- Three.\n\n| Four | Five |\n|---|---|\n|  | 6. Six. 7. Seven. |";

    [Fact]
    public void The_words_check_passes_whitespace_separators_and_bullets_aside()
    {
        Assert.Equal(["One", "here.", "Two", "here.", "Three.", "Four", "Five", "6.", "Six.", "7.", "Seven."], FsdClaims.SectionWords(Checked));
        Assert.Null(FsdClaims.CheckWords(Checked, FsdClaims.Split(Checked)));
        Assert.Null(FsdClaims.CheckWords("a   b\n\nc", [new FsdClaim(1, null, "sentence", "a b c")]));
    }

    [Fact]
    public void The_words_check_catches_a_lost_word_a_changed_word_an_extra_word_and_a_wrong_order()
    {
        var claims = FsdClaims.Split(Checked);

        var lost = FsdClaims.CheckWords(Checked, claims.Where(c => c.Words != "Three."));
        Assert.Equal((5, "Three.", "Four"), (lost!.At, lost.Section, lost.Claims));

        var changed = FsdClaims.CheckWords(Checked, claims.Select(c => c.Words == "Three." ? c with { Words = "Tree." } : c));
        Assert.Equal(("Three.", "Tree."), (changed!.Section, changed.Claims));

        var extra = FsdClaims.CheckWords(Checked, claims.Append(new FsdClaim(99, null, "sentence", "More.")));
        Assert.Equal(((string?)null, "More.", 12), (extra!.Section, extra.Claims, extra.ClaimWords));

        var reversed = FsdClaims.CheckWords(Checked, Enumerable.Reverse(claims));
        Assert.NotNull(reversed);
    }

    // ---- the command's report, and the switch ----

    static SpecApp App(string components = "")
    {
        var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
        var app = new SpecApp($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}' }} }}\n{components}");
        var file = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# 1 Orders\n\nOrders need approval. Then they ship.\n\n# 2 Empty\n\n# 3 Returns\n\nSee Dr. Smith first.\n"));
        FsdImport.Import(app.Repo, file);
        return app;
    }

    [Fact]
    public void The_report_counts_a_document_and_one_section_by_number_or_permanent_id()
    {
        using var app = App();
        var whole = FsdClaims.Report(app.Root, "shop", null);
        Assert.Equal((2, 3, 1, 0), ((int)whole["sectionCount"]!, (int)whole["claimCount"]!, (int)whole["notCleanCount"]!, (int)whole["mismatchCount"]!));
        Assert.Equal(1, (int)whole["notCleanByReason"]![FsdClaims.Problems.MaybeSentenceEnd]!);
        Assert.Equal("3", (string)whole["notClean"]![0]!["number"]!);

        var first = whole["sections"]![0]!["claims"]![0]!;
        var id = (string)first["section"]!;
        Assert.StartsWith("S-", id);
        Assert.Equal(1, (int)first["position"]!);

        var byNumber = FsdClaims.Report(app.Root, "shop", "1");
        var byId = FsdClaims.Report(app.Root, "shop", id);
        Assert.Equal(2, (int)byNumber["claimCount"]!);
        Assert.Equal(byNumber["sections"]!.ToJsonString(), byId["sections"]!.ToJsonString());
        Assert.Equal("- | Orders need approval.\n- | Then they ship.\n", FsdClaims.Text(byNumber));
    }

    [Fact]
    public void An_unknown_section_or_document_is_not_found()
    {
        using var app = App();
        Assert.Equal(ExitCodes.NotFound, Assert.Throws<StangentException>(() => FsdClaims.Report(app.Root, "shop", "9")).ExitCode);
        Assert.Equal(ExitCodes.NotFound, Assert.Throws<StangentException>(() => FsdClaims.Report(app.Root, "none", null)).ExitCode);
    }

    [Fact]
    public void The_report_writes_no_project_file()
    {
        using var app = App();
        static Dictionary<string, DateTime> Files(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc);
        var before = Files(app.Root);
        FsdClaims.Report(app.Root, "shop", null);
        Assert.Equal(before, Files(app.Root));
    }

    [Fact]
    public void The_switch_is_off_by_default_and_off_says_it_is_no_pass()
    {
        using var off = App();
        Assert.False(StangentComponents.IsOn(off.Repo.Config, FsdClaims.Component));
        Assert.Contains("fsd claims", StangentComponents.Find(FsdClaims.Component).Commands);
        var said = FsdClaims.Off();
        Assert.False((bool)said["split"]!);
        Assert.Contains("isn't a pass", (string)said["off"]!);
        Assert.Contains("isn't a pass", FsdClaims.Text(said));

        using var on = App("components: { fsd-claims: true }\n");
        Assert.True(StangentComponents.IsOn(on.Repo.Config, FsdClaims.Component));
    }
}
