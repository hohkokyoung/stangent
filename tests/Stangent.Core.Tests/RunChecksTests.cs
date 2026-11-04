using System.Text.Json.Nodes;
using Stangent.Spec.Checks;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>T.5: before and after, by git.</summary>
public sealed class AppFilesTests : IDisposable
{
    readonly DriverRig rig = new();
    readonly string repo;

    public AppFilesTests() => repo = rig.NewRepo("app", ("src/a.cs", "one\n"), ("src/b.cs", "two\n"));
    public void Dispose() => rig.Dispose();

    void Write(string path, string text)
    {
        var full = Path.Combine(repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    JsonObject Compare(Action run)
    {
        var before = AppFiles.Snapshot(repo);
        run();
        return AppFiles.Compare(repo, before);
    }

    [Fact]
    public void Nothing_changed_passes()
    {
        var report = Compare(() => { });
        Assert.True((bool)report["ok"]!);
        Assert.True((bool)report["checked"]!);
    }

    [Theory]
    [InlineData("src/a.cs")]      // a tracked file
    [InlineData("src/new.cs")]    // a new one
    public void A_changed_or_new_app_file_is_named(string path)
    {
        var report = Compare(() => Write(path, "changed\n"));
        Assert.False((bool)report["ok"]!);
        Assert.Equal(path, (string)report["changed"]![0]!);
    }

    [Fact]
    public void A_deleted_file_is_named()
    {
        var report = Compare(() => File.Delete(Path.Combine(repo, "src/a.cs")));
        Assert.Equal("src/a.cs", (string)Assert.Single(report["changed"]!.AsArray())!);
    }

    [Fact]
    public void A_further_edit_to_a_file_already_dirty_is_seen_and_leaving_it_alone_is_not()
    {
        Write("src/a.cs", "dirty before the run\n");
        Assert.True((bool)Compare(() => { })["ok"]!);
        Assert.False((bool)Compare(() => Write("src/a.cs", "dirtier\n"))["ok"]!);
    }

    [Fact]
    public void A_new_commit_is_seen()
    {
        var report = Compare(() => { Write("src/a.cs", "x\n"); rig.Commit(repo); });
        Assert.False((bool)report["ok"]!);
        Assert.NotNull(report["commitMoved"]);
    }

    [Fact]
    public void Stangents_own_folders_are_left_out()
    {
        var report = Compare(() =>
        {
            Write(".stangent/x.json", "{}");
            Write("specs/reference/p.md", "page");
            Write("stangent.yaml", "version: 1\n");
        });
        Assert.True((bool)report["ok"]!);
    }

    [Fact]
    public void Outside_git_it_says_it_did_not_check()
    {
        var plain = Directory.CreateDirectory(Path.Combine(rig.Dir, "plain")).FullName;
        var report = AppFiles.Compare(plain, AppFiles.Snapshot(plain));
        Assert.True((bool)report["ok"]!);
        Assert.False((bool)report["checked"]!);
        Assert.Equal("not a git repo", (string)report["why"]!);
    }
}

/// <summary>T.6: broken links and duplicate ids, on hand-written files.</summary>
public sealed class ContentChecksTests : IDisposable
{
    readonly DirectoryInfo root = Directory.CreateTempSubdirectory("stangent-content");
    public void Dispose() => root.Delete(true);

    string Reference => Path.Combine(root.FullName, "specs", "reference");

    void Page(string file, string id, params string[] statements)
    {
        Directory.CreateDirectory(Reference);
        File.WriteAllText(Path.Combine(Reference, file + ".json"), new JsonObject
        {
            ["id"] = id,
            ["schemaVersion"] = 1,
            ["sections"] = new JsonObject { ["rules"] = new JsonArray([.. statements.Select(s => (JsonNode)new JsonObject { ["id"] = s, ["text"] = "t", ["cites"] = new JsonArray() })]) },
        }.ToJsonString());
    }

    string[] Kinds() => [.. ContentChecks.Run(root.FullName)["problems"]!.AsArray().Select(p => (string)p!["kind"]!)];

    [Fact]
    public void A_clean_set_passes_and_an_empty_repo_does_too()
    {
        Assert.Empty(Kinds());
        Page("a", "page:/a", "R1", "R2");
        File.WriteAllText(Path.Combine(Reference, "a.md"), "[next](a.json) [out](https://example.com/x) ![img](none.png) [here](#rules)");
        Assert.Empty(Kinds());
    }

    [Fact]
    public void A_link_to_a_file_that_isnt_there_is_broken()
    {
        Page("a", "page:/a");
        File.WriteAllText(Path.Combine(Reference, "a.md"), "see [b](missing/b.md#rules)");
        Assert.Equal(["broken-link"], Kinds());
    }

    [Fact]
    public void A_link_to_a_page_the_graph_calls_for_but_nobody_wrote_yet_is_counted_apart_not_broken()
    {
        Page("a", "page:/a");
        File.WriteAllText(Path.Combine(Reference, "a.md"), "see [b](later/b.md)");
        var report = ContentChecks.Run(root.FullName, new HashSet<string> { "later/b" });
        Assert.True((bool)report["ok"]!);
        Assert.Equal(1, (int)report["counts"]!["linksToPagesNotWritten"]!);
    }

    [Fact]
    public void The_same_page_id_in_two_files_and_a_repeated_statement_are_duplicates()
    {
        Page("a", "page:/a", "R1", "R1");
        Page("b", "page:/a");
        Assert.Equal(["duplicate-statement-id", "duplicate-page-id"], Kinds());
    }

    [Fact]
    public void Links_to_a_page_statement_or_section_that_isnt_there_are_broken_and_a_repeated_link_id_is_a_duplicate()
    {
        Page("a", "page:/a", "R1");
        var entry = (string id, string page, string statement) => new JsonObject
        {
            ["id"] = id, ["state"] = "s", ["page"] = new JsonObject { ["id"] = page, ["statement"] = statement },
        };
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs"));
        var trace = TraceFile.Empty();
        var entries = trace["entries"]!.AsArray();
        entries.Add(entry("L-1", "page:/a", "R1"));
        entries.Add(entry("L-2", "page:/gone", "R1"));
        entries.Add(entry("L-3", "page:/a", "R9"));
        entries.Add(entry("L-1", "page:/a", "R1"));
        var withFsd = entry("L-4", "page:/a", "R1");
        withFsd["fsd"] = new JsonObject { ["doc"] = "d", ["section"] = "9.9" };
        entries.Add(withFsd);
        TraceFile.Save(root.FullName, trace);

        var problems = ContentChecks.Run(root.FullName)["problems"]!.AsArray().Select(p => (string)p!["detail"]!).ToList();
        Assert.Contains(problems, p => p.Contains("`L-1` appears 2 times"));
        Assert.Contains(problems, p => p.Contains("`page:/gone`"));
        Assert.Contains(problems, p => p.Contains("R9 of `page:/a`"));
        Assert.Contains(problems, p => p.Contains("section `9.9` of `d`"));
    }
}

/// <summary>T.8: the hook itself, fed what Claude Code feeds it.</summary>
public sealed class ReadOnlyHookTests : IDisposable
{
    readonly DriverRig rig = new();
    string Cwd => rig.RealPath(rig.Work);

    public void Dispose() => rig.Dispose();

    DriverResult Hook(string tool, string? path = null, string? agent = null, bool driver = false)
    {
        var input = new JsonObject { ["tool_name"] = tool, ["cwd"] = Cwd, ["tool_input"] = new JsonObject { ["file_path"] = path, ["command"] = "dotnet build" }, ["agent_type"] = agent };
        var file = rig.Put("hook-input.json", input.ToJsonString());
        var script = rig.WriteScript("hook.sh", $"{(driver ? "export STANGENT_READ_ONLY=1;" : "")} '{Path.Combine(DriverRig.RepoRoot, "plugin/scripts/read-only-hook.sh")}' < '{file}'");
        return rig.Run(script);
    }

    [Theory]
    [InlineData("Write", "src/App.cs")]
    [InlineData("Edit", "/etc/hosts")]
    [InlineData("MultiEdit", ".stangent/../src/App.cs")]
    [InlineData("Bash", null)]
    public void A_driver_analysis_step_cant_touch_the_app(string tool, string? path)
    {
        if (OperatingSystem.IsWindows()) return;
        var result = Hook(tool, path, driver: true);
        Assert.Equal(2, result.Exit);
        Assert.Contains("read-only", result.Err);
    }

    [Theory]
    [InlineData(".stangent/spec/out.json")]
    [InlineData(".stangent/new/deep/out.json")]
    public void It_can_write_stangents_own_folder(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(0, Hook("Write", path, driver: true).Exit);
        Assert.Equal(0, Hook("Write", Path.Combine(Cwd, path), driver: true).Exit);
    }

    [Fact]
    public void A_link_inside_stangents_folder_doesnt_lead_out()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(rig.Work, ".stangent"));
        Directory.CreateDirectory(Path.Combine(rig.Work, "src"));
        File.CreateSymbolicLink(Path.Combine(rig.Work, ".stangent", "out"), Path.Combine(rig.Work, "src"));
        Assert.Equal(2, Hook("Write", ".stangent/out/App.cs", driver: true).Exit);
    }

    [Theory]
    [InlineData("stangent:stangent-page-writer", 2)]
    [InlineData("stangent-link-judge", 2)]
    [InlineData("stangent:stangent-bug-reproducer", 0)]   // D75: bug proof still builds and runs the app
    [InlineData("some-other-agent", 0)]
    [InlineData(null, 0)]                                  // a person's own session
    public void From_the_plugin_only_stangents_analysis_agents_are_held(string? agent, int exit)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(exit, Hook("Write", "src/App.cs", agent).Exit);
        Assert.Equal(exit, Hook("Bash", null, agent).Exit);
    }

    [Fact]
    public void Reading_is_never_stopped()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(0, Hook("Read", "src/App.cs", driver: true).Exit);
    }
}
