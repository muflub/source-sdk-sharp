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
/// The server's answer: stock's gamerules flag that a round timer's state
/// change sets (<c>SetSetup</c>, tf_gamerules.cpp:5333) and the elected timer
/// (<c>GetActiveRoundTimer</c>) — the two asks the medigun's charge gate
/// joins (tf_weapon_medigun.cpp:1288).
/// </summary>
[SideService(Side.Server, As = [typeof(ITFRoundSetup)])]
public sealed class TFServerRoundSetup(ITeamRoundState round, IRoundTimers timers) : ITFRoundSetup
{
    public bool InSetupWithRoundTimer => round.InSetup && timers.Active is not null;
}
