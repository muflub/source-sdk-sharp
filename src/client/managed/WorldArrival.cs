//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>What the native world does with a world update it has handed over.</summary>
[Flags]
public enum WorldArrivalActions
{
    None = 0,

    /// <summary><c>modemanager-&gt;SwitchMode( false, true )</c>: the normal client mode, forced.</summary>
    NormalMode = 1,

    /// <summary><c>engine-&gt;SetOcclusionParameters</c> with the plan's first two floats (max occludee, min occluder area).</summary>
    Occlusion = 2,

    /// <summary><c>modelinfo-&gt;SetLevelScreenFadeRange</c> with the plan's last two (min, max prop screen width).</summary>
    ScreenFadeRange = 4,
}

/// <summary>
/// What worldspawn's arrival does on the client: stock's <c>C_World::OnDataChanged</c>
/// (c_world.cpp), decided here and carried out by the native world.
///
/// <para>
/// ON THE CREATE, AND ONLY THEN: the client mode is reset to normal ("Always
/// force reset to normal mode upon receipt of world in new map"); a map that
/// starts dark fades in from black over five seconds after a one-second hold;
/// and the renderer is handed the level's two occlusion areas and its prop
/// screen-width fade range. A later update of the world changes none of them,
/// as stock's did not.
/// </para>
/// </summary>
public sealed class WorldArrival
{
    /// <summary>How many floats the plan carries.</summary>
    public const int PlanFloats = 4;

    /// <summary>The start-dark fade: stock's 5 s fade after a 1 s hold, in SCREENFADE_FRACBITS fixed point.</summary>
    public const int StartDarkDuration = 5 << Render.ViewEffects.FadeFracBits;

    /// <inheritdoc cref="StartDarkDuration"/>
    public const int StartDarkHold = 1 << Render.ViewEffects.FadeFracBits;

    /// <summary>Fade in from the colour, replacing every fade already running.</summary>
    public const FadeFlags StartDarkFlags = FadeFlags.In | FadeFlags.Purge;

    private readonly WorldNetState _state;
    private readonly Action<int, int, FadeFlags> _fadeFromBlack;

    /// <param name="state">A locally bound world state the update is decoded into.</param>
    /// <param name="fadeFromBlack">
    /// A fade from opaque black (duration, hold, flags): the view effects'
    /// <c>Fade</c>, where the engine's own View_Fade goes.
    /// </param>
    public WorldArrival(WorldNetState state, Action<int, int, FadeFlags> fadeFromBlack)
    {
        _state = state;
        _fadeFromBlack = fadeFromBlack;
    }

    /// <summary>A world state of its own, bound locally, for <see cref="WorldArrival(WorldNetState, Action{int, int, FadeFlags})"/>.</summary>
    public static WorldArrival Over(Action<int, int, FadeFlags> fadeFromBlack)
    {
        var state = new WorldNetState();
        state.BindLocal();
        return new WorldArrival(state, fadeFromBlack);
    }

    /// <summary>
    /// The update the engine has just decoded into the world's blob: what the
    /// native world must do, with the values in <paramref name="plan"/>
    /// (<see cref="PlanFloats"/> long, or the renderer's two are not asked for).
    /// </summary>
    public WorldArrivalActions OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType, Span<float> plan)
    {
        if (updateType != DataUpdateType.Created)
            return WorldArrivalActions.None;

        _state.Decode(blob);

        if (_state.StartDark)
            _fadeFromBlack(StartDarkDuration, StartDarkHold, StartDarkFlags);

        if (plan.Length < PlanFloats)
            return WorldArrivalActions.NormalMode;

        plan[0] = _state.MaxOccludeeArea;
        plan[1] = _state.MinOccluderArea;
        plan[2] = _state.MinPropScreenSpaceWidth;
        plan[3] = _state.MaxPropScreenSpaceWidth;

        return WorldArrivalActions.NormalMode | WorldArrivalActions.Occlusion | WorldArrivalActions.ScreenFadeRange;
    }
}
