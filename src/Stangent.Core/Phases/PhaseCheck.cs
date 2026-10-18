using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Stangent.Core.Phases;

/// <summary>The front matter of a <c>phases/&lt;n&gt;-&lt;name&gt;.md</c> file (D20).</summary>
public sealed class PhaseFile
{
    public string Phase { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public Dictionary<string, int> Consumes { get; set; } = [];
    public Dictionary<string, int> Provides { get; set; } = [];
    [YamlIgnore] public string Path { get; set; } = "";
}

/// <summary>
/// The D20 dependency check. Compares every phase's <c>consumes</c> versions against the
/// versions currently provided, and every phase file's status against the roadmap table.
/// Flags only — it never edits a phase.
/// </summary>
public static class PhaseCheck
{
    public static readonly string[] Statuses = ["not started", "planned", "in progress", "done", "needs review"];

    static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>Returns the report; <c>ok</c> is false when anything needs attention.</summary>
    public static JsonObject Run(string phasesDir, string? roadmapPath)
    {
        if (!Directory.Exists(phasesDir))
            throw new StangentException("not_found", $"No phases folder at {phasesDir}.", ExitCodes.NotFound);

        var problems = new List<string>();
        var phases = new List<PhaseFile>();
        foreach (var path in Directory.EnumerateFiles(phasesDir, "*.md").Order(StringComparer.Ordinal))
        {
            try { phases.Add(Load(path)); }
            catch (StangentException e) { problems.Add(e.Message); }
        }

        var planned = roadmapPath is not null && File.Exists(roadmapPath) ? PlannedContracts(roadmapPath) : null;
        var providers = new Dictionary<string, (PhaseFile Phase, int Version)>();
        foreach (var phase in phases)
        {
            if (!Statuses.Contains(phase.Status))
                problems.Add($"{phase.Path}: status '{phase.Status}' is not one of: {string.Join(", ", Statuses)}");
            foreach (var (contract, version) in phase.Provides.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (providers.TryGetValue(contract, out var other))
                    problems.Add($"{phase.Path}: contract '{contract}' is also provided by phase {other.Phase.Phase}");
                else
                    providers[contract] = (phase, version);
            }
        }

        var report = new JsonArray();
        foreach (var phase in phases)
        {
            var stale = new JsonArray();
            foreach (var (contract, version) in phase.Consumes.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!providers.TryGetValue(contract, out var provider))
                {
                    // The provider phase may not have a file yet; that's normal. A name nobody
                    // plans to provide is a typo.
                    if (planned is not null && !planned.Contains(contract))
                        problems.Add($"{phase.Path}: consumes '{contract}', which no phase in the roadmap provides");
                    continue;
                }
                if (version < provider.Version)
                    stale.Add(new JsonObject { ["contract"] = contract, ["consumes"] = version, ["current"] = provider.Version, ["providedBy"] = provider.Phase.Phase });
                else if (version > provider.Version)
                    problems.Add($"{phase.Path}: consumes {contract} v{version}, but phase {provider.Phase.Phase} only provides v{provider.Version}");
            }
            var needsReview = stale.Count > 0;
            if (needsReview && phase.Status != "needs review")
                problems.Add($"{phase.Path}: consumes an old contract version; mark it 'needs review' and update it");
            report.Add(new JsonObject
            {
                ["phase"] = phase.Phase, ["name"] = phase.Name, ["status"] = phase.Status, ["path"] = phase.Path,
                ["stale"] = stale,
            });
        }

        if (roadmapPath is not null && File.Exists(roadmapPath))
            problems.AddRange(CompareRoadmap(phases, roadmapPath));

        return new JsonObject
        {
            ["ok"] = problems.Count == 0,
            ["phases"] = report,
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        };
    }

    static PhaseFile Load(string path)
    {
        var lines = File.ReadAllLines(path);
        var end = Array.IndexOf(lines, "---", 1);
        if (lines.Length == 0 || lines[0] != "---" || end < 0)
            throw new StangentException("invalid_phase", $"{path}: no front matter between '---' lines");
        try
        {
            var phase = Reader.Deserialize<PhaseFile?>(string.Join("\n", lines[1..end]))
                ?? throw new StangentException("invalid_phase", $"{path}: empty front matter");
            if (phase.Phase == "" || phase.Name == "")
                throw new StangentException("invalid_phase", $"{path}: front matter needs 'phase' and 'name'");
            phase.Path = System.IO.Path.GetFileName(path);
            return phase;
        }
        catch (YamlException e)
        {
            throw new StangentException("invalid_phase", $"{path}: {e.Message}");
        }
    }

    /// <summary>Contract names in the roadmap table's "Provides contracts" column (the 4th).</summary>
    static HashSet<string> PlannedContracts(string roadmapPath) =>
    [
        .. File.ReadAllLines(roadmapPath)
            .Where(l => l.StartsWith("| **"))
            .Select(l => l.Split('|'))
            .Where(cells => cells.Length > 4)
            .SelectMany(cells => System.Text.RegularExpressions.Regex.Matches(cells[4], "`([a-z0-9-]+)`").Select(m => m.Groups[1].Value)),
    ];

    /// <summary>Each phase file's status must match its row in the roadmap table (row starts "| **&lt;phase&gt;.").</summary>
    static IEnumerable<string> CompareRoadmap(List<PhaseFile> phases, string roadmapPath)
    {
        var rows = File.ReadAllLines(roadmapPath).Where(l => l.StartsWith("| **")).ToList();
        foreach (var phase in phases)
        {
            var row = rows.FirstOrDefault(r => r.StartsWith($"| **{phase.Phase}.") || r.StartsWith($"| **{phase.Name}", StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                yield return $"roadmap: no row for phase {phase.Phase} ({phase.Name})";
                continue;
            }
            var status = row.TrimEnd().TrimEnd('|').Split('|').Last().Trim();
            if (!status.StartsWith(phase.Status))
                yield return $"roadmap: phase {phase.Phase} shows '{status}', but {phase.Path} says '{phase.Status}'";
        }
    }
}
