using Stangent.Benchmark.Tickets;

namespace Stangent.Benchmark.Tests;

public class JiraCopyTests
{
    const string Refusal = "createJiraIssue: {\"error\":true,\"message\":\"Failed to convert markdown to ADF: … blockquote inside a list item (line 4); heading inside a blockquote (line 4). Move …\"}";

    [Fact]
    public void Reads_the_refused_lines_once_each() => Assert.Equal([4], JiraCopy.RefusedLines(Refusal));

    [Fact]
    public void Fences_only_the_named_block_at_its_indentation_keeping_every_character()
    {
        var text = "### Steps\r\n\r\n1. Open it:  \r\n    > # Error: invalid_request\r\n    > \r\n    > **Request ID:** `0HM`\r\n\r\n### Expected Result\r\n";
        var fenced = JiraCopy.Fence(text, [4]);
        Assert.Equal("### Steps\r\n\r\n1. Open it:  \r\n    ```\r\n    > # Error: invalid_request\r\n    > \r\n    > **Request ID:** `0HM`\r\n    ```\r\n\r\n### Expected Result\r\n", fenced);
        Assert.Equal(text, fenced.Replace("    ```\r\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fence_is_longer_than_any_backtick_run_inside_it()
    {
        Assert.Equal("````\n```\ncode\n```\n````", JiraCopy.Fence("```\ncode\n```"));
        Assert.Equal("````\n  > ```x```\n````", JiraCopy.Fence("  > ```x```", [1]).Replace("  ````", "````", StringComparison.Ordinal));
    }

    [Fact]
    public void No_usable_line_means_the_whole_text_is_one_block()
    {
        Assert.Equal("```\na\n```", JiraCopy.Fence("a", [9]));
        Assert.Empty(JiraCopy.RefusedLines("Failed to convert markdown to ADF: something"));
    }
}
