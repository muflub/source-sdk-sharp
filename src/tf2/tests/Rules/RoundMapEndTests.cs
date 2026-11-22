using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// What a multi-stage map does at a stage win and at the map's end (stock
/// CTeamplayRoundBasedRules::State_Enter_TEAM_WIN / State_Think_TEAM_WIN and
/// CheckNextLevelCvar, teamplayroundbased_gamerules.cpp:1851-2003, 1246-1268):
/// a mini-round win yields to the next stage without counting a full round,
/// and a full-reset win with nextlevel set ends the game once the bonus time
/// runs out.
/// </summary>
public sealed class RoundMapEndTests
{
    /// <summary>
    /// Stock's State_Enter_TEAM_WIN counts a round only when the map is forced
    /// to reset (the mini-round arm, :1853-1857); a win with rounds still to
    /// play starts the next stage through RESTART into the PREROUND that loads
    /// its points and respawns everyone into its rooms, and keeps the winner's
    /// point through the restart (the PreviousRoundEnd arm, :1899-1918).
    /// </summary>
    [Fact]
    public void A_mini_round_win_starts_the_next_stage_without_counting_a_full_round()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured, forceMapReset: false);
        float won = rig.Clock.Now;
        int resets = rig.World.Count("reset");
        int spawns = rig.World.Count("spawn");
        int actives = rig.World.Count("activate");
        int caps = rig.World.Count("cp_spawns");
        int respawns = rig.Roster.Respawns.Count;

        rig.Clock.Now = won + 15f;          // mp_bonusroundtime 15, not past it
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);

        rig.Clock.Now = won + 15.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(0, rig.Flow.RoundsPlayed);                    // a mini-round is not a full round
        // The next stage loads through the round's inputs, not a full map reset
        // (stock RoundRespawn cleans the map only on a forced reset or after a
        // waiting-for-players round, teamplayroundbased_gamerules.cpp:2805-2810).
        Assert.Equal(resets, rig.World.Count("reset"));
        Assert.Equal(spawns + 1, rig.World.Count("spawn"));         // the next stage loads through the round inputs
        Assert.Equal(actives + 1, rig.World.Count("activate"));
        Assert.Equal(caps + 1, rig.World.Count("cp_spawns"));       // its rooms are this stage's
        Assert.Equal(0, rig.Scores.Score(TFTeam.Blue));             // only a forced-reset win adds the round point
        Assert.Equal(new[] { "TeamWin->Restart", "Restart->Preround" }, rig.Listener.Heard.TakeLast(2));

        // Both teams land in the new stage's room: the second team on its
        // respawn-wave tick, mp_respawnwavetime behind the first.
        Assert.True(rig.RunUntil(() => rig.Roster.Respawns.Count == respawns + 2, limit: 60f));
    }

    /// <summary>
    /// CheckNextLevelCvar (:1246-1268): once the bonus time on a full-reset win
    /// runs out, a set nextlevel ends the game — the win panel already called
    /// the map won, and the intermission (scores shown, frozen players) replaces
    /// the next round; the level change itself waits mp_chattime behind the
    /// intermission, the cycle's next map answering when nextlevel is empty
    /// (Intermission's own facts).
    /// </summary>
    [Fact]
    public void A_full_reset_win_with_nextlevel_ends_the_game_after_the_bonus_time()
    {
        var rig = new RoundRig();
        rig.Settings.NextLevel = "pl_goldrush";
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);
        Assert.True(rig.WinPanel.Sent.Single().GameOver);           // IsGameOver(): nextlevel with a forced reset

        float won = rig.Clock.Now;
        rig.Clock.Now = won + 15f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);           // the hold is the stock bonus time
        Assert.Equal(0, rig.End.Intermissions);

        rig.Clock.Now = won + 15.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.GameOver, rig.Flow.State);
        Assert.Equal(1, rig.Announcer.Count("game_over NextLevel CVAR"));
        Assert.Equal(1, rig.End.Intermissions);
    }

    /// <summary>
    /// The stage chain of a mini-round map under a set nextlevel: a stage win
    /// that leaves rounds to play carries no forced reset, so the nextlevel
    /// arm (CheckNextLevelCvar, whose stock gate is
    /// <c>m_bForceMapReset &amp;&amp; nextlevel.GetString()</c>,
    /// teamplayroundbased_gamerules.cpp:1246-1268) stays quiet and the machine
    /// yields through RESTART into the next stage; the last stage's
    /// forced-reset win is what answers the nextlevel, counts the full round
    /// and takes the intermission.
    /// </summary>
    [Fact]
    public void Nextlevel_holds_while_mini_round_wins_still_have_stages_to_play()
    {
        var rig = new RoundRig();
        rig.Settings.NextLevel = "ctf_2fort";
        rig.Running();

        // Stage one: no reset, so the next stage is picked and nothing ends.
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured, forceMapReset: false);
        rig.Clock.Now += 15.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(0, rig.End.Intermissions);
        Assert.Equal(0, rig.Announcer.Count("game_over"));
        Assert.Equal(0, rig.Flow.RoundsPlayed);                     // mini-rounds are not full rounds

        // Stage two: the same quiet yield.
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured, forceMapReset: false);
        rig.Clock.Now += 15.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(0, rig.End.Intermissions);
        Assert.Equal(0, rig.Announcer.Count("game_over"));

        // Stage three: the forced-reset win answers the nextlevel and ends the game.
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);
        rig.Clock.Now += 15.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.GameOver, rig.Flow.State);
        Assert.Equal(1, rig.Announcer.Count("game_over NextLevel CVAR"));
        Assert.Equal(1, rig.End.Intermissions);
        Assert.Equal(1, rig.Flow.RoundsPlayed);                     // only the forced-reset win counts
    }
}
