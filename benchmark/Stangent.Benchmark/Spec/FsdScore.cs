using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Benchmark.Spec;

/// <summary>
/// Phase 2b's score (D56, D57): the findings in a repo's specs/trace.yaml against the planted answer key, by the
/// matching rule frozen in the key. Code only. Also the cost per section and per page, and candidates by code vs AI.
/// </summary>
public static class FsdScore
{
    public static JsonObject Run(string root, string keyPath, string? handCheckPath, int sample = 20)
    {
        var key = JsonNode.Parse(File.ReadAllText(keyPath))!.AsObject();
        var doc = (string)key["doc"]!;
        var items = key["items"]!.AsArray().OfType<JsonObject>().ToList();
        var findings = TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>()
            .Where(e => (string)e["state"]! != "agree").Where(e => e["fsd"] is null || (string?)e["fsd"]!["doc"] == doc).ToList();

        // Each planted item takes the first finding that matches it; a finding matches at most one item.
        var used = new HashSet<string>(StringComparer.Ordinal);
        var results = new JsonArray();
        var foundLenient = new Dictionary<string, bool>();
        var foundStrict = new Dictionary<string, bool>();
        foreach (var item in items)
        {
            var id = (string)item["id"]!;
            var match = findings.FirstOrDefault(f => !used.Contains((string)f["id"]!) && Matches(item, f, strict: false));
            if (match is not null) used.Add((string)match["id"]!);
            foundLenient[id] = match is not null;
            foundStrict[id] = match is not null && Matches(item, match, strict: true);
            results.Add(new JsonObject
            {
                ["id"] = id, ["kind"] = item["kind"]!.DeepClone(), ["found"] = match is not null, ["foundAsKind"] = foundStrict[id],
                ["by"] = match?["id"]?.DeepClone(), ["state"] = match?["state"]?.DeepClone(),
            });
        }
        var unplanted = findings.Where(f => !used.Contains((string)f["id"]!)).OrderBy(f => (string)f["id"]!, StringComparer.Ordinal).ToList();
        var rng = new Random(42);
        var handSample = unplanted.OrderBy(_ => rng.Next()).Take(sample).ToList();
        var handCheck = handCheckPath is not null && File.Exists(handCheckPath) ? JsonNode.Parse(File.ReadAllText(handCheckPath))!.AsObject() : null;

        JsonObject Share(Func<JsonObject, bool> which, IReadOnlyDictionary<string, bool> found)
        {
            var subset = items.Where(which).Select(i => found[(string)i["id"]!] ? 1.0 : 0.0).ToArray();
            var (lo, hi) = Range(subset);
            return new JsonObject { ["found"] = (int)subset.Sum(), ["of"] = subset.Length, ["share"] = Math.Round(subset.Average(), 3), ["range95"] = new JsonArray(lo, hi) };
        }
        JsonObject Shares(IReadOnlyDictionary<string, bool> found) => new()
        {
            ["overall"] = Share(_ => true, found),
            ["gap"] = Share(i => (string)i["kind"]! == "gap", found),
            ["conflict"] = Share(i => (string)i["kind"]! == "conflict", found),
            ["undocumented"] = Share(i => (string)i["kind"]! == "undocumented", found),
        };

        // False alarms: unplanted findings the hand check says aren't real drift; until it's done, the worst case (all).
        JsonObject noise;
        var realInSample = handCheck is null ? (int?)null : handSample.Count(f => (string?)handCheck[(string)f["id"]!]?["verdict"] == "real");
        var checkedInSample = handCheck is null ? 0 : handSample.Count(f => handCheck[(string)f["id"]!]?["verdict"] is not null);
        var worst = findings.Count == 0 ? 0 : (double)unplanted.Count / findings.Count;
        noise = new JsonObject
        {
            ["findings"] = findings.Count, ["unplanted"] = unplanted.Count, ["worstCaseShare"] = Math.Round(worst, 3),
            ["sample"] = handSample.Count, ["handChecked"] = checkedInSample, ["realInSample"] = realInSample,
        };
        if (realInSample is { } real && checkedInSample > 0)
        {
            var falseShare = 1 - (double)real / checkedInSample;
            var flags = handSample.Where(f => handCheck![(string)f["id"]!]?["verdict"] is not null)
                .Select(f => (string?)handCheck![(string)f["id"]!]!["verdict"] == "real" ? 0.0 : 1.0).ToArray();
            var (lo, hi) = Range(flags);
            noise["falseAlarmShare"] = Math.Round(worst * falseShare, 3);
            noise["falseAlarmRange95"] = new JsonArray(Math.Round(worst * lo, 3), Math.Round(worst * hi, 3));
        }

        var bars = new JsonObject();
        var lenient = Shares(foundLenient);
        bars["finds"] = (double)lenient["overall"]!["share"]! >= 0.8 && new[] { "gap", "conflict", "undocumented" }.All(k => (double)lenient[k]!["share"]! >= 0.6);
        bars["notTooNoisy"] = noise["falseAlarmShare"] is { } fa ? (double)fa <= 0.30 : worst <= 0.30 ? true : null;

        var runs = Stangent.Spec.Fsd.LinkRun.LoadRuns(root);
        var sections = runs.Where(r => r.Key.StartsWith(doc + "#", StringComparison.Ordinal)).Select(r => r.Value!.AsObject()).ToList();
        return new JsonObject
        {
            ["doc"] = doc,
            ["note"] = "Phase 2b (D56, D57): findings in specs/trace.yaml scored against the frozen answer key. 'found' lets a gap be reported as a conflict and back; 'foundAsKind' needs the planted kind.",
            ["found"] = lenient,
            ["foundAsKind"] = Shares(foundStrict),
            ["noise"] = noise,
            ["bars"] = bars,
            ["candidates"] = new JsonObject
            {
                ["sections"] = sections.Count,
                ["byCode"] = sections.Count(s => (string?)s["candidatesBy"] == "code"),
                ["byAi"] = sections.Count(s => (string?)s["candidatesBy"] == "ai"),
                ["aiFoundNone"] = sections.Count(s => (string?)s["candidatesBy"] == "ai" && s["candidates"]!.AsArray().Count == 0),
            },
            ["cost"] = Cost(sections),
            ["items"] = results,
            ["handCheckSample"] = new JsonArray([.. handSample.Select(f => (JsonNode)new JsonObject
            {
                ["id"] = f["id"]!.DeepClone(), ["state"] = f["state"]!.DeepClone(), ["section"] = f["fsd"]?["section"]?.DeepClone(),
                ["fsdQuote"] = f["fsd"]?["quote"]?.DeepClone(), ["page"] = f["page"]?["id"]?.DeepClone(),
                ["pageQuote"] = f["page"]?["quote"]?.DeepClone(), ["note"] = f["note"]?.DeepClone(),
            })]),
        };
    }

