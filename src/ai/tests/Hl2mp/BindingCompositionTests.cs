using SourceSharp.Abi;
using Xunit;
using SourceSharp.Ai.Server.Senses;

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
        // Both of the test mod's AI assemblies, as mods.yaml lists them (plan_bots_teams_tf2 C.2).
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(VisionPass).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly.GetTypes()]);
        Assert.Contains(typeof(SourceSharp.Ai.Server.IBotGame), registered);
        Assert.Contains(typeof(CombatTap), registered);
        Assert.Contains(typeof(SourceSharp.Server.ISoundEmitHook), registered);
        Assert.Contains(typeof(SourceSharp.Server.IClientStepHook), registered);
    }
}
