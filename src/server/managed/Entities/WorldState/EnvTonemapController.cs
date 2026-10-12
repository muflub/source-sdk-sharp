//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_tonemap_controller: how bright the world is allowed to look.
///
/// <para>
/// Two halves that reach the renderer by different roads, which is the only
/// interesting thing about it. The exposure clamps and the bloom scale are
/// NETWORKED — the client's post-process reads them from globals a client
/// entity fills in. The tonemap scale and rate are CONVARS, written directly,
/// and shared between the two modules because a convar registered twice under
/// one name is one convar with two references to it.
/// </para>
///
/// <para>
/// Nothing here decides anything on its own. What it is for is that a map can
/// say "this room is dark and your eyes do not adjust", and that is nine inputs
/// and no logic — which is exactly the shape that belongs in C#, because the
/// nine inputs are the whole entity.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvTonemapController, ScriptedEntityKind.Effect)]
public sealed class EnvTonemapController : ScriptedEntity
{
    public EnvTonemapController(IGameClock clock, IEngineConsole console, IEngineEntityRender render)
    {
        Clock = clock;
        Console = console;
        Render = render;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    [NetState] public TonemapControllerNetState State = new();

    /// <summary>
    /// The convar the whole scene's exposure is multiplied by. Declared by
    /// both modules, which in Source means one value with a reference on each
    /// side — so writing it here reaches the renderer on a listen server.
    /// </summary>
    private const string TonemapScaleConVar = "mat_hdr_tonemapscale";

    /// <summary>How fast the eye adjusts, when it is adjusting manually.</summary>
    private const string TonemapRateConVar = "mat_hdr_manual_tonemap_rate";

    /// <summary>A blend in progress: where it started, where it ends, and when.</summary>
    private float _blendStart;
    private float _blendEnd;
    private float _blendStartTime;
    private float _blendEndTime;
    private bool _blending;

    /// <summary>
    /// How often a blend steps. Stock's tenth of a second, kept — the value
    /// being blended is read once per frame by the renderer, so stepping it
    /// faster buys nothing a player could see.
    /// </summary>
    private const float BlendInterval = 0.1f;

    public override void OnSpawn() => Render.SetTransmitAlways(Handle, true);

    // --- the networked half: what the client's post-process is allowed to do ---

    [ScriptedEvent("SetAutoExposureMin")]
    private void OnSetAutoExposureMin(float value)
    {
        State.CustomAutoExposureMin    = value;
        State.UseCustomAutoExposureMin = true;
    }

    [ScriptedEvent("SetAutoExposureMax")]
    private void OnSetAutoExposureMax(float value)
    {
        State.CustomAutoExposureMax    = value;
        State.UseCustomAutoExposureMax = true;
    }

    /// <summary>
    /// Drops both clamps at once, which is why the switches are networked
    /// separately from the numbers: zero is a legitimate exposure floor, so
    /// "unset" cannot be spelt as a value.
    /// </summary>
    [ScriptedEvent("UseDefaultAutoExposure")]
    private void OnUseDefaultAutoExposure()
    {
        State.UseCustomAutoExposureMin = false;
        State.UseCustomAutoExposureMax = false;
    }

    [ScriptedEvent("SetBloomScale")]
    private void OnSetBloomScale(float value)
    {
        State.CustomBloomScale        = value;
        State.CustomBloomScaleMinimum = value;
        State.UseCustomBloomScale     = true;
    }

    [ScriptedEvent("UseDefaultBloomScale")]
    private void OnUseDefaultBloomScale() => State.UseCustomBloomScale = false;

    /// <summary>
    /// A bloom ceiling and a bloom floor together.
    ///
    /// <para>
    /// This input has never worked in stock. Its handler calls
    /// <c>sscanf("%f %f", inputdata.value.String(), bloom_max, bloom_min)</c> —
    /// the format string and the input are the wrong way round, and the two
    /// destinations are floats rather than pointers to them. It parses the
    /// literal "%f %f", matches nothing, warns and returns. Recorded in
    /// docs/divergences.md as something stock gets wrong that this does not.
    /// </para>
    ///
    /// <para>
    /// Note that stock's UNREACHED code also never sets the switch, so a map
    /// using only this input would have had no effect even with the parse
    /// fixed. It is set here, because a bloom range a map asked for that does
    /// not apply is the same bug one layer along.
    /// </para>
    /// </summary>
    [ScriptedEvent("SetBloomScaleRange")]
    private void OnSetBloomScaleRange(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            Log.Warning(
                $"[SourceSharp] env_tonemap_controller '{Name}': SetBloomScaleRange wants " +
                $"<max bloom> <min bloom>, got '{value}'\n");
            return;
        }

        State.CustomBloomScale        = ParseFloat(parts[0], 1f);
        State.CustomBloomScaleMinimum = ParseFloat(parts[1], 1f);
        State.UseCustomBloomScale     = true;
    }

    // --- the convar half: the scale itself ---

    [ScriptedEvent("SetTonemapScale")]
    private void OnSetTonemapScale(float value)
    {
        _blending = false;
        StopThinking();
        SetTonemapScale(value);
    }

    [ScriptedEvent("SetTonemapRate")]
    private void OnSetTonemapRate(float value)
        => Console.SetConVarValueByName(TonemapRateConVar,
                                      value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Walks the tonemap scale to a target over a time, both given in one
    /// string as "&lt;target&gt; &lt;seconds&gt;".
    ///
    /// <para>
    /// The start of the blend is read from the convar rather than remembered,
    /// which matters when a second blend interrupts a first: the value in force
    /// is where the new one should start from, and a remembered target is not
    /// where anything currently is.
    /// </para>
    /// </summary>
    [ScriptedEvent("BlendTonemapScale")]
    private void OnBlendTonemapScale(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            Log.Warning(
                $"[SourceSharp] env_tonemap_controller '{Name}': BlendTonemapScale wants " +
                $"<target tonemap scale> <blend time>, got '{value}'\n");
            return;
        }

        _blendEnd       = ParseFloat(parts[0], 1f);
        _blendStartTime = Clock.Now;
        _blendEndTime   = _blendStartTime + ParseFloat(parts[1], 0f);
        _blendStart     = CurrentTonemapScale();
        _blending       = true;

        SetNextThink(BlendInterval);
    }

    public override void OnThink()
    {
        if (!_blending)
            return;

        float now = Clock.Now;
        float span = _blendEndTime - _blendStartTime;

        // A blend of no duration is an immediate set, not a division by zero.
        float fraction = span <= 0f ? 1f : Math.Clamp((now - _blendStartTime) / span, 0f, 1f);

        SetTonemapScale(_blendStart + ((_blendEnd - _blendStart) * fraction));

        if (now >= _blendEndTime)
        {
            _blending = false;
            return;
        }

        SetNextThink(BlendInterval);
    }

    private void SetTonemapScale(float value)
        => Console.SetConVarValueByName(TonemapScaleConVar,
                                      value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private float CurrentTonemapScale()
        => ParseFloat(Console.FindConVarValue(TonemapScaleConVar) ?? "1", 1f);
}
