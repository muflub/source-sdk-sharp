//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Projectiles;
using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Player;

namespace TF2.Server.Projectiles;

/// <summary>
/// Stock's classnames (<c>LINK_ENTITY_TO_CLASS</c>): the rocket
/// (<c>tf_projectile_rocket.cpp:18</c>), the demoman's two pipebombs
/// (<c>tf_weapon_grenade_pipebomb.cpp:406-409</c>), the medic's syringe
/// (<c>tf_projectile_nail.cpp:31</c>), the huntsman's arrow
/// (<c>tf_projectile_arrow.cpp:53</c>) and the pyro's flare
/// (<c>tf_projectile_flare.cpp:24</c>) and the Wrap Assassin's ornament
/// (<c>tf_projectile_ball_ornament</c>, <c>tf_weapon_bat.cpp:119</c>).
/// </summary>
public static class TFProjectileEntityNames
{
    public const string Rocket = "tf_projectile_rocket";
    public const string Pipebomb = "tf_projectile_pipe";
    public const string Stickybomb = "tf_projectile_pipe_remote";
    public const string Syringe = "tf_projectile_syringe";
    public const string Arrow = "tf_projectile_arrow";
    public const string HealingBolt = "tf_projectile_healing_bolt";
    public const string Flare = "tf_projectile_flare";
    public const string Jar = "tf_projectile_jar";
    public const string JarMilk = "tf_projectile_jar_milk";
    public const string JarGas = "tf_projectile_jar_gas";
    public const string Cleaver = "tf_projectile_cleaver";
    public const string BallOfFire = "tf_projectile_balloffire";
    public const string BallOrnament = "tf_projectile_ball_ornament";
    public const string EnergyRing = "tf_projectile_energy_ring";
    public const string EnergyBall = "tf_projectile_energy_ball";
    public const string MechanicalArmOrb = "tf_projectile_mechanicalarmorb";
}

/// <summary>
/// The rocket (<c>CTFProjectile_Rocket</c>, <c>tf_projectile_rocket.cpp</c> over
/// <c>shared/tf/tf_weaponbase_rocket.cpp</c>): straight flight at the
/// launcher's speed, and on its first touch a blast at the stock radius
/// (<see cref="TFProjectileDefs.RocketRadius"/>) with the attacker's own pass
/// at the rocket-jump radius (<see cref="TFProjectileDefs.RocketRadiusForRocketJumps"/>).
///
/// <para>
/// The flight itself is the core's <see cref="FlyingProjectile"/>: it flies
/// flat (no gravity), never hits its own shooter, and is spent on the first
/// touch — stock's <c>CTFBaseRocket::Explode</c> is reached once, the same
/// way.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Rocket, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.ParticleSystem, "mvm_soldier_shockwave")]
public sealed class TFRocket : FlyingProjectile
{
    /// <summary><c>CTFBaseRocket::PhysicsSolidMaskForEntity</c> (tf_weaponbase_rocket.cpp:354-369):
    /// no <c>CONTENTS_HITBOX</c>, so the sweep finds a player's hull. A hitbox ray let
    /// a rocket through a spy's or a scout's arm's width of a heavy's.</summary>
    protected override TraceMask SweepMask => TraceMask.Solid;

    private readonly TFBlastApplier Blast;
    private readonly ITFWeaponOwners Owners;
    private readonly IEnginePlayers Players;
    private readonly AttributeHooks Attributes;
    private readonly TFPlayerCarriers Carriers;
    private readonly ITempEnts Temps;
    private readonly IEnginePrecache Precache;
    private readonly ITFPlayerConditions Conditions;

    public TFRocket(IEngineEntityPhysics physics, EntityMotion motion, TFBlastApplier blastApplier,
                    ITFWeaponOwners owners, IEnginePlayers players, AttributeHooks attributes,
                    TFPlayerCarriers carriers, ITempEnts temps, IEnginePrecache precache,
                    ITFPlayerConditions conditions)
        : base(physics, motion)
    {
        Blast = blastApplier;
        Owners = owners;
        Players = players;
        Attributes = attributes;
        Carriers = carriers;
        Temps = temps;
        Precache = precache;
        Conditions = conditions;
    }

    protected override float GravityScale => 0f;

    protected override string ProjectileModel => TFProjectileDefs.RocketModel;

    /// <summary>The shot the launcher dealt, told to the blast and to the damage layer.</summary>
    public float Damage { get; private set; }
    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>
    /// What the touch hit — stock's <c>m_hEnemy</c>, set on the impact before
    /// anything reads it (<c>CTFBaseRocket::Explode</c>,
    /// <c>tf_weaponbase_rocket.cpp:426</c>): the direct-hit test of the
    /// radius, the stun and the falloff skip all ask it.
    /// </summary>
    public EntityHandle DirectVictim { get; private set; }

    /// <summary>
    /// <c>CTFBaseRocket::GetDeflected</c> (<c>tf_weaponbase_rocket.h:58</c>,
    /// stock's <c>m_iDeflected</c> count): whether a deflection ever moved
    /// this rocket — the reflected-blast arm's question
    /// (<c>tf_gamerules.cpp:6159</c>). Stock counts deflections; every reader
    /// here asks truthiness, so the flag is the count's answer.
    /// </summary>
    public bool Deflected { get; private set; }

    /// <summary>The Air Strike's blast-radius penalty while its holder rocket jumps (:611).</summary>
    private const float AirStrikeJumpRadiusScale = 0.80f;

    /// <summary>What the client's trail mirror reads.</summary>
    [NetState] public TFRocketNetState State = new();

    private int _ownerIndex;

    /// <summary>The launcher's deal: what the blast is worth and whose it is.</summary>
    public void ArmedBy(float damage, TFWeaponId weapon, string weaponName, bool critical, int ownerIndex)
    {
        Damage = damage;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        _ownerIndex = ownerIndex;

        // The client's CreateTrails reads the team off the projectile; here
        // the owner's team rides the row, with the owner and launcher its
        // trail arms ask for (stock's networked m_hOwnerEntity and m_hLauncher).
        State.Critical = critical;
        State.TeamNumber = Team();
        State.OwnerIndex = ownerIndex;
        State.WeaponName = weaponName;
    }

    /// <summary>
    /// Deflected (<c>CTFProjectile_Rocket::Deflected</c>,
    /// <c>tf_projectile_rocket.cpp:147-181</c>): sent back at its old speed
    /// along the blast's direction, owned — and so, through its owner, teamed —
    /// by the deflector, and critical only if they were crit boosted. Stock's
    /// skin change (:180) has no model side here to read it.
    /// </summary>
    public void DeflectBy(EntityHandle deflector, Vector velocity, bool deflectorCritBoosted)
    {
        Steer(velocity, GravityScale);
        Retarget(deflector);
        _ownerIndex = Entities.GetIndex(deflector);
        Deflected = true;
        if (deflectorCritBoosted)
            Critical = true;

        // The retarget is the client's re-create trigger: stock's
        // OnDataChanged sees the deflection and re-runs CreateTrails. The
        // launcher follows the deflector's active weapon, as stock's
        // Deflected sets it (:154) beside the owner (:165).
        WeaponName = Players.GetActiveWeapon(deflector);
        State.TeamNumber = Team();
        State.Critical = Critical;
        State.OwnerIndex = _ownerIndex;
        State.WeaponName = WeaponName;
    }

    /// <summary>The owner's team, through the engine's handle for the index.</summary>
    private int Team() => Players.GetTeam(Players.GetHandle(_ownerIndex));

    protected override void Impact(EntityHandle hit)
    {
        // CTFBaseRocket::Explode (tf_weaponbase_rocket.cpp:426) sets the
        // enemy to what was touched before GetRadius reads it, so the direct
        // hit is the touch; the stun check rides between the radius
        // (:496) and the blast (:509) as stock orders them.
        DirectVictim = hit;
        float radius = Radius(hit);
        StunOnImpact(hit);
        ExplodeAt(Origin, radius, Damage,
                  selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: Damage,
                  fullDamage: Damage, directVictim: hit);
    }

    /// <summary>
    /// CTFBaseRocket::CheckForStunOnImpact (<c>tf_weaponbase_rocket.cpp:530-559</c>)
    /// over <c>GetStunLevel</c> (:564-573): the direct hit's victim — stock's
    /// <c>ToTFPlayer(GetEnemy())</c> — has its feet stopped
    /// (<c>SetAbsVelocity(vec3_origin)</c>) and eats the owner's level of
    /// stun, RemapValClamped(level, 1, 4, 0.5, 0.75) seconds at full strength
    /// (<c>IsMiniBoss</c>'s 0.85 is MvM's), TF_STUN_MOVEMENT |
    /// TF_STUN_NO_EFFECTS, dealt by the owner player. The MvM achievement is
    /// that mode's (docs/divergences.md); the shockwave particle below rides
    /// every stun, as stock's :556-558 does.
    /// </summary>
    private void StunOnImpact(EntityHandle victim)
    {
        int specialist = Attributes.Int(0, "rocket_specialist", Owners.ItemOf(_ownerIndex, WeaponName));
        if (specialist == 0 || !IsDirectHitOnAnEnemy(victim))
            return;

        Entities.SetVelocity(victim, default);
        Carriers.Part<TFStunStack>(victim.EntryIndex)?.Stun(victim, RemapValClamped(specialist, 1f, 4f, 0.5f, 0.75f),
                                                            StunAmount, TFStunFlags.Movement | TFStunFlags.NoEffects,
                                                            Players.GetHandle(_ownerIndex));

        // The effect (:556-558): CPVSFilter at GetAbsOrigin, the world-position
        // ParticleEffect dispatch — no entity rides the record, so Start
        // repeats the origin and the angles stand at zero (QAngle(0, 0, 0)).
        Vector at = Origin;
        TeEffectData shockwave = new()
        {
            Scale = 1f,
            HitBox = Precache.ParticleSystemIndex(StunEffect),
            Origin = at,
            Start = at,
        };
        Temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in shockwave);
    }

    /// <summary>Full strength, the non-MvM arm of CheckForStunOnImpact's <c>flStunAmount</c> (:544).</summary>
    private const float StunAmount = 1f;

    /// <summary>The stun's marker, stock's :558 string.</summary>
    private const string StunEffect = "mvm_soldier_shockwave";

    /// <summary>
    /// CTFBaseRocket::GetRadius (<c>tf_weaponbase_rocket.cpp:579-617</c>):
    /// the stock 146 under the launcher's mult_explosion_radius, and — for a
    /// rocket specialist — a direct hit on an enemy combatant, which widens
    /// the blast by RemapValClamped(level, 1, 4, 1.15, 1.6) and arms the
    /// stun (<c>m_bStunOnImpact</c>, <see cref="StunOnImpact"/>). Stock reads
    /// mult_explosion_radius off the launcher item and rocket_specialist off
    /// the owner player, whose attribute manager reaches the item — the port
    /// asks the owner's item under this weapon's name for both. The
    /// airstrike arm (:602-613): an owner in the blast-jump condition whose
    /// item keys the Air Strike (rocketjump_attackrate_bonus answering
    /// anything but one) takes 0.80 of the radius.
    /// </summary>
    private float Radius(EntityHandle directVictim)
    {
        IAttributeProvider? item = Owners.ItemOf(_ownerIndex, WeaponName);
        float radius = Attributes.Float(TFProjectileDefs.RocketRadius, "mult_explosion_radius", item);

        int specialist = Attributes.Int(0, "rocket_specialist", item);
        if (specialist != 0 && IsDirectHitOnAnEnemy(directVictim))
            radius *= RemapValClamped(specialist, 1f, 4f, 1.15f, 1.6f);

        // :602-613, the Air Strike's own blast while rocket jumping.
        if (Conditions.InCond(Players.GetHandle(_ownerIndex), TFCond.Blastjumping)
            && Attributes.Float(1f, "rocketjump_attackrate_bonus", item) != 1f)
            radius *= AirStrikeJumpRadiusScale;

        return radius;
    }

    /// <summary>
    /// GetEnemy()'s team test and its IsPlayer/MyCombatCharacterPointer: the
    /// combat characters this port builds are its players.
    /// </summary>
    private bool IsDirectHitOnAnEnemy(EntityHandle victim)
    {
        if (victim == EntityHandle.None || !Entities.IsPlayer(victim))
            return false;

        return Players.GetTeam(victim) != Team();
    }

    /// <summary>mathlib.cpp's RemapValClamped.</summary>
    private static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float factor = Math.Clamp((val - a) / (b - a), 0f, 1f);
        return c + (d - c) * factor;
    }

    private void ExplodeAt(Vector at, float radius, float damage, float selfRadius, float selfBaseDamage,
                           float fullDamage, EntityHandle directVictim)
    {
        // The blast is placed before the entity goes: a removed one is nowhere.
        // Its bits are the launcher's own (g_aWeaponDamageTypes): the rocket's
        // blast falls off by half.
        Blast.Apply(at, TFBlast.Of(fullDamage, radius, TFWeaponDamageTypes.Of(Weapon)), Attribution, Handle,
                    ignored: Handle, Weapon, WeaponName, Critical, directVictim,
                    selfRadius, selfBaseDamage);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Rocket} exploded at {at}\n");
        // The removal is the touch's (FlyingProjectile.OnTouch removes after
        // Impact): an explosion of its own would remove the shell twice.
    }
}

/// <summary>
/// The demoman's pipebomb (<c>CTFGrenadePipebombProjectile</c>'s
/// <c>TF_GL_MODE_REGULAR</c> arm, <c>tf_weapon_grenade_pipebomb.cpp:493-537</c>):
/// a physics prop at the pipebomb gravity
/// (<see cref="TFProjectileDefs.PipebombGravity"/>) that explodes on the fuse
/// (<see cref="TFProjectileDefs.PipeFuse"/>) at the timer's reduced damage, or
/// on touching an enemy at the full one (<c>PipebombTouch</c>, :721-842: the
/// damage is restored to <c>m_flFullDamage</c> before that blast). Its own
/// thrower never sets it off (:723).
///
/// <para>
/// The bounce is the prop shell's, as the core <see cref="Grenade"/>'s is;
/// stock's <c>FSOLID_TRIGGER</c> (:533) is why touch reaches it at all.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Pipebomb, ScriptedEntityKind.Prop)]
public sealed class TFPipebomb : ScriptedEntity
{
    /// <summary><c>SURF_SKY</c> (<c>bspflags.h:82</c>), as Bolt and the core rocket name it.</summary>
    private const int SurfSky = 0x0004;

    private readonly IEngineEntityPhysics Physics;
    private readonly TFBlastApplier Blast;
    private readonly IEnginePlayers Players;
    private readonly IEntityDirectory Directory;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Attributes;
    private readonly IEngineWorldQuery World;
    private readonly IServerSkins Skins;
    private readonly IGameClock Clock;
    private readonly IEngineFrame Frame;
    private readonly IEngineDamage Damage;
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly TFPlayerCarriers Carriers;
    private readonly ITFPlayerConditions Conditions;

    public TFPipebomb(IEngineEntityPhysics physics, TFBlastApplier blast, IEnginePlayers players,
                      IEntityDirectory directory, ITFWeaponOwners owners, AttributeHooks attributes,
                      IEngineWorldQuery world, IServerSkins skins, IGameClock clock, IEngineFrame frame,
                      IEngineDamage damage, TFDamageScopes scopes, DamageHitGroups hitGroups,
                      TFPlayerCarriers carriers, ITFPlayerConditions conditions)
    {
        Physics = physics;
        Blast = blast;
        Players = players;
        Directory = directory;
        Owners = owners;
        Attributes = attributes;
        World = world;
        Skins = skins;
        Clock = clock;
        Frame = frame;
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Carriers = carriers;
        Conditions = conditions;
    }

    /// <summary>The model the prop shell carries (<c>TF_WEAPON_PIPEGRENADE_MODEL</c>).</summary>
    public const string Model = TFProjectileDefs.PipeModel;

    public float FullDamage { get; private set; }
    public float Radius { get; private set; }
    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>
    /// Stock's <c>m_iType</c> (<c>SetPipebombMode</c>,
    /// <c>tf_weapon_grenade_pipebomb.cpp:575-578</c>): the grenade mode the
    /// throw stamped — the regular pipe, or the Loose Cannon's
    /// <see cref="TFProjectileDefs.GrenadeModeCannonball"/>, whose shell
    /// (<c>Spawn</c>, :513-516), impact (<see cref="CannonballPush"/>) and
    /// blast bits (<c>GetWeaponID</c>, :135-143) it picks.
    /// </summary>
    public int PipebombMode { get; private set; }

    /// <summary>
    /// Stock's <c>m_bWallShatter</c>: the launcher's <c>set_detonate_mode</c>
    /// AIR (<c>tf_weapon_pipebomblauncher.h:25</c>) poked onto the pipe at
    /// <c>FireProjectileInternal</c> (<c>tf_weapon_grenadelauncher.cpp:272-275</c>) —
    /// the Loch-n-Load's pipe shatters on its first collision instead of
    /// blowing up or waiting for the fuse.
    /// </summary>
    public bool WallShatter { get; private set; }

    /// <summary>
    /// Stock's <c>m_iDeflected</c> (<c>tf_weapon_grenade_pipebomb.cpp:125</c>):
    /// how many deflections moved this pipe — the reflected-blast arm's
    /// question (<c>tf_gamerules.cpp:6159</c>). A pipe's count never resets;
    /// only the sticky's does (<see cref="TFStickybomb"/>).
    /// </summary>
    public int DeflectedCount { get; private set; }

    private EntityHandle _thrower = EntityHandle.None;
    private bool _spent;
    private int _ownerIndex;
    private bool _touched;
    private float _createdAt;

    /// <summary>Stock's <c>m_penetratedEntities</c> (:158): the victims the cannonball pushed.</summary>
    private readonly List<EntityHandle> _penetrated = [];

    // The cannonball impact's numbers (PipebombTouch :781-783, :794, :799-800):
    // the damage ramp's ends, the shove and its lift, the ground stun.
    private const float NearDistanceSq = 512f * 512f;
    private const float FarDistanceSq = 1024f * 1024f;
    private const float ImpactNear = 50f;
    private const float ImpactFar = 25f;
    private const float PushSpeed = 400f;
    private const float PopSpeed = 350f;
    private const float StunTime = 0.5f;
    private const float StunStrength = 1f;

    /// <summary>
    /// The damage the fuse will carry: the throw's full damage times
    /// <c>TF_WEAPON_PIPEBOMB_TIMER_DMG_REDUCTION</c> (Create, :476), less the
    /// world contact's scaling (<see cref="OnTouch"/>); an explode on an
    /// enemy restores the full damage (:824), as stock's m_flDamage does.
    /// </summary>
    private float _fuseDamage;

    public override bool TouchedByAnything => true;

    /// <summary>What the client's trail mirror reads.</summary>
    [NetState] public TFPipebombNetState State = new();

    public override void OnSpawn()
    {
        SetModel(Model);
        Physics.SetGravity(Handle, TFProjectileDefs.PipebombGravity);
        Physics.SetFriction(Handle, TFProjectileDefs.PipebombFriction);
    }

