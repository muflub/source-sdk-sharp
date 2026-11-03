using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// Stock's live team-switch kill (CTFPlayer::ChangeTeam's active-player arm,
/// tf_player.cpp:6687-6696 — "Kill player if switching teams while alive") as
/// the watcher's decision: an alive player moving between RED and BLUE dies
/// through the damage path (nobody as the attacker, generic damage), and the
/// moves stock walks past — the dead switch, a joiner from outside the
/// playing teams, a departure to outside — kill nobody.
/// </summary>
public sealed class TFChangeTeamKillTests
{
    private static readonly EntityHandle Player = new(0x0001_0003u);

    private const int Red = (int)TFTeam.Red;
    private const int Blue = (int)TFTeam.Blue;
    private const int Spectator = (int)TFTeam.Spectator;
    private const int Unassigned = (int)TFTeam.Unassigned;

    /// <summary>The rule over one engine whose life answer the fact chooses.</summary>
    private static (TFChangeTeamKillRule Rule, RecordingEngine Engine) Rig(bool alive)
    {
        var engine = new RecordingEngine().Returns("Players.IsAlive", alive);
        return (new TFChangeTeamKillRule(engine, engine), engine);
    }

    [Fact]
    public void ALiveSwitchBetweenThePlayingTeamsDiesAsASuicide()
    {
        var (rule, engine) = Rig(alive: true);

        rule.TeamChanged(Player, Red, Blue);

        // The house suicide: lethal damage, nobody as the attacker, generic —
        // what stock's CommitSuicide(false, true) asks the damage path for.
        var call = Assert.Single(engine.AllOf("Damage.Apply"));
        Assert.Equal(Player, Assert.IsType<EntityHandle>(call.Args[0]));
        Assert.Equal(EntityHandle.None, Assert.IsType<EntityHandle>(call.Args[1]));
        Assert.Equal(PlayerSelfCommand.SuicideDamage, Assert.IsType<float>(call.Args[2]));
        Assert.Equal(DamageType.Generic, Assert.IsType<DamageType>(call.Args[3]));
    }

    [Fact]
    public void TheDeadSwitchKillsNobody()
    {
        // Stock's guard is !IsDead() (:6692), read after the store — a player
        // who is already dead does not die again.
        var (deadRule, deadEngine) = Rig(alive: false);
        deadRule.TeamChanged(Player, Red, Blue);

        // The control: the same move with a live player DOES kill, so the
        // stimulus reaches the arm and it is the life reading that decides.
        var (liveRule, liveEngine) = Rig(alive: true);
        liveRule.TeamChanged(Player, Red, Blue);

        Assert.Equal(0, deadEngine.CountOf("Damage.Apply"));
        Assert.Equal(1, liveEngine.CountOf("Damage.Apply"));
    }

    [Fact]
    public void JoiningFromOutsideThePlayingTeamsKillsNobody()
    {
        // The kill arm asks iOldTeam == RED || iOldTeam == BLUE (:6692): a
        // queue-waiter who takes a team, or an unassigned joiner, walks past.
        var (fromSpectator, spectatorEngine) = Rig(alive: true);
        fromSpectator.TeamChanged(Player, Spectator, Red);

        var (fromUnassigned, unassignedEngine) = Rig(alive: true);
        fromUnassigned.TeamChanged(Player, Unassigned, Blue);

        Assert.Equal(0, spectatorEngine.CountOf("Damage.Apply"));
        Assert.Equal(0, unassignedEngine.CountOf("Damage.Apply"));
    }

    [Fact]
    public void LeavingToOutsideThePlayingTeamsKillsNobody()
    {
        // Those moves take stock's spectator/unassigned arms (:6671-6686),
        // which never carry the kill.
        var (toSpectator, spectatorEngine) = Rig(alive: true);
        toSpectator.TeamChanged(Player, Red, Spectator);

        var (toUnassigned, unassignedEngine) = Rig(alive: true);
        toUnassigned.TeamChanged(Player, Blue, Unassigned);

        Assert.Equal(0, spectatorEngine.CountOf("Damage.Apply"));
        Assert.Equal(0, unassignedEngine.CountOf("Damage.Apply"));
    }

    [Fact]
    public void TheRuleTakesItsOrderFromTheRulesBand()
    {
        var (rule, _) = Rig(alive: true);

        Assert.Equal(TeamRoundOrders.ChangeTeamKill, rule.HookOrder);
        Assert.Equal(TeamRoundOrders.Band + 11, rule.HookOrder);
    }
}
