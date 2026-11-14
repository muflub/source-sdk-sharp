using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;
using TF2.Descent.Rules;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// A level's robot brains on the AI core (plan_descent_logic 3.6, HO4, HO5):
/// one <see cref="AiSession"/> of <see cref="HordeSlots.Slots"/> slots, a brain
/// at each lent body's pool index under the lease's actor, heroes as actors at
/// slots 50 to 53 (<see cref="HeroSlots"/>), the team layer off. The game thread
/// drives it a tick at a time, never waiting on the workers:
///
/// <list type="number">
/// <item><see cref="Heroes"/>: where the heroes are.</item>
/// <item><see cref="Apply"/>: each body's newest intent (made from an earlier
/// snapshot) to its motion: dropped and counted when the body has since gone to
/// another robot or its robot is dead, a hold once it expires, an order when it
/// is new; an action is counted and handed on.</item>
/// <item><see cref="Sync"/>: a brain added for each newly lent body (its seed, its
/// temperament as the core's eleven levels, <see cref="HordeSkillMap"/>), started
/// from the roster's carried memory; removed for a body returned or a robot dead.</item>
/// <item>The senses: <see cref="See"/> (sight), <see cref="Hear"/>, <see cref="Hit"/>,
/// <see cref="HeroDied"/>: events for the brains that perceive them.</item>
/// <item><see cref="Publish"/>: the snapshot, each robot's view filtered to what
/// it may know (a hero only while it holds sight of it: B.3), handed over.</item>
/// </list>
///
/// <para>
/// <b>Carried memory</b> (plan 3.3) lives in the roster row and is kept current
/// from the events this class tells the brain, so a body's return
/// (<c>RobotBodies.End</c> reads the row) carries what the brain was told:
/// its target seat, where and when it last knew the target, its alert, the hero
/// that hurt it most. A brain is started from it by events, the only way into a
/// fresh brain the core's API and its recording have: <c>Spawned</c> at the body,
/// then, for a carried target or alert, a sound heard at the last known place
/// from the target (so a robot that regains a body goes to look there, and has
/// forgotten the detail).
/// </para>
///
/// <para>
/// <b>Events are never dropped here.</b> Each goes only to a brain that holds
/// its slot; the core keeps it for the brain's next step. One the core
/// discards (a brain removed before its next step) is counted as
/// <see cref="EventsDiscarded"/>, so in = processed + discarded + waiting.
/// </para>
/// </summary>
public sealed class HordeBrains : IHordeSightSink
{
    /// <summary>The robots' team and the heroes' as the views give them (TF2's BLU and RED).</summary>
    public const int RobotTeam = 3, HeroTeam = 2;

    /// <summary>How far a robot of scale 1 sees at a sight range scale of 1 (Alertness 5), and how wide either side of its facing, degrees.</summary>
    public const float BaseSightRange = 3000f, BaseHalfConeDeg = 45f;

    /// <summary>A robot's eye above its feet at scale 1 (the hull is 82 high).</summary>
    public const float EyeHeight = 72f;

    /// <summary>How far a robot's pain cry and its death carry, in units of walk, before the hearer's Alertness scales them.</summary>
    public const float PainReach = 1200f, DeathReach = 1800f;

    /// <summary>A body that has moved less than this along its order for <see cref="StuckSeconds"/> is stuck.</summary>
    public const float StuckUnits = 16f, StuckSeconds = 1f;

    /// <summary>A seat away this long may give its hero slot to a new seat.</summary>
    public const float HeroAwaySeconds = 10f;

    private readonly AiSession _session;
    private readonly RobotRoster _roster;
    private readonly BodyPool _pool;
    private readonly HordeLevel _level;
    private readonly TF2DescentRules _rules;
    private readonly HordeSkillMap _map;
    private readonly SkillCurves _curves;
    private readonly HeroSlots _heroes;
    private readonly HordeVision _vision;
    private readonly IHordeBodyPoses _poses;
    private readonly IHordeBodyAim? _aim;
    private readonly IHordePaths? _paths;
    private readonly float _tickSeconds;
    private readonly Body[] _body;
    private readonly Hero[] _hero = new Hero[HordeSlots.Heroes];
    private readonly BodyEye[] _eyes;
    private readonly HeroTarget[] _targets = new HeroTarget[HordeSlots.Heroes];
    private readonly bool[] _gone = new bool[HordeSlots.Heroes];
    private long _now;
    private readonly int[] _added;
    private readonly int[] _mates = new int[HordeView.PackMates];
    private readonly float[] _mateDistance = new float[HordeView.PackMates];
    private int _addedCount;

