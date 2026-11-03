using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using TF2.Tests.Client;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The round machine built by a container the way the server builds it,
/// beside the objectives that take its contract: a dependency cycle between
/// them refuses the whole mod's load at startup (ServiceResolutionException
/// in LoadSide), which no fact over hand-built objects can see. Measured
/// 2026-10-25: TFTeams -> ITeamRoundState -> TeamRoundFlow -> IRoundWorld ->
/// CoreRoundWorld -> IControlPointRules -> ControlPointRules -> ITeamRoundState.
/// </summary>
public sealed class TeamRoundCompositionTests
{
    private sealed class Settings : IObjectiveSettings
    {
        public int CapStyle => 1;
        public int BlockStyle => 1;
        public float CapDeteriorateTime => 90f;
        public int SimulateMultipleCappers => 0;
        public bool CapLinear => true;
    }

    private static ServiceContainer Container()
    {
        var clock = new FakeClock { Now = 100f };
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        var services = new ServiceContainer();

        // The round machine's neighbours: fakes, as the flow's own facts hold them.
        services.AddInstance<ITeamRoundSettings>(new FakeSettings());
        services.AddInstance<ITeamRoundRoster>(new FakeRoster(clock));
        services.AddInstance<IRoundAnnouncer>(new FakeAnnouncer());
        services.AddInstance<IGameEnd>(new FakeEnd());
        services.AddInstance<IActiveTeamRoundMode>(new ModeSlot());
        services.AddInstance<IRoundTimers>(new FakeTimers());
        services.AddInstance<IGameClock>(clock);
        services.AddInstance<ILog>(new SourceSharp.Abi.RecordingLog());
        services.AddInstance<ITFTeamScores>(new TFTeamScores(new TeamResource()));
        services.AddInstance<IRandomSeeds>(new RandomSeeds(1));   // the server registers one too (ServerModule)

        // The core's half of the world, real.
        services.AddInstance(new GameModes(new SourceSharp.Abi.RecordingLog(), clock, services, convars));
        services.AddInstance(registry.Directory);
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance<IEngineEntities>(TestServices.Fake());
        services.AddInstance<IEngineHost>(TestServices.Fake());   // the machine's dedicated-server read
        services.AddInstance<IMapCleanup>(new NoMapCleanup());   // the core registers one; no map here

        // The objectives' rules, real, over their own neighbours.
        services.AddInstance<IObjectiveResource>(new ObjectiveResource(new ServerWarnScripts()));
        services.AddInstance<IObjectiveSettings>(new Settings());
        services.AddInstance<IObjectiveEntities>(new ObjectiveEntities(registry));

        // The registrations under test, as [SideService] makes them: each
        // class under its own type, its contracts resolving to it.
        services.Add(typeof(TeamBalance), typeof(TeamBalance));
        services.Add(typeof(TeamRoundFlow), typeof(TeamRoundFlow));
        services.AddFactory<ITeamRoundState>(l => l.Require<TeamRoundFlow>());
        services.AddFactory<ITeamRoundControl>(l => l.Require<TeamRoundFlow>());
        services.AddFactory<IRoundTimerHost>(l => l.Require<TeamRoundFlow>());
        services.Add(typeof(KothRoundState), typeof(KothRoundState));
        services.AddFactory<IKothRoundState>(l => l.Require<KothRoundState>());
        services.Add(typeof(CoreRoundWorld), typeof(CoreRoundWorld));
        services.AddFactory<IRoundWorld>(l => l.Require<CoreRoundWorld>());
        services.Add(typeof(ControlPointRules), typeof(ControlPointRules));
        services.AddFactory<IControlPointRules>(l => l.Require<ControlPointRules>());
        return services;
    }

    private sealed class NoMapCleanup : IMapCleanup
    {
        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter) => default;
    }

    [Fact]
    public void The_round_machine_and_the_control_point_rules_resolve_without_a_cycle()
    {
        var services = Container();

        var round = services.Require<ITeamRoundState>();
        var points = services.Require<IControlPointRules>();

        Assert.Equal(RoundState.Pregame, round.State);
        Assert.False(points.PointsMayBeCaptured);   // they read the same machine: not running yet
    }

    [Fact]
    public void The_round_start_reaches_the_control_point_rules_resolved_late()
    {
        var services = Container();
        var world = services.Require<IRoundWorld>();

        // No throw: the rules are found when the round asks, not at construction.
        world.SetupControlPointSpawns();

        Assert.NotNull(services.Resolve<IControlPointRules>());
    }
}
