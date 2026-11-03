extern alias client;

using System.IO;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Tests.Weapons;

using Xunit;

using ClientArsenal = client::SourceSharp.Client.ClientArsenal;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using ClientViewModel = client::SourceSharp.Client.Standalone.ClientSsViewModel;
using ClientDirectory = client::SourceSharp.Client.Standalone.ClientEntityDirectory;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientStandalone = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using ClientAmmoCatalog = client::SourceSharp.Shared.AmmoCatalog;
using ClientAnimNames = client::SourceSharp.Shared.WeaponAnimNames;
using ClientArms = client::SourceSharp.Shared.IWeaponArms;
using ClientCatalog = client::SourceSharp.Shared.WeaponCatalog;
using ClientCommand = client::SourceSharp.Shared.WeaponCommand;
using ClientCond = client::TF2.Shared.TFCond;
using ClientCondMask = client::TF2.Shared.Conditions.TFCondMask;
using ClientCrits = client::TF2.Shared.ITFCritSettings;
using ClientCritSettings = client::TF2.Shared.TFCritSettings;
using ClientBuckets = client::TF2.Shared.TFCritBuckets;
using ClientContainer = client::SourceSharp.Abi.ServiceContainer;
using ClientData = client::TF2.Shared.Player.ITFPlayerData;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientJump = client::TF2.Shared.Movement.TFJump;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientMachine = client::SourceSharp.Shared.WeaponLogic;
using ClientNetCatalog = client::SourceSharp.Abi.NetStateCatalog;
using ClientParts = client::TF2.Shared.TFWeaponParts;
using ClientRules = client::TF2.Shared.Movement.TFMovementRules;
using ClientScriptTypes = client::TF2.Shared.TFWeaponTypes;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientShot = client::SourceSharp.Shared.ShotDamage;
using ClientType = client::SourceSharp.Shared.WeaponType;
using ClientWeaponBehaviours = client::TF2.Shared.TFWeaponBehaviours;
using ClientWeaponNet = client::SourceSharp.Shared.WeaponNetState;
using ClientBehaviours = client::SourceSharp.Shared.WeaponBehaviours;
using ClientChain = client::TF2.Shared.TFWeaponHookChain;
using ClientClass = client::TF2.Shared.TFClass;
using ClientClassTable = client::TF2.Shared.TFClassTable;
using ClientCarriers = client::TF2.Client.Player.TFClientCarriers;
using ClientHooks = client::TF2.Shared.Econ.AttributeHooks;
using ClientItemSchema = client::TF2.Shared.Econ.ItemSchema;
using ClientPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientPrediction = client::TF2.Client.Player.TFPlayerPrediction;
using ClientViewmodelActivity = client::SourceSharp.Shared.ViewmodelActivity;
using ClientViewModelNet = client::SourceSharp.Shared.ViewModelNetState;
using Views = client::TF2.Shared.Player.TFPlayerNetStateViews;

namespace TF2.Tests;

/// <summary>
/// Stage 3.4: a TF weapon, TF's conditions and TF's movement rules predicted
/// on the client and judged against the server's word. The scattergun's shot
/// through the replay's ring (ClientArsenal's machinery over TF's behaviour),
/// the heavy's spin-up condition and the scout's air dash through the player
/// ring (TFPlayerPrediction's machinery over TF's movement rules) — and the
/// source-parity fact that both sides' bindings feed the rules the same
/// <c>tf_player_data</c> fields.
/// </summary>
public sealed class TF2ClientPredictionTests
{
    private const int Local = 1;
    private const string Scattergun = "tf_weapon_scattergun";
    private static readonly EntityHandle LocalPlayer = new(0x0001_0000u | Local);
    private static readonly EntityHandle WeaponHandle = new(0x0100_0041u);
    private static readonly EntityHandle ViewModelHandle = new(0x0100_0042u);
    private const float CommandClock = 10f;

    //-------------------------------------------------------------------------
    // The client world: the arsenal's side, and a twin machine beside it
    // playing the server's role over the same rows and the same command.
    //-------------------------------------------------------------------------

