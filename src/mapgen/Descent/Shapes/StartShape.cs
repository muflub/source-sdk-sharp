namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The way in (tag <c>start</c>, <c>room_role up</c>), a grey placeholder for
/// the cage station and the act II and III arrivals: one door or two.
/// </summary>
public static class StartShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        yield return RoleRooms.Build("start_a", kit, theme, RoomRole.Up, ShapeParts.One, new RoomEntry(["start"]), "Way in");
        yield return RoleRooms.Build("start_b", kit, theme, RoomRole.Up, ShapeParts.Straight, new RoomEntry(["start"]), "Way in");
    }
}
