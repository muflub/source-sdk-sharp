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
/// rpg_missile: what the RPG fires, stock HL2MP's CMissile
/// (shared/hl2mp/weapon_rpg.cpp). The kill feed names it rpg_missile
/// (<see cref="DeathNotice"/>).
///
/// <para>
/// It leaves lofted and falling (the launch model, forward * 300 plus 128 up)
/// and ignites three tenths of a second later: the flying model, Missile.Ignite,
/// a rocket trail, forward * 1500 along the angles it was fired at, and a brief
/// flash on its shooter's screen. From then on it steers, every frame, for the
/// nearest switched-on laser dot its shooter owns (<see cref="LaserDot"/>,
/// ComputeActualDotPosition's line-chasing), and flies straight when there is
/// none. It goes off on the first solid thing it touches (never a trigger),
/// and not against the sky.
/// </para>
///
/// <para>
/// Damage of the kind only missile defences deal shoots it down: it spirals
/// for a second and a half and then goes off, and its launcher may reload at
/// once. Nothing in this build deals that kind, as nothing in stock HL2MP does.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.RpgMissile, ScriptedEntityKind.Projectile)]
[Precache(PrecacheKind.Model, LaunchModel)]
[Precache(PrecacheKind.Model, FlyingModel)]
[Precache(PrecacheKind.Sound, IgniteSound)]
public sealed class Rocket : FlyingProjectile
{
    public Rocket(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                  IEngineWorldQuery world, IEngineEffects effects, IEnginePrecache precache,
                  IEngineHost host, IBlastSweep blast, IServerDecals decals, IEngineSound sound,
                  IScreenEffects screen, ITempEnts temps, IEntityRoster<LaserDot> dots)
        : base(physics, motion)
    {
        Damage = damage;
        World = world;
        Effects = effects;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
        Sound = sound;
        Screen = screen;
        Temps = temps;
        Dots = dots;
    }

    /// <summary>
    /// The managed blast traversal — the falloff, the line of sight and the
    /// blocking rule that used to run inside <c>CGameRules::RadiusDamage</c>.
    /// See <see cref="BlastSweep"/>.
    /// </summary>
    private readonly IBlastSweep Blast;

    /// <summary>the decal system, whose "Scorch" group the blast paints from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>tracing, and what is at a point. For the blast and the sky test.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>getting the blast's sprite and decal onto the client.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>the engine's random stream: the blast's sparks and the auger's wobble.</summary>
    private readonly IEngineHost Host;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>Missile.Ignite, and its stop.</summary>
    private readonly IEngineSound Sound;

    /// <summary>The ignition's flash on the shooter's screen: UTIL_ScreenFade.</summary>
    private readonly IScreenEffects Screen;

    /// <summary>The RPGShotDown client effect.</summary>
    private readonly ITempEnts Temps;

    /// <summary>Every laser dot there is: stock's g_LaserDotList.</summary>
    private readonly IEntityRoster<LaserDot> Dots;

    /// <summary>What a map may set on this entity. See <see cref="RocketVars"/>.</summary>
    [ScriptVars] public RocketVars Vars = new();

    internal const string LaunchModel = "models/weapons/w_missile_launch.mdl";
    internal const string FlyingModel = "models/weapons/w_missile.mdl";
    internal const string IgniteSound = "Missile.Ignite";
    internal const string ShotDownEffect = "RPGShotDown";

    /// <summary>RPG_SPEED.</summary>
    internal const float Speed = 1500f;

    /// <summary>RPG_HOMING_SPEED: how much of each frame's heading is the dot's.</summary>
    internal const float HomingSpeed = 0.125f;

    /// <summary>Spawn's SetNextThink( curtime + 0.3f ): the coast before ignition.</summary>
    internal const float IgniteDelay = 0.3f;

    /// <summary>CMissile::Spawn's m_iHealth = m_iMaxHealth = 100; AugerHealth is twenty below.</summary>
    internal const int MaxHealth = 100, AugerHealth = MaxHealth - 20;

