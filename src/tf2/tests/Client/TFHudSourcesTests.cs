extern alias client;

using System.Text;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Tests.Weapons;

using Xunit;

using Arsenal = client::SourceSharp.Client.ILocalArsenal;
using ClientFilesShim = client::SourceSharp.Abi.IEngineFiles;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Econ = client::TF2.Shared.Econ;
using HudLocalPlayer = client::TF2.Client.Ui.TFHudLocalPlayer;
using OwnersContract = client::TF2.Shared.ITFWeaponOwners;
using HudRoundTimer = client::TF2.Client.Ui.TFHudRoundTimer;
using MedicCharge = client::TF2.Client.Ui.TFHudMedicCharge;
using HudSpy = client::TF2.Client.Ui.TFHudSpy;
using ClientViews = client::TF2.Shared.Player.TFPlayerNetStateViews;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Scripts = client::TF2.Shared.TFWeaponScripts;
using TFClassId = client::TF2.Shared.TFClass;
using Names = client::SourceSharp.Client.Vgui.IPlayerNames;
using Carrier = client::TF2.Shared.Player.TFPlayerNetState;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using TFCondId = client::TF2.Shared.TFCond;
using TimerSource = client::TF2.Client.Entities.ITFRoundTimer;
using Timers = client::TF2.Client.Entities.TFClientRoundTimers;
using CapturePoints = client::TF2.Client.Ui.TFHudCapturePoints;
using Escort = client::TF2.Client.Ui.TFHudEscort;
using Limits = client::TF2.Shared.Resources.ObjectiveLimits;
using Team = client::TF2.Shared.TFTeam;
using RoundState = client::TF2.Shared.Rules.RoundState;
using ClockLimit = client::SourceSharp.Client.ClMpTimeLimitConVar;
using LocalPools = client::SourceSharp.Client.ILocalAmmoPools;
using ShowServerLimit = client::TF2.Client.TFHudShowServerTimeLimitConVar;

namespace TF2.Tests.Client;

/// <summary>
/// The TF2 client assembly's HUD adapters (src/tf2/client/Ui): each maps
/// the client's netstate mirrors into the <c>ITF*</c> sources the HUD
/// screens draw. A fact checks one behaviour of the mapping — the value
/// before the data arrives differs from the one after.
/// </summary>
public sealed class TFHudSourcesTests
{
    [Fact]
    public void The_health_row_reads_the_engine_and_the_cross()
    {
        var engine = new Engine();
        var resources = new PlayerResources();
        var source = Cross(engine, resources);

        // The engine answering for nobody, the cross not arrived: nothing
        // to draw. (The double is strict: a member it was never told
        // throws rather than answering zero.)
        engine.Returns("Hud.LocalPlayerIndex", 0);
        engine.Returns("Hud.LocalPlayerHealth", 0);
        Assert.Equal("", source.ClassName);
        Assert.Equal(0, source.Health);
        Assert.Equal(0, source.MaxHealth);
        Assert.Equal(0, source.MaxBuffedHealth);
        Assert.Equal(0, source.Overheal);

        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 150);
        Arrive(engine, resources, playerClass: (byte)TFClassId.Soldier, maxHealth: 125, maxBuffedHealth: 187);

