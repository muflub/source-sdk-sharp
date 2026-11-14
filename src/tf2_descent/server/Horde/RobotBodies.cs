using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// A level's robot bodies and their weapons as the horde's data side lends them
/// (<see cref="IRobotBodies"/>; plan_descent_logic HO2): body index <c>b</c>
/// (1 to the pool's last) is <see cref="Body"/>(b), made once at level ready
/// with its weapon and parked. <see cref="Begin"/> puts a body out at a
/// robot's state; <see cref="End"/> reads the robot's state back and parks
/// it. Hits on a lent body reach the roster here (<see cref="Hit"/>).
/// </summary>
public sealed class RobotBodies : IRobotBodies
{
    private readonly RobotBody[] _bodies;      // by body index; [0] unused
    private readonly RobotRoster _roster;
    private readonly HordeLevel _level;
    private readonly TF2DescentRules _rules;
    private readonly HordeParking _parking;
    private readonly IRobotHitSink _hits;
    private readonly IEngineEntityRender _render;
    private readonly Dictionary<(string Model, string Sequence), int> _sequences = new();
    private readonly AiVector[] _lastPosition;   // by body index: where the last sync found it

    public RobotBodies(RobotBody[] bodies, RobotRoster roster, HordeLevel level, TF2DescentRules rules, HordeParking parking,
                       IRobotHitSink hits, IEngineEntityRender render)
    {
        _bodies = bodies;
        _roster = roster;
        _level = level;
        _rules = rules;
        _parking = parking;
        _hits = hits;
        _render = render;
        _lastPosition = new AiVector[bodies.Length];
    }

    /// <summary>The highest body index.</summary>
    public int LastBody => _bodies.Length - 1;

    public RobotBody Body(int index) => (uint)(index - 1) < (uint)LastBody
        ? _bodies[index]
        : throw new ArgumentOutOfRangeException(nameof(index), index, $"bodies are 1 to {LastBody}");

    /// <summary>Lent bodies now.</summary>
    public int Lent
    {
        get
        {
            int n = 0;
            for (int b = 1; b <= LastBody; b++)
                if (!_bodies[b].Parked) n++;
            return n;
        }
    }

    /// <summary>Parks every body and weapon: what the pool looks like at level ready.</summary>
    public void ParkAll()
    {
        for (int b = 1; b <= LastBody; b++)
            ParkBody(_bodies[b]);
    }

    public void Begin(BodyLease lease, RobotId robot, in RobotBodyState start)
    {
        var body = Body(lease.Body);
        if (!body.Parked)
            throw new InvalidOperationException($"body {lease.Body} is out for {body.Robot}; it cannot also go to {robot}");

        int i = robot.Index;
        var t = _rules.Robots.Template(_roster.Template(i));
        var stats = _roster.Stats(i);
        var flags = (_roster.IsChampion(i) ? RobotFlags.Champion : 0) | (_roster.IsBoss(i) ? RobotFlags.Boss : 0);
        string model = RobotLooks.BodyModel(t);

        body.Lend(lease, robot, model, (float)_roster.Scale(i), start.Position, start.Yaw, start.Life, stats.Life,
                  TemplateId(t.Id), (byte)Math.Clamp(_roster.MonsterLevel(i), 0, 255), flags, StatusBits(start.Statuses));
        body.Play(Sequence(body, model, RobotLooks.IdleSequence(t)), running: false);
        body.Weapon?.Show(RobotLooks.WeaponModel(t));
        _lastPosition[lease.Body] = start.Position;
        _parking.Unpark(body.Handle);
        if (body.Weapon is { } w)
            _parking.Unpark(w.Handle);
    }

    public RobotBodyState End(BodyLease lease, RobotId robot)
    {
        var body = Body(lease.Body);
        if (body.Lease != lease || body.Robot != robot)
            throw new InvalidOperationException($"body {lease.Body} is lent to {body.Robot} (serial {body.Lease.Serial}), not {robot} (serial {lease.Serial})");

        int i = robot.Index;
        int room = _level.RoomAt(body.Position);
        var end = new RobotBodyState(room >= 0 ? room : _roster.Room(i), body.Position, body.Yaw, _roster.Life(i), _roster.Statuses(i),
                                     body.Clip, _roster.Carried(i));
        ParkBody(body);
        return end;
    }

