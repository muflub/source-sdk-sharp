using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Automap;

/// <summary>
/// The automap's rooms the client holds (plan_descent_logic UI5): exactly the rooms the server
/// revealed to the local hero, as it sent them, by room id. Nothing else of the level is known.
/// </summary>
public interface IAutomapRooms
{
    /// <summary>Counts every change: a source that lays the rooms out again does so when this moves.</summary>
    int Version { get; }

    /// <summary>The revealed rooms, in room id order.</summary>
    IReadOnlyList<AutomapRoomView> Rooms { get; }
}

/// <summary>
/// The automap cache over <c>automap_sync</c> and <c>automap_room</c>: a room replaces the one
/// held under its id (a shrine used re-sends its room); a sync forgets them all, as does the end
/// of a level (the server resyncs at the next spawn, before the next level's init).
/// </summary>
[SideService(Side.Client, As = [typeof(IAutomapRooms)])]
public sealed class AutomapRoomCache : IAutomapRooms, IAutomapHook, IClientLevelHook, IOrderedHook
{
    /// <summary>TF2 Descent's automap block on the client (UI5).</summary>
    public int HookOrder => 1201;

    private readonly SortedDictionary<int, AutomapRoomView> _rooms = new();
    private AutomapRoomView[] _list = [];

    public int Version { get; private set; }

    public IReadOnlyList<AutomapRoomView> Rooms => _list;

    public void OnAutomapSync()
    {
        _rooms.Clear();
        Changed();
    }

    public void OnAutomapRoom(AutomapRoomView room)
    {
        _rooms[room.Room] = room;
        Changed();
    }

    public void OnLevelShutdown()
    {
        _rooms.Clear();
        Changed();
    }

    private void Changed()
    {
        _list = [.. _rooms.Values];
        Version++;
    }
}
