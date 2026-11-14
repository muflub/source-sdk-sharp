using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Furniture;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>The body pool (plan_descent_logic 3.4, 3.6): bodies 1 to 48 and the boss's 49, never lent twice, a new serial each lend.</summary>
public sealed class BodyPoolTests
{
    [Fact]
    public void The_shipped_pool_is_bodies_one_to_forty_eight_and_the_boss_body_forty_nine()
    {
        var pool = BodyPool.For(TheRules.Content.Horde);

        Assert.Equal((48, 49, 49), (pool.PoolBodies, pool.BossBody, pool.LastBody));
        Assert.True(pool.IsBossBody(49));
        Assert.False(pool.IsBossBody(48));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.IsFree(0));
    }

    [Fact]
    public void A_lent_body_is_not_lent_again()
    {
        var pool = new BodyPool(2, 0);
        pool.Lend(1, new RobotId(0, 1));

        Assert.Throws<InvalidOperationException>(() => pool.Lend(1, new RobotId(1, 1)));
        Assert.Equal(new RobotId(0, 1), pool.Holder(1));
    }

    [Fact]
    public void Each_lend_of_a_body_has_a_new_serial()
    {
        var pool = new BodyPool(1, 0);

        var first = pool.Lend(1, new RobotId(0, 1));
        pool.Return(first);
        var second = pool.Lend(1, new RobotId(1, 1));

        Assert.NotEqual(first.Serial, second.Serial);
        Assert.Equal(new SourceSharp.Ai.ActorId(1, second.Serial), second.Actor);
        Assert.True(second.Actor.IsValid);
    }

    [Fact]
    public void A_stale_lease_does_not_return_the_body()
    {
        var pool = new BodyPool(1, 0);
        var first = pool.Lend(1, new RobotId(0, 1));
        pool.Return(first);
        pool.Lend(1, new RobotId(1, 1));

        Assert.Throws<InvalidOperationException>(() => pool.Return(first));
        Assert.False(pool.IsFree(1));
    }

    [Fact]
    public void Over_random_lends_and_returns_no_body_is_ever_held_twice_and_every_serial_moves_on()
    {
        var pool = new BodyPool(6, 1);
        var roll = new Seeded(2026);
        var held = new Dictionary<int, BodyLease>();
        var lastSerial = new int[pool.LastBody + 1];
        int lends = 0, refusals = 0;
        for (int step = 0; step < 20_000; step++)
        {
            int body = 1 + roll.Next(pool.LastBody);
            if (roll.Chance(55))
            {
                if (held.ContainsKey(body))
                {
                    Assert.Throws<InvalidOperationException>(() => pool.Lend(body, new RobotId(step, 1)));
                    refusals++;
                    continue;
                }
                var lease = pool.Lend(body, new RobotId(step, 1));
                Assert.True(lease.Serial > lastSerial[body]);
                lastSerial[body] = lease.Serial;
                held[body] = lease;
                lends++;
            }
            else if (held.Remove(body, out var lease))
                pool.Return(lease);
            Assert.Equal(held.Count, Enumerable.Range(1, pool.LastBody).Count(b => !pool.IsFree(b)));
        }
        Assert.True(lends > 1000 && refusals > 1000, $"lends {lends}, refusals {refusals}");
    }
}