    /// <summary>
    /// Each lent body's place to its roster row (<see cref="RobotRoster.SetPose"/>),
    /// its animation (the run sequence while its place changed since the last
    /// sync, else the stand), its status flags. Run each decision tick, before
    /// the decision reads the roster.
    /// </summary>
    public void Sync()
    {
        for (int b = 1; b <= LastBody; b++)
        {
            var body = _bodies[b];
            if (body.Parked || !_roster.IsLive(body.Robot))
                continue;

            int i = body.Robot.Index;
            int room = _level.RoomAt(body.Position);
            _roster.SetPose(i, room >= 0 ? room : _roster.Room(i), body.Position, body.Yaw);

            bool moved = body.Position != _lastPosition[b];
            _lastPosition[b] = body.Position;
            if (moved != body.Running)
            {
                var t = _rules.Robots.Template(_roster.Template(i));
                body.Play(Sequence(body, body.Model, moved ? RobotLooks.RunSequence(t) : RobotLooks.IdleSequence(t)), moved);
            }

            body.SetStatuses(StatusBits(_roster.Statuses(i)));
        }
    }

    /// <summary>
    /// A hit on <paramref name="body"/> (its <see cref="RobotBody.OnDamagedAt"/>,
    /// after TF's modify rules). A parked body, or one whose robot is dead,
    /// takes nothing and hands nothing on. Otherwise the rules take the robot's
    /// life (<see cref="RobotRules.DamageTaken"/>: its resistance to the hit's
    /// damage type, nothing for the type it is immune to), the hit is handed
    /// on (<see cref="IRobotHitSink"/>) whatever it took, and the answer is the
    /// engine health that leaves the body at the robot's life rounded up: all
    /// of it when the hit killed, so the shell runs the death.
    /// </summary>
    public int Hit(RobotBody body, EntityHandle attacker, float damage, DamageType bits, Vector position, Vector force, HitGroup group)
    {
        if (body.Parked || !_roster.IsLive(body.Robot))
            return 0;

        int i = body.Robot.Index;
        if (_roster.State(i) != RobotState.Embodied)
            return 0;

        string type = RobotDamageKinds.Of(bits);
        var stats = _roster.Stats(i);
        double taken = RobotRules.DamageTaken(stats, type, damage);
        bool killed = _roster.Hurt(i, taken);
        double life = _roster.Life(i);
        int healthAfter = killed ? 0 : Math.Max(1, (int)Math.Ceiling(life));
        int answer = Math.Max(0, body.EngineHealth - healthAfter);
        body.Took(answer);
        body.SetLife(life, stats.Life);
        if (killed)
            body.Died();

        _hits.OnRobotHit(new RobotHit(body.Robot, body.Index, attacker, damage, bits, type, taken, life, killed, group, position, force));
        return answer;
    }

    private void ParkBody(RobotBody body)
    {
        body.Park();
        body.Weapon?.Hide();
        _parking.Park(body.Handle);
        if (body.Weapon is { } w)
            _parking.Park(w.Handle);
    }

    /// <summary>A sequence of <paramref name="model"/> by name, looked up on the server's skeleton once a model; -1 when it has none.</summary>
    private int Sequence(RobotBody body, string model, string name)
    {
        if (!_sequences.TryGetValue((model, name), out int seq))
        {
            seq = _render.LookupSequence(body.Handle, name);
            _sequences[(model, name)] = seq;
        }

        return seq;
    }

    /// <summary>A template's row in robots.json, plus one; 0 is no robot.</summary>
    private byte TemplateId(string id)
    {
        var rows = _rules.Content.Robots.Rows;
        for (int r = 0; r < rows.Count; r++)
            if (rows[r].Id == id)
                return (byte)Math.Min(r + 1, 255);
        return 0;
    }

    /// <summary>A bit per status.json row the robot carries, in table order.</summary>
    private byte StatusBits(RobotStatuses statuses)
    {
        if (statuses.Active.IsEmpty)
            return 0;

        var rows = _rules.Content.Status.Rows;
        int bits = 0;
        foreach (var a in statuses.Active)
            for (int r = 0; r < rows.Count && r < 8; r++)
                if (rows[r].Id == a.Id)
                    bits |= 1 << r;
        return (byte)bits;
    }
}