        // The soldier's 125, the engine's 150, the bonus band of the rest.
        Assert.Equal("Soldier", source.ClassName);
        Assert.Equal(150, source.Health);
        Assert.Equal(125, source.MaxHealth);
        Assert.Equal(187, source.MaxBuffedHealth);
        Assert.Equal(25, source.Overheal);
    }

    [Fact]
    public void Overheal_counts_only_the_health_above_max()
    {
        var engine = new Engine();
        var resources = new PlayerResources();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 150);
        Arrive(engine, resources, playerClass: (byte)TFClassId.Soldier, maxHealth: 125, maxBuffedHealth: 187);
        var source = Cross(engine, resources);
        Assert.Equal(25, source.Overheal);

        engine.Returns("Hud.LocalPlayerHealth", 90);
        Assert.Equal(0, source.Overheal);
    }

    [Fact]
    public void A_classless_local_shows_no_name_and_removal_clears_the_columns()
    {
        var engine = new Engine();
        var resources = new PlayerResources();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 90);
        var source = Cross(engine, resources);

        // No column written yet: the class is Undefined, which the HUD shows
        // as nothing, not as the word "Undefined".
        var cross = Arrive(engine, resources, playerClass: 0, maxHealth: 125, maxBuffedHealth: 187);
        Assert.Equal("", source.ClassName);
        Assert.Equal(125, source.MaxHealth);

        // The cross leaving takes the row with it, as it does the scoreboard.
        cross.OnRemoved();
        Assert.Equal(0, source.MaxHealth);
        Assert.Equal(0, source.MaxBuffedHealth);
    }

    [Fact]
    public void The_timer_row_reads_the_elected_clock()
    {
        var timers = new Timers(new ObjectiveRig().Source);
        var source = new HudRoundTimer(timers, new Engine(), new Engine(), new PlayerResources(),
                                       new ClockLimit(), new ShowServerLimit());

        // No timer elected: the panel has nothing to draw.
        Assert.False(source.Present);
        Assert.Equal(0, source.SecondsLeft);
        Assert.Equal(0, source.TotalSeconds);
        Assert.False(source.ShowTimeRemaining);

        timers.Track(new FakeTimer(index: 1, remaining: 20.4f, showTimeRemaining: true, maxLength: 30));

        Assert.True(source.Present);
        Assert.Equal(20, source.SecondsLeft);
        Assert.Equal(30, source.TotalSeconds);
        Assert.True(source.ShowTimeRemaining);
        Assert.False(source.InSetup);
        Assert.False(source.Overtime);
    }

    [Fact]
    public void The_timer_row_reads_the_local_team_off_the_mirror()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        var resources = new PlayerResources();
        var timers = new Timers(new ObjectiveRig().Source);
        var source = new HudRoundTimer(timers, engine, engine, resources, new ClockLimit(), new ShowServerLimit());

        // No mirror arrived: team 0, and SetTeamBackground's else arm (blue).
        Assert.Equal(0, source.Team);

        // The mirror's slot column is the read, not the bound local.
        var cross = new PlayerResource(engine, resources);
        cross.State.BindLocal();
        cross.State.Team[2] = 3;
        cross.OnSpawn();
        Assert.Equal(3, source.Team);
    }

    [Fact]
    public void Overtime_is_the_clock_running_out_in_play()
    {
        // Still in play with seconds to spare: no overtime.
        var running = Row(new FakeTimer(index: 1, remaining: 5f));
        Assert.True(running.Present);
        Assert.Equal(5, running.SecondsLeft);
        Assert.False(running.Overtime);

        // Past zero: the derived overtime, and the clock keeps counting.
        var overrun = Row(new FakeTimer(index: 1, remaining: -3f));
        Assert.Equal(-3, overrun.SecondsLeft);
        Assert.True(overrun.Overtime);

        // The pre-round clock starting at zero is setup, not overtime.
        var setup = Row(new FakeTimer(index: 1, remaining: -3f, inSetup: true));
        Assert.True(setup.InSetup);
        Assert.False(setup.Overtime);

        // A frozen clock is not an overtime label either.
        var paused = Row(new FakeTimer(index: 1, remaining: -3f, paused: true));
        Assert.True(paused.IsPaused);
        Assert.False(paused.Overtime);

        // No timer at all: nothing is overtime.
        Assert.False(Row(null).Overtime);
    }

    [Fact]
    public void The_sudden_death_and_wait_words_come_off_the_mirror()
    {
        var engine = new Engine();
        var resources = new PlayerResources();
        var timers = new Timers(new ObjectiveRig().Source);
        timers.Track(new FakeTimer(index: 1, remaining: 5f));
        var source = new HudRoundTimer(timers, engine, engine, resources, new ClockLimit(), new ShowServerLimit());

        // Nothing arrived: both words quiet.
        Assert.False(source.Stalemate);
        Assert.False(source.WaitingForPlayers);

        var cross = new PlayerResource(engine, resources);
        cross.State.BindLocal();
        cross.OnSpawn();
        cross.State.RoundState = (byte)RoundState.Stalemate;
        Assert.True(source.Stalemate);              // SetExtraTimePanels :489-508
        Assert.False(source.WaitingForPlayers);

        cross.State.WaitingForPlayers = 1;
        Assert.True(source.WaitingForPlayers);      // :573-589
    }

    [Fact]
    public void The_server_limit_counts_from_the_map_reset()
    {
        var clock = new HudClock { Now = 50f };
        var engine = new Engine();
        var resources = new PlayerResources();
        var limit = new ClockLimit();
        limit.NotifyChanged("10", "0");             // mp_timelimit 10 minutes
        var timers = new Timers(new ObjectiveRig().Source);
        var source = new HudRoundTimer(timers, clock, engine, resources, limit, new ShowServerLimit());

        // The switch's default is off and the mirror has no reset time yet.
        Assert.False(source.ShowServerLimit);       // tf_hud_show_servertimelimit 0
        Assert.Equal(600, source.ServerTimeLimit);  // nServerTimeLimit (tf_time_panel.cpp:744)
        Assert.Equal(0, source.ServerTimeLeft);

        var cross = new PlayerResource(engine, resources);
        cross.State.BindLocal();
        cross.OnSpawn();
        cross.State.MapResetTime = 40f;
        Assert.Equal(590, source.ServerTimeLeft);   // GetTimeLeft :1226-1239, floored
        clock.Now = 641f;
        Assert.Equal(0, source.ServerTimeLeft);     // never below zero
    }

    [Fact]
    public void The_ammo_row_shows_the_active_weapons_clip_and_pool()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 125);
        var resources = new PlayerResources();
        Arrive(engine, resources, playerClass: (byte)TFClassId.Soldier, maxHealth: 125, maxBuffedHealth: 187);

        // Empty hands (ShouldDraw's no-weapon arm, tf_hud_ammostatus.cpp:132-135):
        // nothing to show.
        Assert.False(Cross(engine, resources).ShowsAmmo);

        // A gun that spends primary ammunition: clip and pool, as OnThink
        // reads them (Clip1 at :276, GetAmmoCount at :284-287).
        var source = Cross(engine, resources, new Hands("tf_weapon_rocketlauncher", 3, 24), ServeScripts());
        Assert.True(source.ShowsAmmo);
        Assert.Equal(3, source.Clip);
        Assert.Equal(24, source.Reserve);
    }

    [Fact]
    public void Weapons_that_show_no_ammo_are_the_medigun_metal_and_the_scriptless()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 125);
        var resources = new PlayerResources();
        Arrive(engine, resources, playerClass: (byte)TFClassId.Medic, maxHealth: 125, maxBuffedHealth: 187);

        // The medigun, even scripted to spend primary ammo, is hidden by its
        // kind (:137-140, the arm that stands in for the ubercharge test :150).
        var scripts = ServeScripts(("tf_weapon_medigun", "\"primary_ammo\" \"TF_AMMO_PRIMARY\""));
        Assert.True(Cross(engine, resources, new Hands("tf_weapon_rocketlauncher", 3, 24), scripts).ShowsAmmo);  // stimulus
        Assert.False(Cross(engine, resources, new Hands("tf_weapon_medigun", 0, 40), scripts).ShowsAmmo);

        // A weapon whose pool is metal — the engineer's build resource, not
        // an ammo readout (:147-148).
        var metal = ServeScripts(("tf_weapon_builder", "\"primary_ammo\" \"TF_AMMO_METAL\""));
        Assert.False(Cross(engine, resources, new Hands("tf_weapon_builder", 0, 100), metal).ShowsAmmo);

        // A weapon with no script is stock's empty gNullWeaponInfo: it spends
        // nothing, so there is nothing to draw (:143-144).
        Assert.False(Cross(engine, resources, new Hands("tf_weapon_unheard_of", 1, 1)).ShowsAmmo);
    }

    [Fact]
    public void The_medic_charge_meter_reads_the_carried_weapon_and_names_the_patient()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 150);
        var carriers = new Carriers();
        var scripts = ServeScripts(("tf_weapon_medigun", "\"primary_ammo\" \"TF_AMMO_PRIMARY\""));
        var stands = new GunStands();
        var states = new DrawStates();
        var medic = new MedicCharge(engine, carriers, new Hands("tf_weapon_medigun", 0, 40),
                                    scripts, new Roster((3, "Bob")), stands, states,
                                    new RigOwners(), new Econ.AttributeHooks());

        // No carrier decoded yet: stock's ShouldDraw refuses (tf_hud_mediccharge.cpp:147-170).
        Assert.False(medic.Present);
        Assert.Equal(0f, medic.Charge, 5);

        // The medic's cross arrived, the medigun carried: the LEVEL is the
        // carried weapon's own state (m_flChargeLevel on DT_WeaponMedigun),
        // the release and the patient stay on the carrier.
        var carrier = new Carrier();
        carrier.BindLocal();
        carrier.PlayerClass = (int)TFClassId.Medic;
        carriers.States[2] = carrier;
        stands.Gun(2, 0.5f);
        Assert.True(medic.Present);
        Assert.Equal(0.5f, medic.Charge, 5);
        Assert.False(medic.Releasing);
        Assert.Equal("", medic.HealTarget);

        carrier.HealTargetIndex = 3;
        Assert.Equal("Bob", medic.HealTarget);

        carrier.ChargeReleasing = true;
        Assert.True(medic.Releasing);

        // The replay's working copy, while it is fresh, is what the meter reads.
        states.Working = 0.75f;
        Assert.Equal(0.75f, medic.Charge, 5);

        // A dead medic draws nothing, and neither does one with empty hands.
        engine.Returns("Hud.LocalPlayerHealth", 0);
        Assert.False(medic.Present);
        engine.Returns("Hud.LocalPlayerHealth", 150);
        Assert.False(new MedicCharge(engine, carriers, new Hands(null, 0, 0), scripts, new Roster(), stands, states,
                                     new RigOwners(), new Econ.AttributeHooks()).Present);
    }

    [Fact]
    public void The_charge_meter_draws_for_the_medigun_the_saw_and_a_spending_syringe()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 150);
        var carriers = new Carriers();
        var carrier = new Carrier();
        carrier.BindLocal();
        carrier.PlayerClass = (int)TFClassId.Medic;
        carriers.States[2] = carrier;
        var stands = new GunStands();
        stands.Gun(2, 0.5f);
        var owners = new RigOwners();
        var scripts = ServeScripts();
        MedicCharge Meter(Hands hands) => new(engine, carriers, hands, scripts, new Roster(), stands,
                                              new DrawStates(), owners, new Econ.AttributeHooks());

        // The medigun in hand: the meter (tf_hud_mediccharge.cpp:163).
        Assert.True(Meter(new Hands("tf_weapon_medigun", 0, 40)).Present);

        // The bonesaw: stock's second id (:163) — the medic's every melee.
        Assert.True(Meter(new Hands("tf_weapon_bonesaw", 0, 0)).Present);

        // The vanilla syringe spends no uber per shot: no meter, the ammo
        // panel's guest instead (:163-167 over UberChargeAmmoPerShot,
        // tf_weaponbase.h:299).
        Assert.False(Meter(new Hands("tf_weapon_syringegun_medic", 40, 200)).Present);

        // A syringe whose item pays uber per charge percent (the attrib over
        // 100) draws the meter again.
        owners.Item("ubercharge_ammo", 20f);
        Assert.True(Meter(new Hands("tf_weapon_syringegun_medic", 40, 200)).Present);

        // Any other weapon: neither arm, no meter.
        Assert.False(Meter(new Hands("tf_weapon_rocketlauncher", 4, 20)).Present);
    }

    [Fact]
    public void A_weapon_that_spends_uber_per_shot_leaves_the_readout_to_the_meter()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        var owners = new RigOwners();
        var scripts = ServeScripts(("tf_weapon_syringegun_medic", "\"primary_ammo\" \"TF_AMMO_PRIMARY\""));
        var source = Cross(engine, new PlayerResources(), new Hands("tf_weapon_syringegun_medic", 40, 200),
                           scripts, owners: owners);

        // The vanilla syringe: no per-shot spend, the readout is the ammo
        // panel's (tf_hud_ammostatus.cpp:150-151).
        Assert.True(source.ShowsAmmo);

        // An item that pays uber per shot reads on the charge meter instead.
        owners.Item("ubercharge_ammo", 20f);
        Assert.False(source.ShowsAmmo);
    }

    [Fact]
    public void The_spy_meters_read_the_cloak_and_the_disguise_words()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 125);
        var carriers = new Carriers();
        var spy = new HudSpy(engine, carriers);

        // No carrier decoded yet: neither meter draws.
        Assert.False(spy.CloakPresent);
        Assert.Equal(0f, spy.Cloak, 5);
        Assert.False(spy.Disguised);

        // The spy's cross arrived: the watch's charge reads the carrier's
        // m_flCloakMeter over its 100 (tf_hud_itemeffectmeter.cpp:513-519).
        var carrier = new Carrier();
        carrier.BindLocal();
        carrier.PlayerClass = (int)TFClassId.Spy;
        carrier.CloakMeter = 55f;
        carriers.States[2] = carrier;
        Assert.True(spy.CloakPresent);
        Assert.Equal(0.55f, spy.Cloak, 5);

        // The disguise: the condition word and the two m_n words, the class
        // spelled as the HUD shows it (CDisguiseStatus::ShouldDraw :138-145).
        Assert.False(spy.Disguised);
        carrier.DisguiseClass = (int)TFClassId.Soldier;
        carrier.DisguiseTeam = 2;
        ClientViews.SetConditions(carrier, CondMask.Of(TFCondId.Disguised));
        Assert.True(spy.Disguised);
        Assert.Equal("Soldier", spy.DisguiseClassName);
        Assert.Equal(2, spy.DisguiseTeam);

        // A dead spy's cloak draws nothing, the disguise stays.
        engine.Returns("Hud.LocalPlayerHealth", 0);
        Assert.False(spy.CloakPresent);
        Assert.True(spy.Disguised);
    }

    /// <summary>One adapter over the given hands: empty by default, so a
    /// health fact sees a hero with nothing held. The class table stays
    /// unloaded — a fact that reaches MaxTotalAmmo without a script never
    /// indexes it.</summary>
    private static HudLocalPlayer Cross(Engine engine, PlayerResources resources,
        Arsenal? weapon = null, Scripts? scripts = null, LocalPools? pools = null,
        RigOwners? owners = null, Econ.AttributeHooks? hooks = null) =>
        new(engine, resources, weapon ?? new Hands(null, 0, 0),
            scripts ?? new Scripts(new RecordingLog(), new EmptyFiles()),
            new client::TF2.Shared.TFClassTable(new RecordingLog(), new EmptyFiles()),
            pools ?? new NoPools(),
            owners ?? new RigOwners(), hooks ?? new Econ.AttributeHooks());

    /// <summary>The local player's item as the client contract sees it (the
    /// crosshair rig's <c>RigOwners</c> shape): no item until one is set, the
    /// empty answer every per-shot ask then reads.</summary>
    private sealed class RigOwners : OwnersContract
    {
        private Econ.IAttributeProvider? _item;

        public void Item(string hook, float value)
        {
            var container = new Econ.AttributeContainer(null, Econ.AttributeProviderType.Generic);
            container.Set(Econ.AttributeValue.OfFloat(
                new Econ.AttributeDefinition(9100, "attr 9100", hook,
                    Econ.AttributeDescriptionFormat.Additive, Econ.AttributeEffectType.Neutral,
                    false, false, Econ.AttributeValueType.Default, Econ.MergedKeys.Block("x")), value));
            _item = container;
        }

        public TFClassId ClassOf(int playerIndex) => TFClassId.Undefined;
        public client::TF2.Shared.Conditions.TFConditions? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public Econ.IAttributeProvider? ItemOf(int playerIndex, string weaponName) => _item;
        public Econ.IAttributeProvider? ManagerOf(int playerIndex) => null;
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
    }

    /// <summary>No side published: every pool unpublished, the metal
    /// account's zero arm.</summary>
    private sealed class NoPools : LocalPools
    {
        public bool PoolOf(string ammoName, out int count)
        {
            count = 0;
            return false;
        }
    }

    /// <summary>A side's published word, by name.</summary>
    private sealed class PublishedPools(System.Collections.Generic.Dictionary<string, int> pools) : LocalPools
    {
        public bool PoolOf(string ammoName, out int count)
        {
            count = 0;
            return pools.TryGetValue(ammoName, out count);
        }
    }

    [Fact]
    public void The_metal_account_reads_the_published_pool()
    {
        var engine = new Engine();
        var published = Cross(engine, new PlayerResources(),
            pools: new PublishedPools(new System.Collections.Generic.Dictionary<string, int> { ["TF_AMMO_METAL"] = 137 }));
        Assert.Equal(137, published.Metal);

        // Unpublished: the account panel's own opening zero.
        Assert.Equal(0, Cross(engine, new PlayerResources()).Metal);
    }

    /// <summary>Scripts served sealed as the pack holds them: every shipped
    /// fixture, plus the names a fact asks of written on the spot.</summary>
    private static Scripts ServeScripts(params (string Name, string Body)[] scripts)
    {
        var files = WeaponFixtures.Shipped();
        foreach (var (name, body) in scripts)
            files.Bytes[Scripts.ScriptName(name) + ".ctx"] =
                WeaponFixtures.Seal(Encoding.UTF8.GetBytes($"\"WeaponData\"\n{{\n\t{body}\n}}\n"));
        return new Scripts(new RecordingLog(), new EmptyFiles(files));
    }

    /// <summary>The active weapon as the arsenal answers it; a null name is
    /// empty hands.</summary>
    private sealed class Hands(string? weapon, int clip, int reserve) : Arsenal
    {
        public bool TryGetActive(out string name, out int clipOut, out int reserveOut)
        {
            if (weapon is null)
            {
                (name, clipOut, reserveOut) = ("", 0, 0);
                return false;
            }
            (name, clipOut, reserveOut) = (weapon, clip, reserve);
            return true;
        }
    }

    /// <summary>The file seam over the client's copy of the interface — the
    /// shared interface compiles into each side, so the doubles are built
    /// per side like <see cref="TFWeaponCompositionTests"/>'s shim.</summary>
    private sealed class EmptyFiles(TFClassTableTests.Files? files = null) : ClientFilesShim
    {
        private readonly TFClassTableTests.Files Files = files ?? new TFClassTableTests.Files();

        public int GameFileSize(string path) => Files.GameFileSize(path);
        public string? ReadGameFile(string path) => Files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => Files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => Files.ReadFileOnPath(path, pathId);
    }

    /// <summary>The client's decoded carriers, the facts hand out by index.</summary>
    private sealed class Carriers : client::TF2.Shared.Player.ITFPlayerData
    {
        public readonly Dictionary<int, Carrier> States = new();

        public Carrier? Predicted(int playerIndex) => States.TryGetValue(playerIndex, out var s) ? s : null;
        public Carrier? Networked(int playerIndex) => States.TryGetValue(playerIndex, out var s) ? s : null;
    }

    /// <summary>The g_PR role: one slot answering its name, the rest nobody.</summary>
    private sealed class Roster(params (int Slot, string Name)[] rows) : Names
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            foreach (var (found, name) in rows)
                if (found == slot)
                {
                    info = new ClientPlayerInfo(name, 0, false);
                    return true;
                }
            info = default;
            return false;
        }
    }

    /// <summary>The standalone shells: the local player's carried weapon,
    /// whose own state the meter reads.</summary>
    private sealed class GunStands : client::SourceSharp.Client.IClientStandalones
    {
        private readonly Dictionary<int, client::SourceSharp.Client.Standalone.ClientSsWeapon> _guns = [];

        public client::SourceSharp.Client.Standalone.ClientSsWeapon Gun(int ownerIndex, float meter)
        {
            var gun = new client::SourceSharp.Client.Standalone.ClientSsWeapon(new Engine());
            gun.State.BindLocal();
            gun.State.OwnerIndex = ownerIndex;
            gun.State.BehaviourMeter = meter;
            _guns[ownerIndex] = gun;
            return gun;
        }

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live()
            => _guns.Values.Cast<client::SourceSharp.Client.Standalone.ClientStandaloneEntity>().ToList();

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _guns.GetValueOrDefault(entityIndex);
    }

    /// <summary>The replay's working copy — nothing, until a fact makes it fresh.</summary>
    private sealed class DrawStates : client::SourceSharp.Client.WeaponEffects.IWeaponDrawStates
    {
        public float? Working;

        public client::SourceSharp.Shared.WeaponNetState? PredictedStateOf(
            client::SourceSharp.Client.Standalone.ClientSsWeapon weapon)
        {
            if (Working is not { } meter)
                return null;
            var copy = new client::SourceSharp.Shared.WeaponNetState();
            copy.BindLocal();
            copy.BehaviourMeter = meter;
            return copy;
        }
    }

    /// <summary>One adapter over one freshly elected timer, so each arm of
    /// a fact sees its own election.</summary>
    private static HudRoundTimer Row(FakeTimer? timer)
    {
        var timers = new Timers(new ObjectiveRig().Source);
        if (timer is not null)
            timers.Track(timer);
        return new HudRoundTimer(timers, new Engine(), new Engine(), new PlayerResources(),
                                 new ClockLimit(), new ShowServerLimit());
    }

    /// <summary>A clock a fact moves (client::SourceSharp.Abi.IGameClock).</summary>
    private sealed class HudClock : client::SourceSharp.Abi.IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>The cross arriving with the local row's columns written,
    /// as the entity's first spawn delivers it.</summary>
    private static PlayerResource Arrive(Engine engine, PlayerResources resources,
        byte playerClass, short maxHealth, short maxBuffedHealth, int index = 2)
    {
        var cross = new PlayerResource(engine, resources);
        cross.State.BindLocal();
        cross.State.PlayerClass[index] = playerClass;
        cross.State.MaxHealth[index] = maxHealth;
        cross.State.MaxBuffedHealth[index] = maxBuffedHealth;
        cross.OnSpawn();
        return cross;
    }

    [Fact]
    public void The_point_row_reads_the_objective_resource()
    {
        var rig = new ObjectiveRig();
        var source = new CapturePoints(rig.Source, rig.Engine);

        // Before the map's table arrives: no points, nothing named.
        Assert.Equal(0, source.NumPoints);
        Assert.Equal("", source.CapLayout);
        Assert.Equal((int)Team.Unassigned, source.OwningTeam(0));
        Assert.Equal((int)Team.Unassigned, source.CappingTeam(0));

        rig.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.CapLayoutInHud = "1,0";
            s.Visible[0] = true;
            s.Visible[1] = true;
            s.InMiniRound[0] = true;
            s.InMiniRound[1] = true;
            s.Owner[0] = (byte)Team.Blue;
            s.CappingTeam[0] = (byte)Team.Red;
            s.NumTeamMembers[Limits.TeamArray(0, (int)Team.Red)] = 2;
        });

        // After: the row the icons draw from, straight off the mirror.
        Assert.Equal(2, source.NumPoints);
        Assert.Equal("1,0", source.CapLayout);
        Assert.Equal((int)Team.Blue, source.OwningTeam(0));
        Assert.Equal((int)Team.Red, source.CappingTeam(0));
        Assert.True(source.Visible(0));
        Assert.True(source.InMiniRound(0));
        Assert.Equal(2, source.Cappers(0, (int)Team.Red));
    }

    [Fact]
    public void A_point_nobody_may_cap_is_locked_and_one_a_team_may_cap_is_not()
    {
        var rig = new ObjectiveRig();
        var source = new CapturePoints(rig.Source, rig.Engine);
        rig.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.Visible[0] = true;
            s.Visible[1] = true;
            s.InMiniRound[0] = true;
            s.InMiniRound[1] = true;
            s.Owner[0] = (byte)Team.Blue;
            s.TeamCanCap[Limits.TeamArray(0, (int)Team.Red)] = true;
        });

        // Point 1: neither game team may cap it (IsPointLocked,
        // hud_controlpointicons.cpp:414-433). Point 0: RED may, so it is open.
        Assert.True(source.Locked(1));
        Assert.False(source.Locked(0));
    }

    [Fact]
    public void The_row_cap_progress_advances_to_the_frame()
    {
        var rig = new ObjectiveRig();
        var source = new CapturePoints(rig.Source, rig.Engine);
        rig.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.CappingTeam[0] = (byte)Team.Blue;
            s.TeamInZone[0] = (byte)Team.Blue;
            s.NumTeamMembers[Limits.TeamArray(0, (int)Team.Blue)] = 1;
            s.TeamCapTime[Limits.TeamArray(0, (int)Team.Blue)] = 20f;
            s.LazyCapPerc[0] = 0.5f;
        }, now: 0f);

        // The adapter reads the model advanced to the clock the frame draws at.
        rig.Engine.Returns("Clock.Now", 2f);
        Assert.Equal(0.6f, source.CapProgress(0), 3);
        Assert.Equal(0.6f, rig.Source.CapProgress(0, 2f), 3);
    }

    [Fact]
    public void The_escort_row_reads_the_blue_train()
    {
        var rig = new ObjectiveRig();
        var escort = new Escort(rig.Source, rig.Engine);

        // Before the map's table arrives: no track, a stalled train.
        Assert.False(escort.Present);
        Assert.Equal(0, escort.Points);
        Assert.Equal(0f, escort.Progress);
        Assert.Equal(0, escort.Cappers);
        Assert.Equal(0, escort.SpeedLevel);
        Assert.False(escort.Alarm);

        rig.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.Visible[0] = true;
            s.Visible[1] = true;
            s.InMiniRound[0] = true;
            s.InMiniRound[1] = true;
            s.PathDistance[0] = 0f;
            s.PathDistance[1] = 0.5f;
            s.TrainProgress[3] = 0.4f;
            s.TrainCappers[3] = 2;
            s.TrainSpeedLevel[3] = 1;
            s.TrackAlarm[3] = true;
        });

        // After: the reads the escort draws from — blue's train (the
        // adapter's literal team 3, tf_hud_escort.cpp:445) over the mirror.
        Assert.True(escort.Present);          // a point carries a path distance
        Assert.Equal(2, escort.Points);
        Assert.True(escort.PointOnTrack(0));
        Assert.Equal(0.5f, escort.PathDistance(1));
        Assert.Equal(0.4f, escort.Progress);
        Assert.Equal(2, escort.Cappers);
        Assert.Equal(1, escort.SpeedLevel);
        Assert.True(escort.Alarm);
    }

    [Fact]
    public void A_multiple_train_map_gates_the_track_to_the_local_group()
    {
        var rig = new ObjectiveRig();
        var escort = new Escort(rig.Source, rig.Engine);
        rig.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.Visible[0] = true;
            s.Visible[1] = true;
            s.InMiniRound[0] = true;
            s.InMiniRound[1] = true;
            s.Group[0] = 0;   // the shared track
            s.Group[1] = 1;   // blue's own track (m_nTeam - 2)
        });

        // One train: every visible mini-round point stands on the track.
        Assert.False(rig.Source.MultipleTrains);
        Assert.True(escort.PointOnTrack(0));
        Assert.True(escort.PointOnTrack(1));

        // A tf_logic_multiple_escort on the map: only the local team's
        // group's points show (tf_hud_escort.cpp:1075-1080).
        rig.Arrive(s => s.MultipleTrains = true);

        Assert.True(rig.Source.MultipleTrains);
        Assert.True(escort.MultipleTrains);
        Assert.False(escort.PointOnTrack(0));
        Assert.True(escort.PointOnTrack(1));
    }

    [Fact]
    public void The_recede_countdown_reads_against_the_frame_clock()
    {
        var rig = new ObjectiveRig();
        var escort = new Escort(rig.Source, rig.Engine);
        rig.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.Visible[0] = true;
            s.InMiniRound[0] = true;
            s.TrainRecedeTime[3] = 12f;
        }, now: 10f);

        // The deadline minus the clock the frame draws at, floored at none.
        rig.Engine.Returns("Clock.Now", 10f);
        Assert.Equal(2f, escort.RecedeSeconds, 3);
        rig.Engine.Returns("Clock.Now", 20f);
        Assert.Equal(0f, escort.RecedeSeconds, 3);
    }

    /// <summary>A timer with the fields the row and the election read, so
    /// a fact chooses the clock it wants (the shape of
    /// <see cref="ClientRoundTimerTests"/>'s fake).</summary>
    private sealed class FakeTimer(
        int index,
        float remaining,
        bool showInHud = true,
        bool disabled = false,
        bool paused = false,
        bool inSetup = false,
        bool showTimeRemaining = false,
        int maxLength = 0) : TimerSource
    {
        public EntityHandle Handle => default;

        public int Index => index;

        public float TimeRemaining(float now) => remaining;

        public bool IsPaused => paused;

        public bool InSetup => inSetup;

        public bool ShowInHud => showInHud;

        public bool IsDisabled => disabled;

        public int TimerMaxLength => maxLength;

        public bool ShowTimeRemaining => showTimeRemaining;

        public bool IsStopWatch => false;

        public bool InCaptureWatchState => false;

        public float StopWatchTotalTime => 0f;

        public bool AutoCountdown => false;

        public int InitialLength => 0;

        public int SetupLength => 0;
    }
}
