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
using TF2.Shared.Econ;
using TF2.Shared.Projectiles;
using TF2.Server.Combat;
using TF2.Server.Player;

namespace TF2.Server.Projectiles;

/// <summary>
/// Stock's classnames (<c>LINK_ENTITY_TO_CLASS</c>): the rocket
/// (<c>tf_projectile_rocket.cpp:18</c>), the demoman's two pipebombs
/// (<c>tf_weapon_grenade_pipebomb.cpp:406-409</c>), the medic's syringe
/// (<c>tf_projectile_nail.cpp:31</c>) and the huntsman's arrow
/// (<c>tf_projectile_arrow.cpp:53</c>).
/// </summary>
public static class TFProjectileEntityNames
{
    public const string Rocket = "tf_projectile_rocket";
    public const string Pipebomb = "tf_projectile_pipe";
    public const string Stickybomb = "tf_projectile_pipe_remote";
    public const string Syringe = "tf_projectile_syringe";
    public const string Arrow = "tf_projectile_arrow";
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
    private readonly TFBlastApplier Blast;
    private readonly ITFWeaponOwners Owners;
    private readonly IEnginePlayers Players;
    private readonly AttributeHooks Attributes;
    private readonly TFPlayerCarriers Carriers;
    private readonly ITempEnts Temps;
    private readonly IEnginePrecache Precache;

