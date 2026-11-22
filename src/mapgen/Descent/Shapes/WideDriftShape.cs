namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The wide drift (plan §5, connectors): a low double-track hallway between
/// doors opposite (west and east), 384 to 512 wide, with a centre row of
/// posts between its tracks. Plain; with a loading bay on one side; and with
/// a raised walk a step up along one wall.
/// </summary>
public static class WideDriftShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "widedrift");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        Point w = Parts.DoorPoint(kit, CellFace.West, 0), e = Parts.DoorPoint(kit, CellFace.East, 0);

        // a: 448 wide, posts every 192 or 224.
        float pitch = rng.Pick<float>([192, 224]);
        var a = New("widedrift_a", kit, theme).WithOutline(new Rect(t, m - 224, c - t, m + 224));
        Posts(a, kit, pitch).Rails(t, c - t, m, 224);
        Lamps(a, kit, m, 128);
        a.Pack(m, m + 196);
        yield return a.Build();

        // b: 512 wide with a loading bay to the south.
        float bay = rng.Pick<float>([320, 384]);
        var bb = new Rect(m - bay / 2, m - 256 - 128, m + bay / 2, m - 256);
        var b = New("widedrift_b", kit, theme).WithOutline(new Rect(t, m - 256, c - t, m + 256), bb);
        Posts(b, kit, 224).Rails(t, c - t, m, 224);
        Lamps(b, kit, m, 144);
        b.LampAt(m, bb.CentreY, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, bb.CentreY)
            .Mark(MarkerKind.Barrel, bb.X0 + 40, bb.Y0 + 40);
        yield return b.Build();

        // c: 448 wide, a raised walk a step up along the north wall, its corners cut.
        var hall = new Rect(t, m - 224, c - t, m + 224);
        var cc = New("widedrift_c", kit, theme).WithOutline(hall)
            .Block(new Rect(160, m + 128, c - 160, m + 224), 16);
        Posts(cc, kit, 192).Rails(t, c - t, m - 48, 192);
        Lamps(cc, kit, m, 128);
        cc.Mark(MarkerKind.PackSpawn, m, m + 176, above: 16);
        yield return cc.Build();
    }

    /// <summary>The centre row of posts, clear of both doors' approaches.</summary>
    private static RoomBuilder Posts(RoomBuilder b, RoomKit kit, float pitch)
    {
        float c = kit.Cell, m = c / 2f, reach = kit.WallDepth + RoomBuilder.DoorApproach + 96;
        for (float x = m; x >= reach; x -= pitch)
            b.Post(x, m, 16);
        for (float x = m + pitch; x <= c - reach; x += pitch)
            b.Post(x, m, 16);
        return b;
    }

    /// <summary>
    /// A track's two rails either side of the posts: strips on the floor that are drawn but not solid, so
    /// a giant's box crosses them (a solid strip is a wall to the level's navigation, brief p7).
    /// </summary>
    private static RoomBuilder Rails(this RoomBuilder b, float x0, float x1, float y, float spread)
    {
        x0 += RoomBuilder.DoorApproach;
        x1 -= RoomBuilder.DoorApproach;
        var rails = new List<Brush>();
        foreach (float centre in (float[])[y - spread / 2, y + spread / 2])
            foreach (float rail in (float[])[centre - 32, centre + 24])
                rails.Add(Brush.Box(new Point(x0, rail, b.Floor), new Point(x1, rail + 8, b.Floor + 8), b.Theme.Block));
        return b.WithStrips(rails);
    }

    /// <summary>Two rows of lamps over the tracks, every 256.</summary>
    private static void Lamps(RoomBuilder b, RoomKit kit, float y, float spread)
    {
        Point w = Parts.DoorPoint(kit, CellFace.West, 0), e = Parts.DoorPoint(kit, CellFace.East, 0);
        b.LampsAlong(w with { Y = y - spread }, e with { Y = y - spread }, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampsAlong(w with { Y = y + spread }, e with { Y = y + spread }, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(Parts.WE)
            .WithEntry(Parts.Low("corridor")).WithMapLabel("Haulage");
}
