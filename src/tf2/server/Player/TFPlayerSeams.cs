//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// FOR THE MAP ITEMS (2.4b): what a resupply locker, a health kit and an ammo
/// pack do to a TF player — <c>CTFPlayer::Regenerate</c>,
/// <c>GetMaxHealth</c>, <c>TakeHealth</c>, <c>GetMaxAmmo</c> and
/// <c>GiveAmmo</c> (tf_player.cpp:4014, 8419, 14077;
/// tf_player_shared.cpp:12998). A player with no class, or no carrier, has a
/// max of zero and takes nothing.
/// </summary>
public interface ITFPlayerResupply
{
    /// <summary>
    /// <c>CTFPlayer::Regenerate</c> (func_regenerate's touch): the class's
    /// health (an overheal above it is kept), the kit's weapons with full
    /// clips, every ammo to its class maximum, and the conditions a resupply
    /// ends (burning, jarate, milk, gas, bleeding, the energy drink, phase,
    /// parachute, plague). <paramref name="refillHealthAndAmmo"/> false keeps
    /// the health and the ammo, as an upgrade station's regenerate does.
    /// </summary>
    void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true);

    /// <summary><c>CTFPlayer::GetMaxHealth</c>: the class's <c>health_max</c>.</summary>
    int MaxHealth(EntityHandle player);

    /// <summary>
    /// <c>CTFPlayer::TakeHealth</c>: up to <see cref="MaxHealth"/> (past it
    /// with <paramref name="ignoreMaxHealth"/>, stock's <c>DMG_IGNORE_MAXHEALTH</c>),
    /// nothing under <c>TF_COND_NOHEALINGDAMAGEBUFF</c>. Answers what was taken.
    /// </summary>
    int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false);

    /// <summary><c>CTFPlayer::GetMaxAmmo</c>: the class's <c>AmmoMax</c> for <paramref name="ammo"/>.</summary>
    int MaxAmmo(EntityHandle player, TFAmmo ammo);

    /// <summary><c>CTFPlayer::GiveAmmo</c>: up to <see cref="MaxAmmo"/>; answers what was taken.</summary>
    int GiveAmmo(EntityHandle player, int count, TFAmmo ammo);
}

/// <summary>
/// FOR EVERY LANE THAT ADDS OR ENDS A CONDITION (2.4b's kits, 2.7's afterburn,
/// 2.9's uber, 2.12's cloak): <c>CTFPlayerShared::AddCond</c>,
/// <c>RemoveCond</c>, <c>RemoveAllCond</c> and <c>InCond</c> on a player's
/// one condition set — the set on their <c>tf_player_data</c> carrier, the
/// same one the weapons and the movement read. Each add and removal runs the
/// condition-only arms (<see cref="TFConditionArms"/>) and then every
/// <see cref="ITFConditionHook"/>, as stock's <c>OnConditionAdded</c> and
/// <c>OnConditionRemoved</c> run on every add and removal.
/// </summary>
public interface ITFPlayerConditions
{
    /// <summary>The player's condition set, or null when the player has no carrier.</summary>
    TFConditions? Of(int playerIndex);

    /// <summary>
    /// <c>AddCond</c> (tf_player_shared.cpp:1105-1161): refused
    /// (<see cref="TFCondAdd.Refused"/>) for a dead player, a player with no
    /// carrier, and the competitive winner/loser conditions (no match summary
    /// is ever shown here).
    /// </summary>
    TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default);

    /// <summary><c>RemoveCond</c>: true when the condition was active and now is not.</summary>
    bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false);

    /// <summary><c>RemoveAllCond</c>: every condition goes; answers the set that was active.</summary>
    TFCondMask RemoveAll(EntityHandle player);

    /// <summary><c>InCond</c>.</summary>
    bool InCond(EntityHandle player, TFCond cond);
}

/// <summary>
/// A condition's effect arm: the player half of stock's
/// <c>OnConditionAdded</c>/<c>OnConditionRemoved</c>
/// (tf_player_shared.cpp:1571-1886, 1892-2230) — the arms that touch the
/// player, its weapons or its effects (docs/plans/tf2/conditions.tsv names
/// each arm's line and stage). A chain: each stage registers the arms of its
/// own conditions, and a further mod adds its own beside them. Told after the
/// condition set has changed.
/// </summary>
public interface ITFConditionHook
{
    /// <summary>A condition was added or refreshed (stock runs the arm on every add).</summary>
    void OnAdded(EntityHandle player, TFCond cond);

    /// <summary>A condition ended: removed, expired, or taken by another's arm.</summary>
    void OnRemoved(EntityHandle player, TFCond cond);
}

/// <summary>
/// The weapon entity a player carries: the core's one carried <c>ss_weapon</c>
/// per player, whose entity index a weapon's crit bucket is keyed by
/// (<c>WeaponCommand.WeaponIndex</c>). The engine's
/// <c>GetActiveWeaponEntity</c> answers a native weapon, which a managed
/// player never holds.
/// </summary>
public interface ITFCarriedWeapons
{
    /// <summary>The carried weapon entity, or <see cref="EntityHandle.None"/> before the player has one.</summary>
    EntityHandle WeaponOf(EntityHandle player);
}

/// <summary>The core arsenal's carried weapon (<see cref="Arsenal.CarriedPair"/>).</summary>
[SideService(Side.Server, As = [typeof(ITFCarriedWeapons)])]
public sealed class TFArsenalWeapons(Arsenal arsenal) : ITFCarriedWeapons
{
    public EntityHandle WeaponOf(EntityHandle player) => arsenal.CarriedPair(player).Weapon;
}
