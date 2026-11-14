using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using SourceSharp.Ai.Server.Senses;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// <see cref="EngineRenderedTeams.Score"/>: the team score the HUD shows, read
/// from the active mode's <see cref="IScoring"/> through the real mode proxy.
/// </summary>
public class RenderedTeamsTests
{
    private sealed class Quiet : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class Clock : IGameClock
    {
        public float Now => 0f;
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Selector(string id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    private sealed class Flow : IRoundFlow
    {
        public void Think(float now) { }
        public bool RoundOver => false;
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class Scores(Dictionary<int, int> scores) : IScoring
    {
        public void OnDeath(PlayerDeathArgs death) { }
        public void ResetRound() { }
        public int? TeamScore(int team) => scores.TryGetValue(team, out int v) ? v : null;
    }

    private sealed class TeamMode(IScoring scoring) : IGameMode
    {
        public string Id => "teams";
        public IRoundFlow Rounds { get; } = new Flow();
        public IScoring? Scoring => scoring;
        public bool Teamplay => true;
    }

    private static EngineRenderedTeams Over(Dictionary<int, int> scores)
    {
        var services = new ServiceContainer();
        var mode = new TeamMode(new Scores(scores));
        services.AddInstance(typeof(TeamMode), mode);
        services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode.Id)));
        var modes = new GameModes(new Quiet(), new Clock(), services, DeclaredConVars.Fresh<SvModeSwitchWarmupConVar>(),
                                  DeclaredConVars.Fresh<MpWaitingForPlayersTimeConVar>());
        return new EngineRenderedTeams(modes, null!, DeclaredConVars.Fresh<MpFriendlyFireConVar>(),
                                       DeclaredConVars.Fresh<TdmTeamsConVar>());
    }

    /// <summary>A team below zero is shown below zero (stock CTeam::m_iScore is signed), as the wire now carries it.</summary>
    [Fact]
    public void ANegativeTeamScoreIsWhatTheHudShows()
    {
        EngineRenderedTeams teams = Over(new() { [2] = 4, [3] = -1 });

        Assert.True(teams.Teamplay, "the mode under test plays teams (the read is not the no-teams arm)");
        Assert.Equal((4, -1), (teams.Score(2), teams.Score(3)));
    }

    private sealed class SoloMode : IGameMode
    {
        public string Id => "solo";
        public IRoundFlow Rounds { get; } = new Flow();
    }

    /// <summary>Every player on team 2, as the engine's team store answers.</summary>
    public class OnTeamTwo : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
            => method!.Name == nameof(IEnginePlayers.GetTeam) ? 2 : throw new NotSupportedException(method.Name);
    }

    /// <summary>
    /// A mode switch reaches every bot's view of the teams at once (plan_bots_teams_tf2
    /// D.2, step 8): the friends a decider reads are the rendered teams, which follow
    /// the playing mode's teamplay, so off it a player on team 2 is on none.
    /// </summary>
    [Fact]
    public void ASwitchToAModeWithoutTeamsPutsEveryoneOnNoneAndBackOnTheirTeam()
    {
        var services = new ServiceContainer();
        var mode = new TeamMode(new Scores([]));
        services.AddInstance(typeof(TeamMode), mode);
        services.AddInstance(typeof(SoloMode), new SoloMode());
        services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode.Id)));
        var modes = new GameModes(new Quiet(), new Clock(), services, DeclaredConVars.Fresh<SvModeSwitchWarmupConVar>(),
                                  DeclaredConVars.Fresh<MpWaitingForPlayersTimeConVar>());
        var teams = new EngineRenderedTeams(modes, System.Reflection.DispatchProxy.Create<IEnginePlayers, OnTeamTwo>(),
                                            DeclaredConVars.Fresh<MpFriendlyFireConVar>(), DeclaredConVars.Fresh<TdmTeamsConVar>());
        var player = new EntityHandle(0x10002u);
        Assert.Equal(2, teams.TeamOf(player));

        Assert.True(modes.SwitchTo("solo"));
        Assert.Equal((false, 0, 0), (teams.Teamplay, teams.TeamOf(player), teams.TeamCount));
        Assert.True(modes.SwitchTo("teams"));

        Assert.Equal((true, 2), (teams.Teamplay, teams.TeamOf(player)));
    }

    [Fact]
    public void NoSuchTeamReadsZero()
        => Assert.Equal(0, Over(new() { [2] = 4 }).Score(7));
}
