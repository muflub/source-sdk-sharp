using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// THE AUTOMAP ON THE WIRE (plan_descent_logic UI5; plan_tf2_rpg 2m, R23). The
// server reveals and the client only draws: a room's outline, its door stubs
// and the fixed markers in it reach a hero's client when he enters the room,
// and not before, so the map cannot be read out of client memory ahead of the
// player.
//
//   automap_sync  forget every room held: the rooms that follow are the
//                 hero's whole map (at each spawn, so after a level change)
//   automap_room  one revealed room, in map units: its outline (the bounds of
//                 the cells it covers), its storey, its map label, the doors
//                 it has (each a stub across its wall), and its markers; or a
//                 discovered one (a room through a door of a revealed one, user
//                 2026-11-15): its outline, storey and doors, no label, no marks
//
// Every payload fits one 255-byte user message at its fullest (a fact).
//=============================================================================

/// <summary>What a marker on the automap marks, as the server sends it.</summary>
public enum AutomapMarkKind : byte
{
    /// <summary>A way through: the way down, or the way back up a hero arrived by.</summary>
    Stairs,
    ShrineUnused,
    ShrineUsed,
    ContainerLocked,
    Portal,
    Corpse,
    Boss,
}

/// <summary>A door stub of a revealed room: the middle of its sill, its width along the wall, and the wall's direction.</summary>
/// <param name="AlongY">The wall runs north-south (the door's normal is east-west).</param>
public readonly record struct AutomapDoorStub(int X, int Y, int Width, bool AlongY);

/// <summary>A marker in a revealed room, in map units.</summary>
public readonly record struct AutomapMark(AutomapMarkKind Kind, int X, int Y);

/// <summary>
/// One revealed room as it reaches the hero's client: map units, x east, y north. A
/// <paramref name="Discovered"/> room is one the hero has not entered, seen through a door of one he
/// has: its outline and doors only.
/// </summary>
public sealed record AutomapRoomView(
    int Room, int Storey, int MinX, int MinY, int MaxX, int MaxY, string Label,
    IReadOnlyList<AutomapDoorStub> Doors, IReadOnlyList<AutomapMark> Marks, bool Discovered = false);

public interface IAutomapHook
{
    /// <summary>Forget every room: the hero's map follows whole.</summary>
    void OnAutomapSync();

    void OnAutomapRoom(AutomapRoomView room);
}

/// <summary>The resync's start (<c>automap_sync</c>).</summary>
public partial struct AutomapSync
{
    /// <summary>The rooms the resync will send.</summary>
    public partial byte Rooms { get; set; }
}

[UserMessage(Name)]
public sealed class AutomapSyncMessage(IHooks<IAutomapHook>? hooks) : IUserMessage<AutomapSync>
{
    public AutomapSyncMessage() : this(null) { }

    public const string Name = "automap_sync";

    public void OnReceived(AutomapSync message) => hooks?.Each(nameof(IAutomapHook.OnAutomapSync), h => h.OnAutomapSync());
}

/// <summary>One revealed room on the wire (<c>automap_room</c>).</summary>
public partial struct AutomapRoom
{
    /// <summary>The most doors a room's message carries: a 3 by 3 room's twelve sockets.</summary>
    public const int MaxDoors = 12;

    /// <summary>The most markers a room's message carries.</summary>
    public const int MaxMarks = 8;

    /// <summary>The longest label a room carries.</summary>
    public const int MaxLabel = 32;

    public partial byte Room { get; set; }
    public partial byte Storey { get; set; }
    public partial short MinX { get; set; }
    public partial short MinY { get; set; }
    public partial short MaxX { get; set; }
    public partial short MaxY { get; set; }

    [UserMessageString(MaxLength = MaxLabel)]
    public partial string Label { get; set; }

    [UserMessageArray(MaxCount = MaxDoors)]
    public partial short[] DoorX { get; set; }

    [UserMessageArray(MaxCount = MaxDoors)]
    public partial short[] DoorY { get; set; }

    [UserMessageArray(MaxCount = MaxDoors)]
    public partial short[] DoorWidth { get; set; }

    /// <summary>1 where the door's wall runs north-south.</summary>
    [UserMessageArray(MaxCount = MaxDoors)]
    public partial byte[] DoorAlongY { get; set; }

    [UserMessageArray(MaxCount = MaxMarks)]
    public partial byte[] MarkKind { get; set; }

    [UserMessageArray(MaxCount = MaxMarks)]
    public partial short[] MarkX { get; set; }

    [UserMessageArray(MaxCount = MaxMarks)]
    public partial short[] MarkY { get; set; }

    /// <summary>1 for a discovered room (not entered, seen through a door): outline and doors only.</summary>
    public partial byte Discovered { get; set; }

    public AutomapRoomView View()
    {
        short[] dx = DoorX ?? [], dy = DoorY ?? [], dw = DoorWidth ?? [];
        byte[] da = DoorAlongY ?? [];
        int doors = Math.Min(Math.Min(dx.Length, dy.Length), Math.Min(dw.Length, da.Length));
        var stubs = new AutomapDoorStub[doors];
        for (int i = 0; i < doors; i++)
            stubs[i] = new AutomapDoorStub(dx[i], dy[i], dw[i], da[i] != 0);

        byte[] mk = MarkKind ?? [];
        short[] mx = MarkX ?? [], my = MarkY ?? [];
        int marks = Math.Min(mk.Length, Math.Min(mx.Length, my.Length));
        var list = new AutomapMark[marks];
        for (int i = 0; i < marks; i++)
            list[i] = new AutomapMark((AutomapMarkKind)mk[i], mx[i], my[i]);

        return new AutomapRoomView(Room, Storey, MinX, MinY, MaxX, MaxY, Label ?? "", stubs, list, Discovered != 0);
    }

    /// <summary>A map coordinate as a wire short: rounded, held inside the short's range.</summary>
    public static short Short(double value) =>
        double.IsNaN(value) ? (short)0 : (short)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
}

[UserMessage(Name)]
public sealed class AutomapRoomMessage(IHooks<IAutomapHook>? hooks) : IUserMessage<AutomapRoom>
{
    public AutomapRoomMessage() : this(null) { }

    public const string Name = "automap_room";

    public void OnReceived(AutomapRoom message)
    {
        var view = message.View();
        hooks?.Each(nameof(IAutomapHook.OnAutomapRoom), h => h.OnAutomapRoom(view));
    }
}
