extern alias client;

using Xunit;

using ClientAbi = client::SourceSharp.Abi;
using AbiGlobals = SourceSharp.Abi.Globals;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using Carriers = client::TF2.Client.Player.TFClientCarriers;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientEffects = client::TF2.Client.Weapons.TFClientWeaponEffects;
using EntityHandle = SourceSharp.Abi.EntityHandle;
using NetStateCatalog = client::SourceSharp.Abi.NetStateCatalog;
using PlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using Prediction = client::TF2.Client.Player.TFPlayerPrediction;
using Cond = client::TF2.Shared.TFCond;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using TFClass = client::TF2.Shared.TFClass;
using CloakConsumeVar = client::TF2.Shared.TFSpyCloakConsumeRateConVar;
using CloakRegenVar = client::TF2.Shared.TFSpyCloakRegenRateConVar;
using InvisTimeVar = client::TF2.Shared.TFSpyInvisTimeConVar;
using InvisUnstealthVar = client::TF2.Shared.TFSpyInvisUnstealthTimeConVar;
using Views = client::TF2.Shared.Player.TFPlayerNetStateViews;
using ClientShared = client::SourceSharp.Shared;

namespace TF2.Tests;

/// <summary>
/// The client half of the <c>tf_player_data</c> carrier: the local player's
/// working copy, reset to the decoded state once a frame, saved after each
/// replayed command, and judged against the server's word when it arrives
/// (ClientArsenal's pattern, over <c>IClientPredictedState</c>).
/// </summary>
public sealed class TFPlayerPredictionTests
{
    private const int Local = 1;
    private const int Other = 2;

    /// <summary>The tick the replay's clock steps in, written into the stubbed globals.</summary>
    private const float Tick = PredictionDeps.Tick;

    private static readonly EntityHandle LocalPlayer = new(0x0001_0000u | Local);
    private static readonly EntityHandle OtherPlayer = new(0x0001_0000u | Other);

    /// <summary>One globals block for the whole run: the interval is a constant of the fixture.</summary>
    private static nint GlobalsBlock => PredictionDeps.SharedGlobals();

    /// <summary>The spy convars over the stub, bound as the client's registry binds them.</summary>
    private static ClientAbi.ConVarRegistry SpyConVars() => PredictionDeps.SpyConVars();

    private sealed class Frame
    {
        public readonly ClientLog Log = new();
        public readonly ClientEngine Engine = new();
        public readonly Carriers Carriers = new();
        public readonly PlayerData Carrier;
        public readonly PlayerData OtherCarrier;
        public readonly Prediction Prediction;

        public Frame()
        {
            Engine.Returns("Environment.Globals", new AbiGlobals(GlobalsBlock));
            var convars = SpyConVars();

            Carrier = Data(Carriers, Local);
            OtherCarrier = Data(Carriers, Other);
            Prediction = new Prediction(Log, Carriers, NetStateCatalog.Of(typeof(PlayerData).Assembly, Log),
                new NoStandalones(), Engine,
                convars.Declared<CloakConsumeVar>(), convars.Declared<CloakRegenVar>(),
                convars.Declared<InvisTimeVar>(), convars.Declared<InvisUnstealthVar>());
        }

        /// <summary>The clock of the window's k-th command: the anchor plus the place in it.</summary>
        public float Now(int command) => command * Tick;

        /// <summary>The server's word arrives: the decoded state changes and counts a decode.</summary>
        public void Arrive(Action<PlayerData> write)
        {
            write(Carrier);
            Carrier.OnDataChanged(default, SourceSharp.Abi.DataUpdateType.DataTableChanged);
        }

        public client::TF2.Shared.Player.TFPlayerNetState Working => Prediction.Predicted(Local)!;
    }

