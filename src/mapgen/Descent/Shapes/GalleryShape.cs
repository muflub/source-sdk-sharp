namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The gallery (plan §5, halls): a 1024 hall with a catwalk at the crouched
/// rocket jump's height (B2) along its walls, a container on it, and a rocket
/// shelf (B1) under it with another. One to four doors; which walls the
/// catwalk runs (a U, a ring), the shelf's wall and the hall's outline differ
/// by variant.
/// </summary>
public static class GalleryShape
{
    public const int Height = 1024;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "gallery");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;

        // a: one door; catwalks along the north and south walls, the shelf on the west between them.
        var hall = new Rect(176, 176, c - t, 848);
        var a = New("gallery_a", kit, theme, Parts.E).WithOutline(hall).WithBody(hall);
        a.Catwalk([CellFace.North, CellFace.South], rng.Pick<int>([96, 112]))
            .RocketShelf(CellFace.West, 384, 640, depth: 144, t: rng.Fraction());
        Lamps(a, hall);
        a.Pack(480, 352).Pack(480, 672).Pack(720, 512)
            .Mark(MarkerKind.Barrel, 880, 224);
        yield return a.Build();

        // b: two doors opposite; a ring over the doors, the shelf on the north wall; the hall's corners cut.
        var broad = new Rect(t, 144, c - t, 880);
        var b = New("gallery_b", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad)
            .Chamfers(broad, 128);
        b.Catwalk([CellFace.North, CellFace.East, CellFace.South, CellFace.West], 96)
            .RocketShelf(CellFace.North, 384, 640, depth: 160, t: rng.Fraction());
        Lamps(b, broad);
        b.Pack(320, 320).Pack(704, 704).Pack(704, 320)
            .Mark(MarkerKind.Trap, m, m).Mark(MarkerKind.Dispenser, 256, 720);
        yield return b.Build();

        // c: three doors (north, east, west); an octagon, a catwalk on its east, west and north, the shelf on the south.
        var octagon = new Rect(t, 112, c - t, c - t);
        var cc = New("gallery_c", kit, theme, Parts.NEW).WithOutline(octagon).WithBody(octagon)
            .Chamfers(octagon, 192, Corner.SouthWest, Corner.SouthEast);
        cc.Catwalk([CellFace.North, CellFace.East, CellFace.West], 112)
            .RocketShelf(CellFace.South, 384, 640, depth: 128, t: rng.Fraction());
        Lamps(cc, octagon);
        cc.Pack(320, 400).Pack(704, 400).Pack(m, 720)
            .Mark(MarkerKind.Barrel, 320, 240);
        yield return cc.Build();

        // d: four doors; a square hall, catwalks along the north and south over their doors, the shelf beside the south door.
        var square = new Rect(96, 96, 928, 928);
        var d = New("gallery_d", kit, theme, Parts.NESW)
            .WithOutline(square, Parts.Neck(kit, CellFace.East, 256, 96), Parts.Neck(kit, CellFace.West, 256, 96),
                         Parts.Neck(kit, CellFace.North, 256, 96), Parts.Neck(kit, CellFace.South, 256, 96)).WithBody(square)
            .Chamfers(square, 96);
        d.Catwalk([CellFace.North, CellFace.South], 96)
            .RocketShelf(CellFace.South, 160, 352, depth: 160, t: rng.Fraction());
        Lamps(d, square);
        d.Pack(288, 288).Pack(736, 736).Pack(736, 288)
            .Mark(MarkerKind.Barrel, 288, 736);
        yield return d.Build();
    }

    /// <summary>
    /// A catwalk <paramref name="depth"/> wide at the crouched rocket jump's
    /// top (B2, high enough that a shelf (B1) fits its clear air under it)
    /// along <paramref name="walls"/> of the body, with a reward on each run
    /// that meets no other.
    /// </summary>
    private static RoomBuilder Catwalk(this RoomBuilder b, CellFace[] walls, int depth)
    {
        int h = JumpTable.Of(JumpTier.B2).Ledge(1f);
        Rect r = b.Body;
        b.Holds(JumpTier.B2);
        foreach (CellFace wall in walls)
        {
            bool alongY = wall is CellFace.East or CellFace.West;
            // North and south runs take the corners; east and west fit between them when those are there.
            float from = alongY ? r.Y0 + (walls.Contains(CellFace.South) ? depth : 0) : r.X0;
            float to = alongY ? r.Y1 - (walls.Contains(CellFace.North) ? depth : 0) : r.X1;
            b.WithBrush(JumpFeatures.WallBox(b, wall, from, to, 0, depth, h - JumpFeatures.Slab, h, b.Theme.Block));
        }

        // A reward on every run that does not meet another (a container on the first, shrines on the rest).
        for (int k = 0; k < walls.Length; k++)
        {
            CellFace wall = walls[k];
            bool joined = k > 0 && walls.Take(k).Any(w => (w is CellFace.East or CellFace.West) != (wall is CellFace.East or CellFace.West));
            if (joined)
                continue;
            bool alongY = wall is CellFace.East or CellFace.West;
            float mid = alongY ? r.CentreY : r.CentreX;
            var (lo, hi) = JumpFeatures.WallBounds(b, wall, mid - 32, mid + 32, 0, depth, h, h);
            b.WithMarker(k == 0 ? MarkerKind.Container : MarkerKind.Shrine, new Point((lo.X + hi.X) / 2, (lo.Y + hi.Y) / 2, b.Floor + h));
        }

        return b;
    }

    /// <summary>Hall lamps on a grid over the floor, inside the catwalk.</summary>
    private static void Lamps(RoomBuilder b, Rect r)
    {
        var inner = r.Grow(-160);
        int n = Math.Max(1, (int)MathF.Round(inner.Width / 384));
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                b.LampAt(MathF.Round(inner.X0 + inner.Width * i / n), MathF.Round(inner.Y0 + inner.Depth * j / n), 384, Parts.HallLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([], Jump: true)).WithMapLabel("Gallery");
}
