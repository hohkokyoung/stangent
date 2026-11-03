using System.Text.Json.Nodes;
using Stangent.Core.Evidence;

namespace Stangent.Core.Tests;

public class EvidenceTests
{
    static readonly string Sha = new('a', 40);
    static readonly string Hash = new('b', 64);

    static JsonObject Code() => new()
    {
        ["id"] = "E-0001",
        ["citation"] = new JsonObject { ["kind"] = "code", ["ref"] = "shop/src/Cart.cs", ["lines"] = new JsonObject { ["start"] = 3, ["end"] = 9 }, ["symbol"] = "Cart.Total", ["quote"] = "return sum;" },
        ["commit"] = Sha,
        ["strength"] = "read",
        ["checkedBy"] = new JsonArray(new JsonObject { ["kind"] = "code", ["name"] = "quote-check", ["inputsHash"] = Hash }),
    };

    static IReadOnlyList<string> Errors(JsonObject o) => EvidenceValidator.Validate(o.ToJsonString());

    [Fact]
    public void A_full_code_citation_is_valid_and_survives_a_round_trip()
    {
        var json = Code().ToJsonString();
        Assert.Empty(EvidenceValidator.Validate(json));
        var record = EvidenceRecord.Parse(json);
        Assert.Equal(CitationKind.Code, record.Citation.Kind);
        Assert.Equal(EvidenceStrength.Read, record.Strength);
        Assert.Equal(new LineRange(3, 9), record.Citation.Lines);
        Assert.Equal(CanonicalJson.SerializeCompact(JsonNode.Parse(json)), record.ToJson());
        Assert.Empty(record.Validate());
    }

    [Theory]
    [InlineData("code")]
    [InlineData("test")]
    public void Code_and_test_citations_need_a_commit(string kind)
    {
        var e = Code();
        e["citation"]!["kind"] = kind;
        e.Remove("commit");
        Assert.Contains(Errors(e), m => m.Contains("needs a commit"));
    }

    [Fact]
    public void A_document_citation_needs_a_content_hash_instead_of_a_commit()
    {
        var e = Code();
        e["citation"]!["kind"] = "document";
        e["strength"] = "stated";
        Assert.Contains(Errors(e), m => m.Contains("contentHash"));
        e.Remove("commit");
        e["contentHash"] = Hash;
        Assert.Empty(Errors(e));
    }

    [Theory]
    [InlineData("page")]
    [InlineData("url")]
    public void Pages_and_urls_need_no_version(string kind)
    {
        var e = Code();
        e["citation"]!["kind"] = kind;
        e.Remove("commit");
        Assert.Empty(Errors(e));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    public void A_commit_is_exactly_forty_lowercase_hex(string commit)
    {
        var e = Code();
        e["commit"] = commit;
        Assert.NotEmpty(Errors(e));
    }

    [Theory]
    [InlineData("strength", "maybe")]
    [InlineData("id", "has space")]
    [InlineData("id", "E-1\n")]
    [InlineData("unknown", "field")]
    public void Bad_fields_are_refused(string field, string value)
    {
        var e = Code();
        e[field] = value;
        Assert.NotEmpty(Errors(e));
    }

    [Fact]
    public void Lines_must_run_forward()
    {
        var e = Code();
        e["citation"]!["lines"] = new JsonObject { ["start"] = 9, ["end"] = 3 };
        Assert.Contains("citation.lines: start is after end", Errors(e));
        e["citation"]!["lines"] = new JsonObject { ["start"] = 9, ["end"] = 9 };
        Assert.Empty(Errors(e));
    }

    [Theory]
    [InlineData("guess", true)]
    [InlineData("read", false)]
    [InlineData("stated", false)]
    [InlineData("ran", false)]
    public void Only_a_guess_may_have_no_checker(string strength, bool valid)
    {
        var e = Code();
        e["strength"] = strength;
        e["checkedBy"] = new JsonArray();
        Assert.Equal(valid, Errors(e).Count == 0);
    }

    [Theory]
    [InlineData("agent", false)]
    [InlineData("code", true)]
    [InlineData("person", true)]
    public void Ran_needs_more_than_an_agent(string kind, bool valid)
    {
        var e = Code();
        e["strength"] = "ran";
        e["checkedBy"] = new JsonArray(new JsonObject { ["kind"] = kind, ["name"] = "x" });
        Assert.Equal(valid, Errors(e).Count == 0);
    }

    [Fact]
    public void Parse_refuses_bad_evidence_and_says_why()
    {
        var e = Code();
        e["checkedBy"] = new JsonArray();
        var thrown = Assert.Throws<StangentException>(() => EvidenceRecord.Parse(e.ToJsonString()));
        Assert.Equal("invalid_evidence", thrown.Code);
        Assert.Contains("checkedBy", thrown.Details[0]);
        Assert.Equal(["#: not valid JSON"], EvidenceValidator.Validate("{nope"));
    }
}

public sealed class PermanentIdsTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("stangent-ids").FullName;
    string Ledger => Path.Combine(dir, "sub", "ids.json");

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Ids_count_up_per_scope_in_the_requirement_shape()
    {
        var ids = new PermanentIds(Ledger);
        Assert.Equal("R-0001", ids.Issue("R", "R-{0:D4}"));
        Assert.Equal("R-0002", ids.Issue("R", "R-{0:D4}"));
        Assert.Equal("Q-0001", ids.Issue("Q", "Q-{0:D4}"));
        Assert.Equal(["R-0001", "R-0002"], ids.Active("R"));
    }

