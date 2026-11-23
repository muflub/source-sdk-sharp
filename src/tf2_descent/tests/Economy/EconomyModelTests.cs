using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy.Model;
using Xunit;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// RU14's economy model as an instrument (plan_descent_logic RU14): it is deterministic, and
/// its self-tests show it moves when a planted imbalance moves the game (doubling a robot's
/// life doubles the time to kill it; halving the item drops lowers the gear at depth 15), so
/// its gates can fail.
/// </summary>
public sealed class EconomyModelTests
{
    private static readonly EconomyModel Shipped = new(DescentContent.Shipped());

    [Fact]
    public void A_run_is_a_pure_function_of_its_builds_and_seed()
    {
        var a = Shipped.Run(["gunman"], 3, toDepth: 3);
        var b = new EconomyModel(DescentContent.Shipped()).Run(["gunman"], 3, toDepth: 3);
        var c = Shipped.Run(["gunman"], 4, toDepth: 3);

        Assert.Equal(a.Depths, b.Depths, new ReportComparer());
        Assert.NotEqual(a.Depths.Select(d => d.Seconds), c.Depths.Select(d => d.Seconds));
    }

    private sealed class ReportComparer : IEqualityComparer<DepthReport>
    {
        public bool Equals(DepthReport? x, DepthReport? y) =>
            x is not null && y is not null && x.Depth == y.Depth && x.Seconds == y.Seconds && x.Levels.SequenceEqual(y.Levels)
            && x.Power.SequenceEqual(y.Power) && x.Australium.SequenceEqual(y.Australium) && x.Deaths == y.Deaths && x.Kills == y.Kills;

        public int GetHashCode(DepthReport obj) => obj.Depth;
    }

    /// <summary>
    /// Self-test: three heavies at depth 8 that cannot hurt (damage planted to 0), killed by a
    /// level 15 Gunman in stock gear, take twice as long when the heavy's life is planted at
    /// twice its 300: the measured ratio lies within 5 % of 2.
    /// </summary>
    [Fact]
    public void Doubling_a_robots_life_doubles_the_time_to_kill_it()
    {
        double Seconds(params string[] plant)
        {
            var model = new EconomyModel(PlantedContent.With(["robots:[heavy].damage=0", .. plant]));
            var hero = model.HeroAt("gunman", 15);
            return model.Fight([hero], model.Of("heavy", 1.0, 8, 3)).Seconds;
        }
        double plain = Seconds(), doubled = Seconds("robots:[heavy].life=600");

        Assert.True(plain > 3, $"the stimulus: {plain} s");
        Assert.InRange(doubled / plain, 1.9, 2.1);
    }

    /// <summary>
    /// Self-test: a solo Gunman (seed 1) descending 1 to 15 ends with a gear score at least 3 %
    /// lower when every kill table's item weight is planted at half and a champion's and a
    /// boss's item counts at half, rounded down (a champion's 1 to 0; the bosses' 2, 3, 4 to
    /// 1, 1, 2). Halving the kill tables alone no longer moves it: since HO12 the champions'
    /// and bosses' own drops (vintage at least) are most of the gear a run wears (at
    /// 1e8404f81, six seeds: halved over plain 0.98 to 1.17).
    /// </summary>
    [Fact]
    public void Halving_the_item_drops_lowers_the_gear_at_depth_15()
    {
        static double Gear(EconomyModel m) => m.Run(["gunman"], 1).Last.Power.Average();
        double plain = Gear(Shipped);
        double halved = Gear(new EconomyModel(PlantedContent.With(
            "loot:tables.0.outcomes.item=7", "loot:tables.1.outcomes.item=7", "loot:tables.2.outcomes.item=7.5", "loot:champion.items=0",
            "loot:bosses.0.items=1", "loot:bosses.1.items=1", "loot:bosses.2.items=2")));

        Assert.True(halved < plain * 0.97, $"halved drops: gear {halved:F0} against {plain:F0}");
    }

