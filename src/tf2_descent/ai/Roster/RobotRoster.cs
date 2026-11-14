using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Director;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Roster;

/// <summary>A pack the roll left out because the roster had no room for all of it.</summary>
public readonly record struct DroppedPack(int Room, int Robots);

/// <summary>
/// What a roll put on the roster: robots and packs, and every pack dropped for want of room.
/// <see cref="Summary"/> is the line the server logs; it names each dropped pack.
/// </summary>
public sealed record RosterRoll(int Robots, int Packs, ImmutableArray<DroppedPack> Dropped)
{
    public string Summary => Dropped.IsEmpty
        ? $"horde roster: {Robots} robots in {Packs} packs"
        : $"horde roster: {Robots} robots in {Packs} packs; DROPPED {Dropped.Length} packs for want of room: "
          + string.Join(", ", Dropped.Select(d => $"room {d.Room} ({d.Robots} robots)"));
}

/// <summary>
/// Every robot of a level as data (plan_descent_logic L1, 3.2, 3.3): struct-of-arrays at a fixed
/// capacity (horde.json), allocated once by <see cref="Create"/>. A level's robots are rolled
/// once by <see cref="Load"/>; after it, nothing here allocates: a robot's statuses are a run
/// of <see cref="StatusSlot"/>s (one a status.json row) that <see cref="StatusBook"/> applies
/// and ticks in place. Only the readers that hand out a status value (<see cref="Statuses"/>,
/// <see cref="BodyState"/>) allocate it.
///
/// <para>
/// A row holds identity (index, serial, template, seed, pack, level), what Rules rolled (scale,
/// stats with life and resistances, temperament, champion affixes), live state (state, room,
/// position, yaw, life, statuses with their end ticks, clip, body), and the memory carried
/// across a demotion (<see cref="CarriedMemory"/>). Packs and rooms keep a little state too: a
/// pack's alert and last contact, a room's wakefulness.
/// </para>
/// </summary>
public sealed partial class RobotRoster
{
    private readonly TF2DescentRules _rules;
    private readonly double _ticksPerSecond;

    // Identity and the roll.
    private readonly int[] _serial;
    private readonly string[] _template;
    private readonly ulong[] _seed;
    private readonly int[] _pack;
    private readonly int[] _level;
    private readonly double[] _scale;
    private readonly RobotStats[] _stats;
    private readonly Temperament[] _temperament;
    private readonly ImmutableArray<string>[] _affixes;
    private readonly bool[] _boss;
    private readonly int[] _homeRoom;
    private readonly int[] _homeMarker;
    private readonly AiVector[] _post;      // the row's own place round its home marker (HO9), and its facing there
    private readonly float[] _postYaw;

    // Live.
    private readonly RobotState[] _state;
    private readonly int[] _room;
    private readonly AiVector[] _position;
    private readonly float[] _yaw;
    private readonly double[] _life;
    private readonly StatusBook _book;
    private readonly int _slotsPerRow;      // the book's rows
    private readonly StatusSlot[] _slots;   // _slotsPerRow a robot
    private readonly StatusDue[] _due;      // one tick's damage, for the far path
    private long _statusStamp;              // the order statuses were applied in, across the roster
    private readonly long[] _statusDue;     // the next tick a status ends or deals damage; long.MaxValue for none
    private readonly float[] _speedFactor;  // status slow, kept from the last status change
    private readonly float[] _damageTaken;  // status damage-taken multiplier (marked), kept from the last status change
    private readonly bool[] _stunned;
    private readonly int[] _tauntSeat;      // the hero a taunt makes the robot target; -1 for none
    private readonly long[] _tauntUntil;
    private readonly double[] _weakenPct;   // a debuff on what the robot deals, in percent
    private readonly long[] _weakenUntil;
    private readonly int[] _clip;
    private readonly int[] _body;
    private readonly int[] _bodySerial;

    // Carried across a demotion.
    private readonly int[] _targetSeat;
    private readonly AiVector[] _lastKnown;
    private readonly long[] _lastKnownTick;
    private readonly AlertLevel[] _alert;
    private readonly int[] _threatSeat;
    private readonly float[] _threat;

    // The far simulation's and the embodiment rules' own bookkeeping.
    private readonly int[] _patrolMarker;
    private readonly long[] _dwellUntil;
    private readonly FarActivity[] _activity;
    private readonly long[] _lastNear;

