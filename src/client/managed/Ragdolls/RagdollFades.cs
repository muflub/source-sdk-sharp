//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Ragdolls;

/// <summary>A body fades away when its time is up: what its client half asks of <see cref="RagdollFades"/>.</summary>
public interface IRagdollFades
{
    /// <summary>The body's fade is armed, from now. Once per body — later calls are the same body again.</summary>
    void Begin(EntityHandle body);

    /// <summary>The body is gone.</summary>
    void End(EntityHandle body);
}

/// <summary>
/// c_tf_player.cpp:514, name and default as stock spelled them. The bound on
/// how long a body is SEEN — the server's ragdoll has no lifetime of its own
/// (see <c>SsRagdoll.OnThink</c>); this is the whole clock.
/// </summary>
[ConVar("cl_ragdoll_fade_time", "15.0f")]
public sealed class RagdollFadeTimeConVar : FloatConVar { }

/// <summary>
/// C_TFRagdoll's fade (c_tf_player.cpp:1512-1553), on the frame's
/// <see cref="IClientFrameThink"/> walk the way the dissolve's
/// <see cref="RagdollDissolves"/> and the ash's <see cref="RagdollAsh"/> run.
///
/// <para>
/// THE SHAPE IS STOCK'S, with the grace trimmed. Stock arms
/// <c>m_fDeathTime = curtime + cl_ragdoll_fade_time</c> when the ragdoll is
/// made (:863, :1056) through <c>StartFadeOut</c> (:1624-1631); past that
/// instant the body's alpha runs down at 600 a second in
/// <c>kRenderTransAlpha</c> (:1514-1521), and at zero <c>EndFadeOut</c>
/// (:1634-1660) STOPS THE THINK, clears the ragdoll and turns the render
/// mode off — a hide, never a removal; the server entity outlives its
/// picture, which is why a corpse's fade and its replacement are different
/// clocks on different sides. What is trimmed: stock re-arms the death time
/// for as long as the body is on screen (<c>IsRagdollVisible</c>,
/// :1532-1546, a third of the delay a look) and has a
/// <c>cl_ragdoll_forcefade</c> escape for screenshots; neither is carried
/// until something needs them, and a body whose fade is cut short by a
/// removal is ended through <see cref="IRagdollFades.End"/>.
/// </para>
///
/// <para>
/// THE ALPHA IS OURS, not the entity's. Stock reads
/// <c>GetRenderColor().a</c> and writes it back through
/// <c>SetRenderColorA</c>; the blend this side sets lives where
/// <see cref="IEngineEntityRender.SetBlending"/> put it, with no read back,
/// so the count is carried here from full — the render colour a body is
/// made with. A body half-faded by something else would fade from that
/// something else's value in stock and from full here; nothing on this
/// client writes a ragdoll's alpha today but the dissolve and the ash,
/// which end their own way and are gone long before the death time.
/// </para>
/// </summary>
public sealed class RagdollFades(IEngineEntityRender render, IGameClock clock, RagdollFadeTimeConVar fadeTime)
    : IClientFrameThink, IRagdollFades, IOrderedHook
{
    /// <summary>ClientThink's <c>iFadeSpeed</c> (c_tf_player.cpp:1516): alpha points a second.</summary>
    public const float AlphaPerSecond = 600f;

    private readonly List<(EntityHandle Body, float Death, float Alpha)> _live = [];

    /// <summary>After the ash's and the ice's walks (8110, 8120).</summary>
    public int HookOrder => 8130;

    /// <summary>Bodies still counting down to their fade.</summary>
    public int Live => _live.Count;

    public void Begin(EntityHandle body)
    {
        if (_live.FindIndex(f => f.Body == body) < 0)
            _live.Add((body, clock.Now + fadeTime.Value, 255f));
    }

    public void End(EntityHandle body) => _live.RemoveAll(f => f.Body == body);

    public void ClientThink(float frameTime)
    {
        float now = clock.Now;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            (EntityHandle body, float death, float alpha) = _live[i];

            // "Remove us if our death time has passed" is where the visible
            // half starts, not ends: stock's alpha run is under the same roof
            // (c_tf_player.cpp:1513-1528), reached once the death time is gone.
            if (now < death)
                continue;

            alpha = Math.Max(alpha - AlphaPerSecond * frameTime, 0f);
            _live[i] = (body, death, alpha);
            render.SetBlending(body, translucent: true, ignoreZ: false, (int)alpha);

            if (alpha <= 0f)
                _live.RemoveAt(i);
        }
    }
}
