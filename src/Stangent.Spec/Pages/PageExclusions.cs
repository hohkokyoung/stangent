using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Pages;

/// <summary>T.3 (D131): every page the graph calls for is written, or a person excluded it with a reason. Code only. No file means nothing is excluded.</summary>
public static class PageExclusions
{
    public const string Component = "page-exclusions", Contract = "page-exclusions.v1.json";

    public static string FileIn(string root) => Path.Combine(root, "specs", "page-exclusions.json");

    static JsonObject Load(string root) => PageRun.LoadJson(FileIn(root)) as JsonObject ?? new JsonObject { ["schemaVersion"] = 1, ["excluded"] = new JsonArray() };

    static List<JsonObject> All(JsonObject file) => [.. file["excluded"]!.AsArray().OfType<JsonObject>()];

    public static HashSet<string> Ids(string root) =>
        File.Exists(FileIn(root)) ? All(Load(root)).Select(e => (string)e["page"]!).ToHashSet(StringComparer.Ordinal) : [];

    static void Save(string root, JsonObject file)
    {
        var errors = SchemaRegistry.Embedded.Validate(Contract, file.ToJsonString());
        if (errors.Count > 0) throw new StangentException("usage", "An exclusion needs a page, --why and --by.", ExitCodes.Usage, errors);
        file["excluded"] = new JsonArray([.. All(file).OrderBy(e => (string)e["page"]!, StringComparer.Ordinal).Select(e => e.DeepClone())]);
        Quotes.WriteAtomic(FileIn(root), CanonicalJson.Serialize(file));
    }

    public static JsonObject Exclude(PageSet set, StangentRepo repo, string pageKey, string why, string by)
    {
        if (!StangentComponents.IsOn(repo.Config, Component))
            throw new StangentException("component_off", $"{Component} is off. Turn on `components.{Component}`: excluding a page can change what a link judge reads.", ExitCodes.Usage);
        var page = set.Find(pageKey) ?? throw new StangentException("not_found", $"No page '{pageKey}'. `stangent spec pages` lists them.", ExitCodes.NotFound);
        using var _ = PageRun.Lock(new SpecPaths(repo.Root));
        var file = Load(repo.Root);
        var entry = new JsonObject { ["page"] = page.Id, ["why"] = why, ["by"] = by };
        file["excluded"]!.AsArray().Add(entry);
        Save(repo.Root, file);
        return (JsonObject)entry.DeepClone();
    }

    public static JsonObject Include(StangentRepo repo, string page)
    {
        using var _ = PageRun.Lock(new SpecPaths(repo.Root));
        var file = Load(repo.Root);
        var list = All(file);
        var found = list.FirstOrDefault(e => (string)e["page"]! == page) ?? throw new StangentException("not_found", $"'{page}' isn't excluded.", ExitCodes.NotFound);
        file["excluded"]!.AsArray().Remove(found);
        Save(repo.Root, file);
        return new JsonObject { ["page"] = page, ["excluded"] = false };
    }

    public static JsonObject Coverage(PageSet set, SpecPaths paths)
    {
        var failed = PageRun.Failed(paths).OfType<JsonObject>().ToDictionary(f => (string)f["id"]!, f => (string?)f["reason"], StringComparer.Ordinal);
        var written = new JsonArray();
        var missing = new JsonArray();
        var failing = new JsonArray();
        foreach (var page in set.Pages)
        {
            if (failed.TryGetValue(page.Id, out var reason)) failing.Add(new JsonObject { ["id"] = page.Id, ["kind"] = page.Kind, ["reason"] = reason });
            else if (File.Exists(paths.PageJson(page))) written.Add(new JsonObject { ["id"] = page.Id, ["kind"] = page.Kind });
            else missing.Add(new JsonObject { ["id"] = page.Id, ["kind"] = page.Kind, ["title"] = page.Title });
        }
        var on = set.Excluded.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var entries = All(Load(paths.Root));
        JsonObject Kinds(string kind) => new()
        {
            ["written"] = written.Count(p => (string)p!["kind"]! == kind), ["notWritten"] = missing.Count(p => (string)p!["kind"]! == kind),
            ["failed"] = failing.Count(p => (string)p!["kind"]! == kind), ["excluded"] = set.Excluded.Count(p => p.Kind == kind),
        };
        return new JsonObject
        {
            ["counts"] = new JsonObject
            {
                ["pages"] = set.Pages.Count + set.Excluded.Count, ["written"] = written.Count, ["excluded"] = set.Excluded.Count, ["uncovered"] = missing.Count + failing.Count,
                ["screen"] = Kinds(PageKind.Screen), ["component"] = Kinds(PageKind.Component), ["backend"] = Kinds(PageKind.Backend),
            },
            ["notWritten"] = missing, ["failed"] = failing,
            ["excluded"] = new JsonArray([.. entries.Where(e => on.ContainsKey((string)e["page"]!)).Select(e => e.DeepClone())]),
            ["gone"] = new JsonArray([.. entries.Where(e => set.Settings.Exclusions && !on.ContainsKey((string)e["page"]!)).Select(e => (JsonNode)(string)e["page"]!)]),
        };
    }
}
