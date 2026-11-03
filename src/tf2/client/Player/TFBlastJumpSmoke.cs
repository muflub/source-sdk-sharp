//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

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
/// <c>ClearBlastJumpState</c> (:8709) — on
/// <c>RocketJumped() || StickyJumped()</c> (:1509), and
/// <c>DispatchParticleEffect</c> hands every client the effects. This port's
/// clients read that gate through the events stock's <c>SetBlastJumpState</c>
/// fires for exactly those two bits (:8679-8689): the <c>rocket_jump</c> and
/// <c>sticky_jump</c> events are the gate, so an enemy-blasted rise — the
/// third bit, which fires no event — starts no smoke, as stock's bit check
/// says. The jump's own <c>TF_COND_BLASTJUMPING</c> ride (added :8701, gone
/// :8712) holds the one start per jump and re-arms on the landing — the
/// flag's shape, seen from the other side. The two feet wear slots beside
/// the burn's, the glow's and the drips', one system per foot as stock hangs
/// two <c>ParticleProp</c> effects on the player.
///
/// <para>
/// Clearing stops nothing, as stock's :8707-8713 touches no particle: the
/// systems are one-shots that die of their own duration, and the next jump's
/// start replaces them.
/// </para>
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFBlastJumpSmoke : ITFClientConditionHook, IOrderedHook
{
    /// <summary><c>rocketjump_smoke</c> (tf_player.cpp:1511).</summary>
    public const string Effect = "rocketjump_smoke";

    /// <summary>The smoke's attachments (:1512-1513).</summary>
    public const string FootLeft = "foot_L", FootRight = "foot_R";

    /// <summary>The feet's slots, after the burn's, the glow's and the drips'.</summary>
    public const int FootLeftSlot = 4, FootRightSlot = 5;

    private readonly IEngineEffects _effects;
    private readonly IEngineEntityRender _render;
    private readonly IClientStandalones _standalones;

    /// <summary>The players whose jump is smoking.</summary>
    private readonly HashSet<EntityHandle> _smoking = [];

    public TFBlastJumpSmoke(IEngineEffects effects, IEngineEntityRender render,
                            EventLink link, IClientStandalones standalones)
    {
        _effects = effects;
        _render = render;
        _standalones = standalones;
        link.OnBlastJump(OnJump);    // stock's ListenForGameEvent for the two jump events
    }

    public int HookOrder => TFClientConditionOrders.BlastJumpSmoke;

    /// <summary>The jump's event (stock's gate, :1509): the jumper's feet,
    /// resolved from the event's userid through the player resource. The
    /// cond's rise alone — an enemy-blasted lift — reaches no event, so it
    /// starts nothing here either.</summary>
    private void OnJump(BlastJumpArgs jump)
    {
        EntityHandle player = HandleFor(jump.UserId);
        if (player == EntityHandle.None || !_smoking.Add(player))
            return;

        Smoke(player, FootLeftSlot, FootLeft);
        Smoke(player, FootRightSlot, FootRight);
    }

    /// <summary>The jumper's entity: the player resource slot whose connected
    /// <c>userid</c> matches, then that slot's managed entity handle (the
    /// KillFeed's own lookup shape).</summary>
    private EntityHandle HandleFor(int userId)
    {
        if (userId <= 0)
            return EntityHandle.None;
        if (_standalones.OfClass(EntityNames.PlayerManager)
                .OfType<ClientPlayerResource>().FirstOrDefault() is not { } resource)
            return EntityHandle.None;
        var state = resource.State;
        for (int slot = 1; slot < PlayerResourceNetState.Slots; slot++)
        {
            if (!state.HasFlag(slot, PlayerResourceNetState.FlagConnected) || state.UserId[slot] != userId)
                continue;
            return _standalones.GetByIndex(slot) is { } player ? player.Handle : EntityHandle.None;
        }
        return EntityHandle.None;
    }

    /// <summary>The cond's ride is the jump's own: nothing to start on it.</summary>
    public void OnAdded(EntityHandle player, TFCond cond) { }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Blastjumping)
            _smoking.Remove(player);
    }

    private void Smoke(EntityHandle player, int slot, string foot)
    {
        if (!_effects.ParticleSystemStartSlot(player, Effect, slot))
            return;

        _effects.ParticleSystemFollowEntitySlot(player, 0, player, _render.LookupAttachment(player, foot), slot);
    }
}
