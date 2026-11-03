//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Shared;

namespace TF2.Server.Modes;

/// <summary>
/// Capture the flag (<c>"ctf"</c>, stock <c>TF_GAMETYPE_CTF</c>): the
/// team-round machine with CTF's arms of <c>CTFGameRules</c>
/// (tf_gamerules.cpp). The flags (<see cref="ItemTeamFlag"/>) and the zones
/// (<see cref="FuncCaptureZone"/>) score captures; this mode wins the round
/// for a team at <c>tf_flag_caps_per_round</c> captures
/// (<see cref="CheckCapsPerRound"/>, from the machine's periodic think, as
/// stock's <c>Think</c>), holds the round timer and the stalemate off while a
/// flag is away (<c>TimerMayExpire</c>'s and <c>CanGoToStalemate</c>'s flag
/// arms, the latter gated by the <c>tf_gamerules</c> entity's
/// <c>ctf_overtime</c> key), zeroes the captures at each round's start
/// (<c>RoundRespawn</c>) and when the convar drops to 0 or below
/// (<c>ValidateCapturesPerRound</c>).
/// </summary>
[SideService(Side.Server)]
public sealed class CtfMode(TFModeServices services, ICtfRules ctf, ICtfSettings settings) : TFModeBase(services)
{
    /// <summary>The id <c>sv_mode</c> and the selector name.</summary>
    public const string ModeId = "ctf";

    public override string Id => ModeId;

    public override TFGameType GameType => TFGameType.Ctf;

    /// <summary>
    /// <c>CTFGameRules::RoundRespawn</c>'s "reset the flag captures"
    /// (L15046-15055), which stock runs just before the base class's
    /// <c>SetupOnRoundStart</c>: every round starts at no captures.
    /// </summary>
    protected override void RoundStarting()
    {
        for (var team = TFTeam.Red; team < TFTeam.Count; team++)
            ctf.SetFlagCaptures(team, 0);
    }

    /// <summary>
    /// <c>TimerMayExpire</c>'s flag arm (L14022): with CTF overtime allowed
    /// (<c>GetOvertimeAllowedForCTF</c>, the <c>tf_gamerules</c> entity's
    /// <c>ctf_overtime</c> key), a timer does not expire while any enabled
    /// flag is away from home.
    /// </summary>
    public override bool TimerMayExpire()
    {
        if (ctf.OvertimeAllowedForCtf)
        {
            foreach (var flag in ctf.Flags)
            {
                if (!flag.IsDisabled && !flag.IsHome)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>CanGoToStalemate</c> (L4129): no stalemate while a flag is dropped
    /// or stolen, nor once a team's captures have won.
    /// </summary>
    public override bool CanGoToStalemate()
    {
        foreach (var flag in ctf.Flags)
        {
            if (flag.IsDropped || flag.IsStolen)
                return false;
        }

        // check that one team hasn't won by capping
        if (ctf.CheckCapsPerRound())
            return false;

        return true;
    }

    /// <summary>
    /// <c>CheckCapsPerRound</c> in the periodic think (L8165), and the convar's
    /// change callback: while <c>tf_flag_caps_per_round</c> is 0 or below no
    /// capture counts (a capture scores the team instead), so a nonzero count
    /// is one from before the change, which <c>ValidateCapturesPerRound</c>
    /// zeroes.
    /// </summary>
    public override bool CheckCapsPerRound()
    {
        if (settings.CapsPerRound <= 0)
        {
            for (var team = TFTeam.Red; team < TFTeam.Count; team++)
            {
                if (ctf.FlagCaptures(team) != 0)
                    ctf.SetFlagCaptures(team, 0);
            }

            return false;
        }

        return ctf.CheckCapsPerRound();
    }
}
