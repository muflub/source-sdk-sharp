//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Rules;

namespace TF2.Server.Econ;

/// <summary>
/// Which TF class a player is, for the loadout. The per-player carrier
/// (stage1's minimal <c>tf_player_data</c>, Stage 2.3) answers it; until one
/// registers, every player is <see cref="TFClass.Undefined"/> and the loadout
/// provider has no opinion. A single owner: register it with
/// <c>As = [typeof(ITFPlayerClasses)]</c>.
/// </summary>
public interface ITFPlayerClasses
{
    TFClass ClassOf(EntityHandle player);
}

/// <summary>One slot of a class loadout: the item there, owned or base.</summary>
public readonly record struct LoadoutItem(TFLoadoutSlot Slot, ItemInstance Item);

/// <summary>
/// TF2's spawn kit (stage1 1.6): the Stage 0.7 <see cref="ILoadoutProvider"/>
/// answered from the player's class and <see cref="LocalInventory"/> loadout,
/// as <c>CTFPlayer::ManageRegularWeapons</c> walks it (tf_player.cpp:4496-4560):
/// every slot's item (<c>GetLoadoutItem</c> → <c>GetItemInLoadout</c>, the
/// equipped item or the base item), minus what <c>ItemIsAllowed</c> refuses.
/// The weapon column is those items' <c>item_class</c> in the weapon slots,
/// each as the class's own weapon entity; the ammo column is the class file's
/// <c>AmmoMax</c> (Stage 2.3); the wearables are Stage 2.13's. A player with no
/// class has no opinion, and the core kit stands.
/// </summary>
[SideService(Side.Server)]
public sealed class TFLoadoutProvider(LocalInventory inventory, ItemSchema schema, ITFPlayerClasses? classes = null,
                                      Player.TFPlayerItems? items = null, TFClassTable? table = null,
                                      ITeamRoundState? round = null, ITFStalemateSettings? stalemate = null,
                                      WeaponCatalog? catalog = null, Player.TFPlayerCarriers? carriers = null,
                                      BotInventory? botInventory = null, IBotPlayers? bots = null)
    : ILoadoutProvider, IOrderedHook
{
    private readonly ITeamRoundState? _round = round;
    private readonly ITFStalemateSettings? _stalemate = stalemate;

    /// <summary>TF2's range (999,000-999,999), so a further mod's provider (from ModStages.First) runs after TF2's.</summary>
    public const int Order = 1_000_000 - 1_000 + 60;

    public int HookOrder => Order;

    /// <summary>
    /// The weapon column: each weapon slot's item class as the class's own
    /// weapon entity (<see cref="TFItemClassTranslation"/>, stock's
    /// <c>TranslateWeaponEntForClass</c> at tf_player.cpp:4387), which is the
    /// name of its weapon row. The items behind them are recorded for the
    /// player under those names (<see cref="Player.TFPlayerItems"/>), so each
    /// weapon finds its item's attributes, as a stock weapon holds its
    /// <c>CEconItemView</c>.
    ///
    /// <para>
    /// The kit equips each item on the player, and a stock equipped item
    /// enters its owner's own attribute manager as a provider
    /// (<c>CEconEntity::ReapplyProvision</c>, econ_entity.cpp:285-308:
    /// <c>GetAttributeManager()->ProvideTo( pNewOwner )</c> at :304) — so a
    /// hook on the player, <c>IsAllowedToPickUpFlag</c>'s
    /// <c>cannot_pick_up_intelligence</c>, walks the kit's items. Stock's
    /// <c>CTFPlayer::PostThink</c> initializes that manager
    /// (tf_player.cpp:3301); here it enters with its first grant, a new
    /// kit's grants replacing the old.
    /// </para>
    /// </summary>
    public IEnumerable<string> Weapons(EntityHandle player)
    {
        var cls = classes?.ClassOf(player) ?? TFClass.Undefined;
        var weapons = new List<string>();
        var byWeapon = new Dictionary<string, IAttributeProvider>(StringComparer.Ordinal);
        var granted = new List<AttributeContainer>();
        foreach (var entry in Loadout(player.EntryIndex, cls))
        {
            if (TFItemStrings.IsWearableSlot((int)entry.Slot) || entry.Item.Definition.ItemClass is not { Length: > 0 } itemClass)
                continue;

            string weapon = table is null ? itemClass : TFItemClassTranslation.Translate(itemClass, table[cls]);
            weapons.Add(weapon);
            var container = Attributes(entry.Item);
            byWeapon[weapon] = container;
            granted.Add(container);
        }

        if (items is { } kit)
        {
            var manager = new AttributeContainer();
            foreach (var container in granted)
            {
                container.ProvideTo(manager);
                // The same equip answers this item's GetAttributeOwner with the
                // player (econ_entity.cpp), so a hook walked ON the item —
                // CALL_ATTRIB_HOOK_*_ON_OTHER of the weapon — reaches the
                // manager (whose own list and other items apply), the
                // initiator then skipped in the providers round.
                container.Owner = manager;
            }
            kit.SetManager(player.EntryIndex, manager);
            kit.Set(player.EntryIndex, byWeapon);
        }

        // The same hand-out writes each weapon's item definition index onto
        // the carrier, keyed by the catalogue row — what stock networks on
        // each weapon's own item view (m_iItemDefinitionIndex,
        // econ_item_view.cpp:182) — so the client rebuilds the item from its
        // own parsed schema (GetItemDefinition over GetStaticData, :435) and the
        // predicted attribute hooks read the server's statics. A new kit
        // starts from no items: the previous class's rows clear with it.
        if (catalog is not null && carriers is not null
            && carriers.Get(player.EntryIndex) is { } state)
        {
            state.WeaponItemDefIndex.Fill(0);
            foreach (var (weapon, provider) in byWeapon)
            {
                int row = catalog.IndexOf(weapon);
                if (row >= 0 && row < state.WeaponItemDefIndex.Length
                    && provider is AttributeContainer { Item: { } definition })
                {
                    // +1: the array's untouched zero stays no item while the
                    // schema's own item 0 (the bat) keeps a value of its own.
                    state.WeaponItemDefIndex[row] = definition.Defindex + 1;
                }
            }
        }

        return weapons;
    }

    /// <summary>An item's attributes as its weapon provides them: its own over its definition's statics.</summary>
    public static AttributeContainer Attributes(ItemInstance item)
    {
        var container = new AttributeContainer(item.Definition, AttributeProviderType.Weapon);
        foreach (var value in item.Attributes)
            container.Set(value);
        return container;
    }

    /// <summary>
    /// The ammo column: <c>GiveDefaultItems</c>' fill to the class's maxima
    /// (tf_player.cpp:4246-4250: <c>GiveAmmo( GetMaxAmmo( iAmmo ), iAmmo )</c>
    /// for every ammo), one entry per TF ammo the class carries any of; no
    /// opinion for a player with no class.
    /// </summary>
    public IEnumerable<(string Ammo, int Count)> Ammo(EntityHandle player)
    {
        var cls = classes?.ClassOf(player) ?? TFClass.Undefined;
        if (table is null || cls < TFClass.Scout || cls >= TFClass.CountAll)
            return [];

        var ammo = new List<(string, int)>();
        for (var a = TFAmmo.Primary; a < TFAmmo.Count; a++)
        {
            // TFAmmoCarryRule's (GetMaxAmmo's) one Grenades3 for every class.
            int max = a == TFAmmo.Grenades3 ? 1 : table[cls].MaxAmmo(a);
            if (max > 0)
                ammo.Add((TFAmmoNames.Name(a), max));
        }

        return ammo;
    }

    /// <summary>
    /// The items <paramref name="cls"/> spawns with for the client at
    /// <paramref name="entIndex"/>, slot by slot in slot order; empty for a
    /// class outside Scout..Engineer. A client with no active inventory gets
    /// the base items.
    /// </summary>
    public IReadOnlyList<LoadoutItem> Loadout(int entIndex, TFClass cls)
    {
        if (cls < TFClass.Scout || cls >= TFClass.Civilian)
            return [];

        var owned = OwnedFor(entIndex);
        var items = new List<LoadoutItem>();
        for (var slot = TFLoadoutSlot.Primary; slot < TFLoadoutSlot.Count; slot++)
        {
            if (owned.ItemInLoadout(cls, slot, schema) is { } item && ItemIsAllowed(item, slot, cls))
                items.Add(new LoadoutItem(slot, item));
        }
        return items;
    }

    /// <summary>Whose inventory the loadout reads: the bot's seeded items for a bot, the local inventory for everyone else.</summary>
    private PlayerInventory OwnedFor(int entIndex)
    {
        // The bot's seed is the roster's SeedIndex later (stage F2x); until the AI binding supplies
        // one, the provider passes 0 for every bot. The draw is pure, so 0 already replays the same
        // items for the same class and slot.
        if (bots is not null && botInventory is not null && bots.IsBot(new EntityHandle((uint)entIndex))
            && botInventory.ForBot(entIndex, 0) is { } botOwned)
            return botOwned;
        return inventory.Of(entIndex) ?? new PlayerInventory(0);
    }

    /// <summary>
    /// <c>CTFPlayer::ItemIsAllowed</c> (tf_player.cpp:4416-4485) as far as the
    /// modes this build runs: an item named for passtime is allowed only in a
    /// passtime game (which it never is), and in a melee-only sudden death
    /// only the melee slot's item is — the spy keeps his two PDAs
    /// (tf_player.cpp:4441-4448). The holiday, medieval and tournament arms
    /// wait for the modes that turn them on (docs/divergences.md).
    /// </summary>
    public bool ItemIsAllowed(ItemInstance item, TFLoadoutSlot slot, TFClass cls)
    {
        if (!ItemIsAllowed(item))
            return false;

        if (_round?.State == RoundState.Stalemate && _stalemate is { } stalemate && stalemate.MeleeOnly)
        {
            bool meleeOnlyAllowed = slot == TFLoadoutSlot.Melee
                                    || (cls == TFClass.Spy && slot is TFLoadoutSlot.Pda or TFLoadoutSlot.Pda2);
            if (!meleeOnlyAllowed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// <c>CTFPlayer::ItemIsAllowed</c> (tf_player.cpp:4416-4485) as far as no
    /// game mode is running: an item named for passtime is allowed only in a
    /// passtime game, which this build never runs. The holiday, stalemate
    /// melee-only, medieval and tournament arms wait for the modes that turn
    /// them on (docs/divergences.md).
    /// </summary>
    public static bool ItemIsAllowed(ItemInstance item) =>
        !item.Definition.Name.Contains("passtime", StringComparison.OrdinalIgnoreCase);
}
