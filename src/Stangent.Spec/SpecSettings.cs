using Stangent.Core;

namespace Stangent.Spec;

/// <summary>The <c>spec</c> settings from <c>stangent.yaml</c> (D54), with command-line overrides.</summary>
public sealed record SpecSettings(int Depth, int BackendDepth, bool SplitAtEndpoints, int HubCallers, int MaxBundleChars, bool Exclusions = false)
{
    public static SpecSettings From(StangentConfig config, int? depth = null)
    {
        var c = config.Spec;
        return new(depth ?? c.Depth, c.BackendDepth, c.SplitAtEndpoints, c.HubCallers, c.MaxBundleChars, StangentComponents.IsOn(config, "page-exclusions"));
    }

    /// <summary>What the slices depend on, so a settings change makes every page stale.</summary>
    public string Describe() =>
        $"depth {Depth}, backend depth {BackendDepth}, {(SplitAtEndpoints ? "screens stop at endpoints" : "screens walk into the backend")}, " +
        $"points with {HubCallers}+ callers not walked into";
}
