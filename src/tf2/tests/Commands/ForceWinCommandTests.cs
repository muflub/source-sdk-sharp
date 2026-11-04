using SourceSharp.Abi;

using TF2.Server.Commands;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// mp_forcewin (teamplayroundbased_gamerules.cpp:362-399): the named team
/// wins through the round machine — ALL_POINTS_CAPTURED, or STALEMATE for
/// none — player 1's team when none is named.
/// </summary>
public sealed class ForceWinCommandTests
{
    private sealed class FakeControl : ITeamRoundControl
    {
        public TFTeam Team { get; private set; } = (TFTeam)(-1);

        public WinReason Reason { get; private set; }

        public int Calls { get; private set; }

        public void SetWinningTeam(TFTeam team, WinReason reason, bool forceMapReset = true,
                                    bool switchTeams = false, bool dontAddScore = false, bool final = false)
        {
            Calls++;
            Team = team;
            Reason = reason;
        }

        public void SetStalemate(StalemateReason reason, bool forceMapReset = true, bool switchTeams = false)
            => Calls++;

        public void SetStalemateOnTimelimit(bool stalemate) { }

        public void SetSwitchTeams(bool switchTeams) => Switch = switchTeams;

        public bool ShouldSwitchTeams() => Switch;

        public bool Switch { get; private set; }

        public void SetScrambleTeams(bool scrambleTeams) => Scramble = scrambleTeams;

        public bool ShouldScrambleTeams() => Scramble;

        public bool Scramble { get; private set; }

        public (bool Team, bool Player)? ResetScores { get; private set; }

        public void ShouldResetScores(bool teamScores, bool playerScores) => ResetScores = (teamScores, playerScores);

        public bool? ResetRoundsPlayed { get; private set; }

        public void ShouldResetRoundsPlayed(bool roundsPlayed) => ResetRoundsPlayed = roundsPlayed;
    }

    private sealed class FakeRoster(params EntityHandle[] red) : ITeamRoundRoster
    {
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => team == TFTeam.Red ? red : [];

        public TFTeam TeamOf(EntityHandle player) => red.Contains(player) ? TFTeam.Red : TFTeam.Unassigned;

        public bool IsAlive(EntityHandle player) => true;

        public float DeathTime(EntityHandle player) => -1f;

        public bool IsReadyToPlay(EntityHandle player) => true;

        public void ForceRespawn(EntityHandle player) { }

        public bool ChangeTeam(EntityHandle player, TFTeam team) => true;

        public string Name(EntityHandle player) => "player";

        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    private static (ServiceContainer Services, FakeControl Control, RecordingLog Log) Rig(
        params EntityHandle[] red)
    {
        var services = new ServiceContainer();
        var control = new FakeControl();
        var log = new RecordingLog();
        services.AddInstance<ITeamRoundControl>(control);
        services.AddInstance<ITeamRoundRoster>(new FakeRoster(red));
        return (services, control, log);
    }

    private static void Execute(object command, string args = "") => command.RunLine(args);

    [Fact]
    public void A_named_team_wins_the_round()
    {
        var (services, control, log) = Rig();

        Execute(new ForceWinCommand(log, services), "2");

        Assert.Equal(1, control.Calls);
        Assert.Equal(TFTeam.Red, control.Team);
        Assert.Equal(WinReason.AllPointsCaptured, control.Reason);
        Assert.Equal("", log.Text);
    }

    [Fact]
    public void A_team_may_be_named()
    {
        var (services, control, log) = Rig();

        Execute(new ForceWinCommand(log, services), "blue");

        Assert.Equal(1, control.Calls);
    }

    [Fact]
    public void A_team_completes_from_its_names()
    {
        var (services, _, log) = Rig();

        Assert.Equal(["mp_forcewin red"], new ForceWinCommand(log, services).SuggestFor("r"));
    }

    [Fact]
    public void Team_zero_is_a_forced_stalemate()
    {
        var (services, control, log) = Rig();

        Execute(new ForceWinCommand(log, services), "0");

        Assert.Equal(1, control.Calls);
        Assert.Equal(TFTeam.Unassigned, control.Team);
        Assert.Equal(WinReason.Stalemate, control.Reason);
    }

    [Fact]
    public void Without_a_team_player_1s_team_wins()
    {
        var (services, control, log) = Rig(new EntityHandle(1u), new EntityHandle(2u));

        Execute(new ForceWinCommand(log, services));

        Assert.Equal(1, control.Calls);
        Assert.Equal(TFTeam.Red, control.Team);
        Assert.Equal(WinReason.AllPointsCaptured, control.Reason);
    }

    [Fact]
    public void No_player_and_no_team_is_refused()
    {
        var (services, control, log) = Rig();

        Execute(new ForceWinCommand(log, services));

        Assert.Equal(0, control.Calls);
        Assert.Equal("Unable to determine default team. Usage: mp_forcewin <opt: team#>\n", log.Text);
    }

    [Fact]
    public void Two_teams_is_a_usage_saying()
    {
        var (services, control, log) = Rig();

        new ForceWinCommand(log, services).RunLine("2 3", log.Msg);

        Assert.Equal(0, control.Calls);
        Assert.Contains("usage: mp_forcewin [red|blue|spectator|unassigned]", log.Text);
    }
}
