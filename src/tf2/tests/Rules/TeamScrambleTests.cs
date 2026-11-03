using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The team scramble at the round's start: <c>CTFGameRules::HandleScrambleTeams</c>
/// (tf_gamerules.cpp:16122-16211) behind the <c>ShouldScrambleTeams</c> flag, as
/// <c>RoundRespawn</c> does it (teamplayroundbased_gamerules.cpp:2853-2858): the
/// collect-and-sort, the drop to Spectator, the auto-assign redraw, the score
/// swap pass, and the flag spending itself once.
///
/// <para>
/// The facts run with <see cref="FakeMode.EvenTeamsJoinAttackers"/> on, so
/// stock's coin arm (<see cref="TeamRoundFlow"/>'s <c>AutoAssignTeam</c>) is
/// decided by the attack/defend rule and every redraw is deterministic.
/// </para>
/// </summary>
public sealed class TeamScrambleTests
{
    /// <summary>The player resource's score rows, read by the sort and the swap pass.</summary>
    private sealed class FakeStats : ITFPlayerStats
    {
        public readonly Dictionary<int, int> Scores = new();

        public int Score(int playerIndex) => Scores.TryGetValue(playerIndex, out int score) ? score : 0;

        public void Increment(EntityHandle player, TFStat stat, int amount = 1) => throw new NotSupportedException();
        public int Round(int playerIndex, TFStat stat) => throw new NotSupportedException();
        public int Accumulated(int playerIndex, TFStat stat) => throw new NotSupportedException();
        public int RoundScore(int playerIndex) => throw new NotSupportedException();
        public void ResetPlayer(EntityHandle player) => Scores.Remove(player.EntryIndex);
    }

    [Fact]
    public void A_scramble_drops_everyone_to_spectator_and_redraws_the_teams()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        rig.Mode.EvenTeamsJoinAttackers = true;

