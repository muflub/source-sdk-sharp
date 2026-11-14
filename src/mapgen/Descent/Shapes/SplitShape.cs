namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The split level (plan §5, chambers): a 512 room with a mezzanine (S2, a
/// deck at the double jump's height with stairs at its far end for everyone
/// else) along a wall without a door. One to three doors; the deck's wall,
/// its length and depth and the room's outline differ by variant.
/// </summary>
public static class SplitShape
{
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "split");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ChamberLampAbove;
        Lamp lamp = Parts.ChamberLamp;
        int deck = JumpTable.Of(JumpTier.S2).Ledge();

        // a: one door; a deep room, the deck along the west wall with a container on it.
        var deep = new Rect(176, 160, c - t, 864);
        float depthA = rng.Pick<float>([192, 224]);
        var a = New("split_a", kit, theme, Parts.E).WithOutline(deep).WithBody(deep)
            .Chamfers(deep, 96, Corner.SouthEast, Corner.NorthEast)
            .Mezzanine(CellFace.West, 192, 576, depth: (int)depthA);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(deep.X0 + 96, m, kit.Sill), 288, above, lamp)
            .LampAt(704, 256, above, lamp).LampAt(704, 768, above, lamp).LampAt(deep.X0 + 96, 800, above, lamp)
            .Mark(MarkerKind.Container, deep.X0 + depthA / 2, 288, above: deck)
            .Pack(560, 304).Pack(560, 720)
            .Mark(MarkerKind.Barrel, 820, 216);
        yield return a.Build();

        // b: two doors opposite; the deck along the north wall, the room cut back at its south corners.
        var broad = new Rect(t, 160, c - t, 880);
        float from = rng.Pick<float>([144, 176, 208]);
        var b = New("split_b", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad)
            .Chamfers(broad, 128, Corner.SouthWest, Corner.SouthEast)
            .Mezzanine(CellFace.North, from, from + 400, depth: 240);
        b.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(from + 200, 760, above, lamp).LampAt(m, 256, above, lamp)
            .Pack(400, 288).Pack(704, 336).Pack(800, 560)
            .Mark(MarkerKind.Dispenser, from + 120, 760, above: deck).Mark(MarkerKind.Trap, m, 448);
        yield return b.Build();

        // c: two doors at a corner; an L, the deck along the south wall.
        var south = new Rect(160, 160, c - t, 656);
        var north = new Rect(352, 160, 864, c - t);
        var cc = New("split_c", kit, theme, Parts.NE).WithOutline(south, north).WithBody(south)
            .Chamfers(north, 96, Corner.NorthEast)
            .Mezzanine(CellFace.South, 192, 576, depth: 208);
        cc.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(320, 256, above, lamp).LampAt(864, 256, above, lamp)
            .Pack(272, 560).Pack(720, 800).Pack(880, 304)
            .Mark(MarkerKind.Barrel, 416, 896);
        yield return cc.Build();

        // d: three doors (west, east, south); the deck along the north wall over a long room.
        var room = new Rect(t, t, c - t, 832);
        var d = New("split_d", kit, theme, Parts.WES).WithOutline(room).WithBody(room)
            .Chamfers(room, 160, Corner.NorthWest, Corner.NorthEast)
            .Mezzanine(CellFace.North, 224, 640, depth: 208);
        d.LampsFromDoors(new Point(m, m, kit.Sill), 288, above, lamp).LampAt(432, 720, above, lamp).LampAt(224, 224, above, lamp).LampAt(800, 224, above, lamp)
            .Pack(288, 352).Pack(736, 352).Pack(512, 576)
            .Mark(MarkerKind.Container, 304, 728, above: deck).Mark(MarkerKind.Barrel, 880, 640);
        yield return d.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([])).WithMapLabel("Split level");
}
