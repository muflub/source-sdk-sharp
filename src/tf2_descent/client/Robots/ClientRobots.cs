using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Robots;

/// <summary>
/// The client's <c>tf2_descent_robot</c> (plan_descent_logic HO2, 3.7): draws
/// the server's animation, which only the body's own net state carries (HO0
/// row 2). It scales the model, plays <see cref="RobotNetState.Sequence"/>
/// from its start each time the serial changes, and moves the four pose
/// parameters towards the networked values, eased over frames by
/// <see cref="ClientRobotPoses"/> so a change does not step at the update rate.
/// </summary>
[ClientStandaloneEntity(RobotNetState.ClassName)]
public sealed class ClientRobot(IEngineEntityRender render, ClientRobotPoses poses) : ClientStandaloneEntity(render)
{
    /// <summary>The pose parameters eased, in the order the net state carries them.</summary>
    public const int Poses = 4;

    public override string ClassName => RobotNetState.ClassName;

    [NetState] public RobotNetState State = new();

    private readonly int[] _pose = [-2, -2, -2, -2];
    private readonly float[] _now = new float[Poses];
    private float _scale = -1f;
    private int _serial = -1, _sequence = -1;

    /// <summary>Updates decoded since the entity was made.</summary>
    public int Updates { get; private set; }

    public RobotFlags Flags => (RobotFlags)State.Flags;

    /// <summary>The life left as a fraction of the robot's maximum, 0 to 1: the health bar's.</summary>
    public float LifeFraction => State.Life / 255f;

    public bool Dormant => Render.IsDormant(Handle);

    public override void OnSpawn() => poses.Track(this);

    public override void OnRemoved() => poses.Untrack(this);

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Updates++;
        poses.Track(this);

        if (State.ModelScale > 0f && State.ModelScale != _scale)
        {
            _scale = State.ModelScale;
            Render.SetModelScale(Handle, _scale);
        }

        if (State.Sequence != _sequence || State.SequenceSerial != _serial)
        {
            _sequence = State.Sequence;
            _serial = State.SequenceSerial;
            if (_sequence >= 0)
                Render.PlaySequence(Handle, _sequence, 1f, true);
        }

        if (_pose[0] == -2)
        {
            _pose[0] = Render.LookupPoseParameter(Handle, "move_x");
            _pose[1] = Render.LookupPoseParameter(Handle, "move_y");
            _pose[2] = Render.LookupPoseParameter(Handle, "body_pitch");
            _pose[3] = Render.LookupPoseParameter(Handle, "body_yaw");
        }
    }

    /// <summary>The networked value of pose <paramref name="p"/>.</summary>
    public float Target(int p) => p switch
    {
        0 => State.MoveX,
        1 => State.MoveY,
        2 => State.BodyPitch,
        _ => State.BodyYaw,
    };

    /// <summary>The value pose <paramref name="p"/> is drawn at now.</summary>
    public float Current(int p) => _now[p];

    /// <summary>
    /// One frame of easing: each pose moves <paramref name="step"/> of the way
    /// to its networked value (all of it when the step reaches 1, or when the
    /// gap is under a hundredth). Answers whether any pose is still short of
    /// its value; a pose at its value costs no engine call.
    /// </summary>
    public bool Ease(float step)
    {
        bool moving = false;
        for (int p = 0; p < Poses; p++)
        {
            float target = Target(p), now = _now[p];
            if (now == target || _pose[p] < 0)
                continue;

            float next = ClientRobotPoses.Toward(now, target, step);
            _now[p] = next;
            Render.SetPoseParameter(Handle, _pose[p], next);
            moving |= next != target;
        }

        return moving;
    }

    public string Describe() => FormattableString.Invariant(
        $"#{Handle.EntryIndex} dormant {Dormant} flags {Flags} template {State.Template} level {State.Level} scale {State.ModelScale:0.000} life {LifeFraction:0.000} seq {State.Sequence} serial {State.SequenceSerial} move_x {_now[0]:0.00}/{State.MoveX:0.00} statuses {State.Statuses} updates {Updates}");
}

/// <summary>The client's <c>tf2_descent_robot_weapon</c>: bone-merged to its body by the body's index.</summary>
[ClientStandaloneEntity(RobotWeaponNetState.ClassName)]
public sealed class ClientRobotWeapon(IEngineEntityRender render) : ClientStandaloneEntity(render)
{
    public override string ClassName => RobotWeaponNetState.ClassName;

    [NetState] public RobotWeaponNetState State = new();

    private int _merged = -1;

    public int MergedOnto => _merged;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        int owner = State.OwnerIndex;
        if (owner > 0 && owner != _merged)
        {
            _merged = owner;
            Render.SetBoneMergeSource(Handle, owner);
        }
    }
}

/// <summary>
/// The client's live robots, kept by the robots themselves, and the pose
/// easing once a frame: each robot whose poses are short of their networked
/// values moves <see cref="EaseRate"/> of the gap a second; a robot at rest
/// costs a comparison.
/// </summary>
[SideService(Side.Client)]
public sealed class ClientRobotPoses : IClientFrameThink, IOrderedHook
{
    /// <summary>After TF2's frame thinks (which hold 0).</summary>
    public int HookOrder => 500;

    /// <summary>The share of the gap closed a second: about a tenth of a second to settle.</summary>
    public const float EaseRate = 12f;

    private readonly List<ClientRobot> _live = new();

    public IReadOnlyList<ClientRobot> Live => _live;

    public void Track(ClientRobot robot)
    {
        if (!_live.Contains(robot))
            _live.Add(robot);
    }

    public void Untrack(ClientRobot robot) => _live.Remove(robot);

    public void ClientThink(float frameTime)
    {
        float step = Math.Clamp(frameTime * EaseRate, 0f, 1f);
        foreach (var robot in _live)
            robot.Ease(step);
    }

    /// <summary><paramref name="now"/> moved <paramref name="step"/> of the way to <paramref name="target"/>; the target itself within a hundredth.</summary>
    public static float Toward(float now, float target, float step)
    {
        float next = now + (target - now) * Math.Clamp(step, 0f, 1f);
        return MathF.Abs(target - next) < 0.01f ? target : next;
    }
}

/// <summary><c>tf2_descent_robots_cl</c>: the robots this client holds, dormant or not, and their weapons.</summary>
[ConCommand("tf2_descent_robots_cl", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robots_cl [all]: the robot bodies and weapons this client holds")]
public sealed class ClientRobotsCommand(ILog log, ClientRobotPoses robots, IClientStandalones standalones)
{
    public void Execute(string verb = "")
    {
        int weapons = 0, merged = 0;
        foreach (var e in standalones.Live())
        {
            if (e is ClientRobotWeapon w)
            {
                weapons++;
                if (w.MergedOnto > 0) merged++;
            }
        }

        int dormant = 0, lent = 0;
        foreach (var r in robots.Live)
        {
            if (r.Dormant) dormant++;
            if ((r.Flags & RobotFlags.Lent) != 0) lent++;
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Robots] client holds {robots.Live.Count} robots ({dormant} dormant, {lent} lent), {weapons} weapons ({merged} merged)\n"));
        var sb = new StringBuilder();
        int shown = 0;
        foreach (var r in robots.Live)
        {
            if (verb != "all" && shown >= 12)
                break;
            sb.Append("[TF2Descent/Robots]   ").Append(r.Describe()).Append('\n');
            shown++;
        }

        if (sb.Length > 0)
            log.Msg(sb.ToString());
    }
}