    /// <summary>AUGER_YDEVIANCE, AUGER_XDEVIANCEUP and AUGER_XDEVIANCEDOWN.</summary>
    internal const float AugerYawDeviance = 20f, AugerPitchUp = 8f, AugerPitchDown = 1f;

    /// <summary>The only damage a missile takes: DMG_MISSILEDEFENSE (hl2_shareddefs.h, 1 &lt;&lt; 31) and DMG_AIRBOAT (1 &lt;&lt; 25).</summary>
    internal const DamageType MissileDefense = unchecked((DamageType)(1 << 31)), Airboat = (DamageType)(1 << 25);

    /// <summary>SURF_SKY (bspflags.h).</summary>
    private const int SurfSky = 0x0004;

    /// <summary>MAX_TRACE_LENGTH: SeekThink's starting best distance.</summary>
    private const float MaxTrace = 56756f;

    /// <summary>What the next think does.</summary>
    internal enum Stage
    {
        Ignite,
        Seek,
        Auger,
        None,
    }

    [SaveField] private Stage _stage = Stage.Ignite;
    [SaveField] private int _health = MaxHealth;
    [SaveField] private float _augerTime;
    [SaveField] private float _gracePeriodEndsAt;

    /// <summary>m_takedamage: DAMAGE_YES until it is killed or goes off.</summary>
    [SaveField] private bool _takesDamage = true;

    /// <summary>
    /// m_hOwner: the RPG that fired it still waits on it. Cleared when it is
    /// shot down or goes off, which is NotifyRocketDied's moment — the arsenal
    /// reads this to let the launcher reload.
    /// </summary>
    [SaveField] private bool _launcherWaits;

    private EntityHandle _trail = EntityHandle.None;

    public readonly Output OnExplode = new("OnExplode");

    /// <summary>The stage the next think runs. For the facts.</summary>
    internal Stage NextStage => _stage;

    /// <summary>Whether the RPG that fired it is still waiting on it (stock's m_hOwner != NULL).</summary>
    public bool LauncherWaits => _launcherWaits && !IsSpent;

    /// <summary>Its health, which only missile defences take.</summary>
    internal int MissileHealth => _health;

    /// <summary>
    /// Stock's FLYGRAVITY until it ignites and FLY after: the launch falls
    /// for its first three tenths of a second.
    /// </summary>
    protected override float GravityScale => _stage == Stage.Ignite ? 1f : 0f;

    protected override string ProjectileModel => LaunchModel;

    /// <summary>CMissile::Spawn's UTIL_SetSize, eight units across, until IgniteThink takes the box away.</summary>
    protected override Vector HullExtent => _stage == Stage.Ignite ? new Vector(4f, 4f, 4f) : Vector.Zero;

    /// <summary>CMissile::Spawn: the launch model, a box eight units across, health, and the ignition due.</summary>
    public override void OnSpawn()
    {
        _stage = Stage.Ignite;
        base.OnSpawn();

        _health = MaxHealth;
        _gracePeriodEndsAt = 0f;
        SetNextThink(IgniteDelay);
    }

    /// <summary>
    /// CMissile::Create and the RPG's PrimaryAttack after it: who fired it,
    /// lofted along the aim but still facing it, and, when the first 128 units
    /// ahead of the eye were clear, three tenths of a second of passing through
    /// players (SetGracePeriod).
    /// </summary>
    public void FiredFromLauncher(EntityHandle shooter, Vector angles, bool gracePeriod)
    {
        Vector forward = Angles.Forward(angles);
        Release(shooter, LaunchVelocity(forward), angles);
        _launcherWaits = true;

        if (gracePeriod)
            SetGracePeriod(IgniteDelay);
    }

    /// <summary>CMissile::Create's velocity: forward * 300 + (0, 0, 128).</summary>
    internal static Vector LaunchVelocity(Vector forward) => forward * 300f + new Vector(0f, 0f, 128f);

    /// <summary>SetGracePeriod: not solid, and through players, until it ends.</summary>
    private void SetGracePeriod(float seconds)
    {
        _gracePeriodEndsAt = GameTime + seconds;
        SetNotSolid(true);
        Motion.PassCharacters(Handle, true);
    }

