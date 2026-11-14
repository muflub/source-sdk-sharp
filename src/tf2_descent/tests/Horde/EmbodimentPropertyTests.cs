using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Embodiment;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Furniture;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The embodiment rules over seeded random play (plan_descent_logic 3.4, 3.10): four heroes
/// wandering a level, sight changing every tick, robots dying, a pool too small for the
/// horde. No body is ever lent twice, none is taken or given up in view, and the roster and
/// the pool always agree on who holds what. And a decision on a full roster allocates nothing.
/// </summary>
public sealed class EmbodimentPropertyTests
{
    [Fact]
    public void Over_random_play_no_body_is_lent_twice_and_none_changes_hands_in_view()
    {
        int promotes = 0, demotes = 0, pressure = 0, releases = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var rig = new HordeRig(Row(10, markers: 3, seed: seed, depth: 10), 4, new BodyPool(8, 1));
            var roll = new Seeded(seed * 7919);
            for (int h = 0; h < 4; h++) rig.Heroes.Add(new HeroPlace(h, roll.Next(10), default));
            for (int step = 0; step < 1500; step++)
            {
                rig.Now += 1 + roll.Next(200);
                for (int h = 0; h < 4; h++)
                    if (roll.Chance(30))
                    {
                        int room = Math.Clamp(rig.Heroes[h].Room + (roll.Chance(50) ? 1 : -1), 0, 9);
                        rig.Heroes[h] = new HeroPlace(h, room, Centre(room));
                    }
                for (int r = 0; r < 10; r++) rig.Sight.SeePointsIn[r] = roll.Chance(25);
                for (int i = 0; i < rig.Roster.Count; i++)
                    if (rig.Roster.State(i) == RobotState.Embodied && roll.Chance(1)) rig.Roster.Hurt(i, 1e12);

                rig.Decide();

                // Replay the orders against who holds what: a body is taken only when free.
                var held = Enumerable.Range(1, rig.Pool.LastBody).Where(b => !rig.Pool.IsFree(b)).ToHashSet();
                var orders = rig.Orders.AsSpan().ToArray();
                for (int k = 0; k < orders.Length; k++)
                {
                    var o = orders[k];
                    switch (o.Kind)
                    {
                        case HordeOrderKind.Promote:
                            Assert.True(held.Add(o.Body), $"seed {seed} step {step}: body {o.Body} lent while lent");
                            Assert.False(rig.Sight.CanSee(o.Position), $"seed {seed} step {step}: {o.Robot} takes a body in view");
                            promotes++;
                            break;
                        case HordeOrderKind.Demote:
                            Assert.True(held.Remove(o.Body));
                            Assert.False(rig.Sight.CanSee(rig.Roster.Position(o.Robot.Index)), $"seed {seed} step {step}: {o.Robot} demoted in view");
                            demotes++;
                            if (k + 1 < orders.Length && orders[k + 1].Kind == HordeOrderKind.Promote && orders[k + 1].Body == o.Body) pressure++;
                            break;
                        case HordeOrderKind.Release:
                            Assert.True(held.Remove(o.Body));
                            releases++;
                            break;
                    }
                }

                rig.Applier.Apply(rig.Orders, rig.Now);

                var holders = Enumerable.Range(0, rig.Roster.Count).Where(i => rig.Roster.Body(i) != 0).ToArray();
                Assert.Equal(holders.Length, holders.Select(rig.Roster.Body).Distinct().Count());
                Assert.All(holders, i => Assert.Equal(rig.Roster.Id(i), rig.Pool.Holder(rig.Roster.Body(i))));
                Assert.Equal(holders.Length, Enumerable.Range(1, rig.Pool.LastBody).Count(b => !rig.Pool.IsFree(b)));
                Assert.All(holders, i => Assert.Equal(RobotState.Embodied, rig.Roster.State(i)));
            }
        }
        Assert.True(promotes > 1000 && demotes > 300 && pressure > 50 && releases > 50,
            $"promotes {promotes}, demotes {demotes}, pressure {pressure}, releases {releases}");
    }

    [Fact]
    public void A_thousand_decisions_on_a_full_roster_allocate_nothing()
    {
        var rig = new HordeRig(Row(40, markers: 3, seed: 3, depth: 15), 4);
        Assert.InRange(rig.Roster.Count, rig.Roster.Capacity - 16, rig.Roster.Capacity);   // no further pack fits
        foreach (int room in new[] { 5, 15, 25, 35 }) rig.HeroIn(room);
        rig.Sight.SeePointsIn[5] = true;
        for (int k = 0; k < 20; k++) rig.Step(10);   // bodies out, rooms awake: a steady state
        Assert.True(rig.Pool.FreePoolBodies < rig.Pool.PoolBodies);

        int asked = rig.Sight.Asked;
        // A steady allocation shows in every window; a one-off (the runtime re-jitting a hot method while the box is
        // loaded) shows in one: the quietest of three windows is what a tick costs.
        long allocated = long.MaxValue;
        for (int window = 0; window < 3 && allocated != 0; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 1000; k++)
            {
                rig.Now++;
                rig.Decide();
            }

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(rig.Sight.Asked - asked >= 10_000, $"the decisions asked sight {rig.Sight.Asked - asked} times");
        Assert.Equal(0, allocated);
    }
}
