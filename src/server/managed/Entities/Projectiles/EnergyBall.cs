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
/// prop_combine_ball: the AR2's alt-fire.
///
/// Stock's combine ball, and the thing it does that no other projectile here
/// does is SURVIVE what it hits: it comes off walls, props and players alike and
/// keeps going, with no count of bounces (a weapon's ball never sets one), so a
/// shot in a small room stays a threat until its clock runs out.
///
/// <para>
/// Stock's two endings: the clock, and the sky. Neither hurts anybody: what kills
/// is being hit, stock's physics impact of 150 kg at 1000 u/s. The end is DoExplosion's
/// sound, shake and two rings; the entity stays half a second for them.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PropCombineBall, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.Sound, LaunchSound)]
[Precache(PrecacheKind.Sound, ImpactSound)]
[Precache(PrecacheKind.Sound, KillImpactSound)]
[Precache(PrecacheKind.Sound, ExplosionSound)]
public sealed class EnergyBall : FlyingProjectile
{
    private const string LaunchSound = "NPC_CombineBall.Launch";
    private const string ImpactSound = "NPC_CombineBall.Impact";
    private const string KillImpactSound = "NPC_CombineBall.KillImpact";
    private const string ExplosionSound = "NPC_CombineBall.Explosion";

    /// <summary>The rings' texture (s_nExplosionTexture).</summary>
    private const string RingSprite = "sprites/lgtning.vmt";

    /// <summary>The radius the rings start from: sk_weapon_ar2_alt_fire_radius.</summary>
    internal const float BallRadius = 10f;

    /// <summary>VPhysicsCollision's "only do the bounce so often".</summary>
    internal const float BounceInterval = 0.25f;

    /// <summary>playerLinearTable's last row, which 1000 u/s reaches.</summary>
    internal const float ImpactDamage = 500f;

    /// <summary>DoExplosion's SUB_Remove.</summary>
    internal const float RemoveDelay = 0.5f;

    /// <summary>FBEAM_FADEOUT.</summary>
    private const int BeamFadeOut = 0x8;

    /// <summary>SURF_SKY (bspflags.h).</summary>
    private const int SurfSky = 0x0004;

    public EnergyBall(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                      IEngineWorldQuery world, IEnginePrecache precache, IEngineSound sound,
                      ITempEnts temps, IScreenEffects screen)
        : base(physics, motion)
    {
        Damage = damage;
        World = world;
        Precache = precache;
        Sound = sound;
        Temps = temps;
        Screen = screen;
    }

    /// <summary>tracing: whether what it struck is the sky.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the rings' texture.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>its launch, bounces, hits and end.</summary>
    private readonly IEngineSound Sound;

    /// <summary>the rings.</summary>
    private readonly ITempEnts Temps;

    /// <summary>UTIL_ScreenShake.</summary>
    private readonly IScreenEffects Screen;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>What a map may set on this entity. See <see cref="EnergyBallVars"/>.</summary>
    [ScriptVars] public EnergyBallVars Vars = new();

    /// <summary>Flat. It is under its own power and does not fall.</summary>
    protected override float GravityScale => 0f;

    /// <summary>A weapon's ball has no m_nMaxBounces: only its clock ends it.</summary>
    protected override int Bounces => int.MaxValue;

    /// <summary>Stock's combine ball, so it looks like one without shipping a model.</summary>
    protected override string ProjectileModel => "models/effects/combineball.mdl";

    private bool _spent;
    private float _lastBounce = float.NegativeInfinity;

    public readonly Output OnExplode = new();

    public override void OnSpawn()
    {
        base.OnSpawn();

        // To the SHELL, because the reflection happens inside the step trace
        // where the surface normal is. See Physics.SetBounce.
        Physics.SetBounce(Handle, Bounces);

        Sound.EntityEmitSound(Handle, LaunchSound);

        // Its own clock, so a ball loose in a big room still goes away.
        SetNextThink(Vars.Lifetime);
    }

    /// <summary>DoImpactEffect's sky test, and the bounce's sound, so often.</summary>
    protected internal override void OnStruckSurface(Vector normal)
    {
        if (_spent)
            return;

        Vector origin = Origin;
        SurfaceTraceResult tr = World.TraceLineSurface(origin, origin - normal * 16f, TraceMask.Shot, Handle);
        if (tr.Fraction < 1f && (tr.SurfaceFlags & SurfSky) != 0)
        {
            Explode();
            return;
        }

        if (GameTime - _lastBounce < BounceInterval)
            return;

        _lastBounce = GameTime;
        Sound.EntityEmitSound(Handle, ImpactSound);
    }

    /// <summary>
    /// OnHitEntity: whatever it caught takes the hit, and the ball goes on. The
    /// shell has already reflected it by the time this runs, so the world, which
    /// names nothing, needs nothing here.
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        // A volume is no solid: stock's ball never collides with one, and dm_lockdown
        // wraps its spawn points in trigger_multiple. The world is IsHittableEntity's first no.
        if (_spent || toucher == Shooter || toucher == EntityHandle.None || PassesThrough(toucher)
            || Entities.GetIndex(toucher) == 0)
            return;

        if (Entities.IsPlayer(toucher))
            Sound.EntityEmitSound(Handle, KillImpactSound);
        Damage.Apply(toucher, Attribution, Vars.Damage, DamageType.Crush | DamageType.Dissolve);
    }

    /// <summary>The clock: the end, then half a second on for it to be heard.</summary>
    public override void OnThink()
    {
        if (_spent)
            Remove();
        else
            Explode();
    }

    /// <summary>DoExplosion, with no blast: stock's hurts nobody.</summary>
    private void Explode()
    {
        _spent = true;
        OnExplode.Fire();

        Vector at = Origin;
        Motion.Stop(Handle);
        SetNotSolid(true);

        Sound.EntityEmitSound(Handle, ExplosionSound);
        Screen.Shake(at, 20f, 150f, 1f, 1250f, ShakeCommand.Start);

        int sprite = Precache.PrecacheModel(RingSprite);
        Temps.BeamRingPoint(TeFilters.Broadcast, 0f, at, BallRadius, 1024f, Ring(sprite, 0.2f, 32), BeamFadeOut);
        Temps.BeamRingPoint(TeFilters.Broadcast, 0f, at, BallRadius, 1024f, Ring(sprite, 0.5f, 64), BeamFadeOut);

        SetNextThink(RemoveDelay);
    }

    private static TeBeamLook Ring(int sprite, float life, int alpha)
        => new(sprite, 0, 0, 2, life, 64f, 64f, 0, 0f, 255, 255, 225, alpha, 0);

    /// <summary>The flight never ends on a hit, so nothing calls this.</summary>
    protected override void Impact(EntityHandle hit) { }
}

/// <summary>
/// The map keys <see cref="EnergyBall"/> declares.
/// </summary>
[ScriptVars]
public partial class EnergyBallVars
{
    /// <summary>What it does to whatever it catches. See <see cref="EnergyBall.ImpactDamage"/>.</summary>
    public float Damage { get; set; } = EnergyBall.ImpactDamage;

    /// <summary>How long it lasts: sk_weapon_ar2_alt_fire_duration.</summary>
    public float Lifetime { get; set; } = 4f;
}
