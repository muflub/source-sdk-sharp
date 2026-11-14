//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;

using TF2.Shared.Combat;

using TF2.Shared;

namespace TF2.Server.Projectiles;

/// <summary>
/// The Short Circuit's orb (<c>CTFProjectile_MechanicalArmOrb</c>,
/// <c>tf_weapon_mechanical_arm.cpp:541</c> over the shared file): a ball that
/// flies flat at <see cref="TFProjectileDefs.MechanicalArmOrbSpeed"/> and, on
/// a tenth-second think, eats the enemy projectiles it passes and zaps the
/// players near it (<see cref="OnThink"/>). It dies on its first solid
/// touch, at the lifetime's end, and — with another orb — in the
/// annihilation <see cref="CheckForProjectiles"/> books.
///
/// <para>
/// Stock's spawn flags this port carries as the core's own shape
/// (<c>Spawn</c>, :586-611): FL_GRENADE and gravity none are the projectile
/// kind's; the FSOLID_TRIGGER|NOT_SOLID trigger and the one-unit collision
/// box are <see cref="ScriptedEntityKind.Projectile"/>'s shell. Not here:
/// stock's <c>MOVETYPE_FLY</c> over <c>MOVECOLLIDE_FLY_CUSTOM</c> is the
/// integrator's flight, the render-mode and no-shadow rows are the model's
/// material, and the client's parented trail (<c>dxhr_lightningball_parent_*</c>,
/// :544-545) waits with the client particle arm, as the muzzle fizzle does.
/// The zap's and the eat's particles are stock's complex control-point
/// dispatches (:714-715, :847-848); here the plain position form carries the
/// same record at the same place, no server reader of the control-point
/// fields standing yet. The phase and invulnerable conds are asked; the
/// passtime interception cond's arm (:773) is that mode's, unbuilt.
/// </para>
/// </summary>
[ScriptedEntity(TFProjectileEntityNames.MechanicalArmOrb, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.ParticleSystem, HitEffectRed)]
[Precache(PrecacheKind.ParticleSystem, HitEffectBlue)]
[Precache(PrecacheKind.ParticleSystem, BlastEffectRed)]
[Precache(PrecacheKind.ParticleSystem, BlastEffectBlue)]
[Precache(PrecacheKind.Sound, BlastSound)]
[Precache(PrecacheKind.Sound, ZapSound)]
[Precache(PrecacheKind.Sound, EatSound)]
public sealed class TFMechanicalArmOrb(IEngineEntityPhysics physics, EntityMotion motion,
                                       IEngineWorldQuery world, IEngineDamage damage,
                                       IEnginePlayers players, IEngineSound sound,
                                       IUniformRandom random, ITFPlayerConditions conditions,
                                       ITFPlayerStats stats, TFDamageScopes scopes,
                                       IEntityDirectory directory, ITempEnts temps,
                                       IEnginePrecache precache, ILog log)
    : FlyingProjectile(physics, motion)
{
    /// <summary>The zap's hit particle, per side (<c>ZapPlayer</c>, :713).</summary>
    public const string HitEffectRed = "dxhr_lightningball_hit_red";
    public const string HitEffectBlue = "dxhr_lightningball_hit_blue";

    /// <summary>The blast's particle, per side (<c>ExplodeAndRemove</c>, :676).</summary>
    public const string BlastEffectRed = "drg_cow_explosioncore_normal";
    public const string BlastEffectBlue = "drg_cow_explosioncore_normal_blue";

    /// <summary>The blast's sound, on the orb (:681).</summary>
    public const string BlastSound = "Halloween.spell_lightning_impact";

    /// <summary>The zap's sound, once a think zapped anyone (:792).</summary>
    public const string ZapSound = "TFPlayer.MedicChargedDeath";

    /// <summary>The eaten projectile's sound, on the orb (:850).</summary>
    public const string EatSound = "Weapon_Upgrade.ExplosiveHeadshot";

    /// <summary>The orb think's name, stock's context (<c>Spawn</c>, :607).</summary>
    public const string OrbThinkContext = "OrbThink";

    /// <summary>And the death's (:608).</summary>
    public const string RemoveThinkContext = "ExplodeAndRemoveThink";

    /// <summary>The blast's sixteen (<c>CheckForPlayers(16)</c>, :684).</summary>
    public const int BlastZaps = 16;

    /// <summary>The launcher's script, the hit record's weapon name (<c>SetWeapon</c>, :736).</summary>
    public const string ScriptName = "tf_weapon_mechanical_arm";

    /// <summary>Stock's own caps: five zappable entities a think (:742), sixteen the eat's sphere (:811).</summary>
    private const int MaxZapCandidates = 5;
    private const int MaxEatCandidates = 16;

    private readonly IEngineWorldQuery _world = world;
    private readonly IEngineDamage _damage = damage;
    private readonly IEnginePlayers _players = players;
    private readonly IEngineSound _sound = sound;
    private readonly IUniformRandom _random = random;
    private readonly ITFPlayerConditions _conditions = conditions;
    private readonly ITFPlayerStats _stats = stats;
    private readonly TFDamageScopes _scopes = scopes;
    private readonly IEntityDirectory _directory = directory;
    private readonly ITempEnts _temps = temps;
    private readonly IEnginePrecache _precache = precache;
    private readonly ILog _log = log;

    /// <summary>Whose orb: the owner's index, the zap's attacker and the eat's payee.</summary>
    private int _ownerIndex;

    /// <summary>The zap gate (<c>m_flOrbNextAttackTime</c>, -1 at the spawn, :609, :803).</summary>
    private float _nextAttack = -1f;

    protected override float GravityScale => 0f;

    protected override string ProjectileModel => "models/weapons/w_models/w_drg_ball.mdl";

    /// <summary>Stock's one-unit collision box (<c>Spawn</c>, :598).</summary>
    protected override Vector HullExtent => new(1f, 1f, 1f);

    /// <summary>The launch that armed it: the owner, for every later ask.</summary>
    public void ArmedBy(int ownerIndex)
    {
        _ownerIndex = ownerIndex;
        _nextAttack = -1f;
    }

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Both books of stock's Spawn (:607-609): the orb think at once and
        // the death at the lifetime, named contexts both.
        SetContextThink(OrbThinkContext, 0f);
        SetContextThink(RemoveThinkContext, TFProjectileDefs.MechanicalArmOrbLifetime);
    }

    /// <summary>
    /// The two named thinks (<c>OrbThink</c>, :881-891): zap when the gate is
    /// past, eat always, and the tenth-second rebook, whatever happened.
    /// </summary>
    public override void OnThink(string context)
    {
        if (context == RemoveThinkContext)
        {
            ExplodeAndRemove();
            return;
        }

        if (context != OrbThinkContext)
            return;

        if (GameTime >= _nextAttack)
            CheckForPlayers(TFProjectileDefs.MechanicalArmOrbZapTargets);

        CheckForProjectiles();
        SetContextThink(OrbThinkContext, TFProjectileDefs.MechanicalArmOrbThink);
    }

    /// <summary>
    /// <c>RocketTouch</c> (:651-668): a player is flown through — the orb
    /// passes bodies on its way — and everything else solid ends it, the
    /// blast <see cref="ExplodeAndRemove"/> is. Stock's ignore-list
    /// (<c>ShouldProjectileIgnore</c>, :617-646) is the trigger, respawn-room
    /// and LOD brushes the touch never reports here; the owner is the shell's
    /// own never-the-shooter arm, and the sky surface's own arm (:657) rides
    /// the same removal the touch's does.
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (toucher == Shooter || Entities.IsPlayer(toucher))
            return;

        Spend(toucher);
    }

    /// <summary>The touch's end (<c>RocketTouch</c>'s last arm, :667).</summary>
    protected override void Impact(EntityHandle hit) => ExplodeAndRemove();

    /// <summary>
    /// <c>ExplodeAndRemove</c> (:673-696): the team's blast particle and the
    /// impact sound at the orb, one last wide zap — sixteen, the
    /// shoot-the-floor punish — and gone.
    /// </summary>
    public void ExplodeAndRemove()
    {
        Particle(BlastEffect(), Origin);
        _sound.EntityEmitSound(Handle, BlastSound);
        CheckForPlayers(BlastZaps);
        Remove();
    }

    /// <summary>
    /// <c>CheckForPlayers</c> (:727-804): enemies of the owner's side alive
    /// and in the orb's radius, seen, unphased and not invulnerable, zapped
    /// in the shuffled order the sphere hands back — <paramref
    /// name="numToZap"/> of them, <c>tf_mecharm_orb_zap_damage</c> each,
    /// shock over the plasma custom. A hit sounds once (:790), and the owner
    /// inside the square of eighty takes the same zap, the punish :793-800
    /// keeps.
    /// </summary>
    private void CheckForPlayers(int numToZap)
    {
        EntityHandle owner = _players.GetHandle(_ownerIndex);
        if (!Entities.IsValid(owner))
            return;

        int team = _players.GetTeam(owner);
        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxZapCandidates];
        int count = _world.EntitiesInSphere(Origin, TFProjectileDefs.MechanicalArmOrbSize, nearby);

        Shuffle(nearby, count);

        int hits = 0;
        for (int i = 0; i < count && hits < numToZap; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || victim == owner || !Entities.IsPlayer(victim))
                continue;

            if (!_players.IsAlive(victim) || _players.GetTeam(victim) == team)
                continue;

            // The phase and the invulnerable arms (:773-777).
            if (_conditions.InCond(victim, TFCond.Phase) || _conditions.InCond(victim, TFCond.Invulnerable))
                continue;

            // FVisible over MASK_OPAQUE (:767): no wall between, whatever the
            // shot line below says. Both lines start inside the orb, so both
            // skip it, as FVisible's and the team filter's traces do.
            TraceResult seen = _world.TraceLine(Origin, Entities.GetWorldSpaceCenter(victim), TraceMask.Opaque, Handle);
            if (seen.Fraction != 1f && seen.Entity != victim)
                continue;

            // The zap's line (:781-783): MASK_SHOT less the hitboxes, only
            // the world between orb and victim refusing it.
            TraceResult shot = _world.TraceLine(Origin, Entities.GetOrigin(victim), TraceMask.ShotHull, Handle);
            if (shot.Fraction != 1f && IsOrbWorld(shot.Entity))
                continue;

            Zap(victim);
            hits++;
        }

        if (hits > 0)
        {
            _sound.EntityEmitSound(Handle, ZapSound);

            // The owner inside the square of eighty (:795), the same zap.
            if ((Entities.GetOrigin(owner) - Origin).LengthSquared < 80f * 80f)
                Zap(owner);
        }

        _nextAttack = GameTime + TFProjectileDefs.MechanicalArmOrbZapRefire;
    }

    /// <summary><c>DidHitWorld</c>: entry 0, the world entity.</summary>
    private bool IsOrbWorld(EntityHandle hit) => hit != EntityHandle.None && Entities.GetIndex(hit) == 0;

    /// <summary>
    /// The zap (<c>ZapPlayer</c>, :701-722): the hit particle at the victim
    /// and the damage, stock's numbers, the plasma custom over the shock —
    /// the record's <c>TF_DMG_CUSTOM_PLASMA</c> and <c>DMG_SHOCK</c> (:738, :740).
    /// </summary>
    private void Zap(EntityHandle victim)
    {
        Particle(HitEffect(), Entities.GetWorldSpaceCenter(victim));
        using TFDamageScopes.Scope plasma = _scopes.Push(TFDamageCustom.Plasma, TFWeaponId.MechanicalArm,
                                                        ScriptName);
        _damage.TakeDamage(victim, _players.GetHandle(_ownerIndex), TFProjectileDefs.MechanicalArmOrbZapDamage,
                           DamageType.Shock, forceOrigin: Origin);
    }

    /// <summary>
    /// <c>CheckForProjectiles</c> (:809-876): the enemy grenades within the
    /// orb's radius — the sphere bloated to one and a half times, the hit at
    /// the radius itself — seen, are eaten: the hit particle and the sound,
    /// the projectile gone, its owner's side paying the eater's +2 bonus
    /// points (:864-867). Another orb is the annihilation :852-858 keeps: it
    /// dies the same death, and so do we.
    /// </summary>
    private void CheckForProjectiles()
    {
        float reach = TFProjectileDefs.MechanicalArmOrbSize * 1.5f;
        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxEatCandidates];
        int count = _world.EntitiesInSphere(Origin, reach, nearby);

        EntityHandle owner = _players.GetHandle(_ownerIndex);
        int ownTeam = Entities.IsValid(owner) ? _players.GetTeam(owner) : 0;
        bool annihilate = false;

        for (int i = 0; i < count; i++)
        {
            EntityHandle target = nearby[i];
            if (target == EntityHandle.None || target == Handle)
                continue;

            // Same side stands (:833), the team each projectile's own wire
            // carries; an unnumbered one is nobody's to eat.
            if (_directory.Get(target) is not ScriptedEntity projectile
                || TeamOf(projectile) == 0 || TeamOf(projectile) == ownTeam)
                continue;

            if ((Entities.GetOrigin(target) - Origin).LengthSquared > TFProjectileDefs.MechanicalArmOrbSize
                                                              * TFProjectileDefs.MechanicalArmOrbSize)
                continue;

            // FVisible over MASK_SOLID (:839).
            TraceResult seen = _world.TraceLine(Origin, Entities.GetWorldSpaceCenter(target), TraceMask.Solid);
            if (seen.Fraction != 1f && seen.Entity != target)
                continue;

            Particle(HitEffect(), Entities.GetWorldSpaceCenter(target));
            _sound.EntityEmitSound(Handle, EatSound);

            if (projectile is TFMechanicalArmOrb other)
            {
                other.ExplodeAndRemove();
                annihilate = true;
            }
            else
            {
                // Stock's Destroy(true, false) breaks the projectile where it
                // stands; the dud's client break model is that mirror's (the
                // minigun sweep's same arm).
                Entities.Remove(target);
            }

            // The eat's pay (:864-867): the owner's bonus points, two.
            if (Entities.IsValid(owner))
                _stats.Increment(owner, TFStat.BonusPoints, 2);
        }

        if (annihilate)
            ExplodeAndRemove();
    }

    /// <summary>The victim team, each projectile's own wire's (<c>InSameTeam</c>'s read, :833); a sibling orb's is its owner's.</summary>
    private int TeamOf(ScriptedEntity projectile) => projectile switch
    {
        TFMechanicalArmOrb other => other.OwnTeam(),
        TFRocket rocket => rocket.State.TeamNumber,
        TFPipebomb pipe => pipe.State.TeamNumber,
        TFStickybomb bomb => bomb.State.TeamNumber,
        TFFlare flare => flare.State.TeamNumber,
        TFJar jar => jar.State.TeamNumber,
        TFHealingBolt bolt => bolt.State.TeamNumber,
        _ => 0,
    };

    /// <summary>The orb's side, the owner's team: the particle the effect names.</summary>
    private string HitEffect() => OwnTeam() == (int)TFTeam.Blue ? HitEffectBlue : HitEffectRed;

    private string BlastEffect() => OwnTeam() == (int)TFTeam.Blue ? BlastEffectBlue : BlastEffectRed;

    private int OwnTeam() => _players.GetTeam(_players.GetHandle(_ownerIndex));

    /// <summary>The plain particle dispatch, at <paramref name="at"/>: the record stock's complex entry leaves at the same place.</summary>
    private void Particle(string effect, Vector at)
    {
        TeEffectData data = new()
        {
            Scale = 1f,
            HitBox = _precache.ParticleSystemIndex(effect),
            Origin = at,
            Start = at,
        };
        _temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in data);
    }

    /// <summary>The shuffle (:746-749): Fisher-Yates over what the sphere handed back, off the side's stream.</summary>
    private void Shuffle(Span<EntityHandle> list, int count)
    {
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)_random.RandomFloat(0f, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
