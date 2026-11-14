extern alias client;

using System.Collections.Generic;

using SourceSharp.Abi;

using TF2.Tests.Weapons;

using Xunit;

using ClientArsenal = client::SourceSharp.Client.ClientArsenal;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using ClientViewModel = client::SourceSharp.Client.Standalone.ClientSsViewModel;
using ClientDirectory = client::SourceSharp.Client.Standalone.ClientEntityDirectory;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientStandalone = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using ClientAmmo = client::SourceSharp.Shared.AmmoCatalog;
using ClientCatalog = client::SourceSharp.Shared.WeaponCatalog;
using ClientBehaviours = client::SourceSharp.Shared.WeaponBehaviours;
using ClientChain = client::TF2.Shared.TFWeaponHookChain;
using ClientHooks = client::TF2.Shared.Econ.AttributeHooks;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientNetCatalog = client::SourceSharp.Abi.NetStateCatalog;
using ClientParts = client::TF2.Shared.TFWeaponParts;
using ClientBuckets = client::TF2.Shared.TFCritBuckets;
using ClientCrits = client::TF2.Shared.ITFCritSettings;
using ClientCritSettings = client::TF2.Shared.TFCritSettings;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientScriptTypes = client::TF2.Shared.TFWeaponTypes;
using ClientClassTable = client::TF2.Shared.TFClassTable;
using ClientItemSchema = client::TF2.Shared.Econ.ItemSchema;
using ClientKnifeHook = client::TF2.Client.Weapons.TFKnifeClientHook;
using ClientPlayerData = client::TF2.Shared.Player.ITFPlayerData;
using ClientPlayerState = client::TF2.Shared.Player.TFPlayerNetState;
using ClientWeaponBehaviours = client::TF2.Shared.TFWeaponBehaviours;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;

namespace TF2.Tests.Client;

/// <summary>
/// The melted knife at the client's replay of the switch: the mirror's
/// <c>CanDeploy</c> ask (stock's <c>Weapon_CanSwitchTo</c> through
/// <c>Weapon_Switch</c>, basecombatcharacter_shared.cpp:88) recomputing
    /// stock's line (<c>tf_weapon_knife.cpp:523</c>) from the carrier's
/// published pair — the replay refusing inside the window and taking the
/// knife past it, the server's own answer on the client's clock.
/// </summary>
public sealed class TFKnifeMirrorFacts
{
    private const int Local = 1;
    private const string Knife = "tf_weapon_knife";
    private const string Revolver = "tf_weapon_revolver";
    private static readonly EntityHandle WeaponHandle = new(0x0100_0041u);
    private static readonly EntityHandle ViewModelHandle = new(0x0100_0042u);

    [Fact]
    public void TheReplayRefusesTheMeltedKnifeInsideTheWindowAndTakesItPast()
    {
        var rig = new MirrorRig();
        rig.Carrier.KnifeMeltTimestamp = 100f;
        rig.Carrier.KnifeRegenerateDuration = 10f;

        // The frame's basis, then the switch inside the window: the replay
        // refuses with the server, the revolver the hands.
        rig.Arsenal.OnPlayerCommand(rig.LocalPlayer, new UserCmd { CommandNumber = 10 }, 2, false, 99f);
        rig.Arsenal.OnPlayerCommand(rig.LocalPlayer,
            new UserCmd { CommandNumber = 11, WeaponSelect = rig.KnifeRow + 1 }, 1, true, commandTime: 105f);

        Assert.Equal(rig.RevolverRow, rig.Arsenal.WorkingWeapon.WeaponTypeId);

        // Past it (:523): the same ask walks, and the knife is the hands.
        rig.Arsenal.OnPlayerCommand(rig.LocalPlayer,
            new UserCmd { CommandNumber = 12, WeaponSelect = rig.KnifeRow + 1 }, 1, true, commandTime: 110.5f);

        Assert.Equal(rig.KnifeRow, rig.Arsenal.WorkingWeapon.WeaponTypeId);
    }

    /// <summary>The replay's side: the revolver out, the knife an ask away, the
    /// real TF behaviour chain over a knife hook reading the carrier.</summary>
    private sealed class MirrorRig
    {
        public readonly ClientLog Log = new();
        public readonly ClientEngine Engine = new();
        public readonly ClientPlayerState Carrier = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientArsenal Arsenal;
        public readonly EntityHandle LocalPlayer = new(0x0001_0000u | Local);
        public readonly int KnifeRow;
        public readonly int RevolverRow;

