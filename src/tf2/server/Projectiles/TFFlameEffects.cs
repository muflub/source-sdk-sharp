//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Server.Player;
using TF2.Server.Weapons;

namespace TF2.Server.Projectiles;

/// <summary>
/// The flame arm of the weapon effects (<c>ITFWeaponEffects.Flames</c> and
/// <c>AirBlast</c>, Stage 2.11): what stock's
/// <c>CTFFlameThrower::PrimaryAttack</c> does per tick after the shell — one
/// <c>CTFFlameManager</c> per burst-holding weapon, asked
/// <c>UpdateDamage</c> and <c>AddPoint</c> each firing interval out of the
/// muzzle (<c>tf_flamethrower_new_flame_offset</c>,
/// <c>tf_weapon_flamethrower.cpp:858-898</c>) — and what its
/// <c>SecondaryAttack</c> does: the airblast
/// (<c>CTFWeaponBase::DeflectProjectiles</c>, <c>tf_weaponbase.cpp:5900</c>,
/// the cray force <c>tf_weapon_flamethrower.cpp:1306-1566</c>). The damage
/// and the crit flag are the behaviour's
/// (<see cref="TFFlamethrowerBehaviour"/>).
///
/// <para>
/// Stock's sweep collects the sphere, keeps what it can see and what is
/// deflectable, retests players against a truncated cone
/// (<c>physcollision->IsBoxIntersectingCone</c>, :1685-1699) — the retest is
/// <see cref="TFAirBlastCone"/>, over the target's collision box and the
/// doubled deflection radius stock cones — and the cray force reflects a
/// grounded target off their ground and lifts them to its minimum Z
/// (<c>ComputeCrayAirBlastForce</c>, :1453-1565).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFFlameEffects)])]
public sealed class TFFlameEffects(IEngineEntities entities, IEnginePlayers players,
                                   IEntityDirectory directory, IEngineWorldQuery world,
                                   IEngineNetworking networking, ITFPlayerConditions conditions,
                                   ITFWeaponOwners owners, ILog log,
                                   TFAirBlastCrayConVar airblastCray,
                                   TFFlameThrowerBurstZvelocityConVar burstZvelocity,
                                   TFPushbackScaleConVar pushbackScale,
                                   TFPushbackScaleVerticalConVar pushbackScaleVertical,
                                   TFPlayerCarriers carriers,
                                   ILagCompensation? lagCompensation = null)
    : ITFFlameEffects, IOrderedHook
{
    /// <summary>Stage 2.11's band (+960..+979 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 960;

    /// <summary><c>DeflectProjectiles</c>' 64 (tf_weaponbase.cpp:5918).</summary>
    private const int MaxDeflected = 64;

    /// <summary>Stock's <c>MAX_TRACE_LENGTH</c>.</summary>
    private const float MaxTraceLength = 5676f;

    /// <summary>One manager per burst-holding owner, as stock's m_hFlameManager is per weapon.</summary>
    private readonly Dictionary<EntityHandle, EntityHandle> _managers = [];

    private readonly TFAirBlastCrayConVar _airblastCray = airblastCray;
    private readonly TFFlameThrowerBurstZvelocityConVar _burstZvelocity = burstZvelocity;
    private readonly TFPushbackScaleConVar _pushbackScale = pushbackScale;
    private readonly TFPushbackScaleVerticalConVar _pushbackVertical = pushbackScaleVertical;
    private readonly TFPlayerCarriers _carriers = carriers;

    public int HookOrder => Order;

    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        // The flame's session (tf_weapon_flamethrower.cpp:808, :934): the
        // per-tick cone work sees the other players where the pyro saw them.
        lagCompensation?.Start(owner);
        try
        {
            Vector forward = Angles.Forward(entities.GetEyeAngles(owner));

            // The muzzle: the eye plus the flame offset's forward and right (the
            // convar's "40 5 0"; the up is 0) — GetFlameOriginPos
            // (tf_weapon_flamethrower.cpp:2111-2128).
            Vector right = Angles.RightUp(forward).Right;
            Vector muzzle = entities.GetEyePosition(owner)
                          + forward * TFAirBlastDefs.MuzzleForward + right * TFAirBlastDefs.MuzzleRight;

            if (ManagerFor(owner) is not { } manager)
                return;

            // UpdateDamage and AddPoint each firing interval (:892-897). Stock
            // reads the pyro's class off the attacker per collide; the seam reads
            // classes by index, so the answer rides in here.
            manager.UpdateDamage(script, shot, TFFlameManagerDefs.BurnFrequency,
                                 owners.ClassOf(attack.Command.OwnerIndex) == TFClass.Pyro);

            // The shooter's own velocity rides along (InitializePoint,
            // tf_flame.cpp:250-260).
            manager.AddPoint(muzzle, forward, entities.GetVelocity(owner));
            log.Msg($"[TF2/Flames] flame point by {owner}, {manager.PointCount} live\n");
        }
        finally
        {
            lagCompensation?.Finish(owner);
        }
    }

    /// <summary>
    /// The owner's manager, created on the first tick and re-made when its
    /// last point removed it — stock's m_hFlameManager is a handle the weapon
    /// re-fills when it goes null (:879-890).
    /// </summary>
    private TFFlameManager? ManagerFor(EntityHandle owner)
    {
        if (_managers.TryGetValue(owner, out EntityHandle handle)
            && directory.Get(handle) is TFFlameManager alive)
            return alive;

        handle = entities.Create(TFFlameManagerDefs.ClassName);
        if (!entities.IsValid(handle))
            return null;

        entities.Spawn(handle);

        if (directory.Get(handle) is not TFFlameManager manager)
            return null;

        _managers[owner] = handle;
        return manager;
    }

    /// <summary>
    /// The blast (<c>DeflectProjectiles</c>): refused outright with the
    /// blaster's eyes under the water (:5906-5907); everything in the
    /// deflection sphere the blaster can see, but never the blaster themself
    /// (:5932) — a rocket is sent back, a player pushed or a burning teammate
    /// put out.
    /// </summary>
    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        if (Underwater(owner))
        {
            log.Msg($"[TF2/Flames] airblast refused, {owner} is underwater\n");
            return;
        }

        Vector forward = Angles.Forward(entities.GetEyeAngles(owner));
        Vector eye = entities.GetEyePosition(owner);
        Vector center = eye + forward * TFAirBlastDefs.DeflectionRadius;

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxDeflected];
        int count = world.EntitiesInSphere(center, TFAirBlastDefs.DeflectionRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle target = nearby[i];
            if (target == EntityHandle.None || target == owner)
                continue;

            // Stock's FVisible (MASK_SOLID, :5938): nothing solid between but
            // the target themselves.
            Vector targetCenter = entities.GetWorldSpaceCenter(target);
            TraceResult seen = world.TraceLine(eye, targetCenter, TraceMask.ShotHull, owner);
            if (seen.Fraction != 1f && seen.Entity != target)
                continue;

            if (directory.Get(target) is TFRocket rocket)
            {
                if (NotOwnTeam(rocket.State.TeamNumber, owner))
                    Deflect(rocket, owner, eye, forward, targetCenter);
            }
            else if (directory.Get(target) is TFPipebomb pipe)
            {
                if (NotOwnTeam(pipe.State.TeamNumber, owner))
                    Deflect(pipe, owner, eye, forward, targetCenter);
            }
            else if (directory.Get(target) is TFStickybomb bomb)
            {
                if (NotOwnTeam(bomb.State.TeamNumber, owner))
                    Deflect(bomb, owner, eye, forward, targetCenter);
            }
            else if (entities.IsPlayer(target))
                DeflectPlayer(target, owner, eye, forward, targetCenter);
        }

        log.Msg($"[TF2/Flames] airblast by {owner} over {count} near\n");
    }

    /// <summary>
    /// Stock's own-team refusal (<c>tf_weapon_flamethrower.cpp:1914-1920</c>,
    /// the passtime ball's excepted — the port has no ball): a projectile of
    /// the blaster's own team is not deflected at all. The team is the wire's,
    /// which is the thrower's or the last deflector's as the type keeps it; 0
    /// is no team at all, never one's own.
    /// </summary>
    private bool NotOwnTeam(int team, EntityHandle deflector)
        => team == 0 || team != players.GetTeam(deflector);

    /// <summary>
    /// The deflection's aim (<c>DeflectEntity</c>'s trace and direction,
    /// <c>tf_weaponbase.cpp:6050-6055</c>) with the old speed applied along it
    /// (:6069-6083, the seam's write reaches the physics object): the ray from
    /// the eyes along the aim stops where it may, and the direction is from
    /// that stop to the target.
    /// </summary>
    private Vector Arc(EntityHandle target, EntityHandle owner, Vector eye, Vector forward, Vector targetCenter)
    {
        TraceResult tr = world.TraceLine(eye, eye + forward * MaxTraceLength, TraceMask.ShotHull, owner);
        Vector direction = (tr.End - targetCenter).Normalized;
        entities.SetVelocity(target, direction * entities.GetVelocity(target).Length);
        return direction;
    }

    /// <summary>
    /// A pipe, through the same <c>DeflectEntity</c> (:6043-6090): the arc
    /// above, then the pipe's own <c>Deflected</c> (:6086) — and the facing to
    /// match the direction it leaves on (:6088-6090).
    /// </summary>
    private void Deflect(TFPipebomb pipe, EntityHandle owner, Vector eye, Vector forward, Vector pipeCenter)
    {
        Vector direction = Arc(pipe.Handle, owner, eye, forward, pipeCenter);
        pipe.DeflectedBy(owner, conditions.InCond(owner, TFCond.Critboosted),
                         players.GetActiveWeapon(owner));
        entities.SetAngles(pipe.Handle, Angles.FromVector(direction));
        log.Msg($"[TF2/Flames] {TFProjectileEntityNames.Pipebomb} deflected by {owner}\n");
    }

    /// <summary>
    /// A sticky, through the same <c>DeflectEntity</c> (:6043-6090): the arc,
    /// then the sticky's own <c>Deflected</c> (:6086) — the blow whose force
    /// is the aim times <c>DeflectionForce(WorldAlignSize, 90, 12)</c>
    /// (<c>tf_weaponbase.cpp:6103</c>) — and the facing (:6088-6090).
    /// </summary>
    private void Deflect(TFStickybomb bomb, EntityHandle owner, Vector eye, Vector forward, Vector bombCenter)
    {
        Vector direction = Arc(bomb.Handle, owner, eye, forward, bombCenter);
        (Vector mins, Vector maxs) = entities.GetBounds(bomb.Handle);
        bomb.DeflectedBy(owner, direction * DeflectionForce(maxs - mins, 90f, 12f));
        entities.SetAngles(bomb.Handle, Angles.FromVector(direction));
        log.Msg($"[TF2/Flames] {TFProjectileEntityNames.Stickybomb} deflected by {owner}\n");
    }

    /// <summary>
    /// <c>CTFWeaponBase::DeflectionForce</c> (<c>tf_weaponbase.cpp:6103-6113</c>)
    /// — the same formula as the flamethrower's <c>AirBurstDamageForce</c>
    /// (<c>tf_weapon_flamethrower.cpp:950-960</c>), the old arm's push: the
    /// damage times the reference box's volume constant over the target's,
    /// times the scale — at most 1000.
    /// </summary>
    private static float DeflectionForce(Vector size, float damage, float scale)
        => MathF.Min(damage * (48f * 48f * 82f) / (size.X * size.Y * size.Z) * scale, 1000f);

    /// <summary>
    /// A rocket (<c>DeflectEntity</c>, <c>tf_weaponbase.cpp:6043-6086</c>):
    /// the aim's ray stopped where it may, and the direction is from the
    /// rocket to that stop; the rocket leaves at its old speed along it.
    /// </summary>
    private void Deflect(TFRocket rocket, EntityHandle owner, Vector eye, Vector forward, Vector rocketCenter)
    {
        TraceResult tr = world.TraceLine(eye, eye + forward * MaxTraceLength, TraceMask.ShotHull, owner);
        Vector direction = (tr.End - rocketCenter).Normalized;

        rocket.DeflectBy(owner, direction * entities.GetVelocity(rocket.Handle).Length,
                         conditions.InCond(owner, TFCond.Critboosted));
        log.Msg($"[TF2/Flames] {TFProjectileEntityNames.Rocket} deflected by {owner}\n");
    }

    /// <summary>
    /// A player (<c>CTFFlameThrower::DeflectPlayer</c>,
    /// <c>tf_weapon_flamethrower.cpp:1616-1855</c>): a burning teammate is put
    /// out (:1620-1622) and nothing else touches them; an enemy is retested
    /// against the cone over their collision box (:1685-1699) and, still in,
    /// pushed by the arm <c>tf_airblast_cray</c> names — the cray force
    /// (:1720-1762: the reflect, the remaining power and the conditions, off
    /// the ground with the impulse) or the old pushback (:1764-1830).
    /// </summary>
    private void DeflectPlayer(EntityHandle target, EntityHandle owner, Vector eye, Vector forward,
                               Vector targetCenter)
    {
        if (players.GetTeam(target) == players.GetTeam(owner))
        {
            if (conditions.InCond(target, TFCond.Burning))
            {
                conditions.Remove(target, TFCond.Burning);
                log.Msg($"[TF2/Flames] airblast extinguished {target}\n");
            }
            return;
        }

        // The cone retest (stock's is over GetAbsOrigin + WorldAlignMins/Maxs,
        // against a cone twice the deflection radius long, :1685-1699).
        (Vector mins, Vector maxs) = entities.GetBounds(target);
        Vector targetOrigin = entities.GetOrigin(target);
        if (!TFAirBlastCone.BoxIntersects(targetOrigin + mins, targetOrigin + maxs,
                                          eye, forward, 2f * TFAirBlastDefs.DeflectionRadius,
                                          TFAirBlastDefs.AirBlastConeAngle))
            return;

        // The push direction is the aim with the direction to the target's
        // yaw (:1722-1736): pitch control is off.
        Vector toTarget = (targetCenter - entities.GetWorldSpaceCenter(owner)).Normalized;
        Vector aimAngles = Angles.FromVector(Angles.Forward(entities.GetEyeAngles(owner)));
        Vector toAngles = Angles.FromVector(toTarget);
        Vector pushDirection = Angles.Forward(new Vector(aimAngles.X, toAngles.Y, 0f));

        if (_airblastCray.Value)
        {
            // The cray arm (:1720-1762): the conds, then the force — the
            // ground strip is the seam's clearGround, stock's flag and
            // SetGroundEntity(NULL) pair (:1741-1742).
            conditions.Add(target, TFCond.AirCurrent);
            conditions.Add(target, TFCond.KnockedIntoAir);
            conditions.Add(target, TFCond.LostFooting, duration: TFAirBlastDefs.LoseFootingDuration);
            entities.ApplyVelocityImpulse(target, CrayForce(target, pushDirection), clearGround: true);
        }
        else
        {
            BlastPlayerTheOldWay(target, owner, toTarget);
        }

        log.Msg($"[TF2/Flames] airblast pushed {target}\n");
    }

    /// <summary>
    /// The old airblast (:1764-1830): the stun unless the target is already
    /// airborne, <c>AirBurstDamageForce</c> (:950-960 — the deflection
    /// force's own formula) along the aim and the burst's vertical kick, the
    /// velocity stomped first (:1826), the push through the generic impulse
    /// (<c>tf_player.cpp:3369</c>) — its ground floor the flag answers, its
    /// vulnerability attributes and reverse blast arms waits, as the cray
    /// arm's own do.
    /// </summary>
    private void BlastPlayerTheOldWay(EntityHandle target, EntityHandle owner, Vector toTarget)
    {
        if (!conditions.InCond(target, TFCond.KnockedIntoAir))
            _carriers.Part<TFStunStack>(target.EntryIndex)?.Stun(target, TFAirBlastDefs.OldAirblastStunDuration,
                                                                  TFAirBlastDefs.OldAirblastStunAmount,
                                                                  TFStunFlags.Movement, owner);

        (Vector mins, Vector maxs) = entities.GetBounds(target);
        // The debug scales ride the power and the lift (:1789, :1811) — the
        // push is the force under tf_pushbackscalescale, the Z the burst
        // velocity under tf_pushbackscalescale_vertical.
        Vector push = toTarget * (DeflectionForce(maxs - mins, TFAirBlastDefs.OldAirblastDamage,
                                                  TFAirBlastDefs.OldAirblastPushScale)
                                  * _pushbackScale.Value)
                    + new Vector(0f, 0f, _burstZvelocity.Value * _pushbackVertical.Value);

        if (networking.EntityGetState(target, out _).HasFlag(EntityState.OnGround)
            && push.Z < TFAirBlastDefs.JumpMinSpeed)
            push = new Vector(push.X, push.Y, TFAirBlastDefs.JumpMinSpeed);

        entities.SetVelocity(target, Vector.Zero);
        conditions.Add(target, TFCond.KnockedIntoAir);
        // The flag strip of the generic impulse (:3396); the ground entity
        // goes with it, as the seam's clearGround clears both.
        entities.ApplyVelocityImpulse(target, push, clearGround: true);
    }

    /// <summary>
    /// <c>ComputeCrayAirBlastForce</c> (:1306-1566): the incoming momentum
    /// along the aim is reflected by the coefficient, whatever power is left
    /// over after the reflection's cost pushes (:1431-1451), a grounded
    /// target's into-ground momentum is mirrored off their ground normal
    /// (:1465-1505, traced over the player hull — stock's MASK_PLAYERSOLID,
    /// which the seam's solid mask stands in for), and a grounded target is
    /// lifted to <c>tf_airblast_cray_ground_minz</c> by power stolen from the
    /// push's XY (:1513-1564).
    /// </summary>
    private Vector CrayForce(EntityHandle target, Vector aim)
    {
        Vector targetVelocity = entities.GetVelocity(target);
        bool onGround = networking.EntityGetState(target, out _).HasFlag(EntityState.OnGround);

        Vector result = Vector.Zero;

        float momentumAlongAim = targetVelocity.Dot(aim);
        if (momentumAlongAim < 0f)
            result += aim * (-TFAirBlastDefs.ReflectCoeff * momentumAlongAim);

        // The debug scale lands in the power the push spends
        // (tf_weapon_flamethrower.cpp:1330-1331).
        float remainingPush = TFAirBlastDefs.AirBlastPower * _pushbackScale.Value
                              - result.Length * TFAirBlastDefs.ReflectCostCoeff;
        if (remainingPush > 0f)
            result += aim * remainingPush;

        if (onGround)
        {
            // The ground normal, from a hull trace 5 down (:1472-1475).
            (Vector mins, Vector maxs) = entities.GetBounds(target);
            Vector targetOrigin = entities.GetOrigin(target);
            SurfaceTraceResult ground = world.TraceBox(targetOrigin, targetOrigin + new Vector(0f, 0f, -5f),
                                                       mins, maxs, TraceMask.Solid, target);
            if (ground.Hit)
            {
                float fromGround = (targetVelocity + result).Dot(ground.Normal);
                if (fromGround < 0f)
                    result += ground.Normal * (-2f * fromGround);
            }

            // The minimum Z, bought with the push's own XY (:1513-1564).
            if (TFAirBlastDefs.AirBlastGroundMinZ > 0f)
            {
                float addZ = TFAirBlastDefs.AirBlastGroundMinZ - (targetVelocity + result).Z;
                if (addZ > 0f)
                {
                    float initialXY = new Vector(result.X, result.Y, 0f).Length;
                    addZ = MathF.Min(addZ, initialXY);

                    float magnitudeSquared = result.LengthSquared;
                    if (magnitudeSquared < addZ * addZ)
                    {
                        // Redirect all available power upwards.
                        result = new Vector(0f, 0f, MathF.Sqrt(magnitudeSquared));
                    }
                    else
                    {
                        // Redirect to a (z + addZ) Z component without
                        // changing the magnitude or the XY projection:
                        // N = √(−addZ² − 2·addZ·z + x² + y²) / √(x² + y²).
                        float resultXYSquared = new Vector(result.X, result.Y, 0f).LengthSquared;
                        if (resultXYSquared > 0f)
                        {
                            // Stock's FastSqrt(0) here is a 0/0 NaN; a push
                            // with no XY is left as it is instead.
                            float scaleBase = -(addZ * addZ) - 2f * addZ * result.Z + resultXYSquared;
                            float scale = MathF.Sqrt(MathF.Max(scaleBase, 0f)) / MathF.Sqrt(resultXYSquared);
                            result = new Vector(result.X * scale, result.Y * scale, result.Z + addZ);
                        }
                    }
                }
            }
        }

        // The vertical scale lands once the ground work is done, on the
        // result's Z (:1574-1577) — the multipliers stock starts at 1 and the
        // attributes raise.
        if (_pushbackVertical.Value != 1f)
            result = new Vector(result.X, result.Y, result.Z * _pushbackVertical.Value);

        return result;
    }

    /// <summary>The blaster's eyes under the water: the movement state's water level at WL_Eyes.</summary>
    private bool Underwater(EntityHandle owner)
        => directory.Get(owner) is SsPlayer player && player.State.WaterLevel == GameMovement.WlEyes;
}

