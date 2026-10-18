using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Stangent.Core.Telemetry;

/// <summary>A command run being timed; finish it with <see cref="TelemetryWriter.End"/>.</summary>
public sealed class Span(string name, long startNanos)
{
    public string Name { get; } = name;
    public long StartNanos { get; } = startNanos;
    public string TraceId { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    public string SpanId { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
    /// <summary>Only keys listed in telemetry-event.v1.json are accepted; an event with any other key is not written.</summary>
    public Dictionary<string, object> Attributes { get; } = [];
}

/// <summary>
/// Writes one OpenTelemetry-shaped event per run as a JSON line in
/// <c>&lt;dir&gt;/&lt;yyyy-MM-dd&gt;.jsonl</c> (D21). Every event is validated against the
/// telemetry-event contract before it is written, so content can't leak in.
/// Telemetry never fails a command: an event that can't be written is reported on stderr and dropped.
/// </summary>
public sealed class TelemetryWriter(string dir, SchemaRegistry schemas, TimeProvider time, TextWriter? warnings = null)
{
    public const string Schema = "telemetry-event.v1.json";
    public static readonly string ServiceVersion =
        typeof(TelemetryWriter).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Environment variable that overrides where events go (CI saves this folder with each run).</summary>
    public const string DirVariable = "STANGENT_TELEMETRY_DIR";

    public string Dir => dir;

    public Span Start(string name) => new(name, Nanos());

    /// <summary>Validates and appends the event; returns it, or null if it was rejected.</summary>
    public JsonObject? End(Span span, bool ok)
    {
        var attributes = new JsonObject();
        foreach (var (key, value) in span.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
            attributes[key] = JsonValue.Create(value);

        var evt = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["traceId"] = span.TraceId,
            ["spanId"] = span.SpanId,
            ["name"] = span.Name,
            ["startTimeUnixNano"] = span.StartNanos.ToString(),
            ["endTimeUnixNano"] = Nanos().ToString(),
            ["status"] = new JsonObject { ["code"] = ok ? "STATUS_CODE_OK" : "STATUS_CODE_ERROR" },
            ["resource"] = new JsonObject { ["service.name"] = "stangent", ["service.version"] = ServiceVersion },
            ["attributes"] = attributes,
        };

        var line = CanonicalJson.SerializeCompact(evt);
        var errors = schemas.Validate(Schema, line);
        if (errors.Count > 0)
        {
            warnings?.WriteLine($"stangent: telemetry event dropped, it breaks {Schema}: {string.Join("; ", errors)}");
            return null;
        }
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, time.GetUtcNow().ToString("yyyy-MM-dd") + ".jsonl"), line + "\n");
            return evt;
        }
        catch (IOException e)
        {
            warnings?.WriteLine($"stangent: telemetry event dropped: {e.Message}");
            return null;
        }
    }

    long Nanos() => (time.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks * 100;
}
