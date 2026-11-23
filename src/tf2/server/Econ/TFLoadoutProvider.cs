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
/// answered from the player's class and the items their sources name
/// (<see cref="ITFItemSource"/>: TF2's own is the <see cref="LocalInventory"/> loadout),
/// as <c>CTFPlayer::ManageRegularWeapons</c> walks it (tf_player.cpp:4496-4560):
/// every slot's item (<c>GetLoadoutItem</c> → <c>GetItemInLoadout</c>, the
/// equipped item or the base item), minus what <c>ItemIsAllowed</c> refuses.
/// The weapon column is those items' <c>item_class</c> in the weapon slots,
/// each as the class's own weapon entity; the ammo column is the class file's
/// <c>AmmoMax</c> (Stage 2.3); the wearables are Stage 2.13's. A player with no
/// class has no opinion, and the core kit stands.
/// </summary>
[SideService(Side.Server)]
public sealed class TFLoadoutProvider(IHooks<ITFItemSource> sources,
                                      IHooks<Player.ITFPlayerAttributeSource> attributeSources,
                                      ItemSchema schema, ITFPlayerClasses classes,
                                      Player.TFPlayerItems items, TFClassTable table,
                                      ITeamRoundState round, ITFStalemateSettings stalemate,
                                      WeaponCatalog catalog, Player.TFPlayerCarriers carriers,
                                      AttributeHooks hooks, IEnginePlayers players,
                                      IEnginePrecache precache, ILog log)
    : ILoadoutProvider, IWeaponColumnOwner, IOrderedHook
{
    private readonly ITeamRoundState _round = round;
    private readonly ITFStalemateSettings _stalemate = stalemate;
    private readonly IEnginePrecache _precache = precache;

    /// <summary>TF2's range (999,000-999,999), so a further mod's provider (from ModStages.First) runs after TF2's.</summary>
    public const int Order = 1_000_000 - 1_000 + 60;

    public int HookOrder => Order;

    /// <summary>The last kit handed to each player: weapon row to the loadout slot and item it came from.</summary>
    private readonly Dictionary<int, Dictionary<string, LoadoutItem>> _kits = new();

    /// <summary>
    /// The item behind each weapon row of the kit <see cref="Weapons"/> last
    /// handed the player at <paramref name="entIndex"/>, with its loadout
    /// slot — stock's <c>CEconItemView</c> on each carried weapon, what
    /// <c>ValidateWeapons</c> compares the loadout against
    /// (tf_player.cpp:5158-5170). Empty before a first kit.
    /// </summary>
    public IReadOnlyDictionary<string, LoadoutItem> KitOf(int entIndex)
        => _kits.TryGetValue(entIndex, out var kit) ? kit : new Dictionary<string, LoadoutItem>();

    private readonly Dictionary<int, List<LoadoutItem>> _worn = new();
    private readonly Dictionary<int, int> _kitVersion = new();

    /// <summary>The worn items in weapon slots of the kit <see cref="Weapons"/> last handed the player (a shield, Gunboats, the Razorback): empty before a first kit.</summary>
    public IReadOnlyList<LoadoutItem> WornOf(int entIndex) => _worn.TryGetValue(entIndex, out var worn) ? worn : [];

    /// <summary>How many kits <see cref="Weapons"/> has handed the player: a new number, a new kit.</summary>
    public int KitVersion(int entIndex) => _kitVersion.GetValueOrDefault(entIndex);

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
        var cls = classes.ClassOf(player);
        var weapons = new List<string>();
        var byWeapon = new Dictionary<string, IAttributeProvider>(StringComparer.Ordinal);
        var granted = new List<(string Weapon, AttributeContainer Container)>();
        var kit = new Dictionary<string, LoadoutItem>(StringComparer.Ordinal);
        _kits[player.EntryIndex] = kit;
        var worn = new List<LoadoutItem>();
        _worn[player.EntryIndex] = worn;
        _kitVersion[player.EntryIndex] = _kitVersion.GetValueOrDefault(player.EntryIndex) + 1;
        foreach (var entry in Loadout(player.EntryIndex, cls))
        {
            // The wearables are the spawn's own entities (the wearable arm
            // below); a wearable class in a weapon slot — the demoshield's
            // secondary — is worn, never wielded, so no weapon row stands in
            // for it (the class lookup's CTFWearable, tf_player.cpp:4619-4621).
            if (TFItemStrings.IsWearableSlot((int)entry.Slot)
                || TFItemStrings.IsWearableClass(entry.Item.Definition.ItemClass)
                || entry.Item.Definition.ItemClass is not { Length: > 0 } itemClass)
            {
                if (!TFItemStrings.IsWearableSlot((int)entry.Slot))
                    worn.Add(entry); // a worn item in a weapon slot (a shield, Gunboats): part of the kit its holder knows
                continue;
            }

            string weapon = TFItemClassTranslation.Translate(itemClass, table[cls]);
            var equipped = entry.Item;

            // An equipped class the catalogue holds no row for must not leave
            // the slot empty: the kit then wears the slot's base item, the
            // same answer GetItemInLoadout gives when the equipped item
            // cannot fit (tf_item_inventory.cpp:1443-1470), said once with a
            // line naming the swap. Without the check a no-row name reaches
            // the client as a weapon nothing resolves.
            if (!catalog.Contains(weapon)
                && schema.BaseItem(cls, entry.Slot) is { } baseDefinition && baseDefinition.ItemClass is { Length: > 0 } baseClass)
            {
                string baseWeapon = TFItemClassTranslation.Translate(baseClass, table[cls]);
                log.Warning($"[TF2/Loadout] {itemClass} has no weapon row; {cls}'s {entry.Slot} keeps its base {baseWeapon}\n");
                weapon = baseWeapon;
                equipped = ItemInstance.Base(baseDefinition);
            }

            weapons.Add(weapon);
            var container = Attributes(equipped);
            byWeapon[weapon] = container;
            granted.Add((weapon, container));
            kit[weapon] = new LoadoutItem(entry.Slot, equipped);

            // The models the item wears, precached as its grant lands — the
            // wearable's arm (CEconWearable::Spawn precaches its display
            // model): the world rule's answers, so a kit swap to an unlock
            // the level has not seen is a model, not an error.
            if (entry.Item.Definition.ModelWorld is { Length: > 0 } world)
                _precache.PrecacheModel(world);

            string? playerModel = entry.Item.Definition.PlayerModel(cls);
            if (string.IsNullOrEmpty(playerModel))
                playerModel = entry.Item.Definition.ModelPlayer;
            if (playerModel is { Length: > 0 })
                _precache.PrecacheModel(playerModel);
        }

        var manager = new AttributeContainer();

        // Stock's ReapplyProvision gate (tf_weaponbase.cpp:5709-5740):
        // an item whose provide_on_active mode is 1 provides only while
        // its weapon is the player's active weapon; any other mode always
        // does. The mode reads on the item itself, before the owner link,
        // so the walk sees its own list and its definition's statics
        // (wearables need no mode: they always provide, and their slots
        // are skipped above). Each grant builds a fresh manager, so
        // skipping the provide is stock's StopProvidingTo in one pass.
        string active = players.GetActiveWeapon(players.GetHandle(player.EntryIndex));
        foreach (var (weapon, container) in granted)
        {
            bool provide = hooks.Int(0, "provide_on_active", container) != 1
                           || weapon == active;
            if (provide)
                container.ProvideTo(manager);

            // The same equip answers this item's GetAttributeOwner with the
            // player (econ_entity.cpp), so a hook walked ON the item —
            // CALL_ATTRIB_HOOK_*_ON_OTHER of the weapon — reaches the
            // manager (whose own list and other items apply), the
            // initiator then skipped in the providers round.
            container.Owner = manager;
        }

        CarryOver(player, manager);
        items.SetManager(player.EntryIndex, manager);
        items.Set(player.EntryIndex, byWeapon);

        // The same hand-out writes each weapon's item definition index onto
        // the carrier, keyed by the catalogue row — what stock networks on
        // each weapon's own item view (m_iItemDefinitionIndex,
        // econ_item_view.cpp:182) — so the client rebuilds the item from its
        // own parsed schema (GetItemDefinition over GetStaticData, :435) and the
        // predicted attribute hooks read the server's statics. A new kit
        // starts from no items: the previous class's rows clear with it.
        if (carriers.Get(player.EntryIndex) is { } state)
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

    /// <summary>
    /// What is the player's and not the kit's moves from the manager the last
    /// kit built to <paramref name="manager"/>. Stock keeps one
    /// <c>CAttributeManager</c> for the player's life and a regeneration only
    /// changes which weapons provide to it (<c>ManageRegularWeapons</c>,
    /// tf_player.cpp:4496); here each kit builds a manager, so the rest is
    /// carried: the player's runtime list (<c>m_AttributeList</c>, what
    /// <c>AddCustomAttribute</c> wrote, through its owner
    /// <see cref="Player.TFPlayerAttributes.CarryTo"/> — stock sweeps it at the
    /// spawn and the death, never at a locker) and every provider that is not
    /// one of the outgoing kit's weapons (the worn items, which provide from
    /// their spawn until their strip). The attribute sources are asked anew
    /// (<see cref="Player.ITFPlayerAttributeSource"/>): the containers they
    /// answered last time are let go and the ones they answer now provide
    /// last, after the kit's and the carried.
    /// </summary>
    private void CarryOver(EntityHandle player, AttributeContainer manager)
    {
        int index = player.EntryIndex;
        if (items.ManagerOf(index) is AttributeContainer old)
        {
            carriers.Part<Player.TFPlayerAttributes>(index)?.CarryTo(old, manager);

            var outgoing = items.Kit(index);
            var standing = items.StandingOf(index);
            foreach (var provider in old.Providers.ToArray())
            {
                if (provider is not AttributeContainer held || outgoing.Contains(held))
                    continue;

                held.StopProvidingTo(old);
                if (!standing.Contains(held))
                    held.ProvideTo(manager);
            }
        }

        var provided = new List<AttributeContainer>();
        foreach (var source in attributeSources.All)
        {
            try
            {
                if (source.AttributesOf(player) is { } attributes)
                {
                    attributes.ProvideTo(manager);
                    provided.Add(attributes);
                }
            }
            catch (Exception e)
            {
                log.Warning($"[TF2/Loadout] attribute source {source.GetType().FullName} threw: {e.Message}\n");
            }
        }

        items.SetStanding(index, provided);
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
    /// A player with a class owns his weapons column whatever it holds: a loadout whose
    /// sources left every weapon slot empty gives him no weapon, as stock's
    /// <c>ManageRegularWeapons</c> gives an empty slot nothing (tf_player.cpp:4496-4560),
    /// never the core's HL2 kit. A player with no class has no opinion.
    /// </summary>
    public bool OwnsWeapons(EntityHandle player)
    {
        var cls = classes.ClassOf(player);
        return cls >= TFClass.Scout && cls < TFClass.CountAll;
    }

    /// <summary>
    /// The ammo column: <c>GiveDefaultItems</c>' fill to the class's maxima
    /// (tf_player.cpp:4246-4250: <c>GiveAmmo( GetMaxAmmo( iAmmo ), iAmmo )</c>
    /// for every ammo), one entry per TF ammo the class carries any of; no
    /// opinion for a player with no class.
    /// </summary>
    public IEnumerable<(string Ammo, int Count)> Ammo(EntityHandle player)
    {
        var cls = classes.ClassOf(player);
        if (cls < TFClass.Scout || cls >= TFClass.CountAll)
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
    /// class outside Scout..Engineer. Each slot's item is the item sources'
    /// answer (<see cref="ITFItemSource"/>): TF2's own gives a client with no
    /// active inventory the base items.
    /// </summary>
    public IReadOnlyList<LoadoutItem> Loadout(int entIndex, TFClass cls)
    {
        if (cls < TFClass.Scout || cls >= TFClass.Civilian)
            return [];

        var items = new List<LoadoutItem>();
        for (var slot = TFLoadoutSlot.Primary; slot < TFLoadoutSlot.Count; slot++)
        {
            if (ItemInLoadout(entIndex, cls, slot) is { } item && ItemIsAllowed(item, slot, cls))
                items.Add(new LoadoutItem(slot, item));
        }
        return items;
    }

    /// <summary>
    /// The chain's fold for one slot: every source is asked and the last
    /// opinion stands (<see cref="ITFItemSource"/>). A source that throws is
    /// named on the log and has answered nothing, so the kit the player would
    /// have had without it is what a broken source leaves behind.
    /// </summary>
    private ItemInstance? ItemInLoadout(int entIndex, TFClass cls, TFLoadoutSlot slot)
    {
        ItemInstance? item = null;
        foreach (var source in sources.All)
        {
            try
            {
                TFSlotItem answer = source.ItemInLoadout(entIndex, cls, slot);
                if (answer.HasOpinion)
                    item = answer.Item;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2/Loadout] item source {source.GetType().FullName} threw on {cls}'s {slot}: {e.Message}\n");
            }
        }

        return item;
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

        if (_round.State == RoundState.Stalemate && _stalemate.MeleeOnly)
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
