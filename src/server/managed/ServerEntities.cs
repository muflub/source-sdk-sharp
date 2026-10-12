//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>IServerGameEnts</c>' managed half — plan_server.md Phase 8b,
/// docs/plan-audit.md item C4.
///
/// <para>
/// One method today, and it is the one the plan names: <b>which hierarchy
/// answers for a client, and in which order.</b>
/// </para>
///
/// <para>
/// <b>Why this is a class at all for one method.</b>
/// <c>CServerGameEnts::CheckTransmit</c> is, in plan_server.md's own words,
/// <i>"the only method here whose regression is silent and partial — entities
/// that stop being sent to one client in one area"</i>. The plan's instruction
/// for it is explicit and is a limit as much as a task: <i>"CheckTransmit is
/// one step plus <c>ServerSkyboxAreaForClient</c>"</i>, and the audit repeats
/// it — <i>"keep CheckTransmit's loop native per the plan's own rule — the work
/// is one step plus <c>ServerSkyboxAreaForClient</c>, not a transliteration"</i>.
/// So this is deliberately small, and a future method of the same shape has
/// somewhere to go that is not <see cref="ServerGameDll"/>.
/// </para>
/// </summary>
public sealed class ServerEntities : IServerEntities
{
    private readonly IEngineVisibility _visibility;

    public ServerEntities(IEngineVisibility visibility)
    {
        _visibility = visibility;
    }

    /// <summary>
    /// <b>The order, and it is the whole of this method.</b>
    ///
    /// <para>
    /// Stock's <c>CheckTransmit</c> opened with a disjunction in C++
    /// (gameinterface.cpp:2554-2586, carried into <c>shell_gameents.cpp</c>):
    /// resolve the recipient through the SDK entity list; if that answers
    /// nothing, reach for <c>CStandalonePlayer::FromEdict</c>; then choose
    /// between two skybox areas with a ternary. Which hierarchy answers, and in
    /// which order, is a DECISION, and plan_gamedll.md Part 4's rule is that a
    /// native step is never one.
    /// </para>
    ///
    /// <para>
    /// <b>SDK FIRST, and that is stock's order rather than a preference.</b>
    /// Stock only reaches for the standalone player when
    /// <c>GetContainingEntity</c> came back NULL, so an edict that both
    /// hierarchies would claim is answered by the SDK one. Nothing in the tree
    /// produces such an edict today — the two hierarchies partition the handle
    /// table — but the order is the observable part of the old code and this
    /// method exists to preserve it, not to improve on it.
    /// </para>
    ///
    /// <para>
    /// <b>The out parameter is written only on a hit.</b> Area 0 is a real
    /// visleaf area, so "the world's first area" and "no such recipient" cannot
    /// share a value: <c>CheckTransmit</c> returns early for one of them and
    /// walks every edict in the snapshot for the other.
    /// </para>
    /// </summary>
    /// <param name="clientEdictIndex">
    /// The recipient's edict index, or 0 — which is the edict-0 fallback stock
    /// applies when the engine names no client, computed on the native side
    /// where <c>pInfo-&gt;m_pClientEnt</c> is.
    /// </param>
    public bool SkyboxAreaForClient(int clientEdictIndex, out int area)
    {
        if (_visibility.TrySdkPlayerSkyboxArea(clientEdictIndex, out area))
            return true;

        return _visibility.TryStandalonePlayerSkyboxArea(clientEdictIndex, out area);
    }

    /// <summary>
    /// Stock <c>CBasePlayer::ShouldTransmit</c>'s first arm: a client's own
    /// player is always sent to it ("always update the recipient player data
    /// even if it's nodraw (first person mode)"), before the EF_NODRAW and
    /// observer rule and before any PVS test. A client's player is the entity
    /// at its own edict; edict 0 is the no-client fallback (the world).
    /// CheckTransmit asks only for a recipient <see cref="SkyboxAreaForClient"/>
    /// found, so the player exists.
    /// </summary>
    public int TransmitAlwaysForClient(int clientEdictIndex) => clientEdictIndex > 0 ? clientEdictIndex : -1;
}
