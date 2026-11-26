using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Commands;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// <see cref="TFStatsCommand"/> (<c>tf2_stats [player]</c>): one line per
/// rostered player of the stats service's round/accumulated scored stats and
/// score.
/// </summary>
public sealed class TFStatsCommandTests
{
    private static readonly EntityHandle Red = new(0x0001_0000u | 2);
    private static readonly EntityHandle Blue = new(0x0001_0000u | 3);

    private readonly RecordingLog _log = new();
    private readonly TFPlayerStats _stats = new();

    private sealed class TwoTeams : ITeamRoundRoster
    {
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => team switch
        {
            TFTeam.Red => [Red],
            TFTeam.Blue => [Blue],
            _ => [],
        };

        public TFTeam TeamOf(EntityHandle player) => player == Red ? TFTeam.Red : TFTeam.Blue;
        public bool IsAlive(EntityHandle player) => true;
        public float DeathTime(EntityHandle player) => -1f;
        public bool IsReadyToPlay(EntityHandle player) => true;
        public void ForceRespawn(EntityHandle player) { }
        public bool ChangeTeam(EntityHandle player, TFTeam team) => true;
        public string Name(EntityHandle player) => player == Red ? "Soldier1" : "Soldier2";
        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    private TFStatsCommand Command() => new(_log, _stats, new TwoTeams());

    [Fact]
    public void The_bare_line_prints_each_rostered_players_stats_and_score()
    {
        _stats.Increment(Red, TFStat.Captures, 2);
        _stats.Increment(Red, TFStat.Kills, 1);

        Command().RunLine("");

        var lines = _log.Lines.Where(l => !l.Warning).Select(l => l.Text).ToList();
        Assert.Equal(
        [
            "[TF2/Stats] 2 Soldier1 team 2 captures 2/2 defenses 0/0 flagreturns 0/0 kills 1/1 score 5/5\n",
            "[TF2/Stats] 3 Soldier2 team 3 captures 0/0 defenses 0/0 flagreturns 0/0 kills 0/0 score 0/0\n",
        ], lines);
    }

    [Fact]
    public void A_named_index_prints_that_player_only()
    {
        _stats.Increment(Blue, TFStat.Flagreturns, 1);

        Command().RunLine("3");

        Assert.Equal("[TF2/Stats] 3 Soldier2 team 3 captures 0/0 defenses 0/0 flagreturns 1/1 kills 0/0 score 4/4\n",
                     _log.Lines.Single(l => !l.Warning).Text);
    }

    [Fact]
    public void After_a_new_round_the_line_keeps_the_round_and_accumulated_values_apart()
    {
        _stats.Increment(Red, TFStat.Captures, 3);
        _stats.ResetRoundStats();
        _stats.Increment(Red, TFStat.Captures, 1);

        Command().RunLine("2");

        Assert.Equal("[TF2/Stats] 2 Soldier1 team 2 captures 1/4 defenses 0/0 flagreturns 0/0 kills 0/0 score 2/8\n",
                     _log.Lines.Single(l => !l.Warning).Text);
    }
}
