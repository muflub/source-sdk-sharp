using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Director;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Horde;
using TF2.Descent.Server.Horde.Motion;

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
/// <b>The tick</b>, on the game thread, every server tick: the bodies'
/// motion steps (HO3), then the door shove reads its reports (HO11); at <see cref="DecideHz"/> the bodies' places go to the roster, the
/// heroes are gathered, and the embodiment rules observe, decide and are
/// applied; at horde.json's far rate (2 Hz) the far simulation ticks. Every
/// call the later session package (HO4) moves to the AI workers is in
/// <see cref="Tick"/> and nowhere else. No thread is started here.
/// </para>
/// </summary>
[ScriptedEntity(EntityClass, ScriptedEntityKind.Logical)]
public sealed class HordeController(IHordeRules rules, IHordeSpawner spawner, HordeParking parking, IHooks<IRobotHitSink> hits,
                                    IEngineEntityRender render, ISightWorld sightWorld, IHordeHeroes heroes, RobotMotion motion,
                                    IHordeActors actors, ILog log, Session.IHordeBrainHost brains,
                                    Skills.ISkillHits skillHits, Doors.DoorShove shove) : ScriptedEntity
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

    // The dev cycle: one lend or one return a tick.
    private int _cycleRow = -1, _cycleLeft;

    public bool Begun { get; private set; }

    public HordeLevel? Level { get; private set; }

    public RobotRoster? Roster { get; private set; }

    public RosterRoll? Roll { get; private set; }

    /// <summary>The spawn director's plan the roster was loaded from (HO9).</summary>
    public LevelPlan? Plan { get; private set; }

    /// <summary>The party the roster was rolled for: the one present at level ready (D11).</summary>
    public int PartySize { get; private set; }

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
        PartySize = partySize;
        Plan = new SpawnDirector(r).Plan(level, partySize);
        Roll = Roster.Load(Plan, level);
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

        Bodies = new RobotBodies(bodies, Roster, level, r, parking, new RobotHitChain(hits), render, skillHits);
        for (int b = 1; b <= Pool.LastBody; b++)
            bodies[b].Join(Bodies, b, weapons[b]);
        Bodies.ParkAll();
        motion.Begin(new HordeMotionBodies(Bodies, Roster, r, render), actors, MotionSettings.Default, ticksPerSecond);

        Sight = HeroSight.For(sightWorld, level, MaxHeroes);
        _embodiment = EmbodimentRules.For(settings, level, ticksPerSecond);
        _orders = HordeOrders.For(settings, level);
        _applier = new HordeOrderApplier(Roster, Pool, Bodies);
        _far = FarSimulation.For(settings, level, ticksPerSecond);
        _decideInterval = Math.Max(1, (long)Math.Round(ticksPerSecond / DecideHz));
        _farInterval = FarSimulation.Interval(settings, ticksPerSecond);
        _nextDecide = now;
        _nextFar = now;
        Begun = true;
        brains.Begin(this, now);
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
        StepCycle(now);
        brains.Tick(now);
        motion.Tick(now);
        shove.Tick(this, now);   // the door shove reads the motion's Blocked reports (HO11)

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
            _far!.Tick(Roster!, Sight, brains.TakeFarSounds(), now);
            FarTicks++;
        }
    }

    /// <summary>The level is going: drops everything it owns. The engine removes the entities themselves with the level.</summary>
    public void End()
    {
        brains.End();
        motion.End();
        Begun = false;
        Level = null;
        Roster = null;
        Roll = null;
        Plan = null;
        Pool = null;
        Bodies = null;
        Sight = null;
        _embodiment = null;
        _orders = null;
        _applier = null;
        _far = null;
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
        HandReturns++;
        why = $"robot {row} gave body {lease.Body} back";
        return true;
    }

    /// <summary>
    /// A new robot of <paramref name="template"/> on the roster at
    /// <paramref name="at"/>, in the room holding it (the nearest room when
    /// none does), lent a body at once, with the champion
    /// <paramref name="affixes"/> given (none by default); with
    /// <paramref name="joinLastPack"/> in the newest pack rather than a pack of its own.
    /// </summary>
    public RobotId SpawnByHand(string template, double? scale, AiVector at, float yaw, ulong seed, long now, out string why,
                               IReadOnlyList<string>? affixes = null, bool joinLastPack = false)
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

        foreach (string affix in affixes ?? [])
            if (!rules.Rules.Content.Champions.Contains(affix))
            {
                why = $"'{affix}' is not a champion affix";
                return RobotId.None;
            }

        int room = NearestRoom(at);
        RobotId id;
        try
        {
            id = Roster!.Add(template, scale, room, new PackMarker(at, yaw), seed, affixes: affixes, joinLastPack: joinLastPack);
        }
        catch (ArgumentException e)
        {
            why = e.Message;   // two immunities: the rules refuse the robot
            return RobotId.None;
        }
        if (!id.IsValid)
        {
            why = "the roster is full";
            return RobotId.None;
        }

        if (!LendByHand(id.Index, now, out why))
            return id;
        return id;
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
