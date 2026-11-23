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
        float m = Parts.Span(kit) / 2f;

        // a: a pillared hall, two rows of four.
        // Sized to the normal kit's 992 interior (the boss levels left their 2048 to 4096 kits, plan §3.2).
        var hall = Rect.Around(m, m, 896, 640);
        // On level 5's 512 kit the doors stand on the south cell's axis (brief p18): only the north corners are cut.
        bool big = Parts.SpanCells(kit) > 1;
        var a = New("antechamber_a", kit, theme, hall).Chamfers(hall, 128, big ? [Corner.NorthEast, Corner.NorthWest] : []);
        float r = rng.Pick<float>([40, 48]);
        foreach (float dx in (float[])[-336, -112, 112, 336])
            // On the 512 kit the rows stand north of the doors' axis, so the way between the doors runs south of them.
            foreach (float dy in big ? (float[])[-48, 208] : [-176, 176])
                a.Pillar(m + dx, m + dy, r);
        Light(a, hall);
        a.Pack(m - 256, m - 240).Pack(m + 256, m + 240).Pack(m, m)
            .Mark(MarkerKind.Shrine, m, hall.Y1 - 48, yaw: 270).Mark(MarkerKind.Barrel, m - 320, hall.Y0 + 64);
        yield return a.Build();

        // b: an octagon round a plinth.
        var octagon = Rect.Around(m, m, 768, 768);
        // On level 5's 512 kit the doors stand on the south cell's axis (brief p18): the south corners stay square
        // there (a cut stood across the necks), so each door's neck opens on the octagon's flat side.
        float cut = 224, low = cut;
        var b = New("antechamber_b", kit, theme, octagon);
        if (!big)
            b.Chamfers(octagon, low, Corner.SouthWest, Corner.SouthEast);
        b.Chamfers(octagon, cut, Corner.NorthEast, Corner.NorthWest)
            .Block(Rect.Around(m, m, 160, 160), 32);
        // Posts of the trim floor to roof at each of the octagon's eight corners and up the middle of each cut wall: its
        // walls were big flat panels (brief p15, level 5).
        float x0 = octagon.X0, y0 = octagon.Y0, x1 = octagon.X1, y1 = octagon.Y1, post = 16;
        foreach (var (px, py) in ((float, float)[])[(x0 + low, y0), (x1 - low, y0), (x0 + cut, y1), (x1 - cut, y1), (x0, y0 + low), (x0, y1 - cut), (x1, y0 + low), (x1, y1 - cut),
                                                   (x0 + low / 2, y0 + low / 2), (x1 - low / 2, y0 + low / 2), (x0 + cut / 2, y1 - cut / 2), (x1 - cut / 2, y1 - cut / 2)])
            // On the 512 kit the south corners are not cut, and posts there would stand in the doors' necks.
            if (!(big && py < m))
                b.WithBox(new Point(px - post, py - post, b.Floor), new Point(px + post, py + post, b.CeilingUnderside), b.Theme.TrimOrBlock);
        Light(b, octagon);
        b.Pack(m - 224, m + 224).Pack(m + 224, m - 224).Pack(m - 224, m - 224)
            .Mark(MarkerKind.Dispenser, m, m, above: 32).Mark(MarkerKind.Trap, m + 224, m + 224);
        yield return b.Build();

        // c: a hall with a mezzanine along each long wall.
        var gallery = Rect.Around(m, m, 896, 768);
        var c = New("antechamber_c", kit, theme, gallery)
            .Mezzanine(CellFace.North, gallery.X0 + 64, gallery.X0 + (big ? 432 : 448), depth: 192)
            // On the 512 kit the south one stands west of the middle, its stairs short of the east door, the way between
            // the doors round its north side.
            .Mezzanine(CellFace.South, gallery.X0 + (big ? 256 : 320), gallery.X0 + (big ? 496 : 704), depth: 192);
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
        Necks(new RoomBuilder(name, kit, theme).WithHeight(Height).Seat(Parts.WE)
            .WithEntry(new RoomEntry([])).WithMapLabel("Antechamber"), body);

    private static RoomBuilder Necks(RoomBuilder b, Rect body)
    {
        // On level 5's 512 kit the doors stand off the body's middle (brief p18): each neck runs on into the body, so the
        // door's whole approach is open floor.
        float into = b.Cells > 1 ? 128 : 32;
        return b.WithOutline(body, Parts.Neck(b, CellFace.West, 256, body.X0 - b.Kit.WallDepth + into), Parts.Neck(b, CellFace.East, 256, b.Extent - b.Kit.WallDepth - body.X1 + into))
            .WithBody(body);
    }
}
