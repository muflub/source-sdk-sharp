using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Skills;
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
    private readonly ISkillHits _skillHits;
    private readonly RobotGuardChain? _guards;
    private readonly Dictionary<(string Model, string Sequence), int> _sequences = new();

    // A status's damage tick in flight (BeginStatusTick): the body it is for, its amount and type.
    private int _tickBody;
    private double _tickAmount;
    private string _tickType = "";

    public RobotBodies(RobotBody[] bodies, RobotRoster roster, HordeLevel level, TF2DescentRules rules, HordeParking parking,
                       IRobotHitSink hits, IEngineEntityRender render, ISkillHits skillHits, RobotGuardChain? guards = null)
    {
        _bodies = bodies;
        _roster = roster;
        _level = level;
        _rules = rules;
        _parking = parking;
        _hits = hits;
        _render = render;
        _skillHits = skillHits;
        _guards = guards;
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
                  TemplateId(t.Id), (byte)Math.Clamp(_roster.MonsterLevel(i), 0, 255), flags, (byte)_roster.StatusBits(i));
        body.Play(Sequence(body, model, RobotLooks.IdleSequence(t)), running: false);
        body.Holds(RobotLooks.WeaponModel(t));
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
    /// Each lent body's place to its roster row (<see cref="RobotRoster.SetPose"/>)
    /// and its status flags. Run each decision tick, before the decision reads
    /// the roster. Its animation is its motion's (Motion.RobotMotion, HO3).
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
            body.SetStatuses((byte)_roster.StatusBits(i));
        }
    }

    /// <summary>
    /// A hit on <paramref name="body"/> (its <see cref="RobotBody.OnDamagedAt"/>,
    /// after TF's modify rules). A parked body, or one whose robot is dead,
    /// takes nothing and hands nothing on. Otherwise:
    /// <list type="bullet">
    /// <item>THE TYPE is the hit's: a skill's hit (<see cref="ISkillHits.Current"/>)
    /// is its declared type (<see cref="TF2.Descent.Rules.Skills.SkillRules.DamageTypeOf"/>:
    /// EMP Charge is shock whatever its energy ball's bits say), a status's
    /// damage tick (<see cref="BeginStatusTick"/>) the status's, anything else
    /// its bits' (<see cref="RobotDamageKinds.Of"/>).</item>
    /// <item>THE LIFE the rules take: its resistance to that type
    /// (<see cref="RobotRules.DamageTaken"/>), times its statuses' damage-taken
    /// multiplier (a mark); nothing at all for the type it is immune to, which is
    /// an IMMUNE hit (<see cref="RobotHit.Immune"/>). A status's tick is its
    /// amount, not what TF's layer made of the hit carrying it. Then the guards
    /// (<see cref="IRobotDamageGuard"/>, HO12: a champion's über or shield, a
    /// buster's spin) may take less.</item>
    /// <item>The hit is handed on (<see cref="IRobotHitSink"/>) whatever it took,
    /// an immune one too, so the brain still hears who attacked it.</item>
    /// </list>
    /// The answer is the engine health that leaves the body at the robot's life
    /// rounded up: none for an immune hit, all of it when the hit killed, so the
    /// shell runs the death.
    /// </summary>
    public int Hit(RobotBody body, EntityHandle attacker, float damage, DamageType bits, Vector position, Vector force, HitGroup group)
    {
        bool statusTick = _tickBody != 0 && _tickBody == body.Index;
        _tickBody = 0;
        if (body.Parked || !_roster.IsLive(body.Robot))
            return 0;

        int i = body.Robot.Index;
        if (_roster.State(i) != RobotState.Embodied)
            return 0;

        // Robots are one team, as TF2's are: a robot's attack hurts the heroes and, by its own blast, itself
        // (L12: hurt and pushed as a player is, and a player is not hurt or pushed by a teammate's rocket).
        if (!statusTick && attacker != body.Handle && IsBody(attacker))
            return 0;

        string type = statusTick ? _tickType : TypeOf(bits);
        double dealt = statusTick ? _tickAmount : damage;
        var stats = _roster.Stats(i);
        bool immune = stats.Immunity == type && dealt > 0;
        double taken = immune ? 0 : RobotRules.DamageTaken(stats, type, dealt) * _roster.DamageTakenFactor(i);
        if (taken > 0 && _guards is { } guards)
            taken = guards.Guard(new RobotGuardHit(body, i, attacker, type, position, taken, _roster.Life(i), stats.Life));
        bool killed = _roster.Hurt(i, taken);
        double life = _roster.Life(i);
        int healthAfter = killed ? 0 : Math.Max(1, (int)Math.Ceiling(life));
        int answer = Math.Max(0, body.EngineHealth - healthAfter);
        body.Took(answer);
        body.SetLife(life, stats.Life);
        if (killed)
            body.Died();

        _hits.OnRobotHit(new RobotHit(body.Robot, body.Index, attacker, (float)dealt, bits, type, taken, life, killed, group, position, force,
                                      immune));
        return answer;
    }

    /// <summary>True when <paramref name="entity"/> is one of the pool's bodies, lent or parked (a rocket outlives its robot's lease).</summary>
    private bool IsBody(EntityHandle entity)
    {
        if (entity == EntityHandle.None)
            return false;
        for (int b = 1; b < _bodies.Length; b++)
        {
            if (_bodies[b].Handle == entity)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The next hit on body <paramref name="body"/> is a status's damage tick of
    /// <paramref name="amount"/> (before resistance) of <paramref name="type"/>:
    /// <see cref="Hit"/> takes these in place of what the engine hands it. The
    /// caller then deals the hit through the engine, so every hook a hit has
    /// runs for it, and calls <see cref="EndStatusTick"/>.
    /// </summary>
    public void BeginStatusTick(int body, double amount, string type)
    {
        _tickBody = body;
        _tickAmount = amount;
        _tickType = type;
    }

    /// <summary>
    /// The status tick is dealt: the next hit is a plain one. Answers whether the tick was still
    /// waiting, which is to say the engine never handed the hit to the body.
    /// </summary>
    public bool EndStatusTick()
    {
        bool waiting = _tickBody != 0;
        _tickBody = 0;
        return waiting;
    }

    /// <summary>A hit's damage type: the skill's declared one while a skill's hit is in flight, else its bits'.</summary>
    private string TypeOf(DamageType bits) =>
        _skillHits.Current() is { } skill && _rules.Skills.DamageTypeOf(skill) is { } declared ? declared : RobotDamageKinds.Of(bits);

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
}
