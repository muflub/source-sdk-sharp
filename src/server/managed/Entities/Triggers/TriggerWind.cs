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
/// Weather: a volume that blows the physics objects in it about, gusting rather
/// than pushing steadily.
///
/// The only volume here that does not act on what is inside it directly, and the
/// reason is the one the plan gives for movement. A wind is a force wanted on
/// every physics tick; a force asked for from C# ten times a second is a
/// different thing entirely — either a series of kicks or a strength that
/// depends on the tick rate. So each object that blows in gets a native motion
/// controller, and what this class owns is the decision of what force those
/// controllers carry.
///
/// Which is also why the mistake recorded against point_push cannot be made
/// here: nothing is scaled by a frame time or a think interval, because nothing
/// is applied per think at all.
/// </summary>
[ScriptedEntity(EntityNames.TriggerWind, ScriptedEntityKind.Brush)]
public sealed class TriggerWind : TriggerVolume
{
    public TriggerWind(IGameClock clock, IEngineHost host, IEngineNetworking net,
                       IEngineMotionControllers motion)
        : base(clock)
    {
        Host = host;
        Motion = motion;
        Net = net;
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>the solver's motion controllers, one per toucher being blown.</summary>
    private readonly IEngineMotionControllers Motion;
    /// <summary>What a map may set on this entity. See <see cref="TriggerWindVars"/>.</summary>
    [ScriptVars] public TriggerWindVars TriggerWindVars = new();

    /// <summary>Units of speed, or degrees of yaw, the wind may change per step.</summary>
    private const float MaxChange = 5f;

    /// <summary>How often the wind is reconsidered. Stock's interval, kept.</summary>
    private const float Interval = 0.1f;

    private float _yawBase;
    private float _yaw;
    private float _yawTarget;
    private float _speed;
    private float _speedTarget;

    /// <summary>Whether the next step picks a new gust rather than walking towards one.</summary>
    private bool _changing = true;

    /// <summary>Zeroes are already on every controller, so they can be left alone.</summary>
    private bool _becalmed;

    private readonly Dictionary<EntityHandle, MotionController> _blown = new();
    private readonly List<EntityHandle> _gone = new();

    // Nothing a wind acts on is a player.
    public override bool TouchedByAnything => true;

    /// <summary>Exposed for the tests, which have no engine to think for them.</summary>
    internal float CurrentSpeed => _speed;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // The mapper points the brush the way the wind blows.
        _yawBase = SpawnAngles.Y;
        _yaw = _yawBase;
        _yawTarget = _yawBase;
        _speed = TriggerWindVars.SpeedBase;
        _speedTarget = TriggerWindVars.SpeedBase;

        // Every entrant needs a controller of its own, and a refire delay would
        // quietly skip the second of two crates arriving together.
        Vars.Wait = 0f;

        SetNextThink(Interval);
    }

    /// <summary>
    /// Physics objects, and never a player.
    ///
    /// Stock reaches the same answer in two steps — its vphysics trigger admits
    /// physics movers and players, and the wind then throws the players back out
    /// — because a player in a gale is moved by the movement code rather than by
    /// the solver, and a force on their shadow object does nothing whatsoever.
    ///
    /// The kind test is here rather than in the base because the base asks about
    /// clients and about "everything else", and for a wind the answer to the
    /// first is always no while the flag that matters is the one for props.
    /// </summary>
    protected override TriggerSpawnflags KindFlags
        => TriggerSpawnflags.PhysicsObjects | TriggerSpawnflags.EverythingElse;

    protected override bool Passes(EntityHandle toucher)
    {
        var state = Net.EntityGetState(toucher, out MoveType moveType);

        if ((state & EntityState.Player) != 0 || moveType != MoveType.VPhysics)
            return false;

        return KindWanted && PassesFilter(toucher);
    }

