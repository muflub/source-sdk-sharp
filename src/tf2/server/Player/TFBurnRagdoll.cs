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
/// Says whether the body a death left arrived on fire — stock's server
/// ragdoll is made knowing the burning it died from, and
/// <c>DT_TFRagdoll</c> networks it as <c>m_bBurning</c>
/// (c_tf_player.cpp:518-542). The client ragdoll acts on the flag itself,
/// stamping its own burn clock and starting the corpse's particle
/// (<c>CreateTFRagdoll</c>, c_tf_player.cpp:928-932); the server copies
/// nothing else — the clock is the client's, not the wire's.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBurnRagdoll(IEntityDirectory directory, ITFPlayerData players)
    : IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.BurnRagdoll;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (death.Body == EntityHandle.None)
            return;

        if (directory.Get(death.Body) is not SsRagdoll body)
            return;

        // The conditions live through death until the respawn (nothing in the
        // death path removes them), so the dying player's own word is the one
        // to read.
        body.State.Burning = players.Predicted(death.EntIndex) is { } state
            && state.InCond(TFCond.Burning);
    }
}
