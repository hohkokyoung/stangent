using Stangent.Core;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

public static class DetectorRegistry
{
    /// <summary>Every built-in detector, in the order they run.</summary>
    public static IReadOnlyList<IDetector> BuiltIn { get; } =
    [
        new RoutesDetector(),
        new DiDetector(),
        new PublishHandleDetector(),
        new EntityTablesDetector(),
        new ConfigKeysDetector(),
        new HttpServicesDetector(), // after routes: it links calls to their endpoints
    ];

    /// <summary>Empty list → all built-ins; <c>[none]</c> → none; otherwise the named ones (D17).</summary>
    public static IReadOnlyList<IDetector> Enabled(StangentConfig config)
    {
        if (config.Detectors.Count == 0) return BuiltIn;
        if (config.Detectors is ["none"]) return [];
        var unknown = config.Detectors.Except(BuiltIn.Select(d => d.Name)).ToList();
        if (unknown.Count > 0)
            throw new StangentException("unknown_detector",
                $"stangent.yaml names unknown detector(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", BuiltIn.Select(d => d.Name))}.");
        return [.. BuiltIn.Where(d => config.Detectors.Contains(d.Name))];
    }

    public static IReadOnlyDictionary<string, List<string>> Settings(StangentConfig config, string detector) =>
        config.DetectorSettings.TryGetValue(detector, out var s) ? s : new Dictionary<string, List<string>>();
}

/// <summary>Helpers for the <c>&lt;assembly&gt;|&lt;documentation id&gt;</c> ids the graph uses.</summary>
public static class DocIds
{
    public static string Assembly(string nodeId) => nodeId[..Math.Max(0, nodeId.IndexOf('|'))];

    /// <summary>The type a member id belongs to, e.g. <c>A|M:Ns.T.Run(System.Int32)</c> → <c>A|T:Ns.T</c>.</summary>
    public static string? ContainingType(string memberId)
    {
        var bar = memberId.IndexOf('|');
        if (bar < 0 || memberId.Length < bar + 3 || memberId[bar + 2] != ':' || memberId[bar + 1] == 'T') return null;
        var body = memberId[(bar + 3)..];
        var paren = body.IndexOf('(');
        if (paren >= 0) body = body[..paren];
        var dot = body.LastIndexOf('.');
        return dot < 0 ? null : $"{memberId[..bar]}|T:{body[..dot]}";
    }

    /// <summary>For a constructor id (<c>…#ctor…</c>), the type it constructs; otherwise null.</summary>
    public static string? ConstructedType(string methodId) =>
        methodId.Contains("|M:", StringComparison.Ordinal) && methodId.Contains(".#ctor", StringComparison.Ordinal) ? ContainingType(methodId) : null;
}
