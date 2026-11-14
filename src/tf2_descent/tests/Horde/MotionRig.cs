using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A world of boxes for the motion's facts, engine-free and safe to read from
/// any thread: a floor (the top face of a slab at <see cref="FloorZ"/>,
/// unless <see cref="Floorless"/>) and axis-aligned solid boxes, each standing
/// for an entity (an index; a player or not). A hull is swept as a point
/// against each box grown by the hull, and stops 1/32 short of a face, as the
/// engine's trace does.
/// </summary>
internal sealed class BoxMotionWorld : IMotionWorld
{
    public const float DistEpsilon = 0.03125f;

    private const float Far = 100_000f;

    private int _sweeps;

    public readonly record struct Box(Vector Min, Vector Max, int EntityIndex, bool IsPlayer);

    public List<Box> Boxes { get; } = [];

    public float FloorZ { get; set; }

    public bool Floorless { get; set; }

    public int Sweeps => Volatile.Read(ref _sweeps);

    /// <summary>A box of <paramref name="entityIndex"/> from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public BoxMotionWorld Put(Vector min, Vector max, int entityIndex = 0, bool isPlayer = false)
    {
        Boxes.Add(new Box(min, max, entityIndex, isPlayer));
        return this;
    }

    public MotionTrace Sweep(in Vector start, in Vector end, in Vector mins, in Vector maxs, EntityHandle ignore)
    {
        Interlocked.Increment(ref _sweeps);
        var best = MotionTrace.Clear(end);
        if (!Floorless)
            Clip(new Box(new Vector(-Far, -Far, -Far), new Vector(Far, Far, FloorZ), 0, false), start, end, mins, maxs, ref best);
        for (int i = 0; i < Boxes.Count; i++)
            Clip(Boxes[i], start, end, mins, maxs, ref best);
        return best;
    }

    private static void Clip(in Box box, in Vector start, in Vector end, in Vector mins, in Vector maxs, ref MotionTrace best)
    {
        // The point the hull's origin may not enter: the box grown by the hull.
        float loX = box.Min.X - maxs.X, loY = box.Min.Y - maxs.Y, loZ = box.Min.Z - maxs.Z;
        float hiX = box.Max.X - mins.X, hiY = box.Max.Y - mins.Y, hiZ = box.Max.Z - mins.Z;
        float enter = -1f, leave = 1f;
        int face = -1;
        bool inside = true;

        if (!Face(loX - start.X, loX - end.X, 0, ref enter, ref leave, ref face, ref inside)
            || !Face(loY - start.Y, loY - end.Y, 1, ref enter, ref leave, ref face, ref inside)
            || !Face(loZ - start.Z, loZ - end.Z, 2, ref enter, ref leave, ref face, ref inside)
            || !Face(start.X - hiX, end.X - hiX, 3, ref enter, ref leave, ref face, ref inside)
            || !Face(start.Y - hiY, end.Y - hiY, 4, ref enter, ref leave, ref face, ref inside)
            || !Face(start.Z - hiZ, end.Z - hiZ, 5, ref enter, ref leave, ref face, ref inside))
            return;

        var entity = new EntityHandle((uint)((1 << 16) | box.EntityIndex));
        if (inside)
        {
            if (!best.StartSolid)
                best = new MotionTrace(true, 0f, start, Vector.Zero, true, leave >= 1f, entity, box.EntityIndex, box.IsPlayer);
            return;
        }

        if (enter < leave && enter >= 0f && enter < best.Fraction && !best.StartSolid)
        {
            float sign = face >= 3 ? 1f : -1f;
            var normal = (face % 3) switch
            {
                0 => new Vector(sign, 0f, 0f),
                1 => new Vector(0f, sign, 0f),
                _ => new Vector(0f, 0f, sign),
            };
            var stop = start + (end - start) * enter;
            best = new MotionTrace(true, enter, stop, normal, false, false, entity, box.EntityIndex, box.IsPlayer);
        }
    }

    /// <summary>One face: <paramref name="startOut"/> and <paramref name="endOut"/> are how far outside it the sweep starts and ends.</summary>
    private static bool Face(float startOut, float endOut, int index, ref float enter, ref float leave, ref int face, ref bool inside)
    {
        if (startOut > 0f && endOut > 0f)
            return false;
        if (startOut <= 0f && endOut <= 0f)
            return true;
        if (startOut > 0f)
        {
            inside = false;
            float f = MathF.Max(0f, startOut - DistEpsilon) / (startOut - endOut);
            if (f > enter)
            {
                enter = f;
                face = index;
            }
        }
        else
        {
            float f = (startOut + DistEpsilon) / (startOut - endOut);
            if (f < leave)
                leave = f;
        }

        return true;
    }
}

/// <summary>The actors a look can name: their eyes, as a fact moves them.</summary>
internal sealed class RigActors : IHordeActors
{
    public Dictionary<ActorId, AiVector> Eyes { get; } = new();

