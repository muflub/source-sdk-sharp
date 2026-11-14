namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The stope (plan §5, halls): a 1024 hall worked in terraces at 128, 256 and
/// 384 up one side, each reached by stairs from the one below (so everyone
/// climbs them), and a rocket shelf (B1) with a container on the opposite
/// wall. One to three doors; which wall the terraces climb, their run and
/// the hall's outline differ by variant.
/// </summary>
public static class StopeShape
{
    public const int Height = 1024;

    /// <summary>The terraces' heights above the floor.</summary>
    public static IReadOnlyList<int> Steps { get; } = [128, 256, 384];

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "stope");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;

        // a: one door; terraces climbing the north side westward, a shrine at the top.
        var hall = new Rect(160, 160, c - t, 880);
        var a = New("stope_a", kit, theme, Parts.E).WithOutline(hall).WithBody(hall)
            .Chamfers(hall, 128, Corner.SouthWest);
        Terraces(a, new Rect(160, 160, 816, 880), north: true, rng.Pick<float>([288, 336]));
        a.RocketShelf(CellFace.South, 320, 576, depth: 128, t: rng.Fraction());
        Lamps(a, hall);
        a.Mark(MarkerKind.Shrine, hall.X0 + 64, hall.Y1 - 48, above: Steps[2], yaw: 0)
            .Pack(560, 400).Pack(800, 304).Mark(MarkerKind.PackSpawn, 752, 640, above: Steps[0])
            .Mark(MarkerKind.Barrel, 880, 240);
        yield return a.Build();

        // b: two doors opposite; terraces up the north side, the hall's north corners cut.
        var broad = new Rect(t, 160, c - t, c - t);
        var b = New("stope_b", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad)
            .Chamfers(broad, 96, Corner.SouthWest, Corner.SouthEast);
        Terraces(b, new Rect(160, 160, 880, c - t), north: true, 336);
        b.RocketShelf(CellFace.South, 384, 640, depth: 128, t: rng.Fraction());
        Lamps(b, broad);
        b.Pack(320, 400).Pack(704, 400).Mark(MarkerKind.PackSpawn, 700, 860, above: Steps[1])
            .Mark(MarkerKind.Trap, m, m);
        yield return b.Build();

        // c: three doors (west, east, south); terraces up the north side of a squarer hall.
        var room = new Rect(t, t, c - t, 864);
        var cc = New("stope_c", kit, theme, Parts.WES).WithOutline(room).WithBody(room)
            .Chamfers(room, 160, Corner.NorthWest, Corner.NorthEast);
        Terraces(cc, new Rect(208, t, 816, 864), north: true, 288);
        cc.RocketShelf(CellFace.East, 128, 352, depth: 128, t: rng.Fraction());
        Lamps(cc, room);
        cc.Pack(288, 320).Pack(736, 320).Mark(MarkerKind.PackSpawn, 624, 720, above: Steps[1])
            .Mark(MarkerKind.Barrel, 176, 176);
        yield return cc.Build();

        // d: two doors at a corner; terraces up the west side of an L-shaped hall.
        var south = new Rect(160, 160, c - t, 672);
        var north = new Rect(160, 160, 848, c - t);
        var d = New("stope_d", kit, theme, Parts.NE).WithOutline(south, north).WithBody(new Rect(160, 160, c - t, c - t))
            .Chamfers(south, 128, Corner.SouthEast);
        Terraces(d, new Rect(160, 160, 880, 880), north: false, 288);
        d.RocketShelf(CellFace.South, 560, 816, depth: 128, t: rng.Fraction());
        Lamps(d, new Rect(160, 160, c - t, c - t));
        d.Pack(640, 320).Pack(720, 760).Mark(MarkerKind.PackSpawn, 208, 720, above: Steps[2])
            .Mark(MarkerKind.Dispenser, 880, 224);
        yield return d.Build();
    }

    /// <summary>
    /// The three terraces up the north side of <paramref name="r"/> (or its
    /// west side), each <paramref name="deep"/> narrower than the one below,
    /// with stairs climbing from each level to the next along its front.
    /// </summary>
    private static void Terraces(RoomBuilder b, Rect r, bool north, float deep)
    {
        float step = deep / 3;
        for (int i = 0; i < Steps.Count; i++)
        {
            float back = deep - step * i;
            Rect terrace = north ? new Rect(r.X0, r.Y1 - back, r.X1 - 128 * i, r.Y1) : new Rect(r.X0, r.Y0 + 128 * i, r.X0 + back, r.Y1);
            b.Block(terrace, Steps[i], b.Theme.Wall);
            int from = i == 0 ? 0 : Steps[i - 1];
            // The flight up to this terrace stands on the level below, at the terrace's open end:
            // on the floor in front of the first, on the terrace below's top for the others.
            float run = i == 0 ? 128 : step;
            Rect stairs = north
                ? new Rect(terrace.X1 - 128, terrace.Y0 - run, terrace.X1, terrace.Y0)
                : new Rect(terrace.X1, terrace.Y0, terrace.X1 + run, terrace.Y0 + 128);
            if (i == 0)
                b.StairsUp(stairs, north ? Compass.North : Compass.West, Steps[0]);
            else
                b.StairsBetween(stairs, north ? Compass.North : Compass.West, from, Steps[i]);
        }
    }

    /// <summary>Hall lamps on a grid over the floor, and one over the terraces' top.</summary>
    private static void Lamps(RoomBuilder b, Rect r)
    {
        int nx = Math.Max(1, (int)MathF.Round(r.Width / 448)), ny = Math.Max(1, (int)MathF.Round(r.Depth / 448));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                b.LampAt(MathF.Round(r.X0 + r.Width * (i + 0.5f) / nx), MathF.Round(r.Y0 + r.Depth * (j + 0.5f) / ny), Parts.HallLampAbove, Parts.HallLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([], Jump: true)).WithMapLabel("Stope");
}
