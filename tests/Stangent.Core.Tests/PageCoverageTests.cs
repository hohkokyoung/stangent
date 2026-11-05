using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>T.3: every page is written, or excluded with a reason, and an exclusion moves no other page's hash.</summary>
public class PageCoverageTests
{
    static SpecApp App() => new("version: 1\ncomponents: { page-exclusions: true }\n");

    [Fact]
    public void Coverage_says_what_is_neither_written_nor_excluded()
    {
        using var app = new SpecApp();
        var set = app.Pages();
        var report = PageExclusions.Coverage(set, new SpecPaths(app.Root));
        Assert.Equal(set.Pages.Count, (int)report["counts"]!["uncovered"]!);
        Assert.Equal(set.Pages.Count, report["notWritten"]!.AsArray().Count);
    }

    [Fact]
    public void Off_excluding_refuses()
    {
        using var app = new SpecApp();
        Assert.Equal("component_off", Assert.Throws<StangentException>(() => PageExclusions.Exclude(app.Pages(), app.Repo, SpecApp.Post, "x", "me")).Code);
    }

    [Fact]
    public void An_excluded_page_is_gone_from_the_list_and_counted_with_its_reason()
    {
        using var app = App();
        var before = app.Pages();
        var other = before.Pages.First(p => p.Id != SpecApp.Post);
        var hash = Bundles.Build(before, other, null).InputHash;
        PageExclusions.Exclude(before, app.Repo, SpecApp.Post, "a tooltip", "ana");

        var set = app.Pages();
        Assert.Null(set.Find(SpecApp.Post));
        Assert.Equal(SpecApp.Post, Assert.Single(set.Excluded).Id);
        Assert.Equal(hash, Bundles.Build(set, set.Find(other.Id)!, null).InputHash);
        Assert.DoesNotContain(set.Pages, p => p.Id == SpecApp.Post);
        var report = PageExclusions.Coverage(set, new SpecPaths(app.Root));
        Assert.Equal(before.Pages.Count, (int)report["counts"]!["pages"]!);
        Assert.Equal("a tooltip", (string)report["excluded"]![0]!["why"]!);
        Assert.Equal(before.Pages.Count - 1, (int)report["counts"]!["uncovered"]!);

        PageExclusions.Include(app.Repo, SpecApp.Post);
        Assert.NotNull(app.Pages().Find(SpecApp.Post));
    }

    [Fact]
    public void An_exclusion_without_a_reason_is_refused()
    {
        using var app = App();
        Assert.Equal("usage", Assert.Throws<StangentException>(() => PageExclusions.Exclude(app.Pages(), app.Repo, SpecApp.Post, "", "ana")).Code);
    }

    [Fact]
    public void An_exclusion_the_graph_no_longer_calls_for_is_listed_as_gone()
    {
        using var app = App();
        PageExclusions.Exclude(app.Pages(), app.Repo, SpecApp.Post, "tip", "ana");
        var file = PageExclusions.FileIn(app.Root);
        File.WriteAllText(file, File.ReadAllText(file).Replace(SpecApp.Post, "page:/gone"));
        var report = PageExclusions.Coverage(app.Pages(), new SpecPaths(app.Root));
        Assert.Equal("page:/gone", (string)report["gone"]![0]!);
        Assert.Empty(report["excluded"]!.AsArray());
    }

    [Fact]
    public void With_no_file_the_set_is_what_it_was_even_when_on()
    {
        using var on = App();
        using var off = new SpecApp();
        Assert.Equal(off.Pages().Pages.Select(p => p.Id), on.Pages().Pages.Select(p => p.Id));
    }
}
