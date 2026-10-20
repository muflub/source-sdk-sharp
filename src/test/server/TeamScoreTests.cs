using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The mode's team column on the scoreboard wire (tf2 stage 0.7 part C):
/// what <see cref="PlayerResource.UpdateTeamScores"/> writes into the
/// <see cref="PlayerResourceNetState.TeamScore"/> run from the active mode's
/// <see cref="IScoring.TeamScore"/>, and what it does when no mode owns the
/// column. The plan's "team score writes to PlayerResource and the team score
/// fields it needs (add team score to the resource netstate if absent)" — it
/// was absent, so the run exists now, sized at the engine's MAX_TEAMS and
/// read by the client's GetTeamScore (the pairing test pins the pair).
/// </summary>
public class TeamScoreTests
{
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>
    /// The mode under test: per-team numbers from a dictionary, negative for
    /// "no such team", the asks counted, and an optional throw so the
    /// broken-mode fact can watch the last-known numbers survive.
    /// </summary>
    private sealed class Scoring(Dictionary<int, int>? scores, bool throws = false) : IScoring
    {
        public int Asks { get; private set; }
        public int ThrowingTeams = -1;   // which team index throws, -1 = none

        public void OnDeath(PlayerDeathArgs death) { }
        public void ResetRound() { }

        public int TeamScore(int team)
        {
            Asks++;
            if (throws && team == ThrowingTeams)
                throw new InvalidOperationException("broken mode");
            return scores is not null && scores.TryGetValue(team, out int v) ? v : -1;
        }
    }

    private sealed class Flow : IRoundFlow
    {
        public void Think(float now) { }
        public bool RoundOver => false;
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class Mode(string id, IScoring? scoring) : IGameMode
    {
        public string Id { get; } = id;
        public IRoundFlow Rounds { get; } = new Flow();
        public IScoring? Scoring { get; } = scoring;
    }

    private sealed class Selector(string id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    /// <summary>
    /// A player_manager spawned through the real registry over the module's
    /// own parts — the way PlayerResourceTests builds one — plus the mode
    /// proxy the ctor now takes, so the no-owner arm is a resource whose
    /// proxy has no scoring and the owner arm is the same object with one.
    /// </summary>
    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly ScriptedEntityRegistry Registry = new();
        private readonly RecordingEngine _engine;

        public Rig()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);
            _engine = Fake();
            _engine.Returns("Host.ActiveSide", Side.Server);
            _engine.Returns("Players.GetHandle", EntityHandle.None);

            Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>());
            Services.AddInstance<IPlayerDirectory>(
                new Players(new RecordingLog(), _engine, _engine, _engine, NoButtons));

