using SourceSharp.Ai.TF2;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The class pick (plan_bots_teams_tf2 C.4, P7 step 4): the composition fields the ready class the team needs most (a
/// Medic once it has four), never a class that is not ready, and a dead bot changes only for a clearly better need,
/// under one server-wide window and its own stay.
/// </summary>
public sealed class TFClassPickTests
{
    private static int[] Team(params (TFClass Class, int Count)[] have)
    {
        var counts = new int[(int)TFClass.CountAll];
        foreach (var (c, n) in have)
            counts[(int)c] = n;
        return counts;
    }

    private static bool All(TFClass c) => true;

    /// <summary>Only a ready class is fielded: with the Soldier alone ready, a team of nine Soldiers still gets a tenth
    /// (the composition would want another class). Which classes are ready is each class file's flag, so the fact
    /// holds the rule with its own ready set, not today's.</summary>
    [Fact]
    public void OnlyAReadyClassIsFielded()
        => Assert.Equal(TFClass.Soldier, TFClassComposition.Pick(Team((TFClass.Soldier, 9)), TFTeamStance.Symmetric, c => c == TFClass.Soldier));

    /// <summary>A class lane's runs may field its class before it is ready (ai_bot_classes_ready): listed, the Medic is; not listed, it is not.</summary>
    [Fact]
    public void TheReadyListFieldsAClassBeforeItsFileSaysSo()
    {
        Assert.True(TFBotClassPick.ReadyBy(TFClass.Medic, "scout, medic"));
        Assert.False(TFBotClassPick.ReadyBy(TFClass.Medic, "scout"));
        Assert.True(TFBotClassPick.ReadyBy(TFClass.Soldier, ""));
    }

    /// <summary>With every class ready, the next bot picks the class furthest below its share: an empty team's first is the Soldier (the biggest share).</summary>
    [Fact]
    public void TheClassFurthestBelowItsShareIsPicked()
    {
        Assert.Equal(TFClass.Soldier, TFClassComposition.Pick(Team(), TFTeamStance.Symmetric, All));
        Assert.Equal(TFClass.Scout, TFClassComposition.Pick(Team((TFClass.Soldier, 1)), TFTeamStance.Symmetric, All));
    }

    /// <summary>A team of four with no Medic gets one first; a team of three does not.</summary>
    [Fact]
    public void ATeamOfFourGetsAMedic()
    {
        int[] three = Team((TFClass.Soldier, 1), (TFClass.Scout, 1), (TFClass.Demoman, 1));
        Assert.Equal(TFClass.Medic, TFClassComposition.Pick(three, TFTeamStance.Symmetric, All));
        Assert.NotEqual(TFClass.Medic, TFClassComposition.Pick(Team((TFClass.Soldier, 1), (TFClass.Scout, 1)), TFTeamStance.Symmetric, All));
    }

    /// <summary>On defence a team of three with no Engineer gets one.</summary>
    [Fact]
    public void ADefenceGetsAnEngineer()
        => Assert.Equal(TFClass.Engineer, TFClassComposition.Pick(Team((TFClass.Soldier, 1), (TFClass.Demoman, 1)), TFTeamStance.Defend, All));

    /// <summary>A dead Soldier on a team of five Soldiers with no Medic gains by becoming one; with a Medic there is no need, and nobody changes.</summary>
    [Fact]
    public void AClearlyBetterNeedIsWorthAChangeAndNoNeedIsNot()
    {
        (TFClass to, float gain) = TFClassComposition.Change(TFClass.Soldier, Team((TFClass.Soldier, 4)), TFTeamStance.Symmetric, All);
        Assert.True((to, gain > 0f) == (TFClass.Medic, true), $"{to} {gain}");
        int[] balanced = Team((TFClass.Scout, 1), (TFClass.Demoman, 1), (TFClass.Medic, 1), (TFClass.HeavyWeapons, 1));
        Assert.True(TFClassComposition.Change(TFClass.Soldier, balanced, TFTeamStance.Symmetric, All).Gain <= 0f);
    }

    /// <summary>The stay bonus: a class needed only a little more than the bot's own (by under a player) is no reason to change.</summary>
    [Fact]
    public void ASmallNeedIsNoReasonToChange()
    {
        // A dead Pyro on a team of four (a Soldier, a Demoman and a Medic besides): the Scout is wanted at 0.56 of a player and
        // the Pyro at 0.40, more but not a whole player more, so it stays.
        (TFClass to, float gain) = TFClassComposition.Change(TFClass.Pyro, Team((TFClass.Soldier, 1), (TFClass.Demoman, 1), (TFClass.Medic, 1)), TFTeamStance.Symmetric, All);
        Assert.True(to == TFClass.Scout && gain <= 0f, $"{to} {gain}");
    }

    /// <summary>Two bots dying together make one change: the bigger gain wins the window, ties to the lowest slot.</summary>
    [Fact]
    public void OfTwoWantingAtOnceTheBiggerNeedWinsTiesToTheLowestSlot()
    {
        Assert.Equal(7, TFClassChanges.Winner([(3, TFClass.Medic, 0.5f), (7, TFClass.Medic, 1.5f)])!.Value.Slot);
        Assert.Equal(3, TFClassChanges.Winner([(7, TFClass.Medic, 1f), (3, TFClass.Medic, 1f)])!.Value.Slot);
        Assert.Equal(3, TFClassChanges.Winner([(3, TFClass.Medic, 1f), (7, TFClass.Medic, 1f)])!.Value.Slot);
        Assert.Null(TFClassChanges.Winner([(3, TFClass.Medic, 0f)]));
    }

    /// <summary>The window is one clock for every bot on both teams: after a change no bot may until the cooldown has run.</summary>
    [Fact]
    public void TheWindowIsSharedByEveryBot()
    {
        var clock = new TFClassChanges();
        Assert.True(clock.Open(0f, 30f, setup: false));
        clock.Changed(slot: 3, now: 10f, setup: false);
        Assert.False(clock.Open(39f, 30f, setup: false));
        Assert.True(clock.Open(40f, 30f, setup: false));
    }

    /// <summary>A bot inside its own stay does not change, though the window is open; another bot may.</summary>
    [Fact]
    public void ABotInsideItsStayDoesNotChange()
    {
        var clock = new TFClassChanges();
        clock.Changed(slot: 3, now: 10f, setup: false);
        Assert.False(clock.Stayed(3, 100f, 120f, setup: false));
        Assert.True(clock.Stayed(4, 100f, 120f, setup: false));
        Assert.True(clock.Stayed(3, 130f, 120f, setup: false));
    }

    /// <summary>A change in setup time neither waits for the window nor starts it.</summary>
    [Fact]
    public void SetupTimeDoesNotStartTheClock()
    {
        var clock = new TFClassChanges();
        clock.Changed(slot: 3, now: 10f, setup: true);
        Assert.True(clock.Open(11f, 30f, setup: false));
        Assert.True(clock.Stayed(3, 11f, 120f, setup: false));
    }
}
