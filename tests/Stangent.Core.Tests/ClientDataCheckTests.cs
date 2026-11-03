using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Core.DataCheck;

namespace Stangent.Core.Tests;

public sealed class ClientDataCheckTests : IDisposable
{
    readonly string outside = Directory.CreateTempSubdirectory("stangent-data-outside").FullName;
    readonly string repo = Directory.CreateTempSubdirectory("stangent-data-repo").FullName;

    public ClientDataCheckTests() => Git("init", "-q");

    public void Dispose()
    {
        Directory.Delete(outside, true);
        Directory.Delete(repo, true);
    }

    void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    void Track(string path, string text)
    {
        var full = Path.Combine(repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        Git("add", "--", path);
    }

    string Terms(string text = "Acme Bank\n")
    {
        var path = Path.Combine(outside, "terms.txt");
        File.WriteAllText(path, text);
        return path;
    }

    JsonObject Check(string? terms = null, string? variable = null, string? home = null) =>
        ClientDataCheck.Run(repo, terms, variable, home ?? outside);

    static List<JsonNode> Findings(JsonObject report) => [.. report["findings"]!.AsArray().Select(f => f!)];

    [Fact]
    public void A_clean_repo_passes()
    {
        Track("src/a.cs", "class A {}\n");
        var report = Check(Terms());
        Assert.True((bool)report["ok"]!);
        Assert.True((bool)report["termsChecked"]!);
        Assert.Equal(1, (int)report["filesScanned"]!);
        Assert.Empty(Findings(report));
    }

    [Theory]
    [InlineData("a.docx")] [InlineData("a.doc")] [InlineData("a.pdf")] [InlineData("a.xlsx")] [InlineData("a.xls")]
    [InlineData("a.pptx")] [InlineData("a.ppt")] [InlineData("a.msg")] [InlineData("a.eml")] [InlineData("x/A.PDF")]
    public void A_tracked_document_is_flagged_even_with_no_terms_file(string path)
    {
        Track(path, "x");
        var report = Check();
        Assert.False((bool)report["ok"]!);
        Assert.False((bool)report["termsChecked"]!);
        var finding = Assert.Single(Findings(report));
        Assert.Equal("document", (string)finding["kind"]!);
        Assert.Equal(path, (string)finding["file"]!);
    }

    [Fact]
    public void An_untracked_document_is_not_this_checks_business()
    {
        File.WriteAllText(Path.Combine(repo, "local.pdf"), "x");
        Assert.True((bool)Check()["ok"]!);
    }

    [Fact]
    public void A_committed_allow_entry_with_a_reason_lets_a_document_through()
    {
        Track("fixtures/fsd.docx", "x");
        Track(ClientDataCheck.AllowFile, "# Made-up documents only.\nfixtures/fsd.docx # planted test document, no client text\n");
        var report = Check();
        Assert.True((bool)report["ok"]!);
        Assert.Equal(1, (int)report["allowedDocuments"]!);
    }

    [Fact]
    public void An_allow_file_git_doesnt_track_counts_for_nothing()
    {
        Track("a.pdf", "x");
        File.WriteAllText(Path.Combine(repo, ClientDataCheck.AllowFile), "a.pdf # local only\n");
        var report = Check();
        Assert.False((bool)report["ok"]!);
        Assert.Equal("ignored: not tracked", (string)report["allowFile"]!);
    }

    [Theory]
    [InlineData("a.pdf", "no reason given")]
    [InlineData("a.pdf # ", "no reason given")]
    [InlineData("gone.pdf # used to be here", "not a tracked file")]
    public void A_bad_allow_entry_is_a_finding_and_allows_nothing(string entry, string problem)
    {
        Track("a.pdf", "x");
        Track(ClientDataCheck.AllowFile, entry + "\n");
        var findings = Findings(Check());
        Assert.Contains(findings, f => (string)f["kind"]! == "document");
        var bad = Assert.Single(findings, f => (string)f["kind"]! == "allow-entry");
        Assert.Equal(problem, (string)bad["problem"]!);
        Assert.Equal(1, (int)bad["line"]!);
    }

    [Fact]
    public void A_term_is_found_in_any_case_and_only_its_position_comes_out()
    {
        Track("notes.md", "first line\nfor aCME bank, by March\n");
        var report = Check(Terms("# clients\n\nOther Corp\n  Acme Bank  \n"));
        Assert.False((bool)report["ok"]!);
        var finding = Assert.Single(Findings(report));
        Assert.Equal("term", (string)finding["kind"]!);
        Assert.Equal("notes.md", (string)finding["file"]!);
        Assert.Equal(2, (int)finding["line"]!);
        Assert.Equal(5, (int)finding["column"]!);
        Assert.Equal(4, (int)finding["termLine"]!);
        var printed = report.ToJsonString();
        Assert.DoesNotContain("acme", printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("March", printed);
    }

    [Fact]
    public void A_path_holding_a_term_is_flagged_and_hidden()
    {
        Track("a.md", "plain\n");
        Track("clients/acme bank/brief.pdf", "the Acme Bank plan\n");
        var report = Check(Terms());
        var findings = Findings(report);
        Assert.Equal(["term-in-path", "document", "term"], findings.Select(f => (string)f["kind"]!));
        Assert.All(findings, f => Assert.Equal(2, (int)f["fileIndex"]!));
        Assert.DoesNotContain("acme", report.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_allow_entry_holding_a_term_is_flagged_without_showing_it()
    {
        Track(ClientDataCheck.AllowFile, "gone.pdf # from Acme Bank\n");
        var report = Check(Terms());
        Assert.Contains(Findings(report), f => (string)f["kind"]! == "term" && (string)f["file"]! == ClientDataCheck.AllowFile);
        Assert.DoesNotContain("acme", report.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Binary_files_are_skipped_and_counted()
    {
        File.WriteAllBytes(Path.Combine(repo, "blob.bin"), [.. "Acme Bank"u8, 0, 1, 2]);
        Git("add", "blob.bin");
        Track("a.txt", "fine\n");
        var report = Check(Terms());
        Assert.True((bool)report["ok"]!);
        Assert.Equal(1, (int)report["skippedBinary"]!);
        Assert.Equal(1, (int)report["filesScanned"]!);
    }

    [Fact]
    public void Links_and_deleted_files_are_not_read()
    {
        if (OperatingSystem.IsWindows()) return;
        var secret = Terms();
        File.CreateSymbolicLink(Path.Combine(repo, "link.txt"), secret);
        Git("add", "link.txt");
        Track("gone.txt", "x");
        File.Delete(Path.Combine(repo, "gone.txt"));
        var report = Check(secret);
        Assert.True((bool)report["ok"]!);
        Assert.Equal(2, (int)report["skippedOther"]!);
    }

    [Fact]
    public void Terms_come_from_the_option_then_the_variable_then_the_default()
    {
        Track("a.txt", "alpha beta gamma\n");
        var alpha = Path.Combine(outside, "alpha.txt"); File.WriteAllText(alpha, "alpha\n");
        var beta = Path.Combine(outside, "beta.txt"); File.WriteAllText(beta, "beta\ngamma\n");
        Directory.CreateDirectory(Path.Combine(outside, ".config", "stangent"));
        File.WriteAllText(Path.Combine(outside, ".config", "stangent", "client-terms.txt"), "a\nb\nc\n");

        Assert.Single(Findings(Check(alpha, beta)));
        Assert.Equal(2, Findings(Check(null, beta)).Count);
        Assert.Equal(3, Findings(Check()).Count);
    }

    [Fact]
    public void An_empty_terms_file_checks_nothing_and_says_so()
    {
        Track("a.txt", "x\n");
        var report = Check(Terms("# nothing yet\n"));
        Assert.True((bool)report["ok"]!);
        Assert.False((bool)report["termsChecked"]!);
    }

    [Fact]
    public void A_terms_file_someone_named_must_exist()
    {
        var missing = Path.Combine(outside, "nope.txt");
        Assert.Equal(ExitCodes.NotFound, Assert.Throws<StangentException>(() => Check(missing)).ExitCode);
        Assert.Equal("terms_file_missing", Assert.Throws<StangentException>(() => Check(null, missing)).Code);
    }

    [Fact]
    public void A_terms_file_inside_the_repo_is_refused_without_reading_it()
    {
        Track("t/terms.txt", "Acme Bank\n");
        var thrown = Assert.Throws<StangentException>(() => Check(Path.Combine(repo, "t", "terms.txt")));
        Assert.Equal("terms_file_in_repo", thrown.Code);
        Assert.Equal(ExitCodes.Usage, thrown.ExitCode);
        Assert.DoesNotContain("Acme", thrown.Message);
    }

    [Fact]
    public void A_link_from_outside_to_a_terms_file_inside_is_refused_too()
    {
        if (OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(repo, "terms.txt"), "Acme Bank\n");
        var link = Path.Combine(outside, "link.txt");
        File.CreateSymbolicLink(link, Path.Combine(repo, "terms.txt"));
        Assert.Equal("terms_file_in_repo", Assert.Throws<StangentException>(() => Check(link)).Code);
    }

    [Fact]
    public void Outside_a_git_repo_is_a_usage_error()
    {
        var thrown = Assert.Throws<StangentException>(() => ClientDataCheck.Run(outside, null, null, outside));
        Assert.Equal("not_a_git_repo", thrown.Code);
    }
}
