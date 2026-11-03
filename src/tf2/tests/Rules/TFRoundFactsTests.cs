using SourceSharp.Abi;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// <see cref="TFRoundFacts"/>: what the AI binding reads of the round, against
/// the round rig the machine's own facts hold. Every answer is a read of the
/// services the round itself answers through, so each fact compares the facts'
/// word with the service's own.
/// </summary>
public sealed class TFRoundFactsTests
{
    /// <summary>A HUD timer whose remaining time is the clock's: it falls as the rig's Step moves time.</summary>
    private sealed class FallingTimer(FakeClock clock, float start, float seconds) : ITeamRoundTimer
    {
        public EntityHandle Handle { get; } = new(0x0004_0001u);

        public float TimeRemaining => MathF.Max(0f, seconds - (clock.Now - start));

        public bool ShowInHud { get; set; } = true;

        public bool IsDisabled { get; set; }

        public bool StartPaused { get; set; }

        public void SetShowInHud(bool show) { }

        public void InputShowInHud(bool show) { }

        public void ResumeTimer() { }
    }

    private static TFRoundFacts FactsOf(RoundRig rig)
        => new(rig.Flow, rig.Flow, rig.Timers, rig.Clock);

    [Fact]
    public void InSetupIsTrueWhileTheSetupTimerRuns()
    {
        var rig = new RoundRig();
        var facts = FactsOf(rig);
        rig.Running();
        Assert.False(rig.Flow.InSetup);   // the machine's own word: not in setup
        Assert.False(facts.InSetup);

        // The timer's state change (TeamRoundTimer's SetState arm, the call
        // stock's team_round_timer makes into the rules): setup begins, and a
        // HUD timer stands elected.
        rig.Flow.SetSetup(true);
        rig.Timers.Active = new FallingTimer(rig.Clock, rig.Clock.Now, 60f);
        Assert.True(facts.InSetup);
    }

    [Fact]
    public void SetupSecondsLeftFallsWithTheClock()
    {
        var rig = new RoundRig();
        var facts = FactsOf(rig);
        rig.Running();
        Assert.Equal(0f, facts.SetupSecondsLeft);   // no setup, no seconds

        rig.Flow.SetSetup(true);
        rig.Timers.Active = new FallingTimer(rig.Clock, rig.Clock.Now, 60f);
        Assert.Equal(60f, facts.SetupSecondsLeft);

        rig.Step(5f);
        Assert.Equal(55f, facts.SetupSecondsLeft);
    }

    [Fact]
    public void RoundSecondsLeftIsMinusOneWithoutATimer()
    {
        var rig = new RoundRig();
        var facts = FactsOf(rig);
        rig.Running();
        Assert.True(rig.Timers.Active is null);   // the rig elects no timer by itself
        Assert.Equal(-1f, facts.RoundSecondsLeft);

        rig.Timers.Active = new FakeTimer { TimeRemaining = 30f };
        Assert.Equal(30f, facts.RoundSecondsLeft);
    }

    [Fact]
    public void NextRespawnWaveInMatchesTheWaves()
    {
        var rig = new RoundRig();
        var facts = FactsOf(rig);
        rig.Running();   // a playing round: the wave schedule is live

        foreach (var team in new[] { TFTeam.Red, TFTeam.Blue })
        {
            float next = rig.Flow.NextRespawnWave(team);
            Assert.Equal(MathF.Max(0f, next - rig.Clock.Now), facts.NextRespawnWaveIn(team));
        }

        // The stimulus reached the subject: a scheduled wave is a positive
        // wait, not the no-wave zero.
        Assert.True(rig.Flow.NextRespawnWave(TFTeam.Red) > 0f, "no wave was ever scheduled");
        Assert.True(facts.NextRespawnWaveIn(TFTeam.Red) > 0f, "the wait for a scheduled wave was not positive");
    }

    [Fact]
    public void WinningTeamIsSetOnlyInTeamWin()
    {
        var rig = new RoundRig();
        var facts = FactsOf(rig);
        rig.Running();
        Assert.Equal(TFTeam.Unassigned, facts.WinningTeam);   // no winner yet

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Blue, facts.WinningTeam);
    }

    /// <summary>The round machine's answers, settable, for the composition fact.</summary>
    private sealed class FixedRound : ITeamRoundState
    {
        public RoundState State { get; set; } = RoundState.Pregame;
        public bool InWaitingForPlayers { get; set; }
        public bool InSetup { get; set; }
        public float MapResetTime { get; set; }
        public float StalemateStartTime { get; set; } = -1f;
        public TFTeam WinningTeam { get; set; } = TFTeam.Unassigned;
    }

    /// <summary>Respawn waves with none scheduled, for the composition fact.</summary>
    private sealed class NoWaves : ITeamRespawnWaves
    {
        public float NextRespawnWave(TFTeam team) => 0f;
        public float NextRespawnWave(TFTeam team, EntityHandle player) => 0f;
        public float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true) => 0f;
        public void SetTeamRespawnWaveTime(TFTeam team, float seconds) { }
        public void AddTeamRespawnWaveTime(TFTeam team, float seconds) { }
    }

    [Fact]
    public void TheFactsResolveFromTheServerRegistry()
    {
        var services = new ServiceContainer();
        services.AddInstance<ITeamRoundState>(new FixedRound());
        services.AddInstance<ITeamRespawnWaves>(new NoWaves());
        services.AddInstance<IRoundTimers>(new FakeTimers());
        services.AddInstance<IGameClock>(new FakeClock { Now = 100f });

        // The registration under test, as [SideService] makes it.
        SideServices.Register(services, Side.Server, [typeof(TFRoundFacts)]);

        var facts = services.Require<ITFRoundFacts>();
        Assert.Equal(RoundState.Pregame, facts.State);
        Assert.False(facts.InSetup);
        Assert.Equal(0f, facts.SetupSecondsLeft);
        Assert.Equal(-1f, facts.RoundSecondsLeft);   // no timer elected
        Assert.Equal(0f, facts.NextRespawnWaveIn(TFTeam.Red));
        Assert.False(facts.IsWaitingForPlayers);
        Assert.Equal(TFTeam.Unassigned, facts.WinningTeam);
    }
}
