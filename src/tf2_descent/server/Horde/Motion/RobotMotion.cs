using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Shared;

using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// The horde's motion (plan_descent_logic 3.7, HO3): carries out each lent
/// body's order, tick after tick, through the game's own movement, and is the
/// only thing that moves a body.
///
/// <para>
/// <b>A tick</b> (<see cref="Tick"/>, on the game thread, from the controller):
/// (1) <b>apply</b> the newest finished steps (<see cref="IMotionStepper"/>: at
/// most a tick old; none applied, the bodies hold): <b>accept</b> each with one
/// hull sweep of the body's scaled hull from where it was last put to where the
/// step left it, through the world, props, the other bodies and the heroes; a
/// clear move is taken, a blocked one is clipped at the contact (no slide: the
/// velocity into the face is removed, so the next step slides along it), and
/// the body's state is corrected there, so the next step starts from where the
/// body really is. A step that rose (a stair) and was blocked straight is tried
/// once more, up and then across, as the movement itself climbs; one that fell
/// (off a ledge), across and then down. (2) <b>face
/// and write</b>: each lent body turns towards its look (the look's point, the
/// named actor's eye asked of <see cref="IHordeActors"/> now, or along its
/// motion while it goes somewhere: never along a push or the pushaway, as a
/// player's view does not turn with them) at
/// <see cref="MotionSettings.TurnRate"/>; its place and facing go
/// to the entity only when they changed; its gait's sequence only when the
/// gait changed; its pose dials (the move blend in its own frame, the torso
/// towards the look) only when they moved. (3) <b>plan</b>: each lent body's
/// order becomes a command (<see cref="MotionSteering"/>; an expired order, a
/// hold, or a stunned robot wishes nothing), its pushes are added to its
/// velocity, and every body that is not standing still on the ground with
/// nothing to do is handed to the stepper.
/// </para>
///
/// <para>
/// <b>Reports</b>: <see cref="Blocked"/> (a wishing body clipped by the
/// accept, or whose step struck a wall and covered less than
/// <see cref="MotionSettings.BlockedProgress"/> of its wish: by what, whom,
/// since when) and <see cref="Landed"/> (its last landing: tick, air time, fall
/// speed, rise). A parked or dead body has no motion: its order is dropped.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IRobotMotion), typeof(IRobotImpulse), typeof(IRobotMotionReports)])]
public sealed class RobotMotion(IMotionWorld world) : IRobotMotion, IRobotImpulse, IRobotMotionReports
{
    /// <summary>Entity indices a body can have: the engine's entity list.</summary>
    public const int MaxEntities = 8192;

    /// <summary>The steepest face that is floor (stock's 0.7): a clip on floor is not a block.</summary>
    private const float FloorNormalZ = 0.7f;

    private readonly ThreadCpuClock _clock = new();
    private IMotionBodies? _bodies;
    private IHordeActors? _actors;
    private IMotionStepper? _stepper;
    private IMotionStepper? _own;
    private IMotionStepper? _chosen;
    private MotionSettings _settings = MotionSettings.Default;
    private float _tickSeconds;
    private long _now;

    // By body index; [0] unused.
    private int[] _serial = [];
    private BodyKinematics[] _state = [];
    private MoveIntent[] _move = [];
    private LookIntent[] _look = [];
    private long[] _expires = [];
    private bool[] _ordered = [];
    private int[] _corner = [];
    private float[] _facing = [];
    private AiVector[] _written = [];
    private AiVector[] _placedAt = [];
    private float[] _placedYaw = [];
    private AiVector[] _impulse = [];
    private BlockedReport[] _blocked = [];
    private int[] _clearRun = [];
    private LandedReport[] _landed = [];
    private ArrivedReport[] _arrived = [];
    private bool[] _arrivedOrder = [];
    private AiVector[] _arrivedCorner = [];
    private long[] _airSince = [];
    private float[] _leftZ = [];
    private float[] _peakZ = [];
    private MotionGait[] _gait = [];
    private MotionPose[] _pose = [];
    private float[] _speed = [];
    private float[] _scaleOf = [];
    private int[] _crowdRun = [];
    private bool[] _wished = [];
    private PushawayBody[] _mates = [];
    private List<AiVector>?[] _route = [];
    private int[] _routeAt = [];
    private int[] _bodyOfEntity = [];
    private EntityHandle[] _bodyHandle = [];
    private StepInput[] _inputs = [];
    private StepInput[] _engineInputs = [];
    private long[] _engineUntil = [];
    private int[] _pinnedRun = [];
    private int _submitted, _engineSubmitted;

    // Per tick, for the costs.
    private long _sweeps, _places, _gaits, _poses;

    public bool Begun => _bodies is not null;

    public MotionSettings Settings => _settings;

    public MotionCosts Costs { get; } = new();

    /// <summary>The bodies' audit (<c>tf2_descent_horde_motion_audit</c>): each tick, a place test of every stepped body where it was put.</summary>
    public MotionAudit Audit { get; } = new();

    public IMotionStepper? Stepper => _stepper;

