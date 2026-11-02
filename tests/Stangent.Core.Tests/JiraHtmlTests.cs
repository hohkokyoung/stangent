using System.Text.RegularExpressions;
using Stangent.Tickets.Jira;

namespace Stangent.Core.Tests;

/// <summary>D102: a body Jira sent as HTML reads as the Markdown it would otherwise have sent.</summary>
public class JiraHtmlTests
{
    [Theory]
    [InlineData("<p>We've got a <a href=\"https://github.com/o/r/pull/5751\">PR up</a> now.</p>", "We've got a [PR up](https://github.com/o/r/pull/5751) now.")]
    [InlineData("<p><a href=\"https://vault.example.com\" data-card-appearance=\"inline\">https://vault.example.com</a></p>", "https://vault.example.com")]
    [InlineData("<p>see <a href=\"https://x.test/a (b)\">this</a></p>", "see [this](<https://x.test/a (b)>)")]
    [InlineData("<p><a href=\"\">plain</a></p>", "plain")]
    [InlineData("<figure data-type=\"media-single\"><img src=\"https://img.test/1.png\" alt=\"Image\"></figure><p>License info:</p>", "![Image](https://img.test/1.png)\n\nLicense info:")]
    [InlineData("<p>Look at <code>logs/identity.txt</code> and <code>a`b</code></p>", "Look at `logs/identity.txt` and ``a`b``")]
    [InlineData("<p><em>x wrote:</em> <strong> bold </strong> <s>gone</s></p>", "_x wrote:_  **bold**  ~~gone~~")]
    [InlineData("<h3>Expected Result</h3><p>Works</p>", "### Expected Result\n\nWorks")]
    [InlineData("<p>a<br>b</p><hr><p>c</p>", "a\nb\n\n---\n\nc")]
    [InlineData("<p>&lt;tag&gt; &amp; &quot;q&quot; &#039;s&#039;</p>", "<tag> & \"q\" 's'")]
    [InlineData("<p>Unknown <span class=\"x\">kept</span> text</p>", "Unknown kept text")]
    public void Inline_and_simple_blocks(string html, string markdown) => Assert.Equal(markdown, JiraHtml.ToMarkdown(html));

    [Fact]
    public void Lists_keep_numbers_ticks_and_nesting()
    {
        Assert.Equal("1. Login\n2. Look at `x.txt`", JiraHtml.ToMarkdown("<ol><li><p>Login</p></li><li><p>Look at <code>x.txt</code></p></li></ol>"));
        Assert.Equal("3. c\n4. d", JiraHtml.ToMarkdown("<ol start=\"3\"><li>c</li><li>d</li></ol>"));
        Assert.Equal("- [x] I understand\n- [ ] Not yet", JiraHtml.ToMarkdown(
            "<ul data-type=\"task-list\"><li data-type=\"task-item\"><input type=\"checkbox\" checked> I understand</li><li data-type=\"task-item\"><input type=\"checkbox\"> Not yet</li></ul>"));
        Assert.Equal("- a\n\n  - b\n  - c\n- d", JiraHtml.ToMarkdown("<ul><li><p>a</p><ul><li>b</li><li>c</li></ul></li><li>d</li></ul>"));
    }

    [Fact]
    public void Code_blocks_keep_their_language_lines_and_blank_lines()
    {
        Assert.Equal("```console\n\"GET /a\" 200\n\n\n\"GET /b\" 302   \n```\n\nafter",
            JiraHtml.ToMarkdown("<pre><code class=\"language-console\">&quot;GET /a&quot; 200\n\n\n&quot;GET /b&quot; 302   \n</code></pre><p>after</p>"));
        Assert.Equal("````\n```\nx\n```\n````", JiraHtml.ToMarkdown("<pre><code>```\nx\n```</code></pre>"));
        Assert.Equal("1. Run:\n\n   ```\n   a\n\n   b\n   ```", JiraHtml.ToMarkdown("<ol><li><p>Run:</p><pre><code>a\n\nb</code></pre></li></ol>"));
    }

    [Fact]
    public void Quotes_and_tables()
    {
        Assert.Equal("> # Error\n>\n> ![image](https://i.test/a.png)", JiraHtml.ToMarkdown("<blockquote><h1>Error</h1><figure><img src=\"https://i.test/a.png\" alt=\"image\"></figure></blockquote>"));
        Assert.Equal("| a | b |\n| --- | --- |\n| 1 | x\\|y |", JiraHtml.ToMarkdown("<table><tbody><tr><th>a</th><th>b</th></tr><tr><td>1</td><td>x|y</td></tr></tbody></table>"));
    }

    [Fact]
    public void Broken_HTML_keeps_its_text()
    {
        Assert.Equal("a _b_\n\n c", JiraHtml.ToMarkdown("<p>a <em>b</p> c")); // the paragraph's end closes the em
        Assert.Equal("x < y", JiraHtml.ToMarkdown("<p>x < y</p></div>"));
    }

