using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client
// (plan_side_isolation §2.4): the floor loot entity's networked state and the
// one message that tells a hero what his own seat of a drop holds
// (plan_descent_logic WO2).

namespace TF2.Descent.Shared;

/// <summary>
/// A <c>tf2_descent_loot</c> drop's networked state: the drop's number and
/// nothing else. A drop holds up to four seats, one a hero, and what a seat
/// holds is its owner's alone (R4: item drops are instanced per player). A
/// networked state is one blob sent alike to every client the entity reaches,
/// so no seat rides here: each seat goes to its owner's client alone, by
/// <see cref="LootSeatMessage"/> keyed by the entity's index and
/// <see cref="Drop"/>, and the entity reaches only the clients of heroes who
/// own a seat of it (the server's transmit rule).
///
/// <para>
/// <see cref="Drop"/> is the server's number for the drop the entity holds
/// now, never 0 while it holds one: the pool lends one entity to many drops
/// over a level, so a seat message for an earlier drop of the same entity is
/// told apart by it.
/// </para>
/// </summary>
[StandaloneNetState(ClassName)]
public partial class LootNetState
{
    public const string ClassName = "tf2_descent_loot";

    public partial ushort Drop { get; set; }
}

/// <summary>The models a drop wears: TF2's, from the mod's GAME path (tf2_misc_dir.vpk); no art of Descent's own.</summary>
public static class LootLooks
{
    /// <summary>A drop that holds an item: TF2's gift box (the Christmas ammo pack's look).</summary>
    public const string ItemModel = "models/items/tf_gift.mdl";

    /// <summary>A drop that holds only Australium: Mann vs. Machine's currency pack.</summary>
    public const string AustraliumModel = "models/items/currencypack_small.mdl";

    /// <summary>The model a seat is drawn with on its owner's client: the gift for an item, the pack for Australium alone.</summary>
    public static string ModelFor(LootHolds holds) =>
        (holds & LootHolds.Item) == 0 && (holds & LootHolds.Australium) != 0 ? AustraliumModel : ItemModel;
}

/// <summary>What a seat holds, as bits of <see cref="LootSeat.Holds"/>.</summary>
[Flags]
public enum LootHolds : byte
{
    /// <summary>Nothing: the seat was taken, or the drop left this client.</summary>
    None = 0,

    /// <summary>An item (<see cref="LootSeat.Base"/> and the rest describe it).</summary>
    Item = 1,

    /// <summary>A stack of Australium (<see cref="LootSeat.Australium"/>).</summary>
    Australium = 2,
}

/// <summary>
/// One hero's seat of one drop, sent to that hero's client alone: which entity
/// and drop it is, and what the seat holds. An item is told as its owner may
/// see it on the floor: the base, the rarity, the item level, whether it is
/// identified and, when it is, the Australium unique it is; never an affix.
/// <see cref="Holds"/> of <see cref="LootHolds.None"/> takes the seat away.
/// </summary>
public partial struct LootSeat
{
    /// <summary>The longest item base or unique id a seat carries (the content's longest is 21).</summary>
    public const int MaxId = 40;

    /// <summary>The loot entity's index.</summary>
    public partial short Entity { get; set; }

    /// <summary>The drop the entity holds (<see cref="LootNetState.Drop"/>).</summary>
    public partial ushort Drop { get; set; }

    /// <summary><see cref="LootHolds"/> bits.</summary>
    public partial byte Holds { get; set; }

    [UserMessageString(MaxLength = MaxId)]
    public partial string Base { get; set; }

    public partial byte Rarity { get; set; }

    public partial byte ItemLevel { get; set; }

    public partial bool Identified { get; set; }

    /// <summary>The Australium unique the item is; empty for a rolled item and for one not identified.</summary>
    [UserMessageString(MaxLength = MaxId)]
    public partial string Unique { get; set; }

    /// <summary>The Australium the seat holds.</summary>
    public partial int Australium { get; set; }
}

/// <summary>A seat as the client reads it.</summary>
public sealed record LootSeatView(int Entity, int Drop, LootHolds Holds, string Base, int Rarity, int ItemLevel, bool Identified,
                                  string Unique, long Australium)
{
    public bool HasItem => (Holds & LootHolds.Item) != 0;

    public bool HasAustralium => (Holds & LootHolds.Australium) != 0;

    public static LootSeatView Of(LootSeat s) =>
        new(s.Entity, s.Drop, (LootHolds)s.Holds, s.Base ?? "", s.Rarity, s.ItemLevel, s.Identified, s.Unique ?? "", s.Australium);
}

/// <summary>A seat of a drop this client's hero owns arrived, changed or was taken away.</summary>
public interface ILootSeatHook
{
    void OnLootSeat(LootSeatView seat);
}

[UserMessage(Name)]
public sealed class LootSeatMessage(IHooks<ILootSeatHook>? hooks) : IUserMessage<LootSeat>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public LootSeatMessage() : this(null) { }

    public const string Name = "loot_seat";

    public void OnReceived(LootSeat message)
    {
        var view = LootSeatView.Of(message);
        hooks?.Each(nameof(ILootSeatHook.OnLootSeat), h => h.OnLootSeat(view));
    }
}
