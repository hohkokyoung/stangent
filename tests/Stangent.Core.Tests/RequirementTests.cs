using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;

namespace Stangent.Core.Tests;

/// <summary>Phase 2c's extraction (D99) with canned outputs: quotes and sentences checked by code, the fresh checker,
/// stable ids. No LLM.</summary>
public class RequirementTests
{
    const string Fsd = """
        #! Orders
        The orders screen has a Place order button.
        Orders over 50 need approval.
        ##! Limits
        Every customer can place 3 orders a day.
        """;

    const string Button = "The orders screen has a Place order button.";
    const string Over50 = "Orders over 50 need approval.";

    static SpecApp App(string fsd = Fsd)
    {
        var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
        var app = new SpecApp($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}' }} }}\n");
        Import(app, fsd);
        return app;
    }

    static void Import(SpecApp app, string fsd)
    {
        var file = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(fsd));
        FsdImport.Import(app.Repo, file);
    }

    static JsonArray Q(params string[] quotes) => new([.. quotes.Select(q => (JsonNode)q)]);

    static JsonObject Req(string key, string statement, string title = "A title", params string[] quotes) => new()
    {
        ["key"] = key, ["kind"] = "rule", ["title"] = title, ["statement"] = statement, ["quotes"] = Q(quotes), ["acceptance"] = new JsonArray(),
    };

    static JsonObject Output(JsonObject[] requirements, string[]? background = null) => new()
    {
        ["requirements"] = new JsonArray([.. requirements.Select(r => (JsonNode)r)]),
        ["definitions"] = new JsonArray(), ["journeys"] = new JsonArray(), ["questions"] = new JsonArray(),
        ["background"] = Q(background ?? []),
    };

    static JsonObject Two() => Output([Req("r1", "There's a Place order button.", "Place order button", Button), Req("r2", "Orders over 50 need approval.", "Approval over 50", Over50)]);

    static JsonObject Verdicts(params (string Item, string Verdict, string? Extra)[] verdicts) => new()
    {
        ["checks"] = new JsonArray([.. verdicts.Select(v =>
        {
            var o = new JsonObject { ["item"] = v.Item, ["verdict"] = v.Verdict, ["note"] = "n" };
            if (v.Extra is not null) o["extra"] = v.Extra;
            return (JsonNode)o;
        })]),
    };

    static JsonObject AllOk() => Verdicts(("r1", "ok", null), ("r2", "ok", null));

    static RunStep Give(SpecApp app, RunStep step, JsonNode output, string section = "1")
    {
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        return RequirementRun.Check(app.Repo, "shop", section, null);
    }

    static void Record(SpecApp app, JsonObject output, JsonObject verdicts, string section = "1")
    {
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", section), output, section);
        Assert.Equal("confirm", step.Action);
        Assert.Equal("recorded", Give(app, step, verdicts, section).Status);
    }

    static JsonObject Feature(SpecApp app, string section = "1") =>
        RequirementFile.Features(app.Root, "shop").First(f => (string)f["section"]! == section);

    static JsonObject Item(JsonObject feature, int i) => feature["requirements"]!.AsArray()[i]!.AsObject();

    [Fact]
    public void Sentences_are_found_by_code_and_rules_and_images_are_not_sentences()
    {
        var found = Sentences.Of("""
            Intro one. Intro two has 2 parts.

            | Option | Meaning |
            |---|---|
            | Root Folder | Where it's stored. |

            - A bullet: with text.

            ![](images/a.png)

            A caption
            """);
        // A table's first row counts: Word doesn't say whether it's a header, and it could hold a rule.
        Assert.Equal(["Intro one.", "Intro two has 2 parts.", "Option | Meaning", "Root Folder | Where it's stored.", "A bullet: with text.", "A caption"], found.Select(s => s.Text));
    }

    [Theory]
    [InlineData("Orders over 50 need approval unless the customer is a VIP.", true)]
    [InlineData("Orders over 50 need", false)] // the exception would go unread
    [InlineData("Orders over 50 need approval", false)]
    [InlineData("over 50 need approval unless the customer", false)]
    [InlineData("Some other text entirely, long enough.", false)]
    public void A_quote_cites_a_sentence_only_when_it_covers_every_word(string quote, bool cited) =>
        Assert.Equal(cited, Sentences.Cited(new Sentence("Orders over 50 need approval unless the customer is a VIP."), [quote]));

    [Fact]
    public void Several_quotes_can_cover_a_sentence_together_matching_whole_words()
    {
        var s = new Sentence("Orders over 50 need approval unless the customer is a VIP.");
        Assert.True(Sentences.Cited(s, ["Orders over 50 need approval", "unless the customer is a VIP."]));
        Assert.False(Sentences.Cited(new Sentence("Admin"), ["Administrators can edit every order."]));
        Assert.False(Sentences.Cited(new Sentence("The admin can edit orders now"), ["he admin can edit orders now"]));
        // A quote running on past the sentence still holds it whole.
        Assert.True(Sentences.Cited(new Sentence("Second one here."), ["The first one is here. Second one here."]));
    }

    [Fact]
    public void A_rows_label_alone_cites_nothing_and_a_short_cell_is_no_evidence_of_one_row()
    {
        Sentence Row(string line) => Sentences.Of(line)[0];
        var row = Row("| Root Folder | The folder it is stored in. |");
        Assert.False(Sentences.Cited(row, ["Root Folder"]));
        Assert.True(Sentences.Cited(row, ["The folder it is stored in."]));
        Assert.True(Sentences.Cited(row, ["| Root Folder | The folder it is stored in. |"]));
        var retries = Row("| Password retries | 3 |");
        Assert.False(Sentences.Cited(retries, ["3"]));
        Assert.False(Sentences.Cited(retries, ["Password retries"]));
        Assert.True(Sentences.Cited(retries, ["| Password retries | 3 |"]));
    }

    [Fact]
    public void Background_is_one_whole_sentence_as_listed_and_nothing_more()
    {
        var s = new Sentence("One long sentence with two clauses, and more.");
        Assert.True(Sentences.IsExactly("One long sentence with two clauses, and more.", s));
        Assert.False(Sentences.IsExactly("One long sentence with two clauses", s));
        Assert.False(Sentences.IsExactly("One long sentence with two clauses, and more. Admins can do anything.", s));
        Assert.True(Sentences.IsExactly("| Option | Meaning |", new Sentence("Option | Meaning", ["Option", "Meaning"])));
    }

    [Fact]
    public void A_section_is_written_checked_and_recorded_and_a_rerun_makes_no_AI_call()
    {
        using var app = App();
        var step = RequirementRun.Prepare(app.Repo, "shop", "1");
        Assert.Equal(RequirementRun.Writer, step.Agent);
        var input = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output)!, "writer.md"));
        Assert.Contains(Over50, input);
        step = Give(app, step, Two());
        Assert.Equal(RequirementRun.Checker, step.Agent);
        Assert.Equal("recorded", Give(app, step, AllOk()).Status);

        var feature = Feature(app);
        Assert.Equal("F-0001", (string)feature["id"]!);
        Assert.Equal("R-0001", (string)Item(feature, 0)["id"]!);
        Assert.Equal("proposed", (string)Item(feature, 0)["status"]!);
        Assert.Equal("confirmed", (string)Item(feature, 1)["check"]!);
        Assert.Equal(Over50, (string)Item(feature, 1)["quotes"]![0]!["text"]!);

        // The views: one line per item, and the same item by id.
        var index = YamlForm.Parse(File.ReadAllText(Path.Combine(RequirementFile.DocDir(app.Root, "shop"), "index.yaml")), "index");
        Assert.Contains(index["items"]!.AsArray(), i => (string)i!["id"]! == "R-0002" && (string)i!["title"]! == "Approval over 50");
        Assert.Equal("requirement", (string)RequirementFile.Show(app.Root, "R-0002")!["type"]!);
        Assert.Null(RequirementFile.Show(app.Root, "R-0099"));

        Assert.Equal("current", RequirementRun.Prepare(app.Repo, "shop", "1").Status);
    }

    [Fact]
    public void Features_follow_the_FSDs_section_tree()
    {
        using var app = App();
        Record(app, Output([Req("r1", "Each customer can place 3 orders a day.", "Daily limit", "Every customer can place 3 orders a day.")]), Verdicts(("r1", "ok", null)), "1.1");
        var child = Feature(app, "1.1");
        var parent = Feature(app, "1");
        Assert.Equal((string)parent["id"]!, (string)child["parent"]!);
        Assert.Equal((string)child["id"]!, (string)parent["children"]![0]!);
        Assert.Empty(parent["requirements"]!.AsArray());
    }

    [Fact]
    public void A_sentence_neither_cited_nor_background_fails_after_one_retry()
    {
        using var app = App();
        var step = RequirementRun.Prepare(app.Repo, "shop", "1");
        var missing = Output([Req("r1", "There's a button.", "Button", Button)]);
        step = Give(app, step, missing);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("isn't fully covered", StringComparison.Ordinal) && e.Contains(Over50, StringComparison.Ordinal));
        step = Give(app, step, missing);
        Assert.Equal("failed", step.Status);
        Assert.Empty(RequirementFile.Features(app.Root, "shop"));

        // Not tried again with the same inputs, unless asked.
        Assert.Equal("failed", RequirementRun.Prepare(app.Repo, "shop", "1").Status);
        Assert.Equal("write", RequirementRun.Prepare(app.Repo, "shop", "1", retryFailed: true).Action);
    }

    [Fact]
    public void Background_counts_for_a_sentence_but_a_quote_must_be_word_for_word()
    {
        using var app = App();
        var step = RequirementRun.Prepare(app.Repo, "shop", "1");
        var bad = Output([Req("r1", "There's a button.", "Button", "The orders screen shows a Place order button.")], [Over50]);
        step = Give(app, step, bad);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("word for word", StringComparison.Ordinal));
        step = Give(app, step, Output([Req("r1", "There's a button.", "Button", Button)], [Over50]));
        Assert.Equal(RequirementRun.Checker, step.Agent);
        Assert.Contains("**b1** " + Over50, File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output)!, "checker.md")));
        Assert.Equal("recorded", Give(app, step, Verdicts(("r1", "ok", null), ("b1", "ok", null))).Status);
        Assert.Equal(Over50, (string)Feature(app)["background"]![0]!["text"]!);
        Assert.Equal("confirmed", (string)Feature(app)["background"]![0]!["check"]!);
    }

    [Fact]
    public void Keys_a_step_points_at_must_exist()
    {
        using var app = App();
        var output = Two();
        output["journeys"] = new JsonArray((JsonNode)new JsonObject
        {
            ["key"] = "j1", ["title"] = "Ordering", ["steps"] = Q("r1", "r9"), ["quotes"] = Q(Button), ["scenarios"] = new JsonArray(),
        });
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), output);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("`r9`", StringComparison.Ordinal));
    }

    [Fact]
    public void A_disputed_item_goes_back_to_the_writer_once_and_a_fixed_one_is_confirmed()
    {
        using var app = App();
        var first = Output([Req("r1", "There's a button.", "Button", Button), Req("r2", "Orders over 50 need a manager's approval.", "Approval", Over50)]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), first);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "a manager's")));
        Assert.Equal(RequirementRun.Writer, step.Agent);
        Assert.Contains("What to fix", step.Prompt!);
        step = Give(app, step, Output([Req("r1", "There's a button.", "Button", Button), Req("r2", "Orders over 50 need approval.", "Approval", Over50)]));
        Assert.Equal(RequirementRun.Checker, step.Agent);
        Assert.Equal("recorded", Give(app, step, AllOk()).Status);
        Assert.Equal("confirmed", (string)Item(Feature(app), 1)["check"]!);
        Assert.Equal(1, (int)RequirementRun.LoadRuns(app.Root)["shop#1"]!["fixes"]!);
    }

    [Fact]
    public void An_item_still_disputed_after_its_fix_is_recorded_as_disputed()
    {
        using var app = App();
        var first = Output([Req("r1", "There's a button.", "Button", Button), Req("r2", "Orders over 50 need a manager's approval.", "Approval", Over50)]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), first);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "a manager's")));
        step = Give(app, step, first);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "a manager's")));
        Assert.Equal("recorded", step.Status);
        var r2 = Item(Feature(app), 1);
        Assert.Equal("disputed", (string)r2["check"]!);
        Assert.Contains("a manager's", (string)r2["note"]!);
        Assert.Equal(1, (int)RequirementRun.LoadRuns(app.Root)["shop#1"]!["disputed"]!);
    }

    [Fact]
    public void A_fix_that_breaks_the_code_checks_keeps_the_earlier_output_with_its_disputes()
    {
        using var app = App();
        var first = Output([Req("r1", "There's a button.", "Button", Button), Req("r2", "Orders over 50 need a manager's approval.", "Approval", Over50)]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), first);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "a manager's")));
        step = Give(app, step, Output([Req("r1", "There's a button.", "Button", Button)]));
        Assert.Equal("recorded", step.Status);
        Assert.Equal("disputed", (string)Item(Feature(app), 1)["check"]!);
        Assert.Equal("Orders over 50 need a manager's approval.", (string)Item(Feature(app), 1)["statement"]!);
    }

    [Fact]
    public void A_dispute_must_point_at_the_items_own_words_or_the_item_stays_unchecked()
    {
        using var app = App();
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), Two());
        var made = Verdicts(("r1", "ok", null), ("r2", "says-more", "words it never used"));
        step = Give(app, step, made);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("`r2`", StringComparison.Ordinal));
        step = Give(app, step, made);
        Assert.Equal("recorded", step.Status);
        Assert.Equal("unchecked", (string)Item(Feature(app), 1)["check"]!);
        Assert.Equal("confirmed", (string)Item(Feature(app), 0)["check"]!);
    }

    [Fact]
    public void An_item_with_no_answer_from_the_checker_is_unchecked_never_confirmed()
    {
        using var app = App();
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), Two());
        step = Give(app, step, Verdicts(("r1", "ok", null)));
        Assert.Equal("retry", step.Status);
        Assert.Equal("recorded", Give(app, step, Verdicts(("r1", "ok", null))).Status);
        Assert.Equal("unchecked", (string)Item(Feature(app), 1)["check"]!);
    }

    [Fact]
    public void Unchanged_quotes_keep_their_id_and_a_changed_item_drops_back_to_proposed()
    {
        using var app = App();
        Record(app, Two(), AllOk());
        var feature = Feature(app);
        Item(feature, 0)["status"] = "approved";
        Item(feature, 0)["decided_by"] = "ba@example.com";
        Item(feature, 1)["status"] = "approved";
        RequirementFile.Save(app.Root, feature);

        // The FSD changes, so the section runs again: r1 is word for word as it was, r2 says something else.
        Import(app, Fsd.Replace("Orders over 50 need approval.", "Orders over 50 need approval.\nGuests can't order."));
        var again = Two();
        again["requirements"]![1]!["statement"] = "Orders above 50 are approved first.";
        again["requirements"]!.AsArray().Add((JsonNode)Req("r3", "Guests can't order.", "No guest orders", "Guests can't order."));
        Record(app, again, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("r3", "ok", null)));

        feature = Feature(app);
        Assert.Equal(("R-0001", "approved", "ba@example.com"), ((string)Item(feature, 0)["id"]!, (string)Item(feature, 0)["status"]!, (string?)Item(feature, 0)["decided_by"]));
        Assert.Equal(("R-0002", "proposed"), ((string)Item(feature, 1)["id"]!, (string)Item(feature, 1)["status"]!));
        Assert.Null(Item(feature, 1)["decided_by"]);
        Assert.Equal("R-0003", (string)Item(feature, 2)["id"]!);
    }

    [Fact]
    public void Several_items_citing_one_sentence_each_keep_their_own_id()
    {
        using var app = App();
        Record(app, Output([Req("r1", "One.", "First", Button), Req("r2", "Two.", "Second", Button)], [Over50]), Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null)));
        Import(app, Fsd.Replace(Over50, Over50 + "\nA new line that changes the hash."));
        Record(app, Output([Req("r1", "Two.", "Second", Button), Req("r2", "One.", "First", Button)], [Over50, "A new line that changes the hash."]), Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null), ("b2", "ok", null)));
        var feature = Feature(app);
        Assert.Equal("R-0002", (string)Item(feature, 0)["id"]!);
        Assert.Equal("R-0001", (string)Item(feature, 1)["id"]!);
    }

    [Fact]
    public void An_id_whose_quotes_are_gone_is_retired_and_never_reused()
    {
        using var app = App();
        Record(app, Two(), AllOk());
        Import(app, Fsd.Replace("Orders over 50 need approval.\n", "Orders over 80 need approval.\n"));
        var over80 = "Orders over 80 need approval.";
        Record(app, Output([Req("r1", "There's a Place order button.", "Place order button", Button), Req("r2", "Orders over 80 need approval.", "Approval over 80", over80)]), AllOk());
        var feature = Feature(app);
        Assert.Equal("R-0003", (string)Item(feature, 1)["id"]!);
        Assert.Equal("R-0002", (string)feature["retired"]![0]!["id"]!);
    }

    [Fact]
    public void Definitions_are_referred_to_by_id_and_terms_get_a_glossary_view()
    {
        using var app = App();
        var output = Two();
        output["requirements"]![0]!["refers"] = Q("d1");
        output["definitions"] = new JsonArray((JsonNode)new JsonObject
        {
            ["key"] = "d1", ["kind"] = "glossary", ["name"] = "Order", ["text"] = "What's on the orders screen.", ["quotes"] = Q(Button),
        });
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), output);
        Assert.Equal("recorded", Give(app, step, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("d1", "ok", null))).Status);
        Assert.Equal("D-0001", (string)Item(Feature(app), 0)["refers"]![0]!);
        var glossary = YamlForm.Parse(File.ReadAllText(Path.Combine(RequirementFile.DocDir(app.Root, "shop"), "glossary.yaml")), "glossary");
        Assert.Equal("Order", (string)glossary["terms"]![0]!["name"]!);
    }

    [Fact]
    public void A_question_for_the_BA_keeps_its_id_and_closes_when_it_stops_coming_back()
    {
        using var app = App();
        var output = Two();
        output["questions"] = new JsonArray((JsonNode)new JsonObject { ["text"] = "Over 50 what?", ["quotes"] = Q(Over50) });
        Record(app, output, AllOk());
        Assert.Equal("Q-0001", (string)RequirementFile.Questions(app.Root, "shop")[0]["id"]!);

        Import(app, Fsd.Replace(Over50, Over50 + "\nAnother line."));
        var again = Two();
        again["background"] = Q("Another line.");
        Record(app, again, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null)));
        var questions = RequirementFile.Questions(app.Root, "shop");
        Assert.Single(questions);
        Assert.Equal("dropped", (string)questions[0]["status"]!);
    }

    [Fact]
    public void The_same_content_gives_the_same_bytes_and_reads_back()
    {
        var o = new JsonObject
        {
            ["version"] = 1, ["none"] = null, ["text"] = "A \"quoted\" colon: and\nnew line", ["empty"] = new JsonArray(),
            ["list"] = new JsonArray((JsonNode)"a", (JsonNode)new JsonObject { ["k"] = "v", ["nested"] = new JsonArray((JsonNode)new JsonObject { ["x"] = "y" }) }),
        };
        var yaml = YamlForm.Write(o, "header");
        var back = YamlForm.Parse(yaml, "test");
        Assert.True(JsonNode.DeepEquals(o, back), yaml);
        Assert.Equal(yaml, YamlForm.Write(back, "header"));

        var line = YamlForm.Parse("items:\n  - " + YamlForm.Line(new JsonObject { ["id"] = "R-1", ["title"] = "A \":\" title, with, commas", ["none"] = null }) + "\n", "line");
        Assert.Equal("A \":\" title, with, commas", (string)line["items"]![0]!["title"]!);
        Assert.Null(line["items"]![0]!["none"]);
    }

    [Fact]
    public void A_section_with_no_text_is_skipped()
    {
        using var app = App("""
            #! Empty
            ##! Sub
            Some text here for the sub section.
            """);
        Assert.Equal("skipped", RequirementRun.Prepare(app.Repo, "shop", "1").Status);
        Assert.Equal("write", RequirementRun.Prepare(app.Repo, "shop", "1.1").Action);
    }

    [Fact]
    public void A_section_of_only_an_image_is_skipped()
    {
        Assert.Empty(Sentences.Of("![](images/a.png)\n"));
    }

    [Fact]
    public void Journey_and_question_quotes_alone_cite_nothing()
    {
        using var app = App();
        var output = Output([Req("r1", "There's a button.", "Button", Button)]);
        output["questions"] = new JsonArray((JsonNode)new JsonObject { ["text"] = "Over 50 what?", ["quotes"] = Q(Over50) });
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), output);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains(Over50, StringComparison.Ordinal));
    }

    [Fact]
    public void Criteria_quotes_are_checked_count_as_citing_and_are_in_the_hash()
    {
        using var app = App();
        var bad = Two();
        bad["requirements"]![1]!["acceptance"] = new JsonArray((JsonNode)new JsonObject { ["text"] = "An order of 60 waits.", ["quotes"] = Q("Orders above fifty wait for someone.") });
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), bad);
        Assert.Contains(step.Errors!, e => e.Contains("criterion", StringComparison.Ordinal) && e.Contains("word for word", StringComparison.Ordinal));

        var cites = Output([Req("r1", "There's a button.", "Button", Button)]);
        cites["requirements"]![0]!["acceptance"] = new JsonArray((JsonNode)new JsonObject { ["text"] = "An order of 60 waits.", ["quotes"] = Q(Over50) });
        step = Give(app, step, cites);
        Assert.Equal(RequirementRun.Checker, step.Agent);

        var q = new JsonArray((JsonNode)new JsonObject { ["hash"] = "h" });
        var a1 = new JsonArray((JsonNode)new JsonObject { ["text"] = "one", ["quotes"] = new JsonArray() });
        var a2 = new JsonArray((JsonNode)new JsonObject { ["text"] = "two", ["quotes"] = new JsonArray() });
        Assert.NotEqual(RequirementRecord.ItemHash("rule", "t", "s", q, a1, []), RequirementRecord.ItemHash("rule", "t", "s", q, a2, []));
        Assert.NotEqual(RequirementRecord.ItemHash("rule", "t", "s", q, a1, []), RequirementRecord.ItemHash("rule", "other", "s", q, a1, []));
        Assert.NotEqual(RequirementRecord.ItemHash("rule", "t", "s", q, a1, []), RequirementRecord.ItemHash("rule", "t", "s", q, a1, new JsonArray((JsonNode)"D-0001")));
    }

    [Fact]
    public void Scenario_quotes_are_checked_keys_are_unique_and_statements_are_not_repeated()
    {
        using var app = App();
        var output = Two();
        output["journeys"] = new JsonArray((JsonNode)new JsonObject
        {
            ["key"] = "j1", ["title"] = "Ordering", ["steps"] = Q("r1", "r2"), ["quotes"] = Q(Button),
            ["scenarios"] = new JsonArray((JsonNode)new JsonObject { ["key"] = "s1", ["kind"] = "error", ["title"] = "Refused", ["steps"] = Q("r2"), ["quotes"] = Q("Orders are refused at checkout time.") }),
        });
        output["requirements"]!.AsArray().Add((JsonNode)Req("r1", "Orders over 50 need approval.", "Again", Over50));
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), output);
        Assert.Contains(step.Errors!, e => e.Contains("`s1`", StringComparison.Ordinal) && e.Contains("word for word", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.Contains("`r1` is used twice", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.Contains("say the same thing", StringComparison.Ordinal));
    }

    [Fact]
    public void Background_that_holds_a_rule_goes_back_to_the_writer_and_stays_disputed_if_it_stays()
    {
        using var app = App();
        var hiding = Output([Req("r1", "There's a button.", "Button", Button)], [Over50]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), hiding);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("b1", "needs-item", null)));
        Assert.Equal(RequirementRun.Writer, step.Agent);
        Assert.Contains("marked background", File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output)!, "feedback.md")));
        step = Give(app, step, hiding);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("b1", "needs-item", null)));
        Assert.Equal("recorded", step.Status);
        Assert.Equal("disputed", (string)Feature(app)["background"]![0]!["check"]!);
        Assert.Equal(1, (int)RequirementRun.LoadRuns(app.Root)["shop#1"]!["disputed"]!);
    }

    [Fact]
    public void A_dispute_moved_to_background_is_seen_by_the_checker_again()
    {
        using var app = App();
        var first = Output([Req("r1", "There's a button.", "Button", Button), Req("r2", "Orders over 50 need a manager's approval.", "Approval", Over50)]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), first);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "a manager's")));
        step = Give(app, step, Output([Req("r1", "There's a button.", "Button", Button)], [Over50]));
        Assert.Equal(RequirementRun.Checker, step.Agent);
        Assert.Contains(Over50, File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output)!, "checker.md")));
    }

    [Fact]
    public void A_journeys_order_can_be_disputed()
    {
        using var app = App();
        var output = Two();
        output["journeys"] = new JsonArray((JsonNode)new JsonObject { ["key"] = "j1", ["title"] = "Ordering", ["steps"] = Q("r1", "r2"), ["quotes"] = Q(Button), ["scenarios"] = new JsonArray() });
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), output);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("j1", "says-more", "r1 → r2")));
        Assert.Equal(RequirementRun.Writer, step.Agent);
    }

    [Fact]
    public void A_dispute_dropped_on_retry_stays_unchecked_never_confirmed()
    {
        using var app = App();
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), Two());
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "words it never used")));
        Assert.Equal("retry", step.Status);
        step = Give(app, step, AllOk());
        Assert.Equal("recorded", step.Status);
        Assert.Equal("unchecked", (string)Item(Feature(app), 1)["check"]!);
        Assert.NotNull(step.Errors);
    }

    [Fact]
    public void A_verdict_of_the_wrong_kind_or_answered_twice_is_not_accepted()
    {
        using var app = App();
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), Output([Req("r1", "There's a button.", "Button", Button)], [Over50]));
        step = Give(app, step, Verdicts(("r1", "needs-item", null), ("b1", "says-more", "Orders")));
        Assert.Equal("retry", step.Status);
        Assert.Equal(2, step.Errors!.Count(e => e.Contains("`needs-item`", StringComparison.Ordinal)));

        using var other = App();
        step = Give(other, RequirementRun.Prepare(other.Repo, "shop", "1"), Two());
        step = Give(other, step, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("r2", "says-more", "approval")));
        Assert.Contains(step.Errors!, e => e.Contains("answered twice", StringComparison.Ordinal));
        // Still twice on its one retry: the dispute outranks the "ok", so it goes to the writer.
        step = Give(other, step, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("r2", "says-more", "approval")));
        Assert.Equal(RequirementRun.Writer, step.Agent);
    }

    [Fact]
    public void A_checker_that_writes_nothing_after_a_fix_can_not_reuse_the_first_rounds_verdicts()
    {
        using var app = App();
        var first = Output([Req("r1", "There's a button.", "Button", Button), Req("r2", "Orders over 50 need a manager's approval.", "Approval", Over50)]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), first);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "a manager's")));
        step = Give(app, step, Output([Req("r1", "Only managers may press the button.", "Button", Button), Req("r2", "Orders over 50 need approval.", "Approval", Over50)]));
        Assert.Equal(RequirementRun.Checker, step.Agent);
        Assert.False(File.Exists(Path.Combine(app.Root, step.Output!)));
        step = RequirementRun.Check(app.Repo, "shop", "1", null);
        Assert.Equal("retry", step.Status);
        step = RequirementRun.Check(app.Repo, "shop", "1", null);
        Assert.Equal("recorded", step.Status);
        Assert.Equal("unchecked", (string)Item(Feature(app), 0)["check"]!);
    }

    [Fact]
    public void The_checker_gets_its_own_retry_after_the_writer_used_one()
    {
        using var app = App();
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), new JsonObject());
        Assert.Equal("retry", step.Status);
        step = Give(app, step, Two());
        Assert.Equal(RequirementRun.Checker, step.Agent);
        step = Give(app, step, new JsonObject { ["checks"] = "not a list" });
        Assert.Equal("retry", step.Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Output_of_the_wrong_shape_is_a_retry_not_a_crash(string output)
    {
        using var app = App();
        var step = RequirementRun.Prepare(app.Repo, "shop", "1");
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output);
        Assert.Equal("retry", RequirementRun.Check(app.Repo, "shop", "1", null).Status);
        step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), Two());
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output);
        Assert.Equal("retry", RequirementRun.Check(app.Repo, "shop", "1", null).Status);
    }

    [Fact]
    public void A_check_on_a_section_that_was_removed_says_so()
    {
        using var app = App();
        RequirementRun.Prepare(app.Repo, "shop", "1.1");
        Import(app, "#! Orders\nThe orders screen has a Place order button.\n");
        var e = Assert.Throws<StangentException>(() => RequirementRun.Check(app.Repo, "shop", "1.1", null));
        Assert.Equal("not_found", e.Code);
    }

    [Fact]
    public void Items_with_no_answer_say_so_and_can_be_run_again()
    {
        using var app = App();
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), Two());
        step = Give(app, step, Verdicts(("r1", "ok", null)));
        step = Give(app, step, Verdicts(("r1", "ok", null)));
        Assert.Equal("recorded", step.Status);
        Assert.Contains("1 item(s)", step.Errors![0]);
        Assert.Equal(1, (int)RequirementRun.LoadRuns(app.Root)["shop#1"]!["unchecked"]!);

        var again = RequirementRun.Prepare(app.Repo, "shop", "1");
        Assert.Equal(("current", "none"), (again.Status, again.Action));
        Assert.Contains("--retry-failed", again.Errors![0]);
        Assert.Equal("write", RequirementRun.Prepare(app.Repo, "shop", "1", retryFailed: true).Action);
    }

    [Fact]
    public void Success_clears_a_failure_usage_goes_to_the_right_role_and_every_attempts_cost_is_kept()
    {
        using var app = App();
        var cost = new Usage("m", 1, 1, 1, 0, 0, 0.5, 10);
        var missing = Output([Req("r1", "There's a button.", "Button", Button)]);
        void Write(RunStep s, JsonNode o) => File.WriteAllText(Path.Combine(app.Root, s.Output!), o.ToJsonString());
        var step = RequirementRun.Prepare(app.Repo, "shop", "1");
        Write(step, missing);
        step = RequirementRun.Check(app.Repo, "shop", "1", cost);
        Write(step, missing);
        Assert.Equal("failed", RequirementRun.Check(app.Repo, "shop", "1", cost).Status);
        var run = RequirementRun.LoadRuns(app.Root)["shop#1"]!;
        Assert.NotNull(run["failed"]);
        Assert.Equal(1, (int)run["retries"]!);
        Assert.Equal(1.0, (double)run["spentUsd"]!);

        step = RequirementRun.Prepare(app.Repo, "shop", "1", retryFailed: true);
        Write(step, Two());
        step = RequirementRun.Check(app.Repo, "shop", "1", cost);
        Write(step, AllOk());
        Assert.Equal("recorded", RequirementRun.Check(app.Repo, "shop", "1", new Usage("m", 1, 1, 1, 0, 0, 0.25, 10)).Status);
        run = RequirementRun.LoadRuns(app.Root)["shop#1"]!;
        Assert.Null(run["failed"]);
        Assert.Equal(0.5, Usage.FromJson(run["writer"]).CostUsd);
        Assert.Equal(0.25, Usage.FromJson(run["checker"]).CostUsd);
        Assert.Equal(1.75, (double)run["spentUsd"]!);
    }

    [Fact]
    public void Reworded_items_sharing_a_quote_get_new_ids_rather_than_swapping()
    {
        using var app = App();
        var first = Output([Req("r1", "One.", "First", Button), Req("r2", "Two.", "Second", Button)], [Over50]);
        Record(app, first, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null)));
        var ids = new[] { (string)Item(Feature(app), 0)["id"]!, (string)Item(Feature(app), 1)["id"]! };

        Import(app, Fsd.Replace(Over50, Over50 + "\nA changed line in this section."));
        Record(app, Output([Req("r1", "Two, reworded.", "Second", Button), Req("r2", "One, reworded.", "First", Button)], [Over50, "A changed line in this section."]),
            Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null), ("b2", "ok", null)));
        var feature = Feature(app);
        Assert.DoesNotContain((string)Item(feature, 0)["id"]!, ids);
        Assert.DoesNotContain((string)Item(feature, 1)["id"]!, ids);
        Assert.Equal(ids, feature["retired"]!.AsArray().Select(r => (string)r!["id"]!));
    }

    [Fact]
    public void A_new_title_drops_approval_and_so_does_a_dispute()
    {
        using var app = App();
        Record(app, Two(), AllOk());
        var feature = Feature(app);
        Item(feature, 0)["status"] = "approved";
        Item(feature, 1)["status"] = "approved";
        RequirementFile.Save(app.Root, feature);

        Import(app, Fsd.Replace(Over50, Over50 + "\nA changed line in this section."));
        var again = Output([Req("r1", "There's a Place order button.", "Only managers may press Place order", Button), Req("r2", "Orders over 50 need approval.", "Approval over 50", Over50)], ["A changed line in this section."]);
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), again);
        step = Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "Approval over 50"), ("b1", "ok", null)));
        step = Give(app, step, again);
        Assert.Equal("recorded", Give(app, step, Verdicts(("r1", "ok", null), ("r2", "says-more", "Approval over 50"), ("b1", "ok", null))).Status);
        feature = Feature(app);
        Assert.Equal("proposed", (string)Item(feature, 0)["status"]!);
        Assert.Equal("proposed", (string)Item(feature, 1)["status"]!);
    }

    [Fact]
    public void A_BAs_answer_stays_with_its_own_question()
    {
        using var app = App();
        var output = Two();
        output["questions"] = new JsonArray((JsonNode)new JsonObject { ["text"] = "Over 50 what?", ["quotes"] = Q(Over50) });
        Record(app, output, AllOk());
        var questions = RequirementFile.Questions(app.Root, "shop");
        questions[0]["answer"] = "Dollars.";
        RequirementFile.SaveQuestions(app.Root, "shop", questions);

        Import(app, Fsd.Replace(Over50, Over50 + "\nAnother line."));
        var again = Two();
        again["background"] = Q("Another line.");
        again["questions"] = new JsonArray((JsonNode)new JsonObject { ["text"] = "Who approves?", ["quotes"] = Q(Over50) }, (JsonNode)new JsonObject { ["text"] = "Over 50 what?", ["quotes"] = Q(Over50) });
        Record(app, again, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null)));
        questions = RequirementFile.Questions(app.Root, "shop");
        Assert.Equal("Dollars.", (string?)questions.Single(q => (string)q["text"]! == "Over 50 what?")["answer"]);
        Assert.Null(questions.Single(q => (string)q["text"]! == "Who approves?")["answer"]);
    }

    [Fact]
    public void Definitions_journeys_and_scenarios_are_retired_too()
    {
        using var app = App();
        var output = Two();
        output["definitions"] = new JsonArray((JsonNode)new JsonObject { ["key"] = "d1", ["kind"] = "glossary", ["name"] = "Order", ["text"] = "A thing.", ["quotes"] = Q(Button) });
        output["journeys"] = new JsonArray((JsonNode)new JsonObject
        {
            ["key"] = "j1", ["title"] = "Ordering", ["steps"] = Q("r1"), ["quotes"] = Q(Button),
            ["scenarios"] = new JsonArray((JsonNode)new JsonObject { ["key"] = "s1", ["kind"] = "happy", ["title"] = "It works", ["steps"] = Q("r1"), ["quotes"] = Q(Button) }),
        });
        var step = Give(app, RequirementRun.Prepare(app.Repo, "shop", "1"), output);
        Assert.Equal("recorded", Give(app, step, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("d1", "ok", null), ("j1", "ok", null), ("s1", "ok", null))).Status);

        Import(app, Fsd.Replace(Over50, Over50 + "\nAnother line."));
        var again = Two();
        again["background"] = Q("Another line.");
        Record(app, again, Verdicts(("r1", "ok", null), ("r2", "ok", null), ("b1", "ok", null)));
        Assert.Equal(["D-0001", "J-0001", "S-0001"], Feature(app)["retired"]!.AsArray().Select(r => (string)r!["id"]!));
    }

    [Fact]
    public void A_child_left_out_of_its_parent_by_a_killed_run_is_put_back()
    {
        using var app = App();
        Record(app, Output([Req("r1", "Each customer can place 3 orders a day.", "Daily limit", "Every customer can place 3 orders a day.")]), Verdicts(("r1", "ok", null)), "1.1");
        var parent = Feature(app, "1");
        parent["children"] = new JsonArray();
        RequirementFile.Save(app.Root, parent);
        RequirementFile.Ensure(app.Root, "shop", Stangent.Spec.Trace.SpecIndex.Load(app.Root).Sections[("shop", "1.1")], Stangent.Spec.Trace.SpecIndex.Load(app.Root));
        Assert.Equal((string)Feature(app, "1.1")["id"]!, (string)Feature(app, "1")["children"]![0]!);
    }
}
