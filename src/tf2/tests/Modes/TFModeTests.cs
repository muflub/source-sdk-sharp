using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Modes;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Modes;

/// <summary>
/// TF2's modes in the core's proxy (stage2 2.2, U1, D6, D10): the selector
/// picks CTF by a flag, Payload by a train watcher and control points by a
/// control-point master, all run the one team-round machine, and the mode
/// hooks hear the round and can veto a win.
/// </summary>
public sealed class TFModeTests
{
    // ---- the pick ------------------------------------------------------------------

    [Fact]
    public void A_map_with_a_flag_plays_ctf()
    {
        var rig = new ProxyRig(new FixtureMap("ctf_2fort", "info_player_teamspawn", TFGameModeSelector.FlagClass));

        Assert.Equal(CtfMode.ModeId, rig.Pick());
        Assert.Same(rig.Tf.Round.Flow, rig.Modes.Active!.Rounds);
    }

    [Fact]
    public void A_map_with_a_train_watcher_plays_payload()
    {
        var rig = new ProxyRig(new FixtureMap("pl_badwater", "path_track", TFGameModeSelector.TrainWatcherClass));

        Assert.Equal(PayloadMode.ModeId, rig.Pick());
    }

    [Fact]
    public void A_map_with_neither_plays_the_core_s_null_mode()
    {
        var rig = new ProxyRig(new FixtureMap("cp_dustbowl", "team_control_point", "info_player_teamspawn"));

        Assert.Equal(GameModes.NullModeId, rig.Pick());
        Assert.Same(rig.Rules, rig.Modes.Active!.Rounds);
    }

    [Fact]
    public void A_flag_wins_over_a_train_watcher_as_in_stock_s_order()
    {
        var rig = new ProxyRig(new FixtureMap("plr_test", TFGameModeSelector.TrainWatcherClass, TFGameModeSelector.FlagClass));

        Assert.Equal(CtfMode.ModeId, rig.Pick());
    }

    [Fact]
    public void A_special_delivery_map_is_not_ctf_whatever_its_flags()
    {
        var rig = new ProxyRig(new FixtureMap("sd_doomsday", TFGameModeSelector.FlagClass));

        Assert.Equal(GameModes.NullModeId, rig.Pick());
    }

    [Fact]
    public void A_map_with_a_control_point_master_plays_cp()
    {
        var rig = new ProxyRig(new FixtureMap("cp_badlands", "team_control_point", TFGameModeSelector.ControlPointMasterClass));

        Assert.Equal(CPMode.ModeId, rig.Pick());
        Assert.Same(rig.Tf.Round.Flow, rig.Modes.Active!.Rounds);
    }

    [Fact]
    public void A_control_point_map_with_koth_logic_plays_koth()
    {
        // Stock: Activate sets TF_GAMETYPE_CP at tf_gamerules.cpp:4358-4362 and
        // raises m_bPlayingKoth at 4384-4388 when the map holds a tf_logic_koth;
        // koth_viaduct is a CP map with one.
        var rig = new ProxyRig(new FixtureMap("koth_viaduct", "team_control_point",
                                              TFGameModeSelector.ControlPointMasterClass, TFGameModeSelector.KothLogicClass));

        Assert.Equal(KothMode.ModeId, rig.Pick());
        Assert.Same(rig.Tf.Round.Flow, rig.Modes.Active!.Rounds);
    }

    [Fact]
    public void Picking_the_koth_mode_sets_the_cp_game_type()
    {
        // Stock plays koth under TF_GAMETYPE_CP: the cp flag is set, no other.
        var rig = new ProxyRig(new FixtureMap("koth_viaduct", TFGameModeSelector.ControlPointMasterClass,
                                              TFGameModeSelector.KothLogicClass));
        rig.Tf.Round.Running();

        rig.Pick();

        Assert.Equal(new[] { TFGameType.Cp }, rig.Tf.Flags.Set);
    }

    [Fact]
    public void A_koth_logic_entity_without_a_master_plays_no_mode_as_in_stock()
    {
        // Stock's m_bPlayingKoth alone does nothing: the koth arms all sit
        // behind the control-point game type, which needs the master
        // (tf_gamerules.cpp:4358-4362, 16769; team_control_point_master.cpp:638).
        var rig = new ProxyRig(new FixtureMap("bad_koth", TFGameModeSelector.KothLogicClass));

        Assert.Equal(GameModes.NullModeId, rig.Pick());
    }

    [Fact]
    public void A_train_watcher_wins_over_a_control_point_master_as_in_stock_s_order()
    {
        // Stock sets ESCORT at tf_gamerules.cpp:4350-4356 and only reaches the
        // control-point arm at 4358-4362 when there is no watcher.
        var rig = new ProxyRig(new FixtureMap("plr_test", TFGameModeSelector.TrainWatcherClass,
                                              TFGameModeSelector.ControlPointMasterClass));

        Assert.Equal(PayloadMode.ModeId, rig.Pick());
    }

