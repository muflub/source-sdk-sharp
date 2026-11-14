using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// Deciding with teammates (plan_bots_teams_tf2 B.5): a friend is never a target, a
/// sighted friend nearer on the line holds fire, and under friendly fire a friend in an
/// explosive's blast does too. Each control is the same scene with the friend out of it.
/// </summary>
public sealed class TeamFireTests
{
    private static readonly AiVector Far = new(600f, 0f, 0f);

    private static SelfState OnTeam(Duel d, int team = 2, int weapon = Duel.Smg1, int clip = 30)
        => d.Self(weapon: weapon, clip: clip) with { Team = team };

    /// <summary>Two steps with the scene in sight: the first sees it, the second decides on it.</summary>
    private static IntentCommand Twice(Duel d, SelfState self, (ActorId, AiVector, int)[] seen, params AiEvent[] first)
    {
        d.StepTeams(self, seen, first);
        return d.StepTeams(self, seen);
    }

    [Fact]
    public void ATeammateInSightIsNeverTheTarget()
    {
        var d = new Duel(Duel.Preset("hard"));
        Twice(d, OnTeam(d), [(Duel.Other, new AiVector(150, 0, 0), 2)], d.SeeAt(Duel.Other, new AiVector(150, 0, 0)));
        Assert.Equal(ActorId.None, d.Decider.Doing.Target);
    }

    [Fact]
    public void AnEnemyInSightIsTheTarget()
    {
        var d = new Duel(Duel.Preset("hard"));
        Twice(d, OnTeam(d), [(Duel.Opponent, new AiVector(150, 0, 0), 3)], d.SeeAt(Duel.Opponent, new AiVector(150, 0, 0)));
        Assert.Equal(Duel.Opponent, d.Decider.Doing.Target);
    }

    [Fact]
    public void ATeammateOnTheLineHoldsFire()
    {
        var d = new Duel(Duel.Preset("hard"));
        IntentCommand i = Twice(d, OnTeam(d), [(Duel.Opponent, Far, 3), (Duel.Other, new AiVector(300, 0, 0), 2)], d.SeeAt(Duel.Opponent, Far));
        Assert.Equal(Duel.Opponent, d.Decider.Doing.Target);
        Assert.Equal(ActionKind.None, i.Action.Kind);
        Assert.True(d.Decider.HeldFireFriend > 0);
    }

    [Fact]
    public void ATeammateOffTheLineLetsItFire()
    {
        var d = new Duel(Duel.Preset("hard"));
        IntentCommand i = Twice(d, OnTeam(d), [(Duel.Opponent, Far, 3), (Duel.Other, new AiVector(300, 300, 0), 2)], d.SeeAt(Duel.Opponent, Far));
        Assert.Equal((ActionKind.Fire, 0), (i.Action.Kind, d.Decider.HeldFireFriend));
    }

    /// <summary>A teammate behind the target is not in the way: only a nearer one holds fire.</summary>
    [Fact]
    public void ATeammateBeyondTheTargetLetsItFire()
    {
        var d = new Duel(Duel.Preset("hard"));
        IntentCommand i = Twice(d, OnTeam(d), [(Duel.Opponent, Far, 3), (Duel.Other, new AiVector(900, 0, 0), 2)], d.SeeAt(Duel.Opponent, Far));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>In a deathmatch everyone is fair game: the same scene on team 0 fires through the line.</summary>
    [Fact]
    public void WithNoTeamsNobodyHoldsFire()
    {
        var d = new Duel(Duel.Preset("hard"));
        IntentCommand i = Twice(d, OnTeam(d, team: 0), [(Duel.Opponent, Far, 0), (Duel.Other, new AiVector(300, 0, 0), 0)], d.SeeAt(Duel.Opponent, Far));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>Under friendly fire a frag is not thrown at an enemy standing by a teammate (B.5's splash arm).</summary>
    [Fact]
    public void UnderFriendlyFireAFragWaitsForATeammateInItsBlast()
    {
        var d = new Duel(Duel.Preset("hard"));
        IntentCommand i = Twice(d, OnTeam(d, weapon: Duel.Frag, clip: -1), [(Duel.Opponent, Far, 3), (Duel.Other, Far + new AiVector(0, 120, 0), 2)],
                                [.. d.SeeAt(Duel.Opponent, Far), AiEvent.FriendlyFireSet(Duel.Bot, new Tick(d.Tick + 6), true)]);
        Assert.Equal(ActionKind.None, i.Action.Kind);
        Assert.True(d.Decider.HeldFireFriend > 0);
    }

    /// <summary>The control: friendly fire off, the same throw goes.</summary>
    [Fact]
    public void WithFriendlyFireOffTheFragGoes()
    {
        var d = new Duel(Duel.Preset("hard"));
        IntentCommand i = Twice(d, OnTeam(d, weapon: Duel.Frag, clip: -1), [(Duel.Opponent, Far, 3), (Duel.Other, Far + new AiVector(0, 120, 0), 2)],
                                [.. d.SeeAt(Duel.Opponent, Far), AiEvent.FriendlyFireSet(Duel.Bot, new Tick(d.Tick + 6), false)]);
        Assert.Equal(0, d.Decider.HeldFireFriend);
        Assert.NotEqual(ActionKind.None, i.Action.Kind);
    }

    /// <summary>Headhunter on a team (B.5): the best enemy row gets the whole rank term though a teammate leads the board.</summary>
    [Fact]
    public void HeadhunterRanksTheEnemyRowsOnly()
    {
        SkillProfile hunter = Duel.Preset("hard");
        var d = new Duel(hunter.With(hunter.IndexOf("headhunter"), 10));
        Assert.True(d.Params.RankBonus > 0f, "the stimulus reaches the subject: Headhunter weighs the rank");
        Twice(d, OnTeam(d), [(Duel.Opponent, Far, 3)],
              [.. d.SeeAt(Duel.Opponent, Far), AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Duel.Other, 2, 0),
               d.Row(Duel.Other, 20, 1), d.Row(Duel.Opponent, 10, 2)]);
        Assert.Equal(Duel.Opponent, d.Decider.Doing.Target);
        Assert.Equal(d.Params.RankBonus, d.Decider.Doing.ScoreboardTerm, 3);
    }
}