    // Packs: one per room with a pack, and the boss's.
    private readonly int[] _packRoom;
    private readonly int[] _packFirst;
    private readonly int[] _packSize;
    private readonly ulong[] _packSeed;
    private readonly AlertLevel[] _packAlert;
    private readonly int[] _contactRoom;
    private readonly AiVector[] _contact;
    private readonly long[] _contactTick;
    private readonly int[] _contactSeat;

    // Rooms, sized by the level at load.
    private bool[] _roomAwake = [];
    private long[] _roomLastNear = [];

    private readonly SpawnDirector _director;

    private RobotRoster(TF2DescentRules rules, double ticksPerSecond, int capacity)
    {
        _rules = rules;
        _director = new SpawnDirector(rules);
        _ticksPerSecond = ticksPerSecond;
        _serial = new int[capacity];
        _template = new string[capacity];
        _seed = new ulong[capacity];
        _pack = new int[capacity];
        _level = new int[capacity];
        _scale = new double[capacity];
        _stats = new RobotStats[capacity];
        _temperament = new Temperament[capacity];
        _affixes = new ImmutableArray<string>[capacity];
        _boss = new bool[capacity];
        _homeRoom = new int[capacity];
        _homeMarker = new int[capacity];
        _post = new AiVector[capacity];
        _postYaw = new float[capacity];
        _state = new RobotState[capacity];
        _room = new int[capacity];
        _position = new AiVector[capacity];
        _yaw = new float[capacity];
        _life = new double[capacity];
        _book = rules.StatusBook;
        _slotsPerRow = _book.Count;
        _slots = new StatusSlot[capacity * _slotsPerRow];
        _due = new StatusDue[_slotsPerRow];
        _statusDue = new long[capacity];
        _speedFactor = new float[capacity];
        _damageTaken = new float[capacity];
        _stunned = new bool[capacity];
        _tauntSeat = new int[capacity];
        _tauntUntil = new long[capacity];
        _weakenPct = new double[capacity];
        _weakenUntil = new long[capacity];
        _clip = new int[capacity];
        _body = new int[capacity];
        _bodySerial = new int[capacity];
        _targetSeat = new int[capacity];
        _lastKnown = new AiVector[capacity];
        _lastKnownTick = new long[capacity];
        _alert = new AlertLevel[capacity];
        _threatSeat = new int[capacity];
        _threat = new float[capacity];
        _patrolMarker = new int[capacity];
        _dwellUntil = new long[capacity];
        _activity = new FarActivity[capacity];
        _lastNear = new long[capacity];
        _packRoom = new int[capacity];
        _packFirst = new int[capacity];
        _packSize = new int[capacity];
        _packSeed = new ulong[capacity];
        _packAlert = new AlertLevel[capacity];
        _contactRoom = new int[capacity];
        _contact = new AiVector[capacity];
        _contactTick = new long[capacity];
        _contactSeat = new int[capacity];
    }

    /// <summary>An empty roster at horde.json's capacity, for a server ticking at <paramref name="ticksPerSecond"/>.</summary>
    public static RobotRoster Create(TF2DescentRules rules, double ticksPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        return new RobotRoster(rules, ticksPerSecond, rules.Content.Horde.RosterCapacity);
    }

    public int Capacity => _state.Length;

    /// <summary>Rows in use, 0 to <see cref="Count"/> - 1.</summary>
    public int Count { get; private set; }

    public int PackCount { get; private set; }

    /// <summary>The level the roster was last loaded for.</summary>
    public HordeLevel? Level { get; private set; }

    public double TicksPerSecond => _ticksPerSecond;

    //-------------------------------------------------------------------------
    // The roll
    //-------------------------------------------------------------------------

    /// <summary>
    /// Rolls the level's robots (plan_descent_logic 3.3), once at level load: the spawn
    /// director's plan for (level seed, depth, party size, the level's rooms, markers,
    /// clearances and doors) (<see cref="SpawnDirector.Plan"/>), put on the roster by
    /// <see cref="Load(LevelPlan, HordeLevel)"/>.
    /// </summary>
    public RosterRoll Load(HordeLevel level, int partySize) => Load(_director.Plan(level, partySize), level);

