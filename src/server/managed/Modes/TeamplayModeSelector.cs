//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The core's own mode pick (plan_bots_teams_tf2 A.3 item 4): team deathmatch
/// when <c>mp_teamplay</c> is 1 at level init, else no opinion and the core's
/// <c>"null"</c>. Stock latches the knob once per level, in the rules'
/// constructor (<c>m_bTeamPlayEnabled</c>, hl2mp_gamerules.cpp:195), which is
/// exactly when <see cref="GameModes"/> asks this.
///
/// <para>
/// Asked AFTER every registered <see cref="IGameModeSelector"/>, by
/// <see cref="GameModes"/> itself and not as a hook of the chain: a mod that
/// pins its own mode (TF2's map detection) answers first, and the core takes
/// no number in the chain's order, which belongs to the mods. Mid-level,
/// <c>sv_mode tdm</c> and the admin menu switch without it.
/// </para>
/// </summary>
public sealed class TeamplayModeSelector(TeamPlayConVar teamplay) : IGameModeSelector
{
    public string? PickMode() => teamplay.Value ? TeamDeathmatchMode.ModeId : null;
}
