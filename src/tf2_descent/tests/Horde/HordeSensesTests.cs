using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's hearing and gameplay events (plan_descent_logic HO5, plan_tf2_rpg
/// B.3) with no engine: a sound is heard by path distance through the room
/// graph, never through a wall; a hit is <c>Damaged</c> for its robot and a pain
/// heard by its pack-mates in earshot; a hero's death is seen by those that saw it.
/// </summary>
public sealed class HordeSensesTests
{
    /// <summary>
    /// Three rooms in a row along X (0, 1, 2) and a long room 3 above all three,
    /// joined only to room 2. A point in room 1 and one in room 3 are both 512
    /// units in a straight line from the middle of room 0; the walk to the first
    /// is 512 through one door, to the second three doors round.
    /// </summary>
    private static HordeLevel U() => HordeLevel.Create(5, 1,
        [
            new HordeRoom(0, At(0, 0, 0), At(512, 512, 256), []),
            new HordeRoom(1, At(512, 0, 0), At(1024, 512, 256), []),
            new HordeRoom(2, At(1024, 0, 0), At(1536, 512, 256), []),
            new HordeRoom(3, At(0, 513, 0), At(1536, 1536, 256), []),
        ],
        [
            new HordeDoor(0, 1, At(512, 256), At(1, 0, 0), 64),
            new HordeDoor(1, 2, At(1024, 256), At(1, 0, 0), 64),
            new HordeDoor(2, 3, At(1280, 512), At(0, 1, 0), 64),
        ]);

