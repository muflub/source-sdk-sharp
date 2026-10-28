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
/// env_sprite, env_sprite_oriented and env_glow: a flat picture hung in the
/// world, facing the viewer.
///
/// <para>
/// ONE class under three names, which is what stock is —
/// <c>LINK_ENTITY_TO_CLASS( env_sprite, CSprite )</c>,
/// <c>LINK_ENTITY_TO_CLASS( env_glow, CSprite )</c> and a
/// <c>CSpriteOriented</c> whose entire body is a <c>Spawn</c> that turns the
/// angles round. They move together or not at all: splitting them would give a
/// build where interchangeable names behave differently depending on which one
/// a mapper typed.
/// </para>
///
/// <para>
/// It is the most placed entity in this batch by a distance — dm_lockdown has
/// twenty-three of them, every corridor light and every door indicator — and
/// zero of the other two names.
/// </para>
///
/// <para>
/// WHAT UNBLOCKED IT, because the refusal stood four times. The recorded reason
/// was that five registered classes call <c>CSprite::SpriteCreate</c>, which
/// builds one by CLASSNAME and would get a managed shell back. That count came
/// from a grep over <c>src/game</c>, and <b>src/game is not the build</b>:
/// four of the five are under <c>hl2/</c> and <c>hl2mp/</c>, and
/// <c>sdk_csharp_release.ninja</c> hands the compiler exactly one file out of
/// <c>hl2/</c> and none out of <c>hl2mp/</c>. Counted from the binary instead —
/// <c>objdump -d server.so</c> — there is ONE call site, in
/// <c>CGibShooter::InitPointGib</c>. That one is still reachable (see
/// report.md; <c>env_rotorshooter</c> is live, contrary to the note), so
/// <c>SpriteCreate</c> now builds its <c>CSprite</c> directly rather than
/// through the factory. Four lines of <c>#ifdef</c> in <c>shared/Sprite.cpp</c>,
/// and the classname is free.
/// </para>
///
/// <para>
/// The client half is <see cref="Client.Standalone.ClientSprite"/> and
/// <c>shell_sprite_render.cpp</c>. It is needed for the reason the whole
/// particle family turns on: a managed entity is never on the wire as
/// <c>CSprite</c>, so <c>DT_Sprite</c> stops being sent and the stock client
/// class is never built. Moving the name alone would deliver the entity
/// perfectly and draw nothing.
/// </para>
/// </summary>
public abstract class SpriteBase : ScriptedEntity
{
    public SpriteBase(IEnginePrecache precache, IEngineEntityRender render, IGameClock clock)
    {
        _precache = precache;
        Render = render;
        _clock = clock;
    }

    /// <summary>loading the sprite, and finding out how many frames it has.</summary>
    private readonly IEnginePrecache _precache;

    /// <summary>what this entity looks like, and whether it is sent to everyone.</summary>
    protected readonly IEngineEntityRender Render;

    /// <summary>what the server thinks the time is, which is what a frame is advanced against.</summary>
    private readonly IGameClock _clock;

    /// <summary>What a map may set on this entity. See <see cref="EnvSpriteVars"/>.</summary>
    [ScriptVars] public EnvSpriteVars Vars = new();

    /// <summary>
    /// Whichever of the derived classes' tables this instance actually writes.
    ///
    /// A table is keyed by CLASSNAME, so a class serving several names declares
    /// one per name and picks here; and a subclass — <see cref="EnvSpriteTrail"/>
    /// — has a wider table of its own. Everything below writes through the
    /// interface and does not care which.
    /// </summary>
    protected abstract ISpriteNetState State { get; }

    /// <summary>env_sprite's own spawnflags.</summary>
    [Flags]
    protected internal enum SpawnFlag
    {
        None = 0,

        /// <summary>Drawn from the start even though the mapper named it.</summary>
        StartOn = 1 << 0,

        /// <summary>Animate once and switch off, rather than looping.</summary>
        PlayOnce = 1 << 1,
    }

    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>Stock's MAX_SPRITE_SCALE, and the ceiling its own send prop rounds to.</summary>
    protected internal const float MaxScale = 64.0f;

