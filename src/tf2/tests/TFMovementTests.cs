extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;

using Xunit;

using ClientData = client::TF2.Shared.Player.ITFPlayerData;
using ClientEngineFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientState = client::TF2.Shared.Player.TFPlayerNetState;

namespace TF2.Tests;

/// <summary>The class table both sides read, from the sealed class script fixtures.</summary>
internal static class TFMovementFixtures
{
    /// <summary>The engine read the rules' inputs arm makes, silent: the
    /// handle, observer and team words default so a fact of the walk itself
    /// is not asked to stub the engine. A fact of those arms hands its own
    /// engine with the answers stubbed.</summary>
    public static RecordingEngine LaxEngine()
    {
        var engine = new RecordingEngine();
        engine.Ignores("Players.GetHandle", "Players.GetObserverMode", "Players.GetTeam");
        return engine;
    }

    public static TFClassTable Server() => new(new RecordingLog(), TFClassTableTests.Files.Shipped());

    public static client::TF2.Shared.TFClassTable Client()
        => new(new client::SourceSharp.Abi.RecordingLog(), new ClientFiles(TFClassTableTests.Files.Shipped()));

    /// <summary>
    /// Every convar the movement rules read, declared at stock's defaults in
    /// one registry whose values round-trip (the named backend) — the holders
    /// a composition or a fact hands the rules; a fact that moves one declares
    /// its own registry the same way and hands that instead.
    /// </summary>
    public static ConVarRegistry ConVars()
    {
        var convars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>()));
        convars.RegisterTypes(
        [
            typeof(TFScoutAirDashCountConVar),
            typeof(TFSpyMaxCloakedSpeedConVar),
            typeof(TFClampBackSpeedConVar),
            typeof(TFClampBackSpeedMinConVar),
            typeof(TFPlayerMovementRestartFreezeConVar),
            typeof(TFMovementAircurrentFrictionMultConVar),
            typeof(TFMovementLostFootingRestickConVar),
            typeof(TFMovementLostFootingFrictionConVar),
            typeof(TFMaxChargeSpeedConVar),
            typeof(TFParachuteMaxSpeedXYConVar),
            typeof(TFParachuteMaxSpeedZConVar),
            typeof(TFParachuteMaxSpeedOnfireZConVar),
            typeof(TFParachuteAirControlConVar),
            typeof(TFClampAirducksConVar),
            typeof(TFDemomanChargeFrametimeScalingConVar),
        ]);
        return convars;
    }

    /// <summary>The client compilation's own copies of the same fourteen.</summary>
    public static client::SourceSharp.Abi.ConVarRegistry ClientConVars()
    {
        var convars = new client::SourceSharp.Abi.ConVarRegistry(new ClientClampVars());
        convars.RegisterTypes(
        [
            typeof(client::TF2.Shared.TFScoutAirDashCountConVar),
            typeof(client::TF2.Shared.TFSpyMaxCloakedSpeedConVar),
            typeof(client::TF2.Shared.TFClampBackSpeedConVar),
            typeof(client::TF2.Shared.TFClampBackSpeedMinConVar),
            typeof(client::TF2.Shared.TFPlayerMovementRestartFreezeConVar),
            typeof(client::TF2.Shared.TFMovementAircurrentFrictionMultConVar),
            typeof(client::TF2.Shared.TFMovementLostFootingRestickConVar),
            typeof(client::TF2.Shared.TFMovementLostFootingFrictionConVar),
            typeof(client::TF2.Shared.TFMaxChargeSpeedConVar),
            typeof(client::TF2.Shared.TFParachuteMaxSpeedXYConVar),
            typeof(client::TF2.Shared.TFParachuteMaxSpeedZConVar),
            typeof(client::TF2.Shared.TFParachuteMaxSpeedOnfireZConVar),
            typeof(client::TF2.Shared.TFParachuteAirControlConVar),
            typeof(client::TF2.Shared.TFClampAirducksConVar),
            typeof(client::TF2.Shared.TFDemomanChargeFrametimeScalingConVar),
        ]);
        return convars;
    }

    /// <summary>The client's backend: declared defaults, settable values — the server rig's ClampBackend.</summary>
    private sealed class ClientClampVars : client::SourceSharp.Abi.IConVarBackend
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

    /// <summary>A round state the rules read, fixed at construction.</summary>
    public sealed class RoundAt(TF2.Shared.Rules.RoundState state,
                                TFTeam winner = TFTeam.Unassigned) : TF2.Shared.Rules.ITFRoundStateView
    {
        public TF2.Shared.Rules.RoundState State { get; set; } = state;

        public TFTeam Winner { get; set; } = winner;
    }

    /// <summary>The client compilation's round state, for its container.</summary>
    private sealed class ClientRoundAt : client::TF2.Shared.Rules.ITFRoundStateView
    {
        public client::TF2.Shared.Rules.RoundState State { get; }
            = client::TF2.Shared.Rules.RoundState.RoundRunning;

        public client::TF2.Shared.TFTeam Winner => client::TF2.Shared.TFTeam.Unassigned;
    }

    /// <summary>Nobody on a team: the mask arm's inert answer, the base mask through.</summary>
    public sealed class Teamless : TF2.Shared.Rules.ITFPlayerTeam
    {
        public TF2.Shared.TFTeam Of(int playerIndex) => TF2.Shared.TFTeam.Unassigned;
    }

    /// <summary>One team for every player, the mask arm's facts' rig.</summary>
    public sealed class AllRed(TF2.Shared.TFTeam team = TF2.Shared.TFTeam.Red) : TF2.Shared.Rules.ITFPlayerTeam
    {
        public TF2.Shared.TFTeam Of(int playerIndex) => team;
    }

    /// <summary>The same, client side.</summary>
    private sealed class ClientTeamless : client::TF2.Shared.Rules.ITFPlayerTeam
    {
        public client::TF2.Shared.TFTeam Of(int playerIndex) => client::TF2.Shared.TFTeam.Unassigned;
    }

    /// <summary>One team for every player, the client compilation's copy.</summary>
    public sealed class ClientAllRed(client::TF2.Shared.TFTeam team = client::TF2.Shared.TFTeam.Red)
        : client::TF2.Shared.Rules.ITFPlayerTeam
    {
        public client::TF2.Shared.TFTeam Of(int playerIndex) => team;
    }

    /// <summary>No weapon in hand: the anim seam's empty read, as the interface's summary allows.</summary>
    public sealed class HoldsNothing : TF2.Shared.Anim.ITFAnimWeapons
    {
        public TF2.Shared.Anim.TFAnimWeapon? ActiveWeapon(int playerIndex) => null;
        public TF2.Shared.Anim.TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
    }

    /// <summary>The same, client side.</summary>
    private sealed class ClientHoldsNothing : client::TF2.Shared.Anim.ITFAnimWeapons
    {
        public client::TF2.Shared.Anim.TFAnimWeapon? ActiveWeapon(int playerIndex) => null;
        public client::TF2.Shared.Anim.TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
    }

    /// <summary>
    /// The server side's container over the rules: the class table and the
    /// carriers under their contracts, every convar the rules read at its
    /// default, the seams inert — each under the contract the constructor
    /// names, as the server's registration holds them.
    /// </summary>
    public static ServiceContainer ServerServices(Players players, ConVarRegistry? convars = null)
    {
        var vars = convars ?? ConVars();
        var services = new ServiceContainer();
        vars.PublishTo(services);
        var engine = LaxEngine();
        return services
            .AddInstance(Server())
            .AddInstance<ITFPlayerData>(players)
            .AddInstance<TF2.Shared.ITFWeaponOwners>(new TF2.Shared.NoTFWeaponOwners())
            .AddInstance(new TF2.Shared.Econ.AttributeHooks())
            .AddInstance<TF2.Shared.Rules.ITFRoundStateView>(new RoundAt(TF2.Shared.Rules.RoundState.RoundRunning))
            .AddInstance<TF2.Shared.Rules.ITFPlayerTeam>(new Teamless())
            .AddInstance<TF2.Shared.Anim.ITFAnimWeapons>(new HoldsNothing())
            .AddInstance<IEngineEntities>(engine)
            .AddInstance<IEnginePlayers>(engine);
    }

    /// <summary>The client composition's mirror: the client compilation's own types, the same shape.</summary>
    public static client::SourceSharp.Abi.ServiceContainer ClientServices(ClientData players,
        client::SourceSharp.Abi.ConVarRegistry? convars = null,
        client::TF2.Shared.Rules.ITFPlayerTeam? teams = null)
    {
        var vars = convars ?? ClientConVars();
        var services = new client::SourceSharp.Abi.ServiceContainer();
        vars.PublishTo(services);
        var engine = new client::SourceSharp.Abi.RecordingEngine();
        engine.Ignores("Players.GetHandle", "Players.GetObserverMode", "Players.GetTeam");
        return services
            .AddInstance(Client())
            .AddInstance<ClientData>(players)
            .AddInstance<client::TF2.Shared.ITFWeaponOwners>(new client::TF2.Shared.NoTFWeaponOwners())
            .AddInstance(new client::TF2.Shared.Econ.AttributeHooks())
            .AddInstance<client::TF2.Shared.Rules.ITFRoundStateView>(new ClientRoundAt())
            .AddInstance(teams ?? new ClientTeamless())
            .AddInstance<client::TF2.Shared.Anim.ITFAnimWeapons>(new ClientHoldsNothing())
            .AddInstance<client::SourceSharp.Abi.IEngineEntities>(engine)
            .AddInstance<client::SourceSharp.Abi.IEnginePlayers>(engine);
    }

    /// <summary>
    /// The rules built directly, every constructor dependency handed in as the
    /// sides' registrations hold them: the convars at stock's defaults
    /// (<see cref="ConVars"/>, or the caller's registry when the fact moves
    /// one), the seams the caller names else inert ones.
    /// </summary>
    public static TFMovementRules Rules(ITFPlayerData players,
                                        ConVarRegistry? convars = null,
                                        TF2.Shared.ITFWeaponOwners? owners = null,
                                        TF2.Shared.Econ.AttributeHooks? hooks = null,
                                        TF2.Shared.Rules.ITFRoundStateView? rounds = null,
                                        IEnginePlayers? engines = null,
                                        TF2.Shared.Anim.ITFAnimWeapons? weapons = null,
                                        IEngineEntities? entities = null,
                                        TF2.Shared.Rules.ITFPlayerTeam? teams = null)
    {
        var vars = convars ?? ConVars();
        return new(Server(), players,
                   vars.Declared<TFScoutAirDashCountConVar>(),
                   vars.Declared<TFSpyMaxCloakedSpeedConVar>(),
                   owners ?? new TF2.Shared.NoTFWeaponOwners(),
                   hooks ?? new TF2.Shared.Econ.AttributeHooks(),
                   vars.Declared<TFClampBackSpeedConVar>(),
                   vars.Declared<TFClampBackSpeedMinConVar>(),
                   rounds ?? new RoundAt(TF2.Shared.Rules.RoundState.RoundRunning),
                   engines ?? LaxEngine(),
                   vars.Declared<TFPlayerMovementRestartFreezeConVar>(),
                   vars.Declared<TFMovementAircurrentFrictionMultConVar>(),
                   vars.Declared<TFMovementLostFootingRestickConVar>(),
                   vars.Declared<TFMovementLostFootingFrictionConVar>(),
                   vars.Declared<TFMaxChargeSpeedConVar>(),
                   weapons ?? new HoldsNothing(),
                   entities ?? new RecordingEngine(),
                   vars.Declared<TFParachuteMaxSpeedXYConVar>(),
                   vars.Declared<TFParachuteMaxSpeedZConVar>(),
                   vars.Declared<TFParachuteMaxSpeedOnfireZConVar>(),
                   teams ?? new Teamless(),
                   vars.Declared<TFClampAirducksConVar>(),
                   vars.Declared<TFParachuteAirControlConVar>(),
                   vars.Declared<TFDemomanChargeFrametimeScalingConVar>());
    }

    private sealed class ClientFiles(TFClassTableTests.Files files) : ClientEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    /// <summary>One player's carrier state, by index, for the rules to read and write.</summary>
    public sealed class Players : ITFPlayerData
    {
        public Dictionary<int, TFPlayerNetState> States { get; } = [];

        public TFPlayerNetState Add(int index, TFClass playerClass)
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.OwnerIndex = index;
            state.SetClass(playerClass);
            States[index] = state;
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => States.GetValueOrDefault(playerIndex);
        public TFPlayerNetState? Networked(int playerIndex) => States.GetValueOrDefault(playerIndex);
    }

    public sealed class ClientPlayers : ClientData
    {
        public Dictionary<int, ClientState> States { get; } = [];

        public ClientState Add(int index, TFClass playerClass)
        {
            var state = new ClientState();
            state.BindLocal();
            state.OwnerIndex = index;
            state.PlayerClass = (int)playerClass;
            States[index] = state;
            return state;
        }

        public ClientState? Predicted(int playerIndex) => States.GetValueOrDefault(playerIndex);
        public ClientState? Networked(int playerIndex) => States.GetValueOrDefault(playerIndex);
    }
}

/// <summary>
/// <see cref="TFMaxSpeed"/>: <c>CTFPlayer::TeamFortress_CalculateMaxSpeed</c>'s
/// milestone arms over the real class scripts.
/// </summary>
public sealed class TFMaxSpeedTests
{
    private static readonly TFClassTable Classes = TFMovementFixtures.Server();

