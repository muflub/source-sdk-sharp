namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The crypt (plan §7, levels 9 and 14: "low crypt drifts"): a low combat
/// room, not a hallway, a vaulted passage lined with burial niches either
/// side. Two doors opposite; an L of two doors at a corner; a cross-vault of
/// four doors round a hub with a tomb in each inner corner; and one door, an
/// ossuary chamber with a shrine on a dais at its far end. From level 6
/// down, where the mine has broken into older things (M4).
/// </summary>
public static class CryptShape
{
    /// <summary>The first depth a crypt stands at.</summary>
    public const int FirstDepth = 6;

    /// <summary>A burial niche: its width along the wall and its depth into it.</summary>
    private const float NicheWidth = 128, NicheDepth = 96;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "crypt");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ConnectorLampAbove;
        Lamp lamp = Parts.ConnectorLamp;

        // a: two doors opposite; a passage with niches along both walls, their pitch seeded.
        float half = rng.Pick<float>([144, 160]);
        float pitch = rng.Pick<float>([192, 224]);
        var passage = new Rect(t, m - half, c - t, m + half);
        var a = New("crypt_a", kit, theme, Parts.WE).WithOutline([passage, .. Niches(passage, pitch, north: true, south: true)]).WithBody(passage);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.West, 0), Parts.DoorPoint(kit, CellFace.East, 0), 256, above, lamp)
            .Pack(384, m).Pack(640, m)
            .Mark(MarkerKind.Container, m, m + half + NicheDepth / 2);
        yield return a.Build();

        // b: two doors at a corner; an L with niches on its outer walls.
        var east = new Rect(m - half, m - half, c - t, m + half);
        var north = new Rect(m - half, m - half, m + half, c - t);
        var b = New("crypt_b", kit, theme, Parts.NE)
            .WithOutline([east, north, .. Niches(east with { X0 = m + half }, pitch, north: false, south: true), .. Niches(north with { Y0 = m + half }, pitch, west: true)])
            .WithBody(new Rect(m - half, m - half, m + half, m + half))
            .Chamfers(new Rect(m - half, m - half, c - t, c - t), 96, Corner.SouthWest);
        b.LampsFromDoors(new Point(m, m, kit.Sill), 256, above, lamp)
            .Pack(m + 256, m).Pack(m, m + 256)
            .Mark(MarkerKind.Trap, m, m);
        yield return b.Build();

        // c: four doors; a cross-vault, a square hub with a tomb in each inner corner.
        float arm = 128, hub = rng.Pick<float>([208, 224]);
        var hubRect = new Rect(m - hub, m - hub, m + hub, m + hub);
        var cc = New("crypt_c", kit, theme, Parts.NESW)
            .WithOutline(new Rect(t, m - arm, c - t, m + arm), new Rect(m - arm, t, m + arm, c - t), hubRect).WithBody(hubRect)
            .Chamfers(hubRect, 64);
        foreach (float x in (float[])[m - hub + 56, m + hub - 56])
            foreach (float y in (float[])[m - hub + 56, m + hub - 56])
                cc.Block(new Rect(x - 32, y - 32, x + 32, y + 32), 32);
        cc.LampsFromDoors(new Point(m, m, kit.Sill), 256, above, lamp)
            .Pack(m - 160, m).Pack(m + 160, m)
            .Mark(MarkerKind.Shrine, m, m, yaw: 90);
        yield return cc.Build();

        // d: one door; a neck into an ossuary with niches either side and a shrine on a dais.
        var ossuary = new Rect(176, m - 208, 656, m + 208);
        var dais = new Rect(ossuary.X0, m - 96, ossuary.X0 + 112, m + 96);
        var d = New("crypt_d", kit, theme, Parts.E)
            .WithOutline([new Rect(600, m - 112, c - t, m + 112), ossuary, .. Niches(ossuary with { X0 = ossuary.X0 + 128, X1 = ossuary.X1 - 32 }, 160, north: true, south: true)])
            .WithBody(ossuary)
            .Chamfers(ossuary, 80, Corner.SouthWest, Corner.NorthWest)
            .Block(dais, 16);
        d.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(ossuary.CentreX, m, kit.Sill), 256, above, lamp)
            .LampAt(dais.CentreX + 64, m, above, lamp)
            .Mark(MarkerKind.Shrine, dais.CentreX, m, above: 16, yaw: 0)
            .Pack(ossuary.CentreX + 64, m - 128).Pack(ossuary.CentreX + 64, m + 128);
        yield return d.Build();
    }

    /// <summary>
    /// Burial niches cut into the walls of <paramref name="r"/> every
    /// <paramref name="pitch"/>, kept a door's approach and a niche's width
    /// from its ends: into the north wall, the south, or the west.
    /// </summary>
    private static IEnumerable<Rect> Niches(Rect r, float pitch, bool north = false, bool south = false, bool west = false)
    {
        float margin = RoomBuilder.DoorApproach + NicheWidth;
        if (north || south)
            for (float x = r.X0 + margin; x + NicheWidth <= r.X1 - margin + NicheWidth / 2; x += pitch)
            {
                if (north)
                    yield return new Rect(x, r.Y1, x + NicheWidth, r.Y1 + NicheDepth);
                if (south)
                    yield return new Rect(x, r.Y0 - NicheDepth, x + NicheWidth, r.Y0);
            }

        if (west)
            for (float y = r.Y0 + margin; y + NicheWidth <= r.Y1 - margin + NicheWidth / 2; y += pitch)
                yield return new Rect(r.X0 - NicheDepth, y, r.X0, y + NicheWidth);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(DriftShape.Height).WithDoors(doors)
            .WithEntry(new RoomEntry([], MinDepth: FirstDepth, Low: true)).WithMapLabel("Crypt");
}
