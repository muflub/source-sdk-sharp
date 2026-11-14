namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The chasm (plan §5, halls): a 1024 hall with a chasm across one side of
/// it: two decks two crouch-jumps high, the gap between them a double (S2)
/// or triple (S3) jump wide, a bridge further along for everyone and stairs
/// up beside the first deck (<see cref="JumpFeatures.ChasmLeap"/>). The way
/// between the doors keeps to the floor beside it. One door (an overlook) or
/// two.
/// </summary>
public static class ChasmShape
{
    public const int Height = 1024;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "chasm");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;

        // a: one door; an overlook, the chasm along the north, a rocket shelf on the south.
        var hall = new Rect(160, 160, c - t, 880);
        var a = New("chasm_a", kit, theme, Parts.E).WithOutline(hall).WithBody(hall)
            .Chamfers(hall, 112, Corner.SouthWest)
            .ChasmLeap(192, 688, Compass.East, 192, JumpTier.S2, t: rng.Fraction())
            .RocketShelf(CellFace.South, 384, 640, depth: 128, t: rng.Fraction());
        Lamps(a, hall);
        a.Pack(480, 448).Pack(800, 304).Mark(MarkerKind.PackSpawn, 272, 784, above: 2 * JumpTable.Of(JumpTier.T1).LedgeMax)
            .Mark(MarkerKind.Barrel, 240, 400);
        yield return a.Build();

        // b: two doors opposite; a double jump's chasm along the north.
        var broad = new Rect(t, 160, c - t, 880);
        var b = New("chasm_b", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad)
            .Chamfers(broad, 128, Corner.SouthWest, Corner.SouthEast)
            .ChasmLeap(160, 688, Compass.East, 192, JumpTier.S2, t: rng.Fraction())
            .RocketShelf(CellFace.South, 384, 640, depth: 128, t: rng.Fraction());
        Lamps(b, broad);
        b.Pack(400, 352).Pack(720, 352).Pack(m, 600)
            .Mark(MarkerKind.Trap, 704, 480);
        yield return b.Build();

        // c: two doors opposite; a triple jump's chasm the hall's length, narrower decks.
        var c2 = New("chasm_c", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad)
            .Chamfers(broad, 96, Corner.SouthWest, Corner.SouthEast)
            .ChasmLeap(32, 688, Compass.East, 192, JumpTier.S3, deck: 112, bridge: 64, t: rng.Fraction());
        Lamps(c2, broad);
        c2.Pack(320, 320).Pack(704, 320).Pack(m, 576)
            .Mark(MarkerKind.Barrel, 880, 240);
        yield return c2.Build();

        // d: two doors at a corner; the chasm up the west side.
        var sq = new Rect(112, 112, c - t, c - t);
        var d = New("chasm_d", kit, theme, Parts.NE).WithOutline(sq).WithBody(sq)
            .Chamfers(sq, 128, Corner.SouthEast)
            // From the south wall, so the far deck stops a player's width and more short of the north
            // wall (from 144 it could leave a strip by the corner too narrow to walk out of).
            .ChasmLeap(352, 112, Compass.North, 208, JumpTier.S2, t: rng.Fraction())
            .RocketShelf(CellFace.South, 560, 816, depth: 128, t: rng.Fraction());
        Lamps(d, sq);
        d.Pack(640, 320).Pack(704, 720).Pack(880, 880)
            .Mark(MarkerKind.Dispenser, 528, 208);
        yield return d.Build();
    }

    /// <summary>Hall lamps on a grid over the floor.</summary>
    private static void Lamps(RoomBuilder b, Rect r)
    {
        var inner = r.Grow(-128);
        int nx = Math.Max(1, (int)MathF.Round(inner.Width / 384)), ny = Math.Max(1, (int)MathF.Round(inner.Depth / 384));
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
                b.LampAt(MathF.Round(inner.X0 + inner.Width * i / nx), MathF.Round(inner.Y0 + inner.Depth * j / ny), Parts.HallLampAbove, Parts.HallLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([], Jump: true)).WithMapLabel("Chasm");
}
