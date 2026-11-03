//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Rules;

namespace TF2.Client.Player;

/// <summary>
/// The prediction's half of the round state (<see cref="ITFRoundStateView"/>):
/// the resource's <c>RoundState</c> word, which the server's resource think
/// writes and the client decodes — the same read the win-crit arm and the HUD
/// make. Before the first resource arrives the state is stock's
/// <c>GR_STATE_INIT</c>, outside every freeze.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFRoundStateView)])]
public sealed class TFClientRoundStateView(TFClientPlayerResources? resources) : ITFRoundStateView
{
    public RoundState State
        => resources?.Live is { } live ? (RoundState)live.State.RoundState : RoundState.Init;
}
