using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A hero who dies alone is not killed again for standing up (this lane; plan_descent_logic 4.7):
/// over the brain rig, the robots forget a dead hero for good (<see cref="HordeBrains.HeroGone"/>),
/// cannot see him in his respawn's grace (<see cref="HeroVeil.Hidden"/>), and find him again only
/// by the ordinary rules: a pack that killed him goes home and stays there until he fires.
/// </summary>
public sealed class HeroGraceBrainTests
{
    private const int Seat = 1;

    /// <summary>The respawn's grace in rig ticks: death.json's six seconds.</summary>
    private static readonly int GraceTicks = (int)Math.Ceiling(TheRules.Content.Death.Downed.RespawnGraceSeconds * BrainRig.Tps);

    private static bool Fighting(HuntDecider.Node? node) => node is HuntDecider.Node.Engage or HuntDecider.Node.Chase;

    /// <summary>A robot that saw hero <see cref="Seat"/> two rooms off and whose body went back to the pool after his death, then (when <paramref name="gone"/>) was told of it.</summary>
    private static (BrainRig Rig, EventLog Log, RobotId Id) SawHimThenBodyReturned(bool gone)
    {
        var log = new EventLog();
        var rig = new BrainRig(sink: log);
        var id = rig.Robot(Centre(0));
        rig.Lend(id);
        rig.Hero(Seat, Centre(2));
        Assert.True(rig.TickUntil(() => rig.Brains.Vision.Gains > 0, 400) < 400, "the stimulus: the robot never saw him");
        rig.Heroes.Clear();   // he is dead: no longer among the living players
        if (gone)
            rig.Brains.HeroGone(rig.Now, Seat);
        rig.Tick(2);
        rig.Return(id);
        return (rig, log, id);
    }

    [Fact]
    public void A_robot_whose_body_went_back_after_his_death_carries_nothing_of_him()
    {
        var (rig, _, id) = SawHimThenBodyReturned(gone: true);

        Assert.Equal(CarriedMemory.None, rig.Roster.Carried(id.Index));
    }

    [Fact]
    public void A_robot_lent_a_body_after_his_death_does_not_go_to_his_last_known_place()
    {
        var (rig, log, id) = SawHimThenBodyReturned(gone: true);

        int body = rig.Lend(id);
        rig.Tick(200);

        var actor = rig.Brains.BrainAt(body);
        Assert.DoesNotContain(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == actor);
        Assert.DoesNotContain(rig.Orders.Orders, o => o.Body == body && o.Move.Kind == MoveKind.Follow
                                                      && o.Move.Corner(o.Move.CornerCount - 1).DistanceTo(Centre(2)) < 64f);
        Assert.Equal(HuntDecider.Node.Hold, rig.NodeOf(body));
    }

