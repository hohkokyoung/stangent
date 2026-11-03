using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;

namespace Stangent.Spec.Fsd;

/// <summary>A cell of a grid read from drawn lines; a merged cell has its top-left place in the grid.</summary>
sealed class PdfCell(int row, int col, double left, double bottom, double right, double top)
{
    public int Row { get; } = row;
    public int Col { get; } = col;
    public double Left { get; } = left;
    public double Bottom { get; } = bottom;
    public double Right { get; } = right;
    public double Top { get; } = top;
    public List<PdfWord> Words { get; } = [];
    public List<string> Images { get; } = [];

    public bool Holds(double x, double y) => x >= Left && x <= Right && y >= Bottom && y <= Top;
    public string Text => string.Join(" ", Words.Select(w => w.Text));
}

/// <summary>A grid of closed cells, and the full extent of the rules drawn around it: a word in there but in no cell means the grid is incomplete.</summary>
sealed record PdfTable(int Rows, int Cols, List<PdfCell> Cells, double Left, double Bottom, double Right, double Top, int Group)
{
    public bool Spans(double x, double y) => x >= Left && x <= Right && y >= Bottom && y <= Top;
}

/// <summary>One drawn rule, and which touching group of rules it's in.</summary>
readonly record struct PdfRule(bool Horizontal, double At, double From, double To, int Group);

/// <summary>What a page's lines hold: grids that may be tables, grids that can't be, and every other rule, which may still suggest a table.</summary>
sealed record PdfGrids(List<PdfTable> Tables, List<string> NotTables, List<PdfRule> Rules);

/// <summary>
/// Finds tables from the lines a page draws (D111): strokes, thin rectangles and rectangle edges are snapped into rules, the
/// rules make a grid, and a cell is a grid region closed on all four sides. It only sees geometry: what goes in the cells,
/// and whether it can be trusted, is decided by <see cref="PdfConverter"/>.
/// </summary>
static class PdfTables
{
    /// <summary>Rules closer than this share of the text size are one rule: a thin rectangle's two edges, a double border.</summary>
    const double SnapShare = 0.25;

    /// <summary>A cell taking over this share of the page is a layout frame around the text, not a table cell.</summary>
    const double FrameShare = 0.5;

    readonly record struct Rule(double At, double From, double To);

    /// <param name="map">Turns the page's coordinates to those its text reads in, when its text runs in another direction.</param>
    public static PdfGrids Find(IReadOnlyList<PdfPath> paths, double pageWidth, double pageHeight, double textSize, Func<PdfPoint, PdfPoint>? map = null)
    {
        var tolerance = textSize * SnapShare;
        var (horizontal, vertical) = Rules(paths, tolerance, map ?? (point => point));
        var tables = new List<PdfTable>();
        var notTables = new List<string>();

        // Rules that touch form one candidate grid.
        var parent = Enumerable.Range(0, horizontal.Count + vertical.Count).ToArray();
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (var h = 0; h < horizontal.Count; h++)
            for (var v = 0; v < vertical.Count; v++)
                if (vertical[v].At >= horizontal[h].From - tolerance && vertical[v].At <= horizontal[h].To + tolerance
                    && horizontal[h].At >= vertical[v].From - tolerance && horizontal[h].At <= vertical[v].To + tolerance)
                    parent[Root(h)] = Root(horizontal.Count + v);

        var rules = new List<PdfRule>();
        foreach (var group in Enumerable.Range(0, parent.Length).GroupBy(Root).OrderBy(g => g.Min()))
        {
            var hs = group.Where(i => i < horizontal.Count).Select(i => horizontal[i]).ToList();
            var vs = group.Where(i => i >= horizontal.Count).Select(i => vertical[i - horizontal.Count]).ToList();
            rules.AddRange(hs.Select(h => new PdfRule(true, h.At, h.From, h.To, group.Key)));
            rules.AddRange(vs.Select(v => new PdfRule(false, v.At, v.From, v.To, group.Key)));
            if (hs.Count >= 2 && vs.Count >= 2) tables.AddRange(Grid(hs, vs, tolerance, group.Key));
        }

        var pageArea = pageWidth * pageHeight;
        var frames = tables.Where(t => t.Cells.Any(c => (c.Right - c.Left) * (c.Top - c.Bottom) >= pageArea * FrameShare)).ToList();
        // A cell with a whole table inside it encloses content: it isn't a cell, so its table isn't one either.
        var enclosing = tables.Where(t => tables.Any(o => !ReferenceEquals(o, t) && t.Cells.Any(c => Encloses(c, o)))).ToList();
        foreach (var t in frames.Union(enclosing).Where(t => t.Rows >= 2 && t.Cols >= 2))
            notTables.Add($"a drawn grid of {t.Rows} rows and {t.Cols} columns frames the page or another table, so it isn't read as a table");
        var said = frames.Union(enclosing).Select(t => t.Group).ToHashSet();
        return new PdfGrids([.. tables.Except(frames).Except(enclosing)], notTables, [.. rules.Where(r => !said.Contains(r.Group))]);
    }

