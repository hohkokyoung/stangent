using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>Phase fs layer 4 (D379, D386, D387): <c>fsd write</c> turns rulings into chapter edits. The PM's words are placed
/// by code; a code-right ruling's sentence comes from a fake drafter (its output written as the driver would) and is
/// checked by <c>fsd write-check</c>. Each written entry becomes a confirmed match with <c>writtenFrom</c>.</summary>
public sealed class FsdWriteTests : IDisposable
{
    const string Outline = """
        % Shop
        # 1 Orders
        Orders ship within two days of payment.
        A refund sends the customer an email.
        Each order shows a Cancel button until it ships.
        ## 1.1 Order list
        The order list shows 20 orders per page.
        # 2 Settings
        Settings hold the shop's currency.
        """;

    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public FsdWriteTests()
    {
        repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        Git("init", "-q");
        Write("src/orders.ts", string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// order line {i}")) + "\n");
        Write("src/config.ts", "// a\n// b\n// c\n");
        Write("web/settings.html", "<p>1</p>\n<p>2</p>\n<p>3</p>\n");
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(repo.Repo, file);
        Committed.All(repo.Repo);
        Commit("setup");
        Review(
            F("conflict", "1", "S-0001", "A refund sends the customer an email.", "No email is sent.", "src/orders.ts:5"),
            F("gap", "1", "S-0001", "Orders ship within two days of payment.", "No shipping rule.", "src/config.ts:2"),
            F("match", "1.1", "S-0002", "The order list shows 20 orders per page.", "Pages by 20.", "src/config.ts:1"),
            F("undocumented", null, null, null, "A filters screen.", "web/settings.html:3"));
        Rulings.Refresh(Repo, "shop");
        Commit("review and rulings");
    }

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    StangentRepo Repo => StangentRepo.Discover(Root);
    string RulingsPath => Rulings.PathFor(Root, "shop");
    string ChapterDir => FsdChapters.Dir(FsdImport.DocDir(Root, "shop"));

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

    void Commit(string message, string who = "Pat PM")
    {
        Git("add", "-A");
        Git("-c", $"user.name={who}", "-c", "user.email=x@x", "commit", "-q", "--allow-empty", "-m", message);
    }

    static JsonObject F(string state, string? section, string? sectionId, string? quote, string note, params string[] evidence) => new()
    {
        ["state"] = state, ["section"] = section, ["sectionId"] = sectionId, ["fsdQuote"] = quote, ["screen"] = null, ["note"] = note,
        ["evidence"] = new JsonArray([.. evidence.Select(e => (JsonNode)e)]), ["runs"] = new JsonArray(1, 2),
    };

    void Review(params JsonObject[] findings)
    {
        var document = FsdOutline.Load(Root, "shop");
        Write("specs/fsd/shop/review.json", new JsonObject
        {
            ["contract"] = "fsd-review", ["version"] = 2, ["doc"] = "shop", ["commit"] = "c0ffee", ["fsdVersion"] = (string)document["original"]!["hash"]!,
            ["runs"] = new JsonArray(new JsonObject { ["run"] = 1 }, new JsonObject { ["run"] = 2 }),
            ["findings"] = new JsonArray([.. findings]),
        }.ToJsonString());
    }

    RulingsFile Read() => Rulings.Load(Root, "shop").File!;

    /// <summary>Sets the PM's line under the entry whose key holds <paramref name="keyPart"/>.</summary>
    void Rule(string keyPart, string field, string value)
    {
        var lines = File.ReadAllLines(RulingsPath).ToList();
        var at = lines.FindIndex(l => l.Contains("- key:", StringComparison.Ordinal) && l.Contains(keyPart, StringComparison.Ordinal));
        Assert.True(at >= 0, keyPart);
        var line = lines.FindIndex(at, l => l.TrimStart().StartsWith(field + ":", StringComparison.Ordinal));
        lines[line] = lines[line][..(lines[line].IndexOf(field, StringComparison.Ordinal) + field.Length)] + ": " + value;
        File.WriteAllLines(RulingsPath, lines);
    }

    Dictionary<string, string> Chapters() => Directory.GetFiles(ChapterDir, "*.md").ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

    /// <summary>What changed between two snapshots of the chapters: (file, old line, new line) for each differing line.</summary>
    static List<(string File, string Old, string New)> Changed(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var result = new List<(string, string, string)>();
        foreach (var (name, text) in after)
        {
            var a = before[name].Split('\n');
            var b = text.Split('\n');
            if (a.Length != b.Length) { result.Add((name, $"{a.Length} lines", $"{b.Length} lines")); continue; }
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) result.Add((name, a[i], b[i]));
        }
        return result;
    }

