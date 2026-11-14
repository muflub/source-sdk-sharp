namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The camps (plan M10, §7; family <c>camp</c>, <c>room_role up</c>): the way
/// in at levels 6 and 11, which the boss kills open and the town can send a
/// party to: a lit, settled room with the hoist's cage back to the town in an
/// alcove, a dispenser and a shrine. One door, two opposite and two at a
/// corner at each of the two depths.
/// </summary>
public static class CampShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "camp");
        string[] ids = ["a", "b", "c", "d", "e", "f"];
        int i = 0;
        foreach (int depth in (int[])[6, 11])
            foreach (var (_, doors) in ArrivalShape.DoorSets)
                yield return Build("camp_" + ids[i++], kit, theme, doors, depth, rng);
    }

    private static Room Build(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, int depth, RoomSeed rng)
    {
        Rect body = ArrivalShape.Body(kit);
        CellFace wall = ArrivalShape.FittingWall(doors);
        var b = ArrivalShape.Shell(new RoomBuilder(name, kit, theme).WithHeight(ArrivalShape.Height).WithDoors(doors)
                .WithEntry(new RoomEntry([], MinDepth: depth, MaxDepth: depth)).WithMapLabel("Camp"), body, doors)
            .Chamfers(body, depth == 6 ? 128 : 176);
        float along = wall == CellFace.North ? body.CentreX + rng.Pick<float>([-128, 128]) : body.CentreY + 64;
        b.Cage(body, wall, along, RoomRole.Up);
        // The camp: a dispenser and a shrine by the walls away from the hoist, lanterns over them.
        var (dx, dy) = Stations.Out(body, wall, along + (along > (wall == CellFace.North ? body.CentreX : body.CentreY) ? -192 : 192), -48);
        var (sx, sy) = Stations.Out(body, wall, along, -(wall == CellFace.North ? body.Depth : body.Width) + 64);
        b.Mark(MarkerKind.Dispenser, dx, dy, yaw: Stations.Inward(wall))
            .Mark(MarkerKind.Shrine, sx, sy, yaw: (Stations.Inward(wall) + 180) % 360)
            .LampAt(dx, dy, 160, MineShaft.Lantern).LampAt(sx, sy, 160, MineShaft.Lantern);
        ArrivalShape.Light(b, body);
        return b.Build();
    }
}
