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
/// env_smokestack: a chimney.
///
/// <para>
/// The largest of this whole family and the only one with real server-side work
/// in it. Nineteen networked values, ten map keys, seven inputs — and an
/// <c>Activate</c> that walks the entity list looking for the lights that name
/// it.
/// </para>
///
/// <para>
/// THAT WALK IS WHY IT MOVED IN A PAIR. Stock's
/// <c>gEntList.FindEntityByClassname</c> walks the NATIVE entity list, where
/// every managed entity appears as one of a handful of shell classes rather than
/// under the name a map wrote. Move <c>env_smokestack</c> alone and it can no
/// longer see native lights; move <c>env_particlelight</c> alone and every stock
/// stack silently loses its lighting — the stack still draws and still networks,
/// and reads as flat grey smoke with no fire under it. Moving both puts the
/// lookup in C#, through <see cref="IEntityDirectory.OfClass"/>, where a managed
/// stack finds a managed light. See <see cref="EnvParticleLight"/>.
/// </para>
///
/// <para>
/// And it needed a client half written before either could move, for the reason
/// the whole particle family turns on: a managed entity is never on the wire as
/// <c>CSmokeStack</c>, so <c>DT_SmokeStack</c> stops being sent and the
/// <c>C_SmokeStack</c> that draws is never built. See
/// <see cref="Client.Standalone.ClientSmokeStack"/> and
/// <c>shell_smokestack_render.cpp</c>.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSmokestack, ScriptedEntityKind.Effect)]
public sealed class EnvSmokeStack : ScriptedEntity
{
    public EnvSmokeStack(IEnginePrecache precache, IEngineEntityRender render, IEntityDirectory directory)
    {
        Precache = precache;
        Render = render;
        Directory = directory;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>what this entity looks like — here, only to answer the skybox question.</summary>
    private readonly IEngineEntityRender Render;

    /// <summary>the other managed entities, which is where the lights are.</summary>
    private readonly IEntityDirectory Directory;

    /// <summary>What a map may set on this entity. See <see cref="EnvSmokeStackVars"/>.</summary>
    [ScriptVars] public EnvSmokeStackVars Vars = new();

    [NetState] public SmokeStackNetState State = new();

    /// <summary>Stock's legacy material, used when a map names none.</summary>
    private const string DefaultMaterial = "particle/SmokeStack.vmt";

    public override void OnSpawn()
    {
        State.StartSize  = Vars.StartSize;
        State.EndSize    = Vars.EndSize;
        State.BaseSpread = Vars.BaseSpread;
        State.Twist      = Vars.Twist;
        State.RollSpeed  = Vars.Roll;

        // Stock's KeyValue does this arithmetic the moment either key arrives,
        // which matters only because a map may set an explicit Wind vector
        // instead — and then the LAST key wins. Here both keys are read and the
        // rule is written down: an explicit vector beats an angle and a speed.
        State.Wind = WindFor(Vars.Wind, Vars.WindAngle, Vars.WindSpeed);

        State.MaterialModel = Precache.PrecacheModel(MaterialFor(Vars.SmokeMaterial));

        // The mapper's rendercolor, which stock keeps on the entity rather than
        // on the send table. Stock's constructor default is BLACK at full alpha
        // — see EnvSmokeStackVars.RenderColour.
        State.Red   = ToByte(Vars.RenderColour.X);
        State.Green = ToByte(Vars.RenderColour.Y);
        State.Blue  = ToByte(Vars.RenderColour.Z);
        State.Alpha = (byte)Math.Clamp(Vars.RenderAmount, 0, 255);

        // Stock's Spawn is these two lines, and the asymmetry is stock's: a
        // stack with InitialState unticked is left at whatever m_bEmit already
        // was, which for a fresh CSmokeStack is false. Written both ways because
        // a managed field is not zeroed by allocation.
        State.Emit = Vars.InitialState;

        // The four DEFINE_INPUTs also have to start somewhere, and stock leaves
        // them at zero on the server — CSmokeStack's constructor never touches
        // them. The CLIENT's constructor has real defaults (15, 30, 80, 180),
        // and until the first update arrives those are what it uses; the update
        // then overwrites them with these zeros. So a map that sets none of the
        // four gets a stack that emits nothing, in stock as much as here.
        State.SpreadSpeed = Vars.SpreadSpeed;
        State.Speed       = Vars.Speed;
        State.Rate        = Vars.Rate;
        State.JetLength   = Vars.JetLength;

        // Stock's Activate runs after every entity in the map has spawned, and
        // that ordering is the whole point: the lights have to exist to be
        // found. A think at zero delay is the first moment with the same
        // guarantee — the parallel hierarchy has no Activate of its own.
        SetNextThink(0f);
    }

    /// <summary>
    /// Stock's <c>Activate</c>, once, and then never again.
    ///
    /// <para>
    /// Two things happen here and both need every entity to exist first: the
    /// light search, and the skybox test. The skybox one is the less obvious and
    /// the more consequential — <c>DetectInSkybox</c> raises a flag whose only
    /// effect is that the entity is sent to every client instead of being
    /// PVS-checked, and a 3D skybox is in nobody's PVS. Without it a stack
    /// standing in a skybox is culled for everyone and drawn for no one, which
    /// is the entity's most famous use.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        StopThinking();

        if (Render.DetectInSkybox(Handle))
            Render.SetTransmitAlways(Handle, true);

        CollectLights();
    }