    private static float Speed(TFClass c, TFCondMask conditions = default, TFClass disguise = TFClass.Undefined,
                               TFWeaponId weapon = TFWeaponId.None, float speedMod = 1f, float cloakedCap = 999f,
                               AttributeHooks? attribs = null, IAttributeProvider? manager = null,
                               TFStunFlags stunFlags = TFStunFlags.None, bool carrying = false,
                               int healTarget = -1, Func<int, TFPeerInput?>? peers = null, int self = -1,
                               float hypeScale = 1f, ObserverMode observer = ObserverMode.None,
                               float teamWinScale = 1f)
        => TFMaxSpeed.Calculate(Classes, new TFMoveInputs(c, conditions, disguise, weapon, speedMod, stunFlags,
                                                          carrying, healTarget,
                                                          HypeSpeedScale: hypeScale, Observer: observer,
                                                          TeamWinScale: teamWinScale),
                                cloakedCap, attribs ?? Hooks, manager, peers ?? (_ => null), self, null);

    /// <summary>A peer table over bare classes, the patient's side of the match,
    /// over the walk's own empty hooks and no manager or held item.</summary>
    private static Func<int, TFPeerInput?> Peers(params (int Index, TFClass Class)[] edges)
        => index => edges.Where(e => e.Index == index)
                         .Select(e => new TFPeerInput(e.Index, new TFMoveInputs(e.Class, default), Hooks, null, null))
                         .FirstOrDefault();

    /// <summary>A percentage attribute of the hook class, on a fresh manager as the player's own.</summary>
    private static AttributeContainer ManagerWith(string hookClass, float value)
    {
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5001, "attr 5001", hookClass,
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return manager;
    }

    [Theory]
    [InlineData(TFClass.Scout, 400f)]
    [InlineData(TFClass.Soldier, 240f)]
    [InlineData(TFClass.HeavyWeapons, 230f)]
    [InlineData(TFClass.Spy, 320f)]
    [InlineData(TFClass.Medic, 320f)]
    public void A_class_runs_at_its_scripts_speed(TFClass c, float expected)
        => Assert.Equal(expected, Speed(c));

    [Fact]
    public void Every_class_without_a_condition_runs_at_its_table_value_unscaled()
    {
        for (var c = TFClass.Scout; c < TFClass.CountAll; c++)
            Assert.Equal(Classes[c].MaxSpeed, Speed(c));
    }

    [Fact]
    public void An_aiming_heavy_is_held_to_a_flat_110()
        => Assert.Equal(TFMaxSpeed.HeavyAiming, Speed(TFClass.HeavyWeapons, TFCondMask.Of(TFCond.Aiming)));

    [Fact]
    public void An_aiming_sniper_is_held_to_80()
        => Assert.Equal(80f, Speed(TFClass.Sniper, TFCondMask.Of(TFCond.Aiming)));

    [Fact]
    public void An_aiming_bow_is_held_to_160()
        => Assert.Equal(160f, Speed(TFClass.Sniper, TFCondMask.Of(TFCond.Aiming), weapon: TFWeaponId.CompoundBow));

    [Fact]
    public void Aiming_never_speeds_a_class_up()
    {
        // 110 is the heavy's cap, 80 everyone else's: a class already slower keeps its own.
        Assert.Equal(80f, Speed(TFClass.Soldier, TFCondMask.Of(TFCond.Aiming)));
        Assert.True(Speed(TFClass.Soldier) > 80f, "the stimulus is a real cap");
    }

    [Fact]
    public void The_hype_scale_is_the_walks_last_multiply()
    {
        // tf_player_shared.cpp:11085, the scout's arm after the item hooks.
        Assert.Equal(400f * TFHype.SpeedScaleMax, Speed(TFClass.Scout, hypeScale: TFHype.SpeedScaleMax), 0.5f);
        Assert.Equal(400f, Speed(TFClass.Scout));
    }

    [Fact]
    public void A_disguised_spy_is_no_faster_than_the_disguise()
    {
        var disguised = TFCondMask.Of(TFCond.Disguised);

        Assert.Equal(230f, Speed(TFClass.Spy, disguised, TFClass.HeavyWeapons));
        Assert.Equal(320f, Speed(TFClass.Spy, disguised, TFClass.Scout));   // a min, never a raise
    }

    [Fact]
    public void A_stealthed_disguise_does_not_slow_the_spy()
    {
        var both = TFCondMask.Of([TFCond.Disguised, TFCond.Stealthed]);

        Assert.Equal(320f, Speed(TFClass.Spy, both, TFClass.HeavyWeapons));
        Assert.Equal(320f, Speed(TFClass.Spy, TFCondMask.Of([TFCond.Disguised, TFCond.StealthedUserBuffFading]), TFClass.HeavyWeapons));
    }

    [Fact]
    public void Disguised_as_a_dispenser_stands_still()
        => Assert.Equal(0f, Speed(TFClass.Spy, TFCondMask.Of(TFCond.DisguisedAsDispenser)));

    [Fact]
    public void Stealth_caps_at_the_cloaked_speed_convar()
    {
        Assert.Equal(320f, Speed(TFClass.Spy, TFCondMask.Of(TFCond.Stealthed)));
        Assert.Equal(100f, Speed(TFClass.Spy, TFCondMask.Of(TFCond.Stealthed), cloakedCap: 100f));
    }

    [Fact]
    public void The_active_weapons_speed_mod_scales_the_answer()
    {
        Assert.Equal(200f, Speed(TFClass.Scout, weapon: TFWeaponId.Scattergun, speedMod: 0.5f));
        Assert.Equal(400f, Speed(TFClass.Scout, speedMod: 0.5f));   // no weapon, no mod
    }

    [Fact]
    public void No_class_answers_stocks_one()
        => Assert.Equal(1f, Speed(TFClass.Undefined));

    [Fact]
    public void A_charging_demoman_runs_at_the_charge_speed()
    {
        // tf_player_shared.cpp:10970-10973: the charge's assignment, past the
        // sword's mod — a shield charge is 750 however the items would scale it.
        var charging = TFCondMask.Of(TFCond.ShieldCharge);
        Assert.Equal(TFMaxSpeed.MaxChargeSpeed, Speed(TFClass.Demoman, charging));
        Assert.Equal(TFMaxSpeed.MaxChargeSpeed,
                     Speed(TFClass.Demoman, charging, speedMod: 0.5f));   // the sword's mod under it
        Assert.NotEqual(TFMaxSpeed.MaxChargeSpeed, Speed(TFClass.Soldier, charging));   // demoman's gate
    }

    // ---- the attribute hooks (tf_player_shared.cpp:10913, 11002-11003) -----

    private static readonly AttributeHooks Hooks = new();

    [Fact]
    public void A_player_move_speed_attribute_scales_the_final_speed()
    {
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(400f, Speed(TFClass.Scout, attribs: Hooks));   // no manager, the walk unchanged
        Assert.Equal(200f, Speed(TFClass.Scout, attribs: Hooks, manager: manager));
    }

    [Fact]
    public void The_aiming_attribute_scales_the_cap_while_aiming()
    {
        var aiming = TFCondMask.Of(TFCond.Aiming);
        var manager = ManagerWith(TFMaxSpeed.AimSpeedAttrib, 0.5f);

        Assert.Equal(55f, Speed(TFClass.HeavyWeapons, aiming, attribs: Hooks, manager: manager));
        Assert.Equal(110f, Speed(TFClass.HeavyWeapons, aiming, attribs: Hooks));   // the cap without the attribute
    }

    [Fact]
    public void The_aiming_attribute_waits_for_the_aiming()
    {
        // The hook lives inside the aiming branch (stock :10913), so a soldier
        // at full run is not slowed by an aiming attribute.
        var manager = ManagerWith(TFMaxSpeed.AimSpeedAttrib, 0.5f);

        Assert.Equal(240f, Speed(TFClass.Soldier, attribs: Hooks, manager: manager));
    }

