using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_dusttrail: a column of dust that follows whatever it is on.
///
/// <para>
/// <see cref="EnvSmokeTrail"/>'s twin, and deliberately not merged with it. The
/// two classes are separate in stock — separate send tables, separate datadescs,
/// separate constructors with the same numbers in them — and the whole
/// difference between them is that dust has ONE colour where smoke fades
/// between two, and no attachment index. Merging them would put a field on the
/// wire for a class that does not have it.
/// </para>
///
/// <para>
/// Every default is <c>DustTrail::DustTrail</c>'s, on
/// <see cref="EnvDustTrailVars"/>, for the reason
/// <see cref="EnvSmokeTrail"/> gives: stock sets them in the CONSTRUCTOR, so a
/// map that writes nothing still gets a working trail, and a managed field that
/// started at zero would give an entity that networks perfectly and emits
/// nothing.
/// </para>
///
/// <para>
/// Its one caller in the whole tree is
/// <c>CBaseAnimating::HandleAnimEvent</c> at <c>baseanimating.cpp:1273</c>,
/// which is behind <c>HL2_EPISODIC</c> — not defined in this build, as
/// <c>sdk_csharp_release.ninja</c> shows. So nothing creates one and only a map
/// can, which is what makes the classname safe to move: stock's
/// <c>CreateDustTrail</c> goes through <c>CreateEntityByName</c> and
/// <c>dynamic_cast</c>, and a managed entity fails that cast silently.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvDusttrail, ScriptedEntityKind.Effect)]
public sealed class EnvDustTrail : ScriptedEntity
{
    public EnvDustTrail(IEngineHost host)
    {
        Host = host;
    }

    /// <summary>the process and the match — here, only for what time it is.</summary>
    private readonly IEngineHost Host;

    /// <summary>What a map may set on this entity. See <see cref="EnvDustTrailVars"/>.</summary>
    [ScriptVars] public EnvDustTrailVars Vars = new();

    [NetState] public DustTrailNetState State = new();

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

        // Into 0-1 space, which is the space the client reads it in. Stock does
        // the same divide in its own KeyValue.
        State.Color = Vars.Color * (1f / 255f);

        // The same absolute-time rule env_smoketrail has, and the same reading
        // of it: shared rather than copied, so the two cannot drift.
        State.StopEmitTime = EnvSmokeTrail.StopEmitTimeFor(Vars.EmitTime, Host.CurrentTime);

        // Stock's constructor. There is no key and no input for this on
        // env_dusttrail either.
        State.Emit = true;
    }
}

/// <summary>
/// The map keys <see cref="EnvDustTrail"/> declares, and the defaults stock
/// puts in its constructor.
/// </summary>
[ScriptVars]
public partial class EnvDustTrailVars
{
    /// <summary>Puffs per second.</summary>
    [VarName("spawnrate")]
    public float SpawnRate { get; set; } = 10f;

    /// <summary>
    /// The colour of a puff, as the mapper writes it: 0-255. The entity divides
    /// once, because the client reads it in 0-1 space.
    ///
    /// The default is stock's <c>Init(0.5, 0.5, 0.5)</c> in those units;
    /// 127.5/255 is exactly 0.5. And the KEY is "color", not "rendercolor" —
    /// stock's <c>DustTrail::KeyValue</c> answers that name and no other.
    /// </summary>
    [VarName("color")]
    public Vector Color { get; set; } = new(127.5f, 127.5f, 127.5f);

    public float Opacity { get; set; } = 0.5f;

    /// <summary>How long a puff lasts, in seconds. Stock's key is "lifetime".</summary>
    public float Lifetime { get; set; } = 5f;

    /// <summary>
    /// How long the trail emits for, in seconds. NEGATIVE means the key was not
    /// given — see <see cref="EnvSmokeTrail.StopEmitTimeFor"/>.
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

    [VarName("spawnradius")]
    public float SpawnRadius { get; set; } = 2f;
}