    static bool Encloses(PdfCell cell, PdfTable other) =>
        other.Cells.All(o => o.Left >= cell.Left && o.Right <= cell.Right && o.Bottom >= cell.Bottom && o.Top <= cell.Top);

    /// <summary>Every straight axis-aligned stretch the page draws, snapped and merged. Clipping paths draw nothing, and fills only count when thin enough to be rules.</summary>
    static (List<Rule> Horizontal, List<Rule> Vertical) Rules(IReadOnlyList<PdfPath> paths, double tolerance, Func<PdfPoint, PdfPoint> map)
    {
        var hs = new List<Rule>();
        var vs = new List<Rule>();
        void Add(PdfPoint from, PdfPoint to)
        {
            var (a, b) = (map(from), map(to));
            var (dx, dy) = (Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            if (dy <= tolerance && dx > tolerance) hs.Add(new Rule((a.Y + b.Y) / 2, Math.Min(a.X, b.X), Math.Max(a.X, b.X)));
            else if (dx <= tolerance && dy > tolerance) vs.Add(new Rule((a.X + b.X) / 2, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
        }
        foreach (var path in paths)
        {
            if (path.IsClipping || !(path.IsFilled || path.IsStroked)) continue;
            foreach (var sub in path)
            {
                // A fill thicker than a rule is shading behind text: its edges aren't lines anyone drew.
                if (!path.IsStroked && sub.GetBoundingRectangle() is { } box && box.Width > tolerance && box.Height > tolerance) continue;
                PdfPoint? start = null, at = null;
                foreach (var command in sub.Commands)
                    switch (command)
                    {
                        case PdfSubpath.Move m: start = at = m.Location; break;
                        case PdfSubpath.Line l: Add(l.From, l.To); start ??= l.From; at = l.To; break;
                        case PdfSubpath.BezierCurve b: at = b.EndPoint; break;
                        case PdfSubpath.Close when start is { } s && at is { } e: Add(e, s); at = s; break;
                    }
            }
        }
        return (Merge(hs, tolerance), Merge(vs, tolerance));
    }

    static List<Rule> Merge(List<Rule> rules, double tolerance)
    {
        var merged = new List<Rule>();
        var sorted = rules.OrderBy(r => r.At).ThenBy(r => r.From).ThenBy(r => r.To).ToList();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i + 1;
            while (j < sorted.Count && sorted[j].At - sorted[j - 1].At <= tolerance) j++;
            var cluster = sorted.GetRange(i, j - i).OrderBy(r => r.From).ThenBy(r => r.To).ToList();
            var at = cluster.Average(r => r.At);
            var (from, to) = (cluster[0].From, cluster[0].To);
            foreach (var r in cluster.Skip(1))
            {
                if (r.From <= to + tolerance) to = Math.Max(to, r.To);
                else { merged.Add(new Rule(at, from, to)); (from, to) = (r.From, r.To); }
            }
            merged.Add(new Rule(at, from, to));
            i = j;
        }
        return merged;
    }

    /// <summary>
    /// Splits one grid's area at its rules and joins neighbouring pieces where no rule separates them. A joined region that's a
    /// full rectangle and closed all round is a cell, so merged cells come out whole. Anything else (the ring between a frame
    /// and a table inside it, an open edge) isn't a cell.
    /// </summary>
    static IEnumerable<PdfTable> Grid(List<Rule> hs, List<Rule> vs, double tolerance, int groupId)
    {
        var xs = vs.Select(v => v.At).Distinct().Order().ToArray();
        var ys = hs.Select(h => h.At).Distinct().OrderDescending().ToArray();
        if (xs.Length < 2 || ys.Length < 2) yield break;
        var (rows, cols) = (ys.Length - 1, xs.Length - 1);
        var (extentLeft, extentRight) = (hs.Min(h => h.From), hs.Max(h => h.To));
        var (extentBottom, extentTop) = (vs.Min(v => v.From), vs.Max(v => v.To));
        bool HasH(int y, int col) => hs.Any(h => h.At == ys[y] && h.From <= xs[col] + tolerance && h.To >= xs[col + 1] - tolerance);
        bool HasV(int x, int row) => vs.Any(v => v.At == xs[x] && v.From <= ys[row + 1] + tolerance && v.To >= ys[row] - tolerance);

        var parent = Enumerable.Range(0, rows * cols).ToArray();
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var open = new bool[rows * cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var atom = r * cols + c;
                if (c + 1 < cols && !HasV(c + 1, r)) parent[Root(atom)] = Root(atom + 1);
                if (r + 1 < rows && !HasH(r + 1, c)) parent[Root(atom)] = Root(atom + cols);
                if ((c == 0 && !HasV(0, r)) || (c == cols - 1 && !HasV(cols, r)) || (r == 0 && !HasH(0, c)) || (r == rows - 1 && !HasH(rows, c))) open[atom] = true;
            }

        var cells = new List<(int Top, int Left, int Bottom, int Right)>();
        foreach (var region in Enumerable.Range(0, rows * cols).GroupBy(Root).OrderBy(g => g.Min()))
        {
            var (top, bottom) = (region.Min(a => a / cols), region.Max(a => a / cols));
            var (left, right) = (region.Min(a => a % cols), region.Max(a => a % cols));
            if (region.Count() == (bottom - top + 1) * (right - left + 1) && !region.Any(a => open[a]))
                cells.Add((top, left, bottom + 1, right + 1));
        }

        // Cells that share a corner are one table.
        var joined = Enumerable.Range(0, cells.Count).ToArray();
        int Join(int x) { while (joined[x] != x) x = joined[x] = joined[joined[x]]; return x; }
        var corners = new Dictionary<(int Row, int Col), int>();
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            foreach (var corner in new[] { (c.Top, c.Left), (c.Top, c.Right), (c.Bottom, c.Left), (c.Bottom, c.Right) })
                if (corners.TryGetValue(corner, out var other)) joined[Join(i)] = Join(other);
                else corners[corner] = i;
        }
        foreach (var group in Enumerable.Range(0, cells.Count).GroupBy(Join).OrderBy(g => g.Min()))
        {
            // Columns and rows are only those this table's own cells use.
            var used = group.Select(i => cells[i]).ToList();
            var colEdges = used.SelectMany(c => new[] { c.Left, c.Right }).Distinct().Order().ToList();
            var rowEdges = used.SelectMany(c => new[] { c.Top, c.Bottom }).Distinct().Order().ToList();
            yield return new PdfTable(rowEdges.Count - 1, colEdges.Count - 1, [.. used
                .OrderBy(c => c.Top).ThenBy(c => c.Left)
                .Select(c => new PdfCell(rowEdges.IndexOf(c.Top), colEdges.IndexOf(c.Left), xs[c.Left], ys[c.Bottom], xs[c.Right], ys[c.Top]))],
                extentLeft, extentBottom, extentRight, extentTop, groupId);
        }
    }
}
