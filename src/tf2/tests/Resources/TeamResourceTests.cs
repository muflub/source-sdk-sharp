using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server.Buildings;
using TF2.Server.Entities;
using TF2.Server.Entities.Items;
using TF2.Shared;
using TF2.Shared.Resources;
using TF2.Tests.Buildings;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Resources;

/// <summary>The per-team table (stage 2.4): score, role and player count per TF team.</summary>
public sealed class TeamResourceTests
{
    private sealed class FakeTeams : ITeams
    {
        public readonly Dictionary<int, List<EntityHandle>> ByTeam = new();

        public IReadOnlyList<EntityHandle> Players(int team) => ByTeam.TryGetValue(team, out var list) ? list : [];
        public int Count(int team) => Players(team).Count;
        public int Team(EntityHandle player) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;

        public void SetTeam(int team, params EntityHandle[] players) => ByTeam[team] = [.. players];
    }

    /// <summary>The registry over the fakes the team entity and its buildings read, and the spawned entity.</summary>
    private (ScriptedEntityRegistry Registry, TeamResource Table, EntityHandle Handle, TeamResourceEntity Entity, FakeTeams Teams) Rig()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TeamResourceEntity).Assembly);
        var services = TestServices.Services(out RecordingEngine engine);
        var table = new TeamResource();
        var teams = new FakeTeams();
        services.AddInstance<ITeamResource>(table);
        services.AddInstance<ITeams>(teams);
        services.AddInstance<ITFMapPlayers>(new FakeMapPlayers());
        services.AddInstance<ITFObjectAnnouncer>(new FakeAnnouncer());
        services.AddInstance<ITFAmmoPurse>(new FakePurse());
        services.AddInstance<ITFBuildings>(new TFBuildings(registry));
        services.AddInstance<IUniformRandom>(new RigRandom());
        // the entity index of a player or building is the entry index, per
        // EntityHandle; the engine fake answers it from the asking call
        engine.ReturnsEachCall("Entities.GetIndex", () =>
            (int)(engine.Last("Entities.GetIndex") ?? default).ArgAs<EntityHandle>(0).EntryIndex);
        registry.Services = services;

        var handle = new EntityHandle(0x0022_0022u);
        registry.Spawn(handle, ObjectiveClassNames.TeamResource);
        var entity = Assert.IsType<TeamResourceEntity>(registry.Get(handle));
        return (registry, table, handle, entity, teams);
    }

    [Fact]
    public void A_score_written_before_the_entity_is_published_on_attach()
    {
        var table = new TeamResource();
        table.SetScore((int)TFTeam.Red, 3);
        table.SetRole((int)TFTeam.Blue, 2);

        var state = new TeamResourceNetState();
        state.BindLocal();
        table.Attach(state);

        Assert.Equal(3, state.Score[(int)TFTeam.Red]);
        Assert.Equal(2, state.Role[(int)TFTeam.Blue]);
    }

    [Fact]
    public void The_entity_counts_each_teams_players_on_its_think()
    {
        var (registry, _, handle, entity, teams) = Rig();
        teams.SetTeam((int)TFTeam.Red,
            new EntityHandle(0x0005_0001u), new EntityHandle(0x0005_0002u), new EntityHandle(0x0005_0003u),
            new EntityHandle(0x0005_0004u));

        registry.Think(handle);

        Assert.Equal(4, entity.State.PlayerCount[(int)TFTeam.Red]);

        // the first player leads (CTFTeam::AddPlayer); the leader is the
        // wire's entity index, not a handle
        Assert.Equal(1, entity.State.Leader[(int)TFTeam.Red]);

        // the team emptied: the leader is none, as RemovePlayer's m_hLeader
        // clearing leaves it
        teams.SetTeam((int)TFTeam.Red);
        registry.Think(handle);
        Assert.Equal(0, entity.State.Leader[(int)TFTeam.Red]);
    }

    [Fact]
    public void A_leader_who_leaves_hands_the_team_to_a_random_member()
    {
        var (registry, _, handle, entity, teams) = Rig();
        var first = new EntityHandle(0x0005_0001u);
        var second = new EntityHandle(0x0005_0002u);
        teams.SetTeam((int)TFTeam.Blue, first, second);
        registry.Think(handle);
        Assert.Equal(1, entity.State.Leader[(int)TFTeam.Blue]);

        // the leader leaves: the draw is RigRandom's bottom of the range, so
        // the remaining player leads (tf_team.cpp:465-479)
        teams.SetTeam((int)TFTeam.Blue, second);
        registry.Think(handle);
        Assert.Equal(2, entity.State.Leader[(int)TFTeam.Blue]);
    }

    /// <summary>
    /// The buildings live on the buildings rig, which carries the dispenser's
    /// seams; the team entity is spawned on its registry with the table and
    /// the player fake grafted on.
    /// </summary>
    private static (BuildingsRig Rig, TeamResourceEntity Entity, FakeTeams Teams, TFDispenser Building) BuildingRig()
    {
        var rig = new BuildingsRig();
        var teams = new FakeTeams();
        rig.Services.AddInstance<ITeamResource>(new TeamResource());
        rig.Services.AddInstance<ITeams>(teams);
        var engine = rig.Engine;
        engine.Returns("Entities.GetIndex", 7);

        var handle = new EntityHandle(0x0022_0022u);
        rig.Registry.Spawn(handle, ObjectiveClassNames.TeamResource);
        var entity = Assert.IsType<TeamResourceEntity>(rig.Registry.Get(handle));

        var building = rig.Spawn<TFDispenser>(TFObjectClassNames.Raw(TFObjectType.Dispenser));
        building.StartPlacement(rig.AddBuilder(TFTeam.Red));
        return (rig, entity, teams, building);
    }

    [Fact]
    public void The_entity_lists_each_teams_buildings_as_entity_indices()
    {
        var (rig, entity, _, building) = BuildingRig();

        rig.Registry.Think(new EntityHandle(0x0022_0022u));

        // TEMP probe
        int at = (int)TFTeam.Red * ObjectiveLimits.TeamObjects;

        // one live building of the red team, named by its entity index (the
        // engine fake answers 7); the other teams' runs stay empty
        Assert.Equal(7, entity.State.Objects[at]);
        Assert.Equal(0, entity.State.Objects[(int)TFTeam.Blue * ObjectiveLimits.TeamObjects]);
    }

    [Fact]
    public void A_dropped_building_leaves_the_run_at_the_next_think()
    {
        var (rig, entity, _, building) = BuildingRig();

        rig.Registry.Think(new EntityHandle(0x0022_0022u));
        Assert.Equal(7, entity.State.Objects[(int)TFTeam.Red * ObjectiveLimits.TeamObjects]);

        building.OnKilled(EntityHandle.None);
        rig.Registry.Think(new EntityHandle(0x0022_0022u));

        // IsDead takes it out of the enumeration (TFBuildings), so the run
        // the wire carries empties
        Assert.Equal(0, entity.State.Objects[(int)TFTeam.Red * ObjectiveLimits.TeamObjects]);
    }
}
