using SourceSharp.Ai.Tests.Trees;
using Xunit;
using static SourceSharp.Ai.Tests.Teams.TeamMemoryTests;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// A player changes team mid-game (plan_bots_teams_tf2 B.9): friend or foe is read from the
/// current row, so a swap takes effect at once; what was tracked of an enemy goes when it
/// turns friend; a friend turned enemy is a target only after a recognition; a bot moved
/// itself starts its beliefs over and keeps who it knows.
/// </summary>
public sealed class TeamSwapTests
{
    [Fact]
    public void AFoeTurnedFriendLosesItsAmmoEstimateAndKeepsItsHabits()
    {
        BotMemory m = TeamMemoryTests.Memory();
        m.Step(new Tick(6), View(6, 2, (Foe, 3, 500f)), [AiEvent.SightGained(Bot, new Tick(6), Foe, new AiVector(500, 0, 0), default, 1f)]);
        m.Apply(AiEvent.SeenReload(Bot, new Tick(7), Foe, ReloadPhase.Start, 3));
        Assert.True(m.Board.Of(Foe, make: false)!.Ammo.Known && m.TryHabits(Foe, out _), "the control: tracked as an enemy");
        m.Apply(AiEvent.ActorTeam(Bot, new Tick(9), Foe, 2, 3));
        Assert.False(m.Board.Of(Foe, make: false)!.Ammo.Known);
        Assert.True(m.TryHabits(Foe, out _));
    }

    /// <summary>A bot firing at someone who swaps to its team stops in the same decide, and fires at nothing friendly.</summary>
    [Fact]
    public void ABotFiringAtASwapperStopsAtOnce()
    {
        var d = new Duel(Duel.Preset("hard"));
        var at = new AiVector(400, 0, 0);
        SelfState self = d.Self() with { Team = 2 };
        d.StepTeams(self, [(Duel.Opponent, at, 3)], d.SeeAt(Duel.Opponent, at));
        Assert.Equal(ActionKind.Fire, d.StepTeams(self, [(Duel.Opponent, at, 3)]).Action.Kind);
        IntentCommand i = d.StepTeams(self, [(Duel.Opponent, at, 2)], AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, 2, 3));
        Assert.Equal((ActorId.None, ActionKind.None), (d.Decider.Doing.Target, i.Action.Kind));
    }

    /// <summary>A teammate in sight who swaps to the other team is engaged only after the bot's recognition, not in the same decide.</summary>
    [Fact]
    public void ASwappedFoeIsEngagedOnlyAfterRecognition()
    {
        var d = new Duel(Duel.Preset("hard"));
        var at = new AiVector(400, 0, 0);
        SelfState self = d.Self() with { Team = 2 };
        d.StepTeams(self, [(Duel.Other, at, 2)], d.SeeAt(Duel.Other, at));
        d.StepTeams(self, [(Duel.Other, at, 3)], AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Duel.Other, 3, 2));
        Assert.Equal(ActorId.None, d.Decider.Doing.Target);
        for (int k = 0; k < 20 && d.Decider.Doing.Target != Duel.Other; k++)
            d.StepTeams(self, [(Duel.Other, at, 3)]);
        Assert.Equal(Duel.Other, d.Decider.Doing.Target);
    }

    /// <summary>No grudge grows while friendly (a teammate's kill is friendly fire), and one held from before returns on a swap back.</summary>
    [Fact]
    public void AGrudgeSleepsWhileFriendlyAndWakesOnASwapBack()
    {
        BotMemory m = TeamMemoryTests.Memory();
        m.Step(new Tick(6), View(6, 2, (Foe, 3, 500f)), []);
        m.Apply(AiEvent.Killed(Bot, new Tick(8), Foe));
        float held = m.Board.Of(Foe, make: false)!.GrudgeAt(new Tick(8), 0.015f);
        m.Apply(AiEvent.ActorTeam(Bot, new Tick(9), Foe, 2, 3));
        m.Apply(AiEvent.Killed(Bot, new Tick(10), Foe));
        Assert.Equal(held, m.Board.Of(Foe, make: false)!.GrudgeAt(new Tick(8), 0.015f));
        m.Apply(AiEvent.ActorTeam(Bot, new Tick(11), Foe, 3, 2));
        Assert.True(m.Board.Of(Foe, make: false)!.GrudgeAt(new Tick(11), 0.015f) > 0f);
    }

    /// <summary>A bot moved to another team itself starts its beliefs over: every ammo estimate goes; who it knows stays.</summary>
    [Fact]
    public void ABotMovedItselfForgetsItsEstimates()
    {
        BotMemory m = TeamMemoryTests.Memory();
        m.Step(new Tick(6), View(6, 2, (Foe, 3, 500f)), [AiEvent.SightGained(Bot, new Tick(6), Foe, new AiVector(500, 0, 0), default, 1f)]);
        m.Apply(AiEvent.SeenFire(Bot, new Tick(7), Foe, 3, 5));
        Assert.True(m.Board.Of(Foe, make: false)!.Ammo.Known);
        m.Step(new Tick(12), View(12, 3), [AiEvent.ActorTeam(Bot, new Tick(10), Bot, 3, 2)]);
        Assert.False(m.Board.Of(Foe, make: false)!.Ammo.Known);
        Assert.Equal((new Tick(12), true), (m.OwnSwap, m.IsFriend(m.Board.Of(Foe, make: false)!)));
    }

    /// <summary>The control: joining a team from none (a new level, a first join) is no swap.</summary>
    [Fact]
    public void AFirstTeamIsNoSwap()
    {
        BotMemory m = TeamMemoryTests.Memory();
        m.Step(new Tick(6), View(6, 2), []);
        Assert.Equal(default, m.OwnSwap);
    }
}