    /// <summary>
    /// The thrower's deal: full damage, radius, fuse — and the grenade mode
    /// (<see cref="Mode"/>), whose cannonball re-shells the pipe stock's
    /// <c>Spawn</c> arms (<c>tf_weapon_grenade_pipebomb.cpp:513-516</c>).
    /// </summary>
    public void ThrownBy(EntityHandle thrower, Vector velocity, float fullDamage, float radius,
                         TFWeaponId weapon, string weaponName, bool critical,
                         int grenadeMode = TFProjectileDefs.GrenadeModeRegular)
    {
        _thrower = thrower;
        FullDamage = fullDamage;
        Radius = radius;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        PipebombMode = grenadeMode;
        if (grenadeMode == TFProjectileDefs.GrenadeModeCannonball)
            SetModel(TFProjectileDefs.CannonballModel);
        _ownerIndex = Entities.GetIndex(thrower);
        _createdAt = Clock.Now; // m_flCreationTime, as the sticky's (its ThrownBy)
        _fuseDamage = TFProjectileDefs.PipeTimerDamageScale * fullDamage;

        // FireProjectileInternal's shatter poke (:272-275), read off the
        // launcher item as stock's GetDetonateMode does
        // (tf_weapon_grenadelauncher.cpp:481-486).
        IAttributeProvider? item = Owners.ItemOf(_ownerIndex, WeaponName);
        WallShatter = Attributes.Int(TFProjectileDefs.DetonateModeStandard, "set_detonate_mode", item)
                      == TFProjectileDefs.DetonateModeAir;

        // The client's CreateTrailParticles reads the team off the projectile.
        State.Critical = critical;
        State.TeamNumber = Players.GetTeam(thrower);

        Entities.SetOwner(Handle, thrower);
        Entities.SetVelocity(Handle, velocity);

        // Spawn's fuse booking (tf_weapon_grenade_pipebomb.cpp:521) through
        // SetDetonateTimerLength (tf_weaponbase_grenadeproj.cpp:481-490): the
        // 2 s scale under the launcher's fuse_mult — the Iron Bomber's 0.7.
        SetNextThink(TFProjectileDefs.PipeFuse * Attributes.Float(1f, "fuse_mult",
                                                                Owners.ItemOf(_ownerIndex, WeaponName)));
    }

    /// <summary>
    /// <c>FireProjectileInternal</c>'s charged fuse
    /// (<c>tf_weapon_grenadelauncher.cpp:277-287</c>): the held shot
    /// overrides the spawn's timer with the window it has left, through the
    /// same <c>SetDetonateTimerLength</c> — the more of the charge spent, the
    /// sooner the ball goes. At none the held-nothing arm's trace-and-explode
    /// fires (<c>:281-286</c>): stock traces to the eye and detonates where a
    /// wall stands between; the ball was born at the muzzle, so here it goes
    /// off where it stands.
    /// </summary>
    public void ChargedFuse(float remaining)
    {
        if (remaining > 0f)
        {
            SetNextThink(remaining * Attributes.Float(1f, "fuse_mult",
                                                    Owners.ItemOf(_ownerIndex, WeaponName)));
            return;
        }

        _spent = true;
        Explode(_fuseDamage, EntityHandle.None);
    }

    /// <summary>
    /// <c>CTFWeaponBaseGrenadeProj::VPhysicsUpdate</c>'s server branch
    /// (<c>tf_weaponbase_grenadeproj.cpp:716-795</c>), which stock shares
    /// between both bombs: the ray one tick ahead stops the pipe at closed
    /// doors and other brushes the solver's pair table does not, reflecting
    /// the velocity off the hit normal (:781), absorbing
    /// <see cref="TFProjectileDefs.PipebombElasticity"/> (:784), an enemy hit
    /// half again (:786-789), the angular velocity reversed and halved
    /// (:791). Same gate as the sticky's: a named enemy, or a teammate past
    /// <see cref="CanCollideWithTeammates"/>; the thrower is a miss.
    /// </summary>
    public override void OnPhysicsUpdate()
    {
        if (_spent)
            return;

        Vector velocity = Entities.GetVelocity(Handle);
        Vector start = Origin;
        TraceResult hit = World.TraceLine(start, start + velocity * Frame.GetFrameTime(),
                                          TraceMask.Shot, Handle);

        if (hit.Entity == EntityHandle.None || hit.Entity == _thrower)
            return;

        int myTeam = Entities.IsValid(_thrower) ? Players.GetTeam(_thrower) : 0;
        int hitTeam = Players.GetTeam(hit.Entity);
        bool hitEnemy = hitTeam != 0 && myTeam != 0 && hitTeam != myTeam;
        bool hitFriendly = hitTeam == myTeam && CanCollideWithTeammates;

        if (!hitEnemy && !hitFriendly)
            return;

        // reflect velocity around normal (:781), then absorb 55% (:784).
        Vector reflected = -2f * hit.Normal * velocity.Dot(hit.Normal) + velocity;
        reflected *= TFProjectileDefs.PipebombElasticity;

        if (hitEnemy)
            reflected *= 0.5f;

        Entities.SetVelocity(Handle, reflected);
        Entities.SetAngularVelocity(Handle, Entities.GetAngularVelocity(Handle) * -0.5f);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} bounced off " +
                $"{Entities.GetClassName(hit.Entity)} at {Origin}\n");
    }

    /// <summary>
    /// CBaseProjectile::CanCollideWithTeammates
    /// (<c>baseprojectile.h:53</c>): false for the first
    /// <c>GetCollideWithTeammatesDelay</c>
    /// (<see cref="TFProjectileDefs.CollideWithTeammatesDelay"/>) of life, as
    /// the sticky reads it.
    /// </summary>
    private bool CanCollideWithTeammates
        => Clock.Now - _createdAt >= TFProjectileDefs.CollideWithTeammatesDelay;

    /// <summary>
    /// Deflected's regular-pipe arm (<c>tf_weapon_grenade_pipebomb.cpp:1246-1257</c>),
    /// reached from the airblast's <c>DeflectEntity</c>: the deflector takes
    /// the pipe — team, thrower and launcher, the item the world contact's
    /// scaling reads (<see cref="WorldContactDamageScale"/>) following
    /// (<c>:1248-1249</c>) — and their crit boost rides (<c>:1253-1256</c>).
    /// The count is IncrementDeflected's (<c>:1267</c>), whose skin re-tints
    /// to the new team (<c>:1094-1097</c>). The owner entity is stock's
    /// deliberate keep (<c>:1919-1926</c>: grenades are owned through the
    /// thrower, so the deflection leaves it).
    /// </summary>
    public void DeflectedBy(EntityHandle deflector, bool deflectorCritBoosted, string launcherName)
    {
        _thrower = deflector;
        _ownerIndex = Entities.GetIndex(deflector);
        WeaponName = launcherName;
        if (deflectorCritBoosted)
            Critical = true;
        DeflectedCount++;

        int team = Players.GetTeam(deflector);
        State.Critical = Critical;
        State.TeamNumber = team;
        Skins.SetSkin(Handle, team == (int)TFTeam.Blue ? 1 : 0);
    }

    /// <summary>The fuse: the blast at the timer's reduced damage (<c>:476</c>).</summary>
    public override void OnThink()
    {
        if (_spent)
            return;
        _spent = true;
        Explode(_fuseDamage, directVictim: EntityHandle.None);
    }

    public override void OnTouch(EntityHandle toucher)
    {
        // Never the thrower (:723), never twice.
        if (_spent || toucher == _thrower)
            return;

        // Handle hitting skybox (disappear), PipebombTouch :730-742: a trace
        // along the velocity — from 32 behind the pipe to 32 ahead, MASK_SOLID
        // with the pipe itself ignored — and when it lands on the skybox the
        // pipe is simply gone: UTIL_Remove, no explode, no blast.
        Vector velocity = Entities.GetVelocity(Handle);
        float speed = velocity.Length;
        if (speed > 0f)
        {
            Vector dir = velocity.Normalized;
            Vector spot = Origin - dir * 32f;
            SurfaceTraceResult tr = World.TraceLineSurface(spot, spot + dir * 64f, TraceMask.Solid, Handle);
            if (tr.Fraction < 1f && (tr.SurfaceFlags & SurfSky) != 0)
            {
                _spent = true;
                Remove();
                return;
            }
        }

        // World contact (the port's model of VPhysicsCollision :883-885): the
        // first one scales the fuse's stored damage the way stock's
        // SetDamage(GetDamageScaleOnWorldContact() * GetDamage()) does, and
        // marks m_bTouched — after that the pipe no longer explodes on
        // contact (:753-754); it waits for its fuse.
        if (toucher == EntityHandle.None)
        {
            // The wall shatter (VPhysicsCollision :859-864): the armed pipe's
            // first collision is Fizzle then Detonate, which land in the
            // m_bFizzle arm (:606-617) — sparks and gone, no blast. The
            // sparks are the client's particles; the port's fizzle is the
            // removal.
            if (WallShatter)
            {
                _spent = true;
                Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} shattered at {Origin}\n");
                Remove();
                return;
            }

            if (!_touched)
            {
                _touched = true;
                _fuseDamage *= WorldContactDamageScale();

                // VPhysicsCollision's no-bounce arm (:887-899): the launcher's
                // grenade_no_bounce — the Iron Bomber's — sinks the first
                // bounce to a tenth of the velocity, the spin untouched.
                if (Attributes.Int(0, "grenade_no_bounce", Owners.ItemOf(_ownerIndex, WeaponName)) == 1)
                    Entities.SetVelocity(Handle, Entities.GetVelocity(Handle) * 0.1f);
            }
            return;
        }

        // If we already touched a surface we're not exploding on contact anymore.
        if (_touched)
            return;

        // Blow up if we hit an enemy we can damage (:759): the full damage is
        // restored first (:824), because the fuse would have taken 40% off —
        // unless the cannonball's push takes the touch first (:772-817).
        int toucherTeam = Players.GetTeam(toucher);
        int myTeam = Entities.IsValid(_thrower) ? Players.GetTeam(_thrower) : 0;
        if (toucherTeam != 0 && myTeam != 0 && toucherTeam != myTeam && Hurtable(toucher))
        {
            if (PipebombMode == TFProjectileDefs.GrenadeModeCannonball && CannonballPush(toucher))
                return;

            _spent = true;
            Explode(FullDamage, directVictim: toucher);
        }
    }

    /// <summary>
    /// The cannonball's push, stock's impact numbers
    /// (<c>tf_weapon_grenade_pipebomb.h:28</c>'s
    /// <c>TF_GL_MODE_CANNONBALL</c> arm of <c>PipebombTouch</c>,
    /// <c>:772-817</c>): the ball does not explode on the hit — it wounds and
    /// shoves, once per victim, and keeps flying.
    /// </summary>
    private bool CannonballPush(EntityHandle victim)
    {
        // The push's victims (:776): a player or a building, the thrower
        // behind the damage. Anything else hostile takes the blast
        // (:820-826) as every pipe does.
        if (!Entities.IsValid(_thrower) || !(Entities.IsPlayer(victim) || Directory.Get(victim) is TFBaseObject))
            return false;

        // Already through (:779): the second touch is neither damage nor
        // shove — and still no blast (:816).
        if (_penetrated.Contains(victim))
            return true;

        // The impact's damage scales down over the distance thrown
        // (:781-783): <see cref="ImpactNear"/> at the feet to
        // <see cref="ImpactFar"/> a thousand units out, the cannon's row the
        // bits (<c>GetDamageType</c> over <c>GetWeaponID</c>, :135-143) and
        // the push its custom kind (:785).
        float distanceSq = (Entities.GetOrigin(victim) - Entities.GetOrigin(_thrower)).LengthSquared;
        float impact = TFRadiusDamage.RemapClamped(distanceSq, NearDistanceSq, FarDistanceSq,
                                                   ImpactNear, ImpactFar);
        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(TFDamageCustom.CannonballPush, Weapon, WeaponName))
            Damage.TakeDamage(victim, _thrower, impact, TFWeaponDamageTypes.Of(TFWeaponId.Cannon));

        // The shove (:788-802): the ground stun unless the air current has
        // them already, then the knock — the centres' line at
        // <see cref="PushSpeed"/> and <see cref="PopSpeed"/> up, the
        // airblast's shape (:800).
        if (Entities.IsPlayer(victim))
        {
            if (!Conditions.InCond(victim, TFCond.KnockedIntoAir))
                Carriers.Part<TFStunStack>(victim.EntryIndex)?.Stun(victim, StunTime, StunStrength,
                                                                    TFStunFlags.Movement, _thrower);

            Vector toVictim = (Entities.GetWorldSpaceCenter(victim) - Entities.GetWorldSpaceCenter(_thrower)).Normalized;
            Entities.ApplyVelocityImpulse(victim, toVictim * PushSpeed + new Vector(0f, 0f, PopSpeed),
                                          clearGround: false);
        }

        // The donk list (:808-814): the launcher's window opens here, the
        // blast's double-donk mini-crit its read (TFDonkVictims); the impact's
        // sound is the client's broadcast (:806).
        Carriers.Part<TFDonkVictims>(_thrower.EntryIndex)?.Donk(victim);
        _penetrated.Add(victim);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} pushed {Entities.GetClassName(victim)}\n");
        return true;
    }

    /// <summary>
    /// <c>GetDamageScaleOnWorldContact</c> (:1378-1386): 1, times the
    /// launcher item's <c>grenade_damage_reduction_on_world_contact</c> hook.
    /// </summary>
    private float WorldContactDamageScale()
        => Attributes.Float(1f, "grenade_damage_reduction_on_world_contact",
                            Owners.ItemOf(_ownerIndex, WeaponName));

    /// <summary>
    /// <c>pOther-&gt;m_takedamage != DAMAGE_NO</c> (:759): a player always
    /// answers yes, anything else through its managed entity's
    /// <see cref="ScriptedEntity.TakesDamage"/> (as the bolt asks at
    /// <c>Bolt.cs:200</c>); nothing in the directory takes none.
    /// </summary>
    private bool Hurtable(EntityHandle toucher)
        => Entities.IsPlayer(toucher) || Directory.Get(toucher) is { TakesDamage: true };

    private void Explode(float damage, EntityHandle directVictim)
    {
        // The blast's bits are the projectile's own weapon id's
        // (g_aWeaponDamageTypes[GetWeaponID()]): a regular pipe is
        // TF_WEAPON_GRENADE_DEMOMAN (tf_weapon_grenade_pipebomb.cpp:142), the
        // cannonball TF_WEAPON_CANNON (:137-140) — a half-falloff blast
        // without DMG_USEDISTANCEMOD (tf_shareddefs.cpp:770), neither the
        // launcher's row.
        DamageType bits = PipebombMode == TFProjectileDefs.GrenadeModeCannonball
            ? TFWeaponDamageTypes.Of(TFWeaponId.Cannon)
            : TFWeaponDamageTypes.Of(TFWeaponId.GrenadeDemoman);
        Blast.Apply(Origin, TFBlast.Of(damage, BlastRadius, bits),
                    Attribution(), Handle,
                    ignored: Handle, Weapon, WeaponName, Critical, directVictim,
                    selfRadius: TFProjectileDefs.GrenadeJumpRadius, selfBaseDamage: FullDamage);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} exploded at {Origin}\n");
        Remove();
    }

    /// <summary>
    /// The blast's radius, the stock pipe's <c>GetDamageRadius</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:1417-1433</c>'s base arm,
    /// <c>tf_weaponbase_grenadeproj.cpp:137-142</c>): the thrown radius under
    /// the launcher's <c>mult_explosion_radius</c>, the Loch-n-Load's 0.75.
    /// The air-det ramp is the sticky's alone (:1424).
    /// </summary>
    private float BlastRadius
        => Attributes.Float(Radius, "mult_explosion_radius", Owners.ItemOf(_ownerIndex, WeaponName));

    private EntityHandle Attribution() => Entities.IsValid(_thrower) ? _thrower : Handle;
}

/// <summary>
/// The stickybomb (<c>TF_GL_MODE_REMOTE_DETONATE</c>, <c>tf_weapon_grenade_pipebomb.cpp:499-510</c>):
/// no self-timer at all (<c>SetDetonateTimerLength( FLT_MAX )</c>) — it goes
/// off when its launcher's owner detonates (<see cref="TFStickyTray"/>) —
/// and it sticks to the world on first contact, armed or not
/// (<c>VPhysicsCollision</c>, :928), with the armed rest check as the
/// fallback. Touch never sets it off. A force-moved one
/// (<see cref="OnDamagedAt"/>) re-sticks only once its sleep time has passed.
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Stickybomb, ScriptedEntityKind.Prop)]
public sealed class TFStickybomb : ScriptedEntity
{
    /// <summary>Below this speed an armed sticky has come to rest and sticks (<c>PreArmThink</c>'s rule).</summary>
    public const float RestSpeed = 30f;

    /// <summary>How often an un-stuck armed sticky re-checks its speed, seconds.</summary>
    public const float RestCheckInterval = 0.1f;

    /// <summary>
    /// <c>SF_TF_DYNAMICPROP_GRENADE_COLLISION</c> (<c>tf_gamerules.cpp:19308</c>):
    /// the one <c>training_prop_dynamic</c> spawnflag that makes the prop take a sticky.
    /// </summary>
    public const int GrenadeCollisionSpawnFlag = 512;

    private const string TrainingProp = "training_prop_dynamic";

    /// <summary>Stock's <c>maxEntities</c> of the DetonateStickies sphere (:633).</summary>
    private const int MaxStickyTargets = 64;

    private readonly IEngineEntityPhysics Physics;
    private readonly TFBlastApplier Blast;
    private readonly IEnginePlayers Players;
    private readonly TFStickybombConVars ConVars;
    private readonly IGameClock Clock;
    private readonly IEngineWorldQuery World;
    private readonly IEntityDirectory Directory;
    private readonly IEngineFrame Frame;

    /// <summary>
    /// Every dependency is injected: the server's composition registers the
    /// post-simulation queue (<c>ServerModule</c>'s instance), so the pinning
    /// always waits for the step's end and no direct path stands beside it.
    /// </summary>
    public TFStickybomb(IEngineEntityPhysics physics, TFBlastApplier blast, IEnginePlayers players,
                        TFStickybombConVars convars, IGameClock clock, IEngineWorldQuery world,
                        IEntityDirectory directory, IEngineFrame frame,
                        PostSimulationQueue postSimulation)
    {
        Physics = physics;
        Blast = blast;
        Players = players;
        ConVars = convars;
        Clock = clock;
        World = world;
        Directory = directory;
        Frame = frame;
        _postSimulation = postSimulation;
    }

    /// <summary>Stock's <c>g_PostSimulationQueue</c>, holding the pin until the step's end.</summary>
    private readonly PostSimulationQueue _postSimulation;

    public const string Model = TFProjectileDefs.StickyModel;

    /// <summary>What the client's trail mirror reads.</summary>
    [NetState] public TFStickybombNetState State = new();

    public float FullDamage { get; private set; }
    public float Radius { get; private set; }
    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>
    /// Stock's <c>m_iType</c> (<c>SetPipebombMode</c>,
    /// <c>tf_weapon_grenade_pipebomb.cpp:465</c>): the <c>TF_GL_MODE_*</c> the
    /// birth's translation (<see cref="TFProjectileDefs.PipebombMode"/>)
    /// stamped — remote for the bare sticky, the jumper's practice and the
    /// cannonball over it.
    /// </summary>
    public int PipebombMode { get; private set; } = TFProjectileDefs.GrenadeModeRemoteDetonate;

    private EntityHandle _thrower = EntityHandle.None;
    private float _createdAt;
    private float _minSleepTime;
    private float _deflectedResetAt;
    private bool _armed;
    private float _armTime;
    private bool _stuck;
    private bool _spent;
    private bool _detonateOnPulse;
    private bool _fizzled;
    private TFStickyTray? _tray;
    private int _trayOwner = -1;

