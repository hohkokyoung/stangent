namespace Stangent.Core.Evidence;

/// <summary>
/// The one scale for how much a finding matters, graded by its effect on users (D118). Code can't judge that, so a person sets it,
/// and a finding starts <see cref="Ungraded"/>.
/// </summary>
public static class Severity
{
    /// <summary>Users on the normal path lose data or can't finish.</summary>
    public const string Blocker = "blocker";
    /// <summary>A wrong result on the normal path, or blocked on a rare one.</summary>
    public const string Major = "major";
    /// <summary>Wrong on a rare path, or confusing.</summary>
    public const string Minor = "minor";
    /// <summary>Wording only.</summary>
    public const string Cosmetic = "cosmetic";
    /// <summary>Nobody has graded it yet.</summary>
    public const string Ungraded = "ungraded";

    public static readonly IReadOnlyList<string> All = [Blocker, Major, Minor, Cosmetic, Ungraded];
}
