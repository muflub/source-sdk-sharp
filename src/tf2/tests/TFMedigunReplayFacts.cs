extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using Xunit;

using ClientArsenal = client::SourceSharp.Client.ClientArsenal;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using ClientViewModel = client::SourceSharp.Client.Standalone.ClientSsViewModel;
using ClientPlayer = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using ClientDirectory = client::SourceSharp.Client.Standalone.ClientEntityDirectory;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientStandalone = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using ClientAmmo = client::SourceSharp.Shared.AmmoCatalog;
using ClientData = client::TF2.Shared.Player.ITFPlayerData;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientParts = client::TF2.Shared.TFWeaponParts;
using ClientPlayerState = client::TF2.Shared.Player.TFPlayerNetState;
using ClientPrediction = client::TF2.Client.Weapons.TFClientMedigunPrediction;
using ClientResources = client::TF2.Client.Player.TFClientPlayerResources;
using ClientResource = client::TF2.Client.Player.ClientTFPlayerResource;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientCatalog = client::SourceSharp.Shared.WeaponCatalog;
using ClientScriptTypes = client::TF2.Shared.TFWeaponTypes;
using ClientClassTable = client::TF2.Shared.TFClassTable;
using ClientItemSchema = client::TF2.Shared.Econ.ItemSchema;
using ClientChain = client::TF2.Shared.TFWeaponHookChain;
using ClientHooks = client::TF2.Shared.Econ.AttributeHooks;
using ClientBuckets = client::TF2.Shared.TFCritBuckets;
using ClientBehaviours = client::SourceSharp.Shared.WeaponBehaviours;
using ClientNetCatalog = client::SourceSharp.Abi.NetStateCatalog;
using ClientWeaponBehaviours = client::TF2.Shared.TFWeaponBehaviours;
using ClientCrits = client::TF2.Shared.ITFCritSettings;
using ClientCritSettings = client::TF2.Shared.TFCritSettings;

using TF2.Tests.Weapons;

namespace TF2.Tests;

/// <summary>
/// The medigun's replayed half: the lock the server's arm decides
/// (TFMedigunWeapon's search, tf_weapon_medigun.cpp:1237) is a pick the
/// client's replay of the same command runs itself, so the beam's first
/// predicted frame carries the patient and the clocks — not the stale
/// HealTargetIndex a replay without the pick stands judged by.
/// </summary>
public sealed class TFMedigunReplayFacts
{
    private const int Local = 1;
    private const int Target = 4;
    private const string Medigun = "tf_weapon_medigun";
    private static readonly EntityHandle LocalPlayer = new(0x0001_0000u | Local);
    private static readonly EntityHandle WeaponHandle = new(0x0100_0041u);
    private static readonly EntityHandle ViewModelHandle = new(0x0100_0042u);
    private static readonly EntityHandle TargetHandle = new(0x0001_0000u | Target);
    private const float BasisClock = 9f;
    private const float LockClock = 10f;
    private static readonly Vector Eye = new(64f, 128f, 64f);
    private static readonly Vector AimHit = new(300f, 128f, 64f);

    [Fact]
    public void A_replayed_lock_takes_the_aimed_target_and_the_clocks_the_servers_arm_publishes()
    {
        var rig = new Rig();

        // The frame's basis reset (phase 2), then the replayed commands.
        rig.Arsenal.OnPlayerCommand(LocalPlayer, Command(10, attack: false), 2, false, BasisClock, Eye);
        Assert.Equal(-1, rig.Carrier.HealTargetIndex);

        // The lock: attack held, the aim line stopping on entity 4 — the
        // patient the server's arm of the same command names.
        rig.Arsenal.OnPlayerCommand(LocalPlayer, Command(11, attack: true), 1, true, LockClock, Eye);

        // The carrier's word, as the server's Publish writes it, and the
        // weapon's: the check clock the next command's gate reads, and the
        // pressed edge the m_bCanChangeTarget image (tf_weapon_medigun.cpp:1555).
        Assert.Equal(Target, rig.Carrier.HealTargetIndex);

        var predicted = rig.Arsenal.PredictedStateOf(rig.Weapon);
        Assert.NotNull(predicted);
        Assert.Equal(LockClock + 1f, predicted!.BehaviourTimeC, 3);
        Assert.Equal(client::TF2.Shared.ITFMedigunPrediction.Attack1Down, predicted.BehaviourMode);
    }

    private static UserCmd Command(int number, bool attack)
        => new() { CommandNumber = number, Buttons = attack ? (int)InputButtons.Attack : 0 };