    /// <summary>
    /// Puts the spawn director's <paramref name="plan"/> for <paramref name="level"/> on the
    /// roster, pack by pack in its order (the boss's first), each robot at its own post facing its
    /// marker's way. The plan decides; this only writes rows. Every row starts asleep, at full
    /// life, with no statuses, a full clip, no body and calm memory. Each loaded row's serial moves
    /// on, so an id from an earlier load is stale. The packs the plan dropped for want of room are
    /// the result's.
    /// </summary>
    public RosterRoll Load(LevelPlan plan, HordeLevel level)
    {
        if (plan.Robots > Capacity)
            throw new ArgumentException($"the plan holds {plan.Robots} robots; the roster {Capacity}", nameof(plan));
        Level = level;
        Array.Fill(_state, RobotState.Empty);
        Count = 0;
        PackCount = 0;
        _roomAwake = new bool[level.RoomCount];
        _roomLastNear = new long[level.RoomCount];
        Array.Fill(_roomLastNear, long.MinValue);

        foreach (var pack in plan.Packs)
        {
            int p = BeginPack(pack.Room, pack.Seed);
            foreach (var r in pack.Robots)
                Put(p, r.Roll.Template, r.Seed, r.Roll.Stats.Level, r.Roll.Scale, r.Roll.Stats, r.Roll.Temperament, r.Roll.Affixes, r.Boss,
                    r.Room, r.Marker, new PackMarker(r.Post, r.Yaw));
        }
        return new RosterRoll(Count, PackCount, plan.Dropped);
    }

    /// <summary>The roster's rows as the director's plan names them (<see cref="LevelPlan.Rows"/>), so a roster can be checked against its replay.</summary>
    public IEnumerable<RosterRow> Rows()
    {
        for (int i = 0; i < Count; i++)
            yield return new RosterRow(_pack[i], _homeRoom[i], _homeMarker[i], _template[i], _scale[i], _affixes[i], _temperament[i].Style,
                _stats[i].Life, _post[i], _postYaw[i], _boss[i], _seed[i]);
    }

    private int BeginPack(int room, ulong seed)
    {
        int p = PackCount++;
        _packRoom[p] = room;
        _packFirst[p] = Count;
        _packSize[p] = 0;
        _packSeed[p] = seed;
        _packAlert[p] = AlertLevel.Calm;
        _contactRoom[p] = -1;
        _contact[p] = default;
        _contactTick[p] = 0;
        _contactSeat[p] = -1;
        return p;
    }

    private void Put(int pack, string template, ulong seed, int level, double scale, RobotStats stats, Temperament temperament,
        ImmutableArray<string> affixes, bool boss, int room, int marker, PackMarker at)
    {
        int i = Count++;
        _packSize[pack]++;
        _serial[i]++;
        _template[i] = template;
        _seed[i] = seed;
        _pack[i] = pack;
        _level[i] = level;
        _scale[i] = scale;
        _stats[i] = stats;
        _temperament[i] = temperament;
        _affixes[i] = affixes;
        _boss[i] = boss;
        _homeRoom[i] = room;
        _homeMarker[i] = marker;
        _post[i] = at.Position;
        _postYaw[i] = at.Yaw;
        _state[i] = RobotState.Asleep;
        _room[i] = room;
        _position[i] = at.Position;
        _yaw[i] = at.Yaw;
        _life[i] = stats.Life;
        ClearStatusRow(i);
        _tauntSeat[i] = -1;
        _tauntUntil[i] = 0;
        _weakenPct[i] = 0;
        _weakenUntil[i] = 0;
        _clip[i] = -1;
        _body[i] = 0;
        _bodySerial[i] = 0;
        var calm = CarriedMemory.None;
        _targetSeat[i] = calm.TargetSeat;
        _lastKnown[i] = calm.LastKnownPosition;
        _lastKnownTick[i] = calm.LastKnownTick;
        _alert[i] = calm.Alert;
        _threatSeat[i] = calm.ThreatSeat;
        _threat[i] = calm.Threat;
        _patrolMarker[i] = Math.Max(0, marker);
        _dwellUntil[i] = 0;
        _activity[i] = FarActivity.Hold;
        _lastNear[i] = long.MinValue;
    }

    //-------------------------------------------------------------------------
    // Rows
    //-------------------------------------------------------------------------

    public RobotId Id(int i) => new(Row(i), _serial[i]);

    /// <summary>Whether <paramref name="id"/> still names the robot it was taken for.</summary>
    public bool IsLive(RobotId id) =>
        id.IsValid && (uint)id.Index < (uint)Count && _serial[id.Index] == id.Serial && _state[id.Index] != RobotState.Empty;