    /// <summary>A.6's median gear is drops of the depth: at level 30 and depth 15 most slots wear something better than Stock (a tier roll once returned Stock nearly always).</summary>
    [Fact]
    public void Median_gear_at_depth_15_is_mostly_better_than_stock()
    {
        var hero = new ModelGates(Shipped).Geared("gunman", 30, 15, 7);

        Assert.Equal(9, hero.Equipped.Count);
        Assert.True(hero.Equipped.Values.Count(i => i.Rarity > 0) >= 5, string.Join(", ", hero.Equipped.Select(kv => $"{kv.Key}:{kv.Value.Rarity}")));
    }

    /// <summary>
    /// A rocket's hit in the model is TF's 90 by the hero's own multiplier for a blast
    /// projectile, which holds Marksmanship's damage_weapon_pct once (HE6's follow-up made it a
    /// weapon hit's kind): the model once added it a second time.
    /// </summary>
    [Fact]
    public void A_weapon_hit_counts_damage_weapon_pct_once()
    {
        var hero = Shipped.HeroAt("gunman", 6);
        var stats = Shipped.Rules.Skills.Stats(hero.Sheet, TF2.Descent.Rules.Skills.SkillBonuses.None);

        Assert.True(stats.Total("damage_weapon_pct") >= 25, $"the stimulus: {stats.Total("damage_weapon_pct")}");
        Assert.Equal(90 * stats.DamageMultiplier("blast", "projectile"), Shipped.WeaponPerHit(hero, "primary"), 6);
    }

    /// <summary>
    /// A boss fight summons each phase's adds once, as the phase begins (bosses.json): a level
    /// 30 Gunman, who never falls, kills Sir Nukesalot and exactly the adds of his phases 2 and 3.
    /// </summary>
    [Fact]
    public void A_boss_fight_summons_each_phases_adds_once()
    {
        var hero = Shipped.HeroAt("gunman", 30);
        var fight = Shipped.BossFight([hero], 5)!;
        var script = Shipped.Rules.Bosses.Of("sir_nukesalot")!;
        var adds = script.Phases.SelectMany(p => p.Adds).GroupBy(a => a.Template).ToDictionary(g => g.Key, g => g.Sum(a => a.Count));

        Assert.True(fight.Cleared);
        Assert.Equal(0, fight.Deaths);
        Assert.NotEmpty(adds);
        Assert.Equal(adds.Values.Sum() + 1, fight.Killed.Count);
        foreach (var (template, count) in adds)
            Assert.Equal(count, fight.Killed.Count(r => r.Template == template));
    }

    /// <summary>
    /// The Tank's roof loses the fight: with its track planted short (100 units at its speed,
    /// stops of a second) Major Crits' fight against a level 30 Gunman ends uncleared and says
    /// the roof fell; shipped, the same fight is won.
    /// </summary>
    [Fact]
    public void A_boss_fight_is_lost_when_the_tank_brings_the_roof_down()
    {
        var planted = new EconomyModel(PlantedContent.With("economy_model:boss.tankTrackUnits=100", "bosses:tank.stopSeconds=1", "bosses:tank.finalSeconds=1"));
        var lost = planted.BossFight([planted.HeroAt("gunman", 30)], 10)!;
        var won = Shipped.BossFight([Shipped.HeroAt("gunman", 30)], 10)!;

        Assert.False(won.RoofFell);
        Assert.True(won.Cleared);
        Assert.True(lost.RoofFell);
        Assert.False(lost.Cleared);
    }

    [Fact]
    public void A_build_spends_its_points_by_its_order()
    {
        var hero = Shipped.HeroAt("artillery", 30);

        Assert.Equal(0, hero.Sheet.SkillPoints);
        Assert.Equal(0, hero.Sheet.AttributePoints);
        Assert.Equal(5, hero.Sheet.SkillRanks["patriot_missile"]);
        Assert.Equal(1, hero.Sheet.SkillRanks["screamin_eagle"]);
        Assert.Equal(90, hero.Sheet.Base("aim"));     // Aim to its cap, then Smarts to its own
        Assert.Equal(50, hero.Sheet.Base("smarts"));
    }
}
