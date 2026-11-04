using System.Text;

namespace Stangent.Spec.Fsd;

/// <summary>
/// An OLE compound file, read only as far as stangent needs: its stream names and contents (D123). It holds a macro project's modules, the file inside an OLE package, and a locked
/// Office file. Every chain and tree in the file is walked with a limit, since a hostile file can make them loop, and anything that doesn't add up reads as no container at all.
/// </summary>
public sealed class OleContainer
{
    const int FreeSector = -1, EndOfChain = -2, NoEntry = -1, HeaderBytes = 512, EntryBytes = 128;
    static readonly byte[] Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    readonly byte[] file;
    readonly int sectorBytes, miniBytes, miniCutoff;
    readonly int[] fat;
    int[] miniFat = [];
    byte[] miniStream = [];
    readonly Dictionary<string, (int Start, long Size)> streams = new(StringComparer.Ordinal);

    public IEnumerable<string> Streams => streams.Keys;

    public static bool IsOle(byte[] bytes) => bytes.Length >= Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>The container, or null for anything that isn't one or doesn't add up.</summary>
    public static OleContainer? TryOpen(byte[] bytes)
    {
        if (!IsOle(bytes) || bytes.Length < HeaderBytes) return null;
        try { return new OleContainer(bytes); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException or OverflowException) { return null; }
    }

    OleContainer(byte[] bytes)
    {
        file = bytes;
        var sectorShift = BitConverter.ToUInt16(file, 30);
        var miniShift = BitConverter.ToUInt16(file, 32);
        if (sectorShift is not (9 or 12) || miniShift != 6) throw new InvalidDataException("not a sector size this format has");
        sectorBytes = 1 << sectorShift;
        miniBytes = 1 << miniShift;
        miniCutoff = BitConverter.ToInt32(file, 56);
        fat = ReadFat();
        var directory = Chain(BitConverter.ToInt32(file, 48), fat, long.MaxValue);
        var minis = BitConverter.ToInt32(file, 60);
        if (minis >= 0) miniFat = ToInts(Chain(minis, fat, long.MaxValue));
        Walk(directory);
    }

    int[] ReadFat()
    {
        var sectors = new List<int>();
        for (var i = 0; i < 109; i++) sectors.Add(BitConverter.ToInt32(file, 76 + i * 4));
        var next = BitConverter.ToInt32(file, 68);
        var limit = file.Length / sectorBytes + 1;
        for (var seen = 0; next >= 0 && seen < limit; seen++)
        {
            var at = Offset(next);
            for (var i = 0; i < sectorBytes / 4 - 1; i++) sectors.Add(BitConverter.ToInt32(file, at + i * 4));
            next = BitConverter.ToInt32(file, at + sectorBytes - 4);
        }
        var table = new List<int>();
        foreach (var sector in sectors.Where(s => s >= 0))
        {
            var at = Offset(sector);
            for (var i = 0; i < sectorBytes / 4; i++) table.Add(BitConverter.ToInt32(file, at + i * 4));
        }
        return [.. table];
    }

    int Offset(int sector)
    {
        var at = (long)(sector + 1) * sectorBytes;
        if (at < 0 || at + sectorBytes > file.Length) throw new InvalidDataException("a sector past the end of the file");
        return (int)at;
    }

    byte[] Chain(int start, int[] table, long size)
    {
        var bytes = new MemoryStream();
        var seen = new HashSet<int>();
        for (var s = start; s != EndOfChain && s != FreeSector && bytes.Length < size; s = table[s])
        {
            if (s < 0 || s >= table.Length || !seen.Add(s)) throw new InvalidDataException("a sector chain that loops or leaves the table");
            bytes.Write(file, Offset(s), sectorBytes);
        }
        return bytes.ToArray();
    }

    static int[] ToInts(byte[] bytes)
    {
        var ints = new int[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, ints, 0, ints.Length * 4);
        return ints;
    }

    void Walk(byte[] directory)
    {
        var count = directory.Length / EntryBytes;
        if (count == 0) throw new InvalidDataException("no directory");
        miniStream = Chain(BitConverter.ToInt32(directory, 116), fat, BitConverter.ToInt64(directory, 120));
        var pending = new Stack<(int Entry, string Parent)>();
        pending.Push((BitConverter.ToInt32(directory, 76), ""));
        var seen = new HashSet<int>();
        while (pending.Count > 0)
        {
            var (entry, parent) = pending.Pop();
            if (entry == NoEntry) continue;
            if (entry < 0 || entry >= count || !seen.Add(entry)) throw new InvalidDataException("a directory that loops or leaves itself");
            var at = entry * EntryBytes;
            var nameBytes = BitConverter.ToUInt16(directory, at + 64);
            var name = nameBytes is >= 2 and <= 64 ? Encoding.Unicode.GetString(directory, at, nameBytes - 2) : "";
            var kind = directory[at + 66];
            pending.Push((BitConverter.ToInt32(directory, at + 68), parent));
            pending.Push((BitConverter.ToInt32(directory, at + 72), parent));
            if (kind == 1) pending.Push((BitConverter.ToInt32(directory, at + 76), $"{parent}{name}/"));
            else if (kind == 2) streams[parent + name] = (BitConverter.ToInt32(directory, at + 116), BitConverter.ToInt64(directory, at + 120));
        }
    }

    /// <summary>A stream's bytes, or null if it isn't there or its chain doesn't add up.</summary>
    public byte[]? Read(string name)
    {
        if (!streams.TryGetValue(name, out var s) || s.Size < 0 || s.Size > file.Length) return null;
        try
        {
            var bytes = s.Size < miniCutoff ? Mini(s.Start, s.Size) : Chain(s.Start, fat, s.Size);
            return bytes.Length < s.Size ? null : bytes[..(int)s.Size];
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException) { return null; }
    }

    byte[] Mini(int start, long size)
    {
        var bytes = new MemoryStream();
        var seen = new HashSet<int>();
        for (var s = start; s != EndOfChain && s != FreeSector && bytes.Length < size; s = miniFat[s])
        {
            if (s < 0 || s >= miniFat.Length || !seen.Add(s)) throw new InvalidDataException("a mini chain that loops or leaves the table");
            bytes.Write(miniStream, s * miniBytes, miniBytes);
        }
        return bytes.ToArray();
    }

    /// <summary>
    /// The file inside an OLE package's <c>Ole10Native</c> stream: its name and bytes, or null when the stream isn't there or doesn't add up. Its shape: total size, flags, a label, the
    /// original path, a short marker, a temporary path with its length, then the data with its length.
    /// </summary>
    public (string Name, byte[] Bytes)? Package()
    {
        if (Read("\u0001Ole10Native") is not { } s) return null;
        try
        {
            var at = 6;
            string Text()
            {
                var end = Array.IndexOf(s, (byte)0, at);
                if (end < 0) throw new InvalidDataException("text with no end");
                var text = Encoding.Latin1.GetString(s, at, end - at);
                at = end + 1;
                return text;
            }
            var label = Text();
            var path = Text();
            at += 4;
            var tempLength = BitConverter.ToInt32(s, at);
            if (tempLength < 0 || at + 4 + (long)tempLength > s.Length) return null;
            at += 4 + tempLength;
            var length = BitConverter.ToInt32(s, at);
            at += 4;
            if (length < 0 || at + (long)length > s.Length) return null;
            var name = path.Length > 0 ? path : label;
            return (name.Replace('\\', '/').Split('/')[^1], s[at..(at + length)]);
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or IndexOutOfRangeException) { return null; }
    }
}
