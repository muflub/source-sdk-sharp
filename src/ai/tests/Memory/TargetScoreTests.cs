using Xunit;

namespace SourceSharp.Ai.Tests.Memory;

/// <summary>
/// What an opponent is worth as a target (plan_hl2mp_ai §5.4, §5.5; plan_bots_teams_tf2_part_g
/// G.2.1): nearer is worth more; the one the bot is already about is not dropped for one a little
/// nearer; a grudge adds Tenacity's weight, a rank Headhunter's bonus, ranked on a team among the
/// enemy rows only; one out of sight counts by how sure the bot is; and someone close in plain
/// sight comes before all of it, the nearest first.
/// </summary>
public sealed class TargetScoreTests
{
    private const float Dt = 0.015f;
    private static readonly ActorId Bot = new(3, 1);

    private static float Plain(float distance, bool current = false, bool sighted = true, float confidence = 1f, float bonus = 0f, float grudge = 0f)
        => TargetScore.Priority(sighted, confidence, distance, 1f, bonus, grudge, current);

    [Fact]
    public void NearerIsWorthMoreAndHalfAtTheHalfDistance()
    {
        Assert.True(Plain(300f) > Plain(600f));
        Assert.Equal((1f, 0.5f), (Plain(0f), Plain(768f)));
    }

    [Fact]
    public void TheOneTheBotIsAboutIsNotDroppedForOneALittleNearer()
        => Assert.True(Plain(600f, current: true) > Plain(560f), $"{Plain(600f, current: true)} against {Plain(560f)}");

    /// <summary>The control for the fact above: the hold is a share, and someone much nearer does take over.</summary>
    [Fact]
    public void TheOneTheBotIsAboutIsDroppedForOneMuchNearer()
        => Assert.True(Plain(600f, current: true) < Plain(200f));

    [Fact]
    public void WithoutTheHoldTheNearerOneWins()
        => Assert.True(Plain(600f) < Plain(560f));

    [Fact]
    public void ABonusAndAGrudgeAddToNearness()
        => Assert.Equal(Plain(768f) + 0.3f + 0.2f, Plain(768f, bonus: 0.3f, grudge: 0.2f), 5);

    [Fact]
    public void HeadhuntersDiscountTakesFromNearnessOnly()
        => Assert.Equal(0.5f * 0.4f + 0.3f, TargetScore.Priority(true, 1f, 768f, 0.4f, 0.3f, 0f, false), 5);

    [Fact]
    public void OneOutOfSightCountsByHalfHowSureTheBotIs()
        => Assert.Equal(0.5f * 0.6f * Plain(400f), Plain(400f, sighted: false, confidence: 0.6f), 5);

    [Theory]
    [InlineData(1, 4, 1f)]
    [InlineData(4, 4, 0f)]
    [InlineData(2, 3, 0.5f)]
    [InlineData(0, 4, 0f)]
    [InlineData(1, 1, 1f)]
    [InlineData(1, 0, 1f)]
    public void TheRankBonusFallsLinearlyFromTheLeader(int rank, int rows, float bonus)
        => Assert.Equal(bonus, TargetScore.RankBonus(rank, rows));

    private static BotMemory Memory(int ownTeam, params (int Slot, int Team, int Rank)[] board)
    {
        var m = new BotMemory(Bot, 8, new MemoryParams(4f, 120f), MemoryWorld.Empty(Dt), new HabitStore());
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(1), new SelfState(default, default, default, 100, 0, -1, 0, 0, SelfFlags.Alive, Team: ownTeam));
        var events = new List<AiEvent>();
        foreach ((int slot, int team, int rank) in board)
        {
            var id = new ActorId(slot, 1);
            events.Add(AiEvent.ActorTeam(Bot, new Tick(1), id, team, 0));
            events.Add(AiEvent.ScoreboardRow(Bot, new Tick(1), id, 10 - rank, 0, rank));
        }

