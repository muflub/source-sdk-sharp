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
using TFTeam = client::TF2.Shared.TFTeam;
using CloakConsumeVar = client::TF2.Shared.TFSpyCloakConsumeRateConVar;
using CloakRegenVar = client::TF2.Shared.TFSpyCloakRegenRateConVar;
using InvisTimeVar = client::TF2.Shared.TFSpyInvisTimeConVar;
using InvisUnstealthVar = client::TF2.Shared.TFSpyInvisUnstealthTimeConVar;
using Views = client::TF2.Shared.Player.TFPlayerNetStateViews;
using ClientItemSchema = client::TF2.Shared.Econ.ItemSchema;
using ClientWeaponCatalog = client::SourceSharp.Shared.WeaponCatalog;
using ClientShared = client::SourceSharp.Shared;

using TF2.Tests.Weapons;

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

        public Frame(ClientItemSchema? itemSchema = null, ClientWeaponCatalog? itemCatalog = null)
        {
            Engine.Returns("Environment.Globals", new AbiGlobals(GlobalsBlock));
            var convars = SpyConVars();

            Carrier = Data(Carriers, Local);
            OtherCarrier = Data(Carriers, Other);
            Prediction = new Prediction(Log, Carriers, NetStateCatalog.Of(typeof(PlayerData).Assembly, Log),
                new NoStandalones(), Engine,
                convars.Declared<CloakConsumeVar>(), convars.Declared<CloakRegenVar>(),
                convars.Declared<InvisTimeVar>(), convars.Declared<InvisUnstealthVar>(),
                itemSchema: itemSchema, weaponCatalog: itemCatalog);
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

    //-------------------------------------------------------------------------
    // The Dead Ringer's press: the replay's feign arm, fed by the client's
    // real item path — the watch row the carrier's defindex names, looked up
    // in the schema the client parses (TFClientWeaponOwners.ItemOf).
    //-------------------------------------------------------------------------

    /// <summary>The Dead Ringer's defindex, as the trimmed schema carries it.</summary>
    private const int DeadRinger = 59;

    /// <summary><c>TF_WEAPON_INVIS</c> as the rows spell it.</summary>
    private const string InvisWatch = "tf_weapon_invis";

    /// <summary>The frame whose local player's watch is the item
    /// <paramref name="defindex"/> names, on the wire as defindex + 1.</summary>
    private static Frame WatchFrame(int defindex)
    {
        var items = ClientItems();
        var frame = new Frame(items.Schema, items.Catalog);
        frame.Carrier.State.WeaponItemDefIndex[items.Catalog.IndexOf(InvisWatch)] = defindex + 1;
        Views.SetClass(frame.Carrier.State, TFClass.Spy);
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        return frame;
    }

    [Fact]
    public void The_dead_ringers_press_arms_the_ready_bit_and_leaves_the_cloak_off()
    {
        var frame = WatchFrame(DeadRinger);
        frame.Working.CloakMeter = 100f;
        var effects = new ClientEffects(frame.Prediction);

        bool ran = effects.ClassSpecialSkill(Attack(Local, now: 3f));

        // The feign arm (tf_weapon_invis.cpp:230-242): the bit arms from the
        // full meter and no cloak word moves — the press's own state is the
        // bit's, the field stock predicts (tf_player_shared.cpp:468).
        Assert.False(ran);
        Assert.True(frame.Working.FeignDeathReady);
        Assert.False(Views.InCond(frame.Working, Cond.Stealthed));
        Assert.Equal(0f, frame.Working.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void The_armed_press_spends_the_bit_back_off()
    {
        var frame = WatchFrame(DeadRinger);
        frame.Working.CloakMeter = 100f;
        var effects = new ClientEffects(frame.Prediction);

        Assert.False(effects.ClassSpecialSkill(Attack(Local, now: 3f)));
        Assert.True(frame.Working.FeignDeathReady);

        // Past the 0.1 s the no-skill press stamps (tf_weapon_invis.cpp:259).
        Assert.False(effects.ClassSpecialSkill(Attack(Local, now: 3.2f)));
        Assert.False(frame.Working.FeignDeathReady);
        Assert.False(Views.InCond(frame.Working, Cond.Stealthed));
    }

    //-------------------------------------------------------------------------
    // The pyro's rage: the burn arm of HandleRageGain replayed from the
    // victims' carriers — the client's real item path, the flame row the
    // carrier's defindex names, looked up in the schema the client parses
    // (TFClientWeaponOwners.ItemOf).
    //-------------------------------------------------------------------------

    /// <summary>The Phlogistinator's defindex, as the trimmed schema carries it.</summary>
    private const int Phlogistinator = 594;

    /// <summary><c>TF_WEAPON_FLAMETHROWER</c> as the rows spell it.</summary>
    private const string Flamethrower = "tf_weapon_flamethrower";

    /// <summary>The frame whose local pyro's flame is the Phlogistinator, on the wire as defindex + 1.</summary>
    private static Frame BurnFrame()
    {
        var items = ClientItems();
        var frame = new Frame(items.Schema, items.Catalog);
        frame.Carrier.State.WeaponItemDefIndex[items.Catalog.IndexOf(Flamethrower)] = Phlogistinator + 1;
        return frame;
    }

    [Fact]
    public void A_victims_burn_tick_gains_the_rage_meter_as_the_servers_tick_did()
    {
        var frame = BurnFrame();

        // The victim burns, this player its attacker: the fire's first tick is
        // due now — the window's first command's clock, the anchor's — and the
        // server's tick of it books a point and a third on the pyro's meter
        // (TF_BURNING_DMG 4 over the burn arm's divisor 3, the phlog's buff
        // type 5 the only scale-1 row).
        frame.OtherCarrier.State.BurnAttackerIndex = Local;
        frame.OtherCarrier.State.BurnNextTickTime = 0f;
        frame.OtherCarrier.State.BurnDuration = 3f;

        frame.Prediction.ResetBasis(LocalPlayer, 10);
        Assert.Equal(0f, frame.Working.RageMeter);
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        Assert.Equal(4f / 3f, frame.Working.RageMeter);

        // The server's word, the same gain: the slot holds and nothing stands.
        frame.Arrive(c => c.State.RageMeter = 4f / 3f);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var counts = frame.Prediction.Counts;
        Assert.Equal(0, counts.Mispredicts);
        Assert.Equal(1, counts.Holds);
    }

    [Fact]
    public void A_burn_tick_without_the_flame_gains_nothing()
    {
        // No item world: the flame row names nothing, the buff type is none,
        // and the flag test's no-op the server's arm is (tf_player.cpp:8749).
        var frame = new Frame();

        frame.OtherCarrier.State.BurnAttackerIndex = Local;
        frame.OtherCarrier.State.BurnNextTickTime = 0f;
        frame.OtherCarrier.State.BurnDuration = 3f;

        frame.Prediction.ResetBasis(LocalPlayer, 10);
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        Assert.Equal(0f, frame.Working.RageMeter);
    }

    /// <summary>The client's item world: the shipped scripts' catalogue rows
    /// over the trimmed schema — the pack the client really parses at boot.</summary>
    /// <summary>The press's word, as the server's <c>disguise</c> leaves it on
    /// the carrier (tf_player_shared.cpp:8246-8247, :8268): disguising as a
    /// blue soldier, done at the 132nd tick.</summary>
    private static void Press(PlayerData carrier)
    {
        Views.SetConditions(carrier.State, CondMask.Of(Cond.Disguising));
        carrier.State.CloakMeter = 100f; // the regen the server's ticks paid in
        carrier.State.DesiredDisguiseClass = (int)TFClass.Soldier;
        carrier.State.DesiredDisguiseTeam = (int)TFTeam.Blue;
        carrier.State.DisguiseCompleteTime = 132 * Tick;
    }

    /// <summary>The completion's word, the server's tick past the deadline
    /// (:8302-8312, :8343, OnRemoveDisguising :7218).</summary>
    private static void Completed(PlayerData carrier)
    {
        Views.SetConditions(carrier.State, CondMask.Of(Cond.Disguised));
        carrier.State.DisguiseClass = (int)TFClass.Soldier;
        carrier.State.DisguiseTeam = (int)TFTeam.Blue;
        carrier.State.DesiredDisguiseTeam = (int)TFTeam.Unassigned;
        carrier.State.DisguiseCompleteTime = 0f;
        carrier.State.CloakMeter = 100f;
    }

    /// <summary>The round restart's respawn: the spawn's RemoveAll removes
    /// Disguised, whose <c>OnRemoved</c> clears the disguise outside any
    /// command (TFSpy.OnRemoved; stock keeps the clear in the server's arm,
    /// tf_player_shared.cpp:7267-7273 — the client's own arm is display-only
    /// and skips a predictable entity during the sync, :7236 — so the reset
    /// rides the networked fields and the compare's single error is the
    /// agreement).</summary>
    private static void Respawned(PlayerData carrier)
    {
        Views.SetConditions(carrier.State, default);
        carrier.State.DisguiseClass = (int)TFClass.Undefined;
        carrier.State.DisguiseTeam = (int)TFTeam.Unassigned;
        carrier.State.DisguiseTargetIndex = -1;
        carrier.State.DisguiseHealth = 0;
    }

    [Fact]
    public void The_replay_completes_the_disguise_past_the_deadline_never_on_it()
    {
        var frame = Spy();

        frame.Arrive(Press);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        // Commands run at (command - 12) ticks past the anchor; 144's is the
        // deadline itself and completes nothing — CheckDisguiseTimer compares
        // strictly, curtime > completeTime (tf_player_shared.cpp:3309), the
        // server's tick beside it.
        for (int command = 12; command <= 144; command++)
            frame.Prediction.CommandRan(LocalPlayer, command, firstTime: true);

        Assert.True(Views.InCond(frame.Working, Cond.Disguising));
        Assert.False(Views.InCond(frame.Working, Cond.Disguised));
        Assert.Equal(0, (int)frame.Working.DisguiseClass);

        frame.Prediction.CommandRan(LocalPlayer, 145, firstTime: true);

        // CompleteDisguise's shared half (:8302-8312): the disguise names the
        // wish, the bits trade, the deadline clears and the wish's team ends
        // with the disguising (OnRemoveDisguising, :7218) — the class stands.
        Assert.True(Views.InCond(frame.Working, Cond.Disguised));
        Assert.False(Views.InCond(frame.Working, Cond.Disguising));
        Assert.Equal((int)TFClass.Soldier, frame.Working.DisguiseClass);
        Assert.Equal((int)TFTeam.Blue, frame.Working.DisguiseTeam);
        Assert.Equal(0f, frame.Working.DisguiseCompleteTime, 4);
        Assert.Equal((int)TFTeam.Unassigned, frame.Working.DesiredDisguiseTeam);
        Assert.Equal((int)TFClass.Soldier, frame.Working.DesiredDisguiseClass);
    }

    [Fact]
    public void The_replayed_completions_word_is_held_not_mispredicted()
    {
        var frame = Spy();

        frame.Arrive(Press);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        // The window replays well past the deadline; the ring carries the
        // completion the mirror ran.
        for (int command = 12; command <= 200; command++)
            frame.Prediction.CommandRan(LocalPlayer, command, firstTime: true);

        // The server completed on the same command; its word says so.
        frame.Arrive(Completed);
        frame.Prediction.ResetBasis(LocalPlayer, 200);

        var counts = frame.Prediction.Counts;
        Assert.Equal(0, counts.Mispredicts);
        Assert.Equal((int)TFClass.Soldier, frame.Working.DisguiseClass);
        Assert.True(Views.InCond(frame.Working, Cond.Disguised));
    }

    [Fact]
    public void The_respawns_reset_of_the_disguise_is_one_named_misprediction()
    {
        var frame = Spy();

        frame.Arrive(Press);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        for (int command = 12; command <= 200; command++)
            frame.Prediction.CommandRan(LocalPlayer, command, firstTime: true);

        frame.Arrive(Completed);
        frame.Prediction.ResetBasis(LocalPlayer, 200);

        // The respawn arrives outside any command: the disguise's fields the
        // spawn cleared stand against the ring's completed run — exactly the
        // one error stock's client absorbs too (its re-prediction from the
        // arrival, prediction.cpp:1440-1565, has no respawn branch). The
        // fields stay in the compare, named; the conditions' words are the
        // news arm's, not theirs.
        frame.Arrive(Respawned);
        frame.Prediction.ResetBasis(LocalPlayer, 200);

        var counts = frame.Prediction.Counts;
        Assert.Equal(1, counts.Mispredicts);
        Assert.Contains("MISPREDICTION at command 200", frame.Log.Text);
        Assert.Contains("DisguiseClass, DisguiseTeam", frame.Log.Text);
        Assert.Equal((int)TFClass.Undefined, frame.Working.DisguiseClass);
        Assert.Equal((int)TFTeam.Unassigned, frame.Working.DisguiseTeam);
        Assert.False(Views.InCond(frame.Working, Cond.Disguised));
    }

    [Fact]
    public void The_disguises_press_rides_the_arrival_never_a_misprediction()
    {
        var frame = Spy();
        frame.Working.CloakMeter = 100f; // the basis's word: the meter arrived full
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);

        // The press lands on the server between commands; its wish, deadline
        // and disguising bit ride the next snapshot — the bit as the
        // conditions' news, the pair and deadline sent not predicted, the
        // arrival's word. Nothing the ring could have known is judged.
        frame.Arrive(Press);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var counts = frame.Prediction.Counts;
        Assert.Equal(0, counts.Mispredicts);
        Assert.True(Views.InCond(frame.Working, Cond.Disguising));
    }

    private static (ClientItemSchema Schema, ClientWeaponCatalog Catalog) ClientItems()
    {
        var files = new SideFiles();
        return (new ClientItemSchema(files, new ClientLog()),
                new ClientWeaponCatalog(
                    () => [new client::TF2.Shared.TFWeaponTypes(
                        new client::TF2.Shared.TFWeaponScripts(new ClientLog(), files),
                        new client::TF2.Shared.TFClassTable(new ClientLog(), files),
                        new ClientItemSchema(files, new ClientLog()))],
                    new ClientLog()));
    }

    /// <summary>The shipped scripts and the trimmed schema, served as the
    /// client's file seam — the pack the client really has at boot (a test
    /// served GAME path; a missing file is the schema refusing, never a
    /// silent pass).</summary>
    private sealed class SideFiles : ClientAbi.IEngineFiles
    {
        private readonly TFClassTableTests.Files _files = WeaponFixtures.ShippedWithClasses();

        public SideFiles() =>
            _files.Bytes[client::TF2.Shared.Econ.ItemSchema.FilePath] =
                System.Text.Encoding.UTF8.GetBytes(EconTestFiles.Fixture("items_game.trimmed.txt"));

        public int GameFileSize(string path) => _files.GameFileSize(path);
        public string? ReadGameFile(string path) => _files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => _files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => _files.ReadFileOnPath(path, pathId);
    }
}