    /// <summary>
    /// Copies the lights that name this stack into its own state.
    ///
    /// <para>
    /// Stock's loop, with one difference that is not a choice: the search is
    /// over the MANAGED directory rather than <c>gEntList</c>, because the
    /// engine's list cannot see the parallel hierarchy. See the class remarks.
    /// </para>
    ///
    /// <para>
    /// A stack has one ambient slot and one directional slot, so two lights of
    /// the same kind naming one stack means the last one found wins. That is
    /// stock's behaviour and it is not a documented rule — it falls out of the
    /// loop overwriting the same struct — and here it is worse than stock in one
    /// respect: the managed directory is a dictionary, so WHICH one wins is not
    /// even stable between runs. Recorded in docs/divergences.md.
    /// </para>
    /// </summary>
    private void CollectLights()
    {
        foreach (var entity in Directory.OfClass(EntityNames.EnvParticlelight))
        {
            if (entity is not EnvParticleLight light)
                continue;

            if (!string.Equals(light.ParticleSystemName, Name, StringComparison.OrdinalIgnoreCase))
                continue;

            // Into 0-1 space, which is where stock puts them at the end of
            // Activate and where the client reads them.
            var colour = light.Colour * (1f / 255f);

            if (light.Directional)
            {
                State.DirPos       = light.Place;
                State.DirColor     = colour;
                State.DirIntensity = light.Intensity;
            }
            else
            {
                State.AmbientPos       = light.Place;
                State.AmbientColor     = colour;
                State.AmbientIntensity = light.Intensity;
            }
        }
    }

    /// <summary>
    /// The material a stack draws with, as a name to precache.
    ///
    /// <para>
    /// Stock's rule, which is that the key may or may not carry the extension:
    /// a bare name gets <c>.vmt</c> appended, and an empty key falls back to the
    /// legacy <c>particle/SmokeStack.vmt</c>. That fallback is stock's too, and
    /// it lives at the end of <c>Activate</c> rather than in <c>KeyValue</c>
    /// because it only applies when nothing resolved a model at all.
    /// </para>
    ///
    /// <para>
    /// What is NOT ported is the numbered SERIES: stock's <c>KeyValue</c> probes
    /// the filesystem for <c>&lt;name&gt;1.vmt</c>, <c>&lt;name&gt;2.vmt</c> and
    /// so on and precaches each. Measured dead rather than declined — the only
    /// thing that reads those handles is <c>C_SmokeStack::Start</c> under
    /// <c>HL2_EPISODIC</c>, and without that define <c>SMOKESTACK_MAX_MATERIALS</c>
    /// is 1 and <c>C_SmokeStack::Update</c> forces the frame index to zero. This
    /// build does not define it, so the series would be precached and never
    /// looked at, at the cost of a filesystem call across the ABI.
    /// </para>
    ///
    /// <para>
    /// Static and taking the key, so the rule can be checked without an engine.
    /// </para>
    /// </summary>
    internal static string MaterialFor(string smokeMaterial)
    {
        if (smokeMaterial.Length == 0)
            return DefaultMaterial;

        return smokeMaterial.Contains(".vmt", StringComparison.OrdinalIgnoreCase)
            ? smokeMaterial
            : smokeMaterial + ".vmt";
    }

