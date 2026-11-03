//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The TF player's answer to the map's items, rooms and triggers
/// (<see cref="ITFMapPlayers"/>, the 2.4b seam): every member over the
/// player's own seams, so a kit, a locker or a trigger does what the class
/// flow does — the conditions through <see cref="ITFPlayerConditions"/> (each
/// add and removal runs the condition arms and <see cref="ITFConditionHook"/>s,
/// and a dead player takes none), the health and the ammo through
/// <see cref="ITFPlayerResupply"/>, and the locker's <c>Regenerate</c> as
/// stock's whole <c>CTFPlayer::Regenerate</c> (the missing kit pieces, the
/// class health, the nine conditions). The spy's cloak meter rides the
/// predicted carrier, as the spy lane's own meter does.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFMapPlayers)])]
public sealed class TFPlayerMapPlayers(IEngineEntities entities, IEnginePlayers players, ITFPlayerClasses classes,
                                       ITFPlayerResupply resupply, ITFPlayerConditions conditions,
                                       TFPlayerCarriers carriers,
                                       ITFWeaponOwners? owners = null, AttributeHooks? attributes = null) : ITFMapPlayers
{
    private readonly ITFWeaponOwners _owners = owners ?? new NoTFWeaponOwners();
    private readonly AttributeHooks _attributes = attributes ?? new AttributeHooks();

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public int Team(EntityHandle player) => players.GetTeam(player);

    public TFClass ClassOf(EntityHandle player) => classes.ClassOf(player);

    public int Health(EntityHandle player) => entities.GetHealth(player);

    public int MaxHealth(EntityHandle player) => resupply.MaxHealth(player);

    public int TakeHealth(EntityHandle player, float health) => resupply.TakeHealth(player, health);

    public bool InCond(EntityHandle player, TFCond cond) => conditions.InCond(player, cond);

    public void AddCond(EntityHandle player, TFCond cond, float duration) => conditions.Add(player, cond, duration);

    public void RemoveCond(EntityHandle player, TFCond cond) => conditions.Remove(player, cond);

    public void RemoveAllCond(EntityHandle player) => conditions.RemoveAll(player);

    public bool IsInvulnerable(EntityHandle player)
        => conditions.Of(player.EntryIndex) is { } set
           && (set.Has(TFCond.Invulnerable) || set.Has(TFCond.InvulnerableUserBuff)
               || set.Has(TFCond.InvulnerableHideUnlessDamaged) || set.Has(TFCond.InvulnerableCardEffect));

    public int MaxAmmo(EntityHandle player, TFAmmo ammo) => resupply.MaxAmmo(player, ammo);

    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => resupply.GiveAmmo(player, count, ammo);

    public void Regenerate(EntityHandle player) => resupply.Regenerate(player);

    /// <summary>
    /// <c>CTFPlayerShared::SelfBurn</c>'s condition: burning, with no attacker.
    /// The afterburn's damage and its clock are the damage lane's (2.7).
    /// </summary>
    public void SelfBurn(EntityHandle player, float duration) => conditions.Add(player, TFCond.Burning, TFConditions.Permanent);

    /// <summary>
    /// <c>CTFPlayerShared::AddToSpyCloakMeter</c>
    /// (<c>shared/tf/tf_player_shared.cpp:9725-9758</c>), the item path
    /// (<c>bForce == false</c>). The watch's arms are walked on the player's
    /// own manager — the house shape, the same container the spy lane's
    /// <c>mod_disguise_consumes_cloak</c> arm walks (<c>TFSpy</c>) — and the
    /// owning-watch gate is the spy's class, as the spy lane gates the watch
    /// to the spy alone. The meter is the carrier's predicted
    /// <c>m_flCloakMeter</c>.
    /// </summary>
    public bool AddToSpyCloakMeter(EntityHandle player, float amount)
    {
        if (classes.ClassOf(player) != TFClass.Spy)
            return false; // :9727-9729 — Weapon_OwnsThisID(TF_WEAPON_INVIS): the watch is the spy's own item

        if (carriers.Get(player.EntryIndex) is not { } state)
            return false; // the meter lives on the carrier; a player with none has none to move

        var manager = _owners.ManagerOf(player.EntryIndex);
        if (_attributes.Int(0, "mod_cloak_no_regen_from_items", manager) != 0)
            return false; // :9733-9736 — the watch may refuse regen from items

        // :9740-9750 — a watch that takes no cloak while cloaked takes none when
        // stealthed; every other case takes the amount ReducedCloakFromAmmo hands it.
        if (conditions.InCond(player, TFCond.Stealthed) && _attributes.Int(0, "NoCloakWhenCloaked", manager) != 0)
            return false;
        else
            amount = _attributes.Float(amount, "ReducedCloakFromAmmo", manager);

        bool result = amount > 0f && state.CloakMeter < 100f; // :9753
        state.CloakMeter = Math.Clamp(state.CloakMeter + amount, 0f, 100f); // :9755
        return result;
    }
}
