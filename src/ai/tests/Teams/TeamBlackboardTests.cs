using Xunit;
using static SourceSharp.Ai.Tests.Teams.TeamMemoryTests;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// The blackboard's split (plan_bots_teams_tf2 B.3): an enemy's memory is built, a
/// teammate's never is. No ammo estimate, habits or grudge for a friend; a teammate's
/// hit is a <c>friendly_fire</c> moment. Each fact's control is the same input from a foe.
/// </summary>
public sealed class TeamBlackboardTests
{
    /// <summary>The bot on team 2 with <paramref name="who"/> in sight on <paramref name="team"/>.</summary>
    private static BotMemory Seeing(ActorId who, int team)
    {
        BotMemory m = TeamMemoryTests.Memory();
        m.Step(new Tick(6), View(6, 2, (who, team, 500f)), [AiEvent.SightGained(Bot, new Tick(6), who, new AiVector(500, 0, 0), default, 1f)]);
        return m;
    }

    [Fact]
    public void ATeammateSeenFiringBuildsNoAmmoEstimate()
    {
        BotMemory m = Seeing(Mate, 2);
        m.Apply(AiEvent.SeenFire(Bot, new Tick(7), Mate, 3, 5));
        Assert.False(m.Board.Of(Mate, make: false)!.Ammo.Known);
    }

    [Fact]
    public void AnEnemySeenFiringBuildsOne()
    {
        BotMemory m = Seeing(Foe, 3);
        m.Apply(AiEvent.SeenFire(Bot, new Tick(7), Foe, 3, 5));
        Assert.True(m.Board.Of(Foe, make: false)!.Ammo.Known);
    }

    [Fact]
    public void ATeammatesReloadKeepsNoHabit()
    {
        BotMemory m = Seeing(Mate, 2);
        m.Apply(AiEvent.SeenReload(Bot, new Tick(7), Mate, ReloadPhase.Start, 3));
        Assert.False(m.TryHabits(Mate, out _));
    }

    [Fact]
    public void AnEnemysReloadIsAHabit()
    {
        BotMemory m = Seeing(Foe, 3);
        m.Apply(AiEvent.SeenReload(Bot, new Tick(7), Foe, ReloadPhase.Start, 3));
        Assert.True(m.TryHabits(Foe, out _));
    }

    [Fact]
    public void ATeammatesKillHoldsNoGrudgeButIsFriendlyFire()
    {
        BotMemory m = Seeing(Mate, 2);
        m.Apply(AiEvent.Killed(Bot, new Tick(9), Mate));
        Assert.Equal(0f, m.Board.Of(Mate, make: false)!.GrudgeAt(new Tick(9), 0.015f));
        Assert.Equal((Mate, new Tick(9)), m.LastFriendlyFire);
    }

    [Fact]
    public void AnEnemysKillHoldsAGrudge()
    {
        BotMemory m = Seeing(Foe, 3);
        m.Apply(AiEvent.Killed(Bot, new Tick(9), Foe));
        Assert.True(m.Board.Of(Foe, make: false)!.GrudgeAt(new Tick(9), 0.015f) > 0f);
        Assert.Equal(0, m.FriendlyFireTaken);
    }

    [Fact]
    public void ATeammatesHitIsFriendlyFireNotThreat()
    {
        BotMemory m = Seeing(Mate, 2);
        m.Apply(AiEvent.Damaged(Bot, new Tick(8), Mate, 12f, new AiVector(1, 0, 0), 0));
        Assert.Equal((1, 0f), (m.FriendlyFireTaken, m.Board.Of(Mate, make: false)!.DamageTakenFrom));
    }

    /// <summary>A swap mid-fight (B.9): hits taken before the <c>player_team</c> event stay a foe's, never friendly fire.</summary>
    [Fact]
    public void ASwappersEarlierHitsAreNotFriendlyFire()
    {
        BotMemory m = Seeing(Foe, 3);
        m.Step(new Tick(12), View(12, 2), [AiEvent.Damaged(Bot, new Tick(8), Foe, 12f, new AiVector(1, 0, 0), 0),
                                           AiEvent.ActorTeam(Bot, new Tick(10), Foe, 2, 3)]);
        Assert.Equal((0, 12f), (m.FriendlyFireTaken, m.Board.Of(Foe, make: false)!.DamageTakenFrom));
        Assert.True(m.IsFriend(m.Board.Of(Foe, make: false)!));
    }

    /// <summary>A teammate's footsteps are recognised, nothing to look into; a foe's are.</summary>
    [Fact]
    public void ATeammatesFootstepsAreNotInvestigated()
    {
        BotMemory mate = Seeing(Mate, 2), foe = Seeing(Foe, 3);
        mate.Step(new Tick(12), View(12, 2), [AiEvent.SightLost(Bot, new Tick(12), Mate, new AiVector(500, 0, 0), default),
                                              AiEvent.SoundHeard(Bot, new Tick(12), SoundKind.Footstep, new AiVector(600, 0, 0), 0.5f, Mate, -1)]);
        foe.Step(new Tick(12), View(12, 2), [AiEvent.SightLost(Bot, new Tick(12), Foe, new AiVector(500, 0, 0), default),
                                             AiEvent.SoundHeard(Bot, new Tick(12), SoundKind.Footstep, new AiVector(600, 0, 0), 0.5f, Foe, -1)]);
        Assert.Equal((0, 1), (mate.Board.InvestigationCount, foe.Board.InvestigationCount));
    }
}
