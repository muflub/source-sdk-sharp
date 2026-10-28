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
/// CTriggerWind (triggers.cpp): a volume that blows the physics objects in it
/// about, gusting rather than pushing steadily.
///
/// Each object that blows in gets a native constant-force controller, which
/// the solver applies on every step. Stock's CPhysicsWind computes the force
/// in the step from the current time; here it is recomputed each tick, one
/// solver step at the server's rate. The gusts are WindThink's, ten a second,
/// with stock's integer arithmetic.
/// </summary>
[ScriptedEntity(EntityNames.TriggerWind, ScriptedEntityKind.Brush)]
public sealed class TriggerWind : VPhysicsTriggerVolume
{
    public TriggerWind(IGameClock clock, IEngineHost host, IEngineNetworking net,
                       IEngineMotionControllers motion)
        : base(clock, net)
    {
        Host = host;
        Motion = motion;
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>the solver's motion controllers, one per toucher being blown.</summary>
    private readonly IEngineMotionControllers Motion;
    /// <summary>What a map may set on this entity. See <see cref="TriggerWindVars"/>.</summary>
    [ScriptVars] public TriggerWindVars TriggerWindVars = new();

    /// <summary>MAX_WIND_CHANGE: how far a think walks the speed and the direction.</summary>
    private const int MaxChange = 5;

    /// <summary>WindThink's step.</summary>
    private const float Interval = 0.1f;

    private const string WindContext = "WindThink";
    private const string BlowContext = "WindBlow";

    // CTriggerWind's state, integers as stock's are: the speed and the yaw the
    // wind is at and walks towards, and whether the next think picks a gust.
    private int _dirBase;
    private int _dirCurrent;
    private int _dirTarget;
    private int _speedCurrent;
    private int _speedTarget;
    private bool _switch = true;

    // CPhysicsWind's two numbers, which every object's force reads.
    private int _windYaw;
    private float _windSpeed;

    /// <summary>Zeroes are already on every controller, so they can be left alone.</summary>
    private bool _becalmed;

    private readonly Dictionary<EntityHandle, MotionController> _blown = new();
    private readonly List<EntityHandle> _gone = new();

    /// <summary>Exposed for the tests, which have no engine to think for them.</summary>
    internal int CurrentSpeed => _speedCurrent;

    internal int CurrentYaw => _dirCurrent;

    /// <summary>The speed every object's force is computed from: 0 while disabled.</summary>
    internal float WindSpeed => _windSpeed;

    public override void OnSpawn()
    {
        _switch = true;
        _dirBase = (int)SpawnAngles.Y;

        base.OnSpawn();

        _speedCurrent = TriggerWindVars.SpeedBase;
        _dirCurrent = _dirBase;

        // Every entrant needs a controller of its own, and a refire delay would
        // quietly skip the second of two crates arriving together.
        Vars.Wait = 0f;

        SetContextThink(WindContext, 0f);
        SetContextThink(BlowContext, 0f);
    }

    protected override TriggerSpawnflags KindFlags
        => TriggerSpawnflags.PhysicsObjects | TriggerSpawnflags.EverythingElse;

    /// <summary>
    /// StartTouch's two tests: the vphysics trigger's filters, and never a
    /// player, whom the movement code moves rather than the solver.
    /// </summary>
    protected override bool Passes(EntityHandle toucher)
        => base.Passes(toucher) && (Net.EntityGetState(toucher, out _) & EntityState.Player) == 0;

    protected override void Fire(EntityHandle toucher)
    {
        if (_blown.ContainsKey(toucher))
            return;

        var controller = MotionController.Attach(Motion, toucher, MotionControllerKind.ConstantForce);
        if (controller is null)
            return;

        controller.SetActive(true);
        _blown[toucher] = controller;

        Blow(toucher, controller);
    }

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);
        Release(toucher);
    }

    public override void OnRemoved()
    {
        foreach (var controller in _blown.Values)
            controller.Destroy();

        _blown.Clear();
    }

    /// <summary>InputEnable: the next WindThink a tenth of a second on.</summary>
    protected override void OnDisabledChanged()
    {
        base.OnDisabledChanged();
        if (!Disabled)
            SetContextThink(WindContext, Interval);
    }

    [ScriptedEvent("SetSpeed")]
    private void OnSpeedSet(float speed)
    {
        // Set new speed and mark to switch
        TriggerWindVars.SpeedBase = (int)speed;
        _switch = true;
    }

    public override void OnThink(string context)
    {
        if (context == WindContext)
        {
            SetContextThink(WindContext, WindThink());
        }
        else if (context == BlowContext)
        {
            BlowAll();
            SetContextThink(BlowContext, 0f);
        }
    }

    /// <summary>
    /// CTriggerWind::WindThink, answering the delay to the next one: a gust
    /// picked, walked towards five a think, then held.
    ///
    /// Stock's arithmetic, kept. The yaw gap is the plain difference of the two
    /// numbers, so a gust across zero never arrives and is kept for good. The
    /// direction is not snapped on arrival. The hold is handed to
    /// SetContextThink as an ABSOLUTE time, so a gust is held only while the
    /// map is younger than HoldTime and the next think is otherwise the next
    /// tick.
    /// </summary>
    internal float WindThink()
    {
        // By default...
        float next = Interval;

        // Is it time to change the wind?
        if (_switch)
        {
            _switch = false;

            // Set new target direction and speed
            _speedTarget = TriggerWindVars.SpeedBase + Host.RandomInt(-TriggerWindVars.SpeedNoise, TriggerWindVars.SpeedNoise);
            _dirTarget = (int)AngleMod(_dirBase + Host.RandomInt(-TriggerWindVars.DirectionNoise, TriggerWindVars.DirectionNoise));
        }
        else
        {
            bool done = true;
            // either ramp up, or sleep till change
            if (Math.Abs(_speedTarget - _speedCurrent) > MaxChange)
            {
                _speedCurrent += _speedTarget > _speedCurrent ? MaxChange : -MaxChange;
                done = false;
            }

            if (Math.Abs(_dirTarget - _dirCurrent) > MaxChange)
            {
                _dirCurrent = (int)ApproachAngle(_dirTarget, _dirCurrent, MaxChange);
                done = false;
            }

            if (done)
            {
                _speedCurrent = _speedTarget;
                float until = TriggerWindVars.HoldTime + Host.RandomFloat(-TriggerWindVars.HoldNoise, TriggerWindVars.HoldNoise);
                next = MathF.Max(0f, until - Clock.Now);
                _switch = true;
            }
        }

        // store the wind data in the controller callback
        _windYaw = _dirCurrent;
        _windSpeed = Disabled ? 0f : _speedCurrent;
        return next;
    }

    /// <summary>
    /// CPhysicsWind::Simulate for every object, each tick, so the noise moves
    /// with the clock as stock's does in the step.
    /// </summary>
    private void BlowAll()
    {
        // A wind that has stopped is zeroed once and then left alone (stock's
        // SIM_NOTHING): the native force setter wakes what it is attached to.
        bool applying = _windSpeed != 0f || !_becalmed;
        _becalmed = _windSpeed == 0f;

        foreach (var (handle, controller) in _blown)
        {
            if (!Entities.IsValid(handle))
            {
                _gone.Add(handle);
                continue;
            }

            if (applying)
                Blow(handle, controller);
        }

        // Something can be broken or dissolved inside the volume, and what no
        // longer exists never reports leaving.
        foreach (var handle in _gone)
            Release(handle);

        _gone.Clear();
    }

    /// <summary>What one object feels this instant: stock's force.</summary>
    private void Blow(EntityHandle handle, MotionController controller)
    {
        // Get a cosine modulated noise between 5 and 20 that is object specific.
        // From the entity's index: C++ takes the physics object's ADDRESS,
        // which differs between two machines running the same map.
        int noiseMod = 5 + Entities.GetIndex(handle) % 15;
        float phase = noiseMod * Clock.Now + noiseMod;

        // Turn wind yaw direction into a vector and add noise
        Vector wind = Angles.Forward(new Vector(0f, _windYaw + 30f * MathF.Cos(phase), 0f));

        // Add lift with noise
        wind.Z = 1.1f + MathF.Sin(phase);

        // World space: a wind blows north whatever the crate in it is doing.
        controller.SetForce(wind * (3f * _windSpeed), Vector.Zero, localSpace: false);
    }

    /// <summary>Lets an object go, leaving it whatever the wind already gave it.</summary>
    private void Release(EntityHandle toucher)
    {
        if (_blown.Remove(toucher, out var controller))
            controller.Destroy();
    }

    /// <summary>UTIL_AngleMod: wrapped into one turn, in stock's 1/65536ths of it.</summary>
    internal static float AngleMod(float degrees)
        => (360f / 65536f) * ((int)(degrees * (65536f / 360f)) & 65535);

    /// <summary>UTIL_ApproachAngle: the target approached the short way by at most <paramref name="speed"/>.</summary>
    internal static float ApproachAngle(float target, float value, float speed)
    {
        target = AngleMod(target);
        value = AngleMod(value);

        float delta = target - value;

        // Speed is assumed to be positive
        if (speed < 0f)
            speed = -speed;

        if (delta < -180f)
            delta += 360f;
        else if (delta > 180f)
            delta -= 360f;

        if (delta > speed)
            value += speed;
        else if (delta < -speed)
            value -= speed;
        else
            value = target;

        return value;
    }
}

/// <summary>
/// The map keys <see cref="TriggerWind"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerWindVars
{
    /// <summary>
    /// How hard the wind blows before noise. Capitalised because CBaseEntity
    /// claims the lower-case "speed" for something else, which is why C++ reads
    /// this key by hand.
    /// </summary>
    [VarName("Speed")]
    public int SpeedBase { get; set; }

    /// <summary>How far either side of the baseline a gust may be.</summary>
    public int SpeedNoise { get; set; }

    /// <summary>How far either side of the mapper's yaw a gust may blow.</summary>
    public int DirectionNoise { get; set; }

    /// <summary>
    /// When to pick the next gust once one is reached: stock hands it to
    /// SetContextThink as an absolute time, in whole seconds.
    /// </summary>
    public int HoldTime { get; set; }

    public int HoldNoise { get; set; }
}