    private static PlayerData Data(Carriers carriers, int owner)
    {
        var data = new PlayerData(new ClientEngine(), carriers, new NoStandalones(), new client::TF2.Client.Player.TFClientPlayerResources(),
                                  new ClientEngine(), new client::TF2.Shared.TFClassTable(new ClientLog(), new ClientEngine()),
                                  new client::TF2.Client.Player.TFClientConditionDriver(NoHooks()), new ClientLog());
        data.State.BindLocal();
        data.State.OwnerIndex = owner;
        data.OnSpawn();
        return data;
    }

    /// <summary>No client entities at all: the skin decision finds no body.</summary>
    private static client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook> NoHooks() =>
        new NoConditionHooks();

    private sealed class NoConditionHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }

    private sealed class NoStandalones : client::SourceSharp.Client.IClientStandalones
    {
        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];
        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    [Fact]
    public void Nothing_is_predicted_before_the_local_carrier_arrives()
    {
        var log = new ClientLog();
        var convars = SpyConVars();
        var prediction = new Prediction(log, new Carriers(), NetStateCatalog.Of(typeof(PlayerData).Assembly, log),
            new NoStandalones(), EngineWithGlobals(),
            convars.Declared<CloakConsumeVar>(), convars.Declared<CloakRegenVar>(),
            convars.Declared<InvisTimeVar>(), convars.Declared<InvisUnstealthVar>());

        prediction.ResetBasis(LocalPlayer, 10);

        Assert.Null(prediction.Predicted(Local));
    }

    [Fact]
    public void The_reset_hands_the_rules_a_copy_of_the_decoded_state_for_the_local_player_only()
    {
        var frame = new Frame();
        Views.SetClass(frame.Carrier.State, TFClass.Scout);
        Views.SetClass(frame.OtherCarrier.State, TFClass.HeavyWeapons);

        frame.Prediction.ResetBasis(LocalPlayer, 10);

        Assert.Equal(TFClass.Scout, Views.Class(frame.Working));
        Assert.NotSame(frame.Carrier.State, frame.Working);
        Assert.Null(frame.Prediction.Predicted(Other));
        Assert.Equal(TFClass.HeavyWeapons, Views.Class(frame.Prediction.Networked(Other)!));
    }

    [Fact]
    public void A_replayed_write_saved_under_its_command_holds_against_a_matching_arrival()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Working.AirDash = 1;
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        frame.Arrive(c => c.State.AirDash = 1);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var counts = frame.Prediction.Counts;
        Assert.Equal((1, 1, 0), (counts.Saves, counts.Holds, counts.Mispredicts));
        Assert.Equal(1, frame.Working.AirDash);
    }

    [Fact]
    public void A_mispredicted_field_is_judged_and_named()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Working.AirDash = 1;
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        frame.Arrive(c => c.State.AirDash = 0);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        Assert.Equal(1, frame.Prediction.Counts.Mispredicts);
        Assert.Contains("AirDash", frame.Prediction.Report());
        Assert.Contains("MISPREDICTION at command 11", frame.Log.Text);
    }

    [Fact]
    public void A_tolerated_timer_difference_holds()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Working.LastCritCheckTime = 5.0f;
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        frame.Arrive(c => c.State.LastCritCheckTime = 5.0005f);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var counts = frame.Prediction.Counts;
        Assert.Equal((1, 1, 0), (counts.Holds, counts.Tolerated, counts.Mispredicts));
    }

    [Fact]
    public void The_sent_not_predicted_crit_multiplier_is_never_a_misprediction()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        frame.Arrive(c => c.State.CritMult = 200);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var counts = frame.Prediction.Counts;
        Assert.Equal((1, 0), (counts.Holds, counts.Mispredicts));
        Assert.Equal(200, frame.Working.CritMult);
    }

    [Fact]
    public void Each_frame_replays_from_the_decoded_state_not_from_the_last_frames_prediction()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Working.AirDash = 1;
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        // The next frame, no new packet: the same command is re-predicted from the same start.
        frame.Prediction.ResetBasis(LocalPlayer, 10);

        Assert.Equal(0, frame.Working.AirDash);
        Assert.Equal(0, frame.Prediction.Counts.Holds + frame.Prediction.Counts.Mispredicts);
    }

    [Fact]
    public void A_write_outside_a_replayed_command_corrupts_nothing()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Working.AirDash = 1;
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        // After the frame's last command: the native movement forwarder runs
        // the rules over the working copy, outside the replay.
        frame.Working.AirDash = 7;

        Assert.Equal(0, frame.Carrier.State.AirDash);          // the decoded state is the server's
        frame.Arrive(c => c.State.AirDash = 1);
        frame.Prediction.ResetBasis(LocalPlayer, 11);
        Assert.Equal(0, frame.Prediction.Counts.Mispredicts);   // command 11's slot kept what it ran
        Assert.Equal(1, frame.Working.AirDash);                 // and the next replay starts clean
    }

    [Fact]
    public void A_carrier_counts_from_its_first_update_to_its_removal()
    {
        var carriers = new Carriers();
        var data = new PlayerData(new ClientEngine(), carriers, new NoStandalones(), new client::TF2.Client.Player.TFClientPlayerResources(),
                                  new ClientEngine(), new client::TF2.Shared.TFClassTable(new ClientLog(), new ClientEngine()),
                                  new client::TF2.Client.Player.TFClientConditionDriver(NoHooks()), new ClientLog());
        data.State.BindLocal();
        data.State.OwnerIndex = Local;

        data.OnDataChanged(default, SourceSharp.Abi.DataUpdateType.Created);
        data.OnDataChanged(default, SourceSharp.Abi.DataUpdateType.DataTableChanged);
        Assert.Equal(1, carriers.Count);
        Assert.Same(data, carriers.Of(Local));

        data.OnRemoved();
        Assert.Equal(0, carriers.Count);
        Assert.Null(carriers.Of(Local));
    }

    [Fact]
    public void A_class_change_in_the_arrival_is_news_not_a_misprediction()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        // The server's word names a new class: its spawn rewrote the carrier
        // outside any command (stock's ChangeTeam → Spawn — the class, a full
        // watch), and no replay can have run it, so this command's saved run
        // is news. The next frame replays from the new class all the same.
        frame.Arrive(c =>
        {
            Views.SetClass(c.State, TFClass.Engineer);
            c.State.CloakMeter = 100f;
        });
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var counts = frame.Prediction.Counts;
        Assert.Equal((0, 1), (counts.Mispredicts, counts.ClassNews));
        Assert.Equal(TFClass.Engineer, Views.Class(frame.Working));
        Assert.Equal(100f, frame.Working.CloakMeter);
    }

    [Fact]
    public void A_condition_the_server_expires_between_decodes_is_news_not_a_misprediction()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);

        // The spawn's glow rides the first arrival's words; the next replay's
        // ring carries them (the spawn compare itself is the class news).
        frame.Arrive(c => Views.SetConditions(c.State, CondMask.Of(Cond.TeamGlows)));
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        frame.Prediction.CommandRan(LocalPlayer, 12, firstTime: true);

        // The server's tick expires the glow outside any command
        // (ConditionGameRulesThink is GAME_DLL, and the durations ride no
        // packet): the arrival's words move and the ring's are stale by
        // exactly that write — the compare's news, as the class change is.
        frame.Arrive(c => Views.SetConditions(c.State, default));
        frame.Prediction.ResetBasis(LocalPlayer, 12);

        var counts = frame.Prediction.Counts;
        Assert.Equal((0, 1), (counts.Mispredicts, counts.ConditionNews));
        Assert.False(Views.InCond(frame.Working, Cond.TeamGlows));
    }

    [Fact]
    public void A_condition_word_the_server_did_not_touch_still_stands()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Arrive(c => Views.SetConditions(c.State, CondMask.Of(Cond.TeamGlows)));
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        // The replay itself dropped the bit: the arrival's words did not move,
        // so the difference is this side's to own, not the machine's news.
        Views.SetConditions(frame.Working, default);
        frame.Prediction.CommandRan(LocalPlayer, 12, firstTime: true);

        frame.Arrive(c => Views.SetConditions(c.State, CondMask.Of(Cond.TeamGlows)));
        frame.Prediction.ResetBasis(LocalPlayer, 12);

        var counts = frame.Prediction.Counts;
        Assert.Equal((1, 0), (counts.Mispredicts, counts.ConditionNews));
        Assert.Contains("PlayerCond", frame.Prediction.Report());
    }

    /// <summary>
    /// The container builds the prediction from its declared inputs the frame
    /// first asks for it — the convars published, the engine environment and
    /// the standalone directory in — and nothing of the late composition (the
    /// HUD, the effects' visual halves). The skill's client arm builds off the
    /// same registrations (regression: the server's twin failed the first live
    /// load on a service taken at the movement resolution).
    /// </summary>
    [Fact]
    public void The_prediction_builds_from_the_container_with_its_declared_inputs_alone()
    {
        var log = new ClientLog();
        var convars = SpyConVars();
        var container = new client::SourceSharp.Abi.ServiceContainer()
            .AddInstance<SourceSharp.Abi.ILog>(log)
            .AddInstance(NetStateCatalog.Of(typeof(PlayerData).Assembly, log))
            .Add<Carriers>()    // its own [SideService] mark, dependency-free
            .Add<Prediction>()
            .AddInstance<client::SourceSharp.Client.IClientStandalones>(new NoStandalones())
            .AddInstance<client::SourceSharp.Abi.IEngineEnvironment>(EngineWithGlobals())
            .AddInstance(convars.Declared<InvisTimeVar>())
            .AddInstance(convars.Declared<InvisUnstealthVar>())
            .AddInstance(convars.Declared<CloakConsumeVar>())
            .AddInstance(convars.Declared<CloakRegenVar>());

        Assert.NotNull(container.Create<Prediction>());
        Assert.NotNull(container.Create<ClientEffects>());
    }

    private static ClientEngine EngineWithGlobals()
    {
        var engine = new ClientEngine();
        engine.Returns("Environment.Globals", new AbiGlobals(GlobalsBlock));
        return engine;
    }

    [Fact]
    public void Another_players_command_saves_nothing()
    {
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);

        frame.Prediction.CommandRan(OtherPlayer, 11, firstTime: true);

        Assert.Equal(0, frame.Prediction.Counts.Saves);
    }

    //-------------------------------------------------------------------------
    // The cloak: the replay runs stock's UpdateCloakMeter per command
    // (c_tf_player.cpp PreThink → SharedThink, the local player's arm) and the
    // watch's state half on the skill, so the server's word and the replay's
    // agree on CloakMeter and the change stamps.
    //-------------------------------------------------------------------------

    /// <summary>The frame with the local player spawned a spy and the basis reset.</summary>
    private static Frame Spy(bool stealthed = false)
    {
        var frame = new Frame();
        Views.SetClass(frame.Carrier.State, TFClass.Spy);

        // The arrival's word carries the bit; the tick reads the state the
        // last command left, so it belongs to the basis, not the working copy.
        if (stealthed)
            Views.SetConditions(frame.Carrier.State, CondMask.Of(Cond.Stealthed));

        frame.Prediction.ResetBasis(LocalPlayer, 10);
        return frame;
    }

    /// <summary>The skill's attack: this owner's secondary at the named clock, no effects.</summary>
    private static ClientShared.WeaponAttackContext Attack(int owner, float now)
        => new(default!, null!, null!, now, false, null!, new ClientShared.WeaponCommand(1, 5, owner));

    [Fact]
    public void The_cloak_drains_while_the_prediction_is_stealthed()
    {
        var frame = Spy(stealthed: true);
        frame.Working.CloakMeter = 50f;

        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        // tf_spy_cloak_consume_rate 10 over the command's tick.
        Assert.Equal(50f - 10f * Tick, frame.Working.CloakMeter, 4);
    }

    [Fact]
    public void The_cloak_fills_while_uncloaked_and_stops_at_full()
    {
        var frame = Spy();
        frame.Working.CloakMeter = 99.96f;

        // tf_spy_cloak_regen_rate 3.3 over the tick takes it past 100; the
        // refill clamps there (tf_player_shared.cpp:14196-14197).
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        Assert.Equal(100f, frame.Working.CloakMeter, 4);
    }

    [Fact]
    public void A_non_spy_loses_the_stale_stealth_bit_at_the_tick()
    {
        var frame = new Frame();
        Views.SetClass(frame.Carrier.State, TFClass.Scout);
        Views.SetConditions(frame.Carrier.State, CondMask.Of(Cond.Stealthed));
        frame.Prediction.ResetBasis(LocalPlayer, 10);

        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        // InvisibilityThink's safety net (tf_player.cpp:7977's else arm).
        Assert.False(Views.InCond(frame.Working, Cond.Stealthed));
    }

    [Fact]
    public void The_watch_secondary_while_cloaked_fades_and_stamps_the_unstealth_time()
    {
        var frame = Spy();
        Views.SetConditions(frame.Working, CondMask.Of(Cond.Stealthed));
        var effects = new ClientEffects(frame.Prediction);

        bool ran = effects.ClassSpecialSkill(Attack(Local, now: 3f));

        // Stock's bDoSkill: a fade is not a skill (tf_weapon_invis.cpp:214-251).
        Assert.False(ran);
        Assert.False(Views.InCond(frame.Working, Cond.Stealthed));
        Assert.Equal(5f, frame.Working.InvisChangeCompleteTime, 4);     // 3 + tf_spy_invis_unstealth_time 2
    }

    [Fact]
    public void The_watch_secondary_over_the_meter_floor_cloaks_and_stamps_the_invis_time()
    {
        var frame = Spy();
        frame.Working.CloakMeter = 100f;
        var effects = new ClientEffects(frame.Prediction);

        bool ran = effects.ClassSpecialSkill(Attack(Local, now: 3f));

        Assert.True(ran);
        Assert.True(Views.InCond(frame.Working, Cond.Stealthed));
        Assert.Equal(4f, frame.Working.InvisChangeCompleteTime, 4);     // 3 + tf_spy_invis_time 1
    }

    [Fact]
    public void The_watch_refuses_to_engage_at_the_meter_floor()
    {
        var frame = Spy();
        frame.Working.CloakMeter = 5f;
        var effects = new ClientEffects(frame.Prediction);

        bool ran = effects.ClassSpecialSkill(Attack(Local, now: 3f));

        // Over 8% to start (tf_weapon_invis.cpp:243).
        Assert.False(ran);
        Assert.False(Views.InCond(frame.Working, Cond.Stealthed));
    }

    [Fact]
    public void The_re_derived_debounce_refuses_a_second_toggle_in_the_window()
    {
        var frame = Spy();
        frame.Working.CloakMeter = 100f;
        var effects = new ClientEffects(frame.Prediction);

        // Command 11's tick, the skill inside command 12 — whose save is the
        // first slot with the bit — and the ring keeps both.
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);
        Assert.True(effects.ClassSpecialSkill(Attack(Local, now: 1 * Tick)));
        frame.Prediction.CommandRan(LocalPlayer, 12, firstTime: true);

        // The next basis re-derives the cooldown the component state carries on
        // the server: the last pair of slots whose bit moved, stamped at the
        // later's tick plus the skill's half second (tf_weapon_invis.cpp:255).
        // The arrival's word carries the bit itself; the re-derivation only
        // has to keep the refusal's clock.
        Views.SetConditions(frame.Carrier.State, CondMask.Of(Cond.Stealthed));
        frame.Prediction.ResetBasis(LocalPlayer, 12);

        Assert.False(effects.ClassSpecialSkill(Attack(Local, now: 2 * Tick)));
        Assert.True(Views.InCond(frame.Working, Cond.Stealthed));

        // Past the window the watch engages again.
        Views.SetConditions(frame.Working, default);
        frame.Working.CloakMeter = 100f;
        Assert.True(effects.ClassSpecialSkill(Attack(Local, now: 1f)));
    }
}
