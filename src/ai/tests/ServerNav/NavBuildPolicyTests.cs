using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Which games have a map's navigation built in the server's process
/// (plan_bots_teams_tf2 E.3): the test mod's list registers HL2MP's
/// <see cref="INavBuildPolicy"/>, which builds every map; the tf2 mod's list
/// registers TF2's, which builds every map too (the user, 2026-11-15: "nav3d generation not working on map load").
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
    public void TheTF2ListRegistersTF2sPolicy()
        => Assert.IsType<SourceSharp.Ai.TF2.Nav.TFNavBuilds>(Compose(typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), typeof(VisionPass).Assembly.GetTypes(),
                                                                      typeof(SourceSharp.Ai.TF2.TFBotGame).Assembly.GetTypes())
                                                                 .Require<INavBuildPolicy>());

    [Fact]
    public void TF2sPolicyBuildsATF2Map()
        => Assert.True(new SourceSharp.Ai.TF2.Nav.TFNavBuilds().Builds("cp_badlands"));
}
