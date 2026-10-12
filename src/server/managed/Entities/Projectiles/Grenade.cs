using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// ss_grenade: something thrown that goes off.
///
/// The first thing in this mod that a weapon LAUNCHES rather than traces, and
/// it needed no new engine surface at all — which is worth saying, because the
/// obvious assumption is that projectiles are an engine feature. They are not.
/// A grenade is a physics prop with a fuse, and every piece of that already
/// existed: create an entity, give it a model, shove it, think on a timer,
/// make an explosion.
///
/// <para>
/// It bounces because the prop shell simulates it, which is the behaviour a
/// frag grenade wants and would have been work to get any other way.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsGrenade, ScriptedEntityKind.Prop)]
public sealed class Grenade : ScriptedEntity
{
    public Grenade(IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                   IEnginePrecache precache, IEngineHost host, IBlastSweep blast, IServerDecals decals)
    {
        Damage = damage;
        World = world;
        Effects = effects;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
    }

    /// <summary>
    /// The managed blast traversal — the falloff, the line of sight and the
    /// blocking rule that used to run inside <c>CGameRules::RadiusDamage</c>.
    /// See <see cref="BlastSweep"/>.
    /// </summary>
    private readonly IBlastSweep Blast;

    /// <summary>the decal system, whose "Scorch" group the blast paints from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>tracing, and what is at a point. For the blast.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>getting the blast's sprite and decal onto the client.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>the engine's random stream, which the blast's sparks draw from.</summary>
    private readonly IEngineHost Host;
    /// <summary>What a map may set on this entity. See <see cref="GrenadeVars"/>.</summary>
    [ScriptVars] public GrenadeVars Vars = new();

    /// <summary>Stock HL2's grenade, so it looks like one without shipping a model.</summary>
    private const string Model = "models/Weapons/w_grenade.mdl";

    private EntityHandle _thrower = EntityHandle.None;
    private bool _exploded;

    public readonly Output OnExplode = new("OnExplode");

    public override void OnSpawn()
    {
        SetModel(Model);
        SetNextThink(Vars.Fuse);
    }

    /// <summary>
    /// Who threw it, so the explosion is attributed to them.
    ///
    /// Without this a player killed by their own grenade is killed by nobody,
    /// and the scoreboard has no idea what happened.
    /// </summary>
    internal void ThrownBy(EntityHandle thrower, float fuse)
    {
        _thrower = thrower;
        Vars.Fuse = fuse;

        SetNextThink(fuse);
    }

    public override void OnThink() => Explode();

    private void Explode()
    {
        if (_exploded)
            return;

        _exploded = true;
        OnExplode.Fire();

        // The explosion is made BEFORE the grenade goes, because it is made at
        // the grenade's position and a removed entity has none.
        // ignored: Handle -- an entity that explodes where it stands must say so,
        // or its own hull stops the very first trace of the traversal and every
        // victim in the radius is scored at distance zero. See Explosions.Fire.
        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        Origin, Vars.Magnitude, Vars.Radius,
                        Entities.IsValid(_thrower) ? _thrower : Handle,
                        ignored: Handle, inflictor: Handle);

        Remove();
    }
}

/// <summary>
/// The map keys <see cref="Grenade"/> declares.
/// </summary>
[ScriptVars]
public partial class GrenadeVars
{
    /// <summary>Seconds before it goes off, from the moment it is thrown.</summary>
    public float Fuse { get; set; } = 3f;

    public float Magnitude { get; set; } = 150f;

    public float Radius { get; set; } = 250f;
}
