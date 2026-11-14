using SourceSharp.Ai;

using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Motion;

/// <summary>
/// A route through the level's room graph (plan_descent_logic HO3's dev drive,
/// until the horde has a nav-derived route): from a place to a target, through
/// the centre of each door on the shortest room path, each door crossed
/// square on (a corner <see cref="DoorApproach"/> before it and one after it,
/// along its normal), then the target.
///
/// <para>
/// A place in no room is taken to be in the nearest room by centre. The route
/// knows nothing inside a room: a room shaped so that a straight line from a
/// door to the next cuts a wall stops the body there, which reports blocked.
/// </para>
/// </summary>
public static class DoorRoute
{
    /// <summary>How far before and after a door the route crosses it.</summary>
    public const float DoorApproach = 48f;

    /// <summary>The corners from <paramref name="from"/> to <paramref name="to"/>, into <paramref name="corners"/>; none when no path joins their rooms.</summary>
    public static void Build(HordeLevel level, AiVector from, AiVector to, List<AiVector> corners)
    {
        corners.Clear();
        int room = RoomOf(level, from), goal = RoomOf(level, to);
        if (room < 0 || goal < 0)
            return;

        int guard = level.RoomCount;
        while (room != goal && guard-- > 0)
        {
            int next = level.NextRoom(room, goal);
            if (next < 0 || next == room)
            {
                corners.Clear();
                return;
            }

            int d = level.DoorBetween(room, next);
            if (d < 0)
            {
                corners.Clear();
                return;
            }

            var door = level.Doors[d];
            var into = door.Into(room);
            corners.Add(door.Position - into * DoorApproach);
            corners.Add(door.Position + into * DoorApproach);
            room = next;
        }

        if (room != goal)
        {
            corners.Clear();
            return;
        }

        corners.Add(to);
    }

    /// <summary>The room holding <paramref name="p"/>, else the nearest by centre; -1 for a level with no room.</summary>
    public static int RoomOf(HordeLevel level, AiVector p)
    {
        int room = level.RoomAt(p);
        if (room >= 0)
            return room;

        float best = float.MaxValue;
        for (int r = 0; r < level.RoomCount; r++)
        {
            var c = (level.Rooms[r].Min + level.Rooms[r].Max) * 0.5f;
            float d = c.DistanceTo(p);
            if (d < best)
            {
                best = d;
                room = r;
            }
        }

        return room;
    }
}
