using System.Reflection;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Furniture;
using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class HookOrderTests
{
    /// <summary>
    /// The chains every registrar of the server assembly names (not only the main one's: the
    /// contribution chain, the kill chain and the furniture's hazard chain have registrars of their
    /// own), and the core chains the furniture joins: every member's order is its own.
    /// </summary>
    [Fact]
    public void Every_registrars_chains_and_the_furnitures_have_distinct_orders()
    {
        var assembly = typeof(FurnitureService).Assembly;
        var hooks = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISideServiceRegistrar).IsAssignableFrom(t)
                        && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == Side.Server))
            .SelectMany(t => HooksOf((ISideServiceRegistrar)RuntimeHelpers.GetUninitializedObject(t)))
            .Concat([typeof(IGameLifecycle), typeof(IFrameLifecycle), typeof(IDamageRule), typeof(ITransmitRule), typeof(IClientLifecycle)])
            .Distinct().ToList();

        // The stimulus: the furniture's members are among those compared.
        Assert.Contains(typeof(IHeroContributionSource), hooks);
        Assert.Contains(typeof(IFurnitureHazardHook), hooks);
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IHeroContributionSource)), m => m.Name == nameof(FurnitureContributions));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IDamageRule)), m => m.Name == nameof(FurnitureBlastRule));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(ITransmitRule)), m => m.Name == nameof(FurnitureTransmit));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IGameLifecycle)), m => m.Name == nameof(FurnitureService));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IFrameLifecycle)), m => m.Name == nameof(FurnitureService));

        AssertDistinct(assembly, Side.Server, hooks);
    }
}
