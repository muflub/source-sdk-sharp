using System.Collections.Immutable;

namespace TF2.Descent.Rules.Automap;

/// <summary>A room of a level: its id and the storey it is drawn on (two storeys draw as two layers, 2m).</summary>
public sealed record AutomapRoom(string Id, int Storey = 0);

/// <summary>A door joining two rooms.</summary>
public sealed record AutomapDoor(string Id, string RoomA, string RoomB)
{
    public bool Touches(string room) => RoomA == room || RoomB == room;

    public string Other(string room) => RoomA == room ? RoomB : RoomA;
}

/// <summary>What a fixed marker of a level marks (plan_tf2_rpg 2m).</summary>
public enum LevelMarkerKind
{
    Arrival,
    Stairs,
    Shrine,
    LockedContainer,
}

/// <summary>A marker the level declares: the arrival, the stairs, a shrine, a locked container, in a room.</summary>
public sealed record LevelMarker(string Id, LevelMarkerKind Kind, string Room);

/// <summary>
/// The room graph an automap is drawn over (plan_tf2_rpg 2m): rooms, the doors that join
/// them and the level's fixed markers. An input: the server builds it from what the level
/// ships (WO1), so this model depends on no layout type. <see cref="Create"/> refuses a door
/// or a marker that names a room the level does not have, and a repeated id.
/// </summary>
public sealed record AutomapLevel
{
    private AutomapLevel(ImmutableArray<AutomapRoom> rooms, ImmutableArray<AutomapDoor> doors, ImmutableArray<LevelMarker> markers)
    {
        Rooms = rooms;
        Doors = doors;
        Markers = markers;
    }

    public ImmutableArray<AutomapRoom> Rooms { get; }
    public ImmutableArray<AutomapDoor> Doors { get; }
    public ImmutableArray<LevelMarker> Markers { get; }

    public bool HasRoom(string id) => Rooms.Any(r => r.Id == id);

    public AutomapRoom Room(string id) =>
        Rooms.FirstOrDefault(r => r.Id == id) ?? throw new KeyNotFoundException($"'{id}' is not a room of this level");

    public static AutomapLevel Create(IEnumerable<AutomapRoom> rooms, IEnumerable<AutomapDoor> doors, IEnumerable<LevelMarker>? markers = null)
    {
        var r = rooms.ToImmutableArray();
        var d = doors.ToImmutableArray();
        var m = (markers ?? []).ToImmutableArray();
        Unique(r.Select(x => x.Id), "room");
        Unique(d.Select(x => x.Id), "door");
        Unique(m.Select(x => x.Id), "marker");
        var ids = r.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var door in d)
            if (!ids.Contains(door.RoomA) || !ids.Contains(door.RoomB) || door.RoomA == door.RoomB)
                throw new ArgumentException($"door '{door.Id}' joins '{door.RoomA}' and '{door.RoomB}': it must join two different rooms of the level");
        foreach (var marker in m)
            if (!ids.Contains(marker.Room))
                throw new ArgumentException($"marker '{marker.Id}' is in '{marker.Room}', which is not a room of the level");
        return new AutomapLevel(r, d, m);
    }

    private static void Unique(IEnumerable<string> ids, string what)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id))
                throw new ArgumentException($"{what} id '{id}' appears more than once");
    }
}
