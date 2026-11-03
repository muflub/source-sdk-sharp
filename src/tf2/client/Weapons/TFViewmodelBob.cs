//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace TF2.Client.Weapons;

/// <summary>
/// The viewmodel bob, stock's <c>CalcViewModelBobHelper</c> and
/// <c>AddViewModelBobHelper</c> (tf_weaponbase.cpp:4713, :4801) split the way
/// our pin runs: <see cref="Tick"/> advances the bob state one frame from the
/// local player's horizontal speed and writes the vertical and lateral
/// magnitudes, and the native pin applies them to the viewmodel's origin and
/// angles with stock's factors (shell_client.cpp's ApplyManagedViewModelBob).
///
/// <para>
/// Stock keeps the state per weapon's <c>BobState_t</c>; the pin is applied to
/// the one viewmodel that owns the view, so one shared state carries the same
/// numbers where they are visible. Stock's no-player arm keeps the last pair
/// ("just use old value") and is kept, though the native pin is only reached
/// with a player.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(IViewModelBob)])]
public sealed class TFViewmodelBob(ClBobCycleConVar cycle, ClBobUpConVar bobUp, IClientStandalones standalones, IEngineHud screen)
    : IViewModelBob
{
    private State _state;

    public void Tick(float curTime, float frameTime, Span<float> bob)
    {
        ClientSsPlayer? player = LocalPlayer();
        float speed = 0f;
        if (player is not null)
        {
            Vector v = player.State.Velocity;
            speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);   // GetLocalVelocity().Length2D()
        }

        _state = Advance(_state, curTime, frameTime, player is not null, speed, bobUp.Value, cycle.Value);
        bob[0] = _state.Vertical;
        bob[1] = _state.Lateral;
    }

    private ClientSsPlayer? LocalPlayer()
    {
        int local = screen.LocalPlayerIndex;
        return local > 0 ? standalones.GetByIndex(local) as ClientSsPlayer : null;
    }

    /// <summary>The bob's carried state, what stock's <c>BobState_t</c> keeps of this.</summary>
    public readonly record struct State(float BobTime, float LastBobTime, float LastSpeed, float Vertical, float Lateral);

    /// <summary>
    /// One <c>CalcViewModelBobHelper</c>: the divide-by-zero guards, the speed
    /// clamps, the phase advance and the two waves, exactly as stock computes
    /// them. The frame-time-zero and no-player arms keep the magnitudes and
    /// move no time.
    /// </summary>
    internal static State Advance(State state, float curTime, float frameTime, bool havePlayer, float speed, float bobUp, float bobCycle)
    {
        // Don't allow zeros, because we divide by them.
        if (bobUp <= 0)
            bobUp = 0.01f;
        if (bobCycle <= 0)
            bobCycle = 0.01f;

        // NOTENOTE: For now, let this cycle continue when in the air, because
        // it snaps badly without it.
        if (frameTime == 0 || !havePlayer)
            return state;

        // Find the speed of the player; don't allow too big speed changes.
        float maxSpeedDelta = MathF.Max(0, (curTime - state.LastBobTime) * 320.0f);
        speed = Math.Clamp(speed, state.LastSpeed - maxSpeedDelta, state.LastSpeed + maxSpeedDelta);
        speed = Math.Clamp(speed, -320.0f, 320.0f);

        float bobOffset = speed / 320.0f;   // RemapVal(speed, 0, 320, 0.0f, 1.0f)
        float bobTime = state.BobTime + (curTime - state.LastBobTime) * bobOffset;

        // Calculate the vertical bob, then the lateral one at twice the period.
        float vertical = speed * 0.005f;
        vertical = vertical * 0.3f + vertical * 0.7f * MathF.Sin(Phase(bobTime, bobCycle, bobUp));
        vertical = Math.Clamp(vertical, -7.0f, 4.0f);

        float lateral = speed * 0.005f;
        lateral = lateral * 0.3f + lateral * 0.7f * MathF.Sin(Phase(bobTime, bobCycle * 2.0f, bobUp));
        lateral = Math.Clamp(lateral, -7.0f, 4.0f);

        return new State(bobTime, curTime, speed, vertical, lateral);
    }

    /// <summary>Stock's bob phase: the cycle folded to one revolution, hinged at bobUp.</summary>
    private static float Phase(float time, float cycle, float bobUp)
    {
        float t = time - (int)(time / cycle) * cycle;
        t /= cycle;
        return t < bobUp ? MathF.PI * t / bobUp : MathF.PI + MathF.PI * (t - bobUp) / (1.0f - bobUp);
    }
}
