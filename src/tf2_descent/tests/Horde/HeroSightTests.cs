using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Server.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The heroes' sight (plan_descent_logic 3.4, HO2): the PVS, then a line from
/// each hero's eye, never more lines a tick than the budget, and never
/// "unseen" for a point a hero has a line to.
/// </summary>
public sealed class HeroSightTests
{
    /// <summary>A wall across the row at x = <paramref name="wallX"/>, from y = 0 to <paramref name="wallTo"/>: a line crossing it there is blocked.</summary>
    private static Func<Vector, Vector, bool> Wall(float wallX, float wallTo) => (a, b) =>
    {
        if ((a.X - wallX) * (b.X - wallX) >= 0)
            return true;
        float t = (wallX - a.X) / (b.X - a.X);
        float y = a.Y + t * (b.Y - a.Y);
        return y > wallTo;
    };

    private static (HeroSight Sight, RigSightWorld World) Rig(int budget, params AiVector[] heroes)
    {
        var level = Row(4);
        var world = new RigSightWorld();
        var sight = HeroSight.For(world, level, 4, budget);
        var eyes = heroes.Select((h, i) => new HeroEye(new EntityHandle((uint)(i + 1)), new Vector(h.X, h.Y, h.Z + 64f), level.RoomAt(h))).ToArray();
        sight.BeginTick(eyes);
        return (sight, world);
    }

    [Fact]
    public void A_point_a_hero_has_a_line_to_is_seen()
    {
        var (sight, _) = Rig(128, Centre(0));

        Assert.True(sight.CanSee(Centre(1)));
    }

    [Fact]
    public void A_point_behind_a_wall_is_unseen()
    {
        var (sight, world) = Rig(128, Centre(0));
        world.Clear = Wall(512, 10_000);

        Assert.False(sight.CanSee(Centre(1)));
    }

    [Fact]
    public void A_point_outside_every_heros_pvs_is_unseen_and_costs_no_line()
    {
        var (sight, world) = Rig(128, Centre(0));
        world.Pvs = false;

        Assert.False(sight.CanSee(Centre(1)));
        Assert.Equal(0, world.Lines);
    }

    [Fact]
    public void A_point_asked_twice_in_a_tick_costs_its_lines_once()
    {
        var (sight, world) = Rig(128, Centre(0));
        world.Clear = Wall(512, 10_000);

        sight.CanSee(Centre(1));
        int lines = world.Lines;
        sight.CanSee(Centre(1));

        Assert.Equal(2, lines);
        Assert.Equal(lines, world.Lines);
    }

    [Fact]
    public void A_tick_never_traces_more_lines_than_the_budget()
    {
        var (sight, world) = Rig(16, Centre(0), Centre(3));
        world.Clear = Wall(512, 10_000);

        for (int k = 0; k < 200; k++)
            sight.CanSee(new AiVector(600 + k, 100 + k, 0));

        Assert.Equal(16, world.Lines);
        Assert.Equal(16, sight.TracesThisTick);
        Assert.True(sight.OverBudgetThisTick > 0);
    }

    [Fact]
    public void Past_the_budget_every_point_is_seen()
    {
        var (sight, world) = Rig(4, Centre(0));
        world.Clear = Wall(512, 10_000);
        for (int k = 0; k < 2; k++)
            Assert.False(sight.CanSee(new AiVector(700 + k, 300, 0)));

        Assert.True(sight.CanSee(new AiVector(900, 300, 0)));
    }

    [Fact]
    public void The_budget_comes_back_with_the_next_tick()
    {
        var (sight, world) = Rig(4, Centre(0));
        world.Clear = Wall(512, 10_000);
        for (int k = 0; k < 10; k++)
            sight.CanSee(new AiVector(700 + k, 300, 0));

        sight.BeginTick([new HeroEye(new EntityHandle(1u), new Vector(256, 256, 64), 0)]);

        Assert.Equal(0, sight.TracesThisTick);
        Assert.False(sight.CanSee(new AiVector(700, 300, 0)));
    }

    [Fact]
    public void Over_many_points_and_ticks_a_point_in_line_of_sight_is_never_unseen()
    {
        // A wall at x 512 with a gap above y 300: points beyond it are seen through the gap only.
        var level = Row(4);
        var world = new RigSightWorld { Clear = Wall(512, 300) };
        var sight = HeroSight.For(world, level, 4, 24);
        var rng = new Random(5);
        int seenChecks = 0;
        for (int tick = 0; tick < 50; tick++)
        {
            var hero = new AiVector(100 + rng.Next(300), 100 + rng.Next(300), 0);
            var eye = new Vector(hero.X, hero.Y, 64f);
            sight.BeginTick([new HeroEye(new EntityHandle(1u), eye, 0)]);
            int before = world.Lines;
            for (int k = 0; k < 60; k++)
            {
                var p = new AiVector(520 + rng.Next(1400), rng.Next(512), 0);
                bool inSight = Wall(512, 300)(eye, new Vector(p.X, p.Y, p.Z + 16f));
                bool seen = sight.CanSee(p);
                if (inSight)
                {
                    seenChecks++;
                    Assert.True(seen, $"tick {tick}: {p} is in line of sight of {eye} and was answered unseen");
                }
            }

            Assert.True(world.Lines - before <= 24);
        }

        Assert.True(seenChecks > 100);
    }

    [Fact]
    public void A_room_a_hero_stands_in_is_seen_into_even_out_of_the_pvs()
    {
        var (sight, world) = Rig(128, Centre(2));
        world.Pvs = false;

        Assert.True(sight.CanSeeInto(2));
        Assert.False(sight.CanSeeInto(0));
    }

    [Fact]
    public void A_room_in_a_heros_pvs_is_seen_into()
    {
        var (sight, _) = Rig(128, Centre(0));

        Assert.True(sight.CanSeeInto(3));
    }
}