    [Fact]
    public void The_speed_hook_runs_after_the_weapons_own_mod()
    {
        // Stock's order: the active weapon's speed mod (:10959), then
        // mult_player_movespeed (:11003). Both percentage, the walk composes
        // them in that order on the one value.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(100f, Speed(TFClass.Scout, weapon: TFWeaponId.Scattergun, speedMod: 0.5f,
                                 attribs: Hooks, manager: manager));
    }

    // ---- the held weapon's hook (tf_player_shared.cpp:11058-11063) ----------

    [Fact]
    public void TheGlovesSpeedTheActiveCarrier()
    {
        // mult_player_movespeed_active (tf_player_shared.cpp:11058-11063):
        // CALL_ATTRIB_HOOK_FLOAT_ON_OTHER on the ACTIVE weapon — the Gloves
        // item, not the player's manager. The heavy's script speed of 230 is
        // scaled only when the held item carries the hook.
        var held = ManagerWith(TFMaxSpeed.ActiveSpeedAttrib, 1.3f);
        var playerManager = ManagerWith(TFMaxSpeed.ActiveSpeedAttrib, 1.3f);

        Assert.Equal(230f, TFMaxSpeed.Calculate(Classes, new TFMoveInputs(TFClass.HeavyWeapons, default), 999f,
                                                Hooks, playerManager, _ => null, -1, null));
        Assert.Equal(299f, TFMaxSpeed.Calculate(Classes, new TFMoveInputs(TFClass.HeavyWeapons, default), 999f,
                                                Hooks, null, _ => null, -1, held));
    }

    // ---- the ÜberCharge's own speed (tf_player_shared.cpp:11043-11056) ------

    [Fact]
    public void TheOverdoseSpeedsTheMedicByTheCharge()
    {
        // mult_player_movespeed_resource_level (tf_player_shared
        // .cpp:11043-11056): the ACTIVE weapon's hook, the medic's owned
        // medigun's charge remapped 0..1 onto 1..mod — the Overdose's 1.2 is
        // half ridden at a half meter, on the medic's 320.
        var held = ManagerWith(TFMaxSpeed.ResourceLevelSpeedAttrib, 1.2f);

        Assert.Equal(320f * 1.1f, TFMaxSpeed.Calculate(Classes,
            new TFMoveInputs(TFClass.Medic, default, MedicCharge: 0.5f), 999f,
            Hooks, null, _ => null, -1, held));
    }

    [Fact]
    public void TheOverdoseNeedsTheMedicAndTheMeter()
    {
        // The class arm is stock's medic gate (:11048) and the meter the
        // medigun's: an empty meter remaps to one, and a soldier holding the
        // gun is no arm at all.
        var held = ManagerWith(TFMaxSpeed.ResourceLevelSpeedAttrib, 1.2f);

        Assert.Equal(320f, TFMaxSpeed.Calculate(Classes,
            new TFMoveInputs(TFClass.Medic, default), 999f, Hooks, null, _ => null, -1, held));
        Assert.Equal(240f, TFMaxSpeed.Calculate(Classes,
            new TFMoveInputs(TFClass.Soldier, default, MedicCharge: 1f), 999f, Hooks, null, _ => null, -1, held));
    }

    // ---- the carried speed boost (tf_player_shared.cpp:10919-10927) ----------

    [Fact]
    public void The_speed_boost_adds_under_its_cap()
    {
        // :10925: the boost is the lesser of 40 percent and
        // tf_whip_speed_increase (the convar's default 105) — the heavy's
        // 230 takes its 92, under the cap.
        Assert.Equal(322f, Speed(TFClass.HeavyWeapons, TFCondMask.Of(TFCond.SpeedBoost)), 4);
    }

    [Fact]
    public void The_speed_boost_caps_at_the_whip_increase()
    {
        // The scout's 400: 40 percent is 160, past the cap, so the MIN holds
        // the bonus at 105 (:10925).
        Assert.Equal(505f, Speed(TFClass.Scout, TFCondMask.Of(TFCond.SpeedBoost)), 4);
    }

    [Fact]
    public void The_speed_boost_leaves_a_stationed_player_standing()
    {
        // The maxfbspeed > 0 gate (:10923): a player meant to stand still —
        // the dispenser disguise's zero — keeps it under the boost.
        Assert.Equal(0f, Speed(TFClass.Scout, TFCondMask.Of([TFCond.DisguisedAsDispenser, TFCond.SpeedBoost])), 4);
    }

    [Fact]
    public void No_boost_leaves_the_walk_as_it_came()
    {
        Assert.Equal(230f, Speed(TFClass.HeavyWeapons), 4);
    }

    // ---- the loser state's slow (tf_player_shared.cpp:10989-11000) ----------

    [Fact]
    public void A_loser_state_stun_halves_the_speed()
    {
        var stunned = TFCondMask.Of(TFCond.Stunned);

        Assert.Equal(200f, Speed(TFClass.Scout, stunned, stunFlags: TFStunFlags.LoserState));
    }

    [Fact]
    public void A_stun_from_a_trigger_shames_less_than_the_walk()
    {
        var stunned = TFCondMask.Of(TFCond.Stunned);
        var byTrigger = TFStunFlags.LoserState | TFStunFlags.ByTrigger;

        Assert.Equal(300f, Speed(TFClass.Scout, stunned, stunFlags: byTrigger));
    }

    [Fact]
    public void A_stun_without_the_loser_state_bit_does_not_slow()
    {
        var stunned = TFCondMask.Of(TFCond.Stunned);

        // The movement and controls stuns slow through their own arms.
        Assert.Equal(400f, Speed(TFClass.Scout, stunned, stunFlags: TFStunFlags.Movement));
        Assert.Equal(400f, Speed(TFClass.Scout, stunned, stunFlags: TFStunFlags.Movement | TFStunFlags.Controls));
    }

    [Fact]
    public void The_stun_slow_is_gated_on_the_stunned_condition()
        // The word outliving the condition (the tick that clears it has not
        // run) is stock's InCond arm: no condition, no slow.
        => Assert.Equal(400f, Speed(TFClass.Scout, stunFlags: TFStunFlags.LoserState));

    [Fact]
    public void A_halloween_bomb_head_spares_the_stun_slow()
    {
        var both = TFCondMask.Of([TFCond.Stunned, TFCond.HalloweenBombHead]);

        Assert.Equal(400f, Speed(TFClass.Scout, both, stunFlags: TFStunFlags.LoserState));
    }

    [Fact]
    public void The_stun_slow_waits_for_the_speed_hook()
    {
        // Stock's order: the stun (:10989-11000) before mult_player_movespeed
        // (:11003) — the hook walks the stunned value.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(100f, Speed(TFClass.Scout, TFCondMask.Of(TFCond.Stunned),
                                 stunFlags: TFStunFlags.LoserState, attribs: Hooks, manager: manager));
    }

    // ---- the carried building's toll (tf_player_shared.cpp:10976-10987) -----

    [Fact]
    public void A_carried_building_takes_a_tenth()
        => Assert.Equal(360f, Speed(TFClass.Scout, carrying: true));

    [Fact]
    public void A_halloween_bomb_head_spares_the_carry_toll()
    {
        var bombHead = TFCondMask.Of(TFCond.HalloweenBombHead);

        Assert.Equal(400f, Speed(TFClass.Scout, bombHead, carrying: true));
    }

    [Fact]
    public void The_carry_toll_stacks_with_the_stun_before_the_hook()
    {
        // Stock's order inside bAllowSlowing: the carry (:10983-10987), then
        // the stun (:10989-11000), then mult_player_movespeed (:11003) —
        // 400 x 0.9 x 0.5 x 0.5.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(90f, Speed(TFClass.Scout, TFCondMask.Of(TFCond.Stunned), stunFlags: TFStunFlags.LoserState,
                                 attribs: Hooks, manager: manager, carrying: true));
    }

    // ---- the medic's match (tf_player_shared.cpp:11029-11045) ---------------

    [Fact]
    public void A_medic_matches_a_faster_patients_speed()
    {
        var peers = Peers((4, TFClass.Scout));

        Assert.Equal(400f, Speed(TFClass.Medic, healTarget: 4, peers: peers, self: 3));
    }

    [Fact]
    public void The_match_is_a_max_never_a_demotion()
    {
        // A slower patient leaves the medic's own walk standing; the arms the
        // patient runs are the patient's, here the stun's.
        var stunned = TFCondMask.Of(TFCond.Stunned);

        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 5, peers: Peers((5, TFClass.Soldier)), self: 3));
        Assert.Equal(320f, Speed(TFClass.Medic, stunned, healTarget: 4, self: 3,
                                 peers: i => i == 4
                                     ? new TFPeerInput(4, new TFMoveInputs(TFClass.Scout, stunned,
                                                                           StunFlags: TFStunFlags.LoserState),
                                                       Hooks, null, null)
                                     : null));
    }

    [Fact]
    public void A_medic_without_a_patient_or_a_roster_runs_alone()
    {
        // No target on the medigun, no lookup, a target the side never saw:
        // stock's null patient arm, each way.
        Assert.Equal(320f, Speed(TFClass.Medic));
        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 4));
        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 4, peers: _ => null, self: 3));
    }

    [Fact]
    public void Two_medics_matching_each_other_end_at_the_default()
    {
        // Stock's re-entry guard (:10879-10888): the second walk of a player
        // already in flight answers the class table, so the cycle closes at
        // B's own 320, not A's hooked 160, and A ends at 320 not 160.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 4, attribs: Hooks, manager: manager,
                                 peers: Peers((3, TFClass.Medic), (4, TFClass.Medic)), self: 3));
    }

    [Fact]
    public void A_medic_healing_himself_runs_at_the_table()
    {
        // The self-heal is the re-entry it exists for: the hooked 160 against
        // his own in-flight default, the table's 320 out.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(320f, Speed(TFClass.Medic, healTarget: 3, attribs: Hooks, manager: manager,
                                 peers: Peers((3, TFClass.Medic)), self: 3));
    }

    [Fact]
    public void The_medic_match_waits_for_the_speed_hook()
    {
        // Stock's order: mult_player_movespeed (:11003) BEFORE the match
        // (:11029) — the hooked value is what the patient's raise lifts.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(400f, Speed(TFClass.Medic, healTarget: 4, attribs: Hooks, manager: manager,
                                 peers: Peers((4, TFClass.Scout)), self: 3));
    }

    // ---- the heavy's berzerker (tf_player_shared.cpp:11065-11078) ----------

    [Fact]
    public void An_energy_buffed_heavy_runs_at_1_3_times()
        => Assert.Equal(299f, Speed(TFClass.HeavyWeapons, TFCondMask.Of(TFCond.EnergyBuff)));

    [Fact]
    public void The_berzerker_cap_holds_the_gru_below_1_35_times_the_class()
    {
        // The cap (:11068, :11072-11076): another modifier — the G.R.U.'s
        // mult_player_movespeed 1.5, hooked before the buff as stock's order
        // runs — lands on 230 x 1.5 x 1.3 = 448.5, held at 230 x 1.35 = 310.5.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 1.5f);

        Assert.Equal(310.5f, Speed(TFClass.HeavyWeapons, TFCondMask.Of(TFCond.EnergyBuff),
                                   attribs: Hooks, manager: manager));
    }

    [Fact]
    public void The_berzerker_cap_never_lifts_a_slow_heavy()
    {
        // The cap is a ceiling, not a raise (:11072): a slowed heavy's buffed
        // 0.5 x 1.3 stays under it.
        var manager = ManagerWith(TFMaxSpeed.MoveSpeedAttrib, 0.5f);

        Assert.Equal(149.5f, Speed(TFClass.HeavyWeapons, TFCondMask.Of(TFCond.EnergyBuff),
                                   attribs: Hooks, manager: manager));
    }

    // ---- the observer's head arm (tf_player_shared.cpp:10844-10851) --------

    [Fact]
    public void A_roaming_observer_runs_at_the_scouts_speed()
        => Assert.Equal(400f, Speed(TFClass.HeavyWeapons, observer: ObserverMode.Roaming));

    [Fact]
    public void Any_other_observer_stands_and_skips_the_class_arms()
    {
        // The fixed spectator's 0, ahead of the class check: an undefined
        // class is stock's other early return, not this one.
        Assert.Equal(0f, Speed(TFClass.HeavyWeapons, observer: ObserverMode.Fixed));
        Assert.Equal(0f, Speed(TFClass.Undefined, observer: ObserverMode.DeathCam));
    }

    // ---- the team-win bonus (tf_player_shared.cpp:10938-10954) --------------

    [Fact]
    public void The_winner_runs_110_and_the_loser_90_in_the_bonus_time()
    {
        Assert.Equal(440f, Speed(TFClass.Scout, teamWinScale: 1.1f));
        Assert.Equal(360f, Speed(TFClass.Scout, teamWinScale: 0.9f));
    }

    [Fact]
    public void The_bonus_lands_after_the_cloak_cap()
    {
        // Stock's order: the stealth cap (:10930-10936) before the bonus
        // (:10947) — the capped 300 scales to 330, not capped again.
        Assert.Equal(330f, Speed(TFClass.Scout, TFCondMask.Of(TFCond.Stealthed),
                                 teamWinScale: 1.1f, cloakedCap: 300f));
    }

    [Fact]
    public void The_bonus_scale_is_the_rounds_word()
    {
        // The decision the rules feed the walk: TeamWin pays the winner 1.1
        // and the rest 0.9; any other state, or an unassigned winner, is
        // stock's no-scale arm.
        Assert.Equal(1.1f, TFMaxSpeed.TeamWinScale(TF2.Shared.Rules.RoundState.TeamWin, TFTeam.Red, (int)TFTeam.Red));
        Assert.Equal(0.9f, TFMaxSpeed.TeamWinScale(TF2.Shared.Rules.RoundState.TeamWin, TFTeam.Red, (int)TFTeam.Blue));
        Assert.Equal(1f, TFMaxSpeed.TeamWinScale(TF2.Shared.Rules.RoundState.RoundRunning, TFTeam.Red, (int)TFTeam.Red));
        Assert.Equal(1f, TFMaxSpeed.TeamWinScale(TF2.Shared.Rules.RoundState.TeamWin, TFTeam.Unassigned, (int)TFTeam.Red));
    }

    // ---- the Quick-Fix's charge arm (tf_player_shared.cpp:11021-11025) -----

    /// <summary>A medigun mode on the player's manager, the item's
    /// <c>set_weapon_mode</c> as the medigun's own arms read it.</summary>
    private static AttributeContainer MedigunMode(int mode)
    {
        var def = new AttributeDefinition(30047, "weapon mode", TFMaxSpeed.MedigunModeAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
            false, false, AttributeValueType.Default, MergedKeys.Block("mode"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, mode));
        return manager;
    }

    [Fact]
    public void A_quick_fix_medic_on_a_charging_patient_runs_at_the_charge_speed()
    {
        // The Quick-Fix attach (:11021-11024): the healing gun is a Quick-Fix
        // and the patient charges — tf_max_charge_speed outright, the
        // patient's own walk (a soldier's, 240) unmatched. Stock names demos;
        // its check is the cond alone, no class.
        var charging = TFCondMask.Of(TFCond.ShieldCharge);

        Assert.Equal(TFMaxSpeed.MaxChargeSpeed,
                     Speed(TFClass.Medic, attribs: Hooks, manager: MedigunMode(TFMaxSpeed.QuickFixMedigunMode),
                           healTarget: 4, self: 3,
                           peers: i => i == 4
                               ? new TFPeerInput(4, new TFMoveInputs(TFClass.Soldier, charging), Hooks, null, null)
                               : null));
    }

    [Fact]
    public void A_stock_medigun_on_the_same_demo_matches_the_demos_own_speed()
    {
        // The plain gun's row of the same conditional (:11024): no mode 2, no
        // attach — the patient's own walk, here a charging scout's 400 over
        // the medic's 320, the attach's 750 not taken.
        var charging = TFCondMask.Of(TFCond.ShieldCharge);
        Func<int, TFPeerInput?> peers = i => i == 4
            ? new TFPeerInput(4, new TFMoveInputs(TFClass.Scout, charging), Hooks, null, null)
            : null;

        Assert.Equal(400f, Speed(TFClass.Medic, healTarget: 4, self: 3, peers: peers));
    }

    [Fact]
    public void The_quick_fix_attach_waits_for_the_patients_charge()
    {
        // The cond half of the same conditional (:11022): the Quick-Fix
        // without TF_COND_SHIELD_CHARGE on the patient falls back to the
        // walk, the scout's 400.
        Assert.Equal(400f, Speed(TFClass.Medic, attribs: Hooks,
                                 manager: MedigunMode(TFMaxSpeed.QuickFixMedigunMode),
                                 healTarget: 4, self: 3, peers: Peers((4, TFClass.Scout))));
    }
}

/// <summary><see cref="TFJump"/>: <c>CTFPlayer::CanAirDash</c> and <c>CTFGameMovement::AirDash</c>'s speed.</summary>
public sealed class TFJumpTests
{
    [Fact]
    public void A_scout_dashes_once_and_the_next_press_is_refused()
    {
        Assert.Equal(268.3281573f, TFJump.AirDash(TFClass.Scout, default, airDash: 0, dashCount: 1, null, JumpHooks, null));
        Assert.Equal(0f, TFJump.AirDash(TFClass.Scout, default, airDash: 1, dashCount: 1, null, JumpHooks, null));
    }

    [Fact]
    public void A_soldier_never_dashes()
        => Assert.Equal(0f, TFJump.AirDash(TFClass.Soldier, default, airDash: 0, dashCount: 1, null, JumpHooks, null));

