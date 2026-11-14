namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The antechamber (plan §5, §7; boss levels): the room before the arena,
/// doors west and east, a 512 chamber in the middle of its cell, necks to
/// its doors. A
/// pillared hall; an octagon round a plinth; and a hall with a mezzanine
/// (S2) on each side. The family name is what the layout reads.
/// </summary>
public static class AntechamberShape
{
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "antechamber");
        float m = kit.Cell / 2f;

        // a: a pillared hall, two rows of four.
        // Sized to the normal kit's 992 interior (the boss levels left their 2048 to 4096 kits, plan §3.2).
        var hall = Rect.Around(m, m, 896, 640);
        var a = New("antechamber_a", kit, theme, hall).Chamfers(hall, 128);
        float r = rng.Pick<float>([40, 48]);
        foreach (float dx in (float[])[-336, -112, 112, 336])
            foreach (float dy in (float[])[-176, 176])
                a.Pillar(m + dx, m + dy, r);
        Light(a, hall);
        a.Pack(m - 256, m - 240).Pack(m + 256, m + 240).Pack(m, m)
            .Mark(MarkerKind.Shrine, m, hall.Y1 - 48, yaw: 270).Mark(MarkerKind.Barrel, m - 320, hall.Y0 + 64);
        yield return a.Build();

        // b: an octagon round a plinth.
        var octagon = Rect.Around(m, m, 768, 768);
        var b = New("antechamber_b", kit, theme, octagon).Chamfers(octagon, 224)
            .Block(Rect.Around(m, m, 160, 160), 32);
        Light(b, octagon);
        b.Pack(m - 224, m + 224).Pack(m + 224, m - 224).Pack(m - 224, m - 224)
            .Mark(MarkerKind.Dispenser, m, m, above: 32).Mark(MarkerKind.Trap, m + 224, m + 224);
        yield return b.Build();

        // c: a hall with a mezzanine along each long wall.
        var gallery = Rect.Around(m, m, 896, 768);
        var c = New("antechamber_c", kit, theme, gallery)
            .Mezzanine(CellFace.North, gallery.X0 + 64, gallery.X0 + 448, depth: 192)
            .Mezzanine(CellFace.South, gallery.X0 + 320, gallery.X0 + 704, depth: 192);
        Light(c, gallery);
        c.Pack(m - 128, m).Pack(m + 256, m + 64).Pack(m + 384, gallery.Y1 - 96)
            .Mark(MarkerKind.Container, gallery.X0 + 160, gallery.Y1 - 96, above: JumpTable.Of(JumpTier.S2).Ledge(0.5f, kit.NavVoxel))
            .Mark(MarkerKind.Barrel, gallery.X1 - 128, gallery.Y0 + 272);
        yield return c.Build();
    }

    private static void Light(RoomBuilder b, Rect body)
    {
        b.LampsFromDoors(new Point(body.CentreX, body.CentreY, b.Floor), 320, Parts.ChamberLampAbove, Parts.ChamberLamp);
        foreach (float x in (float[])[body.X0 + 160, body.CentreX, body.X1 - 160])
            foreach (float y in (float[])[body.Y0 + 160, body.Y1 - 160])
                b.LampAt(MathF.Round(x), MathF.Round(y), Parts.ChamberLampAbove, Parts.ChamberLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, Rect body) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(Parts.WE)
            .WithEntry(new RoomEntry([])).WithMapLabel("Antechamber")
            .WithOutline(body, Parts.Neck(kit, CellFace.West, 256, body.X0 - kit.WallDepth + 32), Parts.Neck(kit, CellFace.East, 256, kit.Cell - kit.WallDepth - body.X1 + 32))
            .WithBody(body);
}