    public TFRocket(IEngineEntityPhysics physics, EntityMotion motion, TFBlastApplier blastApplier,
                    ITFWeaponOwners owners, IEnginePlayers players, AttributeHooks attributes,
                    TFPlayerCarriers carriers, ITempEnts temps, IEnginePrecache precache)
        : base(physics, motion)
    {
        Blast = blastApplier;
        Owners = owners;
        Players = players;
        Attributes = attributes;
        Carriers = carriers;
        Temps = temps;
        Precache = precache;
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
    /// airstrike arm (:602-614) waits on the blast-jump condition.
    /// </summary>
    private float Radius(EntityHandle directVictim)
    {
        IAttributeProvider? item = Owners.ItemOf(_ownerIndex, WeaponName);
        float radius = Attributes.Float(TFProjectileDefs.RocketRadius, "mult_explosion_radius", item);

        int specialist = Attributes.Int(0, "rocket_specialist", item);
        if (specialist != 0 && IsDirectHitOnAnEnemy(directVictim))
            radius *= RemapValClamped(specialist, 1f, 4f, 1.15f, 1.6f);

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

    public TFPipebomb(IEngineEntityPhysics physics, TFBlastApplier blast, IEnginePlayers players,
                      IEntityDirectory directory, ITFWeaponOwners owners, AttributeHooks attributes,
                      IEngineWorldQuery world, IServerSkins skins, IGameClock clock, IEngineFrame frame)
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
    }

    /// <summary>The model the prop shell carries (<c>TF_WEAPON_PIPEGRENADE_MODEL</c>).</summary>
    public const string Model = TFProjectileDefs.PipeModel;

    public float FullDamage { get; private set; }
    public float Radius { get; private set; }
    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

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

    /// <summary>The thrower's deal: full damage, radius, fuse.</summary>
    public void ThrownBy(EntityHandle thrower, Vector velocity, float fullDamage, float radius,
                         TFWeaponId weapon, string weaponName, bool critical)
    {
        _thrower = thrower;
        FullDamage = fullDamage;
        Radius = radius;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        _ownerIndex = Entities.GetIndex(thrower);
        _createdAt = Clock.Now; // m_flCreationTime, as the sticky's (its ThrownBy)
        _fuseDamage = TFProjectileDefs.PipeTimerDamageScale * fullDamage;

        // The client's CreateTrailParticles reads the team off the projectile.
        State.Critical = critical;
        State.TeamNumber = Players.GetTeam(thrower);

        Entities.SetOwner(Handle, thrower);
        Entities.SetVelocity(Handle, velocity);
        SetNextThink(TFProjectileDefs.PipeFuse);
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
            if (!_touched)
            {
                _touched = true;
                _fuseDamage *= WorldContactDamageScale();
            }
            return;
        }

        // If we already touched a surface we're not exploding on contact anymore.
        if (_touched)
            return;

        // Blow up if we hit an enemy we can damage (:759): the full damage is
        // restored first (:824), because the fuse would have taken 40% off.
        int toucherTeam = Players.GetTeam(toucher);
        int myTeam = Entities.IsValid(_thrower) ? Players.GetTeam(_thrower) : 0;
        if (toucherTeam != 0 && myTeam != 0 && toucherTeam != myTeam && Hurtable(toucher))
        {
            _spent = true;
            Explode(FullDamage, directVictim: toucher);
        }
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
        // TF_WEAPON_GRENADE_DEMOMAN (tf_weapon_grenade_pipebomb.cpp:142),
        // a half-falloff blast without DMG_USEDISTANCEMOD
        // (tf_shareddefs.cpp:770) — not the launcher's row.
        Blast.Apply(Origin, TFBlast.Of(damage, Radius, TFWeaponDamageTypes.Of(TFWeaponId.GrenadeDemoman)),
                    Attribution(), Handle,
                    ignored: Handle, Weapon, WeaponName, Critical, directVictim,
                    selfRadius: TFProjectileDefs.GrenadeJumpRadius, selfBaseDamage: FullDamage);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} exploded at {Origin}\n");
        Remove();
    }

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

    public TFStickybomb(IEngineEntityPhysics physics, TFBlastApplier blast, IEnginePlayers players,
                        TFStickybombConVars convars, IGameClock clock, IEngineWorldQuery world,
                        IEntityDirectory directory, IEngineFrame frame)
    {
        Physics = physics;
        Blast = blast;
        Players = players;
        ConVars = convars;
        Clock = clock;
        World = world;
        Directory = directory;
        Frame = frame;
    }

    public const string Model = TFProjectileDefs.StickyModel;

    /// <summary>What the client's trail mirror reads.</summary>
    [NetState] public TFStickybombNetState State = new();

    public float FullDamage { get; private set; }
    public float Radius { get; private set; }
    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    private EntityHandle _thrower = EntityHandle.None;
    private float _createdAt;
    private float _minSleepTime;
    private float _deflectedResetAt;
    private bool _armed;
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
        // (tf_weapon_grenade_pipebomb.cpp:1144, GetLiveTime :1186-1210), the
        // constant its declared value.
        SetNextThink(ConVars.LiveTime.Value);
    }

    public override bool TouchedByAnything => true;

    /// <summary>
    /// Pipebombs stick to the world when they touch it
    /// (<c>VPhysicsCollision</c>, <c>tf_weapon_grenade_pipebomb.cpp:916-928</c>):
    /// a world contact or a dynamic prop's sticks it armed or not — unless the
    /// blow that last moved it left a sleep time still running
    /// (<c>gpGlobals-&gt;curtime &gt; m_flMinSleepTime</c>, :928) — and the rest
    /// check below stays as the fallback for a prop the seam cannot name a
    /// collision for. Stock's prop arm is its <c>dynamic_cast&lt;CDynamicProp*&gt;</c>,
    /// approximated by classname, with the cast's exemptions:
    /// <see cref="SticksTo"/>.
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (_stuck || _spent || toucher == EntityHandle.None)
            return;

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
        Physics.EnableMotion(Handle, false);
        Log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Stickybomb} stuck at {Origin}\n");
    }

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
    private float GetDamageRadius()
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
                         TFWeaponId weapon, string weaponName, bool critical)
    {
        _thrower = thrower;
        _createdAt = Clock.Now; // m_flCreationTime, for the young-pipe damage type
        FullDamage = fullDamage;
        Radius = radius;
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;

        // The client's trail mirror reads the team off the projectile.
        State.Critical = critical;
        State.TeamNumber = Players.GetTeam(thrower);

        Entities.SetOwner(Handle, thrower);
        Entities.SetVelocity(Handle, velocity);
    }

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
            if (target == EntityHandle.None || Players.GetTeam(target) == myTeam)
                continue;
            if (Directory.Get(target) is not TFStickybomb bomb || bomb._fizzled)
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
        // remote-detonate pipe adds DMG_USEDISTANCEMOD within 5 s of its
        // creation. The radius is GetDamageRadius's, not the script's — an
        // untouched bomb's air blast comes in short (:1417-1434).
        Blast.Apply(Origin, TFBlast.Of(FullDamage, GetDamageRadius(),
                                       TFProjectileDefs.YoungStickyBits(TFWeaponDamageTypes.Of(Weapon),
                                                                        Clock.Now - _createdAt)),
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

    private float _gravity = TFProjectileDefs.ArrowGravity(0f);

    public TFArrow(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                   TFDamageScopes scopes, DamageHitGroups hitGroups)
        : base(physics, motion)
    {
        Damage = damage;
        Scopes = scopes;
        HitGroups = hitGroups;
    }

    protected override float GravityScale => _gravity;

    protected override string ProjectileModel => TFProjectileDefs.ArrowModel;

    public TFWeaponId Weapon { get; private set; }
    public string WeaponName { get; private set; } = "";
    public bool Critical { get; private set; }

    /// <summary>Whether the bow was alight when this arrow left it.</summary>
    public bool Lit { get; private set; }

    /// <summary>The shot the bow dealt: its charge, and whether the flame had lit it.</summary>
    public void ArmedBy(TFWeaponId weapon, string weaponName, bool critical, bool lit,
                        float damage, float gravity)
    {
        Weapon = weapon;
        WeaponName = weaponName;
        Critical = critical;
        Lit = lit;
        ShotDamage = damage;
        _gravity = gravity;
    }

    /// <summary>The stock bow's shot, scaled by its charge (<see cref="ArmedBy"/>).</summary>
    public float ShotDamage { get; private set; } = TFProjectileDefs.ArrowBaseDamage;

    protected override void Impact(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return;

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
public sealed class TFStickyTray(TFPlayerCarriers? carriers = null) : IPlayerDeathHook
{
    private readonly Dictionary<int, List<TFStickybomb>> _trays = new();

    private readonly TFPlayerCarriers? _carriers = carriers;

    /// <summary>Records a sticky and removes the oldest past <see cref="TFProjectileDefs.MaxStickies"/>.</summary>
    public void Register(int ownerIndex, TFStickybomb sticky)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            _trays[ownerIndex] = tray = new List<TFStickybomb>();

        // The bomb's half of the firing: stock's Create gives it m_hLauncher
        // and PrimaryAttack adds it to the list; the removal notice below
        // comes back through this pair.
        sticky.EnrolledIn(this, ownerIndex);
        tray.Add(sticky);
        if (tray.Count > TFProjectileDefs.MaxStickies)
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
    /// <c>stickies_detonate_stickies</c> (:561-567).
    /// </summary>
    public void DetonateAll(int ownerIndex, bool destroyStickies = false)
    {
        if (!_trays.TryGetValue(ownerIndex, out var tray))
            return;

        for (int i = tray.Count - 1; i >= 0; i--)
        {
            if (tray[i].Armed)
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
        if (_carriers?.Get(ownerIndex) is not { } state)
            return;

        state.PipebombCount = Count(ownerIndex);
    }
}
