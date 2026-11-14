extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;

using Xunit;

using CAbi = client::SourceSharp.Abi;
using CClient = client::SourceSharp.Client;
using CShared = client::SourceSharp.Shared;
using CTF = client::TF2.Shared;
using CTFData = client::TF2.Shared.Player;
using CTFEcon = client::TF2.Shared.Econ;
using CTFPlayer = client::TF2.Client.Player;

namespace TF2.Tests.Player;

/// <summary>
/// The player's attribute walk on the carrier, for every client (user ruling
/// L11): the server's mirror (<see cref="TFAttributeListMirror"/>) writes it
/// through <see cref="TFAttributeWire"/>, the bytes cross as the snapshot
/// carries them, and the client's owners (<c>TFClientWeaponOwners</c>) read
/// it back into a manager and items that walk as the server's do.
/// </summary>
public sealed class TFAttributeWireFacts
{
    /// <summary>The player another client reads (not the local one, 2).</summary>
    private const int Other = 5;

    private const string RocketLauncher = "tf_weapon_rocketlauncher";
    private const string Shotgun = "tf_weapon_shotgun_soldier";

    /// <summary>
    /// A schema with an additive and a percentage on one class (the order a
    /// walk meets them in decides the answer), a rolled fire rate, a runtime
    /// speed, a launcher whose definition holds a static, and a worn item.
    /// </summary>
    private const string Schema = """
        "items_game"
        {
            "qualities"
            {
            }
            "attributes"
            {
                "6"
                {
                    "name"               "fire rate bonus"
                    "attribute_class"    "mult_postfiredelay"
                    "description_format" "value_is_inverted_percentage"
                    "stored_as_integer"  "0"
                }
                "107"
                {
                    "name"               "move speed bonus"
                    "attribute_class"    "mult_player_movespeed"
                    "description_format" "value_is_percentage"
                    "stored_as_integer"  "0"
                }
                "900"
                {
                    "name"               "sheet add"
                    "attribute_class"    "test_sheet"
                    "description_format" "value_is_additive"
                    "stored_as_integer"  "0"
                }
                "901"
                {
                    "name"               "sheet mult"
                    "attribute_class"    "test_sheet"
                    "description_format" "value_is_percentage"
                    "stored_as_integer"  "0"
                }
            }
            "items"
            {
                "18"
                {
                    "name"         "TF_WEAPON_ROCKETLAUNCHER"
                    "item_class"   "tf_weapon_rocketlauncher"
                    "item_slot"    "primary"
                    "item_quality" "normal"
                    "used_by_classes"
                    {
                        "soldier"  "1"
                    }
                    "attributes"
                    {
                        "fire rate bonus"
                        {
                            "attribute_class" "mult_postfiredelay"
                            "value"           "0.9"
                        }
                    }
                }
                "10"
                {
                    "name"         "TF_WEAPON_SHOTGUN_SOLDIER"
                    "item_class"   "tf_weapon_shotgun_soldier"
                    "item_slot"    "secondary"
                    "item_quality" "normal"
                    "used_by_classes"
                    {
                        "soldier"  "1"
                    }
                }
                "30000"
                {
                    "name"         "Worn"
                    "item_class"   "tf_wearable"
                    "item_slot"    "misc"
                    "item_quality" "normal"
                    "used_by_classes"
                    {
                        "soldier"  "1"
                    }
                    "attributes"
                    {
                        "sheet mult"
                        {
                            "attribute_class" "test_sheet"
                            "value"           "3"
                        }
                    }
                }
            }
        }
        """;

    // =========================================================================
    // The layout.

    [Fact]
    public void TheListsRideThePlayersStateInsideItsTier()
    {
        var layout = TFPlayerNetState.NetLayout;

        // The stimulus: the caps' last slots are on the wire.
        Assert.Contains(layout.Fields, f => f.Name == $"AttributeProviders.{TFAttributeWire.MaxProviders - 1}");
        Assert.Contains(layout.Fields, f => f.Name == $"AttributeBits.{TFAttributeWire.MaxValues - 1}");
        Assert.True(layout.Size <= 2048, $"TFPlayerNetState is {layout.Size} bytes, past the 2048-byte tier");
    }

