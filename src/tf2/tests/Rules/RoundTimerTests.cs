using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// <c>team_round_timer</c> (stage2 2.1) and <c>game_round_win</c>, spawned
/// through a real entity registry over a recording engine, the clock moved by
/// hand: the countdown, its inputs and outputs, the setup period, overtime,
/// the HUD election and the networked state.
/// </summary>
public sealed class RoundTimerTests
{
    /// <summary>The round machine as a timer sees it: every answer settable, every call counted.</summary>
    private sealed class FakeHost : IRoundTimerHost
    {
        public RoundState State { get; set; } = RoundState.RoundRunning;
        public bool InWaitingForPlayers { get; set; }
        public bool InSetup { get; private set; }
        public float MapResetTime { get; set; }
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
        public bool InOvertime { get; private set; }
        public bool MayExpire { get; set; } = true;
        public int SetupCalls { get; private set; }

        public void SetSetup(bool setup)
        {
            InSetup = setup;
            SetupCalls++;
        }

        public void SetOvertime(bool overtime) => InOvertime = overtime;

        public bool TimerMayExpire() => MayExpire;
    }

    private sealed class FakeControl : ITeamRoundControl
    {
        public readonly List<string> Calls = new();

        public void SetWinningTeam(TFTeam team, WinReason reason, bool forceMapReset = true, bool switchTeams = false,
                                   bool dontAddScore = false, bool final = false)
            => Calls.Add($"win {team} {reason} reset={forceMapReset} switch={switchTeams}");

        public void SetStalemate(StalemateReason reason, bool forceMapReset = true, bool switchTeams = false)
            => Calls.Add($"stalemate {reason} reset={forceMapReset} switch={switchTeams}");

        public void SetStalemateOnTimelimit(bool stalemate)
            => Calls.Add($"stalemate_on_timelimit {stalemate}");

        public void SetSwitchTeams(bool switchTeams) => Calls.Add($"switch_teams {switchTeams}");

        public bool ShouldSwitchTeams() => LastFlag("switch_teams");

        public void SetScrambleTeams(bool scrambleTeams) => Calls.Add($"scramble_teams {scrambleTeams}");

        public bool ShouldScrambleTeams() => LastFlag("scramble_teams");

        public void ShouldResetScores(bool teamScores, bool playerScores)
            => Calls.Add($"reset_scores {teamScores} {playerScores}");

        public void ShouldResetRoundsPlayed(bool roundsPlayed)
            => Calls.Add($"reset_rounds {roundsPlayed}");

        // The latest write answers, so a False after a True clears.
        private bool LastFlag(string prefix)
            => Calls.LastOrDefault(c => c.StartsWith(prefix, StringComparison.Ordinal)) == $"{prefix} True";
    }

    /// <summary>The convar registry's backend: declared defaults held, writes kept.</summary>
    private sealed class NagConVarBackend : IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;
        public string Get(int id) => _values[id];
        public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = RecordingEngine.Default();
        public readonly FakeHost Host = new();
        public readonly FakeControl Control = new();
        public readonly ScriptedEntityRegistry Registry;
        public readonly RoundTimers Timers;
        public readonly ConVarRegistry Convars;
        private float _now = 100f;
        private uint _next = 0x0003_0001;

        public Rig(int seed = 1234)
        {
            Engine.Strict = false;
            Engine.Returns("GameEvents.EventCreate", new IntPtr(0x55));
            Engine.Returns("Entities.GetIndex", 9);
            Engine.ReturnsEachCall("Clock.Now", () => _now);

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(TeamRoundTimer).Assembly);

