namespace SourceSharp.MapGen.Descent;

/// <summary>
/// The jump features of plan_descent_mine_levels.md §4.3 (task A5), as
/// builder steps the shape generators compose. Each takes its heights and
/// gaps from <see cref="JumpTable"/> (a pick <c>t</c> from 0 to 1 chooses
/// within the tier, for seeded variation), never a literal; each refuses a
/// ledge without the clear air §4.1 asks (the ledge plus 114) under the
/// room's ceiling; and each ledge only a jump above T1 reaches carries a
/// reward marker, because every jump spot pays (§4.2). Mezzanines and chasm
/// decks have stairs instead: their second way up is their pay.
/// </summary>
/// <remarks>
/// Positions are room-local. A feature against a wall is placed by the
/// wall, a range <c>from</c> to <c>to</c> along it (y for the east and west
/// walls, x for the north and south ones) and a depth out from the wall's
/// inner face (the room's body, <see cref="RoomBuilder.Body"/>); heights are
/// above the floor's top.
/// </remarks>
public static class JumpFeatures
{
    /// <summary>The thickness of a shelf, catwalk or ledge slab.</summary>
    public const int Slab = 32;

    /// <summary>
    /// Crate steps (T1): <paramref name="steps"/> stacks of
    /// <paramref name="size"/>-square crates in a row toward
    /// <paramref name="toward"/> from the corner <paramref name="x"/>,
    /// <paramref name="y"/>, each a crouch-jump higher than the last, up to a
    /// low deck everyone can climb.
    /// </summary>
    public static RoomBuilder CrateSteps(this RoomBuilder b, float x, float y, Compass toward, int steps = 2, int size = 64, float t = 1f)
    {
        int rise = JumpTable.Of(JumpTier.T1).Ledge(t, b.Kit.NavVoxel);
        ClearAir(b, rise * steps, "crate steps");
        for (int i = 0; i < steps; i++)
        {
            float dx = toward switch { Compass.East => i * size, Compass.West => -i * size, _ => 0 };
            float dy = toward switch { Compass.North => i * size, Compass.South => -i * size, _ => 0 };
            b.WithBox(new(x + dx, y + dy, b.Floor), new(x + dx + size, y + dy + size, b.Floor + rise * (i + 1)));
        }

        return b;
    }

    /// <summary>
    /// A mezzanine (S2): a deck at the double jump's height along
    /// <paramref name="wall"/> from <paramref name="from"/> to
    /// <paramref name="to"/>, <paramref name="depth"/> deep, with stairs for
    /// everyone else beyond its <paramref name="to"/> end (rising toward the
    /// deck, 16 a step on 32 treads).
    /// </summary>
    public static RoomBuilder Mezzanine(this RoomBuilder b, CellFace wall, float from, float to, int depth = 192, float t = 0.5f)
    {
        int h = JumpTable.Of(JumpTier.S2).Ledge(t, b.Kit.NavVoxel);
        ClearAir(b, h, "a mezzanine");
        b.Holds(JumpTier.S2);
        int steps = h / 16;
        float run = steps * 32;
        b.WithBrush(WallBox(b, wall, from, to, 0, depth, 0, h, b.Theme.Block));
        var (mins, maxs) = WallBounds(b, wall, to, to + run, 0, depth, 0, h);
        return b.WithBrushes(Brush.Stairs(mins, maxs, Back(wall), steps, b.Theme.Block));
    }