    // =========================================================================
    // The round trip.

    /// <summary>The server's half: a soldier's carrier, and a manager in the
    /// kit's shape over <see cref="Schema"/>.</summary>
    private sealed class ServerWalk
    {
        public readonly PlayerRig Rig = new PlayerRig().As(TFClass.Soldier);
        public readonly ItemSchema Schema = EconTestFiles.Schema(TFAttributeWireFacts.Schema);
        public readonly RecordingLog Log = new();
        public readonly AttributeContainer Manager = new();
        public readonly AttributeContainer Launcher;
        public readonly AttributeContainer ShotgunItem;
        public readonly AttributeContainer Worn;
        public readonly AttributeContainer Standing = new();
        public readonly TFAttributeListMirror Mirror;

        public ServerWalk(bool values = true)
        {
            Launcher = new AttributeContainer(Schema.Item(18), AttributeProviderType.Weapon);
            ShotgunItem = new AttributeContainer(Schema.Item(10), AttributeProviderType.Weapon);
            Worn = new AttributeContainer(Schema.Item(30000), AttributeProviderType.Weapon);

            if (values)
            {
                // The runtime list (AddCustomAttribute), on the manager itself:
                // the sheet's x2 and a speed.
                Manager.Set(Value(901, 2f));
                Manager.Set(Value(107, 1.1f));
                // An instance's rolled fire rate on each weapon.
                Launcher.Set(Value(6, 0.8f));
                ShotgunItem.Set(Value(6, 0.5f));
                // The worn item's own +5, before its definition's x3.
                Worn.Set(Value(900, 5f));
                // A standing source's +1.
                Standing.Set(Value(900, 1f));
            }

            // The kit's shape (TFLoadoutProvider.Weapons): the launcher
            // provides, the shotgun is held aside (provide_on_active), both
            // answer the manager as their owner; the worn item provides from
            // its spawn, the standing source last.
            Launcher.ProvideTo(Manager);
            Launcher.Owner = Manager;
            ShotgunItem.Owner = Manager;
            Worn.ProvideTo(Manager);
            Standing.ProvideTo(Manager);

            Rig.Items.SetManager(PlayerRig.Index, Manager);
            Rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider>
            {
                [RocketLauncher] = Launcher,
                [Shotgun] = ShotgunItem,
            });
            Rig.State.WeaponItemDefIndex[Rig.Catalog.IndexOf(RocketLauncher)] = 18 + 1;
            Rig.State.WeaponItemDefIndex[Rig.Catalog.IndexOf(Shotgun)] = 10 + 1;

            Mirror = new TFAttributeListMirror(Rig.Carriers, Rig.Items, Rig.Catalog, Log);
            Mirror.Attach(PlayerRig.Player);
        }

        public AttributeValue Value(int defindex, float value) => AttributeValue.OfFloat(Schema.Attribute(defindex)!, value);

