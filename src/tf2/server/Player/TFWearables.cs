//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The wearables' stages (Stage 2.13's band, +990..+999 of TF2's thousand):
/// after every other TF participant, so the spawn hook sees the class
/// <see cref="TFPlayerSpawns"/> just set and the kit the loadout just handed
/// out (TF2LifecycleOrderTests holds range and uniqueness).
/// </summary>
public static class TFWearableOrders
{
    private const int Band = 1_000_000 - 1_000 + 990;

    /// <summary>The wearable arm of the spawn (and of the death): <c>TFWearableSpawns</c>.</summary>
    public const int Spawn = Band + 0;

    /// <summary><c>TFWearableComponents</c>, in the composite's source chain (after <c>TFPlayerComponents</c>).</summary>
    public const int Components = Band + 1;
}

/// <summary>
/// One player's wearables (Stage 2.13's WearablesComponent): the
/// <c>tf_wearable</c> entities stock's <c>CTFPlayer::ManageRegularWeapons</c>
/// hands out for the WEARABLE slots of the class loadout
/// (tf_player.cpp:4565-4660 — every slot's item whose
/// <c>IsWearableSlot</c> is true becomes a <c>CEconEntity</c> given to the
/// player, and <c>CTFWearable::Equip</c>, tf_item_wearable.cpp:727-755, sets
/// its team skin). The player owns the list through their whole stay:
/// <see cref="Rebuild"/> replaces the whole set at each spawn (the class
/// change's old set goes with the old class's loadout, as stock's
/// <c>ValidateWearables</c>, tf_player.cpp:5251-5323, removes what no longer
/// matches the new class), and <see cref="Detach"/> — the player left, or the
/// level ended — removes what is left, so a departure leaves none. The death
/// arm is <see cref="TFWearableSpawns"/>: stock's wearables die with their
/// player and respawn with them.
///
/// <para>
/// A wearable item with no player display model (<c>model_player_per_class</c>
/// for this class and <c>model_player</c> both empty — stock's spellbook, say)
/// gets no entity: stock spawns one that draws nothing, and nothing here
/// reads it server-side (docs/divergences.md).
/// </para>
/// </summary>
public sealed class TFWearables(TFPlayerCarriers carriers, IEngineEntities entities, IEntityDirectory directory,
                                IEnginePrecache precache, ILog log)
    : IPlayerComponent
{
    private readonly List<EntityHandle> _wearables = [];

    private int _index = -1;

    /// <summary>After the condition ticker (20): the wearables ride the class and its kit.</summary>
    public int Order => 30;

    /// <summary>How many wearables the player wears now.</summary>
    public int Count => _wearables.Count;

    /// <summary>The player's wearable entities, in equip order.</summary>
    public IReadOnlyList<EntityHandle> Wearables => _wearables;

    public void Attach(EntityHandle player)
    {
        _index = player.EntryIndex;
        carriers.EnterPart(_index, this);
    }

    public void Tick(float now) { }

    public void Detach()
    {
        RemoveAll();
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
    }

    /// <summary>
    /// Replace every wearable with one <c>tf_wearable</c> per
    /// <paramref name="wearables"/> entry: the item's display model for
    /// <paramref name="cls"/> (<c>GetPlayerDisplayModel( iClass )</c>'
    /// <c>model_player_per_class</c> else <c>model_player</c>, precached as
    /// <c>CEconWearable::Spawn</c> precaches it), the owner, the team skin and
    /// the definition, as <c>CEconWearable::Equip</c> and
    /// <c>CTFWearable::Equip</c> set them (econ_wearable.cpp:174-206,
    /// tf_item_wearable.cpp:727-755).
    /// </summary>
    public void Rebuild(EntityHandle player, TFClass cls, TFTeam team, IReadOnlyList<LoadoutItem> wearables)
    {
        RemoveAll();

        int teamNumber = (int)team;
        // CEconWearable::GetSkin: ( iTeamNumber == LAST_SHARED_TEAM+1 ) ? 0 : 1,
        // LAST_SHARED_TEAM = TEAM_SPECTATOR (shareddefs.h:283), so TEAM_RED answers 0.
        int skin = teamNumber == (int)TFTeam.Red ? 0 : 1;

        foreach (var entry in wearables)
        {
            var def = entry.Item.Definition;
            string? model = def.PlayerModel(cls);
            if (string.IsNullOrEmpty(model))
                model = def.ModelPlayer;
            if (string.IsNullOrEmpty(model))
                continue;

            int modelIndex = precache.PrecacheModel(model);

            EntityHandle wearable = entities.Create(TFEntityNames.Wearable);
            if (wearable == EntityHandle.None)
            {
                log.Warning($"[TF2/Wearables] no {TFEntityNames.Wearable} could be made for item {def.Defindex} " +
                            $"({def.Name}) of player {_index}\n");
                continue;
            }

            entities.SetOrigin(wearable, entities.GetOrigin(player));
            entities.Spawn(wearable);
            _wearables.Add(wearable);

            if (directory.Get(wearable) is not TFWearableEntity entity)
            {
                log.Warning($"[TF2/Wearables] {wearable} is not a {TFEntityNames.Wearable}; " +
                            $"item {def.Defindex} does not show\n");
                continue;
            }

            entity.State.OwnerIndex = _index;
            entity.State.ModelIndex = modelIndex;
            entity.State.Skin = skin;
            entity.State.ItemDefIndex = def.Defindex;
            // The item rides its entity (stock's CEconEntity::m_Item), so the
            // ON_OTHER arms against the wearable find its attributes.
            entity.Item = Econ.TFLoadoutProvider.Attributes(entry.Item);
        }
    }

    /// <summary>Every wearable the player wears goes (death, level end, departure, rebuild).</summary>
    public void RemoveAll()
    {
        foreach (var wearable in _wearables)
            if (entities.IsValid(wearable))
                entities.Remove(wearable);
        _wearables.Clear();
    }
}