    [Fact]
    public void Picking_a_TF2_mode_resets_its_machine_and_sets_its_game_type()
    {
        var rig = new ProxyRig(new FixtureMap("pl_badwater", TFGameModeSelector.TrainWatcherClass));
        var flow = rig.Tf.Round.Flow;
        rig.Tf.Round.Running();
        Assert.Equal(RoundState.RoundRunning, flow.State);

        rig.Pick();

        Assert.Equal(RoundState.Pregame, flow.State);
        Assert.Equal(new[] { TFGameType.Escort }, rig.Tf.Flags.Set);
    }

    [Fact]
    public void Picking_the_cp_mode_sets_the_cp_game_type()
    {
        var rig = new ProxyRig(new FixtureMap("cp_badlands", TFGameModeSelector.ControlPointMasterClass));
        rig.Tf.Round.Running();

        rig.Pick();

        Assert.Equal(new[] { TFGameType.Cp }, rig.Tf.Flags.Set);
    }

    [Fact]
    public void All_four_modes_register_under_distinct_ids()
    {
        var rig = new ProxyRig(new FixtureMap("x"));

        Assert.Equal(new[] { CPMode.ModeId, CtfMode.ModeId, KothMode.ModeId, PayloadMode.ModeId },
                     rig.Modes.VerifyRegistry().Order());
    }

    [Fact]
    public void Payload_setup_sets_multiple_trains_from_the_map()
    {
        var rig = new TFModeRig();

        // Before: no tf_logic_multiple_escort on the map (the rig's engine
        // answers none), and the setup clears the flag.
        rig.Objectives.SetMultipleTrains(true);
        rig.Payload.SetupRound(0f);
        Assert.False(rig.Objectives.MultipleTrains);

        // After: the map fields one — the gEntList search answers a handle
        // (tf_gamerules.cpp:4350-4357).
        rig.Engine.Returns("Entities.FindAllByClassname", new[] { new EntityHandle(0x0500) });
        rig.Payload.SetupRound(0f);
        Assert.True(rig.Objectives.MultipleTrains);
    }

    [Fact]
    public void AMapWithMultipleEscortLogicHasMultipleTrains()
    {
        // Stock's Activate sets m_bMultipleTrains from a tf_logic_multiple_escort
        // on the map (tf_gamerules.cpp:4350-4357); escort answers the flag through
        // HasMultipleTrains, which the round switch and the escort HUD read.
        var rig = new TFModeRig();

        // Before: no tf_logic_multiple_escort on the map (the rig's engine
        // answers none), and the setup keeps the flag down.
        rig.Payload.SetupRound(0f);
        Assert.False(rig.Payload.HasMultipleTrains);

        // After: the map fields one — the gEntList search answers a handle.
        rig.Engine.Returns("Entities.FindAllByClassname", new[] { new EntityHandle(0x0500) });
        rig.Payload.SetupRound(0f);
        Assert.True(rig.Payload.HasMultipleTrains);
    }

    // ---- the mode's halves ---------------------------------------------------------

    [Fact]
    public void A_TF2_mode_answers_with_the_team_round_machine_wave_respawns_and_team_scores()
    {
        var rig = new TFModeRig();

        Assert.Same(rig.Round.Flow, rig.Ctf.Rounds);
        Assert.IsType<TFRespawnPolicy>(rig.Ctf.Respawn);
        Assert.IsType<TFTeamScoring>(rig.Ctf.Scoring);
        Assert.Same(rig.Ctf, rig.Ctf.Setup);
        // One machine, one policy and one table for every TF2 mode.
        Assert.Same(rig.Ctf.Rounds, rig.Payload.Rounds);
        Assert.Same(rig.Ctf.Scoring, rig.Payload.Scoring);
    }

    [Fact]
    public void The_team_score_reaches_the_scoreboard_through_the_mode()
    {
        var rig = new TFModeRig();
        rig.Round.Running();

        rig.Round.Flow.SetWinningTeam(TFTeam.Blue, WinReason.FlagCaptureLimit);

        Assert.Equal(1, rig.Ctf.Scoring!.TeamScore((int)TFTeam.Blue));
        Assert.Equal(0, rig.Ctf.Scoring.TeamScore((int)TFTeam.Red));
        Assert.True(rig.Ctf.Scoring.TeamScore((int)TFTeam.Spectator) < 0);
    }

    [Fact]
    public void Payload_sends_a_new_player_on_even_teams_to_the_attackers_and_ctf_does_not()
    {
        var rig = new TFModeRig();

        Assert.True(rig.Payload.EvenTeamsJoinAttackers);
        Assert.False(rig.Ctf.EvenTeamsJoinAttackers);
    }

    // ---- the hooks (D10) -------------------------------------------------------------

    [Fact]
    public void A_mode_hook_hears_OnRoundStart_once_per_round()
    {
        var rig = new TFModeRig();
        rig.Round.Running();
        Assert.Equal(new[] { "round_start ctf" }, rig.Hook.Heard);

        rig.Round.Flow.SetWinningTeam(TFTeam.Red, WinReason.FlagCaptureLimit);
        Assert.True(rig.Round.RunUntil(() => rig.Round.Flow.State == RoundState.RoundRunning, limit: 30f));

        Assert.Equal(new[] { "round_start ctf", "round_start ctf" }, rig.Hook.Heard);
    }

