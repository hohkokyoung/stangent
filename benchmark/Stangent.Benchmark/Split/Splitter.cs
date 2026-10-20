namespace Stangent.Benchmark.Split;

/// <summary>
/// A piece of one hunk: old lines <c>[OldFrom, OldFrom+OldLen)</c> of the parent replaced by new lines
/// <c>[NewFrom, NewFrom+NewLen)</c> of the change. A member piece has the members it changes; an outside piece
/// has none, and <see cref="OutsideType"/> is the type it sits in (a class header, braces, usings when null).
/// </summary>
sealed record Piece(int Id, string File, int Order, int OldFrom, int OldLen, int NewFrom, int NewLen, IReadOnlyList<string> Members, string? OutsideType);

/// <summary>One group of a rule: its members, and what applying it alone writes (pieces, plus whole files).</summary>
sealed record SplitGroup(IReadOnlyList<string> Members, IReadOnlyList<string> Files, IReadOnlySet<int> Pieces, IReadOnlySet<string> WholeFiles);

/// <summary>A rule's answer for one case: its groups, and how many groups a shared hunk had to join (D48 refined).</summary>
sealed record RuleSplit(string Rule, IReadOnlyList<SplitGroup> Groups, int JoinedBySharedHunk)
{
    public bool Split => Groups.Count >= 2;
}

/// <summary>
/// Cuts one change into pieces at member boundaries and groups them (D48). Pure: the graphs, the diff and
/// the 📜 links come in; the same inputs always give the same groups.
/// </summary>
sealed class Splitter
{
    public const string Direct = "direct only (✅+🟡, 1 step)";
    public const string Within2 = "within 2 steps (✅+🟡)";
    public const string Within2History = "within 2 steps (✅+🟡) + 📜";
    public const string Folder = "split by folder";
    public static readonly string[] Rules = [Within2, Direct, Within2History, Folder];

    readonly GraphView after;
    readonly GraphView parent;