    public HordeBrains(AiSession session, RobotRoster roster, BodyPool pool, HordeLevel level, TF2DescentRules rules, HordeSkillMap map,
                       SkillCurves curves, HeroSlots heroes, HordeVision vision, IHordeBodyPoses poses, float tickSeconds, Body[] bodies, BodyEye[] eyes,
                       IHordeBodyAim? aim = null, IHordePaths? paths = null)
    {
        _session = session;
        _roster = roster;
        _pool = pool;
        _level = level;
        _rules = rules;
        _map = map;
        _curves = curves;
        _heroes = heroes;
        _vision = vision;
        _poses = poses;
        _aim = aim;
        _paths = paths;
        _tickSeconds = tickSeconds;
        _body = bodies;
        _eyes = eyes;
        _added = new int[bodies.Length];
    }

    /// <summary>
    /// The level's brains: a session over <paramref name="transport"/>, its inputs and
    /// intents to <paramref name="sink"/> (a recorder, or nothing), deciding with
    /// <paramref name="deciders"/>' families, over the embedded curves; the bodies'
    /// guns through <paramref name="aim"/> (none: each view's look is its facing, its
    /// line of fire untraced; see <see cref="HordeView"/>); the level graph's epoch into each view
    /// from <paramref name="paths"/> (none: 0, every robot walks the room route), which should be
    /// the paths <paramref name="deciders"/> plan with.
    /// </summary>
    public static HordeBrains For(RobotRoster roster, BodyPool pool, HordeLevel level, TF2DescentRules rules, IHordeBodyPoses poses,
                                  IAiTransport transport, IAiRecordSink sink, HordeDeciders deciders, double ticksPerSecond,
                                  int sightBudget = HordeVision.DefaultBudget, IHordeBodyAim? aim = null, IHordePaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (pool.LastBody > HordeSlots.LastBody)
            throw new ArgumentException($"the pool's {pool.LastBody} bodies do not fit the session's slots 1 to {HordeSlots.LastBody}", nameof(pool));
        float tick = (float)(1.0 / ticksPerSecond);
        SkillCurves curves = HordeSkillMap.Curves();
        var session = new AiSession(transport, sink, HordeSlots.Slots, HordeSlots.Sighted)
        {
            TeamCallouts = false,
            World = World(tick),
            Curves = curves,
            Deciders = deciders.Make,
        };
        var bodies = new Body[pool.LastBody + 1];
        return new HordeBrains(session, roster, pool, level, rules, HordeSkillMap.For(rules.Temperaments.Skills), curves,
                               new HeroSlots((long)Math.Ceiling(HeroAwaySeconds * ticksPerSecond)), HordeVision.For(pool.LastBody, sightBudget),
                               poses, tick, bodies, new BodyEye[pool.LastBody + 1], aim, paths);
    }

    /// <summary>What the brains remember over: no map knowledge, no nav graph for the core (a decider asks <see cref="IHordePaths"/>, or the room route), the tick.</summary>
    public static MemoryWorld World(float tickSeconds) =>
        new(MapKnowledge.Empty(new WeaponKnowledge([])), null, tickSeconds, id => id.ToString());

    /// <summary>What a recording of these brains names as their world, written before the bots (a replay builds them over it).</summary>
    public static AiRecordedWorld RecordedWorld(float tickSeconds) =>
        new(MapKnowledge.Empty(new WeaponKnowledge([])), tickSeconds, HordeSkillMap.CurvesJson(), 0);

    /// <summary>
    /// A recording's header for a level: the session runs with its team layer off
    /// (<see cref="AiSession.TeamCallouts"/> false, format 8's <see cref="AiRecordingHeader.TeamLayerOff"/>),
    /// so a replay does too. It names no nav graph: the level's graph becomes ready after the
    /// recording begins, and each view names the graph it was published under
    /// (<see cref="HordeView.NavEpoch"/>).
    /// </summary>
    public static AiRecordingHeader Header(HordeLevel level) =>
        new(level.Seed, $"tf2_descent-{level.Depth}-s{level.Seed}", 0, 0, HordeSlots.Slots, HordeSlots.Sighted, AiRecordingHeader.TeamLayerOff);

    public AiSession Session => _session;

    public HordeVision Vision => _vision;

    public HeroSlots HeroSlots => _heroes;

    public HordeSkillMap SkillMap => _map;

    public SkillCurves Curves => _curves;

    // ------------------------------------------------------------------ counters

    public long BrainsAdded { get; private set; }
    public long BrainsRemoved { get; private set; }
    public long IntentsApplied { get; private set; }
    public long IntentsStale { get; private set; }
    public long IntentsExpired { get; private set; }
    public long IntentsDead { get; private set; }
    public long Actions { get; private set; }
    public long EventsDiscarded { get; private set; }
    public long SoundsHeard { get; private set; }

    /// <summary>AllyDamaged and AllyDied events told to pack-mates.</summary>
    public long AlliesTold { get; private set; }

    /// <summary>Arrived events told to brains (<see cref="Arrived"/>).</summary>
    public long ArrivalsTold { get; private set; }
    public long Stuck { get; private set; }
    public long Publishes { get; private set; }

