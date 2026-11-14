//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Combat;

/// <summary>
/// The bucket of crits the sapper's kills store — stock's
/// <c>CTFPlayerShared::m_iRevengeCrits</c> beside
/// <c>SetRevengeCrits</c> (<c>tf_player_shared.cpp:14472-14488</c>):
///
/// <para>
/// — a building removed while a sapper rides it pays the sapper's owner one
/// stored crit (<c>CBaseObject::UpdateOnRemove</c>, <c>tf_obj.cpp:269-283</c>,
/// <see cref="Grant"/>).
/// </para>
/// <para>
/// — the bucket is clamped to <c>[0, 35]</c> (:14474) and shows itself as
/// <c>TF_COND_CRITBOOSTED</c> only while the player's ACTIVE weapon can take
/// the boost — <c>CanHaveRevengeCrits</c>' first arm
/// (<c>tf_weaponbase.cpp:6647-6652</c>: the held item's
/// <c>sapper_kills_collect_crits</c> not zero, walked on the active item the
/// way <see cref="ITFWeaponOwners.ActiveItemOf"/> models stock's
/// <c>GetActiveTFWeapon</c> ON_OTHER root). Adding the first crit adds the
/// condition, the last removal takes it (:14479-14486).
/// </para>
/// <para>
/// — the revolver whose item carries the attribute spends one crit per shot
/// (<c>CTFRevolver::PrimaryAttack</c>, <c>tf_weapon_revolver.cpp:159-171</c>,
/// gated on the held item's <c>sapper_kills_collect_crits</c> == 1,
/// <c>tf_weapon_revolver.h:51</c>; <see cref="SpendOnShot"/>). The bucket's
/// two other consumers, <c>GetCount</c>'s label and the Holster/Deploy
/// re-arm (:229-248, :264-313), wait on the attribute-weapon work
/// (<c>docs/plans/tf2/deferred.tsv</c>); so do <c>CanHaveRevengeCrits</c>'
/// other two arms (<c>extinguish_revenge</c>, <c>sentry_killed_revenge</c>),
/// other items' rows.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFRevengeCrits)])]
public sealed class TFSapperRevengeCrits(ITFPlayerConditions conditions, IEnginePlayers players,
                                   ITFWeaponOwners owners, AttributeHooks hooks) : ITFRevengeCrits
{
    /// <summary><c>clamp(iVal, 0, 35)</c>'s ceiling (tf_player_shared.cpp:14474).</summary>
    public const int MaxStoredCrits = 35;

    private readonly Dictionary<int, int> _counts = new();

    /// <summary>Stock's sapper-crit arm (:269-283): the owner of the sapper
    /// riding the removed building takes the bucket's next crit.</summary>
    public void Grant(EntityHandle owner)
    {
        if (owner == EntityHandle.None)
            return;

        Set(owner.EntryIndex, Count(owner.EntryIndex) + 1);
    }

    /// <summary>The revolver's spend (:159-171): behind the held item's
    /// <c>sapper_kills_collect_crits</c> == 1 (header :51), one stored crit
    /// off the bucket for the shot just fired.</summary>
    public void SpendOnShot(int ownerIndex)
    {
        if (hooks.Int(0, "sapper_kills_collect_crits", owners.ActiveItemOf(ownerIndex)) != 1)
            return;

        int count = Count(ownerIndex);
        if (count > 0)
            Set(ownerIndex, count - 1);
    }

    /// <summary><c>GetRevengeCrits</c>: what the bucket holds for the player.</summary>
    public int Count(int ownerIndex) => _counts.TryGetValue(ownerIndex, out var count) ? count : 0;

    /// <summary><c>SetRevengeCrits</c> (:14472-14488): store the clamp, and
    /// while the held item can take the boost, the first crit adds the boost
    /// condition and the last one's removal takes it.</summary>
    private void Set(int ownerIndex, int value)
    {
        int stored = value < 0 ? 0 : value > MaxStoredCrits ? MaxStoredCrits : value;   // :14474
        _counts[ownerIndex] = stored;

        // CanHaveRevengeCrits' sapper arm (tf_weaponbase.cpp:6647-6652), asked
        // of the ACTIVE weapon as stock's GetActiveTFWeapon does (:14476-14477).
        if (hooks.Int(0, "sapper_kills_collect_crits", owners.ActiveItemOf(ownerIndex)) == 0)
            return;

        EntityHandle owner = players.GetHandle(ownerIndex);
        if (stored > 0 && !conditions.InCond(owner, TFCond.Critboosted))
            conditions.Add(owner, TFCond.Critboosted);                                   // :14479-14482
        else if (stored == 0 && conditions.InCond(owner, TFCond.Critboosted))
            conditions.Remove(owner, TFCond.Critboosted);                              // :14483-14486
    }
}
