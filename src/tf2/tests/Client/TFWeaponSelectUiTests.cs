extern alias client;

using System.Text;

using Xunit;

// The adapter reads the client's own compiled copies, the hud facts' rig
// (TFWeaponSlotForecastTests) with the selection state wrapped around it.
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using Clock = client::SourceSharp.Abi.IGameClock;
using IceKey = client::SourceSharp.Shared.IceKey;
using TFEncryption = client::TF2.Shared.TFEncryption;
using Schema = client::TF2.Shared.Econ.ItemSchema;
using ClassTable = client::TF2.Shared.TFClassTable;
using Scripts = client::TF2.Shared.TFWeaponScripts;
using WeaponTypes = client::TF2.Shared.TFWeaponTypes;
using Catalog = client::SourceSharp.Shared.WeaponCatalog;
using TFClass = client::TF2.Shared.TFClass;
using TFClassNames = client::TF2.Shared.TFClassNames;

using SourceSharp.Tests;
using Views = client::TF2.Shared.Player.TFPlayerNetStateViews;
using ClientCarriers = client::TF2.Client.Player.TFClientCarriers;
using PlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using HudSlots = client::TF2.Client.Weapons.TFWeaponSlots;
using ScriptDb = client::SourceSharp.Shared.WeaponScriptDatabase;
using SelectionHud = client::SourceSharp.Client.WeaponSelectionHud;
using SelectionPlayer = client::SourceSharp.Client.IWeaponSelectionPlayer;
using SelectionActions = client::SourceSharp.Client.IWeaponSelectionActions;
using Localise = client::SourceSharp.Client.Ui.IUiLocalisation;
using LocalPlayer = TF2.Client.Ui.ITFLocalPlayer;
using SelectUi = client::TF2.Client.Ui.TFWeaponSelectUi;

namespace TF2.Tests;

/// <summary>
/// The cards' names: the hud adapter names each slot's weapon as stock's item
/// panel does — the panel is set per item
/// (<c>SetItem(pWeapon-&gt;GetAttributeContainer()-&gt;GetItem())</c>,
/// tf_hud_weaponselection.cpp:560), whose name is the item definition's —
/// the engineer's PDAs reading "Construction"/"Destruction PDA", not the
/// weapon scripts' plainer printnames.
/// </summary>
public sealed class TFWeaponSelectUiTests
{
    private const int LocalPlayer = 3;

    private static (SelectUi Ui, Catalog Weapons) Make(int localPlayer, TFClass cls)
    {
        var log = new ClientLog();
        var bytes = FixtureBytes();
        ClientFiles files = new FilesShim(bytes);
        var classes = new ClassTable(log, files);
        var scripts = new Scripts(log, files);
        var schema = new Schema(files, log);
        var weapons = new Catalog(() => [new WeaponTypes(scripts, classes, schema)], log);

        var carriers = new ClientCarriers();
        var carrier = new PlayerData(new Engine(), carriers, new NoStandalones(),
                                     new client::TF2.Client.Player.TFClientPlayerResources(),
                                     new Engine(), classes,
                                     new client::TF2.Client.Player.TFClientConditionDriver(NoHooks()), log);
        carrier.State.BindLocal();
        carrier.State.OwnerIndex = LocalPlayer;
        carrier.OnSpawn();
        Views.SetClass(carrier.State, cls);

        var screen = new Engine();
        screen.Returns("Hud.LocalPlayerIndex", localPlayer);

        var db = new ScriptDb(log, null, p => bytes.TryGetValue(p, out var b) ? b : null, TFEncryption.Key);

        var slots = new HudSlots(weapons, db, carriers, screen, classes);

        // The player carries his loadout: one row per hud slot, the forecast's
        // own walk (Primary..Pda2) — the mask the client's weapon entities
        // would carry.
        var forecast = new client::TF2.Client.Weapons.TFWeaponSlotForecast(schema, classes, carriers, weapons, screen, slots);
        ulong carried = 0;
        for (int slot = 0; slot < 6; slot++)
        {
            int type = forecast.TypeAt(slot);
            if (type >= 0)
                carried |= 1ul << type;
        }

        var hud = new SelectionHud(new HeldPlayer(carried), new SilentActions(), slots, new FixedClock(), () => 0);
        var types = new WeaponTypes(scripts, classes, schema);
        return (new SelectUi(hud, weapons, scripts, NewLocalise(), new BareLocal(cls), types), weapons);
    }

    private static Localise NewLocalise() => new RawNames();

