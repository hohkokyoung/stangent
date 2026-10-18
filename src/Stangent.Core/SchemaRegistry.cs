using System.Collections.Concurrent;
using NJsonSchema;

namespace Stangent.Core;

/// <summary>
/// The versioned contracts (D8). Schemas are looked up by file name, e.g.
/// <c>artifact-base.v1.json</c> or <c>types/note.v1.json</c>.
/// </summary>
public sealed class SchemaRegistry(Func<string, string?> load)
{
    readonly ConcurrentDictionary<string, JsonSchema?> cache = new();

    /// <summary>The schemas compiled into the tool from the repo's <c>schemas/</c> folder.</summary>
    public static SchemaRegistry Embedded { get; } = new(name =>
    {
        using var stream = typeof(SchemaRegistry).Assembly.GetManifestResourceStream("schemas/" + name)
            ?? typeof(SchemaRegistry).Assembly.GetManifestResourceStream("schemas/" + name.Replace('/', '\\'));
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public bool Has(string name) => Get(name) is not null;

    /// <summary>Validation errors as "path: kind" lines; empty when valid.</summary>
    public IReadOnlyList<string> Validate(string name, string json)
    {
        var schema = Get(name) ?? throw new StangentException("unknown_schema", $"No schema named {name}.");
        return [.. schema.Validate(json)
            .Select(e => $"{(string.IsNullOrEmpty(e.Path) ? "#" : e.Path)}: {e.Kind}")
            .Order(StringComparer.Ordinal)];
    }

    JsonSchema? Get(string name) => cache.GetOrAdd(name, n =>
    {
        var json = load(n);
        return json is null ? null : JsonSchema.FromJsonAsync(json).GetAwaiter().GetResult();
    });
}