/// <summary>
/// The airblast's constants and the muzzle offset: the flamethrower's convars
/// (<c>tf_weapon_flamethrower.cpp:61-122</c>) and
/// <c>tf_flamethrower_new_flame_offset</c> "40 5 0" (:61) at their defaults.
/// </summary>
public static class TFAirBlastDefs
{
    /// <summary>The flame's start relative to the eye, the convar's forward and right.</summary>
    public const float MuzzleForward = 40f;
    public const float MuzzleRight = 5f;

    /// <summary><c>CTFWeaponBase::GetDeflectionRadius</c> (<c>tf_weaponbase.h:427</c>): the blast's reach.</summary>
    public const float DeflectionRadius = 128f;

    public const float AirBlastPower = 600f;
    public const float ReflectCoeff = 2f;
    public const float ReflectCostCoeff = 0.5f;
    public const float LoseFootingDuration = 0.5f;

    /// <summary>
    /// <c>tf_airblast_cray_ground_minz</c> 100: a grounded target's minimum
    /// away-from-ground speed after the push. <c>tf_airblast_cray_ground_reflect</c> defaults on.
    /// </summary>
    public const float AirBlastGroundMinZ = 100f;

    /// <summary>The old arm's stun (<c>tf_weapon_flamethrower.cpp:1780</c>) and burst force (:1784).</summary>
    public const float OldAirblastStunDuration = 0.5f;
    public const float OldAirblastStunAmount = 1f;
    public const float OldAirblastDamage = 60f;
    public const float OldAirblastPushScale = 6f;

