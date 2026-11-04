//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Server.Medigun;
using TF2.Shared;

namespace TF2.Server.Weapons;

/// <summary>
/// <c>CTFPlayer::WantsLagCompensationOnEntity</c> (server/tf/tf_player.cpp:20079-20154),
/// which replaces HL2MP's whole: no attack gate; with friendly fire off a
/// teammate is compensated only for a medic's medigun on its own patient
/// (and for no teammate while it heals another), or for a melee swing, which
/// also skips the cone (the swinger may be inside them); everyone else by the
/// reach and the cone.
/// </summary>
[SideService(Side.Server, As = [typeof(ILagCompensationTargets)])]
public sealed class TFLagCompensationTargets(MpFriendlyFireConVar friendlyFire, IEnginePlayers players, IEngineEntities entities,
                                             TFWeaponScripts scripts, ITFPlayerClasses classes,
                                             ITFMedigunWeapons? mediguns = null)
    : ILagCompensationTargets, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 708;

    public int HookOrder => Order;

    public Decision WantsLagCompensationOnEntity(in LagCompensationTarget target)
    {
        bool meleeingTeammate = false;

        if (!friendlyFire.Value)
        {
            bool sameTeam = players.GetTeam(target.Target) == players.GetTeam(target.Shooter);
            TFWeaponScript? active = ActiveScript(target.Shooter);

            // Lag compensation on medics healing teammates: their patient only.
            bool medic = classes.ClassOf(target.Shooter) == TFClass.Medic;
            if (medic && sameTeam && active is { Id: TFWeaponId.Medigun } && mediguns is not null)
            {
                int patient = mediguns.HealingTargetIndex(entities.GetIndex(target.Shooter));
                if (patient > 0)
                    return patient == entities.GetIndex(target.Target) ? Decision.Yes : Decision.No;
            }

            if (sameTeam)
            {
                // Melee on teammates is compensated (the Soldier's whip and the
                // like); nothing else is, a medic's aside.
                if (active is { Category: TFWeaponCategory.Melee })
                    meleeingTeammate = true;
                else if (!medic)
                    return Decision.No;
            }
        }

        // Not when melee-ing a teammate: we could be inside them.
        return LagCompensationRules.InReach(in target, coneCheck: !meleeingTeammate) ? Decision.Yes : Decision.No;
    }

    private TFWeaponScript? ActiveScript(EntityHandle player)
    {
        string name = players.GetActiveWeapon(player);
        return string.IsNullOrEmpty(name) ? null : scripts.Get(name);
    }
}