    /// <summary>
    /// Stock's <c>m_iDeflected</c> (<c>tf_weapon_grenade_pipebomb.cpp:125</c>):
    /// how many deflections moved this bomb — reset ten seconds after the
    /// last one (<see cref="OnThink"/>), as IncrementDeflected's clock runs
    /// (<c>tf_weapon_grenade_pipebomb.cpp:1100-1108</c>).
    /// </summary>
    public int DeflectedCount { get; private set; }

    public override void OnSpawn()
    {
        SetModel(Model);
        Physics.SetGravity(Handle, TFProjectileDefs.PipebombGravity);
        Physics.SetFriction(Handle, TFProjectileDefs.PipebombFriction);
        // PreArmThink books the arm at the live time
        // (tf_weapon_grenade_pipebomb.cpp:1144, GetLiveTime :1186-1210): the
        // launch's stamped total, or the bare convar for an unstamped bomb.
        SetNextThink(_armTime > 0f ? _armTime : ConVars.LiveTime.Value);
    }

    /// <summary>
    /// <c>GetLiveTime</c>'s total (<c>tf_weapon_grenade_pipebomb.cpp:1186-1190</c>):
    /// <c>tf_grenadelauncher_livetime</c> through the launching launcher's
    /// <c>sticky_arm_time</c> hook, stamped once by the launch that threw this
    /// bomb. The arm think re-books at it, as stock books it at the same
    /// number (:1144). <c>GetDamageRadius</c>' ramp start stays the bare
    /// convar, stock's own split (:1428 reads the convar, not this hook).
    /// </summary>
    public void SetArmTime(float seconds)
    {
        _armTime = seconds;

        // The stamp may arrive after the spawn booked the arm at the bare
        // convar (Born spawns before the launch arms the bomb): re-book at
        // the stamped total so the armed gate waits the full window.
        SetNextThink(seconds > 0f ? seconds : ConVars.LiveTime.Value);
    }

    public override bool TouchedByAnything => true;

    /// <summary>
    /// The engine-touch door to the same stick the collision below decides: a
    /// contact the engine's own touch dispatch names reaches here, and the
    /// rule is the one stock states once for both doors
    /// (<c>VPhysicsCollision</c>, <c>tf_weapon_grenade_pipebomb.cpp:916-928</c>).
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (_stuck || _spent || toucher == EntityHandle.None)
            return;

        StickOnContact(toucher);
    }

    /// <summary>
    /// Stock's <c>CTFGrenadePipebombProjectile::VPhysicsCollision</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:849-947</c>), the sticky's own arm:
    /// the other side of the solved impact, when it is the world or a dynamic
    /// prop and the sleep time has passed (:928), sticks the bomb. The router
    /// has already applied stock's <c>isCollision</c> gate and the shadow pair's
    /// when it delivered this; the base's sound-and-damage body is the frame
    ///-wide <see cref="PhysicsCollisionResponse"/>, not this hook. Stock's
    /// m_bWallShatter and regular-mode arms wait on launcher attributes and the
    /// modes this port's launchers do not carry.
    /// </summary>
    public override void OnVPhysicsCollision(in PhysicsEvent ev, int index)
    {
        if (_stuck || _spent || index is not (0 or 1))
            return;

        bool mine = index == 0;
        uint other = mine ? ev.Entity1 : ev.Entity0;

        if (other == PhysicsEvent.NoEntity)
        {
            // The world's side names no entity (PhysicsCollisionImpacts): a
            // static with no game data is the world's own object or a static
            // prop's, and stock's PhysCreateWorld made both the world's. A
            // sideless mover is no one's — stock's !pHitEntity (:854-857).
            if ((mine ? ev.Static1 : ev.Static0) != 0)
                StickOnWorld();
            return;
        }

        StickOnContact(new EntityHandle(other));
    }

    /// <summary>The world arm of the stick: the sleep gate, then the pin (:928).</summary>
    private void StickOnWorld()
    {
        if (Clock.Now <= _minSleepTime)
            return;

        Stick();
    }

    /// <summary>
    /// The stick's gate for a named toucher, shared by both doors: a world
    /// contact or a dynamic prop's sticks it armed or not — under the same
    /// sleep-time wait (<c>gpGlobals-&gt;curtime &gt; m_flMinSleepTime</c>,
    /// :928). The prop arm is stock's <c>dynamic_cast&lt;CDynamicProp*&gt;</c>,
    /// approximated by classname, with the cast's exemptions:
    /// <see cref="SticksTo"/>.
    /// </summary>
    private void StickOnContact(EntityHandle toucher)
    {
        if (Clock.Now <= _minSleepTime)
            return;

        if (Entities.GetIndex(toucher) == 0 || SticksTo(toucher))
            Stick();
    }

    /// <summary>
    /// The dynamic-prop arm of the stick (<c>:916-925</c>): a
    /// <c>CDynamicProp</c> contact takes the sticky, unless the prop is
    /// parented to sawmill's moving saws or is a training prop without the
    /// grenade-collision spawnflag. Stock casts; the port names classes and
    /// reads the sawmill exemption off the resolved parent's targetname where
    /// stock reads the <c>m_iParent</c> keyvalue string.
    /// </summary>
    private bool SticksTo(EntityHandle toucher)
    {
        if (!IsDynamicProp(toucher))
            return false;

        // Temp: Don't stick to the saw blades in sawmill (:918-921).
        EntityHandle parent = Entities.GetParent(toucher);
        if (Entities.IsValid(parent) && Entities.GetName(parent) is "sawmovelinear01" or "sawmovelinear02")
            return false;

        // PropDynamic_CollidesWithGrenades (tf_gamerules.cpp:19308-19320):
        // a training prop takes a sticky only with the spawnflag.
        if (Entities.GetClassName(toucher) == TrainingProp)
            return (Entities.GetSpawnFlags(toucher) & GrenadeCollisionSpawnFlag) != 0;

        return true;
    }

    private bool IsDynamicProp(EntityHandle toucher)
        => Entities.GetClassName(toucher) is "prop_dynamic" or "prop_dynamic_overridden" or TrainingProp;

    private void Stick()
    {
        _stuck = true;

        // g_PostSimulationQueue.QueueCall( EnableMotion, false ) (:932): the
        // contact is a solver callback, and the pinning waits for the step's
        // end. A stick reached outside the step (the rest check's think) rides
        // the same queue, which runs before the next one.
        _postSimulation.Add(DisableMotion);

        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Stickybomb} stuck at {Origin}\n");
    }

    private void DisableMotion() => Physics.EnableMotion(Handle, false);

    /// <summary>
    /// CTFWeaponBaseGrenadeProj::VPhysicsUpdate's server branch
    /// (<c>tf_weaponbase_grenadeproj.cpp:716-795</c>): the grenade is not
    /// solid to players, so each physics frame ray-casts the step ahead — at
    /// the mask a <c>COLLISION_GROUP_NONE</c> thing stops on, which the seam's
    /// trace already runs — and on a named enemy, or a teammate past stock's
    /// collide delay (<see cref="CanCollideWithTeammates"/>), reflects the
    /// velocity off the hit normal (:781), absorbs
    /// <see cref="TFProjectileDefs.PipebombElasticity"/> (:784) and, an enemy
    /// hit, half again (:786-789), with the angular velocity reversed and
    /// halved (:791). The pipe's own arm of this is its
    /// <c>PipebombTouch</c>: the enemy contact the solver's touch already
    /// detonates on, so the live effect of the ray is the sticky's bounce.
    ///
    /// <para>
    /// The filter passes the grenade and its thrower (:727) — the muzzle
    /// spawns inside the thrower's hull — so a thrower hit is a miss here.
    /// The <c>IsCombatItem</c> branch (:744) has no combat item to reach, and
    /// the <c>tr.startsolid</c> arm (:757) waits on a trace that reports
    /// starting inside something (docs/divergences.md). Stock's mask is
    /// <c>CONTENTS_HITBOX|CONTENTS_MONSTER|CONTENTS_SOLID</c>; the seam's
    /// nearest is <see cref="TraceMask.Shot"/>, whose window, debris and
    /// moveable bits a grenade's ray passes through in stock.
    /// </para>
    /// </summary>
    public override void OnPhysicsUpdate()
    {
        if (_spent)
            return;

        Vector velocity = Entities.GetVelocity(Handle);
        Vector start = Origin;
        TraceResult hit = World.TraceLine(start, start + velocity * Frame.GetFrameTime(),
                                          TraceMask.Shot, Handle);

        if (hit.Entity == EntityHandle.None || hit.Entity == _thrower)
            return;

        int myTeam = Entities.IsValid(_thrower) ? Players.GetTeam(_thrower) : 0;
        int hitTeam = Players.GetTeam(hit.Entity);
        bool hitEnemy = hitTeam != 0 && myTeam != 0 && hitTeam != myTeam;
        bool hitFriendly = hitTeam == myTeam && CanCollideWithTeammates;

        if (!hitEnemy && !hitFriendly)
            return;

        // reflect velocity around normal (:781), then absorb 55% (:784).
        Vector reflected = -2f * hit.Normal * velocity.Dot(hit.Normal) + velocity;
        reflected *= TFProjectileDefs.PipebombElasticity;

        if (hitEnemy)
            reflected *= 0.5f;

        Entities.SetVelocity(Handle, reflected);
        Entities.SetAngularVelocity(Handle, Entities.GetAngularVelocity(Handle) * -0.5f);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Stickybomb} bounced off " +
                $"{Entities.GetClassName(hit.Entity)} at {Origin}\n");
    }

    /// <summary>
    /// CBaseProjectile::CanCollideWithTeammates
    /// (<c>baseprojectile.h:53</c>): false for the first
    /// <c>GetCollideWithTeammatesDelay</c>
    /// (<see cref="TFProjectileDefs.CollideWithTeammatesDelay"/>) of life —
    /// stock's <c>CollideWithTeammatesThink</c> sets the flag at spawn plus
    /// the delay, on the server clock this port reads.
    /// </summary>
    private bool CanCollideWithTeammates
        => Clock.Now - _createdAt >= TFProjectileDefs.CollideWithTeammatesDelay;

    /// <summary>
    /// A shot sticky moves: stock's <c>OnTakeDamage</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:959-1074</c>). An enemy blow on a
    /// stuck bomb carrying one of the five force bits scales the blow's force
    /// by its kind's <c>tf_grenade_forcefrom_*</c> convar (the sonic arm reads
    /// the bullet one, as stock's :983 does), and past
    /// <c>tf_pipebomb_force_to_move</c> the prop is re-animated and re-sticks
    /// only once <c>tf_grenade_force_sleeptime</c> has passed.
    ///
    /// <para>
    /// Stock's break-pipes arm (:1000-1044 — <c>Fizzle</c> plus
    /// <c>Detonate</c> behind the <c>player_destroyed_pipebomb</c> event) is
    /// not here: the bomb carries no fizzle timer in this port, and the
    /// fizzle time waits on a launcher attribute read.
    /// </para>
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        if (attacker == EntityHandle.None || !_stuck || !TakesDamage)
            return 0;

        // bSameTeam (:967): the bomb's team is its thrower's.
        int myTeam = Entities.IsValid(_thrower) ? Players.GetTeam(_thrower) : 0;
        if (Players.GetTeam(attacker) == myTeam)
            return 0;

        if ((damageType & (DamageType.Bullet | DamageType.Buckshot | DamageType.Blast |
                           DamageType.Sonic | TFDamageBits.Melee)) == 0)
            return 0;

        Vector scaled = force;
        if ((damageType & (DamageType.Bullet | TFDamageBits.Melee)) != 0)
            scaled *= ConVars.ForceFromBullet.Value;
        if ((damageType & DamageType.Sonic) != 0)
            scaled *= ConVars.ForceFromBullet.Value;
        else if ((damageType & DamageType.Buckshot) != 0)
            scaled *= ConVars.ForceFromBuckshot.Value;
        else if ((damageType & DamageType.Blast) != 0)
            // The DMG_IGNITE arm (the Cow Mangler's destroy, :992-996) breaks
            // pipes too; ignition is not ported, so the bit is not read.
            scaled *= ConVars.ForceFromBlast.Value;

        // If the force is sufficient, detach & move the pipebomb (:1046-1069).
        float toMove = ConVars.ForceToMove.Value;
        if (scaled.LengthSquared <= toMove * toMove)
            return 0;

        Physics.EnableMotion(Handle, true);
        VPhysicsTakeDamage(damageType, position, scaled);

        // The pipebomb will re-stick to the ground after this time expires.
        _minSleepTime = Clock.Now + ConVars.ForceSleepTime.Value;
        _stuck = false;
        return 1;
    }

    /// <summary>
    /// <c>CTFGrenadePipebombProjectile::GetDamageRadius</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:1417-1434</c>): the winbomb
    /// prevention. A remote-detonate bomb that has never touched the world
    /// (stock's <c>m_bTouched == false</c>, this port's <c>!_stuck</c> —
    /// set only by the stick, cleared by a force-move as stock clears
    /// <c>m_bTouched</c> at :1062) detonates at
    /// <c>tf_sticky_airdet_radius</c> of its radius, ramping to full across
    /// <c>tf_sticky_radius_ramp_time</c> once its arm time
    /// (<see cref="TFProjectileDefs.StickyArmTime"/>, as the arm think reads
    /// it) has passed. Stock's other reader of this one number is the
    /// launcher loop's sticky-destroying <see cref="DetonateStickies"/>
    /// sphere.
    /// </summary>
    public float GetDamageRadius()
    {
        if (_stuck)
            return Radius;

        // The ramp starts at the live time, as stock's flArmTime does
        // (:1428) — the same number the arm think booked itself by.
        float armTime = ConVars.LiveTime.Value;
        return Radius * TFRadiusDamage.RemapClamped(
            Clock.Now - _createdAt,
            armTime,
            armTime
            + ConVars.RadiusRampTime.Value,
            ConVars.AirDetRadius.Value, 1f);
    }

    public void ThrownBy(EntityHandle thrower, Vector velocity, float fullDamage, float radius,
                         TFWeaponId weapon, string weaponName, bool critical,
                         int pipebombMode = TFProjectileDefs.GrenadeModeRemoteDetonate)
    {
        _thrower = thrower;
        _createdAt = Clock.Now; // m_flCreationTime, for the young-pipe damage type
        FullDamage = fullDamage;
        Radius = radius;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        PipebombMode = pipebombMode;

        // Spawn's practice arm (tf_weapon_grenade_pipebomb.cpp:499-502): the
        // w_stickybomb2 shell. The stamp is the first the mode is known —
        // stock learns it before its Spawn, the launch here after ours — so
        // the model change rides the stamp.
        if (PipebombMode == TFProjectileDefs.GrenadeModeRemoteDetonatePractice)
            SetModel(TFProjectileDefs.StickyPracticeModel);

        // The client's trail mirror reads the team off the projectile.
        State.Critical = critical;
        State.TeamNumber = Players.GetTeam(thrower);

        Entities.SetOwner(Handle, thrower);
        Entities.SetVelocity(Handle, velocity);
    }

    /// <summary>
    /// LaunchGrenade's DOT arm (<c>tf_weapon_pipebomblauncher.cpp:305-309</c>):
    /// the launcher whose <c>set_detonate_mode</c> is TF_DETONATE_MODE_DOT —
    /// the Scottish Resistance's — dresses its bomb in the defensive
    /// w_stickybomb_d shell at the launch. Stock's <c>m_bDefensiveBomb</c>
    /// beside it drives the thrower's glow and the always-transmit
    /// (<c>tf_weapon_grenade_pipebomb.cpp:256, 1393, 1406</c>), the client's
    /// effects; the kill-feed custom
    /// (<c>GetDamageCustom</c>, :1360-1362) waits on the blast's custom channel.
    /// </summary>
    public void DefensiveBomb() => SetModel(TFProjectileDefs.StickyDefensiveModel);

    /// <summary>
    /// Deflected's sticky arm (<c>tf_weapon_grenade_pipebomb.cpp:1223-1245</c>),
    /// reached from the airblast's <c>DeflectEntity</c>: the bomb keeps its
    /// thrower and team — the deflection IS the blow, stock's
    /// <c>OnTakeDamage</c> of DMG_SONIC carrying <paramref name="force"/>, the
    /// aim times <c>DeflectionForce(WorldAlignSize, 90, 12)</c>
    /// (<c>tf_weaponbase.cpp:6103</c>), at the bomb itself. The count is
    /// IncrementDeflected's (<c>:1076-1085</c>), whose clock
    /// (<c>tf_pipebomb_deflect_reset_time</c>) <see cref="OnThink"/> serves.
    /// The charge multiplier stock reads off the deflector's held
    /// <c>ITFChargeUpWeapon</c> (:1227-1236) is 1 for every weapon this port's
    /// deflector carries — none keeps a charge meter (the launchers' are the
    /// demoman's).
    /// </summary>
    public void DeflectedBy(EntityHandle deflector, Vector force)
    {
        DeflectedCount++;
        _deflectedResetAt = Clock.Now + ConVars.DeflectResetTime.Value;
        OnDamagedAt(deflector, damage: 0f, DamageType.Sonic, Origin, force);
    }

    /// <summary>
    /// The arm think (<c>ArmThink</c>, <c>tf_weapon_grenade_pipebomb.cpp:1147-1179</c>),
    /// then the rest check until it sticks. The arm think runs stuck or not —
    /// PreArmThink schedules it at spawn (:1142-1145) and asks nothing about
    /// the stick — so a stuck sticky still arms and still takes its
    /// detonate-on-pulse; only the rest check is for a sticky that has not
    /// stuck yet.
    /// </summary>
    public override void OnThink()
    {
        if (_spent)
            return;

        // IncrementDeflected's reset (DetonateThink,
        // tf_weapon_grenade_pipebomb.cpp:1100-1108): ten seconds after the
        // last deflection the count goes, on any think the bomb keeps. A
        // deflection's force-move wakes no think of its own, so an
        // immediately blown-away bomb carries its count until its next one.
        if (_deflectedResetAt > 0f && Clock.Now >= _deflectedResetAt)
        {
            DeflectedCount = 0;
            _deflectedResetAt = 0f;
        }

        if (!_armed)
        {
            _armed = true;

            // The pulse: stock's client fires stickybomb_pulse_* in Simulate
            // at curtime - m_flCreationTime >= GetLiveTime()
            // (tf_weapon_grenade_pipebomb.cpp:345-373) — the arm think's own
            // moment (StickyArmTime 0.8, the default live time), so the
            // server writes the flag and the client's mirror wears it.
            State.Pulsed = true;

            // SetDetonateOnPulse (tf_weapon_grenade_pipebomb.cpp:1175): the
            // next pulse — here the arm think — goes off instead.
            if (_detonateOnPulse)
            {
                Detonate();
                return;
            }

            if (!_stuck)
                SetNextThink(RestCheckInterval);
            return;
        }

        if (_stuck)
            return;

        // The rest check stands in for the collision the touch seam cannot
        // name, so it waits out the sleep time the same way (:928).
        if (Clock.Now <= _minSleepTime)
            return;

        if (Entities.GetVelocity(Handle).Length < RestSpeed)
        {
            Stick();
            return;
        }

        SetNextThink(RestCheckInterval);
    }

    public bool Armed => _armed;

    /// <summary>The bomb's <c>m_bFizzle</c>, as another bomb's DetonateStickies reads it.</summary>
    public bool Fizzled => _fizzled;

    /// <summary>
    /// <c>CTFGrenadePipebombProjectile::DetonateStickies</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:625-674</c>): the
    /// <c>stickies_detonate_stickies</c> launcher's bomb sweeps its own
    /// damage radius for enemy remote-detonate bombs — own team skipped
    /// (:642-643), fizzled skipped (:652-653), no line of sight skipped
    /// (:655-657) — and takes each out with its own <see cref="Detonate"/>,
    /// fizzled first so no second blast rides it (:659-660). Stock's
    /// <c>dynamic_cast</c> + <c>m_iType == TF_GL_MODE_REMOTE_DETONATE</c> is
    /// the directory's class here: every sticky this port fires is the
    /// remote-detonate mode, and a pipe resolves to its own class. Stock's
    /// achievement arm (:667-671) has no layer to land on.
    /// </summary>
    public void DetonateStickies()
    {
        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxStickyTargets];
        int count = World.EntitiesInSphere(Origin, GetDamageRadius(), nearby);
        int myTeam = Entities.IsValid(_thrower) ? Players.GetTeam(_thrower) : 0;

        for (int i = 0; i < count; i++)
        {
            EntityHandle target = nearby[i];
            if (target == EntityHandle.None || Directory.Get(target) is not TFStickybomb bomb || bomb._fizzled)
                continue;

            // The bomb's team is its thrower's: asked of the bomb itself the
            // player seam answers no team, and the sweep fizzled its own bomb.
            if (Players.GetTeam(bomb._thrower) == myTeam)
                continue;

            // UTIL_TraceLine(..., MASK_SOLID_BRUSHONLY, this, ...): the
            // sweeping bomb itself is the pass-through, as stock's `this`.
            if (World.TraceLine(Origin, bomb.Origin, TraceMask.Brushes, Handle).Fraction < 1f)
                continue;

            bomb.Fizzle();
            bomb.Detonate();
        }
    }

    /// <summary>
    /// Marks a young sticky (fired within its live time, i.e. not yet armed)
    /// to detonate on its next pulse, stock's SetDetonateOnPulse
    /// (<c>tf_weapon_pipebomblauncher.cpp:553-556</c>) — the launcher's
    /// detonation does not go through it now.
    /// </summary>
    public void ArmDetonateOnPulse() => _detonateOnPulse = true;

    /// <summary>
    /// <c>Fizzle</c> (<c>tf_weapon_grenade_pipebomb.cpp:691-694</c>): the
    /// bomb's <c>m_bFizzle</c> — the next <see cref="Detonate"/> goes without
    /// blasting. The launcher's owner dying sets it on the whole tray.
    /// </summary>
    public void Fizzle() => _fizzled = true;

    /// <summary>
    /// The launcher's half of the firing: stock's <c>Create</c> hands the
    /// projectile its <c>m_hLauncher</c> and <c>PrimaryAttack</c> adds it to
    /// the launcher's <c>m_Pipebombs</c>; the tray is this port's list, and it
    /// learns both at once, on <see cref="TFStickyTray.Register"/>. The back
    /// reference is what <see cref="OnRemoved"/> reports through.
    /// </summary>
    public void EnrolledIn(TFStickyTray tray, int ownerIndex)
    {
        _tray = tray;
        _trayOwner = ownerIndex;
    }

    /// <summary>
    /// <c>CTFGrenadePipebombProjectile::UpdateOnRemove</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:175-186</c>): the removed bomb tells
    /// its launcher — the <c>DeathNotice</c> that FindAndRemoves it from the
    /// tray (<c>tf_weapon_pipebomblauncher.cpp:508-517</c>).
    /// </summary>
    public override void OnRemoved() => _tray?.Unregister(_trayOwner, this);

    /// <summary>
    /// The fuse goes: with no arming gate — the removed ninth sticky's
    /// "explode NOW" (<c>tf_weapon_pipebomblauncher.cpp:388</c>) runs through
    /// the same Detonate, and stock's arming check lives in the launcher's
    /// young-sticky rule, not the bomb.
    /// </summary>
    public void Detonate()
    {
        if (_spent)
            return;

        _spent = true;

        if (_fizzled)
        {
            // Detonate's m_bFizzle branch (tf_weapon_grenade_pipebomb.cpp:606-617):
            // sparks, Destroy(false) and gibs — BaseClass::Detonate never runs,
            // so no blast. The sparks and gibs are effects this port has not
            // wired (docs/divergences.md).
            Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Stickybomb} fizzled at {Origin}\n");
            Remove();
            return;
        }

        // GetDamageType (tf_weapon_grenade_pipebomb.cpp:148-162): the
        // remote-detonate pipe alone adds DMG_USEDISTANCEMOD within 5 s of
        // its creation — the jumper's practice bomb and the ball detonate on
        // the bare row. The radius is GetDamageRadius's, not the script's —
        // an untouched bomb's air blast comes in short (:1417-1434).
        DamageType bits = TFWeaponDamageTypes.Of(Weapon);
        if (PipebombMode == TFProjectileDefs.GrenadeModeRemoteDetonate)
            bits = TFProjectileDefs.YoungStickyBits(bits, Clock.Now - _createdAt);
        Blast.Apply(Origin, TFBlast.Of(FullDamage, GetDamageRadius(), bits),
                    Entities.IsValid(_thrower) ? _thrower : Handle, Handle, ignored: Handle,
                    Weapon, WeaponName, Critical, directVictim: EntityHandle.None,
                    selfRadius: TFProjectileDefs.GrenadeJumpRadius, selfBaseDamage: FullDamage);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Stickybomb} detonated at {Origin}\n");
        Remove();
    }
}

