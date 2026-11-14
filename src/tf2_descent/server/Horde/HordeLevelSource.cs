using System.Collections.Immutable;
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Level;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// The level as the horde sees it, for the map that has just loaded: the seam
/// between the horde (HO2) and the level reader (WO1).
///
/// <para>
/// <b>What it asks of an implementation.</b> Asked once a level, at
/// <c>LevelReady</c>, on the game thread, after the map's entities exist, with
/// the bare map name and the depth and seed <see cref="DescentLevelName"/>
/// read from it. Answer a <see cref="HordeLevel"/> built with that seed and
/// depth: rooms with ids 0 to n-1 and world-space bounds that hold every point
/// of their floor a robot or a hero can stand on (the horde finds a hero's room
/// by <see cref="HordeLevel.RoomAt"/>); each room's <c>descent_spawn_pack</c>
/// markers as floor positions (the origin a standing robot's feet are at) and
/// yaws in degrees, in the order robots take them; the doors (two rooms, the
/// door's centre, its unit normal from the first room into the second, its
/// width); and the boss, when the level has one (its template, its arena room,
/// its spot). Answer null when the level has nothing for the horde. The
/// answer is the level's for its life: it is not asked again until the next
/// load.
/// </para>
/// </summary>
public interface IHordeLevelSource
{
    HordeLevel? LevelFor(string mapName, int depth, ulong seed);
}

/// <summary>
/// Which maps are levels: a generated level is named <c>tf2_descent-&lt;depth&gt;-s&lt;seed&gt;</c>
/// (src/mapgen's descent-level), depth 1 or deeper. The town (<c>tf2_descent_town</c>)
/// and any other map are not, and get no horde.
/// </summary>
public static class DescentLevelName
{
    public const string Prefix = "tf2_descent-";