    static RulingEntry Written(RulingsFile f) => f.All.Single(e => e.WrittenFrom is not null);

    [Fact]
    public void A_restated_ruling_replaces_only_its_sentence_becomes_a_match_and_a_refresh_keeps_it()
    {
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email; the shop calls the customer.\"");
        var before = Chapters();
        var (result, step) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Null(step);
        Assert.Single(result["written"]!.AsArray());
        // Only that sentence changed, on its own line: every other byte of every chapter is as it was.
        var change = Assert.Single(Changed(before, Chapters()));
        Assert.Equal("A refund sends the customer an email.", change.Old);
        Assert.Equal("A refund sends no email; the shop calls the customer.", change.New);

        var file = Read();
        var e = Written(file);
        Assert.Equal(("match", "S-0001", "A refund sends no email; the shop calls the customer.", "confirmed"), (e.State, e.KeySection, e.KeyQuote, e.Ruling));
        Assert.Equal(("conflict", "A refund sends the customer an email.", "restated"), ((string?)e.WrittenFrom!["state"], (string?)e.WrittenFrom["quote"], (string?)e.WrittenFrom["ruling"]));
        Assert.Null(e.Statement);
        // The section's recorded hash moved to the new text.
        var read = FsdChapters.Read(FsdChapters.Files(FsdImport.DocDir(Root, "shop")));
        Assert.Equal(read.Sections.Single(s => s.PermanentId == "S-0001").TextHash, file.Sections.Single(s => s.SectionId == "S-0001").TextHash);

        // fsd check passes it, the commit approves it, and a refresh from the same review keeps it: not lost, not flagged,
        // the old conflict not back, the gap's ruling untouched.
        Assert.True((bool)FsdCheck.Run(Repo, "shop", [], [])["ok"]!);
        Commit("write the refund rule");
        var refresh = Rulings.Refresh(Repo, "shop");
        Assert.Empty(refresh["sectionsChanged"]!.AsArray());
        file = Read();
        e = Written(file);
        Assert.Equal(("confirmed", (string?)null, (string?)null), (e.Ruling, e.Flagged, e.NotFound));
        Assert.DoesNotContain(file.All, x => x.State == "conflict");
        Assert.Single(refresh["supersededByWritten"]!.AsArray());
        // Written again: nothing to do.
        Assert.Empty(FsdWrite.Run(Repo, "shop", null, null, null, null).Result["written"]!.AsArray());

