namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// A hall (plan §5, halls and shafts; §3.1's standard jump room): 1024
/// high, doors west and east (and north in the second variant), a
/// mezzanine along one wall with its stairs (S2), a rocket shelf on the
/// other with a container (B1), robot packs on the floor, a barrel and a
/// trap. The demonstration of a jump-room family.
/// </summary>
public static class HallShape
{
    /// <summary>The hall's height class (§3.1).</summary>
    public const int Height = 1024;

    /// <summary>A hall's lamp: the default's colour and brightness, reaching further.</summary>
    public static Lamp HallLamp { get; } = Lamp.Default with { Brightness = 120, FiftyPercent = 384, ZeroPercent = 1024 };

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "hall");
        yield return Build("hall_we", kit, theme, rng, [CellFace.West, CellFace.East]);
        yield return Build("hall_wen", kit, theme, rng, [CellFace.West, CellFace.East, CellFace.North]);
        yield return Build("hall_ne", kit, theme, rng, [CellFace.North, CellFace.East]);
        yield return Ring("hall_nesw", kit, theme, rng);
    }

    /// <summary>
    /// The four-door hall: no wall is free for a mezzanine, so a catwalk ring
    /// (B2) runs round above the doors and stepping pillars (S2) cross the
    /// middle, the floor route round them.
    /// </summary>
    private static Room Ring(string name, RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        float m = kit.Cell / 2f, f = kit.Sill;
        var b = new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(ShapeParts.Four)
            .WithEntry(new RoomEntry([], Weight: 2, Jump: true))
            .WithMapLabel("Hall")
            .CatwalkRing(depth: 96, t: 0.25f + rng.Fraction() / 2)
            .SteppingPillars(m - 216, m + 96, Compass.East, JumpTier.S2, count: 3)
            .WithMarker(MarkerKind.PackSpawn, new Point(m, m - 192, f), radius: 96)
            .WithMarker(MarkerKind.PackSpawn, new Point(m - 256, m - 256, f), radius: 64);
        foreach (float x in (float[])[m - 256, m + 256])
            foreach (float y in (float[])[m - 256, m + 256])
                b.WithLamp(new Point(x, y, f + 448), HallLamp);
        return b.Build();
    }

    private static Room Build(string name, RoomKit kit, RoomTheme theme, RoomSeed rng, CellFace[] doors)
    {
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f, f = kit.Sill;
        // The mezzanine goes on a wall without a door; the shelf on the other
        // long wall, above any door there (a shelf's underside is above the head).
        CellFace deckWall = doors.Contains(CellFace.North) ? CellFace.South : rng.Pick([CellFace.North, CellFace.South]);
        CellFace shelfWall = deckWall == CellFace.North ? CellFace.South : CellFace.North;
        float deckFrom = t + 128 + 16 * rng.Int(0, 4);
        var b = new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([], Weight: 2, Jump: true))
            .WithMapLabel("Hall")
            .Mezzanine(deckWall, deckFrom, deckFrom + 384, depth: 192)
            // The upper half of B1, so the shelf's underside clears a door's head under it.
            .RocketShelf(shelfWall, m + 96, m + 352, depth: 128, t: 0.5f + rng.Fraction() / 2)
            .WithMarker(MarkerKind.PackSpawn, new Point(m - 160, m, f), radius: 96)
            .WithMarker(MarkerKind.PackSpawn, new Point(m + 160, m, f), radius: 96)
            .WithMarker(MarkerKind.Barrel, new Point(m + 256, deckWall == CellFace.North ? t + 96 : c - t - 96, f))
            .WithMarker(MarkerKind.Trap, new Point(m - 256, m + 128, f))
            // Player spawns, so a sample level with no up room has a start.
            .WithMarker(MarkerKind.Spawn, new Point(m - 64, m - 64, f), yaw: 0)
            .WithMarker(MarkerKind.Spawn, new Point(m - 64, m + 64, f), yaw: 0);
        // Four lamps over the quarters, hung at 448 above the floor: a hall's
        // ceiling is beyond a lamp's reach, so the lamps light the floor and
        // the walls' lower half and leave the roof in the dark.
        foreach (float x in (float[])[m - 256, m + 256])
            foreach (float y in (float[])[m - 256, m + 256])
                b.WithLamp(new Point(x, y, f + 448), HallLamp);
        return b.Build();
    }
}
