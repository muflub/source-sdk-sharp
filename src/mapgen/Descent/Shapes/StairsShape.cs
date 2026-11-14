namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The way down (tag <c>stairs</c>, <c>room_role down</c>), a grey
/// placeholder for the cage, the ramps, the sumps and the chutes: one door or
/// two.
/// </summary>
public static class StairsShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        yield return RoleRooms.Build("stairs_a", kit, theme, RoomRole.Down, ShapeParts.One, new RoomEntry(["stairs"]), "Way down");
        yield return RoleRooms.Build("stairs_b", kit, theme, RoomRole.Down, ShapeParts.Straight, new RoomEntry(["stairs"]), "Way down");
    }
}