        public float Walk(string hook, IAttributeProvider on) => new AttributeHooks().Float(10f, hook, on);
    }

    /// <summary>The client's half: the carrier's bytes as the snapshot carries them, read by the owners.</summary>
    private sealed class ClientRead
    {
        public readonly CTFData.TFPlayerNetState State = new();
        public readonly CTFPlayer.TFClientWeaponOwners Owners;
        public readonly CShared.WeaponCatalog Catalog;

        public ClientRead(TFPlayerNetState server)
        {
            State.BindLocal();
            State.Decode(server.Snapshot());

            var files = new Files(Schema);
            var catalogFiles = new Files(EconTestFiles.Fixture("items_game.trimmed.txt"));
            Catalog = new CShared.WeaponCatalog(
                () => [new CTF.TFWeaponTypes(new CTF.TFWeaponScripts(new CAbi.RecordingLog(), catalogFiles),
                                             new CTF.TFClassTable(new CAbi.RecordingLog(), catalogFiles),
                                             new CTFEcon.ItemSchema(catalogFiles, new CAbi.RecordingLog()))],
                new CAbi.RecordingLog());
            var hud = new CAbi.RecordingEngine();
            hud.Returns("Hud.LocalPlayerIndex", 2);
            Owners = new CTFPlayer.TFClientWeaponOwners(new Data(State), new NoStandalones(),
                                                        new CTFPlayer.TFClientPlayerResources(),
                                                        new CTFEcon.ItemSchema(files, new CAbi.RecordingLog()),
                                                        Catalog, new EmptyHands(), hud);
        }

        public float Walk(string hook, CTFEcon.IAttributeProvider? on) => new CTFEcon.AttributeHooks().Float(10f, hook, on);
    }

    [Fact]
    public void AServersWalkReadsBackOnAnotherClientAlike()
    {
        var server = new ServerWalk();
        server.Mirror.Tick(0f);
        var client = new ClientRead(server.Rig.State);

        var manager = client.Owners.ManagerOf(Other);
        var launcher = client.Owners.ItemOf(Other, RocketLauncher);
        var shotgun = client.Owners.ItemOf(Other, Shotgun);

        // The sheet's class, walked on the manager: its own x2, then the worn
        // item's own +5 and its definition's x3, then the standing +1, in the
        // walk's order: ((10 x 2) + 5) x 3 + 1 = 76. The providers met first
        // and the list last would be ((10 + 5) x 3 + 1) x 2 = 92.
        Assert.Equal(76f, server.Walk("test_sheet", server.Manager), 3);
        Assert.Equal(server.Walk("test_sheet", server.Manager), client.Walk("test_sheet", manager), 3);

        // The runtime speed on the manager.
        Assert.Equal(server.Walk("mult_player_movespeed", server.Manager),
                     client.Walk("mult_player_movespeed", manager), 3);
        Assert.Equal(11f, client.Walk("mult_player_movespeed", manager), 3);

        // Each weapon's rolled fire rate, walked on the item: the launcher's
        // own 0.8 hides its definition's 0.9 of the same attribute
        // (CEconItemView::IterateAttributes); the shotgun held aside keeps its
        // own 0.5 and still answers the manager as its owner.
        Assert.Equal(server.Walk("mult_postfiredelay", server.Launcher), client.Walk("mult_postfiredelay", launcher), 3);
        Assert.Equal(8f, client.Walk("mult_postfiredelay", launcher), 3);
        Assert.Equal(server.Walk("mult_postfiredelay", server.ShotgunItem), client.Walk("mult_postfiredelay", shotgun), 3);
        Assert.Equal(5f, client.Walk("mult_postfiredelay", shotgun), 3);
    }

    [Fact]
    public void AWalkWithNoValuesOfItsOwnLeavesTheListsEmptyAndTheClientAsItWas()
    {
        var server = new ServerWalk(values: false);
        server.Mirror.Tick(0f);

        Assert.Equal(0, server.Rig.State.AttributeProviderCount);
        Assert.Equal(0, server.Rig.State.AttributeSerial);
        Assert.Equal(0, server.Rig.State.AttributeProviders[0]); // nothing written: nothing to send

        // The client builds from the defindex words alone, as before the
        // lists: the item is its definition's, owned by nobody.
        var client = new ClientRead(server.Rig.State);
        var launcher = Assert.IsType<CTFEcon.AttributeContainer>(client.Owners.ItemOf(Other, RocketLauncher));
        Assert.Null(launcher.Owner);
        Assert.Empty(launcher.Attributes.ToArray());
        Assert.Equal(9f, client.Walk("mult_postfiredelay", launcher), 3); // the definition's 0.9 reached it
    }

    // =========================================================================
    // The writes.

    [Fact]
    public void OneRuntimeChangeRewritesTheListsOnce()
    {
        var server = new ServerWalk();
        server.Mirror.Tick(0f);
        int first = server.Rig.State.AttributeSerial;
        server.Mirror.Tick(0.015f);
        Assert.Equal(first, server.Rig.State.AttributeSerial); // nothing moved, nothing written

        server.Manager.Set(server.Value(107, 1.3f));
        server.Mirror.Tick(0.03f);
        server.Mirror.Tick(0.045f);

        Assert.Equal(first + 1, server.Rig.State.AttributeSerial);
        var client = new ClientRead(server.Rig.State);
        Assert.Equal(13f, client.Walk("mult_player_movespeed", client.Owners.ManagerOf(Other)), 3);
    }

    [Fact]
    public void AStandingSourcesChangeReachesTheClient()
    {
        var server = new ServerWalk();
        server.Mirror.Tick(0f);
        var before = new ClientRead(server.Rig.State);
        float was = before.Walk("test_sheet", before.Owners.ManagerOf(Other));

        server.Standing.Set(server.Value(900, 4f)); // the source's own container, no rebuild
        server.Mirror.Tick(0.015f);

        var after = new ClientRead(server.Rig.State);
        Assert.Equal(76f, was, 3);
        Assert.Equal(server.Walk("test_sheet", server.Manager), after.Walk("test_sheet", after.Owners.ManagerOf(Other)), 3);
        Assert.Equal(79f, after.Walk("test_sheet", after.Owners.ManagerOf(Other)), 3);
    }

    [Fact]
    public void AWalkPastTheCapsIsWrittenToTheCapsCountedAndSaid()
    {
        var server = new ServerWalk(values: false);
        var definitions = Enumerable.Range(0, TFAttributeWire.MaxValues + 8)
            .Select(i => new AttributeDefinition(1000 + i, "cap " + i, "cap_" + i, AttributeDescriptionFormat.Additive,
                                                 AttributeEffectType.Neutral, false, false,
                                                 AttributeValueType.Default, MergedKeys.Block("x")))
            .ToArray();
        foreach (var definition in definitions)
            server.Manager.Set(AttributeValue.OfFloat(definition, 1f));

        server.Mirror.Tick(0f);

        var state = server.Rig.State;
        Assert.Equal(1, server.Mirror.Truncations);
        Assert.Contains(server.Log.Lines, line => line.Warning
                                                  && line.Text.Contains("8 value(s) past the wire's", StringComparison.Ordinal));
        // Written up to the cap, in order: the manager's own list first.
        Assert.Equal(TFAttributeWire.MaxValues, state.AttributeProviderValues[0]);
        Assert.Equal(1000 + TFAttributeWire.MaxValues - 1, state.AttributeDefindex[TFAttributeWire.MaxValues - 1]);
    }

    // =========================================================================
    // The client's surroundings.

    private sealed class Files(string schema) : CAbi.IEngineFiles
    {
        private readonly TFClassTableTests.Files _files = Init(schema);

        private static TFClassTableTests.Files Init(string schema)
        {
            var files = WeaponFixtures.ShippedWithClasses();
            files.Bytes[ItemSchema.FilePath] = System.Text.Encoding.UTF8.GetBytes(schema);
            return files;
        }

        public int GameFileSize(string path) => _files.GameFileSize(path);
        public string? ReadGameFile(string path) => _files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => _files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => _files.ReadFileOnPath(path, pathId);
    }

    /// <summary>Everyone's carrier is the one state; nobody is predicted.</summary>
    private sealed class Data(CTFData.TFPlayerNetState state) : CTFData.ITFPlayerData
    {
        public CTFData.TFPlayerNetState? Predicted(int playerIndex) => null;
        public CTFData.TFPlayerNetState? Networked(int playerIndex) => state;
    }

    private sealed class EmptyHands : CClient.ILocalArsenal
    {
        public CShared.WeaponNetState? ActiveWeaponState => null;

        public bool TryGetActive(out string active, out int clip, out int reserve)
        {
            active = string.Empty;
            clip = 0;
            reserve = 0;
            return false;
        }
    }

    private sealed class NoStandalones : CClient.IClientStandalones
    {
        public IReadOnlyList<CClient.Standalone.ClientStandaloneEntity> Live() => [];
        public CClient.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }
}