    /// <summary>Events told to a brain and not yet handed to a step.</summary>
    public int EventsWaiting
    {
        get
        {
            int n = 0;
            for (int b = 1; b < _body.Length; b++)
                n += _body[b].Pending;
            return n;
        }
    }

    /// <summary>The brain at <paramref name="body"/>, or none.</summary>
    public ActorId BrainAt(int body) => _body[body].Brain;

    /// <summary>The roster row of the brain at <paramref name="body"/>, or -1.</summary>
    public int RowAt(int body) => _body[body].Brain.IsValid ? _body[body].Row : -1;

    /// <summary>The robot's sight range, its cone's half-angle cosine, its recognition and its hearing scale (the senses' parameters).</summary>
    public (float Range, float CosHalfCone, int RecognizeTicks, float Hearing) SenseOf(int body)
    {
        Body s = _body[body];
        return (s.Range, s.CosHalfCone, s.RecognizeTicks, s.Hearing);
    }

    // ------------------------------------------------------------------ the tick

    /// <summary>Where the heroes are this tick (each seat to its actor; a new seat takes a free slot).</summary>
    public void Heroes(long now, ReadOnlySpan<HeroPose> heroes)
    {
        _now = now;
        for (int h = 0; h < _hero.Length; h++)
            _hero[h].Present = false;
        foreach (ref readonly HeroPose pose in heroes)
        {
            ActorId actor = _heroes.Present(pose.Seat, now);
            if (!actor.IsValid)
                continue;
            ref Hero hero = ref _hero[actor.Slot - HordeSlots.FirstHero];
            bool continuing = hero.Actor == actor && hero.SeenAt == now - 1;
            AiVector v = continuing ? (pose.Feet - hero.Feet) * (1f / _tickSeconds) : default;
            hero.Velocity = v.Length > 3500f ? default : v;   // a teleport is no velocity
            hero.Actor = actor;
            hero.Present = true;
            hero.Feet = pose.Feet;
            hero.Eye = pose.Eye;
            hero.Yaw = pose.Yaw;
            hero.Room = pose.Room;
            hero.SightScale = pose.SightScale;
            hero.Veil = pose.Veil;
            hero.SeenAt = now;
        }
    }

    /// <summary>The steps finished since the last tick taken back (<see cref="TakeBack"/>), then each body's newest intent to <paramref name="orders"/> (<see cref="ApplyIntents"/>).</summary>
    public void Apply(long now, IHordeOrders orders)
    {
        TakeBack();
        ApplyIntents(now, orders);
    }

    /// <summary>The core's take-back of finished steps (<see cref="AiSession.Drain"/>), never waiting.</summary>
    public void TakeBack() => _session.Drain();

    /// <summary>Each body's newest intent to <paramref name="orders"/> (see the class remarks).</summary>
    public void ApplyIntents(long now, IHordeOrders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        _now = now;
        for (int b = 1; b < _body.Length; b++)
        {
            if (!_session.TryLatestIntent(b, out IntentCommand intent))
                continue;

            ref Body s = ref _body[b];
            RobotId holder = _pool.Holder(b);
            var lease = new BodyLease(b, _pool.Serial(b));
            if (!holder.IsValid || intent.Bot != lease.Actor)
            {
                // Made for the robot the body was lent to before: never carried out.
                if (Once(ref s, in intent))
                    IntentsStale++;
                continue;
            }

            if (!_roster.IsLive(holder) || _roster.State(holder.Index) == RobotState.Dead)
            {
                if (Once(ref s, in intent))
                    IntentsDead++;
                continue;
            }

            if (now >= intent.ExpiresTick.Value)
            {
                if (!s.Holding)
                {
                    orders.Hold(b);
                    s.Holding = true;
                    s.Following = false;
                    IntentsExpired++;
                }

                continue;
            }

            if (intent.Bot == s.AppliedActor && intent.Seq == s.AppliedSeq)
                continue;

            orders.Order(b, intent.Move, intent.Look, intent.ExpiresTick.Value);
            s.AppliedActor = intent.Bot;
            s.AppliedSeq = intent.Seq;
            s.Holding = false;
            IntentsApplied++;
            bool follow = intent.Move.Kind == MoveKind.Follow && intent.Move.CornerCount > 0;
            if (follow && !s.Following)
            {
                s.StuckFrom = _poses.Position(b);
                s.StuckSince = now;
                s.StuckTold = false;
            }

            s.Following = follow;
            if (intent.Action.Kind != ActionKind.None)
            {
                Actions++;
                orders.Act(b, intent.Action);
            }
        }
    }

    /// <summary>A brain for each newly lent body, none for a returned body or a dead robot (see the class remarks).</summary>
    public void Sync(long now, IHordeOrders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        _now = now;
        _addedCount = 0;
        for (int b = 1; b < _body.Length; b++)
        {
            ref Body s = ref _body[b];
            RobotId holder = _pool.Holder(b);
            bool want = holder.IsValid && _roster.IsLive(holder) && _roster.State(holder.Index) == RobotState.Embodied;
            var actor = new BodyLease(b, _pool.Serial(b)).Actor;
            if (s.Brain.IsValid && (!want || s.Brain != actor))
            {
                Remove(b);
                orders.Hold(b);
            }

            if (want && !s.Brain.IsValid)
            {
                Add(b, holder.Index, actor, now);
                orders.Hold(b);
                _added[_addedCount++] = b;
            }
        }
    }

