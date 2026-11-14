namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// A room's solid brushes (its world, plugs included, and its brush
/// entities that stay solid) and exact questions about them: how far a
/// level ray from a point runs before it meets a wall, where the ceiling
/// and the floor are over a point, and whether a box stands in a brush.
/// The dresser and its lint ask these of any shape, so they work on the
/// brushes, not on what a shape says of itself.
/// </summary>
public sealed class RoomSolids
{
    private readonly Brush[] solids;

    public RoomSolids(Room room, IEnumerable<Brush>? extra = null)
    {
        Room = room;
        solids = [.. room.World,
                  .. room.Entities.Where(e => e.ClassName == "func_brush" && e.Key("Solidity") != "1").SelectMany(e => e.Brushes),
                  .. extra ?? []];
    }

    /// <summary>The room the solids are of.</summary>
    public Room Room { get; }

    /// <summary>Every solid brush.</summary>
    public IReadOnlyList<Brush> Brushes => solids;

    /// <summary>
    /// How far a ray from <paramref name="from"/> along a level axis
    /// (<paramref name="dx"/>, <paramref name="dy"/> one of ±1, 0) runs in
    /// open air before it enters a brush: infinity when it meets none, null
    /// when it starts inside one.
    /// </summary>
    public float? Ray(Point from, int dx, int dy)
    {
        float best = float.MaxValue;
        foreach (Brush b in solids)
        {
            float enter = float.NegativeInfinity, exit = float.PositiveInfinity;
            bool miss = false;
            foreach (BrushFace f in b.Faces)
            {
                Point n = f.Normal;
                float nd = n.X * dx + n.Y * dy, rest = f.Distance - BrushFace.Dot(n, from);
                if (Math.Abs(nd) < 1e-6f)
                {
                    if (rest < Brush.Epsilon)
                    {
                        miss = true;
                        break;
                    }

                    continue;
                }

                float t = rest / nd;
                if (nd > 0)
                    exit = Math.Min(exit, t);
                else
                    enter = Math.Max(enter, t);
            }

            if (miss || exit - enter <= Brush.Epsilon || exit <= Brush.Epsilon)
                continue;
            if (enter <= Brush.Epsilon)
                return null;
            best = Math.Min(best, enter);
        }

        return best == float.MaxValue ? float.PositiveInfinity : best;
    }

    /// <summary>The underside of the lowest brush above <paramref name="z"/> over (x, y): the ceiling there, or null.</summary>
    public float? CeilingAbove(float x, float y, float z)
    {
        float? best = null;
        foreach (Brush b in solids)
            if (b.Mins.X < x && b.Maxs.X > x && b.Mins.Y < y && b.Maxs.Y > y && b.Column(x, y) is { } c && c.Low >= z - Brush.Epsilon)
                best = best is { } v ? Math.Min(v, c.Low) : c.Low;
        return best;
    }

    /// <summary>The top of the highest brush at or below <paramref name="z"/> over (x, y): the floor there, or null.</summary>
    public float? FloorBelow(float x, float y, float z)
    {
        float? best = null;
        foreach (Brush b in solids)
            if (b.Mins.X < x && b.Maxs.X > x && b.Mins.Y < y && b.Maxs.Y > y && b.Column(x, y) is { } c && c.High <= z + Brush.Epsilon)
                best = best is { } v ? Math.Max(v, c.High) : c.High;
        return best;
    }

    /// <summary>Whether the box stands in any brush (touching is not standing in; half a unit of contact is allowed for rounding).</summary>
    public bool Blocked(Point mins, Point maxs)
    {
        Point lo = mins + new Point(0.5f, 0.5f, 0.5f), hi = maxs - new Point(0.5f, 0.5f, 0.5f);
        return solids.Any(b => b.Overlaps(lo, hi));
    }

    /// <summary>Whether the box, grown by <paramref name="by"/> on every side, meets a brush: something holds it up or against a wall.</summary>
    public bool Touches(Point mins, Point maxs, float by = 2)
    {
        Point g = new(by, by, by);
        return solids.Any(b => b.Overlaps(mins - g, maxs + g));
    }
}

/// <summary>An axis-aligned box, and the distances the dresser's rules are measured by.</summary>
public readonly record struct Box(Point Mins, Point Maxs)
{
    public Point Centre => new((Mins.X + Maxs.X) / 2, (Mins.Y + Maxs.Y) / 2, (Mins.Z + Maxs.Z) / 2);

    /// <summary>The shortest distance between this box and <paramref name="other"/> (0 when they meet).</summary>
    public float DistanceTo(Box other)
    {
        float dx = Math.Max(0, Math.Max(other.Mins.X - Maxs.X, Mins.X - other.Maxs.X));
        float dy = Math.Max(0, Math.Max(other.Mins.Y - Maxs.Y, Mins.Y - other.Maxs.Y));
        float dz = Math.Max(0, Math.Max(other.Mins.Z - Maxs.Z, Mins.Z - other.Maxs.Z));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Whether the boxes share volume.</summary>
    public bool Overlaps(Box other) =>
        Mins.X < other.Maxs.X && other.Mins.X < Maxs.X && Mins.Y < other.Maxs.Y && other.Mins.Y < Maxs.Y && Mins.Z < other.Maxs.Z && other.Mins.Z < Maxs.Z;

    /// <summary>The level distance from the box to a point (0 inside its footprint).</summary>
    public float LevelDistanceTo(float x, float y)
    {
        float dx = Math.Max(0, Math.Max(Mins.X - x, x - Maxs.X)), dy = Math.Max(0, Math.Max(Mins.Y - y, y - Maxs.Y));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The box of a door's opening on its wall face, room-local: the gap a player walks through.</summary>
    public static Box DoorOpening(RoomKit kit, CellFace face) => DoorOpening(kit, face, 0, 1);

    /// <summary>The box of a socket's opening on its wall face in a room <paramref name="cells"/> a side.</summary>
    public static Box DoorOpening(RoomKit kit, CellFace face, int offset, int cells)
    {
        var (mins, maxs) = kit.PlugBox(face, offset, cells);
        float c = cells * kit.Cell, t = kit.WallDepth;
        // The plug's room-side face.
        return face switch
        {
            CellFace.East => new(mins with { X = c - t }, maxs with { X = c - t }),
            CellFace.West => new(mins with { X = t }, maxs with { X = t }),
            CellFace.North => new(mins with { Y = c - t }, maxs with { Y = c - t }),
            _ => new(mins with { Y = t }, maxs with { Y = t }),
        };
    }
}
