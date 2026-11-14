namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The cross (plan §5, connectors): a low crossing of four doors. A plain
/// cross; a round hub where four drifts meet; and a square crossing round a
/// central pier, so no door sees the one opposite.
/// </summary>
public static class CrossShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "cross");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        Point hub = new(m, m, kit.Sill);

        // a: a plain cross, its width seeded.
        float w = rng.Pick<float>([240, 288]);
        var a = New("cross_a", kit, theme).WithOutline(new Rect(t, m - w / 2, c - t, m + w / 2), new Rect(m - w / 2, t, m + w / 2, c - t));
        a.LampsFromDoors(hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).Pack(m, m);
        yield return a.Build();

        // b: a round hub 512 across.
        var round = new Rect(m - 256, m - 256, m + 256, m + 256);
        var b = New("cross_b", kit, theme)
            .WithOutline(round, new Rect(t, m - 128, c - t, m + 128), new Rect(m - 128, t, m + 128, c - t)).WithBody(round)
            .RoundCorners(round, 128);
        b.LampsFromDoors(hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).Pack(m + 160, m + 160);
        yield return b.Build();

        // c: a 640 square crossing round a pier.
        float pier = rng.Pick<float>([64, 80]);
        var square = new Rect(m - 320, m - 320, m + 320, m + 320);
        var cc = New("cross_c", kit, theme)
            .WithOutline(square, new Rect(t, m - 128, c - t, m + 128), new Rect(m - 128, t, m + 128, c - t)).WithBody(square)
            .Chamfers(square, 96)
            .Post(m, m, pier);
        foreach (var (x, y) in ((float, float)[])[(m - 192, m - 192), (m + 192, m - 192), (m + 192, m + 192), (m - 192, m + 192)])
            cc.LampAt(x, y, Parts.ConnectorLampAbove, Parts.ConnectorLamp);
        cc.LampsFromDoors(hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m - 200, m + 200)
            .Mark(MarkerKind.Trap, m + 200, m - 200);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(Parts.NESW)
            .WithEntry(Parts.Low("junction")).WithMapLabel("Crossing");
}