    private void Add(int b, int row, ActorId actor, long now)
    {
        ref Body s = ref _body[b];
        ImmutableArray<int> temperament = _roster.Temperament(row).Levels;
        Span<byte> levels = stackalloc byte[_map.Count];
        _map.Fill(temperament, levels);
        SkillProfile profile = _map.Profile(temperament);
        float scale = (float)_roster.Scale(row);
        var template = _rules.Robots.Template(_roster.Template(row));

        s = new Body
        {
            Brain = actor,
            Row = row,
            Family = HordeWeapons.Of(template.Attack),
            Class = TemplateNumber(template.Id),
            LastPos = _poses.Position(b),
            EyeHeight = EyeHeight * scale,
            Range = BaseSightRange * scale * HordeSkillMap.Extra(_curves, profile, "sightRangeScale"),
            CosHalfCone = MathF.Cos(MathF.Min(85f, BaseHalfConeDeg * HordeSkillMap.Extra(_curves, profile, "sightConeScale")) * (MathF.PI / 180f)),
            RecognizeTicks = VisionParams.Ticks(VisionCurves.RecognizeAt(Level(profile, "alertness")), _tickSeconds),
            LostTicks = VisionParams.Ticks(VisionCurves.LostSightSeconds, _tickSeconds),
            Hearing = HordeSkillMap.Extra(_curves, profile, "hearingScale"),
            NavClass = (int)HordeHulls.ClassOf(scale),
        };
        _vision.ForgetBody(b);
        _session.AddBot(actor, _roster.Seed(row), levels, home: HomeOf(row));
        BrainsAdded++;

        var tick = new Tick((int)now);
        Push(b, AiEvent.Spawned(actor, tick, s.LastPos));
        CarriedMemory carried = _roster.Carried(row);
        if (carried.TargetSeat > 0 || carried.Alert != AlertLevel.Calm)
        {
            ActorId target = carried.TargetSeat > 0 ? _heroes.ActorOf(carried.TargetSeat) : ActorId.None;
            float loudness = carried.Alert == AlertLevel.Alerted ? 1f : 0.5f;
            Push(b, AiEvent.SoundHeard(actor, tick, SoundKind.Unknown, carried.LastKnownPosition, loudness, target, -1));
        }
    }

    /// <summary>
    /// A robot's home (format 8's home point, <see cref="BrainSetup.Home"/>): its roster post, the
    /// place of its own the spawn director rolled round its pack's marker; the boss's spot for the
    /// boss; where a dev command put it for a robot put down by hand.
    /// </summary>
    public AiVector? HomeOf(int row) => _roster.Post(row);

    private void Remove(int b)
    {
        ref Body s = ref _body[b];
        _session.RemoveBot(s.Brain);
        EventsDiscarded += s.Pending;
        BrainsRemoved++;
        _vision.ForgetBody(b);
        s = new Body();
    }

    /// <summary>One tick of sight over <paramref name="lines"/> (see <see cref="HordeVision"/>).</summary>
    public void See(long now, IHordeLines lines)
    {
        _now = now;
        for (int b = 1; b < _body.Length; b++)
        {
            ref readonly Body s = ref _body[b];
            if (!s.Brain.IsValid)
            {
                _eyes[b] = default;
                continue;
            }

            AiVector at = _poses.Position(b);
            _eyes[b] = new BodyEye(true, at + new AiVector(0f, 0f, s.EyeHeight), _poses.Yaw(b), s.Range, s.CosHalfCone, s.RecognizeTicks, s.LostTicks);
        }

        for (int h = 0; h < _hero.Length; h++)
        {
            // A hero gone since the last look (HeroGone): his pairs go untold, so no robot "loses sight" of the dead.
            if (_gone[h])
            {
                _vision.ForgetHero(h);
                _gone[h] = false;
            }

            ref readonly Hero hero = ref _hero[h];
            _targets[h] = hero.Present
                ? new HeroTarget(true, hero.Eye, hero.Feet + new AiVector(0f, 0f, 0.6f * (hero.Eye.Z - hero.Feet.Z)), hero.Feet, hero.Velocity,
                                 hero.SightScale, hero.Veil)
                : default;
        }

        _vision.Run(now, _eyes, _targets, lines, this);
    }

