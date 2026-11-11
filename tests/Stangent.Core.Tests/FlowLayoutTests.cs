using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Flows;

namespace Stangent.Core.Tests;

/// <summary>D230: flow files flat in specs/flows/ until an areas.yaml exists, then under their area; moved when areas change.</summary>
public sealed class FlowLayoutTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    string Yaml => Path.Combine(shop.Root, AreaFiles.Confirmed);

    static string Unlinked(string md) => Regex.Replace(md, @"\[(`[^`]+`)\]\([^)]*\)", "$1");

    JsonArray Index() => JsonNode.Parse(File.ReadAllText(Path.Combine(shop.Root, FlowFiles.IndexFile)))!["flows"]!.AsArray();

    [Fact]
    public void Flows_move_under_their_areas_once_an_areas_yaml_exists_and_move_again_when_an_area_is_renamed()
    {
        var set = FlowBuilder.Build(shop.Graph, shop.Root);

        // No areas.yaml: flat, as before; the index maps each id to its file; links are relative Markdown links.
        if (File.Exists(Yaml)) File.Delete(Yaml);
        var flat = AreaLayout.For(shop.Graph, shop.Root, set);
        Assert.False(flat.ByArea);
        FlowFiles.Write(shop.Root, set, flat);
        Assert.All(Index(), f => Assert.Equal($"{FlowFiles.Folder}/{(string)f!["id"]!}.json", (string)f["file"]!));
        var linked = set.Flows.First(f => f.Links.Any(l => l.ToFlow is not null));
        var to = linked.Links.First(l => l.ToFlow is not null).ToFlow!;
        Assert.Contains($"→ [`{to}`]({to}.md)", FlowFiles.Markdown(linked, set, flat), StringComparison.Ordinal);
        var flatFiles = FlowFiles.Render(set, flat);

        // An areas.yaml (the proposal, ties answered with their first choice): each flow under its area's folder.
        var areas = Areas.Of(shop.Graph, shop.Root, set);
        var (proposed, _) = AreaFiles.Propose(areas);
        var file = new AreasFile { Areas = proposed.Areas, Ties = [.. proposed.Ties.Select(t => t with { Answer = t.Choose[0] })] };
        Directory.CreateDirectory(Path.GetDirectoryName(Yaml)!);
        File.WriteAllText(Yaml, AreaFiles.Render(file, set.Commit, areas.Place(file)));
        var layout = AreaLayout.For(shop.Graph, shop.Root, set);
        Assert.True(layout.ByArea);
        var (_, removed, _) = FlowFiles.Write(shop.Root, set, layout);
        Assert.Equal(set.Flows.Count * 2, removed);   // every flat .json and .md moved
        Assert.Equal(["index.json"], Directory.EnumerateFiles(Path.Combine(shop.Root, FlowFiles.Folder)).Select(Path.GetFileName));
        foreach (var f in Index())
        {
            var path = (string)f!["file"]!;
            Assert.StartsWith(AreaLayout.Root + "/", path, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(shop.Root, path)), path);
        }
        var placed = layout.Placement!.AreaOf.First();
        Assert.Equal($"{AreaLayout.Root}/{layout.SlugOf[placed.Value]}/{placed.Key}.json", layout.File(placed.Key));
        // Contents unchanged apart from paths and links: each .json is the same bytes, each .md the same once links are unwrapped.
        var byArea = FlowFiles.Render(set, layout);
        foreach (var f in set.Flows)
        {
            Assert.Equal(flatFiles[$"{FlowFiles.Folder}/{f.Id}.json"], byArea[layout.File(f.Id)]);
            Assert.Equal(Unlinked(flatFiles[$"{FlowFiles.Folder}/{f.Id}.md"]), Unlinked(byArea[layout.Markdown(f.Id)]));
        }
        // Links point at the target's file, relative to the flow's own folder, and that file exists.
        foreach (var f in set.Flows.Where(f => f.Links.Any(l => l.ToFlow is not null)))
            foreach (Match m in Regex.Matches(byArea[layout.Markdown(f.Id)], @"→ \[`[^`]+`\]\(([^)]+)\)"))
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(shop.Root, layout.FolderOf[f.Id], m.Groups[1].Value))), m.Value);
        // Byte for byte: two renders agree, and the check passes on what was written.
        Assert.Equal(byArea, FlowFiles.Render(FlowBuilder.Build(shop.Graph, shop.Root), AreaLayout.For(shop.Graph, shop.Root, set)));
        var check = FlowFiles.Check(shop.Root, shop.Graph, new FlowSettings());
        Assert.True((bool)check["ok"]!, check.ToJsonString());

        // A rename (its name and the tie answers naming it) moves the area's files to its new folder and removes the old one.
        // An area that is no part of another and has none (D227's counting stays the same).
        var first = file.Areas.First(a => layout.Placement.AreaOf.ContainsValue(a.Name) && !a.Name.Contains(" › ", StringComparison.Ordinal)
            && !file.Areas.Any(b => b.Name.StartsWith(a.Name + " › ", StringComparison.Ordinal)));
        var oldDir = Path.Combine(shop.Root, layout.AreaFolder(first.Name));
        var moved = layout.FolderOf.Count(x => x.Value == layout.AreaFolder(first.Name));
        File.WriteAllText(Yaml, File.ReadAllText(Yaml).Replace($"'{first.Name.Replace("'", "''")}'", "'Renamed by a person'", StringComparison.Ordinal));
        var after = AreaLayout.For(shop.Graph, shop.Root, set);
        var (_, gone, _) = FlowFiles.Write(shop.Root, set, after);
        Assert.Equal(moved * 2, gone);
        Assert.False(Directory.Exists(oldDir));
        Assert.Equal(moved, Directory.EnumerateFiles(Path.Combine(shop.Root, AreaLayout.Root, "renamed-by-a-person"), "*.json").Count());
        Assert.True((bool)FlowFiles.Check(shop.Root, shop.Graph, new FlowSettings())["ok"]!);

        // areas.yaml gone again: back to flat, nothing left under specs/areas/.
        File.Delete(Yaml);
        FlowFiles.Write(shop.Root, set, AreaLayout.For(shop.Graph, shop.Root, set));
        Assert.Equal(flatFiles.Keys, FlowFiles.OnDisk(shop.Root).Append(FlowFiles.IndexFile).Distinct().Order(StringComparer.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(shop.Root, AreaLayout.Root)) && Directory.EnumerateFileSystemEntries(Path.Combine(shop.Root, AreaLayout.Root)).Any());
    }
}