/// <summary>
/// The syringe (<c>CTFProjectile_Syringe</c>, <c>shared/tf/tf_projectile_nail.cpp</c>):
/// the nail base's flight — an arc at <see cref="TFProjectileDefs.SyringeGravity"/>,
/// the syringe gun's speed — and on its first touch the weapon's
/// <see cref="TFProjectileDefs.SyringeDamage"/>, carried untripled with its
/// crit flag through the damage layer like every other TF hit.
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Syringe, ScriptedEntityKind.Projectile)]
public sealed class TFSyringe : FlyingProjectile
{
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;

    public TFSyringe(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                     TFDamageScopes scopes, DamageHitGroups hitGroups)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
    }

    protected override float GravityScale => TFProjectileDefs.SyringeGravity;

    protected override string ProjectileModel => TFProjectileDefs.SyringeModel;

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    public void ArmedBy(TFWeaponId weapon, string weaponName, bool critical)
    {
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
    }

    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return;

        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(TFDamageCustom.None, Weapon, WeaponName))
            // The syringe gun's own bits (g_aWeaponDamageTypes): a bullet that
            // ramps up near and never pushes.
            Damage.TakeDamage(hit, Attribution, TFProjectileDefs.SyringeDamage, TFWeaponDamageTypes.Of(Weapon));
    }
}

/// <summary>
/// The huntsman's arrow (<c>CTFProjectile_Arrow</c>,
/// <c>game/server/tf/tf_projectile_arrow.cpp</c> over the nail base's flight):
/// born with the bow's charge-scaled gravity
/// (<see cref="TFProjectileDefs.ArrowGravity"/>) and speed
/// (<see cref="TFProjectileDefs.ArrowSpeed"/>), and on its first enemy touch
/// the bow's shot — a <em>lit</em> arrow carrying the ignite bit and the
/// flying-burn custom with it (<c>m_bArrowAlight</c>'s arm inside the
/// non-teammate test, <c>:478-490</c>: <c>DMG_IGNITE</c> ORed onto the damage
/// type, <c>TF_DMG_CUSTOM_FLYINGBURN</c>). The headshot and crit-bit arms
/// (:492-501) ride the shot's own crit flag here, as elsewhere in this port.
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Arrow, ScriptedEntityKind.Projectile)]
public sealed class TFArrow : FlyingProjectile
{
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;
    private readonly IEntityDirectory Directory;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Hooks;
    private readonly IEnginePlayers Players;
    private readonly ITempEnts Temps;
    private readonly IEnginePrecache Precache;

    private float _gravity = TFProjectileDefs.ArrowGravity(0f);
    private TFProjectile _projectile = TFProjectile.Arrow;

    public TFArrow(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                   TFDamageScopes scopes, DamageHitGroups hitGroups, IEntityDirectory directory,
                   ITFWeaponOwners owners, AttributeHooks hooks, IEnginePlayers players,
                   ITempEnts temps, IEnginePrecache precache)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Directory = directory;
        Owners = owners;
        Hooks = hooks;
        Players = players;
        Temps = temps;
        Precache = precache;
    }

    protected override float GravityScale => _gravity;

    // Spawn's arm (tf_projectile_arrow.cpp:204-207): the rescue bolt's claw
    // where the bow's arrow is the regular shaft.
    protected override string ProjectileModel
        => _projectile == TFProjectile.BuildingRepairBolt ? TFProjectileDefs.RepairClawModel
                                                          : TFProjectileDefs.ArrowModel;

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>Whether the bow was alight when this arrow left it.</summary>
    public bool Lit { get; private set; }

    /// <summary>The shot the bow dealt: its charge, and whether the flame had lit it.</summary>
    public void ArmedBy(TFWeaponId weapon, string weaponName, bool critical, bool lit,
                        float damage, float gravity,
                        TFProjectile projectile = TFProjectile.Arrow)
    {
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        Lit = lit;
        ShotDamage = damage;
        _gravity = gravity;
        _projectile = projectile;
    }

    /// <summary>The stock bow's shot, scaled by its charge (<see cref="ArmedBy"/>).</summary>
    public float ShotDamage { get; private set; } = TFProjectileDefs.ArrowBaseDamage;

    /// <summary><c>arrow_heals_buildings</c>, the repair the fired item's worth names (<c>tf_projectile_arrow.cpp:667</c>).</summary>
    private const string HealBuildingsAttribute = "arrow_heals_buildings";

    /// <summary>
    /// <c>BuildingHealingArrow</c>'s repair ratio, the caller's four
    /// (<c>tf_projectile_arrow.cpp:681</c>): four health the metal.
    /// </summary>
    private const float RescueRepairRatio = 4f;

    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return;

        // OnArrowImpactObject's arm for one of the team's buildings
        // (:633-640 → BuildingHealingArrow, :651-688): the fired item's
        // arrow_heals_buildings pays the repair — no sapper, plasma hold,
        // blueprint or placement on the building, and the wrangler's shielded
        // plate a third of it — and the claw's particle tells where it mended.
        RepairBuilding(hit);

        // Stock's bits are the bow's own (g_aWeaponDamageTypes), the ignite bit
        // and the flying-burn custom added on the lit arm (:486-490) — inside
        // the enemy test, so a teammate hit by a lit arrow takes plain damage.
        var bits = TFWeaponDamageTypes.Of(Weapon);
        var custom = TFDamageCustom.None;
        if (Lit)
        {
            bits |= TFDamageBits.Ignite;
            custom = TFDamageCustom.Flyingburn;
        }

        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(custom, Weapon, WeaponName))
            Damage.TakeDamage(hit, Attribution, ShotDamage, bits);
    }

    /// <summary><c>BuildingHealingArrow</c> (<c>tf_projectile_arrow.cpp:651-688</c>).</summary>
    private void RepairBuilding(EntityHandle hit)
    {
        // The scorer is the shot's player (:658-660), the building his team's
        // (:663-664); anything else is the blow's alone.
        if (Shooter == EntityHandle.None
            || Directory.Get(hit) is not TFBaseObject building
            || building.Team != Players.GetTeam(Shooter))
            return;

        int amount = Hooks.Int(0, HealBuildingsAttribute, Owners.ItemOf(Shooter.EntryIndex, WeaponName));
        if (amount <= 0)
            return;

        if (building.HasSapper || building.IsPlasmaDisabled || building.IsBuilding || building.IsPlacing)
            return;

        // The shielded plate's third (:676-679, SHIELD_NORMAL_VALUE's scale).
        if (building is TFSentryGun { ShieldLevel: TFSentryGun.ShieldNormal })
            amount = (int)(amount * TFSentryGun.ShieldDamageScale);

        if (building.CommandRepair(Shooter, amount, repairMod: 1f, RescueRepairRatio) <= 0)
            return;

        // The tell, on the arrow's own position (:684-687).
        Vector at = Entities.GetOrigin(Handle);
        bool red = Players.GetTeam(Shooter) != (int)TFTeam.Blue;
        var mend = new TeEffectData
        {
            Origin = at,
            Start = at,
            HitBox = Precache.ParticleSystemIndex(red ? TFProjectileDefs.RepairClawHealRed
                                                      : TFProjectileDefs.RepairClawHealBlue),
        };
        Temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in mend);
    }
}

/// <summary>
/// The bison's ring (<c>CTFProjectile_EnergyRing</c>,
/// <c>shared/tf/tf_projectile_energy_ring.cpp</c>): the energy shot's flat
/// flight at the ring's hundred-twenty (the ball's kin below). Its first touch
/// lands the shot the behaviour rolled — the penetrating twenty or the
/// single-hit sixty through the hook — carrying the plasma custom
/// (<c>GetDamageCustom</c>, :267), which the buildings' own plasma-resistance
/// arm (<c>tf_obj.cpp:1896-1899</c>) reads at the half-tenth scale.
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.EnergyRing, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.ParticleSystem, TFProjectileDefs.EnergyDrainEffect)]
[Precache(PrecacheKind.Sound, TFProjectileDefs.EnergyDrainSound)]
public sealed class TFProjectileEnergyRing : FlyingProjectile
{
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Hooks;
    private readonly IEnginePlayers Players;
    private readonly IGameClock Clock;
    private readonly ITempEnts Temps;
    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;

    public TFProjectileEnergyRing(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                                  TFDamageScopes scopes, DamageHitGroups hitGroups,
                                  ITFWeaponOwners owners, AttributeHooks hooks,
                                  IEnginePlayers players, IGameClock clock,
                                  ITempEnts temps, IEnginePrecache precache, IEngineSound sound)
        : base(physics, motion)
    {
        Players = players;
        Clock = clock;
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Owners = owners;
        Hooks = hooks;
        Temps = temps;
        Precache = precache;
        Sound = sound;
    }

    /// <summary><c>tf_bison_tick_time</c> (tf_projectile_energy_ring.cpp:67).</summary>
    public const float BisonTickTime = 0.025f;

    /// <summary>The box's one unit, and one more, so a ring on the face counts as inside.</summary>
    private const float PassMargin = 2f;

    protected override float GravityScale => TFProjectileDefs.EnergyGravity;

    protected override string ProjectileModel => TFProjectileDefs.EnergyModel;

    /// <summary>
    /// <c>CTFBaseProjectile::Spawn</c>'s two-unit box: a box sweeps the body's
    /// hull where a ray would find its hitboxes, and how many of the piercing
    /// ring's hits land rides on where it enters.
    /// </summary>
    protected override Vector HullExtent => new(1f, 1f, 1f);

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>The shot the launcher dealt: its rolled hit, its crit flag, whose it is.</summary>
    public float ShotDamage { get; private set; }

    private int _ownerIndex;

    /// <summary><c>m_flLastHitTime</c>: one clock across every target, as stock's is.</summary>
    private float _lastHit = float.NegativeInfinity;

    /// <summary>The player a piercing ring is inside: the think's hits, until it comes out.</summary>
    private EntityHandle _passing;

    /// <summary>Arms the ring with the behaviour's rolled shot.</summary>
    public void ArmedBy(float damage, TFWeaponId weapon, string weaponName, bool critical, int ownerIndex)
    {
        ShotDamage = damage;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        _ownerIndex = ownerIndex;
    }

    /// <summary>
    /// The item's airblast refusal: <c>energy_weapon_no_deflect</c> on the
    /// owner's item. Stock's own header answers false for every ring
    /// (<c>tf_projectile_energy_ring.h:57</c>), so the ring is spared either
    /// way — the attribute is the schema's half of the same rule.
    /// </summary>
    public bool RefusesDeflect() =>
        Hooks.Int(0, "energy_weapon_no_deflect", Owners.ItemOf(_ownerIndex, WeaponName)) != 0;

    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return;

        // ShouldPenetrate (tf_projectile_energy_ring.cpp:290): the ring flies
        // on through a player. The trace would find the same body again from
        // inside it, so the flight passes players until the think sees it out.
        IAttributeProvider? shooterItem = Owners.ItemOf(_ownerIndex, WeaponName);
        if (Entities.IsPlayer(hit) && Hooks.Int(0, "energy_weapon_penetration", shooterItem) != 0)
        {
            if (Players.GetTeam(hit) != Players.GetTeam(Attribution))
                HitPassing(hit, shooterItem);
            _passing = hit;
            Motion.PassCharacters(Handle, true);
            SetNextThink(0f);
            KeepFlying(Motion.IncomingVelocityOf(Handle), GravityScale);
            return;
        }

