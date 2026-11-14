namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The boss arenas (tag <c>boss_arena</c>; plan §3.2, §7), placeholders until Phase 6, on the normal
/// kit as rooms larger than one cell (M24), each opening only where its boss row joins it
/// (<see cref="RoomBuilder.WithSockets"/>): an arena with every perimeter edge a socket packed in
/// three to six times the time and space (plan §3.2). Lit over all its floor, with cover pillars
/// and the ledges §7 lists.
/// <list type="bullet">
/// <item>Level 5, the Powder Magazine, 2x2: a round blasted chamber with stone pillars, a ring of
/// ledges at the double jump's height (S2) and shelves at the rocket jump's (B1); in at its west
/// socket's south cell, out at its east socket's north cell.</item>
/// <item>Level 10, the Bore, 3x3: a bored cavern round a central roof pillar and four outer ones,
/// catwalks at the crouched rocket jump's height (B2) and rock steps (S2); in and out at the middle
/// of its west and east walls, the room's length kept free for the Tank's track.</item>
/// <item>Level 15, the Pit, 4x4: a lake floor with a raised platform in the middle, islands, pillar
/// tops (S2) and ledges (B1, B2), and its one socket west: it is the bottom of the shaft.</item>
/// </list>
/// </summary>
public static class ArenaShape
{
    /// <summary>Each boss depth's arena: its footprint, its sockets (wall and cell edge) and height.</summary>
    public static IReadOnlyDictionary<int, (int Cells, (CellFace Face, int Offset)[] Sockets, int Height)> Of { get; } =
        new Dictionary<int, (int, (CellFace, int)[], int)>
        {
            [5] = (2, [(CellFace.West, 0), (CellFace.East, 1)], 1024),
            [10] = (3, [(CellFace.West, 1), (CellFace.East, 1)], 1280),
            [15] = (4, [(CellFace.West, 1)], 1024),
        };

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "arena");
        yield return Magazine(kit, theme, rng);
        yield return Bore(kit, theme, rng);
        yield return Pit(kit, theme, rng);
    }

    /// <summary>An arena's builder for <paramref name="depth"/>: its footprint, sockets and height, its corners rounded clear of the sockets.</summary>
    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, int depth)
    {
        var (cells, sockets, height) = Of[depth];
        var b = new RoomBuilder(name, kit, theme).WithFootprint(cells).WithSockets(sockets).WithHeight(height)
            .WithEntry(new RoomEntry(["boss_arena"], MinDepth: depth, MaxDepth: depth, Jump: true)).WithMapLabel("Arena");
        // Rounded short of the first socket along each wall (its approach starts a door's half from the cell's middle).
        return b.RoundCorners(b.Body, kit.DoorLow - 96, 4);
    }

    /// <summary>Lamps on a grid <paramref name="pitch"/> apart over the arena's floor, a hall lamp's height up.</summary>
    private static void Light(RoomBuilder b, float pitch)
    {
        Rect r = b.Body;
        int nx = Math.Max(1, (int)MathF.Ceiling(r.Width / pitch)), ny = Math.Max(1, (int)MathF.Ceiling(r.Depth / pitch));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                b.LampAt(MathF.Round(r.X0 + r.Width * (i + 0.5f) / nx), MathF.Round(r.Y0 + r.Depth * (j + 0.5f) / ny), Parts.HallLampAbove, Parts.HallLamp);
    }

    /// <summary>Level 5: the Powder Magazine.</summary>
    private static Room Magazine(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        var b = New("arena_a", kit, theme, 5);
        float m = b.Extent / 2;
        // Stone pillars on a ring for cover from his cannonballs.
        for (int k = 0; k < 6; k++)
        {
            float a = (30 + 60 * k) * MathF.PI / 180;
            b.Pillar(MathF.Round(m + 480 * MathF.Cos(a)), MathF.Round(m + 480 * MathF.Sin(a)), 72, 10);
        }

        // The ring ledge (S2) on the north and south walls, with stairs at one end of each for everyone,
        // and the rocket shelves (B1) on the west and east walls between their sockets.
        b.Mezzanine(CellFace.North, m - 288, m + 128, depth: 128).Mezzanine(CellFace.South, m - 288, m + 128, depth: 128)
            .RocketShelf(CellFace.West, m + 160, m + 352, depth: 128, t: rng.Fraction())
            .RocketShelf(CellFace.East, m - 352, m - 160, depth: 128, t: rng.Fraction());
        Light(b, 576);
        b.Pack(m - 256, m).Pack(m + 256, m).Pack(m, m + 320).Pack(m, m - 320)
            .Mark(MarkerKind.Barrel, m - 160, m + 480).Mark(MarkerKind.Barrel, m + 160, m - 480);
        return b.Build();
    }

    /// <summary>Level 10: the Bore.</summary>
    private static Room Bore(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        var b = New("arena_b", kit, theme, 10);
        float m = b.Extent / 2;
        // The central pillar that holds the roof, and the four outer ones the Tank drills.
        b.Pillar(m, m, 224, 16);
        foreach (var (dx, dy) in ((float, float)[])[(-896, -896), (896, -896), (896, 896), (-896, 896)])
            b.Pillar(m + dx, m + dy, 144, 12);
        // Catwalks (B2) across the north and south, over the track.
        b.Shelf(JumpTier.B2, CellFace.North, m - 768, m + 768, depth: 160, t: 1f)
            .Shelf(JumpTier.B2, CellFace.South, m - 768, m + 768, depth: 160, t: 1f);
        // Rock steps for the Scout near the west and east walls.
        b.SteppingPillars(m - 1184, m + 448, Compass.North, JumpTier.S2, count: 3, radius: 56)
            .SteppingPillars(m + 1184, m - 448, Compass.South, JumpTier.S2, count: 3, radius: 56);
        Light(b, 576);
        b.Pack(m - 576, m).Pack(m + 576, m).Pack(m, m + 576).Pack(m, m - 576)
            .Mark(MarkerKind.Dispenser, m - 1344, m - 1152).Mark(MarkerKind.Barrel, m + 1344, m + 1152);
        return b.Build();
    }

    /// <summary>Level 15: the Pit.</summary>
    private static Room Pit(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        var b = New("arena_c", kit, theme, 15);
        float m = b.Extent / 2;
        // The platform in the middle, a step up, its sections the encounter will break away later; islands round it.
        b.Block(Rect.Around(m, m, 768, 768), 16, theme.Block);
        foreach (var (dx, dy) in ((float, float)[])[(-768, 640), (768, -640), (-640, -768), (640, 768)])
            b.Block(Rect.Around(m + dx, m + dy, 320, 320), 16, theme.Block);
        // Height is safety: pillar tops for the Scout, ledges for the blast jumpers.
        b.SteppingPillars(m - 1040, m - 256, Compass.North, JumpTier.S2, count: 3, radius: 64)
            .SteppingPillars(m + 1040, m + 256, Compass.South, JumpTier.S2, count: 3, radius: 64)
            .Shelf(JumpTier.B1, CellFace.North, m - 256, m + 256, depth: 160, t: rng.Fraction())
            .Shelf(JumpTier.B2, CellFace.South, m - 256, m + 256, depth: 160, t: rng.Fraction());
        Light(b, 576);
        b.Pack(m - 896, m).Pack(m + 896, m).Pack(m, m + 896)
            .Mark(MarkerKind.Shrine, m, m - 448).Mark(MarkerKind.Barrel, m + 512, m - 960);
        return b.Build();
    }
}
