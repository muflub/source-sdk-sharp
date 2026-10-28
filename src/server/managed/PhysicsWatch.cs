using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_physics_watch "x y z radius steps"</c>: every awake physics object
/// within the radius of the point, after each of the first steps of a level --
/// its position, angles and velocity, its mass and sphere radius, and what it touches (each contact's
/// object and normal force), one <c>[PW]</c> line each. Read from the
/// physics object itself, as stock's PhysFrame hands it to VPhysicsUpdate, so
/// the same lines from a stock build tell the first step where a settle goes
/// apart (dm_lockdown's crate stack). Set it before the map loads; a new level
/// (a new environment) starts the count again.
/// </summary>
[ConVar("cs_sv_physics_watch", "",
    Help = "Log every awake physics object near a point after each step: \"x y z radius steps\"; empty is off.")]
public sealed class PhysicsWatchConVar : StringConVar { }

/// <summary>What <see cref="PhysicsWatchConVar"/> prints. See there.</summary>
public sealed class PhysicsWatch
{
    public PhysicsWatch(IVPhysicsInspect inspect, IVPhysicsSolverObjects objects, IGameClock clock, ILog log,
                        Func<string> setting)
    {
        _inspect = inspect;
        _objects = objects;
        _clock = clock;
        _log = log;
        _setting = setting;
    }

    private readonly IVPhysicsInspect _inspect;
    private readonly IVPhysicsSolverObjects _objects;
    private readonly SnapshotContact[] _contacts = new SnapshotContact[16];
    private readonly IGameClock _clock;
    private readonly ILog _log;
    private readonly Func<string> _setting;

    // The environment the count belongs to, and the steps logged in it.
    private nint _environment;
    private int _step;

    /// <summary>After a physics step: the objects near the point, while the count lasts.</summary>
    public void AfterStep(nint environment)
    {
        if (!TryParse(_setting(), out Vector point, out float radius, out int steps))
            return;

        if (environment != _environment)
        {
            _environment = environment;
            _step = 0;
        }

        if (_step >= steps)
            return;

        _step++;
        foreach (nint o in _inspect.ActiveObjects(environment))
        {
            PhysicsObjectReadout r = _inspect.Read(o);
            float dx = r.Position.X - point.X, dy = r.Position.Y - point.Y, dz = r.Position.Z - point.Z;
            if (dx * dx + dy * dy + dz * dz > radius * radius)
                continue;

            _log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[PW] {_step} {_clock.Now:F3} {r.Name} {r.Position.X:F2} {r.Position.Y:F2} {r.Position.Z:F2} "
                + $"{r.Angles.X:F1} {r.Angles.Y:F1} {r.Angles.Z:F1} {r.Velocity.X:F1} {r.Velocity.Y:F1} {r.Velocity.Z:F1}{(r.Asleep ? " asleep" : "")}"
                + $" m{r.Mass:F1} r{r.SphereRadius:F1}{Touching(o)}\n"));
        }
    }

    /// <summary>
    /// A ShouldSolvePenetration ask about an object near the point: one
    /// <c>[PWPEN]</c> line naming both, as the stock build's hook prints it.
    /// </summary>
    public void Penetration(nint object0, nint object1)
    {
        if (!TryParse(_setting(), out Vector point, out float radius, out _)
            || (!Near(object0, point, radius) && !Near(object1, point, radius)))
            return;

        _log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[PWPEN] {_clock.Now:F3} {_inspect.Read(object0).Name} {_inspect.Read(object1).Name}\n"));
    }

    /// <summary>
    /// A ShouldCollide answer about an object near the point: one
    /// <c>[PWCOL]</c> line naming both with each one's contents, the answer
    /// and the clause that refused.
    /// </summary>
    public void Collide(nint object0, nint object1, int answer, CollisionRefusal why)
    {
        if (!TryParse(_setting(), out Vector point, out float radius, out _)
            || (!Near(object0, point, radius) && !Near(object1, point, radius)))
            return;

        _log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[PWCOL] {_clock.Now:F3} {_inspect.Read(object0).Name} 0x{_objects.Contents(object0):x} {_inspect.Read(object1).Name} 0x{_objects.Contents(object1):x} {answer}{(answer == 0 ? " " + why : "")}\n"));
    }

    private bool Near(nint o, Vector point, float radius)
    {
        if (o == 0)
            return false;
        Vector p = _inspect.Read(o).Position;
        float dx = p.X - point.X, dy = p.Y - point.Y, dz = p.Z - point.Z;
        return dx * dx + dy * dy + dz * dz <= radius * radius;
    }

    /// <summary>" |" and each contact as "name:force", or nothing when it touches nothing.</summary>
    private string Touching(nint o)
    {
        int n = Math.Min(_objects.Contacts(o, _contacts), _contacts.Length);
        if (n == 0)
            return string.Empty;

        var line = new System.Text.StringBuilder(" |");
        for (int i = 0; i < n; i++)
            line.Append(CultureInfo.InvariantCulture, $" {_inspect.Read(_contacts[i].Other).Name}:{_contacts[i].NormalForce:F0}");
        return line.ToString();
    }

    /// <summary>"x y z radius steps", or false.</summary>
    public static bool TryParse(string setting, out Vector point, out float radius, out int steps)
    {
        point = default;
        radius = 0f;
        steps = 0;
        string[] parts = setting.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return false;

        var inv = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[0], NumberStyles.Float, inv, out float x) || !float.TryParse(parts[1], NumberStyles.Float, inv, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, inv, out float z) || !float.TryParse(parts[3], NumberStyles.Float, inv, out radius)
            || !int.TryParse(parts[4], NumberStyles.Integer, inv, out steps))
            return false;

        point = new Vector(x, y, z);
        return true;
    }
}
