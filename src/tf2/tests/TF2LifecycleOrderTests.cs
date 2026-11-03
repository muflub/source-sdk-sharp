extern alias client;

using System.Runtime.CompilerServices;

using SourceSharp.Abi;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// Every TF2 level-hook participant takes a stage of its own in TF2's
/// thousand, below the mods' (ModStages.First) and above every core stage:
/// a tie with any participant refuses the side's whole load
/// (StageCollisionException). ObjectiveResourceSpawner shipped at the default
/// 0, tied the core's NetTrafficProfiler, and the tf2 mod did not load.
/// </summary>
public sealed class TF2LifecycleOrderTests
{
    /// <summary>
    /// Every hook chain a side composes (each <c>IHooks&lt;T&gt;</c> a
    /// constructor takes) refuses two members at one order, and a member that
    /// is no <c>IOrderedHook</c> stands at 0 (CoreServices' StageUniqueness).
    /// The wearables' component source shipped with no order beside
    /// TFPlayerComponents, also at 0: the composite's first ask threw, and a
    /// joining player got no components at all — no carrier, no class.
    /// </summary>
    private static List<string> ChainTies(System.Reflection.Assembly[] assemblies, Type hooksOpen, Type ordered,
                                          Func<Type, bool> registered, int floor)
    {
        var chains = assemblies.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetConstructors())
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Where(p => p.IsGenericType && p.GetGenericTypeDefinition() == hooksOpen)
            .Select(p => p.GetGenericArguments()[0])
            .Distinct()
            .ToList();
        Assert.True(chains.Count >= floor, "only " + chains.Count + " chains found: the scan is not reading the constructors");

        var ties = new List<string>();
        foreach (var chain in chains)
        {
            var members = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && chain.IsAssignableFrom(t) && registered(t))
                .Select(t => (t.FullName, Order: ordered.IsAssignableFrom(t)
                    ? (int)ordered.GetProperty("HookOrder")!.GetValue(RuntimeHelpers.GetUninitializedObject(t))!
                    : 0));
            ties.AddRange(members.GroupBy(m => m.Order).Where(g => g.Count() > 1)
                .Select(g => $"{chain.Name} @ {g.Key}: {string.Join(", ", g.Select(m => m.FullName))}"));
        }
        return ties;
    }

    [Fact]
    public void No_server_hook_chain_has_two_members_at_one_order()
    {
        var ties = ChainTies(
            [typeof(SourceSharp.Server.PlayerComposites).Assembly, typeof(TF2.Server.TFClassTableLoader).Assembly],
            typeof(IHooks<>), typeof(IOrderedHook),
            t => t.GetCustomAttributes(typeof(SideServiceAttribute), false).Length > 0, floor: 5);
        Assert.True(ties.Count == 0, string.Join("\n", ties));
    }

    [Fact]
    public void No_client_hook_chain_has_two_members_at_one_order()
    {
        var ties = ChainTies(
            [typeof(client::SourceSharp.Client.IClientPredictedState).Assembly, typeof(client::TF2.Client.TFClassTableLoader).Assembly],
            typeof(client::SourceSharp.Abi.IHooks<>), typeof(client::SourceSharp.Abi.IOrderedHook),
            t => t.GetCustomAttributes(typeof(client::SourceSharp.Abi.SideServiceAttribute), false).Length > 0, floor: 3);
        Assert.True(ties.Count == 0, string.Join("\n", ties));
    }

    [Fact]
    public void Every_TF2_lifecycle_participant_has_its_own_stage_in_TF2s_range()
    {
        var orders = typeof(TF2.Server.TFClassTableLoader).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IGameLifecycle).IsAssignableFrom(t))
            .Select(t => (t.Name, Order: ((IGameLifecycle)RuntimeHelpers.GetUninitializedObject(t)).LifecycleOrder))
            .ToList();

        Assert.True(orders.Count >= 3, "only " + orders.Count + " participants found: the scan is not reading the assembly");
        Assert.All(orders, o => Assert.InRange(o.Order, 1_000_000 - 1_000, 1_000_000 - 1));
        Assert.Equal(orders.Count, orders.Select(o => o.Order).Distinct().Count());
    }

    /// <summary>
    /// The same for every TF2 hook-chain participant on both sides
    /// (<c>IOrderedHook</c>): a stage in TF2's thousand, none shared, so no
    /// chain can tie a core or a mod participant.
    /// </summary>
    [Fact]
    public void Every_TF2_ordered_hook_has_its_own_order_in_TF2s_range()
    {
        static IEnumerable<(string Name, int Order)> Orders(System.Reflection.Assembly assembly, Type ordered)
            => assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && ordered.IsAssignableFrom(t))
                .Select(t => (t.FullName!, (int)ordered.GetProperty("HookOrder")!.GetValue(RuntimeHelpers.GetUninitializedObject(t))!));

        // A type in src/tf2/shared compiles into both assemblies: one participant
        // per side, so it is counted once (by name), and must answer the same
        // order on both.
        var bySide = Orders(typeof(TF2.Server.TFClassTableLoader).Assembly, typeof(IOrderedHook))
            .Concat(Orders(typeof(client::TF2.Client.TFClassTableLoader).Assembly, typeof(client::SourceSharp.Abi.IOrderedHook)))
            .ToList();
        Assert.All(bySide.GroupBy(o => o.Name), g => Assert.Single(g.Select(o => o.Order).Distinct()));
        var orders = bySide.DistinctBy(o => o.Name).ToList();

        Assert.True(orders.Count >= 4, "only " + orders.Count + " hooks found: the scan is not reading the assemblies");
        Assert.All(orders, o => Assert.InRange(o.Order, 1_000_000 - 1_000, 1_000_000 - 1));
        Assert.Equal(orders.Count, orders.Select(o => o.Order).Distinct().Count());
    }

    /// <summary>
    /// The band <c>999_310..999_319</c> is the D10 anim hook's (plan
    /// <c>docs/plans/plan_tf2_port.md</c> D10, the <c>ITFAnimHook</c> chain):
    /// a participant of any other chain may not stand an order there, so an
    /// anim hook can count on its band and nothing else can squat on it.
    /// </summary>
    [Fact]
    public void Only_anim_hooks_take_the_anim_hook_band()
    {
        static IEnumerable<Type> BandSquatters(System.Reflection.Assembly assembly, Type ordered, Type animHook)
            => assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && ordered.IsAssignableFrom(t))
                .Where(t => (int)ordered.GetProperty("HookOrder")!.GetValue(RuntimeHelpers.GetUninitializedObject(t))! is >= 999_310 and <= 999_319)
                .Where(t => !animHook.IsAssignableFrom(t));

        var server = typeof(TF2.Server.TFClassTableLoader).Assembly;
        Assert.Empty(BandSquatters(server, typeof(IOrderedHook), server.GetType("TF2.Shared.Anim.ITFAnimHook")!));
        Assert.Empty(BandSquatters(typeof(client::TF2.Client.TFClassTableLoader).Assembly,
                                   typeof(client::SourceSharp.Abi.IOrderedHook),
                                   typeof(client::TF2.Shared.Anim.ITFAnimHook)));
    }
}
