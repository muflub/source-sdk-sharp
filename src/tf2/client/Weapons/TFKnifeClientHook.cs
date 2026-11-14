//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Weapons;

/// <summary>
/// The knife's client half: the replay's answers over the predicted carrier
/// (<see cref="TF2.Shared.Player.ITFPlayerData"/>) where no spy slice stands —
/// the melted knife's <c>CanDeploy</c> recomputed from the pair the server's
/// melt published, and the crit question's no (the backstab is the server's
/// damage, docs/divergences.md).
/// </summary>
[SideService(Side.Client)]
public sealed class TFKnifeClientHook(ITFPlayerData players) : TFKnifeHook, TF2.Shared.ITFWeaponHook
{
    /// <summary>The replay's own stage: past the server's 984 and the sapper command's
    /// 985, beside the wrench's 986, so no chain ties the two knives' halves.</summary>
    public new const int Order = 1_000_000 - 1_000 + 987;

    public override int HookOrder => Order;

    /// <summary>The replay's crit answer is no: the knife's crit is the server's
    /// backstab (<c>IsBackstab()</c>, tf_weapon_knife.cpp:487-500), never the
    /// prediction's roll.</summary>
    public TFCritResult? CritAnswer(in TFWeaponHookContext weapon) =>
        IsKnife(in weapon) ? new TFCritResult(false) : null;

    /// <summary>
    /// The melted knife refuses the switch itself (<c>CTFKnife::CanDeploy</c>,
    /// <c>tf_weapon_knife.cpp:521-531</c>): the knife's existence recomputed
    /// from the melt's stamp and window (:523) — here from the carrier's
    /// published pair (<see cref="TFPlayerNetState.KnifeMeltTimestamp"/>), the
    /// same line over the server's word — a melted one answering no until it
    /// regenerates (:525-528) — so the slot key, the wheel and the next-best
    /// pick all stand at the holstered knife, predicted. The server's own
    /// answer is the server hook's, over the spy slice.
    /// Not a knife, or no owner in the ask: the base yes.
    /// </summary>
    public bool CanDeploy(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (!IsKnife(in weapon) || attack.Command.OwnerIndex <= 0)
            return true;

        // The replay's half: the melted knife the server stamped on the
        // carrier, read as the decode left it (:523's line).
        if (players.Predicted(attack.Command.OwnerIndex) is not { } carrier)
            return true;

        return attack.Now - carrier.KnifeMeltTimestamp > carrier.KnifeRegenerateDuration;
    }
}
