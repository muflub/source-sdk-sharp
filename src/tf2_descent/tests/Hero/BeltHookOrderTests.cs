using System.Reflection;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Server.Player;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The belt's and the wear's chain members (HE8, HE9) have orders of their own: after TF2's
/// band, and held by no other member of the same chain in the core, TF2 or TF2 Descent (a
/// tie throws at the first dispatch, or aborts the load for the lifecycle drivers, and only
/// a boot with both would see it).
/// </summary>
public sealed class BeltHookOrderTests
{
    private static int OrderOf(object service, Type chain) =>
        chain == typeof(IClientLifecycle) ? ((IClientLifecycle)service).ClientLifecycleOrder
        : chain == typeof(IGameLifecycle) ? ((IGameLifecycle)service).LifecycleOrder
        : chain == typeof(IFrameLifecycle) ? ((IFrameLifecycle)service).FrameOrder
        : (service as IOrderedHook)?.HookOrder ?? 0;

    private static (string Name, int Order)[] Members(Assembly assembly, Type chain) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && chain.IsAssignableFrom(t)
                        && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == Side.Server))
            .Select(t => (t.Name, OrderOf(RuntimeHelpers.GetUninitializedObject(t), chain)))
            .ToArray();

    [Theory]
    [InlineData(typeof(IItemLedgerHook), nameof(BeltSync))]
    [InlineData(typeof(IFrameLifecycle), nameof(BeltSync))]
    [InlineData(typeof(IClientLifecycle), nameof(BeltSync))]
    [InlineData(typeof(IPlayerSpawnHook), nameof(BeltSync))]
    [InlineData(typeof(IGameLifecycle), nameof(BeltSync))]
    [InlineData(typeof(IClientCommandHook), nameof(BeltClientCommands))]
    [InlineData(typeof(TF2.Shared.ITFWeaponHook), nameof(TF2.Descent.Server.Gear.HeroWearTaps))]
    [InlineData(typeof(IPlayerHurtHook), nameof(TF2.Descent.Server.Gear.HeroWearTaps))]
    [InlineData(typeof(IFrameLifecycle), nameof(TF2.Descent.Server.Gear.HeroWearFlush))]
    [InlineData(typeof(IGameLifecycle), nameof(TF2.Descent.Server.Gear.HeroWearFlush))]
    [InlineData(typeof(IClientLifecycle), nameof(TF2.Descent.Server.Gear.HeroWearFlush))]
    [InlineData(typeof(IItemLedgerHook), nameof(TF2.Descent.Server.Gear.HeroWearFlush))]
    [InlineData(typeof(IItemLedgerHook), nameof(TF2.Descent.Server.Gear.GearWearSync))]
    [InlineData(typeof(IFrameLifecycle), nameof(TF2.Descent.Server.Gear.GearWearSync))]
    [InlineData(typeof(IClientLifecycle), nameof(TF2.Descent.Server.Gear.GearWearSync))]
    [InlineData(typeof(IPlayerSpawnHook), nameof(TF2.Descent.Server.Gear.GearWearSync))]
    [InlineData(typeof(IGameLifecycle), nameof(TF2.Descent.Server.Gear.GearWearSync))]
    public void Each_belt_chain_member_has_an_order_no_other_member_holds(Type chain, string member)
    {
        Assembly[] assemblies = [typeof(HeroBelt).Assembly, typeof(TFPlayerCarriers).Assembly, typeof(ServerModule).Assembly,
                                 typeof(TF2.Shared.TFCritRoll).Assembly];
        var all = assemblies.SelectMany(a => Members(a, chain)).ToArray();
        var mine = Assert.Single(all, m => m.Name == member);

        Assert.True(mine.Order >= ModStages.First, $"{member} at {mine.Order}, inside TF2's band or the core's");
        Assert.Single(all, m => m.Order == mine.Order);
    }
}
