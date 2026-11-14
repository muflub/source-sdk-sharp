using SourceSharp.Abi;

using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>The equipment's chain members (HE4): each order its own inside its chain, and the item source's beside TF2's.</summary>
public sealed partial class HookOrderTests
{
    [Fact]
    public void The_equipment_chains_have_distinct_orders()
    {
        var descent = typeof(HeroEquipment).Assembly;
        Type[] chains = [typeof(IItemLedgerHook), typeof(IHeroContributionSource), typeof(TF2.Server.Econ.ITFItemSource)];

        // The stimulus: the equipment has a member in each chain.
        foreach (var chain in chains)
            Assert.Contains(Implementations(descent, Side.Server, chain), f => f.Name.StartsWith("Hero", StringComparison.Ordinal));

        AssertDistinct(descent, Side.Server, chains);
    }

    /// <summary>
    /// The frame driver refuses two members at one FrameOrder (StageCollisionException at
    /// the side's load): HeroKitSettle at the default 0 beside TF2's TFAutobalance stopped the
    /// first tf2_descent boot of HE4 before the menu.
    /// </summary>
    [Fact]
    public void The_modes_frame_members_have_frame_orders_of_their_own_beside_TF2s_and_the_cores()
    {
        static (string Name, int Order)[] Frames(System.Reflection.Assembly assembly) =>
            assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFrameLifecycle).IsAssignableFrom(t)
                            && t.GetCustomAttributes(typeof(SideServiceAttribute), false).Cast<SideServiceAttribute>().Any(a => a.Side == Side.Server))
                .Select(t => (t.Name, ((IFrameLifecycle)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).FrameOrder))
                .ToArray();

        var mine = Frames(typeof(HeroEquipment).Assembly);
        var theirs = Frames(typeof(TF2.Server.Econ.TFInventoryItemSource).Assembly).Concat(Frames(typeof(IFrameLifecycle).Assembly)).ToArray();

        Assert.Contains(mine, m => m.Name == nameof(HeroKitSettle)); // the stimulus
        Assert.NotEmpty(theirs);
        var clashes = mine.SelectMany(m => mine.Concat(theirs).Where(o => o.Name != m.Name && o.Order == m.Order)
                                              .Select(o => $"{m.Name} and {o.Name} at {m.Order}")).ToArray();
        Assert.Empty(clashes);
    }

    [Fact]
    public void The_equipment_item_source_answers_after_TF2s_own()
    {
        var mine = Implementations(typeof(HeroEquipment).Assembly, Side.Server, typeof(TF2.Server.Econ.ITFItemSource));
        var theirs = Implementations(typeof(TF2.Server.Econ.TFInventoryItemSource).Assembly, Side.Server, typeof(TF2.Server.Econ.ITFItemSource));

        Assert.NotEmpty(theirs);
        Assert.All(mine, m => Assert.True(m.Order > theirs.Max(t => t.Order), $"{m.Name} at {m.Order} is not after TF2's sources"));
    }
}