    public int Asked { get; private set; }

    public bool TryEye(ActorId actor, out AiVector eye)
    {
        Asked++;
        return Eyes.TryGetValue(actor, out eye);
    }
}

/// <summary>
/// Bodies for the motion's engine-free facts: each lent or not, at a speed and
/// scale a fact sets, with every write counted and the last kept. Writing
/// allocates nothing, so the allocation fact can run over it.
/// </summary>
internal sealed class FakeMotionBodies : IMotionBodies
{
    private readonly bool[] _lent;
    private readonly int[] _serial;
    private readonly float[] _scale, _speed, _yaw;
    private readonly bool[] _stunned;
    private readonly AiVector[] _at;

    public FakeMotionBodies(int last)
    {
        LastBody = last;
        int n = last + 1;
        _lent = new bool[n];
        _serial = new int[n];
        _scale = new float[n];
        _speed = new float[n];
        _yaw = new float[n];
        _stunned = new bool[n];
        _at = new AiVector[n];
        Places = new int[n];
        Gaits = new int[n];
        Poses = new int[n];
        LastGait = new MotionGait[n];
        LastPose = new MotionPose[n];
    }

    public int LastBody { get; }

    public int[] Places { get; }
    public int[] Gaits { get; }
    public int[] Poses { get; }
    public MotionGait[] LastGait { get; }
    public MotionPose[] LastPose { get; }

    public EntityHandle Handle(int body) => new((uint)((1 << 16) | (100 + body)));

    /// <summary>Lends body <paramref name="body"/> at <paramref name="at"/> (a new serial).</summary>
    public void Lend(int body, AiVector at, float speed = 230f, float scale = 1f, float yaw = 0f)
    {
        _lent[body] = true;
        _serial[body]++;
        _at[body] = at;
        _speed[body] = speed;
        _scale[body] = scale;
        _yaw[body] = yaw;
        _stunned[body] = false;
    }

    public void Park(int body) => _lent[body] = false;

    public void Speed(int body, float speed) => _speed[body] = speed;

    public void Stun(int body, bool on) => _stunned[body] = on;

    public AiVector At(int body) => _at[body];

    public float Yaw(int body) => _yaw[body];

    public bool Read(int body, out MotionBody info)
    {
        if (!_lent[body])
        {
            info = default;
            return false;
        }

        info = new MotionBody(Handle(body), _serial[body], new RobotId(body, _serial[body]), _scale[body], _at[body], _yaw[body], _speed[body],
                              _stunned[body], 0, false);
        return true;
    }

    public void Place(int body, AiVector at, float yaw)
    {
        _at[body] = at;
        _yaw[body] = yaw;
        Places[body]++;
    }

    public void Gait(int body, MotionGait gait)
    {
        LastGait[body] = gait;
        Gaits[body]++;
    }

    public void Pose(int body, in MotionPose pose)
    {
        LastPose[body] = pose;
        Poses[body]++;
    }
}

/// <summary>A motion over fake bodies and a box world, ticked by hand: the step's and the accept's engine-free facts.</summary>
internal sealed class MotionRig
{
    public const double Tps = 1 / 0.015;

    public MotionRig(int bodies = 4, BoxMotionWorld? world = null, Func<RobotMotion, IMotionStepper>? stepper = null)
    {
        World = world ?? new BoxMotionWorld();
        Bodies = new FakeMotionBodies(bodies);
        Motion = new RobotMotion(World);
        Motion.Begin(Bodies, Actors, MotionSettings.Default, Tps, stepper?.Invoke(Motion));
    }

    public BoxMotionWorld World { get; }
    public RigActors Actors { get; } = new();
    public FakeMotionBodies Bodies { get; }
    public RobotMotion Motion { get; }
    public long Now { get; set; } = 1000;

    public void Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            Motion.Tick(++Now);
    }

    /// <summary>A Follow order through <paramref name="corners"/> (up to four), never expiring.</summary>
    public static MoveIntent Follow(params AiVector[] corners) => new(
        MoveKind.Follow, MoveSpeed.Run, (byte)corners.Length,
        corners.Length > 0 ? corners[0] : default, corners.Length > 1 ? corners[1] : default,
        corners.Length > 2 ? corners[2] : default, corners.Length > 3 ? corners[3] : default,
        default, false, 0);

    /// <summary>Body <paramref name="body"/> lent a unit above the floor at <paramref name="at"/>, and ticked until it has landed on it.</summary>
    public void Stand(int body, AiVector at, float speed = 230f, float scale = 1f)
    {
        Bodies.Lend(body, at + new AiVector(0f, 0f, 1f), speed, scale);
        Tick(12);
    }

    /// <summary>Where body <paramref name="body"/> was last put.</summary>
    public AiVector At(int body) => Bodies.At(body);
}