    [Fact]
    public void The_dash_count_convar_allows_more()
    {
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, airDash: 1, dashCount: 2, null, JumpHooks, null));
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, airDash: 2, dashCount: 2, null, JumpHooks, null));
    }

    // ---- the Atomizer's third-dash hold (tf_player_shared.cpp:12867-12873) ---

    /// <summary>The held weapon's answer needs only a non-null item; the hold
    /// reads no attribute.</summary>
    private static AttributeContainer HeldWeapon() => new();

    /// <summary>The dash that would be the third is held while the weapon has been
    /// in the hands less than seven-tenths of a second (:12871).</summary>
    [Fact]
    public void A_freshly_held_weapon_holds_the_third_dash()
    {
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, airDash: 1, dashCount: 2, HeldWeapon(),
                                       JumpHooks, null, secondsSinceDeploy: 0.5f));
    }

    /// <summary>Past the seven-tenths the same press dashes again (:12871's
    /// comparison): the hold is the weapon's age, not a permanent lock.</summary>
    [Fact]
    public void A_settled_weapon_gives_the_third_dash_back()
    {
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, airDash: 1, dashCount: 2, HeldWeapon(),
                                      JumpHooks, null, secondsSinceDeploy: 0.8f));
    }

    /// <summary>The earlier dashes are never held (:12871, the count of dashes so
    /// far): the first dash answers at once however fresh the hands are, and so does
    /// a hand with no weapon — stock's null <c>pTFActiveWeapon</c> skips the arm.</summary>
    [Fact]
    public void The_first_dash_never_meets_the_hold()
    {
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, airDash: 0, dashCount: 2, HeldWeapon(),
                                      JumpHooks, null, secondsSinceDeploy: 0f));
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, airDash: 1, dashCount: 2, null,
                                      JumpHooks, null, secondsSinceDeploy: 0f));
    }

    [Fact]
    public void The_soda_poppers_hype_allows_five()
    {
        var hype = TFCondMask.Of(TFCond.SodapopperHype);

        Assert.True(TFJump.CanAirDash(TFClass.Scout, hype, airDash: 4, dashCount: 1, null, JumpHooks, null));
        Assert.False(TFJump.CanAirDash(TFClass.Scout, hype, airDash: 5, dashCount: 1, null, JumpHooks, null));
    }

    [Fact]
    public void The_halloween_conditions_override_the_class()
    {
        Assert.True(TFJump.CanAirDash(TFClass.Soldier, TFCondMask.Of(TFCond.HalloweenSpeedBoost), 9, 1, null, JumpHooks, null));
        Assert.False(TFJump.CanAirDash(TFClass.Scout, TFCondMask.Of(TFCond.HalloweenKart), 0, 1, null, JumpHooks, null));
    }

    // ---- the no-dash attribute (tf_player_shared.cpp:12875-12878) ----------

    private static readonly AttributeHooks JumpHooks = new();

    private static AttributeContainer JumpManager(int value)
    {
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5003, "attr 5003", TFJump.DoubleJumpDisabledAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return manager;
    }

    [Fact]
    public void A_double_jump_disabled_player_is_refused_the_first_dash_too()
    {
        var manager = JumpManager(1);

        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, 0, 1, null, JumpHooks, null));   // no manager, the hook unset
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 0, 1, null, JumpHooks, manager));

        // The dash answers 0: no velocity, the jump refused.
        Assert.Equal(0f, TFJump.AirDash(TFClass.Scout, default, 0, 1, null, JumpHooks, manager));
    }

    [Fact]
    public void Only_a_one_disables_the_dash()
    {
        // Stock's `if ( 1 == iNoAirDash )`: any other value leaves the dash.
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, 0, 1, null, JumpHooks,
                                      JumpManager(2)));
    }

    // ---- the active weapon's dash count (tf_player_shared.cpp:12860-12864) ----

    private static AttributeContainer WeaponAttrib(string name, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(5005, "attr 5005", name,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return item;
    }

    [Fact]
    public void The_active_weapons_dash_count_buys_the_second_dash()
    {
        var weapon = WeaponAttrib(TFJump.AirDashCountAttrib, 1);

        // The convar alone spent: the weapon's count is still to come.
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 1, 1, null, JumpHooks, null));
        Assert.True(TFJump.CanAirDash(TFClass.Scout, default, 1, 1, weapon, JumpHooks, null));
        Assert.Equal(TFJump.DashSpeed, TFJump.AirDash(TFClass.Scout, default, 1, 1, weapon, JumpHooks, null));

        // And the raised count is spent in turn.
        Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 2, 1, weapon, JumpHooks, null));
    }

    [Fact]
    public void Without_an_active_weapon_the_count_is_the_convars()
        => Assert.False(TFJump.CanAirDash(TFClass.Scout, default, 1, 1, null, JumpHooks, null));

    // ---- the jump-height attributes (tf_gamemovement.cpp:997-1005, 1023) ----

    [Fact]
    public void The_players_jump_height_scales_the_dash()
    {
        var manager = WeaponAttrib(TFJump.JumpHeightAttrib, 0.5f);

        Assert.Equal(TFJump.DashSpeed * 1.5f,
                     TFJump.AirDash(TFClass.Scout, default, 0, 1, null, JumpHooks, manager));
    }

    [Fact]
    public void The_active_weapons_jump_height_scales_on_top()
    {
        var weapon = WeaponAttrib(TFJump.JumpHeightFromWeaponAttrib, 0.5f);

        // Stock's order: the passive walk, then the weapon's over its result.
        Assert.Equal(TFJump.DashSpeed * 1.5f,
                     TFJump.AirDash(TFClass.Scout, default, 0, 1, weapon, JumpHooks, null));
        Assert.Equal(TFJump.DashSpeed * 2f,
                     TFJump.AirDash(TFClass.Scout, default, 0, 1, weapon, JumpHooks,
                                    WeaponAttrib(TFJump.JumpHeightAttrib, 0.5f)));
    }

    [Fact]
    public void A_refused_dash_has_no_height_to_scale()
        => Assert.Equal(0f, TFJump.AirDash(TFClass.Soldier, default, 0, 1,
                                           WeaponAttrib(TFJump.JumpHeightAttrib, 1f), JumpHooks, null));
}

/// <summary>
/// <see cref="TFMovementRules"/>: the one <c>IMovementRules</c> owner, deciding
/// from the carrier's state only, the same on both sides.
/// </summary>
public sealed class TFMovementRulesTests
{
    private const int Index = 2;

    private static (TFMovementRules Rules, TFMovementFixtures.Players Players) Make()
    {
        var players = new TFMovementFixtures.Players();
        return (TFMovementFixtures.Rules(players), players);
    }

    [Fact]
    public void The_constants_are_the_tf_view_vectors()
    {
        MovementParams p = Make().Rules.Constants!.Value;

        Assert.Equal(new Vector(0f, 0f, 72f), p.View);
        Assert.Equal(new Vector(-24f, -24f, 0f), p.HullMin);
        Assert.Equal(new Vector(24f, 24f, 82f), p.HullMax);
        Assert.Equal(new Vector(24f, 24f, 62f), p.DuckHullMax);
        Assert.Equal(new Vector(0f, 0f, 45f), p.DuckView);
        Assert.Equal(new Vector(0f, 0f, 14f), p.DeadViewHeight);
        Assert.Equal(800f, p.Gravity);
        Assert.NotEqual(Movement.Current.HullMax, p.HullMax);   // the HL2 hull is replaced
    }

    [Fact]
    public void The_ground_jump_leaves_at_stocks_289()
    {
        MovementParams p = Make().Rules.Constants!.Value;

        // The core's JumpVelocity: sqrt(2 g h) at the constants' gravity.
        Assert.Equal(289f, MathF.Sqrt(2.0f * p.Gravity * p.JumpHeight));
    }

    /// <summary>
    /// That sqrt is the non-HL2 arm's: left in the core's HL2 arm, this height
    /// leaves at 252 (live, before this fact: 234 after the command's gravity,
    /// where stock TF2 reads 271).
    /// </summary>
    [Fact]
    public void The_loop_runs_the_arms_stock_tf2_was_built_with()
        => Assert.True(((IMovementRules)Make().Rules).NoHl2Arms);

    /// <summary>
    /// The categorizer record is the override's shape, unconditional: the
    /// observer refusal first, any ladder off the ground, the 250 rise without
    /// the base's lift relief, the step probe, and no player-surface block
    /// (tf_gamemovement.cpp:2334-2459 against gamemovement.cpp:3794-3931).
    /// </summary>
    [Fact]
    public void The_categorize_rules_are_the_override_and_nobody_else()
    {
        CategorizePositionRules r = Make().Rules.CategorizePosition(Index)!.Value;

        Assert.True(r.ObserverFirst);
        Assert.True(r.GroundlessLadder);
        Assert.Equal(250f, r.RiseVelocity);            // not NON_JUMP_VELOCITY's 140
        Assert.False(r.GroundVelocityRelief);          // the base's :3836-3850 dropped
        Assert.True(r.StepSizeProbe);
        Assert.False(r.PlayerSurfaceMaterial);
    }

    /// <summary>
    /// Stock's WalkMove trims the walked xy back to the max every command right
    /// after Accelerating (tf_gamemovement.cpp:1808-1815), and comments both
    /// <c>StayOnGround</c> calls out since its CategorizePosition covers the
    /// ground test (:1906, :1944-1945). HL2 clamps only the wish speed and holds
    /// the ground, so both answers differ from the identity.
    /// </summary>
    [Fact]
    public void The_walk_trims_xy_and_skips_the_ground_hold()
    {
        var rules = (IMovementRules)Make().Rules;

        Assert.True(rules.ClampXY(Index));
        Assert.False(rules.KeepGround(Index));
    }

    /// <summary>
    /// TIME_TO_DUCK is 0.2 under TF_DLL (shareddefs.h:105-113 against HL2's
    /// 0.4), TIME_TO_UNDUCK is 0.2 everywhere — every easing site takes the
    /// fifth of a second whatever constant it passes. The GAME_DLL
    /// SetDuckTimer cooldown (TF_TIME_TO_DUCK 0.3, tf_gamemovement.cpp:3295)
    /// is a different timer and is not this hook's answer.
    /// </summary>
    [Fact]
    public void The_duck_eases_across_a_fifth_of_a_second()
    {
        var rules = (IMovementRules)Make().Rules;

        Assert.Equal(0.2f, rules.DuckSeconds(Index, 0.4f));
        Assert.Equal(0.2f, rules.DuckSeconds(Index, 0.2f));
    }