    private void EndGracePeriod()
    {
        SetNotSolid(false);
        Motion.PassCharacters(Handle, false);
    }

    public override void OnThink()
    {
        switch (_stage)
        {
            case Stage.Ignite:
                IgniteThink();
                break;
            case Stage.Seek:
                SeekThink();
                break;
            case Stage.Auger:
                AugerThink();
                break;
        }
    }

    /// <summary>
    /// IgniteThink: flying flat, the flying model with no box, solid again,
    /// Missile.Ignite, RPG_SPEED along the angles it was fired at, the flash on
    /// its shooter's screen while its launcher still waits on it, and the trail.
    /// </summary>
    private void IgniteThink()
    {
        _stage = Stage.Seek;

        SetModel(FlyingModel);
        Entities.SetBounds(Handle, Vector.Zero, Vector.Zero);
        EndGracePeriod();

        Sound.EntityEmitSound(Handle, IgniteSound);

        Physics.SetGravity(Handle, 0f);
        Steer(Angles.Forward(Entities.GetAngles(Handle)) * Speed, 0f);

        SetNextThink(0f);

        if (_launcherWaits && Shooter != EntityHandle.None)
            Screen.Fade(Shooter, 255, 225, 205, 64, 0.1f, 0f, FadeFlags.In);

        CreateSmokeTrail();
    }

    /// <summary>
    /// CreateSmokeTrail: an env_rockettrail following it at its "0"
    /// attachment, with stock's numbers, alive for 999 seconds.
    /// </summary>
    private void CreateSmokeTrail()
    {
        if (_trail != EntityHandle.None)
            return;

        EntityHandle trail = Entities.Create(EntityNames.EnvRockettrail);
        if (trail == EntityHandle.None || !Entities.IsValid(trail))
            return;

        _trail = trail;
        Entities.SetOrigin(trail, Origin);
        Entities.Spawn(trail);

        int attachment = Math.Max(Entities.LookupAttachmentAny(Handle, "0"), 0);

        if (FindByHandle<EnvRocketTrail>(trail) is { } rocketTrail)
        {
            ConfigureTrail(rocketTrail.State, attachment);
            rocketTrail.SetLifetime(TrailLifetime);
        }

        Entities.SetParent(trail, Handle, attachment);
        Physics.SetNotSolid(trail, true);
    }

    /// <summary>RocketTrail::SetLifetime( 999 ) at the creation, and ( 0.1f ) when it goes off.</summary>
    internal const float TrailLifetime = 999f, TrailLifetimeAfter = 0.1f;

    /// <summary>CreateSmokeTrail's numbers, and FollowEntity's attachment.</summary>
    internal static void ConfigureTrail(RocketTrailNetState state, int attachment)
    {
        state.Opacity = 0.2f;
        state.SpawnRate = 100f;
        state.ParticleLifetime = 0.5f;
        state.StartColor = new Vector(0.65f, 0.65f, 0.65f);
        state.EndColor = new Vector(0f, 0f, 0f);
        state.StartSize = 8f;
        state.EndSize = 32f;
        state.SpawnRadius = 4f;
        state.MinSpeed = 2f;
        state.MaxSpeed = 16f;
        state.Attachment = attachment;
    }

    /// <summary>
    /// SeekThink: the grace period's end, the nearest switched-on dot its
    /// shooter owns, and a heading an eighth of the way from where it is
    /// going to where the dot says. Brought to a halt, it goes off.
    /// </summary>
    private void SeekThink()
    {
        if (_gracePeriodEndsAt != 0f && _gracePeriodEndsAt < GameTime)
        {
            EndGracePeriod();
            _gracePeriodEndsAt = 0f;
        }

        Vector origin = Origin;
        LaserDot? best = null;
        float bestDistance = MaxTrace;

        for (int i = 0; i < Dots.Count; i++)
        {
            var dot = Dots[i];
            if (!dot.IsOn || dot.OwnerEntity != Shooter)
                continue;

            float distance = (origin - dot.Position).Length;
            if (distance < bestDistance)
            {
                best = dot;
                bestDistance = distance;
            }
        }

        SetNextThink(0f);
        if (best is null)
            return;

        Vector shootFrom = best.OwnerEntity != EntityHandle.None
            ? Entities.GetWorldSpaceCenter(best.OwnerEntity)
            : best.ChasePosition;
        Vector target = ActualDotPosition(origin, shootFrom, best.ChasePosition,
                                          best.TargetEntity != EntityHandle.None);

        Vector current = Motion.VelocityOf(Handle);
        Vector heading = Homing(origin, current, target, HomingSpeed);
        Vector velocity = heading * current.Length;

        Entities.SetAngles(Handle, Angles.FromVector(heading));
        Steer(velocity, 0f);

        // "Strange circumstances have brought this missile to halt. Just blow it up."
        if (velocity.X == 0f && velocity.Y == 0f && velocity.Z == 0f)
            Spend(EntityHandle.None);
    }

