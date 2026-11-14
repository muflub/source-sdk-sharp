//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Client.Player;

/// <summary>
/// The client's half of <see cref="ITFPlayerTeam"/>: the player resource's
/// team row, the server's resource think's word decoded
/// (<c>g_TF_PR-&gt;GetTeamNumber</c>, the same read the HUD's target id makes).
/// Before the first resource arrives, nobody is on a team — the mask arm then
/// leaves the stock mask untouched, as stock's switch does for a teamless
/// player (tf_gamemovement.cpp:271-281).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFPlayerTeam)])]
public sealed class TFClientPlayerTeam(TFClientPlayerResources? resources) : ITFPlayerTeam
{
    public TFTeam Of(int playerIndex)
        => resources?.Live is { } live && playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? (TFTeam)live.State.Team[playerIndex]
            : TFTeam.Unassigned;
}
