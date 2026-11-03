using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Rules;
using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFDominations"/>: stock's domination and revenge
/// (<c>CTFGameRules::CalcDominationAndRevenge</c>, tf_gamerules.cpp:11961-12049,
/// and <c>CTFGameStats::TrackKillStats</c>, tf_gamestats.cpp:1856-1871) —
/// the death flags, the dominates state and the round's clear.
/// </summary>
public sealed class TFDominationsTests
{
    private const int Victim = 3;
    private const int Attacker = 4;
    private const int Third = 5;

    private static void Kills(TFDominations dominations, int victim, int attacker, int times = 1)
    {
        for (int i = 0; i < times; i++)
            dominations.DeathFlags(victim, attacker, -1);
    }

    [Fact]
    public void The_flags_are_stocks_bits_at_stocks_count()
    {
        Assert.Equal(0x0001, TFDominations.Domination);
        Assert.Equal(0x0002, TFDominations.AssisterDomination);
        Assert.Equal(0x0004, TFDominations.Revenge);
        Assert.Equal(0x0008, TFDominations.AssisterRevenge);
        Assert.Equal(4, TFDominations.KillsToDominate);
    }

    [Fact]
    public void The_fourth_unanswered_kill_dominates()
    {
        var dominations = new TFDominations();

        Kills(dominations, Victim, Attacker, 2);
        Assert.Equal(0, dominations.DeathFlags(Victim, Attacker, -1));
        Assert.False(dominations.Dominates(Attacker, Victim));

        Assert.Equal(TFDominations.Domination, dominations.DeathFlags(Victim, Attacker, -1));
        Assert.True(dominations.Dominates(Attacker, Victim));
        Assert.Equal(1, dominations.CountOf(Attacker));
    }

    [Fact]
    public void The_fourth_kill_is_the_only_one_that_carries_the_flag()
    {
        var dominations = new TFDominations();

        Kills(dominations, Victim, Attacker, 4);
        // Already dominating: the fifth kill flags nothing (tf_gamerules.cpp:12042).
        Assert.Equal(0, dominations.DeathFlags(Victim, Attacker, -1));
    }

    [Fact]
    public void The_revenge_kill_flags_and_forgives()
    {
        var dominations = new TFDominations();
        Kills(dominations, Victim, Attacker, 4);

        Assert.Equal(TFDominations.Revenge, dominations.DeathFlags(Attacker, Victim, -1));
        Assert.False(dominations.Dominates(Attacker, Victim));
        Assert.Equal(0, dominations.CountOf(Attacker));
    }

    [Fact]
    public void The_answer_resets_the_unanswered_count()
    {
        var dominations = new TFDominations();
        Kills(dominations, Victim, Attacker, 4);
        Assert.Equal(TFDominations.Revenge, dominations.DeathFlags(Attacker, Victim, -1));

        // The revenge answered the run: three more kills are not enough again.
        Kills(dominations, Victim, Attacker, 2);
        Assert.Equal(0, dominations.DeathFlags(Victim, Attacker, -1));
        Assert.Equal(TFDominations.Domination, dominations.DeathFlags(Victim, Attacker, -1));
    }

    [Fact]
    public void An_assisted_domination_flags_the_assister()
    {
        var dominations = new TFDominations();
        Kills(dominations, Victim, Attacker, 3);

        Assert.Equal(TFDominations.AssisterDomination, dominations.DeathFlags(Victim, Third, Attacker));
        Assert.True(dominations.Dominates(Attacker, Victim));
    }

    [Fact]
    public void An_assisted_revenge_flags_the_assister()
    {
        var dominations = new TFDominations();
        Kills(dominations, Victim, Attacker, 4);

        Assert.Equal(TFDominations.AssisterRevenge, dominations.DeathFlags(Attacker, Third, Victim));
        Assert.False(dominations.Dominates(Attacker, Victim));
    }

    [Fact]
    public void A_suicide_records_nothing()
    {
        var dominations = new TFDominations();

        Assert.Equal(0, dominations.DeathFlags(Victim, Victim, -1));
        Assert.Equal(0, dominations.DeathFlags(Victim, -1, -1));
        Assert.False(dominations.Dominates(Victim, Victim));
        Assert.Equal(0, dominations.CountOf(Victim));
    }

