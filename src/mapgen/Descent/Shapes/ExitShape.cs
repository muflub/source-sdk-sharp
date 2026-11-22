namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The way down (tag <c>stairs</c>, <c>room_role down</c>; plan §5, §7's
/// "way down" rows, M6). Levels 1 to 4: a cage station, a different cage
/// from the level's arrival. Levels 5 to 10: a raised floor broken through,
/// gone down by a rubble ramp or a drop (level 5's is the hole the boss's
/// blast leaves, level 10's the bore hole, each shut under a cover the server
/// disables on the kill, <see cref="BossNames.Exit"/>). Levels 11 to 14: a chute too
/// steep to stand on, or a fissure to drop down. One door, two opposite and
/// two at a corner: a body with necks to its doors.
/// </summary>
public static class ExitShape
{
    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "exit");
        // Level 5's way down on its 512 kit (brief p18): 2 by 2 cells beside the arena, its one door west, into the arena.
        if (Parts.SpanCells(kit) > 1)
        {
            yield return Hole("exit_breachw", kit, theme, [CellFace.West], Stations.Descent.Drop, 5, 5, rng, cover: true);
            yield break;
        }

        foreach (var (id, doors) in ArrivalShape.DoorSets)
        {
            yield return Cage("exit_cage" + id, kit, theme, doors, rng);
            yield return Hole("exit_ramp" + id, kit, theme, doors, Stations.Descent.Ramp, 6, 9, rng);
            yield return Hole("exit_collapse" + id, kit, theme, doors, Stations.Descent.Drop, 6, 9, rng);
            // The boss levels' ways down are shut until the boss falls: level 5's collapsed lift shaft floor, level 10's
            // bore hole, each under a cover the server takes away on the kill (BossNames.Exit).
            yield return Hole("exit_breach" + id, kit, theme, doors, Stations.Descent.Drop, 5, 5, rng, cover: true);
            yield return Hole("exit_bore" + id, kit, theme, doors, Stations.Descent.Drop, 10, 10, rng, cover: true);
            yield return Hole("exit_chute" + id, kit, theme, doors, Stations.Descent.Slide, 11, 14, rng);
            yield return Hole("exit_fissure" + id, kit, theme, doors, Stations.Descent.Drop, 11, 14, rng);
        }
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, int minDepth, int maxDepth) =>
        new RoomBuilder(name, kit, theme).WithHeight(ArrivalShape.Height).Seat(doors)
            .WithEntry(new RoomEntry(["stairs"], MinDepth: minDepth, MaxDepth: maxDepth)).WithMapLabel("Way down");

    /// <summary>Levels 1 to 4: the cage down.</summary>
    private static Room Cage(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, RoomSeed rng)
    {
        Rect body = ArrivalShape.Body(kit);
        CellFace wall = ArrivalShape.FittingWall(doors);
        var b = ArrivalShape.Shell(New(name, kit, theme, doors, 1, 4), body, doors)
            .Chamfers(body, 128, Corner.SouthWest);
        float along = wall == CellFace.North ? body.CentreX + rng.Pick<float>([-160, 160]) : body.CentreY + 64;
        b.Cage(body, wall, along, RoomRole.Down);
        ArrivalShape.Light(b, body);
        return b.Build();
    }

    /// <summary>A raised floor along the fitting wall, stairs up from the room, a hole in it gone down by <paramref name="how"/>.</summary>
    private static Room Hole(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, Stations.Descent how, int minDepth, int maxDepth, RoomSeed rng, bool cover = false)
    {
        Rect body = ArrivalShape.Body(kit);
        CellFace wall = ArrivalShape.FittingWall(doors);
        var b = ArrivalShape.Shell(New(name, kit, theme, doors, minDepth, maxDepth), body, doors);
        int height = how == Stations.Descent.Slide ? 192 : 160;
        // The platform: a band along the fitting wall, 288 deep, leaving the body's ends to walk round; its hole in the middle.
        bool alongX = wall is CellFace.North or CellFace.South;
        float a0 = (alongX ? body.X0 : body.Y0) + 160, a1 = (alongX ? body.X1 : body.Y1) - 160;
        Rect platform = Stations.OutRect(body, wall, a0, -288, a1, 0);
        float mid = (a0 + a1) / 2 + rng.Pick<float>([-16, 0, 16]);
        // A collapsed floor's hole is broad; a fissure (deeper down) a long crack.
        Rect hole = how == Stations.Descent.Drop && maxDepth <= 10
            ? Stations.OutRect(body, wall, mid - 96, -256, mid + 96, -64)
            : how == Stations.Descent.Drop
            ? Stations.OutRect(body, wall, mid - 112, -208, mid + 112, -112)
            : Stations.OutRect(body, wall, mid - 144, -224, mid + 144, -96);
        Compass down = alongX ? Compass.East : Compass.North;
        Compass stairs = wall switch { CellFace.North => Compass.South, CellFace.South => Compass.North, CellFace.East => Compass.West, _ => Compass.East };
        var (ax, ay) = Stations.Out(body, wall, a1 - 64, -400);
        b.Hole(platform, height, hole, how, down, stairs, new Point(ax, ay, b.Floor), Stations.Inward(wall));
        // A boss level's: the hole filled to the platform's top by a solid brush in the platform's own surface, so
        // the floor reads whole until the server disables it on the kill. It casts no shadow: casting, its own top was
        // lit a shade under the platform round it and gave the hole away (brief p15, level 10).
        if (cover)
            b.WithNamedBrush(BossNames.Exit, [Brush.Box(new Point(hole.X0, hole.Y0, b.Floor), new Point(hole.X1, hole.Y1, b.Floor + height), theme.Wall)], castsShadow: false);
        ArrivalShape.Light(b, body);
        return b.Build();
    }
}