    /// <summary>
    /// The water's own duck mask (<c>DuckOverrides</c>, tf_gamemovement.cpp:3183-3188):
    /// feet-deep and off the ground, or eyes-deep anywhere, hides IN_DUCK; dry or
    /// feet-deep with feet down, the held duck answers.
    /// </summary>
    [Fact]
    public void The_water_depth_hides_the_duck_when_the_feet_are_not_down()
    {
        var rules = (IMovementRules)Make().Rules;

        // Eyes-deep: ducked even standing in it (:3184-3185's second half).
        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlEyes, 0f, false, false));

        // Feet-deep, airborne: the crouch hides.
        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, false, GameMovement.WlFeet, 0f, false, false));

        // Feet-deep on the ground, and dry: nothing to hide.
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlFeet, 0f, false, false));
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 0f, false, false));
    }

    /// <summary>
    /// The duck timer's cooldown (stock's <c>OnUnDuck</c> stamp :3295-3296 and the
    /// clamp's timer arm :3194-3197): the release stamps TF_TIME_TO_DUCK 0.3 on
    /// the loop's clock, a grounded press inside the stamp waits, one made after
    /// it answers, and the same press airborne ignores the stamp.
    /// </summary>
    [Fact]
    public void A_grounded_press_waits_while_the_duck_cooldown_runs()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = (IMovementRules)TFMovementFixtures.Rules(players);

        rules.DuckRelease(Index, 5f, false);
        Assert.Equal(5.3f, players.Predicted(Index)!.DuckTimer, 2);

        // Inside the stamp, on the ground: refused. After it: answered.
        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 5.25f, false, false));
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 5.4f, false, false));

        // The same stamp, airborne: the timer arm never reaches (:3194 wants ground).
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, false, GameMovement.WlNotInWater, 5.25f, false, false));
    }

    /// <summary>
    /// The quantum-crouch hold (<c>DuckOverrides</c>, tf_gamemovement.cpp:3213-3216):
    /// only a duck already down AND easing hides its own held button — one flag
    /// alone lets the press through.
    /// </summary>
    [Fact]
    public void The_quantum_hold_needs_both_duck_flags()
    {
        var rules = (IMovementRules)Make().Rules;

        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 0f, true, true));
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 0f, true, false));
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 0f, false, false));
    }

    /// <summary>
    /// The charge clamp of <c>SetupMove</c> (server/tf/tf_playermove.cpp:83-106,
    /// the targe exploit fix): the first charging command stores its yaw, a
    /// return within the cap — the charge cap (0.4625 at a tick frame) times
    /// 2.5 (:85-86) — answers as sent (:87), and past it the yaw lands one cap
    /// in from the stored one (:88-94), which the next command stores (:96).
    /// </summary>
    [Fact]
    public void The_charge_clamp_pulls_the_return_into_the_cap()
    {
        var players = new TFMovementFixtures.Players();
        TFPlayerNetState state = players.Add(Index, TFClass.Demoman);
        state.SetConditions(TFCondMask.Of(TFCond.ShieldCharge));
        var rules = (IMovementRules)TFMovementFixtures.Rules(players);

        // Nothing held yet: the first charge command keeps its own yaw (:87-88).
        Assert.Equal(-20f, rules.ChargeYawCap(Index, -20f, 0.015f, 0.015f), 4);

        // One degree back, inside the cap of 1.15625: as sent (:87).
        Assert.Equal(-19f, rules.ChargeYawCap(Index, -19f, 0.015f, 0.015f), 4);

        // Past it from the stored −19: −19 + the cap, not the command's −17 (:88-94).
        Assert.Equal(-17.84375f, rules.ChargeYawCap(Index, -17f, 0.015f, 0.015f), 4);
    }

    /// <summary>
    /// The else arm (:104): nobody charging, the yaw of the moment is the whole
    /// answer — stored, and never clamped against what the last command left.
    /// </summary>
    [Fact]
    public void Nobody_charging_answers_the_yaws_as_sent()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Demoman);
        var rules = (IMovementRules)TFMovementFixtures.Rules(players);

        Assert.Equal(-20f, rules.ChargeYawCap(Index, -20f, 0.015f, 0.015f), 4);
        Assert.Equal(-5f, rules.ChargeYawCap(Index, -5f, 0.015f, 0.015f), 4);
    }

    /// <summary>
    /// The scaling convar carries into the clamp (:85): off, the cap is the
    /// plain base times 2.5 (0.45 → 1.125), so the same return from −20 lands
    /// at −18.875 where a tick-frame cap lands at −18.84375.
    /// </summary>
    [Fact]
    public void The_scaling_off_lands_the_clamp_on_the_plain_cap()
    {
        var players = new TFMovementFixtures.Players();
        TFPlayerNetState state = players.Add(Index, TFClass.Demoman);
        state.SetConditions(TFCondMask.Of(TFCond.ShieldCharge));
        ConVarRegistry vars = TFMovementFixtures.ConVars();
        vars.Declared<TFDemomanChargeFrametimeScalingConVar>().SetValue(false);
        var rules = (IMovementRules)TFMovementFixtures.Rules(players, convars: vars);

        Assert.Equal(-20f, rules.ChargeYawCap(Index, -20f, 0.015f, 0.015f), 4);

        // −18 sits 2 past the stored −20, past the plain cap of 1.125.
        Assert.Equal(-18.875f, rules.ChargeYawCap(Index, -18f, 0.015f, 0.015f), 4);
    }

    /// <summary>
    /// One duck per air event past the count (<c>DuckOverrides</c> :3219-3222 on
    /// TF_AIRDUCKED_COUNT 2 (:90), counted by airborne releases :3299-3301): the
    /// third airborne press after two air ducks is ignored, while a grounded
    /// press under the same count answers.
    /// </summary>
    [Fact]
    public void A_third_air_duck_in_one_jump_is_ignored()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = (IMovementRules)TFMovementFixtures.Rules(players);

        // Two airborne releases: each stamps and counts (:3295-3301).
        rules.DuckRelease(Index, 1f, true);
        rules.DuckRelease(Index, 2f, true);
        Assert.Equal(2, players.Predicted(Index)!.AirDuckedCount);
        Assert.Equal(2.3f, players.Predicted(Index)!.DuckTimer, 2);

        // The cap reached: airborne the duck hides, grounded it answers.
        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, false, GameMovement.WlNotInWater, 9f, false, false));
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 9f, false, false));
    }

    /// <summary>
    /// The landing frees the air event (stock's SetAirDash/SetAirDucked pair at
    /// landing, tf_gamemovement.cpp:2958-2959): the count clears with the dash,
    /// and the next air event ducks afresh.
    /// </summary>
    [Fact]
    public void The_landing_clears_the_air_duck_count()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = (IMovementRules)TFMovementFixtures.Rules(players);

        rules.DuckRelease(Index, 1f, true);
        rules.DuckRelease(Index, 2f, true);
        Assert.Equal(2, players.Predicted(Index)!.AirDuckedCount);

        rules.Grounded(Index);

        Assert.Equal(0, players.Predicted(Index)!.AirDuckedCount);
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, false, GameMovement.WlNotInWater, 9f, false, false));
    }

    /// <summary>
    /// <c>tf_clamp_airducks</c> gates the clamp's arms, not the water
    /// (tf_gamemovement.cpp:3190-3191): with the convar on a cooldown press is
    /// refused, off it answers — and eyes-deep water still hides the duck while
    /// the convar is off, because the water masks ahead of the gate.
    /// </summary>
    [Fact]
    public void The_clamp_convar_gates_the_three_arms_not_the_water()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var convars = TFMovementFixtures.ConVars();
        var rules = (IMovementRules)TFMovementFixtures.Rules(players, convars: convars);

        rules.DuckRelease(Index, 5f, true);

        // On (the declared default): inside the airborne stamp, grounded press waits.
        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 5.25f, false, false));

        convars.Declared<TFClampAirducksConVar>().SetValue(false);

        // Off: the same press answers.
        Assert.Equal(GameMovement.InDuck, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlNotInWater, 5.25f, false, false));

        // And the water still masks ahead of the gate (:3183).
        Assert.Equal(0, rules.DuckButtons(Index, GameMovement.InDuck, true, GameMovement.WlEyes, 9f, false, false));
    }

    [Fact]
    public void Footing_without_a_carrier_answers_nothing()
        => Assert.Null(Make().Rules.Footing(Index, default, new Vector(0f, 0f, 1f)));

    [Fact]
    public void Footing_without_either_condition_answers_nothing()
    {
        var (rules, players) = Make();
        _ = players.Add(Index, TFClass.Soldier);

        Assert.Null(rules.Footing(Index, new Vector(0f, 100f, 0f), new Vector(0f, 0f, 1f)));
    }

    /// <summary>The air current slows the air, not the ground (tf_gamemovement.cpp:2392-2395).</summary>
    [Fact]
    public void Air_current_scales_the_air_friction_alone()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Soldier).SetConditions(TFCondMask.Of(TFCond.AirCurrent));

        FootingAnswer a = rules.Footing(Index, new Vector(0f, 100f, 0f), new Vector(0f, 0f, 1f))!.Value;

        Assert.False(a.InAir);
        Assert.Equal(1f, a.GroundFrictionScale);
        Assert.Equal(0.75f, a.AirFrictionScale);
    }

    /// <summary>Lost footing's takeoff: any away-from-ground velocity is air (:2400-2404).</summary>
    [Fact]
    public void Lost_footings_rise_is_air()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Soldier).SetConditions(TFCondMask.Of(TFCond.LostFooting));

        FootingAnswer a = rules.Footing(Index, new Vector(30f, 0f, 40f), new Vector(0f, 0f, 1f))!.Value;

        Assert.True(a.InAir);
        Assert.Equal(1f, a.GroundFrictionScale);
    }

    /// <summary>Sliding on lost footing scrapes at the friction convar (:2407-2411).</summary>
    [Fact]
    public void Lost_footings_slide_scrapes_the_ground_friction()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Soldier);
        state.SetConditions(TFCondMask.Of(TFCond.LostFooting));

        // 100 across the floor plane, nothing along it: a slide over the 50 restick.
        FootingAnswer a = rules.Footing(Index, new Vector(100f, 0f, 0f), new Vector(0f, 0f, 1f))!.Value;

        Assert.False(a.InAir);
        Assert.Equal(0.1f, a.GroundFrictionScale);
        Assert.True(state.InCond(TFCond.LostFooting), "a slide keeps the condition");
    }

    /// <summary>Too slow to slide, the footing comes back and the condition goes (:2412-2417).</summary>
    [Fact]
    public void Lost_footings_restick_clears_the_condition()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Soldier);
        state.SetConditions(TFCondMask.Of(TFCond.LostFooting));

        FootingAnswer a = rules.Footing(Index, new Vector(10f, 0f, 0f), new Vector(0f, 0f, 1f))!.Value;

        Assert.False(a.InAir);
        Assert.Equal(1f, a.GroundFrictionScale);
        Assert.False(state.InCond(TFCond.LostFooting), "the footing came back");
    }

    [Fact]
    public void Max_speed_is_the_carriers_class_speed_whatever_the_engine_arrived_at()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));

        state.SetClass(TFClass.HeavyWeapons);
        Assert.Equal(230f, rules.MaxSpeedScale(Index, 400f, false, 0));

        state.SetConditions(TFCondMask.Of(TFCond.Aiming));
        Assert.Equal(110f, rules.MaxSpeedScale(Index, 230f, false, 0));
    }

    [Fact]
    public void Without_a_carrier_or_a_class_the_core_speed_stands()
    {
        var (rules, players) = Make();
        Assert.Equal(320f, rules.MaxSpeedScale(Index, 320f, false, 0));

        players.Add(Index, TFClass.Undefined);
        Assert.Equal(320f, rules.MaxSpeedScale(Index, 320f, false, 0));
    }

    /// <summary>The round state, frozen for the fact (<see cref="TF2.Shared.Rules.ITFRoundStateView"/>).</summary>
    /// <summary>
    /// The restart freeze (<c>tf_player_shared.cpp:10819</c>, :10847): while
    /// the round restarts — stock's <c>InRoundRestart</c>, the PREROUND state —
    /// and <c>tf_player_movement_restart_freeze</c> stands, the speed walk
    /// answers 1 (:10872-10874).
    /// </summary>
    [Fact]
    public void A_round_restart_freezes_the_speed_to_stocks_one()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players, rounds: new TFMovementFixtures.RoundAt(TF2.Shared.Rules.RoundState.Preround));

        Assert.Equal(1f, rules.MaxSpeedScale(Index, 400f, false, 0));
    }

    [Fact]
    public void The_running_round_leaves_the_class_speed()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players, rounds: new TFMovementFixtures.RoundAt(TF2.Shared.Rules.RoundState.RoundRunning));

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));
    }

    /// <summary>The freeze is the convar's to lift: registered, its declared default freezes and a 0 does not.</summary>
    [Fact]
    public void The_freeze_follows_the_convar()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var convars = TFMovementFixtures.ConVars();
        var rules = TFMovementFixtures.Rules(players, convars: convars,
                                  rounds: new TFMovementFixtures.RoundAt(TF2.Shared.Rules.RoundState.Preround));

        Assert.Equal(1f, rules.MaxSpeedScale(Index, 400f, false, 0));

        convars.Declared<TFPlayerMovementRestartFreezeConVar>().SetValue(false);

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));
    }

    /// <summary>
    /// <c>CTFGameMovement::PreventBunnyJumping</c> (tf_gamemovement.cpp:1086-1112):
    /// the cap is <c>BUNNYJUMP_MAX_SPEED_FACTOR</c> (1.2) times the max speed
    /// the loop arrived with, whatever the class — stock reads
    /// <c>m_flMaxspeed</c> and asks nothing else.
    /// </summary>
    [Fact]
    public void The_bunny_jump_cap_is_stocks_1_2_times_the_max_speed()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Scout);

        Assert.Equal(480f, rules.BunnyJumpCap(Index, 400f), 2);
        Assert.Equal(276f, rules.BunnyJumpCap(Index, 230f), 2);
    }

    /// <summary>
    /// Stock's backpedal clamp (<c>tf_clamp_back_speed</c> 0.9 /
    /// <c>tf_clamp_back_speed_min</c> 100, tf_gamemovement.cpp:47-48): the
    /// rules hand the loop that pair whatever the player's class — stock's
    /// WalkMove clamp asks nothing of the player. Unbound convars leave the
    /// rule on stock's values; registered ones move it.
    /// </summary>
    [Fact]
    public void The_backpedal_limit_is_stocks_two_convars()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Scout);

        Assert.Equal(new BackpedalLimit(0.9f, 100f), rules.BackpedalLimit(Index, 400f));
    }

    /// <summary>
    /// The pair is live: a registered convar answers through the rule
    /// (TFWinPanelTests' backend, so a <c>SetValue</c> round-trips), and the
    /// next command's clamp is the operator's values, not the declared ones.
    /// </summary>
    [Fact]
    public void The_backpedal_limit_follows_the_two_convars()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Scout);
        var convars = TFMovementFixtures.ConVars();
        var rules = TFMovementFixtures.Rules(players, convars: convars);

        // The engine's read-back of the declared defaults, not a constant.
        Assert.Equal(new BackpedalLimit(0.9f, 100f), rules.BackpedalLimit(Index, 400f));

        convars.Declared<TFClampBackSpeedConVar>().SetValue(0.5f);
        convars.Declared<TFClampBackSpeedMinConVar>().SetValue(150f);

        Assert.Equal(new BackpedalLimit(0.5f, 150f), rules.BackpedalLimit(Index, 400f));
    }

    //=========================================================================
    // The movement stun (CTFGameMovement::StunMove, tf_gamemovement.cpp:537-627):
    // the seized controls and the slowed walk. The rule decides from the
    // carrier's stun mirror — the four fields stock's <c>UpdateLegacyStunSystem</c>
    // keeps networked (tf_player_shared.cpp:7441-7465, gates at :9938-9975) —
    // so the server's run and every client's replay decide alike. The lerp
    // trio (m_flStunLerpTarget, m_flLastMovementStunChange,
    // m_bStunNeedsFadeOut, tf_player_shared.h:1228-1230) is entity-local in
    // stock: sent by none of the tables (:444-482 lists only m_iStunIndex),
    // read by nothing but StunMove — here it is the rules' own per-player
    // history. The heavy-minigun button exception (:548-552) and the
    // CYOA-PDA/match-countdown no-move block (:614-624) are
    // docs/plans/tf2/deferred.tsv's rows.
    //=========================================================================

    /// <summary>
    /// The carrier's stun mirror as <c>UpdateLegacyStunSystem</c> would write
    /// it (:7446-7453): the active index, the entry's flags, its 0..255 amount
    /// and its expiry — with the permanent stun condition stock raises beside
    /// the stack (<c>AddCond( TF_COND_STUNNED, -1.f, ... )</c>, :9932).
    /// </summary>
    private static TFPlayerNetState Stunned(TFPlayerNetState state, int index, TFStunFlags flags,
                                            float amount, float expireTime)
    {
        state.StunIndex = index;
        state.StunFlags = (int)flags;
        state.MovementStunAmount = amount;
        state.StunExpireTime = expireTime;
        state.SetConditions(TFCondMask.Of(TFCond.Stunned));
        return state;
    }

    /// <summary>
    /// Nobody is stunned: without a carrier, and with a carrier whose mirror
    /// never armed, the command stands — stock's StunMove rewrites nothing
    /// outside its two arms (:540, :567, :585 all fail closed).
    /// </summary>
    [Fact]
    public void AnUnstunnedPlayerIsStunnedByNothing()
    {
        var (rules, players) = Make();

        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));

        players.Add(Index, TFClass.Scout);
        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The slow: half a stun (127.5 of 255) halves the wish moves —
    /// <c>mv-&gt;m_flForwardMove *= 1.f - flStunAmount</c> (:576-577) — and
    /// touches neither the buttons nor the up move.
    /// </summary>
    [Fact]
    public void AMovementStunScalesTheMovesByItsAmount()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, 127.5f, 100f);

        Assert.Equal(new MovementStun(12345, 150f, 50f, 50f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The amount gate is clamped both ways as stock's <c>GetAmountStunned</c>
    /// clamps it (:9943): over 255 stops the player outright, and a negative
    /// (clamped to 0) is no movement stun at all.
    /// </summary>
    [Fact]
    public void TheStunAmountIsClampedToItsTwoHundredFiftyFives()
    {
        var (rules, players) = Make();
        var state = Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, -5f, 100f);

        // Clamped to zero, it is no movement stun at all (:9943): no arm of
        // StunMove runs, and there is no lerp history behind it.
        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));

        state.MovementStunAmount = 300f;   // clamped to 255: the whole wish move goes
        Assert.Equal(new MovementStun(12345, 0f, 0f, 50f),
                     rules.MovementStun(Index, 10.1f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// Past its expiry the movement stun is no stun — stock's amount gate
    /// asks <c>flExpireTime &gt; gpGlobals-&gt;curtime</c> (:9942) — and with
    /// no lerp history behind it the command stands.
    /// </summary>
    [Fact]
    public void AnExpiredMovementStunStunsNothing()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, 127.5f, 9.5f);

        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The forward-only flavour also kills the forward move — stock asks
    /// <c>GetStunFlags()</c> (the active entry's flags, tf_player_shared.h:570)
    /// beside the scale, twice over (:578-581, :600-603).
    /// </summary>
    [Fact]
    public void AForwardOnlyStunAlsoZeroesTheForwardMove()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0,
                TFStunFlags.Movement | TFStunFlags.MovementForwardOnly, 127.5f, 100f);

        Assert.Equal(new MovementStun(12345, 0f, 50f, 50f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The controls stun seizes everything: the buttons (no firing, no use,
    /// no jump — :546) and all three moves (:554-559). The heavy-minigun
    /// exception that lets a spun gun through (:548-552) is the deferred row.
    /// </summary>
    [Fact]
    public void AControlStunSeizesTheButtonsAndMoves()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Controls, 0f, 100f);

        Assert.Equal(new MovementStun(0, 0f, 0f, 0f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The loser-state stun seizes the buttons alone (:540-546): stock's
    /// move-zeroing arm is the controls stun's only (:554), and this stun's
    /// flag is neither the movement nor the controls one.
    /// </summary>
    [Fact]
    public void ALoserStateStunSeizesTheButtonsAlone()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.LoserState, 0f, 100f);

        Assert.Equal(new MovementStun(0, 300f, 100f, 50f),
                     rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// Stock's <c>IsControlStunned</c> asks the flags and the condition but
    /// never the expiry (:9952-9961) — and neither does the mirror's reading
    /// of it: a controls stun holds past its expire time.
    /// </summary>
    [Fact]
    public void AControlStunHoldsPastItsExpiry()
    {
        var (rules, players) = Make();
        Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Controls, 0f, 0f);

        Assert.Equal(new MovementStun(0, 0f, 0f, 0f),
                     rules.MovementStun(Index, 100f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The condition is load-bearing: every stun gate stands on
    /// <c>InCond( TF_COND_STUNNED )</c> beside the flags (:9942, :9956, :9970),
    /// so a mirror behind no condition — flags, amount and future expiry
    /// included — is inert.
    /// </summary>
    [Fact]
    public void WithoutTheStunConditionTheMirrorIsInert()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        state.StunIndex = 0;
        state.StunFlags = (int)(TFStunFlags.Controls | TFStunFlags.Movement);
        state.MovementStunAmount = 255f;
        state.StunExpireTime = 100f;

        Assert.Null(rules.MovementStun(Index, 10f, 12345, 300f, 100f, 50f));
    }

    /// <summary>
    /// The fade-out (stock's lerp, :569-609): while the stun holds, half the
    /// moves; when it ends, the 0.2 s out — the clock restarts at the tick the
    /// loss is first noticed (:590, that tick still scales by the full
    /// target), the scale halves across the band (:594's reversed
    /// <c>RemapValClamped</c>), and the tick the band runs out zeroes the lerp
    /// trio and returns the command unscaled one last time (:605-611 — stock
    /// returns TRUE there too; only the call after the clear answers false).
    /// The times are binary fractions so the band's arithmetic is exact.
    /// </summary>
    [Fact]
    public void TheMovementStunFadesOutOverTheStockBand()
    {
        var (rules, players) = Make();
        var state = Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.Movement, 127.5f, 100f);

        Assert.Equal(new MovementStun(1, 150f, 50f, 0f),
                     rules.MovementStun(Index, 10f, 1, 300f, 100f, 0f));

        state.StunExpireTime = 10f;   // the stun is gone; the fade-out owns the next 0.2 s
        // The first tick to notice re-arms the change clock (:590): still the full target.
        Assert.Equal(new MovementStun(1, 150f, 50f, 0f),
                     rules.MovementStun(Index, 10.125f, 1, 300f, 100f, 0f));
        // Halfway (Δ=0.125 → the reversed band says 0.375 of the target): scale keeps 1 − 0.5 × 0.375.
        Assert.Equal(new MovementStun(1, 243.75f, 81.25f, 0f),
                     rules.MovementStun(Index, 10.25f, 1, 300f, 100f, 0f));
        // The band has run out (Δ=0.125 past): the trio zeroes, the moves stand (:605-611).
        Assert.Equal(new MovementStun(1, 300f, 100f, 0f),
                     rules.MovementStun(Index, 10.375f, 1, 300f, 100f, 0f));
        Assert.Null(rules.MovementStun(Index, 10.5f, 1, 300f, 100f, 0f));
    }

    /// <summary>
    /// The speed arm reads the carrier's flags behind stock's gate, the same
    /// one <see cref="MovementStun"/> stands on: <c>IsLoserStateStunned</c>
    /// (tf_player_shared.cpp:9966-9975) asks for the active entry AND the
    /// condition before the word means anything, because the legacy copy goes
    /// stale behind a cleared entry (the mirror, :7446-7453). A stunned scout
    /// walks at half (:10994); the same carrier with the entry gone — the
    /// index cleared, the word still saying LoserState, the condition not yet
    /// re-arrived — walks at full speed, and so does the one with the
    /// condition gone.
    /// </summary>
    [Fact]
    public void The_stun_speed_scale_reads_the_flags_behind_the_gate()
    {
        var (rules, players) = Make();
        var state = Stunned(players.Add(Index, TFClass.Scout), 0, TFStunFlags.LoserState, 0f, 100f);

        Assert.Equal(200f, rules.MaxSpeedScale(Index, 320f, false, 0));   // :10994's half

        state.StunIndex = -1;   // the entry gone; the value word stale, as stock's mirror leaves it
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));   // the gate: index+cond first

        state.SetConditions(default);   // and the condition-gone shape
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 320f, false, 0));
    }

    /// <summary>A convar backend that stores what it is told, so a SetValue round-trips (the TFWinPanelTests rig).</summary>
    private sealed class ClampBackend : IConVarBackend
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

    /// <summary>The speed hooks walk the player's own attribute manager, the server's grant.</summary>
    [Fact]
    public void The_speed_hooks_walk_the_players_attribute_manager()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = TFMovementFixtures.Rules(players, owners: new OwnersOf(items), hooks: new AttributeHooks());

        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5002, "attr 5002", TFMaxSpeed.MoveSpeedAttrib,
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 0.5f));
        items.SetManager(Index, manager);

        Assert.Equal(200f, rules.MaxSpeedScale(Index, 400f, false, 0));

        // The player leaves: the grant and the scale go with them.
        items.Forget(Index);
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));
    }

    /// <summary>The no-dash attribute reaches the dash through the same grant.</summary>
    [Fact]
    public void The_dash_refusal_walks_the_players_attribute_manager()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = TFMovementFixtures.Rules(players, owners: new OwnersOf(items), hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5004, "attr 5004", TFJump.DoubleJumpDisabledAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));
        items.SetManager(Index, manager);
        state.AirDash = 0;

        Assert.Equal(0f, rules.AirJump(in request));

        // The grant goes with the player: the dash comes back.
        items.Forget(Index);
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
    }

    /// <summary>
    /// The BFB's hype speed (<c>tf_player_shared.cpp:11080-11086</c>): the
    /// scout owning the Baby Face's Blaster runs at the meter's remap — full
    /// 1.45, half 1.225 — and nobody else does.
    /// </summary>
    [Fact]
    public void The_bfb_hype_scales_its_owners_speed()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var owners = new OwnersOf(new TFPlayerItems())
        {
            ByName = { [Index] = new() { [TFHype.BrawlerScript] = new AttributeContainer() } },
        };
        var rules = TFMovementFixtures.Rules(players, owners: owners);

        state.HypeMeter = TFHype.FullMeter;
        Assert.Equal(400f * TFHype.SpeedScaleMax, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);

        state.HypeMeter = TFHype.FullMeter / 2f;
        Assert.Equal(400f * 1.225f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);
    }

    /// <summary>The hype speed is the BFB owner's alone: no item, or another class, none.</summary>
    [Fact]
    public void The_hype_speed_is_the_owners_alone()
    {
        var players = new TFMovementFixtures.Players();
        var scout = players.Add(Index, TFClass.Scout);
        var owners = new OwnersOf(new TFPlayerItems())
        {
            ByName = { [Index] = new() { [TFHype.BrawlerScript] = new AttributeContainer() } },
        };
        var rules = TFMovementFixtures.Rules(players, owners: owners);

        // A scout whose hype is banked but whose item is elsewhere: stock's
        // Weapon_OwnsThisID gate is the item's.
        scout.HypeMeter = TFHype.FullMeter;
        owners.ByName[Index].Clear();
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));

        // The class arm: a heavy at the item keeps the heavy's own walk.
        owners.ByName[Index][TFHype.BrawlerScript] = new AttributeContainer();
        players.Predicted(Index)!.SetClass(TFClass.HeavyWeapons);
        Assert.Equal(230f, rules.MaxSpeedScale(Index, 230f, false, 0));
    }

    /// <summary>Holds the shovel in hand for the rules' held-weapon reads.</summary>
    private sealed class HeldShovel : ITFAnimWeapons
    {
        public TFAnimWeapon? ActiveWeapon(int playerIndex) => new(default, TFWeaponId.Shovel);
        public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
    }

    /// <summary>Holds another weapon, so the shovel's arm can answer to its own kind.</summary>
    private sealed class HeldOther : ITFAnimWeapons
    {
        public TFAnimWeapon? ActiveWeapon(int playerIndex) => new(default, TFWeaponId.Minigun);
        public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
    }

    /// <summary>The held item's mode, as the shipped schema carries it on the item.</summary>
    private static AttributeContainer ShovelItem(float mode)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(6101, "attr 6101", "set_weapon_mode",
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), mode));
        return item;
    }

    /// <summary>
    /// <c>CTFShovel::GetSpeedMod</c> (tf_weapon_shovel.cpp:123-143): the held SHOVEL_SPEED_BOOST
    /// shovel scales the soldier's 240 walk by the health tiers — 1 above 80%, then 1.1,
    /// 1.2, 1.4 and 1.6 — and every command re-reads them as the health moves.
    /// </summary>
    [Fact]
    public void The_held_shovel_speeds_the_walk_as_the_health_goes()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Soldier);
        var owners = new OwnersOf(new TFPlayerItems()) { Active = ShovelItem(2f) };
        var engine = new RecordingEngine();
        engine.Returns("Entities.GetHealth", 200);
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks(),
                                  weapons: new HeldShovel(), entities: engine);

        // A full canteen: ratio 1, the plain soldier walk.
        Assert.Equal(240f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);

        // The ticks as hurtme bites through the bands (soldier health_max 200):
        engine.Returns("Entities.GetHealth", 160);   // ratio .8 -> the 1.1 band
        Assert.Equal(264f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);

        engine.Returns("Entities.GetHealth", 89);    // ratio .445 -> the 1.2 band
        Assert.Equal(288f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);

        engine.Returns("Entities.GetHealth", 40);    // ratio .2 -> the 1.6 band
        Assert.Equal(384f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);
    }

    /// <summary>The Equalizer's mode (the damage boost) leaves the walk at one times itself.</summary>
    [Fact]
    public void The_damage_mode_shovel_leaves_the_walk_one_times_itself()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Soldier);
        var owners = new OwnersOf(new TFPlayerItems()) { Active = ShovelItem(1f) };
        var engine = new RecordingEngine();
        engine.Returns("Entities.GetHealth", 40);
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks(),
                                  weapons: new HeldShovel(), entities: engine);

        Assert.Equal(240f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);
    }

    /// <summary>The speed arm is the shovel's: another held weapon with the same mode stays plain.</summary>
    [Fact]
    public void The_shovel_speed_is_the_shovels_alone()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Soldier);
        var owners = new OwnersOf(new TFPlayerItems()) { Active = ShovelItem(2f) };
        var engine = new RecordingEngine();
        engine.Returns("Entities.GetHealth", 40);
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks(),
                                  weapons: new HeldOther(), entities: engine);

        Assert.Equal(240f, rules.MaxSpeedScale(Index, 400f, false, 0), 0.5f);
    }

    /// <summary>The dash count and the jump height walk the ACTIVE weapon's item, both sides alike.</summary>
    [Fact]
    public void The_active_weapons_item_reaches_the_dash()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var owners = new OwnersOf(new TFPlayerItems());
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };
        var weapon = new AttributeContainer();
        weapon.Set(AttributeValue.OfFloat(new AttributeDefinition(5006, "attr 5006", TFJump.AirDashCountAttrib,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));

        state.AirDash = 1;
        Assert.Equal(0f, rules.AirJump(in request));   // the convar's one is spent

        owners.Active = weapon;
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(2, state.AirDash);
    }

    /// <summary>The dash pays the BFB's hype toll; a buffed scout's writes are refused.</summary>
    [Fact]
    public void The_dash_pays_the_hype_the_bfb_prices()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var owners = new OwnersOf(new TFPlayerItems());
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };
        var weapon = new AttributeContainer();
        weapon.Set(AttributeValue.OfFloat(new AttributeDefinition(5007, "attr 5007", TFHype.JumpResetAttribute,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 20));

        state.HypeMeter = 50f;
        owners.Active = weapon;

        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(30f, state.HypeMeter);

        // SetScoutHypeMeter's IsHypeBuffed out (tf_player_shared.cpp:14126):
        // the buffed dash leaves the meter.
        var buffed = new TFMovementFixtures.Players();
        var buffedState = buffed.Add(Index, TFClass.Scout);
        buffedState.SetConditions(TFCondMask.Of(TFCond.SodapopperHype));
        buffedState.HypeMeter = 50f;
        var buffedRules = TFMovementFixtures.Rules(buffed, owners: owners, hooks: new AttributeHooks());

        Assert.Equal(TFJump.DashSpeed, buffedRules.AirJump(in request));
        Assert.Equal(50f, buffedState.HypeMeter);
    }

    /// <summary>The drawn bow holds the jump: the seam's refusal reaches both arms.</summary>
    [Fact]
    public void A_drawn_bow_holds_the_jump_and_the_dash()
    {
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Scout);
        var owners = new OwnersOf(new TFPlayerItems()) { MayJump = false };
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        // CheckJumpButton's head gate (tf_gamemovement.cpp:1210): nothing is
        // pushed, and the dash behind it (:1240) is not counted either.
        Assert.Equal(0f, rules.JumpScale(in request));
        Assert.Equal(0f, rules.AirJump(in request));
        Assert.Equal(0, players.Predicted(Index)!.AirDash);

        // The trigger up, the jump and the dash again.
        owners.MayJump = true;
        Assert.Equal(1f, rules.JumpScale(in request));
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(1, players.Predicted(Index)!.AirDash);
    }

    /// <summary>
    /// The Mantreads' air-control scale rides the blast-jump condition alone and its
    /// home is the air cap, where stock reads both attributes
    /// (<c>CTFGameMovement::GetAirSpeedCap</c>, tf_gamemovement.cpp:2081-2094): the
    /// attribute on the player's manager lifts the cap only while the condition
    /// stands, the max speed no longer changes with it, and landing ends it.
    /// </summary>
    [Fact]
    public void The_blast_jump_scale_sits_behind_the_condition_and_landing_ends_it()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = TFMovementFixtures.Rules(players, owners: new OwnersOf(items), hooks: new AttributeHooks());

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5008, "attr 5008", "mod_air_control_blast_jump",
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1.5f));
        items.SetManager(Index, manager);

        // No condition: neither attribute answers, the base cap stands (:2081's gate),
        // and the walk's speed answer is unchanged beside it (:2081).
        Assert.Equal(30f, rules.AirSpeedCap(Index, 30f));
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));

        // The blast-jump condition on: the attribute scales the cap (:2083-2087)
        // and no longer the max speed, whose answer the blast-jump arm left.
        var blast = default(TFCondMask);
        blast.Set(TFCond.Blastjumping);
        state.SetConditions(in blast);
        Assert.Equal(45f, rules.AirSpeedCap(Index, 30f));
        Assert.Equal(400f, rules.MaxSpeedScale(Index, 400f, false, 0));

        // Landing clears the condition (TFBlastJump.cs's clear): the cap stands again.
        state.SetConditions(default);
        Assert.Equal(30f, rules.AirSpeedCap(Index, 30f));
    }

    /// <summary>
    /// The chute lifts the air cap by its multiplier while it is out, and the base
    /// cap answers without it (<c>CTFGameMovement::GetAirSpeedCap</c>
    /// tf_gamemovement.cpp:2067-2070): 30 outside the chute, and the stock default's
    /// multiplier times the base — 30 × 2.5 — while the condition stands.
    /// </summary>
    [Fact]
    public void The_chute_multiplies_the_air_cap_while_it_is_out()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players);

        Assert.Equal(30f, rules.AirSpeedCap(Index, 30f));

        var chute = default(TFCondMask);
        chute.Set(TFCond.ParachuteActive);
        state.SetConditions(in chute);
        Assert.Equal(75f, rules.AirSpeedCap(Index, 30f));
    }

    /// <summary>
    /// The shield charge answers the charge's own convar outright, before every
    /// other arm (:2051-2054): the cap is <c>tf_max_charge_speed</c>'s default,
    /// the base cap never entering the answer.
    /// </summary>
    [Fact]
    public void The_shield_charge_answers_the_charge_speed_to_the_air_cap()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players);

        var charged = default(TFCondMask);
        charged.Set(TFCond.ShieldCharge);
        state.SetConditions(in charged);
        Assert.Equal(750f, rules.AirSpeedCap(Index, 30f));
    }

    /// <summary>
    /// The scout's full press is raised to the max speed it stands under
    /// (<c>HighMaxSpeedMove</c>, tf_gamemovement.cpp:886-895): forward held at
    /// the client's reference speed under the 520 max walks at 520, the side
    /// move kept beside it.
    /// </summary>
    [Fact]
    public void The_full_press_is_raised_to_the_max_speed_under_it()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players);

        Assert.Equal(new HighMaxSpeedPair(520f, 0f), rules.HighMaxSpeed(Index, 450f, 0f, 520f));
    }

    /// <summary>
    /// A press that already stands over the max speed is kept as sent (:886's
    /// gate): 450 forward against a 400 max asks nothing of the pair.
    /// </summary>
    [Fact]
    public void A_press_over_the_max_speed_is_kept_as_sent()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players);

        Assert.Null(rules.HighMaxSpeed(Index, 450f, 0f, 400f));
    }

    /// <summary>
    /// Half a press matches neither reference speed and is kept (:888-894):
    /// 225 forward under a 520 max walks at 225, the raise only ever meeting a
    /// full press.
    /// </summary>
    [Fact]
    public void The_half_press_matches_no_reference_and_is_kept()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(Index, TFClass.Scout);
        var rules = TFMovementFixtures.Rules(players);

        Assert.Null(rules.HighMaxSpeed(Index, 225f, 0f, 520f));
    }

    /// <summary>
    /// The B.A.S.E. Jumper's press toggles the chute through the player's own
    /// manager (tf_gamemovement.cpp:1132-1150): each airborne press flips the
    /// condition, and without the attribute nothing toggles.
    /// </summary>
    [Fact]
    public void The_airborne_press_toggles_the_chute_behind_the_attribute()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = TFMovementFixtures.Rules(players, owners: new OwnersOf(items), hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        // No attribute yet: the press dashes and leaves the chute alone.
        rules.AirJump(in request);
        Assert.False(state.InCond(TFCond.ParachuteActive));

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5009, "attr 5009", "parachute_attribute",
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));
        items.SetManager(Index, manager);
        state.AirDash = 0;

        rules.AirJump(in request);                            // :1141 — none goes on
        Assert.True(state.InCond(TFCond.ParachuteActive));

        rules.AirJump(in request);                            // :1139 — deployed comes off
        Assert.False(state.InCond(TFCond.ParachuteActive));

        // The grant goes with the player: the press keeps toggling nothing.
        items.Forget(Index);
        rules.AirJump(in request);
        Assert.False(state.InCond(TFCond.ParachuteActive));
    }

    /// <summary>
    /// The pair as stock's toggle sets it (tf_gamemovement.cpp:1139-1151): a first airborne press sets BOTH
    /// TF_COND_PARACHUTE_ACTIVE and TF_COND_PARACHUTE_DEPLOYED, the second
    /// takes the active one off and keeps the deployed record (:1139-1141,
    /// nothing removes :1151 there), and while the record stands a further
    /// press does not reopen — the toggle-allowed cvar ships off (:58), so
    /// the record is the only gate (:1148). The landing's remover
    /// (tf_player_shared.cpp:3131, ported in TFWeaponBehaviour) is what lets
    /// the next airborne window deploy again.
    /// </summary>
    [Fact]
    public void The_airborne_press_deploys_the_pair_and_the_record_holds_the_reopen()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Scout);
        var items = new TFPlayerItems();
        var rules = TFMovementFixtures.Rules(players, owners: new OwnersOf(items), hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5009, "attr 5009", "parachute_attribute",
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));
        items.SetManager(Index, manager);

        // The first airborne press: the pair goes on together (:1150-1151).
        rules.AirJump(in request);
        Assert.True(state.InCond(TFCond.ParachuteActive));
        Assert.True(state.InCond(TFCond.ParachuteDeployed));

        // The second closes the active one; the record stays (:1139-1141).
        rules.AirJump(in request);
        Assert.False(state.InCond(TFCond.ParachuteActive));
        Assert.True(state.InCond(TFCond.ParachuteDeployed));

        // A third press cannot reopen while the record stands (:1148).
        rules.AirJump(in request);
        Assert.False(state.InCond(TFCond.ParachuteActive));

        // The landing clears the pair (tf_player_shared.cpp:3131's remover):
        // the next airborne window deploys again.
        var fresh = default(TFCondMask);
        state.SetConditions(in fresh);
        rules.AirJump(in request);
        Assert.True(state.InCond(TFCond.ParachuteActive));
        Assert.True(state.InCond(TFCond.ParachuteDeployed));
    }

    /// <summary>The owners seam as the side's registry holds it: the manager grant, nothing else answerable here.</summary>
    private sealed class OwnersOf(TFPlayerItems items) : ITFWeaponOwners
    {
        /// <summary>The active weapon's item, set by the fact that needs it.</summary>
        public IAttributeProvider? Active;

        /// <summary>Items by weapon row, for the name-checked asks (the BFB's hype speed).</summary>
        public Dictionary<int, Dictionary<string, IAttributeProvider>> ByName { get; } = [];

        /// <summary>The drawn-bow refusal, set by the fact that needs it.</summary>
        public bool MayJump { get; set; } = true;

        public TFClass ClassOf(int playerIndex) => TFClass.Undefined;
        public TFConditions? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;

        public IAttributeProvider? ItemOf(int playerIndex, string weaponName)
            => ByName.TryGetValue(playerIndex, out var byName) && byName.TryGetValue(weaponName, out var named) ? named : null;

        public IAttributeProvider? ActiveItemOf(int playerIndex) => Active;
        public IAttributeProvider? ManagerOf(int playerIndex) => items.ManagerOf(playerIndex);
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
        public bool OwnerCanJump(int playerIndex) => MayJump;
    }

    [Fact]
    public void A_scout_dashes_once_the_third_press_is_refused_and_ground_gives_it_back()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 100f };

        // The first press left the ground (the core's jump); the second is in the air.
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(1, state.AirDash);
        Assert.Equal(0f, rules.AirJump(in request));
        Assert.Equal(1, state.AirDash);

        rules.Grounded(Index);
        Assert.Equal(0, state.AirDash);
        Assert.Equal(TFJump.DashSpeed, rules.AirJump(in request));
    }

    [Fact]
    public void A_landing_clears_the_scatterguns_airtime_with_the_dash()
    {
        // SetGroundEntity's resets (tf_gamemovement.cpp:2956-2958): the dash's
        // count and the shove's two latches ride the same landing.
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout) ?? throw new InvalidOperationException("no state");

        state.AirDash = 1;
        state.ScattergunJump = true;
        state.WeaponKnockback = true;
        rules.Grounded(Index);

        Assert.Equal(0, state.AirDash);
        Assert.False(state.ScattergunJump);
        Assert.False(state.WeaponKnockback);
    }

    [Fact]
    public void A_soldier_is_refused_and_nothing_is_counted()    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Soldier);

        Assert.Equal(0f, rules.AirJump(new JumpRequest { PlayerIndex = Index }));
        Assert.Equal(0, state.AirDash);
    }

    [Fact]
    public void No_carrier_refuses_the_dash()
        => Assert.Equal(0f, Make().Rules.AirJump(new JumpRequest { PlayerIndex = Index }));

    [Theory]
    [InlineData(TFClass.Scout, 65f)]
    [InlineData(TFClass.Soldier, 68f)]
    [InlineData(TFClass.HeavyWeapons, 75f)]
    [InlineData(TFClass.Sniper, 75f)]
    [InlineData(TFClass.Undefined, 72f)]
    public void The_standing_eye_is_the_classs(TFClass c, float eye)
    {
        var (rules, players) = Make();
        players.Add(Index, c);

        Assert.Equal(eye, rules.EyeHeight(Index, 72f));
    }

    /// <summary>One command stream, run twice from fresh rules and state: the answers are byte-identical.</summary>
    [Fact]
    public void The_same_input_stream_gives_the_same_answers()
    {
        static byte[] Run()
        {
            var (rules, players) = Make();
            var state = players.Add(Index, TFClass.Scout);
            var answers = new List<float>();
            var random = new Random(1234);
            for (int i = 0; i < 200; i++)
                Step(rules, state, random, answers);
            return answers.SelectMany(BitConverter.GetBytes).ToArray();
        }

        byte[] first = Run(), second = Run();
        Assert.Equal(first, second);
        Assert.Contains(TFJump.DashSpeed, Floats(first));   // the stream reached a dash
    }

    private static IEnumerable<float> Floats(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 4)
            yield return BitConverter.ToSingle(bytes, i);
    }

    private static readonly TFClass[] Normal =
    [
        TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman, TFClass.Medic,
        TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer,
    ];

    private static readonly TFCond[] Milestone =
    [
        TFCond.Aiming, TFCond.Disguised, TFCond.Stealthed, TFCond.DisguisedAsDispenser,
        TFCond.SodapopperHype, TFCond.HalloweenKart, TFCond.HalloweenSpeedBoost,
    ];

    /// <summary>One random command: sometimes a class or condition change, then the three asks, recorded.</summary>
    private static void Step(TFMovementRules rules, TFPlayerNetState state, Random random, List<float> answers)
    {
        int roll = random.Next(10);
        if (roll == 0)
            state.SetClass(Normal[random.Next(Normal.Length)]);
        else if (roll == 1)
            state.SetConditions(random.Next(3) == 0 ? default : TFCondMask.Of(Milestone[random.Next(Milestone.Length)]));
        else if (roll == 2)
            rules.Grounded(Index);
        state.DisguiseClass = (int)Normal[random.Next(Normal.Length)];

        answers.Add(rules.MaxSpeedScale(Index, 320f, false, 0));
        answers.Add(rules.AirJump(new JumpRequest { PlayerIndex = Index }));
        answers.Add(rules.EyeHeight(Index, 72f));
        answers.Add(state.AirDash);
    }

    /// <summary>
    /// The rules built in a server container and in a client container (each
    /// side's own copy of the shared source) over the same carrier states give
    /// identical answers, and leave identical state, for 100 random inputs.
    /// </summary>
    [Fact]
    public void Server_and_client_rules_agree_over_100_random_inputs()
    {
        var serverPlayers = new TFMovementFixtures.Players();
        var server = TFMovementFixtures.ServerServices(serverPlayers).Create<TFMovementRules>();

        var clientPlayers = new TFMovementFixtures.ClientPlayers();
        var client = TFMovementFixtures.ClientServices(clientPlayers)
            .Create<client::TF2.Shared.Movement.TFMovementRules>();

        var s = serverPlayers.Add(Index, TFClass.Scout);
        var c = clientPlayers.Add(Index, TFClass.Scout);
        var random = new Random(77);
        int dashes = 0, distinctSpeeds = 0;
        var speeds = new HashSet<float>();

        for (int i = 0; i < 100; i++)
        {
            int playerClass = (int)Normal[random.Next(Normal.Length)];
            var cond = random.Next(3) == 0 ? default : TFCondMask.Of(Milestone[random.Next(Milestone.Length)]);
            int disguise = (int)Normal[random.Next(Normal.Length)];
            int airDash = random.Next(3);
            bool ground = random.Next(4) == 0;

            s.PlayerClass = c.PlayerClass = playerClass;
            s.DisguiseClass = c.DisguiseClass = disguise;
            s.AirDash = c.AirDash = airDash;
            s.SetConditions(cond);
            (c.PlayerCond, c.PlayerCondEx, c.PlayerCondEx2, c.PlayerCondEx3, c.PlayerCondEx4)
                = (cond.Word0, cond.Word1, cond.Word2, cond.Word3, cond.Word4);
            if (ground)
            {
                server.Grounded(Index);
                client.Grounded(Index);
            }

            var request = new JumpRequest { PlayerIndex = Index, VerticalSpeed = 50f };
            float speed = server.MaxSpeedScale(Index, 320f, false, 0);
            Assert.Equal(speed, client.MaxSpeedScale(Index, 320f, false, 0));
            float dash = server.AirJump(in request);
            Assert.Equal(dash, client.AirJump(in request));
            Assert.Equal(server.EyeHeight(Index, 72f), client.EyeHeight(Index, 72f));
            Assert.Equal(s.AirDash, c.AirDash);

            if (dash > 0f)
                dashes++;
            if (speeds.Add(speed))
                distinctSpeeds++;
        }

        Assert.True(dashes > 0, "some inputs dashed");
        Assert.True(distinctSpeeds > 3, $"the inputs reached several arms ({distinctSpeeds} speeds)");
    }

    /// <summary>
    /// Stock's fall-through (<c>TauntMove</c> :710-715): a plain taunt without
    /// the sliding gate's yes leaves the command exactly as sent — stock's
    /// <c>tf_allow_sliding_taunt</c> default and the unset item flag.
    /// </summary>
    [Fact]
    public void TauntingWithoutSlidingLeavesTheCommandToTheWalk()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        state.SetConditions(TFCondMask.Of(TFCond.Taunting));

        Assert.Null(rules.MovementTaunt(Index, 1f / 60f, 300f));
    }

    /// <summary>
    /// The sliding arm at the item defaults (:644-645; tf_player.cpp:1041-1044
    /// leave speed and ramp at zero): the smoothed move lands on zero and the
    /// caps ride with it (:698-706).
    /// </summary>
    [Fact]
    public void AThrillingTauntSlidesAtTheItemDefaults()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Scout);
        state.SetConditions(TFCondMask.Of([TFCond.Taunting, TFCond.HalloweenThriller]));

        Assert.Equal(new MovementTaunt(0f, 0f, 0f), rules.MovementTaunt(Index, 1f / 60f, 300f));
    }

    /// <summary>A side whose carrier has not arrived answers like stock's no-taunt fall-through.</summary>
    [Fact]
    public void WithoutACarrierTheTauntLeavesTheWalkAlone()
    {
        var (rules, _) = Make();

        Assert.Null(rules.MovementTaunt(Index, 1f / 60f, 300f));
    }

    //=========================================================================
    // The charge's command (CTFGameMovement::ChargeMove,
    // tf_gamemovement.cpp:484-532): the speed and the turn limit.
    //=========================================================================

    /// <summary>
    /// The charge rides stock's numbers: the max speed and the forward move
    /// both <c>tf_max_charge_speed</c>'s 750 (:508, :513), the side and up
    /// moves zeroed (:514-515) — the charge's turn is the facing's, no strafe
    /// or jump bends it — and the buttons held to the charge's own attack2
    /// with the held swing's attack riding along (:516-529). Not charging, the
    /// command walks as sent (:486's else).
    /// </summary>
    [Fact]
    public void TheChargeHasStocksSpeedAndTurnLimit()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Demoman);
        int buttons = GameMovement.InAttack2 | GameMovement.InJump | GameMovement.InAttack;

        Assert.Null(rules.MovementCharge(Index, buttons, 300f, 100f, 50f));

        state.SetConditions(TFCondMask.Of(TFCond.ShieldCharge));

        Assert.Equal(new MovementCharge(TFMaxSpeed.MaxChargeSpeed,
                                        GameMovement.InAttack2 | GameMovement.InAttack,
                                        TFMaxSpeed.MaxChargeSpeed, 0f, 0f),
                     rules.MovementCharge(Index, buttons, 300f, 100f, 50f));
        Assert.Equal(new MovementCharge(TFMaxSpeed.MaxChargeSpeed, GameMovement.InAttack,
                                        TFMaxSpeed.MaxChargeSpeed, 0f, 0f),
                     rules.MovementCharge(Index, buttons & ~GameMovement.InAttack2, 300f, 100f, 50f));
    }

    /// <summary>The boots' toll as the player's own percentage, on a fresh manager.</summary>
    private static AttributeContainer TollManager()
    {
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5099, "attr 5099", TFMaxSpeed.ShieldRequiredSpeedAttrib,
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 0.8f));
        return manager;
    }

    /// <summary>
    /// The shield wearer's toll (tf_player_shared.cpp:11005-11008): while the
    /// demoman wears the shield, the boots'
    /// <c>mult_player_movespeed_shieldrequired</c> scales the speed on the
    /// player's own walk, right after <c>mult_player_movespeed</c>.
    /// </summary>
    [Fact]
    public void The_shield_wearer_takes_the_boots_speed_toll()
    {
        var owners = new TF2.Tests.Weapons.FakeOwners { Shield = true };
        owners.Managers[Index] = TollManager();
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Demoman);
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks());

        Assert.Equal(0.8f * TFMovementFixtures.Server()[TFClass.Demoman].MaxSpeed,
                     rules.MaxSpeedScale(Index, 320f, false, 0), 4);
    }

    [Fact]
    public void A_player_without_the_shield_takes_no_toll()
    {
        var owners = new TF2.Tests.Weapons.FakeOwners();
        owners.Managers[Index] = TollManager();
        var players = new TFMovementFixtures.Players();
        _ = players.Add(Index, TFClass.Demoman);
        var rules = TFMovementFixtures.Rules(players, owners: owners, hooks: new AttributeHooks());

        Assert.Equal(TFMovementFixtures.Server()[TFClass.Demoman].MaxSpeed,
                     rules.MaxSpeedScale(Index, 320f, false, 0), 4);
    }

    /// <summary>
    /// The B.A.S.E. Jumper's trim (<c>CTFGameMovement::FullWalkMove</c>,
    /// tf_gamemovement.cpp:2626-2638): open, the chute caps a downward fall
    /// at the z convar's ceiling; closed, the velocity stands as it is, and
    /// so does one that is already under it.
    /// </summary>
    [Fact]
    public void The_open_chute_caps_the_fall_and_the_closed_one_does_not()
    {
        var (rules, players) = Make();
        var state = players.Add(Index, TFClass.Demoman);
        var falling = new Vector(0f, 0f, -500f);

        Assert.Equal(-500f, rules.ParachuteFallCap(Index, falling).Z, 3);

        state.SetConditions(TFCondMask.Of(TFCond.ParachuteActive));
        Assert.Equal(-100f, rules.ParachuteFallCap(Index, falling).Z, 3);

        Assert.Equal(-20f, rules.ParachuteFallCap(Index, new Vector(0f, 0f, -20f)).Z, 3);
        Assert.Equal(40f, rules.ParachuteFallCap(Index, new Vector(0f, 0f, 40f)).Z, 3);
    }

    /// <summary>
    /// The chute's xy damp (:2630-2638): past the drag an axis is trimmed to
    /// drag plus <c>(speed − drag) / 3 − 10</c> — the clamp keeps its sign;
    /// inside the drag nothing moves.
    /// </summary>
    [Fact]
    public void The_open_chute_damps_xy_past_the_drag()
    {
        var (rules, players) = Make();
        players.Add(Index, TFClass.Demoman)
               .SetConditions(TFCondMask.Of(TFCond.ParachuteActive));

        Vector damped = rules.ParachuteFallCap(Index, new Vector(310f, 50f, -200f));
        Assert.Equal(293.3333f, damped.X, 3);   // 300 + (310 − 300)/3 − 10
        Assert.Equal(50f, damped.Y, 3);
        Assert.Equal(-100f, damped.Z, 3);

        Vector south = rules.ParachuteFallCap(Index, new Vector(-400f, -20f, -200f));
        Assert.Equal(-323.3333f, south.X, 3);   // 400 is 100 past the drag
        Assert.Equal(-20f, south.Y, 3);
    }

    /// <summary>
    /// Burning parachutists take the on-fire ceiling (:2628): with
    /// TF_COND_BURNING up too the z cap is the onfire holder's, else the
    /// plain z holder's declared −100.
    /// </summary>
    [Fact]
    public void The_burning_chute_takes_the_on_fire_ceiling()
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(Index, TFClass.Demoman);
        var convars = TFMovementFixtures.ConVars();
        convars.Declared<TFParachuteMaxSpeedOnfireZConVar>().SetValue(-50f);
        var rules = TFMovementFixtures.Rules(players, convars: convars);

        state.SetConditions(TFCondMask.Of(TFCond.ParachuteActive));
        Assert.Equal(-100f, rules.ParachuteFallCap(Index, new Vector(0f, 0f, -500f)).Z, 3);

        state.SetConditions(TFCondMask.Of([TFCond.ParachuteActive, TFCond.Burning]));
        Assert.Equal(-50f, rules.ParachuteFallCap(Index, new Vector(0f, 0f, -500f)).Z, 3);
    }
}

