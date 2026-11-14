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
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The Frontier Justice's revenge window (<c>CTFPlayerShared::SetRevengeCrits</c>,
/// <c>tf_player_shared.cpp:14472-14488</c>): the crit pile the owner's
/// sentry's death pays (<c>CTFShotgun_Revenge::SentryKilled</c>,
/// <c>tf_weapon_shotgun.cpp:151-163</c>, from the death's
/// <c>Kills * 2 + Assists</c>, tf_obj_sentrygun.cpp:2062), spent one a shot
/// (<c>CTFShotgun_Revenge::PrimaryAttack</c>, :132-146). The count clamps at
/// stock's 0..35 (:14474) and every change arms the boost the way the setter's
/// tail does: while the ACTIVE weapon can have revenge crits at all
/// (<c>CTFWeaponBase::CanHaveRevengeCrits</c>,
/// <c>tf_weaponbase.cpp:6647-6665</c>), a count over zero wears
/// <c>TF_COND_CRITBOOSTED</c> and a count at none loses it.
///
/// <para>
/// Stock lights and puts out the cond again at the weapon's deploy and
/// holster (<c>tf_weapon_shotgun.cpp:168-195</c>); here the shot's own
/// bookends carry the arm instead (<see cref="TFRevengeHook"/>), so the
/// window lights for the held Justice's shots however the count arrived —
/// stock's grant through a switch is the deploy arm's re-add. The count
/// dies with its holder at the death, as the heads do
/// (<see cref="TFSwordHeads"/>): stock resets it on the spawn's shared init
/// (<c>tf_player_shared.cpp:1014</c>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFRevengeCrits(TFPlayerCarriers carriers, ITFWeaponOwners owners, AttributeHooks attributes)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>The setter's clamp (tf_player_shared.cpp:14474).</summary>
    public const int MaxCrits = 35;

    /// <summary>The revenge loader's script (<c>TF_WEAPON_SENTRY_REVENGE</c>'s).</summary>
    public const string ShotgunScript = "tf_weapon_sentry_revenge";

    /// <summary>The dead engineer's pile goes with him: stock's spawn reset one life earlier, as the heads take it.</summary>
    public int HookOrder => TFPlayerOrders.RevengeCritReset;

    /// <summary><c>GetRevengeCrits</c> (tf_player_shared.h:664).</summary>
    public int Of(int playerIndex) => carriers.Get(playerIndex)?.RevengeCrits ?? 0;

    /// <summary><c>SetRevengeCrits( GetRevengeCrits() + iCrits )</c>, the sentry death's pay (:14472-14488).</summary>
    public void Grant(int playerIndex, int crits) => Set(playerIndex, Of(playerIndex) + crits);

    /// <summary>The shot's spend (<c>CTFShotgun_Revenge::PrimaryAttack</c>'s tail, tf_weapon_shotgun.cpp:143-145).</summary>
    public void Consume(int playerIndex) => Set(playerIndex, Of(playerIndex) - 1);

    /// <summary>
    /// The setter's cond tail for a count that did not move: the window the
    /// grant lit while another weapon was in the hands lights at the Justice's
    /// own next shot. Stock re-adds at the deploy (:184-192); this is that
    /// arm, asked where the shot is.
    /// </summary>
    public void Arm(int playerIndex)
    {
        if (carriers.Get(playerIndex) is { } holder)
            Arm(playerIndex, holder.RevengeCrits);
    }

    /// <summary>The count dies with its holder, the heads' rule (<see cref="TFSwordHeads"/>).</summary>
    public void OnPlayerDied(PlayerDeath death)
    {
        if (carriers.Get(death.Player.EntryIndex) is { } dying)
            dying.RevengeCrits = 0;
    }

    /// <summary>The setter's body: the clamp, then the cond tail (:14474-14487).</summary>
    private void Set(int playerIndex, int count)
    {
        if (carriers.Get(playerIndex) is not { } holder)
            return;

        holder.RevengeCrits = count < 0 ? 0 : count > MaxCrits ? MaxCrits : count;
        Arm(playerIndex, holder.RevengeCrits);
    }

    /// <summary>
    /// The tail: while the active weapon can have revenge crits at all, a
    /// count over zero wears the boost and a count at none loses it
    /// (<c>tf_player_shared.cpp:14476-14487</c>).
    /// </summary>
    private void Arm(int playerIndex, int count)
    {
        if (carriers.ConditionsOf(playerIndex) is not { } conditions
            || !CanHaveRevengeCrits(playerIndex))
            return;

        bool boosted = conditions.Has(TFCond.Critboosted);
        if (count > 0 && !boosted)
            conditions.Add(TFCond.Critboosted);
        else if (count == 0 && boosted)
            conditions.Remove(TFCond.Critboosted);
    }

    /// <summary><c>CTFWeaponBase::CanHaveRevengeCrits</c> over the active weapon's item (tf_weaponbase.cpp:6647-6665).</summary>
    private bool CanHaveRevengeCrits(int playerIndex)
    {
        if (owners.ActiveItemOf(playerIndex) is not { } item)
            return false;

        return attributes.Int(0, "sapper_kills_collect_crits", item) != 0
            || attributes.Int(0, "extinguish_revenge", item) != 0
            || attributes.Int(0, "sentry_killed_revenge", item) != 0;
    }
}
