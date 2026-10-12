//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// What the three tracer effects (fx_tracer.cpp) share: where a tracer starts
/// (<c>GetTracerOrigin</c>), whether tracers draw, and the local player.
///
/// <para>
/// THE LOCAL PLAYER IS THE ENGINE'S SLOT. Stock asked
/// <c>C_BasePlayer::GetLocalPlayer()</c> and returned when it was NULL, and in
/// this mod it always is (the viewer is a <c>C_StandalonePlayer</c>), so stock's
/// <c>Tracer</c> and <c>ParticleTracer</c> callbacks never drew: the recording
/// (<c>Fixtures/effectlist-stock.tsv.gz</c>) shows <c>player=0</c> on every one.
/// That is a dead stock arm, and the rule is to port it rather than keep it
/// dead: the managed callbacks take the viewer from <c>engine-&gt;GetLocalPlayer()</c>,
/// as the client's recipient filters already do (local_recipient.h).
/// </para>
/// </summary>
public sealed class TracerSupport
{
    /// <summary><c>TRACER_FLAG_WHIZ</c>, <c>TRACER_FLAG_USEATTACHMENT</c> (shareddefs.h).</summary>
    public const int FlagWhiz = 0x0001, FlagUseAttachment = 0x0002;

    /// <summary><c>TRACER_SPEED</c>.</summary>
    public const int TracerSpeed = 5000;

    private readonly IFxRender _render;
    private readonly IEngineConsole _console;
    private readonly Func<bool> _drawTracers;

    public TracerSupport(IFxRender render, IEngineConsole console, Func<bool> drawTracers)
    {
        _render = render;
        _console = console;
        _drawTracers = drawTracers;
    }

    /// <summary><c>r_drawtracers</c>.</summary>
    public bool DrawTracers => _drawTracers();

    /// <summary>The viewer's slot, or 0 when there is none yet (stock's NULL player).</summary>
    public int LocalPlayer => Math.Max(_render.LocalPlayer(), 0);

    /// <summary>The view's right vector (<c>AngleVectors( engine-&gt;GetViewAngles() )</c>).</summary>
    public Vector ViewRight()
    {
        QAngle a = _render.ViewAngles();
        MoveVec.AngleVectors(new Vector(a.Pitch, a.Yaw, a.Roll), out _, out Vector right, out _);
        return right;
    }

    /// <summary>
    /// <c>GetTracerOrigin</c>: the dispatch's start, or the owning entity's
    /// attachment when the effect asks for it.
    /// </summary>
    public Vector Origin(in EffectData data)
    {
        Vector start = data.Start;

        if ((data.Flags & FlagUseAttachment) != 0)
        {
            int found = _render.EntityAttachment(data.EntIndex, data.AttachmentIndex, out Vector at, out string model);
            if (found < 0)
                return start;

            if (found == 0)
            {
                if (int.TryParse(_console.FindConVarValue("developer"), out int level) && level >= 1)
                    _console.Msg($"GetTracerOrigin: Couldn't find attachment {data.AttachmentIndex} on model {model}\n");
            }
            else
            {
                start = at;
            }
        }

        return start;
    }

    /// <summary>Whether the effect's owner is the local player (stock's <c>iEntIndex &amp;&amp; iEntIndex == player-&gt;entindex()</c>).</summary>
    public bool IsOwnShot(in EffectData data, int localPlayer) => data.EntIndex != 0 && data.EntIndex == localPlayer;
}

/// <summary><c>TracerCallback</c> (fx_tracer.cpp): a bullet's streak.</summary>
public sealed class TracerEffect(TracerSupport tracers, ClientSideEffectFactory effects) : IClientEffectHandler
{
    public string EffectName => "Tracer";

    public void Dispatch(in EffectData data)
    {
        int player = tracers.LocalPlayer;
        if (player <= 0)
            return;

        if (!tracers.DrawTracers)
            return;

        Vector start = tracers.Origin(data);
        float velocity = data.Scale;
        bool whiz = (data.Flags & TracerSupport.FlagWhiz) != 0;

        // The local player's own shot: a short streak off to the right of the
        // eye, the way the view model holds the gun.
        if (tracers.IsOwnShot(data, player))
        {
            Vector right = tracers.ViewRight();
            Vector foo = FxMath.MA(data.Start, 4, right);
            foo.Z -= 0.5f;
            effects.PlayerTracer(foo, data.Origin);
            return;
        }

        if (velocity == 0)
            velocity = TracerSupport.TracerSpeed;

        effects.Tracer(start, data.Origin, (int)velocity, whiz);
    }
}

/// <summary><c>ParticleTracerCallback</c> (fx_tracer.cpp): a tracer drawn by a particle system.</summary>
public sealed class ParticleTracerEffect(TracerSupport tracers, ClientSideEffectFactory effects, IFxRender render) : IClientEffectHandler
{
    public string EffectName => "ParticleTracer";

    public void Dispatch(in EffectData data)
    {
        int player = tracers.LocalPlayer;
        if (player <= 0)
            return;

        if (!tracers.DrawTracers)
            return;

        Vector start = tracers.Origin(data);
        Vector end = data.Origin;

        if (tracers.IsOwnShot(data, player))
        {
            Vector right = tracers.ViewRight();
            start = FxMath.MA(data.Start, 4, right);
            start.Z -= 0.5f;
        }

        Vector toEnd = FxMath.Sub(end, start);
        FxMath.Normalize(ref toEnd);
        Vector a = Angles.FromVector(toEnd);
        render.DispatchParticleEffect(data.HitBox, start, end, new QAngle(a.X, a.Y, a.Z), data.EntIndex);

        if ((data.Flags & TracerSupport.FlagWhiz) != 0)
            effects.TracerSound(start, end, ClientSideEffectFactory.TracerTypeDefault);
    }
}

/// <summary><c>TracerSoundCallback</c> (fx_tracer.cpp): the near-miss alone, its type in the flags.</summary>
public sealed class TracerSoundEffect(TracerSupport tracers, ClientSideEffectFactory effects) : IClientEffectHandler
{
    public string EffectName => "TracerSound";

    public void Dispatch(in EffectData data)
        => effects.TracerSound(tracers.Origin(data), data.Origin, data.Flags);
}