        Hurt(hit, shooterItem);
    }

    /// <summary>
    /// Stock's <c>Touch</c> runs every tick the ring overlaps a player: the
    /// passing ring's next hits, gated on <see cref="BisonTickTime"/>
    /// (:260), and the flight's players back on once it is out of the body.
    /// </summary>
    public override void OnThink()
    {
        base.OnThink();
        if (_passing == EntityHandle.None)
            return;

        // The bounds come as offsets from the body's origin. The sweep stops the
        // box touching the hull's face, a unit outside it: without the margin
        // the think calls that out, the sweep hits the face again, and the ring
        // never moves on.
        var (mins, maxs) = Entities.GetBounds(_passing);
        Vector at = Origin - Entities.GetOrigin(_passing);
        if (!Entities.IsValid(_passing)
            || at.X < mins.X - PassMargin || at.Y < mins.Y - PassMargin || at.Z < mins.Z - PassMargin
            || at.X > maxs.X + PassMargin || at.Y > maxs.Y + PassMargin || at.Z > maxs.Z + PassMargin)
        {
            _passing = EntityHandle.None;
            Motion.PassCharacters(Handle, false);
            return;
        }

        if (Players.GetTeam(_passing) != Players.GetTeam(Attribution))
            HitPassing(_passing, Owners.ItemOf(_ownerIndex, WeaponName));
        SetNextThink(0f);
    }

    private void HitPassing(EntityHandle hit, IAttributeProvider? item)
    {
        float now = Clock.Now;
        if (now - _lastHit < BisonTickTime)
            return;
        _lastHit = now;
        Hurt(hit, item);
    }

    private void Hurt(EntityHandle hit, IAttributeProvider? item)
    {
        using (HitGroups.Push(HitGroup.Generic, Critical))
        // Stock's ring answers TF_DMG_CUSTOM_PLASMA for every hit
        // (tf_projectile_energy_ring.cpp:267): the custom rides the scope so
        // the building's plasma-resistance arm reads it.
        using (Scopes.Push(TFDamageCustom.Plasma, Weapon, WeaponName))
            Damage.TakeDamage(hit, Attribution, ShotDamage, TFWeaponDamageTypes.Of(Weapon));

        if (DrainVictim(hit, item))
            DrainFeedback(hit);
    }

    /// <summary>
    /// The pomson's two on-hit drains (<c>ApplyPostHitEffects</c>,
    /// <c>tf_weaponbase.cpp:5553-5613</c>): the firing item's
    /// <c>subtract_victim_medigun_charge_onhit</c> and
    /// <c>subtract_victim_cloak_on_hit</c> over zero, each scaled after 512 Hu
    /// to 1536 Hu (:5576-5580 — full drain nearer, none by 1536) and handed to
    /// <see cref="ITFWeaponOwners.DrainOnHitMeters"/> for the victim's class.
    /// Stock drains once per shot, guarded by the last-victim timer (:5562);
    /// this entity's one touch is spent in the base's <c>Spend</c>, which
    /// carries the same once-per-shot. Zero attributes answer the no-op, as
    /// stock's arms only run on a positive (:5569, :5593). Answers whether a
    /// meter moved — the telling stands on it.
    /// </summary>
    private bool DrainVictim(EntityHandle hit, IAttributeProvider? item)
    {
        int medicCharge = Hooks.Int(0, "subtract_victim_medigun_charge_onhit", item);
        int cloak = Hooks.Int(0, "subtract_victim_cloak_on_hit", item);
        if (medicCharge <= 0 && cloak <= 0)
            return false;

        // Stock's distance scale on the squared eye-to-victim (:5576-5579);
        // RemapValClamped clamps both ends, so the >512 guard needs no twin.
        Vector to = Entities.GetOrigin(hit) - Entities.GetOrigin(Attribution);
        float scale = TFDamageModifyRules.RemapValClamped(to.X * to.X + to.Y * to.Y + to.Z * to.Z,
                                                          512f * 512f, 1536f * 1536f, 1f, 0f);

        return Owners.DrainOnHitMeters(Entities.GetIndex(hit),
                                       medicCharge > 0 ? medicCharge / 100f * scale : 0f,
                                       cloak > 0 ? cloak / 100f * scale : 0f);
    }

    /// <summary>
    /// The drain's telling (<c>ApplyPostHitEffects</c>' tail,
    /// <c>tf_weaponbase.cpp:5615-5640</c>): the drain particle over the victim
    /// and the sound on them, only when no disguise or cloak hides the hit
    /// (:5616-5617 — the conditions' absence is a visible victim, as every
    /// null walk here is). Stock hangs the particle off the victim's head
    /// bone; the plain position form carries the same record at the same
    /// place, as the orb's particles do. Stock's two filters part the
    /// attacker's ears from the victim-position sound; the one entity sound
    /// is the record the seam carries.
    /// </summary>
    private void DrainFeedback(EntityHandle victim)
    {
        if (Owners.ConditionsOf(Entities.GetIndex(victim)) is { } seen
            && (seen.Has(TFCond.Disguised) || seen.Has(TFCond.Stealthed)))
            return;

        bool red = Players.GetTeam(Players.GetHandle(_ownerIndex)) == (int)TFTeam.Red;
        Vector at = Entities.GetWorldSpaceCenter(victim);
        TeEffectData drain = new()
        {
            Scale = 1f,
            HitBox = Precache.ParticleSystemIndex(TFProjectileDefs.EnergyDrainEffect),
            Origin = at,
            Start = at,
            CustomColors = true,
            Color1 = red ? TFProjectileDefs.ParticleWeaponRed1 : TFProjectileDefs.ParticleWeaponBlue1,
            Color2 = red ? TFProjectileDefs.ParticleWeaponRed2 : TFProjectileDefs.ParticleWeaponBlue2,
        };
        Temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in drain);
        Sound.EntityEmitSound(victim, TFProjectileDefs.EnergyDrainSound);
    }
}

/// <summary>
/// The mangler's ball (<c>CTFProjectile_EnergyBall</c>,
/// <c>game/server/tf/tf_projectile_energy_ball.cpp</c>): the charged shot's flat
/// flight, the ball carrying the stored hit — the script's damage through the
/// hook chain — to its first touch, where it explodes as stock's
/// <c>Explode</c> (:259-337): the oriented explosion particle whose name the
/// charge and the team pick (:281-286, :342-352), the charged ball's screen
/// shake (:289-292), and the rocket-shaped radius blast whose self pass runs
/// at 1.33 times the rocket-jump radius when the shot was charged (:306-325,
/// :323). The hit's bits and custom are the ball's own (<c>GetDamageType</c>
/// :365-382, <c>GetDamageCustom</c> :387-390): the charged ball ignites and
/// answers the charged plasma custom beside no crit bit; the plain one carries
/// the crit bit when its shot rolled one, and the plain plasma custom.
///
/// <para>
/// Not here (docs/plans/tf2/deferred.tsv): the impact sound's attacker-loud
/// split (<c>ImpactSound</c>, :165-189), the combat-sound insert (:296) and
/// the rocket base's stun pair (<c>CheckForStunOnImpact</c>, called at :314).
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.EnergyBall, ScriptedEntityKind.Projectile)]
public sealed class TFProjectileEnergyBall : FlyingProjectile
{
    /// <summary><c>CTFBaseRocket::PhysicsSolidMaskForEntity</c> (tf_weaponbase_rocket.cpp:354-369):
    /// no <c>CONTENTS_HITBOX</c>, so the sweep finds a player's hull. A hitbox ray let
    /// a rocket through a spy's or a scout's arm's width of a heavy's.</summary>
    protected override TraceMask SweepMask => TraceMask.Solid;

    private readonly TFBlastApplier Blast;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Hooks;
    private readonly IEnginePlayers Players;
    private readonly ITempEnts Temps;
    private readonly IEnginePrecache Precache;
    private readonly IScreenEffects Screen;
    private readonly IEngineSound Sound;

    public TFProjectileEnergyBall(IEngineEntityPhysics physics, EntityMotion motion, TFBlastApplier blastApplier,
                                  ITFWeaponOwners owners, AttributeHooks attributes, IEnginePlayers players,
                                  ITempEnts temps, IEnginePrecache precache, IScreenEffects screen,
                                  IEngineSound sound)
        : base(physics, motion)
    {
        Blast = blastApplier;
        Owners = owners;
        Hooks = attributes;
        Players = players;
        Temps = temps;
        Precache = precache;
        Screen = screen;
        Sound = sound;
    }

    protected override float GravityScale => TFProjectileDefs.EnergyGravity;

    protected override string ProjectileModel => TFProjectileDefs.EnergyModel;

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>The stored shot the launcher dealt.</summary>
    public float ShotDamage { get; private set; }

    /// <summary>The held button's charge rode the shot (<c>m_bChargedShot</c>, tf_projectile_energy_ball.h:56).</summary>
    public bool Charged { get; private set; }

    private int _ownerIndex;

    /// <summary>Arms the ball with the launcher's stored hit and whether the shot was charged.</summary>
    public void ArmedBy(float damage, TFWeaponId weapon, string weaponName, bool critical, bool charged, int ownerIndex)
    {
        ShotDamage = damage;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        Charged = charged;
        _ownerIndex = ownerIndex;
    }

    /// <summary>
    /// The item's airblast refusal: <c>energy_weapon_no_deflect</c> on the
    /// owner's item. Stock's header answers the deflectable true for every ball
    /// (<c>tf_projectile_energy_ball.h:53</c>); the attribute is how an item
    /// carries the ring's refusal onto the deflectable kind.
    /// </summary>
    public bool RefusesDeflect() =>
        Hooks.Int(0, "energy_weapon_no_deflect", Owners.ItemOf(_ownerIndex, WeaponName)) != 0;

    /// <summary>
    /// The deflection: the steered flight along the deflected line, then the
    /// ownership the stock <c>Deflected</c> carries
    /// (<c>tf_projectile_energy_ball.cpp:216-241</c>) — the deflector as
    /// owner, their active weapon as launcher, the crit under their boost.
    /// </summary>
    public void DeflectBy(EntityHandle deflector, Vector velocity, bool deflectorCritBoosted, string launcherName)
    {
        Steer(velocity, GravityScale);
        Retarget(deflector);
        _ownerIndex = Entities.GetIndex(deflector);
        WeaponName = launcherName;
        if (deflectorCritBoosted)
            Critical = true;
    }

    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return;

        // Explode (:259-337): the touched enemy is saved first (:268) and the
        // blast scores it at distance zero; the explosion particle and — the
        // charged ball's alone — the screen shake (:289-292) go out at the
        // ball's own origin and centre before anything is dealt. The item's
        // building refusal is not stock's: the attribute has no reader.
        Vector at = Origin;

        ExplodeParticle(at);

        if (Charged)
            Screen.Shake(Entities.GetWorldSpaceCenter(Handle), 25.0f, 150.0f, 1.0f, 750f);

        // The blast (:306-325): the radius under the launcher's
        // mult_explosion_radius as the rocket's reads it, the touched victim
        // scored first, and the attacker's own pass at the rocket-jump radius
        // — 1.33 times it charged (:323). The hit carries the ball's own
        // bits and plasma custom (:365-390).
        IAttributeProvider? item = Owners.ItemOf(_ownerIndex, WeaponName);
        float radius = Hooks.Float(TFProjectileDefs.RocketRadius, "mult_explosion_radius", item);
        Blast.Apply(at, TFBlast.Of(ShotDamage, radius, BlastBits()), Attribution, Handle,
                    ignored: Handle, Weapon, WeaponName, Charged ? false : Critical, hit,
                    TFProjectileDefs.RocketRadiusForRocketJumps * (Charged ? 1.33f : 1f), ShotDamage,
                    Charged ? TFDamageCustom.PlasmaCharged : TFDamageCustom.Plasma);

        if (DrainVictim(hit, item))
            DrainFeedback(hit);
    }

    /// <summary>
    /// The hit's bits (<c>GetDamageType</c>, :365-382): blast with the half
    /// falloff and the distance mod always; the charged ball ignites instead
    /// of carrying the crit bit, the plain one takes the crit bit with its
    /// shot. The plain blast matches the launcher's row in
    /// <see cref="TFWeaponDamageTypes"/>.
    /// </summary>
    private DamageType BlastBits()
    {
        DamageType bits = DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.UseDistanceMod;
        if (Charged)
            bits |= TFDamageBits.Ignite;
        else if (Critical)
            bits |= TFDamageBits.Critical;
        return bits;
    }

    /// <summary>
    /// The oriented explosion particle (:281-286, :342-352): the charge picks
    /// the core name, the owner's team the blue suffix — the same dispatch
    /// shape the rocket's stun shockwave keeps.
    /// </summary>
    private void ExplodeParticle(Vector at)
    {
        string name = Charged ? "drg_cow_explosioncore_charged" : "drg_cow_explosioncore_normal";
        if (Players.GetTeam(Players.GetHandle(_ownerIndex)) != (int)TFTeam.Red)
            name += "_blue";

        TeEffectData effect = new()
        {
            Scale = 1f,
            HitBox = Precache.ParticleSystemIndex(name),
            Origin = at,
            Start = at,
        };
        Temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in effect);
    }

    /// <summary>
    /// The pomson's two on-hit drains on the ball's touch too, the same arms
    /// as the ring's <see cref="TFProjectileEnergyRing"/> — stock's
    /// <c>ApplyPostHitEffects</c> is called by the firing weapon for either
    /// energy projectile (<c>tf_weaponbase.cpp:5553-5613</c>). Answers whether
    /// a meter moved, as the ring's does.
    /// </summary>
    private bool DrainVictim(EntityHandle hit, IAttributeProvider? item)
    {
        int medicCharge = Hooks.Int(0, "subtract_victim_medigun_charge_onhit", item);
        int cloak = Hooks.Int(0, "subtract_victim_cloak_on_hit", item);
        if (medicCharge <= 0 && cloak <= 0)
            return false;

        Vector to = Entities.GetOrigin(hit) - Entities.GetOrigin(Attribution);
        float scale = TFDamageModifyRules.RemapValClamped(to.X * to.X + to.Y * to.Y + to.Z * to.Z,
                                                          512f * 512f, 1536f * 1536f, 1f, 0f);

        return Owners.DrainOnHitMeters(Entities.GetIndex(hit),
                                       medicCharge > 0 ? medicCharge / 100f * scale : 0f,
                                       cloak > 0 ? cloak / 100f * scale : 0f);
    }

    /// <summary>The drain's telling, the ring's <see cref="TFProjectileEnergyRing"/> word for word.</summary>
    private void DrainFeedback(EntityHandle victim)
    {
        if (Owners.ConditionsOf(Entities.GetIndex(victim)) is { } seen
            && (seen.Has(TFCond.Disguised) || seen.Has(TFCond.Stealthed)))
            return;

        bool red = Players.GetTeam(Players.GetHandle(_ownerIndex)) == (int)TFTeam.Red;
        Vector at = Entities.GetWorldSpaceCenter(victim);
        TeEffectData drain = new()
        {
            Scale = 1f,
            HitBox = Precache.ParticleSystemIndex(TFProjectileDefs.EnergyDrainEffect),
            Origin = at,
            Start = at,
            CustomColors = true,
            Color1 = red ? TFProjectileDefs.ParticleWeaponRed1 : TFProjectileDefs.ParticleWeaponBlue1,
            Color2 = red ? TFProjectileDefs.ParticleWeaponRed2 : TFProjectileDefs.ParticleWeaponBlue2,
        };
        Temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in drain);
        Sound.EntityEmitSound(victim, TFProjectileDefs.EnergyDrainSound);
    }
}

/// <summary>
/// The medic's healing bolt (<c>CTFProjectile_HealingBolt</c>,
/// <c>game/server/tf/tf_projectile_arrow.cpp:1186</c>, the arrow's flight
/// under the crossbow's fixed charge): the syringe's model at three times
/// the scale (<c>Spawn</c>, :213-217), flying at
/// <see cref="TFProjectileDefs.CrossbowSpeed"/> under
/// <see cref="TFProjectileDefs.CrossbowGravity"/>.
///
/// <para>
/// The first touch falls either way of the team, stock's
/// <c>StrikeTarget</c> split (:478-545): an enemy takes the shot — the ramp
/// <c>GetDamage</c> rides (:315-324, half strength at birth, full a sixth of
/// a second in) — and a teammate player the heal (<c>ImpactTeamPlayer</c>,
/// :1198-1289): the blocked-healing refusal, double the ramped shot under
/// the victim's heal mult and active weapon's penalty, health to the class
/// max, the impact cue, the shooter's heal stat and the three events
/// (<c>player_healed</c>, <c>player_healonhit</c>, <c>crossbow_heal</c>),
/// the shooter's medigun uber at the healed-per-second rate the
/// time-since-damage scales, and the overheal cond. Not here
/// (docs/plans/tf2/deferred.tsv): the kill-eater tick of :1288 — stock's
/// <c>EconEntity_OnOwnerKillEaterEvent_Batched</c>, whose kill-eater
/// machinery this build does not carry yet; the
/// <c>arrow_heals_buildings</c> arm, whose friendly buildings the damage
/// pass already serves as the arrow's do.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.HealingBolt, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.Sound, HealSound)]
public sealed class TFHealingBolt : FlyingProjectile
{
    /// <summary>The heal's cue, on the bolt (<c>ImpactSound</c>, :1235).</summary>
    internal const string HealSound = "Weapon_Arrow.ImpactFleshCrossbowHeal";

    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;
    private readonly IEnginePlayers Players;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Hooks;
    private readonly ITFPlayerConditions Conditions;
    private readonly ITFMedigunWeapons Mediguns;
    private readonly ITFPlayerResupply Resupply;
    private readonly ITFPickupNotices Notices;
    private readonly ITFPlayerStats Stats;
    private readonly IEngineSound Sound;
    private readonly IGameClock Clock;
    private readonly IEngineFrame Frame;
    private readonly TFDamageRecords Records;

    public TFHealingBolt(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                         TFDamageScopes scopes, DamageHitGroups hitGroups, IEnginePlayers players,
                         ITFWeaponOwners owners, AttributeHooks hooks, ITFPlayerConditions conditions,
                         ITFMedigunWeapons mediguns, ITFPlayerResupply resupply, ITFPickupNotices notices,
                         IEngineSound sound, IGameClock clock, IEngineFrame frame, TFDamageRecords records,
                         ITFPlayerStats stats)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Players = players;
        Owners = owners;
        Hooks = hooks;
        Conditions = conditions;
        Mediguns = mediguns;
        Resupply = resupply;
        Notices = notices;
        Stats = stats;
        Sound = sound;
        Clock = clock;
        Frame = frame;
        Records = records;
    }

    protected override float GravityScale => TFProjectileDefs.CrossbowGravity;

    protected override string ProjectileModel => TFProjectileDefs.SyringeModel;

    public override void OnSpawn()
    {
        base.OnSpawn();
        SetModelScale(3f, 0f);
    }

    /// <summary>What the client's trail mirror reads.</summary>
    [NetState] public TFHealingBoltNetState State = new();

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>The shot the crossbow dealt, before <see cref="Ramped"/>'s life scale.</summary>
    public float ShotDamage { get; private set; }

    private float _createdAt;
    private int _ownerIndex;

    /// <summary>The shot the crossbow dealt, and whose it is.</summary>
    public void ArmedBy(float damage, TFWeaponId weapon, string weaponName, bool critical, int ownerIndex)
    {
        ShotDamage = damage;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        _ownerIndex = ownerIndex;

        // The trail's team, as the flare's ArmedBy writes its own; the ramp's
        // age runs from here, the launch frame the spawn stands in.
        State.TeamNumber = Players.GetTeam(Players.GetHandle(ownerIndex));
        _createdAt = Clock.Now;
    }

    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return;

        // The arrow's split (:478-545): the owner's team heals, everyone
        // else takes the shot. A friendly building gets the same blow here and
        // refuses it in TFBaseObject.OnDamaged, which leaves it as stock does.
        float damage = Ramped;
        if (Entities.IsPlayer(hit) && Players.GetTeam(hit) == State.TeamNumber)
        {
            Heal(hit, damage);
            return;
        }

        // The crossbow's own bits (g_aWeaponDamageTypes); the bolt no-headshots
        // (:301-306) and the crit flag rides, as everywhere in this port.
        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(TFDamageCustom.None, Weapon, WeaponName))
            Damage.TakeDamage(hit, Attribution, damage, TFWeaponDamageTypes.Of(Weapon));
    }

    /// <summary>The shot under <c>GetDamage</c>'s life ramp (:315-324).</summary>
    private float Ramped
        => ShotDamage * TFDamageModifyRules.RemapValClamped(Clock.Now - _createdAt, 0f, 0.6f, 0.5f, 1.0f);

    /// <summary><c>ImpactTeamPlayer</c> (:1198-1289), the heal arm.</summary>
    private void Heal(EntityHandle victim, float damage)
    {
        int victimIndex = victim.EntryIndex;

        // Don't heal players using a weapon that blocks healing (:1208-1216).
        IAttributeProvider? active = Owners.ActiveItemOf(victimIndex);
        if (active is not null && Hooks.Int(0, "weapon_blocks_healing", active) != 0)
            return;

        // Double the shot, the victim's own heal mult over it and the active
        // weapon's penalty with it (:1218-1228) — the ON_OTHER hooks walk the
        // player's attribute manager and the held item, as everywhere here.
        float heal = damage * 2f;
        heal = Hooks.Float(heal, "mult_healing_from_medics", Owners.ManagerOf(victimIndex));
        if (active is not null)
            heal = Hooks.Float(heal, "mult_health_fromhealers_penalty_active", active);

        // DMG_GENERIC's TakeHealth: up to the class max, never past it (:1230).
        int healed = Resupply.TakeHealth(victim, heal);
        if (healed <= 0)
            return;

        Sound.EntityEmitSound(Handle, HealSound);

        // Stock books the heal before the events (:1237): the shooter's own
        // heal stat, under the bogus-value refusal of tf_gamestats.cpp:719-727.
        int reported = (int)heal;
        if (reported is >= 0 and <= 1000)
            Stats.Increment(Players.GetHandle(_ownerIndex), TFStat.Healing, reported);

        // All three events carry stock's flHealth, the ask rather than the taken
        // (:1248, :1255, :1271).
        Notices.PlayerHealed(victim, Players.GetHandle(_ownerIndex), reported);
        Notices.PlayerHealOnHit(victim, reported);
        Notices.CrossbowHeal(Players.GetHandle(_ownerIndex), victim, reported);

        // The uber (:1275-1285): the medigun's heal rate, twenty-four a
        // second, scaled by the wound's age — the fresher the slower, three
        // at ten seconds, one at fifteen — across the frame the touch stands
        // in. A player never hit stands outside every scale, stock's zero
        // stamp answering huge.
        float sinceDamage = Records.LastHit(victimIndex) is { } record ? Clock.Now - record.Time : float.PositiveInfinity;
        float scale = TFDamageModifyRules.RemapValClamped(sinceDamage, 10f, 15f, 3f, 1f);
        Mediguns.SetChargeLevel(_ownerIndex, Mediguns.ChargeLevel(_ownerIndex) + healed / (24f * scale) * Frame.GetFrameTime());

        // And the cond, a tenth plus a tenth (:1286).
        Conditions.Add(victim, TFCond.HealthOverhealed, 1.2f);
    }
}

