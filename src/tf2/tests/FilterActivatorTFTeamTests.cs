using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Entities;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// TF2's own team filter (tf_filters.cpp CFilterTFTeam): the activator's
/// team is compared against the TeamNum the map gives the filter itself.
/// Stock's HL2 filter_activator_team reads a different key ("filterteam"),
/// so a TF2 map — every ctf_2fort spawn-door trigger names one — needs its
/// own class beside the other TF entities.
/// </summary>
public sealed class FilterActivatorTFTeamTests
{
    private static readonly EntityHandle Ent = new(0x0021_0021u);
    private static readonly EntityHandle Subject = new(0x0022_0022u);

    [Fact]
    public void TFTeamMatchesTheFiltersOwnTeam()
    {
        // Same team: the activator answers 2 where the filter says TeamNum 2.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FilterActivatorTFTeam).Assembly);
        var services = TestServices.Services(out RecordingEngine engine);
        engine.Returns("Players.GetTeam", 2);
        registry.Services = services;
        registry.KeyValue(Ent, FilterActivatorTFTeam.MapClass, "TeamNum", "2");
        registry.Spawn(Ent, FilterActivatorTFTeam.MapClass);

        Assert.True(registry.Get(Ent) is FilterBase, "the tf2 team filter spawns");
        Assert.True(((FilterBase)registry.Get(Ent)!).Passes(Subject),
                    "a player on the filter's own team passes");

        // Another team: the same filter with a blue activator refuses.
        var registry2 = new ScriptedEntityRegistry(TestEngines.Server());
        registry2.RegisterAll(typeof(FilterActivatorTFTeam).Assembly);
        var services2 = TestServices.Services(out RecordingEngine engine2);
        engine2.Returns("Players.GetTeam", 3);
        registry2.Services = services2;
        registry2.KeyValue(Ent, FilterActivatorTFTeam.MapClass, "TeamNum", "2");
        registry2.Spawn(Ent, FilterActivatorTFTeam.MapClass);

        Assert.True(((FilterBase)registry2.Get(Ent)!).Passes(Subject) == false,
                    "a player on another team is refused");
    }
}