    public RobotState State(int i) => _state[Row(i)];
    public string Template(int i) => _template[Row(i)];
    public ulong Seed(int i) => _seed[Row(i)];
    public int Pack(int i) => _pack[Row(i)];
    public int MonsterLevel(int i) => _level[Row(i)];
    public double Scale(int i) => _scale[Row(i)];
    public RobotStats Stats(int i) => _stats[Row(i)];
    public Temperament Temperament(int i) => _temperament[Row(i)];
    public ImmutableArray<string> Affixes(int i) => _affixes[Row(i)];
    public bool IsChampion(int i) => !_affixes[Row(i)].IsEmpty;
    public bool IsBoss(int i) => _boss[Row(i)];
    public int HomeRoom(int i) => _homeRoom[Row(i)];

    /// <summary>The index of the home room marker the robot was rolled at; -1 for the boss, who stands at its own spot.</summary>
    public int HomeMarker(int i) => _homeMarker[Row(i)];

    /// <summary>
    /// The robot's post (plan_descent_logic HO9): its own place round its home marker, where it
    /// was rolled to stand, apart from its pack-mates; the boss's spot for the boss; where a dev
    /// command put it for one of those. Its home for the brains and the far simulation.
    /// </summary>
    public AiVector Post(int i) => _post[Row(i)];

    /// <summary>The way the robot faces at its post: its marker's.</summary>
    public float PostYaw(int i) => _postYaw[Row(i)];

    public int Room(int i) => _room[Row(i)];
    public AiVector Position(int i) => _position[Row(i)];
    public float Yaw(int i) => _yaw[Row(i)];
    public double Life(int i) => _life[Row(i)];
    /// <summary>The robot's statuses as the rules' value, in the order they were applied. Allocates: a reader's, not the tick's.</summary>
    public RobotStatuses Statuses(int i) => _book.View(SlotsOf(Row(i)));

    /// <summary>A bit a status the robot carries, by status.json row (the body's networked flags).</summary>
    public int StatusBits(int i) => StatusBook.Bits(SlotsOf(Row(i)));

    /// <summary>The robot's slot of status.json row <paramref name="row"/>: its stacks, end, next damage tick and applier.</summary>
    public StatusSlot StatusSlot(int i, int row) => SlotsOf(Row(i))[row];
    public int Clip(int i) => _clip[Row(i)];

    /// <summary>The body the robot holds, 0 for none.</summary>
    public int Body(int i) => _body[Row(i)];

    /// <summary>The lease of the body the robot holds; not valid when it holds none.</summary>
    public BodyLease Lease(int i) => new(_body[Row(i)], _bodySerial[i]);

    public CarriedMemory Carried(int i)
    {
        Row(i);
        return new(_targetSeat[i], _lastKnown[i], _lastKnownTick[i], _alert[i], _threatSeat[i], _threat[i]);
    }

    /// <summary>The far simulation's last decision for the robot.</summary>
    public FarActivity Activity(int i) => _activity[Row(i)];

    /// <summary>The status slow on the robot's speed, as of its last status change (a stun is 0).</summary>
    public float SpeedFactor(int i) => _speedFactor[Row(i)];

    public bool Stunned(int i) => _stunned[Row(i)];

    /// <summary>The status multiplier on damage the robot takes (marked), as of its last status change.</summary>
    public float DamageTakenFactor(int i) => _damageTaken[Row(i)];

    /// <summary>The next tick one of the robot's statuses ends or deals damage; <see cref="long.MaxValue"/> with none.</summary>
    public long StatusDue(int i) => _statusDue[Row(i)];

    /// <summary>The hero seat a taunt makes the robot target at <paramref name="now"/>; -1 for none.</summary>
    public int TauntSeat(int i, long now) => _tauntUntil[Row(i)] > now ? _tauntSeat[i] : -1;

    /// <summary>The tick the robot's taunt ends (0 for none).</summary>
    public long TauntUntil(int i) => _tauntUntil[Row(i)];

    /// <summary>The debuff on what the robot deals at <paramref name="now"/>, in percent (0 for none).</summary>
    public double WeakenPct(int i, long now) => _weakenUntil[Row(i)] > now ? _weakenPct[i] : 0;

    /// <summary>The tick the robot's debuff on what it deals ends (0 for none).</summary>
    public long WeakenUntil(int i) => _weakenUntil[Row(i)];

    /// <summary>The last tick a hero was within the wake distance of the robot or could see it (<see cref="long.MinValue"/> for never).</summary>
    public long LastNear(int i) => _lastNear[Row(i)];

