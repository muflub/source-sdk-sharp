namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The rooms larger than one cell (M24, plan section 5; map tools'
/// multi-cell rooms): a socket on every cell edge of the perimeter, the open
/// floor the whole interior, features kept off the internal cell faces (no
/// surface may lie on one) and out of the sockets' approaches. Each has a
/// floor a Heavy walks from every socket to every other, and its jump
/// features from the tier table.
/// <list type="bullet">
/// <item><c>hall</c> (levels 2 to 4): a 2x2 yard hall, 1024 high with a rocket
/// shelf and a mezzanine, or 512 high with square piers and crate steps.</item>
/// <item><c>grotto</c> (level 6 on): a 2x2 and a 3x3 cavern, 1536 high, an
/// uneven displaced floor (power 3) and roof, rock pillars, a high shelf.</item>
/// <item><c>flood</c> (level 8 on): a 2x2 gallery flooded to a wading depth
/// round a dry island, with stepping pillars.</item>
/// </list>
/// </summary>
public static class BigShapes
{
    /// <summary>A larger room's lamps: the hall lamp, dimmer, since a larger room holds a grid of them.</summary>
    private static readonly Lamp Lamp = Parts.HallLamp with { Brightness = 60 };

    /// <summary>Lamps over the floor on a grid, kept off the internal cell faces and out of the jump air's middle.</summary>
    private static void Lamps(RoomBuilder b, float above, float pitch)
    {
        float e = b.Extent, t = b.Kit.WallDepth;
        for (float x = t + pitch / 2; x < e - t; x += pitch)
            for (float y = t + pitch / 2; y < e - t; y += pitch)
                b.LampAt(x, y, above, Lamp);
    }

    /// <summary>Pack spawns spread over the floor, away from every socket, and a container.</summary>
    private static void Markers(RoomBuilder b, int packs, (float X, float Y)[]? at = null)
    {
        float e = b.Extent, c = b.Kit.Cell;
        var spots = at ?? [(c - 288, c - 288), (c + 288, c + 288), (c + 288, c - 288), (c - 288, c + 288)];
        foreach (var (x, y) in spots.Take(packs))
            b.Pack(x, y);
        b.Mark(MarkerKind.Barrel, e - 224, 224);
    }

    /// <summary>
    /// The great halls (levels 2 to 4, M24): the level's set piece, each with an interior a player
    /// reads as a place (the review of 2026-11-10: the halls were empty boxes). Every variant keeps
    /// the 96 in front of each of its eight sockets clear and a giant's walk between them; its focus
    /// is its main feature, which the dressing builds on.
    /// <list type="bullet">
    /// <item><c>hall_a</c>, 1024 high: a loading dock along the north wall in three sections, the
    /// north sockets opening into the bays between them, a ramp up, a row of posts along its edge; a
    /// Scout's mezzanine on the east wall and a rocket shelf on the south.</item>
    /// <item><c>hall_b</c>, 512 high: a yard round a stepped plinth in its middle (a machine or an ore
    /// heap stands on it), between two rows of four piers.</item>
    /// <item><c>hall_c</c>, 1024 high: a gallery round the north and west walls over the doors,
    /// reached by a free-standing stair and held on posts, with a Scout's perch on it and a rocket
    /// shelf above it.</item>
    /// </list>
    /// </summary>
    public static IEnumerable<Room> Hall(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "hall");
        float c = kit.Cell, e = 2 * c, t = kit.WallDepth;

        // a: the loading dock.
        var a = new RoomBuilder("hall_a", kit, theme).WithFootprint(2).WithHeight(1024)
            .WithEntry(new RoomEntry([], MinDepth: 2, MaxDepth: 4, Jump: true)).WithMapLabel("Loading hall");
        const float dockFront = 1696, dock = 48;
        // Three sections between the north sockets' bays, each a step-free 48 (a crouch-jump, T1) above the floor.
        foreach (var (x0, x1) in new[] { (t, kit.DoorLow - 16f), (kit.DoorHigh + 16f, c + kit.DoorLow - 16), (c + kit.DoorHigh + 16, e - t) })
            a.Block(new Rect(x0, dockFront, x1, e - t), dock);
        // A ramp up to the middle section for everyone.
        a.WithBrush(Brush.Wedge(new(c - 224, dockFront - 192, a.Floor), new(c + 224, dockFront, a.Floor + dock), Compass.North, theme.Block));
        // Posts along the dock's edge, on its deck, holding up nothing but the eye.
        foreach (float x in (float[])[704, 896, 1152, 1344])
            a.WithBox(new(x - 16, dockFront + 16, a.Floor + dock), new(x + 16, dockFront + 48, a.CeilingUnderside));
        a.Mezzanine(CellFace.East, kit.DoorHigh + 48, c + 176, depth: 160, t: rng.Fraction())
            .RocketShelf(CellFace.South, kit.DoorHigh + 48, c + kit.DoorLow - 48, depth: 144, t: rng.Fraction());
        a.WithFocus(new Point(c, dockFront - 96, a.Floor));
        Lamps(a, 448, 512);
        Markers(a, 3, [(c - 352, c - 128), (c + 352, c - 128), (c, 640)]);
        a.Mark(MarkerKind.Container, c + 256, e - 160, above: dock);
        yield return a.Build();

        // b: the yard round a plinth, between two rows of piers.
        var b = new RoomBuilder("hall_b", kit, theme).WithFootprint(2).WithHeight(512)
            .WithEntry(new RoomEntry([], MinDepth: 2, MaxDepth: 4)).WithMapLabel("Yard");
        b.Block(Rect.Around(c, c, 576, 576), 16).Block(Rect.Around(c, c, 384, 384), 32);
        foreach (float y in (float[])[c - 448, c + 448])
            foreach (float x in (float[])[256, 640, e - 640, e - 256])
                b.Pillar(x, y, 40);
        b.WithFocus(new Point(c, c, b.Floor + 32));
        Lamps(b, 384, 512);
        Markers(b, 4, [(400, c), (e - 400, c), (c, 400), (c, e - 400)]);
        yield return b.Build();

        // c: the gallery over the doors, round two walls, on posts, with its stair.
        var cc = new RoomBuilder("hall_c", kit, theme).WithFootprint(2).WithHeight(1024)
            .WithEntry(new RoomEntry([], MinDepth: 2, MaxDepth: 4, Jump: true)).WithMapLabel("Gallery hall");
        const int gallery = 224, deep = 240;
        float under = kit.DoorHeight;
        // Its slab sits on the doors' heads (a giant walks under it), the north wing the wall's length, the west wing down to the south wall.
        cc.WithBox(new(t, e - t - deep, cc.Floor + under), new(e - t, e - t, cc.Floor + gallery));
        cc.WithBox(new(t, t, cc.Floor + under), new(t + deep, e - t - deep, cc.Floor + gallery));
        foreach (float x in (float[])[352, 832, 1216, 1696])
            cc.WithBox(new(x - 12, e - t - deep, cc.Floor), new(x + 12, e - t - deep + 24, cc.Floor + under));
        foreach (float y in (float[])[352, 832, 1216])
            cc.WithBox(new(t + deep - 24, y - 12, cc.Floor), new(t + deep, y + 12, cc.Floor + under));
        // The stair, free-standing, up to the north wing.
        cc.StairsUp(new Rect(c + 128, e - t - deep - 448, c + 320, e - t - deep), Compass.North, gallery);
        // A Scout's perch on the north wing (S2 above the gallery), and a rocket shelf over the west wing.
        int perch = JumpTable.Of(JumpTier.S2).Ledge(rng.Fraction(), kit.NavVoxel);
        cc.WithBox(new(560, e - t - 176, cc.Floor + gallery), new(720, e - t, cc.Floor + gallery + perch)).Holds(JumpTier.S2)
            .WithMarker(MarkerKind.Container, new Point(640, e - t - 88, cc.Floor + gallery + perch));
        cc.Shelf(JumpTier.B2, CellFace.West, c - 96, c + 320, depth: 128, t: 1f);
        cc.WithFocus(new Point(c, c, cc.Floor));
        Lamps(cc, 448, 512);
        Markers(cc, 3, [(c - 256, c - 160), (c + 352, c - 96), (c + 96, 560)]);
        yield return cc.Build();
    }

    public static IEnumerable<Room> Grotto(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "grotto");
        foreach (var (variant, n) in new[] { ("a", 2), ("b", 3), ("c", 2) })
        {
            float c = kit.Cell, e = n * c, t = kit.WallDepth;
            var g = new RoomBuilder("grotto_" + variant, kit, theme).WithFootprint(n).WithHeight(1536)
                .WithEntry(new RoomEntry([], MinDepth: 6, MaxDepth: 14, Jump: true)).WithMapLabel("Grotto");
            // An uneven floor of mounds, a step high at most, across the middle (crossing the internal faces).
            float crest = 12 + 2 * rng.Int(0, 3);
            g.WithBrush(Brush.DisplacedBox(new(c - 400, c - 400, g.Floor), new(e - c + 400, e - c + 400, g.Floor + 2), 3,
                (u, v) => MathF.Round(crest * MathF.Sin(MathF.PI * u) * MathF.Sin(MathF.PI * v)), theme.Floor));
            // Rock pillars between the cells, off every internal face.
            for (int i = 0; i < n; i++)
                g.Pillar(i * c + c / 2 + (variant == "c" ? -160 : 160), c / 2 + 96, 56, 10);
            g.Shelf(JumpTier.B2, CellFace.West, c - 300, c + 300, depth: 160, t: rng.Fraction());
            // A cave, not a box (brief p4): its corners rounded, rough rock along its walls between the
            // sockets and the shelf, a roof hanging in lumps and a few stalactites.
            var inner = new Rect(t, t, e - t, e - t);
            foreach (Corner corner in (Corner[])[Corner.SouthWest, Corner.SouthEast, Corner.NorthEast, Corner.NorthWest])
                g.WithBrushes(g.RoundCorner(inner, corner, 224, 4));
            float phase = rng.Fraction() * 6.283f;
            foreach (CellFace wall in (CellFace[])[CellFace.North, CellFace.South, CellFace.East, CellFace.West])
                foreach (var (lo, hi) in RockSpans(kit, n, wall == CellFace.West ? [(c - 332, c + 332)] : []))
                    CavernShape.RockRun(g, inner, wall, lo, hi, phase + (int)wall);
            float roof = Math.Min(e - 2 * t - 256, 1792);
            CavernShape.Roof(g, Rect.Around(e / 2, e / 2, roof, roof), rng, 192);
            CavernShape.Stalactites(g, Rect.Around(e / 2, e / 2, roof, roof), rng, 4 + 2 * n);
            // Low enough that a lamp reaches the floor of the corners (the lint's lit rule, read over the whole room).
            Lamps(g, 448, 512);
            // Off the mounds, a cell's half from each wall's middle.
            Markers(g, 4, [(c / 2, e / 2), (e - c / 2, e / 2), (e / 2, c / 2), (e / 2, e - c / 2)]);
            yield return g.Build();
        }
    }

    /// <summary>
    /// The spans along one wall of an <paramref name="n"/> by <paramref name="n"/> room that rough rock may
    /// cover: past its rounded corners, a door's approach clear either side of every socket, and clear of
    /// <paramref name="keep"/> (a shelf against that wall).
    /// </summary>
    private static IEnumerable<(float Lo, float Hi)> RockSpans(RoomKit kit, int n, (float Lo, float Hi)[] keep)
    {
        float c = kit.Cell, t = kit.WallDepth, gap = RoomBuilder.DoorApproach + 32;
        var holes = Enumerable.Range(0, n).Select(k => (Lo: k * c + kit.DoorLow - gap, Hi: k * c + kit.DoorHigh + gap)).Concat(keep).OrderBy(h => h.Lo).ToArray();
        float at = t + 240;
        foreach (var (lo, hi) in holes)
        {
            if (lo - at >= 128)
                yield return (at, lo);
            at = Math.Max(at, hi);
        }

        if (n * c - t - 240 - at >= 128)
            yield return (at, n * c - t - 240);
    }

    public static IEnumerable<Room> Flood(RoomKit kit, RoomTheme theme, ulong seed)
    {
        float c = kit.Cell;
        foreach (var (variant, island, height) in new[] { ("a", 200, 512), ("b", 296, 512), ("c", 136, 1024) })
        {
            var f = new RoomBuilder("flood_" + variant, kit, theme).WithFootprint(2).WithHeight(height)
                .WithEntry(new RoomEntry([], MinDepth: 8, MaxDepth: 14)).WithMapLabel("Flooded gallery");
            // Wading water over the floor between the walls' walks (a Heavy wades it), round a dry island.
            var basin = new Rect(c - 640, c - 640, c + 640, c + 640);
            f.WithWater(basin, f.Floor, f.Floor + PoolShape.Water, theme.Water ?? PoolShape.WaterMaterial);
            f.Block(new Rect(c - island, c - 136, c + 136, c + island), 48);
            f.SteppingPillars(c - 520, c + 420, Compass.East, JumpTier.S2, count: 3);
            Lamps(f, height == 512 ? 384 : 448, 512);
            Markers(f, 3);
            yield return f.Build();
        }
    }
}
