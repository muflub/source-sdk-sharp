using System.Collections.Immutable;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Automap;
using TF2.Descent.Rules.Furniture;
using AutomapMarker = TF2.Descent.Rules.Automap.LevelMarker;
using AutomapMarkerKind = TF2.Descent.Rules.Automap.LevelMarkerKind;

namespace TF2.Descent.Server.Level;

/// <summary>
/// The level as each consumer takes it (plan_descent_logic WO1, mine plan D5): the horde's
/// <see cref="HordeLevel"/>, the furniture placement's rooms and the automap's
/// <see cref="AutomapLevel"/>. Pure. The rooms and markers carry string ids in the two Rules
/// models, the same in both (<see cref="RoomKey"/>, <see cref="MarkerKey"/>), so a shrine the
/// furniture places is the shrine the automap marks.
/// </summary>
public static class LevelAdapters
{
    /// <summary>A room's id in the Rules models: its index.</summary>
    public static string RoomKey(int room) => room.ToString(CultureInfo.InvariantCulture);

    /// <summary>A marker's id in the Rules models: <c>m</c> and its index.</summary>
    public static string MarkerKey(int marker) => "m" + marker.ToString(CultureInfo.InvariantCulture);

    /// <summary>A door's id in the automap: <c>d</c> and its index.</summary>
    public static string DoorKey(int door) => "d" + door.ToString(CultureInfo.InvariantCulture);

    private static AiVector Ai(Vector v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// The level as the horde sees it, rolled from <paramref name="seed"/>. Room ids are the level's
    /// (their indices); each room's markers are its <c>descent_spawn_pack</c> markers in marker order;
    /// a door's normal points from its lower room into its higher. A pair of rooms joined by more
    /// than one doorway (two larger rooms side by side) keeps the doorway of lowest id, because the
    /// horde holds one door a pair. With <paramref name="bossTemplate"/> and an arena, the boss
    /// stands on the arena's first pack marker, or the arena's floor centre when it has none.
    /// </summary>
    public static HordeLevel ToHorde(LevelData level, ulong seed, string? bossTemplate = null)
    {
        var rooms = level.Rooms.Select(r => new HordeRoom(r.Id, Ai(r.Min), Ai(r.Max),
            [.. level.In(r.Id).Where(m => m.Kind == LevelMarkerKind.PackSpawn).Select(m => new PackMarker(Ai(m.Position), m.Yaw))]));
        var doors = level.Doors.GroupBy(d => (d.RoomA, d.RoomB)).Select(g => g.MinBy(d => d.Id)!).OrderBy(d => d.Id)
            .Select(d => new HordeDoor(d.RoomA, d.RoomB, Ai(d.Position), Ai(d.Normal), d.Width));
        HordeBoss? boss = null;
        if (bossTemplate is not null && level.BossArena is int arena)
        {
            LevelRoom room = level.Rooms[arena];
            var spot = level.In(arena).FirstOrDefault(m => m.Kind == LevelMarkerKind.PackSpawn) is { } first
                ? new PackMarker(Ai(first.Position), first.Yaw)
                : new PackMarker(new AiVector(room.Centre.X, room.Centre.Y, room.Min.Z), 0);
            boss = new HordeBoss(bossTemplate, arena, spot);
        }
        return HordeLevel.Create(seed, level.Depth, rooms, doors, boss);
    }

    /// <summary>The level as the horde sees it, rolled from the level's own seed; throws when the level has none yet.</summary>
    public static HordeLevel ToHorde(LevelData level, string? bossTemplate = null) =>
        ToHorde(level, level.Seed ?? throw new InvalidOperationException(
            $"{level.Map} has no seed yet ({level.SeedFrom}): the horde rolls from it"), bossTemplate);

    /// <summary>The marker kinds that furniture stands on (the furniture table's <c>poi</c> rows).</summary>
    public static bool IsFurniture(LevelMarkerKind kind) =>
        kind is LevelMarkerKind.Shrine or LevelMarkerKind.Container or LevelMarkerKind.Dispenser or LevelMarkerKind.Barrel or LevelMarkerKind.Trap;

    /// <summary>
    /// The rooms and their furniture markers for <see cref="FurnishingRules.Furnish"/>: every room, in
    /// id order, with its shrine, container, dispenser, barrel and trap markers (id <see cref="MarkerKey"/>,
    /// poi its <c>poi_type</c>). Pack spawns, spawns and arrivals are not furniture.
    /// </summary>
    public static IReadOnlyList<RoomMarkers> ToFurniture(LevelData level) =>
        [.. level.Rooms.Select(r => new RoomMarkers(RoomKey(r.Id),
            [.. level.In(r.Id).Where(m => IsFurniture(m.Kind)).Select(m => new FurnitureMarker(MarkerKey(m.Id), m.PoiType))]))];

    /// <summary>
    /// The room graph the automap is drawn over: every room on storey 0 (a level's doors all stand on
    /// one floor, mine plan section 2), every door, and the fixed markers: the arrival (the up room's
    /// <c>arrival</c> point), the stairs (the way down), each shrine, and each container named in
    /// <paramref name="lockedContainers"/> (whether a container is locked is the furniture's roll,
    /// not the level's: its <see cref="Placed.KeyId"/>).
    /// </summary>
    public static AutomapLevel ToAutomap(LevelData level, IEnumerable<string>? lockedContainers = null)
    {
        var locked = (lockedContainers ?? []).ToHashSet(StringComparer.Ordinal);
        var markers = new List<AutomapMarker>();
        foreach (LevelMarker m in level.Markers)
        {
            if (m.Room < 0)
                continue;
            AutomapMarkerKind? kind = m.Kind switch
            {
                LevelMarkerKind.Arrival when level.Rooms[m.Room].Role == LevelRoomRole.Arrival => AutomapMarkerKind.Arrival,
                LevelMarkerKind.Shrine => AutomapMarkerKind.Shrine,
                LevelMarkerKind.Container when locked.Contains(MarkerKey(m.Id)) => AutomapMarkerKind.LockedContainer,
                _ => null,
            };
            if (kind is { } k)
                markers.Add(new AutomapMarker(MarkerKey(m.Id), k, RoomKey(m.Room)));
        }
        if (level.Exit is { } exit)
            markers.Add(new AutomapMarker("exit", AutomapMarkerKind.Stairs, RoomKey(exit.Room)));

        return AutomapLevel.Create(
            level.Rooms.Select(r => new AutomapRoom(RoomKey(r.Id))),
            level.Doors.Select(d => new AutomapDoor(DoorKey(d.Id), RoomKey(d.RoomA), RoomKey(d.RoomB))),
            markers);
    }
}
