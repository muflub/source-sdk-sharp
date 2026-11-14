using SourceSharp.Abi;

using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>The hero's own chains (HE2, HE3) and the kill seam's (HE5): their members' orders are distinct.</summary>
public sealed partial class HookOrderTests
{
    [Fact]
    public void The_kill_and_hero_chains_have_distinct_orders()
    {
        var hooks = HooksOf(new RobotKillRegistrar()).Concat(HooksOf(new HeroRegistrar())).ToList();
        Assert.Contains(typeof(IRobotKillHook), hooks);
        Assert.Contains(typeof(IHeroStatsWatcher), hooks);

        // The stimulus: the kill chain has a member to compare.
        Assert.Contains(Implementations(typeof(RobotKills).Assembly, Side.Server, typeof(IRobotKillHook)), f => f.Name == nameof(HeroExperience));

        AssertDistinct(typeof(RobotKills).Assembly, Side.Server, hooks);
    }

    /// <summary>
    /// HO10's loot is paid after HE5's experience: the kill's XP, the level-up
    /// and the bestiary's count are on the sheet before the ledger is asked for
    /// the drop, so a slow or failing ledger costs the hero nothing of his sheet.
    /// </summary>
    [Fact]
    public void The_kill_chain_pays_the_experience_then_the_loot()
    {
        var members = Implementations(typeof(RobotKills).Assembly, Side.Server, typeof(IRobotKillHook));

        var xp = Assert.Single(members, f => f.Name == nameof(HeroExperience));
        var loot = Assert.Single(members, f => f.Name == "LootRewards");
        Assert.True(loot.Order > xp.Order, $"the loot ({loot.Order}) is paid before the experience ({xp.Order})");
        Assert.True(loot.Order >= ModStages.First, "the loot's order is inside TF2's band or the core's");
    }
}
