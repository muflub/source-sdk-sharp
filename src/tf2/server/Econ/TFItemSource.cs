//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// One source's answer for one loadout slot (<see cref="ITFItemSource"/>): no
/// opinion (the <c>default</c>), the slot left empty, or the item carried
/// there.
/// </summary>
public readonly record struct TFSlotItem
{
    private TFSlotItem(ItemInstance? item)
    {
        HasOpinion = true;
        Item = item;
    }

    /// <summary>False for a source that leaves the slot to the others.</summary>
    public bool HasOpinion { get; }

    /// <summary>The item in the slot; null with an opinion is the slot held empty.</summary>
    public ItemInstance? Item { get; }

    /// <summary>The slot is another source's to answer.</summary>
    public static TFSlotItem NoOpinion => default;

    /// <summary>The player carries nothing there — not the class's base item either.</summary>
    public static TFSlotItem Empty => new(null);

    /// <summary>The player carries <paramref name="item"/> there, with its own attributes.</summary>
    public static TFSlotItem Of(ItemInstance item) => new(item);
}

/// <summary>
/// Where a player's kit comes from: which item instance they carry in each
/// loadout slot of a class — stock's <c>GetLoadoutItem</c> ask of the
/// player's inventory (<c>CTFPlayer::ManageRegularWeapons</c>,
/// tf_player.cpp:4496-4560), as a chain.
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, asked in
/// <see cref="IOrderedHook.HookOrder"/>'s order): every source is asked for
/// every slot and the <b>last opinion wins</b>, the rule
/// <see cref="ILoadoutProvider"/>'s columns keep. TF2's own source
/// (<see cref="TFInventoryItemSource"/>, in TF2's band) answers every slot
/// from the player's inventory, so a mod loaded over TF2 — whose orders start
/// at <c>ModStages.First</c>, after it — supplies the items of the players it
/// claims by answering for them, and answers
/// <see cref="TFSlotItem.NoOpinion"/> for everyone else. Nothing is replaced:
/// the mod registers one more implementation.
/// </para>
///
/// <para>
/// The item is an <see cref="ItemInstance"/>, so its own attributes ride with
/// it onto the weapon or wearable it becomes
/// (<see cref="TFLoadoutProvider.Attributes"/>). An answer is still subject
/// to <see cref="TFLoadoutProvider.ItemIsAllowed(ItemInstance, TFLoadoutSlot, TFClass)"/>
/// and to the kit's own rules (a class with no weapon row keeps the slot's
/// base item). A source that throws is named on the log and has answered
/// nothing.
/// </para>
/// </summary>
public interface ITFItemSource
{
    /// <summary>
    /// What the player at <paramref name="entIndex"/> carries in
    /// <paramref name="slot"/> as <paramref name="cls"/>.
    /// </summary>
    TFSlotItem ItemInLoadout(int entIndex, TFClass cls, TFLoadoutSlot slot);
}

/// <summary>
/// TF2's own item source: the bot's seeded items for a bot, the local
/// inventory for everyone else — the equipped item or the class's base item
/// (<c>GetItemInLoadout</c>, tf_item_inventory.cpp:1443-1470), and a client
/// with no active inventory gets the base items. It answers every slot, so it
/// is the floor of the chain.
/// </summary>
[SideService(Side.Server)]
public sealed class TFInventoryItemSource(LocalInventory inventory, BotInventory botInventory, IBotPlayers bots,
                                          ItemSchema schema) : ITFItemSource, IOrderedHook
{
    /// <summary>TF2's range (999,000-999,999): a further mod's source (from ModStages.First) answers after it.</summary>
    public const int Order = 1_000_000 - 1_000 + 61;

    /// <summary>What a client with no active inventory reads: nothing owned, every slot its base item.</summary>
    private readonly PlayerInventory _none = new(0);

    public int HookOrder => Order;

    public TFSlotItem ItemInLoadout(int entIndex, TFClass cls, TFLoadoutSlot slot)
        => OwnedFor(entIndex).ItemInLoadout(cls, slot, schema) is { } item ? TFSlotItem.Of(item) : TFSlotItem.Empty;

    /// <summary>Whose inventory the loadout reads: the bot's seeded items for a bot, the local inventory for everyone else.</summary>
    private PlayerInventory OwnedFor(int entIndex)
    {
        // A bot's draw is seeded by its name (BotInventory.SeedOf), so bots' loadouts differ and the
        // same roster replays the same items.
        if (bots.IsBot(new EntityHandle((uint)entIndex))
            && botInventory.ForBot(entIndex) is { } botOwned)
            return botOwned;
        return inventory.Of(entIndex) ?? _none;
    }
}

/// <summary>The loadout's chains (<see cref="ITFItemSource"/>, <see cref="ITFLocalInventoryGate"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TFEconRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<ITFItemSource>();
        registry.AddHooks<ITFLocalInventoryGate>();
    }
}
