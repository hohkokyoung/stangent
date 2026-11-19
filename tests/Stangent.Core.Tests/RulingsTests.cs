using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>The truth map's layer 2 (D363, D365, D366): <c>fsd rulings</c> drafts and refreshes <c>rulings.yaml</c> from
/// review.json; <c>fsd rulings-check</c> checks it, takes who ruled from git, and writes <c>truth-map.json</c> and
/// <c>truth-map.md</c>. No AI: review.json is written as a merge would have.</summary>
public sealed class RulingsTests : IDisposable
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

    public RulingsTests()
    {
        repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        Git("init", "-q");
        Write("src/orders.ts", string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// order line {i}")) + "\n");
        Write("src/config.ts", "// a\n// b\n// c\n");
        Write("web/settings.html", "<p>1</p>\n<p>2</p>\n<p>3</p>\n");
        Write("specs/flows/index.json", """{ "flows": [ { "id": "click-refund", "file": "specs/flows/click-refund.json", "name": "Refund an order", "kind": "click" } ] }""");
        Write("specs/flows/click-refund.json", """
            { "id": "click-refund", "name": "Refund an order", "kind": "click",
              "actor": { "who": "signed-in user", "why": "[Authorize]", "label": "certain" },
              "steps": [ { "n": 1, "at": "src/orders.ts:5", "what": "calls RefundService.Refund", "label": "certain", "conditions": [] },
                         { "n": 2, "at": "src/orders.ts:6", "what": "saves Refund", "label": "guessed", "conditions": [ { "code": "order.Paid", "kind": "if" } ] } ],
              "scenarios": [ { "name": "main path: no early exit taken", "conditions": [] }, { "name": "order not paid", "conditions": [ { "code": "!order.Paid" } ] } ],
              "exits": [], "guesses": [ { "step": 2 } ], "unknowns": [] }
            """);
        Write("specs/inventory.json", """
            { "screens": [ { "id": "page:p|/settings", "title": "Settings (/settings)", "lines": [ { "at": "web/settings.html:2" } ] } ],
              "sharedParts": [], "appFrame": { "groups": [] } }
            """);
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, Stangent.Spec.Fsd.DocxWriter.Write(Outline));
        FsdImport.Import(repo.Repo, file);
        Committed.All(repo.Repo);
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

    static JsonObject F(string id, string state, string? section, string? sectionId, string? quote, string? screen, string note, int[] runs, params string[] evidence) => new()
    {
        ["id"] = id, ["state"] = state, ["section"] = section, ["sectionId"] = sectionId, ["fsdQuote"] = quote, ["screen"] = screen, ["note"] = note,
        ["evidence"] = new JsonArray([.. evidence.Select(e => (JsonNode)e)]), ["runs"] = new JsonArray([.. runs.Select(r => (JsonNode)r)]),
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

    static readonly int[] Both = [1, 2];
    static readonly int[] One = [1];
    static readonly int[] Two = [2];

    /// <summary>A review with one of each kind of entry.</summary>
    void FirstReview() => Review(
        F("F-001", "conflict", "1", "S-0001", "A refund sends the customer an email.", null, "No email is sent.", Both, "src/orders.ts:5"),
        F("F-002", "gap", "1", "S-0001", "Orders ship within two days of payment.", null, "No shipping rule.", One, "src/config.ts:2"),
        F("F-003", "undocumented", "2", "S-0003", null, null, "A debug toggle.", One, "web/settings.html:2"),
        F("F-004", "undocumented", "2", "S-0003", null, null, "A beta toggle.", Two, "web/settings.html:3"),
        F("F-005", "match", "1", "S-0001", "Each order shows a Cancel button until it ships.", null, "Cancel shown.", Both, "src/orders.ts:6"),
        F("F-006", "match", "1.1", "S-0002", "The order list shows 20 orders per page.", null, "Pages by 20.", One, "src/config.ts:1"),
        F("F-007", "match", "2", "S-0003", "Settings hold the shop's currency.", null, "Currency held.", Two, "web/settings.html:2"),
        F("F-008", "undocumented", null, null, null, "/all-filters", "A filters screen.", Two, "src/config.ts:3"));

    RulingsFile Read() => Rulings.Load(Root, "shop").File!;

    static RulingEntry Entry(RulingsFile file, string state, string? quoteStart = null) =>
        file.All.Single(e => e.State == state && (quoteStart is null || (e.KeyQuote ?? "").StartsWith(quoteStart, StringComparison.Ordinal)));

    void Edit(string from, string to)
    {
        var text = File.ReadAllText(RulingsPath);
        Assert.Contains(from, text);
        var at = text.IndexOf(from, StringComparison.Ordinal);
        File.WriteAllText(RulingsPath, text[..at] + to + text[(at + from.Length)..]);   // the first only: section 1's block comes first
    }

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

    [Fact]
    public void Draft_groups_entries_by_section_keys_them_and_sets_match_confidence()
    {
        FirstReview();
        var result = Rulings.Refresh(Repo, "shop");
        Assert.True((bool)result["created"]!);
        Assert.Equal(7, (int)result["entries"]!);   // the two settings toggles share a screen link: one entry (D365 (2))
        var file = Read();
        Assert.Equal(["S-0001", "S-0002", "S-0003"], file.Sections.Select(s => s.SectionId));
        // In a section: differences in reading order, then undocumented, then matches.
        Assert.Equal(["gap", "conflict", "match"], file.Sections[0].Entries.Select(e => e.State));
        var toggles = file.All.Single(e => e.KeyLink == "screen:page:p|/settings");
        Assert.Equal("screen:page:p|/settings", toggles.KeyLink);
        Assert.Equal(["A debug toggle.", "A beta toggle."], toggles.Notes);
        Assert.Equal([1, 2], toggles.Runs);
        Assert.Equal(["web/settings.html:2", "web/settings.html:3"], toggles.Evidence);
        Assert.Equal("click-refund", Entry(file, "conflict").Link.Id);
        // D366 (4): every run and a flow or screen → high; one of them → medium; neither → low.
        Assert.Equal("high", Entry(file, "match", "Each order").Confidence);
        Assert.Equal("low", Entry(file, "match", "The order list").Confidence);
        Assert.Equal("medium", Entry(file, "match", "Settings hold").Confidence);
        Assert.Null(Entry(file, "conflict").Confidence);
        var loose = Assert.Single(file.Undocumented);
        Assert.Null(loose.KeySection);
        Assert.All(file.All, e => Assert.Null(e.Ruling));
        // The file parses back to the same bytes.
        Assert.Equal(File.ReadAllText(RulingsPath), Rulings.Write(Read()));
        Assert.Empty(Rulings.Load(Root, "shop").Problems);
    }

    [Fact]
    public void A_refresh_carries_rulings_by_key_keeps_old_rulings_as_not_found_and_drops_blank_ones()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "ruling", "code-right");
        Rule("A refund sends", "pmNote", "\"We stopped emails in 2024.\"");
        Rule("Orders ship within", "ruling", "fsd-right");
        // A new review: other ids and order, the conflict's quote with other spacing and curly quotes, the gap gone, a new gap.
        Review(
            F("F-001", "gap", "1.1", "S-0002", "The order list shows 20 orders per page.", null, "Now a gap.", Both, "src/config.ts:1"),
            F("F-002", "conflict", "1", "S-0001", "A  refund sends the customer an email.", null, "Still no email.", One, "src/orders.ts:5"),
            F("F-003", "undocumented", "2", "S-0003", null, null, "A debug toggle.", Both, "web/settings.html:2"));
        var result = Rulings.Refresh(Repo, "shop");
        var counts = result["counts"]!;
        Assert.Equal((3, 1), ((int)counts["carried"]!, (int)counts["notFoundNow"]!));
        var file = Read();
        var conflict = Entry(file, "conflict");
        Assert.Equal("code-right", conflict.Ruling);
        Assert.Equal("We stopped emails in 2024.", conflict.PmNote);
        Assert.Null(conflict.NotFound);
        var gone = Entry(file, "gap", "Orders ship");
        Assert.Equal(("fsd-right", "c0ffee"), (gone.Ruling, gone.NotFound));
        // The list sentence was a blank match and is now a gap: the same key, so one entry, nothing to carry.
        Assert.Null(Entry(file, "gap", "The order list").Ruling);
        // Blank entries no run found again are dropped, and listed.
        Assert.Equal(3, result["droppedBlank"]!.AsArray().Count);
        Assert.DoesNotContain(file.All, e => e.KeyQuote?.StartsWith("Each order", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void A_ruled_entry_whose_link_changed_is_flagged_until_the_pm_deletes_the_line()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "ruling", "code-right");
        // The flow both the ruled conflict and the blank high match link to changes.
        var flow = Path.Combine(Root, "specs/flows/click-refund.json");
        File.WriteAllText(flow, File.ReadAllText(flow).Replace("saves Refund", "saves Refund and Payment"));
        var result = Rulings.Refresh(Repo, "shop");
        Assert.Equal(1, (int)result["counts"]!["flaggedNow"]!);
        var file = Read();
        Assert.Equal(Rulings.LinkChanged, Entry(file, "conflict").Flagged);
        Assert.Equal("code-right", Entry(file, "conflict").Ruling);
        Assert.Null(Entry(file, "match", "Each order").Flagged);   // not ruled: nothing to look at again
        // The PM looks and deletes the line; the same link again flags nothing.
        File.WriteAllLines(RulingsPath, File.ReadAllLines(RulingsPath).Where(l => l.Trim() != $"flagged: \"{Rulings.LinkChanged}\""));
        Assert.Equal(0, (int)Rulings.Refresh(Repo, "shop")["counts"]!["flaggedNow"]!);
        Assert.Null(Entry(Read(), "conflict").Flagged);
    }

    [Fact]
    public void A_confirmed_section_shows_a_later_match_as_new_and_a_state_change_blanks_a_ruling()
    {
        Review(
            F("F-001", "conflict", "1", "S-0001", "A refund sends the customer an email.", null, "No email is sent.", Both, "src/orders.ts:5"),
            F("F-002", "match", "1", "S-0001", "Each order shows a Cancel button until it ships.", null, "Cancel shown.", Both, "src/orders.ts:6"));
        Rulings.Refresh(Repo, "shop");
        Edit("    matches:\n    entries:", "    matches: confirmed\n    entries:");
        Rule("A refund sends", "ruling", "fsd-right");
        Review(
            F("F-001", "match", "1", "S-0001", "A refund sends the customer an email.", null, "It does send one.", Both, "src/orders.ts:5"),
            F("F-002", "match", "1", "S-0001", "Each order shows a Cancel button until it ships.", null, "Cancel shown.", Both, "src/orders.ts:6"),
            F("F-003", "match", "1", "S-0001", "Orders ship within two days of payment.", null, "Two days.", Both, "src/orders.ts:6"));
        Rulings.Refresh(Repo, "shop");
        var file = Read();
        Assert.True(file.Sections[0].MatchesConfirmed);
        var was = Entry(file, "match", "A refund");
        Assert.Null(was.Ruling);
        Assert.Contains("now calls this a match", was.Flagged);
        Assert.False(Entry(file, "match", "Each order").New);   // confirmed with the section
        Assert.True(Entry(file, "match", "Orders ship").New);   // a match added to a confirmed section
        Assert.Equal("confirmed", TruthMap.Standing(Entry(file, "match", "Each order"), file.Sections[0]));
        Assert.Equal("unconfirmed", TruthMap.Standing(Entry(file, "match", "Orders ship"), file.Sections[0]));
    }

    [Fact]
    public void A_changed_section_drops_its_rulings_unless_the_new_text_holds_the_restated_statement_which_is_then_a_written_match()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Edit("    matches:\n    entries:", "    matches: confirmed\n    entries:");
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email.\"");
        Rule("Orders ship within", "ruling", "fsd-right");
        Rule("The order list", "ruling", "confirmed");   // 1.1's text is part of 1's, but its own block is checked on its own text
        // A new FSD version: section 1 now says what the PM restated.
        var changed = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(changed, Stangent.Spec.Fsd.DocxWriter.Write(Outline.Replace("A refund sends the customer an email.", "A refund sends no email.\nOrders ship within two days of payment.")));
        FsdNewVersion.Take(Repo, changed);
        Committed.All(Repo);
        FirstReview();
        var result = Rulings.Refresh(Repo, "shop");
        Assert.Contains("S-0001", result["sectionsChanged"]!.AsArray().Select(s => (string)s!));
        Assert.DoesNotContain("S-0002", result["sectionsChanged"]!.AsArray().Select(s => (string)s!));
        var file = Read();
        // D386: a restated statement the new text holds is simply a match, written from the ruling.
        var restated = file.All.Single(e => e.WrittenFrom is not null);
        Assert.Equal(("match", "A refund sends no email.", "confirmed", (string?)null), (restated.State, restated.KeyQuote, restated.Ruling, restated.Flagged));
        Assert.Equal(("conflict", "restated", "A refund sends the customer an email."), ((string?)restated.WrittenFrom!["state"], (string?)restated.WrittenFrom["ruling"], (string?)restated.WrittenFrom["quote"]));
        Assert.Equal(1, (int)result["counts"]!["writtenByHand"]!);
        var gap = Entry(file, "gap");
        Assert.Null(gap.Ruling);
        Assert.Equal("FSD section changed since ruled (was fsd-right)", gap.Flagged);
        Assert.Null(file.Sections[0].Matches);
        Assert.Contains("matches were confirmed", Entry(file, "match", "Each order").Flagged);
        Assert.Equal("confirmed", Entry(file, "match", "The order list").Ruling);
    }

    void NewVersion(string outline)
    {
        File.WriteAllBytes(Path.Combine(Root, "shop.docx"), Stangent.Spec.Fsd.DocxWriter.Write(outline));
        FsdNewVersion.Take(Repo, Path.Combine(Root, "shop.docx"));
        Committed.All(Repo);
    }

    [Fact]
    public void A_subsection_edit_keeps_the_parents_rulings_unless_their_quote_went_and_a_written_match_is_flagged_when_its_section_changes_again()
    {
        Review(
            F("F-001", "conflict", "1", "S-0001", "A refund sends the customer an email.", null, "No email is sent.", Both, "src/orders.ts:5"),
            F("F-002", "gap", "1", "S-0001", "The order list shows 20 orders per page.", null, "Quoted from 1.1 under 1.", Both, "src/config.ts:1"));
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "ruling", "restated");
        Rule("A refund sends", "statement", "\"A refund sends no email.\"");
        Rule("The order list", "ruling", "fsd-right");
        // Only 1.1 changes, and with it the sentence 1's gap quotes; 1's own text, and its conflict, are untouched.
        NewVersion(Outline.Replace("The order list shows 20 orders per page.", "The order list shows 50 orders per page."));
        var result = Rulings.Refresh(Repo, "shop");
        Assert.Empty(result["sectionsChanged"]!.AsArray());
        var file = Read();
        Assert.Equal(("restated", (string?)null), (Entry(file, "conflict").Ruling, Entry(file, "conflict").Flagged));
        Assert.Equal("its FSD quote is no longer in the section since ruled (was fsd-right)", Entry(file, "gap").Flagged);
        Assert.Null(Entry(file, "gap").Ruling);
        // 1 now says what the PM restated: a written match (D386). Then it changes again: the match is blank and flagged,
        // and the review's old conflict, still in review.json, doesn't come back.
        NewVersion(Outline.Replace("A refund sends the customer an email.", "A refund sends no email."));
        Rulings.Refresh(Repo, "shop");
        Assert.Equal("confirmed", Read().All.Single(e => e.WrittenFrom is not null).Ruling);
        NewVersion(Outline.Replace("A refund sends the customer an email.", "Refunds are manual."));
        var last = Rulings.Refresh(Repo, "shop");
        var again = Read().All.Single(e => e.WrittenFrom is not null);
        Assert.Equal(((string?)null, "FSD section changed since ruled (was confirmed)", (string?)null), (again.Ruling, again.Flagged, again.NotFound));
        Assert.DoesNotContain(Read().All, e => e.State == "conflict");
        Assert.Single(last["supersededByWritten"]!.AsArray());
    }

    [Fact]
    public void A_refresh_keeps_crlf_line_endings_so_blame_stays_with_the_pm()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "ruling", "code-right");
        File.WriteAllText(RulingsPath, File.ReadAllText(RulingsPath).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Commit("rulings", "Pat PM");
        Rulings.Refresh(Repo, "shop");
        Assert.Contains("\r\n", File.ReadAllText(RulingsPath));
        Commit("refresh", "Dev");
        TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok);
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var conflict = map["sections"]![0]!["entries"]!.AsArray().OfType<JsonObject>().Single(e => (string)e["state"]! == "conflict");
        Assert.Equal("Pat PM", (string)conflict["by"]!);
    }

    [Fact]
    public void Bad_yaml_names_its_line_and_an_entrys_link_does_not_depend_on_finding_order()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "pmNote", "a: b");
        var e = Assert.Throws<StangentException>(() => Rulings.Refresh(Repo, "shop"));
        Assert.Contains("at line ", e.Message);
        Assert.Contains("in quotes", e.Message);

        // Two findings of one quote linked to two files of equal rank: the same link whichever comes first.
        string LinkOf(params JsonObject[] findings)
        {
            Review(findings);
            File.Delete(RulingsPath);
            Rulings.Refresh(Repo, "shop");
            return Entry(Read(), "gap").Link.Id;
        }
        var a = F("F-001", "gap", "1", "S-0001", "Orders ship within two days of payment.", null, "a", One, "src/config.ts:2");
        var b = F("F-002", "gap", "1", "S-0001", "Orders ship within two days of payment.", null, "b", Two, "web/missing.ts");
        Assert.Equal(LinkOf(a, b), LinkOf((JsonObject)b.DeepClone(), (JsonObject)a.DeepClone()));
    }

    [Fact]
    public void Check_refuses_bad_values_a_restated_ruling_with_no_statement_and_entries_with_no_key()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Rule("A refund sends", "ruling", "keep");                 // undocumented's word on a conflict
        Rule("Orders ship within", "ruling", "restated");          // no statement
        Edit("    matches:\n    entries:", "    matches: yes\n    entries:");
        File.AppendAllText(RulingsPath, "  - state: \"undocumented\"\n    link: { kind: \"file\", id: \"x\", title: \"x\", hash: \"h\", exact: false }\n    evidence: []\n    runs: []\n    notes: []\n    ruling: keep\n    statement:\n    pmNote:\n");
        var report = TruthMap.Check(Repo, "shop", out var ok);
        Assert.False(ok);
        var problems = report["problems"]!.AsArray().Select(p => (string)p!).ToList();
        Assert.Contains(problems, p => p.Contains("ruling \"keep\" isn't one of code-right, fsd-right, restated (a conflict)", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("a restated ruling needs a statement", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("matches \"yes\" isn't confirmed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("it has no key", StringComparison.Ordinal));
        Assert.False(File.Exists(TruthMap.JsonPath(Root, "shop")));
        // A refresh won't overwrite a file that needs fixing.
        var e = Assert.Throws<StangentException>(() => Rulings.Refresh(Repo, "shop"));
        Assert.Equal("invalid_rulings", e.Code);
    }

    [Fact]
    public void Who_ruled_comes_from_git_blame_and_survives_a_refresh()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        Commit("draft rulings");
        Rule("A refund sends", "ruling", "\"code-right\"   # the PM's own quotes and comment");
        Rule("Orders ship within", "ruling", "fsd-right");
        Edit("    matches:\n    entries:", "    matches: confirmed\n    entries:");
        var before = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok);
        Assert.Equal(3, (int)before["counts"]!["uncommitted"]!);
        Assert.Contains(before["warnings"]!.AsArray(), w => ((string)w!).Contains("committed yet", StringComparison.Ordinal));
        Commit("rulings", "Pat PM");
        // A refresh writes the PM's lines back as typed, so blame still names them.
        Rulings.Refresh(Repo, "shop");
        Commit("refresh", "Dev");
        TruthMap.Check(Repo, "shop", out ok);
        Assert.True(ok);
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var s1 = map["sections"]!.AsArray().OfType<JsonObject>().Single(s => (string)s["sectionId"]! == "S-0001");
        var entries = s1["entries"]!.AsArray().OfType<JsonObject>().ToList();
        var conflict = entries.Single(e => (string)e["state"]! == "conflict");
        Assert.Equal(("code-right", "Pat PM", true), ((string)conflict["ruling"]!, (string)conflict["by"]!, (bool)conflict["committed"]!));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", (string)conflict["on"]!);
        var match = entries.Single(e => (string)e["state"]! == "match");
        Assert.Equal(("confirmed", "Pat PM"), ((string)match["standing"]!, (string)match["by"]!));   // the section's matches line
        Assert.Equal("Pat PM", (string)s1["matches"]!["by"]!);
    }

    [Fact]
    public void The_truth_map_is_schema_valid_the_same_on_a_rebuild_and_lists_medium_and_low_matches_first()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        var report = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok);
        Assert.False((bool)report["done"]!);
        Assert.Equal(["low", "medium"], report["verifyFirst"]!.AsArray().Select(v => (string)v!["confidence"]!));
        Assert.Equal(3, (int)report["links"]!["node"]! + (int)report["links"]!["file"]!);   // the gap, the low match, the loose screen
        Assert.Contains("fallback", (string)report["summary"]!);
        var json = File.ReadAllText(TruthMap.JsonPath(Root, "shop"));
        var md = TruthView.ReadAll(Root, "shop");
        Assert.Empty(SchemaRegistry.Embedded.Validate(TruthMap.Schema, json));
        TruthMap.Check(Repo, "shop", out _);
        Assert.Equal(json, File.ReadAllText(TruthMap.JsonPath(Root, "shop")));
        Assert.Equal(md, TruthView.ReadAll(Root, "shop"));

        var map = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(("truth-map", 2), ((string)map["contract"]!, (int)map["version"]!));
        Assert.Equal(3, map["sections"]!.AsArray().Count);   // every section, with its text
        Assert.Equal("Settings hold the shop's currency.", ((string)map["sections"]![2]!["text"]!).Trim());
        var flow = map["flows"]!["click-refund"]!;
        Assert.Equal(("signed-in user", 2, 1), ((string)flow["actor"]!["who"]!, flow["steps"]!.AsArray().Count, (int)flow["guesses"]!));
        Assert.Equal("!order.Paid", (string)flow["scenarios"]![1]!["conditions"]![0]!);
        Assert.Single(map["undocumented"]!.AsArray());

        Assert.StartsWith("<!-- Generated by `stangent fsd rulings-check`", md, StringComparison.Ordinal);
        Assert.Contains("`/stangent:rule shop`", md);
        // Numbered in the order shown: a section's matches question, then its entries; the code no section covers last (D371).
        Assert.Equal([1, 5, 7], map["sections"]!.AsArray().Select(s => (int)s!["matches"]!["n"]!));
        Assert.Equal([2, 3, 4], map["sections"]![0]!["entries"]!.AsArray().Select(e => (int)e!["n"]!));
        Assert.Equal(10, (int)map["undocumented"]![0]!["n"]!);
        // The PM's view (D415): the index, one summary per area, one file per module, questions in plain words.
        Assert.Contains("# The spec and the code: index", md);
        Assert.Contains("> A refund sends the customer an email.", md);
        Assert.Contains("### 3. Built differently:", md);
        Assert.Contains("**Where it stands:** **not answered yet**", md);
        Assert.Contains("**Which is right?**\n- `fsd`: the spec is right.", md);
        Assert.Contains("### 1. Looks right:", md);
        Assert.Contains("· low confidence", md);
        Assert.DoesNotContain("rulings.yaml under", md);   // no syntax for the PM
        Assert.Contains("# Not in the spec: summary", md);
        Assert.Contains("<details><summary>For developers: where in the code</summary>", md);
    }

    /// <summary>The number <paramref name="n"/>'s entry in today's truth-map.json.</summary>
    int N(string state, string? quoteStart = null)
    {
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var all = map["sections"]!.AsArray().SelectMany(s => s!["entries"]!.AsArray()).Concat(map["undocumented"]!.AsArray()).OfType<JsonObject>();
        return (int)all.Single(e => (string)e["state"]! == state && (quoteStart is null || ((string?)e["key"]!["quote"] ?? "").StartsWith(quoteStart, StringComparison.Ordinal)))["n"]!;
    }

    JsonObject Answer(int n, string? answer, string? words = null, string? note = null) => PmAnswers.Apply(Repo, "shop", n, answer, words, note);

    [Fact]
    public void The_pm_answers_by_number_and_only_the_answered_lines_change()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        Commit("draft rulings");
        var before = File.ReadAllLines(RulingsPath);
        Assert.Equal("3 · 1 Orders, “A refund sends the customer an email.”: the FSD is right (the code has a bug).", (string)Answer(3, "the FSD is right.")["summary"]!);
        Answer(2, null, words: "Orders ship within three days.");
        Answer(1, "yes");
        Answer(8, "keep", "The settings screen sets the shop's currency.");
        Answer(10, "Remove", note: "An old screen.");
        Answer(6, "no");
        Answer(3, "code");   // answered again: the map isn't rebuilt, so 3 still means the conflict
        var file = Read();
        Assert.Equal("code-right", Entry(file, "conflict").Ruling);
        Assert.Equal(("restated", "Orders ship within three days."), (Entry(file, "gap").Ruling, Entry(file, "gap").Statement));
        Assert.True(file.Sections[0].MatchesConfirmed);
        Assert.Equal("keep", file.All.Single(e => e.KeyLink == "screen:page:p|/settings").Ruling);
        Assert.Equal(("remove", "An old screen."), (file.Undocumented[0].Ruling, file.Undocumented[0].PmNote));
        Assert.Equal("not-a-match", Entry(file, "match", "The order list").Ruling);
        // Every other line byte for byte, so git blame keeps naming whoever wrote it.
        var after = File.ReadAllLines(RulingsPath);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(9, before.Zip(after).Count(p => p.First != p.Second));   // the keep's sentence too (D417)
        Assert.Empty(Rulings.Load(Root, "shop").Problems);

        Answer(7, "yes");
        Commit("rulings", "Pat PM");
        var report = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok);
        Assert.True((bool)report["done"]!);
        var md = TruthView.ReadAll(Root, "shop");
        Assert.Contains(", 0 questions open,", md);
        Assert.True(md.Contains("answered: **the code is right (the FSD needs updating)** by Pat PM on", StringComparison.Ordinal), md);
        Assert.Contains("**Your words:** Orders ship within three days.", md);
    }

    [Fact]
    public void An_answer_that_does_not_fit_its_item_is_refused_and_writes_nothing()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        var text = File.ReadAllText(RulingsPath);
        foreach (var (n, answer, words) in new (int, string?, string?)[] {
            (3, "yes", null),            // a conflict: code or fsd
            (4, "keep", null),           // a match: yes or no
            (4, null, "my words"),       // a match takes no words
            (1, "no", null),             // a section's no goes on the one match
            (8, "fsd", null),            // undocumented code: keep or remove
            (2, "drop", null),           // found by the review: answered, not dropped
            (2, "restated", null),       // in my words, with no words
            (2, null, null),             // nothing said
            (4, "restated", "my words"), // a match is never restated: rulings-check would refuse it
            (2, "clear", "words"),       // clear takes no words
            (99, "yes", null) })         // no such question
        {
            var e = Assert.Throws<StangentException>(() => Answer(n, answer, words));
            Assert.Equal("bad_answer", e.Code);
        }
        Assert.Equal(text, File.ReadAllText(RulingsPath));
        // Numbers from a map rebuilt since are refused.
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        Assert.Contains($"questions numbered for map `{PmAnswers.MapId(map)}`", TruthView.ReadAll(Root, "shop"));
        Assert.Equal("map_rebuilt", Assert.Throws<StangentException>(() => PmAnswers.Apply(Repo, "shop", 3, "fsd", null, null, "0000beef")).Code);
        Assert.Equal(PmAnswers.MapId(map), (string)PmAnswers.Apply(Repo, "shop", 3, "fsd", null, null, PmAnswers.MapId(map))["map"]!);
        File.WriteAllText(RulingsPath, text);
        // A refresh the PM hasn't read yet: the item they answer is gone, so the answer is refused, not put elsewhere.
        Review(F("F-002", "conflict", "1", "S-0001", "A refund sends the customer an email.", null, "No email is sent.", Both, "src/orders.ts:5"));
        Rulings.Refresh(Repo, "shop");
        var gone = Assert.Throws<StangentException>(() => Answer(2, "fsd"));
        Assert.Contains("isn't in rulings.yaml any more", gone.Message);
    }

    [Fact]
    public void Answering_clears_a_flag_and_an_item_the_review_lost_can_be_dropped()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        Answer(N("conflict"), "code");
        Answer(N("gap"), "fsd");
        var flow = Path.Combine(Root, "specs/flows/click-refund.json");
        File.WriteAllText(flow, File.ReadAllText(flow).Replace("saves Refund", "saves Refund and Payment"));
        Review(F("F-001", "conflict", "1", "S-0001", "A refund sends the customer an email.", null, "No email is sent.", Both, "src/orders.ts:5"));
        Rulings.Refresh(Repo, "shop");
        Assert.Equal(Rulings.LinkChanged, Entry(Read(), "conflict").Flagged);
        Assert.Equal("c0ffee", Entry(Read(), "gap").NotFound);
        TruthMap.Check(Repo, "shop", out _);
        var flaggedMd = TruthView.ReadAll(Root, "shop");
        Assert.Contains("answer `drop` to remove it", flaggedMd);
        Assert.Contains("**look again**: the code it points at changed since it was answered", flaggedMd);   // plain words, not the file's reason
        Answer(N("conflict"), "code");   // looked again, still right
        Assert.Null(Entry(Read(), "conflict").Flagged);
        Assert.Equal("dropped: removed from the rulings", (string)Answer(N("gap"), "drop")["answer"]!);
        Assert.DoesNotContain(Read().All, e => e.State == "gap");
    }

    [Fact]
    public void A_fully_ruled_file_is_done()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        foreach (var (key, value) in new[] { ("A refund sends", "code-right"), ("Orders ship within", "fsd-right"), ("screen:page:p|/settings", "remove"), ("file:src/config.ts", "keep"),
            ("The order list", "not-a-match"), ("Settings hold", "confirmed") })
            Rule(key, "ruling", value);
        Rule("file:src/config.ts", "statement", "Config sets the shop's defaults.");   // a keep says what the kept code should do (D417)
        Edit("    matches:\n    entries:", "    matches: confirmed\n    entries:");
        var report = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok, string.Join("; ", report["problems"]!.AsArray().Select(p => (string)p!)));
        Assert.True((bool)report["done"]!);
        Assert.Empty(report["verifyFirst"]!.AsArray());
    }

    static JsonObject Item(JsonObject finding, string id) { finding["item"] = id; return finding; }

    const string WordedFlow = """
        { "id": "click-refund", "name": "Refund an order", "kind": "click",
          "start": { "at": "src/orders.ts:4", "code": "<button (click)=\"refund()\">Refund</button>" },
          "actor": { "who": "signed-in user", "why": "[Authorize]", "label": "certain" },
          "steps": [ { "n": 1, "kind": "call", "at": "src/orders.ts:5", "what": "calls RefundService.Refund", "code": "refunds.Refund(order)", "label": "certain", "conditions": [] },
                     { "n": 2, "kind": "database", "at": "src/orders.ts:6", "what": "saves Refund", "code": "db.Refunds.Add(refund)", "label": "guessed", "conditions": [ { "code": "order.Paid", "kind": "if" } ] },
                     { "n": 3, "kind": "database", "at": "src/orders.ts:8", "what": "reads Order", "code": "db.Orders.Find(id)", "label": "certain", "conditions": [] } ],
          "scenarios": [ { "name": "main path: no early exit taken", "conditions": [] } ],
          "exits": [ { "kind": "BadRequest", "at": "src/orders.ts:7", "code": "return BadRequest();", "conditions": [ { "code": "!order.Paid", "kind": "if" } ] } ],
          "guesses": [ { "step": 2 } ], "unknowns": [] }
        """;

    void Answer(params (string Item, string Text)[] lines) =>
        File.WriteAllText(Path.Combine(FlowWords.RunDir(Root, "shop"), "output.json"), new JsonObject
        {
            ["flows"] = new JsonArray(new JsonObject
            {
                ["flow"] = "V1", ["title"] = "Refund an order",
                ["lines"] = new JsonArray([.. lines.Select(l => (JsonNode)new JsonObject { ["item"] = l.Item, ["text"] = l.Text })]),
            }),
        }.ToJsonString());

    [Fact]
    public void A_line_of_plain_words_names_no_code_its_step_doesnt_have()
    {
        Assert.Null(FlowWords.LineProblem("The refund is saved when the order was paid.", "saves Refund — db.Refunds.Add(refund)"));
        Assert.Contains("RefundService.Save", FlowWords.LineProblem("The refund goes through RefundService.Save.", "saves Refund")!);
        Assert.Contains("refundService", FlowWords.LineProblem("It calls refundService first.", "saves Refund")!);
        Assert.Contains("is_paid", FlowWords.LineProblem("Only when is_paid holds.", "saves Refund")!);
        Assert.Null(FlowWords.LineProblem("Only when order.Paid holds.", "saves Refund when order.Paid"));   // the step's own name may be used
        Assert.Contains("backtick", FlowWords.LineProblem("Saves `Refund`.", "saves Refund")!);
        Assert.Contains("line break", FlowWords.LineProblem("One.\nTwo.", "saves Refund")!);
        Assert.Contains("empty", FlowWords.LineProblem("", "x")!);
        Assert.Null(FlowWords.LineProblem("Files over 1.5 MB are refused.", "x"));   // a number isn't a code name
        Assert.Null(FlowWords.LineProblem("Some lists, e.g. promoted ones, are shared.", "x"));
        Assert.Contains("MAX_RETRIES", FlowWords.LineProblem("It stops after MAX_RETRIES tries.", "x")!);
        Assert.Contains("/api/readinglist", FlowWords.LineProblem("It posts to /api/readinglist.", "x")!);
        Assert.Null(FlowWords.LineProblem("Read-only users and sign-in pages are fine.", "x"));
    }

    [Fact]
    public void Linked_flows_are_picked_by_code_worded_by_an_ai_checked_by_code_and_shown_in_the_view()
    {
        Write("specs/flows/click-refund.json", WordedFlow);
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        // The picks: the start, who may start it, what it changes (a read isn't a change), how it stops early (D416 (1)).
        var index = FlowWords.FlowIndex(Root);
        var picked = FlowWords.Pick(Root, index["click-refund"])!;
        Assert.Equal(["S", "A", "C1", "X1"], picked.Items.Select(i => i.Id));
        Assert.Equal(["order.Paid"], picked.Items.Single(i => i.Id == "C1").Conditions);
        Assert.Contains("_not in plain words yet", TruthView.ReadAll(Root, "shop"));

        // No cost choice: counted and the command named, nothing prepared (rule 9).
        var (counted, none) = FlowWords.Run(Repo, "shop", null);
        Assert.Null(none);
        Assert.Equal(1, (int)counted["waiting"]!);
        Assert.Contains("--max-cost", (string)counted["next"]!);

        var (_, step) = FlowWords.Run(Repo, "shop", "1");
        Assert.Equal(("prepared", "view", FlowWords.Writer), (step!.Status, step.Action, step.Agent));
        var input = File.ReadAllText(Path.Combine(FlowWords.RunDir(Root, "shop"), "input.md"));
        Assert.Contains("- **C1** (change): `saves Refund — db.Refunds.Add(refund)` — when `order.Paid`", input);
        Assert.Contains("- **X1** (stop): `BadRequest: return BadRequest();` — when `!order.Paid`", input);

        // A code name the step doesn't have, and a step with no line: one retry, nothing kept for that flow yet.
        Answer(("S", "The user clicks Refund on an order."), ("A", "Any signed-in user."), ("C1", "The refund is saved through RefundService.Save."));
        var (first, retry) = FlowWords.Check(Repo, "shop", null);
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("RefundService.Save", StringComparison.Ordinal));
        Assert.Contains(retry.Errors!, e => e.Contains("X1: no line", StringComparison.Ordinal));
        Assert.False(File.Exists(FlowWords.WordsPath(Root, "shop")));

        Answer(("S", "The user clicks Refund on an order."), ("A", "Any signed-in user."), ("C1", "The refund is saved, if the order was paid."),
            ("X1", "The order isn't paid: the app refuses the refund."));
        var (done, check) = FlowWords.Check(Repo, "shop", null);
        Assert.Equal("checked", check.Status);
        Assert.Equal(["click-refund"], done["worded"]!.AsArray().Select(x => (string)x!));
        Assert.Empty(SchemaRegistry.Embedded.Validate(FlowWords.Schema, File.ReadAllText(FlowWords.WordsPath(Root, "shop"))));
        var view = TruthView.ReadAll(Root, "shop");
        Assert.Contains("**How it works today** (Refund an order; from the code's flow `click-refund`", view);
        Assert.Contains("- **Actor:** Any signed-in user.", view);
        Assert.Contains("  2. The refund is saved, if the order was paid.", view);
        Assert.Contains("  - The order isn't paid: the app refuses the refund.", view);
        Assert.Contains("```mermaid\nflowchart TD\n  S[\"The user clicks Refund on an order.\"]\n  S --> C1[", view);
        Assert.Contains("  S -.-> X1[", view);
        Assert.Contains("**Actors across the app:** Any signed-in user. (1 Orders)", view);

        // Reused while the picks are the same, even when an edit above moved their lines: nothing to word, nothing paid.
        Write("specs/flows/click-refund.json", WordedFlow.Replace("src/orders.ts:6", "src/orders.ts:16", StringComparison.Ordinal));
        TruthMap.Check(Repo, "shop", out _);
        Assert.Contains("- **Actor:** Any signed-in user.", TruthView.ReadAll(Root, "shop"));
        var (again, noStep) = FlowWords.Run(Repo, "shop", "1");
        Assert.Null(noStep);
        Assert.Equal(0, (int)again["waiting"]!);
        // The flow changes: its words no longer apply, and the view says so instead of showing stale words.
        Write("specs/flows/click-refund.json", WordedFlow.Replace("\"saves Refund\"", "\"saves Refund and Payment\"", StringComparison.Ordinal));
        TruthMap.Check(Repo, "shop", out _);
        Assert.Contains("_not in plain words yet", TruthView.ReadAll(Root, "shop"));
        Assert.Equal(1, (int)FlowWords.Run(Repo, "shop", null).Result["waiting"]!);
    }

    [Fact]
    public void A_line_that_fails_twice_keeps_the_codes_own_label_marked()
    {
        Write("specs/flows/click-refund.json", WordedFlow);
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        FlowWords.Run(Repo, "shop", "none");
        var bad = new[] { ("S", "The user clicks Refund."), ("A", "Any signed-in user."), ("C1", "Calls db.Refunds.Save at once."), ("X1", "Unpaid orders are refused.") };
        Answer(bad);
        Assert.Equal("retry", FlowWords.Check(Repo, "shop", null).Step.Status);
        Answer(bad);
        var (result, step) = FlowWords.Check(Repo, "shop", null);
        Assert.Equal("checked", step.Status);
        Assert.Contains(result["problems"]!.AsArray(), p => ((string)p!).Contains("db.Refunds.Save", StringComparison.Ordinal));
        var view = TruthView.ReadAll(Root, "shop");
        Assert.Contains("_(the code's own label: its plain words failed the check)_", view);
        Assert.Contains("- **Actor:** Any signed-in user.", view);   // the passing lines are kept
    }

    [Fact]
    public void Controls_the_checklist_left_out_are_listed_numbered_last_and_hold_up_nothing()
    {
        // D414: the two settings toggles are checklist controls (S01.001, S01.002), so their entry is listed, not asked; the
        // filters screen is the reviewer's own finding, so it is still a question.
        Review(
            F("F-001", "conflict", "1", "S-0001", "A refund sends the customer an email.", null, "No email is sent.", Both, "src/orders.ts:5"),
            Item(F("F-003", "undocumented", "2", "S-0003", null, null, "The FSD doesn't mention the field “Debug”.", Both, "web/settings.html:2"), "S01.001"),
            Item(F("F-004", "undocumented", "2", "S-0003", null, null, "The FSD doesn't mention the field “Beta”.", Both, "web/settings.html:3"), "S01.002"),
            F("F-008", "undocumented", null, null, null, "/all-filters", "A filters screen.", Two, "src/config.ts:3"));
        Rulings.Refresh(Repo, "shop");
        var file = Read();
        var listed = Assert.Single(file.All, e => e.OnDemand);
        Assert.Equal("screen:page:p|/settings", listed.KeyLink);
        Assert.Contains("onDemand: true", File.ReadAllText(RulingsPath));
        Rule("A refund sends", "ruling", "code-right");
        Rule("src/config.ts", "ruling", "remove");
        var report = TruthMap.Check(Repo, "shop", out var ok);
        Assert.True(ok, string.Join("; ", report["problems"]!.AsArray().Select(p => (string)p!)));
        Assert.True((bool)report["done"]!);   // the listed entry is blank and holds nothing up
        Assert.Equal(1, (int)report["counts"]!["listed"]!);
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var entries = map["sections"]!.AsArray().SelectMany(x => x!["entries"]!.AsArray()).Concat(map["undocumented"]!.AsArray()).OfType<JsonObject>().ToList();
        var n = entries.Where(e => (bool?)e["onDemand"] != true).Max(e => (int)e["n"]!);
        var ln = (int)entries.Single(e => (bool?)e["onDemand"] == true)["n"]!;
        Assert.True(ln > n, $"listed #{ln} after every question (last question #{n})");
        Assert.Equal("blank", (string)entries.Single(e => (bool?)e["onDemand"] == true)["standing"]!);   // a ticket on it stays unclear
        var view = TruthView.ReadAll(Root, "shop");
        Assert.Contains("Listed, no answer needed", view);
        Assert.Contains($"The FSD doesn't mention the field “Debug”. (#{ln})", view);
        Assert.DoesNotContain($"### {ln}.", view);   // never asked as a question
        // An answer given anyway is kept (D414 (4)).
        Answer(ln, "remove");
        Assert.Equal("remove", Read().All.Single(e => e.OnDemand).Ruling);
    }

    [Fact]
    public void A_keep_needs_the_pms_sentence_a_new_bare_one_is_refused_and_a_committed_one_counts_as_blank()
    {
        FirstReview();
        Rulings.Refresh(Repo, "shop");
        TruthMap.Check(Repo, "shop", out _);
        // Through Claude: a bare keep is refused, and nothing is written (D417).
        var text = File.ReadAllText(RulingsPath);
        var config = (int)JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!["undocumented"]!.AsArray().OfType<JsonObject>()
            .Concat(JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!["sections"]!.AsArray().SelectMany(x => x!["entries"]!.AsArray()).OfType<JsonObject>())
            .Single(e => (string?)e["key"]!["link"] == "file:src/config.ts")["n"]!;
        var refused = Assert.Throws<StangentException>(() => Answer(config, "keep"));
        Assert.Contains("keep needs one sentence", refused.Message);
        Assert.Equal(text, File.ReadAllText(RulingsPath));
        // By hand, not committed: rulings-check refuses it.
        Rule("file:src/config.ts", "ruling", "keep");
        var report = TruthMap.Check(Repo, "shop", out var ok);
        Assert.False(ok);
        Assert.Contains(report["problems"]!.AsArray(), p => ((string)p!).Contains("keep needs a sentence", StringComparison.Ordinal));
        // Committed before D417: kept, counted as not answered, and listed until it has words.
        Commit("an old bare keep", "Pat PM");
        report = TruthMap.Check(Repo, "shop", out ok);
        Assert.True(ok, string.Join("; ", report["problems"]!.AsArray().Select(p => (string)p!)));
        Assert.Single(report["needsWords"]!.AsArray());
        var map = JsonNode.Parse(File.ReadAllText(TruthMap.JsonPath(Root, "shop")))!.AsObject();
        var kept = map["undocumented"]!.AsArray().Concat(map["sections"]!.AsArray().SelectMany(s => s!["entries"]!.AsArray())).OfType<JsonObject>()
            .Single(e => (string?)e["key"]!["link"] == "file:src/config.ts");
        Assert.Equal(("blank", true), ((string)kept["standing"]!, (bool)kept["needsWords"]!));
        Assert.Contains("but with no sentence: **say what the kept code should do**", TruthView.ReadAll(Root, "shop"));
        // With the sentence, it counts.
        Answer((int)kept["n"]!, "keep", "Config sets the shop's defaults.");
        Commit("the keep's sentence", "Pat PM");
        report = TruthMap.Check(Repo, "shop", out ok);
        Assert.True(ok);
        Assert.Empty(report["needsWords"]!.AsArray());
    }
}