    /// <summary>
    /// The last frame index, which is the frame COUNT minus one.
    ///
    /// Zero for a still sprite, and that is what stops it animating: stock's
    /// <c>TurnOn</c> only starts the think when this is above 1. Every sprite
    /// dm_lockdown places is a still one, framerate 10 and all.
    ///
    /// Read off the precached model ONCE, in <see cref="OnSpawn"/>, which a hot
    /// reload does not run again -- so it is <c>[SaveField]</c>, one mark here
    /// covering the sprite and the trail alike through this base
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _maxFrame;

    /// <summary>When the frame was last advanced, so a variable tick advances by the right amount.</summary>
    private float _lastAnimateAt;

    /// <summary>
    /// Which material to draw. The "model" key for a plain sprite; a trail
    /// prefers its own "spritename" and falls back to this, which is stock's
    /// <c>CSpriteTrail</c> spawn written the other way round.
    /// </summary>
    protected virtual string MaterialName => Vars.Model;

    /// <summary>
    /// Whether this classname turns its angles round on spawn. Only
    /// env_sprite_oriented does.
    /// </summary>
    protected virtual bool IsOriented => ClassName == EntityNames.EnvSpriteOriented;

    public override void OnSpawn()
    {
        int index = _precache.PrecacheModel(MaterialName);
        State.ModelIndex = index;

        // The last frame index, not the count. -1 from a model that would not
        // load leaves this at -1, which reads as "no frames" and keeps the
        // animation off — the honest answer for a sprite that is not there.
        _maxFrame = _precache.ModelFrameCount(index) - 1f;

        // Stock's clamp, and stock's complaint with it. A scale outside the
        // range is a level-design mistake rather than a crash, so it is said and
        // corrected rather than refused.
        float scale = Vars.Scale;

        if (scale < 0f || scale > MaxScale)
        {
            Log.Warning($"[SourceSharp/C#] LEVEL DESIGN ERROR: {ClassName} '{Name}' has scale {scale}, outside [0..{MaxScale}]\n");
            scale = Math.Clamp(scale, 0f, MaxScale);
        }

        State.Scale = scale;
        State.Frame = 0f;
        State.RenderMode = Vars.RenderMode;
        State.RenderFx = Vars.RenderFx;
        State.GlowProxySize = Vars.GlowProxySize;
        State.HdrColorScale = Vars.HDRColorScale;

        (State.Red, State.Green, State.Blue) = ColourFrom(Vars.RenderColor);

        // Stock's Spawn does SetBrightness( m_clrRender->a ), which is renderamt.
        // Both go on the wire: one becomes the quad's vertex alpha and one
        // becomes the fx blend the renderer multiplies over it.
        State.Brightness = Math.Clamp(Vars.RenderAmt, 0, 255);

        // Stock's angle fixup, and it is a Worldcraft compatibility rule rather
        // than anything to do with sprites: the editor only ever set yaw, and a
        // sprite's own orientation reads ROLL, so a yaw with no roll is moved
        // across.
        Vector angles = Entities.GetAngles(Handle);
        Entities.SetAngles(Handle, FixUpAngles(angles, IsOriented));

        // Stock's UpdateTransmitState: FL_EDICT_ALWAYS for a sprite with no
        // move parent, which is every sprite in this build.
        //
        // NOT a nicety. Sprites are strewn across a level and rarely change, so
        // stock sends each one once and leaves it on the client rather than
        // bursting them in and out at every PVS boundary — its own comment says
        // exactly that. PVS-checking twenty-three of them would make every
        // corridor light pop as the player walks.
        Render.SetTransmitAlways(Handle, true);

        State.Visible = StartsOn(Flags, named: Name.Length > 0);

        if (State.Visible)
            TurnOn();

        OnSpriteSpawned();
    }

    /// <summary>
    /// Anything a subclass has to say after the sprite half of the spawn.
    /// <see cref="EnvSpriteTrail"/> writes its six trail values here.
    /// </summary>
    protected virtual void OnSpriteSpawned() { }

