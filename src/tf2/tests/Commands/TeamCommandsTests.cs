using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Commands;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// <c>mp_switchteams</c> and <c>mp_scrambleteams</c>
/// (teamplayroundbased_gamerules.cpp:276-321): the admin gate, the flag each
/// arms, the five-second game restart both set, and the reset flags they
/// disarm so that restart keeps what the switch trades and the scramble
/// sorts by — <c>mp_scrambleteams 2</c> keeping the rounds played too.
/// </summary>
public sealed class TeamCommandsTests
{
    private sealed class FakeControl : ITeamRoundControl
    {
        public readonly List<string> Calls = new();

        public (bool Team, bool Player)? ResetScores { get; private set; }

        public bool? ResetRounds { get; private set; }

        public void SetWinningTeam(TFTeam team, WinReason reason, bool forceMapReset = true,
                                    bool switchTeams = false, bool dontAddScore = false, bool final = false)
            => Calls.Add("win");

        public void SetStalemate(StalemateReason reason, bool forceMapReset = true, bool switchTeams = false)
            => Calls.Add("stalemate");

        public void SetStalemateOnTimelimit(bool stalemate) { }

        public void SetSwitchTeams(bool switchTeams) => Calls.Add($"switch {switchTeams}");

        public bool ShouldSwitchTeams() => Calls.Contains("switch True");

        public void SetScrambleTeams(bool scrambleTeams) => Calls.Add($"scramble {scrambleTeams}");

        public bool ShouldScrambleTeams() => Calls.Contains("scramble True");

        public void ShouldResetScores(bool teamScores, bool playerScores)
            => ResetScores = (teamScores, playerScores);

        public void ShouldResetRoundsPlayed(bool roundsPlayed) => ResetRounds = roundsPlayed;
    }

    private static (ServiceContainer Services, FakeControl Control, FakeSettings Settings) Rig()
    {
        var services = new ServiceContainer();
        var control = new FakeControl();
        var settings = new FakeSettings();
        services.AddInstance<ITeamRoundControl>(control);
        services.AddInstance<ITeamRoundSettings>(settings);
        return (services, control, settings);
    }

    /// <summary>
    /// The engine double answering the admin rule's three questions
    /// (<see cref="ServerAdmin.MayRun(IEnginePlayers, IEngineHost)"/>): who
    /// typed it and whether this is a dedicated server, with the slot holding
    /// the typing player's handle scripted per call, as
    /// <c>EntFireAccess.SlotOf</c> walks the slots.
    /// </summary>
    private static RecordingEngine Engine(bool dedicated, EntityHandle commandClient, int slotHoldingIt = 0)
    {
        var engine = new RecordingEngine();
        engine.Returns("Host.IsDedicatedServer", dedicated);
        engine.Returns("Players.GetCommandClient", commandClient);
        int asked = 0;
        engine.ReturnsEachCall("Players.GetHandle",
            () => ++asked == slotHoldingIt ? commandClient : EntityHandle.None);
        return engine;
    }

    private static RecordingEngine ConsoleEngine() => Engine(dedicated: false, commandClient: EntityHandle.None);

    [Fact]
    public void mp_switchteams_arms_the_switch_the_restart_and_keeps_every_score()
    {
        // cc_SwitchTeams statement for statement (:278-288): the admin's word
        // arms the switch, mp_restartgame 5, and disarms the next reset's
        // team, player and rounds-played arms — the switch trades the team
        // scores, so they must stand.
        var (services, control, settings) = Rig();
        var engine = ConsoleEngine();

        new SwitchTeamsCommand(services, engine, engine).Execute("");

        Assert.Contains("switch True", control.Calls);
        Assert.Equal(5, settings.RestartGame);
        Assert.Equal((false, false), control.ResetScores);
        Assert.Equal(false, control.ResetRounds);
    }

    [Fact]
    public void mp_switchteams_from_a_players_slot_is_refused()
    {
        // UTIL_IsCommandIssuedByServerAdmin (:278): slot two typed it, which
        // is not an admin — nothing is armed and no restart set.
        var (services, control, settings) = Rig();
        var engine = Engine(dedicated: false, commandClient: new EntityHandle(7u), slotHoldingIt: 2);

        new SwitchTeamsCommand(services, engine, engine).Execute("");

        Assert.Empty(control.Calls);
        Assert.Null(control.ResetScores);
        Assert.Null(control.ResetRounds);
        Assert.Equal(0, settings.RestartGame);
    }

    [Fact]
    public void mp_scrambleteams_arms_the_scramble_resets_team_scores_and_keeps_the_players()
    {
        // cc_ScrambleTeams (:305-307): the scramble flag, the restart, and
        // ShouldResetScores(true, false) — the team scores go with the redraw,
        // the accumulated stats stand for the sort and the swap pass. The
        // rounds-played flag is not touched without the second argument
        // (its default stays armed).
        var (services, control, settings) = Rig();
        var engine = ConsoleEngine();

        new ScrambleTeamsCommand(services, engine, engine).Execute("");

        Assert.Contains("scramble True", control.Calls);
        Assert.Equal(5, settings.RestartGame);
        Assert.Equal((true, false), control.ResetScores);
        Assert.Null(control.ResetRounds);
    }

    [Fact]
    public void mp_scrambleteams_2_keeps_the_rounds_played_too()
    {
        // The argument arm (:309-316): ArgC() == 2 and the word is 2 —
        // mp_scrambleteams 2, used at a competitive match's restart.
        var (services, control, settings) = Rig();
        var engine = ConsoleEngine();

        new ScrambleTeamsCommand(services, engine, engine).Execute("2");

        Assert.Contains("scramble True", control.Calls);
        Assert.Equal((true, false), control.ResetScores);
        Assert.Equal(false, control.ResetRounds);
    }

    [Fact]
    public void Any_other_word_than_2_leaves_the_rounds_played_resetting()
    {
        // atoi(args[1]) == 2 decides alone (:312): "3" is a second argument
        // that does not match, and two arguments overshoot the ArgC() == 2
        // guard.
        var (services, control, _) = Rig();
        var engine = ConsoleEngine();

        new ScrambleTeamsCommand(services, engine, engine).Execute("3");
        Assert.Null(control.ResetRounds);

        new ScrambleTeamsCommand(services, engine, engine).Execute("2 2");
        Assert.Null(control.ResetRounds);
    }
}