    [Fact]
    public void A_mode_hook_s_win_veto_is_honoured()
    {
        var rig = new TFModeRig();
        rig.Round.Running();
        rig.Hook.AllowsWins = false;

        rig.Round.Flow.SetWinningTeam(TFTeam.Red, WinReason.FlagCaptureLimit);

        Assert.True(rig.Hook.WinChecks > 0, "the hook was asked");
        Assert.Equal(RoundState.RoundRunning, rig.Round.Flow.State);
        Assert.Equal(0, rig.Round.Scores.Score(TFTeam.Red));
    }

    [Fact]
    public void A_mode_hook_that_allows_lets_the_win_stand()
    {
        var rig = new TFModeRig();
        rig.Round.Running();

        rig.Round.Flow.SetWinningTeam(TFTeam.Red, WinReason.FlagCaptureLimit);

        Assert.True(rig.Hook.WinChecks > 0, "the hook was asked");
        Assert.Equal(RoundState.TeamWin, rig.Round.Flow.State);
    }

    [Fact]
    public void A_mode_hook_lengthens_the_respawn_wave()
    {
        var rig = new TFModeRig();
        rig.Round.Settings.RespawnWaveTime = 5f;
        rig.Round.Running();
        Assert.Equal(5f, rig.Round.Flow.RespawnWaveMaxLength(TFTeam.Red));

        rig.Hook.WaveAdd = 3f;

        Assert.Equal(8f, rig.Round.Flow.RespawnWaveMaxLength(TFTeam.Red));
    }

    [Fact]
    public void An_objective_event_reaches_every_mode_hook()
    {
        var rig = new TFModeRig();

        rig.Ctf.ReportObjective(new TFObjectiveEvent("flag_captured", TFTeam.Blue, 1));

        Assert.Equal(new[] { "objective ctf flag_captured Blue 1" }, rig.Hook.Heard);
    }

    [Fact]
    public void A_throwing_win_check_does_not_veto()
    {
        var rig = new TFModeRig();
        var services = new TFModeServices(rig.Round.Flow, rig.Round.Flow, rig.Round.Roster, rig.Round.Scores,
                                          rig.Flags, rig.Round.Log, new ListHooks<ITFModeHook>(new ThrowingHook()));
        var mode = new CtfMode(services, rig.CtfRules, rig.CtfSettings);

        Assert.True(mode.AllowWin(TFTeam.Red, WinReason.FlagCaptureLimit));
        Assert.Contains(rig.Round.Log.Lines, l => l.Warning && l.Text.Contains("ModifyWinCheck threw", StringComparison.Ordinal));
    }

    private sealed class ThrowingHook : ITFModeHook
    {
        public void OnRoundStart(TFModeBase mode) { }
        public void OnObjectiveEvent(TFModeBase mode, TFObjectiveEvent objective) { }
        public bool ModifyWinCheck(TFModeBase mode, TFTeam team, WinReason reason) => throw new InvalidOperationException("broken mod");
        public float ModifyRespawnWave(TFModeBase mode, TFTeam team, float seconds) => seconds;
    }

    // ---- structure -----------------------------------------------------------------------

    [Fact]
    public void No_type_outside_Modes_names_a_concrete_mode_type()
    {
        var assembly = typeof(TFModeBase).Assembly;
        var modes = ModeTypes();
        Assert.Contains(typeof(CtfMode), modes);
        Assert.Contains(typeof(PayloadMode), modes);

        Assert.Empty(Offenders(assembly, modes, typeof(TFModeBase).Namespace!));
    }

    [Fact]
    public void The_structure_check_finds_a_type_that_names_a_mode()
    {
        // The positive arm, over this assembly: ModeHolder holds a CtfMode field.
        var found = Offenders(typeof(ModeHolder).Assembly, ModeTypes(), typeof(TFModeBase).Namespace!);

        Assert.Contains(found, o => o.StartsWith(typeof(ModeHolder).FullName!, StringComparison.Ordinal));
    }

    private static HashSet<Type> ModeTypes()
        => typeof(TFModeBase).Assembly.GetTypes()
            .Where(t => typeof(IGameMode).IsAssignableFrom(t) && !t.IsInterface)
            .ToHashSet();

    /// <summary>Every type outside <paramref name="allowed"/> whose constructor, field or property names a mode type.</summary>
    private static List<string> Offenders(Assembly scanned, HashSet<Type> modes, string allowed)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = new List<string>();
        foreach (var type in scanned.GetTypes().Where(t => t.Namespace != allowed))
        {
            var named = type.GetConstructors(all).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                .Concat(type.GetFields(all).Select(f => f.FieldType))
                .Concat(type.GetProperties(all).Select(p => p.PropertyType));
            offenders.AddRange(named.Where(modes.Contains).Select(t => $"{type.FullName} names {t.Name}"));
        }

        return offenders;
    }

    private sealed class ModeHolder
    {
#pragma warning disable CS0649 // never assigned: only its type is read
        private readonly CtfMode? _mode;
#pragma warning restore CS0649

        public override string ToString() => _mode?.Id ?? "";
    }
}