    /// <summary>The shipped scripts and the core catalogues, the client's copies.</summary>
    private sealed class ClientWorld
    {
        public readonly ClientLog Log = new();
        public readonly ClientEngine Engine = new();
        public readonly ClientScripts Scripts;
        public readonly ClientCatalog Catalog;
        public readonly ClientAmmoCatalog Ammo;

        public ClientWorld()
        {
            var files = new Served(WeaponFixtures.ShippedWithClasses());
            Scripts = new ClientScripts(Log, files);
            Catalog = new ClientCatalog(() => [new ClientScriptTypes(Scripts, new ClientClassTable(Log, files), new ClientItemSchema(files, Log))], Log);
            Ammo = new ClientAmmoCatalog(() => [new client::TF2.Shared.TFAmmoTypes()], Log);

            Engine.Returns("Hud.LocalPlayerIndex", Local);
            Engine.Returns("EntityRender.LookupSequenceByActivity", 0);   // the fire anim resolves
        }

        /// <summary>
        /// A fresh side's parts: its own crit buckets and settings, as each
        /// side's really are. The extra hooks join the chain after TF2's own
        /// (a fact's forced crit).
        /// </summary>
        public ClientParts Parts(params client::TF2.Shared.ITFWeaponHook[] extraHooks)
        {
            List<client::TF2.Shared.ITFWeaponHook> hooks =
                [new client::TF2.Shared.TFAttributeWeaponHook(new ClientHooks()), new client::TF2.Shared.TFCritBoostHook()];
            hooks.AddRange(extraHooks);
            return new(Scripts, ClientChain.Of([.. hooks]), new ClientBuckets(), new NoRandomCrits());
        }

        public ClientBehaviours Behaviours(ClientParts parts)
            => new(() => [new ClientWeaponBehaviours(parts)], Log);

        /// <summary>The shipped pack, served as the client's files.</summary>
        private sealed class Served(TFClassTableTests.Files files) : ClientFiles
        {
            public int GameFileSize(string path) => files.GameFileSize(path);
            public string? ReadGameFile(string path) => files.ReadGameFile(path);
            public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
            public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
        }

        private sealed class NoRandomCrits : ClientCrits
        {
            public ClientCritSettings Current { get; } = new(RandomCrits: false);
        }
    }

    /// <summary>The arsenal's pair, found by class as the replay finds it.</summary>
    private sealed class Pair : ClientStandalones
    {
        public readonly ClientDirectory Directory = new();

