//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Entities.Ctf;
using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// The teammate glow's force-transmit, <c>CTFPlayer::ShouldTransmit</c>
/// (tf_player.cpp:3508-3534) over its team arm
/// <c>ShouldForceTransmitsForTeam</c> (:22887-22891), on the core's
/// <see cref="ITransmitRule"/> seam: for a TF-player entity, the recipient
/// spectating, or same-team-and-(dead-or-glowing), is sent the teammate
/// whatever the PVS says (:3528-3530, :22889-22890); everyone else keeps the
/// ordinary PVS check (:3533). The glow is the recipient's own
/// (<c>m_Shared.InCond( TF_COND_TEAM_GLOWS )</c> reads on the caller, the
/// recipient), so a glow-holder sees all their living teammates through the
/// walls past the PVS, and the dead see theirs.
///
/// <para>
/// The student/coach and passtime arms of the stock walk (:3510-3526) are not
/// ported: no coach mode, no passtime mode this build has
/// (docs/plans/tf2/deferred.tsv). The own-player arm of the FULLCHECK walk
/// (<see cref="SourceSharp.Server.Interop.ClientsBinding"/>'s
/// <c>ServerTransmitAlwaysForClient</c>) already sends every player to their
/// own client, so the single-client case needs no glow to hold.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITransmitRule)])]
public sealed class TFGlowTransmit(ICtfPlayers players, IEnginePlayers enginePlayers) : ITransmitRule, IOrderedHook
{
    public int HookOrder => CtfOrders.GlowTransmit;

    /// <summary>
    /// The stock walk, thinned to the team arm: a non-player edict is none of
    /// this rule's business (:3533's <c>BaseClass::ShouldTransmit</c>); the
    /// slot's client is asked as the caller of :22887 — its own team, its own
    /// glow, its own life. A gone slot answers no team (<see cref="TFTeam.Unassigned"/>),
    /// which the stock null-client check also left to the PVS.
    /// </summary>
    public TransmitAnswer ShouldTransmit(EntityHandle entity, int recipientSlot)
    {
        if (!players.IsPlayer(entity))
            return TransmitAnswer.Default;

        EntityHandle recipient = enginePlayers.GetHandle(recipientSlot);

        // :22889 — spectators see everyone.
        if (players.Team(recipient) == TFTeam.Spectator)
            return TransmitAnswer.Always;

        // :22890 — same team, and the recipient is dead or holds the glow.
        if (players.Team(recipient) != players.Team(entity))
            return TransmitAnswer.Default;

        return !players.IsAlive(recipient) || players.Conditions(recipient).Has(TFCond.TeamGlows)
            ? TransmitAnswer.Always
            : TransmitAnswer.Default;
    }
}