        rig.Flow.SetScrambleTeams(true);
        rig.Flow.ShouldResetScores(true, false);   // as mp_scrambleteams does (:307): the restart keeps the player scores the sort and swap read
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));   // the countdown fires
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));   // and brings the next round

        // The clear goes back to front (:16134-16148), so blue drops first; then
        // the front-to-back redraw (:16151-16160): with empty teams the first takes
        // the attackers (Blue), the second the other. Everyone crosses.
        Assert.Equal(new (EntityHandle, TFTeam)[]
        {
            (blue, TFTeam.Spectator), (red, TFTeam.Spectator),
            (red, TFTeam.Blue), (blue, TFTeam.Red),
        }, rig.Roster.Moves);
    }

    [Fact]
    public void The_scramble_sorts_the_redraw_by_player_score()
    {
        // The auto-assign fills the team with fewer players first, so the sort
        // (ScramblePlayersSort, tf_gamerules.cpp:14133-14150) decides who gets
        // the attackers' berth: the top scorer. blue's player has the higher
        // score, so it is redrawn first despite joining second — and keeps the
        // attackers' side; without the sort the collection order would draw
        // red first and everyone would cross instead.
        var stats = new FakeStats();
        var rig = new RoundRig(stats: stats);
        var (red, blue) = rig.Running();
        rig.Mode.EvenTeamsJoinAttackers = true;
        stats.Scores[rig.Roster.EntIndex(red)] = 1;
        stats.Scores[rig.Roster.EntIndex(blue)] = 100;

        rig.Flow.SetScrambleTeams(true);
        rig.Flow.ShouldResetScores(true, false);   // the command's shape (teamplayroundbased_gamerules.cpp:307)
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        // Sorted [blue, red]: cleared back-to-front (red then blue), redrawn
        // front-to-back — blue first onto the attackers, red second.
        Assert.Equal(new (EntityHandle, TFTeam)[]
        {
            (red, TFTeam.Spectator), (blue, TFTeam.Spectator),
            (blue, TFTeam.Blue), (red, TFTeam.Red),
        }, rig.Roster.Moves);
    }

    [Fact]
    public void The_scramble_swaps_a_pair_to_balance_the_scores()
    {
        // Four players, scores 100/90/10/0. The count-based redraw hands the
        // attackers (Blue) the 100 and the 10 (sum 110) and Red the 90 and the 0
        // (sum 90), a delta of 20. The swap pass (:16182-16205) then trades the
        // 90 for the 100, bringing both teams to 100.
        var stats = new FakeStats();
        var rig = new RoundRig(stats: stats);
        var hundred = rig.Roster.Add(TFTeam.Red);
        var ninety = rig.Roster.Add(TFTeam.Blue);
        var ten = rig.Roster.Add(TFTeam.Red);
        var zero = rig.Roster.Add(TFTeam.Blue);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        rig.Mode.EvenTeamsJoinAttackers = true;

        stats.Scores[rig.Roster.EntIndex(hundred)] = 100;
        stats.Scores[rig.Roster.EntIndex(ninety)] = 90;
        stats.Scores[rig.Roster.EntIndex(ten)] = 10;
        stats.Scores[rig.Roster.EntIndex(zero)] = 0;

        rig.Flow.SetScrambleTeams(true);
        rig.Flow.ShouldResetScores(true, false);   // the command's shape (teamplayroundbased_gamerules.cpp:307)
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        // The two teams are now score-equal: the 100 sits on Red with the 0,
        // the 90 on Blue with the 10 — the greedy pass alone would have put the
        // 100 and the 10 together.
        int TeamScore(TFTeam team) => rig.Roster.Players(team).Sum(p => stats.Score(rig.Roster.EntIndex(p)));
        Assert.Equal(100, TeamScore(TFTeam.Red));
        Assert.Equal(100, TeamScore(TFTeam.Blue));
    }

    [Fact]
    public void No_swap_pass_runs_without_the_player_scores()
    {
        // Stock skips the swap block with no player resource
        // (tf_gamerules.cpp:16165-16166). Three players whose scores would
        // balance — the greedy pass gives Blue 100+40 and Red 60, and the swap
        // would trade the 60 for the 100 to bring both to 100 — keep the
        // greedy split when the machine has no score table.
        //
        // (Stock's other gate, three players or fewer at :16163, needs no
        // fact: a two-player swap only trades the two single-player sums, so
        // its delta equals the old one and the strict-improvement test at
        // :16092 already refuses it.)
        var stats = new FakeStats();
        var rig = new RoundRig();
        var top = rig.Roster.Add(TFTeam.Red);
        var middle = rig.Roster.Add(TFTeam.Red);
        var low = rig.Roster.Add(TFTeam.Blue);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        rig.Mode.EvenTeamsJoinAttackers = true;
        stats.Scores[rig.Roster.EntIndex(top)] = 100;
        stats.Scores[rig.Roster.EntIndex(middle)] = 60;
        stats.Scores[rig.Roster.EntIndex(low)] = 40;

        rig.Flow.SetScrambleTeams(true);
        rig.Flow.ShouldResetScores(true, false);   // the command's shape (teamplayroundbased_gamerules.cpp:307)
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        // The plain count-based redraw (top and low to the attackers, middle
        // to the defenders), and no swap moves afterward.
        Assert.Equal(6, rig.Roster.Moves.Count);
        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(top));
        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(middle));
        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(low));
    }

    [Fact]
    public void On_a_map_that_is_not_attack_and_defend_the_redraw_tosses_a_coin()
    {
        // With the teams even and no attack/defend answer, the auto-assign
        // tosses a coin (CTFPlayer::GetAutoTeam's last arm, the one the
        // scramble reaches through ForceChangeTeam(TF_TEAM_AUTOASSIGN)):
        // across seeds the first pick lands on both sides — the same coin
        // sweep TFTeamsTests takes, here through the scramble's redraw.
        var firstPicks = new HashSet<TFTeam>();
        for (var seed = 1234; seed < 1250; seed++)
        {
            var rig = new RoundRig(seed: seed);
            rig.Running();
            rig.Mode.EvenTeamsJoinAttackers = false;

            rig.Flow.SetScrambleTeams(true);
            rig.Flow.ShouldResetScores(true, false);   // the command's shape (teamplayroundbased_gamerules.cpp:307)
            rig.Settings.RestartGame = 1;
            Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));
            Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

            // The redraw keeps the count balanced: whoever the coin sends the
            // first player to, the second takes the other (the fewer-players arm).
            int red = rig.Roster.Players(TFTeam.Red).Count;
            int blue = rig.Roster.Players(TFTeam.Blue).Count;
            Assert.Equal(1, red);
            Assert.Equal(1, blue);
            firstPicks.Add(rig.Roster.Moves[^2].Team);   // the first of the two redraw moves
        }

        Assert.Equal(new[] { TFTeam.Red, TFTeam.Blue }, firstPicks.OrderBy(t => (int)t).ToArray());
    }

    [Fact]
    public void The_scramble_spends_its_flag_and_comes_back_no_more()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        rig.Mode.EvenTeamsJoinAttackers = true;

        rig.Flow.SetScrambleTeams(true);
        rig.Flow.ShouldResetScores(true, false);   // the command's shape (teamplayroundbased_gamerules.cpp:307)
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        Assert.False(rig.Flow.ShouldScrambleTeams());
        int moves = rig.Roster.Moves.Count;

        // A second round won without re-arming the flag scrambles no one again.
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.OpponentsDead);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(moves, rig.Roster.Moves.Count);
    }

    [Fact]
    public void An_unscrambled_round_moves_nobody()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Empty(rig.Roster.Moves);
        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(red));
        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(blue));
    }
}
