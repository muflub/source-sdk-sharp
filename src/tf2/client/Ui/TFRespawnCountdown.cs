//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Client.Player;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Client.Ui;

/// <summary>
/// The respawn countdown's data (stock's <c>CTFSpectatorGUI::UpdateReinforcements</c>,
/// tf_spectatorgui.cpp:343-447): the local row's <c>m_flNextRespawnTime</c> —
/// an absolute time, 0 while alive — against the clock now, and the round's
/// two named states. Stock's hidden arms (not on a team, alive, in the
/// freeze cam) are the screen's gate; the data answers what the label asks.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFRespawnCountdown)])]
public sealed class TFRespawnCountdown(IEngineHud screen, TFClientPlayerResources resources, IGameClock clock)
    : ITFRespawnCountdown
{
    /// <summary>Whole seconds until the wave lets the player out
    /// (<c>flNextRespawn - gpGlobals-&gt;curtime</c>, :400-401); stock's
    /// <c>(int)</c> cast truncates, so a fraction of a second still counts.</summary>
    public int WaitSeconds
        => Row is ({ } live, var index) ? Math.Max(0, (int)(live.State.NextRespawnTime[index] - clock.Now)) : 0;

    public bool HasWave => Row is ({ } live, var index) && live.State.NextRespawnTime[index] > 0f;

    public bool Stalemate => RoundIs(RoundState.Stalemate);

    public bool TeamWin => RoundIs(RoundState.TeamWin);

    /// <summary>Whether the round state word says <paramref name="state"/>:
    /// the gamerules' <c>m_iRoundState</c> carried on the player resource.</summary>
    private bool RoundIs(RoundState state)
        => Row is ({ } live, _) && live.State.RoundState == (byte)state;

    /// <summary>The local row, when the TF resource has arrived and the local
    /// index is a player's slot (0 is the world).</summary>
    private (ClientTFPlayerResource? Live, int Index) Row
        => resources.Live is { } live && screen.LocalPlayerIndex is > 0 and < TFPlayerResourceNetState.Slots
            ? (live, screen.LocalPlayerIndex)
            : (null, 0);
}