    //-------------------------------------------------------------------------
    // Packs and rooms
    //-------------------------------------------------------------------------

    public int PackRoom(int p) => _packRoom[PackRow(p)];
    public int PackFirst(int p) => _packFirst[PackRow(p)];
    public int PackSize(int p) => _packSize[PackRow(p)];
    public ulong PackSeed(int p) => _packSeed[PackRow(p)];
    public AlertLevel PackAlert(int p) => _packAlert[PackRow(p)];
    public int ContactRoom(int p) => _contactRoom[PackRow(p)];
    public AiVector Contact(int p) => _contact[PackRow(p)];
    public long ContactTick(int p) => _contactTick[PackRow(p)];

    /// <summary>The hero seat the pack's last contact came from; -1 when it was not a hero (or there was none).</summary>
    public int ContactSeat(int p) => _contactSeat[PackRow(p)];

    /// <summary>Whether the pack is alerted to a hero (the second rung of the embodiment priority).</summary>
    public bool PackAlertedToHero(int p) => _packAlert[PackRow(p)] == AlertLevel.Alerted && _contactSeat[p] >= 0;

    public bool RoomAwake(int room) => _roomAwake[room];

    /// <summary>The last tick a hero was within the wake distance of <paramref name="room"/> (<see cref="long.MinValue"/> for never).</summary>
    public long RoomLastNear(int room) => _roomLastNear[room];

    /// <summary>
    /// Alerts a pack to a contact at <paramref name="tick"/> (the far simulation's sounds, and the
    /// server's senses for an embodied robot's sight). A later contact replaces an earlier one;
    /// the pack's sleeping robots wake into the far simulation.
    /// </summary>
    public void AlertPack(int p, int room, AiVector position, long tick, int seat)
    {
        PackRow(p);
        if (_packAlert[p] == AlertLevel.Alerted && tick < _contactTick[p]) return;
        _packAlert[p] = AlertLevel.Alerted;
        _contactRoom[p] = room;
        _contact[p] = position;
        _contactTick[p] = tick;
        _contactSeat[p] = seat;
        for (int i = _packFirst[p], end = _packFirst[p] + _packSize[p]; i < end; i++)
            if (_state[i] == RobotState.Asleep) _state[i] = RobotState.Far;
    }

    /// <summary>The pack forgets its contact.</summary>
    public void CalmPack(int p)
    {
        PackRow(p);
        _packAlert[p] = AlertLevel.Calm;
    }

    /// <summary>Wakes a room: its sleeping robots join the far simulation, where the embodiment rules lend them bodies.</summary>
    public void WakeRoom(int room)
    {
        _roomAwake[room] = true;
        for (int i = 0; i < Count; i++)
            if (_state[i] == RobotState.Asleep && _room[i] == room) _state[i] = RobotState.Far;
    }

    /// <summary>Puts a room to sleep: the far robots standing in it whose pack is calm become rows that do not tick.</summary>
    public void SleepRoom(int room)
    {
        _roomAwake[room] = false;
        for (int i = 0; i < Count; i++)
            if (_state[i] == RobotState.Far && _room[i] == room && _packAlert[_pack[i]] == AlertLevel.Calm)
                _state[i] = RobotState.Asleep;
    }

    //-------------------------------------------------------------------------
    // Live changes
    //-------------------------------------------------------------------------

    /// <summary>Where an embodied robot's body is now (the server, each tick).</summary>
    public void SetPose(int i, int room, AiVector position, float yaw)
    {
        Row(i);
        _room[i] = room;
        _position[i] = position;
        _yaw[i] = yaw;
    }

    /// <summary>Takes <paramref name="amount"/> of life, already through the rules; at 0 the robot is dead. Returns whether this hit killed it.</summary>
    public bool Hurt(int i, double amount)
    {
        Row(i);
        if (_state[i] is RobotState.Dead or RobotState.Empty) return false;
        _life[i] = Math.Max(0, _life[i] - amount);
        if (_life[i] > 0) return false;
        _state[i] = RobotState.Dead;
        ClearStatusRow(i);
        return true;
    }

    /// <summary>Applies status <paramref name="id"/> at <paramref name="now"/> for its own duration, by no hero (<see cref="ApplyStatus(int, int, long, double, int)"/>).</summary>
    public StatusApplied ApplyStatus(int i, string id, long now)
    {
        int row = _book.RowOf(id);
        if (row < 0) throw new ArgumentException($"'{id}' is not a status", nameof(id));
        return ApplyStatus(i, row, now, 0, 0);
    }

