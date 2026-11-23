namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The cavern (plan §5, halls and shafts): a natural cave, its corners
/// rounded off, its door-free walls rough rock (displaced faces pushing into
/// the room), rubble mounds on its floor (displaced tops), stalactites from
/// its roof and a high alcove (B2) with a container. Two to four doors, and
/// one; one variant is a shaft. The mounds rise toward their east side, so
/// a preview shows which way a displacement's rows run.
/// </summary>
public static class CavernShape
{
    /// <summary>The first depth a cavern stands at: level 6, where the mine breaks into the cave (plan §7, M4); Act I is the engineered mine.</summary>
    public const int FirstDepth = 6;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "cavern");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;

        // a: one door; an oval cave.
        var oval = new Rect(144, 160, c - t, 864);
        var a = New("cavern_a", kit, theme, Parts.E, 1024).WithOutline(oval).WithBody(oval);
        a.WithBrushes(a.RoundCorner(oval, Corner.SouthWest, 256, 4)).WithBrushes(a.RoundCorner(oval, Corner.NorthWest, 256, 4));
        Rock(a, oval, rng, 256, CellFace.North, CellFace.South);
        Mound(a, new Rect(256, 352, 448, 544), rng);
        Stalactites(a, oval, rng, 5);
        Roof(a, oval.Grow(-96), rng, 128);
        a.Shelf(JumpTier.B2, CellFace.West, 400, 624, depth: 144, t: rng.Fraction());
        Lamps(a, oval);
        a.Pack(560, 320).Pack(560, 704).Pack(352, 704)
            .Mark(MarkerKind.Barrel, 880, 240);
        yield return a.Build();

        // b: two doors opposite; two lobes joined by a waist.
        var west = new Rect(t, 176, 528, 848);
        var east = new Rect(496, 128, c - t, 784);
        var b = New("cavern_b", kit, theme, Parts.WE, 1024).WithOutline(west, east, new Rect(400, 352, 640, 672)).WithBody(new Rect(t, 128, c - t, 848));
        b.WithBrushes(b.RoundCorner(west, Corner.NorthWest, 192, 3)).WithBrushes(b.RoundCorner(west, Corner.SouthWest, 160, 3))
            .WithBrushes(b.RoundCorner(east, Corner.NorthEast, 160, 3)).WithBrushes(b.RoundCorner(east, Corner.SouthEast, 192, 3));
        Rock(b, west, rng, 192, CellFace.North);
        Rock(b, east, rng, 192, CellFace.South);
        Mound(b, new Rect(640, 576, 832, 752), rng);
        Stalactites(b, new Rect(t, 128, c - t, 848), rng, 6);
        Roof(b, west.Grow(-96), rng, 128);
        Roof(b, east.Grow(-96), rng, 112);
        b.Shelf(JumpTier.B2, CellFace.North, 128, 352, depth: 128, t: rng.Fraction());
        Lamps(b, west);
        Lamps(b, east);
        b.Pack(256, 704).Pack(768, 288).Pack(m, m + 96)
            .Mark(MarkerKind.Trap, 288, 320);
        yield return b.Build();

        // c: three doors (north, east, west); a wide cave, its south side rounded.
        var wide = new Rect(t, 128, c - t, c - t);
        var cc = New("cavern_c", kit, theme, Parts.NEW, 1024).WithOutline(wide).WithBody(wide);
        cc.WithBrushes(cc.RoundCorner(wide, Corner.SouthWest, 256, 4)).WithBrushes(cc.RoundCorner(wide, Corner.SouthEast, 256, 4))
            .WithBrushes(cc.RoundCorner(wide, Corner.NorthWest, 160, 3)).WithBrushes(cc.RoundCorner(wide, Corner.NorthEast, 160, 3));
        Rock(cc, wide, rng, 256, CellFace.South);
        RockAround(cc, kit, wide, 160, rng.Fraction() * 6.283f, 1, CellFace.North, CellFace.East, CellFace.West);
        Mound(cc, new Rect(224, 640, 400, 816), rng);
        Stalactites(cc, wide, rng, 6);
        Roof(cc, wide.Grow(-128), rng, 144);
        cc.Shelf(JumpTier.B2, CellFace.South, 432, 592, depth: 128, t: rng.Fraction());
        Lamps(cc, wide);
        cc.Pack(320, 400).Pack(704, 400).Pack(704, 720)
            .Mark(MarkerKind.Shrine, m, 192, yaw: 90);
        yield return cc.Build();

        // d: four doors; a shaft-high cave, round, with a stalactite forest.
        var round = new Rect(96, 96, 928, 928);
        var d = New("cavern_d", kit, theme, Parts.NESW, 1536)
            .WithOutline(round, Parts.Neck(kit, CellFace.East, 256, 96), Parts.Neck(kit, CellFace.West, 256, 96),
                         Parts.Neck(kit, CellFace.North, 256, 96), Parts.Neck(kit, CellFace.South, 256, 96)).WithBody(round)
            .RoundCorners(round, 288, 4);
        Mound(d, new Rect(224, 592, 416, 784), rng);
        Stalactites(d, round, rng, 8);
        Roof(d, round.Grow(-128), rng, 192);
        Spire(d, 720, 720, 64, JumpTier.B3, rng);
        Spire(d, 320, 320, 56, JumpTier.B2, rng);
        Lamps(d, round);
        d.LampAt(m, m, 896, Parts.HighLamp)
            .Pack(704, 320).Pack(512, 288).Pack(288, 512)
            .Mark(MarkerKind.Barrel, 600, 400);
        yield return d.Build();
    }

    /// <summary>How far a cave wall bulges into the room at most, above head height; at the floor a third of it.</summary>
    public const int Bulge = 96;

    /// <summary>
    /// Rough rock: a slab against each named wall of <paramref name="r"/>, its
    /// face pushed into the room by up to <see cref="Bulge"/> over a player's
    /// head and a third of that at the floor, in lumps along and up it.
    /// </summary>
    private static void Rock(RoomBuilder b, Rect r, RoomSeed rng, float inset, params CellFace[] walls)
    {
        float phase = rng.Fraction() * 6.283f;
        foreach (CellFace wall in walls)
        {
            // Inset from the rectangle's corners, which are rounded.
            float lo = (wall is CellFace.North or CellFace.South ? r.X0 : r.Y0) + inset, hi = (wall is CellFace.North or CellFace.South ? r.X1 : r.Y1) - inset;
            RockRun(b, r, wall, lo, hi, phase);
        }
    }

    /// <summary>
    /// Rough rock along the <paramref name="wall"/> side of <paramref name="r"/> from <paramref name="lo"/>
    /// to <paramref name="hi"/> along it (a grotto's walls between its sockets): a slab whose face is
    /// pushed into the room by up to <see cref="Bulge"/> over a player's head and a third at the floor.
    /// </summary>
    public static void RockRun(RoomBuilder b, Rect r, CellFace wall, float lo, float hi, float phase) => RockRun(b, r, wall, lo, hi, phase, b.Floor, 128);

    /// <summary>The least run of rough rock a wall takes beside a door (brief p9: a single cave's walls beside its doors are this short).</summary>
    public const int ShortRun = 96;

    /// <summary>How far over a door's head rough rock starts above it, clear of its frame's flange and keystone.</summary>
    public const int OverDoor = 96;

    /// <summary>
    /// Rough rock over every door of a cave wall and beside it (brief p9: the caverns and grottos kept
    /// flat walls round their doors, floor to roof): on each side of <paramref name="r"/> whose wall is
    /// the room's, a run from <see cref="OverDoor"/> above the door's head to the roof across the door's
    /// approach, and runs down to the floor between the approach and <paramref name="corner"/> from the
    /// rectangle's corners (rounded off), <see cref="ShortRun"/> long at least. <paramref name="doors"/>
    /// are the room's door faces; a larger room's sockets are on every cell edge of each.
    /// </summary>
    public static void RockAround(RoomBuilder b, RoomKit kit, Rect r, float corner, float phase, int cells, params CellFace[] doors)
    {
        float c = kit.Cell, gap = RoomBuilder.DoorApproach + 32, roof = b.CeilingUnderside;
        foreach (CellFace wall in doors)
        {
            float lo = wall is CellFace.North or CellFace.South ? r.X0 : r.Y0, hi = wall is CellFace.North or CellFace.South ? r.X1 : r.Y1;
            float at = lo + corner;
            for (int k = 0; k < cells; k++)
            {
                float d0 = k * c + kit.DoorLow - gap, d1 = k * c + kit.DoorHigh + gap;
                if (d0 - at >= ShortRun)
                    RockRun(b, r, wall, at, d0, phase + k, b.Floor, ShortRun);
                if (roof - (kit.Head + OverDoor) >= 128)
                    RockRun(b, r, wall, d0, d1, phase + 2 * k + 1, kit.Head + OverDoor, ShortRun);
                at = d1;
            }

            if (hi - corner - at >= ShortRun)
                RockRun(b, r, wall, at, hi - corner, phase + cells, b.Floor, ShortRun);
        }
    }

    /// <summary>Rough rock over the door on the cell edge <paramref name="k"/> along <paramref name="wall"/>, from <see cref="OverDoor"/> above its head to the roof, across its approach.</summary>
    public static void RockOverDoor(RoomBuilder b, RoomKit kit, Rect r, CellFace wall, int k, float phase)
    {
        float gap = RoomBuilder.DoorApproach + 32;
        if (b.CeilingUnderside - (kit.Head + OverDoor) >= 128)
            RockRun(b, r, wall, k * kit.Cell + kit.DoorLow - gap, k * kit.Cell + kit.DoorHigh + gap, phase, kit.Head + OverDoor, ShortRun);
    }

    private static void RockRun(RoomBuilder b, Rect r, CellFace wall, float lo, float hi, float phase, float bottom, float least)
    {
        if (hi - lo < least)
            return;
        float top = b.CeilingUnderside;
        Surface rock = b.Cells > 1 ? b.Theme.LargeWall ?? b.Theme.Wall : b.Theme.Wall;
        float Relief(float u, float v) => MathF.Round(Bulge * MathF.Sin(MathF.PI * u)
            * (0.5f + 0.3f * MathF.Sin(9 * u + 5 * v + phase) + 0.2f * MathF.Sin(17 * u - 11 * v + 2 * phase))
            * (0.33f + 0.67f * MathF.Min(1, v * 4)));
        Brush slab = wall switch
        {
            CellFace.North => Brush.DisplacedWall(new(lo, r.Y1 - 32, bottom), new(hi, r.Y1, top), Compass.South, 3, Relief, rock),
            CellFace.South => Brush.DisplacedWall(new(lo, r.Y0, bottom), new(hi, r.Y0 + 32, top), Compass.North, 3, Relief, rock),
            CellFace.East => Brush.DisplacedWall(new(r.X1 - 32, lo, bottom), new(r.X1, hi, top), Compass.West, 3, Relief, rock),
            _ => Brush.DisplacedWall(new(r.X0, lo, bottom), new(r.X0 + 32, hi, top), Compass.East, 3, Relief, rock),
        };
        b.WithBrush(slab);
    }

    /// <summary>
    /// A mound over <paramref name="r"/>: a slab 2 high whose top rises to a
    /// crest toward its east side, no higher than a step over the floor, so
    /// the walked floor is uneven and still walked.
    /// </summary>
    private static void Mound(RoomBuilder b, Rect r, RoomSeed rng)
    {
        float crest = 12 + 2 * rng.Int(0, 3);
        b.WithBrush(Brush.DisplacedBox(new(r.X0, r.Y0, b.Floor), new(r.X1, r.Y1, b.Floor + 2), 3,
            (u, v) => MathF.Round(crest * MathF.Sin(MathF.PI * v) * MathF.Sin(MathF.PI * u * 0.75f) * (0.4f + 0.6f * u)), b.Theme.Floor));
    }

    /// <summary>An uneven roof over <paramref name="r"/>: a slab under the ceiling whose underside hangs down in lumps by up to <paramref name="drop"/>.</summary>
    public static void Roof(RoomBuilder b, Rect r, RoomSeed rng, float drop)
    {
        float phase = rng.Fraction() * 6.283f, top = b.CeilingUnderside;
        b.WithBrush(Brush.DisplacedUnder(new(r.X0, r.Y0, top - 16), new(r.X1, r.Y1, top), 3,
            (u, v) => MathF.Round(drop * MathF.Sin(MathF.PI * u) * MathF.Sin(MathF.PI * v) * (0.6f + 0.4f * MathF.Sin(7 * u + 5 * v + phase))), b.Theme.Ceiling));
    }

    /// <summary>Stalactites from the roof over <paramref name="r"/>, high enough to clear the jump air.</summary>
    public static void Stalactites(RoomBuilder b, Rect r, RoomSeed rng, int count)
    {
        var inner = r.Grow(-160);
        for (int i = 0; i < count; i++)
        {
            float x = MathF.Round(inner.X0 + inner.Width * rng.Fraction()), y = MathF.Round(inner.Y0 + inner.Depth * rng.Fraction());
            float length = 96 + 16 * rng.Int(0, 8);
            b.WithBrush(Brush.Spike(x, y, 24 + 8 * rng.Int(0, 3), 6, b.CeilingUnderside - length, b.CeilingUnderside, b.Theme.Wall));
        }
    }

    /// <summary>A rock spire whose top is a ledge of <paramref name="tier"/>, with a container on it and no other way up.</summary>
    private static void Spire(RoomBuilder b, float x, float y, float radius, JumpTier tier, RoomSeed rng)
    {
        int h = JumpTable.Of(tier).Ledge(rng.Fraction(), b.Kit.NavVoxel);
        if (b.Floor + JumpTierSpec.ClearAbove(h) > b.CeilingUnderside)
            throw new ArgumentException($"a spire at {h} needs clear air to {JumpTierSpec.ClearAbove(h)} above the floor");
        b.Holds(tier);
        b.WithBrush(Brush.Prism(x, y, radius, 9, b.Floor, b.Floor + h, b.Theme.Wall));
        b.Mark(MarkerKind.Container, x, y, above: h);
    }

    /// <summary>Hall lamps over the floor of <paramref name="r"/>.</summary>
    private static void Lamps(RoomBuilder b, Rect r)
    {
        var inner = r.Grow(-160);
        int nx = Math.Max(1, (int)MathF.Round(inner.Width / 384)), ny = Math.Max(1, (int)MathF.Round(inner.Depth / 384));
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
                b.LampAt(MathF.Round(inner.X0 + inner.Width * i / nx), MathF.Round(inner.Y0 + inner.Depth * j / ny), Parts.HallLampAbove, Parts.HallLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, int height) =>
        new RoomBuilder(name, kit, theme).WithHeight(height).WithDoors(doors)
            .WithEntry(new RoomEntry([], MinDepth: FirstDepth, Jump: true)).WithMapLabel("Cavern");
}
