//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Client.Player;

/// <summary>
/// The blast jump's smoke, the client half of stock's
/// <c>TFPlayerThink</c> arm (tf_player.cpp:1509-1515):
/// <c>rocketjump_smoke</c> on each foot, <c>foot_L</c> and
/// <c>foot_R</c> at <c>PATTACH_POINT_FOLLOW</c>, once per jump.
///
/// <para>
/// Stock decides the arm on the server, behind
/// <c>m_bCreatedRocketJumpParticles</c> — set at :1514, cleared by
/// <c>ClearBlastJumpState</c> (:8709) — and
/// <c>DispatchParticleEffect</c> hands every client the effects. This port's
/// clients decide it themselves from the networked
/// <c>TF_COND_BLASTJUMPING</c> (the condition the same functions
/// add, :8701, and remove, :8712): the cond's rising edge is the jump, its
/// fall the landing, so one start per jump and no restart while it rides —
/// the flag's shape, seen from the other side. The two feet wear slots
/// beside the burn's, the glow's and the drips', one system per foot as stock
/// hangs two <c>ParticleProp</c> effects on the player.
///
/// <para>
/// Clearing stops nothing, as stock's :8707-8713 touches no particle: the
/// systems are one-shots that die of their own duration, and the next jump's
/// start replaces them.
/// </para>
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFBlastJumpSmoke(IEngineEffects effects, IEngineEntityRender render)
    : ITFClientConditionHook, IOrderedHook
{
    /// <summary><c>rocketjump_smoke</c> (tf_player.cpp:1511).</summary>
    public const string Effect = "rocketjump_smoke";

    /// <summary>The smoke's attachments (:1512-1513).</summary>
    public const string FootLeft = "foot_L", FootRight = "foot_R";

    /// <summary>The feet's slots, after the burn's, the glow's and the drips'.</summary>
    public const int FootLeftSlot = 4, FootRightSlot = 5;

    /// <summary>The players whose jump is smoking.</summary>
    private readonly HashSet<EntityHandle> _smoking = [];

    public int HookOrder => TFClientConditionOrders.BlastJumpSmoke;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.Blastjumping || !_smoking.Add(player))
            return;

        Smoke(player, FootLeftSlot, FootLeft);
        Smoke(player, FootRightSlot, FootRight);
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Blastjumping)
            _smoking.Remove(player);
    }

    private void Smoke(EntityHandle player, int slot, string foot)
    {
        if (!effects.ParticleSystemStartSlot(player, Effect, slot))
            return;

        effects.ParticleSystemFollowEntitySlot(player, 0, player, render.LookupAttachment(player, foot), slot);
    }
}
