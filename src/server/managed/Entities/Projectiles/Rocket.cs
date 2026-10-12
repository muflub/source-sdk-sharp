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
/// ss_rocket: what the RPG fires.
///
/// Unguided, and that is a divergence rather than a simplification — stock's
/// RPG steers its missile toward a laser dot the player paints. See
/// docs/divergences.md.
/// </summary>
[ScriptedEntity(EntityNames.SsRocket, ScriptedEntityKind.Projectile)]
public sealed class Rocket : FlyingProjectile
{
    public Rocket(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                  IEngineWorldQuery world, IEngineEffects effects, IEnginePrecache precache,
                  IEngineHost host, IBlastSweep blast, IServerDecals decals)
        : base(physics, motion)
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

    /// <summary>tracing, and what is at a point. For the blast.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>getting the blast's sprite and decal onto the client.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>the engine's random stream, which the blast's sparks draw from.</summary>
    private readonly IEngineHost Host;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;
    /// <summary>What a map may set on this entity. See <see cref="RocketVars"/>.</summary>
    [ScriptVars] public RocketVars Vars = new();

    /// <summary>Flat. A rocket under thrust does not fall.</summary>
    protected override float GravityScale => 0f;

    protected override string ProjectileModel => "models/weapons/w_missile.mdl";

    public readonly Output OnExplode = new("OnExplode");

    protected override void Impact(EntityHandle hit)
    {
        OnExplode.Fire();

        // At the rocket's position, before it goes: the explosion is placed
        // where the entity is, and a removed entity is nowhere.
        // ignored: Handle -- an entity that explodes where it stands must say so,
        // or its own hull stops the very first trace of the traversal and every
        // victim in the radius is scored at distance zero. See Explosions.Fire.
        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        Origin, Vars.Magnitude, Vars.Radius, Attribution,
                        ignored: Handle, inflictor: Handle);
    }
}

/// <summary>
/// The map keys <see cref="Rocket"/> declares.
/// </summary>
[ScriptVars]
public partial class RocketVars
{
    /// <summary>Stock's <c>CMissile::Spawn</c> — <c>SetDamage( 200.0f )</c>.</summary>
    public float Magnitude { get; set; } = 200f;

    /// <summary>
    /// Stock's <c>CMissile::EXPLOSION_RADIUS</c>, which is 200 and was 300 here.
    ///
    /// Not a free 50%: the traversal's falloff is magnitude/radius per unit, so
    /// a wider blast is a HARDER one at every distance as well as a longer one.
    /// At 100 units 300 gave 133 where 200 gives 100, and out at 250 it still
    /// gave 33 where stock had already stopped. On a map with explosive barrels
    /// the extra hundred units is what reaches the next barrel, and the chain
    /// comes back to whoever fired.
    /// </summary>
    public float Radius { get; set; } = 200f;
}
