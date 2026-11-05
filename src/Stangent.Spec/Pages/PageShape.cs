using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Pages;

/// <summary>What a page was written from, with where the code sits left out: line numbers, and which file it's in.</summary>
public sealed record PageShape(string Hash, IReadOnlyDictionary<string, string> Symbols);

/// <summary>
/// I.4 (D131). A page's input hash covers line numbers and paths, so code that only moved reads as changed and asks for a paid run. This compares the rest:
/// the same instructions and schema, the same points and lines, the same code. Same shape: the page's statements are still as good as they were, so code moves
/// its citations and its hash on, with no AI. Anything else stays stale. A file renamed where the symbol's id holds its path (TypeScript) has new ids, so it stays stale too.
/// Off by default (<c>follow-moves</c>), because it rewrites a saved hash. The file beside the page is written only with this or <c>statement-update</c> on.
/// </summary>
public static class PageMoves
{
    public const string Component = "follow-moves";

    public static string FileFor(string pageJson) => pageJson[..^".json".Length] + ".shape.json";

    public static PageShape Shape(PageSet set, PageDef page)
    {
        var slice = set.Slices[page.Id];
        var symbols = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in slice.Nodes.Where(n => n.Node.File is not null && n.Node.Line > 0))
            if (set.Source.Lines(n.Node.File!) is { } lines && lines.Length > 0)
                symbols[n.Node.Id] = CanonicalJson.Hash(string.Join('\n', Shown(n, lines)));
        var sb = new StringBuilder(SharedHead.Text(true, [SharedHead.WriterInstructions], Bundles.WriterSchema));
        sb.Append($"{page.Id}\n{page.Kind}\n{page.Entry}\n{page.Title}\n{set.Settings.Describe()}\n{slice.Depth}\n");
        var listed = slice.Nodes.Where(Bundles.Listed).ToList();
        foreach (var n in listed.OrderBy(n => n.Node.Id, StringComparer.Ordinal).ThenBy(n => n.Role, StringComparer.Ordinal))
            sb.Append($"{n.Node.Id}|{n.Node.Kind}|{n.Label}|{n.Depth}|{n.Role}|{symbols.GetValueOrDefault(n.Node.Id)}\n");
        foreach (var e in slice.Edges.OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal).ThenBy(e => e.Kind, StringComparer.Ordinal))
            sb.Append($"{e.Source}|{e.Kind}|{e.Target}|{e.Label}\n");
        foreach (var u in slice.Unknowns.OrderBy(u => u.Owner, StringComparer.Ordinal).ThenBy(u => u.Reason, StringComparer.Ordinal))
            sb.Append($"{u.Owner}|{u.Reason}\n");
        sb.Append(slice.CutOff);
        return new PageShape(CanonicalJson.Hash(sb.ToString()), symbols);
    }

    /// <summary>The lines the bundle shows for a point: its body, or its first line for what isn't shown with a body.</summary>
    static IEnumerable<string> Shown(SliceNode n, string[] lines)
    {
        var from = Math.Min(n.Node.Line, lines.Length);
        var to = Bundles.ShowsBody(n) ? Math.Min(Math.Max(n.Node.Line, n.Node.EndLine), lines.Length) : from;
        return lines[(from - 1)..to];
    }

    public static void WriteFile(SpecPaths paths, PageDef page, PageShape shape)
    {
        var file = new JsonObject
        {
            ["version"] = 1, ["page"] = page.Id, ["shape"] = shape.Hash,
            ["symbols"] = new JsonObject([.. shape.Symbols.Select(s => KeyValuePair.Create(s.Key, (JsonNode?)s.Value))]),
        };
        var errors = SchemaRegistry.Embedded.Validate("page-shape.v1.json", file.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_shape", "A page's shape broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(FileFor(paths.PageJson(page)), CanonicalJson.Serialize(file));
    }

    public static JsonObject? LoadFile(SpecPaths paths, PageDef page) => PageRun.LoadJson(FileFor(paths.PageJson(page))) as JsonObject;

    /// <summary>The page's inputs moved on, and only by where the code sits. False with the switch off, or with no shape to compare (a page written before it was on).</summary>
    public static bool OnlyMoved(PageSet set, SpecPaths paths, PageDef page, JsonObject recorded, string inputHash) =>
        set.Settings.FollowMoves && (string?)recorded["generated"]?["bundleHash"] != inputHash
        && LoadFile(paths, page) is { } saved && (string?)saved["shape"] == Shape(set, page).Hash;

    /// <summary>Moves a page on with no AI: its input hash, commit, slice figures and unknowns' places, then its citations and Markdown. Its statements don't change.</summary>
    public static void Follow(PageSet set, StangentConfig config, SpecPaths paths, PageDef page, JsonObject recorded, Bundle bundle)
    {
        var slice = set.Slices[page.Id];
        var generated = recorded["generated"]!.AsObject();
        generated["commit"] = set.Commit;
        generated["bundleHash"] = bundle.InputHash;
        generated["slice"]!["chars"] = bundle.Chars;
        recorded["unknowns"] = new JsonArray([.. slice.Unknowns.Select(u => (JsonNode)new JsonObject { ["file"] = u.File, ["line"] = u.Line, ["reason"] = u.Reason, ["in"] = u.Owner })]);
        var errors = SchemaRegistry.Embedded.Validate("reference-page.v1.json", recorded.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_page", "A followed page broke reference-page v1 (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(paths.PageJson(page), CanonicalJson.Serialize(recorded));
        PageRun.WriteRendered(set, config, paths, page, recorded);
    }

    /// <summary><c>spec follow</c>: pages whose code only moved go on; a current page with no shape on file gets one, for next time.</summary>
    public static JsonObject Run(PageSet set, StangentConfig config, SpecPaths paths)
    {
        if (!set.Settings.FollowMoves) throw new StangentException("component_off", $"{Component} is off. Turn on `components.{Component}`: it rewrites a saved hash.", ExitCodes.Usage);
        var followed = new JsonArray();
        int started = 0, stale = 0;
        foreach (var page in set.Pages)
        {
            if (PageRun.LoadJson(paths.PageJson(page)) is not JsonObject recorded) continue;
            var bundle = Bundles.Build(set, page, null);
            if ((string?)recorded["generated"]?["bundleHash"] == bundle.InputHash)
            {
                if (LoadFile(paths, page) is null) { WriteFile(paths, page, Shape(set, page)); started++; }
            }
            else if (OnlyMoved(set, paths, page, recorded, bundle.InputHash))
            {
                Follow(set, config, paths, page, recorded, bundle);
                followed.Add(page.Id);
            }
            else stale++;
        }
        return new JsonObject { ["followed"] = followed, ["shapesStarted"] = started, ["stillStale"] = stale };
    }
}
