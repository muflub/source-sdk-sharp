using SourceSharp.Ai;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Boss;

/// <summary>
/// A boss's arena as its brain keeps to it (plan_descent_logic HO13; plan_tf2_rpg 5d: a boss is
/// arena-bound by design): the room that holds its home, its floor bounds drawn in by a margin so
/// a body's hull keeps off the walls and out of the doorways. Pure; a level with no room at the
/// home (a dev boss put down outside every room) has an arena of the nearest room.
/// </summary>
public readonly record struct BossArena(int Room, AiVector Min, AiVector Max)
{
    /// <summary>The arena of the room holding <paramref name="home"/> in <paramref name="level"/>, drawn in by <paramref name="margin"/> (never past its middle).</summary>
    public static BossArena Of(HordeLevel level, AiVector home, float margin)
    {
        ArgumentNullException.ThrowIfNull(level);
        int room = level.RoomAt(home);
        if (room < 0)
            room = Nearest(level, home);
        var r = level.Rooms[room];
        float mx = MathF.Min(margin, (r.Max.X - r.Min.X) * 0.25f), my = MathF.Min(margin, (r.Max.Y - r.Min.Y) * 0.25f);
        return new BossArena(room, new AiVector(r.Min.X + mx, r.Min.Y + my, r.Min.Z), new AiVector(r.Max.X - mx, r.Max.Y - my, r.Max.Z));
    }

    /// <summary>The arena's middle on the floor at <paramref name="z"/>.</summary>
    public AiVector Centre(float z) => new((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f, z);

    /// <summary>Whether <paramref name="p"/> stands inside the drawn-in bounds (on the floor plan).</summary>
    public bool Holds(AiVector p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    /// <summary><paramref name="p"/> moved onto the drawn-in bounds (on the floor plan; its height kept).</summary>
    public AiVector Clamp(AiVector p) => new(Math.Clamp(p.X, Min.X, Max.X), Math.Clamp(p.Y, Min.Y, Max.Y), p.Z);

    private static int Nearest(HordeLevel level, AiVector p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int r = 0; r < level.RoomCount; r++)
        {
            var c = (level.Rooms[r].Min + level.Rooms[r].Max) * 0.5f;
            float d = c.DistanceTo(p);
            if (d < bestD)
                (best, bestD) = (r, d);
        }

        return best;
    }
}

/// <summary>A point of the Tank's route: where, its station's number along the track (1-based; 0 for a point it rolls through), and whether it is the final station (the roof).</summary>
public readonly record struct TankStop(AiVector Position, int Station, bool Final);

/// <summary>
/// The Tank's track (mine plan D11, M14): the arena's layout's track when the level ships one (the
/// ARNA contract of 2026-11-16, <see cref="Route"/>); a level that ships none (its arena carries
/// pack markers and no track) has a track of the arena's own markers in a loop round its middle, from the marker after the one nearest the
/// Tank's start, then a last station short of the middle by the roof pillar's room
/// (<c>centreClearUnits</c>): the pillar it brings down. An arena with no markers loops its four
/// quarter points. Pure: the same level and start give the same track on the server and in a replay.
/// </summary>
public static class TankTrack
{
    /// <summary>
    /// The Tank's route. When the level ships its arena's track (the ARNA contract,
    /// <see cref="HordeBoss.Layout"/>): its points in the file's order (the loop closes back to point
    /// 0), the stations those flagged so, numbered 1, 2, ... along the file (a point flagged final
    /// counts as a station whatever its station bit), the final the first point flagged final, or the
    /// last station when none is (the last point when no point is a station). Otherwise the arena's
    /// markers (<see cref="Of"/>), every point a station numbered in order, the last the final. Pure.
    /// </summary>
    public static TankStop[] Route(HordeLevel level, BossArena arena, AiVector start, float centreClear)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.Boss?.Layout is { Track.Length: > 0 } layout)
        {
            var track = layout.Track;
            int final = -1, lastStation = -1;
            for (int k = 0; k < track.Length; k++)
            {
                if (track[k].Final && final < 0)
                    final = k;
                if (track[k].Station || track[k].Final)
                    lastStation = k;
            }

            if (final < 0)
                final = lastStation >= 0 ? lastStation : track.Length - 1;
            var shipped = new TankStop[track.Length];
            int number = 0;
            for (int k = 0; k < track.Length; k++)
            {
                bool station = track[k].Station || k == final;
                shipped[k] = new TankStop(track[k].Position, station ? ++number : 0, k == final);
            }

            return shipped;
        }

        var points = Of(level, arena, start, centreClear);
        var route = new TankStop[points.Length];
        for (int k = 0; k < points.Length; k++)
            route[k] = new TankStop(points[k], k + 1, k == points.Length - 1);
        return route;
    }

    public static AiVector[] Of(HordeLevel level, BossArena arena, AiVector start, float centreClear)
    {
        ArgumentNullException.ThrowIfNull(level);
        var room = level.Rooms[arena.Room];
        AiVector centre = arena.Centre(start.Z);
        var points = new List<AiVector>();
        foreach (var m in room.Markers)
            points.Add(m.Position with { Z = start.Z });
        if (points.Count == 0)
        {
            float qx = (arena.Max.X - arena.Min.X) * 0.25f, qy = (arena.Max.Y - arena.Min.Y) * 0.25f;
            points.AddRange([centre + new AiVector(-qx, -qy, 0f), centre + new AiVector(qx, -qy, 0f),
                             centre + new AiVector(qx, qy, 0f), centre + new AiVector(-qx, qy, 0f)]);
        }

        // Round the middle, counter-clockwise; ties by distance so two markers on one bearing keep one order.
        points.Sort((a, b) =>
        {
            int c = Bearing(centre, a).CompareTo(Bearing(centre, b));
            return c != 0 ? c : centre.DistanceTo(a).CompareTo(centre.DistanceTo(b));
        });
        int nearest = 0;
        for (int i = 1; i < points.Count; i++)
            if (points[i].DistanceTo(start) < points[nearest].DistanceTo(start))
                nearest = i;

        var track = new AiVector[points.Count + 1];
        for (int k = 0; k < points.Count; k++)
            track[k] = points[(nearest + 1 + k) % points.Count];
        AiVector last = track[points.Count - 1];
        AiVector toLast = last - centre;
        float d = toLast.Length2D;
        track[points.Count] = d > centreClear && d > 1e-3f ? centre + new AiVector(toLast.X / d * centreClear, toLast.Y / d * centreClear, 0f) : last;
        return track;
    }

    private static float Bearing(AiVector centre, AiVector p) => MathF.Atan2(p.Y - centre.Y, p.X - centre.X);
}
