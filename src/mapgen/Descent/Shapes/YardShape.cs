namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The yard (plan §5, chambers): a broad 512 floor of rail tracks (two strips
/// a step high each), a turntable plinth where tracks meet, and in some a
/// loading dock a few steps up; the themes add the carts. One to four doors.
/// </summary>
public static class YardShape
{
    public const int Height = 512;

    /// <summary>The height of a rail strip and of the turntable plinth.</summary>
    public const int Rail = 8;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "yard");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ChamberLampAbove;
        Lamp lamp = Parts.ChamberLamp;
        float stop = t + RoomBuilder.DoorApproach;

        // a: one door; a siding of two tracks ending at a turntable.
        var siding = new Rect(160, 160, c - t, 864);
        float table = rng.Pick<float>([128, 144]);
        var a = New("yard_a", kit, theme, Parts.E).WithOutline(siding).WithBody(siding)
            .Chamfers(siding, 160, Corner.NorthWest, Corner.SouthWest)
            .Track(Compass.East, 400, 400, c - stop).Track(Compass.East, 624, 400, c - stop)
            .Turntable(352, m, table);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(352, m, kit.Sill), 256, above, lamp)
            .LampAt(640, 256, above, lamp).LampAt(640, 768, above, lamp).LampAt(320, 256, above, lamp).LampAt(320, 768, above, lamp)
            .Pack(560, 512).Pack(352, 760).Pack(352, 264)
            .Mark(MarkerKind.Barrel, 880, 224).Mark(MarkerKind.Dispenser, 880, 800);
        yield return a.Build();

        // b: three doors (north, east, west); a through track, a spur north, a loading dock a step up along the south wall.
        var yard = new Rect(t, 160, c - t, c - t);
        var b = New("yard_b", kit, theme, Parts.NEW).WithOutline(yard).WithBody(yard)
            .Chamfers(yard, 128, Corner.NorthWest, Corner.NorthEast)
            .Track(Compass.East, m, stop, c - stop).Track(Compass.North, m, m + 80, c - stop)
            .Dock(new Rect(176, 160, 848, 304), 48, Compass.North);
        b.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(320, 232, above, lamp).LampAt(704, 232, above, lamp).LampAt(256, 784, above, lamp).LampAt(768, 784, above, lamp)
            .Pack(288, 640).Pack(736, 704).Mark(MarkerKind.PackSpawn, 704, 232, above: 48)
            .Mark(MarkerKind.Barrel, 240, 232, above: 48).Mark(MarkerKind.Barrel, 880, 640);
        yield return b.Build();

        // c: four doors; an octagon, tracks crossing at a turntable.
        var octagon = new Rect(96, 96, 928, 928);
        var cc = New("yard_c", kit, theme, Parts.NESW)
            .WithOutline(octagon, Parts.Neck(kit, CellFace.East, 256, 96), Parts.Neck(kit, CellFace.West, 256, 96),
                         Parts.Neck(kit, CellFace.North, 256, 96), Parts.Neck(kit, CellFace.South, 256, 96)).WithBody(octagon)
            .Chamfers(octagon, 208)
            .Track(Compass.East, m, stop, c - stop).Track(Compass.North, m, stop, c - stop)
            .Turntable(m, m, rng.Pick<float>([160, 176]));
        cc.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(288, 288, above, lamp).LampAt(736, 736, above, lamp).LampAt(288, 736, above, lamp).LampAt(736, 288, above, lamp)
            .Pack(288, 288).Pack(736, 736).Pack(288, 736)
            .Mark(MarkerKind.Barrel, 736, 288);
        yield return cc.Build();

        // d: two doors at a corner; an L, a curve of track and a dock in the far corner.
        var south = new Rect(160, 160, c - t, 640);
        var north = new Rect(384, 160, 848, c - t);
        var d = New("yard_d", kit, theme, Parts.NE).WithOutline(south, north).WithBody(new Rect(160, 160, c - t, c - t))
            .Chamfers(south, 128, Corner.SouthWest)
            .Track(Compass.East, m, m, c - stop).Track(Compass.North, m, m, c - stop)
            .Turntable(m, m, 128)
            .Dock(new Rect(176, 352, 352, 640), 48, Compass.East);
        d.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(264, 496, above, lamp).LampAt(800, 256, above, lamp).LampAt(400, 256, above, lamp)
            .Pack(704, 288).Pack(640, 800).Mark(MarkerKind.PackSpawn, 264, 480, above: 48)
            .Mark(MarkerKind.Dispenser, 448, 224);
        yield return d.Build();
    }

    /// <summary>A track: two rail strips 64 apart either side of the line at <paramref name="across"/>, from <paramref name="from"/> to <paramref name="to"/> along <paramref name="along"/> (east or north).</summary>
    private static RoomBuilder Track(this RoomBuilder b, Compass along, float across, float from, float to)
    {
        foreach (float rail in (float[])[across - 36, across + 28])
            b.WithBox(along == Compass.East ? new Point(from, rail, b.Floor) : new Point(rail, from, b.Floor),
                      along == Compass.East ? new Point(to, rail + 8, b.Floor + Rail) : new Point(rail + 8, to, b.Floor + Rail));
        return b;
    }

    /// <summary>A turntable's round plinth, a rail's height.</summary>
    private static RoomBuilder Turntable(this RoomBuilder b, float x, float y, float radius) =>
        b.WithBrush(Brush.Prism(x, y, radius, 16, b.Floor, b.Floor + Rail, b.Theme.Block));

    /// <summary>A loading dock over <paramref name="r"/>, <paramref name="height"/> up, with steps down its middle toward <paramref name="front"/>.</summary>
    private static RoomBuilder Dock(this RoomBuilder b, Rect r, int height, Compass front)
    {
        b.Block(r, height, b.Theme.Wall);
        float run = height / 16 * 32;
        Rect steps = front switch
        {
            Compass.North => new(r.CentreX - 64, r.Y1, r.CentreX + 64, r.Y1 + run),
            Compass.South => new(r.CentreX - 64, r.Y0 - run, r.CentreX + 64, r.Y0),
            Compass.East => new(r.X1, r.CentreY - 64, r.X1 + run, r.CentreY + 64),
            _ => new(r.X0 - run, r.CentreY - 64, r.X0, r.CentreY + 64),
        };
        Compass back = front switch { Compass.North => Compass.South, Compass.South => Compass.North, Compass.East => Compass.West, _ => Compass.East };
        return b.StairsUp(steps, back, height);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([])).WithMapLabel("Yard");
}