            var services = new ServiceContainer();
            foreach (var (contract, _) in EngineServices.Areas(new SourceSharp.Abi.RecordingLog()))
                services.AddInstance(contract, Engine);
            services.AddInstance<IGameClock>(Engine);
            services.AddInstance<ILog>(new SourceSharp.Abi.RecordingLog());
            services.AddInstance(new ConsoleLines());
            services.AddInstance<IUserMessageSender>(Engine);
            services.AddInstance<IEntityDirectory>(Registry);
            Timers = new RoundTimers(Registry);
            services.AddInstance<IRoundTimers>(Timers);
            services.AddInstance<IRoundTimerHost>(Host);
            services.AddInstance<ITeamRoundControl>(Control);
            Convars = new ConVarRegistry(new NagConVarBackend());
            Convars.RegisterTypes([typeof(TFOvertimeNagConVar)]);
            Convars.PublishTo(services);
            // The overtime nag's coin (TeamRoundFlow's seeded-Random way; the
            // server registers its own seed source, ServerModule.cs:891).
            services.AddInstance<IRandomSeeds>(new RandomSeeds(seed));
            Registry.Services = services;
        }

        public float Now
        {
            get => _now;
            set => _now = value;
        }

        public TeamRoundTimer Timer(params (string Key, string Value)[] keys)
        {
            var handle = new EntityHandle(_next++);
            foreach (var (key, value) in keys)
                Registry.KeyValue(handle, RoundTimers.ClassName, key, value);
            Registry.Spawn(handle, RoundTimers.ClassName);
            return (TeamRoundTimer)Registry.Get(handle)!;
        }

        public GameRoundWin RoundWin(params (string Key, string Value)[] keys)
        {
            var handle = new EntityHandle(_next++);
            foreach (var (key, value) in keys)
                Registry.KeyValue(handle, GameRoundWin.Classname, key, value);
            Registry.Spawn(handle, GameRoundWin.Classname);
            return (GameRoundWin)Registry.Get(handle)!;
        }

        public void Input(ScriptedEntity entity, string name, string value = "")
        {
            float number = float.TryParse(value, System.Globalization.NumberStyles.Float,
                                          System.Globalization.CultureInfo.InvariantCulture, out float n) ? n : 0f;
            Assert.True(Registry.Input(entity.Handle, name, value, number, EntityHandle.None), $"{name} was not taken");
        }

        /// <summary>Runs the timer's think every 0.05 s for <paramref name="seconds"/>.</summary>
        public void Run(ScriptedEntity timer, float seconds)
        {
            float end = _now + seconds;
            while (_now < end)
            {
                _now += TeamRoundTimer.ThinkInterval;
                Registry.Think(timer.Handle);
            }
        }
    }

    [Fact]
    public void A_spawned_timer_is_paused_at_its_length_and_runs_when_resumed()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "300"));

        rig.Run(timer, 10f);
        Assert.True(timer.IsPaused);
        Assert.Equal(300f, timer.TimeRemaining);

        rig.Input(timer, "Resume");
        rig.Run(timer, 10f);
        Assert.InRange(timer.TimeRemaining, 289.9f, 290.1f);
    }

    [Fact]
    public void AddTime_then_Pause_freezes_the_remaining_time()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"), ("start_paused", "0"));
        rig.Input(timer, "Resume");
        rig.Run(timer, 5f);

        rig.Input(timer, "AddTime", "30");
        rig.Input(timer, "Pause");
        float frozen = timer.TimeRemaining;
        rig.Run(timer, 20f);

        Assert.InRange(frozen, 124.9f, 125.1f);
        Assert.Equal(frozen, timer.TimeRemaining);
        Assert.True(timer.State.TimerPaused);
        Assert.Equal(frozen, timer.State.TimeRemaining);
    }

    [Fact]
    public void AddTime_is_refused_outside_a_running_round_and_capped_at_the_max_length()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"), ("max_length", "110"));

        rig.Host.State = RoundState.Preround;
        rig.Input(timer, "AddTime", "30");
        Assert.Equal(100f, timer.TimeRemaining);

        rig.Host.State = RoundState.RoundRunning;
        rig.Input(timer, "AddTime", "30");
        Assert.Equal(110f, timer.TimeRemaining);
    }

    [Fact]
    public void OnFinished_fires_once()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "3"));
        rig.Input(timer, "Resume");

        rig.Run(timer, 10f);

        Assert.Equal(1, timer.OnFinished.FireCount);
        Assert.Equal(0f, timer.TimeRemaining);
    }

    [Fact]
    public void A_veto_holds_the_timer_at_zero_in_overtime_and_lets_it_finish_after()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "2"), ("show_in_hud", "1"));
        rig.Input(timer, "Resume");
        rig.Host.MayExpire = false;

        rig.Run(timer, 5f);
        Assert.Equal(0, timer.OnFinished.FireCount);
        Assert.True(rig.Host.InOvertime);

        rig.Host.MayExpire = true;
        rig.Run(timer, 0.1f);
        Assert.Equal(1, timer.OnFinished.FireCount);
    }

    [Fact]
    public void The_minute_and_second_outputs_fire_on_the_way_down()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "65"));
        rig.Input(timer, "Resume");

        rig.Run(timer, 61f);

        Assert.Equal(1, timer.On1MinRemain.FireCount);
        Assert.Equal(1, timer.On30SecRemain.FireCount);
        Assert.Equal(1, timer.On10SecRemain.FireCount);
        Assert.Equal(1, timer.On5SecRemain.FireCount);
        Assert.Equal(0, timer.On1SecRemain.FireCount);   // 4 s still to go
        Assert.Equal(0, timer.On2MinRemain.FireCount);   // never had two minutes
    }

    [Fact]
    public void A_setup_period_counts_down_first_then_the_round_length_and_tells_the_machine()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "600"), ("setup_length", "5"));
        Assert.True(rig.Host.InSetup);
        Assert.True(timer.InSetupPeriod);
        Assert.Equal(5f, timer.TimeRemaining);

        rig.Input(timer, "Resume");
        rig.Run(timer, 6f);

        Assert.Equal(1, timer.OnSetupFinished.FireCount);
        Assert.False(rig.Host.InSetup);
        Assert.False(timer.InSetupPeriod);
        Assert.InRange(timer.TimeRemaining, 598f, 600f);
    }

    [Fact]
    public void The_round_win_pauses_a_running_timer()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"));
        rig.Input(timer, "Resume");

        rig.Host.State = RoundState.TeamWin;
        rig.Run(timer, 5f);

        Assert.True(timer.IsPaused);
        Assert.InRange(timer.TimeRemaining, 99.9f, 100.01f);
    }

    [Fact]
    public void RoundSpawn_restarts_the_setup_and_runs_unless_the_map_starts_it_paused()
    {
        var rig = new Rig();
        var paused = rig.Timer(("timer_length", "100"));
        var running = rig.Timer(("timer_length", "100"), ("start_paused", "0"), ("setup_length", "10"));

        paused.OnRoundSpawn();
        running.OnRoundSpawn();

        Assert.True(paused.IsPaused);
        Assert.False(running.IsPaused);
        Assert.Equal(10f, running.TimeRemaining);
        Assert.True(running.InSetupPeriod);
    }

    [Fact]
    public void RoundSpawn_waits_out_the_waiting_for_players_period()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"), ("start_paused", "0"));
        rig.Host.InWaitingForPlayers = true;

        timer.OnRoundSpawn();

        Assert.True(timer.IsPaused);
    }

    [Fact]
    public void A_disabled_timer_ignores_its_inputs_until_enabled()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"), ("StartDisabled", "1"));

        rig.Input(timer, "Resume");
        rig.Run(timer, 5f);
        Assert.Equal(100f, timer.TimeRemaining);
        Assert.True(timer.IsDisabled);

        rig.Input(timer, "Enable");
        rig.Run(timer, 5f);
        Assert.InRange(timer.TimeRemaining, 94.9f, 95.1f);
    }

    [Fact]
    public void Showing_a_timer_in_the_HUD_takes_the_HUD_from_the_other()
    {
        var rig = new Rig();
        var first = rig.Timer(("timer_length", "100"), ("show_in_hud", "1"));
        var second = rig.Timer(("timer_length", "200"));
        rig.Timers.SetActive(first);
        Assert.Same(first, rig.Timers.Active);

        rig.Input(second, "ShowInHUD", "1");

        Assert.Same(second, rig.Timers.Active);
        Assert.False(first.ShowInHud);
        Assert.True(second.State.ShowInHud);
    }

    [Fact]
    public void The_network_state_carries_the_stock_fields()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "300"), ("max_length", "600"), ("setup_length", "60"),
                              ("show_in_hud", "1"), ("auto_countdown", "0"));

        Assert.Equal(300, timer.State.TimerInitialLength);
        Assert.Equal(600, timer.State.TimerMaxLength);
        Assert.Equal(60, timer.State.SetupTimeLength);
        Assert.Equal(TeamRoundTimerNetState.StateSetup, timer.State.State);
        Assert.True(timer.State.ShowInHud);
        Assert.False(timer.State.AutoCountdown);
        Assert.True(timer.State.StartPaused);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "EntityRender.SetTransmitAlways" && Equals(c.Args[1], true));
    }

    // ---- the round's inputs (CoreRoundWorld) ------------------------------------------

    private sealed class RecordingControlPointRules : TF2.Server.Entities.IControlPointRules
    {
        public int Setups { get; private set; }
        public bool PointsMayBeCaptured => true;
        public bool TeamMayCapturePoint(int team, int pointIndex) => true;
        public int GetFarthestOwnedControlPoint(int team, bool withSpawnpoints) => -1;
        public void RecalculateControlPointState() { }
        public void SetupControlPointSpawns() => Setups++;
        public void SetupSpawnPointsForRound() { }
        public bool ControlSpawnsFor(int team, int pointIndex) => false;
    }

    /// <summary>A map cleanup these facts never ask: they are about the round's inputs.</summary>
    private sealed class NoMapCleanup : IMapCleanup
    {
        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter) => default;
    }

    private static CoreRoundWorld World(Rig rig, RecordingControlPointRules points)
    {
        var services = new ServiceContainer();
        services.AddInstance<TF2.Server.Entities.IControlPointRules>(points);
        return new CoreRoundWorld(new NoMapCleanup(), rig.Registry.Directory, rig.Registry, rig.Engine, rig.Timers,
                                  services, new SourceSharp.Abi.RecordingLog());
    }

    [Fact]
    public void The_round_start_sends_RoundSpawn_as_an_input_to_every_entity_that_takes_it()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"), ("setup_length", "10"), ("start_paused", "0"));
        rig.RoundWin(("TeamNum", "2"));   // takes no RoundSpawn
        rig.Run(timer, 0.1f);
        rig.Input(timer, "Resume");
        rig.Run(timer, 3f);
        Assert.InRange(timer.TimeRemaining, 6.8f, 7.2f);

        int took = World(rig, new RecordingControlPointRules()).RoundSpawn();

        Assert.Equal(1, took);
        Assert.Equal(10f, timer.TimeRemaining);   // the setup period again
    }

    [Fact]
    public void The_round_start_hands_the_control_points_their_spawn_setup()
    {
        var rig = new Rig();
        var points = new RecordingControlPointRules();

        World(rig, points).SetupControlPointSpawns();

        Assert.Equal(1, points.Setups);
    }

    // ---- the rules' own HUD timers (CoreRoundWorld) -----------------------------------

    /// <summary>
    /// The engine answers <c>Entities.Create</c> with <paramref name="handle"/>
    /// and the managed half is bound at it already (the native spawn callback
    /// the engine's spawn makes).
    /// </summary>
    private static TeamRoundTimer AnswerCreateWith(Rig rig, EntityHandle handle)
    {
        rig.Engine.Returns("Entities.Create", handle);
        rig.Registry.Spawn(handle, RoundTimers.ClassName);
        return (TeamRoundTimer)rig.Registry.Get(handle)!;
    }

    private static int Creates(Rig rig) => rig.Engine.AllOf("Entities.Create").Count();

    [Fact]
    public void The_waiting_timer_is_created_named_and_shown_running_for_the_seconds_given()
    {
        var rig = new Rig();
        var timer = AnswerCreateWith(rig, new EntityHandle(0x0050_0001u));

        // The SetName + show_in_hud + SetTime + Resume + Enable run
        // (teamplayroundbased_gamerules.cpp:1014-1020), true: it made one.
        Assert.True(World(rig, new RecordingControlPointRules()).EnsureWaitingTimer(30f));
        Assert.Equal("zz_teamplay_waiting_timer", rig.Registry.Directory.NameOf(timer.Handle));
        Assert.True(timer.ShowInHud);
        Assert.Equal(30f, timer.TimeRemaining);
        Assert.False(timer.IsPaused);
        Assert.False(timer.IsDisabled);
        Assert.Same(timer, rig.Timers.Active);
    }

    [Fact]
    public void The_waiting_timer_is_created_once_and_kept()
    {
        var rig = new Rig();
        var timer = AnswerCreateWith(rig, new EntityHandle(0x0050_0001u));
        var world = World(rig, new RecordingControlPointRules());

        Assert.True(world.EnsureWaitingTimer(30f));
        Assert.False(world.EnsureWaitingTimer(20f));

        // A live timer stands: no second create, and the time it runs is not touched.
        Assert.Equal(1, Creates(rig));
        Assert.Equal(30f, timer.TimeRemaining);
    }

    [Fact]
    public void The_waiting_timers_creation_hides_the_active_HUD_timer_and_shows_itself()
    {
        var rig = new Rig();
        var active = rig.Timer(("timer_length", "100"), ("show_in_hud", "1"));
        rig.Timers.SetActive(active);
        var waiting = AnswerCreateWith(rig, new EntityHandle(0x0050_0002u));

        // Stock's create arm (teamplayroundbased_gamerules.cpp:1007-1010): the
        // hide precedes the show, or it takes the waiting timer down with it
        // and the HUD never sees the clock (live, ctf_2fort: showInHud False).
        Assert.True(World(rig, new RecordingControlPointRules()).EnsureWaitingTimer(30f));

        Assert.True(waiting.ShowInHud);
        Assert.False(active.ShowInHud);
        Assert.Same(waiting, rig.Timers.Active);
    }

    [Fact]
    public void The_waiting_timer_ask_hides_nothing_once_its_timer_is_live()
    {
        var rig = new Rig();
        var waiting = AnswerCreateWith(rig, new EntityHandle(0x0050_0002u));
        var world = World(rig, new RecordingControlPointRules());
        Assert.True(world.EnsureWaitingTimer(30f));

        // A later timer shown in the HUD stands: only the creation hides
        // (stock's hide sits inside the `if ( !m_hWaitingForPlayersTimer )` arm).
        var other = rig.Timer(("timer_length", "100"), ("show_in_hud", "1"));
        rig.Timers.SetActive(other);
        Assert.False(world.EnsureWaitingTimer(20f));

        Assert.True(other.ShowInHud);
    }

    /// <summary>Stock's cancel arm (teamplayroundbased_gamerules.cpp:962-969):
    /// the hidden HUD timer the wait's creation left (their :1010 HideActive)
    /// resumes when it is not meant to start paused, and counts again.</summary>
    [Fact]
    public void The_cancel_resumes_a_hidden_HUD_timer_the_map_does_not_start_paused()
    {
        var rig = new Rig();
        var hud = rig.Timer(("timer_length", "100"), ("show_in_hud", "1"), ("start_paused", "0"));
        rig.Timers.SetActive(hud);
        rig.Timers.HideActive();   // the waiting timer's creation hid it (:1010)

        rig.Timers.ResumeHiddenIfRunning();

        Assert.False(hud.IsPaused);
        rig.Run(hud, 3f);
        Assert.InRange(hud.TimeRemaining, 96.9f, 97.1f);   // counting again: three seconds gone
    }

    /// <summary>The same arm's paused half (:965): a start_paused timer keeps
    /// its pause -- the resume asks the map's key first.</summary>
    [Fact]
    public void A_hidden_start_paused_timer_keeps_its_pause_through_the_cancel_resume()
    {
        var rig = new Rig();
        var hud = rig.Timer(("timer_length", "100"), ("show_in_hud", "1"));   // start_paused defaults to true
        rig.Timers.SetActive(hud);
        rig.Timers.HideActive();

        rig.Timers.ResumeHiddenIfRunning();

        Assert.True(hud.IsPaused);
        rig.Run(hud, 3f);
        Assert.Equal(100f, hud.TimeRemaining);   // the pause holds its seconds
    }

    [Fact]
    public void A_retreating_wait_asks_SetTime_of_the_timer_it_already_has()
    {
        var rig = new Rig();
        var timer = AnswerCreateWith(rig, new EntityHandle(0x0050_0001u));
        var world = World(rig, new RecordingControlPointRules());
        world.EnsureWaitingTimer(30f);

        world.RetuneWaitingTimer(45f);

        Assert.Equal(1, Creates(rig));   // the retune makes no second timer
        Assert.Equal(45f, timer.TimeRemaining);
    }

    [Fact]
    public void The_waiting_timer_removal_takes_the_entity_once()
    {
        var rig = new Rig();
        var handle = new EntityHandle(0x0050_0001u);
        AnswerCreateWith(rig, handle);
        var world = World(rig, new RecordingControlPointRules());
        world.EnsureWaitingTimer(30f);

        world.RemoveWaitingTimer();
        Assert.Single(rig.Engine.AllOf("Entities.Remove"));

        // The engine has done it (the shell's unbind): a second ask removes
        // nothing — stock's `if ( m_hWaitingForPlayersTimer )` (:866-869).
        rig.Registry.Removed(handle);
        world.RemoveWaitingTimer();
        Assert.Single(rig.Engine.AllOf("Entities.Remove"));
    }

    [Fact]
    public void The_stalemate_timer_is_shown_running_for_the_seconds_given()
    {
        var rig = new Rig();
        var timer = AnswerCreateWith(rig, new EntityHandle(0x0050_0002u));

        World(rig, new RecordingControlPointRules()).SpawnStalemateTimer(60);

        // The show_in_hud + SetTime + Resume + Enable run (:2037-2042).
        Assert.True(timer.ShowInHud);
        Assert.Equal(60f, timer.TimeRemaining);
        Assert.Same(timer, rig.Timers.Active);
    }

    [Fact]
    public void The_stalemate_timer_is_reused_across_sudden_deaths()
    {
        var rig = new Rig();
        var timer = AnswerCreateWith(rig, new EntityHandle(0x0050_0002u));
        var world = World(rig, new RecordingControlPointRules());

        world.SpawnStalemateTimer(60);
        world.SpawnStalemateTimer(90);

        // Stock keeps m_hStalemateTimer and only SetTimes it again (:2033-2042).
        Assert.Equal(1, Creates(rig));
        Assert.Equal(90f, timer.TimeRemaining);
    }

    [Fact]
    public void The_time_limit_timer_is_created_named_shown_and_shown_again_on_reuse()
    {
        var rig = new Rig();
        var timer = AnswerCreateWith(rig, new EntityHandle(0x0050_0003u));
        var world = World(rig, new RecordingControlPointRules());

        world.SpawnTimeLimitTimer(300);
        Assert.Equal("zz_teamplay_timelimit_timer", rig.Registry.Directory.NameOf(timer.Handle));
        Assert.True(timer.ShowInHud);
        Assert.Equal(300f, timer.TimeRemaining);

        // A map reset took the entity; the next spawn makes a new one.
        rig.Registry.Removed(timer.Handle);
        var fresh = AnswerCreateWith(rig, new EntityHandle(0x0050_0004u));
        world.SpawnTimeLimitTimer(240);

        Assert.Equal(2, Creates(rig));
        Assert.Equal("zz_teamplay_timelimit_timer", rig.Registry.Directory.NameOf(fresh.Handle));
        Assert.Equal(240f, fresh.TimeRemaining);
    }

    [Fact]
    public void A_HUD_timer_that_is_not_the_time_limit_one_means_the_map_has_an_active_timer()
    {
        var rig = new Rig();
        var mapTimer = rig.Timer(("timer_length", "600"), ("show_in_hud", "1"));

        Assert.True(World(rig, new RecordingControlPointRules()).MapHasActiveTimer());
        Assert.True(mapTimer.ShowInHud);
    }

    [Fact]
    public void The_time_limit_timer_itself_is_no_maps_active_timer()
    {
        var rig = new Rig();
        rig.Timer(("timer_length", "600"), ("show_in_hud", "1"), ("targetname", "zz_teamplay_timelimit_timer"));

        Assert.False(World(rig, new RecordingControlPointRules()).MapHasActiveTimer());
    }

    [Fact]
    public void A_timer_that_is_not_shown_in_the_HUD_is_no_active_timer()
    {
        var rig = new Rig();
        rig.Timer(("timer_length", "600"));

        Assert.False(World(rig, new RecordingControlPointRules()).MapHasActiveTimer());
    }

    // ---- the time-added announcer (teamplay_round_timer.cpp:1280-1319) ------

    /// <summary>A fired game event: its name and the keys it carried.</summary>
    private sealed record FiredEvent(string Name, Dictionary<string, string> Keys);

    /// <summary>Every event the timer created and fired, in order, with its keys.</summary>
    private static List<FiredEvent> Fired(Rig rig)
    {
        var done = new List<FiredEvent>();
        (string Name, Dictionary<string, string> Keys)? open = null;
        foreach (var c in rig.Engine.Calls)
        {
            switch (c.Member)
            {
                case "GameEvents.EventCreate":
                    open = (c.ArgAs<string>(0)!, new Dictionary<string, string>());
                    break;
                case "GameEvents.EventSetInt" when open is not null:
                    open.Value.Keys[c.ArgAs<string>(1)!] = System.Convert.ToString(c.Args[2])!;
                    break;
                case "GameEvents.EventSetString" when open is not null:
                    open.Value.Keys[c.ArgAs<string>(1)!] = c.ArgAs<string>(2)!;
                    break;
                case "GameEvents.EventFire" when open is not null:
                    done.Add(new FiredEvent(open.Value.Name, open.Value.Keys));
                    open = null;
                    break;
            }
        }
        return done;
    }

    /// <summary>The broadcast-audio events fired: (team, sound) pairs.</summary>
    private static List<(string Team, string Sound)> Broadcasts(Rig rig)
        => Fired(rig).Where(e => e.Name == TFMessages.EventBroadcastAudio)
                     .Select(e => (e.Keys["team"], e.Keys["sound"])).ToList();

    /// <summary>A running, elected HUD timer, the clock moved to just before the round's end.</summary>
    private static TeamRoundTimer AnnouncingTimer(Rig rig)
    {
        var timer = rig.Timer(("timer_length", "100"), ("start_paused", "0"));
        rig.Input(timer, "ShowInHUD", "1");   // elected, as OnEnable would be
        rig.Run(timer, 5f);
        return timer;
    }

    [Fact]
    public void Time_added_with_nobody_responsible_announces_the_addition_and_fires_the_event()
    {
        var rig = new Rig();
        var timer = AnnouncingTimer(rig);

        rig.Input(timer, "AddTime", "30");

        // stock :1301-1305: the plain sound to everyone, team 0 = unassigned.
        Assert.Equal([("0", "Announcer.TimeAdded")], Broadcasts(rig));

        var timeAdded = Assert.Single(Fired(rig), e => e.Name == TFMessages.EventTimerTimeAdded);
        Assert.Equal("9", timeAdded.Keys["timer"]);
        Assert.Equal("30", timeAdded.Keys["seconds_added"]);
    }

    [Fact]
    public void The_awarded_team_hears_the_award_and_the_other_the_loss()
    {
        var rig = new Rig();
        var timer = AnnouncingTimer(rig);

        rig.Input(timer, "AddTeamTime", "2 30");   // "<team> <seconds>"

        Assert.Equal([("2", "Announcer.TimeAwardedForTeam"),
                      ("3", "Announcer.TimeAddedForEnemy")], Broadcasts(rig));
        Assert.Contains(Fired(rig), e => e.Name == TFMessages.EventTimerTimeAdded);
    }

    [Fact]
    public void A_won_round_still_gets_its_time_and_event_but_no_announcement()
    {
        var rig = new Rig();
        var timer = AnnouncingTimer(rig);
        rig.Host.State = RoundState.TeamWin;

        rig.Input(timer, "AddTeamTime", "2 30");

        // stock :1282 RoundHasBeenWon: no sounds once the round is won, but
        // the event under :1308 still runs.
        Assert.Empty(Broadcasts(rig));
        Assert.Contains(Fired(rig), e => e.Name == TFMessages.EventTimerTimeAdded);
    }

    [Fact]
    public void A_timer_the_HUD_does_not_show_announces_nothing_and_fires_no_event()
    {
        var rig = new Rig();
        var timer = rig.Timer(("timer_length", "100"), ("start_paused", "0"));   // not elected, not shown
        rig.Run(timer, 5f);

        rig.Input(timer, "AddTime", "30");

        Assert.Empty(Broadcasts(rig));
        Assert.DoesNotContain(Fired(rig), e => e.Name == TFMessages.EventTimerTimeAdded);
    }

    // ---- the overtime nag (teamplay_round_timer.cpp:1005-1022) ------------------

    /// <summary>The fired <c>overtime_nag</c> events: one per coin that came up heads.</summary>
    private static List<FiredEvent> Nags(Rig rig)
        => Fired(rig).Where(e => e.Name == TFMessages.EventOvertimeNag).ToList();

    /// <summary>A held timer thinking on zero: overtime, with <c>tf_overtime_nag</c> asked for.</summary>
    private static TeamRoundTimer OvertimeTimer(Rig rig)
    {
        var timer = rig.Timer(("timer_length", "2"), ("show_in_hud", "1"));
        rig.Input(timer, "Resume");
        rig.Host.MayExpire = false;
        return timer;
    }

    [Fact]
    public void Overtime_nags_while_the_coin_lands_heads()
    {
        var rig = new Rig(seed: 2);   // the timer's first two draws are heads
        rig.Convars.Declared<TFOvertimeNagConVar>().SetValue(true);
        var timer = OvertimeTimer(rig);

        rig.Run(timer, 4f);   // 2 s down, 2 s at zero: two ~1 s-spaced draws, both heads

        Assert.True(rig.Host.InOvertime);
        Assert.Equal(2, Nags(rig).Count);
    }

    [Fact]
    public void A_tails_coin_says_nothing()
    {
        var rig = new Rig(seed: 5);   // the first draw is tails
        rig.Convars.Declared<TFOvertimeNagConVar>().SetValue(true);
        var timer = OvertimeTimer(rig);

        rig.Run(timer, 2.9f);   // 2 s down, 0.9 s at zero: the one draw inside is the tails one

        Assert.True(rig.Host.InOvertime);
        Assert.Empty(Nags(rig));
    }

    [Fact]
    public void The_convar_off_says_nothing()
    {
        var rig = new Rig(seed: 2);   // the coin would have spoken twice
        var timer = OvertimeTimer(rig);

        rig.Run(timer, 4f);

        Assert.True(rig.Host.InOvertime);   // overtime still happens
        Assert.Empty(Nags(rig));            // but the nag's switch stands
    }

    [Fact]
    public void The_nag_fires_once_a_second_not_once_a_think()
    {
        var rig = new Rig(seed: 2);   // draws heads, heads, tails, heads
        rig.Convars.Declared<TFOvertimeNagConVar>().SetValue(true);
        var timer = OvertimeTimer(rig);

        rig.Run(timer, 6f);   // four ~1 s-spaced draws; three of them heads

        // Three nags in six seconds at zero, not one every 0.05 s think: the
        // second stamp is spent before the next draw.
        Assert.Equal(3, Nags(rig).Count);
    }

    // ---- game_round_win ------------------------------------------------------------

    [Fact]
    public void A_team_s_game_round_win_wins_the_round_with_its_reason()
    {
        var rig = new Rig();
        var win = rig.RoundWin(("TeamNum", "3"), ("force_map_reset", "1"), ("switch_teams", "1"));

        rig.Input(win, "RoundWin");

        Assert.Equal(new[] { "win Blue DefendUntilTimeLimit reset=True switch=True" }, rig.Control.Calls);
        Assert.Equal(1, win.OnRoundWin.FireCount);
    }

    [Fact]
    public void A_teamless_game_round_win_is_a_timer_stalemate()
    {
        var rig = new Rig();
        var win = rig.RoundWin(("force_map_reset", "1"));

        rig.Input(win, "RoundWin");

        Assert.Equal(new[] { "stalemate Timer reset=True switch=False" }, rig.Control.Calls);
    }

    [Fact]
    public void A_no_reset_game_round_win_never_switches_teams()
    {
        var rig = new Rig();
        var win = rig.RoundWin(("TeamNum", "2"), ("switch_teams", "1"), ("win_reason", "1"));

        rig.Input(win, "RoundWin");

        Assert.Equal(new[] { "win Red AllPointsCaptured reset=False switch=False" }, rig.Control.Calls);
    }
}