    /// <summary>
    /// Stepping pillars (S2 or S3): <paramref name="count"/> round pillars
    /// in a row toward <paramref name="toward"/> from the first's centre, a
    /// crouch-jump's gap apart, their tops at the tier's height (for S3 the
    /// first is at S2's, so the row climbs), the floor route round them. The
    /// last pillar carries <paramref name="reward"/>. A pillar of radius 48
    /// or more holds a standing player's 48-square hull.
    /// </summary>
    public static RoomBuilder SteppingPillars(this RoomBuilder b, float x, float y, Compass toward, JumpTier tier, int count = 3,
                                              int radius = 48, float t = 0.5f, MarkerKind reward = MarkerKind.Container)
    {
        if (tier is not (JumpTier.S2 or JumpTier.S3))
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "stepping pillars are S2 or S3");
        int top = JumpTable.Of(tier).Ledge(t, b.Kit.NavVoxel), first = JumpTable.Of(JumpTier.S2).Ledge(t, b.Kit.NavVoxel);
        ClearAir(b, top, "stepping pillars");
        b.Holds(tier);
        float pitch = 2 * radius + JumpTable.Of(JumpTier.T1).Gap(0.75f);
        Point at = default;
        for (int i = 0; i < count; i++)
        {
            at = Along(new(x, y, 0), toward, i * pitch);
            int h = i == 0 ? first : top;
            b.WithBrush(b.Theme.SquarePillars
                ? Brush.Box(new(at.X - radius, at.Y - radius, b.Floor), new(at.X + radius, at.Y + radius, b.Floor + h), b.Theme.Block)
                : Brush.Prism(at.X, at.Y, radius, 12, b.Floor, b.Floor + h, b.Theme.Block));
            if (i == count - 1)
                b.WithMarker(reward, new(at.X, at.Y, b.Floor + h));
        }

