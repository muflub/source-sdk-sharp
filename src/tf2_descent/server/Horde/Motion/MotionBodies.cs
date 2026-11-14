using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// A lent, living body as its motion reads it: its entity, the lend's serial
/// (a new lend is a new serial), the robot, its scale, where the body was last
/// put and facing, its speed now (the robot's move speed times its status
/// slow), whether it is stunned, its knockback resistance, and whether it is a
/// Heavy (stock halves another's blast on a Heavy).
/// </summary>
public readonly record struct MotionBody(
    EntityHandle Handle,
    int Serial,
    RobotId Robot,
    float Scale,
    AiVector Position,
    float Yaw,
    float Speed,
    bool Stunned,
    double KnockbackResistPct,
    bool Heavy);

/// <summary>
/// The bodies a motion moves (plan_descent_logic 3.7): what it reads of each
/// and the three writes it makes. The horde's answer is
/// <see cref="HordeMotionBodies"/>; a fact hands its own.
/// </summary>
public interface IMotionBodies
{
    /// <summary>The highest body index (bodies are 1 to it).</summary>
    int LastBody { get; }

    /// <summary>The body's entity, lent or parked.</summary>
    EntityHandle Handle(int body);

    /// <summary>False for a parked body or one whose robot is dead: it has no motion.</summary>
    bool Read(int body, out MotionBody info);

    /// <summary>Puts the body at <paramref name="at"/>, facing <paramref name="yaw"/> degrees.</summary>
    void Place(int body, AiVector at, float yaw);

    /// <summary>Plays the sequence of <paramref name="gait"/> from its start (called only when the gait changes).</summary>
    void Gait(int body, MotionGait gait);

    /// <summary>The four pose dials, onto the body's net state.</summary>
    void Pose(int body, in MotionPose pose);
}

/// <summary>
/// The horde's bodies as a motion reads and writes them: <see cref="RobotBodies"/>
/// and the roster's speed and status, made by the controller at level start.
/// The sequences of a lend are looked up once, when its serial is first seen,
/// on the server's skeleton (<see cref="RobotLooks"/>: stand, run, and the air
/// loop, which every robot model has but the Spy's primary).
/// </summary>
public sealed class HordeMotionBodies : IMotionBodies
{
    private readonly RobotBodies _bodies;
    private readonly RobotRoster _roster;
    private readonly TF2DescentRules _rules;
    private readonly IEngineEntityRender _render;
    private readonly Dictionary<(string Model, string Sequence), int> _sequences = new();
    private readonly int[] _serial, _stand, _run, _air;
    private readonly bool[] _heavy;

    public HordeMotionBodies(RobotBodies bodies, RobotRoster roster, TF2DescentRules rules, IEngineEntityRender render)
    {
        _bodies = bodies;
        _roster = roster;
        _rules = rules;
        _render = render;
        int n = bodies.LastBody + 1;
        _serial = new int[n];
        _stand = new int[n];
        _run = new int[n];
        _air = new int[n];
        _heavy = new bool[n];
    }

    public int LastBody => _bodies.LastBody;

    public EntityHandle Handle(int body) => _bodies.Body(body).Handle;

    public bool Read(int body, out MotionBody info)
    {
        var b = _bodies.Body(body);
        if (b.Parked || ((RobotFlags)b.State.Flags & RobotFlags.Dead) != 0 || !_roster.IsLive(b.Robot)
            || _roster.State(b.Robot.Index) != RobotState.Embodied)
        {
            info = default;
            return false;
        }

        int i = b.Robot.Index;
        if (_serial[body] != b.Lease.Serial)
            Learn(body, b, i);

        var stats = _roster.Stats(i);
        info = new MotionBody(b.Handle, b.Lease.Serial, b.Robot, b.Scale, b.Position, b.Yaw, (float)stats.Speed * _roster.SpeedFactor(i),
                              _roster.Stunned(i), stats.KnockbackResistPct, _heavy[body]);
        return true;
    }

    public void Place(int body, AiVector at, float yaw) => _bodies.Body(body).MoveTo(at, yaw);

    public void Gait(int body, MotionGait gait)
    {
        int seq = gait switch
        {
            MotionGait.Run => _run[body],
            MotionGait.Air => _air[body],
            _ => _stand[body],
        };

        if (seq >= 0)
            _bodies.Body(body).Play(seq, gait != MotionGait.Stand);
    }

    public void Pose(int body, in MotionPose pose) => _bodies.Body(body).Pose(pose.MoveX, pose.MoveY, pose.BodyYaw, pose.BodyPitch);

    private void Learn(int body, RobotBody b, int i)
    {
        var t = _rules.Robots.Template(_roster.Template(i));
        _serial[body] = b.Lease.Serial;
        _stand[body] = Sequence(b, RobotLooks.IdleSequence(t));
        _run[body] = Sequence(b, RobotLooks.RunSequence(t));
        _air[body] = Sequence(b, RobotLooks.AirSequence(t));
        _heavy[body] = t.Class == "heavy";
    }

    private int Sequence(RobotBody b, string name)
    {
        if (!_sequences.TryGetValue((b.Model, name), out int seq))
        {
            seq = _render.LookupSequence(b.Handle, name);
            _sequences[(b.Model, name)] = seq;
        }

        return seq;
    }
}
