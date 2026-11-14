namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The chimney (plan §5, shafts): a shaft room 1536 to 1792 high, its body
/// narrower than the cell, ledges up its walls: a rocket shelf (B1), a ledge
/// at the crouched rocket jump's height (B2) and one at the sticky jump's
/// (B3), each with its reward (<see cref="JumpFeatures.ChimneyLedges"/>).
/// One or two doors.
/// </summary>
public static class ChimneyShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "chimney");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;

        // a: one door; a deep shaft, ledges on its three far walls.
        var shaft = new Rect(208, 176, c - t, 848);
        var a = New("chimney_a", kit, theme, Parts.E, rng).WithOutline(shaft).WithBody(shaft)
            .Chamfers(shaft, 112, Corner.SouthWest, Corner.NorthWest)
            .ChimneyLedges(CellFace.North, CellFace.South, span: 256, t: rng.Fraction())
            .RocketShelf(CellFace.West, 368, 656, depth: 128, t: rng.Fraction());
        Lamps(a, shaft);
        a.Pack(400, 400).Pack(640, 640).Mark(MarkerKind.Barrel, 880, 240);
        yield return a.Build();

        // b: two doors opposite; a long shaft, the ledges on its north and south walls, the shelf beside the B2.
        var hall = new Rect(t, 192, c - t, 832);
        var b = New("chimney_b", kit, theme, Parts.WE, rng).WithOutline(hall).WithBody(hall)
            .Chamfers(hall, 96)
            .ChimneyLedges(CellFace.North, CellFace.South, span: 224, t: rng.Fraction())
            .RocketShelf(CellFace.North, 160, 352, depth: 128, t: rng.Fraction());
        Lamps(b, hall);
        b.Pack(352, 352).Pack(672, 672).Mark(MarkerKind.Trap, 704, 368);
        yield return b.Build();

        // c: two doors at a corner; a square shaft off the corner's necks, the ledges on its south and west walls.
        var square = new Rect(176, 176, 848, 848);
        var cc = New("chimney_c", kit, theme, Parts.NE, rng)
            .WithOutline(square, Parts.Neck(kit, CellFace.North, 256, 176), Parts.Neck(kit, CellFace.East, 256, 176)).WithBody(square)
            .Chamfers(square, 144, Corner.SouthWest)
            .ChimneyLedges(CellFace.South, CellFace.West, span: 256, t: rng.Fraction())
            .RoofGallery(CellFace.North, 192, 352, depth: 112);
        Lamps(cc, square);
        cc.Pack(400, 640).Pack(672, 352).Mark(MarkerKind.Dispenser, 800, 800);
        yield return cc.Build();
    }

    /// <summary>Hall lamps over the floor, and higher ones that show the ledges.</summary>
    private static void Lamps(RoomBuilder b, Rect r)
    {
        var inner = r.Grow(-176);
        foreach (float x in (float[])[inner.X0, inner.X1])
            foreach (float y in (float[])[inner.Y0, inner.Y1])
                b.LampAt(MathF.Round(x), MathF.Round(y), Parts.HallLampAbove, Parts.HallLamp);
        b.LampAt(r.CentreX, r.CentreY, 896, Parts.HighLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, RoomSeed rng) =>
        new RoomBuilder(name, kit, theme).WithHeight(rng.Pick<int>([1536, 1792])).WithDoors(doors)
            .WithEntry(new RoomEntry([], Jump: true)).WithMapLabel("Chimney");
}
