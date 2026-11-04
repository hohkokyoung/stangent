using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stangent.Core.State;

/// <summary>One line of a lifecycle's history.</summary>
public sealed record StateMove(string At, string Kind, string Id, string? From, string To, StateActor Who, string Reason)
{
    public JsonObject ToJson() => new()
    {
        ["at"] = At, ["kind"] = Kind, ["id"] = Id, ["from"] = From, ["to"] = To,
        ["who"] = new JsonObject { ["kind"] = Who.Kind, ["name"] = Who.Name }, ["reason"] = Reason,
    };
}

/// <summary>
/// Every lifecycle's state changes (D114): a move the kind's table doesn't list is refused, and a legal one adds one line
/// to the history. The history is append-only JSONL and is never part of an input hash or a byte-identical artifact, so
/// its timestamps change nothing else. An append holds the file itself under an OS lock, as the stores' lock files do.
/// </summary>
public static class StateChanges
{
    /// <summary>The kind of a line that says an item's id changed: <c>from</c> is the old id, <c>to</c> the new. It isn't a lifecycle, so no table lists it.</summary>
    public const string IdMoved = "id-moved";

    static readonly TimeSpan LockWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The one entry point. <paramref name="from"/> is null for a new item. Moving to the same state changes nothing and
    /// adds no line, unless the table lists it as a move. Returns whether a line was added.
    /// </summary>
    public static bool Move(string root, string kindName, string id, string? from, string to, StateActor who, string reason)
    {
        var kind = StateKinds.Get(kindName);
        from ??= kind.NullState;
        var rule = kind.Moves.FirstOrDefault(m => m.From == from && m.To == to);
        var allowed = from is null ? kind.Initial.Contains(to) && who.Kind == StateActor.CodeKind
            : rule is not null ? rule.Who.Contains(who.Kind)
            : from == to && kind.States.Contains(to);
        if (!allowed) throw Illegal(kind, id, from, to, who);
        if (from == to && rule is null) return false;
        var move = new StateMove(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), kind.Name, id, from, to, who, reason);
        Append(Path.Combine(root, kind.HistoryFile), CanonicalJson.SerializeCompact(move.ToJson()) + "\n");
        return true;
    }

    /// <summary>
    /// A move on a JSON item whose field holds the state: reads where it is, moves, writes the field. A new object that
    /// replaces an old one reads from <paramref name="previous"/>. The field is written even when nothing moved, so a new
    /// object always has it.
    /// </summary>
    public static void Apply(string root, JsonObject item, string kindName, string id, string to, StateActor who, string reason, JsonObject? previous = null)
    {
        var kind = StateKinds.Get(kindName);
        var field = kind.Field ?? throw new StangentException("state_has_no_field", $"Lifecycle '{kindName}' isn't held in a field of its own: call Move.");
        Move(root, kindName, id, (string?)(previous ?? item)[field], to, who, reason);
        item[field] = to == kind.NullState ? null : to;
        if (to == kind.NullState)
            foreach (var cleared in kind.ClearedOnNull ?? []) item[cleared] = null;
    }

    /// <summary>
    /// Items got new ids: one line for each, connecting old to new, so <see cref="History"/> follows them and no old line is rewritten.
    /// They go in one write at one time, because they happen together: when 3 moves to 4 and 4 to 5, an id's line is read with the others.
    /// </summary>
    public static void RecordIdMoves(string root, string kindName, IReadOnlyList<(string From, string To)> moved, StateActor who, string reason)
    {
        if (moved.Count == 0) return;
        var at = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        Append(Path.Combine(root, StateKinds.Get(kindName).HistoryFile),
            string.Concat(moved.Select(m => CanonicalJson.SerializeCompact(new StateMove(at, IdMoved, m.To, m.From, m.To, who, reason).ToJson()) + "\n")));
    }

    /// <summary>
    /// The item's moves, oldest first, across any change of its id, with the lines that say so. Asked about an id the item has since left, it gives
    /// what happened up to leaving. An id used again later starts fresh: the earlier item's moves aren't shown as its own. An item
    /// with no moves of this kind has no history, whatever its ids did.
    /// A damaged line stops the read, because a history with holes misleads.
    /// </summary>
    public static IReadOnlyList<StateMove> History(string root, string kindName, string id)
    {
        var kind = StateKinds.Get(kindName);
        var path = Path.Combine(root, kind.HistoryFile);
        if (!File.Exists(path)) return [];
        var lines = Read(path).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => Parse(l, path)).ToArray();
        var moves = new List<StateMove>();
        var current = id;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Kind != IdMoved)
            {
                if (lines[i].Kind == kind.Name && lines[i].Id == current) moves.Add(lines[i]);
                continue;
            }
            var first = i;
            while (first > 0 && lines[first - 1].Kind == IdMoved && lines[first - 1].At == lines[i].At) first--;
            var together = lines[first..(i + 1)];
            if (together.FirstOrDefault(m => m.To == current) is { } came)
            {
                moves.Add(came);
                current = came.From!;
            }
            else if (together.FirstOrDefault(m => m.From == current) is { } left)
            {
                if (moves.Count > 0) break;
                moves.Add(left);
            }
            i = first;
        }
        if (moves.All(m => m.Kind == IdMoved)) return [];
        moves.Reverse();
        return moves;
    }

    static StateMove Parse(string line, string path)
    {
        try
        {
            var o = JsonNode.Parse(line)!.AsObject();
            return new StateMove((string)o["at"]!, (string)o["kind"]!, (string)o["id"]!, (string?)o["from"], (string)o["to"]!,
                new StateActor((string)o["who"]!["kind"]!, (string)o["who"]!["name"]!), (string)o["reason"]!);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new StangentException("bad_state_history", $"A line of {path} can't be read; fix it by hand.");
        }
    }

    static StangentException Illegal(StateKind kind, string id, string? from, string to, StateActor who)
    {
        var legal = from is null
            ? kind.Initial.Select(s => $"(new) -> {s} by code")
            : kind.Moves.Where(m => m.From == from).Select(m => $"{m.From} -> {m.To} by {string.Join(" or ", m.Who)}");
        return new StangentException("illegal_transition", $"{kind.Name} {id}: {who.Kind} '{who.Name}' can't move it from {from ?? "(new)"} to {to}.",
            ExitCodes.Failed, [.. legal.DefaultIfEmpty($"nothing: {from} is a final state, or not a state of {kind.Name}")]);
    }

    static void Append(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.UTF8.GetBytes(line);
        using var stream = Open(() => new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None));
        stream.Write(bytes);
    }

    static string Read(string path)
    {
        using var stream = Open(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static FileStream Open(Func<FileStream> open)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return open(); }
            catch (IOException) when (waited.Elapsed < LockWait) { Thread.Sleep(20); }
        }
    }
}