        m.Step(new Tick(1), v, [.. events]);
        return m;
    }

    [Fact]
    public void WithNoTeamsTheRowsAreTheBoardsAndARankIsItsOwn()
    {
        BotMemory m = Memory(0, (1, 0, 1), (2, 0, 3), (4, 0, 4));
        Assert.Equal(4, TargetScore.Rows(m));
        Assert.Equal(3, TargetScore.RankOf(m, m.Board.Actor(2)));
    }

    /// <summary>On a team the leader of the bot's own side is no row: the enemies are ranked among themselves.</summary>
    [Fact]
    public void OnATeamOnlyTheEnemyRowsAreRanked()
    {
        BotMemory m = Memory(2, (1, 2, 1), (2, 3, 2), (4, 3, 4), (5, 2, 3));
        Assert.Equal(2, TargetScore.Rows(m));
        Assert.Equal((1, 2), (TargetScore.RankOf(m, m.Board.Actor(2)), TargetScore.RankOf(m, m.Board.Actor(4))));
    }

    [Fact]
    public void AnEnemyWithNoRowHasNoRank()
    {
        BotMemory m = Memory(2, (2, 3, 0), (4, 3, 1));
        Assert.Equal((1, 0), (TargetScore.Rows(m), TargetScore.RankOf(m, m.Board.Actor(2))));
    }

    [Fact]
    public void AGrudgeIsWorthTenacitysWeightAtTwoKillsAndHalfAtOne()
    {
        BotMemory m = Memory(0, (2, 0, 1), (4, 0, 2));
        var killer = new ActorId(2, 1);
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(2), new SelfState(default, default, default, 0, 0, -1, 0, 0, SelfFlags.None));
        m.Step(new Tick(2), v, [AiEvent.Killed(Bot, new Tick(2), killer)]);
        Assert.Equal(0.2f, TargetScore.Grudge(m.Board.Actor(2), new Tick(2), Dt, 0.4f), 4);
        Assert.Equal(0f, TargetScore.Grudge(m.Board.Actor(4), new Tick(2), Dt, 0.4f));

        v.Reset(Bot, new Tick(3), new SelfState(default, default, default, 0, 0, -1, 0, 0, SelfFlags.None));
        m.Step(new Tick(3), v, [AiEvent.Killed(Bot, new Tick(3), killer), AiEvent.Killed(Bot, new Tick(3), killer)]);
        Assert.Equal(0.4f, TargetScore.Grudge(m.Board.Actor(2), new Tick(3), Dt, 0.4f), 4);
    }

    /// <summary>Against the one its HUD marks as its nemesis (plan_bots_teams_tf2_part_g G.2.5) the grudge counts <see cref="TargetScore.NemesisGrudge"/> times, and as before once the mark is off.</summary>
    [Fact]
    public void TheGrudgeAgainstANemesisIsMultipliedWhileTheHudMarksIt()
    {
        BotMemory m = Memory(0, (2, 0, 1), (4, 0, 2));
        var killer = new ActorId(2, 1);
        m.Apply(AiEvent.Killed(Bot, new Tick(2), killer));
        m.Apply(AiEvent.Killed(Bot, new Tick(2), killer));
        float plain = TargetScore.Grudge(m.Board.Actor(2), new Tick(2), Dt, 0.4f);
        m.Apply(AiEvent.Nemesis(Bot, new Tick(2), killer, true));
        float marked = TargetScore.Grudge(m.Board.Actor(2), new Tick(2), Dt, 0.4f);
        m.Apply(AiEvent.Nemesis(Bot, new Tick(2), killer, false));
        Assert.Equal((0.4f, 0.4f * TargetScore.NemesisGrudge, 0.4f), (plain, marked, TargetScore.Grudge(m.Board.Actor(2), new Tick(2), Dt, 0.4f)));
    }

    /// <summary>The mark multiplies a grudge: with none held (a mark on someone who never killed the bot) there is nothing to multiply.</summary>
    [Fact]
    public void ANemesisTheBotHoldsNoGrudgeAgainstIsWorthNothingMore()
    {
        BotMemory m = Memory(0, (2, 0, 1));
        m.Apply(AiEvent.Nemesis(Bot, new Tick(2), new ActorId(2, 1), true));
        Assert.Equal(0f, TargetScore.Grudge(m.Board.Actor(2), new Tick(2), Dt, 0.4f));
    }

    [Fact]
    public void ThePickIsTheBestPriority()
    {
        var pick = new TargetPick(400f);
        Assert.True(pick.Takes(true, 900f, 0.4f));
        Assert.False(pick.Takes(true, 800f, 0.3f));
        Assert.True(pick.Takes(false, 700f, 0.5f));
    }

    [Fact]
    public void SomeoneCloseInPlainSightOutranksABetterPriorityAndTheNearestOfThemIsFirst()
    {
        var pick = new TargetPick(400f);
        Assert.True(pick.Takes(true, 900f, 0.9f));
        Assert.True(pick.Takes(true, 350f, 0.1f), "close, whatever its priority");
        Assert.False(pick.Takes(true, 900f, 2f), "nobody further takes over");
        Assert.False(pick.Takes(true, 380f, 2f), "nor someone close but further");
        Assert.True(pick.Takes(true, 200f, 0.05f), "the nearest of the close ones");
    }

    [Fact]
    public void SomeoneCloseButOutOfSightIsRankedByPriorityLikeAnyone()
    {
        var pick = new TargetPick(400f);
        Assert.True(pick.Takes(true, 900f, 0.9f));
        Assert.False(pick.Takes(false, 100f, 0.2f));
    }
}
