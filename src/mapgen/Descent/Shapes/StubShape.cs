namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The stub (plan §5, connectors): a low dead end of one door (east) that is
/// worth the walk (M18): every stub is tagged <c>shrine</c> and ends at a
/// shrine marker. A drift ending in an octagonal alcove; an L turning into a
/// niche; and a short neck into a small chamber with the shrine on a dais.
/// </summary>
public static class StubShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "stub");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        Point e = Parts.DoorPoint(kit, CellFace.East, 0);

        // a: a drift ending in an octagonal alcove, its length seeded.
        float x0 = rng.Pick<float>([176, 240, 304]);
        var alcove = new Rect(x0, m - 176, x0 + 352, m + 176);
        var a = New("stub_a", kit, theme).WithOutline(new Rect(x0 + 176, m - 120, c - t, m + 120), alcove).WithBody(alcove)
            .Chamfers(alcove, 112);
        a.LampsAlong(e, new Point(alcove.CentreX, m, kit.Sill), 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Mark(MarkerKind.Shrine, alcove.X0 + 64, m, yaw: 0)
            .Pack(alcove.CentreX + 48, m + 96);
        yield return a.Build();

        // b: an L, the drift turning north into a niche.
        var niche = new Rect(m - 128, m + 120, m + 128, c - 112);
        var b = New("stub_b", kit, theme).WithOutline(new Rect(m - 128, m - 120, c - t, m + 120), niche).WithBody(niche)
            .Chamfers(new Rect(m - 128, m - 120, m + 128, c - 112), 96, Corner.SouthWest)
            .Chamfers(niche, 64, Corner.NorthWest, Corner.NorthEast);
        b.LampsAlong(e, new Point(m, m, kit.Sill), 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampAt(m, niche.Y1 - 112, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Mark(MarkerKind.Shrine, m, niche.Y1 - 56, yaw: 270)
            .Pack(m + 128, m);
        yield return b.Build();

        // c: a neck into a small chamber, the shrine on a dais a step up.
        var chamber = new Rect(208, m - 224, 640, m + 224);
        var dais = new Rect(chamber.X0, m - 112, chamber.X0 + 128, m + 112);
        var cc = New("stub_c", kit, theme).WithOutline(new Rect(600, m - 112, c - t, m + 112), chamber).WithBody(chamber)
            .Chamfers(chamber, 96)
            .Block(dais, 16)
            .Post(chamber.X0 + 192, chamber.Y0 + 72, 16).Post(chamber.X0 + 192, chamber.Y1 - 72, 16);
        cc.LampsAlong(e, new Point(chamber.CentreX, m, kit.Sill), 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampAt(dais.CentreX + 48, m, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Mark(MarkerKind.Shrine, dais.CentreX, m, above: 16, yaw: 0)
            .Pack(chamber.X1 - 96, chamber.Y0 + 96);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(Parts.E)
            .WithEntry(Parts.Low("corridor", more: ["shrine"])).WithMapLabel("Shrine");
}