        // The truth map shows what it was written from, and who.
        Commit("refresh");
        var check = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok, string.Join("; ", check["problems"]!.AsArray()));
        var w = Assert.Single(check["written"]!.AsArray());
        Assert.Equal(("restated", "Pat PM"), ((string?)w!["from"], (string?)w["committer"]));
        var md = File.ReadAllText(TruthMap.MarkdownPath(Root, "shop"));
        Assert.Contains("Written into the FSD from the PM's own words", md);
        Assert.Contains("it replaced: “A refund sends the customer an email.”", md);
        Assert.DoesNotContain("inFsd", File.ReadAllText(TruthMap.JsonPath(Root, "shop")));
    }

    [Fact]
    public void Words_for_code_no_section_covers_are_listed_without_a_section_and_added_at_the_end_of_the_one_named()
    {
        Rule("file:web", "ruling", "restated");
        Rule("file:web", "statement", "\"Settings also list every saved filter.\"");
        var loose = Read().Undocumented.Single();
        Assert.Equal("restated", loose.Ruling);
        var before = Chapters();
        var (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Empty(result["written"]!.AsArray());
        Assert.Contains("--section", (string)Assert.Single(result["listed"]!.AsArray())!["why"]!);
        Assert.Empty(Changed(before, Chapters()));

        (result, _) = FsdWrite.Run(Repo, "shop", null, "S-0003", null, null);
        Assert.Equal("added", (string)Assert.Single(result["written"]!.AsArray())!["how"]!);
        var chapter = Chapters().Single(c => c.Key.Contains("s0003", StringComparison.Ordinal)).Value;
        Assert.Contains("Settings hold the shop's currency.\n\nSettings also list every saved filter.", chapter);
        var file = Read();
        Assert.Empty(file.Undocumented);
        var e = file.Sections.Single(s => s.SectionId == "S-0003").Entries.Single();
        Assert.Equal(("match", "S-0003", "undocumented"), (e.State, e.KeySection, (string?)e.WrittenFrom!["state"]));
        Assert.NotNull((string?)e.WrittenFrom["link"]);
        Assert.True((bool)FsdCheck.Run(Repo, "shop", [], [])["ok"]!);
    }

    /// <summary>The gap gets a code-right ruling, and the run is prepared under a cap.</summary>
    RunStepJson PrepareCodeRight()
    {
        Rule("Orders ship within", "ruling", "code-right");
        var (none, noStep) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Null(noStep);
        Assert.False((bool)none["drafting"]!["prepared"]!);   // rule 9: no cap chosen, nothing prepared
        var (result, step) = FsdWrite.Run(Repo, "shop", "1", null, null, null);
        Assert.NotNull(step);
        Assert.Equal(FsdWrite.Writer, step!.Agent);
        Assert.Equal(1.0, (double)FsdWrite.DriverLimits(Repo, "shop")["maxBudgetUsd"]!);
        var input = File.ReadAllText(Path.Combine(FsdWrite.RunDir(Root, "shop"), "input.md"));
        Assert.Contains("> Orders ship within two days of payment.", input);
        Assert.Contains("`src/config.ts:2`", input);
        return new RunStepJson(result, Path.Combine(Root, step.Output!));
    }

    sealed record RunStepJson(JsonObject Result, string Output);

    static void Draft(string output, string replaces, string sentence, params string[] cites) => File.WriteAllText(output, new JsonObject
    {
        ["drafts"] = new JsonArray(new JsonObject
        {
            ["entry"] = "W1", ["replaces"] = replaces, ["sentence"] = sentence, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]),
        }),
    }.ToJsonString());

    [Fact]
    public void A_code_right_draft_that_passes_the_check_is_written_with_its_cites()
    {
        var run = PrepareCodeRight();
        var before = Chapters();
        Draft(run.Output, "Orders ship within two days of payment.", "Orders ship when a person marks them shipped; there is no time limit.", "src/config.ts:2");
        var (result, step) = FsdWrite.Check(Repo, "shop", null);
        Assert.Equal("checked", step.Status);
        Assert.Single(result["written"]!.AsArray());
        var change = Assert.Single(Changed(before, Chapters()));
        Assert.Equal(("Orders ship within two days of payment.", "Orders ship when a person marks them shipped; there is no time limit."), (change.Old, change.New));
        var e = Written(Read());
        Assert.Equal("code-right", (string?)e.WrittenFrom!["ruling"]);
        Assert.Equal(["src/config.ts:2"], e.WrittenFrom["cites"]!.AsArray().Select(c => (string)c!));
        Assert.Empty(Rulings.Load(Root, "shop").Problems);   // the schema takes writtenFrom.cites
        Assert.True((bool)FsdCheck.Run(Repo, "shop", [], [])["ok"]!);
    }

    [Theory]
    [InlineData("Orders ship within two days.", "src/config.ts:2", "word for word")]                       // a wrong quote
    [InlineData("Orders ship within two days of payment.", "src/config.ts:99", "past the end")]             // a made-up line
    [InlineData("Orders ship within two days of payment.", "src/nothing.ts:1", "no file")]                 // a made-up file
    [InlineData("Orders ship within two days of payment.", "src/orders.ts:20", "outside the code")]         // a line outside the link
    public void A_code_right_draft_is_refused_retried_once_then_listed_and_nothing_changes(string replaces, string cite, string why)
    {
        var run = PrepareCodeRight();
        var before = Chapters();
        var rulings = File.ReadAllText(RulingsPath);
        Draft(run.Output, replaces, "Orders ship whenever.", cite);
        var (first, step) = FsdWrite.Check(Repo, "shop", null);
        Assert.Equal("retry", step.Status);
        Assert.Contains(first["problems"]!.AsArray(), p => ((string)p!).Contains(why, StringComparison.Ordinal));
        Draft(run.Output, replaces, "Orders ship whenever.", cite);
        Assert.Empty(TruthMap.Problems(Read(), Rulings.Load(Root, "shop").Problems));
        var (second, last) = FsdWrite.Check(Repo, "shop", null);
        Assert.Equal("checked", last.Status);
        Assert.Contains(why, (string)Assert.Single(second["listed"]!.AsArray())!["why"]!);
        Assert.Empty(Changed(before, Chapters()));
        Assert.Equal(rulings, File.ReadAllText(RulingsPath));
    }

    [Fact]
    public void A_draft_cannot_carry_a_heading_a_marker_or_a_second_line_into_the_fsd()
    {
        foreach (var bad in new[] { "# 9 New chapter", "Orders ship. <!-- S-0002 -->", "Orders ship.\n## 1.2 Sneaky" })
        {
            var run = PrepareCodeRight();
            Draft(run.Output, "Orders ship within two days of payment.", bad, "src/config.ts:2");
            var (result, step) = FsdWrite.Check(Repo, "shop", null);
            Assert.Equal("retry", step.Status);
            Assert.Empty(result["written"]!.AsArray());
        }
    }

    [Fact]
    public void A_section_edited_since_its_rulings_or_a_quote_found_twice_is_listed_not_written()
    {
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email.\"");
        // A hand edit elsewhere in section 1, not committed: writing would hide it under the moved hash.
        var name = Chapters().Keys.Single(k => k.Contains("s0001", StringComparison.Ordinal));
        var path = Path.Combine(ChapterDir, name);
        File.WriteAllText(path, File.ReadAllText(path).Replace("Each order shows", "Every order shows", StringComparison.Ordinal));
        var before = Chapters();
        var (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Empty(result["written"]!.AsArray());
        Assert.Contains("edited since its rulings were recorded", (string)Assert.Single(result["listed"]!.AsArray())!["why"]!);
        Assert.Empty(Changed(before, Chapters()));

        // The same sentence twice in the section: which one is meant can't be told.
        File.WriteAllText(path, File.ReadAllText(path).Replace("Every order shows", "Each order shows", StringComparison.Ordinal)
            .Replace("Each order shows a Cancel button until it ships.", "Each order shows a Cancel button until it ships.\nA refund sends the customer an email.", StringComparison.Ordinal));
        Commit("the sentence twice");
        Rulings.Refresh(Repo, "shop");   // the section changed: its ruling is blanked and flagged; the PM answers again
        File.WriteAllLines(RulingsPath, File.ReadAllLines(RulingsPath).Where(l => !l.TrimStart().StartsWith("flagged:", StringComparison.Ordinal)));
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email.\"");
        (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Contains("2 times", (string)Assert.Single(result["listed"]!.AsArray())!["why"]!);
    }

    [Fact]
    public void A_ruling_drafted_from_jira_is_written_only_once_committed_and_an_unwritten_committed_ruling_still_judges()
    {
        var drafted = JiraRulings.Draft(Repo, "shop", new JsonObject { ["section"] = "S-0001", ["quote"] = "A refund sends the customer an email." }, "conflict", null, [],
            new JiraRulings.Reply("SHOP-1", "101", "Jo Jira", "2026-11-17T10:00:00.000+0000", "A refund sends no email."));
        Assert.Equal("drafted", (string)drafted["result"]!);
        var (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Empty(result["written"]!.AsArray());
        Assert.Contains("Jira", (string)Assert.Single(result["listed"]!.AsArray())!["why"]!);

        // Committed, it judges tickets before it is written (D376) …
        Commit("confirm the Jira ruling", "Dana Lead");
        Assert.True(TruthMap.Check(Repo, "shop", out _)["ok"]!.GetValue<bool>());
        var truth = Stangent.Tickets.TruthList.Build(Repo, ["src/orders.ts"]);
        var t = Assert.Single(truth["entries"]!.AsArray());
        Assert.Equal(("ruled", true), ((string)t!["standing"]!, (bool)t["usable"]!));

        // … and is written the same way as the PM's own words, keeping its Jira source and who confirmed it.
        (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Single(result["written"]!.AsArray());
        var e = Written(Read());
        Assert.Equal(("Jo Jira", "Dana Lead"), ((string?)e.Jira!["author"], (string?)e.WrittenFrom!["by"]));
        Commit("write it", "Dana Lead");
        Assert.True(TruthMap.Check(Repo, "shop", out _)["ok"]!.GetValue<bool>());
        t = Assert.Single(Stangent.Tickets.TruthList.Build(Repo, ["src/orders.ts"])["entries"]!.AsArray());
        Assert.Equal(("confirmed", true, "A refund sends no email."), ((string)t!["standing"]!, (bool)t["usable"]!, (string?)t["key"]!["quote"]));
    }

    [Fact]
    public void Bold_marks_around_the_quote_are_replaced_with_it_and_marks_that_run_past_it_are_listed()
    {
        var name = Chapters().Keys.Single(k => k.Contains("s0001", StringComparison.Ordinal));
        var path = Path.Combine(ChapterDir, name);
        File.WriteAllText(path, File.ReadAllText(path).Replace("A refund sends the customer an email.", "**A refund sends the customer an email.**", StringComparison.Ordinal)
            .Replace("Orders ship within two days of payment.", "Orders ship **within two days of payment. Always** on time.", StringComparison.Ordinal));
        Commit("bold");
        Review(
            F("conflict", "1", "S-0001", "A refund sends the customer an email.", "No email is sent.", "src/orders.ts:5"),
            F("gap", "1", "S-0001", "Orders ship within two days of payment.", "No shipping rule.", "src/config.ts:2"));
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email.\"");
        Rule("Orders ship within", "ruling", "restated");
        Rule("Orders ship within", "statement", "\"Orders ship when marked.\"");
        var (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Single(result["written"]!.AsArray());
        Assert.Contains("bold or code marks", (string)Assert.Single(result["listed"]!.AsArray())!["why"]!);
        var text = File.ReadAllText(path);
        Assert.Contains("\nA refund sends no email.\n", text);
        Assert.Contains("Orders ship **within two days of payment. Always** on time.", text);
    }

    string ChapterOf(string id) => Path.Combine(ChapterDir, Chapters().Keys.Single(k => k.Contains(id.Replace("-", "").ToLowerInvariant(), StringComparison.Ordinal)));

    [Fact]
    public void A_wrapped_quote_then_words_added_to_a_later_section_of_the_same_file_both_land_right()
    {
        // Fresh-check defect 1: the --section target was found before an edit that moved its lines.
        var path = ChapterOf("S-0001");
        File.WriteAllText(path, File.ReadAllText(path).Replace("A refund sends the customer an email.", "A refund sends the\ncustomer an email.", StringComparison.Ordinal));
        Commit("wrap");
        Rulings.Refresh(Repo, "shop");
        Assert.Equal(1, Read().Sections.Single(b => b.SectionId == "S-0001").Entries.Count(e => e.State == "conflict"));
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email.\"");
        Rule("file:web", "ruling", "restated");
        Rule("file:web", "statement", "\"The order list can be sorted by date.\"");
        var (result, _) = FsdWrite.Run(Repo, "shop", null, "S-0002", null, null);
        Assert.Equal(2, result["written"]!.AsArray().Count);
        Assert.Empty(result["listed"]!.AsArray());
        var text = File.ReadAllText(path);
        Assert.Contains("A refund sends no email.", text);
        Assert.DoesNotContain("customer an email", text);
        Assert.Contains("The order list shows 20 orders per page.\n\nThe order list can be sorted by date.", text);
        Assert.True((bool)FsdCheck.Run(Repo, "shop", [], [])["ok"]!);
    }

    [Fact]
    public void Words_go_into_a_crlf_chapter_with_crlf_and_into_an_empty_section_under_its_heading()
    {
        var path = ChapterOf("S-0003");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Settings hold the shop's currency.", "", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        Commit("empty settings, crlf");
        Rule("file:web", "ruling", "restated");
        Rule("file:web", "statement", "\"Settings list every saved filter.\"");
        var (result, _) = FsdWrite.Run(Repo, "shop", null, "S-0003", null, null);
        Assert.Single(result["written"]!.AsArray());
        var text = File.ReadAllText(path);
        Assert.Contains("Settings list every saved filter.", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.True((bool)FsdCheck.Run(Repo, "shop", [], [])["ok"]!);
    }

    [Theory]
    [InlineData("- Orders ship when marked.")]
    [InlineData("> Orders ship when marked.")]
    [InlineData("Orders ship ``` when marked.")]
    [InlineData("Orders ship **when marked.")]
    [InlineData("Orders <b>ship</b> when marked.")]
    public void Words_that_would_change_the_sections_layout_are_listed(string words)
    {
        Rule("Orders ship within", "ruling", "restated");
        Rule("Orders ship within", "statement", System.Text.Json.JsonSerializer.Serialize(words));
        var before = Chapters();
        var (result, _) = FsdWrite.Run(Repo, "shop", null, null, null, null);
        Assert.Empty(result["written"]!.AsArray());
        Assert.Single(result["listed"]!.AsArray());
        Assert.Empty(Changed(before, Chapters()));
    }

    [Fact]
    public void A_cite_with_a_huge_line_number_fails_the_draft_and_does_not_crash()
    {
        var run = PrepareCodeRight();
        Draft(run.Output, "Orders ship within two days of payment.", "Orders ship when marked.", "src/config.ts:99999999999");
        var (result, step) = FsdWrite.Check(Repo, "shop", null);
        Assert.Equal("retry", step.Status);
        Assert.Contains(result["problems"]!.AsArray(), p => ((string)p!).Contains("aren't a range", StringComparison.Ordinal));
    }

    [Fact]
    public void A_retry_cannot_rewrite_an_entry_already_written_and_a_jira_ruling_uncommitted_since_is_refused()
    {
        // Two code-right rulings, one from Jira; the first draft writes the PM's one and fails the other.
        Rule("Orders ship within", "ruling", "code-right");
        var drafted = JiraRulings.Draft(Repo, "shop", new JsonObject { ["section"] = "S-0001", ["quote"] = "A refund sends the customer an email." }, "conflict", null, [],
            new JiraRulings.Reply("SHOP-1", "101", "Jo Jira", "2026-11-17T10:00:00.000+0000", "the code is right"));
        Assert.Equal("drafted", (string)drafted["result"]!);
        Commit("rulings");
        var (_, step) = FsdWrite.Run(Repo, "shop", "1", null, null, null);
        var output = Path.Combine(Root, step!.Output!);
        var input = File.ReadAllText(Path.Combine(FsdWrite.RunDir(Root, "shop"), "input.md"));
        var gapId = input.IndexOf("> Orders ship", StringComparison.Ordinal) < input.IndexOf("> A refund", StringComparison.Ordinal) ? "W1" : "W2";
        var jiraId = gapId == "W1" ? "W2" : "W1";
        // The Jira ruling's line changes, uncommitted, between write and write-check.
        File.WriteAllText(RulingsPath, File.ReadAllText(RulingsPath).Replace("\"Jo Jira\"", "\"Jo Jira (edited)\"", StringComparison.Ordinal));
        File.WriteAllText(output, new JsonObject { ["drafts"] = new JsonArray(
            new JsonObject { ["entry"] = gapId, ["replaces"] = "Orders ship within two days of payment.", ["sentence"] = "Orders ship when marked.", ["cites"] = new JsonArray("src/config.ts:2") },
            new JsonObject { ["entry"] = jiraId, ["replaces"] = "A refund sends the customer an email.", ["sentence"] = "A refund sends no email.", ["cites"] = new JsonArray("src/orders.ts:5") }) }.ToJsonString());
        Assert.Empty(TruthMap.Problems(Read(), Rulings.Load(Root, "shop").Problems));
        var (first, retry) = FsdWrite.Check(Repo, "shop", null);
        Assert.Equal("retry", retry.Status);
        Assert.Single(first["written"]!.AsArray());
        Assert.Contains(first["problems"]!.AsArray(), p => ((string)p!).Contains("Jira", StringComparison.Ordinal));
        var afterFirst = Chapters();
        // The retry tries the written entry again with other words: refused, nothing rewritten.
        File.WriteAllText(output, new JsonObject { ["drafts"] = new JsonArray(
            new JsonObject { ["entry"] = gapId, ["replaces"] = "Orders ship within two days of payment.", ["sentence"] = "Orders never ship.", ["cites"] = new JsonArray("src/config.ts:2") }) }.ToJsonString());
        Assert.Empty(TruthMap.Problems(Read(), Rulings.Load(Root, "shop").Problems));
        var (second, last) = FsdWrite.Check(Repo, "shop", null);
        Assert.Equal("checked", last.Status);
        Assert.Empty(second["written"]!.AsArray());
        Assert.Contains(second["problems"]!.AsArray(), p => ((string)p!).Contains("written already", StringComparison.Ordinal));
        Assert.Empty(Changed(afterFirst, Chapters()));
        Assert.Single(second["listed"]!.AsArray());
    }
}
