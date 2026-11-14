using SourceSharp.Ai;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// A walk between two points as the level's room graph has it (plan_descent_logic
/// 3.6, HO4's fallback while the level's <c>NavGraph</c> cannot be had: see
/// docs/plans/tf2_descent/plan_descent_logic.md HO4): straight to the goal
/// inside one room, else through the door of each room a shortest route enters,
/// in order, then the goal. At most <see cref="MoveIntent.MaxCorners"/> corners,
/// nearest first; the route past the fourth is asked again when the walker gets
/// there. A point in no room walks straight. Allocates nothing.
/// </summary>
public static class RoomRoute
{
    /// <summary>Writes the corners from <paramref name="from"/> to <paramref name="to"/> into <paramref name="corners"/> (4 long); answers how many.</summary>
    public static int Corners(HordeLevel level, AiVector from, AiVector to, Span<AiVector> corners)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (corners.Length < MoveIntent.MaxCorners)
            throw new ArgumentException($"a route holds {MoveIntent.MaxCorners} corners", nameof(corners));

        int a = level.RoomAt(from), b = level.RoomAt(to);
        int n = 0;
        if (a >= 0 && b >= 0 && a != b)
        {
            int room = a;
            while (room != b && n < MoveIntent.MaxCorners)
            {
                int next = level.NextRoom(room, b);
                if (next < 0)
                    break;
                int door = level.DoorBetween(room, next);
                if (door < 0)
                    break;
                corners[n++] = level.Doors[door].Position;
                room = next;
            }
        }

        if (n < MoveIntent.MaxCorners)
            corners[n++] = to;

        // A door the walker stands in is behind it: a room's bounds hold their walls, so a
        // walker in a doorway is still in the room it is leaving.
        int skip = 0;
        while (skip < n - 1 && corners[skip].DistanceTo(from) <= NearUnits)
            skip++;
        if (skip > 0)
        {
            corners[skip..n].CopyTo(corners);
            n -= skip;
        }

        return n;
    }

    /// <summary>How near a corner the walker stands on it.</summary>
    public const float NearUnits = 8f;

    /// <summary>A move along the route to <paramref name="to"/> at <paramref name="speed"/>.</summary>
    public static MoveIntent Follow(HordeLevel level, AiVector from, AiVector to, MoveSpeed speed)
    {
        Span<AiVector> c = stackalloc AiVector[MoveIntent.MaxCorners];
        int n = Corners(level, from, to, c);
        AiVector first = c[0] - from;
        return new MoveIntent(MoveKind.Follow, speed, (byte)n, c[0], n > 1 ? c[1] : default, n > 2 ? c[2] : default, n > 3 ? c[3] : default,
                              first.Normalised(), CrouchOn: false, Ticks: 0);
    }
}