    public IReadOnlyList<FileChange> Files { get; }
    public List<Piece> Pieces { get; } = [];
    public SortedSet<string> ChangedTypes { get; } = new(StringComparer.Ordinal);
    /// <summary>Every changed member: members whose lines changed, plus changed types (header lines, new or deleted types).</summary>
    public SortedSet<string> Members { get; } = new(StringComparer.Ordinal);
    /// <summary>Files with no changed member (project files, config, resources): applied whole with every group.</summary>
    public SortedSet<string> SharedFiles { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> MemberFiles { get; } = new(StringComparer.Ordinal);
    public int Hunks { get; private set; }
    public int HunksSplit { get; private set; }

    readonly Dictionary<string, SortedSet<string>> filesOfMember = new(StringComparer.Ordinal);
    /// <summary>Old and new ids of a member whose signature changed: one member.</summary>
    readonly List<(string Old, string New)> signatureChanges = [];

    public Splitter(IReadOnlyList<FileChange> files, GraphView after, GraphView parent)
    {
        this.after = after;
        this.parent = parent;
        Files = files;
        foreach (var f in files)
            for (var h = 0; h < f.Hunks.Count; h++) Cut(f, h);
        foreach (var key in Pieces.SelectMany(p => p.Members).Distinct().ToList())
            if ((after.Has(key) ? after : parent).ContainerType(key) is { } type && after.Has(type) != parent.Has(type))
                ChangedTypes.Add(type);
        Members.UnionWith(Pieces.SelectMany(p => p.Members));
        Members.UnionWith(ChangedTypes);
        foreach (var p in Pieces)
            foreach (var key in KeysOf(p))
            {
                if (!filesOfMember.TryGetValue(key, out var set)) filesOfMember[key] = set = new(StringComparer.Ordinal);
                set.Add(p.File);
                MemberFiles.Add(p.File);
            }
        foreach (var f in files.Where(f => !MemberFiles.Contains(f.Path))) SharedFiles.Add(f.Path);
    }

    /// <summary>The members a piece belongs to: its own, or the changed type it sits in.</summary>
    IEnumerable<string> KeysOf(Piece p) =>
        p.Members.Count > 0 ? p.Members : p.OutsideType is { } t && ChangedTypes.Contains(t) ? [t] : [];

    sealed record Run(string Key, int From, int Len, IReadOnlyList<string> Members, string? Type)
    {
        public int End => From + Len;
    }

    List<Run> Runs(FileChange f, int from, int count, bool newSide)
    {
        var view = newSide ? after : parent;
        var lines = newSide ? f.NewLines : f.OldLines;
        var runs = new List<Run>();
        for (var i = from; i < from + count; i++)
        {
            var owner = view.Owner(f.Path, i + 1);
            if (owner.Members.Count == 0 && owner.Type is { } type && i < lines.Length && !IsTrivial(FileChange.Text(lines[i])))
                ChangedTypes.Add(type);
            var key = owner.Members.Count > 0 ? "m:" + string.Join('\u001f', owner.Members.Select(NameOf).Distinct()) : "o:" + owner.Type;
            if (runs.Count > 0 && runs[^1].Key == key)
                runs[^1] = runs[^1] with { Len = runs[^1].Len + 1, Members = [.. runs[^1].Members.Union(owner.Members).Order(StringComparer.Ordinal)] };
            else runs.Add(new Run(key, i, 1, owner.Members, owner.Type));
        }
        return runs;
    }

    /// <summary>
    /// A member's name without its parameter list: <c>M:Ns.T.Run(System.Int32)</c> → <c>M:Ns.T.Run</c>. A member whose
    /// signature changed has a different id on each side of the diff, but it's the same text, so it's one member.
    /// </summary>
    public static string NameOf(string key) => key.StartsWith("M:", StringComparison.Ordinal) && key.IndexOf('(') is var i and > 0 ? key[..i] : key;

    /// <summary>Lines that change no meaning on their own: blank, braces and punctuation, comments, regions.</summary>
    public static bool IsTrivial(string line)
    {
        var t = line.Trim();
        return t.Length == 0 || t.All(c => "{}()[];,".Contains(c)) || t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)
            || t.StartsWith('*') || t.StartsWith("#region", StringComparison.Ordinal) || t.StartsWith("#endregion", StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits a hunk at member boundaries. Old and new runs with the same owner are matched in order (LCS);
    /// what's left between matches becomes insertions then deletions, each its own piece, so any subset
    /// still applies as text.
    /// </summary>
    void Cut(FileChange f, int h)
    {
        var hunk = f.Hunks[h];
        Hunks++;
        var oldRuns = hunk.OldCount > 0 ? Runs(f, hunk.OldFrom, hunk.OldCount, newSide: false) : [];
        var newRuns = hunk.NewCount > 0 ? Runs(f, hunk.NewFrom, hunk.NewCount, newSide: true) : [];
        var matches = Lcs(oldRuns.Select(r => r.Key).ToList(), newRuns.Select(r => r.Key).ToList());
        matches.Add((oldRuns.Count, newRuns.Count));
        var before = Pieces.Count;
        var order = h * 10_000;
        int oi = 0, ni = 0, cursor = hunk.OldFrom;
        foreach (var (i, j) in matches)
        {
            for (; ni < j; ni++)
                Pieces.Add(new Piece(Pieces.Count, f.Path, order++, cursor, 0, newRuns[ni].From, newRuns[ni].Len, newRuns[ni].Members, newRuns[ni].Type));
            for (; oi < i; oi++)
            {
                Pieces.Add(new Piece(Pieces.Count, f.Path, order++, oldRuns[oi].From, oldRuns[oi].Len, 0, 0, oldRuns[oi].Members, oldRuns[oi].Type));
                cursor = oldRuns[oi].End;
            }
            if (i < oldRuns.Count)
            {
                var members = oldRuns[i].Members.Union(newRuns[j].Members).Order(StringComparer.Ordinal).ToList();
                foreach (var a in oldRuns[i].Members)
                    foreach (var b in newRuns[j].Members.Where(b => b != a && NameOf(b) == NameOf(a))) signatureChanges.Add((a, b));
                Pieces.Add(new Piece(Pieces.Count, f.Path, order++, oldRuns[i].From, oldRuns[i].Len, newRuns[j].From, newRuns[j].Len, members, oldRuns[i].Type ?? newRuns[j].Type));
                cursor = oldRuns[i].End;
                oi = i + 1;
                ni = j + 1;
            }
        }
        if (Pieces.Count - before > 1) HunksSplit++;
    }

    static List<(int, int)> Lcs(List<string> a, List<string> b)
    {
        var dp = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var result = new List<(int, int)>();
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (a[i] == b[j]) { result.Add((i, j)); i++; j++; }
            else if (dp[i + 1, j] >= dp[i, j + 1]) i++;
            else j++;
        }
        return result;
    }

    /// <summary>
    /// Groups for one graph rule: members joined by ✅/🟡 lines within <paramref name="depth"/> steps, in either
    /// direction, through unchanged points too, in the change's graph (the parent's for deleted members), plus
    /// members whose files are joined by 📜 when <paramref name="history"/> is given.
    /// </summary>
    public RuleSplit Group(string rule, int depth, IReadOnlyCollection<(string A, string B)>? history = null)
    {
        var uf = new UnionFind(Members);
        foreach (var key in Members)
        {
            var view = after.Has(key) ? after : parent;
            // A changed type's text holds its changed members: they can't be applied without it.
            if (view.ContainerType(key) is { } container && Members.Contains(container)) uf.Union(key, container);
            foreach (var other in view.Reach(key, depth))
                if (Members.Contains(other)) uf.Union(key, other);
        }
        if (history is not null)
            foreach (var (a, b) in history)
            {
                var inA = filesOfMember.Where(kv => kv.Value.Contains(a)).Select(kv => kv.Key).ToList();
                var inB = filesOfMember.Where(kv => kv.Value.Contains(b)).Select(kv => kv.Key).ToList();
                if (inA.Count == 0 || inB.Count == 0) continue;
                foreach (var k in inA.Concat(inB)) uf.Union(inA[0], k);
            }
        foreach (var (a, b) in signatureChanges) uf.Union(a, b);
        var before = uf.Count;
        // Text no rule could cut: one line owned by several members (e.g. `int a, b;`).
        foreach (var p in Pieces.Where(p => p.Members.Count > 1))
            foreach (var k in p.Members.Skip(1)) uf.Union(p.Members[0], k);
        var joined = before - uf.Count;
        return new RuleSplit(rule, [.. uf.Groups().Select(GroupOf)], joined);
    }

    SplitGroup GroupOf(IReadOnlyList<string> members)
    {
        var set = members.ToHashSet(StringComparer.Ordinal);
        var own = Pieces.Where(p => KeysOf(p).Any(set.Contains)).ToList();
        var files = own.Select(p => p.File).ToHashSet(StringComparer.Ordinal);
        // Edits outside members (usings, braces, attributes of unchanged types) go with every group in their file.
        var outside = Pieces.Where(p => !KeysOf(p).Any() && files.Contains(p.File));
        return new SplitGroup(members, [.. files.Order(StringComparer.Ordinal)], own.Concat(outside).Select(p => p.Id).ToHashSet(), SharedFiles);
    }

    /// <summary>The naive baseline: each changed file's folder is a group (whole files), with the same shared group.</summary>
    public RuleSplit ByFolder()
    {
        var groups = MemberFiles.GroupBy(f => Path.GetDirectoryName(f) ?? "", StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var files = g.ToHashSet(StringComparer.Ordinal);
                var members = filesOfMember.Where(kv => kv.Value.Overlaps(files)).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
                return new SplitGroup(members, [.. files.Order(StringComparer.Ordinal)], Pieces.Where(p => files.Contains(p.File)).Select(p => p.Id).ToHashSet(), SharedFiles);
            });
        return new RuleSplit(Folder, [.. groups], 0);
    }

    sealed class UnionFind
    {
        readonly Dictionary<string, string> parentOf = new(StringComparer.Ordinal);
        public UnionFind(IEnumerable<string> keys) { foreach (var k in keys) parentOf[k] = k; Count = parentOf.Count; }
        public int Count { get; private set; }

        string Find(string k)
        {
            while (parentOf[k] != k) k = parentOf[k] = parentOf[parentOf[k]];
            return k;
        }

        public void Union(string a, string b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (ra == rb) return;
            // The smaller key is the root, so groups come out the same whatever the order of unions.
            if (string.CompareOrdinal(ra, rb) < 0) parentOf[rb] = ra; else parentOf[ra] = rb;
            Count--;
        }

        public IEnumerable<IReadOnlyList<string>> Groups() =>
            parentOf.Keys.GroupBy(Find).Select(g => (IReadOnlyList<string>)[.. g.Order(StringComparer.Ordinal)]).OrderBy(g => g[0], StringComparer.Ordinal);
    }
}
