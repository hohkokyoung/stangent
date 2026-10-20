using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Stangent.Benchmark.Split;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.History;

namespace Stangent.Benchmark.Tests;

/// <summary>
/// A temporary git repo with one C# project and two commits. The change has two independent parts, one with
/// a hidden link (a caller two steps away), a <c>.csproj</c> edit, a deleted method, and a hunk spanning two
/// members. Graphs are built once at the change and its parent, as the split check builds them.
/// </summary>
public sealed class SplitFixture : IAsyncLifetime
{
    public string Root { get; } = Directory.CreateTempSubdirectory("stangent-split-").FullName;
    public string Parent { get; private set; } = "";
    public string Change { get; private set; } = "";
    public CodeGraph AfterGraph { get; private set; } = null!;
    public CodeGraph ParentGraph { get; private set; } = null!;

    const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>

        """;

    public static readonly (string Path, string Text)[] Base =
    [
        (".gitignore", ".stangent/\nbin/\nobj/\n"),
        ("Directory.Build.props", "<Project>\n</Project>\n"),
        ("stangent.yaml", "version: 1\n"),
        ("Fixture.slnx", "<Solution>\n  <Project Path=\"Lib/Lib.csproj\" />\n</Solution>\n"),
        ("Lib/Lib.csproj", Project),
        ("Lib/Orders/OrderMath.cs", """
            namespace Lib.Orders;

            public class OrderMath
            {
                public int Total(int a, int b) => a + b;
            }

            """),
        ("Lib/Orders/OrderService.cs", """
            namespace Lib.Orders;

            public class OrderService
            {
                readonly OrderMath math = new();

                public int Place(int a, int b)
                {
                    return math.Total(a, b);
                }

                public int Old()
                {
                    return 1;
                }

                public int UsesOld()
                {
                    return Old() + 1;
                }
            }

            """),
        ("Lib/Billing/Invoice.cs", """
            namespace Lib.Billing;

            public class Invoice
            {
                public string Render(int total)
                {
                    return Format.Money(total);
                }
            }

            """),
        ("Lib/Billing/Format.cs", """
            namespace Lib.Billing;

            public static class Format
            {
                public static string Money(int value)
                {
                    return Helper.Pad(value);
                }
            }

            """),
        ("Lib/Billing/Helper.cs", """
            namespace Lib.Billing;

            public static class Helper
            {
                public static string Pad(int value)
                {
                    return value.ToString();
                }
            }

            """),
    ];

    public static readonly (string Path, string Text)[] Changed =
    [
        // The .csproj edit: no members, so the shared group.
        ("Lib/Lib.csproj", Project.Replace("<ImplicitUsings>enable</ImplicitUsings>", "<ImplicitUsings>enable</ImplicitUsings>\n    <Description>fixture</Description>")),
        // One hunk spanning two members: Total changes and Tax is added right after it.
        ("Lib/Orders/OrderMath.cs", """
            namespace Lib.Orders;

            public class OrderMath
            {
                public int Total(int a, int b) => a + b + 0;
                public int Tax(int total) => total / 10;
            }

            """),
        // Part 1: Place changes with Total. Old is deleted, and UsesOld stops calling it.
        ("Lib/Orders/OrderService.cs", """
            namespace Lib.Orders;

            public class OrderService
            {
                readonly OrderMath math = new();

                public int Place(int a, int b)
                {
                    return math.Total(a, b) * 1;
                }

                public int UsesOld()
                {
                    return 2;
                }
            }

            """),
        // Part 2: Render uses the new Tax, and Pad is two steps away from Render (through Format.Money).
        ("Lib/Billing/Invoice.cs", """
            using Lib.Orders;

            namespace Lib.Billing;

            public class Invoice
            {
                public string Render(int total)
                {
                    return Format.Money(total + new OrderMath().Tax(total));
                }
            }

            """),
        ("Lib/Billing/Helper.cs", """
            namespace Lib.Billing;

            public static class Helper
            {
                public static string Pad(int value)
                {
                    return value.ToString().PadLeft(8);
                }
            }

