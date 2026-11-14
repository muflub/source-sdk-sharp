using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// <c>tf2_descent_horde</c>: the horde controller (plan_descent_logic L1, 3.2,
/// HO2). A logical entity, so it takes no edict; one a level, made at level
/// ready by <see cref="HordeService"/>. For the level's life it owns the
/// roster, the body pool, the bodies and their weapons, the embodiment rules,
/// the order applier, the far simulation and the heroes' sight, and runs them
/// from <see cref="Tick"/>.
///
/// <para>
/// <b>The tick</b>, on the game thread, every server tick: the dev walkers
/// step; at <see cref="DecideHz"/> the bodies' places go to the roster, the
/// heroes are gathered, and the embodiment rules observe, decide and are
/// applied; at horde.json's far rate (2 Hz) the far simulation ticks. Every
/// call the later session package (HO4) moves to the AI workers is in
/// <see cref="Tick"/> and nowhere else. No thread is started here.
/// </para>
/// </summary>
[ScriptedEntity(EntityClass, ScriptedEntityKind.Logical)]
public sealed class HordeController(IHordeRules rules, IHordeSpawner spawner, HordeParking parking, IRobotHitSink hits,
                                    IEngineEntityRender render, ISightWorld sightWorld, IHordeHeroes heroes, ILog log) : ScriptedEntity
{
    public const string EntityClass = "tf2_descent_horde";

    /// <summary>Embodiment decisions a second.</summary>
    public const double DecideHz = 10;

    /// <summary>Heroes sight can hold: every player slot.</summary>
    public const int MaxHeroes = 33;

    private readonly HeroPlace[] _places = new HeroPlace[MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[MaxHeroes];

    private EmbodimentRules? _embodiment;
    private HordeOrders? _orders;
    private HordeOrderApplier? _applier;
    private FarSimulation? _far;
    private long _decideInterval, _farInterval, _nextDecide, _nextFar;
    private bool[] _walking = [];
    private AiVector[] _walkHome = [];

    // The dev cycle: one lend or one return a tick.
    private int _cycleRow = -1, _cycleLeft;

    public bool Begun { get; private set; }

    public HordeLevel? Level { get; private set; }

    public RobotRoster? Roster { get; private set; }

    public RosterRoll? Roll { get; private set; }

    public BodyPool? Pool { get; private set; }

    public RobotBodies? Bodies { get; private set; }

    public HeroSight? Sight { get; private set; }

    public HordeSettings? Settings { get; private set; }

    /// <summary>Bodies and weapons this controller made: all at <see cref="Begin"/>, never after.</summary>
    public int BodiesMade { get; private set; }

    public int WeaponsMade { get; private set; }

    public long Ticks { get; private set; }

    public long Decisions { get; private set; }

    public long FarTicks { get; private set; }

    /// <summary>Heroes the last decision saw.</summary>
    public int Heroes { get; private set; }

    /// <summary>Dev lends and returns done by hand or by the cycle.</summary>
    public long HandLends { get; private set; }

    public long HandReturns { get; private set; }

    /// <summary>
    /// The level's horde: the roster rolled for <paramref name="partySize"/>,
    /// the pool's bodies and weapons made and parked, the rules built over
    /// <paramref name="level"/> at <paramref name="ticksPerSecond"/>. Answers
    /// false, and makes nothing more, when the engine refuses an entity.
    /// </summary>
    public bool Begin(HordeLevel level, double ticksPerSecond, int partySize, long now)
    {
        if (Begun)
            throw new InvalidOperationException("the horde controller has begun already: one a level");

        var r = rules.Rules;
        var settings = r.Content.Horde;
        Settings = settings;
        Level = level;
        Roster = RobotRoster.Create(r, ticksPerSecond);
        Roll = Roster.Load(level, partySize);
        Pool = BodyPool.For(settings);

        var bodies = new RobotBody[Pool.LastBody + 1];
        var weapons = new RobotWeapon[Pool.LastBody + 1];
        for (int b = 1; b <= Pool.LastBody; b++)
        {
            var body = spawner.Body();
            if (body is null)
            {
                log.Warning($"[TF2Descent/Horde] the engine refused robot body {b} of {Pool.LastBody}: no horde this level\n");
                return false;
            }

            BodiesMade++;
            var weapon = spawner.Weapon(body);
            if (weapon is null)
            {
                log.Warning($"[TF2Descent/Horde] the engine refused robot weapon {b} of {Pool.LastBody}: no horde this level\n");
                return false;
            }

            WeaponsMade++;
            bodies[b] = body;
            weapons[b] = weapon;
        }

        Bodies = new RobotBodies(bodies, Roster, level, r, parking, hits, render);
        for (int b = 1; b <= Pool.LastBody; b++)
            bodies[b].Join(Bodies, b, weapons[b]);
        Bodies.ParkAll();

        Sight = HeroSight.For(sightWorld, level, MaxHeroes);
        _embodiment = EmbodimentRules.For(settings, level, ticksPerSecond);
        _orders = HordeOrders.For(settings, level);
        _applier = new HordeOrderApplier(Roster, Pool, Bodies);
        _far = FarSimulation.For(settings, level, ticksPerSecond);
        _decideInterval = Math.Max(1, (long)Math.Round(ticksPerSecond / DecideHz));
        _farInterval = FarSimulation.Interval(settings, ticksPerSecond);
        _nextDecide = now;
        _nextFar = now;
        _walking = new bool[Pool.LastBody + 1];
        _walkHome = new AiVector[Pool.LastBody + 1];
        Begun = true;
        return true;
    }

    /// <summary>Server ticks between decisions.</summary>
    public long DecideInterval => _decideInterval;

    /// <summary>Server ticks between far ticks.</summary>
    public long FarInterval => _farInterval;

    /// <summary>One server tick of the horde. See the class remarks for what runs when.</summary>
    public void Tick(long now)
    {
        if (!Begun)
            return;

        Ticks++;
        StepWalkers();
        StepCycle(now);

        bool decide = now >= _nextDecide, far = now >= _nextFar;
        if (!decide && !far)
            return;

        int n = heroes.Gather(Level!, _places, _eyes);
        Heroes = n;
        Sight!.BeginTick(_eyes.AsSpan(0, n));
        ReadOnlySpan<HeroPlace> places = _places.AsSpan(0, n);

        if (decide)
        {
            _nextDecide = now + _decideInterval;
            Bodies!.Sync();
            _embodiment!.Observe(Roster!, places, Sight, now);
            _embodiment.Decide(Roster!, Pool!, places, Sight, now, _orders!);
            _applier!.Apply(_orders!, now);
            Decisions++;
        }

        if (far)
        {
            _nextFar = now + _farInterval;
            _far!.Tick(Roster!, Sight, ReadOnlySpan<SoundEvent>.Empty, now);
            FarTicks++;
        }
    }

    /// <summary>The level is going: drops everything it owns. The engine removes the entities themselves with the level.</summary>
    public void End()
    {
        Begun = false;
        Level = null;
        Roster = null;
        Roll = null;
        Pool = null;
        Bodies = null;
        Sight = null;
        _embodiment = null;
        _orders = null;
        _applier = null;
        _far = null;
        _walking = [];
        _walkHome = [];
        _cycleRow = -1;
        _cycleLeft = 0;
    }

    //-------------------------------------------------------------------------
    // By hand (the dev commands)
    //-------------------------------------------------------------------------

    /// <summary>Lends robot row <paramref name="row"/> a free body where it stands, as a promotion would, waking its room first if it sleeps.</summary>
    public bool LendByHand(int row, long now, out string why)
    {
        if (!Ready(row, out why))
            return false;

        var roster = Roster!;
        var pool = Pool!;
        var state = roster.State(row);
        if (state == RobotState.Asleep)
            roster.WakeRoom(roster.Room(row));
        if (roster.State(row) != RobotState.Far)
        {
            why = $"robot {row} is {roster.State(row)}: only a far or sleeping robot takes a body";
            return false;
        }

        int body = roster.IsBoss(row) ? (pool.BossBody != 0 && pool.IsFree(pool.BossBody) ? pool.BossBody : 0) : FirstFree();
        if (body == 0)
        {
            why = "no body is free";
            return false;
        }

        var id = roster.Id(row);
        var lease = pool.Lend(body, id);
        var start = roster.Promote(row, lease, now);
        Bodies!.Begin(lease, id, start);
        HandLends++;
        why = $"robot {row} ({roster.Template(row)}) took body {body}";
        return true;
    }

    /// <summary>Takes robot row <paramref name="row"/>'s body back, as a demotion would.</summary>
    public bool ReturnByHand(int row, long now, out string why)
    {
        if (!Ready(row, out why))
            return false;

        var roster = Roster!;
        var lease = roster.Lease(row);
        if (!lease.IsValid)
        {
            why = $"robot {row} holds no body";
            return false;
        }

        var id = roster.Id(row);
        var end = Bodies!.End(lease, id);
        Pool!.Return(roster.Demote(row, end, now));
        _walking[lease.Body] = false;
        HandReturns++;
        why = $"robot {row} gave body {lease.Body} back";
        return true;
    }

    /// <summary>
    /// A new robot of <paramref name="template"/> on the roster at
    /// <paramref name="at"/>, in the room holding it (the nearest room when
    /// none does), lent a body at once.
    /// </summary>
    public RobotId SpawnByHand(string template, double? scale, AiVector at, float yaw, ulong seed, long now, out string why)
    {
        if (!Begun)
        {
            why = "no horde this level";
            return RobotId.None;
        }

        if (!rules.Rules.Content.Robots.Contains(template))
        {
            why = $"'{template}' is not a robot template";
            return RobotId.None;
        }

        int room = NearestRoom(at);
        var id = Roster!.Add(template, scale, room, new PackMarker(at, yaw), seed);
        if (!id.IsValid)
        {
            why = "the roster is full";
            return RobotId.None;
        }

        if (!LendByHand(id.Index, now, out why))
            return id;
        return id;
    }

    /// <summary>Starts or stops the dev walk of the body robot row <paramref name="row"/> holds: a 48-unit circle round where it stands.</summary>
    public bool Walk(int row, bool on, out string why)
    {
        if (!Ready(row, out why))
            return false;

        int body = Roster!.Body(row);
        if (body == 0)
        {
            why = $"robot {row} holds no body";
            return false;
        }

        _walking[body] = on;
        _walkHome[body] = Bodies!.Body(body).Position;
        why = $"robot {row} (body {body}) {(on ? "walks" : "stands")}";
        return true;
    }

    /// <summary>Walks or stops every lent body; answers how many.</summary>
    public int WalkAll(bool on)
    {
        if (!Begun)
            return 0;

        int n = 0;
        for (int b = 1; b < _walking.Length; b++)
        {
            var body = Bodies!.Body(b);
            if (body.Parked)
                continue;
            _walking[b] = on;
            _walkHome[b] = body.Position;
            n++;
        }

        return n;
    }

    /// <summary>Lends and returns robot row <paramref name="row"/>'s body <paramref name="cycles"/> times, one step a tick.</summary>
    public bool Cycle(int row, int cycles, out string why)
    {
        if (!Ready(row, out why))
            return false;

        _cycleRow = row;
        _cycleLeft = 2 * cycles;
        why = $"robot {row}: {cycles} lends and returns over the next {2 * cycles} ticks";
        return true;
    }

    /// <summary>Whether a cycle is still running.</summary>
    public bool Cycling => _cycleLeft > 0;

    private void StepCycle(long now)
    {
        if (_cycleLeft <= 0 || _cycleRow < 0)
            return;

        _cycleLeft--;
        bool ok = Roster!.Body(_cycleRow) != 0 ? ReturnByHand(_cycleRow, now, out string why) : LendByHand(_cycleRow, now, out why);
        if (!ok)
        {
            log.Warning($"[TF2Descent/Horde] cycle stopped: {why}\n");
            _cycleLeft = 0;
        }
    }

    private void StepWalkers()
    {
        for (int b = 1; b < _walking.Length; b++)
        {
            if (!_walking[b])
                continue;

            var body = Bodies!.Body(b);
            if (body.Parked)
            {
                _walking[b] = false;
                continue;
            }

            float a = Ticks * 0.03f + b * 0.37f;
            var home = _walkHome[b];
            body.MoveTo(new AiVector(home.X + 48f * MathF.Cos(a), home.Y + 48f * MathF.Sin(a), home.Z), a * (180f / MathF.PI) + 90f);
        }
    }

    private int FirstFree()
    {
        var pool = Pool!;
        for (int b = 1; b <= pool.PoolBodies; b++)
            if (pool.IsFree(b))
                return b;
        return 0;
    }

    private int NearestRoom(AiVector at)
    {
        var level = Level!;
        int room = level.RoomAt(at);
        if (room >= 0)
            return room;

        float best = float.MaxValue;
        for (int r = 0; r < level.RoomCount; r++)
        {
            var c = (level.Rooms[r].Min + level.Rooms[r].Max) * 0.5f;
            float d = c.DistanceTo(at);
            if (d < best)
            {
                best = d;
                room = r;
            }
        }

        return Math.Max(room, 0);
    }

    private bool Ready(int row, out string why)
    {
        if (!Begun)
        {
            why = "no horde this level";
            return false;
        }

        if ((uint)row >= (uint)Roster!.Count)
        {
            why = $"no robot {row}: the roster has {Roster.Count}";
            return false;
        }

        why = "";
        return true;
    }
}
