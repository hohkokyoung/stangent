using Stangent.Graph;
using Stangent.Graph.Impact;

namespace Stangent.Core.Tests;

/// <summary>code-graph v7 (D362 (5)): a class's string constants that fill its file-name pattern link to the template
/// files they name; a template beside them that no constant names is a blind spot. On a copy of the shop fixture with a
/// mailer added.</summary>
public sealed class TemplateFilesTests
{
    const string Mailer = """
        namespace Shop.Core.Mail;

        public class Mailer
        {
            private const string TemplatePath = @"{0}.html";
            public const string WelcomeTemplate = "Welcome";
            public static readonly string ResetTemplate = "Reset";
            public const string Subject = "Your order";       // a value, not a base name
            public const string Unused = "Nothing";            // names no file

            public string Body(string name) => string.Format(TemplatePath, name);
            public string Welcome() => Body(WelcomeTemplate);
            public string Reset() => Body(ResetTemplate);
            public string Frame() => Body("base");             // a literal names one too
            public string Log() => "Sent";                     // a literal that names no file
        }

        public class Other
        {
            public const string Welcome = "Welcome";           // no pattern in this class: no line
        }
        """;

    static void Write(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public async Task Constants_that_fill_a_file_name_pattern_link_to_the_templates_they_name()
    {
        var root = ShopCopy.Create();
        try
        {
            Write(root, "Shop.Core/Mail/Mailer.cs", Mailer);
            Write(root, "Shop.Core/Mail/Templates/Welcome.html", "<p>Hello</p>\n<p>{{Name}}</p>\n");
            Write(root, "Shop.Core/Mail/Templates/Reset.html", "<p>Reset</p>\n");
            Write(root, "Shop.Core/Mail/Templates/Orphan.html", "<p>?</p>\n");
            Write(root, "Shop.Core/Mail/Templates/base.html", "<html>{{Body}}</html>\n");
            Write(root, "Shop.Web/config/templates/Welcome.html", "<p>Hello again</p>\n");
            Write(root, "Shop.Web/config/templates/Reset.html", "<p>Reset again</p>\n");
            Write(root, "Shop.Web/stray/Welcome.html", "<p>one name alone: no template folder</p>\n");
            Write(root, "Shop.Core/bin/Debug/Templates/Welcome.html", "<p>build output</p>\n");
            var (graph, _) = await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(), TestContext.Current.CancellationToken);

            const string welcome = "Shop.Core|F:Shop.Core.Mail.Mailer.WelcomeTemplate";
            var lines = graph.AllEdges.Where(e => e.Kind == EdgeKind.SendsTemplate).Select(e => (e.Source, e.Target, e.Label)).OrderBy(e => e.Source).ThenBy(e => e.Target).ToList();
            (string, string, Label)[] expected = [
                ("Shop.Core|F:Shop.Core.Mail.Mailer.ResetTemplate", "template-file:Shop.Core/Mail/Templates/Reset.html", Label.Guessed),
                ("Shop.Core|F:Shop.Core.Mail.Mailer.ResetTemplate", "template-file:Shop.Web/config/templates/Reset.html", Label.Guessed),
                (welcome, "template-file:Shop.Core/Mail/Templates/Welcome.html", Label.Guessed),
                (welcome, "template-file:Shop.Web/config/templates/Welcome.html", Label.Guessed),
                ("Shop.Core|M:Shop.Core.Mail.Mailer.Frame", "template-file:Shop.Core/Mail/Templates/base.html", Label.Guessed)];   // a literal, from its method
            Assert.Equal(expected.OrderBy(e => e.Item1, StringComparer.Ordinal).ThenBy(e => e.Item2, StringComparer.Ordinal),
                lines.Select(l => (l.Source, l.Target, l.Label)).OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal));
            var node = graph.AllNodes.Single(n => n.Id == "template-file:Shop.Core/Mail/Templates/Welcome.html");
            Assert.Equal((NodeKind.TemplateFile, "Welcome.html", "Shop.Core/Mail/Templates/Welcome.html", 1, 2), (node.Kind, node.Name, node.File, node.Line, node.EndLine));
            Assert.DoesNotContain(graph.AllNodes, n => n.File?.Contains("/bin/", StringComparison.Ordinal) == true && n.Kind == NodeKind.TemplateFile);
            // Beside the named ones and named by none: maybe a name made at runtime.
            var blind = Assert.Single(graph.AllBlindSpots, b => b.Detector == "template-files");
            Assert.Contains("Shop.Core/Mail/Templates/Orphan.html", blind.Reason);
            Assert.Equal("Shop.Core|F:Shop.Core.Mail.Mailer.TemplatePath", blind.Owner);

            // Walked by impact: a change to the template reaches the code that sends it.
            var report = new ImpactAnalyzer(graph).Analyze(new ChangeSpec("file", "Shop.Core/Mail/Templates/Welcome.html"), root);
            var code = report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!;
            Assert.Contains(code["items"]!.AsArray(), i => ((string?)i!["name"])?.Contains("Welcome", StringComparison.Ordinal) == true && (string?)i["kind"] == NodeKind.Method);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_lone_name_matching_a_file_elsewhere_is_no_template_and_attribute_text_names_nothing()
    {
        var root = ShopCopy.Create();
        try
        {
            Write(root, "Shop.Core/Conf.cs", """
                namespace Shop.Core;

                public class Conf
                {
                    const string P = "{0}.json";
                    public const string Env = "package";
                    [System.Obsolete("index")] public string Get() => System.String.Format(P, "index");
                }

                public class Pages
                {
                    const string P = "Pages/{0}.md";
                    public const string Help = "help";
                }
                """);
            Write(root, "package.json", "{}\n");
            Write(root, "Shop.Web/ClientApp/package.json", "{}\n");
            Write(root, "Shop.Web/ClientApp/tsconfig.json", "{}\n");
            Write(root, "index.json", "{}\n");
            Write(root, "docs/Pages/help.md", "# Help\n");
            var (graph, _) = await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(), TestContext.Current.CancellationToken);
            var lines = graph.AllEdges.Where(e => e.Kind == EdgeKind.SendsTemplate).Select(e => (e.Source, e.Target)).ToList();
            // "package" and "index" each find a file, but no folder holds two of Conf's names: no template folder, no line,
            // no blind spot. A pattern that names its folder needs no second name.
            Assert.Equal([("Shop.Core|F:Shop.Core.Pages.Help", "template-file:docs/Pages/help.md")], lines);
            Assert.DoesNotContain(graph.AllBlindSpots, b => b.Detector == "template-files");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
