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
    public static Lamp ChamberLamp { get; } = Lamp.Default with { Brightness = 70, FiftyPercent = 320, ZeroPercent = 900 };

    /// <summary>A hall's lamp, hung about 448 up.</summary>
    public static Lamp HallLamp { get; } = Lamp.Default with { Brightness = 85, FiftyPercent = 384, ZeroPercent = 1024 };

    /// <summary>A lamp hung high in a shaft over its ledges.</summary>
    public static Lamp HighLamp { get; } = HallLamp with { Brightness = 70 };

    /// <summary>Where a low room's lamps hang, above its floor.</summary>
    public const int ConnectorLampAbove = 168;

    /// <summary>Where a chamber's lamps hang, above its floor.</summary>
    public const int ChamberLampAbove = 320;

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

    /// <summary>
    /// The cells a side a room of the normal kit's size covers on <paramref name="kit"/>: 1 on the
    /// normal kit, 2 on level 5's 512 kit (brief p18: the maze level's other rooms keep their size and
    /// become rooms of 2 by 2 cells).
    /// </summary>
    public static int SpanCells(RoomKit kit) => Math.Max(1, RoomKit.NormalCell / kit.Cell);

    /// <summary>The extent of a room of the normal kit's size on <paramref name="kit"/>: <see cref="SpanCells"/> cells.</summary>
    public static float Span(RoomKit kit) => SpanCells(kit) * kit.Cell;

    /// <summary>
    /// Gives a room of the normal kit's size its doors on <paramref name="doors"/>: on the normal kit one
    /// door a wall; on a smaller cell (level 5's 512) a room of <see cref="SpanCells"/> cells a side with
    /// a socket on the first cell edge of each wall (the south's on east and west, the west's on north and
    /// south), so a row of such rooms meets face to face.
    /// </summary>
    public static RoomBuilder Seat(this RoomBuilder b, params CellFace[] doors)
    {
        int n = SpanCells(b.Kit);
        return n == 1 ? b.WithDoors(doors) : b.WithFootprint(n).WithSockets([.. doors.Select(f => (f, 0))]);
    }

    /// <summary>The door axis of <paramref name="b"/>'s socket on <paramref name="face"/>: its first socket's middle along the wall.</summary>
    private static float AxisOf(RoomBuilder b, CellFace face) =>
        b.Kit.Cell / 2f + b.Kit.Cell * b.Sockets.Where(s => s.Face == face).Select(s => s.Offset).DefaultIfEmpty(0).First();

    /// <summary>A door's neck as <see cref="Neck(RoomKit, CellFace, float, float)"/>, in <paramref name="b"/>'s own box and on its socket's axis (a larger room's).</summary>
    public static Rect Neck(RoomBuilder b, CellFace face, float width, float length)
    {
        float c = b.Extent, t = b.Kit.WallDepth, m = AxisOf(b, face), w = width / 2f;
        return face switch
        {
            CellFace.East => new(c - t - length, m - w, c - t, m + w),
            CellFace.West => new(t, m - w, t + length, m + w),
            CellFace.North => new(m - w, c - t - length, m + w, c - t),
            _ => new(m - w, t, m + w, t + length),
        };
    }

    /// <summary>A door's point as <see cref="DoorPoint(RoomKit, CellFace, float)"/>, in <paramref name="b"/>'s own box and on its socket's axis.</summary>
    public static Point DoorPoint(RoomBuilder b, CellFace face, float inset)
    {
        float c = b.Extent, t = b.Kit.WallDepth, m = AxisOf(b, face), f = b.Kit.Sill;
        return face switch
        {
            CellFace.East => new(c - t - inset, m, f),
            CellFace.West => new(t + inset, m, f),
            CellFace.North => new(m, c - t - inset, f),
            _ => new(m, t + inset, f),
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
            b.LampsAlong(DoorPoint(b, face, 0), hub, spacing, above, lamp);
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
        => b.WithBrush(b.Theme.SquarePillars
            // A square pier no wider than the round pillar's flats (the inscribed square of its circle).
            ? Brush.Box(new(x - MathF.Round(radius * 0.7f), y - MathF.Round(radius * 0.7f), b.Floor),
                new(x + MathF.Round(radius * 0.7f), y + MathF.Round(radius * 0.7f), b.CeilingUnderside), b.Theme.Block)
            : Brush.Prism(x, y, radius, sides, b.Floor, b.CeilingUnderside, b.Theme.Block, startDegrees: 180f / sides));

    /// <summary>A square post, floor to ceiling, in the theme's block.</summary>
    public static RoomBuilder Post(this RoomBuilder b, float x, float y, float half)
        => b.WithBox(new Point(x - half, y - half, b.Floor), new Point(x + half, y + half, b.CeilingUnderside));

    /// <summary>
    /// A lump of rock from <paramref name="z0"/> to <paramref name="z1"/> inside the box (<paramref name="cx"/> ±
    /// <paramref name="rx"/>, <paramref name="cy"/> ± <paramref name="ry"/>): its footprint an uneven convex polygon
    /// (its corners on the ellipse at angles the seed turns), its top that footprint drawn in by half toward a point off
    /// its middle, so each side leans in by its own amount and it reads as broken rock, not a block (brief p15: the
    /// Bore's cover and rubble were plain cubes). Every corner is on a whole unit and each side's four corners lie in
    /// one plane (the top's edges are the footprint's, halved).
    /// </summary>
    public static Brush Rock(float cx, float cy, float rx, float ry, float z0, float z1, Surface surface, RoomSeed rng, int sides = 7)
    {
        if (sides < 5 || rx < 16 || ry < 16 || z1 <= z0)
            throw new ArgumentException($"a rock needs five sides, 16 of radius and a height ({sides}, {rx} by {ry}, {z0} to {z1})");
        float x = MathF.Round(cx), y = MathF.Round(cy), turn = rng.Int(0, 360);
        // Even offsets from the middle, so the halved top lands on whole units.
        static float Even(float v) => 2 * MathF.Round(v / 2);
        var foot = new (float X, float Y)[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = (turn + 360f * (i + rng.Int(-30, 31) / 100f) / sides) * MathF.PI / 180;
            foot[i] = (Even(rx * MathF.Cos(a)), Even(ry * MathF.Sin(a)));
        }

        // The top's middle, off the footprint's by up to a fifth of each radius.
        float ox = MathF.Round(rx * rng.Int(-20, 21) / 100f), oy = MathF.Round(ry * rng.Int(-20, 21) / 100f);
        Point Low((float X, float Y) p) => new(x + p.X, y + p.Y, z0);
        Point High((float X, float Y) p) => new(x + ox + p.X / 2, y + oy + p.Y / 2, z1);
        var faces = new List<BrushFace>
        {
            new([.. foot.Select(High)], surface),
            new([.. foot.Reverse().Select(Low)], surface),
        };
        for (int i = 0; i < sides; i++)
        {
            var (a, c) = (foot[i], foot[(i + 1) % sides]);
            faces.Add(new([Low(a), Low(c), High(c), High(a)], surface));
        }

        return new Brush(faces, surface);
    }

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