    [Fact]
    public void Review_findings_stay_fixed()
    {
        // A list's other children (Jira nests task lists directly in the list) are kept under the item before them.
        Assert.Equal("- a\n  - b\n  stray", JiraHtml.ToMarkdown("<ul><li>a</li><ul><li>b</li></ul>stray</ul>"));
        Assert.Equal("- [ ]\n\n  - child", JiraHtml.ToMarkdown("<ul><li><input type=\"checkbox\"><ul><li>child</li></ul></li></ul>"));
        // An unquoted value ending in "/" isn't a self-closing tag; "<br/>" and "<img />" are.
        Assert.Equal("[docs](http://x/)", JiraHtml.ToMarkdown("<a href=http://x/>docs</a>"));
        Assert.Equal("a\nb ![i](s)", JiraHtml.ToMarkdown("a<br/>b <img src=\"s\" alt=\"i\" />"));
        // The private-use marks code blocks use can't come from the ticket.
        Assert.Equal("ab\n\n```\nc\n\n\nd\n```", JiraHtml.ToMarkdown("<p>a\uE000b</p><pre>c\n\n\nd</pre>"));
        // A table's caption is kept; a table in a cell isn't counted as rows of the outer one.
        Assert.Equal("Cap\n\n| x |\n| --- |\n| y |", JiraHtml.ToMarkdown("<table><caption>Cap</caption><thead><tr><th>x</th></tr></thead><tbody><tr><td>y</td></tr></tbody></table>"));
        Assert.Equal(2, JiraHtml.ToMarkdown("<table><tr><td>o</td></tr><tr><td><table><tr><td>i</td></tr></table></td></tr></table>").Split('\n').Count(l => l.StartsWith("| ") && !l.StartsWith("| ---")));
        // An image with no source keeps its alt text; a line break right after <pre> is dropped, as in HTML.
        Assert.Equal("logo", JiraHtml.ToMarkdown("<img alt=\"logo\">"));
        Assert.Equal("```\nx\n```", JiraHtml.ToMarkdown("<pre>\nx</pre>"));
        Assert.Equal("> ```\n> a\n> b\n> ```", JiraHtml.ToMarkdown("<blockquote><pre>a<br>b</pre></blockquote>"));
    }

    [Fact]
    public void Unclosed_tags_take_linear_time()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        JiraHtml.ToMarkdown(string.Concat(Enumerable.Repeat("<a ", 40_000)));
        JiraHtml.ToMarkdown("<a title=\"" + new string('x', 120_000));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    /// <summary>Real bodies Jira returned as HTML (tests/fixtures/tickets/html): nothing a reader or the finder could use is lost.</summary>
    [Fact]
    public void Real_Jira_bodies_lose_no_link_image_code_or_number()
    {
        var dir = Path.Combine(RepoRoot.Find(), "tests", "fixtures", "tickets", "html");
        var files = Directory.GetFiles(dir, "*.html");
        Assert.True(files.Length >= 90);
        foreach (var f in files)
        {
            var html = File.ReadAllText(f);
            var md = JiraHtml.ToMarkdown(html);
            var why = Path.GetFileName(f);
            // Pinned: each body's Markdown is checked in beside it (regenerate only after reading the difference).
            Assert.True(File.ReadAllText(Path.ChangeExtension(f, ".md")) == md + "\n", $"{why}: differs from its .md");
            foreach (Match m in Regex.Matches(html, "<a [^>]*href=\"([^\"]+)\""))
                Assert.True(md.Contains(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), StringComparison.Ordinal), $"{why}: link {m.Groups[1].Value}");
            foreach (Match m in Regex.Matches(html, "<img [^>]*src=\"([^\"]+)\""))
                Assert.True(md.Contains("](" + System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) + ")", StringComparison.Ordinal), $"{why}: image {m.Groups[1].Value}");
            foreach (Match m in Regex.Matches(html, "(?<!<pre>)<code>([^<`]+)</code>"))
                Assert.True(md.Contains("`" + System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) + "`", StringComparison.Ordinal), $"{why}: code {m.Groups[1].Value}");
            Assert.Equal(Regex.Matches(html, "<pre>").Count * 2, Regex.Matches(md, "^\\s*(?:> )?```", RegexOptions.Multiline).Count);
            Assert.Equal(Regex.Matches(html, "type=\"checkbox\"").Count, Regex.Matches(md, @"- \[[ x]\] ").Count);
            // No tag survives, except tags the reporter typed as text (Jira stores them escaped: "&lt;img").
            const string tags = @"(p|a|li|ul|ol|em|strong|code|pre|figure|img|h\d|blockquote)\b";
            Assert.True(Regex.Matches(Regex.Replace(md, "```.*?```", "", RegexOptions.Singleline), "</?" + tags).Count <= Regex.Matches(html, "&lt;/?" + tags).Count, $"{why}: a tag left");
            // Every word of the visible text is still there.
            var words = Regex.Matches(System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"[\p{L}\p{N}]{3,}").Select(w => w.Value).Distinct();
            foreach (var w in words) Assert.True(md.Contains(w, StringComparison.Ordinal), $"{why}: word {w}");
        }
    }
}
