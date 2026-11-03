//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;

using TF2.Client.Player;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The scoreboard's domination reads: the count column off the live
/// <c>tf_player_manager</c> (stock's <c>g_TF_PR-&gt;GetActiveDominations</c>,
/// tf_clientscoreboard.cpp:1335) and the nemesis pair off the local player's
/// own carrier — stock's client arm answers only the local player's shared
/// data (<c>IsPlayerDominated</c>'s fallthrough, tf_player_shared.cpp:
/// 7739-7751), so <c>m_bPlayerDominated</c> names whom the local player
/// dominates and <c>m_bPlayerDominatingMe</c> who dominates them
/// (tf_clientscoreboard.cpp:1507-1519).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFScoreboardDominations)])]
public sealed class TFScoreboardDominations(TFClientPlayerResources resources, ITFPlayerData data,
                                            IEngineHud screen) : ITFScoreboardDominations
{
    public int DominationsOf(int playerIndex)
    {
        if (resources.Live is not { } resource)
            return 0;                        // no resource: no row has a count
        return resource.State.ActiveDominations[playerIndex];
    }

    /// <summary>The row's player dominates the local one (the nemesis arm:
    /// <c>pTFPlayer-&gt;m_Shared.IsPlayerDominated(local)</c> falls through
    /// to the local player's <c>m_bPlayerDominatingMe</c> row).</summary>
    public bool DominatesLocal(int playerIndex)
        => Local is { } state && state.IsPlayerDominatingMe(playerIndex);

    /// <summary>The local player dominates the row's (the dominated arm:
    /// <c>pLocalTFPlayer-&gt;m_Shared.IsPlayerDominated(playerIndex)</c>).</summary>
    public bool DominatedByLocal(int playerIndex)
        => Local is { } state && state.IsPlayerDominated(playerIndex);

    /// <summary>The local player's decoded carrier, 0 the world and out of
    /// range nobody (the same slot guard TFHudSpy keeps).</summary>
    private TFPlayerNetState? Local
        => screen.LocalPlayerIndex is > 0 ? data.Networked(screen.LocalPlayerIndex) : null;
}
