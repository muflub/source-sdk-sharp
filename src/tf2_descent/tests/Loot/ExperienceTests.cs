using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;
using Xunit;

namespace TF2.Descent.Tests.Loot;

/// <summary>
/// The XP a kill pays (plan_tf2_rpg D10, 2f; plan_descent_logic RU4): the robot's XP value,
/// scaled by experience.json's curve of monster level against hero level, the same for every
/// member in range; and the most a kill of a template could ever pay, the bound a checkpoint's
/// claimed XP is held to.
/// </summary>
public sealed class ExperienceTests
{
    private static readonly TF2DescentRules Rules = new();
    private static ExperienceRules X => Rules.Experience;

    [Fact]
    public void A_monster_of_the_heros_level_pays_its_full_xp()
    {
        Assert.Equal(100, X.LevelPct(10, 10));
        Assert.Equal(40, X.KillXp("soldier", 1.0, 1, [], heroLevel: 1));   // soldier: 40 XP at scale 1, monster level 1
    }

    [Theory]
    [InlineData(20, 10, 5)]    // the hero ten levels above: the floor
    [InlineData(30, 1, 5)]     // and past it
    [InlineData(10, 5, 50)]    // five above
    [InlineData(10, 12, 110)]  // two below: between 100 and 125
    [InlineData(10, 20, 140)]  // ten below: the cap
    [InlineData(1, 29, 140)]   // and past it
    public void The_level_difference_scales_the_xp_by_the_curve(int heroLevel, int monsterLevel, double pct)
    {
        Assert.Equal(pct, X.LevelPct(heroLevel, monsterLevel), 9);
    }

    [Fact]
    public void A_deeper_monster_never_pays_less()
    {
        for (int hero = 1; hero <= 30; hero++)
            for (int m = 1; m < 40; m++)
                Assert.True(X.LevelPct(hero, m + 1) >= X.LevelPct(hero, m));
    }

    [Fact]
    public void A_kill_pays_the_robots_xp_times_the_percent_rounded()
    {
        var robot = Rules.Robots.Stats(Rules.Content.Robots["heavy"], 1.07, Rules.Robots.MonsterLevel(6), ["extra_fast"]);

        Assert.Equal((long)Math.Round(robot.Xp * X.LevelPct(14, robot.Level) / 100, MidpointRounding.AwayFromZero), X.KillXp(robot, 14));
        Assert.True(X.KillXp(robot, 14) > 0);
    }

    /// <summary>D10: every member in range is paid in full; the party raises a robot's life, never its XP.</summary>
    [Fact]
    public void Every_member_in_range_gets_the_full_xp_whatever_the_party()
    {
        var t = Rules.Content.Robots["demoman"];
        var solo = Rules.Robots.Stats(t, 1.0, 9, [], partySize: 1);
        var four = Rules.Robots.Stats(t, 1.0, 9, [], partySize: 4);

        Assert.True(four.Life > solo.Life);
        Assert.Equal(X.KillXp(solo, 9), X.KillXp(four, 9));
    }

    /// <summary>The bound holds over every robot of 3,000 rolled packs (depths 1 to 15, parties of 1 to 4), for the hero it pays most.</summary>
    [Fact]
    public void No_rolled_kill_pays_more_than_the_most_a_kill_of_its_template_could()
    {
        long seen = 0;
        for (int i = 0; i < 3000; i++)
        {
            var pack = Rules.Packs.RollPack((ulong)i, i % 7, 1 + i % 15, 1 + i % 4, 4);
            foreach (var r in pack.Robots)
            {
                long xp = X.KillXp(r.Stats, heroLevel: 1);
                Assert.True(xp <= X.MostXpPerKill(r.Template), $"{r.Template} paid {xp}, over its bound {X.MostXpPerKill(r.Template)}");
                seen = Math.Max(seen, xp);
            }
        }
        Assert.True(seen > 0);
    }

    [Fact]
    public void The_bound_is_the_largest_deepest_best_affixed_kill_at_the_curves_top()
    {
        var t = Rules.Content.Robots["giant_heavy"];
        var best = Rules.Robots.Stats(t, t.ScaleMax, Rules.Robots.MonsterLevel(15), ["giant", "crit_aura", "fireproof"]);

        Assert.Equal((long)Math.Ceiling(best.Xp * 1.4 - 1e-9), X.MostXpPerKill("giant_heavy"));
        Assert.Equal(X.MostXpPerKill("giant_heavy"), X.MostXpPerKill("giant_heavy+giant"));   // a loot key names its template
    }

    [Fact]
    public void A_name_that_is_no_template_pays_nothing()
    {
        Assert.Equal(0, X.MostXpPerKill("soldier_bot"));
    }
}