        public MirrorRig()
        {
            Carrier.BindLocal();  // the replay's own buffer, as the prediction's working copy stands
            var files = new Served(WeaponFixtures.ShippedWithClasses());
            var scripts = new ClientScripts(Log, files);
            var schema = new ClientItemSchema(files, Log);
            var catalog = new ClientCatalog(() => [new ClientScriptTypes(scripts, new ClientClassTable(Log, files),
                                                                      schema)], Log);
            var ammo = new ClientAmmo(() => [new client::TF2.Shared.TFAmmoTypes()], Log);
            KnifeRow = catalog.IndexOf(Knife);
            RevolverRow = catalog.IndexOf(Revolver);

            Engine.Returns("Hud.LocalPlayerIndex", Local);
            Engine.Returns("EntityRender.LookupSequenceByActivity", 0);

            Weapon = new ClientWeapon(Engine) { Handle = WeaponHandle };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = Local;
            Weapon.State.WeaponTypeId = RevolverRow;
            Weapon.State.DeployEpoch = 1;

            var viewModel = new ClientViewModel(Engine, Engine) { Handle = ViewModelHandle };
            viewModel.State.BindLocal();
            viewModel.State.OwnerIndex = Local;

            var pair = new Pair();
            pair.Directory.Bind(Weapon.Handle, -1, Weapon);
            pair.Directory.Bind(viewModel.Handle, -1, viewModel);

            var data = new ReplayedCarrier { [Local] = Carrier };
            var parts = new ClientParts(scripts,
                                        ClientChain.Of(
                                            [new client::TF2.Shared.TFAttributeWeaponHook(new ClientHooks()),
                                             new ClientKnifeHook(data)]),
                                        new ClientBuckets(), new NoRandomCrits(),
                                        new client::TF2.Client.Player.TFClientWeaponOwners(data, pair,
                                                                                           new client::TF2.Client.Player.TFClientPlayerResources(),
                                                                                           schema, catalog, new EmptyArsenal(), Engine),
                                        NoEffects,
                                        new client::TF2.Client.Buildings.TFClientBuilderPlacement(),
                                        data,
                                        Setup, BoundCharge(), BoundSpread(), Reload, BoundBurstammo(),
                                        new SilentMedigun(),
                                        new client::TF2.Client.Buildings.TFClientSapperState(),
                                        new client::TF2.Client.Buildings.TFClientSentryWrangle(),
                                        new client::TF2.Client.Player.TFClientRevengeCrits());

            Arsenal = new ClientArsenal(Log, pair, Engine, Engine, Engine,
                                        ClientNetCatalog.Of(typeof(ClientArsenal).Assembly, Log),
                                        catalog, ammo,
                                        new ClientBehaviours(() => [new ClientWeaponBehaviours(parts)], Log));
        }

        /// <summary>The shipped pack, served as the client's files.</summary>
        private sealed class Served(TFClassTableTests.Files files) : ClientFiles
        {
            public int GameFileSize(string path) => files.GameFileSize(path);
            public string? ReadGameFile(string path) => files.ReadGameFile(path);
            public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
            public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
        }

        /// <summary>No hands: the mirror's owners ask nothing of the local arsenal.</summary>
        private sealed class EmptyArsenal : client::SourceSharp.Client.ILocalArsenal
        {
            public bool TryGetActive(out string name, out int clip, out int reserve)
            {
                name = "";
                clip = 0;
                reserve = 0;
                return false;
            }
        }
    }

    /// <summary>The replay's carrier words, by player index: the local player's
    /// decoded state, the pair the server's melt published.</summary>
    private sealed class ReplayedCarrier : Dictionary<int, ClientPlayerState>, ClientPlayerData
    {
        public ClientPlayerState? Predicted(int playerIndex) => TryGetValue(playerIndex, out var state) ? state : null;

        public ClientPlayerState? Networked(int playerIndex) => TryGetValue(playerIndex, out var state) ? state : null;
    }

    private sealed class NoRandomCrits : ClientCrits
    {
        public ClientCritSettings Current { get; } = new(RandomCrits: false);
    }

    // The seams the knife's mirror never touches, as the client's own silent
    // answers hold them, and the declared defaults beside them.
    private static client::TF2.Shared.ITFWeaponEffects NoEffects => new client::TF2.Shared.NoTFWeaponEffects();

    private sealed class NoSetup : client::TF2.Shared.Rules.ITFRoundSetup
    {
        public bool InSetupWithRoundTimer => false;
    }

    private static readonly client::TF2.Shared.Rules.ITFRoundSetup Setup = new NoSetup();

    private sealed class NoReload : client::TF2.Shared.ITFAutoReload
    {
        public bool Wants(int ownerIndex) => false;
    }

    private static readonly client::TF2.Shared.ITFAutoReload Reload = new NoReload();

    private static client::TF2.Shared.WeaponMedigunChargeRateConVar BoundCharge()
    {
        var convar = new client::TF2.Shared.WeaponMedigunChargeRateConVar();
        convar.Bind(-1, "weapon_medigun_charge_rate", "40");
        return convar;
    }

    private static client::TF2.Shared.TFUseFixedWeaponspreadsConVar BoundSpread()
    {
        var convar = new client::TF2.Shared.TFUseFixedWeaponspreadsConVar();
        convar.Bind(-1, "tf_use_fixed_weaponspreads", "0");
        return convar;
    }

    private static client::TF2.Shared.TFFlameThrowerBurstammoConVar BoundBurstammo()
    {
        var convar = new client::TF2.Shared.TFFlameThrowerBurstammoConVar();
        convar.Bind(-1, "tf_flamethrower_burstammo", "20");
        return convar;
    }

    private sealed class SilentMedigun : client::TF2.Shared.ITFMedigunPrediction
    {
        public void Frame(in client::SourceSharp.Shared.WeaponAttackContext attack,
                          client::TF2.Shared.TFWeaponScript script, bool held) { }
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
}
