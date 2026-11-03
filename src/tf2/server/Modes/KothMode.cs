//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Server.Modes;

/// <summary>
/// King of the Hill (<c>"koth"</c>): stock plays KOTH as the control-point
/// game type with <c>m_bPlayingKoth</c> set — <c>CTFGameRules::Activate</c>
/// (shared/tf/tf_gamerules.cpp:4384-4388) raises the flag when the map holds
/// a <c>tf_logic_koth</c> entity, and the CP arm (L4358-4362) still sets
/// <c>TF_GAMETYPE_CP</c>, so koth is CP with a timer on it. The masters and
/// points of <see cref="CPMode"/> answer to the <c>tf_logic_koth</c> entity
/// (<c>CKothLogic</c>, tf_gamerules.cpp:19863-20000, ported as
/// <c>KothLogic</c> with this lane):
/// the point stays locked for the first seconds of the round, each team's
/// hold-the-point timer waits paused, and the win is the first team whose
/// timer runs to zero while it owns the point (the arms of both
/// <c>CheckWinConditions</c>s, team_control_point_master.cpp:679-700 and
/// team_control_point_round.cpp:122-141).
/// </summary>
[SideService(Side.Server)]
public sealed class KothMode(TFModeServices services) : TFModeBase(services)
{
    /// <summary>The id <c>sv_mode</c> and the selector name.</summary>
    public const string ModeId = "koth";

    public override string Id => ModeId;

    /// <summary>Stock sets <c>TF_GAMETYPE_CP</c> and the koth flag: koth plays the CP game type.</summary>
    public override TFGameType GameType => TFGameType.Cp;

    /// <summary>Stock's <c>IsInKothMode</c>: this mode is the flag.</summary>
    public override bool InKothMode => true;
}
