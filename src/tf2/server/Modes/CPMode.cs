//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Server.Modes;

/// <summary>
/// Control points (<c>"cp"</c>, stock <c>TF_GAMETYPE_CP</c>): the team-round
/// machine with the control-point game type. The points and their master
/// (<see cref="Entities.TeamControlPointMaster"/>) are the referee — the
/// master declares the round won for the team that owns every point of the
/// group being played — and the defender's countdown is
/// <see cref="Entities.CPTimerLogic"/> (<c>tf_logic_cp_timer</c>), which also
/// holds the round timer open while its team can still take its point.
///
/// <para>
/// The even-teams arm of stock's <c>CTFPlayer::GetAutoTeam</c> (tf_player.cpp:6129)
/// sends new players to the attackers for the escort game type or when RED's
/// team role is defenders; control-point maps carry the decision through the
/// team role: the map fires <c>SetRedTeamRole</c> at the rules proxy and the
/// arm answers from RED's role (<see cref="Rules.TFTeamRoles"/>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class CPMode(TFModeServices services) : TFModeBase(services)
{
    /// <summary>The id <c>sv_mode</c> and the selector name.</summary>
    public const string ModeId = "cp";

    public override string Id => ModeId;

    public override TFGameType GameType => TFGameType.Cp;
}