    /// <summary>The stepper handed to <see cref="Use"/> that runs now, or null when the motion's own (given at <see cref="Begin"/>, or the engine's inline one) does.</summary>
    public IMotionStepper? Chosen => _chosen;

    /// <summary>A server tick in seconds, as the steps are run at.</summary>
    public float TickSeconds => _tickSeconds;

    /// <summary>The most bodies a tick hands a stepper: what a stepper is made for.</summary>
    public int Capacity => _serial.Length;

    /// <summary>
    /// Runs the steps on <paramref name="stepper"/> from the next tick (the horde's nav hands a
    /// worker over the level's voxels), or on the motion's own again with null. Steps the stepper
    /// left unfinished are not applied: the bodies hold a tick. The motion never disposes a
    /// stepper handed here: its maker does.
    /// </summary>
    public void Use(IMotionStepper? stepper)
    {
        var next = stepper ?? _own;
        if (ReferenceEquals(next, _stepper))
            return;
        _stepper = next;
        _chosen = stepper;
        _submitted = 0;
        _engineSubmitted = 0;
        StepperSwitches++;
    }

    /// <summary>Times <see cref="Use"/> changed the stepper.</summary>
    public long StepperSwitches { get; private set; }

    /// <summary>
    /// Times a body stepped on a handed stepper (the voxels') was found pinned and stepped on
    /// the engine's world for <see cref="EngineTicks"/> (<see cref="PinnedTicks"/>), and the
    /// body steps run that way.
    /// </summary>
    public long EngineFallbacks { get; private set; }

    public long EngineSteps { get; private set; }

    /// <summary>
    /// A body stepped off the game thread that wished to move and covered less than
    /// <see cref="PinnedShare"/> of its wish for this many ticks in a row, held by nothing the
    /// game's world names (no hero, robot or prop: the world, or nothing), is pinned: the voxels
    /// and the engine disagree where it stands (it stands on a prop the voxels drop it through,
    /// or in a voxel a wall fills that the engine leaves free). It steps on the engine's world
    /// for <see cref="EngineTicks"/>.
    /// </summary>
    public const int PinnedTicks = 8;

    public const float PinnedShare = 0.25f;

    public const int EngineTicks = 66;

    public long Ticks { get; private set; }

    public long OrdersTaken { get; private set; }

    public long OrdersDropped { get; private set; }

    public long Pushes { get; private set; }

    public long Clips { get; private set; }

    /// <summary>Ticks that applied no step though one was asked for (a stalled or refused stepper).</summary>
    public long Holds { get; private set; }

    /// <summary>
    /// Starts moving <paramref name="bodies"/> at <paramref name="ticksPerSecond"/>.
    /// <paramref name="actors"/> answers where an actor a look names has its eye;
    /// it is handed here, not to the constructor, because the session that
    /// answers it is itself given this motion.
    /// <paramref name="stepper"/> runs the steps; none runs them on the main
    /// thread over this motion's world (the engine's).
    /// </summary>
    public void Begin(IMotionBodies bodies, IHordeActors actors, MotionSettings settings, double ticksPerSecond,
                      IMotionStepper? stepper = null)
    {
        if (Begun)
            End();

        _settings = settings;
        _tickSeconds = (float)(1.0 / ticksPerSecond);
        int n = bodies.LastBody + 1;
        _serial = new int[n];
        _state = new BodyKinematics[n];
        _move = new MoveIntent[n];
        _look = new LookIntent[n];
        _expires = new long[n];
        _ordered = new bool[n];
        _corner = new int[n];
        _facing = new float[n];
        _written = new AiVector[n];
        _placedAt = new AiVector[n];
        _placedYaw = new float[n];
        _impulse = new AiVector[n];
        _blocked = new BlockedReport[n];
        _clearRun = new int[n];
        _landed = new LandedReport[n];
        _arrived = new ArrivedReport[n];
        _arrivedOrder = new bool[n];
        _arrivedCorner = new AiVector[n];
        _airSince = new long[n];
        _leftZ = new float[n];
        _peakZ = new float[n];
        _gait = new MotionGait[n];
        _pose = new MotionPose[n];
        _speed = new float[n];
        _scaleOf = new float[n];
        _crowdRun = new int[n];
        _wished = new bool[n];
        _mates = new PushawayBody[1];
        _route = new List<AiVector>?[n];
        _routeAt = new int[n];
        _inputs = new StepInput[n];
        _engineInputs = new StepInput[n];
        _engineUntil = new long[n];
        _pinnedRun = new int[n];
        _bodyOfEntity = new int[MaxEntities];
        _bodyHandle = new EntityHandle[n];
        for (int b = 1; b < n; b++)
        {
            var handle = bodies.Handle(b);
            _bodyHandle[b] = handle;
            int index = handle.EntryIndex;
            if (index is >= 0 and < MaxEntities)
                _bodyOfEntity[index] = b;
        }

        _own = stepper ?? new InlineMotionStepper(GameMovementStep.Create(world, _tickSeconds), n, _clock);
        _stepper = _own;
        _chosen = null;
        _bodies = bodies;
        _actors = actors;
        _submitted = 0;
    }

