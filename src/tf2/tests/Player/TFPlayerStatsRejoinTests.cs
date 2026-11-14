using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// TF2's totals through the core's rejoin seam (plan_bots_teams_tf2 A.6):
/// <see cref="TFPlayerStats"/> answers <see cref="IRejoinScoreHook"/>, so a
/// player who leaves and rejoins the same match gets their score and stats
/// back, in whatever slot they come back to.
/// </summary>
public sealed class TFPlayerStatsRejoinTests
{
    private static readonly EntityHandle Left = new(0x0001_0003u);
    private static readonly EntityHandle Back = new(0x0002_0007u);

    [Fact]
    public void A_rejoining_player_gets_their_score_and_stats_back()
    {
        var stats = new TFPlayerStats();
        stats.Increment(Left, TFStat.Kills, 4);
        stats.Increment(Left, TFStat.Captures, 1);
        int score = stats.Score(Left.EntryIndex);

        object? kept = ((IRejoinScoreHook)stats).Capture(Left);
        ((IClientLifecycle)stats).OnClientDisconnected(Left.EntryIndex);
        Assert.Equal(0, stats.Score(Left.EntryIndex));

        ((IRejoinScoreHook)stats).Restore(Back, kept!);

        Assert.Equal(6, score);
        Assert.Equal(score, stats.Score(Back.EntryIndex));
        Assert.Equal(4, stats.Accumulated(Back.EntryIndex, TFStat.Kills));
    }

    [Fact]
    public void A_player_with_no_stats_keeps_nothing()
    {
        Assert.Null(((IRejoinScoreHook)new TFPlayerStats()).Capture(Left));
    }
}