        public IReadOnlyList<ClientStandalone> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandalone? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);
        public IEnumerable<ClientStandalone> OfClass(string? className) => Directory.OfClass(className);
        public IEnumerable<ClientStandalone> OfName(string? name) => Directory.OfName(name);
    }

    /// <summary>One armed scattergun: the arsenal's pair plus a twin machine's own states.</summary>
    private sealed class Rig
    {
        public readonly ClientWorld World = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientViewModel ViewModel;
        public readonly ClientArsenal Arsenal;
        public readonly ClientWeaponNet TwinWeapon = new();
        public readonly ClientViewModelNet TwinViewModel = new();
        public readonly TwinArms TwinArms;
        public readonly ClientMachine Twin;

        public Rig(params client::TF2.Shared.ITFWeaponHook[] extraHooks)
        {
            int typeId = World.Catalog.IndexOf(Scattergun);

            Weapon = new ClientWeapon(World.Engine) { Handle = WeaponHandle };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = Local;
            Weapon.State.WeaponTypeId = typeId;
            Weapon.State.Clip = 6;
            Weapon.State.Reserve = 40;

            ViewModel = new ClientViewModel(World.Engine, World.Engine) { Handle = ViewModelHandle };
            ViewModel.State.BindLocal();
            ViewModel.State.OwnerIndex = Local;

            var pair = new Pair();
            pair.Directory.Bind(Weapon.Handle, -1, Weapon);
            pair.Directory.Bind(ViewModel.Handle, -1, ViewModel);

            Arsenal = new ClientArsenal(World.Log, pair, World.Engine, World.Engine, World.Engine,
                                        ClientNetCatalog.Of(typeof(ClientArsenal).Assembly, World.Log),
                                        World.Catalog, World.Ammo, World.Behaviours(World.Parts(extraHooks)));

            TwinWeapon.BindLocal();
            TwinWeapon.OwnerIndex = Local;
            TwinWeapon.WeaponTypeId = typeId;
            TwinWeapon.Clip = 6;
            TwinWeapon.Reserve = 40;
            TwinViewModel.BindLocal();
            TwinViewModel.OwnerIndex = Local;

            TwinArms = new TwinArms(World.Engine, ViewModelHandle);
            Twin = new ClientMachine(TwinArms, World.Catalog, World.Behaviours(World.Parts(extraHooks)), World.Ammo);
        }

        /// <summary>The server's word arriving: the decoded states change and count a decode.</summary>
        public void Arrive()
        {
            Weapon.State.Decode(TwinWeapon.Snapshot());
            Weapon.OnDataChanged(default, DataUpdateType.DataTableChanged);
            ViewModel.State.Decode(TwinViewModel.Snapshot());
            ViewModel.OnDataChanged(default, DataUpdateType.DataTableChanged);
        }
    }

    /// <summary>The twin machine's arms: records the shot, resolves the anim as the replay does.</summary>
    private sealed class TwinArms : ClientArms
    {
        public readonly List<(string Weapon, int Pellets, float Spread, ClientShot Shot)> Bullets = new();
        public readonly List<string> Sounds = new();
        public readonly List<(string Particle, string Weapon)> Flashes = new();
        public readonly List<(int ItemDef, int WeaponId, string Weapon)> Brasses = new();

        private readonly ClientEngine _engine;
        private readonly EntityHandle _viewModel;

        public TwinArms(ClientEngine engine, EntityHandle viewModel)
        {
            _engine = engine;
            _viewModel = viewModel;
        }

        public void Sound(string soundScript) => Sounds.Add(soundScript);
        public void FireBullets(in ClientType type, int pellets, float spreadDegrees)
            => throw new InvalidOperationException("the TF gun fires with the shot the behaviour decided");
        public void FireBullets(in ClientType type, int pellets, float spreadDegrees, ClientShot shot)
            => Bullets.Add((type.Name, pellets, spreadDegrees, shot));
        public void MeleeSwing(in ClientType type) => throw new InvalidOperationException("a scattergun is not a melee");
        public void Launch(in ClientType type, string projectile, float speed, bool thrown)
            => throw new InvalidOperationException("a scattergun launches nothing");
        public void MuzzleFlash(in ClientType type, string particleEffect) => Flashes.Add((particleEffect, type.Name));
        public void ShellEject(in ClientType type, int itemDefIndex, int weaponId)
            => Brasses.Add((itemDefIndex, weaponId, type.Name));
        public int LookupSequence(WeaponAnim anim)
            => ClientAnimNames.Resolve(_engine, _viewModel, anim, ClientViewmodelActivity.Stock(), Local);
        public float AnimDuration(WeaponAnim anim) => 0f;
    }

    private static UserCmd Command(int number, bool attack)
        => new() { CommandNumber = number, Buttons = attack ? (int)InputButtons.Attack : 0 };

    //-------------------------------------------------------------------------
    // A: the scattergun's shot through the weapon ring.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_scattergun_shot_spends_a_clip_round_on_the_client_and_the_acknowledged_server_state_agrees()
    {
        var rig = new Rig();
        var basis = Command(10, attack: false);
        var cmd = Command(11, attack: true);
        var ack = Command(11, attack: false);

        rig.Arsenal.OnPlayerCommand(LocalPlayer, in basis, 2, false);

        // The server's twin runs the command, and the client's replay predicts it.
        rig.Twin.RunCommand(rig.TwinWeapon, rig.TwinViewModel, cmd.Buttons, CommandClock, effectsLive: true,
                            ClientCommand.For(in cmd, WeaponHandle, LocalPlayer));
        rig.Arsenal.OnPlayerCommand(LocalPlayer, in cmd, 1, true, CommandClock);

        // The client spent the round before any packet said to.
        var predicted = rig.Arsenal.PredictedStateOf(rig.Weapon);
        Assert.NotNull(predicted);
        Assert.Equal(5, predicted!.Clip);

        // And so did the server: one round, ten pellets at the row's cone, no crit.
        Assert.Equal(5, rig.TwinWeapon.Clip);
        var (weapon, pellets, spread, shot) = Assert.Single(rig.TwinArms.Bullets);
        Assert.Equal(Scattergun, weapon);
        Assert.Equal(10, pellets);
        Assert.Equal(3.8704f, spread, 3);
        Assert.False(shot.Critical);
        Assert.Equal(new[] { "Weapon_Scatter_Gun.Single" }, rig.TwinArms.Sounds);

        // The server's word arrives and the acknowledged command is judged.
        rig.Arrive();
        rig.Arsenal.OnPlayerCommand(LocalPlayer, in ack, 2, false);

        var (saves, holds, tolerated, mispredicts, _, _) = rig.Arsenal.Counts;
        Assert.Equal((1, 2, 0, 0), (saves, holds, tolerated, mispredicts));
    }

    [Fact]
    public void An_acknowledged_state_that_did_not_spend_the_round_is_judged_a_misprediction()
    {
        var rig = new Rig();
        var basis = Command(10, attack: false);
        var cmd = Command(11, attack: true);
        var ack = Command(11, attack: false);

        rig.Arsenal.OnPlayerCommand(LocalPlayer, in basis, 2, false);

        // The client predicts the shot; the server never saw the press (it idles).
        rig.Twin.RunCommand(rig.TwinWeapon, rig.TwinViewModel, 0, CommandClock, effectsLive: true,
                            ClientCommand.For(in cmd, WeaponHandle, LocalPlayer));
        rig.Arsenal.OnPlayerCommand(LocalPlayer, in cmd, 1, true, CommandClock);

        rig.Arrive();
        rig.Arsenal.OnPlayerCommand(LocalPlayer, in ack, 2, false);

        var (saves, holds, _, mispredicts, _, _) = rig.Arsenal.Counts;
        // The scattergun's behaviour answers the looping seam with the base's
        // restart, so the replay's re-send bumps parity alike on both sides'
        // computations: the viewmodel ring agrees and holds its word. Only the
        // weapon ring — the round the ack never spent — stands judged.
        Assert.Equal((1, 1, 1), (saves, holds, mispredicts));   // the weapon ring mispredicts
        Assert.Contains("MISPREDICTION", rig.World.Log.Text);
    }

    //-------------------------------------------------------------------------
    // A2: the shot's own effects — the crit sound, the flash, the brass.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A chain hook that decides the crit whole, as a kind's own helpers do
    /// (stock's <c>CalcIsAttackCriticalHelperNoCritCheck</c> overrides answered
    /// by <c>CritAnswer</c>): every attack is a crit, on both sides' runs.
    /// </summary>
    private sealed class AlwaysCrit : client::TF2.Shared.ITFWeaponHook
    {
        public int HookOrder => 0;

        public client::TF2.Shared.TFCritResult? CritAnswer(in client::TF2.Shared.TFWeaponHookContext weapon)
            => new(Critical: true);
    }

    /// <summary>
    /// <c>PlayWeaponShootSound</c> (<c>tf_weaponbase_gun.cpp:829-839</c>): a
    /// crit attack takes the script's BURST, its SINGLE sound otherwise (fact
    /// A above hears the plain shot). The roll is forced through the chain and
    /// answered by both sides — the server's twin and the client's
    /// prediction — so the ring still agrees: a crit of a weapon that does not
    /// fire rapid changes the sound and the damage flag, and no state.
    /// </summary>
    [Fact]
    public void A_predicted_crit_takes_the_burst_sound_and_the_ring_still_agrees()
    {
        var rig = new Rig(new AlwaysCrit());
        var basis = Command(10, attack: false);
        var cmd = Command(11, attack: true);
        var ack = Command(11, attack: false);

        rig.Arsenal.OnPlayerCommand(LocalPlayer, in basis, 2, false);

        rig.Twin.RunCommand(rig.TwinWeapon, rig.TwinViewModel, cmd.Buttons, CommandClock, effectsLive: true,
                            ClientCommand.For(in cmd, WeaponHandle, LocalPlayer));
        rig.Arsenal.OnPlayerCommand(LocalPlayer, in cmd, 1, true, CommandClock);

        // The shot is the crit's: BURST on the live run, and the bullet carries the flag.
        Assert.Equal(new[] { "Weapon_Scatter_Gun.SingleCrit" }, rig.TwinArms.Sounds);
        var (_, _, _, shot) = Assert.Single(rig.TwinArms.Bullets);
        Assert.True(shot.Critical);

        // The server's word agrees: the same roll spends the same round.
        rig.Arrive();
        rig.Arsenal.OnPlayerCommand(LocalPlayer, in ack, 2, false);

        var (saves, holds, tolerated, mispredicts, _, _) = rig.Arsenal.Counts;
        Assert.Equal((1, 2, 0, 0), (saves, holds, tolerated, mispredicts));
    }

    /// <summary>
    /// <c>DoFireEffects</c>' flash (<c>tf_weaponbase_gun.cpp:330-368</c>) and
    /// <c>OnFireEvent</c>'s brass (<c>tf_weaponbase.cpp:4950-4977</c>): the
    /// first prediction of the command asks the script's muzzle particle, and
    /// the brass carrying the item's definition index — no item in the hands,
    /// stock's <c>GetItem()</c> null, so 0 — and the script's weapon id. The
    /// replay (a second run of the command, one fire delay later so the gun
    /// fires again as it re-runs) asks for none of them a second time: the
    /// state is computed again, the effects are the first prediction's.
    /// </summary>
    [Fact]
    public void The_first_prediction_asks_the_muzzle_particle_and_the_brass_and_the_replay_asks_neither()
    {
        var rig = new Rig();
        var cmd = Command(11, attack: true);

        rig.Twin.RunCommand(rig.TwinWeapon, rig.TwinViewModel, cmd.Buttons, CommandClock, effectsLive: true,
                            ClientCommand.For(in cmd, WeaponHandle, LocalPlayer));

        var (particle, flashWeapon) = Assert.Single(rig.TwinArms.Flashes);
        Assert.Equal("muzzle_scattergun", particle);
        Assert.Equal(Scattergun, flashWeapon);

        var (itemDef, weaponId, brassWeapon) = Assert.Single(rig.TwinArms.Brasses);
        Assert.Equal(0, itemDef);                                        // stock's GetItem() null → 0
        Assert.Equal((int)client::TF2.Shared.TFWeaponId.Scattergun, weaponId);   // TF_WEAPON_SCATTERGUN
        Assert.Equal(Scattergun, brassWeapon);

        // The replay, past the fire delay so the tail runs again: the counts hold at one.
        rig.Twin.RunCommand(rig.TwinWeapon, rig.TwinViewModel, cmd.Buttons, CommandClock + 1f, effectsLive: false,
                            ClientCommand.For(in cmd, WeaponHandle, LocalPlayer));

        Assert.Single(rig.TwinArms.Flashes);
        Assert.Single(rig.TwinArms.Brasses);
        Assert.Single(rig.TwinArms.Sounds);
    }

    //-------------------------------------------------------------------------
    // B and C: conditions and movement through the player ring.
    //-------------------------------------------------------------------------

    /// <summary>The player frame: the carriers, the local carrier, the prediction.</summary>
    private sealed class Frame
    {
        /// <summary>No client entities: the skin decision finds no body.</summary>
        private sealed class NoStandalones : client::SourceSharp.Client.IClientStandalones
        {
            public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];
            public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
        }

        public readonly ClientLog Log = new();
        public readonly ClientCarriers Carriers = new();
        public readonly ClientPlayerData Carrier;
        public readonly ClientPrediction Prediction;

        /// <summary>No condition arms: the driver's chain is empty.</summary>
        private static client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook> NoHooks() =>
            new NoConditionHooks();

        private sealed class NoConditionHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
        {
            public IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

            public void Each(string what, Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
        }

        public Frame()
        {
            Carrier = new ClientPlayerData(new ClientEngine(), Carriers, new NoStandalones(),
                                           new client::TF2.Client.Player.TFClientPlayerResources(),
                                           new ClientEngine(), new ClientClassTable(new ClientLog(), new ClientEngine()),
                                           new client::TF2.Client.Player.TFClientConditionDriver(NoHooks()),
                                           new ClientLog());
            Carrier.State.BindLocal();
            Carrier.State.OwnerIndex = Local;
            Carrier.OnSpawn();
            var convars = PredictionDeps.SpyConVars();
            Prediction = new ClientPrediction(Log, Carriers, ClientNetCatalog.Of(typeof(ClientPlayerData).Assembly, Log),
                                              new NoStandalones(), PredictionDeps.Engine(),
                                              convars.Get<client::TF2.Shared.TFSpyCloakConsumeRateConVar>()!,
                                              convars.Get<client::TF2.Shared.TFSpyCloakRegenRateConVar>()!,
                                              convars.Get<client::TF2.Shared.TFSpyInvisTimeConVar>()!,
                                              convars.Get<client::TF2.Shared.TFSpyInvisUnstealthTimeConVar>()!);
        }

        /// <summary>The server's word arriving: the decoded state changes and counts a decode.</summary>
        public void Arrive(Action<ClientPlayerData> write)
        {
            write(Carrier);
            Carrier.OnDataChanged(default, SourceSharp.Abi.DataUpdateType.DataTableChanged);
        }

        public client::TF2.Shared.Player.TFPlayerNetState Working => Prediction.Predicted(Local)!;

        /// <summary>The rules reading (and writing) the predicted working copy.</summary>
        public ClientRules Rules()
            => new ClientContainer()
                .AddInstance(TFMovementFixtures.Client())
                .AddInstance<ClientData>(Prediction)
                .Create<ClientRules>();
    }

    [Fact]
    public void The_heavys_aiming_condition_lowers_the_predicted_max_speed_and_holds_when_the_server_agrees()
    {
        var frame = new Frame();
        Views.SetClass(frame.Carrier.State, ClientClass.HeavyWeapons);
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        var rules = frame.Rules();

        Assert.Equal(230f, rules.MaxSpeedScale(Local, 320f, false, 0));   // the table speed, no correction yet

        // Spin-up: TF_COND_AIMING arrives and the predicted speed drops to the aiming cap.
        frame.Arrive(c => Views.SetConditions(c.State, ClientCondMask.Of(ClientCond.Aiming)));
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        Assert.Equal(110f, rules.MaxSpeedScale(Local, 230f, false, 0));

        // The command runs and the server's word — the same aiming state — is acknowledged.
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);
        frame.Arrive(c => Views.SetConditions(c.State, ClientCondMask.Of(ClientCond.Aiming)));
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var (saves, holds, tolerated, mispredicts, _, _, _) = frame.Prediction.Counts;
        Assert.Equal((1, 1, 0, 0), (saves, holds, tolerated, mispredicts));
        Assert.Equal(110f, rules.MaxSpeedScale(Local, 230f, false, 0));   // no correction: the client had it
    }

    [Fact]
    public void The_scouts_air_dash_is_predicted_and_acknowledged()
    {
        var frame = new Frame();
        Views.SetClass(frame.Carrier.State, ClientClass.Scout);
        frame.Prediction.ResetBasis(LocalPlayer, 10);
        var rules = frame.Rules();
        var request = new JumpRequest { PlayerIndex = Local, VerticalSpeed = 100f };

        Assert.Equal(ClientJump.DashSpeed, rules.AirJump(in request));
        Assert.Equal(1, frame.Working.AirDash);
        Assert.Equal(0f, rules.AirJump(in request));                      // the one dash is spent

        // The server saw the dash too: the acknowledged state carries it.
        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);
        frame.Arrive(c => c.State.AirDash = 1);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var (saves, holds, tolerated, mispredicts, _, _, _) = frame.Prediction.Counts;
        Assert.Equal((1, 1, 0, 0), (saves, holds, tolerated, mispredicts));
        Assert.Equal(1, frame.Working.AirDash);
    }

    [Fact]
    public void The_ammo_row_the_server_writes_lone_is_the_arrivals_word_not_a_misprediction()
    {
        // The pool is the server's write between commands (the mirror's), no
        // prediction of the client's own: the acknowledged compare takes the
        // arrival's row, and no misprediction is counted for it.
        var frame = new Frame();
        frame.Prediction.ResetBasis(LocalPlayer, 10);

        frame.Prediction.CommandRan(LocalPlayer, 11, firstTime: true);
        frame.Arrive(c => c.State.Ammo[1] = 20);
        frame.Prediction.ResetBasis(LocalPlayer, 11);

        var (saves, holds, tolerated, mispredicts, _, _, _) = frame.Prediction.Counts;
        Assert.Equal((1, 1, 0, 0), (saves, holds, tolerated, mispredicts));
        Assert.Equal(20, frame.Working.Ammo[1]);
    }

    //-------------------------------------------------------------------------
    // D: source parity — both bindings feed the rules the same state fields.
    //-------------------------------------------------------------------------

    private static string Read(params string[] parts) => File.ReadAllText(RepoLayout.Repo(parts));

    [Fact]
    public void Both_bindings_feed_the_movement_rules_the_same_player_data_fields()
    {
        // The rules read and write one source on each side: ITFPlayerData.
        string rules = Read("src", "tf2", "shared", "Movement", "TFMovementRules.cs");
        Assert.Contains("if (_players.Predicted(playerIndex) is not { } state || state.Class() == TFClass.Undefined)", rules, StringComparison.Ordinal);
        Assert.Contains("return TFMaxSpeed.Calculate(_classes, TFMoveInputs.Of(state), CloakedCap,", rules, StringComparison.Ordinal);
        Assert.Contains("if (_players.Predicted(request.PlayerIndex) is not { } state)", rules, StringComparison.Ordinal);
        Assert.Contains("state.AirDash++;", rules, StringComparison.Ordinal);

        // And one set of fields goes into the inputs: class, conditions,
        // disguise, the stun word — read behind stock's gate (9966-9975's
        // index+condition arms, whose loss The_stun_speed_scale_reads_the_-
        // flags_behind_the_gate catches by behaviour) — the carried building
        // and the heal target.
        string maxSpeed = Read("src", "tf2", "shared", "Movement", "TFMaxSpeed.cs");
        Assert.Contains("public static TFMoveInputs Of(TFPlayerNetState state)", maxSpeed, StringComparison.Ordinal);
        Assert.Contains("=> new(state.Class(), state.Conditions(), state.DisguiseAs(),", maxSpeed, StringComparison.Ordinal);
        Assert.Contains("StunFlags: state.StunIndex >= 0 && state.InCond(TFCond.Stunned)", maxSpeed, StringComparison.Ordinal);
        Assert.Contains("(TFStunFlags)state.StunFlags", maxSpeed, StringComparison.Ordinal);
        Assert.Contains("CarryingObject: state.CarryingObject,", maxSpeed, StringComparison.Ordinal);
        Assert.Contains("HealTargetIndex: state.HealTargetIndex);", maxSpeed, StringComparison.Ordinal);

        // Both sides' bindings answer the same two questions about the carrier.
        string carrier = Read("src", "tf2", "shared", "Player", "TFPlayerData.cs");
        Assert.Contains("TFPlayerNetState? Predicted(int playerIndex);", carrier, StringComparison.Ordinal);
        Assert.Contains("TFPlayerNetState? Networked(int playerIndex);", carrier, StringComparison.Ordinal);

        // The server has one state, so both asks answer it; the client predicts
        // its own player and reads the networked states of the rest.
        string server = Read("src", "tf2", "server", "Player", "TFPlayerCarriers.cs");
        Assert.Contains("public TFPlayerNetState? Predicted(int playerIndex) => Get(playerIndex);", server, StringComparison.Ordinal);
        Assert.Contains("public TFPlayerNetState? Networked(int playerIndex) => Get(playerIndex);", server, StringComparison.Ordinal);

        string clientSide = Read("src", "tf2", "client", "Player", "TFPlayerPrediction.cs");
        Assert.Contains("public TFPlayerNetState? Predicted(int playerIndex)", clientSide, StringComparison.Ordinal);
        Assert.Contains("_hasWorking && playerIndex == _localIndex ? _working : null;", clientSide, StringComparison.Ordinal);
        Assert.Contains("public TFPlayerNetState? Networked(int playerIndex) => Find(playerIndex)?.State;", clientSide, StringComparison.Ordinal);
    }
}
