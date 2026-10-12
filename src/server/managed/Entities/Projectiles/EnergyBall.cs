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
/// ss_energyball: the AR2's alt-fire.
///
/// Stock's combine ball, and the thing it does that no other projectile here
/// does is SURVIVE hitting the world — it ricochets off walls and keeps going,
/// so a shot down a corridor stays a threat after it has stopped being aimed.
/// That is the whole weapon, and it is why the shell learned to bounce.
///
/// <para>
/// Two endings, not three. Catching something is one; the other is the clock,
/// which also covers running out of bounces — a spent ball stops moving and
/// then times out where it lies. A separate "stopped bouncing" ending would
/// need the shell to distinguish a bounce from a halt, and the timer already
/// makes that difference invisible.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsEnergyball, ScriptedEntityKind.Projectile)]
public sealed class EnergyBall : FlyingProjectile
{
    public EnergyBall(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
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
    /// <summary>What a map may set on this entity. See <see cref="EnergyBallVars"/>.</summary>
    [ScriptVars] public EnergyBallVars Vars = new();

    /// <summary>Flat. It is under its own power and does not fall.</summary>
    protected override float GravityScale => 0f;

    /// <summary>
    /// The one projectile in this build that comes off a wall and keeps going,
    /// which is the whole reason it is not a rocket.
    /// </summary>
    protected override int Bounces => Vars.Bounces;

    /// <summary>Stock's combine ball, so it looks like one without shipping a model.</summary>
    protected override string ProjectileModel => "models/effects/combineball.mdl";

    private bool _spent;

    public readonly Output OnExplode = new("OnExplode");

    public override void OnSpawn()
    {
        base.OnSpawn();

        // To the SHELL, because the reflection happens inside the step trace
        // where the surface normal is. See Physics.SetBounce.
        Physics.SetBounce(Handle, Vars.Bounces);

        // Its own clock, so a ball loose in a big room still goes away.
        SetNextThink(Vars.Lifetime);
    }

    /// <summary>
    /// A wall is not a hit, which is the divergence from every other projectile
    /// in this file.
    ///
    /// The base class ends the flight on any touch. This one hears about the
    /// same touches and deliberately ignores the world: the shell has already
    /// reflected it by the time this runs, so doing nothing here IS the bounce.
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (toucher == Shooter)
            return;

        if (toucher == EntityHandle.None)
            return;

        Finish(toucher);
    }

    public override void OnThink() => Finish(EntityHandle.None);

    private void Finish(EntityHandle hit)
    {
        if (_spent)
            return;

        _spent = true;
        Impact(hit);
        Remove();
    }

    protected override void Impact(EntityHandle hit)
    {
        OnExplode.Fire();

        // Only if it caught something. A ball that ran out of clock in mid-air
        // is the common case and must not be an error.
        if (hit != EntityHandle.None)
            Damage.Apply(hit, Attribution, Vars.Damage, DamageType.Blast);

        // At the ball's position, before it goes: the explosion is placed where
        // the entity is, and a removed entity is nowhere.
        // ignored: Handle -- an entity that explodes where it stands must say so,
        // or its own hull stops the very first trace of the traversal and every
        // victim in the radius is scored at distance zero. See Explosions.Fire.
        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        Origin, Vars.Magnitude, Vars.Radius, Attribution,
                        ignored: Handle, inflictor: Handle);
    }
}

/// <summary>
/// The map keys <see cref="EnergyBall"/> declares.
/// </summary>
[ScriptVars]
public partial class EnergyBallVars
{
    /// <summary>What it does to whatever it actually catches.</summary>
    public float Damage { get; set; } = 60f;

    public float Magnitude { get; set; } = 120f;

    public float Radius { get; set; } = 180f;

    /// <summary>
    /// How many walls it survives. Finite rather than endless: a ball that
    /// ricocheted forever would eventually find whoever fired it, and would
    /// never stop being something the server has to simulate.
    /// </summary>
    public int Bounces { get; set; } = 8;

    /// <summary>How long it lasts if nothing stops it.</summary>
    public float Lifetime { get; set; } = 4f;
}
