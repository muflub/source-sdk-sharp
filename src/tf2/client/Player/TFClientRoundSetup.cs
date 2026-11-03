//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Client.Entities;

using TF2.Shared.Rules;

namespace TF2.Client.Player;

/// <summary>
/// The prediction's half of the medigun's setup gate
/// (<see cref="ITFRoundSetup"/>): stock's networked <c>m_bInSetup</c> rides
/// the player resource beside the round state — the setup period runs
/// inside the living round, its doors shut, so the state word alone cannot
/// name it (teamplayroundbased_gamerules.cpp:91/:111, set through
/// <c>SetSetup</c> by the round timer's state change) — and the elected
/// timer is the round-timer mirror's (<c>GetActiveRoundTimer</c>'s client
/// half, <c>teamplayroundbased_gamerules.h:205</c>). The server's decode of
/// the meter corrects the prediction's drift besides.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFRoundSetup)])]
public sealed class TFClientRoundSetup(TFClientPlayerResources? resources, ITFRoundTimers? timers) : ITFRoundSetup
{
    public bool InSetupWithRoundTimer
        => resources?.Live is { } live
           && live.State.InSetup != 0
           && timers?.Active is not null;
}
