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
/// <c>IServerGameClients</c>' managed half — plan_server.md Phase 7,
/// docs/plan-audit.md item C5.
///
/// <para>
/// One method today, and it is the one that is a DECISION: <b>which hierarchy
/// answers for a client's ears, and in which order.</b>
/// </para>
///
/// <para>
/// <b>Why this is a class at all for one method.</b> C5 lists eight sequences
/// on <c>IServerGameClients</c>, and the two the plan names as going LAST —
/// <c>ProcessUsercmds</c> and <c>ClientSetupVisibility</c> — are the two with
/// the most behaviour in them. This is the shape the rest arrive into, so that
/// a per-client question has somewhere to go that is not
/// <see cref="ServerGameDll"/> or <see cref="ServerEntities"/>.
/// </para>
/// </summary>
public sealed class ServerClients : IServerClients
{
    private readonly IEnginePlayers _players;

    /// <summary>
    /// The managed entity directory, so the first arm of
    /// <see cref="EarPositionForClient"/> can answer from the managed player
    /// rather than crossing back into native code — step 3c.
    ///
    /// <para>
    /// <b>OPTIONAL, AND THAT IS NOT A CONVENIENCE.</b> The two native steps
    /// behind it are the whole of what this crossing used to be, and they are
    /// still the answer for a client the managed directory does not know about
    /// — which is every client on any path that reaches this before the managed
    /// player exists. A null directory takes the same route.
    /// </para>
    /// </summary>
    private readonly IEntityDirectory? _entities;

    public ServerClients(IEnginePlayers players, IEntityDirectory? entities = null)
    {
        _players = players;
        _entities = entities;
    }

    /// <summary>
    /// <b>The order, and it is the whole of this method.</b>
    ///
    /// <para>
    /// <c>ClientEarPosition</c> opened with a disjunction written as an
    /// <c>#ifdef</c> (shell_gameclients.cpp): reach for
    /// <c>CStandalonePlayer::FromEdict</c>; if that answers nothing, resolve
    /// through <c>UTIL_SDKPlayerFromEdict</c>; if THAT answers nothing,
    /// <c>Assert(0)</c>. Which hierarchy answers, and in which order, is a
    /// decision, and plan_gamedll.md Part 4's rule is that a native step is
    /// never one.
    /// </para>
    ///
    /// <para>
    /// <b>STANDALONE FIRST, which is the reverse of
    /// <see cref="ServerEntities.SkyboxAreaForClient"/>.</b> That is what the
    /// body said, and here it is load-bearing rather than incidental: the SDK
    /// arm's miss path is the assert, and Phase 10 recorded it as firing "the
    /// first time anything emitted a sound ON such a player, which the managed
    /// weapon machine now does every trigger pull". Asking the SDK hierarchy
    /// first would put that assert back for every standalone player.
    /// </para>
    ///
    /// <para>
    /// <b>The out parameter is written only on a hit.</b> The world origin is a
    /// real position, and the caller's miss path asserts before writing
    /// <c>vec3_origin</c> — so "the listener is at the origin" and "no such
    /// client" cannot share three floats.
    /// </para>
    ///
    /// <para>
    /// <b>The assert is NOT here.</b> It is what the caller does with a false,
    /// it is an engine-facing diagnostic, and managed code that owned it would
    /// be deciding when an engine assert fires.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>A THIRD ARM WENT IN FRONT OF THE TWO, AND IT IS THE ONLY ONE THAT
    /// ANSWERS TODAY</b> — step 3c. The two below each ask ONE NATIVE
    /// hierarchy, and the standalone one asks it by crossing back into native
    /// code to read <c>CStandalonePlayer::EyePosition</c>: managed code being
    /// asked a question and forwarding it straight back where it came from, at
    /// the cost of a crossing each way, per PVS-filtered message. The managed
    /// player has both halves of that sum itself
    /// (<see cref="SsPlayer.ViewOrigin"/>), so it answers.
    ///
    /// <para>
    /// The two native steps STAY, in their order, and the order is still the
    /// observable part. They are what answers for a client that has no managed
    /// player yet — during the signon, and for the HLTV and Replay masters,
    /// which are clients with no entity at all.
    /// </para>
    /// </remarks>
    public bool EarPositionForClient(int clientEdictIndex, out Vector ears)
    {
        if (TryManagedPlayerViewOrigin(clientEdictIndex, out ears))
            return true;

        if (_players.TryStandalonePlayerEarPosition(clientEdictIndex, out ears))
            return true;

        return _players.TrySdkPlayerEarPosition(clientEdictIndex, out ears);
    }

    /// <summary>
    /// The managed player bound to an edict, asked for its view origin.
    ///
    /// <para>
    /// <b>MATCHED BY HANDLE ENTRY AND NOT BY A CROSSING.</b> An entity with an
    /// edict is bound in the directory under a handle whose ENTRY IS ITS EDICT
    /// INDEX (<see cref="EntityDirectory.EntryIndex"/>), so "which managed
    /// player is edict 1" is arithmetic on a handle this side already holds.
    /// Asking the engine for the handle would have been the crossing this arm
    /// exists to remove.
    /// </para>
    ///
    /// <para>
    /// <c>OfClass</c> over the player classname and not over every entity: the
    /// walk is the number of connected players, which is one or two here and
    /// bounded by <c>maxplayers</c> anywhere.
    /// </para>
    /// </summary>
    private bool TryManagedPlayerViewOrigin(int clientEdictIndex, out Vector origin)
    {
        origin = default;

        if (_entities is null || clientEdictIndex < 1)
            return false;

        foreach (ScriptedEntity instance in _entities.OfClass(EntityNames.SsPlayer))
        {
            if (instance is SsPlayer player
                && EntityDirectory.EntryIndex(player.Handle) == clientEdictIndex)
            {
                origin = player.ViewOrigin;
                return true;
            }
        }

        return false;
    }
}
