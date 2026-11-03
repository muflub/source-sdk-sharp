extern alias client;

using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

using Xunit;

using CPlayer = client::TF2.Client.Player;

namespace TF2.Tests.Player;

/// <summary>
/// The players' stats (<c>CTFGameStats</c>' round and accumulated,
/// <c>CalcPlayerScore</c>) and <c>tf_player_manager</c>'s rows
/// (<c>CTFPlayerResource::UpdateConnectedPlayer</c>): the class, the max
/// health, the score, the round's damage and healing once a second, the
/// respawn time, an empty slot zeroed.
/// </summary>
public sealed class TFPlayerResourceTests
{
    private sealed class Waves : TF2.Server.Rules.ITeamRespawnWaves
    {
        public float NextRespawnWave(TFTeam team) => 42f;
        public float NextRespawnWave(TFTeam team, EntityHandle player) => 42f;
        public float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true) => 10f;
        public void SetTeamRespawnWaveTime(TFTeam team, float seconds) { }
        public void AddTeamRespawnWaveTime(TFTeam team, float seconds) { }
    }

    private static (PlayerRig Rig, TFPlayerStats Stats, TFPlayerResourceRows Rows, TFPlayerResourceNetState State) Make(
        TFClass cls, TF2.Server.Combat.TFDominations? dominations = null, TF2.Server.Rules.ITeamRoundState? rounds = null)
    {
        var rig = new PlayerRig().As(cls);
        rig.Engine.Returns("Players.GetObserverMode", ObserverMode.None);
        var stats = new TFPlayerStats();
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            new TFPlayerConditions(rig.Carriers, rig.Engine), rig.Loadout, rig.Engine);
        var state = new TFPlayerResourceNetState();
        state.BindLocal();
        return (rig, stats, new TFPlayerResourceRows(rig.Carriers, resupply, stats, rig.Engine, new Waves(), dominations, rounds), state);
    }

    [Fact]
    public void A_stat_counts_in_the_round_and_the_accumulated()
    {
        var stats = new TFPlayerStats();

        stats.Increment(PlayerRig.Player, TFStat.Kills);
        stats.Increment(PlayerRig.Player, TFStat.Damage, 150);

        Assert.Equal(1, stats.Round(PlayerRig.Index, TFStat.Kills));
        Assert.Equal(150, stats.Accumulated(PlayerRig.Index, TFStat.Damage));
        Assert.Equal(0, stats.Round(PlayerRig.Index + 1, TFStat.Kills));
    }

    [Fact]
    public void A_round_respawn_resets_only_the_rounds_stats()
    {
        var stats = new TFPlayerStats();
        stats.Increment(PlayerRig.Player, TFStat.Kills, 3);

        ((TF2.Server.Rules.ITeamRoundListener)stats).OnStateChanged(RoundState.TeamWin, RoundState.Preround);

        Assert.Equal(0, stats.Round(PlayerRig.Index, TFStat.Kills));
        Assert.Equal(3, stats.Accumulated(PlayerRig.Index, TFStat.Kills));
    }

    [Fact]
    public void A_departure_forgets_the_players_stats()
    {
        var stats = new TFPlayerStats();
        stats.Increment(PlayerRig.Player, TFStat.Kills, 3);

        ((IClientLifecycle)stats).OnClientDisconnected(PlayerRig.Index);

        Assert.Equal(0, stats.Accumulated(PlayerRig.Index, TFStat.Kills));
    }

    /// <summary>CalcPlayerScore: kills 1 each, captures 2, healing 1 per 600, assists 1 per 2, headshots 1 per 2.</summary>
    [Fact]
    public void The_score_is_calc_player_scores_sum()
    {
        var stats = new TFPlayerStats();
        stats.Increment(PlayerRig.Player, TFStat.Kills, 4);
        stats.Increment(PlayerRig.Player, TFStat.Captures, 1);
        stats.Increment(PlayerRig.Player, TFStat.Healing, 1300);
        stats.Increment(PlayerRig.Player, TFStat.Killassists, 3);
        stats.Increment(PlayerRig.Player, TFStat.Headshots, 5);

        Assert.Equal(4 + 2 + 2 + 1 + 2, stats.Score(PlayerRig.Index));
    }

    [Fact]
    public void A_bogus_healing_scores_nothing()
    {
        var stats = new TFPlayerStats();
        stats.Increment(PlayerRig.Player, TFStat.Healing, 20_000_000);

        Assert.Equal(0, stats.Score(PlayerRig.Index));
    }

    [Fact]
    public void A_players_row_carries_the_class_the_max_health_and_the_score()
    {
        var (_, stats, rows, state) = Make(TFClass.HeavyWeapons);
        stats.Increment(PlayerRig.Player, TFStat.Kills, 2);

        rows.Update(state, 10f);

        Assert.Equal((byte)TFClass.HeavyWeapons, state.PlayerClass[PlayerRig.Index]);
        Assert.Equal(300, state.MaxHealth[PlayerRig.Index]);
        Assert.Equal(300, state.MaxBuffedHealth[PlayerRig.Index]);
        Assert.Equal(2, state.TotalScore[PlayerRig.Index]);
        Assert.Equal(0f, state.NextRespawnTime[PlayerRig.Index]);
    }

    [Fact]
    public void The_damage_and_healing_rows_are_sent_once_a_second()
    {
        var (_, stats, rows, state) = Make(TFClass.Medic);
        stats.Increment(PlayerRig.Player, TFStat.Healing, 50);
        rows.Update(state, 10f);
        Assert.Equal(50, state.Healing[PlayerRig.Index]);

        stats.Increment(PlayerRig.Player, TFStat.Healing, 50);
        rows.Update(state, 10.5f);
        Assert.Equal(50, state.Healing[PlayerRig.Index]);

        rows.Update(state, 11.1f);
        Assert.Equal(100, state.Healing[PlayerRig.Index]);
    }

    [Fact]
    public void A_dead_players_row_says_when_their_wave_lets_them_out()
    {
        var (rig, _, rows, state) = Make(TFClass.Scout);
        rig.Engine.Returns("Players.IsAlive", false);

        rows.Update(state, 10f);

        Assert.Equal(42f, state.NextRespawnTime[PlayerRig.Index]);
    }

    /// <summary>UpdatePlayerRespawnData's arm (tf_player_resource.cpp:274-275):
    /// a set override answers the row, not the wave.</summary>
    [Fact]
    public void A_dead_players_row_answers_the_override_rather_than_the_wave()
    {
        var (rig, _, rows, state) = Make(TFClass.Scout);
        rig.Engine.Returns("Players.IsAlive", false);
        var ov = new TFRespawnOverride(rig.Carriers, new TF2.Tests.Rules.FakeClock { Now = 20f });
        ov.Attach(PlayerRig.Player);
        ov.Apply(5f, null); // release at 25, where the fake wave says 42

        rows.Update(state, 30f);

        Assert.Equal(25f, state.NextRespawnTime[PlayerRig.Index]);
    }

    /// <summary>The same arm reads the pair without an alive gate: stock clears
    /// it at spawn, not at the read.</summary>
    [Fact]
    public void A_standing_players_override_answers_the_row_too()
    {
        var (rig, _, rows, state) = Make(TFClass.Scout); // alive
        var ov = new TFRespawnOverride(rig.Carriers, new TF2.Tests.Rules.FakeClock { Now = 12f });
        ov.Attach(PlayerRig.Player);
        ov.Apply(3f, null); // release at 15

        rows.Update(state, 30f);

        Assert.Equal(15f, state.NextRespawnTime[PlayerRig.Index]);
    }

    [Fact]
    public void A_slot_with_no_player_is_zeroed()
    {
        var (rig, _, rows, state) = Make(TFClass.Scout);
        state.PlayerClass[5] = 3;
        state.MaxHealth[5] = 200;

        rows.Update(state, 10f);

        Assert.Equal(0, state.PlayerClass[5]);
        Assert.Equal(0, state.MaxHealth[5]);
        Assert.Equal(125, state.MaxHealth[PlayerRig.Index]);
        _ = rig;
    }

    /// <summary>m_iActiveDominations (tf_player_resource.cpp:239): the player's live dominations.</summary>
    [Fact]
    public void A_dominating_players_row_carries_their_domination_count()
    {
        var dominations = new TF2.Server.Combat.TFDominations();
        var (_, _, rows, state) = Make(TFClass.Soldier, dominations);
        dominations.DeathFlags(9, PlayerRig.Index, -1);
        dominations.DeathFlags(9, PlayerRig.Index, -1);
        dominations.DeathFlags(9, PlayerRig.Index, -1);
        dominations.DeathFlags(9, PlayerRig.Index, -1);

        rows.Update(state, 10f);

        Assert.Equal(1, state.ActiveDominations[PlayerRig.Index]);
        Assert.Equal(0, state.ActiveDominations[9]);
    }

    /// <summary>The gamerules' word rides the resource so a dead player's
    /// HUD names the stalemate and next-round arms (the netstate's
    /// RoundState, written every think).</summary>
    [Fact]
    public void The_rows_carry_the_rounds_state_word()
    {
        var (_, _, rows, state) = Make(TFClass.Soldier, rounds: new FixedRounds(RoundState.Stalemate));

        rows.Update(state, 10f);

        Assert.Equal((byte)RoundState.Stalemate, state.RoundState);
    }

    [Fact]
    public void The_resource_is_made_at_level_init()
    {
        var engine = new RecordingEngine();
        engine.Returns("Entities.Create", new EntityHandle(0x0005_0070u));
        IGameLifecycle spawner = new TFPlayerResourceSpawner(engine, new RecordingLog());

        spawner.OnLevelInit("ctf_2fort");

        Assert.Equal(TFEntityNames.PlayerResource, Assert.Single(engine.AllOf("Entities.Create")).ArgAs<string>(0));
        Assert.Equal(1, engine.CountOf("Entities.Spawn"));
    }

    [Fact]
    public void The_rows_fit_the_two_kilobyte_tier()
    {
        int blob = TFPlayerResourceNetState.NetLayout.Size;

        Assert.True(blob > 1024, $"the blob is {blob} bytes: a run has been dropped");
        Assert.True(blob <= 2048, $"the blob is {blob} bytes and no tier carries it");
    }

    [Fact]
    public void The_client_reports_the_rows_it_decoded()
    {
        var resources = new CPlayer.TFClientPlayerResources();
        Assert.Contains("none has arrived", resources.Report());
    }

    /// <summary>The rows carry the round's state word, and the winning team
    /// beside it: stock's m_iWinningTeam on the gamerules proxy, so a client
    /// can tell whose winners' crit it is watching (the netstate's
    /// WinningTeam, written every think).</summary>
    [Fact]
    public void The_rows_carry_the_winning_teams_word()
    {
        var (_, _, rows, state) = Make(TFClass.Soldier, rounds: new FixedRounds(RoundState.TeamWin, TFTeam.Red));

        rows.Update(state, 10f);

        Assert.Equal((byte)RoundState.TeamWin, state.RoundState);
        Assert.Equal((byte)TFTeam.Red, state.WinningTeam);
    }

    /// <summary>The wait and map-reset words ride beside the state: stock's
    /// m_bInWaitingForPlayers and m_flMapResetTime on the gamerules proxy
    /// (the clock's bands and its server-time count both read them).</summary>
    [Fact]
    public void The_rows_carry_the_wait_and_map_reset_words()
    {
        var (_, _, rows, state) = Make(TFClass.Soldier, rounds: new FixedRounds(RoundState.RoundRunning,
                                                                               waiting: true, mapReset: 40f));

        rows.Update(state, 50f);

        Assert.Equal((byte)1, state.WaitingForPlayers);
        Assert.Equal(40f, state.MapResetTime);
    }

    /// <summary>The setup flag rides beside the state: stock's m_bInSetup on
    /// the gamerules proxy (teamplayroundbased_gamerules.cpp:91/:111), what
    /// the medigun's charge gate reads on the client as the server's
    /// ITeamRoundState.InSetup half does.</summary>
    [Fact]
    public void The_rows_carry_the_setup_flag()
    {
        var (_, _, rows, state) = Make(TFClass.Soldier, rounds: new FixedRounds(RoundState.RoundRunning, setup: true));

        rows.Update(state, 10f);

        Assert.Equal((byte)1, state.InSetup);
    }

    [Fact]
    public void The_rows_clear_the_setup_flag_outside_setup()
    {
        var (_, _, rows, state) = Make(TFClass.Soldier, rounds: new FixedRounds(RoundState.RoundRunning));

        rows.Update(state, 10f);

        Assert.Equal((byte)0, state.InSetup);
    }

    /// <summary>A stand-in round machine for the words the rows write.</summary>
    private sealed class FixedRounds(RoundState state, TFTeam winning = TFTeam.Unassigned,
                                     bool waiting = false, float mapReset = 0f, bool setup = false)
        : TF2.Server.Rules.ITeamRoundState
    {
        public RoundState State => state;

        public bool InWaitingForPlayers => waiting;

        public float MapResetTime => mapReset;

        public float StalemateStartTime => -1f;

        public bool InSetup => setup;

        public TFTeam WinningTeam => winning;
    }
}