            Registry.RegisterAll(typeof(PlayerResource).Assembly);
            Registry.Services = Services;
            Registry.Lines.Tap = (text, _) => Logged.Add(text);
        }

        /// <summary>Names the mode, the way a selector does at level init.</summary>
        public void Use(IGameMode mode)
        {
            Services.AddInstance(mode.GetType(), mode);
            Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode.Id)));
        }

        public PlayerResource Spawn()
        {
            // The proxy the module registers as a service, built directly as
            // GameModesTests does — the container cannot fill its convars
            // parameter, which is why ServerModule hands it built.
            Modes = new GameModes(new RecordingLog(),
                                  Fake().Returns("Clock.Now", 0f), Services, ConVars);
            Services.AddInstance(Modes);
            var handle = new EntityHandle(90);
            Registry.Spawn(handle, EntityNames.PlayerManager);
            return (PlayerResource)Registry.Get(handle)!;
        }

        /// <summary>The proxy <see cref="Spawn"/> built, for a fact that switches mode.</summary>
        public GameModes? Modes { get; private set; }

        /// <summary>
        /// What the entity's own logger said, through the registry side's
        /// console (InstanceLogTests' route for an entity's line). The tap is
        /// installed from the rig's birth because the warning the facts read
        /// is logged BEFORE they look — a tap set at reading time would miss
        /// every line that had already been written.
        /// </summary>
        public readonly List<string> Logged = [];
    }

    [Fact]
    public void AModeWithScoringPutsItsNumbersOnTheWire()
    {
        var rig = new Rig();
        var scoring = new Scoring(new() { [2] = 41, [3] = 7 });
        rig.Use(new Mode("arena", scoring));
        PlayerResource resource = rig.Spawn();

        resource.UpdateTeamScores();

        Assert.True(resource.State.TeamScore[2] == 41, "the mode's team 2 score reached the run");
        Assert.True(resource.State.TeamScore[3] == 7, "and team 3's");
        Assert.True(resource.State.TeamScore[1] == 0, "a team the mode does not name reads zero");
        Assert.True(scoring.Asks >= EngineLimits.MaxTeams,
            $"the mode was barely asked ({scoring.Asks}): the stimulus did not reach it");
    }

    [Fact]
    public void NoScoringClearsWhatAModeLeftBehind()
    {
        // THE DIFFERENTIAL, and the fact the switch needs: the run is written
        // through to the blob, so numbers a previous mode put there stay
        // unless the pass zeroes them. BEFORE: a hand-planted hundred points;
        // AFTER: the same pass with no scoring on the active mode.
        var rig = new Rig();
        PlayerResource resource = rig.Spawn();
        resource.State.TeamScore[2] = 100;

        resource.UpdateTeamScores();                      // no mode registered at all

        Assert.True(resource.State.TeamScore[2] == 0, "the stale number is gone");

        // And a mode that LEAVES the concern out clears it too (the interface
        // default is null scoring): same clear, not a skip.
        resource.State.TeamScore[3] = 50;
        // Switched to, not just registered: the proxy's pick is cached per
        // level, so a registration after the first pass changes nothing.
        rig.Use(new Mode("arena", scoring: null));
        Assert.True(rig.Modes!.SwitchTo("arena"));
        Assert.Equal("arena", rig.Modes.ActiveId);        // the arm really runs under the scoring-less mode
        resource.UpdateTeamScores();
        Assert.True(resource.State.TeamScore[3] == 0, "a mode without scoring does not inherit the last mode's header");
    }

    [Fact]
    public void ANoSuchTeamAnswerWritesZeroNotTheLastNumber()
    {
        var rig = new Rig();
        var scoring = new Scoring(new() { [2] = 41 });
        rig.Use(new Mode("arena", scoring));
        PlayerResource resource = rig.Spawn();
        resource.State.TeamScore[5] = 33;     // a team this mode never had

        resource.UpdateTeamScores();

        Assert.True(resource.State.TeamScore[2] == 41);
        Assert.True(resource.State.TeamScore[5] == 0,
            "the mode says it has no team 5, so the wire must not keep a score for it");
    }

    [Fact]
    public void AnOversizeTeamScoreClampsRatherThanWraps()
    {
        var rig = new Rig();
        rig.Use(new Mode("arena", new Scoring(new() { [2] = 40000 })));
        PlayerResource resource = rig.Spawn();

        resource.UpdateTeamScores();

        Assert.True(resource.State.TeamScore[2] == short.MaxValue,
            "32,768 wrapped would draw as a team in enormous deficit");
    }

    [Fact]
    public void AThrowingScoringIsNamedAndTheTeamKeepsItsLastScore()
    {
        var rig = new Rig();
        var scoring = new Scoring(new() { [2] = 41 }, throws: true) { ThrowingTeams = 3 };
        rig.Use(new Mode("arena", scoring));
        PlayerResource resource = rig.Spawn();
        resource.State.TeamScore[3] = 60;     // team 3 was winning when the mod broke

        resource.UpdateTeamScores();

        Assert.True(resource.State.TeamScore[2] == 41, "the teams that answer still update");
        Assert.True(resource.State.TeamScore[3] == 60, "a broken answer must not zero a team that was winning");
        string[] lines = rig.Logged.ToArray();
        Assert.True(lines.Any(l => l.Contains("TeamScore(3)") && l.Contains("Scoring")),
            $"the throwing scoring was not named: {string.Join(" | ", lines.Where(l => l.Contains("scoring", StringComparison.OrdinalIgnoreCase)))}");
    }
}
