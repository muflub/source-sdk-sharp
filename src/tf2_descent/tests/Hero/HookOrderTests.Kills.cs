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
}
