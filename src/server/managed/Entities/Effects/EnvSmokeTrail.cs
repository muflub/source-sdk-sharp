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
/// env_smoketrail: a column of smoke that follows whatever it is on.
///
/// <para>
/// The head of the six classes in <c>smoke_trail.cpp</c> and the widest of them.
/// Stock's server half is a send table of fifteen values, a <c>KeyValue</c> that
/// handles three keys the datadesc cannot, a <c>SetEmit</c> and a
/// <c>FollowEntity</c> that only C++ callers ever reached, and no
/// <c>Spawn</c> at all. Everything a smoke trail does is on the client.
/// </para>
///
/// <para>
/// Which makes the CONSTRUCTOR the whole of the server behaviour worth porting,
/// and the thing that would have gone silently wrong if it had not been. Stock
/// sets thirteen of the fifteen fields in <c>SmokeTrail::SmokeTrail</c> and not
/// in <c>Spawn</c>, so a map that writes no keys at all still gets ten puffs a
/// second, five seconds each, grey fading to black. A managed field starts at
/// zero instead — and a trail whose spawn rate is zero emits nothing while
/// reporting every field correctly through <c>debug-api payload</c>. Those
/// defaults are on <see cref="EnvSmokeTrailVars"/>, where a mapper's key
/// overwrites them exactly as stock's keyvalue pass overwrites the
/// constructor's.
/// </para>
///
/// <para>
/// It needed a client half written before the classname could move, for the
/// reason the whole particle and trail family turns on: a managed entity is
/// never on the wire as <c>SmokeTrail</c> — it arrives through one of the six
/// tier tables in <c>generic_nettable.h</c> — so <c>DT_SmokeTrail</c> stops
/// being sent and the <c>C_SmokeTrail</c> that draws is never built. See
/// <see cref="Client.Standalone.ClientSmokeTrail"/> and
/// its sim in SourceSharp.Client.Effects (lane c2-sims).
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSmoketrail, ScriptedEntityKind.Effect)]
public sealed class EnvSmokeTrail : ScriptedEntity
{
    public EnvSmokeTrail(IEngineHost host)
    {
        Host = host;
    }

    /// <summary>the process and the match — here, only for what time it is.</summary>
    private readonly IEngineHost Host;

    /// <summary>What a map may set on this entity. See <see cref="EnvSmokeTrailVars"/>.</summary>
    [ScriptVars] public EnvSmokeTrailVars Vars = new();

    [NetState] public SmokeTrailNetState State = new();

    public override void OnSpawn()
    {
        State.SpawnRate        = Vars.SpawnRate;
        State.Opacity          = Vars.Opacity;
        State.ParticleLifetime = Vars.Lifetime;
        State.MinSpeed         = Vars.MinSpeed;
        State.MaxSpeed         = Vars.MaxSpeed;
        State.MinDirectedSpeed = Vars.MinDirectedSpeed;
        State.MaxDirectedSpeed = Vars.MaxDirectedSpeed;
        State.StartSize        = Vars.StartSize;
        State.EndSize          = Vars.EndSize;
        State.SpawnRadius      = Vars.SpawnRadius;

        // Into 0-1 space, which is the space the client reads them in. Stock
        // does the same divide in its own KeyValue, one key at a time.
        State.StartColor = Vars.StartColor * (1f / 255f);
        State.EndColor   = Vars.EndColor * (1f / 255f);

        State.StopEmitTime = StopEmitTimeFor(Vars.EmitTime, Host.CurrentTime);

        // Stock's constructor, and there is no way to change it: env_smoketrail
        // has no key and no input for emitting, and SetEmit is only reachable
        // from C++. A map-placed one emits from the moment it exists until its
        // emittime runs out.
        State.Emit = true;
    }

    /// <summary>
    /// When a trail stops emitting, from the mapper's duration and the clock.
    ///
    /// <para>
    /// Stock's <c>emittime</c> key is a DURATION and the networked field is an
    /// ABSOLUTE time: <c>KeyValue</c> writes <c>curtime + atof(value)</c> once,
    /// at parse, and the client compares the result to its own clock every
    /// frame. Zero means never stop, so a trail with no key must not be given
    /// the current time — that would stop it the instant it started.
    /// </para>
    ///
    /// <para>
    /// Which is what the negative default is for. An unset key has to be
    /// distinguishable from <c>emittime 0</c>, because stock treats those
    /// differently: no key leaves the field at zero and never stops, and an
    /// explicit zero gives <c>curtime + 0</c> and stops immediately.
    /// </para>
    ///
    /// <para>
    /// Static and taking the clock, so it can be checked without an engine —
    /// the same reason <c>EnvShake.RadiusFor</c> is one.
    /// </para>
    /// </summary>
    internal static float StopEmitTimeFor(float emitTime, float now)
        => emitTime < 0f ? 0f : now + emitTime;
}

/// <summary>
/// The map keys <see cref="EnvSmokeTrail"/> declares, and the defaults stock
/// puts in its constructor.
///
/// <para>
/// Every default here is <c>SmokeTrail::SmokeTrail</c>'s, not a choice. Stock
/// sets them before the keyvalue pass runs, so a map that writes nothing still
/// gets a working trail; a managed class that let them start at zero would get
/// an entity that networks perfectly and emits nothing.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvSmokeTrailVars
{
    /// <summary>Puffs per second. Stock's "spawnrate".</summary>
    [VarName("spawnrate")]
    public float SpawnRate { get; set; } = 10f;

    /// <summary>
    /// The colour a puff starts at, as the mapper writes it: 0-255.
    ///
    /// The default is stock's <c>Init(0.5, 0.5, 0.5)</c> expressed in the
    /// mapper's units, because that is the space this key arrives in and the
    /// entity divides once. 127.5/255 is exactly 0.5.
    /// </summary>
    [VarName("startcolor")]
    public Vector StartColor { get; set; } = new(127.5f, 127.5f, 127.5f);

    /// <summary>The colour a puff ends at, 0-255. Stock's default is black.</summary>
    [VarName("endcolor")]
    public Vector EndColor { get; set; } = new(0f, 0f, 0f);

    public float Opacity { get; set; } = 0.5f;

    /// <summary>How long a puff lasts, in seconds. Stock's key is "lifetime".</summary>
    public float Lifetime { get; set; } = 5f;

    /// <summary>
    /// How long the trail emits for, in seconds. NEGATIVE means the key was not
    /// given, which is not the same as zero — see
    /// <see cref="EnvSmokeTrail.StopEmitTimeFor"/>.
    /// </summary>
    [VarName("emittime")]
    public float EmitTime { get; set; } = -1f;

    [VarName("minspeed")]
    public float MinSpeed { get; set; } = 2f;

    [VarName("maxspeed")]
    public float MaxSpeed { get; set; } = 4f;

    [VarName("mindirectedspeed")]
    public float MinDirectedSpeed { get; set; }

    [VarName("maxdirectedspeed")]
    public float MaxDirectedSpeed { get; set; }

    [VarName("startsize")]
    public float StartSize { get; set; } = 35f;

    [VarName("endsize")]
    public float EndSize { get; set; } = 55f;

    /// <summary>How far off the entity's own origin a puff may appear.</summary>
    [VarName("spawnradius")]
    public float SpawnRadius { get; set; } = 2f;
}