    /// <summary>The level is going: the stepper is stopped and every state dropped.</summary>
    public void End()
    {
        _own?.Dispose();
        _own = null;
        _stepper = null;
        _chosen = null;
        _bodies = null;
        _actors = null;
        _bodyOfEntity = [];
        _bodyHandle = [];
        Costs.Stop();
        Audit.Stop();
    }

    //-------------------------------------------------------------------------
    // The seam
    //-------------------------------------------------------------------------

    public void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick)
    {
        if (!Live(body, out var info))
        {
            OrdersDropped++;
            return;
        }

        Adopt(body, in info);
        _route[body] = null;
        SetOrder(body, in move, in look, expiresTick);
    }

    public void Hold(int body)
    {
        if (!Live(body, out var info))
        {
            OrdersDropped++;
            return;
        }

        Adopt(body, in info);
        _route[body] = null;
        SetOrder(body, MoveIntent.Hold, _ordered[body] ? _look[body] : LookIntent.AlongPath, long.MaxValue);
    }

    public bool Push(int body, AiVector velocityChange)
    {
        if (!Live(body, out var info))
            return false;

        Adopt(body, in info);
        _impulse[body] += velocityChange;
        Pushes++;
        return true;
    }

    /// <summary>
    /// The dev drive: walks <paramref name="body"/> along <paramref name="corners"/>,
    /// any number of them, four at a time as orders; a seam order replaces it.
    /// </summary>
    public bool Route(int body, IReadOnlyList<AiVector> corners)
    {
        if (!Live(body, out var info) || corners.Count == 0)
            return false;

        Adopt(body, in info);
        _route[body] = [.. corners];
        _routeAt[body] = 0;
        FeedRoute(body);
        return true;
    }

    /// <summary>A lent, living body as the motion reads it (false for a parked or dead one).</summary>
    public bool TryBody(int body, out MotionBody info) => Live(body, out info);

    /// <summary>The corners left on a body's dev route (0 for none).</summary>
    public int RouteLeft(int body) => _route[body] is { } r ? r.Count - _routeAt[body] - _corner[body] : 0;

    //-------------------------------------------------------------------------
    // Reports
    //-------------------------------------------------------------------------

    public bool TryView(int body, out RobotMotionView view)
    {
        if ((uint)body >= (uint)_serial.Length || _serial[body] == 0)
        {
            view = default;
            return false;
        }

        var s = _state[body];
        view = new RobotMotionView(_written[body], new AiVector(s.Velocity.X, s.Velocity.Y, s.Velocity.Z), s.OnGround, _facing[body],
                                   _ordered[body], _move[body], _look[body], _expires[body], _corner[body], _gait[body],
                                   _blocked[body], _landed[body], _airSince[body]);
        return true;
    }

    public BlockedReport Blocked(int body) => (uint)body < (uint)_blocked.Length ? _blocked[body] : default;

    public LandedReport Landed(int body) => (uint)body < (uint)_landed.Length ? _landed[body] : default;

    public ArrivedReport Arrived(int body) => (uint)body < (uint)_arrived.Length ? _arrived[body] : default;

    /// <summary>Arrivals reported, all bodies.</summary>
    public long Arrivals { get; private set; }

    //-------------------------------------------------------------------------
    // The tick
    //-------------------------------------------------------------------------

    /// <summary>One server tick of every lent body's motion. See the class remarks.</summary>
    public void Tick(long now)
    {
        if (_bodies is not { } bodies)
            return;

        long w0 = MotionCosts.Stamp();
        double c0 = _clock.Micros();
        double step0 = _stepper!.StepMicros;
        _now = now;
        Ticks++;
        _sweeps = _places = _gaits = _poses = 0;

        // (1) Apply the newest finished steps.
        int age = 2;
        if (_stepper.TryTake(now, out var outputs, out long stepTick))
        {
            age = (int)Math.Clamp(now - stepTick, 0, 1);
            for (int i = 0; i < outputs.Length; i++)
                Apply(bodies, in outputs[i], now);
        }
        else if (_submitted > 0)
        {
            Holds++;
        }

        // The pinned bodies' steps, run on the engine's world beside a handed stepper.
        if (_engineSubmitted > 0 && !ReferenceEquals(_own, _stepper) && _own!.TryTake(now, out var engine, out _))
        {
            for (int i = 0; i < engine.Length; i++)
                Apply(bodies, in engine[i], now);
        }

        // (2) Face and write, (3) plan.
        int n = 0, m = 0;
        for (int b = 1; b < _serial.Length; b++)
        {
            if (!bodies.Read(b, out var info))
            {
                if (_serial[b] != 0)
                    Forget(b);
                continue;
            }

            if (info.Serial != _serial[b])
                Start(b, in info, now);
            _speed[b] = info.Speed;
            _scaleOf[b] = info.Scale;

            Face(bodies, b, in info);
            if (!Plan(b, in info, now, out var input))
                continue;
            if (_chosen is not null && _engineUntil[b] > now)
                _engineInputs[m++] = input;
            else
                _inputs[n++] = input;
        }

        _submitted = n;
        if (n > 0)
            _stepper.Submit(now, _inputs.AsSpan(0, n));
        _engineSubmitted = m;
        if (m > 0)
        {
            _own!.Submit(now, _engineInputs.AsSpan(0, m));
            EngineSteps += m;
        }

        if (Audit.On)
            Audit.Check(this, bodies, world, now);

        if (Costs.On)
            Costs.Tick(ThreadCpuClock.Since(w0), _clock.Micros() - c0, _stepper.StepMicros - step0, age, _sweeps, _places, _gaits, _poses, n);
    }

    /// <summary>The last place a body was put (the accepted one).</summary>
    public AiVector Written(int body) => _written[body];

    /// <summary>The hull of a body at <paramref name="scale"/>.</summary>
    public static (Vector Mins, Vector Maxs) Hull(float scale) => (RobotHull.Mins * scale, RobotHull.Maxs * scale);

    private void Apply(IMotionBodies bodies, in StepOutput o, long now)
    {
        int b = o.Body;
        if (_serial[b] != o.Serial || !bodies.Read(b, out var info) || info.Serial != o.Serial)
            return;

        var from = _written[b];
        var next = o.State;
        var to = next.Origin;
        var start = HordeVectors.ToEngine(from);
        var (mins, maxs) = Hull(info.Scale);

        bool clipped = false;
        MotionTrace blocker = default;
        if (to.X != start.X || to.Y != start.Y || to.Z != start.Z)
        {
            _sweeps++;
            var t = world.Sweep(start, to, mins, maxs, info.Handle);
            if (t.StartSolid)
            {
                // Already overlapping something (put down there): a move is taken only to a place that is clear.
                _sweeps++;
                var there = world.Sweep(to, to, mins, maxs, info.Handle);
                if (there.StartSolid)
                {
                    clipped = true;
                    blocker = there;
                    next.Origin = start;
                    next.Velocity = Vector.Zero;
                }
            }
            else if (t.Hit && t.Fraction < 1f && !Climbed(start, to, mins, maxs, info.Handle) && !Descended(start, to, mins, maxs, info.Handle))
            {
                next.Origin = t.End;
                next.Velocity = ClipVelocity(next.Velocity, t.Normal);
                if (t.Normal.Z < FloorNormalZ)
                {
                    clipped = true;
                    blocker = t;
                }
            }
        }

        if (clipped)
            Clips++;

        bool wasGround = _state[b].OnGround;
        var prevVelocity = _state[b].Velocity;
        _state[b] = next;
        var at = HordeVectors.ToAi(next.Origin);
        _written[b] = at;

        // The blocked report.
        bool blocked = clipped;
        float moved = new AiVector(at.X - from.X, at.Y - from.Y, 0f).Length2D;
        bool slow = o.Expect > 0f && moved < _settings.BlockedProgress * o.Expect;
        // Held up in a crowd: robots pass through each other, so another robot never stops a body, and passing one
        // takes a few ticks of pushaway. A wishing body pushed among robots for CrowdTicks in a row reports the
        // nearest one it overlaps, from the run's first tick, whatever wall the push also pressed it into.
        _crowdRun[b] = o.Expect > 0f && o.Crowd != 0 ? _crowdRun[b] + 1 : 0;
        if (!clipped && _crowdRun[b] >= _settings.CrowdTicks)
        {
            Report(b, BlockerKind.Robot, o.Crowd, now);
            if (_crowdRun[b] == _settings.CrowdTicks)
                _blocked[b] = _blocked[b] with { Since = now - _crowdRun[b] + 1 };
        }
        else
        {
            if (!blocked && slow && o.Struck)
            {
                blocked = true;
                blocker = o.Blocker;
            }

            Report(b, blocked, in blocker, now);
        }

        // A body pinned under a handed stepper steps on the engine's world for a while (see PinnedTicks).
        if (_chosen is not null)
        {
            var kind = _blocked[b].Kind;
            bool pinned = o.Expect > 0f && moved < PinnedShare * o.Expect && kind is not (BlockerKind.Hero or BlockerKind.Robot or BlockerKind.Prop);
            _pinnedRun[b] = pinned ? _pinnedRun[b] + 1 : 0;
            if (_pinnedRun[b] >= PinnedTicks && _engineUntil[b] <= now)
            {
                _engineUntil[b] = now + EngineTicks;
                _pinnedRun[b] = 0;
                EngineFallbacks++;
            }
        }

        // The landed report.
        bool onGround = next.OnGround;
        if (wasGround && !onGround)
        {
            _airSince[b] = now;
            _leftZ[b] = from.Z;
            _peakZ[b] = at.Z;
        }
        else if (!onGround)
        {
            _peakZ[b] = MathF.Max(_peakZ[b], at.Z);
        }
        else if (!wasGround && _airSince[b] != 0)
        {
            _landed[b] = new LandedReport(now, _airSince[b], MathF.Max(0f, -prevVelocity.Z), MathF.Max(0f, _peakZ[b] - _leftZ[b]));
            _airSince[b] = 0;
        }
    }

    /// <summary>Whether a rising step blocked straight goes clear up and then across (a stair, as the movement climbs it).</summary>
    private bool Climbed(in Vector start, in Vector to, in Vector mins, in Vector maxs, EntityHandle ignore)
    {
        if (to.Z <= start.Z + 0.5f)
            return false;

        var up = new Vector(start.X, start.Y, to.Z);
        _sweeps += 2;
        return !world.Sweep(start, up, mins, maxs, ignore).Hit && !world.Sweep(up, to, mins, maxs, ignore).Hit;
    }

    /// <summary>
    /// Whether a falling step blocked straight goes clear across and then down (off a ledge lower
    /// than a step, as the movement steps down): the straight line from a body standing on the
    /// ledge to the floor below strikes the ledge's top at once, a floor's face, and would keep the
    /// body on the ledge for good, following and never blocked.
    /// </summary>
    private bool Descended(in Vector start, in Vector to, in Vector mins, in Vector maxs, EntityHandle ignore)
    {
        if (to.Z >= start.Z - 0.5f)
            return false;

        var across = new Vector(to.X, to.Y, start.Z);
        _sweeps += 2;
        return !world.Sweep(start, across, mins, maxs, ignore).Hit && !world.Sweep(across, to, mins, maxs, ignore).Hit;
    }

    private static Vector ClipVelocity(Vector v, Vector normal)
    {
        float into = v.X * normal.X + v.Y * normal.Y + v.Z * normal.Z;
        return into < 0f ? v - normal * into : v;
    }

    private void Report(int b, bool blocked, in MotionTrace blocker, long now)
    {
        if (!blocked)
        {
            if (_blocked[b].Blocked && ++_clearRun[b] >= _settings.BlockedClearTicks)
                _blocked[b] = BlockedReport.Clear;
            return;
        }

        var (kind, who) = Classify(in blocker);
        Report(b, kind, who, now);
    }

    /// <summary>A blocked tick by <paramref name="kind"/> <paramref name="who"/>: the run goes on, or a new one starts.</summary>
    private void Report(int b, BlockerKind kind, int who, long now)
    {
        _clearRun[b] = 0;
        var r = _blocked[b];
        _blocked[b] = r.Kind == kind && r.Who == who
            ? r with { Last = now, Ticks = r.Ticks + 1 }
            : new BlockedReport(kind, who, now, now, 1);
    }

    /// <summary>The body index of <paramref name="entity"/> when it is one of this level's robot bodies, else 0.</summary>
    public int BodyOf(EntityHandle entity)
    {
        int index = entity.EntryIndex;
        if (index < 0 || index >= _bodyOfEntity.Length)
            return 0;
        int b = _bodyOfEntity[index];
        return b > 0 && _bodyHandle[b] == entity ? b : 0;
    }

    /// <summary>What a sweep struck, as a blocked report names it.</summary>
    internal (BlockerKind Kind, int Who) Classify(in MotionTrace t)
    {
        int index = t.EntityIndex;
        if (index <= 0)
            return (BlockerKind.World, 0);
        if (index < MaxEntities && _bodyOfEntity[index] > 0)
            return (BlockerKind.Robot, _bodyOfEntity[index]);
        return t.IsPlayer ? (BlockerKind.Hero, index) : (BlockerKind.Prop, index);
    }

    private void Face(IMotionBodies bodies, int b, in MotionBody info)
    {
        var s = _state[b];
        var at = _written[b];
        var velocity = new AiVector(s.Velocity.X, s.Velocity.Y, 0f);
        float facing = _facing[b];

        // Where the body looks: a place, an actor's eye now, or along its motion.
        float lookYaw = facing, elevation = 0f;
        bool target = false;
        AiVector point = default;
        if (_ordered[b] && _now < _expires[b])
        {
            var look = _look[b];
            switch (look.Kind)
            {
                case LookKind.Actor:
                    if (_actors is { } actors && actors.TryEye(look.Actor, out var eye))
                    {
                        point = eye;
                        target = true;
                    }
                    else if (look.Point != AiVector.Zero)
                    {
                        point = look.Point;
                        target = true;
                    }

                    break;
                case LookKind.Point or LookKind.Glance or LookKind.CornerCheck or LookKind.Watch:
                    point = look.Point;
                    target = true;
                    break;
            }
        }

        if (target)
        {
            var eyeAt = at + new AiVector(0f, 0f, s.ViewOffset.Z > 0f ? s.ViewOffset.Z : GameMovementStep.EyeOf(info.Scale).Z);
            var d = point - eyeAt;
            lookYaw = MotionAngles.YawOf(d, facing);
            elevation = MotionAngles.ElevationOf(d);
        }
        else if (_wished[b] && velocity.Length2D > _settings.RunGaitSpeed)
        {
            lookYaw = MotionAngles.YawOf(velocity, facing);
        }

        facing = MotionAngles.Turn(facing, lookYaw, _settings.TurnRate * _tickSeconds);
        _facing[b] = facing;

        if (at != _placedAt[b] || facing != _placedYaw[b])
        {
            bodies.Place(b, at, facing);
            _placedAt[b] = at;
            _placedYaw[b] = facing;
            _places++;
        }

        bool air = !s.OnGround && _airSince[b] != 0 && _now - _airSince[b] >= _settings.AirGaitTicks;
        var gait = MotionPoses.Gait(!air, velocity, _settings.RunGaitSpeed);
        if (gait != _gait[b])
        {
            _gait[b] = gait;
            bodies.Gait(b, gait);
            _gaits++;
        }

        var (mx, my) = MotionPoses.Move(velocity.X, velocity.Y, facing, info.Speed, 1f);
        var (ty, tp) = target ? MotionPoses.Torso(lookYaw, elevation, facing, _settings.BodyYawLimit, _settings.BodyPitchLimit) : (0f, 0f);
        var pose = new MotionPose(mx, my, ty, tp);
        var was = _pose[b];
        float eps = _settings.PoseEpsilon;
        if (MathF.Abs(pose.MoveX - was.MoveX) > eps || MathF.Abs(pose.MoveY - was.MoveY) > eps
            || MathF.Abs(pose.BodyYaw - was.BodyYaw) > eps || MathF.Abs(pose.BodyPitch - was.BodyPitch) > eps
            || (pose != was && pose == default))
        {
            _pose[b] = pose;
            bodies.Pose(b, in pose);
            _poses++;
        }
    }

    private bool Plan(int b, in MotionBody info, long now, out StepInput input)
    {
        var s = _state[b];
        if (_route[b] is not null && _corner[b] > 0)
            FeedRoute(b);

        // Pushes join the velocity between steps.
        var push = _impulse[b];
        if (push != AiVector.Zero)
        {
            s.Velocity += HordeVectors.ToEngine(push);
            _impulse[b] = AiVector.Zero;
            _state[b] = s;
        }

        var wish = MotionWish.None;
        if (_ordered[b] && now < _expires[b] && !info.Stunned)
        {
            var (mins, maxs) = Hull(info.Scale);
            float radius = MotionSteering.ArriveRadius(_settings, (maxs.X - mins.X) * 0.5f);
            wish = MotionSteering.Wish(in _move[b], ref _corner[b], _written[b], radius, _settings.WalkFraction);
        }

        if (!wish.Wishing && _blocked[b].Blocked)
            _blocked[b] = BlockedReport.Clear;

        // The order's last corner reached: once a distinct last corner (a brain re-sends its order each decide).
        if (wish.Arrived && !_arrivedOrder[b])
        {
            _arrivedOrder[b] = true;
            var last = _move[b].Corner(_move[b].CornerCount - 1);
            if (!_arrived[b].Ever || (last - _arrivedCorner[b]).Length2D > ArrivedApart)
            {
                _arrivedCorner[b] = last;
                _arrived[b] = new ArrivedReport(now, _written[b], _arrived[b].Count + 1);
                Arrivals++;
            }
        }

        float speed = info.Stunned ? 0f : info.Speed * wish.SpeedShare;
        if (wish.ToLast)
            speed = MotionSteering.Arrive(speed, wish.LastDistance, _settings.BrakeRate, _settings.LeastArriveSpeed);
        float yaw = wish.Wishing ? MotionAngles.YawOf(wish.Direction, _facing[b]) : _facing[b];
        _wished[b] = wish.Wishing;
        float forward = wish.Wishing ? speed : 0f, side = 0f;

        // Robots are one team: they pass through each other and push apart where they overlap (stock's teammate pushaway).
        int crowd = info.Stunned ? 0 : Separate(b, in info, in s, yaw, ref forward, ref side);

        bool still = s.Velocity.X == 0f && s.Velocity.Y == 0f && s.Velocity.Z == 0f;
        if (!wish.Wishing && !wish.Jump && !wish.Crouch && crowd == 0 && s.OnGround && still && !s.Ducked)
        {
            input = default;
            return false;
        }

        int buttons = (wish.Jump ? GameMovement.InJump : 0) | (wish.Crouch ? GameMovement.InDuck : 0);
        input = new StepInput
        {
            Body = b,
            Serial = _serial[b],
            Handle = info.Handle,
            State = s,
            Yaw = yaw,
            ForwardMove = forward,
            SideMove = side,
            Buttons = buttons,
            MaxSpeed = MathF.Max(crowd != 0 ? MathF.Max(speed, info.Speed) : speed, 1f),
            Scale = info.Scale,
            CurTime = now * _tickSeconds,
            Expect = wish.Wishing ? speed * _tickSeconds : 0f,
            Crowd = crowd,
        };
        return true;
    }

    /// <summary>
    /// Stock's teammate pushaway (<see cref="TeammatePushaway.Adjust"/>, the
    /// port of <c>C_TFPlayer::AvoidPlayers</c>) for a robot among the other
    /// bodies with motion: each whose box overlaps this body's adds a sideways
    /// move away from it, stronger the deeper the overlap (up to
    /// <see cref="TeammatePushaway.MaxSeparationForce"/>). Answers the nearest
    /// such body's index, or 0 when none overlaps.
    ///
    /// <para>
    /// Three changes to stock, for robots that stand still in a crowd (stock's
    /// players are moving, and push from one teammate, the first found):
    /// every overlapping body pushes, and the pushes add up, so a body in a
    /// pack of four is not pulled back into one while pushed from another; the
    /// sum is never weaker than <see cref="MotionSettings.LeastSeparation"/>
    /// (stock's fades to nothing at the box's edge, under the floor's stop
    /// speed, and two standing bodies stayed half inside each other) nor
    /// faster than the robot; and two bodies on the very same spot (a pack lent
    /// on one marker, two thrown into one place), where there is no direction
    /// from one centre to the other, take the other to be half a unit to this
    /// body's right when its index is higher, to its left when lower, so the
    /// pair push opposite ways.
    /// </para>
    /// </summary>
    private int Separate(int b, in MotionBody info, in BodyKinematics s, float yaw, ref float forward, ref float side)
    {
        var (mins, maxs) = Hull(info.Scale);
        var me = _written[b];
        var self = new PushawaySelf(HordeVectors.ToEngine(me), mins, maxs, s.Velocity, yaw, info.Speed,
                                    s.OnGround && (s.Flags & GameMovement.FlDucking) != 0);
        int nearest = 0;
        float nearestDistance = float.MaxValue, sumForward = 0f, sumSide = 0f;
        for (int o = 1; o < _serial.Length; o++)
        {
            if (o == b || _serial[o] == 0)
                continue;

            var (omins, omaxs) = Hull(_scaleOf[o]);
            var at = _written[o];
            if (at.X + omins.X > me.X + maxs.X || at.X + omaxs.X < me.X + mins.X
                || at.Y + omins.Y > me.Y + maxs.Y || at.Y + omaxs.Y < me.Y + mins.Y
                || at.Z + omins.Z > me.Z + maxs.Z || at.Z + omaxs.Z < me.Z + mins.Z)
                continue;

            var origin = HordeVectors.ToEngine(at);
            if (MathF.Abs(at.X - me.X) < CoincidentDistance && MathF.Abs(at.Y - me.Y) < CoincidentDistance)
            {
                float r = yaw * (MathF.PI / 180f);
                float sign = o > b ? CoincidentDistance : -CoincidentDistance;
                origin += new Vector(MathF.Sin(r) * sign, -MathF.Cos(r) * sign, 0f);
            }

            _mates[0] = new PushawayBody(origin, omins, omaxs);
            float f = 0f, sd = 0f;
            if (!TeammatePushaway.Adjust(in self, _mates.AsSpan(0, 1), TeammatePushaway.MaxSeparationForce, info.Speed, info.Speed,
                                         info.Speed, ref f, ref sd))
                continue;

            sumForward += f;
            sumSide += sd;
            float d = (at - me).Length2D;
            if (d < nearestDistance)
            {
                nearestDistance = d;
                nearest = o;
            }
        }

        if (nearest == 0)
            return 0;

        float push = MathF.Sqrt(sumForward * sumForward + sumSide * sumSide);
        float least = MathF.Min(_settings.LeastSeparation, info.Speed);
        float scale = push <= 0f ? 0f : push < least ? least / push : push > info.Speed ? info.Speed / push : 1f;
        forward += sumForward * scale;
        side += sumSide * scale;

        // The whole move inside the robot's speed, its direction kept (stock's clamp to the input limits).
        float move = MathF.Sqrt(forward * forward + side * side);
        if (move > info.Speed && move > 0f)
        {
            forward *= info.Speed / move;
            side *= info.Speed / move;
        }

        return nearest;
    }

    /// <summary>Two bodies nearer than this on both floor axes are on one spot (see <see cref="Separate"/>).</summary>
    public const float CoincidentDistance = 0.5f;

    private void FeedRoute(int b)
    {
        var route = _route[b]!;
        _routeAt[b] = Math.Min(route.Count, _routeAt[b] + _corner[b]);
        int left = route.Count - _routeAt[b];
        if (left <= 0)
        {
            // Every corner reached: hold at the last.
            SetOrder(b, MoveIntent.Hold, LookIntent.AlongPath, long.MaxValue);
            _route[b] = null;
            return;
        }

        int k = _routeAt[b];
        var move = new MoveIntent(MoveKind.Follow, MoveSpeed.Run, (byte)Math.Min(MoveIntent.MaxCorners, left),
                                  route[k], left > 1 ? route[k + 1] : default, left > 2 ? route[k + 2] : default,
                                  left > 3 ? route[k + 3] : default, default, false, 0);
        SetOrder(b, in move, LookIntent.AlongPath, long.MaxValue);
    }

    /// <summary>Two arrivals of a lend are told apart only when their orders' last corners are farther apart than this.</summary>
    public const float ArrivedApart = 32f;

    private void SetOrder(int b, in MoveIntent move, in LookIntent look, long expires)
    {
        _arrivedOrder[b] = false;
        _move[b] = move;
        _look[b] = look;
        _expires[b] = expires;
        _corner[b] = 0;
        _ordered[b] = true;
        OrdersTaken++;
    }

    private bool Live(int body, out MotionBody info)
    {
        info = default;
        return _bodies is { } bodies && body >= 1 && body < _serial.Length && bodies.Read(body, out info);
    }

    /// <summary>Takes a body lent since the motion last looked.</summary>
    private void Adopt(int body, in MotionBody info)
    {
        if (info.Serial != _serial[body])
            Start(body, in info, _now);
    }

    private void Start(int b, in MotionBody info, long now)
    {
        _serial[b] = info.Serial;
        _scaleOf[b] = info.Scale;
        _crowdRun[b] = 0;
        _wished[b] = false;
        var settled = Settle(in info);
        var at = HordeVectors.ToEngine(settled);
        _state[b] = BodyKinematics.At(at, GameMovementStep.EyeOf(info.Scale));
        _move[b] = MoveIntent.Hold;
        _look[b] = LookIntent.AlongPath;
        _expires[b] = 0;
        _ordered[b] = false;
        _corner[b] = 0;
        _facing[b] = info.Yaw;
        _written[b] = settled;
        _placedAt[b] = info.Position;
        _placedYaw[b] = info.Yaw;
        _impulse[b] = AiVector.Zero;
        _blocked[b] = BlockedReport.Clear;
        _clearRun[b] = 0;
        _landed[b] = default;
        _arrived[b] = default;
        _arrivedOrder[b] = false;
        _engineUntil[b] = 0;
        _pinnedRun[b] = 0;
        _airSince[b] = Math.Max(1, now);
        _leftZ[b] = settled.Z;
        _peakZ[b] = settled.Z;
        _gait[b] = MotionGait.Stand;
        _pose[b] = default;
        _route[b] = null;
        _routeAt[b] = 0;
    }

    /// <summary>
    /// Where a newly lent body starts: its place lifted by <see cref="SettleLift"/>
    /// (a body put down on a floor's plane touches it, which the engine's place
    /// test counts as in solid, and the movement would never take it off); when
    /// that is still in solid (another body lent at the same marker, a prop, a
    /// wall its hull reaches into), the first clear place on rings of a half,
    /// one and two hull widths round it, eight a ring, from whose middle a line
    /// to the marker's middle meets no world (so never behind a wall; a static
    /// prop is world too, so never behind one either); else where it was put.
    /// Only at a lend: up to 49 sweeps.
    /// </summary>
    private AiVector Settle(in MotionBody info)
    {
        var (mins, maxs) = Hull(info.Scale);
        var lifted = info.Position + new AiVector(0f, 0f, SettleLift);
        if (!world.Sweep(HordeVectors.ToEngine(lifted), HordeVectors.ToEngine(lifted), mins, maxs, info.Handle).StartSolid)
            return lifted;

        float width = maxs.X - mins.X + 2f;
        var middle = new AiVector(0f, 0f, maxs.Z * 0.5f);
        for (int ring = 1; ring <= 4; ring *= 2)
        {
            for (int k = 0; k < 8; k++)
            {
                float a = k * (MathF.PI / 4f);
                var p = lifted + new AiVector(MathF.Cos(a) * width * ring * 0.5f, MathF.Sin(a) * width * ring * 0.5f, 0f);
                var e = HordeVectors.ToEngine(p);
                if (world.Sweep(e, e, mins, maxs, info.Handle).StartSolid)
                    continue;

                // Only a place on this side of the walls: a line back to the marker meets no world (a body or a prop it was put in is fine).
                var back = world.Sweep(HordeVectors.ToEngine(p + middle), HordeVectors.ToEngine(lifted + middle), Vector.Zero, Vector.Zero, info.Handle);
                if (!back.Hit || Classify(in back).Kind != BlockerKind.World)
                    return p;
            }
        }

        return info.Position;
    }

    /// <summary>How far above the place it was put a newly lent body starts: four times the engine's 1/32 touching distance, clear of a floor it was put on, too little to be seen.</summary>
    public const float SettleLift = 0.125f;

    private void Forget(int b)
    {
        _serial[b] = 0;
        _ordered[b] = false;
        _route[b] = null;
        _impulse[b] = AiVector.Zero;
        _blocked[b] = BlockedReport.Clear;
    }

    /// <summary>
    /// A body put somewhere else by hand (HO12: a teleporting champion): its motion forgets the
    /// body, so at the next tick it starts again from where the body now stands, as at a lend
    /// (settled, holding, no order); a step still in flight for it is dropped by its serial.
    /// </summary>
    public void Restart(int body)
    {
        if ((uint)body < (uint)_serial.Length)
            _serial[body] = 0;
    }

    /// <summary>The robot a lent body's motion belongs to, for the dev print (none for a body without motion).</summary>
    public bool Moving(int body) => (uint)body < (uint)_serial.Length && _serial[body] != 0;

    internal RobotId RobotOf(int body) => _bodies is { } bodies && bodies.Read(body, out var info) ? info.Robot : RobotId.None;
}
