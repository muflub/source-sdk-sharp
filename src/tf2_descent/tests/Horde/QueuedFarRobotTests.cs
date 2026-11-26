using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A far pack sent to a hero and stopped at a door because he sees into the room beyond it
/// (the far simulation's queue): live on level 5's maze (2026-11-21) a hero's PVS reached rooms
/// three and four doors off, the queue stood beyond the wake distance, no body was ever lent there
/// and the pack waited out its alert and went home. A queued robot takes a body where it stands,
/// unseen, whatever its distance; one on its way that is not queued keeps the wake distance.
/// <para>The leash (plan L25, 2026-11-22) keeps a pack within three doors of its room, so a queue
/// beyond the wake distance (two rooms) of the hero it was sent to stands in the pack's own room:
/// these facts send it to a hero three doors off.</para>
/// </summary>
public sealed class QueuedFarRobotTests
{
    private static readonly long Interval = FarSimulation.Interval(TheRules.Content.Horde, RosterTests.TicksPerSecond);

    /// <summary>
    /// Six rooms in a row, the only pack in room 5, the hero in room 2 (on the pack's leash of three
    /// doors) seeing into rooms 2 to <paramref name="sees"/>; the pack sent to him.
    /// </summary>
    private static (HordeRig Rig, FarSimulation Far, int[] Pack) Sent(int sees = 4)
    {
        var level = HordeLevel.Create(11, 4, Enumerable.Range(0, 6).Select(i => RowRoom(i, i == 5 ? 2 : 0)), Enumerable.Range(0, 5).Select(RowDoor));
        var rig = new HordeRig(level);
        var far = FarSimulation.For(TheRules.Content.Horde, level, RosterTests.TicksPerSecond);
        Assert.Equal(TheRules.Content.Horde.LeashDoors, level.Distance(5, 2));
        rig.HeroIn(2);
        for (int r = 2; r <= sees; r++)
            rig.Sight.SeeInto[r] = true;
        rig.Roster.AlertPack(0, 2, Centre(2), rig.Now, 1);
        int[] pack = [.. Enumerable.Range(rig.Roster.PackFirst(0), rig.Roster.PackSize(0))];
        return (rig, far, pack);
    }

    private static void Walk(HordeRig rig, FarSimulation far, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            rig.Now += Interval;
            far.Tick(rig.Roster, rig.Sight, [], rig.Now);
        }
    }

    [Fact]
    public void A_far_robot_queued_at_a_door_beyond_the_wake_distance_takes_a_body_where_it_stands()
    {
        var (rig, far, pack) = Sent();
        Walk(rig, far, 40);
        Assert.All(pack, i => Assert.Equal((5, FarActivity.Queue), (rig.Roster.Room(i), rig.Roster.Activity(i))));
        Assert.True(rig.Level.Distance(2, 5) > TheRules.Content.Horde.WakeRooms, "the queue is beyond the wake distance");

        rig.Decide();

        Assert.Equal(pack, rig.Promoted().Order().ToArray());
        Assert.All(rig.Of(HordeOrderKind.Promote), o => Assert.Equal(rig.Roster.Position(o.Robot.Index), o.Position));
    }

    [Fact]
    public void A_queued_robot_a_hero_can_see_is_still_given_no_body()
    {
        var (rig, far, pack) = Sent();
        Walk(rig, far, 40);
        rig.Sight.SeePointsIn[5] = true;

        rig.Decide();

        Assert.Empty(rig.Promoted());
    }

    [Fact]
    public void A_robot_given_a_body_on_its_way_to_its_packs_contact_goes_on_to_look_there()
    {
        var rig = new BrainRig(Row(5, markers: 0));
        var robot = rig.Robot(Centre(1));
        rig.Roster.AlertPack(rig.Roster.Pack(robot.Index), 4, Centre(4), rig.Now, 1);
        Assert.Equal(-1, rig.Roster.Carried(robot.Index).TargetSeat);
        rig.Lines.Clear = (_, _) => false;
        rig.Slide = false;
        rig.Hero(1, Centre(4));
        int body = rig.Lend(robot);

        rig.Tick(30);

        Assert.Equal(HuntDecider.Node.Investigate, rig.NodeOf(body));
    }

    [Fact]
    public void A_robot_given_a_body_with_its_pack_calm_stays_home()
    {
        var rig = new BrainRig(Row(5, markers: 0));
        var robot = rig.Robot(Centre(1));
        rig.Lines.Clear = (_, _) => false;
        rig.Slide = false;
        rig.Hero(1, Centre(4));
        int body = rig.Lend(robot);

        rig.Tick(30);

        Assert.Contains(rig.NodeOf(body), new HuntDecider.Node?[] { HuntDecider.Node.Hold, HuntDecider.Node.Home });
    }

    [Fact]
    public void A_far_robot_on_its_way_beyond_the_wake_distance_is_not_given_a_body()
    {
        var (rig, far, pack) = Sent(sees: 2);
        Walk(rig, far, 1);
        Assert.Contains(pack, i => rig.Roster.Activity(i) == FarActivity.Converge && rig.Roster.Room(i) >= 5);

        rig.Decide();

        Assert.DoesNotContain(rig.Promoted(), i => rig.Roster.Room(i) >= 5);
    }
}
