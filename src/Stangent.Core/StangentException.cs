namespace Stangent.Core;

/// <summary>
/// A failure the CLI reports as <c>{"ok":false,"error":{...}}</c>. Codes are stable
/// snake_case strings (they also go into telemetry); the message is for humans.
/// </summary>
public class StangentException(string code, string message, int exitCode = ExitCodes.Failed, IReadOnlyList<string>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;
    public IReadOnlyList<string> Details { get; } = details ?? [];
}

/// <summary>Exit codes, part of the cli-conventions contract (docs/cli-conventions.md).</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    /// <summary>A check failed: validation, stale phases, a missing link target.</summary>
    public const int Failed = 1;
    public const int Usage = 2;
    public const int NotFound = 3;
    public const int AiDisabled = 4;
}
