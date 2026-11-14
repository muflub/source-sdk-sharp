using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Server.Horde.Motion;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The engine a robot's attack asks (<see cref="IRobotAttackWorld"/>): lines and boxes answered
/// by <see cref="Shot"/> (nothing hit unless a fact says), every hit, launch and sound kept, the
/// projectiles it launched alive until a fact removes them, and a refusal to launch on demand.
/// </summary>
internal sealed class RigAttackWorld : IRobotAttackWorld
{
    private uint _next = 3000;

    /// <summary>What a line from, to (the shooter ignored) meets; by default nothing.</summary>
    public Func<Vector, Vector, TraceResult> Shot { get; set; } = (_, to) => new TraceResult(false, 1f, to, default, EntityHandle.None);

    /// <summary>What a swing's box meets; by default as the line does.</summary>
    public Func<Vector, Vector, TraceResult>? Swept { get; set; }

    public HashSet<EntityHandle> Players { get; } = [];
    public HashSet<EntityHandle> Alive { get; } = [];
    public bool RefuseLaunch { get; set; }

    public List<(EntityHandle Victim, EntityHandle Attacker, float Damage, DamageType Bits, Vector Direction)> Hurts { get; } = [];
    public List<(string Class, EntityHandle Owner, Vector Origin, Vector Velocity, float Damage, int Team, EntityHandle Made)> Launches { get; } = [];
    public List<(EntityHandle Entity, string Sound)> Sounds { get; } = [];
    public List<(Vector From, Vector To)> Lines { get; } = [];

    public TraceResult Line(Vector from, Vector to, EntityHandle self)
    {
        Lines.Add((from, to));
        return Shot(from, to);
    }

    public TraceResult Box(Vector from, Vector to, float half, EntityHandle self) => (Swept ?? Shot)(from, to);

    public bool IsPlayer(EntityHandle entity) => Players.Contains(entity);

    public bool Exists(EntityHandle entity) => Alive.Contains(entity);

    public int Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector direction)
    {
        Hurts.Add((victim, attacker, damage, bits, direction));
        return (int)MathF.Ceiling(damage);
    }

    public EntityHandle Launch(string className, EntityHandle owner, Vector origin, Vector velocity, float damage, int team)
    {
        if (RefuseLaunch)
            return EntityHandle.None;
        var made = new EntityHandle((1u << 16) | _next++);
        Alive.Add(made);
        Launches.Add((className, owner, origin, velocity, damage, team, made));
        return made;
    }

    public void Sound(EntityHandle entity, string sound) => Sounds.Add((entity, sound));

    /// <summary>The attack gesture every model has: sequence 17, half a second; -1 for none.</summary>
    public (int Sequence, float Seconds) Gestured { get; set; } = (17, 0.5f);

    public (int Sequence, float Seconds) Gesture(EntityHandle entity, string activity) => Gestured;

    /// <summary>A line that meets <paramref name="victim"/> wherever it is aimed.</summary>
    public static Func<Vector, Vector, TraceResult> Meets(EntityHandle victim) => (_, to) => new TraceResult(true, 0.5f, to, default, victim);
}

/// <summary>The motion's reports as a fact sets them: a look a body holds now.</summary>
internal sealed class RigMotionReports : IRobotMotionReports
{
    public Dictionary<int, LookIntent> Looks { get; } = [];

    public bool TryView(int body, out RobotMotionView view)
    {
        view = Looks.TryGetValue(body, out var look)
            ? new RobotMotionView(default, default, true, 0f, true, MoveIntent.Hold, look, long.MaxValue, 0, default, default, default, 0)
            : default;
        return Looks.ContainsKey(body);
    }

    public TF2.Descent.Ai.Motion.BlockedReport Blocked(int body) => default;

    public TF2.Descent.Ai.Motion.LandedReport Landed(int body) => default;
}

/// <summary>
/// The horde's hands over the server horde rig (<see cref="ServerHordeRig"/>): a real controller,
/// real bodies and roster, and <see cref="RobotAttacks"/> over a fake engine (<see cref="RigAttackWorld"/>)
/// and fake heroes, ticked by <see cref="Tick"/> before any ask a fact makes.
/// </summary>
internal sealed class AttackRig
{
    public AttackRig()
    {
        Horde = new ServerHordeRig();
        Clock = new RigClock(Horde);
        Attacks = new RobotAttacks(new RigRules(), World, Horde.Heroes, Motion, Clock);
        Attacks.Begin(Horde.Controller, Horde.Now);
    }

    public ServerHordeRig Horde { get; }
    public RigClock Clock { get; }
    public RigAttackWorld World { get; } = new();
    public RigMotionReports Motion { get; } = new();
    public RobotAttacks Attacks { get; }
    public long Now => Horde.Now;
    public RobotRoster Roster => Horde.Roster;

    /// <summary>A hero entity the lines can meet.</summary>
    public static EntityHandle HeroEntity => new(1);

    /// <summary>A robot of <paramref name="template"/> lent a body, facing +x, level.</summary>
    public (RobotId Id, RobotBody Body) Robot(string template, double scale = 1.0)
    {
        var (id, body) = Horde.Lent(template, scale);
        body.MoveTo(Centre(0), 0f);
        return (id, body);
    }

    /// <summary>A second robot in the first's pack, lent a body <paramref name="offset"/> away.</summary>
    public (RobotId Id, RobotBody Body) Mate(string template, AiVector offset)
    {
        var at = Centre(0) + offset;
        var id = Horde.Roster.Add(template, 1.0, 0, new TF2.Descent.Ai.Level.PackMarker(at, 0f), 77, joinLastPack: true);
        if (!Horde.Controller.LendByHand(id.Index, Horde.Now, out string why))
            throw new InvalidOperationException(why);
        var body = Horde.Bodies.Body(Horde.Roster.Body(id.Index));
        body.MoveTo(at, 0f);
        return (id, body);
    }

    /// <summary>One server tick of the hands (windups, beams, the projectile count, dev asks), the clock moved on first.</summary>
    public void Tick(int ticks = 1)
    {
        for (int t = 0; t < ticks; t++)
            Attacks.Tick(++Horde.Now);
    }

    public void Fire(RobotBody body) => Attacks.Act(body.Index, new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0));

    public void Heal(RobotBody body) => Attacks.Act(body.Index, new ActionIntent(ActionKind.Ability, FireStyle.Tap, TF2.Descent.Ai.Brains.HordeAbilities.Heal, 0));
}
