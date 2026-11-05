using System.Text.Json.Nodes;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using static Stangent.Core.Tests.FsdLinkTests;

namespace Stangent.Core.Tests;

/// <summary>Asking how the app works, from the pages (E.2, D124), with a fake AI's answers. The real <c>claude</c> never runs.</summary>
public class PageAskTests
{
    const string Question = "What happens to orders over 100?";

    static SpecApp On()
    {
        var app = App();
        app.Repo.Config.Spec.Ask = true;
        return app;
    }

    static JsonObject Cited(string text, params (string Page, string Statement)[] cites) => new()
    {
        ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)new JsonObject { ["page"] = c.Page, ["statement"] = c.Statement })]),
    };

    static JsonObject Answer(params JsonObject[] sentences) => new() { ["answer"] = new JsonArray(sentences) };

    static RunStep Asked(SpecApp app, string question = Question)
    {
        var (step, answer) = PageAsk.Prepare(app.Repo, question);
        Assert.Null(answer);
        Assert.Equal(PageAsk.Asker, step.Agent);
        return step;
    }

    [Fact]
    public void Off_it_does_nothing_and_says_so()
    {
        using var app = App();
        var (step, answer) = PageAsk.Prepare(app.Repo, Question);
        Assert.Equal(("off", "none"), (step.Status, step.Action));
        Assert.Null(answer);
        Assert.False(Directory.Exists(Path.Combine(app.Root, ".stangent", "spec-ask")));
        Assert.False(new StangentConfig().Spec.Ask);
    }

    [Fact]
    public void Pages_are_picked_by_the_questions_words_and_ranked_title_first()
    {
        using var app = On();
        var index = SpecIndex.Load(app.Root);
        // "place" is in the POST page's title, and only in a statement on the orders page.
        var picked = PageAsk.Pick(index, "place", 5);
        Assert.Equal(SpecApp.Post, picked[0].Page);
        Assert.True(picked[0].InTitle > 0);
        Assert.Equal(("page:/orders", 0), (picked[1].Page, picked[1].InTitle));
        Assert.True(picked[1].InStatements > 0);
        Assert.DoesNotContain(picked, p => p.Page == "page:/cart");
        Assert.Empty(PageAsk.Pick(index, "xylophones zeppelins", 5));
    }

    [Fact]
    public void The_cap_is_a_setting()
    {
        using var app = On();
        var index = SpecIndex.Load(app.Root);
        Assert.Equal(5, new StangentConfig().Spec.AskMaxPages);
        Assert.Single(PageAsk.Pick(index, "place", 1));
        app.Repo.Config.Spec.AskMaxPages = 1;
        var step = Asked(app, "place");
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "bundle.md"));
        Assert.Contains($"`{SpecApp.Post}`", bundle);
        Assert.DoesNotContain("`page:/orders`", bundle);
    }

    [Fact]
    public void The_bundle_holds_the_question_and_pages_with_new_text_and_no_other_step_changes()
    {
        using var app = On();
        var step = Asked(app);
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "bundle.md"));
        Assert.Contains(Bundles.Instructions("page-asker.md"), bundle);
        Assert.Contains(Question, bundle);
        Assert.Contains("**R1** (rules) Orders over 100 are refused", bundle);
        Assert.Equal("page-asker-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
    }

    [Fact]
    public void A_cited_answer_passes_with_the_lines_from_reference_lines_and_is_labelled()
    {
        using var app = On();
        var file = SpecIndex.Load(app.Root).PageFiles[SpecApp.Post];
        File.WriteAllText(PageLines.FileFor(file), new JsonObject { ["version"] = 1, ["page"] = SpecApp.Post, ["commit"] = new string('a', 40), ["symbols"] = new JsonObject { [SpecApp.Place] = new JsonObject { ["file"] = "src/Orders.cs", ["start"] = 3, ["end"] = 9 } } }.ToJsonString());
        var step = Asked(app);
        Write(app, step, Answer(Cited("Orders over 100 are refused.", (SpecApp.Post, "R1"))));
        var (done, answer) = PageAsk.Check(app.Repo, Question, null);
        Assert.Equal(("answered", "none"), (done.Status, done.Action));
        Assert.Equal("as the pages describe it", (string)answer!["label"]!);
        Assert.StartsWith("As the pages describe it:", (string)answer["text"]!);
        var cite = answer["sentences"]![0]!["cites"]![0]!;
        Assert.Contains("Orders over 100", (string)cite["says"]!);
        var line = cite["lines"]![0]!;
        Assert.Equal(SpecApp.Place, (string)line["symbol"]!);
        Assert.Equal("src/Orders.cs:3-9", (string)line["at"]!);
        Assert.Equal(new string('a', 40), (string)cite["commit"]!);
    }

    [Fact]
    public void An_id_on_no_page_in_the_bundle_gets_one_retry_then_its_refused()
    {
        using var app = On();
        var step = Asked(app);
        Write(app, step, Answer(Cited("Made up.", (SpecApp.Post, "R99")), Cited("Elsewhere.", ("page:/nope", "R1"))));
        var (retry, none) = PageAsk.Check(app.Repo, Question, null);
        Assert.Equal("retry", retry.Status);
        Assert.Null(none);
        Assert.Contains(retry.Errors!, e => e.Contains("R99", StringComparison.Ordinal));
        Assert.Contains(retry.Errors!, e => e.Contains("isn't in the bundle", StringComparison.Ordinal));

        Write(app, retry, Answer(Cited("Made up.", (SpecApp.Post, "R99"))));
        var (refused, answer) = PageAsk.Check(app.Repo, Question, null);
        Assert.Equal("not-covered", refused.Status);
        Assert.Equal(PageAsk.NotCovered, (string)answer!["text"]!);
        Assert.Empty(answer["sentences"]!.AsArray());
        Assert.Contains("R99", (string)answer["why"]!);
    }

    [Fact]
    public void A_sentence_citing_nothing_is_refused()
    {
        using var app = On();
        var step = Asked(app);
        Write(app, step, Answer(Cited("Trust me.")));
        var (retry, _) = PageAsk.Check(app.Repo, Question, null);
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("cites nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_answer_says_the_pages_dont_cover_this()
    {
        using var app = On();
        Write(app, Asked(app), Answer());
        var (step, answer) = PageAsk.Check(app.Repo, Question, null);
        Assert.Equal("not-covered", step.Status);
        Assert.Equal(PageAsk.NotCovered, (string)answer!["text"]!);
    }

    [Fact]
    public void No_word_on_any_page_makes_no_call()
    {
        using var app = On();
        var (step, answer) = PageAsk.Prepare(app.Repo, "xylophones zeppelins");
        Assert.Equal(("not-covered", "none"), (step.Status, step.Action));
        Assert.Equal(PageAsk.NotCovered, (string)answer!["text"]!);
    }

    [Fact]
    public void The_same_question_on_the_same_pages_makes_no_second_call()
    {
        using var app = On();
        Write(app, Asked(app), Answer(Cited("Refused.", (SpecApp.Post, "R1"))));
        PageAsk.Check(app.Repo, Question, null);
        var (again, answer) = PageAsk.Prepare(app.Repo, Question);
        Assert.Equal(("answered", "none"), (again.Status, again.Action));
        Assert.Equal("Refused.", (string)answer!["sentences"]![0]!["text"]!);
    }

    [Fact]
    public void With_ai_off_a_question_needing_a_call_stops()
    {
        using var app = On();
        app.Repo.Config.Ai.Enabled = false;
        Assert.Equal("ai_disabled", Assert.Throws<StangentException>(() => PageAsk.Prepare(app.Repo, Question)).Code);
    }

    [Fact]
    public void A_check_with_nothing_prepared_says_so()
    {
        using var app = On();
        Assert.Equal("not_prepared", Assert.Throws<StangentException>(() => PageAsk.Check(app.Repo, "never asked", null)).Code);
    }

    [Fact]
    public void A_page_with_no_line_citations_says_why_rather_than_leaving_them_out()
    {
        using var app = On();
        var page = SpecIndex.Load(app.Root).PageFiles[SpecApp.Post];
        File.Delete(PageLines.FileFor(page));
        Write(app, Asked(app), Answer(Cited("Refused.", (SpecApp.Post, "R1"))));
        var (_, answer) = PageAsk.Check(app.Repo, Question, null);
        var line = answer!["sentences"]![0]!["cites"]![0]!["lines"]![0]!;
        Assert.Null(line["at"]);
        Assert.Contains("no line citations", (string)line["why"]!);
        Assert.Equal(SpecApp.Post, (string)answer["notCited"]![0]!["page"]!);
    }

    [Fact]
    public void Off_nothing_the_other_steps_read_mentions_it()
    {
        using var app = App();
        var step = LinkRun_prepare(app);
        var bundle = SharedHead.Assemble(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md")));
        Assert.DoesNotContain("stangent-page-asker", bundle);
        Assert.DoesNotContain("as the pages describe it", bundle);
    }

    static RunStep LinkRun_prepare(SpecApp app) => Stangent.Spec.Fsd.LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
}
