using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Robots;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Promotion and demotion (plan_descent_logic 3.3, 3.10): a body starts with the row's life,
/// statuses and carried memory, and a demotion saves the body's back, so nothing is lost or
/// invented across any number of round trips.
/// </summary>
public sealed class TransferTests
{
    private const double Tps = RosterTests.TicksPerSecond;

    private static (RobotRoster Roster, BodyPool Pool) Awake(int rooms = 2)
    {
        var roster = RosterTests.Rolled(Row(rooms));
        for (int r = 0; r < rooms; r++) roster.WakeRoom(r);
        return (roster, BodyPool.For(TheRules.Content.Horde));
    }

    /// <summary>A state as text, statuses by value, so two states compare by what they hold.</summary>
    private static string Describe(RobotBodyState s) =>
        $"{s.Room}|{s.Position}|{s.Yaw:R}|{s.Life:R}|{s.Clip}|{s.Carried}|"
        + string.Join(";", s.Statuses.Active.Select(a => $"{a.Id}x{a.Stacks}@{a.EndTick}/{a.NextDamageTick}"));

    [Fact]
    public void A_body_starts_with_exactly_the_rows_state()
    {
        var (roster, pool) = Awake();
        var memory = new CarriedMemory(2, At(10, 20, 30), 400, AlertLevel.Alerted, 1, 55.5f);
        roster.SetCarried(0, memory);
        roster.ApplyStatus(0, "slow", 100);
        roster.Hurt(0, 7.25);
        var row = roster.BodyState(0);

        var start = roster.Promote(0, pool.Lend(1, roster.Id(0)), 120);

        Assert.Equal(Describe(row), Describe(start));
        Assert.Equal(memory, start.Carried);
        Assert.Equal(roster.Stats(0).Life - 7.25, start.Life);
        Assert.Equal((RobotState.Embodied, 1), (roster.State(0), roster.Body(0)));
    }

    [Fact]
    public void A_fresh_robots_body_starts_whole_with_calm_memory()
    {
        var (roster, pool) = Awake();

        var start = roster.Promote(0, pool.Lend(1, roster.Id(0)), 0);

        Assert.Equal((roster.Stats(0).Life, -1, CarriedMemory.None), (start.Life, start.Clip, start.Carried));
        Assert.Empty(start.Statuses.Active);
    }

    [Fact]
    public void A_demotion_saves_what_the_body_holds_and_clears_the_body()
    {
        var (roster, pool) = Awake();
        var lease = pool.Lend(3, roster.Id(0));
        roster.Promote(0, lease, 0);
        var end = new RobotBodyState(1, At(700, 90), 33, 12.5, RobotStatuses.None, 4,
            new CarriedMemory(3, At(1, 2, 3), 77, AlertLevel.Suspicious, 0, 9));

        var back = roster.Demote(0, end, 200);

        Assert.Equal(lease, back);
        Assert.Equal(Describe(end), Describe(roster.BodyState(0)));
        Assert.Equal((RobotState.Far, 0), (roster.State(0), roster.Body(0)));
    }

    [Fact]
    public void A_body_given_up_at_no_life_leaves_a_dead_robot()
    {
        var (roster, pool) = Awake();
        roster.Promote(0, pool.Lend(1, roster.Id(0)), 0);

        roster.Demote(0, roster.BodyState(0) with { Life = 0 }, 1);

        Assert.Equal(RobotState.Dead, roster.State(0));
    }

    [Fact]
    public void Only_a_far_robot_takes_a_body_and_only_a_bodied_one_gives_it_up()
    {
        var roster = RosterTests.Rolled(Row(2));
        var pool = BodyPool.For(TheRules.Content.Horde);

        Assert.Throws<InvalidOperationException>(() => roster.Promote(0, pool.Lend(1, roster.Id(0)), 0));   // asleep
        roster.WakeRoom(0);
        Assert.Throws<InvalidOperationException>(() => roster.Demote(0, roster.BodyState(0), 0));          // far, no body
    }

    [Fact]
    public void Over_random_lives_nothing_is_lost_or_invented_across_promotions_and_demotions()
    {
        int roundTrips = 0, farTicks = 0, damages = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var (roster, pool) = Awake();
            var roll = new Seeded(seed);
            int i = roll.Next(roster.Count);
            var stats = roster.Stats(i);
            RobotBodyState model = roster.BodyState(i);   // what the robot is, wherever it lives
            BodyLease lease = default;
            long now = 0;
            string[] statuses = ["burn", "bleed", "slow", "stun", "marked"];
            for (int step = 0; step < 400 && model.Life > 0; step++)
            {
                now += 1 + roll.Next(20);
                bool embodied = roster.State(i) == RobotState.Embodied;
                switch (roll.Next(6))
                {
                    case 0 when !embodied:
                        lease = pool.Lend(1 + roll.Next(pool.PoolBodies), roster.Id(i));
                        var start = roster.Promote(i, lease, now);
                        Assert.Equal(Describe(model), Describe(start));
                        break;
                    case 1 when embodied:
                        Assert.Equal(lease, roster.Demote(i, model, now));
                        pool.Return(lease);
                        Assert.Equal(Describe(model), Describe(roster.BodyState(i)));
                        roundTrips++;
                        break;
                    case 2 when embodied:
                        // The body is hit: its life goes, through the robot's resistance.
                        model = model with { Life = Math.Max(0, model.Life - RobotRules.DamageTaken(stats, "bullet", 1 + roll.Next(15))) };
                        damages++;
                        break;
                    case 3 when embodied:
                        model = model with
                        {
                            Statuses = TheRules.Statuses.Apply(model.Statuses, statuses[roll.Next(statuses.Length)], now, Tps, stats.Immunity).After,
                            Carried = new CarriedMemory(roll.Next(4), At(roll.Next(999), roll.Next(999), 0), now, (AlertLevel)roll.Next(3), roll.Next(4), roll.Next(500)),
                            Position = At(roll.Next(1024), roll.Next(512)),
                            Yaw = roll.Next(360),
                            Clip = roll.Next(30),
                        };
                        break;
                    case 4 when !embodied:
                        // Far: statuses keep ticking on the row, and the model follows the same rules.
                        var tick = TheRules.Statuses.Tick(model.Statuses, now, Tps);
                        double dealt = tick.Damage.Sum(d => RobotRules.DamageTaken(stats, d.DamageType, d.Amount));
                        model = model with { Statuses = tick.After, Life = dealt > 0 ? Math.Max(0, model.Life - dealt) : model.Life };
                        roster.TickStatuses(i, now);
                        Assert.Equal(Describe(model), Describe(roster.BodyState(i)));
                        farTicks++;
                        break;
                    case 5 when !embodied:
                        roster.ApplyStatus(i, statuses[roll.Next(statuses.Length)], now);
                        model = model with { Statuses = roster.Statuses(i) };
                        break;
                }
            }
        }
        Assert.True(roundTrips > 500 && farTicks > 500 && damages > 500, $"round trips {roundTrips}, far ticks {farTicks}, damages {damages}");
    }
}