    /// <summary>The TF states the replay's half reads: the local carrier's working copy and the target's word.</summary>
    private sealed class FakeCarrierData : ClientData
    {
        public Dictionary<int, ClientPlayerState> ByIndex = new();

        public ClientPlayerState? Predicted(int playerIndex) => ByIndex.GetValueOrDefault(playerIndex);

        public ClientPlayerState? Networked(int playerIndex) => ByIndex.GetValueOrDefault(playerIndex);
    }

    /// <summary>The arsenal's pair: the gun, its viewmodel and the aimed-at player.</summary>
    private sealed class Pair : ClientStandalones
    {
        public readonly ClientDirectory Directory = new();

        public IReadOnlyList<ClientStandalone> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandalone? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);
        public IEnumerable<ClientStandalone> OfClass(string? className) => Directory.OfClass(className);
        public IEnumerable<ClientStandalone> OfName(string? name) => Directory.OfName(name);
    }

    /// <summary>The replay's side: the medigun armed, its parts carrying the prediction.</summary>
    private sealed class Rig
    {
        public readonly ClientLog Log = new();
        public readonly ClientEngine Engine = new();
        public readonly ClientPlayerState Carrier = new();
        public readonly ClientPlayerState TargetState = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientArsenal Arsenal;

        public Rig()
        {
            Carrier.BindLocal();
            Carrier.HealTargetIndex = -1;    // the server's published none, as the decode leaves it
            TargetState.BindLocal();

            var files = new Served(WeaponFixtures.ShippedWithClasses());
            var log = new ClientLog();
            var scripts = new ClientScripts(log, files);
            var catalog = new ClientCatalog(() => [new ClientScriptTypes(scripts, new ClientClassTable(log, files), new ClientItemSchema(files, log))], log);
            var ammo = new ClientAmmo(() => [new client::TF2.Shared.TFAmmoTypes()], log);

            // The world's answer: every aim line stops on the target entity.
            Engine.Returns("WorldQuery.ClientTraceLine", Hit);
            Engine.Returns("WorldQuery.ClientClipRayToEntities", Hit);
            Engine.Returns("EntityRender.LookupSequenceByActivity", 0);   // any fire anim resolves

            Weapon = new ClientWeapon(Engine) { Handle = WeaponHandle };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = Local;
            Weapon.State.WeaponTypeId = catalog.IndexOf(Medigun);

            var viewModel = new ClientViewModel(Engine, Engine) { Handle = ViewModelHandle };
            viewModel.State.BindLocal();
            viewModel.State.OwnerIndex = Local;

            var targetPlayer = new ClientPlayer(Engine) { Handle = TargetHandle };
            targetPlayer.State.BindLocal();
            targetPlayer.State.Alive = true;
            targetPlayer.State.Origin = AimHit with { Z = 0 };

            var pair = new Pair();
            // Bound at their entries, as the client's registry binds: the pick's
            // ByIndex(4) is the by-index table, not the handle's entry.
            pair.Directory.Bind(Weapon.Handle, WeaponHandle.EntryIndex, Weapon);
            pair.Directory.Bind(viewModel.Handle, ViewModelHandle.EntryIndex, viewModel);
            pair.Directory.Bind(targetPlayer.Handle, Target, targetPlayer);

            var data = new FakeCarrierData { ByIndex = { [Local] = Carrier, [Target] = TargetState } };
            var resources = new ClientResources();
            var resource = new ClientResource(Engine, resources);
            resource.State.BindLocal();
            resource.State.Team[Local] = 2;
            resource.State.Team[Target] = 2;
            resources.Live = resource;

            var prediction = new ClientPrediction(Engine, Engine, pair, data, resources);
            var parts = new ClientParts(scripts, ClientChain.Of(
                                            [new client::TF2.Shared.TFAttributeWeaponHook(new ClientHooks())]),
                                        new ClientBuckets(),
                                        new NoRandomCrits(), playerData: data, medigunPrediction: prediction);
            var behaviours = new ClientBehaviours(() => [new ClientWeaponBehaviours(parts)], log);

            Arsenal = new ClientArsenal(log, pair, Engine, Engine, Engine,
                                        ClientNetCatalog.Of(typeof(ClientArsenal).Assembly, log),
                                        catalog, ammo, behaviours);
        }

        private static ClientTraceResult Hit =>
            new(true, 0.5f, AimHit, Vector.Zero, 0, Target, 0, 0);
    }

    private sealed class NoRandomCrits : ClientCrits
    {
        public ClientCritSettings Current { get; } = new(RandomCrits: false);
    }

    /// <summary>The shipped pack, served as the client's files.</summary>
    private sealed class Served(TFClassTableTests.Files files) : ClientFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }
}
