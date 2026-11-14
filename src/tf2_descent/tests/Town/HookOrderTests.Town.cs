using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Server.Town;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class HookOrderTests
{
    /// <summary>
    /// The town (plan_descent_logic WO8) on every chain it joins, each order its own: the ledger's
    /// and the stats' chains after the owner's item sender (so the items a verb moved reach the
    /// client before the window that shows them is sent again), the client commands beside the
    /// hero's own verbs, and the level, frame and client lifecycles.
    /// </summary>
    [Fact]
    public void The_towns_members_have_orders_of_their_own_after_the_owner_views()
    {
        var assembly = typeof(TownService).Assembly;
        foreach (var hook in new[] { typeof(IItemLedgerHook), typeof(IHeroStatsWatcher), typeof(IClientCommandHook),
                                     typeof(IGameLifecycle), typeof(IFrameLifecycle), typeof(IClientLifecycle) })
        {
            var members = Implementations(assembly, Side.Server, hook);
            Assert.Contains(members, m => m.Name is nameof(TownService) or nameof(TownCommands));
            Assert.Empty(members.GroupBy(m => m.Order).Where(g => g.Count() > 1).Select(g => $"{hook.Name} {g.Key}: {string.Join(", ", g.Select(m => m.Name))}"));
        }

        var ledger = Implementations(assembly, Side.Server, typeof(IItemLedgerHook));
        Assert.True(ledger.Single(m => m.Name == nameof(TownService)).Order > ledger.Single(m => m.Name == nameof(OwnerItemSender)).Order);
        var stats = Implementations(assembly, Side.Server, typeof(IHeroStatsWatcher));
        Assert.True(stats.Single(m => m.Name == nameof(TownService)).Order > stats.Single(m => m.Name == nameof(OwnerItemSender)).Order);
    }
}
