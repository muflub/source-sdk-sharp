using System.Globalization;
using System.Text;

using TF2.Descent.Ai.Motion;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// The motion's audit (<c>tf2_descent_horde_motion_audit</c>, HO3's prop test):
/// while on, every tick, a place test of each body with motion at the place
/// the motion last put it, with its own scaled hull, through the world, props,
/// the other bodies and the heroes. A body found in solid is counted by what
/// it is in. Dev only: one more sweep a body a tick.
/// </summary>
public sealed class MotionAudit
{
    public const int MaxBodies = 64;

    private readonly long[] _checked = new long[MaxBodies + 1];
    private readonly long[] _moving = new long[MaxBodies + 1];
    private readonly long[] _inside = new long[MaxBodies + 1];
    private readonly long[] _first = new long[MaxBodies + 1];
    private readonly int[] _lastWho = new int[MaxBodies + 1];
    private readonly BlockerKind[] _lastKind = new BlockerKind[MaxBodies + 1];

    public bool On { get; private set; }

    public void Start()
    {
        Array.Clear(_checked);
        Array.Clear(_moving);
        Array.Clear(_inside);
        Array.Clear(_first);
        Array.Clear(_lastWho);
        Array.Clear(_lastKind);
        On = true;
    }

    public void Stop() => On = false;

    internal void Check(RobotMotion motion, IMotionBodies bodies, IMotionWorld world, long now)
    {
        for (int b = 1; b <= Math.Min(MaxBodies, bodies.LastBody); b++)
        {
            if (!motion.Moving(b) || !bodies.Read(b, out var info) || !motion.TryView(b, out var view))
                continue;

            var at = HordeVectors.ToEngine(motion.Written(b));
            var (mins, maxs) = RobotMotion.Hull(info.Scale);
            var t = world.Sweep(at, at, mins, maxs, info.Handle);
            _checked[b]++;
            if (view.Velocity.Length2D > 1f || !view.OnGround)
                _moving[b]++;
            if (!t.StartSolid)
                continue;

            _inside[b]++;
            if (_first[b] == 0)
                _first[b] = now;
            (_lastKind[b], _lastWho[b]) = motion.Classify(in t);
        }
    }

    public string Report()
    {
        var sb = new StringBuilder();
        for (int b = 1; b <= MaxBodies; b++)
        {
            if (_checked[b] == 0)
                continue;
            sb.Append(CultureInfo.InvariantCulture,
                $"body {b}: ticks {_checked[b]} (moving {_moving[b]}), inside solid {_inside[b]} ({(_checked[b] > 0 ? 100.0 * _inside[b] / _checked[b] : 0):0.00}%)");
            if (_inside[b] > 0)
                sb.Append(CultureInfo.InvariantCulture, $", first at tick {_first[b]}, last in {_lastKind[b]} entity {_lastWho[b]}");
            sb.Append("; ");
        }

        return sb.Length == 0 ? "no body audited" : sb.ToString();
    }
}
