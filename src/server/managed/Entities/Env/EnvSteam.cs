using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_steam and env_steamjet: a jet of steam or a heat haze.
///
/// <para>
/// The server entity is eleven networked numbers, four of which a map may also
/// drive as inputs, and an on/off switch. Stock's <c>Spawn</c> is four lines:
/// precache two materials, ask its own classname whether it is the legacy one,
/// and copy the initial state into the emit flag. There is no behaviour here at
/// all — everything a steam jet does is on the client.
/// </para>
///
/// <para>
/// Which is exactly why this row needed a client half written before it could
/// move, and why fifteen of its sixteen neighbours in report.md section 2 could
/// not simply be lifted. Stock's jet draws because the server networks a
/// <c>CSteamJet</c> and the engine builds the <c>C_SteamJet</c> paired with
/// <c>DT_SteamJet</c>. A managed entity never appears on the wire as that class
/// — every one of them arrives through one of the six tier tables in
/// <c>generic_nettable.h</c> — so moving the classname alone would have
/// delivered the entity perfectly and drawn nothing. See
/// <see cref="Client.Standalone.ClientSteamJet"/> and
/// <c>shell_steamjet_render.cpp</c>.
/// </para>
///
/// <para>
/// The two classnames are one class, as they are in stock, and the whole
/// difference between them is <see cref="ISteamJetNetState.FaceLeft"/>: the
/// legacy env_steamjet pointed left where env_steam points forward.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSteam, ScriptedEntityKind.Effect)]
[ScriptedEntity(EntityNames.EnvSteamjet, ScriptedEntityKind.Effect)]
public sealed class EnvSteam : ScriptedEntity
{
    public EnvSteam(IEnginePrecache precache)
    {
        Precache = precache;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;
    /// <summary>What a map may set on this entity. See <see cref="EnvSteamVars"/>.</summary>
    [ScriptVars] public EnvSteamVars Vars = new();

    // One table per classname, because a table is keyed by the name the entity
    // is spawned under. The FIELDS are declared once — see ISteamJetNetState —
    // so the two cannot drift.
    [NetState] public EnvSteamNetState Steam = new();
    [NetState] public EnvSteamJetNetState Jet = new();

    /// <summary>Whichever of the two this instance actually is.</summary>
    internal ISteamJetNetState State => ClassName == EntityNames.EnvSteamjet
        ? Jet
        : (ISteamJetNetState)Steam;

    public override void OnSpawn()
    {
        Precache.PrecacheMaterial("particle/particle_smokegrenade");
        Precache.PrecacheMaterial("sprites/heatwave");

        var state = State;

        state.SpreadSpeed = Vars.SpreadSpeed;
        state.Speed       = Vars.Speed;
        state.StartSize   = Vars.StartSize;
        state.EndSize     = Vars.EndSize;
        state.Rate        = Vars.Rate;
        state.JetLength   = Vars.JetLength;
        state.Type        = Vars.Type;
        state.RollSpeed   = Vars.RollSpeed;
        state.SpawnFlags  = SpawnFlags;

        state.Red   = ToByte(Vars.RenderColor.X);
        state.Green = ToByte(Vars.RenderColor.Y);
        state.Blue  = ToByte(Vars.RenderColor.Z);

        // The whole of what separates the two classnames, and stock decides it
        // the same way: by asking its own classname in Spawn.
        state.FaceLeft = ClassName == EntityNames.EnvSteamjet;

        // Stock only ever SETS this, never clears it — an entity whose
        // InitialState is unticked is left at whatever the member already was,
        // which for a freshly allocated CSteamJet is false. Written out both
        // ways here because the managed state is not zeroed by allocation.
        state.Emit = Vars.InitialState;
    }

    //-------------------------------------------------------------------------
    // The four DEFINE_INPUTs, which in Source are a keyvalue and an input with
    // one name. Both halves are here: the keys are on EnvSteamVars and read at
    // spawn, and these write the same networked fields afterwards.
    //-------------------------------------------------------------------------
    [ScriptedEvent("JetLength")]   private void OnJetLength(float v)   => State.JetLength = v;
    [ScriptedEvent("SpreadSpeed")] private void OnSpreadSpeed(float v) => State.SpreadSpeed = v;
    [ScriptedEvent("Speed")]       private void OnSpeed(float v)       => State.Speed = v;
    [ScriptedEvent("Rate")]        private void OnRate(float v)        => State.Rate = v;

    [ScriptedEvent("TurnOn")]  private void OnTurnOn()  => State.Emit = true;
    [ScriptedEvent("TurnOff")] private void OnTurnOff() => State.Emit = false;
    [ScriptedEvent("Toggle")]  private void OnToggle()  => State.Emit = !State.Emit;

    // Stock's Use is NOT ported, and it is measured dead rather than declined.
    // CSteamJet::Use answers USE_ON and USE_OFF only — there is no toggle case
    // and no default — and it refuses a player outright with
    // if (!pActivator->IsPlayer()). Both ways in are therefore closed: the use
    // key hands it a player, and CBaseEntity::InputUse — the only other route,
    // and what an ent_fire Use becomes — passes USE_TOGGLE, which the switch has
    // no branch for. Nothing in this build calls Use with an explicit USE_ON on
    // a steam jet. The four inputs above are how a map has always switched one.

    private static byte ToByte(float value) => (byte)Math.Clamp((int)value, 0, 255);
}

/// <summary>
/// The map keys <see cref="EnvSteam"/> declares.
///
/// <para>
/// Four of them — JetLength, SpreadSpeed, Speed and Rate — are
/// <c>DEFINE_INPUT</c> in stock, which makes each both a key and an input with
/// the same name. Both halves are declared: the key here, the input on the
/// entity.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvSteamVars
{
    /// <summary>How fast a puff drifts sideways. Stock's server leaves this at zero unless a map sets it.</summary>
    public float SpreadSpeed { get; set; }

    public float Speed { get; set; }

    public float StartSize { get; set; }

    public float EndSize { get; set; }

    /// <summary>Puffs per second.</summary>
    public float Rate { get; set; }

    public float JetLength { get; set; }

    /// <summary>Starts emitting. Stock's key is "InitialState".</summary>
    public bool InitialState { get; set; }

    /// <summary>0 is steam, 1 is a heat haze.</summary>
    public int Type { get; set; }

    /// <summary>How fast a puff turns as it travels. Stock's constructor defaults this to 8, before any key is read.</summary>
    public float RollSpeed { get; set; } = 8f;

    /// <summary>Tints the lighting ramp. Stock reads it off the entity's own m_clrRender rather than off the send table.</summary>
    [VarName("rendercolor")]
    public Vector RenderColor { get; set; } = new(255f, 255f, 255f);
}