    /// <summary>
    /// Every stage of <paramref name="body"/>'s sight of hero slot <paramref name="hero"/> (0 to 3) as of
    /// the last <see cref="See"/>, its lines traced through <paramref name="lines"/> now (the dev print's);
    /// false when the body has no brain or the hero was not there.
    /// </summary>
    public bool ProbeSight(int body, int hero, IHordeLines lines, out SightProbe probe, out float sightScale)
    {
        probe = default;
        sightScale = 0f;
        if ((uint)body >= (uint)_body.Length || (uint)hero >= (uint)_targets.Length || !_eyes[body].Present || !_targets[hero].Present)
            return false;
        probe = _vision.Probe(body, hero, in _eyes[body], in _targets[hero], lines);
        sightScale = _targets[hero].SightScale;
        return true;
    }

    /// <summary>
    /// A hero is dead (this lane's rule; plan_descent_logic 3.3, 3.5): every brain with a body is
    /// told to forget him as the core forgets a player gone to watch (<see cref="AiEvent.ActorSpectating"/>:
    /// his last place, what it heard of him, nothing learnt while he is gone); nothing on the
    /// roster keeps his seat (<see cref="RobotRoster.ForgetSeat"/>: each row's carried target,
    /// threat and taunt, each pack's contact); and at the next look his sight pairs are dropped
    /// untold. Answers the brains told.
    /// </summary>
    public int HeroGone(long now, int seat)
    {
        _now = now;
        ActorId hero = _heroes.ActorOf(seat);
        _roster.ForgetSeat(seat);
        if (!hero.IsValid)
            return 0;
        _gone[hero.Slot - HordeSlots.FirstHero] = true;
        return TellSpectating(hero, now, gone: true);
    }

    /// <summary>A dead hero stands again: every brain with a body is told he is back, a stranger it finds again only by its senses. Answers the brains told.</summary>
    public int HeroBack(long now, int seat)
    {
        _now = now;
        ActorId hero = _heroes.ActorOf(seat);
        return hero.IsValid ? TellSpectating(hero, now, gone: false) : 0;
    }

    private int TellSpectating(ActorId hero, long now, bool gone)
    {
        int told = 0;
        var tick = new Tick((int)now);
        for (int b = 1; b < _body.Length; b++)
        {
            if (!_body[b].Brain.IsValid)
                continue;
            Push(b, AiEvent.ActorSpectating(_body[b].Brain, tick, hero, gone));
            told++;
        }

        return told;
    }

    void IHordeSightSink.Gained(int body, int hero, AiVector at, AiVector velocity, float fraction)
    {
        ref Body s = ref _body[body];
        ActorId target = _hero[hero].Actor;
        Push(body, AiEvent.SightGained(s.Brain, new Tick((int)_now), target, at, velocity, fraction));
        int seat = _heroes.SeatOf(target);
        Carry(s.Row, seat, at, AlertLevel.Alerted);
        int room = _level.RoomAt(at);
        _roster.AlertPack(_roster.Pack(s.Row), room >= 0 ? room : _roster.Room(s.Row), at, _now, seat);
    }

    void IHordeSightSink.Lost(int body, int hero, AiVector at, AiVector velocity)
    {
        ref Body s = ref _body[body];
        ActorId target = _hero[hero].Actor;
        Push(body, AiEvent.SightLost(s.Brain, new Tick((int)_now), target, at, velocity));
        Carry(s.Row, _heroes.SeatOf(target), at, AlertLevel.Alerted);
    }

    /// <summary>
    /// The tick's sounds, heard by each brain whose robot the walk from the sound
    /// reaches (the room graph's path distance: never through a wall) within the
    /// sound's reach times its hearing scale (Alertness); louder nearer.
    /// </summary>
    public void Hear(long now, ReadOnlySpan<HordeSound> sounds) => Hear(now, sounds, 0);

    /// <summary>
    /// The sounds of the last moments, heard by the brains <see cref="Sync"/> just
    /// added (<see cref="Added"/>): a robot lent a body between a sound and the far
    /// simulation's next tick is out of the far simulation's hearing by then, and
    /// had no brain when the sound was heard, so it hears it now.
    /// </summary>
    public void HearAdded(long now, ReadOnlySpan<HordeSound> recent)
    {
        for (int i = 0; i < _addedCount; i++)
            Hear(now, recent, _added[i]);
    }

    /// <summary>The bodies whose brains the last <see cref="Sync"/> added.</summary>
    public ReadOnlySpan<int> Added => _added.AsSpan(0, _addedCount);

    private void Hear(long now, ReadOnlySpan<HordeSound> sounds, int only)
    {
        _now = now;
        var tick = new Tick((int)now);
        foreach (ref readonly HordeSound sound in sounds)
        {
            if (sound.Room < 0)
                continue;
            ActorId source = sound.Seat > 0 ? _heroes.ActorOf(sound.Seat) : ActorId.None;
            for (int b = only > 0 ? only : 1; b < (only > 0 ? only + 1 : _body.Length); b++)
            {
                ref readonly Body s = ref _body[b];
                if (!s.Brain.IsValid)
                    continue;
                if (!Reaches(b, sound.Room, sound.Position, sound.Reach, out float loudness))
                    continue;
                Push(b, AiEvent.SoundHeard(s.Brain, tick, sound.Kind, sound.Position, loudness, source, -1));
                SoundsHeard++;
                if (sound.Seat > 0)
                    Carry(s.Row, sound.Seat, sound.Position, AlertLevel.Suspicious);
            }
        }
    }