    [Fact]
    public void A_round_restart_clears_the_domination()
    {
        var dominations = new TFDominations();
        Kills(dominations, Victim, Attacker, 4);

        ((ITeamRoundListener)dominations).OnStateChanged(RoundState.TeamWin, RoundState.Preround);

        Assert.False(dominations.Dominates(Attacker, Victim));
        Assert.Equal(0, dominations.CountOf(Attacker));
        // The counts too: four fresh kills before the next domination.
        Kills(dominations, Victim, Attacker, 2);
        Assert.Equal(0, dominations.DeathFlags(Victim, Attacker, -1));
        Assert.Equal(TFDominations.Domination, dominations.DeathFlags(Victim, Attacker, -1));
    }

    [Fact]
    public void A_departure_forgives_their_kills()
    {
        var dominations = new TFDominations();
        Kills(dominations, Victim, Attacker, 4);

        ((IClientLifecycle)dominations).OnClientDisconnected(Attacker);

        Assert.False(dominations.Dominates(Attacker, Victim));
        Assert.Equal(0, dominations.CountOf(Attacker));
        Kills(dominations, Victim, Attacker, 2);
        Assert.Equal(0, dominations.DeathFlags(Victim, Attacker, -1));
        Assert.Equal(TFDominations.Domination, dominations.DeathFlags(Victim, Attacker, -1));
    }

    /// <summary>A domination marks the dominator's carrier
    /// (<c>SetPlayerDominated</c>, tf_player_shared.cpp:7701-7706) and the
    /// revenge forgives it.</summary>
    [Fact]
    public void A_domination_marks_the_dominators_carrier()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var dominations = new TFDominations(rig.Carriers);

        Kills(dominations, 9, PlayerRig.Index, 4);

        Assert.True(rig.State.PlayerDominated[9]);
        Assert.False(rig.State.PlayerDominatingMe[9]);

        Assert.Equal(TFDominations.Revenge, dominations.DeathFlags(PlayerRig.Index, 9, -1));
        Assert.False(rig.State.PlayerDominated[9]);
    }

    /// <summary>The dominated player's own carrier learns who sits on them
    /// (<c>SetPlayerDominatingMe</c> through <c>SetPlayerDominated</c>'s
    /// second line, tf_player_shared.cpp:7705).</summary>
    [Fact]
    public void A_domination_marks_the_dominated_carrier()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var dominations = new TFDominations(rig.Carriers);

        Kills(dominations, PlayerRig.Index, 9, 4);

        Assert.True(rig.State.PlayerDominatingMe[9]);
        Assert.False(rig.State.PlayerDominated[9]);

        Assert.Equal(TFDominations.Revenge, dominations.DeathFlags(9, PlayerRig.Index, -1));
        Assert.False(rig.State.PlayerDominatingMe[9]);
    }

    /// <summary>A departure forgives the survivors' carrier bits and their
    /// counts (stock's <c>RemoveNemesisRelationships</c>,
    /// tf_player.cpp:3979-3992, the decrement arm).</summary>
    [Fact]
    public void A_departure_forgives_the_survivors_carrier_bits_and_counts()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var dominations = new TFDominations(rig.Carriers);
        Kills(dominations, 9, PlayerRig.Index, 4);

        ((IClientLifecycle)dominations).OnClientDisconnected(9);

        Assert.False(rig.State.PlayerDominated[9]);
        Assert.Equal(0, dominations.CountOf(PlayerRig.Index));
    }

    /// <summary>A round restart clears the carriers' bits with the rest
    /// (stock's <c>ResetScores</c> → <c>RemoveNemesisRelationships</c>).</summary>
    [Fact]
    public void A_round_restart_clears_the_carriers_bits()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var dominations = new TFDominations(rig.Carriers);
        Kills(dominations, 9, PlayerRig.Index, 4);

        ((ITeamRoundListener)dominations).OnStateChanged(RoundState.TeamWin, RoundState.Preround);

        Assert.False(rig.State.PlayerDominated[9]);
        Assert.False(rig.State.PlayerDominatingMe[9]);
    }

    /// <summary>The notice writes what the state computed (tf_gamerules.cpp:11500-11518: the flags, then the kill).</summary>
    [Fact]
    public void The_death_notice_writes_the_flags()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun" };
        rig.Hit(6f, DamageType.Bullet);

        var dominations = new TFDominations();
        Kills(dominations, CombatRig.Victim.EntryIndex, CombatRig.Attacker.EntryIndex, 3);
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock, rig.Carriers, dominations);
        var fields = new TFDeathNoticeTests.Fields();

        notice.Fill(new DeathNoticeFacts(CombatRig.Victim, CombatRig.Attacker, CombatRig.Attacker,
                                         CombatRig.Attacker, "tf_weapon_scattergun"), fields);

        Assert.Equal(TFDominations.Domination, fields.Set["death_flags"]);
    }
}