    static bool Matches(JsonObject item, JsonObject f, bool strict)
    {
        var kind = (string)item["kind"]!;
        var state = (string)f["state"]!;
        var section = (string?)f["fsd"]?["section"];
        if (kind is "gap" or "conflict")
        {
            if (strict ? state != kind : state is not ("gap" or "conflict")) return false;
            if (section != (string)item["section"]!) return false;
            var quote = Quotes.Normalize((string?)f["fsd"]?["quote"] ?? "").ToLowerInvariant();
            var text = Quotes.Normalize((string)item["text"]!).ToLowerInvariant();
            if (quote.Length == 0) return false;
            if (quote.Contains(text) || text.Contains(quote)) return true;
            var words = Regex.Matches(quote, "[a-z0-9]+").Select(m => m.Value).ToList();
            var inText = Regex.Matches(text, "[a-z0-9]+").Select(m => m.Value).ToHashSet();
            return words.Count > 0 && words.Count(inText.Contains) >= 0.6 * words.Count;
        }
        if (state != "undocumented") return false;
        var page = (string?)f["page"]?["id"];
        var onPage = item["pages"]!.AsArray().Any(p => (string)p! == page);
        var onSection = item["section"] is { } s && (string)s! == section;
        if (!onPage && !onSection) return false;
        if (item["keywords"] is not JsonArray keywords) return onPage;
        var said = ((string?)f["page"]?["quote"] + " " + (string?)f["note"]).ToLowerInvariant();
        return keywords.Any(k => said.Contains((string)k!, StringComparison.Ordinal));
    }

    /// <summary>95% range of a share by resampling (1,000 resamples, seed 42), as in D47 and D57.</summary>
    static (double, double) Range(double[] values)
    {
        if (values.Length == 0) return (0, 0);
        var rng = new Random(42);
        var means = new double[1000];
        for (var r = 0; r < means.Length; r++)
        {
            double sum = 0;
            for (var i = 0; i < values.Length; i++) sum += values[rng.Next(values.Length)];
            means[r] = sum / values.Length;
        }
        Array.Sort(means);
        return (Math.Round(means[25], 3), Math.Round(means[974], 3));
    }

    static JsonObject Cost(List<JsonObject> sections)
    {
        JsonObject Role(string role)
        {
            var runs = sections.Select(s => Usage.FromJson(s[role])).Where(u => u.Calls > 0).ToList();
            return new JsonObject
            {
                ["calls"] = runs.Sum(u => u.Calls), ["sections"] = runs.Count,
                ["inputTokensPerSection"] = runs.Count == 0 ? 0 : runs.Average(u => u.InputTokens + u.CacheReadTokens + u.CacheWriteTokens),
                ["outputTokensPerSection"] = runs.Count == 0 ? 0 : runs.Average(u => u.OutputTokens),
                ["secondsPerSection"] = runs.Count == 0 ? 0 : Math.Round(runs.Average(u => u.Ms) / 1000.0, 1),
                ["costUsd"] = Math.Round(runs.Sum(u => u.CostUsd), 2),
            };
        }
        return new JsonObject { ["finder"] = Role("finder"), ["judge"] = Role("judge"), ["checker"] = Role("checker") };
    }
}
