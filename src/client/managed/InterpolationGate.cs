//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// Whether entities interpolate this frame: the frame-level policy at the top
/// of stock's <c>C_BaseEntity::InterpolateServerEntities</c>
/// (c_baseentity.cpp), which set <c>s_bInterpolate</c> once per frame for
/// <c>BaseInterpolatePart1</c> to read.
///
/// <para>
/// Interpolation is off when the player has turned <c>cl_interpolate</c> off,
/// while a timedemo plays back, while the engine is paused, and -- the one that
/// matters in play -- when the net channel has heard nothing for half a second
/// outside demo playback, which is what stops a stalled client sliding every
/// entity on a guess. A closed gate draws every entity at the last value the
/// server sent (stock's <c>MoveToLastReceivedPosition</c>):
/// <see cref="Interpolation.RenderPass"/>'s <c>interpolate: false</c>.
/// </para>
///
/// <para>
/// It was native (<c>EntityInterp_UpdateFrameGate</c>, shell_entitystatics.cpp)
/// and wrote a flag nothing read after the C_BaseEntity hierarchy left, so the
/// policy was computed and then lost; it is managed's now, and the render pass
/// honours it.
/// </para>
/// </summary>
public sealed class InterpolationGate
{
    /// <summary>The ConVar, declared natively because the server reads it back off the wire (FCVAR_USERINFO).</summary>
    public const string ConVarName = "cl_interpolate";

    /// <summary>How long the net channel may be silent before interpolation stops: stock's 0.5 s.</summary>
    public const float StallSeconds = 0.5f;

    private readonly IPlaybackState _playback;
    private readonly IClientNetChannel _channel;
    private readonly Func<string, string?> _readConVar;

    /// <param name="playback">The engine's pause and demo state.</param>
    /// <param name="channel">The engine's net channel, for the time since a packet last arrived.</param>
    /// <param name="readConVar">A ConVar's current value by name (<c>IEngineConsole.FindConVarValue</c>).</param>
    public InterpolationGate(IPlaybackState playback, IClientNetChannel channel, Func<string, string?> readConVar)
    {
        _playback = playback;
        _channel = channel;
        _readConVar = readConVar;
    }

    /// <summary>
    /// The rule, over its five inputs. <paramref name="sinceLastReceived"/> is
    /// null when there is no net channel, which stock's <c>nci &amp;&amp;</c>
    /// skips; it is not read during demo playback.
    /// </summary>
    public static bool Decide(bool clInterpolate, bool playingTimeDemo, bool paused,
                              bool playingDemo, float? sinceLastReceived)
    {
        if (!clInterpolate)
            return false;

        // Don't interpolate during timedemo playback
        if (playingTimeDemo || paused)
            return false;

        // Don't interpolate, either, if we are timing out
        if (!playingDemo && sinceLastReceived is float since && since > StallSeconds)
            return false;

        return true;
    }

    /// <summary>
    /// <c>cl_interpolate</c>'s <c>GetBool()</c>: its integer value non-zero
    /// (<c>ConVar::GetInt</c> truncates the float, so "0.5" is off). A value
    /// the engine does not answer is the declaration's default, "1.0f".
    /// </summary>
    public static bool ConVarOn(string? value)
        => value is null || (int)VariantParse.Atof(value) != 0;

    /// <summary>This frame's answer, read in stock's order.</summary>
    public bool Open()
    {
        bool clInterpolate = ConVarOn(_readConVar(ConVarName));
        bool timeDemo = _playback.IsPlayingTimeDemo;
        bool paused = _playback.IsPaused;
        bool demo = _playback.IsPlayingDemo;
        float? since = demo ? null : _channel.Channel()?.SinceLastReceived;

        return Decide(clInterpolate, timeDemo, paused, demo, since);
    }
}
