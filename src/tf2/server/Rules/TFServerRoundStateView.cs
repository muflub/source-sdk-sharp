//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// The server's answer: the round flow's own state — stock's gamerules
/// <c>State_Get()</c>, whose restart window is the state check itself
/// (<c>InRoundRestart</c>, teamplayroundbased_gamerules.h:179:
/// <c>GR_STATE_PREROUND</c>).
/// </summary>
[SideService(Side.Server, As = [typeof(ITFRoundStateView)])]
public sealed class TFServerRoundStateView(ITeamRoundState rounds) : ITFRoundStateView
{
    public RoundState State => rounds.State;
}