    /// <summary>JUMP_MIN_SPEED (tf_player.cpp:151): the generic impulse's floor on a grounded target's Z.</summary>
    public const float JumpMinSpeed = 268.3281572999747f;

    /// <summary><c>tf_flamethrower_airblast_cone_angle</c> 35: the cone retest's half-angle, in degrees.</summary>
    public const float AirBlastConeAngle = 35f;
}

/// <summary>
/// A box against a truncated cone — <c>physcollision->IsBoxIntersectingCone</c>
/// over vphysics's <c>truncatedcone_t</c> (apex at <c>origin</c>, the base disc
/// of radius <c>h·tanθ</c> at <c>origin + h·normal</c>) — as plain math, for
/// the airblast's player retest (<c>tf_weapon_flamethrower.cpp:1685-1699</c>).
///
/// <para>
/// A separating-axis test over the cone's face families. The caps: the cone
/// lives in <c>0 ≤ normal·(p−apex) ≤ h</c>. The box's three axes against the
/// cone's support, which is the hull of the apex and the base disc and
/// reaches along a unit direction <c>d</c> to
/// <c>apex·d + h·(d·normal) ± r·√(1−(d·normal)²)</c>. The side planes, one
/// per radial direction φ through the apex with normal
/// <c>(u_φ − tanθ·normal)/L</c>: a corner of radius ρ and height z along the
/// axis clears the plane at φ when <c>cos(φ−β) ≥ tanθ·z/ρ</c> for its
/// azimuth β, so the box clears a whole plane when the corners' arcs share a
/// φ — which, when they share any, they share at an arc's end. None of the
/// three families separating means the box meets the cone; a touching tie
/// counts as meeting, as GJK's <c>startsolid</c> answers.
/// </para>
/// </summary>
public static class TFAirBlastCone
{
    public static bool BoxIntersects(Vector boxMins, Vector boxMaxs, Vector apex,
                                     Vector normal, float height, float halfAngleDegrees)
    {
        float k = MathF.Tan(halfAngleDegrees * MathF.PI / 180f);
        float radius = height * k;

        // The caps: the box's two faces in axis order, whichever end is which.
        float atMins = normal.Dot(boxMins - apex);
        float atMaxs = normal.Dot(boxMaxs - apex);
        float lo = MathF.Min(atMins, atMaxs);
        float hi = MathF.Max(atMins, atMaxs);
        if (hi < 0f || lo > height)
            return false;

        // The box's axes against the cone's support.
        if (SupportDisjoint(1f, 0f, 0f) || SupportDisjoint(0f, 1f, 0f) || SupportDisjoint(0f, 0f, 1f))
            return false;

        return !SidePlanesSeparate();

        bool SupportDisjoint(float ex, float ey, float ez)
        {
            float alongNormal = ex * normal.X + ey * normal.Y + ez * normal.Z;
            float lateral = radius * MathF.Sqrt(MathF.Max(0f, 1f - alongNormal * alongNormal));
            float atApex = ex * apex.X + ey * apex.Y + ez * apex.Z;
            float atBase = atApex + height * alongNormal;
            float coneLo = MathF.Min(atApex, atBase - lateral);
            float coneHi = MathF.Max(atApex, atBase + lateral);
            float boxLo = ex * boxMins.X + ey * boxMins.Y + ez * boxMins.Z;
            float boxHi = ex * boxMaxs.X + ey * boxMaxs.Y + ez * boxMaxs.Z;
            return boxHi < coneLo || boxLo > coneHi;
        }

        bool SidePlanesSeparate()
        {
            // A basis across the axis, for the corners' azimuths.
            Vector helper = MathF.Abs(normal.X) <= MathF.Abs(normal.Y) && MathF.Abs(normal.X) <= MathF.Abs(normal.Z)
                ? new Vector(1f, 0f, 0f)
                : (MathF.Abs(normal.Y) <= MathF.Abs(normal.Z) ? new Vector(0f, 1f, 0f) : new Vector(0f, 0f, 1f));
            Vector u = normal.Cross(helper).Normalized;
            Vector v = normal.Cross(u);

            Span<float> beta = stackalloc float[8];
            Span<float> clearsBelow = stackalloc float[8];
            Span<float> ends = stackalloc float[16];
            int corners = 0, candidates = 0;

            for (int i = 0; i < 8; i++)
            {
                Vector corner = new(
                    (i & 1) == 0 ? boxMins.X : boxMaxs.X,
                    (i & 2) == 0 ? boxMins.Y : boxMaxs.Y,
                    (i & 4) == 0 ? boxMins.Z : boxMaxs.Z);
                Vector rel = corner - apex;
                float z = normal.Dot(rel);
                Vector radial = rel - normal * z;
                float rho = radial.Length;

                // tanθ·z/ρ: ≤ −1 the corner clears every φ, ≥ 1 none — a
                // corner the side planes can never clear leaves none to
                // separate along. A corner on the axis is ±∞ by its height.
                float t;
                if (rho <= 0f)
                    t = z <= 0f ? -2f : 2f;
                else
                    t = k * z / rho;

                if (t >= 1f)
                    return false;

                beta[corners] = MathF.Atan2(radial.Dot(v), radial.Dot(u));
                clearsBelow[corners] = t;
                corners++;

                if (t > -1f)
                {
                    float half = MathF.Acos(t);
                    ends[candidates++] = beta[corners - 1] - half;
                    ends[candidates++] = beta[corners - 1] + half;
                }
            }

            // Every corner clears every φ: the box is beyond all of them.
            if (candidates == 0)
                return true;

            for (int e = 0; e < candidates; e++)
            {
                float phi = ends[e];
                bool all = true;
                for (int c = 0; c < corners && all; c++)
                    all = MathF.Cos(phi - beta[c]) >= clearsBelow[c];
                if (all)
                    return true;
            }

            return false;
        }
    }
}
