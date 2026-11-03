//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Projectiles;

using TF2.Shared;

namespace TF2.Server.Weapons;

/// <summary>
/// The minigun's arm of the weapon effects (stage 2.3): what stock's
/// <c>CTFMinigun</c> does past the shot itself. The projectile sweep
/// (<c>AttackEnemyProjectiles</c>, <c>shared/tf/tf_weapon_minigun.cpp:564-654</c>)
/// the <c>attack_projectiles</c> item arms on each fired shot: everything
/// deflectable in a 300-unit sphere within 38 units of the aim line, seen,
/// ungated and not grounded, is beaten toward destruction — the weak count
/// twice, the strong and a crit once — and the sweep recharges a tenth of a
/// second, three after a kill. The ring of fire (<c>RingOfFireAttack</c>,
/// :498-558) the <c>ring_of_fire_while_aiming</c> item arms while spun: every
/// half a second, enemies near the ground plane under the gun within 135
/// units, seen and not invulnerable, take the item's plasma, and the ring's
/// particle plays at the shooter.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFMinigunEffects)])]
[Precache(PrecacheKind.Sound, AxeHitSound)]
[Precache(PrecacheKind.Sound, RicochetSound)]
[Precache(PrecacheKind.ParticleSystem, RingParticle)]
public sealed class TFMinigunEffects(IEngineEntities entities, IEnginePlayers players,
                                     IEngineWorldQuery world, IEngineNetworking networking,
                                     IEngineSound sound, IEngineDamage damage,
                                     IEnginePrecache precache, ITempEnts temps,
                                     ITFPlayerConditions conditions, IEntityDirectory directory,
                                     ITFPlayerStats stats, ILog log) : ITFMinigunEffects
{
    /// <summary>The kill's world sound (:634), on the gun.</summary>
    public const string AxeHitSound = "Halloween.HeadlessBossAxeHitWorld";

    /// <summary>The nicked projectile's (:647), on the projectile.</summary>
    public const string RicochetSound = "FX_RicochetSound.Ricochet";

    /// <summary>The ring's particle (:549), at the shooter's origin.</summary>
    public const string RingParticle = "heavy_ring_of_fire";

    /// <summary>Stock's sweep: 300 out (:574).</summary>
    public const float SweepDist = 300f;

    /// <summary>How far off the aim line a hit stands (:575). The miniboss's 56 and gun position wait for a miniboss to hold them.</summary>
    public const float HitDist = 38f;

    /// <summary>The recharge (:576): a tenth, three tenths after a kill by the weak count (:639-642).</summary>
    public const float Recharge = 0.1f;
    public const float WeakRecharge = 0.3f;

    /// <summary>The ring's reach (:510) and refire (:557).</summary>
    public const float RingRadius = 135f;
    public const float RingRefire = 0.5f;

    /// <summary>Stock's own cap on the sweep (:594); the ring's query span rides it.</summary>
    private const int MaxSweep = 32;

    /// <summary>The sweep's recharge, per holder (<c>m_flAegisCheckTime</c>).</summary>
    private readonly Dictionary<EntityHandle, float> _aegis = [];

    /// <summary>The beats each projectile has taken (<c>m_iDestroyableHitCount</c>), toward this arm's own kill.</summary>
    private readonly Dictionary<EntityHandle, int> _hits = [];

    /// <summary>The next ring per holder (<c>m_flNextRingOfFireAttackTime</c>), rung half a second ahead on every spin-up (:479, <see cref="Rearmed"/>).</summary>
    private readonly Dictionary<EntityHandle, float> _rings = [];

    /// <summary>
    /// The sweep (<c>AttackEnemyProjectiles</c>): refused outright inside the
    /// recharge (:566); then, per projectile of another team in the sphere
    /// (:596-600) and on the aim line (:604), visible (:607), not grounded
    /// (:610) — a beat, toward the kill the count and the shot's crit
    /// (<c>m_bCritShot</c>, :629) between them decide.
    /// </summary>
    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        float now = attack.Now;
        if (_aegis.TryGetValue(owner, out float aegis) && now < aegis)
            return;

        float recharge = Recharge;
        Vector gun = entities.GetEyePosition(owner);
        Vector forward = Angles.Forward(entities.GetEyeAngles(owner));
        Vector aimEnd = gun + forward * SweepDist;

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxSweep];
        int count = world.EntitiesInSphere(gun, SweepDist, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle target = nearby[i];
            if (target == EntityHandle.None || target == owner)
                continue;

            if (directory.Get(target) is not ScriptedEntity projectile
                || projectile is not (TFRocket or TFPipebomb or TFStickybomb))
                continue;

            if (TeamOf(projectile) == players.GetTeam(owner))
                continue;

            // The aim line (CalcDistanceToLineSegment over the gun and 300 out, :603-605).
            if (LineDistance(entities.GetOrigin(target), gun, aimEnd) > HitDist)
                continue;

            // FVisible (MASK_SOLID, :607): nothing solid between but the projectile.
            Vector center = entities.GetWorldSpaceCenter(target);
            TraceResult seen = world.TraceLine(gun, center, TraceMask.Solid, owner);
            if (seen.Fraction != 1f && seen.Entity != target)
                continue;

            if (networking.EntityGetState(target, out _).HasFlag(EntityState.OnGround))
                continue;

            int hits = _hits.TryGetValue(target, out int taken) ? taken + 1 : 1;
            _hits[target] = hits;

            // The kill count (:628-630): a crit always, else the attribute's
            // ramp — two beats for the weak count, one from two up.
            int needed = critical ? 1 : (int)CritBucket.RemapValClamped(projectiles, 1f, 2f, 2f, 1f);
            if (hits < needed)
            {
                sound.EntityEmitSound(target, RicochetSound);
                continue;
            }

            // Stock's Destroy(false, true) leaves the dud's break model to the
            // client's own user message; here the projectile is simply gone.
            entities.Remove(target);
            _hits.Remove(target);
            sound.EntityEmitSound(owner, AxeHitSound);
            stats.Increment(owner, TFStat.BonusPoints, 2);
            log.Msg($"[TF2/Minigun] sweep by {owner} broke {target} on beat {hits} of {needed}\n");

            if (projectiles < 2)
                recharge = WeakRecharge;
        }

        _aegis[owner] = now + recharge;
    }

    /// <summary>
    /// <c>SetWeaponState</c>'s other reset (:475-480): leaving the unspun
    /// states rings the next ring half a second ahead, beside the drain clock
    /// the behaviour keeps.
    /// </summary>
    public void Rearmed(in WeaponAttackContext attack) =>
        _rings[players.GetHandle(attack.Command.OwnerIndex)] = attack.Now + RingRefire;

    /// <summary>
    /// The ring (<c>RingOfFireAttack</c>): refused inside its half a second and
    /// before the first spin-up has rung it (:500); then, per player of another
    /// team in the 135-unit sphere (:514-519) whose bounding box stands near
    /// the gun's ground plane (:522-527, the hull as the player's bounds) and
    /// within the radius past the box query (:529-533), visible centre to
    /// centre (:535-544), the item's plasma (<c>DMG_PLASMA</c> at the
    /// shooter's origin, :546); the particle plays at :549 whatever the ring
    /// hit, and the clock rings on (:557).
    /// </summary>
    public void RingOfFire(in WeaponAttackContext attack, int ringDamage)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        float now = attack.Now;
        if (!_rings.TryGetValue(owner, out float next) || next == 0f || next > now)
            return;

        Vector origin = entities.GetOrigin(owner);

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxSweep];
        int count = world.EntitiesInSphere(origin, RingRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || victim == owner || !entities.IsPlayer(victim))
                continue;

            if (players.GetTeam(victim) == players.GetTeam(owner)
                || conditions.InCond(victim, TFCond.Invulnerable))
                continue;

            // The ground plane (GetPlayerMins/Maxs over the victim's origin,
            // :522-527): the player's hull is its collision bounds.
            Vector at = entities.GetOrigin(victim);
            (Vector mins, Vector maxs) = entities.GetBounds(victim);
            if (!(origin.Z > at.Z + mins.Z - 32f && origin.Z < at.Z + maxs.Z))
                continue;

            // The box query's nearest point (:529-533).
            if (!TFRadiusDamage.Reaches(origin, RingRadius, mins + at, maxs + at))
                continue;

            // LOS, centre to centre (MASK_SOLID_BRUSHONLY, :535-544); stock
            // traces from the weapon's centre, the hull the gun hangs from.
            Vector center = entities.GetWorldSpaceCenter(owner);
            TraceResult seen = world.TraceLine(center, entities.GetWorldSpaceCenter(victim),
                                               TraceMask.Brushes, owner);
            if (seen.Fraction != 1f && seen.Entity != victim)
                continue;

            damage.TakeDamage(victim, owner, ringDamage, DamageType.Plasma, forceOrigin: origin);
        }

        TeEffectData ringEffect = new()
        {
            Scale = 1f,
            HitBox = precache.ParticleSystemIndex(RingParticle),
            Origin = origin,
            Start = origin,
        };
        temps.DispatchEffect(TeFilters.Pvs(origin), 0f, "ParticleEffect", in ringEffect);

        _rings[owner] = now + RingRefire;
    }

    /// <summary>The projectile's team, the wire's (<see cref="TFFlameEffects"/>' reads).</summary>
    private static int TeamOf(ScriptedEntity projectile) => projectile switch
    {
        TFRocket rocket => rocket.State.TeamNumber,
        TFPipebomb pipe => pipe.State.TeamNumber,
        TFStickybomb bomb => bomb.State.TeamNumber,
        _ => 0,
    };

    /// <summary><c>CalcDistanceToLineSegment</c> (mathlib_base.cpp): <paramref name="p"/> to the segment.</summary>
    private static float LineDistance(in Vector p, in Vector a, in Vector b)
    {
        Vector ab = b - a;
        float t = ab.LengthSquared > 0f ? Math.Clamp((p - a).Dot(ab) / ab.LengthSquared, 0f, 1f) : 0f;
        return (p - (a + ab * t)).Length;
    }

}
