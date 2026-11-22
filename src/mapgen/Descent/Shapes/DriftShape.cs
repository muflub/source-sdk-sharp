namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The drift (plan §5, connectors): a straight low hallway between two doors
/// opposite, 240 to 384 wide. Its variants differ in outline: a plain bore, one
/// with a loading bay off its side, and one that swells into a short cut
/// chamber with posts at its corners. One lamp every 256 along it (§6, level
/// 1's spacing), and at most one pack (it is a corridor).
/// </summary>
public static class DriftShape
{
    /// <summary>The drift's height class: low (plan §3.1).</summary>
    public const int Height = 256;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "drift");
        // On level 5's 512 kit the drift is a room of 2 by 2 cells (brief p18), its bore on the south cell's axis.
        float c = Parts.Span(kit), t = kit.WallDepth, m = c / 2f, ay = kit.Cell / 2f;

        // a: a plain bore, its width seeded.
        float w = rng.Pick<float>([256, 288, 320]);
        var a = New("drift_a", kit, theme).WithOutline(new Rect(t, ay - w / 2, c - t, ay + w / 2));
        a.LampsAlong(Parts.DoorPoint(a, CellFace.West, 0), Parts.DoorPoint(a, CellFace.East, 0), 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, ay);
        yield return a.Build();

        // b: a loading bay off one side, its place along the drift seeded.
        float bx = rng.Pick<float>([320, 384, 448]);
        var bay = new Rect(bx, ay + 120, bx + 288, ay + 320);
        var b = New("drift_b", kit, theme).WithOutline(new Rect(t, ay - 120, c - t, ay + 120), bay)
            .WithFocus(new Point(bay.CentreX, bay.CentreY, kit.Sill));
        b.LampsAlong(Parts.DoorPoint(b, CellFace.West, 0), Parts.DoorPoint(b, CellFace.East, 0), 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .LampAt(bay.CentreX, bay.CentreY + 48, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(bay.CentreX, bay.CentreY)
            .Mark(MarkerKind.Barrel, bay.X0 + 40, bay.Y1 - 40);
        yield return b.Build();

        // c: narrow necks swelling into a 448 chamber, posts at its corners and its corners cut. The posts
        // stand 96 off the walls, a giant's width with a skirting: at 64 a post and a chamfer left a slot
        // the navigation reached one node of (depth 3, seed 2, 2026-11-10).
        var hub = new Rect(m - 224, ay - 224, m + 224, ay + 224);
        var cc = New("drift_c", kit, theme).WithOutline(new Rect(t, ay - 120, c - t, ay + 120), hub).WithBody(hub)
            .Chamfers(hub, 96);
        foreach (float x in (float[])[hub.X0 + 112, hub.X1 - 112])
            foreach (float y in (float[])[hub.Y0 + 112, hub.Y1 - 112])
                cc.Post(x, y, 16);
        cc.LampsAlong(Parts.DoorPoint(cc, CellFace.West, 0), Parts.DoorPoint(cc, CellFace.East, 0), 256, Parts.ConnectorLampAbove, Parts.ConnectorLamp)
            .Pack(m, ay + 112);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).Seat(Parts.WE)
            .WithEntry(Parts.Low("corridor")).WithMapLabel("Drift");
}