            """),
    ];

    public async ValueTask InitializeAsync()
    {
        Write(Base);
        Git("init", "-q");
        Commit("base");
        Parent = Head();
        Write(Changed);
        Commit("change: orders and billing");
        Change = Head();
        var repo = StangentRepo.Discover(Root);
        Git("checkout", "-q", Parent);
        (ParentGraph, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(), TestContext.Current.CancellationToken);
        Git("checkout", "-q", Change);
        (AfterGraph, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(), TestContext.Current.CancellationToken);
    }

    void Write((string Path, string Text)[] files)
    {
        foreach (var (path, text) in files)
        {
            var full = System.IO.Path.Combine(Root, path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
    }

    void Commit(string message)
    {
        Git("add", "-A");
        Git("-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "-q", "-m", message);
    }

    public string Head() => Git("rev-parse", "HEAD").Trim();

    public string Git(params string[] args) => Encoding.UTF8.GetString(SplitDiff.Git(Root, args));

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}

public class SplitTests(SplitFixture fx) : IClassFixture<SplitFixture>
{
    const string Place = "M:Lib.Orders.OrderService.Place(System.Int32,System.Int32)";
    const string UsesOld = "M:Lib.Orders.OrderService.UsesOld";
    const string Old = "M:Lib.Orders.OrderService.Old";
    const string Total = "M:Lib.Orders.OrderMath.Total(System.Int32,System.Int32)";
    const string Tax = "M:Lib.Orders.OrderMath.Tax(System.Int32)";
    const string Render = "M:Lib.Billing.Invoice.Render(System.Int32)";
    const string Pad = "M:Lib.Billing.Helper.Pad(System.Int32)";

    Splitter NewSplitter() => new(SplitDiff.Read(fx.Root, fx.Parent, fx.Change), new GraphView(fx.AfterGraph), new GraphView(fx.ParentGraph));

    static List<string[]> Members(RuleSplit r) => [.. r.Groups.Select(g => g.Members.ToArray())];

    [Fact]
    public void Changed_members_come_from_both_sides_of_the_diff()
    {
        var s = NewSplitter();
        Assert.Equal([Pad, Render, Tax, Total, Old, Place, UsesOld], s.Members);
        Assert.Equal(["Lib/Billing/Helper.cs", "Lib/Billing/Invoice.cs", "Lib/Orders/OrderMath.cs", "Lib/Orders/OrderService.cs"], s.MemberFiles);
    }

    [Fact]
    public void Direct_lines_keep_the_hidden_link_apart_and_within_two_steps_join_it()
    {
        var s = NewSplitter();

        var direct = s.Group(Splitter.Direct, 1);
        Assert.Equal([[Pad], [Render, Tax], [Total, Place], [Old, UsesOld]], Members(direct));

        var within2 = s.Group(Splitter.Within2, 2);
        // Pad joins Render through the unchanged Format.Money; the deleted Old joins UsesOld, its caller in the parent's graph.
        Assert.Equal([[Pad, Render, Tax], [Total, Place], [Old, UsesOld]], Members(within2));
        Assert.Equal(0, within2.JoinedBySharedHunk);
    }

    [Fact]
    public void History_lines_join_the_files_they_link()
    {
        var s = NewSplitter();
        var joined = s.Group(Splitter.Within2History, 2, [("Lib/Billing/Invoice.cs", "Lib/Orders/OrderMath.cs")]);
        Assert.Equal([[Pad, Render, Tax, Total, Place], [Old, UsesOld]], Members(joined));
    }

    [Fact]
    public void Split_by_folder_groups_whole_files()
    {
        var folder = NewSplitter().ByFolder();
        Assert.Equal([["Lib/Billing/Helper.cs", "Lib/Billing/Invoice.cs"], ["Lib/Orders/OrderMath.cs", "Lib/Orders/OrderService.cs"]],
            folder.Groups.Select(g => g.Files.ToArray()));
    }

    [Fact]
    public void Files_without_changed_members_form_the_shared_group_applied_with_every_group()
    {
        var s = NewSplitter();
        Assert.Equal(["Lib/Lib.csproj"], s.SharedFiles);
        Assert.All(s.Group(Splitter.Within2, 2).Groups, g => Assert.Contains("Lib/Lib.csproj", g.WholeFiles));
    }

    [Fact]
    public void A_hunk_spanning_two_members_is_split_at_the_member_boundary()
    {
        var s = NewSplitter();
        var math = s.Pieces.Where(p => p.File == "Lib/Orders/OrderMath.cs").ToList();
        Assert.Equal(2, math.Count);
        Assert.Equal([Total], math[0].Members);
        Assert.Equal([Tax], math[1].Members);
        Assert.True(s.HunksSplit >= 1);

        // The orders group gets the new Total but not Tax, which went with billing.
        var files = SplitDiff.Read(fx.Root, fx.Parent, fx.Change);
        var orders = s.Group(Splitter.Within2, 2).Groups.Single(g => g.Members.Contains(Place));
        var text = Encoding.UTF8.GetString(GroupApplier.Contents(files, s.Pieces, orders, f => [.. f.NewLines.SelectMany(l => l)])["Lib/Orders/OrderMath.cs"]!);
        Assert.Contains("a + b + 0", text);
        Assert.DoesNotContain("Tax", text);
    }

    [Fact]
    public void Edits_outside_members_go_with_every_group_in_their_file()
    {
        var s = NewSplitter();
        var files = SplitDiff.Read(fx.Root, fx.Parent, fx.Change);
        var groups = s.Group(Splitter.Within2, 2).Groups;
        // Invoice.cs's new `using Lib.Orders;` is outside every member: it goes with Render's group.
        var billing = groups.Single(g => g.Members.Contains(Render));
        var invoice = Encoding.UTF8.GetString(GroupApplier.Contents(files, s.Pieces, billing, f => [.. f.NewLines.SelectMany(l => l)])["Lib/Billing/Invoice.cs"]!);
        Assert.StartsWith("using Lib.Orders;", invoice);
        // OrderService.cs has two groups; the blank line removed with Old is outside members and goes with both.
        var outside = s.Pieces.Single(p => p.File == "Lib/Orders/OrderService.cs" && p.Members.Count == 0);
        Assert.All(groups.Where(g => g.Files.Contains("Lib/Orders/OrderService.cs")), g => Assert.Contains(outside.Id, g.Pieces));
    }

    [Fact]
    public void Applying_a_group_alone_then_restoring_leaves_a_clean_tree()
    {
        fx.Git("checkout", "-q", fx.Parent);
        try
        {
            var s = NewSplitter();
            var files = SplitDiff.Read(fx.Root, fx.Parent, fx.Change);
            var billing = s.Group(Splitter.Within2, 2).Groups.Single(g => g.Members.Contains(Render));
            var applier = new GroupApplier(fx.Root, fx.Parent, fx.Change);
            var created = applier.Apply(files, s.Pieces, billing);

            Assert.Contains("PadLeft", File.ReadAllText(Path.Combine(fx.Root, "Lib/Billing/Helper.cs")));
            Assert.Contains("Tax", File.ReadAllText(Path.Combine(fx.Root, "Lib/Orders/OrderMath.cs")));
            Assert.Contains("Old()", File.ReadAllText(Path.Combine(fx.Root, "Lib/Orders/OrderService.cs"))); // not this group's
            Assert.Contains("fixture", File.ReadAllText(Path.Combine(fx.Root, "Lib/Lib.csproj")));         // the shared group
            Assert.True(File.Exists(Path.Combine(fx.Root, GroupApplier.StateFile)));

            applier.Restore(created);
            Assert.Equal("", fx.Git("status", "--porcelain"));
            Assert.Equal(fx.Parent, fx.Head());
            Assert.False(File.Exists(Path.Combine(fx.Root, GroupApplier.StateFile)));
        }
        finally
        {
            fx.Git("checkout", "-q", "-f", fx.Change);
        }
    }

    [Fact]
    public void A_group_that_misses_a_link_fails_to_build_alone()
    {
        fx.Git("checkout", "-q", fx.Parent);
        try
        {
            var s = NewSplitter();
            var files = SplitDiff.Read(fx.Root, fx.Parent, fx.Change);
            // Render's piece without Tax's: what a split missing the Render → Tax line would apply.
            var renderOnly = new SplitGroup([Render], ["Lib/Billing/Invoice.cs"],
                s.Pieces.Where(p => p.File == "Lib/Billing/Invoice.cs").Select(p => p.Id).ToHashSet(), s.SharedFiles);
            var applier = new GroupApplier(fx.Root, fx.Parent, fx.Change);
            var created = applier.Apply(files, s.Pieces, renderOnly);
            try
            {
                var checker = new DotnetChecker(fx.Root, new SplitConfig { Solution = "Fixture.slnx" }, Path.Combine(fx.Root, ".stangent", "split"), TextWriter.Null);
                var build = checker.Build(dotnet: true, frontend: false);
                Assert.False(build.Ok);
                Assert.Contains(build.Errors, e => e.Contains("CS1061") && e.Contains("Tax"));
            }
            finally
            {
                applier.Restore(created);
            }
        }
        finally
        {
            fx.Git("checkout", "-q", "-f", fx.Change);
        }
    }

    [Fact]
    public void Test_results_are_read_from_trx_with_skipped_tests_left_out()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="a" testName="adds(1)" outcome="Passed" />
                <UnitTestResult testId="a" testName="adds(2)" outcome="Failed" />
                <UnitTestResult testId="b" testName="skipped" outcome="NotExecuted" />
              </Results>
              <TestDefinitions>
                <UnitTest id="a"><TestMethod className="Lib.Tests.MathTests" name="adds" /></UnitTest>
                <UnitTest id="b"><TestMethod className="Lib.Tests.MathTests" name="skipped" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
        Assert.Equal([("Lib.Tests.MathTests.adds(1)", true), ("Lib.Tests.MathTests.adds(2)", false)], DotnetChecker.ReadTrx(trx).ToArray());
    }

    [Fact]
    public async Task The_whole_check_runs_twice_with_identical_results_and_puts_the_tree_back()
    {
        var first = await RunCheck();
        var second = await RunCheck();
        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Equal(fx.Change, fx.Head());
        Assert.Equal("", fx.Git("status", "--porcelain"));

        Assert.Equal(1, (int)first["verdict"]!["useful"]!["examined"]!);
        Assert.Equal(1, (int)first["skipped"]!["no parent"]!);
        var rules = first["cases"]![0]!["rules"]!;
        Assert.Equal(3, rules[Splitter.Within2]!["groups"]!.AsArray().Count);
        Assert.Equal(4, rules[Splitter.Direct]!["groups"]!.AsArray().Count);
        Assert.True((bool)rules[Splitter.Within2]!["holdsUp"]!);
        // The fake checker saw each distinct group applied alone, every time on top of the parent.
        Assert.All(rules[Splitter.Direct]!["groups"]!.AsArray(), g => Assert.True((bool)g!["compiles"]!));
    }

    [Fact]
    public async Task A_run_that_stopped_resumes_from_its_progress_file_with_the_same_results()
    {
        var progress = Path.Combine(fx.Root, ".stangent", "test-progress.jsonl");
        File.Delete(progress);
        var full = await RunCheck(progress);
        // As if the run had crashed after the first commit it walked: keep the header and that commit only.
        File.WriteAllLines(progress, File.ReadAllLines(progress).Take(2));
        var builds = 0;
        var resumed = await RunCheck(progress, () => builds++);
        Assert.Equal(full.ToJsonString(), resumed.ToJsonString());
        Assert.Equal(3, File.ReadAllLines(progress).Length);
        Assert.Equal(0, builds); // the change was already done; the root commit needs no graph
    }

    async Task<JsonObject> RunCheck(string? progress = null, Action? onGraph = null)
    {
        var repo = StangentRepo.Discover(fx.Root);
        var history = new HistoryIndex(HistoryLog.Read(fx.Root, true, [".cs"]) ?? [],
            fx.AfterGraph.AllNodes.Where(n => n.Kind == NodeKind.File && n.File is not null).Select(n => n.File!).ToHashSet(), [], []);
        var check = new SplitCheck(fx.Root, new SplitConfig(), new FakeChecker(fx),
            async () => { onGraph?.Invoke(); return (await GraphBuilder.BuildAsync(repo, new BuildOptions(), TestContext.Current.CancellationToken)).Graph; },
            history, repo.Config.History, () => { }, TextWriter.Null) { ProgressFile = progress };
        return await check.RunAsync(fx.Change);
    }

    /// <summary>Builds nothing; checks each group was applied onto the parent and never leaks into the next.</summary>
    sealed class FakeChecker(SplitFixture fx) : ISplitChecker
    {
        public BuildResult Build(bool dotnet, bool frontend)
        {
            var dirty = fx.Git("status", "--porcelain").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // Only a group's own edits are ever in the tree, on top of the parent.
            if (dirty.Length > 0) Assert.Equal(fx.Parent, fx.Head());
            return new BuildResult(true, []);
        }

        public Dictionary<string, bool> Test(IReadOnlyList<string> testProjects) => [];
    }
}
