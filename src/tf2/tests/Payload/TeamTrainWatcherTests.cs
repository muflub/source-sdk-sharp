using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Payload;

/// <summary>
/// CTeamTrainWatcher's behaviours (team_train_watcher.cpp): the capper-count
/// speed ladder through the map's SetNumTrainCappers input, the recede after
/// tf_escort_recede_time, the timer veto, the progress pass, the alarm, the
/// payload_pushed publish and the sparks that burn while the cart backs up.
/// </summary>
public sealed class TeamTrainWatcherTests
{
    /// <summary>A straight track n1→n2→n3, a 400 u/s train on it and a watcher driving it, all round-activated.</summary>
    internal static (PayloadRig Rig, TeamTrainWatcher Watcher, FuncTrackTrain Train, PathTrack N1, PathTrack N2, PathTrack N3)
        Track()
    {
        var rig = new PayloadRig();
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(500f, 0f, 0f));
        var n3 = rig.Node("n3", new Vector(1000f, 0f, 0f));
        n1.LinkTo(n2);
        n2.LinkTo(n3);

        var train = rig.Train("cart");
        var watcher = rig.Watcher("cart", "n1", "n3");

        rig.StartRound();
        train.StartOn(n1);

        return (rig, watcher, train, n1, n2, n3);
    }

    [Fact]
    public void Capper_counts_drive_the_speed_ladder_through_the_maps_input()
    {
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        Assert.Equal(TeamTrainWatcher.SpeedOneCapper * train.MaxSpeed, train.CurrentSpeed);

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "3", 3f, area.Handle);
        Assert.Equal(TeamTrainWatcher.SpeedThreeCappers * train.MaxSpeed, train.CurrentSpeed);

        // the input recovers stock's pCaller: the activator IS the area, and its
        // blocked state is what stops the recede arming
        Assert.Equal(area.Handle, watcher.AreaCap!.Handle);
    }

    [Fact]
    public void The_watcher_binds_the_capture_area_parented_to_its_train()
    {
        // team_train_watcher.cpp:826-872: on activation a train-driving watcher
        // finds the trigger_area_capture parented to its train and hands it
        // itself, so the area's capper count reaches the watcher without a map
        // input. The area's own think drives the ladder here.
        var (rig, watcher, train, _, _, _) = Track();
        _ = rig.Point("cp_a", 0);
        var area = rig.Area("cp_a");
        rig.StartRound();

        rig.ParentOf = train.Handle;
        watcher.WatcherActivate();
        rig.ParentOf = EntityHandle.None;

        var blue = rig.Players.Add(1, TFTeam.Blue);
        rig.Registry.Touch(area.Handle, blue);
        rig.ThinkAt(area, 0.5f);

        // the count arrived through the parentage binding: the ladder answered it
        Assert.Equal(TeamTrainWatcher.SpeedOneCapper * train.MaxSpeed, train.CurrentSpeed);
    }

    [Fact]
    public void The_progress_node_is_the_end_of_the_leg_even_when_the_cart_halts()
    {
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        train.FinishStep(); // the leg to n2 completes; the cart is on its way to n3

        // the cart halts mid-leg (a blocked cap): it stands between n2 and n3.
        // Stock's progress node is m_ppath->GetNext() — the end of the leg the
        // cart is on — so a halted cart halfway to n3 is three quarters along.
        rig.EngineOrigin = new Vector(750f, 0f, 0f);
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "-1", -1f, area.Handle);
        rig.ThinkAt(watcher, 0.1f);

        Assert.Equal(0.75f, rig.Mode.Progress[(int)TFTeam.Blue], 2);
        Assert.Equal(750f, watcher.GetTrainDistanceAlongTrack(), 1);
    }

    [Fact]
    public void Every_hill_on_the_path_reaches_the_objective_table()
    {
        var rig = new PayloadRig();
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(100f, 0f, 0f));
        var n3 = rig.Node("n3", new Vector(200f, 0f, 0f));
        var n4 = rig.Node("n4", new Vector(300f, 0f, 0f));
        var n5 = rig.Node("n5", new Vector(400f, 0f, 0f));
        var n6 = rig.Node("n6", new Vector(500f, 0f, 0f));
        var n7 = rig.Node("n7", new Vector(600f, 0f, 0f));
        n1.LinkTo(n2);
        n2.LinkTo(n3);
        n3.LinkTo(n4);
        n4.LinkTo(n5);
        n5.LinkTo(n6);
        n6.LinkTo(n7);

        // path_track's uphill bit (0x20) on every second node, answered per
        // handle: the recording engine logs a call before it asks the factory,
        // so the last call names the node asking (PhysicsEventRouterTests's
        // shape).
        HashSet<EntityHandle> uphill = [n2.Handle, n4.Handle, n6.Handle];
        rig.Engine.ReturnsEachCall("Entities.GetSpawnFlags", () =>
            uphill.Contains((EntityHandle)rig.Engine.Calls[^1].Args[0]!) ? 32 : 0);

        var train = rig.Train("cart");
        var watcher = rig.Watcher("cart", "n1", "n7");

        rig.StartRound();

        // three uphill stretches: every one of them reaches the table as a
        // (start, end) pair. Stock's guard is the objective resource's whole
        // array — TEAM_TRAIN_HILLS_ARRAY_SIZE - 1 — not the hill count.
        Assert.Equal(3, rig.Mode.Hills.Count);
        Assert.Equal(100f / 600f + 0.005f, rig.Mode.Hills[0].Start, 3);
        Assert.Equal(500f / 600f + 0.005f, rig.Mode.Hills[2].Start, 3);
        Assert.All(rig.Mode.Hills, hill => Assert.False(hill.Downhill));
        _ = train;
    }

    [Fact]
    public void A_position_no_segment_bounds_reports_the_worst_distance()
    {
        var (rig, watcher, _, _, _, _) = Track();

        // behind the start node: no segment holds the projection. Stock seeds
        // the walk with FLT_MAX, so the answer reads as "ahead of any train".
        watcher.ProjectPointOntoPath(new Vector(-100f, 0f, 0f), out _, out float along);
        Assert.Equal(float.MaxValue, along);
        Assert.True(watcher.IsAheadOfTrain(new Vector(-100f, 0f, 0f)));
    }

    [Fact]
    public void A_blocked_cart_stalls_and_holds_the_round_timer()
    {
        var (rig, watcher, train, _, _, _) = Track();
        var point = rig.Point("cp_a", 0);
        var area = rig.Area("cp_a");
        rig.StartRound(); // the point and the area join the round that already ran
        var blue = rig.Players.Add(1, TFTeam.Blue);
        var red = rig.Players.Add(2, TFTeam.Red);

        // both teams in the zone: the area blocks, as the capture facts read it
        rig.Registry.Touch(area.Handle, blue);
        rig.ThinkAt(area, 0.5f);
        rig.Registry.Touch(area.Handle, red);
        rig.ThinkAt(area, 1f);
        Assert.True(area.IsBlocked, "the area must actually block for this fact to mean anything");

        // a rolling cart is driven to a stop by the blocked report, and the
        // block holds the round timer
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "-1", -1f, area.Handle);
        Assert.Equal(0f, train.CurrentSpeed);
        Assert.True(watcher.TimerMayExpire() is false, "a blocked cart's round does not expire under the watcher");

        // the block arms no recede: nothing fires at the window's end (the
        // clock stands at 1 s from the area's last think, so the 30 s window
        // closes at 31)
        rig.ThinkAt(watcher, 31.1f);
        Assert.Equal(0, watcher.OnTrainStartRecede.FireCount);
    }

    [Fact]
    public void The_disabled_watcher_lets_the_timer_expire()
    {
        var rig = new PayloadRig();
        var watcher = rig.Spawn<TeamTrainWatcher>(TeamTrainWatcher.MapClass, ("start_disabled", "1"));
        rig.StartRound();

        Assert.True(watcher.IsDisabled);
        Assert.True(watcher.TimerMayExpire());
    }

    [Fact]
    public void The_cart_recedes_after_tf_escort_recede_time()
    {
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        train.FinishStep(); // the cart is on its way to n2 when the last capper steps off
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        Assert.True(watcher.TimerMayExpire() is false, "waiting to recede holds the round timer");

        // the convar's default is 30
        rig.ThinkAt(watcher, 29.9f);
        Assert.Equal(0, watcher.OnTrainStartRecede.FireCount);
        Assert.Equal(0f, train.CurrentSpeed);

        rig.ThinkAt(watcher, 30.1f);
        Assert.Equal(1, watcher.OnTrainStartRecede.FireCount);
        Assert.Equal(TeamTrainWatcher.RecedeSpeed * train.MaxSpeed, train.CurrentSpeed);

        // a capper returns: the recede cancels and the round may expire
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        Assert.True(watcher.TimerMayExpire());
    }

    [Fact]
    public void Overtime_shortens_the_window_but_never_recedes()
    {
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");
        rig.RoundRules.InOvertime = true;

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        train.FinishStep();
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);

        rig.ThinkAt(watcher, 4.9f);
        Assert.Equal(0, watcher.OnTrainStartRecede.FireCount);

        // overtime never recedes: the shorter window passes, the wait clears
        // and the round timer runs again, but the cart is not driven back
        rig.ThinkAt(watcher, 5.1f);
        Assert.Equal(0, watcher.OnTrainStartRecede.FireCount);
        Assert.True(watcher.TimerMayExpire());
        Assert.Equal(0f, train.CurrentSpeed);
    }

    [Fact]
    public void Progress_moves_with_the_train_and_checkpoint_links_report_their_distance()
    {
        var (rig, _, train, _, _, _) = Track();
        var cp = rig.Point("cp_mid", 0);
        var linked = rig.Watcher("cart", "n1", "n3", ("linked_pathtrack_1", "n2"), ("linked_cp_1", "cp_mid"));
        linked.WatcherActivate();

        // the train stands at n2, heading for n3: half of the 1000-unit track
        train.FinishStep();
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(linked, 0.1f);

        Assert.Equal(0.5f, rig.Mode.Progress[(int)TFTeam.Blue], 2);
        Assert.Equal(0.5f, linked.GetTrainProgress(), 2);
        Assert.Equal(500f, linked.GetTrainDistanceAlongTrack(), 1);

        // n2's link names its distance from the start, as the HUD's checkpoint reads it
        Assert.Equal(0.5f, rig.Mode.PathDistance[cp.PointIndex], 2);
    }

    [Fact]
    public void The_think_publishes_the_carts_cappers_recede_deadline_and_speed_pip()
    {
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "2", 2f, area.Handle);
        Assert.Equal(2, watcher.GetCapturerCount());
        train.FinishStep();
        rig.ThinkAt(watcher, 0.1f);
        Assert.Equal(2, rig.Mode.Cappers[(int)TFTeam.Blue]);

        // the cappers leave: the recede arms, and its deadline is the HUD's countdown
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        rig.ThinkAt(watcher, 0.3f);
        Assert.Equal(0, rig.Mode.Cappers[(int)TFTeam.Blue]);
        Assert.Equal(0.1f + watcher.GetRecedeTotalTime(), rig.Mode.RecedeTime[(int)TFTeam.Blue], 2);

        // the window passes: the cart backs up, and the pip reads backwards
        rig.ThinkAt(watcher, 0.1f + watcher.GetRecedeTotalTime() + 0.1f);
        Assert.Equal(-1, rig.Mode.SpeedLevel[(int)TFTeam.Blue]);
    }

    [Fact]
    public void The_alarm_plays_at_the_final_checkpoint_and_stops_when_the_cart_pulls_away()
    {
        var (rig, _, train, _, _, _) = Track();
        var cp = rig.Point("cp_final", 0);
        var watcher = rig.Watcher("cart", "n1", "n3", ("linked_pathtrack_1", "n3"), ("linked_cp_1", "cp_final"));
        watcher.WatcherActivate();

        // the train stands at n2 heading for n3, 500 units from the goal: no alarm
        train.FinishStep();
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.1f);
        Assert.False(rig.Mode.TrackAlarm.GetValueOrDefault((int)TFTeam.Blue));
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Sound.SoundPatchCreate");
        Assert.Empty(rig.Ramps.Calls);

        // inside the alarm distance of the goal: the loop starts at the final CP
        rig.EngineOrigin = new Vector(950f, 0f, 0f);
        rig.ThinkAt(watcher, 0.2f);
        Assert.True(rig.Mode.TrackAlarm[(int)TFTeam.Blue]);
        var patch = rig.Engine.Calls.Single(c => c.Member == "Sound.SoundPatchCreate");
        Assert.Equal(TeamTrainWatcher.AlarmSound, patch.ArgAs<string>(2));
        Assert.Contains(rig.Ramps.Calls, c => c.StartsWith("play "));

        // the single-shot re-arm names the last linked point
        watcher.OnThink(TeamTrainWatcher.AlarmThinkContext);
        var single = rig.Engine.Calls.Last(c => c.Member == "Sound.EntityEmitSound");
        Assert.Equal(TeamTrainWatcher.AlarmSingleSound, single.ArgAs<string>(1));

        // pulled away: the alarm stops and the patch is freed
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.3f);
        Assert.False(rig.Mode.TrackAlarm[(int)TFTeam.Blue]);
        Assert.Contains(rig.Ramps.Calls, c => c.StartsWith("destroy "));
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Sound.SoundPatchFree");
    }

    [Fact]
    public void The_speed_ladder_follows_the_maps_bands()
    {
        var (rig, _, train, _, _, _) = Track();
        // the bands a payload map names; stock's m_iTrainSpeedLevel for the HUD's pips
        var watcher = rig.Watcher("cart", "n1", "n3",
            ("hud_min_speed_level_1", "100"), ("hud_min_speed_level_2", "200"), ("hud_min_speed_level_3", "300"));
        watcher.WatcherActivate();

        // 120 u/s: over the first band only
        train.SetSpeedDirAccel(0.3f);
        rig.ThinkAt(watcher, 0.1f);
        Assert.Equal(1, watcher.TrainSpeedLevel);

        // 40 u/s: under every band
        train.SetSpeedDirAccel(0.1f);
        rig.ThinkAt(watcher, 0.2f);
        Assert.Equal(0, watcher.TrainSpeedLevel);

        // 240 u/s: the second band
        train.SetSpeedDirAccel(0.6f);
        rig.ThinkAt(watcher, 0.3f);
        Assert.Equal(2, watcher.TrainSpeedLevel);

        // 380 u/s: the top band
        train.SetSpeedDirAccel(0.95f);
        rig.ThinkAt(watcher, 0.4f);
        Assert.Equal(3, watcher.TrainSpeedLevel);
    }

    [Fact]
    public void The_checkpoint_warning_rearms_only_past_twice_the_alert_distance()
    {
        var rig = new PayloadRig();
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(2000f, 0f, 0f));
        var n3 = rig.Node("n3", new Vector(3000f, 0f, 0f));
        n1.LinkTo(n2);
        n2.LinkTo(n3);
        _ = rig.Point("cp_mid", 0);
        var train = rig.Train("cart");
        var watcher = rig.Watcher("cart", "n1", "n3", ("linked_pathtrack_1", "n2"), ("linked_cp_1", "cp_mid"));
        rig.StartRound();
        train.StartOn(n1);
        train.FinishStep(); // the cart is on the leg to n3

        // 500 from the checkpoint: inside the alert band, the warning arms once
        rig.EngineOrigin = new Vector(1500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.1f);
        Assert.True(watcher.AlertPlayed(0), "inside the alert band the warning arms");

        // a pull-back that is less than twice the alert distance keeps it armed
        rig.EngineOrigin = new Vector(1450f, 0f, 0f);
        rig.ThinkAt(watcher, 0.2f);
        Assert.True(watcher.AlertPlayed(0), "a small pull-back does not reset the warning");

        // back past twice the alert distance: the flag resets
        rig.EngineOrigin = new Vector(300f, 0f, 0f);
        rig.ThinkAt(watcher, 0.3f);
        Assert.False(watcher.AlertPlayed(0), "past twice the alert distance the warning re-arms");

        // and the next approach arms it again
        rig.EngineOrigin = new Vector(1500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.4f);
        Assert.True(watcher.AlertPlayed(0));
    }

    [Fact]
    public void The_veto_service_holds_the_round_timer_for_a_waiting_watcher()
    {
        var services = new ServiceContainer();
        services.AddInstance<ILog>(new RecordingLog());
        var rig = new PayloadRig();
        services.AddInstance<IEntityDirectory>(rig.Registry);

        var train = rig.Train("cart");
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(500f, 0f, 0f));
        n1.LinkTo(n2);
        var watcher = rig.Watcher("cart", "n1", "n2");
        var area = rig.Area("cp_a");

        // the veto reaches the round machine only through the side's
        // registration: the scan claims the [SideService] mark, the registrar
        // names the chain, and the machine reads IHooks<ITimerExpiryVeto>
        SideServices.Register(services, Side.Server,
            [typeof(TrainWatcherTimerVeto), typeof(TeamRoundRegistrar)]);

        var veto = Assert.Single(services.ResolveAll<ITimerExpiryVeto>());
        Assert.IsType<TrainWatcherTimerVeto>(veto);
        Assert.InRange(((IOrderedHook)veto).HookOrder, 999_600, 999_699);
        Assert.True(veto.TimerMayExpire(), "no watcher has vetoed yet");

        rig.StartRound();
        train.StartOn(n1);
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        Assert.True(veto.TimerMayExpire() is false, "the waiting watcher vetoes through the service");

        // the chain the round machine's timer reads carries the same one
        var chain = services.Resolve<IHooks<ITimerExpiryVeto>>()!;
        Assert.Same(veto, Assert.Single(chain.All));
    }

    [Fact]
    public void A_watcher_activated_before_the_tracks_first_think_finds_the_path()
    {
        // a round restart after a cleanup: the map's blocks are re-created and
        // activated with no think in between, and the round fires RoundActivate
        // on the re-created watcher before the re-created tracks have had one.
        // Stock links a path_track in its Activate, so the chain is joined
        // before any RoundActivate can run.
        var rig = new PayloadRig();
        rig.Node("n1", Vector.Zero, ("target", "n2"));
        rig.Node("n2", new Vector(500f, 0f, 0f), ("target", "n3"));
        rig.Node("n3", new Vector(1000f, 0f, 0f));
        var cp = rig.Point("cp_mid", 0);
        var train = rig.Train("cart");
        var watcher = rig.Watcher("cart", "n1", "n3", ("linked_pathtrack_1", "n2"), ("linked_cp_1", "cp_mid"));

        rig.StartRound();

        // the walk found the track: the checkpoint's distance from the start
        // is on the objective table, where stock asserts m_flTotalPathDistance
        // is never 0
        rig.ThinkAt(watcher, 0.1f);
        Assert.Equal(0.5f, rig.Mode.PathDistance[cp.PointIndex], 2);

        _ = train;
    }

    [Fact]
    public void A_pushed_capper_is_published_once_per_foot_the_cart_advances()
    {
        // team_train_watcher.cpp:1247-1282: the watcher drains its distance
        // accumulator one foot (16 units) at a time — a single if, not a while —
        // and per drained foot fires payload_pushed once per touching player,
        // server-only, with the capper's UserID and a distance of 1.
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");
        var blue = rig.Players.Add(1, TFTeam.Blue);

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "2", 2f, area.Handle);
        rig.Registry.Touch(area.Handle, blue);
        train.FinishStep(); // the cart is on its way to n3 at 308 u/s: level 3
        rig.Engine.Returns("Players.GetUserId", 7)
                  .Returns("GameEvents.EventCreate", new IntPtr(42))
                  .Returns("GameEvents.EventFire", true);

        // the first think banks the whole 500-unit jump but drains one foot only
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.1f);
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventCreate"));

        // the next 16 units drain the next foot
        rig.EngineOrigin = new Vector(516f, 0f, 0f);
        rig.ThinkAt(watcher, 0.2f);
        Assert.Equal(2, rig.Engine.CountOf("GameEvents.EventCreate"));

        var create = rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventCreate");
        Assert.Equal("payload_pushed", create.ArgAs<string>(0));

        // the capper's UserID, taken from the engine with the touching entity
        // once per publish
        var asked = rig.Engine.Calls.Where(c => c.Member == "Players.GetUserId").ToList();
        Assert.Equal(2, asked.Count);
        Assert.All(asked, a => Assert.Equal(blue, a.ArgAs<EntityHandle>(0)));
        var pusher = rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "pusher");
        Assert.Equal(7, pusher.ArgAs<int>(2));

        var distance = rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "distance");
        Assert.Equal(1, distance.ArgAs<int>(2));

        // server-only: the fire asks the manager not to broadcast
        var fire = rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventFire");
        Assert.Equal(new IntPtr(42), fire.ArgAs<IntPtr>(0));
        Assert.True(fire.ArgAs<bool>(1));

        // a think with no forward progress publishes nothing new
        rig.ThinkAt(watcher, 0.3f);
        Assert.Equal(2, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    [Fact]
    public void A_stopped_cart_banks_the_walk_and_publishes_nothing()
    {
        // the drain's guard is the speed level, not the walk: at level 0 the
        // accumulator keeps growing (stock :1252-1255) and no event is made —
        // same setup as the publishing fact but with nobody capping.
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");
        var blue = rig.Players.Add(1, TFTeam.Blue);
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        train.FinishStep();
        rig.Registry.Touch(area.Handle, blue);
        rig.Engine.Returns("Players.GetUserId", 7)
                  .Returns("GameEvents.EventCreate", new IntPtr(42))
                  .Returns("GameEvents.EventFire", true);

        // the last capper steps off: the cart stops, the walk still banks
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.1f);

        Assert.Equal(0, watcher.TrainSpeedLevel);
        Assert.Equal(500f, watcher.GetPushedDistanceAccumulator(), 1);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
        // the stimulus was in place: the player stood in the bound area
        Assert.Equal(area.Handle, watcher.AreaCap!.Handle);
        Assert.Contains(blue, area.TouchingEntities);
    }

    [Fact]
    public void The_foot_drains_with_no_area_bound_and_publishes_nothing()
    {
        // stock drains the accumulator before it ever looks at the area
        // (:1258, then the m_hAreaCap guard at :1261): with no bound area the
        // foot is spent and no event is made. The activator is no area, so
        // InternalSetNumTrainCappers leaves m_hAreaCap null as stock's
        // inputdata.pCaller cast would.
        var (rig, watcher, train, _, _, _) = Track();
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "2", 2f, EntityHandle.None);
        train.FinishStep();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));

        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.1f);

        Assert.Null(watcher.AreaCap);
        Assert.Equal(484f, watcher.GetPushedDistanceAccumulator(), 1); // 500 less one foot
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    [Fact]
    public void Only_the_players_standing_in_the_area_are_published_to()
    {
        // stock walks the player list and asks the area (ToBaseMultiplayerPlayer
        // + IsTouching, :1263-1268); the port walks the area's occupants and
        // asks the engine which are players — the same set, filtered the other
        // way round. A path_track standing in the zone is asked about and passed.
        var (rig, watcher, train, _, _, _) = Track();
        var area = rig.Area("cp_a");
        var blue = rig.Players.Add(1, TFTeam.Blue);
        var stranger = rig.Node("stray", new Vector(500f, 100f, 0f));

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "2", 2f, area.Handle);
        rig.Registry.Touch(area.Handle, blue);
        rig.Registry.Touch(area.Handle, stranger.Handle);
        train.FinishStep();
        rig.Engine.Returns("Players.GetUserId", 7)
                  .Returns("GameEvents.EventCreate", new IntPtr(42))
                  .Returns("GameEvents.EventFire", true);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            (EntityHandle)rig.Engine.Calls[^1].Args[0]! == blue);

        int askedBefore = rig.Engine.CountOf("Entities.IsPlayer");
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        rig.ThinkAt(watcher, 0.1f);

        // both occupants were asked, one was published
        Assert.Equal(askedBefore + 2, rig.Engine.CountOf("Entities.IsPlayer"));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventCreate"));
        Assert.Equal(blue, rig.Engine.Calls.Single(c => c.Member == "Players.GetUserId").ArgAs<EntityHandle>(0));
    }

    /// <summary>
    /// A track whose watcher names the sparks under the cart, plus a spark of a
    /// different name that must keep its rest, and the recede cycle: cappers
    /// drive it forward, the last one steps off, the window passes, the cart
    /// backs up. Returns the named spark and the watcher mid-recede.
    /// </summary>
    private static (PayloadRig Rig, TeamTrainWatcher Watcher, FuncTrackTrain Train, EnvSpark Named, EnvSpark Other)
        RecedingTrackWithSparks()
    {
        var rig = new PayloadRig();
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(500f, 0f, 0f));
        var n3 = rig.Node("n3", new Vector(1000f, 0f, 0f));
        n1.LinkTo(n2);
        n2.LinkTo(n3);

        var train = rig.Train("cart");
        var watcher = rig.Watcher("cart", "n1", "n3", ("env_spark_name", "cart_sparks"));
        var named = rig.Spawn<EnvSpark>(EntityNames.EnvSpark, ("targetname", "cart_sparks"));
        var other = rig.Spawn<EnvSpark>(EntityNames.EnvSpark, ("targetname", "some_other_sparks"));

        rig.StartRound();
        train.StartOn(n1);

        var area = rig.Area("cp_a");
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        train.FinishStep();
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        rig.ThinkAt(watcher, 30.1f); // the 30 s window passes and the cart backs up

        return (rig, watcher, train, named, other);
    }

    [Fact]
    public void The_named_sparks_burn_while_the_cart_runs_backwards()
    {
        // HandleTrainMovement's speed-change arm, team_train_watcher.cpp:610-617:
        // a negative wanted speed sets the watcher's collected sparks burning,
        // and only the ones WatcherActivate collected by name (:866-871).
        var (_, _, _, named, other) = RecedingTrackWithSparks();

        Assert.True(named.IsSparking(), "the recede sets the named sparks burning");
        Assert.False(other.IsSparking(), "a spark of a different name keeps its rest");
    }

    [Fact]
    public void The_sparks_die_when_the_backing_cart_stops()
    {
        // WatcherThink :1151-1160: the level falling to zero (here from -1, the
        // cart driven to a stop by something other than the watcher's own
        // speed ladder) puts the sparks out.
        var (rig, watcher, train, named, _) = RecedingTrackWithSparks();
        Assert.True(named.IsSparking(), "the recede armed them first");

        train.SetSpeedDirAccel(0f); // an arrival, not the watcher's own decision
        rig.ThinkAt(watcher, 30.2f);

        Assert.False(named.IsSparking(), "the level falling to zero takes them out");
    }

    [Fact]
    public void A_watcher_that_does_not_drive_the_train_never_sets_the_sparks()
    {
        // HandleSparks' guard (:500): a watcher without handle_train_movement
        // neither collects nor commands the named sparks across the same
        // back-up-and-stop cycle.
        var rig = new PayloadRig();
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(500f, 0f, 0f));
        n1.LinkTo(n2);
        var train = rig.Train("cart");
        var watcher = rig.Spawn<TeamTrainWatcher>(TeamTrainWatcher.MapClass,
            ("targetname", "watcher"), ("train", "cart"), ("start_node", "n1"), ("goal_node", "n2"),
            ("env_spark_name", "cart_sparks"));
        var named = rig.Spawn<EnvSpark>(EntityNames.EnvSpark, ("targetname", "cart_sparks"));
        rig.StartRound();
        train.StartOn(n1);

        train.SetSpeedDirAccel(-0.2f);
        rig.ThinkAt(watcher, 0.1f);
        train.SetSpeedDirAccel(0f);
        rig.ThinkAt(watcher, 0.2f);

        Assert.False(named.IsSparking());
    }

    [Fact]
    public void Disabling_the_watcher_leaves_burning_sparks_burning()
    {
        // Stock sets m_bDisabled (:385) BEFORE m_Sparks.Purge () :390 and
        // HandleSparks(false) :407, whose guard reads the flag (:500): the
        // stop-call is dead code and a disabled watcher's live sparks keep
        // their timer running. Ported as stock, leak and all.
        var (rig, watcher, _, named, _) = RecedingTrackWithSparks();
        Assert.True(named.IsSparking(), "they burn first, or this fact means nothing");

        rig.Registry.Input(watcher.Handle, "Disable", string.Empty, 0f, EntityHandle.None);

        Assert.True(watcher.IsDisabled);
        Assert.True(named.IsSparking(), "the disable does not reach them — as in stock");
    }

    [Fact]
    public void An_empty_spark_name_burns_the_unnamed_sparks_as_stock_does()
    {
        // stock finds by name and an unnamed entity carries exactly the empty
        // name: a watcher with no env_spark_name collects the UNNAMED env_sparks
        // (FindEntityByName on "", :866-871). A named spark is not one of them.
        var rig = new PayloadRig();
        var n1 = rig.Node("n1", Vector.Zero);
        var n2 = rig.Node("n2", new Vector(500f, 0f, 0f));
        var n3 = rig.Node("n3", new Vector(1000f, 0f, 0f));
        n1.LinkTo(n2);
        n2.LinkTo(n3);

        var train = rig.Train("cart");
        var watcher = rig.Watcher("cart", "n1", "n3");
        var stray = rig.Spawn<EnvSpark>(EntityNames.EnvSpark);
        var named = rig.Spawn<EnvSpark>(EntityNames.EnvSpark, ("targetname", "somewhere_else"));
        rig.StartRound();
        train.StartOn(n1);

        var area = rig.Area("cp_a");
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "1", 1f, area.Handle);
        train.FinishStep();
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        rig.ThinkAt(watcher, 30.1f);

        Assert.True(stray.IsSparking(), "the unnamed spark answers the empty name");
        Assert.False(named.IsSparking());
    }
}
