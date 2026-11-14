//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Ragdolls;

/// <summary>A frozen body's thaw, what its client half asks of <see cref="RagdollIce"/>.</summary>
public interface IIceRagdolls
{
    /// <summary>The body arrived frozen at <paramref name="now"/>, on the game's clock.</summary>
    void Begin(EntityHandle body, float now);

    /// <summary>The body is gone.</summary>
    void End(EntityHandle body);
}

/// <summary>
/// The ice statue's hold, and its end (stock's <c>m_frozenTimer</c>):
/// <c>C_TFRagdoll::FrameAdvance</c> holds a frozen body motionless until
/// 9–11 s from its arrival — <c>m_frozenTimer.Start(RandomFloat(9, 11))</c>,
/// c_tf_player.cpp:741, the hold at :1026-1030 — and then lets physics have
/// it (:1011-1025). This side has no per-entity client think, so the frame's
/// <see cref="IClientFrameThink"/> walk carries the clock, the way the ash's
/// <see cref="RagdollAsh"/> and the dissolve's <see cref="RagdollDissolves"/>
/// do; the thaw itself is <see cref="IClientRagdolls.Thaw"/>.
///
/// <para>
/// THE FREEZE STOCK RUNS BESIDE IT — <c>m_freezeTimer</c>'s tenth-to
/// three-quarter second of played death anim (:740, :1001-1005) — has no
/// shape to take here: this tree's body never animates after its collapse,
    /// so the statue stands from the arrival, frozen, and the only clock the
/// player sees is the hold.
/// </para>
/// </summary>
public sealed class RagdollIce(IClientRagdolls ragdolls, IUniformRandom random, IGameClock clock)
    : IClientFrameThink, IIceRagdolls, IOrderedHook
{
    /// <summary>Stock's hold, in seconds: <c>RandomFloat(9, 11)</c> from the
    /// arrival (c_tf_player.cpp:741).</summary>
    public const float FrozenMinSeconds = 9f;

    public const float FrozenMaxSeconds = 11f;

    private readonly List<(EntityHandle Body, float ThawAt)> _frozen = [];

    public int HookOrder => 8120;

    /// <summary>Bodies still holding frozen.</summary>
    public int Frozen => _frozen.Count;

    public void Begin(EntityHandle body, float now)
    {
        if (_frozen.FindIndex(a => a.Body == body) < 0)
            _frozen.Add((body, now + random.RandomFloat(FrozenMinSeconds, FrozenMaxSeconds)));
    }

    public void End(EntityHandle body) => _frozen.RemoveAll(a => a.Body == body);

    public void ClientThink(float frameTime)
    {
        float now = clock.Now;

        for (int i = _frozen.Count - 1; i >= 0; i--)
        {
            (EntityHandle body, float thawAt) = _frozen[i];

            if (now < thawAt)
                continue;

            _frozen.RemoveAt(i);
            ragdolls.Thaw(body);
        }
    }
}
