extern alias client;

using SourceSharp.Abi;
using Xunit;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using Timer = client::TF2.Client.Entities.ClientTeamRoundTimer;
using TimerSource = client::TF2.Client.Entities.ITFRoundTimer;
using Timers = client::TF2.Client.Entities.TFClientRoundTimers;
using TimerState = client::TF2.Shared.Rules.TeamRoundTimerNetState;

namespace TF2.Tests.Client;

/// <summary>
/// The client half of <c>team_round_timer</c>: the decoded state and stock's
/// client arithmetic over it (<c>GetTimeRemaining</c>, <c>GetTimerMaxLength</c>,
/// teamplay_round_timer.cpp:377-430), plus the election stock's
/// <c>GetActiveRoundTimer</c> makes through the objective resource.
/// </summary>
public sealed class ClientRoundTimerTests
{
    [Fact]
    public void A_packet_decodes_the_fields_the_hud_reads()
    {
        var rig = new TimerRig();

        // Unbound, the state reads zero, which is RT_STATE_SETUP; the first
        // packet carries the server's own state.
        Assert.True(rig.Timer.InSetup);
        Assert.False(rig.Timer.ShowInHud);
        Assert.Equal(0, rig.Timers.Count);

        rig.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.ShowInHud = true;
            s.ShowTimeRemaining = true;
            s.StopWatchTimer = true;
            s.InCaptureWatchState = true;
            s.TotalTime = 42.5f;
            s.TimerEndTime = 100f;
            s.TimeRemaining = 33f;
            s.TimerPaused = true;
            s.AutoCountdown = true;
            s.TimerMaxLength = 90;
            s.TimerLength = 60;
            s.TimerInitialLength = 61;
            s.SetupTimeLength = 20;
            s.IsDisabled = true;
        });

        Assert.False(rig.Timer.InSetup);
        Assert.True(rig.Timer.ShowInHud);
        Assert.True(rig.Timer.ShowTimeRemaining);
        Assert.True(rig.Timer.IsStopWatch);
        Assert.True(rig.Timer.InCaptureWatchState);
        Assert.Equal(42.5f, rig.Timer.StopWatchTotalTime, 3);
        Assert.True(rig.Timer.IsPaused);
        Assert.True(rig.Timer.AutoCountdown);
        Assert.Equal(61, rig.Timer.InitialLength);
        Assert.Equal(20, rig.Timer.SetupLength);
        Assert.True(rig.Timer.IsDisabled);

        Assert.Equal(1, rig.Timer.Decodes);
        Assert.Equal(1, rig.Timers.Count);
        Assert.Same(rig.Timer, rig.Timers.All[0]);
    }

    [Fact]
    public void A_running_timer_counts_down_from_its_end_time()
    {
        var rig = new TimerRig();
        rig.Arrive(s => s.TimerEndTime = 100f);

        Assert.Equal(70f, rig.Timer.TimeRemaining(30f), 3);
        Assert.Equal(0f, rig.Timer.TimeRemaining(120f), 3);
    }

    [Fact]
    public void A_paused_timer_holds_its_remaining_time()
    {
        var rig = new TimerRig();
        rig.Arrive(s =>
        {
            s.TimerPaused = true;
            s.TimeRemaining = 33f;
            s.TimerEndTime = 100f;
        });

        Assert.Equal(33f, rig.Timer.TimeRemaining(90f), 3);
        Assert.Equal(33f, rig.Timer.TimeRemaining(10f), 3);
    }

    [Fact]
    public void A_stopwatch_in_a_capture_watch_shows_its_total()
    {
        var rig = new TimerRig();
        rig.Arrive(s =>
        {
            s.StopWatchTimer = true;
            s.InCaptureWatchState = true;
            s.TotalTime = 42.5f;
            s.TimerPaused = true;
            s.TimeRemaining = 33f;
            s.TimerEndTime = 100f;
        });

        Assert.Equal(42.5f, rig.Timer.TimeRemaining(90f), 3);
    }

    [Fact]
    public void The_max_length_is_the_setup_length_else_the_max_or_current_length()
    {
        var setup = new TimerRig();
        setup.Arrive(s =>
        {
            s.State = TimerState.StateSetup;
            s.SetupTimeLength = 20;
            s.TimerMaxLength = 90;
            s.TimerLength = 60;
        });
        Assert.Equal(20, setup.Timer.TimerMaxLength);

        var max = new TimerRig();
        max.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.TimerMaxLength = 90;
            s.TimerLength = 60;
        });
        Assert.Equal(90, max.Timer.TimerMaxLength);

        var current = new TimerRig();
        current.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.TimerLength = 60;
        });
        Assert.Equal(60, current.Timer.TimerMaxLength);
    }

    [Fact]
    public void The_active_timer_is_the_one_the_objective_resource_names()
    {
        var objectives = new ObjectiveRig();
        var timers = new Timers(objectives.Source);
        timers.Track(new FakeTimer(index: 7, showInHud: true, disabled: false));
        timers.Track(new FakeTimer(index: 42, showInHud: false, disabled: false));

        // No index published: the fallback election picks the HUD timer.
        Assert.Equal(7, timers.Active!.Index);

        objectives.Arrive(s => s.TimerToShowInHud = 42);

        // Stock's answer wins even though this one is not a HUD timer.
        Assert.Equal(42, timers.Active!.Index);
    }

    [Fact]
    public void No_timer_is_active_before_one_is_shown_in_the_hud()
    {
        var timers = new Timers(new ObjectiveRig().Source);
        Assert.Null(timers.Active);

        timers.Track(new FakeTimer(index: 1, showInHud: false, disabled: false));
        timers.Track(new FakeTimer(index: 2, showInHud: true, disabled: true));
        Assert.Null(timers.Active);
    }

    [Fact]
    public void The_fallback_is_the_first_enabled_hud_timer()
    {
        var timers = new Timers(new ObjectiveRig().Source);
        timers.Track(new FakeTimer(index: 1, showInHud: false, disabled: false));
        timers.Track(new FakeTimer(index: 2, showInHud: true, disabled: true));
        timers.Track(new FakeTimer(index: 3, showInHud: true, disabled: false));

        Assert.Equal(3, timers.Active!.Index);
    }

    [Fact]
    public void A_timer_is_live_from_its_first_update_to_its_removal()
    {
        var timers = new Timers(new ObjectiveRig().Source);
        var timer = new Timer(Engine.Default(), timers);
        timer.State.BindLocal();

        timer.OnSpawn();
        Assert.Equal(1, timers.Count);

        timer.OnDataChanged(default, DataUpdateType.Created);
        Assert.Equal(1, timers.Count);

        timer.OnRemoved();
        Assert.Equal(0, timers.Count);
        Assert.Null(timers.Active);
    }

    /// <summary>One timer over a bound source state, delivered as the registry delivers it.</summary>
    private sealed class TimerRig
    {
        public readonly Timers Timers;

        public readonly Timer Timer;

        public TimerRig(ObjectiveRig? objectives = null)
        {
            Timers = new Timers((objectives ?? new ObjectiveRig()).Source);
            Timer = new Timer(Engine.Default(), Timers);
            Timer.State.BindLocal();
        }

        public void Arrive(Action<TimerState>? write = null)
        {
            var state = new TimerState();
            state.BindLocal();
            write?.Invoke(state);

            byte[] blob = state.Snapshot();
            Timer.State.Decode(blob);
            Timer.OnDataChanged(blob, DataUpdateType.DataTableChanged);
        }
    }

    /// <summary>A timer with the fields the election reads, so a fact can choose its index.</summary>
    private sealed class FakeTimer(int index, bool showInHud, bool disabled) : TimerSource
    {
        public EntityHandle Handle => default;

        public int Index => index;

        public float TimeRemaining(float now) => 0f;

        public bool IsPaused => false;

        public bool InSetup => false;

        public bool ShowInHud => showInHud;

        public bool IsDisabled => disabled;

        public int TimerMaxLength => 0;

        public bool ShowTimeRemaining => false;

        public bool IsStopWatch => false;

        public bool InCaptureWatchState => false;

        public float StopWatchTotalTime => 0f;

        public bool AutoCountdown => false;

        public int InitialLength => 0;

        public int SetupLength => 0;
    }
}