        return b;
    }

    /// <summary>
    /// A chasm leap (S2 or S3): two decks <paramref name="deck"/> long and
    /// <paramref name="width"/> wide toward <paramref name="leap"/> from the
    /// corner <paramref name="x"/>, <paramref name="y"/>, the tier's gap
    /// between them, a bridge along their far side for everyone, and stairs
    /// up beside the first deck. The decks stand two crouch-jumps high, so a
    /// fall into the chasm is a walk back round.
    /// </summary>
    public static RoomBuilder ChasmLeap(this RoomBuilder b, float x, float y, Compass leap, float width, JumpTier tier,
                                        int deck = 160, int bridge = 96, float t = 0.5f)
    {
        if (tier is not (JumpTier.S2 or JumpTier.S3))
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "a chasm leap is S2 or S3");
        int h = 2 * JumpTable.Of(JumpTier.T1).LedgeMax, gap = JumpTable.Of(tier).Gap(t);
        ClearAir(b, h, "a chasm leap");
        b.Holds(tier);
        int steps = h / 16;
        if (deck % steps != 0)
            throw new ArgumentException($"a chasm deck of {deck} does not take {steps} whole treads");

        // Along the leap: deck A [0, deck], the gap, deck B; across: [0, width].
        b.WithBrush(Span(b, x, y, leap, 0, deck, 0, width, h));
        b.WithBrush(Span(b, x, y, leap, deck + gap, deck + gap + deck, 0, width, h));
        // The bridge, at the decks' height, along the far side of the gap.
        b.WithBrush(Span(b, x, y, leap, deck, deck + gap, width - bridge, width, h));
        // The gap's floor holds the depth's hazard where the theme says (level 13's lava): between the decks, short
        // of the bridge, open on the near side, so whoever falls in walks out.
        if (b.Theme.HazardFloor is { Chasms: true })
        {
            var (g0, g1) = SpanBounds(x, y, leap, deck, deck + gap, 0, width - bridge, b.Floor, b.Floor);
            b.HazardFloor(new Rect(g0.X, g0.Y, g1.X, g1.Y));
        }

        // The stairs, beside deck A on the near side, rising toward the leap.
        var (mins, maxs) = SpanBounds(x, y, leap, 0, deck, -bridge, 0, b.Floor, b.Floor + h);
        return b.WithBrushes(Brush.Stairs(mins, maxs, leap, steps, b.Theme.Block));
    }

    /// <summary>
    /// A rocket shelf (B1): a slab on <paramref name="wall"/> at the
    /// uncrouched rocket jump's height, with <paramref name="reward"/> on it
    /// and no other way up.
    /// </summary>
    public static RoomBuilder RocketShelf(this RoomBuilder b, CellFace wall, float from, float to, int depth = 128, float t = 0.5f,
                                          MarkerKind reward = MarkerKind.Container)
        => Ledge(b, JumpTier.B1, wall, from, to, depth, t, reward, "a rocket shelf");

    /// <summary>
    /// A catwalk ring (B2): a catwalk <paramref name="depth"/> wide round
    /// the four walls of the room's square at the crouched rocket jump's
    /// height, with <paramref name="reward"/> in its corner by the north and
    /// east walls.
    /// </summary>
    public static RoomBuilder CatwalkRing(this RoomBuilder b, int depth = 96, float t = 0.5f, MarkerKind reward = MarkerKind.Container)
    {
        int h = JumpTable.Of(JumpTier.B2).Ledge(t, b.Kit.NavVoxel);
        ClearAir(b, h, "a catwalk ring");
        b.Holds(JumpTier.B2);
        Rect r = b.Body;
        b.WithBrush(WallBox(b, CellFace.East, r.Y0, r.Y1, 0, depth, h - Slab, h, b.Theme.Block));
        b.WithBrush(WallBox(b, CellFace.West, r.Y0, r.Y1, 0, depth, h - Slab, h, b.Theme.Block));
        b.WithBrush(WallBox(b, CellFace.North, r.X0 + depth, r.X1 - depth, 0, depth, h - Slab, h, b.Theme.Block));
        b.WithBrush(WallBox(b, CellFace.South, r.X0 + depth, r.X1 - depth, 0, depth, h - Slab, h, b.Theme.Block));
        return b.WithMarker(reward, new(r.X1 - depth / 2f, r.Y1 - depth / 2f, b.Floor + h));
    }

    /// <summary>
    /// Chimney ledges (B2 and B3), for a shaft room: a ledge at the
    /// crouched rocket jump's height on <paramref name="low"/> and one at the
    /// sticky jump's on <paramref name="high"/>, each
    /// <paramref name="span"/> long in the middle of its wall, each with a
    /// reward.
    /// </summary>
    public static RoomBuilder ChimneyLedges(this RoomBuilder b, CellFace low, CellFace high, float span = 256, int depth = 128, float t = 0.5f)
    {
        Rect r = b.Body;
        float ml = low is CellFace.East or CellFace.West ? r.CentreY : r.CentreX, mh = high is CellFace.East or CellFace.West ? r.CentreY : r.CentreX;
        Ledge(b, JumpTier.B2, low, ml - span / 2, ml + span / 2, depth, t, MarkerKind.Shrine, "a chimney ledge");
        return Ledge(b, JumpTier.B3, high, mh - span / 2, mh + span / 2, depth, t, MarkerKind.Container, "a chimney ledge");
    }

    /// <summary>
    /// A roof gallery (B3): a ledge on <paramref name="wall"/> at the top of
    /// the sticky tier, for a shaft room, with <paramref name="reward"/>.
    /// </summary>
    public static RoomBuilder RoofGallery(this RoomBuilder b, CellFace wall, float from, float to, int depth = 128, float t = 1f,
                                          MarkerKind reward = MarkerKind.Container)
        => Ledge(b, JumpTier.B3, wall, from, to, depth, t, reward, "a roof gallery");

    /// <summary>
    /// A ledge of <paramref name="tier"/> (B1 to B3) on <paramref name="wall"/>
    /// from <paramref name="from"/> to <paramref name="to"/>, with
    /// <paramref name="reward"/> on it and no other way up: a high alcove, a
    /// rocket shelf, a roof gallery.
    /// </summary>
    public static RoomBuilder Shelf(this RoomBuilder b, JumpTier tier, CellFace wall, float from, float to, int depth = 128, float t = 0.5f,
                                    MarkerKind reward = MarkerKind.Container)
    {
        if (tier < JumpTier.B1)
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "a shelf is a blast tier (B1 to B3); a Scout's spot is a mezzanine or pillars");
        return Ledge(b, tier, wall, from, to, depth, t, reward, "a shelf");
    }

    //-------------------------------------------------------------------------

    private static RoomBuilder Ledge(RoomBuilder b, JumpTier tier, CellFace wall, float from, float to, int depth, float t, MarkerKind reward, string what)
    {
        int h = JumpTable.Of(tier).Ledge(t, b.Kit.NavVoxel);
        ClearAir(b, h, what);
        b.Holds(tier);
        b.WithBrush(WallBox(b, wall, from, to, 0, depth, h - Slab, h, b.Theme.Block));
        var (mins, maxs) = WallBounds(b, wall, from, to, 0, depth, h, h);
        return b.WithMarker(reward, new((mins.X + maxs.X) / 2, (mins.Y + maxs.Y) / 2, b.Floor + h));
    }

    /// <summary>Refuses a ledge at <paramref name="h"/> above the floor without §4.1's clear air under the ceiling.</summary>
    private static void ClearAir(RoomBuilder b, int h, string what)
    {
        float need = b.Floor + JumpTierSpec.ClearAbove(h);
        if (need > b.CeilingUnderside)
            throw new ArgumentException(
                $"{what} at {h} needs clear air to {JumpTierSpec.ClearAbove(h)} above the floor; the room's ceiling is at {b.CeilingUnderside - b.Floor}");
    }

    /// <summary>A box against a wall (see the class remarks), its heights above the floor.</summary>
    internal static Brush WallBox(RoomBuilder b, CellFace wall, float from, float to, float out0, float out1, float z0, float z1, Surface surface)
    {
        var (mins, maxs) = WallBounds(b, wall, from, to, out0, out1, z0, z1);
        return Brush.Box(mins, maxs, surface);
    }

    internal static (Point Mins, Point Maxs) WallBounds(RoomBuilder b, CellFace wall, float from, float to, float out0, float out1, float z0, float z1)
    {
        Rect r = b.Body;
        float f = b.Floor;
        if (to <= from)
            throw new ArgumentException($"a feature on the {wall} wall runs from {from} to {to}");
        return wall switch
        {
            CellFace.East => (new(r.X1 - out1, from, f + z0), new(r.X1 - out0, to, f + z1)),
            CellFace.West => (new(r.X0 + out0, from, f + z0), new(r.X0 + out1, to, f + z1)),
            CellFace.North => (new(from, r.Y1 - out1, f + z0), new(to, r.Y1 - out0, f + z1)),
            _ => (new(from, r.Y0 + out0, f + z0), new(to, r.Y0 + out1, f + z1)),
        };
    }

    /// <summary>The direction back along a wall, toward lower <c>from</c>: south for the east and west walls, west for the others.</summary>
    private static Compass Back(CellFace wall) => wall is CellFace.East or CellFace.West ? Compass.South : Compass.West;

    private static Point Along(Point p, Compass toward, float d) => toward switch
    {
        Compass.East => p with { X = p.X + d },
        Compass.West => p with { X = p.X - d },
        Compass.North => p with { Y = p.Y + d },
        _ => p with { Y = p.Y - d },
    };

    /// <summary>A box from the floor to <paramref name="h"/>, given along a heading and across it from a corner.</summary>
    private static Brush Span(RoomBuilder b, float x, float y, Compass along, float a0, float a1, float c0, float c1, float h)
    {
        var (mins, maxs) = SpanBounds(x, y, along, a0, a1, c0, c1, b.Floor, b.Floor + h);
        return Brush.Box(mins, maxs, b.Theme.Block);
    }

    /// <summary>
    /// The box at distances <paramref name="a0"/> to <paramref name="a1"/>
    /// along <paramref name="along"/> and <paramref name="c0"/> to
    /// <paramref name="c1"/> across it (to the heading's left: north of an
    /// eastward run) from the corner (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static (Point Mins, Point Maxs) SpanBounds(float x, float y, Compass along, float a0, float a1, float c0, float c1, float z0, float z1)
    {
        Point p = Map(a0, c0), q = Map(a1, c1);
        return (new(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), z0), new(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), z1));

        Point Map(float a, float c) => along switch
        {
            Compass.East => new(x + a, y + c, 0),
            Compass.West => new(x - a, y - c, 0),
            Compass.North => new(x - c, y + a, 0),
            _ => new(x + c, y - a, 0),
        };
    }
}
