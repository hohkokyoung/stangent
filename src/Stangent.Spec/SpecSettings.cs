using Stangent.Core;

namespace Stangent.Spec;

/// <summary>The <c>spec</c> settings from <c>stangent.yaml</c> (D54), with command-line overrides.</summary>
public sealed record SpecSettings(int Depth, int BackendDepth, bool SplitAtEndpoints, int HubCallers, int MaxBundleChars, bool Exclusions = false, bool FollowMoves = false, bool KeepShape = false, bool DisproveSmall = false, bool SharedThrough = false, bool DisproveCallers = false, bool SecurityFlag = false, bool PageGapStrict = false)
{
    public static SpecSettings From(StangentConfig config, int? depth = null)
    {
        var c = config.Spec;
        return new(depth ?? c.Depth, c.BackendDepth, c.SplitAtEndpoints, c.HubCallers, c.MaxBundleChars, StangentComponents.IsOn(config, "page-exclusions"), StangentComponents.IsOn(config, "follow-moves"),
            StangentComponents.IsOn(config, "follow-moves") || StangentComponents.IsOn(config, "statement-update"), StangentComponents.IsOn(config, "disprove-small"), StangentComponents.IsOn(config, "shared-through"), StangentComponents.IsOn(config, "disprove-callers"), StangentComponents.IsOn(config, "security-flag"), StangentComponents.IsOn(config, "page-gap-strict"));
    }

    /// <summary>What the slices depend on, so a settings change makes every page stale.</summary>
    public string Describe() =>
        $"depth {Depth}, backend depth {BackendDepth}, {(SplitAtEndpoints ? "screens stop at endpoints" : "screens walk into the backend")}, " +
        $"points with {HubCallers}+ callers not walked into";
}