/// <summary>
/// The wearables' claim on the per-player composite (the chain's second
/// TF2 source, beside <see cref="TFPlayerComponents"/>): the one wearable
/// component every player wears.
/// </summary>
[SideService(Side.Server)]
public sealed class TFWearableComponents(TFPlayerCarriers carriers, IEngineEntities entities,
                                         IEntityDirectory directory, IEnginePrecache precache, ILog log)
    : IPlayerComponentSource, IOrderedHook
{
    public int HookOrder => TFWearableOrders.Components;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
        => [new TFWearables(carriers, entities, directory, precache, log)];
}

/// <summary>
/// The wearables' arms of the spawn and the death (Stage 2.13): the class
/// loadout's WEARABLE slots become <c>tf_wearable</c> entities once the kit is
/// in (<c>ManageRegularWeapons</c>, which <c>GiveDefaultItems</c> runs after
/// the weapons, tf_player.cpp:4565-4660), and the death strips them
/// (stock's wearables die with their player and come back with the next
/// spawn's loadout walk).
///
/// <para>
/// Items with no player model at all are not wearables even here: the schema's
/// walk skips what the class cannot wear; <see cref="TFWearables.Rebuild"/>
/// answers the rest.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFWearableSpawns(TFPlayerCarriers carriers, TFLoadoutProvider loadout,
                                     IEnginePlayers players, ILog log)
    : IPlayerSpawnHook, IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFWearableOrders.Spawn;

    public void OnSpawning(EntityHandle player, float now) { }

    public void OnSpawned(EntityHandle player, float now)
    {
        int index = player.EntryIndex;
        var cls = carriers.ClassOf(player);
        if (cls is <= TFClass.Undefined or >= TFClass.CountAll)
        {
            // The classless player (observer) wears nothing; stock's observer
            // walk hands out no loadout either. The connect's first spawn is
            // one, and comes before the player's components attach.
            carriers.Part<TFWearables>(index)?.RemoveAll();
            return;
        }

        if (carriers.Part<TFWearables>(index) is not { } wearables)
        {
            log.Warning($"[TF2/Wearables] player {index} has no wearable component; nothing was worn\n");
            return;
        }

        var items = new List<LoadoutItem>();
        foreach (var entry in loadout.Loadout(index, cls))
        {
            if (TFItemStrings.IsWearableSlot((int)entry.Slot))
                items.Add(entry);
        }

        wearables.Rebuild(player, cls, (TFTeam)players.GetTeam(player), items);
    }

    public void OnPlayerDied(PlayerDeath death)
        => carriers.Part<TFWearables>(death.Player.EntryIndex)?.RemoveAll();
}
