using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Who gets a body (plan_descent_logic 3.4, 3.10): a fact per rule, each with its negative.
/// The rules decide; the facts read the orders and, where a fact needs it, apply them.
/// </summary>
public sealed class EmbodimentTests
{
    private const long Second = 66;

    //-------------------------------------------------------------------------
    // Wake
    //-------------------------------------------------------------------------

    [Fact]
    public void A_room_two_rooms_from_a_hero_wakes_and_its_robots_take_bodies_at_their_posts()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        int[] room2 = rig.In(2);

        var orders = rig.Decide();

        Assert.Contains(HordeOrder.Wake(2), orders.AsSpan().ToArray());
        Assert.NotEmpty(room2);
        Assert.All(room2, i => Assert.Contains(i, rig.Promoted()));
        Assert.All(rig.Of(HordeOrderKind.Promote).Where(o => room2.Contains(o.Robot.Index)),
            o => Assert.Equal(rig.Roster.Post(o.Robot.Index), o.Position));
    }

    [Fact]
    public void A_room_three_rooms_from_a_hero_stays_asleep_and_bodiless()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);

        rig.Step();

        Assert.False(rig.Roster.RoomAwake(3));
        Assert.All(rig.In(3), i => Assert.Equal(RobotState.Asleep, rig.Roster.State(i)));
        Assert.DoesNotContain(rig.Promoted(), i => rig.Roster.Room(i) == 3);
    }

    //-------------------------------------------------------------------------
    // Never in view
    //-------------------------------------------------------------------------

    [Fact]
    public void A_robot_whose_spot_a_hero_can_see_is_not_given_a_body()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        rig.Sight.SeePointsIn[2] = true;

        rig.Decide();

        Assert.NotEmpty(rig.In(2));
        Assert.DoesNotContain(rig.Promoted(), i => rig.Roster.Room(i) == 2);
        Assert.Contains(rig.Promoted(), i => rig.Roster.Room(i) == 1);
    }

    [Fact]
    public void A_body_a_hero_can_see_is_never_demoted_however_long_no_hero_is_near()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        rig.Step();
        int[] bodied = [.. Enumerable.Range(0, rig.Roster.Count).Where(i => rig.Roster.State(i) == RobotState.Embodied)];
        rig.Heroes[0] = new HeroPlace(0, 7, Centre(7));
        foreach (int i in bodied) rig.Sight.Points.Add(rig.Roster.Position(i));

        rig.Now += 60 * Second;
        rig.DecideOnly();   // the bookkeeping says no hero near for a minute; sight alone must hold them

        Assert.NotEmpty(bodied);
        Assert.Empty(rig.Demoted().Intersect(bodied));
    }

    [Fact]
    public void An_unseen_body_with_no_hero_near_for_the_delay_is_demoted()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        rig.Step();
        int[] bodied = [.. Enumerable.Range(0, rig.Roster.Count).Where(i => rig.Roster.State(i) == RobotState.Embodied)];
        rig.Heroes[0] = new HeroPlace(0, 7, Centre(7));

        rig.Step(10 * Second);

        Assert.NotEmpty(bodied);
        Assert.Equal(bodied, rig.Demoted().Intersect(bodied).Order());
        Assert.All(bodied, i => Assert.Equal((RobotState.Far, 0), (rig.Roster.State(i), rig.Roster.Body(i))));
    }

    [Fact]
    public void A_body_is_kept_until_the_delay_has_passed()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        rig.Step();
        rig.Heroes[0] = new HeroPlace(0, 7, Centre(7));

        rig.Step(10 * Second - 1);

        Assert.DoesNotContain(rig.Demoted(), i => rig.Roster.HomeRoom(i) <= 2);
    }

    //-------------------------------------------------------------------------
    // Priority under a short pool
    //-------------------------------------------------------------------------

    /// <summary>Eight rooms in a row, packs only in rooms 0 and 7, at depth 15 for a party of four.</summary>
    private static HordeLevel TwoPacks(ulong seed) =>
        HordeLevel.Create(seed, 15, Enumerable.Range(0, 8).Select(i => RowRoom(i, i is 0 or 7 ? 3 : 0)), Enumerable.Range(0, 7).Select(RowDoor));

    /// <summary>The first seed whose room-0 pack has a champion and three ordinary robots.</summary>
    private static ulong SeedWithAChampion()
    {
        for (ulong seed = 1; seed < 5000; seed++)
        {
            var pack = TheRules.Packs.RollPack(seed, 0, 15, 4, 3);
            // A champion after an ordinary robot, so the champion's rank is the only thing that puts it first.
            int champion = pack.Robots.IndexOf(pack.Robots.FirstOrDefault(r => r.Champion)!);
            if (champion > 0 && !pack.Robots[0].Champion && pack.Robots.Count(r => !r.Champion) >= 3) return seed;
        }
        throw new InvalidOperationException("no seed below 5000 rolls an ordinary robot, then a champion, and three ordinary robots in room 0");
    }

    /// <summary>
    /// A hero in room 3 and five far claimants: A in the hero's room, B of the other pack (alerted
    /// to the hero) two rooms off, C ordinary one room off, D a champion one room off, E two rooms
    /// off. Everyone else is dead.
    /// </summary>
    private static (HordeRig Rig, int A, int B, int C, int D, int E) FiveClaimants(int poolBodies, int championRoom = 4)
    {
        var rig = new HordeRig(TwoPacks(SeedWithAChampion()), 4, new BodyPool(poolBodies, 1));
        var r = rig.Roster;
        int[] ordinary = [.. Enumerable.Range(r.PackFirst(0), r.PackSize(0)).Where(i => !r.IsChampion(i))];
        int d = Enumerable.Range(r.PackFirst(0), r.PackSize(0)).First(r.IsChampion);
        int c = ordinary[0], a = ordinary[1], e = ordinary[2], b = r.PackFirst(1);
        Assert.True(c < d, "the ordinary robot must be the lower row, or the row alone would put the champion second");
        r.WakeRoom(0);
        r.WakeRoom(7);
        rig.Place(a, 3);
        rig.Place(b, 5);
        rig.Place(c, 2);
        rig.Place(d, championRoom);
        rig.Place(e, 1);
        r.AlertPack(1, 3, Centre(3), rig.Now, 0);
        rig.KillAllBut(a, b, c, d, e);
        rig.HeroIn(3);
        return (rig, a, b, c, d, e);
    }

    [Fact]
    public void A_pool_of_two_goes_to_the_robot_in_the_heros_room_then_the_alerted_pack()
    {
        var (rig, a, b, _, _, _) = FiveClaimants(2);

        rig.Decide();

        Assert.Equal([a, b], rig.Promoted());
    }

    [Fact]
    public void Past_the_alerted_pack_the_nearer_robot_comes_first_and_a_champion_first_at_one_distance()
    {
        var (rig, a, b, c, d, e) = FiveClaimants(4);

        rig.Decide();

        Assert.Equal([a, b, d, c], rig.Promoted());
        Assert.DoesNotContain(e, rig.Promoted());
    }

    [Fact]
    public void A_champion_further_off_does_not_outrank_a_nearer_ordinary_robot()
    {
        var (rig, a, b, c, d, _) = FiveClaimants(3, championRoom: 5);

        rig.Decide();

        Assert.Equal([a, b, c], rig.Promoted());
        Assert.DoesNotContain(d, rig.Promoted());
    }

    [Fact]
    public void A_pack_alerted_to_no_hero_is_on_the_distance_rung()
    {
        var (rig, a, b, c, d, e) = FiveClaimants(5);
        rig.Roster.AlertPack(1, 3, Centre(3), rig.Now + 1, -1);   // a later contact that was no hero

        rig.Decide();

        // B is two rooms off, so it now comes after the robots one room off.
        Assert.Equal([a, d, c], rig.Promoted()[..3]);
        Assert.Equal(new[] { b, e }.Order(), rig.Promoted()[3..].Order());
    }

    //-------------------------------------------------------------------------
    // Pressure
    //-------------------------------------------------------------------------

    /// <summary>A hero in room 4; X and Y bodied two rooms off, in a pool of two; Z far one room off; everyone else dead.</summary>
    private static (HordeRig Rig, int X, int Y, int Z) Pressure()
    {
        var rig = new HordeRig(TwoPacks(SeedWithAChampion()), 4, new BodyPool(2, 1));
        var r = rig.Roster;
        int[] ordinary = [.. Enumerable.Range(r.PackFirst(0), r.PackSize(0)).Where(i => !r.IsChampion(i))];
        int x = ordinary[0], y = ordinary[1], z = ordinary[2];
        rig.Embody(x, 1);
        rig.Embody(y, 2);
        rig.Place(x, 2);
        rig.Place(y, 2);
        rig.Place(z, 3);
        rig.KillAllBut(x, y, z);
        rig.HeroIn(4);
        return (rig, x, y, z);
    }

    [Fact]
    public void Under_pressure_the_lowest_ranked_unseen_body_goes_to_the_higher_ranked_claimant()
    {
        var (rig, _, y, z) = Pressure();

        rig.Decide();

        Assert.Equal([HordeOrder.Demote(rig.Roster.Id(y), 2), HordeOrder.Promote(rig.Roster.Id(z), 2, 3, rig.Roster.Position(z), 0)],
            rig.Orders.AsSpan().ToArray().Where(o => o.Kind is HordeOrderKind.Demote or HordeOrderKind.Promote));
    }

    [Fact]
    public void Under_pressure_a_seen_body_is_passed_over_for_the_next_unseen_one()
    {
        var (rig, x, y, z) = Pressure();
        rig.Sight.Points.Add(rig.Roster.Position(y));

        rig.Decide();

        Assert.Equal([x], rig.Demoted());
        Assert.Equal([z], rig.Promoted());
    }

    [Fact]
    public void Under_pressure_no_seen_body_is_ever_taken()
    {
        var (rig, x, y, _) = Pressure();
        rig.Sight.Points.Add(rig.Roster.Position(x));
        rig.Sight.Points.Add(rig.Roster.Position(y));

        rig.Decide();

        Assert.Empty(rig.Demoted());
        Assert.Empty(rig.Promoted());
    }

    [Fact]
    public void A_claimant_that_ranks_below_every_holder_takes_no_body()
    {
        var (rig, x, y, z) = Pressure();
        rig.Place(z, 2);
        rig.Place(x, 3);
        rig.Place(y, 3);

        rig.Decide();

        Assert.Empty(rig.Demoted());
        Assert.Empty(rig.Promoted());
    }

    //-------------------------------------------------------------------------
    // The boss
    //-------------------------------------------------------------------------

    private static HordeRig BossLevel(int poolBodies = 48) =>
        new(Row(6, boss: new HordeBoss("captain_punch", 2, new PackMarker(At(1100, 300), 0))), 1, new BodyPool(poolBodies, 1));

    [Fact]
    public void The_boss_takes_the_boss_body_and_no_pool_body()
    {
        var rig = BossLevel();
        rig.HeroIn(0);

        rig.Decide();

        var boss = rig.Of(HordeOrderKind.Promote).Single(o => o.Robot.Index == 0);
        Assert.Equal(rig.Pool.BossBody, boss.Body);
        Assert.All(rig.Of(HordeOrderKind.Promote).Where(o => o.Robot.Index != 0), o => Assert.False(rig.Pool.IsBossBody(o.Body)));
    }

    [Fact]
    public void The_boss_takes_its_body_with_the_pool_empty()
    {
        var rig = BossLevel(poolBodies: 0);
        rig.HeroIn(0);

        rig.Decide();

        Assert.Equal([0], rig.Promoted());
    }

    /// <summary>HO13: a hero brought straight into the arena (a teleport, a portal) sees the boss's spot; the boss still takes its body.</summary>
    [Fact]
    public void The_boss_takes_its_body_in_view_once_a_hero_stands_in_its_arena()
    {
        var rig = BossLevel();
        rig.HeroIn(2);
        rig.Sight.SeePointsIn[2] = true;

        rig.Decide();

        Assert.Contains(0, rig.Promoted());
    }

    [Fact]
    public void The_boss_waits_out_of_view_while_no_hero_stands_in_its_arena()
    {
        var rig = BossLevel();
        rig.HeroIn(1);
        rig.Sight.SeePointsIn[2] = true;   // a hero next door looking in

        rig.Decide();

        Assert.DoesNotContain(0, rig.Promoted());
    }

    [Fact]
    public void The_boss_is_not_demoted_while_a_hero_is_in_its_arena()
    {
        var rig = BossLevel();
        rig.HeroIn(2);
        rig.Step();
        Assert.Equal(RobotState.Embodied, rig.Roster.State(0));

        rig.Now += 120 * Second;
        rig.DecideOnly();

        Assert.DoesNotContain(0, rig.Demoted());
    }

    [Fact]
    public void The_boss_is_demoted_once_no_hero_is_in_its_arena_or_near()
    {
        var rig = BossLevel();
        rig.HeroIn(2);
        rig.Step();
        rig.Heroes[0] = new HeroPlace(0, 5, Centre(5));

        rig.Now += 120 * Second;
        rig.DecideOnly();

        Assert.Contains(0, rig.Demoted());
    }

    [Fact]
    public void Pressure_never_takes_the_bosss_body()
    {
        var rig = BossLevel(poolBodies: 0);
        rig.HeroIn(0);
        rig.Step();
        Assert.Equal(RobotState.Embodied, rig.Roster.State(0));
        int claimant = rig.In(1)[0];

        rig.Decide();

        Assert.Equal(RobotState.Far, rig.Roster.State(claimant));
        Assert.Empty(rig.Demoted());
    }

    //-------------------------------------------------------------------------
    // Death and sleep
    //-------------------------------------------------------------------------

    [Fact]
    public void A_dead_robots_body_is_released_even_in_view()
    {
        var rig = new HordeRig(Row(4));
        rig.HeroIn(0);
        rig.Step();
        int i = rig.In(0)[0];
        int body = rig.Roster.Body(i);
        rig.Roster.Hurt(i, 1e12);
        rig.Sight.Points.Add(rig.Roster.Position(i));

        rig.Step();

        Assert.Contains(HordeOrder.Release(rig.Roster.Id(i), body), rig.Orders.AsSpan().ToArray());
        Assert.Equal((RobotState.Dead, 0), (rig.Roster.State(i), rig.Roster.Body(i)));
        Assert.NotEqual(rig.Roster.Id(i), rig.Pool.Holder(body));
    }

    [Fact]
    public void A_room_with_no_hero_near_for_the_delay_and_no_body_in_it_sleeps()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        rig.Step();
        rig.Heroes[0] = new HeroPlace(0, 7, Centre(7));
        rig.Step(10 * Second);   // the bodies go back

        rig.Step(10 * Second);

        Assert.All([0, 1, 2], r => Assert.False(rig.Roster.RoomAwake(r)));
        Assert.All(rig.In(1), i => Assert.Equal(RobotState.Asleep, rig.Roster.State(i)));
    }

    [Fact]
    public void An_empty_room_a_hero_has_left_sleeps_at_the_delay_and_not_before()
    {
        var rig = new HordeRig(TwoPacks(1));
        rig.HeroIn(3);
        rig.Step();
        Assert.True(rig.Roster.RoomAwake(1));
        rig.Heroes[0] = new HeroPlace(0, 7, Centre(7));

        rig.Step(10 * Second - 1);
        Assert.True(rig.Roster.RoomAwake(1));

        rig.Step(1);
        Assert.False(rig.Roster.RoomAwake(1));
        Assert.True(rig.Roster.RoomAwake(5));
    }

    [Fact]
    public void A_room_with_a_body_in_it_does_not_sleep()
    {
        var rig = new HordeRig(Row(8));
        rig.HeroIn(0);
        rig.Step();
        rig.Heroes[0] = new HeroPlace(0, 7, Centre(7));
        int i = rig.In(1)[0];
        rig.Sight.Points.Add(rig.Roster.Position(i));   // seen, so it keeps its body

        rig.Step(30 * Second);

        Assert.True(rig.Roster.RoomAwake(1));
        Assert.Equal(RobotState.Embodied, rig.Roster.State(i));
    }
}
