using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>Every event and intent a session hands its sink, for a fact to read.</summary>
internal sealed class EventLog : IAiRecordSink
{
    public List<AiEvent> Events { get; } = [];
    public List<IntentCommand> Intents { get; } = [];
    public List<ActorId> Added { get; } = [];

    public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) => Added.Add(bot);
    public void BotRemoved(ActorId bot) { }
    public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches) { }
    public void Event(in AiEvent e) => Events.Add(e);
    public void Snapshot(AiSnapshot snapshot) { }
    public void Intent(in IntentCommand intent) => Intents.Add(intent);
    public void UserCmd(in AiUserCmd cmd) { }
    public void Frame(in AiActorFrame frame) { }
    public void Observed(in AiEvent e) { }
}

/// <summary>Orders that keep nothing (a measurement's).</summary>
internal sealed class NullOrders : IHordeOrders
{
    public int Count { get; private set; }

    public void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick) => Count++;

    public void Hold(int body) => Count++;

    public void Act(int body, in ActionIntent action) => Count++;
}

/// <summary>
/// What a fact and a <see cref="StallDecider"/> share: the decider says it is
/// inside its stall, the fact says when it may leave, and the decider says it
/// has left. The stall also ends by itself after <see cref="CapMs"/>, so a main
/// thread that waited on it fails the fact instead of hanging the run.
/// </summary>
internal sealed class StallGate : IDisposable
{
    public const int CapMs = 30_000;

    public ManualResetEventSlim Entered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);
    public ManualResetEventSlim Left { get; } = new(false);

    /// <summary>Lets a decide still inside its stall go. The events are left alone: a worker may be waiting on one.</summary>
    public void Dispose() => Release.Set();
}

/// <summary>A decider that stalls inside one of its decides until its gate lets it go, and otherwise holds.</summary>
internal sealed class StallDecider(BrainSetup setup, int stallAtDecide, StallGate gate) : IDecider
{
    private int _decides;

    public BtTrace Trace { get; } = new();
    public DoingRecord Doing => default;
    public IReadOnlyList<string> NodeNames => [];
    public IReadOnlyList<int> NodeBranches => [];

    public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
    {
        if (++_decides == stallAtDecide)
        {
            gate.Entered.Set();
            gate.Release.Wait(StallGate.CapMs);
            gate.Left.Set();
        }

        return IntentCommand.Hold(setup.Bot, tick, seq);
    }
}

/// <summary>
/// The horde's brains over the AI core with no engine (plan_descent_logic HO4):
/// a brain per lent body and never two, the applier's checks (a body lent to
/// another robot since, an expired intent, a dead robot), the view a brain is
/// handed, events across a stall, the main thread's allocations, teardown.
/// </summary>
public sealed class HordeBrainsTests
{
    [Fact]
    public void A_brain_is_added_when_a_body_is_lent_and_removed_when_it_is_returned()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Tick();
        Assert.Equal(rig.Roster.Lease(id.Index).Actor, rig.Brains.BrainAt(body));
        Assert.Equal(1, rig.Brains.Session.BotCount);