    /// <summary>
    /// Stock's Worldcraft fixup, and the oriented variant's half-turn.
    ///
    /// <para>
    /// Two rules that stock applies in two places and that a reader should see
    /// together. <c>CSprite::Spawn</c> moves a bare yaw across to roll, because
    /// the editor only ever wrote yaw and a sprite reads roll.
    /// <c>CSpriteOriented::Spawn</c> then adds 180 to the yaw, because an
    /// oriented sprite's "forward" points at the viewer rather than out of the
    /// sprite — stock's own parenthesised "(gah)".
    /// </para>
    ///
    /// <para>
    /// Static and taking both facts so it can be checked without an engine.
    /// </para>
    /// </summary>
    protected internal static Vector FixUpAngles(Vector angles, bool oriented)
    {
        // Saved BEFORE the swap, which is what stock does: CSpriteOriented
        // keeps a copy of the angles, calls down, and then works from its copy.
        float originalYaw = angles.Y;

        if (angles.Y != 0f && angles.Z == 0f)
            angles = new Vector(angles.X, 0f, angles.Y);

        if (oriented)
            angles = new Vector(angles.X, AngleMod(originalYaw + 180f), angles.Z);

        return angles;
    }

    /// <summary>Stock's anglemod: wrapped into [0, 360).</summary>
    protected internal static float AngleMod(float degrees)
    {
        float wrapped = degrees % 360f;
        return wrapped < 0f ? wrapped + 360f : wrapped;
    }

    /// <summary>
    /// Whether a sprite is drawn from the moment it spawns.
    ///
    /// <para>
    /// Stock's own condition, and the second half is the surprising part: an
    /// UNNAMED sprite is on whatever its flags say, because nothing could ever
    /// switch it on. Only a named one waits. The same rule env_embers has.
    /// </para>
    /// </summary>
    protected internal static bool StartsOn(SpawnFlag flags, bool named)
        => !named || flags.HasFlag(SpawnFlag.StartOn);

    /// <summary>
    /// Whether this sprite animates at all, which stock decides in TurnOn.
    ///
    /// <para>
    /// It needs BOTH a framerate and more than one frame — a still sprite with
    /// "framerate" "10" does nothing, which is every sprite dm_lockdown places.
    /// The one-shot flag overrides that, because a one-shot sprite has to reach
    /// its last frame to switch itself off.
    /// </para>
    /// </summary>
    protected internal static bool Animates(float framerate, float maxFrame, SpawnFlag flags)
        => (framerate != 0f && maxFrame > 1f) || flags.HasFlag(SpawnFlag.PlayOnce);

    /// <summary>CSprite::TurnOn: shown, and animating if it animates.</summary>
    public void TurnOn()
    {
        State.Visible = true;
        State.Frame = 0f;

        if (!Animates(Vars.Framerate, _maxFrame, Flags))
            return;

        _lastAnimateAt = _clock.Now;
        SetNextThink(0f);
    }

    /// <summary>CSprite::TurnOff. A sprite fading to its death keeps thinking toward it.</summary>
    public void TurnOff()
    {
        State.Visible = false;
        if (!_dying)
            StopThinking();
    }

    /// <summary>
    /// CSprite::SetBrightness( brightness, time ): the new brightness, which the
    /// client reaches over <paramref name="time"/> seconds (at once for none).
    /// </summary>
    public void SetBrightness(int brightness, float time = 0f)
    {
        State.Brightness = Math.Clamp(brightness, 0, 255);
        State.BrightnessTime = time;
    }

    /// <summary>CSprite::SetScale( scale, time ): the same for the size.</summary>
    public void SetScale(float scale, float time = 0f)
    {
        State.ScaleTime = time;
        State.Scale = scale;
    }

    /// <summary>
    /// CSprite::FadeAndDie: to brightness zero over <paramref name="duration"/>,
    /// then gone (AnimateUntilDead).
    /// </summary>
    public void FadeAndDie(float duration)
    {
        SetBrightness(0, duration);
        _dying = true;
        _dieTime = _clock.Now + duration;
        _lastAnimateAt = _clock.Now;
        SetNextThink(0f);
    }

