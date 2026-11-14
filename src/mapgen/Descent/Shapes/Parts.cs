namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>A corner of a rectangle on the floor plan.</summary>
public enum Corner
{
    SouthWest,
    SouthEast,
    NorthEast,
    NorthWest,
}

/// <summary>
/// Pieces the shape families of plan §5 share: door sets, door necks, the
/// lamps by height class, markers on the floor, and the brushes that bend an
/// outline off the square (a diagonal cut across a corner, a rounded corner,
/// a round pillar). Every size is from the kit or the family; every jump
/// height from <see cref="JumpTable"/>.
/// </summary>
public static class Parts
{
    /// <summary>A low room's lamp: the default's colour, dimmer, reaching 512 (half at 256).</summary>
    public static Lamp ConnectorLamp { get; } = Lamp.Default with { Brightness = 45 };

    /// <summary>A chamber's lamp: reaching a 512 room's floor from under its ceiling.</summary>
    public static Lamp ChamberLamp { get; } = Lamp.Default with { Brightness = 80, FiftyPercent = 320, ZeroPercent = 900 };

    /// <summary>A hall's lamp, hung about 448 up.</summary>
    public static Lamp HallLamp { get; } = Lamp.Default with { Brightness = 100, FiftyPercent = 384, ZeroPercent = 1024 };

    /// <summary>A lamp hung high in a shaft over its ledges.</summary>
    public static Lamp HighLamp { get; } = HallLamp with { Brightness = 70 };

    /// <summary>Where a low room's lamps hang, above its floor.</summary>
    public const int ConnectorLampAbove = 168;

    /// <summary>Where a chamber's lamps hang, above its floor.</summary>
    public const int ChamberLampAbove = 384;

    /// <summary>Where a hall's lamps hang, above its floor.</summary>
    public const int HallLampAbove = 448;

    public static CellFace[] E => [CellFace.East];

    public static CellFace[] WE => [CellFace.West, CellFace.East];

    public static CellFace[] NE => [CellFace.North, CellFace.East];

    public static CellFace[] NEW => [CellFace.North, CellFace.East, CellFace.West];

    public static CellFace[] WES => [CellFace.West, CellFace.East, CellFace.South];

    public static CellFace[] NESW => [CellFace.North, CellFace.East, CellFace.South, CellFace.West];

    /// <summary>
    /// A door's neck: open floor <paramref name="width"/> wide on the door's
    /// axis, from the wall's inner face <paramref name="length"/> into the room.
    /// </summary>
    public static Rect Neck(RoomKit kit, CellFace face, float width, float length)
    {
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f, w = width / 2f;
        return face switch
        {
            CellFace.East => new(c - t - length, m - w, c - t, m + w),
            CellFace.West => new(t, m - w, t + length, m + w),
            CellFace.North => new(m - w, c - t - length, m + w, c - t),
            _ => new(m - w, t, m + w, t + length),
        };
    }

    /// <summary>The point on a door's axis <paramref name="inset"/> in from the wall's inner face, on the floor.</summary>
    public static Point DoorPoint(RoomKit kit, CellFace face, float inset)
    {
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f, f = kit.Sill;
        return face switch
        {
            CellFace.East => new(c - t - inset, m, f),
            CellFace.West => new(t + inset, m, f),
            CellFace.North => new(m, c - t - inset, f),
            _ => new(m, t + inset, f),
        };
    }

    /// <summary>A lamp at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="above"/> the floor.</summary>
    public static RoomBuilder LampAt(this RoomBuilder b, float x, float y, float above, Lamp lamp)
        => b.WithLamp(new Point(x, y, b.Floor + above), lamp);

    /// <summary>
    /// Lamps over the way from <paramref name="from"/> to <paramref name="to"/>
    /// (floor points), no further apart than <paramref name="spacing"/>, the
    /// first and last half a step in: the path is lit (§6).
    /// </summary>
    public static RoomBuilder LampsAlong(this RoomBuilder b, Point from, Point to, float spacing, float above, Lamp lamp)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, length = MathF.Sqrt(dx * dx + dy * dy);
        int n = Math.Max(1, (int)MathF.Ceiling(length / spacing));
        for (int i = 0; i < n; i++)
        {
            float k = (i + 0.5f) / n;
            b.LampAt(MathF.Round(from.X + dx * k), MathF.Round(from.Y + dy * k), above, lamp);
        }