    /// <summary>
    /// A hit on a robot (the hit sink): <c>Damaged</c> for its brain, from the
    /// hero in <paramref name="attackerSeat"/> (-1 none), towards where that hero
    /// stands; to each pack-mate's brain in earshot B.3's <c>AllyDamaged</c> or
    /// <c>AllyDied</c> (the ally, where it stands, the damage, the hero); and to the
    /// brain of a robot of another pack in earshot its pain or death, heard as a
    /// sound where it stands, the hero who caused it its source (so the cry is a lead
    /// to him, and goes when he dies: the core forgets a dead hero's sounds). Earshot
    /// is the walk from the hit within the pain's or the death's reach times the
    /// hearer's hearing.
    /// </summary>
    public void Hit(long now, RobotId robot, int attackerSeat, float taken, bool killed, AiVector at)
    {
        _now = now;
        if (!_roster.IsLive(robot))
            return;
        var tick = new Tick((int)now);
        ActorId attacker = attackerSeat > 0 ? _heroes.ActorOf(attackerSeat) : ActorId.None;
        int pack = _roster.Pack(robot.Index);
        int room = _level.RoomAt(at);
        int hurtBody = _roster.Body(robot.Index);
        ActorId ally = hurtBody > 0 && hurtBody < _body.Length ? _body[hurtBody].Brain : ActorId.None;
        for (int b = 1; b < _body.Length; b++)
        {
            ref readonly Body s = ref _body[b];
            if (!s.Brain.IsValid)
                continue;
            if (s.Row == robot.Index)
            {
                AiVector dir = default;
                if (attacker.IsValid && _hero[attacker.Slot - HordeSlots.FirstHero] is { Present: true } hero)
                    dir = (hero.Feet - at).Normalised();
                Push(b, AiEvent.Damaged(s.Brain, tick, attacker, taken, dir, 0));
                if (attackerSeat > 0)
                    Carry(s.Row, attackerSeat, dir.LengthSquared > 0f ? _hero[attacker.Slot - HordeSlots.FirstHero].Feet : at, AlertLevel.Alerted, taken);
                continue;
            }

            if (room < 0 || !Reaches(b, room, at, killed ? DeathReach : PainReach, out float loudness))
                continue;
            if (_roster.Pack(s.Row) == pack)
            {
                Push(b, killed ? AiEvent.AllyDied(s.Brain, tick, ally, at, attacker) : AiEvent.AllyDamaged(s.Brain, tick, ally, at, taken, attacker));
                AlliesTold++;
            }
            else
            {
                Push(b, AiEvent.SoundHeard(s.Brain, tick, killed ? SoundKind.Death : SoundKind.Pain, at, loudness, attacker, -1));
            }
        }
    }

    /// <summary>
    /// The motion's word that body <paramref name="body"/> reached its order's last corner at
    /// <paramref name="at"/>: an <c>Arrived</c> for its brain (the core's applier feedback). A body
    /// with no brain is told nothing.
    /// </summary>
    public void Arrived(long now, int body, AiVector at)
    {
        _now = now;
        if ((uint)body >= (uint)_body.Length || !_body[body].Brain.IsValid)
            return;
        Push(body, AiEvent.Arrived(_body[body].Brain, new Tick((int)now), at));
        ArrivalsTold++;
    }

    /// <summary>A hero died: each brain that held sight of it saw it fall (the kill feed's line, as the core knows it).</summary>
    public void HeroDied(long now, int seat, ActorId killer)
    {
        _now = now;
        ActorId hero = _heroes.ActorOf(seat);
        if (!hero.IsValid)
            return;
        int h = hero.Slot - HordeSlots.FirstHero;
        for (int b = 1; b < _body.Length; b++)
            if (_body[b].Brain.IsValid && _vision.Holds(b, h))
                Push(b, AiEvent.KillFeed(_body[b].Brain, new Tick((int)now), hero, killer));
    }

    /// <summary>Builds the tick's snapshot (<see cref="Build"/>) and hands it to the session (<see cref="Hand"/>).</summary>
    public void Publish(long now) => Hand(Build(now));