    [Fact]
    public void A_robot_lent_a_body_after_a_hero_merely_went_out_of_sight_goes_to_look_for_him()
    {
        // The control for the fact above: the same robot, nobody told of a death.
        var (rig, log, id) = SawHimThenBodyReturned(gone: false);

        int body = rig.Lend(id);
        rig.Tick(200);

        var actor = rig.Brains.BrainAt(body);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == actor);
        Assert.Contains(rig.Orders.Orders, o => o.Body == body && o.Move.Kind == MoveKind.Follow
                                                && o.Move.Corner(o.Move.CornerCount - 1).DistanceTo(Centre(2)) < 64f);
    }

    [Fact]
    public void The_robot_that_held_him_when_he_died_is_told_no_loss_of_sight()
    {
        var log = new EventLog();
        var rig = new BrainRig(sink: log);
        var id = rig.Robot(Centre(0));
        int body = rig.Lend(id);
        rig.Hero(Seat, Centre(1));
        rig.TickUntil(() => rig.Brains.Vision.Holds(body, 0), 400);
        Assert.True(rig.Brains.Vision.Holds(body, 0), "the stimulus: it held him");

        rig.Heroes.Clear();
        int told = rig.Brains.HeroGone(rig.Now, Seat);
        rig.Tick(60);

        Assert.Equal(1, told);
        Assert.DoesNotContain(log.Events, e => e.Kind == AiEventKind.SightLost);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.ActorSpectating && e.Code == 1);
        Assert.Equal(CarriedMemory.None, rig.Roster.Carried(id.Index));
    }

    [Fact]
    public void His_respawn_tells_every_brain_he_is_back()
    {
        var log = new EventLog();
        var rig = new BrainRig(sink: log);
        rig.Lend(rig.Robot(Centre(0)));
        rig.Lend(rig.Robot(Centre(0) + At(0, 60), joinLastPack: true));
        rig.Hero(Seat, Centre(1));
        rig.Tick();
        rig.Brains.HeroGone(rig.Now, Seat);

        int told = rig.Brains.HeroBack(rig.Now, Seat);
        rig.Tick();

        Assert.Equal(2, told);
        Assert.Equal(2, log.Events.Count(e => e.Kind == AiEventKind.ActorSpectating && e.Code == 0));
    }

    //-------------------------------------------------------------------------
    // A family robot hit by him, then fighting him: once he is dead, the hit before the fight is no lead.

    private static string Leaf(BrainRig rig, int body) => rig.Brains.Session.BrainIn(body)?.Decider?.Doing.Leaf ?? "";

    /// <summary>
    /// A rocket soldier (its family's decider, as the game registers it) hit from behind by hero
    /// <see cref="Seat"/>; he then stands in front of it and it fights him; he dies and every brain is told.
    /// Answers the leaves its decider ends in over the three seconds after.
    /// </summary>
    private static List<string> AfterAFightThatBeganWithAHit()
    {
        var rig = new BrainRig(register: d => TF2.Descent.Ai.Families.HordeFamilies.Register(d, TF2.Descent.Ai.Families.FamilyNumbers.For(TheRules)));
        var id = rig.Robot(Centre(1));
        int body = rig.Lend(id, yaw: 0f);
        rig.Slide = false;
        rig.Hero(Seat, Centre(1) + At(-300, 0));   // behind it: out of its cone
        rig.Tick(30);
        rig.Brains.Hit(rig.Now + 1, id, Seat, 10f, false, rig.Poses.Position(body));
        rig.Tick(30);
        rig.Hero(Seat, Centre(1) + At(400, 0));    // now in front of it
        Assert.True(rig.TickUntil(() => rig.Brains.Vision.Holds(body, 0), 400) < 400, "the stimulus: it never saw him");
        rig.Tick(60);                               // it fights him

        rig.Heroes.Clear();
        rig.Brains.HeroGone(rig.Now, Seat);
        var leaves = new List<string>();
        for (int t = 0; t < 3 * (int)BrainRig.Tps; t++)
        {
            rig.Tick();
            leaves.Add(Leaf(rig, body));
        }

        return leaves;
    }

    [Fact]
    public void A_family_robot_that_fought_him_does_not_go_looking_for_the_hit_that_began_the_fight_once_he_is_dead()
    {
        var leaves = AfterAFightThatBeganWithAHit();

        Assert.DoesNotContain(HuntDecider.NameOf(HuntDecider.Node.Investigate), leaves);
        Assert.Equal(HuntDecider.NameOf(HuntDecider.Node.Hold), leaves[^1]);
    }

    //-------------------------------------------------------------------------
    // A robot of another pack heard the fight: the cry is a lead to the hero who caused it, gone with him.

    /// <summary>
    /// Two packs; hero <see cref="Seat"/> (out of everyone's sight: walls) hurts a robot of one, and a
    /// robot of the other stands in earshot. Then, when <paramref name="gone"/>, he dies and every brain
    /// is told. Answers the rig, its log, the stranger's body and the leaves it ends in over 3 s.
    /// </summary>
    private static (EventLog Log, ActorId Stranger, ActorId Hero, List<string> Leaves) AStrangerHeardTheFight(bool gone)
    {
        var log = new EventLog();
        var rig = new BrainRig(Row(4, markers: 3), sink: log);
        int pack = Enumerable.Range(0, rig.Roster.PackCount).First(p => rig.Roster.PackSize(p) >= 1);
        int other = Enumerable.Range(0, rig.Roster.PackCount).First(p => p != pack);
        var hit = rig.Roster.Id(rig.Roster.PackFirst(pack));
        var stranger = rig.Roster.Id(rig.Roster.PackFirst(other));
        rig.Roster.WakeRoom(rig.Roster.Room(hit.Index));
        rig.Roster.WakeRoom(rig.Roster.Room(stranger.Index));
        int hurt = rig.Lend(hit), body = rig.Lend(stranger);
        rig.Hero(Seat, Centre(3) + At(100, 100));
        rig.Lines.Clear = (_, _) => false;
        rig.Tick(30);
        // In earshot: within the pain's reach (horde.json's senses.hearing.pain) at the lowest hearing scale.
        rig.Poses.Set(body, rig.Poses.Position(hurt) + At(200, 0), 0f);
        rig.Tick(30);

        rig.Brains.Hit(rig.Now + 1, hit, Seat, 30f, killed: false, rig.Poses.Position(hurt));
        rig.Tick();
        if (gone)
        {
            rig.Heroes.Clear();
            rig.Brains.HeroGone(rig.Now, Seat);
        }

        var leaves = new List<string>();
        for (int t = 0; t < 3 * (int)BrainRig.Tps; t++)
        {
            rig.Tick();
            leaves.Add(Leaf(rig, body));
        }

        return (log, rig.Brains.BrainAt(body), rig.Brains.HeroSlots.ActorOf(Seat), leaves);
    }

    [Fact]
    public void A_robot_of_another_pack_hears_a_cry_as_a_lead_to_the_hero_who_caused_it()
    {
        var (log, stranger, hero, _) = AStrangerHeardTheFight(gone: false);

        var heard = Assert.Single(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == stranger);
        Assert.Equal(hero, heard.Other);
    }

    [Fact]
    public void While_he_lives_the_stranger_goes_to_look_where_the_cry_came_from()
    {
        var (_, _, _, leaves) = AStrangerHeardTheFight(gone: false);

        Assert.Contains(HuntDecider.NameOf(HuntDecider.Node.Investigate), leaves);   // the control for the fact below
    }

    [Fact]
    public void Once_he_is_dead_the_stranger_does_not_go_to_look_where_the_cry_came_from()
    {
        var (_, _, _, leaves) = AStrangerHeardTheFight(gone: true);

        Assert.DoesNotContain(HuntDecider.NameOf(HuntDecider.Node.Investigate), leaves);
    }

    /// <summary>
    /// One pack of two; hero <see cref="Seat"/> (out of sight: walls) hurts one, its mate hears it
    /// (AllyDamaged). Then, when <paramref name="gone"/>, he dies and every brain is told. Answers the
    /// leaves the mate's decider ends in over 3 s.
    /// </summary>
    private static List<string> AMateHeardTheFight(bool gone)
    {
        var rig = new BrainRig(Row(4, markers: 3));
        int pack = Enumerable.Range(0, rig.Roster.PackCount).First(p => rig.Roster.PackSize(p) >= 2);
        var hit = rig.Roster.Id(rig.Roster.PackFirst(pack));
        var mate = rig.Roster.Id(rig.Roster.PackFirst(pack) + 1);
        rig.Roster.WakeRoom(rig.Roster.Room(hit.Index));
        int hurt = rig.Lend(hit), body = rig.Lend(mate);
        rig.Hero(Seat, Centre(3) + At(100, 100));
        rig.Lines.Clear = (_, _) => false;
        rig.Tick(30);
        rig.Poses.Set(body, rig.Poses.Position(hurt) + At(300, 0), 0f);
        rig.Tick(30);

        rig.Brains.Hit(rig.Now + 1, hit, Seat, 30f, killed: false, rig.Poses.Position(hurt));
        rig.Tick();
        if (gone)
        {
            rig.Heroes.Clear();
            rig.Brains.HeroGone(rig.Now, Seat);
        }

        var leaves = new List<string>();
        for (int t = 0; t < 3 * (int)BrainRig.Tps; t++)
        {
            rig.Tick();
            leaves.Add(Leaf(rig, body));
        }

        return leaves;
    }

    [Fact]
    public void While_he_lives_a_mate_goes_to_where_its_pack_mate_was_hurt()
    {
        Assert.Contains(HuntDecider.NameOf(HuntDecider.Node.Investigate), AMateHeardTheFight(gone: false));   // the control
    }

    [Fact]
    public void Once_he_is_dead_a_mate_does_not_go_to_where_he_hurt_its_pack_mate()
    {
        Assert.DoesNotContain(HuntDecider.NameOf(HuntDecider.Node.Investigate), AMateHeardTheFight(gone: true));
    }

    //-------------------------------------------------------------------------
    // The whole sequence (item 3 of the brief): kill, forget, respawn in sight with grace,
    // the grace ends with him behind them: no engage; he fires: engage.

    private sealed class Pack
    {
        public BrainRig Rig { get; }
        public EventLog SinkLog { get; } = new();
        public int[] Bodies { get; }

        public Pack()
        {
            Rig = new BrainRig(sink: SinkLog);
            var first = Rig.Robot(Centre(0) + At(-120, 0));
            Bodies =
            [
                Rig.Lend(first),
                Rig.Lend(Rig.Robot(Centre(0) + At(-120, 80), joinLastPack: true)),
                Rig.Lend(Rig.Robot(Centre(0) + At(-120, -80), joinLastPack: true)),
            ];
            Rig.Slide = false;   // each body stays where the fact puts it, facing where the fact turns it
        }

        public bool AnyFighting => Bodies.Any(b => Fighting(Rig.NodeOf(b)));

        public int Gains(long since) => SinkLog.Events.Count(e => e.Kind == AiEventKind.SightGained && e.Tick.Value > since);

        public void Face(float yaw)
        {
            foreach (int b in Bodies)
                Rig.Poses.Set(b, Rig.Poses.Position(b), yaw);
        }
    }

    /// <summary>
    /// The pack fought the hero standing in front of them (east) and killed him; every brain was
    /// told (the downed state's tap); two seconds on, each has nothing left to hunt.
    /// </summary>
    private static Pack KilledHim()
    {
        var p = new Pack();
        var where = Centre(0) + At(250, 0);
        p.Rig.Hero(Seat, where);
        Assert.True(p.Rig.TickUntil(() => p.Bodies.All(b => p.Rig.Brains.Vision.Holds(b, 0)) && p.AnyFighting, 600) < 600,
                    "the stimulus: the pack never saw him and fought");

        p.Rig.Heroes.Clear();
        p.Rig.Brains.HeroGone(p.Rig.Now, Seat);
        p.Rig.Tick(2 * (int)BrainRig.Tps);
        Assert.False(p.AnyFighting);
        return p;
    }

    /// <summary>He stands again where he fell, in the pack's view, in his grace; every brain is told he is back.</summary>
    private static long Respawned(Pack p)
    {
        p.Rig.Hero(Seat, Centre(0) + At(250, 0), veil: HeroVeil.Hidden);
        p.Rig.Brains.HeroBack(p.Rig.Now, Seat);
        return p.Rig.Now;
    }

    [Fact]
    public void Respawned_in_full_view_of_the_pack_that_killed_him_he_is_not_seen_through_his_grace()
    {
        var p = KilledHim();
        long at = Respawned(p);

        bool fought = false;
        for (int t = 0; t < GraceTicks; t++)
        {
            p.Rig.Tick();
            fought |= p.AnyFighting;
        }

        Assert.Equal(0, p.Gains(at));
        Assert.False(fought);
        Assert.All(p.Bodies, b => Assert.True(p.Rig.Brains.Vision.Watching(b, 0)));   // they had him in view all the while
    }

    [Fact]
    public void After_his_grace_standing_still_behind_them_he_is_left_alone_and_when_he_fires_they_engage()
    {
        var p = KilledHim();
        long at = Respawned(p);
        p.Rig.Tick(GraceTicks / 2);
        p.Face(180f);   // they turn for home: their backs to him
        p.Rig.Tick(GraceTicks - GraceTicks / 2);

        // The grace lapses by time; he stands where he is and does nothing.
        p.Rig.Hero(Seat, Centre(0) + At(250, 0), veil: HeroVeil.Watched);
        bool fought = false;
        for (int t = 0; t < 10 * (int)BrainRig.Tps; t++)
        {
            p.Rig.Tick();
            fought |= p.AnyFighting;
        }

        Assert.Equal(0, p.Gains(at));
        Assert.False(fought);

        // He fires: the grace's veil is gone, and the shot is heard.
        long shot = p.Rig.Now;
        p.Rig.Hero(Seat, Centre(0) + At(250, 0));
        p.Rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, Centre(0) + At(250, 0), 1500f, Seat, p.Rig.Level.RoomAt(Centre(0) + At(250, 0))));
        p.Rig.Slide = true;   // they may turn and move again
        Assert.True(p.Rig.TickUntil(() => p.AnyFighting, 10 * (int)BrainRig.Tps) < 10 * (int)BrainRig.Tps, "the pack never engaged after his shot");
        Assert.True(p.Gains(shot) > 0);
    }

    [Fact]
    public void After_his_grace_standing_still_in_their_view_the_robots_that_watched_him_stay_blind()
    {
        var p = KilledHim();
        long at = Respawned(p);
        p.Rig.Tick(GraceTicks);

        p.Rig.Hero(Seat, Centre(0) + At(250, 0), veil: HeroVeil.Watched);
        bool fought = false;
        for (int t = 0; t < 10 * (int)BrainRig.Tps; t++)
        {
            p.Rig.Tick();
            fought |= p.AnyFighting;
        }

        Assert.Equal(0, p.Gains(at));
        Assert.False(fought);
    }
}
