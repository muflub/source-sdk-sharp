namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The pit room (plan §5, chambers): a sunken centre 128 below terraces that
/// stand round it, with stairs up to each terrace, and stepping pillars
/// rising out of the pit whose tops stand a double jump above the terraces
/// (S2), the last holding a reward. The doors open on the pit floor, so the
/// way between them never climbs. One to four doors; the terraces' layout
/// (a U, two banks, an L, four corners) differs by variant.
/// </summary>
public static class PitShape
{
    public const int Height = 512;

    /// <summary>How far the terraces stand above the pit.</summary>
    public const int Depth = 128;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "pit");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ChamberLampAbove;
        Lamp lamp = Parts.ChamberLamp;

        // a: one door; terraces in a U round the pit (north, west, south).
        var body = new Rect(176, 176, c - t, 848);
        var a = New("pit_a", kit, theme, Parts.E).WithOutline(body).WithBody(body)
            .Terrace(new Rect(176, 688, 816, 848)).Terrace(new Rect(176, 176, 816, 336)).Terrace(new Rect(176, 336, 336, 688))
            .StairsUp(new Rect(624, 560, 768, 688), Compass.North, Depth)
            .StairsUp(new Rect(624, 336, 768, 464), Compass.South, Depth);
        a.Pillars([(432, 480), (560, 576)], rng);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(400, m, kit.Sill), 288, above, lamp)
            .LampAt(320, 768, above, lamp).LampAt(320, 256, above, lamp).LampAt(700, 768, above, lamp).LampAt(700, 256, above, lamp).LampAt(256, m, above, lamp)
            .Mark(MarkerKind.PackSpawn, 400, 784, above: Depth).Mark(MarkerKind.PackSpawn, 400, 240, above: Depth).Pack(880, 760)
            .Mark(MarkerKind.Barrel, 880, 256);
        yield return a.Build();

        // b: two doors opposite; banks north and south, the pit a trench between them.
        float bank = rng.Pick<float>([160, 192]);
        var room = new Rect(t, 160, c - t, 864);
        var b = New("pit_b", kit, theme, Parts.WE).WithOutline(room).WithBody(room)
            .Chamfers(room, 112)
            .Terrace(new Rect(208, room.Y1 - bank, 816, room.Y1)).Terrace(new Rect(208, room.Y0, 816, room.Y0 + bank))
            .StairsUp(new Rect(208, room.Y1 - bank - 128, 336, room.Y1 - bank), Compass.North, Depth)
            .StairsUp(new Rect(688, room.Y0 + bank, 816, room.Y0 + bank + 128), Compass.South, Depth);
        b.Pillars([(560, room.Y1 - bank - 72)], rng);
        b.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(m, room.Y1 - bank / 2, above, lamp).LampAt(m, room.Y0 + bank / 2, above, lamp)
            .LampAt(256, room.Y1 - bank / 2, above, lamp).LampAt(768, room.Y0 + bank / 2, above, lamp)
            .Pack(400, m).Mark(MarkerKind.PackSpawn, 640, room.Y0 + bank / 2, above: Depth).Mark(MarkerKind.PackSpawn, 400, room.Y1 - bank / 2, above: Depth)
            .Mark(MarkerKind.Trap, 640, m);
        yield return b.Build();

        // c: two doors at a corner; an L of terraces west and south, the pit to the north-east.
        var sq = new Rect(160, 160, c - t, c - t);
        var cc = New("pit_c", kit, theme, Parts.NE).WithOutline(sq).WithBody(sq)
            .Chamfers(sq, 128, Corner.NorthWest, Corner.SouthEast)
            .Terrace(new Rect(160, 288, 352, 880)).Terrace(new Rect(160, 160, 880, 352))
            .StairsUp(new Rect(352, 752, 480, 880), Compass.West, Depth)
            .StairsUp(new Rect(752, 352, 880, 480), Compass.South, Depth);
        cc.Pillars([(560, 560)], rng);
        cc.LampsFromDoors(new Point(m + 96, m + 96, kit.Sill), 288, above, lamp).LampAt(256, 576, above, lamp).LampAt(576, 256, above, lamp)
            .LampAt(256, 256, above, lamp).LampAt(800, 800, above, lamp)
            .Mark(MarkerKind.PackSpawn, 256, 640, above: Depth).Mark(MarkerKind.PackSpawn, 640, 256, above: Depth).Pack(720, 720)
            .Mark(MarkerKind.Barrel, 448, 912);
        yield return cc.Build();

        // d: four doors; a terrace in each corner, the pit a cross, a pillar by each terrace.
        float k = rng.Pick<float>([304, 320]);
        var d = New("pit_d", kit, theme, Parts.NESW).WithOutline(new Rect(t, t, c - t, c - t));
        // A terrace in each corner, its stairs on its side toward the pit's middle, clear of the door bands.
        foreach (Corner corner in Enum.GetValues<Corner>())
        {
            var (cx, cy, sx, sy) = Parts.CornerOf(new Rect(t, t, c - t, c - t), corner);
            var terrace = new Rect(Math.Min(cx, cx + sx * (k - t)), Math.Min(cy, cy + sy * (k - t)), Math.Max(cx, cx + sx * (k - t)), Math.Max(cy, cy + sy * (k - t)));
            d.Terrace(terrace);
            float ix = sx > 0 ? terrace.X1 : terrace.X0, iy = sy > 0 ? terrace.Y1 : terrace.Y0;
            // Along the terrace's inner edge in y, beside its inner corner in x, climbing back toward it.
            var stairs = new Rect(Math.Min(ix, ix - sx * 112), Math.Min(iy, iy + sy * 128), Math.Max(ix, ix - sx * 112), Math.Max(iy, iy + sy * 128));
            d.StairsUp(stairs, sy > 0 ? Compass.South : Compass.North, Depth);
            d.LampAt(terrace.CentreX, terrace.CentreY, above, lamp);
        }

        d.Pillars([(k + 88, k + 88)], rng);
        d.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp)
            .Mark(MarkerKind.PackSpawn, k / 2, k / 2, above: Depth).Mark(MarkerKind.PackSpawn, c - k / 2, c - k / 2, above: Depth).Pack(m + 96, m + 96)
            .Mark(MarkerKind.Shrine, c - k / 2, k / 2, above: Depth, yaw: 135)
            .Mark(MarkerKind.Barrel, m + 120, m - 96);
        yield return d.Build();
    }

    /// <summary>A terrace: a box <see cref="Depth"/> high over <paramref name="r"/>.</summary>
    private static RoomBuilder Terrace(this RoomBuilder b, Rect r) => b.Block(r, Depth, b.Theme.Wall);

    /// <summary>
    /// Stepping pillars out of the pit, their tops a double jump (S2) above
    /// the terraces; the last holds a container.
    /// </summary>
    private static void Pillars(this RoomBuilder b, (float X, float Y)[] at, RoomSeed rng)
    {
        int top = Depth + JumpTable.Of(JumpTier.S2).Ledge(rng.Fraction());
        b.Holds(JumpTier.S2);
        for (int i = 0; i < at.Length; i++)
        {
            b.WithBrush(Brush.Prism(at[i].X, at[i].Y, 48, 12, b.Floor, b.Floor + top, b.Theme.Block));
            if (i == at.Length - 1)
                b.Mark(MarkerKind.Container, at[i].X, at[i].Y, above: top);
        }
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([])).WithMapLabel("Pit");
}
