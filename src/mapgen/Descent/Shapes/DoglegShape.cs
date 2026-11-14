namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The dogleg (plan §5, connectors): a low hallway between doors opposite
/// (west and east) that breaks the sight line, so the door across is never in
/// view. A partition from one wall; a Z through a band offset to one side,
/// round an island; and a serpentine between two partitions.
/// </summary>
public static class DoglegShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "dogleg");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        Point w = Parts.DoorPoint(kit, CellFace.West, 0), e = Parts.DoorPoint(kit, CellFace.East, 0);

        // a: one partition from the south wall; the way goes round its north end.
        float px = rng.Pick<float>([464, 496, 528]);
        var body = new Rect(176, m - 120, c - 176, m + 288);
        var a = New("dogleg_a", kit, theme)
            .WithOutline(new Rect(t, m - 120, c - t, m + 120), body).WithBody(body)
            .Chamfers(body, 96, Corner.NorthWest, Corner.NorthEast)
            .Block(new Rect(px, body.Y0, px + 64, m + 136), Parts.Wall(kit, DriftShape.Height), theme.Wall);
        a.LampsAlong(w, new Point(px + 32, m + 212, kit.Sill), 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampsAlong(new Point(px + 32, m + 212, kit.Sill), e, 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(px + 32, m + 220);
        yield return a.Build();

        // b: a Z: the necks meet a band to the north, an island blocking the straight line.
        var band = new Rect(224, m + 8, c - 224, m + 344);
        var b = New("dogleg_b", kit, theme)
            .WithOutline(new Rect(t, m - 120, 352, m + 120), new Rect(c - 352, m - 120, c - t, m + 120), band,
                         new Rect(224, m - 120, 352, m + 8), new Rect(c - 352, m - 120, c - 224, m + 8)).WithBody(band)
            .Block(new Rect(m - 64, m + 8, m + 64, m + 152), Parts.Wall(kit, DriftShape.Height), theme.Wall)
            .Chamfers(band, 112, Corner.NorthWest, Corner.NorthEast);
        b.LampsAlong(w, new Point(288, m + 248, kit.Sill), 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampsAlong(new Point(288, m + 248, kit.Sill), new Point(c - 288, m + 248, kit.Sill), 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampsAlong(new Point(c - 288, m + 248, kit.Sill), e, 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, m + 256);
        yield return b.Build();

        // c: a serpentine between a partition from the south and one from the north.
        float gap = rng.Pick<float>([144, 160]);
        var hall = new Rect(160, m - 256, c - 160, m + 256);
        var cc = New("dogleg_c", kit, theme)
            .WithOutline(new Rect(t, m - 120, c - t, m + 120), hall).WithBody(hall)
            .Block(new Rect(384, hall.Y0, 432, hall.Y1 - gap), Parts.Wall(kit, DriftShape.Height), theme.Wall)
            .Block(new Rect(592, hall.Y0 + gap, 640, hall.Y1), Parts.Wall(kit, DriftShape.Height), theme.Wall);
        cc.LampsAlong(w, new Point(408, hall.Y1 - gap / 2, kit.Sill), 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampsAlong(new Point(408, hall.Y1 - gap / 2, kit.Sill), new Point(616, hall.Y0 + gap / 2, kit.Sill), 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampsAlong(new Point(616, hall.Y0 + gap / 2, kit.Sill), e, 224, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, m)
            .Mark(MarkerKind.Trap, 280, hall.Y0 + 48);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(Parts.WE)
            .WithEntry(Parts.Low("corridor")).WithMapLabel("Dogleg");
}