/// <summary>
/// <see cref="TFChargeCap"/>: <c>CTFPlayerShared::CalculateChargeCap</c>
/// (tf_player_shared.cpp:6100-6115) — the base 0.45 degrees (:6102) through the
/// wearer's <c>charge_turn_control</c> hook (:6104), times the frametime scale
/// while <c>tf_demoman_charge_frametime_scaling</c> is on (:6107-6112): the
/// remap of the frame time over [<c>TICK_INTERVAL</c> × 0.2, × 2] onto
/// [0.25, 2] with the constants of :107-108. One arm per fact.
/// </summary>
public sealed class TFChargeCapTests
{
    private static readonly AttributeHooks Hooks = new();

    /// <summary>
    /// The scale's band is anchored on the tick, not on one second: a frame of
    /// exactly one tick sits inside the band at (tick − 0.2·tick) / (2·tick −
    /// 0.2·tick), and the remap answers 0.25 + that × 1.75 — for one tick
    /// itself 1.0277…, so the cap is 0.45 × it ≈ 0.4625 (:6110). No attribute:
    /// a player with no manager leaves the hook value untouched (:6104).
    /// </summary>
    [Fact]
    public void One_ticks_frametime_lifts_the_cap_above_the_base()
    {
        float cap = TFChargeCap.Calculate(0.015f, 0.015f, true, Hooks, null);
        Assert.Equal(0.4625f, cap, 4);
    }

