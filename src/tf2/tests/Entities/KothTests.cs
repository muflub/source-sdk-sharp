using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// The koth logic (2.6): the <c>tf_logic_koth</c> entity's round work — it
/// builds the two team hold-the-point timers at <c>RoundSpawn</c> and locks
/// the point at <c>RoundActivate</c> — and the win gate that lets a team win
/// only when its timer has run out under its own feet.
/// </summary>
public sealed class KothTests
{
    /// <summary>
    /// A one-point koth map with the logic on it, its round started: the
    /// engine's timer creations are armed to attach real
    /// <see cref="TeamRoundTimer"/>s to the rig's registry as they happen
    /// (stock's <c>CBaseEntity::Create</c>, answered by the managed registry).
    /// </summary>
    private static (ObjectiveRig Rig, KothLogic Logic, TeamControlPoint Point, TeamControlPointMaster Master)
        KothMap(params (string Key, string Value)[] logicKeys)
    {
        var rig = new ObjectiveRig();
        var master = rig.Master();
        var point = rig.Point("cp_koth", 0);
        rig.ArmKothTimerSpawns();
        var logic = rig.SpawnKothLogic(logicKeys);
        rig.StartRound();
        return (rig, logic, point, master);
    }

    private static void ArmKothTimerSpawns(ObjectiveRig rig)
    {
        var created = new Queue<EntityHandle>();
        rig.Engine.ReturnsEachCall("Entities.Create", () =>
        {
            var handle = rig.NewHandle();
            created.Enqueue(handle);
            return handle;
        });
        rig.Engine.WhenCalled("Entities.Spawn", call =>
            rig.Registry.Spawn((EntityHandle)call.Args[0]!, RoundTimers.ClassName));
    }

    // ---- the koth flag ---------------------------------------------------------------

    [Fact]
    public void The_koth_flag_is_the_koth_logic_entity_standing()
    {
        var rig = new ObjectiveRig();
        Assert.False(rig.Koth.InKothMode);

        rig.SpawnKothLogic();

        Assert.True(rig.Koth.InKothMode);
    }

    // ---- the team timers (InputRoundSpawn, tf_gamerules.cpp:19880-19914) ---------------

    [Fact]
    public void RoundSpawn_creates_the_two_named_koth_team_timers()
    {
        var (rig, _, _, _) = KothMap();

        var blue = rig.Koth.KothTeamTimer(TFTeam.Blue);
        var red = rig.Koth.KothTeamTimer(TFTeam.Red);

        Assert.NotNull(blue);
        Assert.NotNull(red);
        Assert.NotSame(blue, red);
        Assert.Equal("zz_blue_koth_timer", blue!.Name);
        Assert.Equal("zz_red_koth_timer", red!.Name);
        Assert.Same(blue, rig.Entities.FindByName<TeamRoundTimer>("zz_blue_koth_timer"));
        Assert.Same(red, rig.Entities.FindByName<TeamRoundTimer>("zz_red_koth_timer"));
    }

