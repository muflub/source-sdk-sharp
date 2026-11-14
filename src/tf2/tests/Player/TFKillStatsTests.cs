using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// A death on TF's scoreboard (<see cref="TFKillStats"/>): the victim's
/// death, the attacker's kill and its point, a suicide that is no kill, and
/// nothing for a killer that is not a player.
/// </summary>
public sealed class TFKillStatsTests
{
    private static readonly EntityHandle Victim = new(0x0001_0002u);
    private static readonly EntityHandle Killer = new(0x0001_0003u);
    private static readonly EntityHandle Prop = new(0x0004_0123u);

    private static (TFPlayerStats Stats, TFKillStats Hook) Rig()
    {
        var stats = new TFPlayerStats();
        var engine = RecordingEngine.Default();
        engine.ReturnsEachCall("Players.GetHandle", () => (int)engine.Calls[^1].Args[0]! switch
        {
            2 => Victim,
            3 => Killer,
            _ => EntityHandle.None,
        });
        return (stats, new TFKillStats(stats, engine));
    }

    private static PlayerDeath Death(EntityHandle attacker) => new(Victim, Victim.EntryIndex, attacker, default, EntityHandle.None);

    [Fact]
    public void A_kill_is_the_killers_point_and_the_victims_death()
    {
        var (stats, hook) = Rig();

        hook.OnPlayerDied(Death(Killer));

        Assert.Equal(1, stats.Accumulated(Killer.EntryIndex, TFStat.Kills));
        Assert.Equal(TFPlayerStats.ScoreKill, stats.Score(Killer.EntryIndex));
        Assert.Equal(1, stats.Accumulated(Victim.EntryIndex, TFStat.Deaths));
        Assert.Equal(0, stats.Accumulated(Victim.EntryIndex, TFStat.Kills));
    }

    [Fact]
    public void A_suicide_is_a_suicide_and_a_death_and_no_kill()
    {
        var (stats, hook) = Rig();

        hook.OnPlayerDied(Death(Victim));

        Assert.Equal(1, stats.Accumulated(Victim.EntryIndex, TFStat.Suicides));
        Assert.Equal(1, stats.Accumulated(Victim.EntryIndex, TFStat.Deaths));
        Assert.Equal(0, stats.Accumulated(Victim.EntryIndex, TFStat.Kills));
        Assert.Equal(0, stats.Score(Victim.EntryIndex));
    }

    [Fact]
    public void A_killer_that_is_no_player_books_nothing_but_the_death()
    {
        var (stats, hook) = Rig();

        hook.OnPlayerDied(Death(Prop));
        hook.OnPlayerDied(Death(EntityHandle.None));

        Assert.Equal(0, stats.Accumulated(Prop.EntryIndex, TFStat.Kills));
        Assert.Equal(2, stats.Accumulated(Victim.EntryIndex, TFStat.Deaths));
    }
}