        return b;
    }

    /// <summary>A lamp over each door's way in, from the door to <paramref name="hub"/>.</summary>
    public static RoomBuilder LampsFromDoors(this RoomBuilder b, Point hub, float spacing, float above, Lamp lamp)
    {
        foreach (CellFace face in b.DoorFaces)
            b.LampsAlong(DoorPoint(b.Kit, face, 0), hub, spacing, above, lamp);
        return b;
    }

    /// <summary>A pack spawn on the floor at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static RoomBuilder Pack(this RoomBuilder b, float x, float y, int radius = 64)
        => b.WithMarker(MarkerKind.PackSpawn, new Point(x, y, b.Floor), radius: radius);

    /// <summary>A marker at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="above"/> the floor.</summary>
    public static RoomBuilder Mark(this RoomBuilder b, MarkerKind kind, float x, float y, float above = 0, int yaw = 0)
        => b.WithMarker(kind, new Point(x, y, b.Floor + above), yaw);

    /// <summary>A triangle's footprint, wound counter-clockwise whatever order it is given in.</summary>
    public static (float X, float Y)[] Ccw((float X, float Y) a, (float X, float Y) b, (float X, float Y) c)
    {
        float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return cross > 0 ? [a, b, c] : [a, c, b];
    }

    /// <summary>A diagonal cut across <paramref name="r"/>'s <paramref name="corner"/>: a solid triangle with legs of <paramref name="size"/>, wall high.</summary>
    public static Brush Chamfer(this RoomBuilder b, Rect r, Corner corner, float size, Surface? surface = null)
    {
        var (cx, cy, sx, sy) = CornerOf(r, corner);
        return Brush.Column(Ccw((cx, cy), (cx + sx * size, cy), (cx, cy + sy * size)), b.Floor, b.CeilingUnderside, surface ?? b.Theme.Wall);
    }

    /// <summary>
    /// A rounded corner: the solid between <paramref name="r"/>'s corner and
    /// a quarter circle of <paramref name="radius"/>, as a fan of
    /// <paramref name="segments"/> triangles from the corner.
    /// </summary>
    public static IEnumerable<Brush> RoundCorner(this RoomBuilder b, Rect r, Corner corner, float radius, int segments = 3, Surface? surface = null)
    {
        var (cx, cy, sx, sy) = CornerOf(r, corner);
        float ox = cx + sx * radius, oy = cy + sy * radius;
        var arc = new List<(float X, float Y)>();
        for (int i = 0; i <= segments; i++)
        {
            double a = Math.PI / 2 * i / segments;
            arc.Add(((float)Math.Round(ox - sx * radius * Math.Cos(a)), (float)Math.Round(oy - sy * radius * Math.Sin(a))));
        }

        for (int i = 0; i < segments; i++)
            yield return Brush.Column(Ccw((cx, cy), arc[i], arc[i + 1]), b.Floor, b.CeilingUnderside, surface ?? b.Theme.Wall);
    }

    /// <summary>All four corners of <paramref name="r"/> rounded.</summary>
    public static RoomBuilder RoundCorners(this RoomBuilder b, Rect r, float radius, int segments = 3)
    {
        foreach (Corner corner in Enum.GetValues<Corner>())
            b.WithBrushes(b.RoundCorner(r, corner, radius, segments));
        return b;
    }

    /// <summary>The corners of <paramref name="r"/> listed cut diagonally by <paramref name="size"/>.</summary>
    public static RoomBuilder Chamfers(this RoomBuilder b, Rect r, float size, params Corner[] corners)
    {
        foreach (Corner corner in corners.Length == 0 ? Enum.GetValues<Corner>() : corners)
            b.WithBrush(b.Chamfer(r, corner, size));
        return b;
    }

    /// <summary>A solid column of the wall, floor to ceiling, over a convex footprint (any winding).</summary>
    public static RoomBuilder Solid(this RoomBuilder b, params (float X, float Y)[] footprint)
    {
        var points = footprint;
        double area = 0;
        for (int i = 0; i < points.Length; i++)
            area += (double)points[i].X * points[(i + 1) % points.Length].Y - (double)points[(i + 1) % points.Length].X * points[i].Y;
        if (area < 0)
            points = [.. points.Reverse()];
        return b.WithBrush(Brush.Column(points, b.Floor, b.CeilingUnderside, b.Theme.Wall));
    }

    /// <summary>The height of a wall from the floor's top to the ceiling's underside, in a room <paramref name="height"/> high.</summary>
    public static float Wall(RoomKit kit, int height) => height - 2 * kit.WallDepth;

    /// <summary>A round pillar, floor to ceiling, in the theme's block.</summary>
    public static RoomBuilder Pillar(this RoomBuilder b, float x, float y, float radius, int sides = 8)
        => b.WithBrush(Brush.Prism(x, y, radius, sides, b.Floor, b.CeilingUnderside, b.Theme.Block, startDegrees: 180f / sides));

    /// <summary>A square post, floor to ceiling, in the theme's block.</summary>
    public static RoomBuilder Post(this RoomBuilder b, float x, float y, float half)
        => b.WithBox(new Point(x - half, y - half, b.Floor), new Point(x + half, y + half, b.CeilingUnderside));

    /// <summary>A box from the floor up <paramref name="height"/> over <paramref name="r"/>.</summary>
    public static RoomBuilder Block(this RoomBuilder b, Rect r, float height, Surface? surface = null)
        => b.WithBox(new Point(r.X0, r.Y0, b.Floor), new Point(r.X1, r.Y1, b.Floor + height), surface);

    /// <summary>
    /// A flight of stairs over <paramref name="r"/> rising toward
    /// <paramref name="rises"/> from the floor to <paramref name="height"/>,
    /// 16 a step: everyone walks it (T0).
    /// </summary>
    public static RoomBuilder StairsUp(this RoomBuilder b, Rect r, Compass rises, int height)
    {
        if (height % 16 != 0)
            throw new ArgumentException($"stairs to {height} are not whole 16-unit steps");
        return b.WithBrushes(Brush.Stairs(new Point(r.X0, r.Y0, b.Floor), new Point(r.X1, r.Y1, b.Floor + height), rises, height / 16, b.Theme.Block));
    }

    /// <summary>
    /// A flight of stairs over <paramref name="r"/> rising toward
    /// <paramref name="rises"/> from <paramref name="from"/> to
    /// <paramref name="to"/> above the floor (a terrace to the next), 16 a step.
    /// </summary>
    public static RoomBuilder StairsBetween(this RoomBuilder b, Rect r, Compass rises, int from, int to)
    {
        if ((to - from) % 16 != 0 || to <= from)
            throw new ArgumentException($"stairs from {from} to {to} are not whole 16-unit steps");
        return b.WithBrushes(Brush.Stairs(new Point(r.X0, r.Y0, b.Floor + from), new Point(r.X1, r.Y1, b.Floor + to), rises, (to - from) / 16, b.Theme.Block));
    }

    /// <summary>
    /// The corner point of <paramref name="r"/> and the signs that point from
    /// it into the rectangle along x and y.
    /// </summary>
    public static (float X, float Y, float Sx, float Sy) CornerOf(Rect r, Corner corner) => corner switch
    {
        Corner.SouthWest => (r.X0, r.Y0, 1, 1),
        Corner.SouthEast => (r.X1, r.Y0, -1, 1),
        Corner.NorthEast => (r.X1, r.Y1, -1, -1),
        _ => (r.X0, r.Y1, 1, -1),
    };

    /// <summary>A low room's entry: tagged, low, every depth unless narrowed.</summary>
    public static RoomEntry Low(string tag, int weight = 1, int minDepth = 1, int maxDepth = LibraryManifestWriter.Depths, params string[] more)
        => new([tag, .. more], Weight: weight, MinDepth: minDepth, MaxDepth: maxDepth, Low: true);
}