/// <summary>
/// The Dragon's Fury's ball (<c>CTFProjectile_BallOfFire</c>,
/// <c>tf_projectile_dragons_fury.cpp:43</c>): an invisible trigger-bounded
/// sphere at the empty model (<c>:61, 167</c>), flying flat at
/// <see cref="TFProjectileDefs.BallSpeed"/> (<c>:83-84</c>), and on its combat
/// touch the burn arm (:262-431): a teammate player only lights the Sniper's
/// held bow (:241-260); an enemy takes the shot with the ignite bit
/// (<c>:297</c>), three times over on a victim already burning
/// (<c>:337-340</c>), and every non-player combat target takes the bonus by
/// default (:358-360); then the launcher's meter refill gets its once-per-ball
/// boost while the bar still runs (:226-237, :296-310). The burn it leaves is
/// the stock pair's — half as long on a pyro (:344-348). Not here
/// (docs/plans/tf2/deferred.tsv): the distance-limit and lifetime thinks
/// (<c>:92-103</c>, the caps riding <see cref="TFProjectileDefs.BallDistance"/>
/// and <see cref="TFProjectileDefs.BallLifetime"/> until the flight seam
/// carries them), the alive and visibility tests (:286-290), the forward
/// hitbox trace of the bonus test (:330-333), and the impact sounds.
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.BallOfFire, ScriptedEntityKind.Projectile)]
public sealed class TFBallOfFire : FlyingProjectile
{
    /// <summary><c>CTFBaseRocket::PhysicsSolidMaskForEntity</c> (tf_weaponbase_rocket.cpp:354-369):
    /// no <c>CONTENTS_HITBOX</c>, so the sweep finds a player's hull. A hitbox ray let
    /// a rocket through a spy's or a scout's arm's width of a heavy's.</summary>
    protected override TraceMask SweepMask => TraceMask.Solid;

    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;
    private readonly IEnginePlayers Players;
    private readonly ITFWeaponOwners Owners;
    private readonly ITFPlayerConditions Conditions;
    private readonly IEntityDirectory Directory;

    private readonly HashSet<int> _hits = new();
    private bool _refunded;

    public TFBallOfFire(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                        TFDamageScopes scopes, DamageHitGroups hitGroups, IEnginePlayers players,
                        ITFWeaponOwners owners, ITFPlayerConditions conditions,
                        IEntityDirectory directory)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Players = players;
        Owners = owners;
        Conditions = conditions;
        Directory = directory;
    }

    protected override float GravityScale => 0f;                       // MOVETYPE_FLY (:73)

    protected override string ProjectileModel => TFProjectileDefs.BallModel;

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>The shot the launcher dealt, under the item's hook (:86-88).</summary>
    public float ShotDamage { get; private set; } = TFProjectileDefs.BallBaseDamage;

    /// <summary>The launcher's deal: what the shot is worth, whose it is, and its crit.</summary>
    public void ArmedBy(float damage, TFWeaponId weapon, string weaponName, bool critical, int ownerIndex)
    {
        ShotDamage = damage;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
    }

    /// <summary>
    /// The combat touch (<c>Burn</c> :262-431): the hit-list dedup (:277-281),
    /// the team split, the bonus weighing before the ignite (<c>:350</c>'s own
    /// order), the hurt with the ignite bit and the Fury customs (:297, :340),
    /// the burn cond, and the launcher's once-per-ball refund behind the hurt
    /// (:364). The shooter's own entity is skipped (:189-192).
    /// </summary>
    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None || hit == Shooter)
            return;

        if (!_hits.Add(hit.EntryIndex))
            return;

        // InSameTeam's two arms (:268-275): the teammate player only lights a
        // bow, and nothing refunds from a friendly touch.
        if (Entities.IsPlayer(hit) && Players.GetTeam(hit) == Players.GetTeam(Attribution))
        {
            LightTeammatesBow(hit);
            return;
        }

        // The bonus: burning on the player arm (:337-338), always on the
        // non-player one (:358-359). The forward hitbox trace of the player's
        // second half waits with the flight seam.
        bool bonus = !Entities.IsPlayer(hit) || Conditions.InCond(hit, TFCond.Burning);
        float damage = ShotDamage * (bonus ? TFProjectileDefs.BallBurnBonus : 1f);

        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(bonus ? TFDamageCustom.DragonsFuryBonusBurning : TFDamageCustom.DragonsFuryIgnite,
                           Weapon, WeaponName))
            Damage.TakeDamage(hit, Attribution, damage,
                              TFWeaponDamageTypes.Of(Weapon) | TFDamageBits.Ignite);

        if (Entities.IsPlayer(hit))
        {
            // Burn(...) at the convar's seconds, half on a pyro (:342-351).
            float burn = Owners.ClassOf(hit.EntryIndex) == TFClass.Pyro
                ? TFProjectileDefs.BallBurnDuration * 0.5f
                : TFProjectileDefs.BallBurnDuration;
            Conditions.Add(hit, TFCond.Burning, burn, Attribution);
        }

        RefundLauncher();
    }

    /// <summary>
    /// <c>OnCollideWithTeammate</c> (:241-260): the Sniper's held bow lights,
    /// once per projectile through the same hit list — the dedup above already
    /// booked the entry. The flag rides the carried weapon's state, the flame
    /// tick's own bow arm's door.
    /// </summary>
    private void LightTeammatesBow(EntityHandle victim)
    {
        if (Owners.ClassOf(victim.EntryIndex) != TFClass.Sniper)
            return;

        if (TFWeaponNames.IdOf(Players.GetActiveWeapon(victim)) != TFWeaponId.CompoundBow)
            return;

        if (Directory.Get(Players.GetActiveWeaponEntity(victim)) is SsWeapon { State: { } state })
            state.ArrowAlight = true;
    }

    /// <summary>
    /// The launcher's refund (:226-237, :296-302): once for the whole ball, and
    /// only while the bar still runs — a full meter answers nothing, stock's
    /// first gate (:298-299). The boost rides the launcher's own predicted
    /// state, the same door the bow-light arm writes.
    /// </summary>
    private void RefundLauncher()
    {
        if (_refunded)
            return;

        if (Directory.Get(Players.GetActiveWeaponEntity(Shooter)) is not SsWeapon { State: { } state })
            return;

        _refunded = true;

        if (state.ItemMeter >= 1f)
            return;

        state.FlameBallChargeScale = TFItemMeter.HitRechargeBoost;
    }
}

/// <summary>
/// The pyro's flare (<c>CTFProjectile_Flare</c>, <c>tf_projectile_flare.cpp</c>
/// over the nail base's flight): the arc at <see cref="TFProjectileDefs.FlareGravity"/>,
/// and on its first touch the shot at <see cref="TFProjectileDefs.FlareSpeed"/>'s
/// class — a victim already burning takes it as a crit, the flag set before the
/// damage so the hit itself carries it (<c>Explode</c>, :340-347), every hit
/// through <c>TF_DMG_CUSTOM_BURNING_FLARE</c> (:346). A player hit is gone at
/// once (:370-376); anything else sticks where it landed for a tenth of a
/// second, unseen, then goes (:350-359, ImpactThink :478-498) — the seam's
/// linger, where stock's stays drawn the tenth.
///
/// <para>
/// The detonating modes go instead at <see cref="Detonate"/> (<c>Explode_Air</c>
/// through <c>Detonate</c>, :382-447): the world-struck flare of a Detonator
/// or Scorch Shot blasts where it landed rather than sticking (:362-366, the
/// Scorch's arm full-radius, the Detonator's own radius none), the alt-fire
/// and the impact sharing the one blast — half falloff, the flare custom, the
/// owner's own pass always armed at <see cref="TFProjectileDefs.FlareJumpRadius"/>
/// (:419-421, <c>TF_FLARE_RADIUS_FOR_FJS</c>). The burning-player crit is the
/// plain flare's alone (:341's <c>!bDetonate &amp;&amp; !bNoRandomCrit</c>).
/// </para>
///
/// <para>
/// Not here (docs/plans/tf2/deferred.tsv): the spawn's eye-to-flare brush trace that
/// fizzles a flared-through-wall (the same gap as the launch trace) and the
/// impact sounds and effects; the heat-seek and the taunt flag.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Flare, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.Sound, TFFlare.ScorchImpactSound)]
public sealed class TFFlare : FlyingProjectile
{
    /// <summary><c>CTFBaseRocket::PhysicsSolidMaskForEntity</c> (tf_weaponbase_rocket.cpp:354-369):
    /// no <c>CONTENTS_HITBOX</c>, so the sweep finds a player's hull. A hitbox ray let
    /// a rocket through a spy's or a scout's arm's width of a heavy's.</summary>
    protected override TraceMask SweepMask => TraceMask.Solid;

    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;
    private readonly ITFPlayerConditions Conditions;
    private readonly IEnginePlayers Players;
    private readonly TFBlastApplier Blast;
    private readonly AttributeHooks Attributes;
    private readonly ITFWeaponOwners Owners;
    private readonly TFFlareTray Tray;
    private readonly TFPlayerCarriers Carriers;
    private readonly IEngineSound Sound;
    private readonly IUniformRandom Random;

    private float _gravity = TFProjectileDefs.FlareGravity;
    private Vector _spin = Vector.Zero;

    public TFFlare(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                   TFDamageScopes scopes, DamageHitGroups hitGroups,
                   ITFPlayerConditions conditions, IEnginePlayers players,
                   TFBlastApplier blast, AttributeHooks attributes, ITFWeaponOwners owners,
                   TFFlareTray tray, TFPlayerCarriers carriers, IEngineSound sound,
                   IUniformRandom random)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Conditions = conditions;
        Players = players;
        Blast = blast;
        Attributes = attributes;
        Owners = owners;
        Tray = tray;
        Carriers = carriers;
        Sound = sound;
        Random = random;
    }

    protected override float GravityScale => _gravity;

    protected override string ProjectileModel => TFProjectileDefs.FlareModel;

    /// <summary>What the client's trail mirror reads.</summary>
    [NetState] public TFFlareNetState State = new();

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>
    /// The firing gun's mode, stock's <c>GetFlareGunType</c> read at the fire
    /// (<c>FlareGunTypes_t</c>, <c>tf_weapon_flaregun.h:23-29</c>): the
    /// impact's stick-or-blast and the burning crit both ask it.
    /// </summary>
    public int FlareMode { get; private set; }

    /// <summary>The shot the launcher dealt, what the touch is worth.</summary>
    public float ShotDamage { get; private set; }

    public void ArmedBy(float damage, TFWeaponId weapon, string weaponName, bool critical, float gravity,
                        int ownerIndex, int flareMode)
    {
        ShotDamage = damage;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        FlareMode = flareMode;
        OwnerIndex = ownerIndex;
        _gravity = gravity;

        // The trail's pair, as the rocket's ArmedBy writes its own: the
        // owner's team and the crit the shot carries.
        State.Critical = critical;
        State.TeamNumber = Players.GetTeam(Players.GetHandle(ownerIndex));
    }

    private int OwnerIndex { get; set; }

    /// <summary>
    /// The flare leaves the launcher's list with itself
    /// (<c>SendDeathNotice</c> → <c>DeathNotice</c> → <c>FindAndRemove</c>,
    /// <c>tf_projectile_flare.cpp:462-473</c>,
    /// <c>tf_weapon_flaregun.cpp:191-200</c>): the tray's unregister is this
    /// port's notice, so no removed flare is left a handle there.
    /// </summary>
    public override void OnRemoved() => Tray.Unregister(OwnerIndex, this);

    /// <summary>
    /// The blast (<c>Detonate</c> → <c>Explode_Air</c>,
    /// <c>tf_projectile_flare.cpp:436-447, :382-431</c>): the shot's damage
    /// over <see cref="GetRadius"/> unless it is the owner's arm alone, the
    /// weapon's own bits under half falloff — the blast bit when the self
    /// arm is the whole of it, stock's <c>| DMG_BLAST</c> (:415) — and the
    /// flare custom. The owner's own pass rides every blast at
    /// <see cref="TFProjectileDefs.FlareJumpRadius"/> for the base damage, as
    /// stock's <c>flRJRadius</c> arms it (:419-421, :5661-5714).
    /// </summary>
    public void Detonate(bool selfOnly)
    {
        BlastAt(selfOnly);
        Remove();
    }

    /// <summary>The blast alone: the touch's arm, which <see cref="Spend"/>'s tail removes after.</summary>
    private void BlastAt(bool selfOnly)
    {
        DamageType bits = TFWeaponDamageTypes.Of(Weapon) | TFDamageBits.HalfFalloff;
        if (selfOnly)
            bits |= DamageType.Blast;

        Blast.Apply(Origin, TFBlast.Of(ShotDamage, selfOnly ? 0f : GetRadius(), bits),
                    Attribution, Handle, ignored: Handle, Weapon, WeaponName, Critical,
                    directVictim: EntityHandle.None,
                    selfRadius: TFProjectileDefs.FlareJumpRadius, selfBaseDamage: ShotDamage,
                    custom: TFDamageCustom.FlareExplosion);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Flare} detonated at {Origin}\n");
    }

    /// <summary>The blast's reach (<c>GetRadius</c>, <c>tf_projectile_flare.cpp:452-457</c>).</summary>
    private float GetRadius() =>
        Attributes.Float(TFProjectileDefs.FlareDetRadius, "mult_explosion_radius",
                         Owners.ItemOf(OwnerIndex, WeaponName));

    protected override void Impact(EntityHandle hit)
    {
        if (hit != EntityHandle.None)
        {
            // The Scorch Shot's player arm (:228-296) ends in a return of its
            // own — the hurt, the shove, the bounce — and never reaches the
            // common damage below.
            if (FlareMode == TFProjectileBehaviour.FlareScorch && Entities.IsPlayer(hit))
            {
                ScorchHit(hit);
                return;
            }

            // Flares that hit a burning player crit (:340-344): before the
            // damage, so this very hit carries the flag. A detonating mode's
            // does not — :341's !bDetonate && !bNoRandomCrit, every mode past
            // the plain one.
            bool plain = FlareMode == TFProjectileBehaviour.FlareNormal;
            if (plain && Conditions.InCond(hit, TFCond.Burning))
                Critical = true;

            using (HitGroups.Push(HitGroup.Generic, Critical))
            using (Scopes.Push(TFDamageCustom.BurningFlare, Weapon, WeaponName))
                // The flare gun's own bits (g_aWeaponDamageTypes): a bullet
                // that ignites what it lands on.
                Damage.TakeDamage(hit, Attribution, ShotDamage, TFWeaponDamageTypes.Of(Weapon));
        }

        // A player hit is the end (:370-376). The world's is the stick —
        // unless the gun detonates its flares (:362-366): the blast is the
        // stick's replacement, the Scorch's full-radius, the Detonator's
        // bare. The Detonate's Remove is the end stock's ImpactThink never
        // reaches, its think cleared under the removal (:441).
        if (Entities.IsPlayer(hit))
            return;

        if (FlareMode is TFProjectileBehaviour.FlareDetonate or TFProjectileBehaviour.FlareScorch)
        {
            // The blast of Detonate (:362-366 through :436-447) without its
            // removal: the touch's own is the one the shell grants, stock's
            // single UTIL_Remove inside Explode_Air (:444).
            BlastAt(selfOnly: FlareMode != TFProjectileBehaviour.FlareScorch);
            return;
        }

        Linger(TFProjectileDefs.FlareStickTime);
    }

    /// <summary>The Scorch Shot's impact sound (:287).</summary>
    internal const string ScorchImpactSound = "Rubber.BulletImpact";

    /// <summary>
    /// The Scorch Shot's player hit (<c>Explode</c>, <c>tf_projectile_flare.cpp:228-296</c>):
    /// the shot without its push force (:243 — the shove below is the whole of
    /// it), then, for an enemy not immune to pushback, the knockback along the
    /// flight's line — 400 into a burning victim, 100 otherwise (:264) — with
    /// the ground stun when they are not already airborne (:259-262). The
    /// flare itself survives as debris, almost stopped, popping up to 100 u/s
    /// under a random tilt (:268-284), audible (:287).
    /// </summary>
    private void ScorchHit(EntityHandle victim)
    {
        Vector velocity = Entities.GetVelocity(Handle);

        // Now it only collides with the world (:234).
        Physics.SetCollisionGroup(Handle, CollisionGroup.Debris);

        bool burning = Conditions.InCond(victim, TFCond.Burning);

        // The taunted hit's TF_DMG_CUSTOM_FLARE_PELLET (:246) waits on the
        // taunt flag (docs/plans/tf2/deferred.tsv, with the heat-seek).
        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(TFDamageCustom.None, Weapon, WeaponName))
            Damage.TakeDamage(victim, Attribution, ShotDamage,
                              TFWeaponDamageTypes.Of(Weapon) | DamageType.PreventPhysicsForce);

        int attackerTeam = Players.GetTeam(Attribution);
        if (!Conditions.InCond(victim, TFCond.ImmuneToPushback)
            && attackerTeam != Players.GetTeam(victim))
        {
            Vector along = velocity.Normalized;
            Vector direction = new(along.X, along.Y, 1f);

            if (!Conditions.InCond(victim, TFCond.KnockedIntoAir))
                Carriers.Part<TFStunStack>(victim.EntryIndex)?.Stun(victim, ScorchStunTime, ScorchStunStrength,
                                                                   TFStunFlags.Movement, Attribution);

            Entities.ApplyVelocityImpulse(victim, direction * (burning ? ScorchPushBurning : ScorchPush),
                                          clearGround: false);
        }

        // Almost all of its speed gone, popping into the air (:268-272); the
        // new facing rides the keep-flying's own VectorAngles (:275-277).
        Vector bounce = new Vector(velocity.X * ScorchBounceKeep, velocity.Y * ScorchBounceKeep, ScorchPopSpeed)
            + new Vector(Jitter(), Jitter(), Jitter());
        _spin = new Vector(SignedSpin(), SignedSpin(), SignedSpin());
        KeepFlying(bounce, _gravity);
        Sound.EntityEmitSound(Handle, ScorchImpactSound);
    }

    /// <summary>One axis of the bounce's RandomVector(-2, 2) (:272).</summary>
    private float Jitter() => Random.RandomFloat(-ScorchJitter, ScorchJitter);

    /// <summary>
    /// One axis of the tumble, RandomAngle(180, 720) signed by a coin per
    /// axis (:279-282).
    /// </summary>
    private float SignedSpin() => Random.RandomFloat(180f, 720f) * (Random.RandomInt(0, 1) == 0 ? 1f : -1f);

    /// <summary>The flare points as it tumbles (<c>LaunchSpin</c> rides every launch).</summary>
    protected override Vector LaunchSpin => _spin;

    /// <summary>The shove, burning victim (:264).</summary>
    private const float ScorchPushBurning = 400f;

    /// <summary>The shove, everyone else (:264).</summary>
    private const float ScorchPush = 100f;

    /// <summary>The half of the speed the bounce keeps (:269-270).</summary>
    private const float ScorchBounceKeep = 0.07f;

    /// <summary>The pop's upward speed (:271).</summary>
    private const float ScorchPopSpeed = 100f;

    /// <summary>The pop's per-axis jitter, RandomVector's bound (:272).</summary>
    private const float ScorchJitter = 2f;

    /// <summary>The ground stun, StunPlayer's 0.5 s (:261).</summary>
    private const float ScorchStunTime = 0.5f;

    /// <summary>The ground stun's strength (:261).</summary>
    private const float ScorchStunStrength = 1f;
}

