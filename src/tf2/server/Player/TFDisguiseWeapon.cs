//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The disguise weapon (<c>CTFPlayerShared::DetermineDisguiseWeapon</c>,
/// tf_player_shared.cpp:8394-8575): the weapon a disguised spy is seen
/// holding. Stock hands out a second, fake weapon entity
/// (<c>m_hDisguiseWeapon</c>); here the ONE weapon entity a player carries is
/// what wears the disguise, so the disguise is the carried entity dressed
/// with the disguise class's own primary (<c>DetermineDisguiseWeapon</c>'s
/// forced-primary arm :8396-8399, the base item resolved like the slot
/// forecast's) for as long as <c>TF_COND_DISGUISED</c> stands — restored to
/// the carried weapon's own model when it ends, over every deploy between
/// (<see cref="WeaponNetState.DeployEpoch"/>, which
/// <c>Arsenal.Deploy</c>'s own dress precedes).
///
/// <para>
/// Stock's target's-own-item arm and the medieval mode's forced melee are
/// narrowed away with the wearables
/// (<c>DetermineDisguiseWearables</c>, docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFDisguiseWeapons(IEngineEntityRender render, IEntityDirectory entities,
                                      IEnginePlayers players, ItemSchema schema,
                                      TFClassTable classes, WeaponCatalog weapons)
{
    // What the dress is wearing, per player: the entity dressed, the row's
    // name and the deploy epoch it was dressed at.
    private readonly Dictionary<int, (EntityHandle Weapon, string Row, int Epoch)> _dressed = new();

    /// <summary>The think's tail: keep the dress right for the tick that is ending.</summary>
    public void Tick(int playerIndex, TFPlayerNetState state)
    {
        if (CarriedWeapon(playerIndex) is not { } carried)
        {
            _dressed.Remove(playerIndex);
            return;
        }

        if (state.Conditions().Has(TFCond.Disguised))
        {
            if (DisguiseRow((TFClass)state.DisguiseClass) is not { } row)
                return;

            if (_dressed.TryGetValue(playerIndex, out var held)
                && held.Weapon == carried.Handle
                && held.Epoch == carried.State.DeployEpoch
                && held.Row == row.Name)
                return;

            render.SetModel(carried.Handle, row.WorldModel);
            _dressed[playerIndex] = (carried.Handle, row.Name, carried.State.DeployEpoch);
        }
        else if (_dressed.Remove(playerIndex))
        {
            // The disguise went (the removal arms, a death's RemoveAll): the
            // carried weapon's own model back, the engine's active-weapon
            // answer naming its row.
            string active = players.GetActiveWeapon(players.GetHandle(playerIndex));
            if (weapons.For(active) is { } own)
                render.SetModel(carried.Handle, own.WorldModel);
        }
    }

    /// <summary>The weapon entity the player carries, the one whose model the disguise wears.</summary>
    private SsWeapon? CarriedWeapon(int playerIndex)
        => entities.OfClass(EntityNames.SsWeapon)
                   .OfType<SsWeapon>()
                   .FirstOrDefault(w => w.State.OwnerIndex == playerIndex);

    /// <summary>The disguise class's own primary, as the slot forecast resolves the loadout rows.</summary>
    private WeaponType? DisguiseRow(TFClass disguiseClass)
    {
        if (schema.BaseItem(disguiseClass, TFLoadoutSlot.Primary) is not { } item
            || item.ItemClass is not { Length: > 0 } itemClass)
            return null;

        return weapons.For(TFItemClassTranslation.Translate(itemClass, classes[disguiseClass]));
    }
}
