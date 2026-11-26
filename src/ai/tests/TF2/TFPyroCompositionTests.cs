using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The TF2 AI's side services, with the Pyro's HUD meter among them, register as the server's composition registers a
/// mod's (<see cref="SideServices.Register"/>): no contract twice. Live, 2026-11-21: TFPyroMeters claimed
/// <see cref="ITFHudMeters"/> as the Medic's meters do, and the server stopped at "ITFHudMeters is registered twice".
/// </summary>
public sealed class TFPyroCompositionTests
{
    [Fact]
    public void ThePyrosMeterRegistersBesideTheMedics()
    {
        var services = new ServiceContainer();
        SideServices.Register(services, Side.Server, typeof(TFPyroMeters).Assembly.GetTypes());
        Assert.Contains(services.ServiceTypes, t => t == typeof(TFPyroMeters));
    }

    /// <summary>
    /// Every HUD meter reader in the assembly has its own hook order (IHooks refuses a tie, and AiSenses' post-think threw
    /// every tick live, 2026-11-21, when the Pyro's reader left its order at 0 beside the Medic's). The order is read off an
    /// uninitialised instance: each reader's is a constant.
    /// </summary>
    [Fact]
    public void EveryHudMeterReaderHasItsOwnOrder()
    {
        int[] orders = [.. typeof(TFPyroMeters).Assembly.GetTypes()
                              .Where(t => typeof(ITFHudMeters).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                              .Select(t => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t) is IOrderedHook o ? o.HookOrder : 0)];
        Assert.True(orders.Length >= 2, $"{orders.Length} readers");
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }
}