    [Fact]
    public void A_removed_id_is_kept_marked_and_never_given_again()
    {
        var ids = new PermanentIds(Ledger);
        ids.Issue("R", "R-{0}");
        ids.Issue("R", "R-{0}");
        ids.Remove("R", "R-2");
        ids.Remove("R", "R-2");

        var reopened = new PermanentIds(Ledger);
        Assert.Equal("R-3", reopened.Issue("R", "R-{0}"));
        Assert.Equal(["R-1", "R-3"], reopened.Active("R"));
        Assert.Equal(["R-2"], reopened.Removed("R"));
    }

    [Fact]
    public void Removing_an_id_never_issued_is_refused()
    {
        var ids = new PermanentIds(Ledger);
        ids.Issue("R", "R-{0}");
        Assert.Equal("unknown_id", Assert.Throws<StangentException>(() => ids.Remove("R", "R-9")).Code);
        Assert.Equal("unknown_id", Assert.Throws<StangentException>(() => ids.Remove("X", "R-1")).Code);
    }

    [Theory]
    [InlineData("R-fixed")]
    [InlineData("{1}")]
    [InlineData("{0}-{0}")]
    [InlineData("{0:E0}")]
    [InlineData("{0")]
    [InlineData("{0:D4}\n")]
    public void A_format_that_could_repeat_an_id_is_refused(string format) =>
        Assert.Equal("bad_id_format", Assert.Throws<StangentException>(() => new PermanentIds(Ledger).Issue("R", format)).Code);

    [Fact]
    public void A_scope_keeps_its_first_format()
    {
        var ids = new PermanentIds(Ledger);
        ids.Issue("R", "R-{0:D4}");
        Assert.Equal("id_format_changed", Assert.Throws<StangentException>(() => ids.Issue("R", "R-{0:D5}")).Code);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"version":1,"scopes":{"R":{"format":"R-{0}"}}}""")]
    public void A_damaged_file_stops_the_call_instead_of_starting_over(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ledger)!);
        File.WriteAllText(Ledger, text);
        Assert.Equal("bad_id_ledger", Assert.Throws<StangentException>(() => new PermanentIds(Ledger).Issue("R", "R-{0}")).Code);
        Assert.Equal(text, File.ReadAllText(Ledger));
    }

    [Fact]
    public void Parallel_callers_never_get_the_same_id_and_no_temp_file_stays()
    {
        var got = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 40, _ => got.Add(new PermanentIds(Ledger).Issue("R", "R-{0}")));
        Assert.Equal(40, got.Distinct().Count());
        Assert.Equal(40, new PermanentIds(Ledger).Active("R").Count);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Ledger)!, "*.tmp"));
    }

    [Fact]
    public void The_file_is_canonical_so_the_same_state_gives_the_same_bytes()
    {
        new PermanentIds(Ledger).Issue("R", "R-{0}");
        var text = File.ReadAllText(Ledger);
        Assert.Equal(CanonicalJson.Serialize(JsonNode.Parse(text)!), text);
    }
}