    /// <summary>CSprite::FadeOutFromSpawn: a hundredth of a second, then FadeAndDie( 0.25 ).</summary>
    public void FadeOutFromSpawn()
    {
        _fadeOutPending = true;
        SetNextThink(0.01f);
    }

    [SaveField] private bool _dying;
    [SaveField] private float _dieTime;
    [SaveField] private bool _fadeOutPending;

    /// <summary>Stock's AnimateThink: advance by however much time has passed.</summary>
    public override void OnThink()
    {
        float now = _clock.Now;

        if (_fadeOutPending)
        {
            _fadeOutPending = false;
            FadeAndDie(0.25f);
            return;
        }

        // AnimateUntilDead: gone once its time is up, animating until then.
        if (_dying && now > _dieTime)
        {
            Remove();
            return;
        }

        (State.Frame, bool finished) =
            Advance(State.Frame, Vars.Framerate * (now - _lastAnimateAt), _maxFrame, Flags);

        _lastAnimateAt = now;

        if (finished)
        {
            TurnOff();
            if (!_dying)
                return;
        }

        SetNextThink(0f);
    }

    /// <summary>
    /// Stock's <c>Animate</c>: add, and then either stop or wrap.
    ///
    /// <para>
    /// The wrap is <c>fmod</c> against the LAST frame index rather than the
    /// count, which is stock's and means an animation of n frames cycles
    /// through n-1 of them. Kept because it is what every existing sprite was
    /// authored against, not because it is right.
    /// </para>
    ///
    /// <para>
    /// Static and taking every fact, so the wrap and the one-shot stop can be
    /// checked without an engine or a clock.
    /// </para>
    /// </summary>
    protected internal static (float Frame, bool Finished) Advance(float frame, float by,
                                                         float maxFrame, SpawnFlag flags)
    {
        frame += by;

        if (frame <= maxFrame)
            return (frame, false);

        if (flags.HasFlag(SpawnFlag.PlayOnce))
            return (frame, true);

        return (maxFrame > 0f ? frame % maxFrame : frame, false);
    }

    /// <summary>Stock's three colour inputs, which each replace one channel and clamp it.</summary>
    [ScriptedEvent("ColorRedValue")]
    private void OnColorRedValue(float value) => State.Red = (byte)Math.Clamp((int)value, 0, 255);

    [ScriptedEvent("ColorGreenValue")]
    private void OnColorGreenValue(float value) => State.Green = (byte)Math.Clamp((int)value, 0, 255);

    [ScriptedEvent("ColorBlueValue")]
    private void OnColorBlueValue(float value) => State.Blue = (byte)Math.Clamp((int)value, 0, 255);

    /// <summary>
    /// Stock's "SetScale" input, which is a <c>DEFINE_INPUT</c> straight onto
    /// the field — no ramp and no duration, whatever the two-argument
    /// <c>SetScale</c> in the header suggests.
    /// </summary>
    [ScriptedEvent("SetScale")]
    private void OnSetScale(float value) => State.Scale = Math.Clamp(value, 0f, MaxScale);

    [ScriptedEvent("ShowSprite")]
    private void OnShowSprite() => TurnOn();

    [ScriptedEvent("HideSprite")]
    private void OnHideSprite() => TurnOff();

    [ScriptedEvent("ToggleSprite")]
    private void OnToggleSprite() => Toggle();

    /// <summary>
    /// Stock's obsolete Use handler, which toggles.
    ///
    /// The use TYPE is not reachable on the parallel hierarchy — "Use" arrives
    /// with an empty variant — so <c>ShouldToggle</c>'s four-way switch becomes
    /// the toggle its default case already was. The same divergence env_embers
    /// records.
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUsed() => Toggle();

    private void Toggle()
    {
        if (State.Visible)
            TurnOff();
        else
            TurnOn();
    }

