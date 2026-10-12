//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// The client's <see cref="ViewEffects"/> and what reaches it (C7): the Shake
/// and Fade messages here in managed code, and the native callers of
/// vieweffects (the level start, the engine's View_Fade, the world's start-dark
/// fade, the view's CalcShake and the renderer's GetFadeParams) through
/// IManagedRenderClient. CalcShake's rumble angle goes to the rumble mixer, as
/// stock's UpdateScreenShakeRumble did.
/// </summary>
public sealed class ViewEffectsHost
{
    private readonly IUniformRandom _random;
    private readonly IGameClock _clock;
    private readonly Func<bool> _show;
    private readonly Func<IVtableEngineClient?> _engine;
    private readonly Action<float>? _rumble;

    public ViewEffectsHost(IUniformRandom random, IGameClock clock, ShakeShowConVar show, Func<IVtableEngineClient?> engine,
                           Action<float>? rumble)
        : this(random, clock, () => show.Value, engine, rumble)
    {
    }

    internal ViewEffectsHost(IUniformRandom random, IGameClock clock, Func<bool> show, Func<IVtableEngineClient?> engine, Action<float>? rumble)
    {
        _random = random;
        _clock = clock;
        _show = show;
        _engine = engine;
        _rumble = rumble;
    }

    public ViewEffects Effects { get; } = new();

    /// <summary>The Shake message, at the client's current time.</summary>
    public void Shake(ShakeCommand command, float amplitude, float frequency, float duration)
        => Effects.Shake(command, amplitude, frequency, duration, _clock.Now);

    /// <summary>A fade: the Fade message, the engine's View_Fade, the world's start-dark fade.</summary>
    public void Fade(int duration, int holdTime, FadeFlags flags, byte r, byte g, byte b, byte a, float curTime)
        => Effects.Fade(duration, holdTime, flags, r, g, b, a, curTime);

    /// <summary>CalcShake for this frame's view; its rumble angle to the mixer, shake_show's lines to the overlay.</summary>
    public ShakeFrame CalcShake(float curTime, float frameTime)
    {
        IVtableEngineClient? engine = _show() ? _engine() : null;
        ShakeFrame f = Effects.CalcShake(curTime, frameTime, _random,
            engine is null ? null : (index, text) => engine.ConsolePrintAt(index, 2.0f, 1.0f, 0.8f, 0.1f, true, text));
        _rumble?.Invoke(f.RumbleAngle);
        return f;
    }
}

/// <summary>view_effects.cpp's <c>shake_show</c>.</summary>
[ConVar("shake_show", "0", Help = "Displays a list of the active screen shakes.")]
public sealed class ShakeShowConVar : BoolConVar
{
}

/// <summary>view_effects.cpp's <c>shake_stop</c>.</summary>
[ConCommand("shake_stop", Help = "Stops all active screen shakes.\n", Flags = ConVarFlags.Cheat)]
public sealed class ShakeStopCommand(ViewEffectsHost host) : IConCommand
{
    public void Execute(string args) => host.Effects.ClearAllShakes();
}