    protected override void Fire(EntityHandle toucher)
    {
        if (_blown.ContainsKey(toucher))
            return;

        var controller = MotionController.Attach(Motion, toucher, MotionControllerKind.ConstantForce);
        if (controller is null)
            return;

        controller.SetActive(true);
        _blown[toucher] = controller;

        Blow(toucher, controller, Disabled ? 0f : _speed);
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

    /// <summary>Retunes the baseline and starts a new gust towards it straight away.</summary>
    [ScriptedEvent("SetSpeed")]
    private void OnSpeedSet(float speed)
    {
        TriggerWindVars.SpeedBase = (int)speed;
        _changing = true;
    }

    public override void OnThink()
    {
        float hold = Advance();
        float speed = Disabled ? 0f : _speed;

        // A wind that has stopped is zeroed once and then left alone: the native
        // force setter wakes whatever it is attached to, so repeating it would
        // keep every object in a switched-off volume awake for the rest of the
        // map. Stock gets this for free by declining to supply a force at all.
        bool applying = speed != 0f || !_becalmed;
        _becalmed = speed == 0f;

        foreach (var (handle, controller) in _blown)
        {
            if (!Entities.IsValid(handle))
            {
                _gone.Add(handle);
                continue;
            }

            if (applying)
                Blow(handle, controller, speed);
        }

        // Something can be broken or dissolved inside the volume, and what no
        // longer exists never reports leaving.
        foreach (var handle in _gone)
            Release(handle);

        _gone.Clear();

        SetNextThink(hold);
    }

    /// <summary>
    /// Moves the wind one step towards the gust it is heading for, and answers
    /// how long before it should be asked again.
    ///
    /// A gust is a speed and a yaw picked around the map's baselines; the wind
    /// then walks towards them a few units at a time, holds what it reached, and
    /// picks another. The walking is the whole character of it — a wind that
    /// snapped between gusts reads as something hitting the props rather than as
    /// weather.
    ///
    /// Internal because the tests have no engine to think for them.
    /// </summary>
    internal float Advance()
    {
        if (_changing)
        {
            _changing = false;
            _speedTarget = TriggerWindVars.SpeedBase + Host.RandomFloat(-TriggerWindVars.SpeedNoise, TriggerWindVars.SpeedNoise);
            _yawTarget = AngleMod(_yawBase + Host.RandomFloat(-TriggerWindVars.DirectionNoise, TriggerWindVars.DirectionNoise));

            return Interval;
        }

        bool arrived = true;

        if (MathF.Abs(_speedTarget - _speed) > MaxChange)
        {
            _speed += _speedTarget > _speed ? MaxChange : -MaxChange;
            arrived = false;
        }

        // The angle BETWEEN the two, not the difference of the numbers. Stock
        // subtracts them, so a gust whose yaw crosses zero is measured as 350-odd
        // degrees away from a place it has already reached, and the wind never
        // arrives: it holds that one gust for the rest of the map.
        if (MathF.Abs(AngleDiff(_yawTarget, _yaw)) > MaxChange)
        {
            _yaw = ApproachAngle(_yawTarget, _yaw, MaxChange);
            arrived = false;
        }

        if (!arrived)
            return Interval;

        _speed = _speedTarget;
        _yaw = _yawTarget;
        _changing = true;

        // Stock hands this to SetContextThink, which takes an ABSOLUTE time — so a
        // hold of three seconds asks to be reconsidered three seconds after the
        // map loaded, which is always in the past. Stock therefore never holds a
        // gust at all. Here it is a delay, and HoldTime does what the key says.
        return MathF.Max(Interval, TriggerWindVars.HoldTime + Host.RandomFloat(-TriggerWindVars.HoldNoise, TriggerWindVars.HoldNoise));
    }

    /// <summary>
    /// What one object feels this instant.
    ///
    /// Both noises are stock's and both are per object rather than per wind: the
    /// yaw wanders thirty degrees either side of the gust and the lift breathes
    /// between nothing and twice its own weight. Objects sharing a phase would
    /// cross the volume in formation, which reads as a conveyor rather than as air.
    /// </summary>
    private void Blow(EntityHandle handle, MotionController controller, float speed)
    {
        // Per object, from its index. C++ takes the physics object's ADDRESS,
        // which is an arbitrary number that differs between two machines running
        // the same map; an entity index does not.
        float wobble = 5f + Entities.GetIndex(handle) % 15;
        float phase = wobble * Clock.Now + wobble;

        Vector wind = Angles.Forward(new Vector(0f, _yaw + 30f * MathF.Cos(phase), 0f));

        // Written rather than added: the horizontal part is a unit vector and this
        // is stock's lift, which is always upwards and never quite steady.
        wind.Z = 1.1f + MathF.Sin(phase);

        // World space — a wind blows north whatever the crate in it is doing.
        controller.SetForce(wind * (3f * speed), Vector.Zero, localSpace: false);
    }

    /// <summary>Lets an object go, leaving it whatever the wind already gave it.</summary>
    private void Release(EntityHandle toucher)
    {
        if (_blown.Remove(toucher, out var controller))
            controller.Destroy();
    }

    /// <summary>An angle in [0, 360), which is the range the engine's own angles come in.</summary>
    internal static float AngleMod(float degrees)
    {
        float wrapped = degrees % 360f;
        return wrapped < 0f ? wrapped + 360f : wrapped;
    }

    /// <summary>
    /// How far it is from one angle to another, the short way round, in
    /// [-180, 180]. The sign is which way to turn.
    /// </summary>
    internal static float AngleDiff(float target, float current)
    {
        float delta = AngleMod(target - current);
        return delta > 180f ? delta - 360f : delta;
    }

    /// <summary>Steps an angle towards another by at most <paramref name="change"/> degrees.</summary>
    internal static float ApproachAngle(float target, float current, float change)
        => AngleMod(current + Math.Clamp(AngleDiff(target, current), -change, change));
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

    /// <summary>Seconds to hold a gust once it has been reached.</summary>
    public float HoldTime { get; set; }

    public float HoldNoise { get; set; }
}