    /// <summary>
    /// "255 255 255" into three bytes.
    ///
    /// <para>
    /// A fourth number is allowed and ignored: "rendercolor" is three in every
    /// map in this tree, but Hammer has written four, and the alpha of a
    /// rendercolor has never been what renderamt means.
    /// </para>
    /// </summary>
    protected internal static (byte R, byte G, byte B) ColourFrom(Vector rgb)
        => ((byte)Math.Clamp((int)rgb.X, 0, 255),
            (byte)Math.Clamp((int)rgb.Y, 0, 255),
            (byte)Math.Clamp((int)rgb.Z, 0, 255));
}

/// <summary>
/// env_sprite, env_sprite_oriented and env_glow: the three names stock gives
/// <c>CSprite</c>, and the only class that writes those three tables.
/// </summary>
[ScriptedEntity(EntityNames.EnvSprite, ScriptedEntityKind.Effect)]
[ScriptedEntity(EntityNames.EnvSpriteOriented, ScriptedEntityKind.Effect)]
[ScriptedEntity(EntityNames.EnvGlow, ScriptedEntityKind.Effect)]
public sealed class EnvSprite : SpriteBase
{
    public EnvSprite(IEnginePrecache precache, IEngineEntityRender render, IGameClock clock)
        : base(precache, render, clock)
    {
    }

    /// <summary>
    /// The three classnames' tables, written through one interface.
    ///
    /// Only one of them is live on any instance — whichever classname the map
    /// used — and <see cref="State"/> is the one the rest of the class talks to.
    /// </summary>
    [NetState] public EnvSpriteNetState Sprite = new();

    [NetState] public EnvSpriteOrientedNetState Oriented = new();

    [NetState] public EnvGlowNetState Glow = new();

    protected override ISpriteNetState State => ClassName == EntityNames.EnvSpriteOriented
        ? Oriented
        : ClassName == EntityNames.EnvGlow ? Glow : Sprite;
}

/// <summary>
/// The map keys <see cref="SpriteBase"/> declares.
///
/// <para>
/// Six of them are <c>CBaseEntity</c>'s rather than <c>CSprite</c>'s — the
/// model, the four render keys and the colour. Stock gets those from the base
/// class's own datadesc and networks them through <c>DT_BaseEntity</c>; the
/// parallel hierarchy has neither, so they are declared here and carried
/// explicitly.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvSpriteVars
{
    /// <summary>The .spr or .vmt to draw. There is no default: a sprite with no material draws nothing.</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// How big, as a multiple of the sprite's own size. Stock's field default is
    /// zero and <c>DrawSpriteModel</c> reads zero as one.
    /// </summary>
    public float Scale { get; set; }

    /// <summary>Frames per second. Only does anything on a sprite with more than one frame.</summary>
    public float Framerate { get; set; }

    /// <summary>
    /// How big the occlusion proxy the glow modes fade against is. Stock's
    /// CONSTRUCTOR default, not its Spawn's — a managed field would start at
    /// zero and every glow sprite would be either fully on or fully off.
    /// </summary>
    public float GlowProxySize { get; set; } = 2.0f;

    /// <summary>
    /// Multiplies the material's $HDRCOLORSCALE. Stock's CONSTRUCTOR default
    /// again, and the same trap: zero here is a sprite that vanishes under HDR.
    /// </summary>
    [VarName("HDRColorScale")]
    public float HDRColorScale { get; set; } = 1.0f;

    /// <summary>Which blend mode. 5 is additive and 9 is world glow.</summary>
    [VarName("rendermode")]
    public int RenderMode { get; set; }

    /// <summary>Pulses and fades the renderer applies on top of the blend mode.</summary>
    [VarName("renderfx")]
    public int RenderFx { get; set; }

    /// <summary>The sprite's tint, "r g b".</summary>
    [VarName("rendercolor")]
    public Vector RenderColor { get; set; } = new(255f, 255f, 255f);

    /// <summary>
    /// How bright, 0..255. Stock's Spawn copies it into the brightness, so it is
    /// both the quad's vertex alpha and the renderer's blend.
    /// </summary>
    [VarName("renderamt")]
    public int RenderAmt { get; set; } = 255;
}
