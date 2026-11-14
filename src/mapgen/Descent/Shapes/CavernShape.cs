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
        Mound(cc, new Rect(224, 640, 400, 816), rng);
        Stalactites(cc, wide, rng, 6);
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
        Spire(d, 720, 720, 64, JumpTier.B3, rng);
        Spire(d, 320, 320, 56, JumpTier.B2, rng);
        Lamps(d, round);
        d.LampAt(m, m, 896, Parts.HighLamp)
            .Pack(704, 320).Pack(512, 288).Pack(288, 512)
            .Mark(MarkerKind.Barrel, 600, 400);
        yield return d.Build();
    }

    /// <summary>Rough rock: a slab against each named wall of <paramref name="r"/>, its face pushed into the room by up to 48.</summary>
    private static void Rock(RoomBuilder b, Rect r, RoomSeed rng, float inset, params CellFace[] walls)
    {
        float phase = rng.Fraction() * 6.283f, top = b.CeilingUnderside;
        float Relief(float u, float v) => MathF.Round(48 * MathF.Sin(MathF.PI * u) * (0.55f + 0.45f * MathF.Sin(9 * u + 5 * v + phase)) * MathF.Min(1, v * 6));
        foreach (CellFace wall in walls)
        {
            // Inset from the rectangle's corners, which are rounded.
            float lo = (wall is CellFace.North or CellFace.South ? r.X0 : r.Y0) + inset, hi = (wall is CellFace.North or CellFace.South ? r.X1 : r.Y1) - inset;
            if (hi - lo < 128)
                continue;
            Brush slab = wall switch
            {
                CellFace.North => Brush.DisplacedWall(new(lo, r.Y1 - 32, b.Floor), new(hi, r.Y1, top), Compass.South, 3, Relief, b.Theme.Wall),
                CellFace.South => Brush.DisplacedWall(new(lo, r.Y0, b.Floor), new(hi, r.Y0 + 32, top), Compass.North, 3, Relief, b.Theme.Wall),
                CellFace.East => Brush.DisplacedWall(new(r.X1 - 32, lo, b.Floor), new(r.X1, hi, top), Compass.West, 3, Relief, b.Theme.Wall),
                _ => Brush.DisplacedWall(new(r.X0, lo, b.Floor), new(r.X0 + 32, hi, top), Compass.East, 3, Relief, b.Theme.Wall),
            };
            b.WithBrush(slab);
        }
    }

    /// <summary>A rubble mound over <paramref name="r"/>: a slab 8 high whose top rises to a crest toward its east side.</summary>
    private static void Mound(RoomBuilder b, Rect r, RoomSeed rng)
    {
        float crest = 24 + 8 * rng.Int(0, 3);
        b.WithBrush(Brush.DisplacedBox(new(r.X0, r.Y0, b.Floor), new(r.X1, r.Y1, b.Floor + 8), 3,
            (u, v) => MathF.Round(crest * MathF.Sin(MathF.PI * v) * MathF.Sin(MathF.PI * u * 0.75f) * (0.4f + 0.6f * u)), b.Theme.Floor));
    }

    /// <summary>Stalactites from the roof over <paramref name="r"/>, high enough to clear the jump air.</summary>
    private static void Stalactites(RoomBuilder b, Rect r, RoomSeed rng, int count)
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
        int h = JumpTable.Of(tier).Ledge(rng.Fraction());
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
            .WithEntry(new RoomEntry([], Jump: true)).WithMapLabel("Cavern");
}
