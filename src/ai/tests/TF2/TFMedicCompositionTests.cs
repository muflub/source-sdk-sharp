using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Medic lane's readers compose as the server composes the TF2 binding (<see cref="SideServices.Register"/>): the
/// target ID reader registers without naming the meters chain a second time (live, ctf_2fort, 2026-11-21: "ITFHudMeters is
/// registered twice" stopped the server), and every reader of the chain has its own hook order (two at one order throw
/// at the chain's first call).
/// </summary>
public sealed class TFMedicCompositionTests
{
    [Fact]
    public void TheTargetIdReaderRegistersBesideTheMedigunMeters()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(VisionPass).Assembly.GetTypes(), .. typeof(TFHudReader).Assembly.GetTypes()]);
        Assert.Contains(typeof(TFTargetIdMeters), registered);
        Assert.Contains(typeof(TFMedigunMeters), registered);
    }

    [Fact]
    public void EveryHudMeterReaderHasItsOwnHookOrder()
    {
        Type[] readers = [.. typeof(TFHudReader).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t))];
        Assert.Contains(typeof(TFTargetIdMeters), readers);
        int[] orders = [.. readers.Select(t => RuntimeHelpers.GetUninitializedObject(t) is IOrderedHook h ? h.HookOrder : 0)];
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }
}