        rig.Return(id);
        rig.Tick();
        Assert.False(rig.Brains.BrainAt(body).IsValid);
        Assert.Equal(0, rig.Brains.Session.BotCount);
    }

    [Fact]
    public void A_body_lent_again_to_another_robot_has_one_brain_the_new_robots()
    {
        var rig = new BrainRig();
        var a = rig.Robot(Centre(0), seed: 1);
        var b = rig.Robot(Centre(0) + At(32, 0), seed: 2);
        int body = rig.Lend(a);
        rig.Tick();
        rig.Return(a);
        Assert.Equal(body, rig.Lend(b));
        rig.Tick();
        Assert.Equal(1, rig.Brains.Session.BotCount);
        Assert.Equal(rig.Roster.Lease(b.Index).Actor, rig.Brains.BrainAt(body));
        Assert.Equal(2, rig.Brains.BrainAt(body).Serial);
        Assert.Equal(b.Index, rig.Brains.RowAt(body));
    }

    [Fact]
    public void A_robot_that_dies_loses_its_brain()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Tick();
        rig.Roster.Hurt(id.Index, 1e9);
        rig.Tick();
        Assert.False(rig.Brains.BrainAt(body).IsValid);
        Assert.Equal(0, rig.Brains.Session.BotCount);
    }

    /// <summary>A robot chasing a hero two rooms off: it has an intent with a move by the time this returns.</summary>
    private static (BrainRig Rig, RobotId Id, int Body) Chasing()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Hero(1, Centre(2));
        rig.TickUntil(() => rig.Orders.Orders.Any(o => o.Body == body && o.Move.Kind == MoveKind.Follow), 400);
        Assert.Contains(rig.Orders.Orders, o => o.Body == body && o.Move.Kind == MoveKind.Follow);
        return (rig, id, body);
    }

    [Fact]
    public void An_intent_made_for_a_body_since_lent_to_another_robot_is_dropped_and_counted()
    {
        var (rig, a, body) = Chasing();
        var b = rig.Robot(Centre(1), seed: 9);

        // The embodiment rules' decide, inside the controller's tick: the body goes back and out again.
        rig.Return(a);
        Assert.Equal(body, rig.Lend(b));
        int orders = rig.Orders.Orders.Count;
        rig.Now++;
        rig.Brains.Apply(rig.Now, rig.Orders);

        Assert.Equal(1, rig.Brains.IntentsStale);
        Assert.Equal(orders, rig.Orders.Orders.Count);
        rig.Brains.Apply(rig.Now, rig.Orders);
        Assert.Equal(1, rig.Brains.IntentsStale);
    }

    [Fact]
    public void An_expired_intent_holds_the_body_once()
    {
        var (rig, _, body) = Chasing();
        var last = rig.Orders.Orders.Last(o => o.Body == body);
        int holds = rig.Orders.Holds.Count(h => h == body);
        rig.Now = last.Expires;
        rig.Brains.Apply(rig.Now, rig.Orders);
        rig.Brains.Apply(rig.Now + 1, rig.Orders);

        Assert.Equal(holds + 1, rig.Orders.Holds.Count(h => h == body));
        Assert.Equal(1, rig.Brains.IntentsExpired);
    }

    /// <summary>Every hero actor a session of these slots could name: the four slots at serials 1 to 4.</summary>
    private static IEnumerable<ActorId> AnyHero() =>
        from h in Enumerable.Range(HordeSlots.FirstHero, HordeSlots.Heroes) from s in Enumerable.Range(1, 4) select new ActorId(h, s);

    private static PerceivedView ViewOf(BrainRig rig, int body)
    {
        Assert.True(rig.Brains.Session.Snapshots.TryGet(new Tick((int)rig.Now), out var snap));
        return snap.ViewFor(body) ?? throw new InvalidOperationException($"no view for body {body}");
    }

    [Fact]
    public void A_robots_view_never_yields_a_hero_it_holds_no_sight_of()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Hero(1, Centre(0) + At(200, 0));
        rig.Hero(2, Centre(1));
        rig.Lines.Clear = (_, _) => false;
        for (int t = 0; t < 200; t++)
        {
            rig.Tick();
            var view = ViewOf(rig, body);
            Assert.Equal(0, view.SightedCount);
            foreach (var hero in AnyHero())
                Assert.False(view.TryActor(hero, out _), $"tick {t}: the view yields {hero}");
        }

        // The control: the same heroes in the open are seen once recognised.
        rig.Lines.Clear = (_, _) => true;
        rig.Tick(60);
        Assert.True(AnyHero().Any(h => ViewOf(rig, body).TryActor(h, out _)), "the control: no hero in sight on an open level");
    }

    [Fact]
    public void A_robot_facing_away_does_not_see_a_hero_behind_it()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(1));
        int body = rig.Lend(id, yaw: 180f);
        rig.Slide = false;
        rig.Hero(1, Centre(1) + At(200, 0));
        rig.Tick(200);
        Assert.Equal(0, rig.Brains.Vision.Gains);

        rig.Poses.Set(body, Centre(1), 0f);
        rig.Tick(60);
        Assert.Equal(1, rig.Brains.Vision.Gains);
    }

    [Fact]
    public void A_robot_is_started_from_its_carried_memory()
    {
        var log = new EventLog();
        var traced = new BrainRig(sink: log);
        var robot = traced.Robot(Centre(0));
        traced.Hero(3, Centre(3));
        traced.Lines.Clear = (_, _) => false;
        traced.Tick();     // seat 3 takes a hero slot
        traced.Roster.SetCarried(robot.Index, new CarriedMemory(3, Centre(2), traced.Now - 50, AlertLevel.Alerted, -1, 0));
        int body = traced.Lend(robot);
        traced.Tick();

        var actor = traced.Brains.BrainAt(body);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.Spawned && e.Bot == actor);
        var heard = Assert.Single(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == actor);
        Assert.Equal(Centre(2), heard.Position);
        Assert.Equal(traced.Brains.HeroSlots.ActorOf(3), heard.Other);

        // It goes to look where it last knew the hero to be.
        traced.TickUntil(() => traced.Orders.Orders.Any(o => o.Body == body && o.Move.Kind == MoveKind.Follow), 60);
        var order = traced.Orders.Orders.Last(o => o.Body == body);
        Assert.Equal(Centre(2), order.Move.Corner(order.Move.CornerCount - 1));
    }

    [Fact]
    public void What_a_brain_was_told_is_carried_back_to_the_roster_row()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Hero(2, Centre(0) + At(150, 0));
        rig.TickUntil(() => rig.Brains.Vision.Gains > 0, 200);
        var seen = rig.Roster.Carried(id.Index);
        Assert.Equal(2, seen.TargetSeat);
        Assert.Equal(AlertLevel.Alerted, seen.Alert);

        rig.Heroes.Clear();
        rig.Tick(2);
        rig.Return(id);
        var carried = rig.Roster.Carried(id.Index);
        Assert.Equal(2, carried.TargetSeat);
        Assert.Equal(Centre(0) + At(150, 0), carried.LastKnownPosition);
        Assert.Equal(RobotState.Far, rig.Roster.State(id.Index));
        _ = body;
    }

    [Fact]
    public void Events_in_equal_events_processed_across_a_stalled_decide_and_the_main_thread_never_waits()
    {
        using var pool = new AiWorkerPool();
        pool.Start(1);
        using var stall = new StallGate();
        var rig = new BrainRig(transport: new PooledTransport(pool),
                               register: d => d.Register(HordeWeapons.RocketLauncher, (setup, _) => new StallDecider(setup, stallAtDecide: 5, stall)));
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Hero(1, Centre(0) + At(150, 0));

        // A server's pace, a tick each 15 ms, until the worker is inside the fifth decide and stays there.
        for (int t = 0; t < 2000 && !stall.Entered.IsSet; t++)
        {
            rig.Sounds.Add(new HordeSound(SoundKind.Footstep, Centre(0) + At(100, 0), 2000f, 1, 0));
            rig.Tick();
            Thread.Sleep(15);
        }

        Assert.True(stall.Entered.IsSet, "the control: the worker never reached the decide that stalls");
        int holdsBefore = rig.Orders.Holds.Count(h => h == body);

        // A hundred ticks while that decide is held: each one returns, though the step it would wait for cannot.
        // A main thread that waited would sit in its first tick until the stall's own cap let the decide go.
        for (int t = 0; t < 100; t++)
        {
            rig.Sounds.Add(new HordeSound(SoundKind.Footstep, Centre(0) + At(100, 0), 2000f, 1, 0));
            rig.Tick();
        }

        Assert.False(stall.Left.IsSet, "the main thread waited: the stalled decide had left by the time a hundred ticks were done");
        Assert.True(rig.Brains.Session.Counters.InFlight > 0, "the control: the stalled step is still in flight");
        Assert.True(rig.Brains.IntentsExpired >= 1, "no intent expired across the stall");
        Assert.True(rig.Orders.Holds.Count(h => h == body) > holdsBefore, "the robot did not hold");

        // Recovery: let the decide go; no more events; run past the brain's next decide tick and let the pool finish.
        stall.Release.Set();
        Assert.True(stall.Left.Wait(StallGate.CapMs), "the control: the stalled decide never left");
        rig.Tick(DecideSchedule.Period + 1);
        rig.Brains.Session.Drain(wait: true);
        var c = rig.Brains.Session.Counters;
        Assert.True(c.EventsIn >= 100, $"the control: {c.EventsIn} events in");
        Assert.Equal(0, rig.Brains.EventsWaiting);
        Assert.Equal(0, rig.Brains.EventsDiscarded);
        Assert.Equal(c.EventsIn, c.EventsProcessed);
        Assert.Equal(0, c.EventsDropped);
    }

    [Fact]
    public void A_thousand_steady_snapshot_builds_and_publishes_allocate_nothing_on_the_main_thread()
    {
        using var pool = new AiWorkerPool();
        pool.Start(2);
        var rig = new BrainRig(transport: new PooledTransport(pool));
        var heroes = new HeroPose[] { new(1, Centre(1), Centre(1) + At(0, 0, 64), 1) };
        for (int r = 0; r < 12; r++)
            rig.Lend(rig.Robot(Centre(r % 4) + At(10 * r, 5 * r), seed: (ulong)(100 + r)), yaw: 90f * r);
        var orders = new NullOrders();
        var sounds = new HordeSound[] { new(SoundKind.Footstep, Centre(1), 800f, 1, 1) };

        // The horde's own work, and apart from it the core's two calls (its take-back and its publish).
        long ours = 0, core = 0;
        void Step()
        {
            rig.Now++;
            long a = GC.GetAllocatedBytesForCurrentThread();
            rig.Brains.Heroes(rig.Now, heroes);
            long b = GC.GetAllocatedBytesForCurrentThread();
            rig.Brains.TakeBack();
            long c = GC.GetAllocatedBytesForCurrentThread();
            rig.Brains.ApplyIntents(rig.Now, orders);
            rig.Brains.Sync(rig.Now, orders);
            rig.Brains.See(rig.Now, rig.Lines);
            rig.Brains.Hear(rig.Now, sounds);
            var snap = rig.Brains.Build(rig.Now);
            long d = GC.GetAllocatedBytesForCurrentThread();
            rig.Brains.Hand(snap);
            long e = GC.GetAllocatedBytesForCurrentThread();
            ours += (b - a) + (d - c);
            core += (c - b) + (e - d);
        }

        for (int i = 0; i < 600; i++)
            Step();
        ours = core = 0;
        long appliedBefore = rig.Brains.IntentsApplied;
        for (int i = 0; i < 1000; i++)
            Step();

        Assert.Equal(12, rig.Brains.Session.BotCount);
        Assert.True(rig.Brains.Publishes >= 1600 && rig.Brains.Vision.Traces > 0 && rig.Brains.SoundsHeard > 0, "the control: the senses and publishes ran");
        Assert.True(rig.Brains.IntentsApplied > appliedBefore, "the control: intents were applied while measured");
        Assert.True(ours == 0, $"the horde's half allocated {ours} bytes over 1,000 ticks (the core's take-back and publish: {core} bytes, not ours)");
    }

    [Fact]
    public void Ending_leaves_no_brain_and_a_second_level_starts_clean()
    {
        var rig = new BrainRig();
        for (int r = 0; r < 5; r++)
            rig.Lend(rig.Robot(Centre(r % 4), seed: (ulong)r));
        rig.Hero(1, Centre(1));
        rig.Tick(30);
        Assert.Equal(5, rig.Brains.Session.BotCount);

        rig.Brains.End();
        Assert.Equal(0, rig.Brains.Session.BotCount);
        for (int b = 1; b <= HordeSlots.LastBody; b++)
            Assert.False(rig.Brains.BrainAt(b).IsValid);

        var second = new BrainRig(Row(3, markers: 0, seed: 99));
        Assert.Equal(0, second.Brains.Session.BotCount);
        var id = second.Robot(Centre(2));
        int body = second.Lend(id);
        second.Hero(7, Centre(0));
        second.Tick(12);
        Assert.Equal(1, second.Brains.Session.BotCount);
        Assert.Equal(new ActorId(HordeSlots.FirstHero, 1), second.Brains.HeroSlots.ActorOf(7));
        Assert.Equal(new ActorId(body, 1), second.Brains.BrainAt(body));
    }
}
