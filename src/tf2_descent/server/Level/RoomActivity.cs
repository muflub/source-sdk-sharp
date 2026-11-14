using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Horde;

namespace TF2.Descent.Server.Level;

/// <summary>
/// The level's rooms as what a room holds needs them (plan_descent_logic
/// section 3.4, WO2, WO3): which room a point is in, and whether that room is
/// awake. What a sleeping room holds is data, so the loot floor's drops and
/// the furniture give their entities back to their pools when their room
/// sleeps and take them again when it wakes.
/// </summary>
public interface IRoomActivity
{
    /// <summary>The room holding <paramref name="at"/>; -1 when no room does, or the map has no rooms (the town).</summary>
    int RoomAt(Vector at);

    /// <summary>Whether <paramref name="room"/> is awake. No room (-1) is always awake.</summary>
    bool Awake(int room);
}

/// <summary>
/// The rooms as the horde knows them: the level's room for a point, and the
/// roster's awake flag (the embodiment rules wake a room when a hero comes
/// within two rooms and put it to sleep when none has been for a while).
/// With no horde (the town, a map that is no level) every point is in no
/// room, and no room sleeps.
/// </summary>
[SideService(Side.Server, As = [typeof(IRoomActivity)])]
public sealed class HordeRoomActivity(HordeService horde) : IRoomActivity
{
    public int RoomAt(Vector at) =>
        horde.Current is { Begun: true, Level: { } level } ? level.RoomAt(HordeVectors.ToAi(at)) : -1;

    public bool Awake(int room) =>
        room < 0
        || horde.Current is not { Begun: true, Level: { } level, Roster: { } roster }
        || room >= level.RoomCount
        || roster.RoomAwake(room);
}
