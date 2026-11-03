//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// <c>obj_sentrygun</c> at level 1 (tf_obj_sentrygun.cpp): the turret that
/// hunts the nearest visible enemy within <c>SENTRY_MAX_RANGE</c>, turns onto
/// it, and shells it — the whole fight at the stock level 1 numbers.
///
/// <para>
/// <b>The fight.</b> The sentry thinks every <c>SENTRY_THINK_DELAY</c>, and
/// every beat re-finds: the nearest live enemy of the other team within
/// range and clear of line of sight — the held one revalidated too, and a
/// quarter-nearer rival takes its place. A changed find sets the first shell
/// a beat off; nothing valid leaves the turret sweeping between its deployed
/// bounds. While attacking it turns (the yaw ramping three base rates a
/// think toward thirty of them, the pitch at the fast fixed one) and, once
/// the aim has settled inside the fire tolerance and
/// the hopper's beat has run, fires: one shell through the core's damage
/// path, the builder as its attacker, with the shot stopped by whatever the
/// bullet trace stops on.
/// </para>
///
/// <para>
/// <b>The levels.</b> The wrench raises it (the base's flow): level 2 doubles
/// the hopper's beat (0.1 s) and grows the magazine, level 3 stocks the
/// rocket pod — a rocket down a clear line every three seconds at the pod's
/// 100 damage — and the eye climbs to the level's offset. The sapper and the
/// disguise-aware targeting are deferred rows (<c>docs/plans/tf2/deferred.tsv</c>).
/// The shells leave the eye point because muzzle attachments ride with the
/// deferred model.
/// </para>
/// </summary>
[ScriptedEntity(TFSentryGun.MapClass, ScriptedEntityKind.Item)]
public sealed class TFSentryGun(ITFMapPlayers players, ITFObjectAnnouncer announcer, IEnginePrecache precache,
                                 IEngineEntityRender render, IEngineWorldQuery world, IEngineDamage damage,
                                 IUniformRandom random, ITFAmmoPurse purse, TFBlastApplier blast,
                                 DamageInflictors inflictors, TFSentryGunDamageConVar sentryDamage,
                                 TFPlayerCarriers? carriers = null)
    : TFBaseObject(players, announcer, precache, render, purse, carriers)
{
    public const string MapClass = "obj_sentrygun";

    /// <summary><c>SENTRY_THINK_DELAY</c>.</summary>
    public const float SentryThinkDelay = 0.05f;

    /// <summary><c>SENTRY_MAX_RANGE</c>: the hunting radius.</summary>
    public const float SentryMaxRange = 1100f;

    /// <summary><c>SENTRYGUN_MAX_SHELLS_1/2/3</c> (tf_shareddefs.h:1354-1356): the hopper grows once, at level 2.</summary>
    public const int MaxShellsLevel1 = 150, MaxShellsLevel2 = 200, MaxShellsLevel3 = 200;

    /// <summary><c>SENTRYGUN_MAX_ROCKETS</c> (tf_shareddefs.h:1357): the pod's magazine.</summary>
    public const int MaxRocketsStored = 20;

    /// <summary><c>SENTRYGUN_EYE_OFFSET_LEVEL_1/2/3</c> (tf_shareddefs.h:1351-1353).</summary>
    public const float EyeHeightLevel1 = 32f, EyeHeightLevel2 = 40f, EyeHeightLevel3 = 46f;

    /// <summary><c>GetDeployedBounds</c>: the turret may not turn further than this either side of where it stood.</summary>
    public const float DeployedYawBound = 50f;

    /// <summary>The aim must settle inside this angle before the shells run.</summary>
    public const float FireTolerance = 10f;

    /// <summary><c>SENTRY_BASETURN_RATE</c> and the pitch's rate (<c>iBaseTurnRate * 5</c>, tf_obj_sentrygun.cpp:1837).</summary>
    public const float YawTurnRate = 6f, PitchTurnRate = 30f;

    /// <summary>
    /// <c>MoveTurret</c>'s yaw ramp (tf_obj_sentrygun.cpp:1873-1894, :1944):
    /// tracking climbs three base rates a think while the cap is not yet
    /// reached — so a floor reset lands the last climb one step past, at
    /// 192 — and hunting climbs one to the same test against ten; an aim
    /// settled on both axes, or a stalled rate, floors at five.
    /// </summary>
    public const float YawTrackMax = YawTurnRate * 30f, YawSweepMax = YawTurnRate * 10f, YawFloorRate = YawTurnRate * 5f;

    /// <summary>Stock's beats: the first shell a find ago, the level 1 hopper's 0.2 and the twin guns' 0.1 (tf_obj_sentrygun.cpp:1288-1296).</summary>
    public const float FoundAttackDelay = 0.05f, ShellIntervalLevel1 = 0.2f, ShellIntervalLevel2 = 0.1f;

    /// <summary>Level 3's rocket cadence (tf_obj_sentrygun.cpp:1376).</summary>
    public const float RocketInterval = 3f;

    /// <summary><c>SENTRYGUN_ADD_SHELLS</c>/<c>ADD_ROCKETS</c> (tf_obj_sentrygun.cpp:51-52): the wrench's refill clips.</summary>
    public const int AddShells = 40, AddRockets = 8;

    /// <summary><c>tf_sentrygun_metal_per_shell</c> = 1 and <c>..._per_rocket</c> = 2 (tf_obj_sentrygun.cpp:139-140).</summary>
    public const int MetalPerShell = 1, MetalPerRocket = 2;

    /// <summary><c>GetDeployedView</c>'s stand-in: the pitch limits (stock's −50..50).</summary>
    public const float MinPitch = -50f, MaxPitch = 50f;

    /// <summary>
    /// The disabled arrival's barrel-down 30 (tf_obj_sentrygun.cpp:1728):
    /// the pitch is stock's, down-positive, as VectorAngles builds it
    /// (mathlib_base.cpp:1020).
    /// </summary>
    public const float DisabledPitch = 30f;

    private const string SentryThink = "SentryThink";
    private const int SphereCapacity = 32;

    private readonly IEngineWorldQuery _world = world;
    private readonly IEngineDamage _damage = damage;
    private readonly IUniformRandom _random = random;
    private readonly TFBlastApplier _blast = blast;
    private readonly DamageInflictors _inflictors = inflictors;
    private readonly TFSentryGunDamageConVar _sentryDamage = sentryDamage;
    private readonly EntityHandle[] _sphere = new EntityHandle[SphereCapacity];

    [SaveField] private int _shells = MaxShellsLevel1;
    [SaveField] private int _rockets;
    [SaveField] private bool _attacking;
    [SaveField] private EntityHandle _target;
    [SaveField] private float _nextAttackTime;
    [SaveField] private float _nextRocketTime;
    [SaveField] private float _yaw, _pitch, _goalYaw, _goalPitch;
    [SaveField] private float _rightBound, _leftBound;
    [SaveField] private float _turnRate;
    [SaveField] private bool _turningRight = true;
    [SaveField] private bool _disabledHold;

    // The pose parameters against the model worn — stock re-derives them
    // after every model change (tf_obj_sentrygun.cpp:2130-2131), so they are
    // not carried; the lazy cache re-derives on the model's name.
    private int _pitchParam = -1, _yawParam = -1;
    private string _poseModel = "";

    public override TFObjectType Type => TFObjectType.Sentrygun;

    /// <summary><c>m_iAmmoShells</c>.</summary>
    public int Shells => _shells;

    /// <summary><c>m_iAmmoRockets</c>.</summary>
    public int Rockets => _rockets;

    /// <summary><c>m_iMaxAmmoShells</c>: the level's hopper (tf_shareddefs.h:1354-1356).</summary>
    public int MaxShells => Level switch
    {
        1 => MaxShellsLevel1,
        2 => MaxShellsLevel2,
        _ => MaxShellsLevel3,
    };

    /// <summary><c>m_iMaxAmmoRockets</c>: the pod's magazine at level 3, empty below it.</summary>
    public int MaxRockets => Level >= 3 ? MaxRocketsStored : 0;

    /// <summary><c>m_bAttacking</c>.</summary>
    public bool IsAttacking => _attacking;

    /// <summary><c>GetEnemy</c>: who the turret is tracking.</summary>
    public EntityHandle Target => _target;

    /// <summary>The turret's aim, where the facts read the turn.</summary>
    public float Yaw => _yaw;

    public float Pitch => _pitch;

    /// <summary>
    /// <c>CTFSentryGun::OnGoActive</c>: state SEARCHING, loaded, and the
    /// turret booked on its fast think; bounds close around the way it stood.
    /// </summary>
    protected override void OnGoActive()
    {
        _shells = MaxShells;
        _attacking = false;
        _target = EntityHandle.None;
        Vector angles = Entities.GetAngles(Handle);
        _yaw = _goalYaw = Normalize(angles.Y);
        _pitch = _goalPitch = angles.X;

        // GetDeployedBounds (tf_obj_sentrygun.cpp:408-420): the two bounds
        // angle-modded around the build facing — the pair sorted so the
        // lower numeric one is m_iRightBound, where m_bTurningRight starts.
        float right = Normalize((int)angles.Y - DeployedYawBound);
        float left = Normalize((int)angles.Y + DeployedYawBound);
        _rightBound = MathF.Min(right, left);
        _leftBound = MathF.Max(right, left);

        SetContextThink(SentryThink, SentryThinkDelay);
    }

    protected override void OnDestroyed() => StopThinking(SentryThink);

    protected override void OnCarried() => StopThinking(SentryThink);

    /// <summary>
    /// <c>CObjectSentrygun::StartUpgrading</c>'s ammo arms
    /// (tf_obj_sentrygun.cpp:540-567): the level's hopper — full, unless the
    /// lift carried it — and at level 3 the pod's magazine stocked whole.
    /// </summary>
    public override void StartUpgrading()
    {
        base.StartUpgrading();

        if (!IsCarryDeploy)
        {
            _shells = MaxShells;
            if (Level >= 3)
                _rockets = MaxRocketsStored;
        }
    }

    /// <summary>
    /// <c>CObjectSentrygun::OnWrenchHit</c>'s ammo arms
    /// (tf_obj_sentrygun.cpp:643-702), after the base's repair and upgrade:
    /// while not mid-upgrade, the pocket's metal feeds the hopper — one metal
    /// the shell, <c>SENTRYGUN_ADD_SHELLS</c> a swing, clipped to the level's
    /// max — and what is left over at level 3 the pod, two metal the rocket,
    /// <c>SENTRYGUN_ADD_ROCKETS</c> a swing.
    /// </summary>
    protected override bool OnWrenchHit(EntityHandle player)
    {
        bool didWork = base.OnWrenchHit(player);

        if (IsUpgrading)
            return didWork;

        string metalName = TFAmmoNames.Name(TFAmmo.Metal);

        if (_shells < MaxShells)
        {
            int affordable = Purse.Pools(player).GetValueOrDefault(metalName) / MetalPerShell;
            int toAdd = Math.Min(AddShells, affordable);
            toAdd = Math.Min(MaxShells - _shells, toAdd);
            if (toAdd > 0)
            {
                Purse.TakeAmmo(player, toAdd * MetalPerShell, metalName);
                _shells += toAdd;
                didWork = true;
            }
        }

        if (Level >= 3 && _rockets < MaxRocketsStored)
        {
            int affordable = Purse.Pools(player).GetValueOrDefault(metalName) / MetalPerRocket;
            int toAdd = Math.Min(AddRockets, affordable);
            toAdd = Math.Min(MaxRocketsStored - _rockets, toAdd);
            if (toAdd > 0)
            {
                Purse.TakeAmmo(player, toAdd * MetalPerRocket, metalName);
                _rockets += toAdd;
                didWork = true;
            }
        }

        return didWork;
    }

    /// <summary>
    /// <c>CTFSentryGun::UpdateSentry</c>: re-find while hunting, track and
    /// fire while attacking, and stop when the object is not standing.
    /// </summary>
    public override void OnThink(string context)
    {
        if (context != SentryThink || IsDead || !IsBuilt)
            return;

        // OnEndDisabled (tf_obj_sentrygun.cpp:1784-1801): the sweep resumes —
        // the direction flipped, the pitch back to level.
        if (_disabledHold)
        {
            _disabledHold = false;
            _turningRight = !_turningRight;
            _goalPitch = 0f;
        }

        // Sapper, etc. (FindTarget's IsDisabled refusal, and Attack's reset to
        // SEARCHING): a disabled turret holds no enemy and fires nothing.
        if (IsDisabled)
        {
            _attacking = false;
            _target = EntityHandle.None;

            // OnStartDisabled (tf_obj_sentrygun.cpp:1770-1779): the goal
            // pinned where the turret stands, once.
            if (!_disabledHold)
            {
                _disabledHold = true;
                _goalYaw = _yaw;
                _goalPitch = _pitch;
            }

            // MoveTurret's shape while pinned: the pitch eases toward the
            // goal, and on arrival the disabled arm of SentryRotate sets the
            // barrel-down 30 (tf_obj_sentrygun.cpp:1725-1729). The disabled
            // sound has no seam here. The yaw, pinned, does not move.
            if (_pitch == _goalPitch)
                _goalPitch = DisabledPitch;

            _pitch = TurnPitch(_pitch, _goalPitch, PitchTurnRate * SentryThinkDelay);
            AimPose();
            SetContextThink(SentryThink, SentryThinkDelay);
            return;
        }

        // Attack re-finds every beat (tf_obj_sentrygun.cpp:1229): the held
        // enemy revalidates through the same visible loop (:1084) and a
        // quarter-nearer one can take it (:1042); nothing valid sends the
        // turret back to searching (:1231) and the sweep (:1694).
        if (!FindTarget())
        {
            _attacking = false;
            _target = EntityHandle.None;
            Sweep();
        }
        else
        {
            AimAndFire(_target);
        }

        SetContextThink(SentryThink, SentryThinkDelay);
    }

    /// <summary>
    /// <c>CTFSentryGun::IsValidEnemy</c>: live, of the other team, and still
    /// inside the range measured from the eye. (The sapper, the cloak and the
    /// disguise arms are deferred.)
    /// </summary>
    private bool IsEnemyValid(EntityHandle candidate)
    {
        if (candidate == EntityHandle.None || !Entities.IsPlayer(candidate))
            return false;

        if (!Players.IsAlive(candidate))
            return false;

        if (Players.Team(candidate) == Team)
            return false;

        Vector to = Entities.GetWorldSpaceCenter(candidate) - EyePosition;
        return to.LengthSquared <= SentryMaxRange * SentryMaxRange;
    }

    /// <summary>
    /// <c>CTFSentryGun::FindTarget</c>: the nearest valid enemy the turret
    /// can see, by squared centre-to-eye distance — the held one included,
    /// which is how an attack revalidates its line of sight (:1084). The
    /// players are scanned first; the objects loop runs only when no player
    /// stood (:998). A changed find resets the first shell's beat
    /// (FoundTarget, tf_obj_sentrygun.cpp:1190); true when something stands.
    /// </summary>
    private bool FindTarget()
    {
        Vector eye = EyePosition;
        int count = _world.EntitiesInSphere(eye, SentryMaxRange, _sphere);

        EntityHandle held = _attacking ? _target : EntityHandle.None;
        EntityHandle best = EntityHandle.None;
        float bestDistance = float.MaxValue;
        float heldDistance = float.MaxValue;

        for (int i = 0; i < count && i < SphereCapacity; i++)
        {
            EntityHandle candidate = _sphere[i];

            Vector to = Entities.GetWorldSpaceCenter(candidate) - eye;
            float distance = to.LengthSquared;

            // Stock skips a candidate past the best so far before it is
            // validated (:954) — so the held's distance is recorded only
            // when the held wins a scan step (:964-966), which is what
            // makes the hysteresis order-dependent.
            if (distance > bestDistance)
                continue;

            if (!IsEnemyValid(candidate))
                continue;

            if (!VisibleFrom(eye, candidate))
                continue;

            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;

                if (candidate == held)
                    heldDistance = distance;
            }
        }

        // The objects pass (tf_obj_sentrygun.cpp:998-1030): the other
        // team's standing buildings, a placing one and the sapper
        // attachments passed over (ValidTargetObject :1090-1109). Here the
        // held's distance is recorded before the skip (:1014-1017), so the
        // hysteresis over buildings is not order-dependent.
        if (best == EntityHandle.None)
        {
            for (int i = 0; i < count && i < SphereCapacity; i++)
            {
                EntityHandle candidate = _sphere[i];
                if (FindByHandle<TFBaseObject>(candidate) is not { } building
                    || building.Team == Team || building.IsPlacing
                    || building.Type == TFObjectType.AttachmentSapper)
                    continue;

                Vector to = Entities.GetWorldSpaceCenter(candidate) - eye;
                float distance = to.LengthSquared;

                if (candidate == held)
                    heldDistance = distance;

                if (distance > bestDistance)
                    continue;

                if (!VisibleFrom(eye, candidate))
                    continue;

                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }

        // The 25% hysteresis (tf_obj_sentrygun.cpp:1042): the held enemy
        // stands unless the newcomer is a quarter nearer. A held one that
        // failed its revalidation never records a distance, so any valid
        // find takes over.
        if (held != EntityHandle.None && heldDistance != float.MaxValue
            && best != held && bestDistance >= heldDistance * 0.75f)
        {
            best = held;
            bestDistance = heldDistance;
        }

        if (best == EntityHandle.None)
            return false;

        if (!_attacking || best != held)
            _nextAttackTime = GameTime + FoundAttackDelay;

        _target = best;
        _attacking = true;
        return true;
    }

    /// <summary>
    /// <c>SentryRotate</c> (tf_obj_sentrygun.cpp:1694-1765) without its
    /// sounds: with nothing to fire at, the turret swings toward a deployed
    /// bound and, arrived, turns round — glancing up or down a third of the
    /// time (its draws are the deferred random's).
    /// </summary>
    private void Sweep()
    {
        float bound = _turningRight ? _rightBound : _leftBound;

        // The hunt's ramp (tf_obj_sentrygun.cpp:1871-1883): climbs while
        // far — the cap tested before the add, so the last climb lands one
        // step past YawSweepMax — and eases back near the bound.
        if (ShortAngleDelta(_yaw, bound) > 30f)
        {
            if (_turnRate < YawSweepMax)
                _turnRate += YawTurnRate;
        }
        else if (_turnRate > YawFloorRate)
        {
            _turnRate -= YawTurnRate;
        }

        float step = _turnRate * SentryThinkDelay;
        _goalYaw = bound;
        float yaw = Turn(_yaw, bound, step);
        float pitch = TurnPitch(_pitch, _goalPitch, PitchTurnRate * SentryThinkDelay);

        // MoveTurret's answer is whether either axis moved (:1942-1945): a
        // sweep standing at its goal — or a stalled rate — floors the rate.
        bool moved = yaw != _yaw || pitch != _pitch;
        if (!moved || _turnRate <= 0f)
            _turnRate = YawFloorRate;

        _yaw = yaw;
        _pitch = pitch;
        AimPose();

        // SentryRotate turns round only once the turret stood still
        // (:1721), and a third of the time glances up or down — stock
        // truncates the draw (:1761).
        if (!moved)
        {
            _turningRight = !_turningRight;
            if (_random.RandomFloat(0f, 1f) < 0.3f)
                _goalPitch = (int)_random.RandomFloat(-10f, 10f);
        }
    }

    /// <summary>
    /// MoveTurret's pose writes (tf_obj_sentrygun.cpp:1851, :1931-1935): the
    /// pose mirrors the aim about the body — the pitch negated (stock's
    /// pitch is down-positive), the yaw measured off the build facing and
    /// negated. The indices re-derive when the model changes, as stock's
    /// SetModel re-looks them up (:2130-2131).
    /// </summary>
    private void AimPose()
    {
        string model = Render.GetModelName(Handle);
        if (model != _poseModel)
        {
            _poseModel = model;
            _pitchParam = Render.LookupPoseParameter(Handle, "aim_pitch");
            _yawParam = Render.LookupPoseParameter(Handle, "aim_yaw");
        }

        if (_pitchParam >= 0)
            Render.SetPoseParameter(Handle, _pitchParam, -_pitch);

        if (_yawParam >= 0)
            Render.SetPoseParameter(Handle, _yawParam, -(_yaw - Entities.GetAngles(Handle).Y));
    }

    /// <summary><c>FVisible( pPlayer, MASK_SHOT | CONTENTS_GRATE )</c> (tf_obj_sentrygun.cpp:1084): nothing but the target stops the line from the eye to the centre.</summary>
    private bool VisibleFrom(Vector eye, EntityHandle target)
    {
        TraceResult trace = _world.TraceLine(eye, Entities.GetWorldSpaceCenter(target), TraceMask.SentrySight, Handle);
        return !trace.Hit || trace.Entity == target;
    }

    /// <summary>
    /// <c>SetIdealPitchAndYaw</c> and <c>MoveTurret</c>, then
    /// <c>CTFSentryGun::Attack</c>'s moment: the goal is the angle onto the
    /// target's centre — the yaw only angle-modded, the pitch clamped to
    /// ±50 (tf_obj_sentrygun.cpp:1240-1257; the deployment bounds are the
    /// idle sweep's alone) — and the shells run only once the aim has
    /// settled and the beat has come.
    /// </summary>
    private void AimAndFire(EntityHandle target)
    {
        Vector to = Entities.GetWorldSpaceCenter(target) - EyePosition;
        _goalYaw = Normalize(MathF.Atan2(to.Y, to.X) * (180f / MathF.PI));
        float horizontal = MathF.Sqrt(to.X * to.X + to.Y * to.Y);
        _goalPitch = Math.Clamp(MathF.Atan2(-to.Z, horizontal) * (180f / MathF.PI), MinPitch, MaxPitch);

        // The tracking ramp (tf_obj_sentrygun.cpp:1885-1895): while the
        // target is far the rate climbs three base rates a think — the cap
        // tested before the add, so the last climb lands one step past
        // YawTrackMax.
        if (ShortAngleDelta(_yaw, _goalYaw) > 30f && _turnRate < YawTrackMax)
            _turnRate += YawTurnRate * 3f;

        float yaw = Turn(_yaw, _goalYaw, _turnRate * SentryThinkDelay);
        float pitch = TurnPitch(_pitch, _goalPitch, PitchTurnRate * SentryThinkDelay);

        // MoveTurret's reset (:1942-1945): an aim settled on both axes —
        // or a stalled rate — stands back at the floor.
        if ((yaw == _yaw && pitch == _pitch) || _turnRate <= 0f)
            _turnRate = YawFloorRate;

        _yaw = yaw;
        _pitch = pitch;
        AimPose();

        if (_nextAttackTime > GameTime)
            return;

        // Stock's moment (:1262): the two axes as one vector's length, the
        // raw difference of the angle-modded angles — across the 0/360 seam
        // it runs the long way round and refuses.
        float yawDelta = _goalYaw - _yaw;
        float pitchDelta = _goalPitch - _pitch;
        if (MathF.Sqrt(yawDelta * yawDelta + pitchDelta * pitchDelta) > FireTolerance)
            return;

        Fire(target);
    }

    /// <summary>
    /// <c>CTFSentryGun::Fire</c>'s shell: the bullet trace runs from the eye
    /// to the target's centre, and the core's damage path carries it to
    /// whatever it reaches — with the builder as its attacker. The hopper
    /// gives up a shell whether the trace hit or not, and the beat is the
    /// level's (0.2 s at level 1, 0.1 s on the twin guns,
    /// tf_obj_sentrygun.cpp:1288-1296). A level 3 pod with rockets and its
    /// beat run fires one first (:1451-1457).
    /// </summary>
    private void Fire(EntityHandle target)
    {
        float shellInterval = Level == 1 ? ShellIntervalLevel1 : ShellIntervalLevel2;

        // Level 3 fires a rocket every three seconds its magazine backs
        // (tf_obj_sentrygun.cpp:1450-1457), before the shell.
        if (Level == 3 && _rockets > 0 && _nextRocketTime <= GameTime)
        {
            if (FireRocket(target))
                _nextRocketTime = GameTime + RocketInterval;
        }

        // The empty hopper (tf_obj_sentrygun.cpp:1631-1639): the beat runs
        // again, no shell and no damage.
        if (_shells <= 0)
        {
            _nextAttackTime = GameTime + shellInterval;
            return;
        }

        _shells--;
        _nextAttackTime = GameTime + shellInterval;

        TraceResult trace = _world.TraceLine(EyePosition, Entities.GetWorldSpaceCenter(target), TraceMask.Shot, Handle);
        EntityHandle victim = trace.Hit ? trace.Entity : target;

        // A wall stops the shell; a clear line or an enemy (or an unwitting
        // fellow in the way) takes it.
        if (trace.Hit && victim == EntityHandle.None)
            return;

        // The sentry is the hit's inflictor (stock's CTakeDamageInfo(this,
        // this, ...) at :1622-1630): the kill feed names obj_sentrygun, the
        // death rules credit the object, and the sentry-damager history
        // records the wound.
        using (_inflictors.Push(Handle, Origin))
            _damage.TakeDamage(victim, Builder, _sentryDamage.Value, DamageType.Bullet);
    }

    /// <summary>
    /// <c>CObjectSentrygun::FireRocket</c> (tf_obj_sentrygun.cpp:1313-1388)
    /// without its sounds and gesture: the line to the target's centre is
    /// traced first, and only a clear one (or one ending on something that is
    /// not the world) is fired down (:1347-1349); the pod's spawn is the eye
    /// point — the rocket attachments ride with the deferred model — and the
    /// magazine gives up a rocket.
    /// </summary>
    private bool FireRocket(EntityHandle target)
    {
        Vector from = EyePosition;
        Vector to = Entities.GetWorldSpaceCenter(target);

        // The MASK_SOLID wall check (:1334-1349): a blocked line to the
        // world fires nothing — the pod is not an artillery piece.
        TraceResult trace = _world.TraceLine(from, to, TraceMask.Shot, Handle);
        if (trace.Hit && trace.Entity == EntityHandle.None)
            return false;

        Vector aim = to - from;
        EntityHandle handle = Entities.Create(TFSentryRocket.MapClass);
        if (!Entities.IsValid(handle))
            return false;

        Entities.SetOrigin(handle, from);
        Entities.Spawn(handle);
        if (FindByHandle<TFSentryRocket>(handle) is not { } rocket)
            return false;

        rocket.FiredBy(Handle, Builder, aim.Normalized * TFSentryRocket.Speed);
        _rockets--;
        return true;
    }

    /// <summary>The eye: the base's origin at the level's eye height (tf_shareddefs.h:1351-1353).</summary>
    private Vector EyePosition => Origin + new Vector(0f, 0f, EyeHeightForLevel(Level));

    /// <summary>The level's <c>SENTRYGUN_EYE_OFFSET</c>.</summary>
    public static float EyeHeightForLevel(int level) => level switch
    {
        1 => EyeHeightLevel1,
        2 => EyeHeightLevel2,
        _ => EyeHeightLevel3,
    };

    /// <summary>Shortest signed rotation from <paramref name="from"/> to <paramref name="to"/>, in degrees.</summary>
    private static float ShortAngleSigned(float from, float to)
    {
        float delta = (to - from) % 360f;
        if (delta > 180f)
            delta -= 360f;
        else if (delta < -180f)
            delta += 360f;
        return delta;
    }

    private static float ShortAngleDelta(float from, float to) => MathF.Abs(ShortAngleSigned(from, to));

    /// <summary>Advance one angle toward another by no more than the step, the shortest way round.</summary>
    private static float Turn(float current, float goal, float step)
    {
        float delta = ShortAngleSigned(current, goal);
        if (MathF.Abs(delta) <= step)
            return Normalize(goal);
        return Normalize(current + MathF.Sign(delta) * step);
    }

    /// <summary>
    /// The pitch's turn: both ends live clamped inside ±50, so the delta
    /// never wraps — stock folds the pitch to [-180, 180] and clamps it,
    /// and the mod would be the identity there.
    /// </summary>
    private static float TurnPitch(float current, float goal, float step)
    {
        float delta = goal - current;
        if (MathF.Abs(delta) <= step)
            return goal;
        return current + MathF.Sign(delta) * step;
    }

    /// <summary>Stock's yaw space: the angle modded into [0, 360) (UTIL_AngleMod).</summary>
    private static float Normalize(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
            angle += 360f;
        return angle;
    }
}
