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
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

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
[Precache(PrecacheKind.Sound, "Building_MiniSentrygun.Fire")]
[Precache(PrecacheKind.Sound, "Building_Sentrygun.Fire")]
[Precache(PrecacheKind.Sound, "Building_Sentrygun.Fire2")]
[Precache(PrecacheKind.Sound, "Building_Sentrygun.Fire3")]
[Precache(PrecacheKind.Sound, "Building_Sentrygun.Empty")]
public sealed class TFSentryGun(ITFMapPlayers players, ITFObjectAnnouncer announcer, IEnginePrecache precache,
                                 IEngineEntityRender render, IEngineWorldQuery world, IEngineDamage damage,
                                 IUniformRandom random, ITFAmmoPurse purse, TFBlastApplier blast,
                                 DamageInflictors inflictors, TFSentryGunDamageConVar sentryDamage,
                                 TFSentryGunMiniDamageConVar miniSentryDamage, IEngineSound sound,
                                 TFPlayerCarriers carriers, TFConceptSpeech speech,
                                 ITFWeaponOwners owners, AttributeHooks hooks,
                                 TFDamageScopes scopes, ITFRevengeCrits crits,
                                 TFRevengeCrits revenge, ServerBodygroups bodygroups,
                                 IServerSkins skins)
    : TFBaseObject(players, announcer, precache, render, purse, carriers, speech, owners, hooks, scopes, crits, bodygroups, skins)
{
    private readonly TFRevengeCrits _revenge = revenge;

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

    /// <summary>The mini's beat multiplier (<c>tf_obj_sentrygun.cpp:1278-1281</c>): the interval shrinks to three quarters, shells a quarter quicker.</summary>
    public const float MiniFireRateMultiplier = 0.75f;

    /// <summary><c>MoveTurret</c>'s mini arm (tf_obj_sentrygun.cpp:1825-1830): the base turn rate, thirty-five hundredths quicker on both axes.</summary>
    public const float MiniTurnRateMultiplier = 1.35f;

    /// <summary>The report's script sounds (:1569-1612): the mini's own, and the plain turret's per level.</summary>
    public const string MiniFireSound = "Building_MiniSentrygun.Fire",
                        FireSoundLevel1 = "Building_Sentrygun.Fire",
                        FireSoundLevel2 = "Building_Sentrygun.Fire2",
                        FireSoundLevel3 = "Building_Sentrygun.Fire3",
                        EmptySoundName = "Building_Sentrygun.Empty";

    /// <summary><c>PITCH_HIGH</c> as <c>EmitSentrySound</c> sets it for a mini (tf_obj_sentrygun.cpp:2214-2219; the pitch scale of iesound.h).</summary>
    public const int PitchHigh = 120;

    /// <summary>The mini's turn-rate scale (<see cref="MiniTurnRateMultiplier"/>), nothing otherwise.</summary>
    private float TurnRateScale => IsMiniBuilding ? MiniTurnRateMultiplier : 1f;

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

    /// <summary><c>WRANGLER_DISABLE_TIME</c> (tf_obj_sentrygun.cpp:71): how long the shield lingers.</summary>
    public const float WranglerShieldFade = 3f;

    /// <summary><c>SHIELD_NORMAL</c> as the levels number (tf_obj.h:61-63).</summary>
    public const int ShieldNone = 0, ShieldNormal = 1;

    /// <summary><c>SHIELD_NORMAL_VALUE</c> (tf_obj.h:66): the damage a shielded hull takes.</summary>
    public const float ShieldDamageScale = 0.33f;

    /// <summary>The wrangled beat's half (<c>m_flFireRate *= 0.5f</c>, tf_obj_sentrygun.cpp:1275).</summary>
    public const float WrangledFireRateScale = 0.5f;

    /// <summary>How far past its dot a wrangled shell flies (<c>flDistToTarget + 100</c>, tf_obj_sentrygun.cpp:1539).</summary>
    public const float WranglerShellOvershoot = 100f;

    /// <summary><c>VECTOR_CONE_3DEGREES</c>' half-angle (<c>info.m_vecSpread</c>, tf_obj_sentrygun.cpp:1531-1538): the cone a wrangled shell flies in; an unwrangled shot keeps its straight line.</summary>
    public const float WranglerSpreadHalfAngle = 1.5f;

    /// <summary>The dot's minimum standoff (tf_obj_sentrygun.cpp:907): closer and the turret stands down.</summary>
    public const float WranglerMinAimDistance = 30f;

    /// <summary><c>GetPushMultiplier</c> (tf_obj_sentrygun.cpp:1662-1668): the shells' shove scale — eight for a mini, sixteen otherwise.</summary>
    public const float MiniPushMultiplier = 8f, PushMultiplier = 16f;

    /// <summary>The distance scale (<c>SENTRY_MAX_RANGE_SQRD</c>, tf_obj_sentrygun.h:35-36): past the turret's own range the shove halves (tf_player.cpp:10328-10332).</summary>
    public const float PushDistanceScale = 0.5f;

    private const string SentryThink = "SentryThink";
    private const int SphereCapacity = 32;

    private readonly IEngineWorldQuery _world = world;
    private readonly IEngineDamage _damage = damage;
    private readonly IUniformRandom _random = random;

    /// <summary>The wrangled cone's sample stream, as stock's <c>FireBullets</c> draws it per shot (baseentity_shared.cpp:1719); held by the turret, as the flame manager's is.</summary>
    private readonly SeededUniformStream _spreadStream = new();

    /// <summary>The seed for the next wrangled cone: the shot's index, so each shell draws its own offsets.</summary>
    private int _wrangledShots;
    private readonly TFBlastApplier _blast = blast;
    private readonly DamageInflictors _inflictors = inflictors;
    private readonly TFSentryGunDamageConVar _sentryDamage = sentryDamage;
    private readonly TFSentryGunMiniDamageConVar _miniSentryDamage = miniSentryDamage;
    private readonly IEngineSound _sound = sound;
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

    // The wrangle (m_bPlayerControlled and the shield, tf_obj_sentrygun.cpp
    // :121-122's networked pair): the builder's laser pointer holds the
    // turret on its dot. The aim and the one-shot flags are the commands'
    // own, re-set every command and consumed by a think.
    [SaveField] private bool _playerControlled;
    [SaveField] private int _shieldLevel;
    [SaveField] private float _shieldFadeTime;
    private bool _wrangled, _fireNextFrame, _rocketNextFrame;
    private Vector _laserAim;

    // The brief auto-aim (m_hAutoAimTarget and m_flAutoAimStartTime, the
    // networked pair at tf_obj_sentrygun.h:124): the player the holder's dot
    // lasered, remembered beside the stamp the find measures its window
    // against (:882, AUTOAIM < 0.2 s).
    [SaveField] private EntityHandle _autoAimTarget = EntityHandle.None;
    [SaveField] private float _autoAimStartTime;

    /// <summary>The window a lased player holds the target (<c>flAutoAimTime &lt; 0.2f</c>, tf_obj_sentrygun.cpp:882).</summary>
    public const float AutoAimWindow = 0.2f;

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

    /// <summary><c>m_bPlayerControlled</c>: the builder's pointer holds the turret.</summary>
    public bool IsPlayerControlled => _playerControlled;

    /// <summary><c>m_nShieldLevel</c>: <see cref="ShieldNone"/> or <see cref="ShieldNormal"/>.</summary>
    public int ShieldLevel => _shieldLevel;

    /// <summary>
    /// The wrangle's word, one command of it (<see cref="TFSentryWrangle"/>):
    /// where the dot stands, and the triggers held. The control engages at
    /// the next think, the shield with it.
    /// </summary>
    public void LaserAim(Vector aim) => (_laserAim, _wrangled) = (aim, true);

    /// <summary><c>FireNextFrame</c> (tf_obj_sentrygun.h:83).</summary>
    public void LaserFire() => _fireNextFrame = true;

    /// <summary><c>FireRocketNextFrame</c> (tf_obj_sentrygun.h:84).</summary>
    public void LaserFireRockets() => _rocketNextFrame = true;

    /// <summary>
    /// <c>ClearTarget</c> through <c>DestroyLaserDot</c>
    /// (tf_weapon_laser_pointer.cpp:265-284): the pointer left the hands.
    /// The shield fades from the last controlled think's arm
    /// (<c>WRANGLER_DISABLE_TIME</c>, tf_obj_sentrygun.cpp:790).
    /// </summary>
    public void LaserReleased()
    {
        _wrangled = false;
        _fireNextFrame = false;
        _rocketNextFrame = false;
    }

    /// <summary>
    /// <c>SetAutoAimTarget</c> (tf_obj_sentrygun.cpp:2263-2282, spoken from the
    /// pointer's dot trace at tf_weapon_laser_pointer.cpp:317-323): the lased
    /// player is remembered with the stamp, and the find prefers them over the
    /// dot while the window holds and the eye's line reaches (:881-904). No
    /// hit at all is no word (:2265-2266); the dummy refusal of :2268-2278 has
    /// no arm here — this build carries no target dummy entity.
    /// </summary>
    public void SetAutoAimTarget(EntityHandle player)
    {
        if (player == EntityHandle.None)
            return;

        _autoAimTarget = player;
        _autoAimStartTime = GameTime;
    }

    /// <summary>
    /// The wrangled line: the dot's stand, or the centre of the lased player
    /// while the brief auto-aim holds them as the held one (the aim read of
    /// the held's <c>GetEnemyAimPosition</c>, tf_obj_sentrygun.cpp:1531).
    /// </summary>
    private Vector LaserAimPoint =>
        _playerControlled && _target == EntityHandle.None
            ? _laserAim
            : Entities.GetWorldSpaceCenter(_target);

    /// <summary>
    /// <c>CObjectSentrygun::StartBuilding</c>'s tail (tf_obj_sentrygun.cpp:343-346):
    /// a mini's light group comes on with the construction.
    /// </summary>
    public override void StartBuilding(float buildRate = 1f)
    {
        base.StartBuilding(buildRate);

        if (IsMiniBuilding)
        {
            SetBodygroup(FindBodygroupByName("mini_sentry_light"), 1);
        }
    }

    /// <summary>
    /// <c>CTFSentryGun::OnGoActive</c>: state SEARCHING, loaded, and the
    /// turret booked on its fast think; bounds close around the way it stood.
    /// The mini's light rides the same guard stock puts after the model
    /// (:398-401).
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

        if (IsMiniBuilding)
        {
            SetBodygroup(FindBodygroupByName("mini_sentry_light"), 1);
        }
    }

    protected override void OnDestroyed() => StopThinking(SentryThink);

    protected override void OnCarried() => StopThinking(SentryThink);

    /// <summary>
    /// <c>CObjectSentrygun::Killed</c>'s tail (tf_obj_sentrygun.cpp:2055-2064):
    /// the owner's shotgun is told the death — the revenge loader's
    /// <c>SentryKilled</c> pays its <c>Kills * 2 + Assists</c> when the owned
    /// gun carries <c>sentry_killed_revenge</c>, stock's
    /// <c>Weapon_OwnsThisID(TF_WEAPON_SENTRY_REVENGE)</c> walk, here the
    /// loadout item behind the script.
    /// </summary>
    public override void OnKilled(EntityHandle attacker)
    {
        base.OnKilled(attacker);

        if (Builder == EntityHandle.None
            || Owners.ItemOf(Builder.EntryIndex, TFRevengeCrits.ShotgunScript) is not { } shotgun
            || Hooks.Int(0, "sentry_killed_revenge", shotgun) != 1)
            return;

        _revenge.Grant(Builder.EntryIndex, Kills * 2 + Assists);
    }

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
    /// <c>CObjectSentrygun::OnTakeDamage</c>'s shield
    /// (tf_obj_sentrygun.cpp:1970-1975): a hull under the player's control
    /// takes <c>SHIELD_NORMAL_VALUE</c> of the blow — sapped none of it, the
    /// shield and the sapper not stacking — before the base's refusals and
    /// the shell's subtract.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
        => base.OnDamaged(attacker, _shieldLevel > 0 && !HasSapper ? damage * ShieldDamageScale : damage, damageType);

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
            AimAndFire();
        }

        // The shield's fade (tf_obj_sentrygun.cpp:299-303): past its arm the
        // level drops and the aim levels with it.
        if (_shieldLevel > 0 && GameTime > _shieldFadeTime)
        {
            _shieldLevel = ShieldNone;
            _goalPitch = 0f;
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
    /// <c>CTFSentryGun::FindTarget</c>: the wrangled arm first — the builder's
    /// pointer deployed is the control (:862-920), the dot the target and the
    /// shield with it, no seek under the shield (:924) — then the nearest
    /// valid enemy the turret can see, by squared centre-to-eye distance —
    /// the held one included, which is how an attack revalidates its line of
    /// sight (:1084). The players are scanned first; the objects loop runs
    /// only when no player stood (:998). A changed find resets the first
    /// shell's beat (FoundTarget, tf_obj_sentrygun.cpp:1190); true when
    /// something stands.
    /// </summary>
    private bool FindTarget()
    {
        // m_bPlayerControlled resets each find and the wrangle re-arms it
        // (:788-792, :871): the control is the behaviour's last word, the
        // shield's arm the think's own while it holds (:790's re-arm).
        bool wasControlled = _playerControlled;
        _playerControlled = _wrangled;

        if (_playerControlled)
        {
            _shieldLevel = ShieldNormal;
            _shieldFadeTime = GameTime + WranglerShieldFade;

            // The brief auto-aim period (:881-904): while the window holds and
            // the eye's line reaches them, the lased player takes the dot's
            // place as the target; a blocked line or an expired window hands
            // the word back to the dot (:896-904's clears).
            EntityHandle lased = _autoAimTarget;
            Vector stand = _laserAim;
            if (lased != EntityHandle.None && GameTime - _autoAimStartTime < AutoAimWindow)
            {
                TraceResult reach = _world.TraceLine(EyePosition, Entities.GetWorldSpaceCenter(lased),
                                                     TraceMask.SentrySight, Handle);
                if (reach.Hit && reach.Entity == lased)
                    stand = Entities.GetOrigin(lased);
                else
                    lased = EntityHandle.None;
            }
            else
            {
                lased = EntityHandle.None;
            }

            _autoAimTarget = lased;

            // The dot nearer than its standoff is no target (:907-918): the
            // turret stands to the shield gate, controlled and sweeping. A
            // lased player is measured from their own origin, as the held one
            // of :907 is.
            if ((stand - Origin).Length > WranglerMinAimDistance)
            {
                // The engage's FoundTarget (:911): a beat before the first
                // shell, the same find keeping the turret.
                if (!_attacking)
                    _nextAttackTime = GameTime + FoundAttackDelay;

                _attacking = true;
                _target = lased;
                return true;
            }
        }
        else if (wasControlled)
        {
            // The find that drops the control arms the fade once more
            // (:788-790) — the release path's own arm stands beside it.
            _shieldFadeTime = GameTime + WranglerShieldFade;
        }

        // Don't auto track to targets while under the shield (:922-925): the
        // fade runs its three seconds after the control let go.
        if (_shieldLevel > 0)
            return false;

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
        // step past YawSweepMax — and eases back near the bound. Every rate
        // of it rides on MoveTurret's base, so the mini's thirty-five
        // hundredths lift the caps, the climbs and the floors alike (:1825-1830).
        float scale = TurnRateScale;
        if (ShortAngleDelta(_yaw, bound) > 30f)
        {
            if (_turnRate < YawSweepMax * scale)
                _turnRate += YawTurnRate * scale;
        }
        else if (_turnRate > YawFloorRate * scale)
        {
            _turnRate -= YawTurnRate * scale;
        }

        float step = _turnRate * SentryThinkDelay;
        _goalYaw = bound;
        float yaw = Turn(_yaw, bound, step);
        float pitch = TurnPitch(_pitch, _goalPitch, PitchTurnRate * scale * SentryThinkDelay);

        // MoveTurret's answer is whether either axis moved (:1942-1945): a
        // sweep standing at its goal — or a stalled rate — floors the rate.
        bool moved = yaw != _yaw || pitch != _pitch;
        if (!moved || _turnRate <= 0f)
            _turnRate = YawFloorRate * scale;

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
    /// target's centre — the wrangled turret's, the dot's position
    /// (<c>GetEnemyAimPosition</c> of the laser dot, its own origin) — the
    /// yaw only angle-modded, the pitch clamped to ±50
    /// (tf_obj_sentrygun.cpp:1240-1257; the deployment bounds are the idle
    /// sweep's alone). The shells run once the aim has settled and the beat
    /// has come — under the control, only on the held trigger's word
    /// (:1264), the beat half its own (:1275) — and the rockets ride the
    /// alt-fire's flag past the aim gate (:1303-1307).
    /// </summary>
    private void AimAndFire()
    {
        Vector to = LaserAimPoint - EyePosition;
        _goalYaw = Normalize(MathF.Atan2(to.Y, to.X) * (180f / MathF.PI));
        float horizontal = MathF.Sqrt(to.X * to.X + to.Y * to.Y);
        _goalPitch = Math.Clamp(MathF.Atan2(-to.Z, horizontal) * (180f / MathF.PI), MinPitch, MaxPitch);

        // The tracking ramp (tf_obj_sentrygun.cpp:1885-1895): while the
        // target is far the rate climbs three base rates a think — the cap
        // tested before the add, so the last climb lands one step past
        // YawTrackMax. The mini lifts the whole ramp with its base (:1825-1830).
        float scale = TurnRateScale;
        if (ShortAngleDelta(_yaw, _goalYaw) > 30f && _turnRate < YawTrackMax * scale)
            _turnRate += YawTurnRate * scale * 3f;

        float yaw = Turn(_yaw, _goalYaw, _turnRate * SentryThinkDelay);
        float pitch = TurnPitch(_pitch, _goalPitch, PitchTurnRate * scale * SentryThinkDelay);

        // MoveTurret's reset (:1942-1945): an aim settled on both axes —
        // or a stalled rate — stands back at the floor.
        if ((yaw == _yaw && pitch == _pitch) || _turnRate <= 0f)
            _turnRate = YawFloorRate * scale;

        _yaw = yaw;
        _pitch = pitch;
        AimPose();

        // Stock's moment (:1262): the two axes as one vector's length, the
        // raw difference of the angle-modded angles — across the 0/360 seam
        // it runs the long way round and refuses.
        float yawDelta = _goalYaw - _yaw;
        float pitchDelta = _goalPitch - _pitch;
        if (_nextAttackTime <= GameTime && MathF.Sqrt(yawDelta * yawDelta + pitchDelta * pitchDelta) <= FireTolerance)
        {
            // Under the control the shells wait for the trigger (:1264-1268);
            // the beat runs either way (:1270-1296), the control's at half.
            if (!_playerControlled || _fireNextFrame)
            {
                _fireNextFrame = false;
                Fire();
            }
            else
            {
                _nextAttackTime = GameTime + ShellInterval();
            }
        }

        // The rockets, past the aim gate as stock orders them (:1303-1307):
        // the flag alone, the pod's own cadence and magazine inside.
        if (_playerControlled && _rocketNextFrame)
        {
            _rocketNextFrame = false;
            if (Level == 3 && _rockets > 0 && _nextRocketTime <= GameTime && FireRocket(_laserAim))
                _nextRocketTime = GameTime + RocketInterval;
        }
    }

    /// <summary>The hopper's beat (<c>m_flFireRate</c>'s shape, tf_obj_sentrygun.cpp:1270-1296).</summary>
    private float ShellInterval()
    {
        float interval = Level == 1 ? ShellIntervalLevel1 : ShellIntervalLevel2;

        if (IsMiniBuilding)
            interval *= MiniFireRateMultiplier;

        if (_playerControlled)
            interval *= WrangledFireRateScale;

        return interval;
    }

    /// <summary>
    /// <c>CTFSentryGun::Fire</c>'s shell: the bullet trace runs from the eye
    /// to the target's centre, and the core's damage path carries it to
    /// whatever it reaches — with the builder as its attacker. The hopper
    /// gives up a shell whether the trace hit or not, and the beat is the
    /// level's (0.2 s at level 1, 0.1 s on the twin guns,
    /// tf_obj_sentrygun.cpp:1288-1296). A level 3 pod with rockets and its
    /// beat run fires one first — the wrangled turret's rockets are the
    /// trigger's (:1451-1454's <c>!m_bPlayerControlled</c>).
    /// </summary>
    private void Fire()
    {
        float shellInterval = ShellInterval();

        // Level 3 fires a rocket every three seconds its magazine backs
        // (tf_obj_sentrygun.cpp:1450-1457), before the shell.
        if (Level == 3 && _rockets > 0 && _nextRocketTime <= GameTime && !_playerControlled)
        {
            if (FireRocket(Entities.GetWorldSpaceCenter(_target)))
                _nextRocketTime = GameTime + RocketInterval;
        }

        // The empty hopper (tf_obj_sentrygun.cpp:1631-1639): the beat runs
        // again, no shell and no damage — and the empty click tells it.
        if (_shells <= 0)
        {
            _nextAttackTime = GameTime + shellInterval;
            EmitSentrySound(EmptySoundName);
            return;
        }

        _shells--;
        _nextAttackTime = GameTime + shellInterval;

        // The report (:1569-1612): the mini has its own sound, the plain
        // turret's climbs with its level — both after the muzzle flash of
        // the deferred model, as stock orders them.
        if (IsMiniBuilding)
            _sound.EntityEmitSound(Handle, MiniFireSound);
        else
            EmitSentrySound(Level switch
            {
                2 => FireSoundLevel2,
                3 => FireSoundLevel3,
                _ => FireSoundLevel1,
            });

        // The aim: the target's centre, or the wrangled dot's own position
        // (GetEnemyAimPosition of a non-player, tf_obj_sentrygun.cpp:240).
        Vector aim = LaserAimPoint;

        // The shot's direction, cone-spread under the Wrangler's control and
        // straight otherwise (info.m_vecSpread, tf_obj_sentrygun.cpp:1531-1538):
        // FireBullets applies the cone to the direction it traces (stock's
        // baseentity_shared.cpp:1719), so the whole flight rides it.
        Vector away = (aim - EyePosition).Normalized;
        if (_playerControlled)
        {
            (Vector right, Vector up) = Angles.RightUp(away);
            away = ShotSpread.Apply(new Angles.Basis(away, right, up),
                                    ShotSpread.SpreadOf(WranglerSpreadHalfAngle),
                                    _wrangledShots++, _spreadStream).Normalized;
        }

        // A wrangled shell flies a hundred units past its dot (info.m_flDistance's
        // flDistToTarget + 100, tf_obj_sentrygun.cpp:1539): the dot sits on the surface it
        // found, so a trace that stopped there would reach nothing.
        Vector end = _playerControlled ? aim + away * WranglerShellOvershoot : aim;
        TraceResult trace = _world.TraceLine(EyePosition, end, TraceMask.Shot, Handle);
        EntityHandle victim = trace.Hit ? trace.Entity : _target;

        // A wall stops the shell; a clear line or an enemy (or an unwitting
        // fellow in the way) takes it. A wrangled shell has no target to fall back on.
        if ((trace.Hit && victim == EntityHandle.None) || (!trace.Hit && _playerControlled))
            return;

        // The sentry is the hit's inflictor (stock's CTakeDamageInfo(this,
        // this, ...) at :1622-1630): the kill feed names obj_sentrygun, the
        // death rules credit the object, and the sentry-damager history
        // records the wound.
        // The mini's shells hit for the mini's number, the halved damage of
        // the smaller body (:1542-1546).
        float shellDamage = IsMiniBuilding ? _miniSentryDamage.Value : _sentryDamage.Value;
        using (_inflictors.Push(Handle, Origin))
        {
            // The shell carries no weapon (stock builds the FireBullets info
            // without one, tf_obj_sentrygun.cpp:1520-1554): the hit's launcher
            // is nobody's held item, so mult_dmg_vs_buildings and the sapper's
            // gate ask nothing of this blow. The wrangled shell's custom
            // (ModifyFireBulletsDamage, tf_obj_sentrygun.cpp:1651-1657) rides
            // along: the kill feed's sentry arm reads it for the pointer's name.
            if (Scopes is { } scopes)
            {
                using (scopes.PushNoLauncher(_playerControlled ? TFDamageCustom.PlayerSentry : TFDamageCustom.None,
                                             TFWeaponId.None, ""))
                    _damage.TakeDamage(victim, Builder, shellDamage, DamageType.Bullet);
            }
            else
            {
                _damage.TakeDamage(victim, Builder, shellDamage, DamageType.Bullet);
            }
        }

        // Sentries push a lot harder than a gun (:10317-10334): the shove of a
        // bullet from a base object scales by GetPushMultiplier — eight for a
        // mini, sixteen otherwise (:1662-1668) — and halves past the turret's
        // own range, so a wrangled shot at distance does not throw the whole
        // world back (:10328-10332). The arm reads a player's own damage hook
        // (:10232), so only players take it.
        if (Entities.IsPlayer(victim))
        {
            float multiplier = IsMiniBuilding ? MiniPushMultiplier : PushMultiplier;
            Vector between = Origin - Entities.GetOrigin(victim);
            if (between.LengthSquared > SentryMaxRange * SentryMaxRange)
                multiplier *= PushDistanceScale;

            (Vector mins, Vector maxs) = Entities.GetBounds(victim);
            Vector push = away * -TFRadiusDamage.DamageForce(maxs - mins, shellDamage, multiplier);
            Entities.ApplyVelocityImpulse(victim, push, clearGround: false);
        }
    }

    /// <summary>
    /// <c>EmitSentrySound</c> (:2206-2222): a mini's click rides a quarter-tone
    /// high (<c>SND_CHANGE_PITCH</c> at <c>PITCH_HIGH</c>, :2214-2219 — the
    /// pitch scale of iesound.h); otherwise the sound keeps its script's own.
    /// </summary>
    private void EmitSentrySound(string soundName)
    {
        if (IsMiniBuilding)
            _sound.EntityEmitSoundAs(Handle, soundName, volume: 1f, PitchHigh);
        else
            _sound.EntityEmitSound(Handle, soundName);
    }

    /// <summary>
    /// <c>CObjectSentrygun::FireRocket</c> (tf_obj_sentrygun.cpp:1313-1388)
    /// towards <paramref name="aim"/> — the target's centre, or the wrangled
    /// dot's — without its sounds and gesture: the line is traced first, and
    /// only a clear one (or one ending on something that is
    /// not the world) is fired down (:1347-1349); the pod's spawn is the eye
    /// point — the rocket attachments ride with the deferred model — and the
    /// magazine gives up a rocket.
    /// </summary>
    private bool FireRocket(Vector aimPoint)
    {
        Vector from = EyePosition;
        Vector to = aimPoint;

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
