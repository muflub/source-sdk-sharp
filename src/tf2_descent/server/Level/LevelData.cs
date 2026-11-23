using System.Collections.Immutable;
using SourceSharp.Abi;

namespace TF2.Descent.Server.Level;

/// <summary>Where a piece of the level came from.</summary>
public enum PieceSource
{
    /// <summary>Read as the level ships it.</summary>
    Shipped,

    /// <summary>Not shipped; worked out from what is, by a stated rule.</summary>
    Derived,

    /// <summary>Neither shipped nor derivable: the level data has none.</summary>
    Absent,
}

/// <summary>The pieces of a level the load line accounts for.</summary>
public enum LevelPiece
{
    Rooms,
    Doors,
    Markers,
    Arrivals,
    Exit,
    BossArena,
    Seed,
}

/// <summary>What a room is to the level (its layout role).</summary>
public enum LevelRoomRole
{
    /// <summary>A room of the path or a branch.</summary>
    Fill,

    /// <summary>The way in (<c>room_role up</c>): a party arrives here.</summary>
    Arrival,

    /// <summary>The way down (<c>room_role down</c>).</summary>
    Exit,

    /// <summary>A treasure room.</summary>
    Treasure,

    /// <summary>A boss level's room before the arena.</summary>
    Antechamber,

    /// <summary>A boss level's arena.</summary>
    Arena,
}

/// <summary>A marker's kind: the Descent <c>poi_type</c>s (src/mapgen/Descent/Markers.cs) and the tools' own arrival.</summary>
public enum LevelMarkerKind
{
    PackSpawn,
    Shrine,
    Container,
    Dispenser,
    Barrel,
    Trap,
    MapMarker,
    Spawn,
    Arrival,

    /// <summary>A crate a hit breaks, where the dresser would stand one (the map session's marker).</summary>
    BreakableCrate,

    /// <summary>A barrel a hit breaks, where the dresser would stand one (the map session's marker).</summary>
    BreakableBarrel,
}

/// <summary>
/// A placed room. <see cref="Id"/> is its index in <see cref="LevelData.Rooms"/>. Its bounds are
/// its cells: <see cref="Min"/> and <see cref="Max"/> in level coordinates, the floor of the
/// navigation grid to the top of the room's own height.
/// </summary>
/// <param name="Name">The room's name as the level places it (the layout codec, <c>TF2.Descent.Rules.Layout.RoomName</c>).</param>
/// <param name="Family">The shape family the name spells (<c>gallery</c>, <c>arrival</c>, ...), or the whole name when it is not in the codec.</param>
/// <param name="Column">The west cell it covers, as placed.</param>
/// <param name="Row">The south cell it covers, as placed.</param>
/// <param name="CellsX">Cells it covers east, as placed.</param>
/// <param name="CellsY">Cells it covers north, as placed.</param>
/// <param name="Rotation">Quarter turns counter-clockwise, 0 to 3.</param>
/// <param name="Role">Its role: up and down are the <c>.nav3d</c>'s transition roles, the rest its family in the name.</param>
/// <param name="Low">A low room (height class <c>l</c>).</param>
/// <param name="Jump">A jump room (flag <c>j</c>).</param>
/// <param name="Label">Its map label, or empty.</param>
/// <param name="Light">Its light from the <c>.map2d</c>'s <c>LITE</c> section (<see cref="RoomLight"/>: level 1 = 100 on the brightness gate's scale), or null when the level does not say or the room was not measured.</param>
/// <param name="DarkByDesign">The level says the room holds no light of its own (the <c>LITE</c> section's flag 1).</param>
public sealed record LevelRoom(
    int Id,
    string Name,
    string Family,
    int Column,
    int Row,
    int CellsX,
    int CellsY,
    int Rotation,
    Vector Min,
    Vector Max,
    LevelRoomRole Role,
    bool Low,
    bool Jump,
    string Label,
    float? Light = null,
    bool DarkByDesign = false)
{
    /// <summary>Whether <paramref name="p"/> is inside the room's bounds (faces included).</summary>
    public bool Contains(Vector p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    /// <summary>The middle of the room's bounds.</summary>
    public Vector Centre => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2);
}

/// <summary>
/// A joined doorway between two rooms: <see cref="Position"/> is the middle of its sill on the
/// cell face, <see cref="Normal"/> the horizontal unit vector from <see cref="RoomA"/> into
/// <see cref="RoomB"/>, <see cref="Width"/> the opening's width and <see cref="Height"/> its height.
/// <see cref="RoomA"/> is the lower room id.
/// </summary>
public sealed record LevelDoor(int Id, int RoomA, int RoomB, Vector Position, Vector Normal, float Width, float Height, string Socket);

/// <summary>A marker: its <c>poi_type</c>, kind, position, facing (degrees, counter-clockwise from +x), the room it stands in, and its label.</summary>
public sealed record LevelMarker(int Id, string PoiType, LevelMarkerKind Kind, Vector Position, float Yaw, int Room, string Label);

/// <summary>A point a hero arriving on the level stands on.</summary>
public sealed record ArrivalPoint(Vector Position, float Yaw, int Room, PieceSource Source);

/// <summary>The level's way down: the centre of its transition volume and the room it is in.</summary>
public sealed record LevelExit(Vector Position, int Room, string Map);

/// <summary>
/// A Descent level as data (plan_descent_logic WO1): its rooms with their bounds, the doors
/// between them, every marker, where a party arrives, the way down, the boss arena, and the
/// depth and seed. Built once at level ready by <see cref="LevelReader"/> from what the level
/// ships; immutable. <see cref="Sources"/> says, for each piece, whether it was shipped,
/// derived (and the load line names the rule) or absent. <see cref="Arena"/> is the boss arena's
/// layout when the <c>.map2d</c> ships an <c>ARNA</c> section (<see cref="ArenaLayout"/>), else null.
/// </summary>
public sealed record LevelData(
    string Map,
    int Depth,
    ulong? Seed,
    string SeedFrom,
    ImmutableArray<LevelRoom> Rooms,
    ImmutableArray<LevelDoor> Doors,
    ImmutableArray<LevelMarker> Markers,
    ImmutableArray<ArrivalPoint> Arrivals,
    LevelExit? Exit,
    int? BossArena,
    ImmutableDictionary<LevelPiece, PieceSource> Sources,
    ImmutableArray<string> Notes,
    ArenaLayout? Arena = null)
{
    /// <summary>Where <paramref name="piece"/> came from; absent when the reader said nothing of it.</summary>
    public PieceSource Source(LevelPiece piece) => Sources.TryGetValue(piece, out var s) ? s : PieceSource.Absent;

    /// <summary>The first room whose bounds hold <paramref name="p"/>, or -1.</summary>
    public int RoomAt(Vector p)
    {
        for (int i = 0; i < Rooms.Length; i++)
            if (Rooms[i].Contains(p))
                return i;
        return -1;
    }

    /// <summary>The markers of one kind, in id order.</summary>
    public IEnumerable<LevelMarker> OfKind(LevelMarkerKind kind) => Markers.Where(m => m.Kind == kind);

    /// <summary>The markers standing in one room, in id order.</summary>
    public IEnumerable<LevelMarker> In(int room) => Markers.Where(m => m.Room == room);

    /// <summary>The rooms one door away from <paramref name="room"/>, in id order.</summary>
    public IEnumerable<int> Neighbours(int room) =>
        Doors.Where(d => d.RoomA == room || d.RoomB == room).Select(d => d.RoomA == room ? d.RoomB : d.RoomA).Distinct().Order();
}