    /// <summary>
    /// Applies status.json row <paramref name="row"/> at <paramref name="now"/> for
    /// <paramref name="seconds"/> (the row's duration when 0) by hero seat
    /// <paramref name="applier"/> (0 for none), through <see cref="StatusBook.Apply"/>, against the
    /// robot's immunity. A dead robot takes nothing (answers Immune).
    /// </summary>
    public StatusApplied ApplyStatus(int i, int row, long now, double seconds, int applier)
    {
        Row(i);
        if (_state[i] is RobotState.Dead or RobotState.Empty) return StatusApplied.Immune;
        var what = _book.Apply(SlotsOf(i), row, now, _ticksPerSecond, _stats[i].Immunity, seconds, applier, ++_statusStamp);
        Refresh(i, now);
        return what;
    }

    /// <summary>
    /// Runs the robot's statuses up to <paramref name="now"/> (<see cref="StatusBook.Tick"/>) and
    /// writes the damage each owes, before resistance, into <paramref name="due"/> (one entry a
    /// damaging status: <see cref="StatusBook.Count"/> at most), without taking any life: the
    /// caller deals it (a body's hit path). Does nothing, and asks the rules nothing, before the
    /// next tick one is due. Answers how many entries it wrote.
    /// </summary>
    public int RunStatuses(int i, long now, Span<StatusDue> due)
    {
        Row(i);
        if (now < _statusDue[i] || _state[i] is RobotState.Dead or RobotState.Empty) return 0;
        int n = _book.Tick(SlotsOf(i), now, _ticksPerSecond, due);
        Refresh(i, now);
        return n;
    }

    /// <summary>
    /// The far path (3.5): runs the robot's statuses up to <paramref name="now"/> and takes the
    /// damage they owe from its life here, each through the robot's resistance and the
    /// damage-taken multiplier in force before the tick (a mark). Does nothing before the next
    /// tick one is due. Returns the damage dealt.
    /// </summary>
    public double TickStatuses(int i, long now)
    {
        Row(i);
        if (now < _statusDue[i] || _state[i] is RobotState.Dead or RobotState.Empty) return 0;
        double mark = _book.DamageTakenMultiplier(SlotsOf(i), now);
        int n = RunStatuses(i, now, _due);
        double dealt = 0;
        for (int k = 0; k < n; k++)
            dealt += RobotRules.DamageTaken(_stats[i], _book.Row(_due[k].Row).DamageType!, _due[k].Amount) * mark;
        if (dealt > 0) Hurt(i, dealt);
        return dealt;
    }

    /// <summary>A taunt (a hero's threat skill): the robot targets hero seat <paramref name="seat"/> until <paramref name="until"/>.</summary>
    public void Taunt(int i, int seat, long until)
    {
        Row(i);
        _tauntSeat[i] = seat;
        _tauntUntil[i] = until;
        _targetSeat[i] = seat;
        _alert[i] = AlertLevel.Alerted;
    }

    /// <summary>A debuff on what the robot deals: <see cref="RobotConditionRules.Weaken"/> against the one in force.</summary>
    public void Weaken(int i, double pct, long until, long now)
    {
        Row(i);
        (_weakenPct[i], _weakenUntil[i]) = RobotConditionRules.Weaken(_weakenPct[i], _weakenUntil[i], pct, until, now);
    }

    private Span<StatusSlot> SlotsOf(int i) => _slots.AsSpan(i * _slotsPerRow, _slotsPerRow);

    private void ClearStatusRow(int i)
    {
        SlotsOf(i).Clear();
        _statusDue[i] = long.MaxValue;
        _speedFactor[i] = 1;
        _damageTaken[i] = 1;
        _stunned[i] = false;
    }

    private void Refresh(int i, long now)
    {
        var slots = SlotsOf(i);
        _statusDue[i] = _book.NextDue(slots);
        _speedFactor[i] = (float)_book.SpeedMultiplier(slots, now);
        _damageTaken[i] = (float)_book.DamageTakenMultiplier(slots, now);
        _stunned[i] = _book.Stunned(slots, now);
    }

    private int Row(int i)
    {
        if ((uint)i >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(i), i, $"the roster has {Count} rows");
        return i;
    }

    private int PackRow(int p)
    {
        if ((uint)p >= (uint)PackCount) throw new ArgumentOutOfRangeException(nameof(p), p, $"the roster has {PackCount} packs");
        return p;
    }
}
