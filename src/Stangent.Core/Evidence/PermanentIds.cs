using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stangent.Core.Evidence;

/// <summary>
/// Permanent ids (D113): per scope, ids in one format, never given twice. A removed id stays on the file, marked
/// removed, so it can't come back. Every call reads the file under a lock and writes it whole, so parallel drivers
/// share one ledger; the lock is <c>&lt;file&gt;.lock</c> beside it.
/// </summary>
public sealed partial class PermanentIds(string path)
{
    static readonly TimeSpan LockWait = TimeSpan.FromSeconds(30);

    /// <summary>Hands out the next id of the scope. The format holds one number, e.g. <c>R-{0:D4}</c>; a scope keeps its
    /// first format, so old ids stay readable.</summary>
    public string Issue(string scope, string format)
    {
        if (string.IsNullOrWhiteSpace(scope)) throw new StangentException("bad_id_scope", "An id scope needs a name.", ExitCodes.Usage);
        // Only plain or padded numbers: any other number format can give two numbers the same text.
        if (!FormatShape().IsMatch(format))
            throw new StangentException("bad_id_format", $"An id format holds one number, as {{0}} or {{0:D4}}: '{format}'.", ExitCodes.Usage);
        return Locked(ledger =>
        {
            var entry = ledger[scope]?.AsObject();
            if (entry is null) ledger[scope] = entry = new JsonObject { ["format"] = format, ["last"] = 0, ["removed"] = new JsonArray() };
            if ((string)entry["format"]! != format)
                throw new StangentException("id_format_changed", $"Scope '{scope}' issues ids as '{entry["format"]}', not '{format}'.", ExitCodes.Usage);
            var next = (int)entry["last"]! + 1;
            entry["last"] = next;
            return (Text(format, next), true);
        });
    }

    /// <summary>Marks an issued id removed. Removing it again changes nothing.</summary>
    public void Remove(string scope, string id) => Locked(ledger =>
    {
        var entry = ledger[scope]?.AsObject();
        if (entry is null || !Issued(entry).Contains(id))
            throw new StangentException("unknown_id", $"Scope '{scope}' never issued '{id}'.", ExitCodes.NotFound);
        var removed = entry["removed"]!.AsArray();
        if (removed.Any(r => (string)r! == id)) return (id, false);
        removed.Add(id);
        return (id, true);
    });

    /// <summary>The scope's ids in use, oldest first.</summary>
    public IReadOnlyList<string> Active(string scope) => Locked(ledger =>
    {
        if (ledger[scope]?.AsObject() is not { } entry) return ((IReadOnlyList<string>)[], false);
        var removed = entry["removed"]!.AsArray().Select(r => (string)r!).ToHashSet();
        return ([.. Issued(entry).Where(id => !removed.Contains(id))], false);
    });

    /// <summary>The scope's removed ids, in the order they were removed.</summary>
    public IReadOnlyList<string> Removed(string scope) => Locked(ledger =>
        (ledger[scope]?["removed"]?.AsArray().Select(r => (string)r!).ToList() ?? (IReadOnlyList<string>)[], false));

    static string Text(string format, int number) => string.Format(CultureInfo.InvariantCulture, format, number);

    static IEnumerable<string> Issued(JsonObject entry) =>
        Enumerable.Range(1, (int)entry["last"]!).Select(n => Text((string)entry["format"]!, n));

    T Locked<T>(Func<JsonObject, (T Value, bool Changed)> body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var held = TakeLock();
        var doc = Load();
        var (value, changed) = body(doc["scopes"]!.AsObject());
        if (changed) Save(doc);
        return value;
    }

    FileStream TakeLock()
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (waited.Elapsed < LockWait) { Thread.Sleep(20); }
        }
    }

    /// <summary>A damaged file stops the call: starting over would give out old ids again.</summary>
    JsonObject Load()
    {
        if (!File.Exists(path)) return new JsonObject { ["version"] = 1, ["scopes"] = new JsonObject() };
        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            foreach (var (_, scope) in doc["scopes"]!.AsObject())
            {
                _ = (string)scope!["format"]!;
                _ = (int)scope["last"]!;
                _ = scope["removed"]!.AsArray();
            }
            return doc;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new StangentException("bad_id_ledger", $"The id ledger {path} can't be read; fix it by hand, ids must not be given twice.");
        }
    }

    void Save(JsonObject doc)
    {
        var temp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, CanonicalJson.Serialize(doc));
        File.Move(temp, path, overwrite: true);
    }

    [GeneratedRegex(@"\A[^{}\s]*\{0(:D[0-9]{1,2})?\}[^{}\s]*\z")]
    private static partial Regex FormatShape();
}
