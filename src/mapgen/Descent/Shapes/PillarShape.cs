namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The pillar room (plan §5, chambers): cover for the ground game, 512 high,
/// one to four doors. Its variants: a long hall of round pillars ending at a
/// plinth (one door); a broad room of square piers in two rows (two
/// opposite); an L round three pillars (two at a corner); an octagon with a
/// ring of pillars (three); and a cross with a pillar at each inner corner
/// (four).
/// </summary>
public static class PillarShape
{
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "pillar");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ChamberLampAbove;
        Lamp lamp = Parts.ChamberLamp;

        // a: one door; a long hall of round pillars ending at a plinth with a container.
        var hall = new Rect(176, 240, c - t, 784);
        float r = rng.Pick<float>([48, 56, 64]);
        var a = New("pillar_a", kit, theme, Parts.E).WithOutline(hall).WithBody(hall)
            .Chamfers(hall, 144, Corner.SouthWest, Corner.NorthWest)
            .Block(new Rect(hall.X0 + 16, m - 64, hall.X0 + 112, m + 64), 32);
        foreach (float x in (float[])[400, 656])
            foreach (float y in (float[])[384, 640])
                a.Pillar(x, y, r);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(hall.X0 + 96, m, kit.Sill), 288, above, lamp)
            .LampAt(528, 320, above, lamp).LampAt(528, 704, above, lamp)
            .Mark(MarkerKind.Container, hall.X0 + 64, m, above: 32)
            .Pack(528, 304).Pack(528, 720).Pack(528, m)
            .Mark(MarkerKind.Barrel, c - 112, hall.Y0 + 48);
        yield return a.Build();

        // b: two doors opposite; a broad room of square piers in two rows.
        float half = rng.Pick<float>([40, 48]);
        var broad = new Rect(t, 176, c - t, 848);
        var b = New("pillar_b", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad)
            .Chamfers(broad, 128);
        foreach (float x in (float[])[288, 512, 736])
            foreach (float y in (float[])[336, 688])
                b.Post(x, y, half);
        b.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(400, 256, above, lamp).LampAt(624, 768, above, lamp)
            .Pack(400, 512).Pack(624, 240).Pack(624, 784)
            .Mark(MarkerKind.Trap, 512, 512).Mark(MarkerKind.Dispenser, 176, 784);
        yield return b.Build();

        // c: two doors at a corner; an L round three pillars.
        var south = new Rect(176, 176, c - t, 656);
        var north = new Rect(368, 176, 848, c - t);
        var cc = New("pillar_c", kit, theme, Parts.NE).WithOutline(south, north).WithBody(new Rect(176, 176, c - t, c - t))
            .Chamfers(south, 160, Corner.SouthWest).Chamfers(north, 96, Corner.NorthEast)
            .Pillar(352, 352, 56).Pillar(704, 336, 56).Pillar(704, 784, 48);
        cc.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(272, 448, above, lamp).LampAt(800, 272, above, lamp)
            .Pack(256, 560).Pack(448, 816).Pack(880, 240)
            .Mark(MarkerKind.Barrel, 448, 224);
        yield return cc.Build();

        // d: three doors; an octagon with four pillars on a ring.
        var octagon = new Rect(96, 96, 928, 928);
        float ring = rng.Pick<float>([256, 288]);
        var d = New("pillar_d", kit, theme, Parts.NEW).WithOutline(octagon, Parts.Neck(kit, CellFace.East, 256, 96),
                Parts.Neck(kit, CellFace.West, 256, 96), Parts.Neck(kit, CellFace.North, 256, 96)).WithBody(octagon)
            .Chamfers(octagon, 224);
        foreach (int deg in (int[])[45, 135, 225, 315])
            d.Pillar(MathF.Round(m + ring * MathF.Cos(deg * MathF.PI / 180)), MathF.Round(m + ring * MathF.Sin(deg * MathF.PI / 180)), 52);
        d.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(m, 224, above, lamp).LampAt(m, m, above, lamp)
            .Pack(m, 256).Pack(272, 400).Pack(752, 624)
            .Mark(MarkerKind.Shrine, m, 160, yaw: 90);
        yield return d.Build();

        // e: four doors; a cross with a pillar at each inner corner.
        float arm = rng.Pick<float>([224, 256]);
        var cross = new Rect(m - arm, m - arm, m + arm, m + arm);
        var e = New("pillar_e", kit, theme, Parts.NESW)
            .WithOutline(new Rect(t, m - arm, c - t, m + arm), new Rect(m - arm, t, m + arm, c - t)).WithBody(cross);
        foreach (float x in (float[])[m - arm + 72, m + arm - 72])
            foreach (float y in (float[])[m - arm + 72, m + arm - 72])
                e.Pillar(x, y, 40);
        e.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp)
            .Pack(m - arm + 72, m).Pack(m + arm - 72, m).Pack(m, m + arm - 64)
            .Mark(MarkerKind.Barrel, m - arm + 40, 160).Mark(MarkerKind.Trap, m, m);
        yield return e.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([])).WithMapLabel("Pillar room");
}
