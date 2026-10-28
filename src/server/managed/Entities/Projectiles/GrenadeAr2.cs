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
/// grenade_ar2: the SMG1's alt-fire, stock's CGrenadeAR2 (grenade_ar2.cpp).
///
/// <para>
/// A contact grenade: it flies under a lighter gravity than the world's
/// (UTIL_ScaleForGravity( 400 )), tumbling, trailing dark smoke, and goes off
/// on the first solid thing it touches -- a fireball temp entity, a scorch
/// along its flight, a shake, and the blast credited to whoever fired it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.GrenadeAr2, ScriptedEntityKind.Projectile)]
public sealed class GrenadeAr2 : FlyingProjectile
{
    public GrenadeAr2(IEngineEntityPhysics physics, EntityMotion motion, IEngineWorldQuery world,
                      IEnginePrecache precache, ITempEnts temps, IServerDecals decals, IBlastSweep blast,
                      IScreenEffects screen, IEngineHost host)
        : base(physics, motion)
    {
        World = world;
        Precache = precache;
        Temps = temps;
        Decals = decals;
        Blast = blast;
        Screen = screen;
        Host = host;
    }

    /// <summary>the scorch's trace.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball's sprite.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>the explosion and the decal temp entities.</summary>
    private readonly ITempEnts Temps;

    /// <summary>the decal system, whose groups the scorch is picked from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>RadiusDamage, managed.</summary>
    private readonly IBlastSweep Blast;

    /// <summary>UTIL_ScreenShake.</summary>
    private readonly IScreenEffects Screen;

    /// <summary>the engine's random stream, for the tumble.</summary>
    private readonly IEngineHost Host;

    /// <summary>What a map may set on this entity. See <see cref="GrenadeAr2Vars"/>.</summary>
    [ScriptVars] public GrenadeAr2Vars Vars = new();

    /// <summary>UTIL_ScaleForGravity( 400 ) under HL2MP's sv_gravity of 600.</summary>
    protected override float GravityScale => 400f / 600f;

    protected override string ProjectileModel => "models/Weapons/ar2_grenade.mdl";

    /// <summary>CGrenadeAR2::Spawn's UTIL_SetSize: a six-unit box.</summary>
    protected override Vector HullExtent => new(3f, 3f, 3f);

    /// <summary>SetLocalAngularVelocity( RandomAngle( -400, 400 ) ), from the SMG1's SecondaryAttack.</summary>
    protected override Vector LaunchSpin
        => new(Host.RandomFloat(-400f, 400f), Host.RandomFloat(-400f, 400f), Host.RandomFloat(-400f, 400f));

    private const string Fireball = "sprites/zerogxplode.vmt";
    private const string SmallScorch = "SmallScorch";

    private EntityHandle _smoke = EntityHandle.None;

    public readonly Output OnExplode = new();

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Hits everything but debris.
        Physics.SetCollisionGroup(Handle, CollisionGroup.Projectile);

        // SmokeTrail::CreateSmokeTrail, following the grenade, emitting for ten
        // seconds: dark puffs a second long, twelve units growing to forty-eight.
        _smoke = Entities.Create(EntityNames.EnvSmoketrail);
        if (_smoke == EntityHandle.None || !Entities.IsValid(_smoke))
        {
            _smoke = EntityHandle.None;
            return;
        }

        // Stock's numbers, in the keys env_smoketrail reads (colours in 0-255).
        (string Key, string Value)[] keys =
        [
            ("spawnrate", "48"), ("lifetime", "1"), ("startcolor", "25.5 25.5 25.5"), ("endcolor", "0 0 0"),
            ("startsize", "12"), ("endsize", "48"), ("spawnradius", "4"), ("minspeed", "4"), ("maxspeed", "24"),
            ("opacity", "0.2"), ("emittime", "10"),
        ];
        foreach (var (key, value) in keys)
            Entities.SetKeyValue(_smoke, key, value);
        Entities.SetOrigin(_smoke, Origin);
        Entities.Spawn(_smoke);
        Entities.SetParent(_smoke, Handle);
    }

    /// <summary>CGrenadeAR2::Detonate, at the first solid thing it meets.</summary>
    protected override void Impact(EntityHandle hit)
    {
        OnExplode.Fire();
        RemoveSmoke();

        Vector at = Origin;
        Temps.Explosion(TeFilters.Pas(at), 0f, at, Precache.PrecacheModel(Fireball), 2.0f, 15, 0,
                        (int)Vars.Radius, (int)Vars.Magnitude);

        // A scorch sixty units along the way it was flying: the world's, or a
        // small one on anything else.
        Vector forward = Motion.IncomingVelocityOf(Handle).Normalized;
        TraceResult tr = World.TraceLine(at, at + forward * 60f, TraceMask.Shot, Handle);
        if (tr.Fraction != 1f)
        {
            int index = tr.Entity == EntityHandle.None ? 0 : Entities.GetIndex(tr.Entity);
            int decal = Decals.IndexForName(index == 0 ? EnvExplosion.ScorchDecal : SmallScorch);
            if (decal >= 0)
                Temps.Decal(TeFilters.Broadcast, 0f, tr.End, at, index, 0, decal);
        }

        Screen.Shake(at, 25f, 150f, 1f, 750f, ShakeCommand.Start);

        Blast.Apply(at, Vars.Magnitude, Vars.Radius, DamageType.Blast,
                    attacker: Shooter != EntityHandle.None && Entities.IsValid(Shooter) ? Shooter : Handle,
                    inflictor: Handle, ignored: Handle);
    }

    /// <summary>The trail goes with it, if it is still here.</summary>
    public override void OnRemoved() => RemoveSmoke();

    private void RemoveSmoke()
    {
        if (_smoke != EntityHandle.None && Entities.IsValid(_smoke))
            Entities.Remove(_smoke);
        _smoke = EntityHandle.None;
    }
}

/// <summary>
/// The map keys <see cref="GrenadeAr2"/> declares: the SMG1 sets them
/// (SMG1_GRENADE_DAMAGE and SMG1_GRENADE_RADIUS, weapon_smg1.cpp).
/// </summary>
[ScriptVars]
public partial class GrenadeAr2Vars
{
    public float Magnitude { get; set; } = 100f;

    public float Radius { get; set; } = 250f;
}