    [Fact]
    public void The_engineers_pda_cards_name_the_items()
    {
        var (ui, weapons) = Make(LocalPlayer, TFClass.Engineer);

        // The build and destroy rows' items: #TF_Weapon_PDA_Engineer_Builder
        // and ..._Destroyer, where the scripts say ..._Build/..._Destroy.
        Assert.Equal("#TF_Weapon_PDA_Engineer_Builder", ui.WeaponInSlot(3).Name);
        Assert.Equal("#TF_Weapon_PDA_Engineer_Destroyer", ui.WeaponInSlot(4).Name);
    }

    [Fact]
    public void The_shotgun_card_keeps_the_guns_name()
    {
        var (ui, _) = Make(LocalPlayer, TFClass.Engineer);

        // The item and the script agree on the guns; the item's word wins
        // either way.
        Assert.Equal("#TF_Weapon_Shotgun", ui.WeaponInSlot(0).Name);
    }

    [Fact]
    public void The_spys_sapper_card_names_the_sappers_item()
    {
        var (ui, _) = Make(LocalPlayer, TFClass.Spy);

        // The builder family's retyped shell (SetSubType) wears the sapper
        // item, whose name is the panel's — not the builder script's plain
        // #TF_Weapon_Builder.
        Assert.Equal("#TF_Weapon_Spy_Sapper", ui.WeaponInSlot(1).Name);
    }

    /// <summary>The loadout carried: the mask the hud's slot reads walk.</summary>
    private sealed class HeldPlayer(ulong carried) : SelectionPlayer
    {
        public bool Exists => true;

        public bool HudAllowed => true;

        public bool FrozenOrDead => false;

        public ulong Carried => carried;

        public ulong Empty => 0;

        public int Active => -1;
    }

    /// <summary>No world action: the facts read cards, none switch.</summary>
    private sealed class SilentActions : SelectionActions
    {
        public void Select(int weaponType) { }

        public void LastWeapon() { }

        public void Sound(string soundName) { }
    }

    /// <summary>Still time: the buckets never age under a read.</summary>
    private sealed class FixedClock : Clock
    {
        public float Now => 0f;
    }

    /// <summary>No tables: the raw token passes, the fallback the fact asserts.</summary>
    private sealed class RawNames : Localise
    {
        public string? Find(string token) => null;
    }

    /// <summary>A RED soldier of no health: only the team colours the card.</summary>
    private sealed class BareLocal(TFClass cls) : LocalPlayer
    {
        private static string NameOf(TFClass cls) => TFClassNames.NonLocalized(cls);

        public string ClassName => NameOf(cls);

        public int Health => 100;

        public int MaxHealth => 100;

        public int MaxBuffedHealth => 100;

        public int Overheal => 0;

        public bool ShowsAmmo => false;

        public int Clip => -1;

        public int Team => 2;

        public int Reserve => 0;

        public int MaxTotalAmmo => 0;

        public int Metal => 0;
    }

    // --- the forecast rig's doubles, verbatim ---

    private static Dictionary<string, byte[]> FixtureBytes()
    {
        string[] weapons = Directory.GetFiles(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons"), "tf_weapon_*.txt")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var classes = new TFClass[]
        {
            TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman, TFClass.Medic,
            TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer, TFClass.Civilian,
        };

        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in weapons)
            bytes[Scripts.ScriptName(name) + ".ctx"] = Seal(Encoding.UTF8.GetBytes(
                File.ReadAllText(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons", name + ".txt"))));
        foreach (var c in classes)
            bytes[ClassTable.ScriptName(c) + ".ctx"] = Seal(Encoding.UTF8.GetBytes(
                TFClassDataTests.Fixture(TFClassNames.Raw(c))));
        bytes[Schema.FilePath] = Encoding.UTF8.GetBytes(
            EconTestFiles.Fixture("items_game.trimmed.txt"));

        return bytes;
    }

    private static byte[] Seal(byte[] plain)
    {
        var ice = new IceKey(0);
        ice.Set(Encoding.ASCII.GetBytes(TFEncryption.Key));
        byte[] sealedBytes = plain.ToArray();
        for (int at = 0; at + 8 <= sealedBytes.Length; at += 8)
            ice.Encrypt(plain.AsSpan(at, 8), sealedBytes.AsSpan(at, 8));
        return sealedBytes;
    }

    private static client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook> NoHooks() =>
        new NoConditionHooks();

    private sealed class NoConditionHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public System.Collections.Generic.IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }

    private sealed class NoStandalones : client::SourceSharp.Client.IClientStandalones
    {
        public System.Collections.Generic.IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class FilesShim(Dictionary<string, byte[]> bytes) : ClientFiles
    {
        public int GameFileSize(string path) => bytes.TryGetValue(path, out var b) ? b.Length : -1;

        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path) => bytes.TryGetValue(path, out var b) ? b : null;

        public byte[]? ReadFileOnPath(string path, string pathId) =>
            string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;
    }
}
