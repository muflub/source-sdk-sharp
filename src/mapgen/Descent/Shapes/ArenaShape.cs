namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The boss arenas (tag <c>boss_arena</c>; plan §3.2, §7), placeholders until
/// Phase 6: one per boss kit, lit over all its floor, with cover pillars and
/// the ledges §7 lists, doors west and east. Level 5's (2048): a round
/// blasted chamber with stone pillars, a ring of ledges at the double jump's
/// height (S2) and shelves at the rocket jump's (B1). Level 10's (4096): a
/// bored cavern round a central roof pillar and four outer ones, catwalks at
/// the crouched rocket jump's height (B2) and rock steps (S2). Level
/// 15's (3072): a lake floor with a raised platform in the middle, islands,
/// pillar tops (S2) and ledges (B1, B2). The boss kits' 32-unit voxel holds
/// no triple jump's height, so their pillars are S2.
/// </summary>
public static class ArenaShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "arena");
        switch (kit.Cell)
        {
            case 2048:
                yield return Magazine(kit, theme, rng);
                break;
            case 4096:
                yield return Bore(kit, theme, rng);
                break;
            case 3072:
                yield return Pit(kit, theme, rng);
                break;
        }
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, int height, Rect body, int depth) =>
        new RoomBuilder(name, kit, theme).WithHeight(height).WithDoors(Parts.WE)
            .WithEntry(new RoomEntry(["boss_arena"], MinDepth: depth, MaxDepth: depth, Jump: true)).WithMapLabel("Arena")
            .WithOutline(body, Parts.Neck(kit, CellFace.West, 256, body.X0 - kit.WallDepth + 32), Parts.Neck(kit, CellFace.East, 256, kit.Cell - kit.WallDepth - body.X1 + 32))
            .WithBody(body);

    /// <summary>Lamps on a grid <paramref name="pitch"/> apart over <paramref name="r"/>, and along each door's passage.</summary>
    private static void Light(RoomBuilder b, Rect r, float pitch)
    {
        int nx = Math.Max(1, (int)MathF.Ceiling(r.Width / pitch)), ny = Math.Max(1, (int)MathF.Ceiling(r.Depth / pitch));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                b.LampAt(MathF.Round(r.X0 + r.Width * (i + 0.5f) / nx), MathF.Round(r.Y0 + r.Depth * (j + 0.5f) / ny), Parts.HallLampAbove, Parts.HallLamp);
        b.LampsFromDoors(new Point(r.CentreX, r.CentreY, b.Floor), 384, Parts.ChamberLampAbove, Parts.ChamberLamp);
    }

    /// <summary>Level 5: the Powder Magazine.</summary>
    private static Room Magazine(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        float m = kit.Cell / 2f;
        var round = Rect.Around(m, m, 1856, 1856);
        var b = New("arena_a", kit, theme, 1024, round, 5).RoundCorners(round, 576, 5);
        // Stone pillars on a ring for cover from his cannonballs.
        for (int k = 0; k < 6; k++)
        {
            float a = (30 + 60 * k) * MathF.PI / 180;
            b.Pillar(MathF.Round(m + 480 * MathF.Cos(a)), MathF.Round(m + 480 * MathF.Sin(a)), 72, 10);
        }

        // The ring ledge (S2) on the straight runs of the north and south walls, with stairs at one end of each for everyone,
        // and the rocket shelves (B1) between the doors and the corners.
        b.Mezzanine(CellFace.North, m - 288, m + 128, depth: 128).Mezzanine(CellFace.South, m - 288, m + 128, depth: 128)
            .RocketShelf(CellFace.West, m + 160, m + 352, depth: 128, t: rng.Fraction())
            .RocketShelf(CellFace.East, m - 352, m - 160, depth: 128, t: rng.Fraction());
        Light(b, round, 576);
        b.Pack(m - 256, m).Pack(m + 256, m).Pack(m, m + 320).Pack(m, m - 320)
            .Mark(MarkerKind.Barrel, m - 160, m + 480).Mark(MarkerKind.Barrel, m + 160, m - 480);
        return b.Build();
    }

    /// <summary>Level 10: the Bore.</summary>
    private static Room Bore(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        float m = kit.Cell / 2f;
        var cave = Rect.Around(m, m, 3840, 3840);
        var b = New("arena_b", kit, theme, 1280, cave, 10).RoundCorners(cave, 960, 6);
        // The central pillar that holds the roof, and the four outer ones the Tank drills.
        b.Pillar(m, m, 256, 16);
        foreach (var (dx, dy) in ((float, float)[])[(-960, -960), (960, -960), (960, 960), (-960, 960)])
            b.Pillar(m + dx, m + dy, 160, 12);
        // Catwalks (B2) across the north and south, over the track.
        b.Shelf(JumpTier.B2, CellFace.North, m - 768, m + 768, depth: 160, t: 1f)
            .Shelf(JumpTier.B2, CellFace.South, m - 768, m + 768, depth: 160, t: 1f);
        // Rock steps for the Scout by the west and east passages: S2, since the 32-unit voxel holds no triple jump's height (136 to 144).
        b.SteppingPillars(m - 1280, m + 512, Compass.North, JumpTier.S2, count: 3, radius: 64)
            .SteppingPillars(m + 1280, m - 512, Compass.South, JumpTier.S2, count: 3, radius: 64);
        Light(b, cave, 576);
        b.Pack(m - 640, m).Pack(m + 640, m).Pack(m, m + 640).Pack(m, m - 640)
            .Mark(MarkerKind.Dispenser, m - 1440, m - 1152).Mark(MarkerKind.Barrel, m + 1440, m + 1152);
        return b.Build();
    }

    /// <summary>Level 15: the Pit.</summary>
    private static Room Pit(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        float m = kit.Cell / 2f;
        var lake = Rect.Around(m, m, 2816, 2816);
        var b = New("arena_c", kit, theme, 1024, lake, 15).RoundCorners(lake, 768, 6);
        // The platform in the middle, a step up, its sections the encounter will break away later; islands round it.
        b.Block(Rect.Around(m, m, 768, 768), 16, theme.Block);
        foreach (var (dx, dy) in ((float, float)[])[(-768, 640), (768, -640), (-640, -768), (640, 768)])
            b.Block(Rect.Around(m + dx, m + dy, 320, 320), 16, theme.Block);
        // Height is safety: pillar tops for the Scout, ledges for the blast jumpers.
        b.SteppingPillars(m - 1024, m - 256, Compass.North, JumpTier.S2, count: 3, radius: 64)
            .SteppingPillars(m + 1024, m + 256, Compass.South, JumpTier.S2, count: 3, radius: 64)
            .Shelf(JumpTier.B1, CellFace.North, m - 256, m + 256, depth: 160, t: rng.Fraction())
            .Shelf(JumpTier.B2, CellFace.South, m - 256, m + 256, depth: 160, t: rng.Fraction());
        Light(b, lake, 576);
        b.Pack(m - 896, m).Pack(m + 896, m).Pack(m, m + 896)
            .Mark(MarkerKind.Shrine, m, m - 448).Mark(MarkerKind.Barrel, m + 512, m - 960);
        return b.Build();
    }
}