    /// <summary>The tick's stuck checks, then its snapshot (each view as the class remarks say), not yet handed over.</summary>
    public AiSnapshot Build(long now)
    {
        _now = now;
        var tick = new Tick((int)now);
        int stuckTicks = (int)MathF.Ceiling(StuckSeconds / _tickSeconds);
        for (int b = 1; b < _body.Length; b++)
        {
            ref Body s = ref _body[b];
            if (!s.Brain.IsValid || !s.Following || s.Holding)
                continue;
            AiVector at = _poses.Position(b);
            if (at.DistanceTo(s.StuckFrom) > StuckUnits)
            {
                s.StuckFrom = at;
                s.StuckSince = now;
                s.StuckTold = false;
            }
            else if (!s.StuckTold && now - s.StuckSince >= stuckTicks)
            {
                s.StuckTold = true;
                Stuck++;
                Push(b, new AiEvent(AiEventKind.Stuck, s.Brain, tick, ActorId.None, at, default, 0f, 0, 0));
            }
        }

        int epoch = _paths?.Epoch ?? 0;
        AiSnapshot snap = _session.Snapshots.BeginWrite(tick);
        for (int b = 1; b < _body.Length; b++)
        {
            ref Body s = ref _body[b];
            if (!s.Brain.IsValid)
                continue;
            AiVector at = _poses.Position(b);
            AiVector velocity = (at - s.LastPos) * (1f / _tickSeconds);
            s.LastPos = at;
            s.Velocity = velocity;
        }

        for (int b = 1; b < _body.Length; b++)
        {
            ref readonly Body s = ref _body[b];
            if (!s.Brain.IsValid)
                continue;
            AiVector at = s.LastPos;
            int row = s.Row;
            AiAngles eye = _aim is { } aim ? aim.Aim(b) : new AiAngles(0f, _poses.Yaw(b), 0f);
            int line = _aim is { } fire && DecideSchedule.IsDecideTick(tick, b)
                ? (int)MathF.Min(HordeView.FireLineReach, MathF.Max(0f, fire.LineOfFire(b, HordeView.FireLineReach)))
                : HordeView.NotTraced;
            var stats = _roster.Stats(row);
            var self = new SelfState(at, s.Velocity, eye, (int)Math.Ceiling(_roster.Life(row)), (int)Math.Ceiling(stats.Life), s.Family,
                                     _roster.Clip(row), line, SelfFlags.Alive | SelfFlags.Grounded, AltAmmo: epoch, Grenades: s.NavClass,
                                     Team: RobotTeam, Class: s.Class);
            PerceivedView view = snap.Add(s.Brain, self);
            for (int h = 0; h < _hero.Length; h++)
            {
                ref readonly Hero hero = ref _hero[h];
                if (hero.Present && _vision.Holds(b, h))
                    view.AddSighted(new ActorFacts(hero.Actor, hero.Feet, hero.Velocity, HordeView.Hero(hero.Yaw), ActorActivity.None, _vision.Fraction(b, h), Team: HeroTeam));
            }

            AddMates(b, view);
        }

        return snap;
    }

    /// <summary>
    /// The pack-mates of body <paramref name="b"/>'s robot that hold bodies, the nearest
    /// <see cref="HordeView.PackMates"/>, nearest first (a tie to the lower body), each
    /// with its family and its life's share (<see cref="HordeView.Mate"/>).
    /// </summary>
    private void AddMates(int b, PerceivedView view)
    {
        ref readonly Body me = ref _body[b];
        int pack = _roster.Pack(me.Row);
        int n = 0;
        for (int c = 1; c < _body.Length; c++)
        {
            ref readonly Body o = ref _body[c];
            if (c == b || !o.Brain.IsValid || _roster.Pack(o.Row) != pack)
                continue;
            float d = me.LastPos.DistanceTo(o.LastPos);
            int k = n < _mates.Length ? n++ : _mates.Length;
            if (k == _mates.Length)
            {
                if (d >= _mateDistance[k - 1])
                    continue;
                k--;
            }

            while (k > 0 && _mateDistance[k - 1] > d)
            {
                _mates[k] = _mates[k - 1];
                _mateDistance[k] = _mateDistance[k - 1];
                k--;
            }

            _mates[k] = c;
            _mateDistance[k] = d;
        }

        for (int i = 0; i < n; i++)
        {
            ref readonly Body o = ref _body[_mates[i]];
            double most = _roster.Stats(o.Row).Life;
            double share = most > 0 ? _roster.Life(o.Row) / most : 0;
            view.AddSighted(new ActorFacts(o.Brain, o.LastPos, o.Velocity, HordeView.Mate(o.Family, share), ActorActivity.None, 1f, Team: RobotTeam));
        }
    }

    /// <summary>The core's publish of <paramref name="snap"/>: each brain whose decide tick it is gets a step, with the events told to it since its last.</summary>
    public void Hand(AiSnapshot snap)
    {
        ArgumentNullException.ThrowIfNull(snap);
        _session.Publish(snap);
        Publishes++;
        for (int b = 1; b < _body.Length; b++)
            if (_body[b].Brain.IsValid && DecideSchedule.IsDecideTick(snap.Tick, b))
                _body[b].Pending = 0;
    }

    /// <summary>
    /// The level is over: call after the transport has stopped (a pool stopped and
    /// joined). Steps handed over and not run are dropped (the core counts them),
    /// and every brain is removed.
    /// </summary>
    public void End()
    {
        _session.Abandon();
        for (int b = 1; b < _body.Length; b++)
            if (_body[b].Brain.IsValid)
                Remove(b);
    }