    [Fact]
    public void The_koth_team_timers_show_in_the_HUD()
    {
        var (rig, _, _, _) = KothMap();

        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Blue)!.ShowInHud);
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Red)!.ShowInHud);
    }

    [Fact]
    public void The_koth_team_timers_start_paused_at_the_logic_s_initial_time()
    {
        var (rig, _, _, _) = KothMap(("timer_length", "120"));

        var blue = rig.Koth.KothTeamTimer(TFTeam.Blue)!;
        var red = rig.Koth.KothTeamTimer(TFTeam.Red)!;

        Assert.True(blue.IsPaused);
        Assert.True(red.IsPaused);
        Assert.Equal(120f, blue.TimeRemaining);
        Assert.Equal(120f, red.TimeRemaining);
    }

    // ---- the point's lock (InputRoundActivate, tf_gamerules.cpp:19919-19944) -----------

    [Fact]
    public void RoundActivate_locks_the_point_and_unlocks_it_after_the_delay()
    {
        var (rig, _, point, _) = KothMap(("unlock_point", "30"));

        // The stock arm feeds SetLocked 30 and SetUnlockTime 30 to every point
        // of the first master: the round opens with the point unreachable.
        Assert.True(point.IsLocked);

        rig.Clock.Now = 29f;
        point.UnlockThink();
        Assert.True(point.IsLocked);

        rig.Clock.Now = 30.5f;
        point.UnlockThink();
        Assert.False(point.IsLocked);
    }

    [Fact]
    public void An_unlock_point_of_zero_leaves_the_point_open()
    {
        // Stock still feeds SetLocked with the (zero) delay and skips
        // SetUnlockTime: (int)0 > 0 is false, so the point is never locked.
        var (rig, _, point, _) = KothMap(("unlock_point", "0"));

        Assert.False(point.IsLocked);
    }

    // ---- the win gate (CheckWinConditions, team_control_point_master.cpp:679-700) -------

    [Fact]
    public void The_koth_win_is_suppressed_while_the_holder_s_timer_is_running()
    {
        var (rig, _, point, master) = KothMap();

        // Red holds the one point; its timer still reads the initial 180.
        point.ForceOwner((int)TFTeam.Red);

        master.CheckWinConditions();

        // Stock: the koth arm sees GetTimeRemaining() > 0 and keeps the round going.
        Assert.Empty(rig.Control.Wins);
        Assert.Equal(0, master.OnWonByTeam1.FireCount);
    }

    [Fact]
    public void A_koth_team_whose_timer_reaches_zero_while_holding_wins()
    {
        var (rig, _, point, master) = KothMap();

        point.ForceOwner((int)TFTeam.Red);
        rig.Koth.KothTeamTimer(TFTeam.Red)!.SetTimeRemaining(0);

        master.CheckWinConditions();

        Assert.Single(rig.Control.Wins);
        Assert.Equal(TFTeam.Red, rig.Control.Wins[0].Team);
        Assert.Equal(1, master.OnWonByTeam1.FireCount);
    }

    // ---- the waiting-for-players cap gate (tf_gamerules.cpp:16781-16782) ----------------

    [Fact]
    public void A_koth_map_captures_nothing_while_waiting_for_players()
    {
        // unlock_point 0: the point stands open, so only the WFP arm can stop it.
        var (rig, _, _, _) = KothMap(("unlock_point", "0"));

        // The one point needs no previous point and is open: normally cappable.
        Assert.True(rig.Rules.TeamMayCapturePoint((int)TFTeam.Red, 0));

        rig.Round.InWaitingForPlayers = true;
        Assert.False(rig.Rules.TeamMayCapturePoint((int)TFTeam.Red, 0));
    }

    [Fact]
    public void Waiting_for_players_gates_captures_on_a_cp_map_only_through_the_round_machine()
    {
        // Same rig, no koth logic: TeamMayCapturePoint still says yes; only
        // PointsMayBeCaptured (the shared gate) says no, as in non-koth stock.
        var rig = new ObjectiveRig();
        rig.Master();
        rig.Point("cp_plain", 0);
        rig.Round.InWaitingForPlayers = true;

        Assert.True(rig.Rules.TeamMayCapturePoint((int)TFTeam.Red, 0));
        Assert.False(rig.Rules.PointsMayBeCaptured);
    }

    // ---- the capture-run timers (the stock maps' capture outputs) -------------------------

    /// <summary>
    /// koth_viaduct's capture I/O, walked for real: its trigger_capture_area
    /// carries <c>"OnCapTeam1" "tf_gamerules,SetRedKothClockActive,,0,-1"</c>,
    /// so a completed red cap reaches the rules proxy's input and the stock
    /// clock arm runs red's timer and waits blue's (tf_gamerules.cpp:1666-1683).
    /// </summary>
    [Fact]
    public void The_capture_area_output_runs_the_holders_clock_and_waits_the_others()
    {
        // The map's entities all stand before the round starts, as on load:
        // the area takes its Activate with the rest.
        var rig = new ObjectiveRig();
        rig.Master();
        var point = rig.Point("cp_koth", 0);
        rig.ArmKothTimerSpawns();
        rig.SpawnKothLogic(("unlock_point", "0"));
        rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass, ("targetname", "tf_gamerules"));
        var area = rig.Area("cp_koth", 4f);
        rig.StartRound();

        // The map's own connection, spelled as koth_viaduct writes it.
        rig.Registry.Directory.Connect(area.Handle, "OnCapTeam1", "tf_gamerules,SetRedKothClockActive,,0,-1");

        // Both timers stand paused from RoundSpawn; red takes the point.
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Red)!.IsPaused);
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));
        for (float t = 0.5f; t <= 7.5f; t += 0.5f)
            rig.ThinkAt(area, t);
        rig.ThinkAt(area, 8f);
        rig.Registry.Directory.ServiceIo(rig.Clock.Now);

        Assert.Equal((int)TFTeam.Red, point.Owner);
        Assert.Equal(1, area.OnCapTeam1.FireCount);
        Assert.False(rig.Koth.KothTeamTimer(TFTeam.Red)!.IsPaused);
        Assert.True(rig.Koth.KothTeamTimer(TFTeam.Blue)!.IsPaused);
    }

    // ---- the Set/Add timer inputs (tf_gamerules.cpp:19949-20000) ------------------------

    [Fact]
    public void The_SetRedTimer_input_sets_the_holder_s_time()
    {
        var (rig, logic, _, _) = KothMap();

        rig.Registry.Input(logic.Handle, "SetRedTimer", string.Empty, 45f, EntityHandle.None);

        Assert.Equal(45f, rig.Koth.KothTeamTimer(TFTeam.Red)!.TimeRemaining);
    }

    [Fact]
    public void The_AddBlueTimer_input_adds_to_the_holder_s_time()
    {
        var (rig, logic, _, _) = KothMap(("timer_length", "100"));

        rig.Registry.Input(logic.Handle, "AddBlueTimer", string.Empty, 30f, EntityHandle.None);

        Assert.Equal(130f, rig.Koth.KothTeamTimer(TFTeam.Blue)!.TimeRemaining);
    }
}
