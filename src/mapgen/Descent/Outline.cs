namespace SourceSharp.MapGen.Descent;

/// <summary>An axis-aligned rectangle on the floor plan, room-local: x from <see cref="X0"/> to <see cref="X1"/>, y from <see cref="Y0"/> to <see cref="Y1"/>.</summary>
public readonly record struct Rect(float X0, float Y0, float X1, float Y1)
{
    public float Width => X1 - X0;

    public float Depth => Y1 - Y0;

    public float CentreX => (X0 + X1) / 2;

    public float CentreY => (Y0 + Y1) / 2;

    public bool IsEmpty => X1 <= X0 || Y1 <= Y0;

    /// <summary>Whether (x, y) is inside or on the rectangle.</summary>
    public bool Contains(float x, float y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    /// <summary>The rectangle grown by <paramref name="by"/> on every side (shrunk for a negative).</summary>
    public Rect Grow(float by) => new(X0 - by, Y0 - by, X1 + by, Y1 + by);

    /// <summary>A rectangle from its centre and its size.</summary>
    public static Rect Around(float x, float y, float width, float depth) => new(x - width / 2, y - depth / 2, x + width / 2, y + depth / 2);

    public override string ToString() => $"[{X0} {Y0}]-[{X1} {Y1}]";
}

/// <summary>
/// Floor-plan arithmetic on rectangles: what is left of a rectangle when
/// others are taken out of it, as few rectangles as a greedy merge gives.
/// The shell of an outlined room (<see cref="RoomBuilder.WithOutline"/>) is
/// the cell less its open floor, cut this way into wall boxes.
/// </summary>
public static class Rects
{
    /// <summary>
    /// <paramref name="outer"/> less every one of <paramref name="holes"/>,
    /// as disjoint rectangles: the plan is cut on every edge any rectangle
    /// has, each strip's solid runs are merged along x, and runs of the same
    /// extent in neighbouring strips are merged along y.
    /// </summary>
    public static IReadOnlyList<Rect> Subtract(Rect outer, IEnumerable<Rect> holes)
    {
        var cut = holes.Select(h => new Rect(Math.Max(h.X0, outer.X0), Math.Max(h.Y0, outer.Y0), Math.Min(h.X1, outer.X1), Math.Min(h.Y1, outer.Y1)))
            .Where(h => !h.IsEmpty).ToArray();
        float[] xs = [.. cut.SelectMany(h => new[] { h.X0, h.X1 }).Append(outer.X0).Append(outer.X1).Distinct().Order()];
        float[] ys = [.. cut.SelectMany(h => new[] { h.Y0, h.Y1 }).Append(outer.Y0).Append(outer.Y1).Distinct().Order()];

        // Each y strip's solid runs along x.
        var open = new List<Rect>();
        for (int j = 0; j + 1 < ys.Length; j++)
        {
            float cy = (ys[j] + ys[j + 1]) / 2;
            int start = -1;
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                float cx = (xs[i] + xs[i + 1]) / 2;
                bool solid = !cut.Any(h => cx > h.X0 && cx < h.X1 && cy > h.Y0 && cy < h.Y1);
                if (solid && start < 0)
                    start = i;
                if (!solid && start >= 0)
                {
                    open.Add(new Rect(xs[start], ys[j], xs[i], ys[j + 1]));
                    start = -1;
                }
            }

            if (start >= 0)
                open.Add(new Rect(xs[start], ys[j], xs[^1], ys[j + 1]));
        }

        // Runs of one x extent in touching strips become one.
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int a = 0; a < open.Count && !merged; a++)
                for (int b = 0; b < open.Count && !merged; b++)
                    if (a != b && open[a].X0 == open[b].X0 && open[a].X1 == open[b].X1 && open[a].Y1 == open[b].Y0)
                    {
                        open[a] = open[a] with { Y1 = open[b].Y1 };
                        open.RemoveAt(b);
                        merged = true;
                    }
        }

        return open;
    }

    /// <summary>Whether every point of <paramref name="r"/> lies in the union of <paramref name="cover"/>.</summary>
    public static bool Covers(IEnumerable<Rect> cover, Rect r) => r.IsEmpty || Subtract(r, cover).Count == 0;
}