    /// <summary>Where hero actor <paramref name="actor"/>'s eye is now; false when it is not a present hero.</summary>
    public bool TryHeroEye(ActorId actor, out AiVector eye)
    {
        int h = actor.Slot - HordeSlots.FirstHero;
        if ((uint)h < (uint)_hero.Length && _hero[h].Present && _hero[h].Actor == actor)
        {
            eye = _hero[h].Eye;
            return true;
        }

        eye = default;
        return false;
    }

    /// <summary>Where robot actor <paramref name="actor"/>'s eye is now; false when its brain is gone.</summary>
    public bool TryRobotEye(ActorId actor, out AiVector eye)
    {
        if ((uint)actor.Slot < (uint)_body.Length && actor.Slot > 0 && _body[actor.Slot].Brain == actor)
        {
            eye = _poses.Position(actor.Slot) + new AiVector(0f, 0f, _body[actor.Slot].EyeHeight);
            return true;
        }

        eye = default;
        return false;
    }

    // ------------------------------------------------------------------ helpers

    private void Push(int b, in AiEvent e)
    {
        _session.Push(e);
        _body[b].Pending++;
    }

    private bool Reaches(int b, int room, AiVector at, float reach, out float loudness)
    {
        AiVector me = _poses.Position(b);
        int mine = _level.RoomAt(me);
        if (mine < 0)
            mine = _roster.Room(_body[b].Row);
        float heard = reach * _body[b].Hearing;
        float d = mine >= 0 ? _level.PathDistance(room, at, mine, me) : float.PositiveInfinity;
        loudness = heard > 0f ? 1f - d / heard : 0f;
        return d <= heard;
    }

    /// <summary>
    /// The carried memory (plan 3.3) moved by what a brain was just told: a hero
    /// seen or hitting it becomes the target, a hero heard only when it has none;
    /// the last known place and tick follow the target (or any contact while it has
    /// none); the alert only rises; the threat entry adds up per seat and the
    /// larger seat's wins.
    /// </summary>
    private void Carry(int row, int seat, AiVector at, AlertLevel alert, float threat = 0f)
    {
        CarriedMemory c = _roster.Carried(row);
        int target = c.TargetSeat;
        if (seat > 0 && (target <= 0 || alert == AlertLevel.Alerted))
            target = seat;
        bool aboutTarget = seat > 0 ? seat == target : target <= 0;
        int threatSeat = c.ThreatSeat;
        float threatAmount = c.Threat;
        if (threat > 0f && seat > 0)
        {
            if (threatSeat == seat)
                threatAmount += threat;
            else if (threatSeat < 0 || threat > threatAmount)
                (threatSeat, threatAmount) = (seat, threat);
        }

        _roster.SetCarried(row, new CarriedMemory(target, aboutTarget ? at : c.LastKnownPosition, aboutTarget ? _now : c.LastKnownTick,
                                                  (AlertLevel)Math.Max((int)c.Alert, (int)alert), threatSeat, threatAmount));
    }

    private bool Once(ref Body s, in IntentCommand intent)
    {
        if (s.RejectedActor == intent.Bot && s.RejectedSeq == intent.Seq)
            return false;
        s.RejectedActor = intent.Bot;
        s.RejectedSeq = intent.Seq;
        return true;
    }

    private static int Level(SkillProfile profile, string key)
    {
        int i = profile.IndexOf(key);
        return i < 0 ? 0 : profile.Levels[i];
    }

    private int TemplateNumber(string id)
    {
        var rows = _rules.Content.Robots.Rows;
        for (int r = 0; r < rows.Count; r++)
            if (rows[r].Id == id)
                return r + 1;
        return 0;
    }

    /// <summary>A body's brain and what the tick keeps of it.</summary>
    public struct Body
    {
        public ActorId Brain;
        public int Row;
        public int Family;
        public int Class;
        public AiVector LastPos;
        public AiVector Velocity;
        public float EyeHeight;
        public float Range;
        public float CosHalfCone;
        public int RecognizeTicks;
        public int LostTicks;
        public float Hearing;

        /// <summary>The clearance class the robot plans by (<see cref="HordeHulls.ClassOf"/> of its scale).</summary>
        public int NavClass;
        public int Pending;
        public ActorId AppliedActor;
        public int AppliedSeq;
        public ActorId RejectedActor;
        public int RejectedSeq;
        public bool Holding;
        public bool Following;
        public AiVector StuckFrom;
        public long StuckSince;
        public bool StuckTold;
    }

    private struct Hero
    {
        public ActorId Actor;
        public bool Present;
        public AiVector Feet;
        public AiVector Eye;
        public AiVector Velocity;
        public float Yaw;
        public int Room;
        public float SightScale;
        public HeroVeil Veil;
        public long SeenAt;
    }
}