    /// <summary>
    /// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> (:6104): the multiplier format
    /// multiplies, so a wearer's ×2 <c>charge_turn_control</c> doubles the
    /// base before any frametime scale — the scale off answers 0.45 × 2 = 0.9.
    /// </summary>
    [Fact]
    public void The_charge_turn_control_hook_scales_the_cap()
    {
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(584, "attr 584", "charge_turn_control",
            AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 2f));

        float cap = TFChargeCap.Calculate(0.015f, 0.015f, false, Hooks, manager);
        Assert.Equal(0.9f, cap, 4);
    }

    /// <summary>
    /// The convar gates the multiplier (:6107): at its stock declared default
    /// the scale runs and a tick's frame lifts the cap above the base, and with
    /// the holder flipped off the answer is the plain hook value, 0.45 exactly.
    /// </summary>
    [Fact]
    public void The_scaling_convar_gates_the_frametime_multiplier()
    {
        var convars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>()));
        convars.RegisterTypes([typeof(TFDemomanChargeFrametimeScalingConVar)]);
        var scaling = convars.Declared<TFDemomanChargeFrametimeScalingConVar>();

        Assert.True(scaling.Value, "stock declares the default 1");
        Assert.Equal(0.4625f, TFChargeCap.Calculate(0.015f, 0.015f, scaling.Value, Hooks, null), 4);

        scaling.SetValue(false);
        Assert.Equal(0.45f, TFChargeCap.Calculate(0.015f, 0.015f, scaling.Value, Hooks, null));
    }

    /// <summary>
    /// Below the band's low input the remap clamps to its floor (mathlib's
    /// clamped lerp): a half-tick frame of 0.0015 s sits under 0.2 × tick, so
    /// the scale sticks at 0.25 and the cap at 0.45 × it (:6110).
    /// </summary>
    [Fact]
    public void A_frame_under_the_low_bound_lands_on_the_scale_floor()
    {
        float cap = TFChargeCap.Calculate(0.0015f, 0.015f, true, Hooks, null);
        Assert.Equal(0.1125f, cap, 4);
    }

    /// <summary>
    /// Above the band's high input the remap clamps to its ceiling: a two-tick
    /// frame sits at the high end, so the scale answers 2 and the cap doubles
    /// the hook value to 0.9 (:6110).
    /// </summary>
    [Fact]
    public void A_frame_over_the_high_bound_lands_on_the_scale_ceiling()
    {
        float cap = TFChargeCap.Calculate(0.06f, 0.015f, true, Hooks, null);
        Assert.Equal(0.9f, cap, 4);
    }
}