    public static bool TryParse(string mapName, out int depth, out ulong seed)
    {
        depth = 0;
        seed = 0;
        if (!mapName.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        ReadOnlySpan<char> rest = mapName.AsSpan(Prefix.Length);
        int dash = rest.IndexOf("-s", StringComparison.Ordinal);
        return dash > 0
               && int.TryParse(rest[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out depth) && depth >= 1
               && ulong.TryParse(rest[(dash + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out seed);
    }
}

/// <summary>What the stand-in level asks of the world: where the heroes arrive, and whether a robot can stand at a point.</summary>
public interface IStandInWorld
{
    /// <summary>The first player spawn point of the map, or null when it has none.</summary>
    AiVector? Arrival();

    /// <summary>
    /// The floor under <paramref name="point"/> (searched from 64 units above
    /// it to 256 below) where a standing robot's hull is clear, or null.
    /// </summary>
    AiVector? StandAt(AiVector point);
}

/// <summary>
/// A DEV STAND-IN for the level reader (WO1, another lane's): a row of rooms
/// north of where the heroes arrive, each a 1024-unit cell of the generated
/// level's grid, the first holding the arrival and no robots, the rest with a
/// few pack markers each, joined by a door at each cell edge. Built for the
/// generated levels, whose rooms are 1024-unit cells; on depth 1 seed 1 the row
/// is the arrival shaft and the three drifts north of it. A marker the world
/// says no robot can stand at is left out, and the count is logged. Replaced by
/// the level's own data wherever a level ships it (<see cref="LevelHordeSource"/>);
/// it remains the answer for a level whose files could not be read.
/// </summary>
[SideService(Side.Server)]
public sealed class StandInLevelSource(IStandInWorld world, ILog log) : IHordeLevelSource
{
    public HordeLevel? LevelFor(string mapName, int depth, ulong seed)
    {
        if (world.Arrival() is not { } arrival)
        {
            arrival = StandInLevel.DefaultArrival;
            log.Msg($"[TF2Descent/Horde] stand-in level: {mapName} has no player spawn entity: the stand-in's own arrival\n");
        }

        var level = StandInLevel.Around(arrival, depth, seed, world.StandAt, out int dropped);
        log.Msg(FormattableString.Invariant(
            $"[TF2Descent/Horde] stand-in level around ({arrival.X:0},{arrival.Y:0},{arrival.Z:0}): {level.RoomCount} rooms, {level.Doors.Length} doors, {level.Rooms.Sum(r => r.Markers.Length)} markers ({dropped} left out: no room to stand)\n"));
        return level;
    }
}

/// <summary>The stand-in's geometry, pure: the cells and markers around an arrival point.</summary>
public static class StandInLevel
{
    /// <summary>The generated levels' grid cell.</summary>
    public const float Cell = 1024f;

    /// <summary>
    /// Where the heroes arrive when the map has no spawn entity the server can
    /// find (the generated levels' <c>info_player_start</c> has no managed
    /// class): depth 1 seed 1's, from its preview sheet.
    /// </summary>
    public static AiVector DefaultArrival => new(2336f, 4608f, 16f);

    /// <summary>Rooms in the row: the arrival's and three north of it.</summary>
    public const int Rooms = 4;

    /// <summary>Pack markers a cell.</summary>
    public const int MarkersPerRoom = 4;

    /// <summary>Where pack marker <paramref name="i"/> stands in a cell, from its centre: four along the cell's north-south line.</summary>
    public static (float X, float Y) MarkerOffset(int i) => i switch
    {
        0 => (-40f, -288f),
        1 => (40f, -96f),
        2 => (-40f, 96f),
        _ => (40f, 288f),
    };

    /// <summary>
    /// The row of rooms whose first cell holds <paramref name="arrival"/>.
    /// <paramref name="standAt"/> answers the floor a marker stands on, or null
    /// to leave it out; <paramref name="dropped"/> counts those left out.
    /// </summary>
    public static HordeLevel Around(AiVector arrival, int depth, ulong seed, Func<AiVector, AiVector?> standAt, out int dropped)
    {
        float cx = MathF.Floor(arrival.X / Cell) * Cell + Cell / 2;
        float cy = MathF.Floor(arrival.Y / Cell) * Cell + Cell / 2;
        float floor = arrival.Z;
        dropped = 0;

        var rooms = new List<HordeRoom>(Rooms);
        var doors = new List<HordeDoor>(Rooms - 1);
        for (int k = 0; k < Rooms; k++)
        {
            float y = cy + k * Cell;
            var markers = ImmutableArray.CreateBuilder<PackMarker>();
            if (k > 0)
            {
                for (int m = 0; m < MarkersPerRoom; m++)
                {
                    var (dx, dy) = MarkerOffset(m);
                    if (standAt(new AiVector(cx + dx, y + dy, floor)) is { } at)
                        markers.Add(new PackMarker(at, 270f));   // facing south, towards the arrival
                    else
                        dropped++;
                }
            }

            rooms.Add(new HordeRoom(k, new AiVector(cx - Cell / 2, y - Cell / 2, floor - 256f),
                                    new AiVector(cx + Cell / 2, y + Cell / 2, floor + 768f), markers.ToImmutable()));
            if (k > 0)
                doors.Add(new HordeDoor(k - 1, k, new AiVector(cx, y - Cell / 2, floor), new AiVector(0f, 1f, 0f), 128f));
        }

        return HordeLevel.Create(seed, depth, rooms, doors);
    }
}

/// <summary>The stand-in's world: the map's first player spawn, and a floor trace and a hull test.</summary>
[SideService(Side.Server, As = [typeof(IStandInWorld)])]
public sealed class EngineStandInWorld(IEngineEntities entities, IEngineWorldQuery world) : IStandInWorld
{
    public AiVector? Arrival()
    {
        foreach (string cls in (string[])["info_player_teamspawn", "info_player_start"])
            foreach (var h in entities.FindAllByClassname(cls))
                return HordeVectors.ToAi(entities.GetOrigin(h));
        return null;
    }

    public AiVector? StandAt(AiVector point)
    {
        Vector top = new(point.X, point.Y, point.Z + 64f);
        Vector bottom = new(point.X, point.Y, point.Z - 256f);
        TraceResult down = world.TraceLine(top, bottom, TraceMask.Brushes);
        if (!down.Hit || down.Fraction <= 0f)
            return null;

        Vector feet = down.End + new Vector(0f, 0f, 1f);
        HullResult clear = world.TraceHull(feet, feet, RobotHull.Mins, RobotHull.Maxs, TraceMask.Solid);
        return clear.StartSolid || clear.Blocked ? null : HordeVectors.ToAi(down.End);
    }
}

/// <summary>
/// The level the horde is given: the level's own rooms, doors and pack markers
/// as its files ship them (<see cref="Level.ILevelData.Horde"/>, read at level
/// init, before the horde asks at level ready), and the stand-in only when the
/// level has none to give (its files missing or unreadable, or no seed yet),
/// said in one line so a horde on a stand-in is never mistaken for the level's. HO13: a boss
/// depth's level whose files name its arena holds its boss (bosses.json's for the depth, D13), on
/// the arena's first pack marker (<see cref="Level.LevelAdapters.ToHorde(Level.LevelData, ulong, string?)"/>).
/// </summary>
[SideService(Side.Server, As = [typeof(IHordeLevelSource)])]
public sealed class LevelHordeSource(Level.ILevelData levels, StandInLevelSource standIn, IHordeRules rules, ILog log) : IHordeLevelSource
{
    public HordeLevel? LevelFor(string mapName, int depth, ulong seed)
    {
        if (levels.Horde is { } level)
        {
            if (level.Boss is null && levels.Current is { BossArena: int arena } data && rules.Rules.Bosses.AtDepth(data.Depth) is { } boss)
            {
                level = Level.LevelAdapters.ToHorde(data, level.Seed, boss.Id);
                log.Msg($"[TF2Descent/Horde] {mapName}: the boss {boss.Id} holds arena room {arena} at ({level.Boss!.Spot.Position.X:0},{level.Boss.Spot.Position.Y:0},{level.Boss.Spot.Position.Z:0})\n");
            }

            return level;
        }

        log.Msg($"[TF2Descent/Horde] {mapName}: the level gave no rooms for the horde: the stand-in level\n");
        return standIn.LevelFor(mapName, depth, seed);
    }
}
