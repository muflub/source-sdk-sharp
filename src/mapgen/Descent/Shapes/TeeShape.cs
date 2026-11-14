namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The tee (plan §5, connectors): a low junction of three doors (north, east,
/// west). A plain tee; an octagonal hub where the three drifts meet; and a
/// through drift whose stem opens off a niche, with a bay opposite.
/// </summary>
public static class TeeShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "tee");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        Point hub = new(m, m, kit.Sill);

        // a: a plain tee, its width seeded.
        float w = rng.Pick<float>([240, 288]);
        var a = New("tee_a", kit, theme).WithOutline(new Rect(t, m - w / 2, c - t, m + w / 2), new Rect(m - w / 2, m - w / 2, m + w / 2, c - t));
        a.LampsFromDoors(hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).Pack(m, m);
        yield return a.Build();

        // b: an octagonal hub 448 across.
        var octagon = new Rect(m - 224, m - 224, m + 224, m + 224);
        var b = New("tee_b", kit, theme)
            .WithOutline(octagon, new Rect(t, m - 128, c - t, m + 128), new Rect(m - 128, m, m + 128, c - t)).WithBody(octagon)
            .Chamfers(octagon, 128)
            .Post(m, m - 120, 24);
        b.LampsFromDoors(hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).LampAt(m, m - 160, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m + 120, m - 128);
        yield return b.Build();

        // c: a through drift, the stem off a niche and a bay opposite with two posts.
        float bay = rng.Pick<float>([160, 192]);
        var cc = New("tee_c", kit, theme)
            .WithOutline(new Rect(t, m - 128, c - t, m + 128), new Rect(m - 112, m, m + 112, c - t),
                         new Rect(m - 256, m - 128 - bay, m + 256, m - 128), new Rect(m - 192, m + 128, m + 192, m + 224));
        cc.Post(m - 96, m - 128 - bay / 2, 16).Post(m + 96, m - 128 - bay / 2, 16)
            .LampsFromDoors(hub, 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp).LampAt(m, m - 128 - bay / 2, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, m - 128 - bay / 2)
            .Mark(MarkerKind.Barrel, m - 216, m - 128 - bay + 40);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(Parts.NEW)
            .WithEntry(Parts.Low("junction")).WithMapLabel("Junction");
}