    [Fact]
    public void A_sound_in_the_next_room_through_its_door_is_heard_and_one_as_near_through_a_wall_is_not()
    {
        var log = new EventLog();
        var rig = new BrainRig(U(), sink: log);
        var me = At(256, 256);
        int body = rig.Lend(rig.Robot(me));
        rig.Tick();
        var actor = rig.Brains.BrainAt(body);
        float reach = 560f / rig.Brains.SenseOf(body).Hearing;
        AiVector nextRoom = At(768, 256), behindWall = At(256, 768);
        Assert.Equal(nextRoom.DistanceTo(me), behindWall.DistanceTo(me));

        // A hero's shots (seat 1): the hearing's rooms are a hero's (horde.json's senses.hearing.rooms).
        rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, behindWall, reach, 1, rig.Level.RoomAt(behindWall)));
        rig.Tick();
        Assert.DoesNotContain(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == actor);

        rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, nextRoom, reach, 1, rig.Level.RoomAt(nextRoom)));
        rig.Tick();
        var heard = Assert.Single(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == actor);
        Assert.Equal(nextRoom, heard.Position);
        Assert.InRange(heard.Amount, 0.01f, 0.2f);
    }

    [Fact]
    public void Hearing_reaches_farther_the_more_alert_the_robot()
    {
        var rig = new BrainRig(U());
        var hearing = new List<float>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            int body = rig.Lend(rig.Robot(At(64 + seed, 128), seed: seed));
            rig.Tick();
            int alertness = rig.Roster.Temperament(rig.Brains.RowAt(body)).Levels[TheRules.Temperaments.Index("alertness")];
            hearing.Add(alertness * 1000 + rig.Brains.SenseOf(body).Hearing);
        }

        // Ordered by Alertness, the hearing scale never falls.
        var byLevel = hearing.Select(h => (Level: (int)(h / 1000), Scale: h % 1000)).OrderBy(x => x.Level).ToArray();
        Assert.True(byLevel.Select(x => x.Level).Distinct().Count() >= 3, "the control: the rolls cover three Alertness levels");
        for (int i = 1; i < byLevel.Length; i++)
            Assert.True(byLevel[i].Scale >= byLevel[i - 1].Scale, $"alertness {byLevel[i].Level} hears at {byLevel[i].Scale}, below {byLevel[i - 1].Scale}");
    }

    /// <summary>Two robots of one rolled pack and one of another, all lent bodies.</summary>
    private static (BrainRig Rig, EventLog Log, RobotId Hit, RobotId Mate, RobotId Stranger) Pack()
    {
        var log = new EventLog();
        var rig = new BrainRig(Row(4, markers: 3), sink: log);
        int pack = Enumerable.Range(0, rig.Roster.PackCount).First(p => rig.Roster.PackSize(p) >= 2);
        int other = Enumerable.Range(0, rig.Roster.PackCount).First(p => p != pack);
        var hit = rig.Roster.Id(rig.Roster.PackFirst(pack));
        var mate = rig.Roster.Id(rig.Roster.PackFirst(pack) + 1);
        var stranger = rig.Roster.Id(rig.Roster.PackFirst(other));
        foreach (var id in new[] { hit, mate, stranger })
        {
            rig.Roster.WakeRoom(rig.Roster.Room(id.Index));
            rig.Lend(id);
        }

        rig.Hero(4, Centre(3) + At(100, 100));
        rig.Lines.Clear = (_, _) => false;
        rig.Tick();
        return (rig, log, hit, mate, stranger);
    }

    [Fact]
    public void A_hit_is_damage_to_its_robot_from_the_heros_seat()
    {
        var (rig, log, hit, _, _) = Pack();
        int body = rig.Roster.Body(hit.Index);
        rig.Brains.Hit(rig.Now, hit, 4, 30f, false, rig.Poses.Position(body));
        var damaged = Assert.Single(log.Events, e => e.Kind == AiEventKind.Damaged);
        Assert.Equal(rig.Brains.BrainAt(body), damaged.Bot);
        Assert.Equal(rig.Brains.HeroSlots.ActorOf(4), damaged.Other);
        Assert.Equal(30f, damaged.Amount);
        Assert.Equal(4, rig.Roster.Carried(hit.Index).ThreatSeat);
    }

    [Fact]
    public void A_pack_mate_in_earshot_is_told_its_ally_died_and_by_whom_and_hears_no_sound()
    {
        var (rig, log, hit, mate, stranger) = Pack();
        int body = rig.Roster.Body(hit.Index);
        var hitActor = rig.Brains.BrainAt(body);
        rig.Brains.Hit(rig.Now, hit, 4, 30f, killed: true, rig.Poses.Position(body));
        var mateActor = rig.Brains.BrainAt(rig.Roster.Body(mate.Index));
        var strangerActor = rig.Brains.BrainAt(rig.Roster.Body(stranger.Index));
        var died = Assert.Single(log.Events, e => e.Kind == AiEventKind.AllyDied && e.Bot == mateActor);
        Assert.Equal(hitActor, died.Other);
        Assert.Equal(rig.Brains.HeroSlots.ActorOf(4), died.Killer);
        Assert.Equal(rig.Poses.Position(body), died.Position);
        Assert.DoesNotContain(log.Events, e => e.Bot == mateActor && e.Kind == AiEventKind.SoundHeard);
        Assert.DoesNotContain(log.Events, e => e.Bot == strangerActor && e.Kind is AiEventKind.AllyDied or AiEventKind.AllyDamaged or AiEventKind.Damaged);
    }

    [Fact]
    public void A_pack_mate_in_earshot_is_told_its_ally_was_hurt_how_much_and_by_whom()
    {
        var (rig, log, hit, mate, _) = Pack();
        int body = rig.Roster.Body(hit.Index);
        rig.Brains.Hit(rig.Now, hit, 4, 30f, killed: false, rig.Poses.Position(body));
        var mateActor = rig.Brains.BrainAt(rig.Roster.Body(mate.Index));
        var hurt = Assert.Single(log.Events, e => e.Kind == AiEventKind.AllyDamaged && e.Bot == mateActor);
        Assert.Equal(rig.Brains.BrainAt(body), hurt.Other);
        Assert.Equal(rig.Brains.HeroSlots.ActorOf(4), hurt.Killer);
        Assert.Equal(30f, hurt.Amount);
        Assert.Equal(1, rig.Brains.AlliesTold);
    }

    [Fact]
    public void A_robot_of_another_pack_in_earshot_hears_the_death_as_a_sound_and_one_out_of_it_hears_nothing()
    {
        var (rig, log, hit, _, stranger) = Pack();
        int body = rig.Roster.Body(hit.Index), other = rig.Roster.Body(stranger.Index);
        var strangerActor = rig.Brains.BrainAt(other);
        var at = rig.Poses.Position(body);

        rig.Poses.Set(other, at + At(150, 0), 0f);
        rig.Brains.Hit(rig.Now, hit, 4, 30f, killed: true, at);
        var heard = Assert.Single(log.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot == strangerActor);
        Assert.Equal((int)SoundKind.Death, heard.Code);
        Assert.Equal(at, heard.Position);

        log.Events.Clear();
        rig.Poses.Set(other, Centre(3) + At(400, 0), 0f);
        rig.Brains.Hit(rig.Now, hit, 4, 30f, killed: true, at);
        Assert.DoesNotContain(log.Events, e => e.Bot == strangerActor);
    }

    [Fact]
    public void A_robot_with_nothing_to_hunt_goes_to_where_a_pack_mate_was_hurt()
    {
        var (rig, _, hit, mate, _) = Pack();
        int body = rig.Roster.Body(hit.Index), mateBody = rig.Roster.Body(mate.Index);
        rig.Slide = false;
        var at = rig.Poses.Position(body) + At(0, 260);
        rig.Poses.Set(body, at, 0f);
        rig.Tick(2 * DecideSchedule.Period);
        Assert.False(rig.Orders.Now.TryGetValue(mateBody, out var before) && before.Move.Kind == MoveKind.Follow, "the control: the mate was already walking");

        rig.Brains.Hit(rig.Now, hit, -1, 30f, killed: false, at);
        rig.Tick(2 * DecideSchedule.Period);

        Assert.True(rig.Orders.Now.TryGetValue(mateBody, out var order), "the mate stayed where it was");
        Assert.Equal(MoveKind.Follow, order.Move.Kind);
        Assert.Equal(at, order.Move.Corner(order.Move.CornerCount - 1));
    }

    [Fact]
    public void A_heroes_death_is_told_only_to_robots_that_held_sight_of_it()
    {
        var log = new EventLog();
        var rig = new BrainRig(sink: log);
        int seeing = rig.Lend(rig.Robot(Centre(0)), yaw: 0f);
        int blind = rig.Lend(rig.Robot(Centre(0) + At(0, 50), seed: 7), yaw: 180f);
        rig.Slide = false;
        rig.Hero(2, Centre(0) + At(200, 0));
        rig.TickUntil(() => rig.Brains.Vision.Gains > 0, 200);
        rig.Brains.HeroDied(rig.Now, 2, ActorId.None);
        var told = Assert.Single(log.Events, e => e.Kind == AiEventKind.KillFeed);
        Assert.Equal(rig.Brains.BrainAt(seeing), told.Bot);
        Assert.Equal(rig.Brains.HeroSlots.ActorOf(2), told.Other);
        _ = blind;
    }
}