    /// <summary>
    /// ComputeActualDotPosition: a dot on something that can be hurt is chased
    /// directly; otherwise, while the missile is nearer its shooter than the
    /// dot is, or within 512 units of the dot, it chases the point on the
    /// shooter-to-dot line nearest it, 256 units further along — it rides the
    /// beam — and the dot itself beyond that.
    /// </summary>
    internal static Vector ActualDotPosition(Vector missile, Vector shootFrom, Vector chase, bool hasTarget)
    {
        if (hasTarget)
            return chase;

        Vector laser = chase - shootFrom;
        float laserLength = laser.Length;
        Vector laserDir = laser.Normalized;

        float missileLength = (missile - shootFrom).Length;
        float targetLength = (missile - chase).Length;

        if (missileLength < laserLength || targetLength <= 512f)
            return PointOnLineNearestPoint(shootFrom, chase, missile) + laserDir * 256f;

        return chase;
    }

    /// <summary>UTIL_PointOnLineNearestPoint, unclamped, as the RPG calls it.</summary>
    internal static Vector PointOnLineNearestPoint(Vector start, Vector end, Vector point)
    {
        Vector endToStart = end - start;
        Vector orgToStart = point - start;
        float numerator = endToStart.Dot(orgToStart);
        float denominator = endToStart.Length * orgToStart.Length;
        float intersect = orgToStart.Length * (numerator / denominator);
        return start + endToStart.Normalized * intersect;
    }

    /// <summary>
    /// SeekThink's turn, as a unit heading (the speed is kept): homing *
    /// toward-the-target plus (1 - homing) * the current heading; a heading
    /// that cancels out becomes the target's direction. Stationary, it turns
    /// straight to the target.
    /// </summary>
    internal static Vector Homing(Vector origin, Vector velocity, Vector target, float homing)
    {
        Vector toTarget = target - origin;
        float distance = toTarget.Length;
        Vector targetDir = toTarget.Normalized;

        float speed = velocity.Length;
        Vector dir = velocity.Normalized;
        Vector heading = dir;

        if (speed != 0f)
        {
            heading = targetDir * homing + dir * (1f - homing);
            float length = heading.Length;
            heading = heading.Normalized;

            if (length < 1e-3f)
                heading = distance != 0f ? targetDir : dir;
        }
        else
        {
            heading = targetDir;
        }

        return heading;
    }

    /// <summary>
    /// AugerThink: past its auger time it goes off; until then it wobbles,
    /// up to twenty degrees of yaw and a little pitch each twentieth of a
    /// second, at a thousand units a second.
    /// </summary>
    private void AugerThink()
    {
        if (_augerTime < GameTime)
        {
            Spend(EntityHandle.None);
            return;
        }

        Vector angles = Entities.GetAngles(Handle);
        angles.Y += Host.RandomFloat(-AugerYawDeviance, AugerYawDeviance);
        angles.X += Host.RandomFloat(-AugerPitchDown, AugerPitchUp);
        Entities.SetAngles(Handle, angles);

        Steer(Angles.Forward(angles) * 1000f, 0f);
        SetNextThink(0.05f);
    }

    /// <summary>
    /// OnTakeDamage_Alive and Event_Killed: only missile defences hurt it; the
    /// blow that takes it to its auger health, or kills it, shoots it down.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if ((damageType & (MissileDefense | Airboat)) == 0 || IsSpent || !_takesDamage)
            return 0;

