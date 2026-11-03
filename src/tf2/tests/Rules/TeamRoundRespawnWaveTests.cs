using SourceSharp.Abi;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The respawn waves (stock <c>CheckRespawnWaves</c>, <c>GetNextRespawnWave</c>,
/// <c>GetRespawnWaveMaxLength</c>): the dead come back together on their
/// team's wave edge, once they have waited the death animation, the freeze
/// panel and one full unscaled wave.
/// </summary>
public sealed class TeamRoundRespawnWaveTests
{
    /// <summary>When each of <paramref name="players"/> was respawned after <paramref name="after"/>.</summary>
    private static float RespawnedAt(RoundRig rig, EntityHandle player, float after)
        => rig.Roster.Respawns.First(r => r.Player == player && r.At > after).At;

    [Fact]
    public void Two_players_dying_one_second_apart_respawn_on_the_same_wave_edge()
    {
        var rig = new RoundRig();
        var (red, _) = rig.Running();
        var red2 = rig.Roster.Add(TFTeam.Red);

        // Two on red: 10 s scaled by RemapValClamped( 2, 1, 8, 0.25, 1 ) is
        // 3.6, floored to 5. The deaths are placed so that both minimum times
        // (death + 6.4 + the unscaled 10) fall 1.5 and 2.5 s into one wave,
        // whatever the wave's phase: stock lets a player out at the first
        // wave edge after their minimum time.
        Assert.Equal(5f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
        float edge = rig.Flow.NextRespawnWave(TFTeam.Red);
        Assert.True(edge > 0f, "a running round schedules a wave");
        float killAt = edge + 5f * 4 - (TeamRoundFlow.DeathAnimLength + 10f) + 1.5f;
        Assert.True(rig.RunUntil(() => rig.Clock.Now >= killAt, limit: 30f));
        float died = rig.Clock.Now;

        rig.Roster.Kill(red);
        rig.Step(1.0f);
        rig.Roster.Kill(red2);

        Assert.True(rig.RunUntil(() => rig.Roster.IsAlive(red) && rig.Roster.IsAlive(red2), limit: 60f));

        float first = RespawnedAt(rig, red, died);
        float second = RespawnedAt(rig, red2, died);
        Assert.Equal(first, second);
        // Not before 2 + 0.4 + 4 s of death and freeze cam plus one unscaled 10 s wave.
        Assert.True(first - died >= TeamRoundFlow.DeathAnimLength + 10f, $"respawned {first - died:F1}s after dying");
    }

    [Fact]
    public void A_dead_player_waits_for_the_wave_and_does_not_come_back_alone()
    {
        var rig = new RoundRig();
        var (red, _) = rig.Running();
        float died = rig.Clock.Now;
        rig.Roster.Kill(red);

        rig.RunUntil(() => false, limit: TeamRoundFlow.DeathAnimLength + 9f);

        Assert.False(rig.Roster.IsAlive(red));
        Assert.DoesNotContain(rig.Roster.Respawns, r => r.Player == red && r.At > died);
    }

    [Fact]
    public void A_wave_leaves_the_living_where_they_are()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        int before = rig.Roster.Respawns.Count;

        rig.RunUntil(() => false, limit: 30f);   // six 5 s waves per team

        Assert.True(rig.Flow.NextRespawnWave(TFTeam.Red) > 0f, "waves are scheduled");
        Assert.Equal(before, rig.Roster.Respawns.Count);
        Assert.True(rig.Roster.IsAlive(red) && rig.Roster.IsAlive(blue));
    }

    [Fact]
    public void The_wave_shrinks_with_the_team_down_to_five_seconds()
    {
        var rig = new RoundRig();
        rig.Settings.RespawnWaveTime = 20f;
        rig.Running();   // one player per team

        // RemapValClamped( 1, 1, 8, 0.25, 1.0 ) = 0.25: 20 s becomes 5.
        Assert.Equal(5f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
        Assert.Equal(20f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red, scaleWithNumPlayers: false));

        for (int i = 0; i < 7; i++)
            rig.Roster.Add(TFTeam.Red);

        Assert.Equal(20f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));   // eight: the optimum
    }

    [Fact]
    public void A_short_wave_is_not_scaled()
    {
        var rig = new RoundRig();
        rig.Settings.RespawnWaveTime = 5f;
        rig.Running();

        Assert.Equal(5f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
    }

    [Fact]
    public void There_are_no_waves_outside_a_running_round()
    {
        var rig = new RoundRig();
        rig.Roster.Add(TFTeam.Red);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround));

        Assert.Equal(0f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
    }

    [Fact]
    public void mp_disable_respawn_times_lets_the_dead_out_after_the_death_animation_alone()
    {
        var rig = new RoundRig();
        rig.Settings.DisableRespawnTimes = true;
        var (red, _) = rig.Running();
        float died = rig.Clock.Now;
        rig.Roster.Kill(red);

        Assert.True(rig.RunUntil(() => rig.Roster.IsAlive(red), limit: 10f));

        float back = RespawnedAt(rig, red, died) - died;
        Assert.InRange(back, TeamRoundFlow.DeathAnimLength, TeamRoundFlow.DeathAnimLength + 0.2f);
    }

    [Fact]
    public void A_team_wave_time_set_by_the_map_replaces_mp_respawnwavetime_until_the_round_resets()
    {
        var rig = new RoundRig();
        rig.Running();
        for (int i = 0; i < 7; i++)
            rig.Roster.Add(TFTeam.Blue);   // eight on blue: unscaled

        rig.Flow.SetTeamRespawnWaveTime(TFTeam.Blue, 6f);
        Assert.Equal(6f, rig.Flow.RespawnWaveMaxLength(TFTeam.Blue));

        rig.Flow.AddTeamRespawnWaveTime(TFTeam.Blue, 4f);
        Assert.Equal(10f, rig.Flow.RespawnWaveMaxLength(TFTeam.Blue));

        // RoundRespawn puts the original (the first value set) back.
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning, limit: 30f));
        Assert.Equal(6f, rig.Flow.RespawnWaveMaxLength(TFTeam.Blue));
    }

    [Fact]
    public void The_mode_scales_the_wave()
    {
        var rig = new RoundRig();
        rig.Settings.RespawnWaveTime = 5f;
        rig.Mode.WaveScale = 2f;
        rig.Running();

        Assert.Equal(10f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
    }

    [Fact]
    public void The_policy_never_lets_the_core_respawn_a_TF2_player_on_a_button()
    {
        var rig = new RoundRig();
        var (red, _) = rig.Running();
        rig.Roster.Kill(red);
        var policy = new TFRespawnPolicy(rig.Flow, rig.Roster);

        Assert.False(policy.WaveRelease(red, rig.Clock.Now));
        Assert.False(policy.WaveRelease(red, rig.Clock.Now + 1000f));
    }

    [Fact]
    public void The_policy_s_wait_is_the_time_from_death_to_the_player_s_wave()
    {
        var rig = new RoundRig();
        var (red, _) = rig.Running();
        float died = rig.Clock.Now;
        rig.Roster.Kill(red);
        var policy = new TFRespawnPolicy(rig.Flow, rig.Roster);

        float wait = policy.Delay(red);

        Assert.Equal(rig.Flow.NextRespawnWave(TFTeam.Red, red) - died, wait, 3);
        Assert.True(wait >= TeamRoundFlow.DeathAnimLength + 10f, $"wait {wait}");
    }

    [Fact]
    public void In_sudden_death_nobody_has_a_wave()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer);
        rig.Roster.Kill(red);

        Assert.Equal(0f, rig.Flow.NextRespawnWave(TFTeam.Red, red));
        Assert.Equal(0f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
    }
}
