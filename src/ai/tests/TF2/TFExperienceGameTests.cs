using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.TF2;
using SourceSharp.Server;
using TF2.Server.Objectives;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Rules;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The games' answers to the experience feed (plan_bots_teams_tf2 Part G,
/// G.3): TF2 keeps experience and names a row's context from the mod; HL2MP
/// keeps none (Q-G2).
/// </summary>
public sealed class TFExperienceGameTests
{
    private static TFActorFacts Actor(TFClass cls, TFTeam team, TFClass disguise = TFClass.Undefined, TFTeam disguiseTeam = TFTeam.Unassigned)
        => new(cls, team, disguise, disguiseTeam, 0, 0f, 0, false, false, false, disguise != TFClass.Undefined, false, false);

    private static TFObjective Point(TFTeam owner, TFObjectiveKind kind = TFObjectiveKind.ControlPoint)
        => new(1, kind, owner, TFTeam.Unassigned, TFObjectiveState.Open, default, 0f, 0, 0f, 0f);

    [Fact]
    public void TF2KeepsExperience()
    {
        Assert.True(new TFExperienceGame(null!, null!, null!, null!, null!, null!).Keeps);
    }

    [Fact]
    public void HL2MPKeepsNoneAndGivesNoPlayerARole()
    {
        IExperienceGame game = new Hl2mpExperienceGame();
        Assert.False(game.Keeps);
        Assert.Equal("", game.Mode);
        Assert.Equal((0, 0), game.Of(new EntityHandle(3)));
    }

    [Fact]
    public void TheModeIsTheActiveModesIdAndNoneForTheNullMode()
    {
        Assert.Equal("ctf", TFExperienceGame.ModeOf("ctf"));
        Assert.Equal("", TFExperienceGame.ModeOf(GameModes.NullModeId));
        Assert.Equal("", TFExperienceGame.ModeOf(null));
    }

    [Fact]
    public void TheStageIsTheControlPointsBlueOwns()
    {
        Assert.Equal(0, TFExperienceGame.StageOf([]));
        Assert.Equal(0, TFExperienceGame.StageOf([Point(TFTeam.Red), Point(TFTeam.Blue, TFObjectiveKind.Flag), Point(TFTeam.Blue, TFObjectiveKind.Cart)]));
        Assert.Equal(2, TFExperienceGame.StageOf([Point(TFTeam.Blue), Point(TFTeam.Blue), Point(TFTeam.Red), Point(TFTeam.Unassigned)]));
    }

    [Fact]
    public void APlayersRoleIsItsRealTeamAndItsClassItsRealClass()
    {
        Assert.Equal(((int)TFTeam.Red, (int)TFClass.Soldier), TFExperienceGame.Of(Actor(TFClass.Soldier, TFTeam.Red)));
        // A disguise is drawn, not played: the Spy's rows are a Blue Spy's.
        Assert.Equal(((int)TFTeam.Blue, (int)TFClass.Spy), TFExperienceGame.Of(Actor(TFClass.Spy, TFTeam.Blue, TFClass.Medic, TFTeam.Red)));
    }

    [Fact]
    public void APlayerOffTheTwoTeamsHasNoRole()
    {
        Assert.Equal(0, TFExperienceGame.Of(Actor(TFClass.Undefined, TFTeam.Spectator)).Role);
        Assert.Equal(0, TFExperienceGame.Of(Actor(TFClass.Scout, TFTeam.Unassigned)).Role);
        Assert.Equal(0, TFExperienceGame.RoleOf(TFTeam.Unassigned));
    }

    [Fact]
    public void ARoundIsPlayedWhileRunningOrInSuddenDeath()
    {
        Assert.False(TFExperienceGame.Over(RoundState.RoundRunning, waitingForPlayers: false));
        Assert.False(TFExperienceGame.Over(RoundState.Stalemate, waitingForPlayers: false));
    }

    [Fact]
    public void NoRoundIsPlayedInAnyOtherStateOrWhileWaitingForPlayers()
    {
        foreach (RoundState state in new[] { RoundState.Init, RoundState.Pregame, RoundState.StartGame, RoundState.Preround, RoundState.TeamWin, RoundState.Restart, RoundState.GameOver, RoundState.Bonus, RoundState.BetweenRounds })
            Assert.True(TFExperienceGame.Over(state, waitingForPlayers: false), state.ToString());
        Assert.True(TFExperienceGame.Over(RoundState.RoundRunning, waitingForPlayers: true));
    }
}
