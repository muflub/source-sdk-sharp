extern alias client;

using System.Text;

using Xunit;

using TF2.Tests;

// Everything the forecast reads rides the client's own compiled copy of the
// shared source, so the rig builds it there too.
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
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
using Forecast = client::TF2.Client.Weapons.TFWeaponSlotForecast;

namespace TF2.Tests;

/// <summary>
/// The slot binds' forecast: the loadout row a slot names, as the catalog
/// index the wire's weaponselect carries — the shotgun, pistol and wrench an
/// engineer equips, and nothing for a cosmetic slot or a nameless player.
/// </summary>
public sealed class TFWeaponSlotForecastTests
{
    private const int LocalPlayer = 3;

    private static readonly string[] WeaponFixtures =
        Directory.GetFiles(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons"), "tf_weapon_*.txt")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static readonly TFClass[] Scripted =
    [
        TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman, TFClass.Medic,
        TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer, TFClass.Civilian,
    ];

    private static (Forecast Forecast, Catalog Weapons) Make(int localPlayer, TFClass cls)
    {
        var log = new ClientLog();
        var files = ShippedFiles();
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

        return (new Forecast(schema, classes, carriers, weapons, screen), weapons);
    }

    /// <summary>The shipped scripts, class scripts and trimmed schema, sealed as the pack holds them.</summary>
    private static ClientFiles ShippedFiles()
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in WeaponFixtures)
            bytes[Scripts.ScriptName(name) + ".ctx"] = Seal(Encoding.UTF8.GetBytes(
                File.ReadAllText(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons", name + ".txt"))));
        foreach (var c in Scripted)
            bytes[ClassTable.ScriptName(c) + ".ctx"] = Seal(Encoding.UTF8.GetBytes(
                TFClassDataTests.Fixture(TFClassNames.Raw(c))));
        bytes[Schema.FilePath] = Encoding.UTF8.GetBytes(
            EconTestFiles.Fixture("items_game.trimmed.txt"));

        return new FilesShim(bytes);
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

    /// <summary>No condition arms: the driver's chain is empty.</summary>
    private static client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook> NoHooks() =>
        new NoConditionHooks();

    private sealed class NoConditionHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }

    /// <summary>No client entities: the carrier's skin decision finds no body.</summary>
    private sealed class NoStandalones : client::SourceSharp.Client.IClientStandalones
    {
        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];
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

    [Fact]
    public void The_engineers_slots_name_the_shotgun_the_pistol_and_the_wrench()
    {
        var (forecast, weapons) = Make(LocalPlayer, TFClass.Engineer);

        Assert.Equal(weapons.IndexOf("tf_weapon_shotgun_primary"), forecast.TypeAt(0));
        Assert.Equal(weapons.IndexOf("tf_weapon_pistol"), forecast.TypeAt(1));
        Assert.Equal(weapons.IndexOf("tf_weapon_wrench"), forecast.TypeAt(2));
    }

    [Fact]
    public void The_fourth_and_fifth_keys_name_the_class_pdas()
    {
        // Stock's g_SlotsToLoadoutSlotsPerClass (item_quickswitch.cpp:38):
        // engineer's keys 4 and 5 name the PDA and PDA2 rows, spy's key 4 the
        // disguise kit — not the (empty) utility row the digit index alone
        // answers.
        var (engineer, weapons) = Make(LocalPlayer, TFClass.Engineer);
        int build = weapons.IndexOf("tf_weapon_pda_engineer_build");
        int destroy = weapons.IndexOf("tf_weapon_pda_engineer_destroy");
        Assert.True(build >= 0 && destroy >= 0, "the trimmed schema carries the two engineer PDAs");
        Assert.Equal(build, engineer.TypeAt(3));
        Assert.Equal(destroy, engineer.TypeAt(4));

        var (spy, spyWeapons) = Make(LocalPlayer, TFClass.Spy);
        int kit = spyWeapons.IndexOf("tf_weapon_pda_spy");
        Assert.True(kit >= 0, "the trimmed schema carries the spy disguise kit");
        Assert.Equal(kit, spy.TypeAt(3));
    }

    [Fact]
    public void A_slot_that_equips_no_weapon_forecasts_nothing()
    {
        var (forecast, _) = Make(LocalPlayer, TFClass.Engineer);

        // Head and beyond are cosmetics; stock's GetBaseItemForClass refuses
        // them outright.
        Assert.Equal(-1, forecast.TypeAt(7));
        Assert.Equal(-1, forecast.TypeAt(9));
    }

    [Fact]
    public void A_nameless_player_forecasts_nothing()
    {
        var (forecast, _) = Make(-1, TFClass.Engineer);

        Assert.Equal(-1, forecast.TypeAt(0));
    }
}