/// <summary>
/// A Detonator's live flares, in the order they were fired — stock's
/// <c>m_Flares</c> on the gun (<c>CTFFlareGun::AddFlare</c>,
/// <c>tf_weapon_flaregun.cpp:179-186</c>, the list kept only by the
/// detonating mode, <c>tf_weaponbase_gun.cpp:760-763</c>). The alt-fire
/// detonates the lot (<c>SecondaryAttack</c>, :147-173) and a flare the
/// world takes leaves through <see cref="TFFlare.OnRemoved"/> — the
/// <c>DeathNotice</c> the port's per-owner trays stand in for, as the
/// stickybomb launcher's does (<see cref="TFStickyTray"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFFlareTray
{
    private readonly Dictionary<int, List<TFFlare>> _trays = new();

    public void Register(int ownerIndex, TFFlare flare)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            _trays[ownerIndex] = tray = new List<TFFlare>();
        tray.Add(flare);
    }

    /// <summary>The <c>FindAndRemove</c> of <c>DeathNotice</c> (:191-200).</summary>
    public void Unregister(int ownerIndex, TFFlare flare)
    {
        if (_trays.TryGetValue(ownerIndex, out var tray))
            tray.Remove(flare);
    }

    /// <summary>
    /// The alt-fire (<c>SecondaryAttack</c>, <c>tf_weapon_flaregun.cpp:160-171</c>):
    /// every live flare detonates, whole-radius — stock walks the list as it
    /// stands, each <c>Detonate</c>'s own removal already seen to here.
    /// </summary>
    public void DetonateAll(int ownerIndex)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            return;

        for (int i = tray.Count - 1; i >= 0; i--)
            tray[i].Detonate(selfOnly: false);

        tray.Clear();
    }
}

/// <summary>
/// A launcher's stickies, in the order they were fired — stock's eight-slot
/// <c>m_Pipebombs</c> (<c>CTFPipebombLauncher::PrimaryAttack</c>,
/// <c>tf_weapon_pipebomblauncher.cpp</c>). The ninth arrival removes the
/// oldest, the launcher's <c>SecondaryAttack</c> detonates the lot, a sticky
/// the engine removes reports out through its <see cref="TFStickybomb.OnRemoved"/>
/// (stock's <c>UpdateOnRemove</c> → <c>DeathNotice</c> → <c>FindAndRemove</c>,
/// <c>tf_weapon_pipebomblauncher.cpp:508-517</c>), and an owner's death —
/// stock's launcher removal, whose <c>UpdateOnRemove</c> is the dying-owner
/// fizzle (:479-486) — fizzles the whole tray.
/// </summary>
[SideService(Side.Server)]
public sealed class TFStickyTray(TFPlayerCarriers carriers) : IPlayerDeathHook, IOrderedHook
{
    /// <summary>Its own place on the death walk (<see cref="TFPlayerOrders.StickyTray"/>).</summary>
    public int HookOrder => TFPlayerOrders.StickyTray;

    private readonly Dictionary<int, List<TFStickybomb>> _trays = new();

    private readonly TFPlayerCarriers _carriers = carriers;

    /// <summary>
    /// Records a sticky and removes the oldest past the cap — the stock
    /// <see cref="TFProjectileDefs.MaxStickies"/>, or the launch's
    /// <c>add_max_pipebombs</c> total the caller computed through the hook
    /// (<c>tf_weapon_pipebomblauncher.cpp:381-392</c>, the cap through
    /// <c>ATTRIB_HOOK_FLOAT</c> over the launcher's item).
    /// </summary>
    public void Register(int ownerIndex, TFStickybomb sticky,
                         int maxStickies = TFProjectileDefs.MaxStickies)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            _trays[ownerIndex] = tray = new List<TFStickybomb>();

        // The bomb's half of the firing: stock's Create gives it m_hLauncher
        // and PrimaryAttack adds it to the list; the removal notice below
        // comes back through this pair.
        sticky.EnrolledIn(this, ownerIndex);
        tray.Add(sticky);
        if (tray.Count > maxStickies)
        {
            TFStickybomb oldest = tray[0];
            tray.RemoveAt(0);
            oldest.Detonate();
        }

        Publish(ownerIndex);
    }

    /// <summary>
    /// The <c>DeathNotice</c>'s <c>FindAndRemove</c>
    /// (<c>tf_weapon_pipebomblauncher.cpp:508-517</c>), the far side of
    /// <see cref="TFStickybomb.OnRemoved"/>: a sticky the engine took leaves
    /// the list, so <see cref="DetonateAll"/> and the count never speak of an
    /// entity the world no longer holds. An unknown sticky is simply not there
    /// — the arms that detonate through the tray take their entry first, as
    /// stock's <c>DetonateRemotePipebombs</c> does not prune while it walks.
    /// </summary>
    public void Unregister(int ownerIndex, TFStickybomb sticky)
    {
        if (_trays.TryGetValue(ownerIndex, out var tray))
            tray.Remove(sticky);

        Publish(ownerIndex);
    }

    /// <summary>
    /// Detonates every sticky <paramref name="ownerIndex"/> has out. A young
    /// one — fired within its live time, i.e. not yet armed past
    /// <see cref="TFProjectileDefs.StickyArmTime"/> — does not go now: it
    /// stays in the tray, as stock's <c>DetonateRemotePipebombs</c> skips it
    /// (<c>tf_weapon_pipebomblauncher.cpp:549-559</c>). Stock marks it
    /// <c>SetDetonateOnPulse</c> only when its live time is 0.5 s or less
    /// (:553-556); the default <c>tf_grenadelauncher_livetime</c> is 0.8, so
    /// the mark never fires — it would take a <c>sticky_arm_time</c>
    /// attribute on the launcher, which this port does not read yet (the
    /// caller of <see cref="TFStickybomb.ArmDetonateOnPulse"/> then). The
    /// armed ones leave the tray and go, each sweeping its
    /// <see cref="TFStickybomb.DetonateStickies"/> sphere first when
    /// <paramref name="destroyStickies"/> arms the launcher's
    /// <c>stickies_detonate_stickies</c> (:561-567). With
    /// <paramref name="inView"/> the Scottish Resistance's aim mode
    /// (<c>ModifyPipebombsInView</c>, :525-528) stands in for the whole-tray
    /// walk: only an armed bomb the predicate accepts goes, the rest stay.
    /// </summary>
    public void DetonateAll(int ownerIndex, bool destroyStickies = false,
                            Func<TFStickybomb, bool>? inView = null)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            return;

        for (int i = tray.Count - 1; i >= 0; i--)
        {
            if (tray[i].Armed && (inView is null || inView(tray[i])))
            {
                TFStickybomb sticky = tray[i];
                tray.RemoveAt(i);
                // CanDestroyStickies (tf_weapon_pipebomblauncher.cpp:561-565):
                // the destroying launcher sweeps each bomb's sphere before
                // its own Detonate.
                if (destroyStickies)
                    sticky.DetonateStickies();
                sticky.Detonate();
            }
        }

        Publish(ownerIndex);
    }

    /// <summary>
    /// <c>DetonateRemotePipebombs(bFizzle: true)</c>
    /// (<c>tf_weapon_pipebomblauncher.cpp:483, :543-546</c>): every sticky
    /// this owner has out — young included, the young-sticky skip being the
    /// detonate arm's only (:549-551) — fizzles and leaves, blasting nothing.
    /// Stock's <c>EF_NODRAW</c> skip (:540) has no port counterpart: nothing
    /// here leaves a sticky invisible but alive.
    /// </summary>
    public void FizzleAll(int ownerIndex)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            return;

        for (int i = tray.Count - 1; i >= 0; i--)
        {
            TFStickybomb sticky = tray[i];
            tray.RemoveAt(i);
            sticky.Fizzle();
            sticky.Detonate();
        }

        Publish(ownerIndex);
    }

    /// <summary>
    /// The dying owner's launcher: stock removes the launcher entity with the
    /// death (a class change kills first), and that removal's
    /// <c>UpdateOnRemove</c> (:479-486) is the fizzle — "if we just died, we
    /// want to fizzle our pipebombs". This port has no launcher entity to
    /// remove, so the tray hears the death itself.
    /// </summary>
    public void OnPlayerDied(PlayerDeath death) => FizzleAll(death.EntIndex);

    /// <summary>How many stickies an owner has out (the facts' window).</summary>
    public int Count(int ownerIndex) => _trays.TryGetValue(ownerIndex, out var tray) ? tray.Count : 0;

    /// <summary>
    /// The owner's carrier hears the new count — the HUD's pipebomb panel
    /// reads it there (<see cref="TF2.Shared.Player.TFPlayerNetState.PipebombCount"/>).
    /// No carrier, no write: a player between lives has nothing the HUD reads.
    /// </summary>
    private void Publish(int ownerIndex)
    {
        if (_carriers.Get(ownerIndex) is not { } state)
            return;

        state.PipebombCount = Count(ownerIndex);
    }
}

/// <summary>
/// The jarate and the mad milk in flight (<c>CTFProjectile_Jar</c> and
/// <c>CTFProjectile_JarMilk</c>, <c>tf_weapon_jar.cpp</c>): one lobbed
/// projectile under the pipebomb's arc — stock's
/// <c>SetPipebombMode</c> before spawn (<c>tf_weapon_jar.cpp:296</c>) is the
/// gravity, the spin and the touch rules — that shatters on its first solid
/// or player contact and throws its effect over <see cref="TFProjectileDefs.JarRadius"/>.
///
/// <para>
/// One class wears both names: the milk is stock's
/// <c>CTFProjectile_JarMilk</c>, which overrides nothing but the impact
/// effect, the condition and the model (its header comment,
/// <c>tf_weapon_jar.h:196-216</c>) — here the <see cref="Milk"/> test on the
/// spawned name. The damage is stock's own zero (<c>m_flFullDamage = 0</c>,
/// <c>tf_weapon_jar.cpp:300</c>): the jar wounds nobody, it only conditions.
/// </para>
///
/// <para>Deferred (<c>docs/plans/tf2/deferred.tsv</c>, jarate rows): the
/// attacker credit and kill-eater arms, the
/// <c>PlayerJarated</c>/<c>PlayerExtinguished</c> usermessages and events, the
/// achievements and the concepts; the truce and passtime refusals; the
/// <c>wet_immunity</c> and <c>applies_snare_effect</c> attribute reads; the
/// burning teammate's cooldown refund. The burning teammate's drench itself
/// is kept — it is the jar's second half of the trade.</para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Jar, ScriptedEntityKind.Projectile)]
[ScriptedEntity(TFProjectileEntityNames.JarMilk, ScriptedEntityKind.Projectile)]
[ScriptedEntity(TFProjectileEntityNames.JarGas, ScriptedEntityKind.Projectile)]
public sealed class TFJar : FlyingProjectile
{
    /// <summary>Skybox surface bit (<c>SURF_SKY</c>), the pipebomb's own sky trace's bit.</summary>
    private const int JarSkySurface = 0x0004;

    private const int MaxJarredExceptions = 512;

    private readonly IEngineWorldQuery World;
    private readonly ITFPlayerConditions Conditions;
    private readonly IEnginePlayers Players;
    private readonly ITempEnts Temps;
    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Hooks;
    private readonly IEntityDirectory Directory;

    public TFJar(IEngineEntityPhysics physics, EntityMotion motion, IEngineWorldQuery world,
                 ITFPlayerConditions conditions, IEnginePlayers players,
                 ITempEnts temps, IEnginePrecache precache, IEngineSound sound,
                 ITFWeaponOwners owners, AttributeHooks hooks, IEntityDirectory directory)
        : base(physics, motion)
    {
        World = world;
        Conditions = conditions;
        Players = players;
        Temps = temps;
        Precache = precache;
        Sound = sound;
        Owners = owners;
        Hooks = hooks;
        Directory = directory;
    }

    /// <summary>The milk face of the shared class.</summary>
    public bool Milk => ClassName == TFProjectileEntityNames.JarMilk;

    /// <summary>The gas face of the shared class (<c>CTFJarGas</c>, <c>tf_weapon_jar_gas.cpp</c>).</summary>
    public bool Gas => ClassName == TFProjectileEntityNames.JarGas;

    /// <summary>The pipebomb's arc: stock spawns under <c>SetPipebombMode</c>.</summary>
    protected override float GravityScale => TFProjectileDefs.PipebombGravity;

    protected override string ProjectileModel => Milk
        ? TFProjectileDefs.JarMilkModel
        : TFProjectileDefs.JarModel;

    /// <summary>Stock's tumble about the roll axis only (<c>GetAngularImpulse</c>, <c>tf_weapon_jar.h:66</c>).</summary>
    protected override Vector LaunchSpin => new(0f, 0f, TFProjectileDefs.JarSpinRoll);

    /// <summary>What the client's trail mirror reads; the same pair the flare's carries.</summary>
    [NetState] public TFJarNetState State = new();

    /// <summary>The thrower's team and the shot's crit, as every projectile's <c>ArmedBy</c> writes them.</summary>
    public void ArmedBy(bool critical, int ownerIndex)
    {
        State.Critical = critical;
        State.TeamNumber = Players.GetTeam(Players.GetHandle(ownerIndex));

        ApplyCustomModel(Owners.ActiveItemOf(ownerIndex));
    }

    public override void OnSpawn()
    {
        base.OnSpawn();

        // The shatter, heard before it is seen — both precached at spawn as
        // stock's Precache is (<c>tf_weapon_jar.cpp:118-123</c>).
        Precache.PrecacheParticleSystem(ImpactEffect);
        Precache.PrecacheSound(TFProjectileDefs.JarExplodeSound);
    }

    /// <summary>The shatter's particle, stock's <c>GetImpactEffect</c>.</summary>
    private string ImpactEffect => Milk
        ? TFProjectileDefs.JarMilkImpactEffect
        : TFProjectileDefs.JarImpactEffect;

    protected override void Impact(EntityHandle hit)
    {
        // The touch rules are the pipebomb's own, worn by the
        // SetPipebombMode the jar's Create called first
        // (PipebombTouch, tf_weapon_jar.cpp:505-534): a jar never breaks on
        // its thrower — the base's OnTouch already skipped him — and flies
        // through anything that is neither world nor player.
        if (hit != EntityHandle.None && !Entities.IsPlayer(hit))
        {
            KeepFlying(Entities.GetVelocity(Handle), GravityScale);
            return;
        }

        // The teammate grace (:521-528): a dry teammate is flown through
        // too; only a burning one is worth the drench.
        if (hit != EntityHandle.None
            && Players.GetTeam(hit) == OwnerTeam()
            && !Conditions.InCond(hit, TFCond.Burning))
        {
            KeepFlying(Entities.GetVelocity(Handle), GravityScale);
            return;
        }

        // Handle hitting skybox (PipebombTouch :530-540, the pipebomb's own
        // trace): a jar that would shatter on the sky is simply gone, no
        // splash. The base's Spend removes what this declines to arm.
        Vector velocity = Entities.GetVelocity(Handle);
        if (velocity.LengthSquared > 0f)
        {
            Vector dir = velocity.Normalized;
            Vector spot = Origin - dir * 32f;
            SurfaceTraceResult sky = World.TraceLineSurface(spot, spot + dir * 64f, TraceMask.Solid, Handle);
            if (sky.Fraction < 1f && (sky.SurfaceFlags & JarSkySurface) != 0)
                return;
        }

        Splash();
    }

    /// <summary>
    /// JarExplode at the contact (<c>tf_weapon_jar.cpp:318-473</c>): the
    /// particle and the sound where the jar burst, then every living player
    /// in the radius whose centre the burst sees — the enemy takes the
    /// condition for <see cref="TFProjectileDefs.JarDuration"/> seconds from
    /// the thrower, a burning teammate is put out.
    /// </summary>
    private void Splash()
    {
        Vector at = Origin;

        TeEffectData splat = new()
        {
            Scale = 1f,
            HitBox = Precache.ParticleSystemIndex(ImpactEffect),
            Origin = at,
            Start = at,
        };
        Temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in splat);
        Sound.EmitSoundAtPosition(TFProjectileDefs.JarExplodeSound, at);

