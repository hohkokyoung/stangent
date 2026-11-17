using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Tickets;

namespace Stangent.Core.Tests;

/// <summary>Truth map layer 5 (D364, D374): <c>ticket pull</c> reads a <c>Ruling:</c> reply to the PM's posted question and
/// drafts it into <c>rulings.yaml</c> uncommitted; <c>rulings-check</c> shows the Jira author beside the git committer; a
/// drafted ruling backs no ticket until committed. On the shop FSD of <see cref="RulingsTests"/>, with the proof, the posted
/// draft and the pulled comments written by hand. No AI, no Jira.</summary>
public sealed class RulingRepliesTests : IDisposable
{
    const string Key = "SHOP-1";
    const string Outline = """
        % Shop
        # 1 Orders
        Orders ship within two days of payment.
        A refund sends the customer an email.
        """;

    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public RulingRepliesTests()
    {
        repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\ncomponents:\n  truth-verdict: true\n");
        Git("init", "-q");
        Write("src/orders.ts", string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// order line {i}")) + "\n");
        Write("src/config.ts", "// a\n// b\n// c\n");
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(Repo, file);
        var document = FsdOutline.Load(Root, "shop");
        Write("specs/fsd/shop/review.json", new JsonObject
        {
            ["contract"] = "fsd-review", ["version"] = 2, ["doc"] = "shop", ["commit"] = "c0ffee", ["fsdVersion"] = (string)document["original"]!["hash"]!,
            ["runs"] = new JsonArray(new JsonObject { ["run"] = 1 }),
            ["findings"] = new JsonArray(
                Finding("F-001", "conflict", "A refund sends the customer an email.", "src/orders.ts:5"),
                Finding("F-002", "gap", "Orders ship within two days of payment.", "src/orders.ts:9")),
        }.ToJsonString());
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        Commit("setup");
    }

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    StangentRepo Repo => StangentRepo.Discover(Root);
    string RulingsPath => Rulings.PathFor(Root, "shop");
    string TicketDir => Path.Combine(Root, ".stangent", "tickets", Key);

    static JsonObject Finding(string id, string state, string quote, string evidence) => new()
    {
        ["id"] = id, ["state"] = state, ["section"] = "1", ["sectionId"] = "S-0001", ["fsdQuote"] = quote, ["note"] = "The review's note.",
        ["evidence"] = new JsonArray(evidence), ["runs"] = new JsonArray(1),
    };

    void Write(string rel, string text)
    {
        var path = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    void Git(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    void Commit(string message, string who = "Dev")
    {
        Git("add", "-A");
        Git("-c", $"user.name={who}", "-c", "user.email=x@x", "commit", "-q", "--allow-empty", "-m", message);
    }

    /// <summary>The truth map's entry for the quote, as the bundle lists it (with its doc, section and title).</summary>
    JsonObject BundleEntry(string quoteStart)
    {
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var s = map["sections"]!.AsArray().OfType<JsonObject>().Single(x => (string)x["sectionId"]! == "S-0001");
        var e = (JsonObject)s["entries"]!.AsArray().OfType<JsonObject>().Single(x => ((string?)x["key"]!["quote"])?.StartsWith(quoteStart, StringComparison.Ordinal) == true).DeepClone();
        e["id"] = "T1"; e["doc"] = "shop"; e["section"] = "1"; e["sectionId"] = "S-0001"; e["title"] = "Orders";
        return e;
    }

    /// <summary>The proof stopped as unclear on <paramref name="entry"/>, and the questions draft asking the PM was posted as comment 100.</summary>
    JsonObject Asked(JsonObject? entry, string kind = "unclear")
    {
        var verdict = new JsonObject { ["verdict"] = kind, ["forced"] = kind == "unclear", ["entry"] = entry, ["quote"] = null, ["why"] = "nobody has ruled on it" };
        Write($".stangent/tickets/{Key}/bug-proof.json", new JsonObject
        {
            ["schemaVersion"] = 2, ["key"] = Key, ["outcome"] = "not-provable",
            ["attempts"] = new JsonArray(new JsonObject { ["outcome"] = "not-provable", ["noProof"] = kind, ["verdict"] = verdict }),
        }.ToJsonString());
        Write($".stangent/tickets/{Key}/drafts/questions-internal.json", new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = Key, ["kind"] = "questions", ["tone"] = "internal",
            ["text"] = "Which browser was it?\n\n" + TicketDrafts.TruthQuestion(verdict, "internal"),
            ["posted"] = new JsonObject { ["by"] = "Sam Dev", ["at"] = "2026-11-17T09:00:00Z", ["commentId"] = "100" },
        }.ToJsonString());
        return verdict;
    }

    static JsonObject Comment(string id, string author, string text) => new() { ["id"] = id, ["author"] = author, ["created"] = $"2026-11-17T1{id[^1]}:00:00Z", ["text"] = text };

    static JsonObject Pulled(params JsonObject[] after) => new()
    {
        ["key"] = Key,
        ["comments"] = new JsonArray([Comment("99", "Pat PM", "Ruling: an old line before the question"), Comment("100", "Sam Dev", "the question"), .. after]),
    };

    JsonObject Pull(params JsonObject[] after) => RulingReplies.Read(Repo, Key, Pulled(after));

    [Fact]
    public void A_reply_line_reads_as_a_fixed_word_or_the_pms_own_words()
    {
        Assert.Equal("the FSD is right", JiraRulings.RulingLine("**Ruling:** the FSD is right"));
        Assert.Equal("keep", JiraRulings.RulingLine("  ruling: keep"));
        Assert.Null(JiraRulings.RulingLine("Reply with a line starting `Ruling:`, for example `Ruling: the FSD is right`"));
        Assert.Null(JiraRulings.RulingLine("> Ruling: quoted from someone else"));
        Assert.Equal(new JiraRulings.Said("fsd-right", null), JiraRulings.Read("The FSD is right."));
        Assert.Equal(new JiraRulings.Said("code-right", null), JiraRulings.Read("code right"));
        Assert.Equal(new JiraRulings.Said("remove", null), JiraRulings.Read("Remove it!"));
        Assert.Equal(new JiraRulings.Said(null, "Refunds email the customer within an hour."), JiraRulings.Read("Refunds email the customer within an hour."));
        Assert.Null(JiraRulings.Read("  "));
        // A fixed word with more after it is asked back, not turned into a rule; marks and list bullets are allowed.
        Assert.NotNull(JiraRulings.Read("yes, it does that")!.Unclear);
        Assert.NotNull(JiraRulings.Read("Keep it - thanks")!.Unclear);
        Assert.Equal(new JiraRulings.Said("keep", null), JiraRulings.Read(JiraRulings.RulingLine("- Ruling: `keep`")!));
        Assert.Equal("fsd right", JiraRulings.RulingLine("1. **Ruling**: fsd right"));
    }

    [Fact]
    public void A_ruling_reply_is_drafted_into_its_entry_uncommitted_and_backs_no_ticket_until_committed()
    {
        Asked(BundleEntry("A refund sends"));
        var before = File.ReadAllLines(RulingsPath);
        var result = Pull(Comment("101", "Pat PM", "Thanks for asking.\n**Ruling:** the FSD is right"));
        Assert.Equal("drafted", (string)result["state"]!);
        Assert.Contains("not committed", (string)result["summary"]!);
        Assert.Contains("confirms it by committing", (string)result["summary"]!);
        var entry = Rulings.Load(Root, "shop").File!.All.Single(e => e.State == "conflict");
        Assert.Equal("fsd-right", entry.Ruling);
        Assert.Equal(("SHOP-1", "101", "Pat PM"), ((string)entry.Jira!["key"]!, (string)entry.Jira["comment"]!, (string)entry.Jira["author"]!));
        // Only the entry's ruling line changed and its jira line was added (D371's "only those lines change").
        var after = File.ReadAllLines(RulingsPath);
        Assert.Equal(before.Length + 1, after.Length);
        Assert.Equal(2, after.Except(before).Count());
        // Read again: already there, nothing written.
        Assert.Equal("already", (string)Pull(Comment("101", "Pat PM", "**Ruling:** the FSD is right"))["state"]!);

        var check = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok);
        var jira = Assert.Single(check["fromJira"]!.AsArray())!;
        Assert.Equal((false, "Pat PM"), ((bool)jira["committed"]!, (string)jira["jiraAuthor"]!));
        var drafted = TruthList.Build(Repo, ["src/orders.ts"])["entries"]!.AsArray().OfType<JsonObject>().Single(e => (string)e["state"]! == "conflict");
        Assert.False((bool)drafted["usable"]!);
        Assert.Contains("isn't committed yet", (string)drafted["why"]!);

        Commit("Rule SHOP-1", "Dana Lead");
        check = TruthMap.Check(Repo, "shop", out _);
        jira = check["fromJira"]![0]!;
        Assert.Equal((true, "Dana Lead", true), ((bool)jira["committed"]!, (string)jira["committer"]!, (bool)jira["differ"]!));
        Assert.Contains("in Jira by Pat PM (SHOP-1, comment 101), confirmed by Dana Lead", File.ReadAllText(TruthMap.MarkdownPath(Root, "shop")));
        Assert.True((bool)TruthList.Build(Repo, ["src/orders.ts"])["entries"]!.AsArray().OfType<JsonObject>().Single(e => (string)e["state"]! == "conflict")["usable"]!);

        // The PM answers it again through Claude: the answer is theirs now, so its jira line goes.
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var n = (int)map["sections"]!.AsArray().OfType<JsonObject>().SelectMany(x => x["entries"]!.AsArray().OfType<JsonObject>()).Single(e => (string)e["state"]! == "conflict")["n"]!;
        PmAnswers.Apply(Repo, "shop", n, "code", null, null);
        var answered = Rulings.Load(Root, "shop").File!.All.Single(e => e.State == "conflict");
        Assert.Equal(("code-right", null), (answered.Ruling, answered.Jira));
    }

    [Fact]
    public void Replies_that_dont_fit_or_disagree_are_listed_and_nothing_is_guessed()
    {
        Asked(BundleEntry("A refund sends"));
        var before = File.ReadAllText(RulingsPath);
        // `keep` doesn't answer "which is right?" about a conflict.
        var misfit = Pull(Comment("101", "Pat PM", "Ruling: keep"));
        Assert.Equal("listed", (string)misfit["state"]!);
        Assert.Contains("doesn't fit", (string)misfit["summary"]!);
        // Two people saying different things: for a person to settle.
        var split = Pull(Comment("101", "Pat PM", "Ruling: the FSD is right"), Comment("102", "Sam Dev", "Ruling: the code is right"));
        Assert.Equal("listed", (string)split["state"]!);
        Assert.Contains("different people", (string)split["summary"]!);
        // One person changing their mind settles it: each person's latest reply counts.
        var settled = Pull(Comment("101", "Pat PM", "Ruling: the FSD is right"), Comment("102", "Sam Dev", "Ruling: the code is right"), Comment("103", "Sam Dev", "Ruling: fsd right"));
        Assert.Equal("drafted", (string)settled["state"]!);
        Git("checkout", "--", "specs/fsd/shop/rulings.yaml");
        // Taken out of rulings.yaml by a person: not drafted again.
        Assert.Contains("taken out of rulings.yaml", (string)Pull(Comment("103", "Sam Dev", "Ruling: fsd right"))["summary"]!);
        // "Should the rule change?" isn't answered by yes or no.
        Asked(BundleEntry("A refund sends"), "contradicted");
        Assert.Contains("doesn't say what the rule is", (string)Pull(Comment("104", "Pat PM", "Ruling: no"))["summary"]!);
        Asked(BundleEntry("A refund sends"));
        // A comment with nothing after Ruling: is listed with why.
        var empty = Pull(Comment("101", "Pat PM", "Ruling:"));
        Assert.Equal("nothing follows `Ruling:`", (string)empty["replies"]![0]!["notRead"]!);
        // The proof moved on to another entry since the question was posted.
        var verdict = Asked(BundleEntry("A refund sends"));
        var proof = JsonNode.Parse(File.ReadAllText(Path.Combine(TicketDir, "bug-proof.json")))!.AsObject();
        proof["attempts"]![0]!["verdict"]!["entry"] = BundleEntry("Orders ship");
        File.WriteAllText(Path.Combine(TicketDir, "bug-proof.json"), proof.ToJsonString());
        Assert.Contains("has changed since the question was posted", (string)Pull(Comment("101", "Pat PM", "Ruling: fsd right"))["summary"]!);
        Assert.Equal(before, File.ReadAllText(RulingsPath));
        Assert.NotNull(verdict);
        Assert.Contains("starts with `yes` but says more", (string)Pull(Comment("101", "Pat PM", "Ruling: yes, it does that"))["replies"]![0]!["notRead"]!);
    }

    [Fact]
    public void A_ticket_nothing_covered_gets_an_entry_of_its_own_that_a_review_refresh_keeps()
    {
        Asked(null);
        Write($".stangent/tickets/{Key}/bundle.json", """{ "points": [ { "kind": "file", "id": "src/config.ts" } ] }""");
        // One person's later reply replaces their earlier one, here one that didn't fit.
        var result = Pull(Comment("101", "Pat PM", "Ruling: the FSD is right"), Comment("102", "Pat PM", "Ruling: A deleted order disappears from the order list."));
        Assert.Equal("drafted", (string)result["state"]!);
        Assert.Equal("102", (string)result["replies"]![0]!["replacedBy"]!);
        var own = Assert.Single(Rulings.Load(Root, "shop").File!.Undocumented);
        Assert.Equal(("ticket:SHOP-1", "undocumented", "restated"), (own.KeyLink, own.State, own.Ruling));
        Assert.Equal("A deleted order disappears from the order list.", own.Statement);
        Assert.Equal(["src/config.ts"], own.Evidence);
        Assert.True(TruthMap.Check(Repo, "shop", out _)["ok"]!.GetValue<bool>());
        // A review refresh keeps it as it is: it was never a review finding, so "not found again" doesn't apply.
        Rulings.Refresh(Repo, "shop");
        own = Assert.Single(Rulings.Load(Root, "shop").File!.Undocumented);
        Assert.Null(own.NotFound);
        Assert.Equal("restated", own.Ruling);
        TruthMap.Check(Repo, "shop", out _);
        Assert.Contains("added from Jira SHOP-1, not a review finding", File.ReadAllText(TruthMap.MarkdownPath(Root, "shop")));
        // Committed, then new words from Jira: only the statement line changes, and that alone keeps it uncommitted.
        Commit("Rule SHOP-1", "Dana Lead");
        Assert.Equal("drafted", (string)Pull(Comment("103", "Pat PM", "Ruling: A deleted order leaves the order list at once."))["state"]!);
        Assert.False((bool)TruthMap.Check(Repo, "shop", out _)["fromJira"]![0]!["committed"]!);
        Assert.False((bool)TruthList.Build(Repo, ["src/config.ts"])["entries"]![0]!["usable"]!);
    }
}