        bool alreadyDamaged = _health <= AugerHealth;
        int taken = (int)damage;
        _health -= taken;

        if (_health <= 0)
        {
            // Event_Killed: m_takedamage = DAMAGE_NO, and down it goes.
            _takesDamage = false;
            ShotDown();
        }
        else if (!alreadyDamaged && _health <= AugerHealth)
        {
            ShotDown();
        }

        return taken;
    }

    /// <summary>
    /// ShotDown: the RPGShotDown effect, a damaged trail, a second and a half
    /// of augering, and the launcher told at once so it may reload.
    /// </summary>
    private void ShotDown()
    {
        var data = new TeEffectData { Origin = Origin };
        Temps.DispatchEffect(TeFilters.Pas(data.Origin), 0f, ShotDownEffect, in data);

        if (_trail != EntityHandle.None && FindByHandle<EnvRocketTrail>(_trail) is { } trail)
            trail.State.Damaged = true;

        _stage = Stage.Auger;
        _augerTime = GameTime + 1.5f;
        SetNextThink(0f);

        _launcherWaits = false;
    }

    /// <summary>Touched: MissileTouch's Explode. The base refuses triggers and the shooter.</summary>
    protected override void Impact(EntityHandle hit) => Explode();

    /// <summary>
    /// Explode: the blast where it is (not against the sky), the trail left to
    /// die in a tenth of a second, the launcher told, and Missile.Ignite stopped.
    /// </summary>
    private void Explode()
    {
        _stage = Stage.None;
        _takesDamage = false;
        StopThinking();
        OnExplode.Fire();

        Vector origin = Origin;
        Vector forward = Angles.Forward(Entities.GetAngles(Handle));
        SurfaceTraceResult tr = World.TraceLineSurface(origin, origin + forward * 16f, TraceMask.Shot, Handle);

        SetNotSolid(true);

        if (tr.Fraction == 1f || (tr.SurfaceFlags & SurfSky) == 0)
            DoExplosion(origin);

        if (_trail != EntityHandle.None)
        {
            if (FindByHandle<EnvRocketTrail>(_trail) is { } trail)
                trail.SetLifetime(TrailLifetimeAfter);
            _trail = EntityHandle.None;
        }

        _launcherWaits = false;
        Sound.EntityStopSound(Handle, IgniteSound);
    }

    /// <summary>
    /// DoExplosion: ExplosionCreate with the missile's damage and twice that
    /// for the radius, no sparks, no dynamic light and no smoke, from its
    /// shooter, inflicted by the missile.
    /// </summary>
    private void DoExplosion(Vector origin)
    {
        // ignored: Handle -- an entity that explodes where it stands must say so,
        // or its own hull stops the very first trace of the traversal and every
        // victim in the radius is scored at distance zero. See Explosions.Fire.
        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        origin, Vars.Magnitude, Vars.Radius, Attribution,
                        ignored: Handle,
                        flags: EnvExplosion.SpawnFlag.NoSparks | EnvExplosion.SpawnFlag.NoDynamicLights
                             | EnvExplosion.SpawnFlag.NoSmoke,
                        inflictor: Handle);
    }

    /// <summary>The trail goes with it if it never got the chance to fade.</summary>
    public override void OnRemoved()
    {
        if (_trail != EntityHandle.None && Entities.IsValid(_trail))
            Entities.Remove(_trail);
        _trail = EntityHandle.None;
    }
}

/// <summary>
/// The map keys <see cref="Rocket"/> declares.
/// </summary>
[ScriptVars]
public partial class RocketVars
{
    /// <summary>
    /// The missile's damage: the RPG's SetDamage( GetHL2MPWpnData().m_iPlayerDamage ),
    /// which is weapon_rpg.txt's "damage", 150.
    /// </summary>
    public float Magnitude { get; set; } = 150f;

    /// <summary>
    /// DoExplosion's radius, GetDamage() * 2: 300 for the RPG's 150.
    /// </summary>
    public float Radius { get; set; } = 300f;
}