        // Stock's sweep flags the FL_CLIENT and FL_NPC flags; this build has
        // no NPCs, so the players are the whole sweep.
        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxJarredExceptions];
        int count = World.EntitiesInSphere(at, TFProjectileDefs.JarRadius, nearby);
        int myTeam = OwnerTeam();
        TFCond cond = Milk ? TFCond.MadMilk : Gas ? TFCond.Gas : TFCond.Urine;

        for (int i = 0; i < count; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || !Entities.IsPlayer(victim))
                continue;

            // Skip dead players and the blind ones: stock's !IsAlive gate,
            // then the line-of-sight trace (MASK_SHOT & ~CONTENTS_HITBOX --
            // the MASK_SHOT_HULL the fold names), whose world hit is the
            // only blocker stock honours; its ignore-players filter lets
            // the port's trace stop on one and still see.
            if (!Players.IsAlive(victim))
                continue;

            TraceResult los = World.TraceLine(at, Entities.GetWorldSpaceCenter(victim),
                                              TraceMask.ShotHull, Handle);
            if (los.Fraction < 1f && IsWorld(los.Entity))
                continue;

            if (Players.GetTeam(victim) == myTeam)
            {
                // The burning teammate (JarExplode's ExtinguishPlayer arm,
                // :446-468): the same bare removal the port's airblast
                // makes, its bonus-point and event tails deferred above,
                // and stock's refund to the thrower's bar under it.
                if (Conditions.InCond(victim, TFCond.Burning))
                {
                    Conditions.Remove(victim, TFCond.Burning);
                    RefundLauncherBar(victim);
                }
                continue;
            }

            // The enemy (JarExplode's wet arm, :420-446): invulnerable and
            // phased players stay dry, the rest wear the condition from the
            // thrower. The truce and passtime refusals and the wet_immunity
            // read are deferred with the credit arms.
            if (Conditions.Of(victim.EntryIndex) is { } state
                && (new TFConditionEffects().IsInvulnerable(state.Bits) || state.Has(TFCond.Phase)))
                continue;

            Conditions.Add(victim, cond, TFProjectileDefs.JarDuration, Attribution);

            // The gas's own lighting (<c>CTFJarGas::JarExplode</c>): a
            // gassed enemy also burns for the item's afterburn rate, stock's
            // <c>TF_GAS_AFTERBURN_RATE</c> as <see
            // cref="TFBurn.AfterburnRateOnHit"/> answers it.
            if (Gas)
                Conditions.Add(victim, TFCond.Burning,
                               TFBurn.AfterburnRateOnHit(TFWeaponId.JarGas), Attribution);
        }
    }

    private bool IsWorld(EntityHandle entity) => entity != EntityHandle.None && Entities.GetIndex(entity) == 0;

    private int OwnerTeam() => Entities.IsValid(Attribution) ? Players.GetTeam(Attribution) : 0;

    /// <summary>
    /// Stock's refund behind the douse (<c>tf_weapon_jar.cpp:452-468</c>):
    /// putting out someone else's fire hands a share of the refill back to
    /// the thrower — the share their held item answers on the hook — and
    /// stock spends it only while the bar still runs behind it
    /// (<c>DecrementBarRegenTime</c> on the recharge, :461-465). Ours rides
    /// the same share onto the carried meter under the same guard, held at
    /// full as the tick holds it. The thrower dousing their own fire keeps
    /// the whole bar (<c>pAttacker != pPlayer</c>, :454).
    /// </summary>
    private void RefundLauncherBar(EntityHandle ally)
    {
        EntityHandle launcher = Attribution;
        if (launcher == ally || !Entities.IsValid(launcher))
            return;

        // The jars' own seconds are stock's <c>HasEffectBarRegeneration</c>
        // gate on the launcher's weapon (:454).
        if (TFItemMeter.EffectBarSeconds(Milk ? TFWeaponId.JarMilk : TFWeaponId.Jar) <= 0f)
            return;

        float hooked = Hooks.Float(1f, "extinguish_reduces_cooldown",
                                   Owners.ActiveItemOf(Entities.GetIndex(launcher)));
        float refund = 1f - hooked;
        if (refund <= 0f)
            return;

        if (Directory.Get(Players.GetActiveWeaponEntity(launcher)) is SsWeapon { State: { } state }
            && state.ItemMeter < refund)
        {
            state.ItemMeter = Math.Min(1f, state.ItemMeter + refund);
        }
    }

    /// <summary>
    /// <c>SetCustomPipebombModel</c> (:261-284, the milk's own :836-855): the
    /// thrower's held item names the face the jar wears — the festive
    /// jarate's and the bread monster's for the plain jar, the bread
    /// monster's alone for the milk; any other answer keeps the born
    /// model. The face numbers are the stock <c>ProjectileType_t</c> slots
    /// (<c>tf_shareddefs.h:546-578</c>): festive jarate 22, bread monster
    /// jarate 24 and madmilk 25.
    /// </summary>
    private void ApplyCustomModel(IAttributeProvider? item)
    {
        int face = Hooks.Int(0, "override_projectile_type", item);
        string? model = Milk
            ? face is 24 or 25 ? TFProjectileDefs.JarMilkBreadmonsterModel : null
            : face switch
            {
                22 => TFProjectileDefs.JarFestiveModel,
                24 or 25 => TFProjectileDefs.JarBreadmonsterModel,
                _ => null,
            };

        if (model is { } chosen)
            SetModel(chosen);
    }
}

/// <summary>
/// The Flying Guillotine's cleaver (<c>CTFProjectile_Cleaver</c>,
/// <c>tf_weapon_jar.cpp:980-1149</c>): the jar's pipebomb flight — the same
/// arc, spin aside, under <see cref="TFProjectileDefs.PipebombGravity"/> —
/// thrown at the ten-forward-parts-to-one-up tilt of
/// <see cref="TFProjectileDefs.CleaverAimForward"/> and
/// <see cref="TFProjectileDefs.CleaverAimUp"/>, at
/// <see cref="TFProjectileDefs.CleaverSpeed"/>.
///
/// <para>
/// The hit (<c>OnHit</c>, :993-1078) is the enemy player's alone: the
/// invulnerable and the teammate are the miss, as is everything that is not
/// a player at all. The hit bleeds its five seconds from the thrower
/// (:1036) and lands the 50 (<c>GetDamage</c>, :242) as a
/// <see cref="TFDamageCustom.Cleaver"/> slash, the thrown crit riding
/// <c>DMG_CRITICAL</c> (:1049-1053) — both sounds to the world and again to
/// the thrower (:1062-1073 folded to the one position broadcast) — and the
/// miss thunks once on the world (:1083-1096). Both arms lie where they
/// struck for <see cref="TFProjectileDefs.CleaverLingerSeconds"/>, the hit's
/// invisible (<c>RemoveCleaver</c>'s NODRAW, :1143-1149), the miss's body
/// showing.
/// </para>
///
/// <para>
/// The recharge (:1024-1032): a hit past
/// <see cref="TFProjectileDefs.CleaverRechargeFlightTime"/> aloft hands the
/// launcher <see cref="TFProjectileDefs.CleaverRechargeSeconds"/> of its
/// wait back, the store's walk — never on the thrower's own wound
/// (<c>pOwner != pPlayer</c>, :1028).
/// </para>
///
/// <para>
/// Not here (<c>docs/plames/tf2/deferred.tsv</c>, cleaver row): the truce
/// arm (:1016-1020); the per-recipient sound split (:1062-1073); stock's
/// <c>TF_DMG_CUSTOM_CLEAVER_CRIT</c> ladder arm (<c>tf_gamerules.cpp:6171
/// </c>) — nothing in this SDK sets the custom, so the long-range mini-crit
/// has no producer to port.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.Cleaver, ScriptedEntityKind.Projectile)]
public sealed class TFCleaver : FlyingProjectile
{
    /// <summary>Skybox surface bit (<c>SURF_SKY</c>), the pipebomb's own sky trace's bit.</summary>
    private const int CleaverSkySurface = 0x0004;

    private readonly IEngineWorldQuery World;
    private readonly IEngineDamage Damage;
    private readonly DamageHitGroups HitGroups;
    private readonly TFDamageScopes Scopes;
    private readonly TFBleeds Bleeds;
    private readonly IEnginePlayers Players;
    private readonly ITFPlayerConditions Conditions;
    private readonly IGameClock Clock;
    private readonly IEngineSound Sound;
    private readonly IEnginePrecache Precache;
    private readonly IWeaponGiver Arsenal;

    public TFCleaver(IEngineEntityPhysics physics, EntityMotion motion, IEngineWorldQuery world,
                     IEngineDamage damage, DamageHitGroups hitGroups, TFDamageScopes scopes,
                     TFBleeds bleeds, IEnginePlayers players, ITFPlayerConditions conditions,
                     IGameClock clock, IEngineSound sound, IEnginePrecache precache,
                     IWeaponGiver arsenal)
        : base(physics, motion)
    {
        World = world;
        Damage = damage;
        HitGroups = hitGroups;
        Scopes = scopes;
        Bleeds = bleeds;
        Players = players;
        Conditions = conditions;
        Clock = clock;
        Sound = sound;
        Precache = precache;
        Arsenal = arsenal;
    }

    /// <summary>The jar's pipebomb arc, the flight shared with the jars' lob.</summary>
    protected override float GravityScale => TFProjectileDefs.PipebombGravity;

    protected override string ProjectileModel => TFProjectileDefs.CleaverModel;

    /// <summary>Stock's tumble, pitch 500 (<c>GetAngularImpulse</c>, <c>tf_weapon_jar.h:123</c>).</summary>
    protected override Vector LaunchSpin => new(TFProjectileDefs.CleaverSpinPitch, 0f, 0f);

    /// <summary>
    /// The hit's body is NODRAW'd where it struck (<c>RemoveCleaver</c>);
    /// the miss's (<c>Explode</c>) keeps its model, so the flag follows the
    /// arm that ran.
    /// </summary>
    private bool _worldArm;

    /// <summary>The world arm's body keeps its model; only the hit's is NODRAW'd.</summary>
    protected override bool LingerHides => !_worldArm;

    private float _createdAt;

    /// <summary>The world thunk, once (<c>m_bSoundPlayed</c>, <c>tf_weapon_jar.cpp:984</c>).</summary>
    private bool _soundPlayed;

    /// <summary>What the client's trail mirror reads; the team the glow names.</summary>
    [NetState] public TFCleaverNetState State = new();

    public bool Critical { get; private set; }

    public string WeaponName { get; private set; } = "";

    /// <summary>The thrower's team, the shot's crit and the launcher's name, as the jar's <c>ArmedBy</c> writes its pair.</summary>
    public void ArmedBy(bool critical, int ownerIndex, string weaponName)
    {
        Critical = critical;
        WeaponName = weaponName;
        State.TeamNumber = Players.GetTeam(Players.GetHandle(ownerIndex));
        _createdAt = Clock.Now;
    }

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Both arms' sounds, precached at spawn as stock's Precache is
        // (<c>tf_weapon_jar.cpp:963-970</c>).
        Precache.PrecacheSound(TFProjectileDefs.CleaverImpactFleshSound);
        Precache.PrecacheSound(TFProjectileDefs.CleaverImpactWorldSound);
    }

    protected override void Impact(EntityHandle hit)
    {
        // The touch rules are the pipebomb's own, worn by the SetPipebombMode
        // the cleaver's Create called first (PipebombTouch,
        // tf_weapon_jar.cpp:505-534): anything that is neither world nor
        // player is flown through, and so is a dry teammate — the jar face's
        // burning exception, the cleaver wearing the jar's projectile type
        // (the base ctor's m_iProjectileType, :242).
        if (hit != EntityHandle.None && !Entities.IsPlayer(hit))
        {
            KeepFlying(Entities.GetVelocity(Handle), GravityScale);
            return;
        }

        if (hit != EntityHandle.None
            && Players.GetTeam(hit) == OwnerTeam()
            && !Conditions.InCond(hit, TFCond.Burning))
        {
            KeepFlying(Entities.GetVelocity(Handle), GravityScale);
            return;
        }

        // Handle hitting skybox (PipebombTouch :536-552, the pipebomb's own
        // trace): a cleaver that would stick in the sky is simply gone. The
        // base's Spend removes what this declines to arm.
        Vector velocity = Entities.GetVelocity(Handle);
        if (velocity.LengthSquared > 0f)
        {
            Vector dir = velocity.Normalized;
            Vector spot = Origin - dir * 32f;
            SurfaceTraceResult sky = World.TraceLineSurface(spot, spot + dir * 64f, TraceMask.Solid, Handle);
            if (sky.Fraction < 1f && (sky.SurfaceFlags & CleaverSkySurface) != 0)
                return;
        }

        if (hit != EntityHandle.None && Woundable(hit))
        {
            HitPlayer(hit);
            return;
        }

        Missed();
    }

    /// <summary>
    /// <c>OnHit</c>'s wound (tf_weapon_jar.cpp:1024-1077): the flight's pay
    /// back to the launcher, the five-second bleed from the thrower, and the
    /// 50 as a cleaver slash — the crit bit set before the damage so the hit
    /// itself carries it — under the impact sound.
    /// </summary>
    private void HitPlayer(EntityHandle victim)
    {
        // The flight's pay (OnHit :1024-1032): past half a second aloft, the
        // launcher's wait is shorter by the constant — never on the thrower's
        // own wound (:1028).
        if (Clock.Now - _createdAt >= TFProjectileDefs.CleaverRechargeFlightTime
            && victim != Shooter)
            Arsenal.AdvanceCarriedMeter(Shooter, WeaponName, TFProjectileDefs.CleaverRechargeSeconds);

        // "just do the bleed effect directly since the bleed attribute comes
        // from the inflictor, which is the cleaver" (:1034-1036).
        Bleeds.Of(victim.EntryIndex)?.Make(Clock.Now, Players.IsAlive(victim), Shooter,
                                           TFWeaponId.Cleaver, WeaponName,
                                           TFProjectileDefs.CleaverBleedSeconds);

        // The love tap (:1038-1060): the 50, the cleaver custom, the crit bit
        // set before the damage so the hit itself carries it.
        using (HitGroups.Push(HitGroup.Generic, Critical))
        using (Scopes.Push(TFDamageCustom.Cleaver, TFWeaponId.Cleaver, WeaponName))
            Damage.TakeDamage(victim, Shooter, TFProjectileDefs.CleaverDamage,
                              TFWeaponDamageTypes.Of(TFWeaponId.GrenadeCleaver)
                              | (Critical ? TFDamageBits.Critical : 0));

        Sound.EmitSoundAtPosition(TFProjectileDefs.CleaverImpactFleshSound, Origin);

        // RemoveCleaver (:1143-1149): unseen where it struck, gone in two.
        Linger(TFProjectileDefs.CleaverLingerSeconds);
    }

    /// <summary>
    /// <c>Explode</code>'s miss (:1083-1096): the world thunk, once, and the
    /// two-second lie with the body showing.
    /// </summary>
    private void Missed()
    {
        if (!_soundPlayed)
        {
            Sound.EmitSoundAtPosition(TFProjectileDefs.CleaverImpactWorldSound, Origin);
            _soundPlayed = true;
        }

        _worldArm = true;
        Linger(TFProjectileDefs.CleaverLingerSeconds);
    }

    /// <summary>
    /// <c>OnHit</code>'s gates (:1009-1014): a player whose condition state
    /// stands under no invulnerable pair. A player no state answers is bare,
    /// as the jar's gate reads its null.
    /// </summary>
    private bool Woundable(EntityHandle victim)
        => Conditions.Of(victim.EntryIndex) is not { } conds
           || !(new TFConditionEffects().IsInvulnerable(conds.Bits)
                 || conds.Has(TFCond.InvulnerableWearingoff));

    private int OwnerTeam() => Entities.IsValid(Shooter) ? Players.GetTeam(Shooter) : 0;
}

/// <summary>
/// The Wrap Assassin's ornament (<c>CTFBall_Ornament</c>,
/// <c>tf_weapon_bat.cpp</c>): the bat's alt-fire lobs it — ten forward parts
/// to one up over <see cref="TFProjectileDefs.OrnamentSpeed"/> — at the
/// grenade base's gravity and friction (<c>InitGrenade</c>,
/// <c>tf_weaponbase_grenadeproj.cpp:245-247</c>), and its first touch is
/// <c>PipebombTouch</c> (:1223-1244): the blast
/// (<c>Explode</c> :1285-1321, <see cref="TFProjectileDefs.OrnamentExplodeDamageMult"/>
/// of the shot over <see cref="TFProjectileDefs.OrnamentExplodeRadius"/>, the
/// BLAST and no-force bits, <c>TF_DMG_CUSTOM_BASEBALL</c>), then the struck
/// enemy player's own (<c>ApplyBallImpactEffectOnVictim</c> :1151-1218) — the
/// five-second bleed from the launcher (:1185), and the shot again as a
/// direct hit, the long flight's always-crit under it (:1176-1181,
/// <see cref="TFProjectileDefs.OrnamentLongFlight"/>).
///
/// <para>
/// Not here (docs/plans/tf2/deferred.tsv): <c>ShouldBallTouch</c>'s screening
/// (the respawn-room, ladder, func_lod, projectile, skybox and train filters,
/// :935-990) — the seam's touch carries none of those flags — and the
/// impact's sounds and particles, the throw's random spin
/// (<c>GetBallDynamics</c> :246) and the m_iOriginalOwnerID bookkeeping of
/// the caught-ball mode the port's bat has none of.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.BallOrnament, ScriptedEntityKind.Projectile)]
public sealed class TFBallOrnament : FlyingProjectile
{
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineDamage Damage;
    private readonly IEnginePlayers Players;
    private readonly ITFPlayerConditions Conditions;
    private readonly TFBleeds Bleeds;
    private readonly TFBlastApplier Blast;
    private readonly IGameClock Clock;

    private float _createdAt;

    public TFBallOrnament(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                          TFDamageScopes scopes, DamageHitGroups hitGroups, IEnginePlayers players,
                          ITFPlayerConditions conditions, TFBleeds bleeds, TFBlastApplier blast,
                          IGameClock clock)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
        Players = players;
        Conditions = conditions;
        Bleeds = bleeds;
        Blast = blast;
        Clock = clock;
    }

    protected override float GravityScale => TFProjectileDefs.OrnamentGravity;

    protected override string ProjectileModel => TFProjectileDefs.OrnamentModel;

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";

    /// <summary>The shot the throw baked, the bat's damage under its mult_dmg.</summary>
    public float ShotDamage { get; private set; }

    public bool Critical { get; private set; }

    /// <summary>The thrower's deal: the shot, whose it is and its roll (<c>CreateBall</c>, :1089-1094).</summary>
    public void ArmedBy(float damage, TFWeaponId weapon, string name, bool critical)
    {
        ShotDamage = damage;
        Weapon = weapon;
        WeaponName = name;
        Critical = critical;
        _createdAt = Clock.Now; // m_flCreationTime, as the sticky's (its ThrownBy)
    }

    public override void OnSpawn()
    {
        base.OnSpawn();
        Physics.SetFriction(Handle, TFProjectileDefs.OrnamentFriction);
        // CTFStunBall::Spawn's context think (:672): SUB_Remove at the
        // literal 15.0 the think is set with, struck or not.
        SetContextThink(ExpiryContext, TFProjectileDefs.OrnamentLifetime);
    }

    /// <summary>The spawn think's name, the expiry <see cref="TFProjectileDefs.OrnamentLifetime"/> out.</summary>
    public const string ExpiryContext = "OrnamentExpire";

    public override void OnThink(string context)
    {
        if (context == ExpiryContext)
            Remove();
        else
            base.OnThink(context);
    }

    /// <summary>
    /// <c>PipebombTouch</c> (:1223-1244): the blast first, every touch alike
    /// (:1238), then the enemy player's own (:1240-1243) — invulnerable
    /// players keep whole (:1168-1169), the long flight's hit is the crit
    /// (:1176-1181), the bleed lands before the hurt does (:1185, :1211).
    /// </summary>
    protected override void Impact(EntityHandle hit)
    {
        // Explode (:1311-1318): the blast is never the crit — stock's info
        // carries the bits alone.
        Blast.Apply(Origin,
                    TFBlast.Of(ShotDamage * TFProjectileDefs.OrnamentExplodeDamageMult,
                               TFProjectileDefs.OrnamentExplodeRadius,
                               DamageType.Blast | DamageType.PreventPhysicsForce),
                    Attribution, Handle, ignored: Handle, Weapon, WeaponName,
                    critical: false, custom: TFDamageCustom.Baseball);

        if (!Entities.IsPlayer(hit) || Players.GetTeam(hit) == Players.GetTeam(Attribution))
            return;

        // Can't bleed an invul player (:1168-1169) — the whole arm, the
        // direct hit with it. A player no condition state stands under is
        // bare, as the jar's gate reads its null.
        bool invulnerable = Conditions.Of(hit.EntryIndex) is { } conds
                            && (new TFConditionEffects().IsInvulnerable(conds.Bits)
                                || conds.Has(TFCond.InvulnerableWearingoff));
        if (invulnerable)
            return;

        bool crit = Critical || Clock.Now - _createdAt >= TFProjectileDefs.OrnamentLongFlight;

        // "just do the bleed effect directly since the bleed attribute comes
        // from the inflictor, which is the bat" (:1183-1185).
        Bleeds.Of(hit.EntryIndex)?.Make(Clock.Now, Players.IsAlive(hit), Attribution,
                                        Weapon, WeaponName, TFProjectileDefs.OrnamentBleedSeconds);

        // The love tap (:1194-1212): the shot again, the baseball custom, the
        // crit bit set before the damage so the hit itself carries it.
        using (HitGroups.Push(HitGroup.Generic, crit))
        using (Scopes.Push(TFDamageCustom.Baseball, Weapon, WeaponName))
            Damage.TakeDamage(hit, Attribution, ShotDamage,
                              TFWeaponDamageTypes.Of(Weapon) | (crit ? TFDamageBits.Critical : 0));
    }
}