    /// <summary>
    /// The wind vector, from either of the two ways a map may say it.
    ///
    /// <para>
    /// Stock accepts an explicit <c>Wind</c> vector OR a <c>WindAngle</c> and a
    /// <c>WindSpeed</c>, and in stock the LAST key parsed wins because each
    /// writes the same member as it arrives. A map cannot control that order, so
    /// the rule is made explicit here instead: an explicit vector beats the
    /// angle-and-speed pair, and the pair applies only when no vector was given.
    /// Every map that sets both was relying on an accident either way.
    /// </para>
    ///
    /// <para>
    /// Static and taking all three, so it can be checked without an engine.
    /// </para>
    /// </summary>
    internal static Vector WindFor(Vector wind, int windAngle, int windSpeed)
    {
        if (wind.X != 0f || wind.Y != 0f || wind.Z != 0f)
            return wind;

        float radians = windAngle * (MathF.PI / 180f);

        return new Vector(MathF.Cos(radians) * windSpeed,
                          MathF.Sin(radians) * windSpeed,
                          0f);
    }

    //-------------------------------------------------------------------------
    // The four DEFINE_INPUTs, which in Source are a keyvalue and an input with
    // one name. Both halves are here: the keys are on EnvSmokeStackVars and read
    // at spawn, and these write the same networked fields afterwards.
    //
    // Rate is the odd one. It reaches the wire like the other three, and the
    // client never re-reads it: C_SmokeStack arms its spawn timer in Start and
    // has no SetSpawnRate. So driving it changes what the entity reports and not
    // what it emits — which is stock's behaviour, kept rather than repaired.
    //-------------------------------------------------------------------------
    [ScriptedEvent("JetLength")]   private void OnJetLength(float v)   => State.JetLength = v;
    [ScriptedEvent("SpreadSpeed")] private void OnSpreadSpeed(float v) => State.SpreadSpeed = v;
    [ScriptedEvent("Speed")]       private void OnSpeed(float v)       => State.Speed = v;
    [ScriptedEvent("Rate")]        private void OnRate(float v)        => State.Rate = v;

    [ScriptedEvent("TurnOn")]  private void OnTurnOn()  => State.Emit = true;
    [ScriptedEvent("TurnOff")] private void OnTurnOff() => State.Emit = false;
    [ScriptedEvent("Toggle")]  private void OnToggle()  => State.Emit = !State.Emit;

    private static byte ToByte(float value) => (byte)Math.Clamp((int)value, 0, 255);
}

/// <summary>
/// The map keys <see cref="EnvSmokeStack"/> declares.
///
/// <para>
/// Four of them — JetLength, SpreadSpeed, Speed and Rate — are
/// <c>DEFINE_INPUT</c> in stock, which makes each both a key and an input with
/// the same name. Both halves are declared: the key here, the input on the
/// entity.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvSmokeStackVars
{
    public float StartSize { get; set; }
    public float EndSize { get; set; }

    /// <summary>How wide the mouth of the stack is.</summary>
    public float BaseSpread { get; set; }

    /// <summary>Degrees a second the column rotates about its own axis.</summary>
    public float Twist { get; set; }

    /// <summary>How fast a puff turns as it rises. Stock's key is "Roll".</summary>
    public float Roll { get; set; }

    /// <summary>Starts emitting.</summary>
    public bool InitialState { get; set; }

    /// <summary>An explicit wind vector, which beats the angle and speed below.</summary>
    public Vector Wind { get; set; }

    /// <summary>Which way the wind blows, in degrees.</summary>
    public int WindAngle { get; set; }

    /// <summary>How hard the wind blows.</summary>
    public int WindSpeed { get; set; }

    /// <summary>
    /// The material to draw the smoke with, with or without its <c>.vmt</c>.
    /// Empty falls back to stock's legacy <c>particle/SmokeStack.vmt</c>.
    /// </summary>
    public string SmokeMaterial { get; set; } = "";

    /// <summary>How fast a puff drifts sideways as it rises.</summary>
    public float SpreadSpeed { get; set; }

    /// <summary>How fast a puff rises.</summary>
    public float Speed { get; set; }

    /// <summary>Puffs per second, read once when the client builds the emitter.</summary>
    public float Rate { get; set; }

    /// <summary>How far the column reaches.</summary>
    public float JetLength { get; set; }

    /// <summary>
    /// Tints the whole column. Stock's constructor sets BLACK at full alpha,
    /// which is what makes an unlit stack read as smoke rather than as a white
    /// cloud — so the default here is black rather than the white every other
    /// entity's rendercolor defaults to.
    /// </summary>
    [VarName("rendercolor")]
    public Vector RenderColour { get; set; } = new(0f, 0f, 0f);

    /// <summary>How solid the column is overall. Stock's default is 255.</summary>
    [VarName("renderamt")]
    public int RenderAmount { get; set; } = 255;
}
