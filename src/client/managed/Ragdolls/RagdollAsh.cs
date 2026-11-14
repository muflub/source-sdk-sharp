//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Ragdolls;

/// <summary>A body burns to ash: what its client half asks of <see cref="RagdollAsh"/>.</summary>
public interface IRagdollAsh
{
    /// <summary>The body began to ash at <paramref name="start"/>, on the game's clock.</summary>
    void Begin(EntityHandle body, float start);

    /// <summary>The body is gone.</summary>
    void End(EntityHandle body);
}

/// <summary>
/// The ashing corpse's half-second (c_tf_player.cpp:1435-1465): stock's
/// <c>C_TFRagdoll::ClientThink</c> counts <c>m_flTimeToDissolve</c> down
/// from <see cref="FlameRules.AshDissolveSeconds"/> and then hides the body —
/// <c>AddEffects(EF_NODRAW)</code> — and stops the ash particle
/// (:1447-1448). This side has no per-entity client think, so the frame's
/// <see cref="IClientFrameThink"/> walk carries the clock, the way the
/// dissolve's <c>RagdollDissolves</c> does. The fade to nothing is the
/// same call the dissolve's last frame makes
/// (<see cref="IEngineEntityRender.SetBlending"/>, alpha nought), the one
/// proven invisible on this side; the particle leaves by its
/// <see cref="CorpseSlot"/>, so a body burning and ashing at once keeps its
/// flames.
/// </summary>
public sealed class RagdollAsh(IEngineEntityRender render, IEngineEffects effects, IGameClock clock)
    : IClientFrameThink, IRagdollAsh, IOrderedHook
{
    /// <summary>The ash particle's slot on the body, beside the burn's plain system and the crit glow's.</summary>
    public const int CorpseSlot = 7;

    private readonly List<(EntityHandle Body, float Start)> _live = [];

    public int HookOrder => 8110;

    /// <summary>Bodies still counting their half-second down.</summary>
    public int Live => _live.Count;

    public void Begin(EntityHandle body, float start)
    {
        if (_live.FindIndex(a => a.Body == body) < 0)
            _live.Add((body, start));
    }

    public void End(EntityHandle body) => _live.RemoveAll(a => a.Body == body);

    public void ClientThink(float frameTime)
    {
        float now = clock.Now;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            (EntityHandle body, float start) = _live[i];
            if (now - start < FlameRules.AshDissolveSeconds)
                continue;

            _live.RemoveAt(i);
            render.SetBlending(body, translucent: true, ignoreZ: false, alpha: 0);
            effects.ParticleSystemStopSlot(body, CorpseSlot);
        }
    }
}
