using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFPlayerObjectiveTimes"/>: CBaseMultiplayerPlayer's last-objective
/// time (server/player.h:845-846, the -1 sentinel from player.cpp:654) as the
/// capture area uses it, written per capper and blocker each think
/// (trigger_area_capture.cpp:369 and :382).
/// </summary>
public sealed class TFPlayerObjectiveTimesTests
{
    private static readonly EntityHandle Player = new(0x0001_0000u | 4);

    [Fact]
    public void A_player_who_never_touched_an_objective_reads_the_999_sentinel()
        => Assert.Equal(999f, new TFPlayerObjectiveTimes().GetTimeSinceLastObjective(4, 20f));

    [Fact]
    public void The_time_since_counts_from_the_time_it_was_written()
    {
        var times = new TFPlayerObjectiveTimes();

        times.SetLastObjectiveTime(Player, 13f);

        Assert.Equal(7f, times.GetTimeSinceLastObjective(4, 20f), 3);
    }

    [Fact]
    public void The_last_write_wins()
    {
        var times = new TFPlayerObjectiveTimes();

        times.SetLastObjectiveTime(Player, 5f);
        times.SetLastObjectiveTime(Player, 18f);

        Assert.Equal(2f, times.GetTimeSinceLastObjective(4, 20f), 3);
    }

    [Fact]
    public void A_departing_player_is_forgotten()
    {
        var times = new TFPlayerObjectiveTimes();
        times.SetLastObjectiveTime(Player, 18f);

        ((IClientLifecycle)times).OnClientDisconnected(4);

        Assert.Equal(999f, times.GetTimeSinceLastObjective(4, 20f));
    }

    [Fact]
    public void The_level_end_forgets_everyone()
    {
        var times = new TFPlayerObjectiveTimes();
        times.SetLastObjectiveTime(Player, 18f);

        ((IGameLifecycle)times).OnLevelShutdown();

        Assert.Equal(999f, times.GetTimeSinceLastObjective(4, 20f));
    }
}
