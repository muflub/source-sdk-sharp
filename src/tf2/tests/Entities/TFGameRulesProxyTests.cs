using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// The <c>tf_gamerules</c> map entity (<c>CTFGameRulesProxy</c>,
/// shared/tf/tf_gamerules.cpp:1556-1764): the entity the stock maps' I/O
/// targets for the rules' word — the koth clocks and the team respawn waves
/// here, the rest of the datadesc's inputs as ports are needed. Stock's maps
/// fire <c>OnCapTeam1</c> → <c>SetRedKothClockActive</c> and
/// <c>SetRedTeamRespawnWaveTime</c> at it (koth_viaduct's trigger_capture_area
/// outputs); these facts drive the inputs directly, and the koth capture fact
/// in <see cref="KothTests"/> walks the whole output-to-input chain.
/// </summary>
public sealed class TFGameRulesProxyTests
{
    /// <summary>
    /// The one-point koth map with its timers spawned and paused, as
    /// <c>KothTests.KothMap</c> leaves it, plus the rules proxy the map's
    /// outputs name.
    /// </summary>
    private static (ObjectiveRig Rig, TFGameRulesProxy Proxy) KothMapWithProxy()
    {
        var rig = new ObjectiveRig();
        rig.Master();
        rig.Point("cp_koth", 0);

        rig.Engine.ReturnsEachCall("Entities.Create", () => rig.NewHandle());
        rig.Engine.WhenCalled("Entities.Spawn", call =>
            rig.Registry.Spawn((EntityHandle)call.Args[0]!, RoundTimers.ClassName));

        rig.SpawnKothLogic(("unlock_point", "0"));
        rig.StartRound();

        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass,
                                                 ("targetname", TFGameRulesProxy.MapClass));
        return (rig, proxy);
    }

    // ---- the koth clocks (InputSetRedKothClockActive / InputSetBlueKothClockActive,
    //      tf_gamerules.cpp:1666-1716) --------------------------------------------------

    [Fact]
    public void The_SetRedKothClockActive_input_resumes_the_red_timer_and_pauses_the_blue_s()
    {
        var (rig, proxy) = KothMapWithProxy();

        // Both timers start paused at the round's RoundSpawn.
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Red)!.IsPaused);
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Blue)!.IsPaused);

        rig.Registry.Input(proxy.Handle, "SetRedKothClockActive", string.Empty, 0f, EntityHandle.None);

        Assert.False(rig.Koth.KothTeamTimer(TFTeam.Red)!.IsPaused);
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Blue)!.IsPaused);
    }

    [Fact]
    public void The_SetBlueKothClockActive_input_resumes_the_blue_timer_and_pauses_the_red_s()
    {
        var (rig, proxy) = KothMapWithProxy();

        rig.Registry.Input(proxy.Handle, "SetBlueKothClockActive", string.Empty, 0f, EntityHandle.None);

        Assert.False(rig.Koth.KothTeamTimer(TFTeam.Blue)!.IsPaused);
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Red)!.IsPaused);
    }

    [Fact]
    public void A_koth_clock_input_with_no_timers_changes_nothing()
    {
        // Stock gates only on TFGameRules(); a map with no koth logic leaves
        // the timers null and the input finds nothing (the pTimer-null arms).
        var rig = new ObjectiveRig();
        rig.Master();
        rig.Point("cp_plain", 0);
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass,
                                                ("targetname", TFGameRulesProxy.MapClass));

        rig.Registry.Input(proxy.Handle, "SetRedKothClockActive", string.Empty, 0f, EntityHandle.None);
        rig.Registry.Input(proxy.Handle, "SetBlueKothClockActive", string.Empty, 0f, EntityHandle.None);

        Assert.Null(rig.Koth.KothTeamTimer(TFTeam.Red));
        Assert.Null(rig.Koth.KothTeamTimer(TFTeam.Blue));
    }

    // ---- the team respawn waves (InputSet/Add*TeamRespawnWaveTime,
    //      tf_gamerules.cpp:1737-1764) ---------------------------------------------------

    [Fact]
    public void The_SetRedTeamRespawnWaveTime_input_sets_the_team_s_wave_time()
    {
        var rig = new ObjectiveRig();
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass,
                                                ("targetname", TFGameRulesProxy.MapClass));

        rig.Registry.Input(proxy.Handle, "SetRedTeamRespawnWaveTime", string.Empty, 10f, EntityHandle.None);

        Assert.Contains((TFTeam.Red, 10f), rig.Waves.Set);
        Assert.Empty(rig.Waves.Added);
    }

    [Fact]
    public void The_AddBlueTeamRespawnWaveTime_input_adds_to_the_team_s_wave_time()
    {
        var rig = new ObjectiveRig();
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass,
                                                ("targetname", TFGameRulesProxy.MapClass));

        rig.Registry.Input(proxy.Handle, "AddBlueTeamRespawnWaveTime", string.Empty, 5f, EntityHandle.None);

        Assert.Contains((TFTeam.Blue, 5f), rig.Waves.Added);
        Assert.Empty(rig.Waves.Set);
    }

    // ---- the goal strings (InputSetRed/BlueTeamGoalString, tf_gamerules.cpp:1769-1780) ----

    [Fact]
    public void The_goal_string_inputs_write_the_teams_table()
    {
        var rig = new ObjectiveRig();
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass,
                                                ("targetname", TFGameRulesProxy.MapClass));

        rig.Registry.Input(proxy.Handle, "SetRedTeamGoalString", "#2fort_red_setup_goal", 0f, EntityHandle.None);
        rig.Registry.Input(proxy.Handle, "SetBlueTeamGoalString", "#2fort_blue_setup_goal", 0f, EntityHandle.None);

        Assert.Equal("#2fort_red_setup_goal", rig.TeamTable.Goal((int)TFTeam.Red));
        Assert.Equal("#2fort_blue_setup_goal", rig.TeamTable.Goal((int)TFTeam.Blue));
    }

    // ---- the ctf_overtime key (CTFGameRulesProxy::Activate, tf_gamerules.cpp:1904) ----

    [Fact]
    public void Activate_pushes_the_map_s_ctf_overtime_key_to_the_rules()
    {
        var rig = new ObjectiveRig();
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass, ("ctf_overtime", "0"));
        Assert.True(rig.Ctf.OvertimeAllowedForCtf); // the ctor default stands until Activate

        proxy.OnServerActivate();

        Assert.False(rig.Ctf.OvertimeAllowedForCtf);
    }

    [Fact]
    public void Without_the_key_activate_pushes_the_on_default()
    {
        var rig = new ObjectiveRig();
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass);
        rig.Ctf.SetOvertimeAllowedForCtf(false);

        proxy.OnServerActivate();

        Assert.True(rig.Ctf.OvertimeAllowedForCtf);
    }

    // ---- SetStalemateOnTimelimit (teamplayroundbased_gamerules.cpp:175-185) and
    // ---- SetCTFCaptureBonusTime (tf_gamerules.cpp:1688-1694) ---------------------------

    [Fact]
    public void The_SetStalemateOnTimelimit_input_reaches_the_round_machine()
    {
        var (rig, proxy) = KothMapWithProxy();
        Assert.Null(rig.Control.StalemateOnTimelimit);

        rig.Registry.Input(proxy.Handle, "SetStalemateOnTimelimit", string.Empty, 1f, EntityHandle.None);
        Assert.True(rig.Control.StalemateOnTimelimit);

        rig.Registry.Input(proxy.Handle, "SetStalemateOnTimelimit", string.Empty, 0f, EntityHandle.None);
        Assert.False(rig.Control.StalemateOnTimelimit);
    }

    [Fact]
    public void The_SetCTFCaptureBonusTime_input_reaches_the_carrier_effects()
    {
        var rig = new ObjectiveRig();
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass);

        rig.Registry.Input(proxy.Handle, "SetCTFCaptureBonusTime", string.Empty, 30f, EntityHandle.None);

        Assert.Contains("bonus_time 30", rig.Effects.Done);
    }

    // ---- the creation (CTFGameRules::CreateStandardEntities, tf_gamerules.cpp:12067-12090) ----

    [Fact]
    public void The_proxy_is_made_and_named_at_level_init()
    {
        var rig = new ObjectiveRig();
        var handle = rig.NewHandle();
        rig.Engine.Returns("Entities.Create", handle);
        rig.Engine.WhenCalled("Entities.Spawn", call =>
            rig.Registry.Spawn((EntityHandle)call.Args[0]!, TFGameRulesProxy.MapClass));
        var log = new RecordingLog();
        IGameLifecycle spawner = new TFGameRulesProxySpawner(rig.Engine, rig.Registry.Directory, log);

        spawner.OnLevelInit("koth_viaduct");

        Assert.Equal(TFGameRulesProxy.MapClass, Assert.Single(rig.Engine.AllOf("Entities.Create")).ArgAs<string>(0));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Spawn"));
        var proxy = Assert.IsType<TFGameRulesProxy>(rig.Registry.Get(handle));
        Assert.Equal("tf_gamerules", proxy.Name);
        // The name is the directory's — what the maps' outputs resolve against.
        Assert.Equal("tf_gamerules", rig.Registry.Directory.NameOf(handle));
        Assert.Contains("tf_gamerules up", log.Text);
    }
}
