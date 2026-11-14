using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Ai.Embodiment;

/// <summary>What an order asks the horde to do.</summary>
public enum HordeOrderKind : byte
{
    /// <summary>Wake <see cref="HordeOrder.Room"/>: its sleeping robots join the far simulation.</summary>
    WakeRoom,
    /// <summary>Put <see cref="HordeOrder.Room"/> to sleep: its calm far robots stop ticking.</summary>
    SleepRoom,
    /// <summary>A dead robot's body goes back to the pool.</summary>
    Release,
    /// <summary>The robot gives <see cref="HordeOrder.Body"/> back and goes far.</summary>
    Demote,
    /// <summary>The robot takes <see cref="HordeOrder.Body"/> at <see cref="HordeOrder.Position"/>.</summary>
    Promote,
}

/// <summary>One decision of the embodiment rules. Unused fields are 0 (no robot, no body) or -1 (no room).</summary>
public readonly record struct HordeOrder(HordeOrderKind Kind, RobotId Robot, int Body, int Room, AiVector Position, float Yaw)
{
    public static HordeOrder Wake(int room) => new(HordeOrderKind.WakeRoom, RobotId.None, 0, room, default, 0);

    public static HordeOrder Sleep(int room) => new(HordeOrderKind.SleepRoom, RobotId.None, 0, room, default, 0);

    public static HordeOrder Release(RobotId robot, int body) => new(HordeOrderKind.Release, robot, body, -1, default, 0);

    public static HordeOrder Demote(RobotId robot, int body) => new(HordeOrderKind.Demote, robot, body, -1, default, 0);

    public static HordeOrder Promote(RobotId robot, int body, int room, AiVector at, float yaw) =>
        new(HordeOrderKind.Promote, robot, body, room, at, yaw);
}

/// <summary>
/// The caller's buffer of orders: a fixed array, cleared and refilled by each decision, so a
/// decision allocates nothing. Orders are in the sequence they are applied: wakes, sleeps,
/// releases, demotions, then promotions (a demotion under pressure directly before the
/// promotion it makes room for).
/// </summary>
public sealed class HordeOrders
{
    private readonly HordeOrder[] _items;

    public HordeOrders(int capacity) => _items = new HordeOrder[capacity];

    /// <summary>Room for any one decision over <paramref name="level"/>: a wake or sleep a room, and at most four orders a body.</summary>
    public static HordeOrders For(HordeSettings settings, HordeLevel level) => new(2 * level.RoomCount + 4 * settings.Bodies + 4);

    public int Count { get; private set; }

    public int Capacity => _items.Length;

    public HordeOrder this[int i] => (uint)i < (uint)Count ? _items[i] : throw new ArgumentOutOfRangeException(nameof(i));

    public ReadOnlySpan<HordeOrder> AsSpan() => _items.AsSpan(0, Count);

    public void Clear() => Count = 0;

    internal void Add(in HordeOrder order)
    {
        if (Count == _items.Length) throw new InvalidOperationException($"the order buffer holds {_items.Length} orders");
        _items[Count++] = order;
    }
}
