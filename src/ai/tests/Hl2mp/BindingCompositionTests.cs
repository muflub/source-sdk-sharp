using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// The binding's side services register as the server's composition
/// registers a mod's (<see cref="SideServices.Register"/>): no contract
/// twice, so a live boot does not stop at "registered twice" (Phase 4:
/// CombatTap named itself in its own As list and the server did not start).
/// </summary>
public sealed class BindingCompositionTests
{
    [Fact]
    public void EveryServiceOfTheBindingRegistersOnce()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server, typeof(VisionPass).Assembly.GetTypes());
        Assert.Contains(typeof(CombatTap), registered);
        Assert.Contains(typeof(SourceSharp.Server.ISoundEmitHook), registered);
    }
}
