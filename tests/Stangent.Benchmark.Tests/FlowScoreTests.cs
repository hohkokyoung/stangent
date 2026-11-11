using Stangent.Benchmark.Flows;

namespace Stangent.Benchmark.Tests;

/// <summary>Item 1's code check reads the statement a step starts, to its end (D211).</summary>
public class FlowScoreTests
{
    static string[] Code(string text) => text.Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void A_long_call_chain_is_read_to_its_semicolon_and_not_into_the_next_statement()
    {
        var lines = Code(string.Join("\n", [
            "table",
            .. Enumerable.Range(1, 20).Select(i => $"    .WithColumn(\"C{i}\").AsString(1000).Nullable()"),
            "    .WithColumn(\"CurrencyId\").AsInt32().ForeignKey<Currency>(onDelete: Rule.SetNull).Nullable();",
            "Next();",
        ]));
        var text = FlowScore.Statement(lines, 1);
        Assert.Contains("ForeignKey", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Next", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_verbatim_string_with_brackets_and_quotes_and_a_lambda_body_with_semicolons_stay_inside_the_statement()
    {
        var sql = Code(""""
            return new SqlBuilder(db)
            .Select($@"""Episodes"".""SeriesId"" AS SeriesId,
                         SUM(CASE WHEN x > 0 THEN 1 ELSE 0 END) AS N;",
                parameters)
            .GroupBy<Episode>(x => x.SeriesId);
            Other();
            """");
        Assert.Contains("GroupBy", FlowScore.Statement(sql, 1), StringComparison.Ordinal);
        Assert.DoesNotContain("Other", FlowScore.Statement(sql, 1), StringComparison.Ordinal);
        var task = Code("""
            var task = taskFactory.StartNew(() =>
                 {
                     var r = list.Fetch();
                     if (r.Ok) { Save(r); }
                 }).LogExceptions();
            tasks.Add(task);
            """);
        Assert.Contains("LogExceptions", FlowScore.Statement(task, 1), StringComparison.Ordinal);
        Assert.DoesNotContain("tasks.Add", FlowScore.Statement(task, 1), StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_header_an_argument_and_a_statement_without_a_semicolon_end_where_they_end()
    {
        var code = Code("""
            } else if (Check(a)) {
                Run(b);
            }
            Send(x,
                Make(y),
                Other(z));
            const a = 1
            const b = Load()
            """);
        Assert.Equal("else if (Check(a)) {", FlowScore.Statement(code, 1));
        // An argument is read to the bracket closing its call: the arguments after it, not the next statement.
        Assert.Contains("Make(y)", FlowScore.Statement(code, 5), StringComparison.Ordinal);
        Assert.DoesNotContain("const a", FlowScore.Statement(code, 5), StringComparison.Ordinal);
        Assert.DoesNotContain("Load", FlowScore.Statement(code, 7), StringComparison.Ordinal);
        Assert.Equal("", FlowScore.Statement(code, 99));
    }

    [Fact]
    public void A_line_opening_with_a_brace_or_closing_what_came_before_is_read_from_its_own_code()
    {
        var code = Code("""
                  {translate('Details')}
                </Tab>
            { "rootFolderInfo", FormatRootFolder(a, b) }
            )} - ${formatDateTime(
            """);
        Assert.Contains("translate", FlowScore.Statement(code, 1), StringComparison.Ordinal);
        Assert.DoesNotContain("Tab", FlowScore.Statement(code, 1), StringComparison.Ordinal);
        Assert.Contains("FormatRootFolder", FlowScore.Statement(code, 3), StringComparison.Ordinal);
        Assert.Contains("formatDateTime", FlowScore.Statement(code, 4), StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_line_block_keeps_its_line_and_a_chain_goes_on_past_comment_lines()
    {
        var code = Code("""
            for (const s of series) { this.downloadService.download(s); }
            Other();
            var products = await (await Load())
                //ACL and store mapping
                .WhereAwait(async p => await Allowed(p))
                .ToListAsync();
            Next();
            """);
        Assert.Contains("download", FlowScore.Statement(code, 1), StringComparison.Ordinal);
        Assert.DoesNotContain("Other", FlowScore.Statement(code, 1), StringComparison.Ordinal);
        Assert.Contains("WhereAwait", FlowScore.Statement(code, 3), StringComparison.Ordinal);
        Assert.DoesNotContain("Next", FlowScore.Statement(code, 3), StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_import_under_another_name_counts_as_the_function_it_names()
    {
        var file = Code("""
            import fetchJson from 'Utilities/requestAction';
            await fetchJson({ url });
            """);
        Assert.True(FlowScore.Imported("requestAction", file, FlowScore.Statement(file, 2)));
        Assert.False(FlowScore.Imported("deleteAction", file, FlowScore.Statement(file, 2)));
    }
}
