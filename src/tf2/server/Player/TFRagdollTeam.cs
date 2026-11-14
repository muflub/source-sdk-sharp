//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The team and class a death's body wears, written on the corpse at the
/// death: stock's ragdoll <c>m_iTeam</c>/<c>m_iClass</c>, the pair the client
/// colours and poses the body by (tf_player.cpp:15625-15626). A body that
/// took its team from its owner's LIVE row would change colour when the
/// owner switched sides after dying — the corpse keeps the team it died on
/// (ruled 2026-11-02). A real death's body is the player's own team and
/// class, disguised or not (<c>CreateRagdollEntity</c>, tf_player.cpp:15625-15626);
/// only the Dead Ringer's fake corpse wears a disguise (:15870-15879), and
/// that corpse is not this hook's. The corpse path's own stamp
/// (<c>Corpses.Make</c>) is the team's; this walk reads it at the death and
/// adds the class.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRagdollTeam(IEntityDirectory directory, ITFPlayerData players, IEnginePlayers engine)
    : IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.RagdollTeam;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (death.Body == EntityHandle.None)
            return;

        if (directory.Get(death.Body) is not SsRagdoll body)
            return;

        // The team they died on and the class they died as (:15625-15626),
        // read once at the death, so the byte on the body is the death's and
        // not the store's future.
        TFPlayerNetState? state = players.Networked(death.EntIndex);
        body.State.Team = (byte)engine.GetTeam(death.Player);
        if (state is { } living)
            body.State.Class = (byte)living.PlayerClass;
    }
}
