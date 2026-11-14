namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The bend (plan §5, connectors): a low quarter turn between doors on two
/// adjacent walls (north and east). A square elbow; a wide elbow cut on the
/// diagonal inside and out, so it reads as a curve; and a sweep round a rock
/// pillar through a room cut off its far corner.
/// </summary>
public static class BendShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "bend");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        Point n = Parts.DoorPoint(kit, CellFace.North, 0), e = Parts.DoorPoint(kit, CellFace.East, 0), hub = new(m, m, kit.Sill);

        // a: a square elbow, its width seeded.
        float w = rng.Pick<float>([256, 288]);
        var a = New("bend_a", kit, theme).WithOutline(new Rect(m - w / 2, m - w / 2, m + w / 2, c - t), new Rect(m - w / 2, m - w / 2, c - t, m + w / 2));
        a.LampsAlong(n, hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).LampsAlong(hub, e, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, m);
        yield return a.Build();

        // b: a wide elbow, its outer corner cut and its inner corner opened on the diagonal.
        float h = 160;
        var corner = new Rect(m - h, m - h, m + h, m + h);
        var inner = new Rect(m + h, m + h, m + h + 128, m + h + 128);
        var b = New("bend_b", kit, theme)
            .WithOutline(new Rect(m - h, m - h, m + h, c - t), new Rect(m - h, m - h, c - t, m + h), inner)
            .Solid((inner.X1, inner.Y0), (inner.X1, inner.Y1), (inner.X0, inner.Y1));
        b.Chamfers(corner, 192, Corner.SouthWest)
            .LampsAlong(n, hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).LampsAlong(hub, e, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m + 96, m + 96);
        yield return b.Build();

        // c: a sweep round a rock pillar, through a room whose south-west is cut away.
        var room = new Rect(m - 208, m - 208, c - t, c - t);
        float px = rng.Pick<float>([744, 760]);
        var cc = New("bend_c", kit, theme).WithOutline(room).WithBody(room)
            .Chamfers(room, 224, Corner.SouthWest)
            .Pillar(px, px, 96, 10);
        cc.LampAt(m, m, Parts.ConnectorLampAbove, Parts.ConnectorLamp).LampAt(m, 840, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampAt(840, m, Parts.ConnectorLampAbove, Parts.ConnectorLamp).LampAt(912, 912, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m - 64, m + 64)
            .Mark(MarkerKind.Barrel, 912, 900);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(Parts.NE)
            .WithEntry(Parts.Low("corridor")).WithMapLabel("Bend");
}
