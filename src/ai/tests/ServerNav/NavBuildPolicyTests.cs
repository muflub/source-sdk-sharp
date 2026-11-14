using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Which games have a map's navigation built in the server's process
/// (plan_bots_teams_tf2 E.3): the test mod's list registers HL2MP's
/// <see cref="INavBuildPolicy"/>, which builds every map; the tf2 mod's list
/// registers none, so a TF2 map keeps its refusal until P6's binding asks.
/// </summary>
public sealed class NavBuildPolicyTests
{
    private static ServiceContainer Compose(params Type[][] lists)
    {
        var services = new ServiceContainer();
        SideServices.Register(services, Side.Server, [.. lists.SelectMany(l => l)]);
        return services;
    }

    [Fact]
    public void TheTestModsListRegistersHl2mpsPolicyAndItBuildsAMap()
    {
        ServiceContainer services = Compose(typeof(VisionPass).Assembly.GetTypes(), typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly.GetTypes());

        Assert.True(services.IsRegistered(typeof(INavBuildPolicy)));
        Assert.True(new SourceSharp.Ai.Hl2mp.Hl2mpNavBuilds().Builds("dm_lockdown"));
    }

    [Fact]
    public void TheTF2ListRegistersNoPolicy()
        => Assert.False(Compose(typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), typeof(VisionPass).Assembly.GetTypes(),
                                typeof(SourceSharp.Ai.TF2.TFBotGame).Assembly.GetTypes())
                            .IsRegistered(typeof(INavBuildPolicy)));
}
