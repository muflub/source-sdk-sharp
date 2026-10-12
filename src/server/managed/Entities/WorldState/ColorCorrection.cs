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
/// color_correction: a lookup table applied to the whole screen, strongest when
/// the player is standing next to it.
///
/// <para>
/// Unlike the three world-state controllers, this one has real server-side
/// behaviour to move: a fade. <c>Enable</c> and <c>Disable</c> do not switch it,
/// they start a ramp toward the ceiling or toward nothing, and either can
/// interrupt the other part way — which is why the fade has to remember where
/// it started rather than where it should have been.
/// </para>
///
/// <para>
/// The other half of the weight belongs to the client and stays there: how far
/// the player is from this entity, between <c>minfalloff</c> and
/// <c>maxfalloff</c>. That is a distance measured against a position that moves
/// every frame, so asking the server for it would be asking the wrong machine.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.ColorCorrection, ScriptedEntityKind.Effect)]
public sealed class ColorCorrection : ScriptedEntity
{
    public ColorCorrection(IGameClock clock, IEngineEntityRender render)
    {
        Clock = clock;
        Render = render;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    [NetState] public ColorCorrectionNetState State = new();

    /// <summary>What a map may set on this entity. See <see cref="ColorCorrectionVars"/>.</summary>
    [ScriptVars] public ColorCorrectionVars Vars = new();

    /// <summary>
    /// Stock's think rate for the fade — one tick. Kept, because the value is
    /// networked and a coarser step would be visible as a staircase in a slow
    /// fade rather than as a smooth one.
    /// </summary>
    private const float FadeInterval = 0.015f;

    /// <summary>Which way the fade is going, or neither.</summary>
    private enum Fade { None, In, Out }

    private Fade _fading = Fade.None;
    private float _fadeStartTime;

    /// <summary>
    /// The weight the current fade began from.
    ///
    /// <para>
    /// Remembered rather than recomputed, and it is what makes an interrupted
    /// fade behave: a correction disabled half way through fading in should
    /// take half the fade-out time, not all of it, because it is only half on.
    /// Stock scales the duration by exactly this ratio and so does this.
    /// </para>
    /// </summary>
    private float _fadeStartWeight;

    public override void OnSpawn()
    {
        // The whole map's screen is tinted by this, so every client needs it
        // wherever they are — the falloff is what decides how much, and that is
        // computed on the client from a position it already has.
        Render.SetTransmitAlways(Handle, true);

        State.LookupFile    = Vars.LookupFile;
        State.MinFalloff    = Vars.MinFalloff;
        State.MaxFalloff    = Vars.MaxFalloff;
        State.Enabled       = !Vars.StartDisabled;

        // Stock sets a flat 1.0 here and ignores maxweight, which contradicts
        // every other path in the entity — both fades clamp to maxweight, and
        // so does Enable when there is no fade. A correction keyed
        // "maxweight 0.5" was therefore fully applied until something faded it.
        // Recorded in docs/divergences.md.
        State.CurrentWeight = Vars.StartDisabled ? 0f : Vars.MaxWeight;
    }

    [ScriptedEvent("Enable")]
    private void OnEnable()
    {
        State.Enabled = true;

        if (Vars.FadeInDuration > 0f)
            StartFade(Fade.In);
        else
            Settle(Vars.MaxWeight);
    }

    [ScriptedEvent("Disable")]
    private void OnDisable()
    {
        State.Enabled = false;

        if (Vars.FadeOutDuration > 0f)
            StartFade(Fade.Out);
        else
            Settle(0f);
    }

    [ScriptedEvent("SetFadeInDuration")]
    private void OnSetFadeInDuration(float value) => Vars.FadeInDuration = value;

    [ScriptedEvent("SetFadeOutDuration")]
    private void OnSetFadeOutDuration(float value) => Vars.FadeOutDuration = value;

    private void StartFade(Fade direction)
    {
        _fading          = direction;
        _fadeStartTime   = Clock.Now;
        _fadeStartWeight = State.CurrentWeight;

        SetNextThink(FadeInterval);
    }

    private void Settle(float weight)
    {
        _fading = Fade.None;
        State.CurrentWeight = weight;
        StopThinking();
    }

    /// <summary>
    /// One step of whichever fade is running.
    ///
    /// <para>
    /// Stock spells this as two context thinks that check each other's
    /// conditions — <c>FadeInThink</c> bails if <c>!m_bEnabled</c> and
    /// <c>FadeOutThink</c> bails if <c>m_bEnabled</c>, which is a mutex written
    /// as two halves. One think with a direction is the same rule with nothing
    /// left to keep in step.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        if (_fading == Fade.None || Vars.MaxWeight == 0f)
        {
            Settle(State.CurrentWeight);
            return;
        }

        bool fadingIn = _fading == Fade.In;
        float target  = fadingIn ? Vars.MaxWeight : 0f;

        // How long this fade should take, scaled by how much of the journey is
        // actually left. Interrupting a fade half way is the case this exists
        // for; without it a correction toggled twice in quick succession moves
        // at a different speed each time.
        float ratio    = Math.Clamp(_fadeStartWeight / Vars.MaxWeight, 0f, 1f);
        float duration = fadingIn
            ? Vars.FadeInDuration * (1f - Math.Min(ratio, 0.99f))
            : Vars.FadeOutDuration * Math.Max(ratio, 0.01f);

        if (duration <= 0f)
        {
            Settle(target);
            return;
        }

        float progress = Math.Clamp((Clock.Now - _fadeStartTime) / duration, 0f, 1f);

        State.CurrentWeight = _fadeStartWeight + ((target - _fadeStartWeight) * progress);

        if (progress >= 1f)
        {
            Settle(target);
            return;
        }

        SetNextThink(FadeInterval);
    }
}

/// <summary>
/// The map keys <see cref="ColorCorrection"/> declares.
/// </summary>
[ScriptVars]
public partial class ColorCorrectionVars
{
    [VarName("filename")]
    public string LookupFile { get; set; } = string.Empty;

    public float MinFalloff { get; set; }

    public float MaxFalloff { get; set; } = 1000f;

    public float MaxWeight { get; set; } = 1f;

    public float FadeInDuration { get; set; }

    public float FadeOutDuration { get; set; }

    public bool StartDisabled { get; set; }
}
