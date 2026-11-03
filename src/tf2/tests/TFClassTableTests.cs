extern alias client;

using System.Reflection;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server;
using TF2.Shared;

using Xunit;

using ClientLoader = client::TF2.Client.TFClassTableLoader;
using ClientTable = client::TF2.Shared.TFClassTable;
using ClientEngineFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientOrderedHook = client::SourceSharp.Abi.IOrderedHook;
using ClientSideServiceAttribute = client::SourceSharp.Abi.SideServiceAttribute;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFClassTable"/>: <c>CTFPlayerClassDataMgr</c>. The class
/// scripts are served from the decrypted fixtures, sealed again as the
/// shipped <c>.ctx</c> files are (ICE level 0 under <see cref="TFEncryption.Key"/>),
/// so the table's path from the GAME read to the parse is the one the game takes.
/// </summary>
public sealed class TFClassTableTests
{
    private static readonly TFClass[] Scripted =
    [
        TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman, TFClass.Medic,
        TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer, TFClass.Civilian,
    ];

    internal sealed class Files : IEngineFiles
    {
        public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = new();

        public int GameFileSize(string path) => Bytes.TryGetValue(path, out var b) ? b.Length : -1;
        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path)
        {
            Asked.Add(path);
            return Bytes.TryGetValue(path, out var b) ? b : null;
        }

        public byte[]? ReadFileOnPath(string path, string pathId) =>
            string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;

        /// <summary>Every class script as the shipped VPK holds it: .ctx only.</summary>
        public static Files Shipped()
        {
            var files = new Files();
            foreach (var c in Scripted)
                files.Bytes[TFClassTable.ScriptName(c) + ".ctx"] = Seal(Encoding.UTF8.GetBytes(TFClassDataTests.Fixture(TFClassNames.Raw(c))));
            return files;
        }
    }

    /// <summary>The client's copy of the file seam (the shared interface compiles into each side), over the same bytes.</summary>
    private sealed class ClientFiles(Files files) : ClientEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
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

    [Fact]
    public void Every_class_has_its_data_after_a_load_of_the_sealed_scripts()
    {
        var files = Files.Shipped();
        var log = new RecordingLog();
        var table = new TFClassTable(log, files);

        // The stimulus is sealed: the plain text is not in what the table reads.
        Assert.DoesNotContain("speed_max", Encoding.UTF8.GetString(files.Bytes["scripts/playerclasses/scout.ctx"]));

        Assert.Equal(10, table.Load());

        foreach (var c in Scripted)
        {
            var data = table[c];
            Assert.True(data.IsParsed, c + " did not parse");
            Assert.Equal(c, data.Class);
            Assert.Equal(TFClassNames.Raw(c), data.Name);
            Assert.True(data.MaxSpeed > 0, c + " has no speed");
            Assert.True(data.MaxHealth > 0, c + " has no health");
        }
        Assert.Empty(log.Warnings());
        Assert.Equal(400f, table[TFClass.Scout].MaxSpeed);
        Assert.Equal(300, table[TFClass.HeavyWeapons].MaxHealth);
        Assert.Equal(TFWeaponId.Minigun, table[TFClass.HeavyWeapons].Weapons[2]);
        Assert.Equal(200, table[TFClass.Engineer].MaxAmmo(TFAmmo.Metal));
    }

    [Fact]
    public void The_scripts_are_the_raw_class_names_txt_first_then_ctx()
    {
        var files = new Files();
        new TFClassTable(new RecordingLog(), files).Load();

        Assert.Equal(20, files.Asked.Count);
        Assert.Equal("scripts/playerclasses/scout.txt", files.Asked[0]);
        Assert.Equal("scripts/playerclasses/scout.ctx", files.Asked[1]);
        Assert.Contains("scripts/playerclasses/heavyweapons.ctx", files.Asked);
        Assert.Contains("scripts/playerclasses/demoman.ctx", files.Asked);
        Assert.DoesNotContain(files.Asked, p => p.Contains("undefined", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_loaded_class_is_logged_with_its_speed_health_and_ammo()
    {
        var log = new RecordingLog();
        new TFClassTable(log, Files.Shipped()).Load();

        Assert.Contains("[TF2/Classes] scout: speed_max 400 health_max 125 ammo 32/36/100/1/1/0\n", log.Messages());
        Assert.Contains("[TF2/Classes] 10 of 10 class scripts loaded\n", log.Messages());
        Assert.Equal(11, log.Messages().Count);
    }

    [Fact]
    public void A_loose_txt_overrides_the_ctx()
    {
        var files = Files.Shipped();
        files.Bytes["scripts/playerclasses/scout.txt"] = Encoding.UTF8.GetBytes("PlayerClass { \"speed_max\" \"999\" \"health_max\" \"1\" }");

        var table = new TFClassTable(new RecordingLog(), files);

        Assert.Equal(999f, table[TFClass.Scout].MaxSpeed);
        Assert.Equal(240f, table[TFClass.Soldier].MaxSpeed);
    }

    [Fact]
    public void A_missing_script_is_logged_and_leaves_that_class_missing()
    {
        var files = Files.Shipped();
        Assert.True(files.Bytes.Remove("scripts/playerclasses/spy.ctx"));
        var log = new RecordingLog();
        var table = new TFClassTable(log, files);

        Assert.Equal(9, table.Load());

        Assert.Equal(TFClassData.Missing(TFClass.Spy) with { Weapons = table[TFClass.Spy].Weapons, Grenades = table[TFClass.Spy].Grenades,
                                                               AmmoMax = table[TFClass.Spy].AmmoMax, Buildables = table[TFClass.Spy].Buildables,
                                                               DeathSounds = table[TFClass.Spy].DeathSounds },
                     table[TFClass.Spy]);
        Assert.False(table[TFClass.Spy].IsParsed);
        Assert.All(table[TFClass.Spy].Weapons, w => Assert.Equal(TFWeaponId.None, w));
        Assert.True(table[TFClass.Medic].IsParsed);
        string warning = Assert.Single(log.Warnings());
        Assert.Contains("spy", warning);
        Assert.Contains("scripts/playerclasses/spy.ctx", warning);
        Assert.Contains("[TF2/Classes] 9 of 10 class scripts loaded\n", log.Messages());
    }

    [Fact]
    public void A_malformed_script_is_logged_and_leaves_that_class_missing()
    {
        var files = Files.Shipped();
        files.Bytes["scripts/playerclasses/pyro.txt"] = Encoding.UTF8.GetBytes("PlayerClass { \"speed_max\" \"300\"");
        var log = new RecordingLog();
        var table = new TFClassTable(log, files);

        Assert.Equal(9, table.Load());

        Assert.False(table[TFClass.Pyro].IsParsed);
        Assert.Equal(0f, table[TFClass.Pyro].MaxSpeed);
        Assert.Contains("malformed", Assert.Single(log.Warnings()));
    }

    [Fact]
    public void A_script_with_no_block_leaves_that_class_missing()
    {
        var files = Files.Shipped();
        files.Bytes["scripts/playerclasses/medic.txt"] = Encoding.UTF8.GetBytes("\"speed_max\" \"320\"");
        var log = new RecordingLog();
        var table = new TFClassTable(log, files);

        Assert.False(table[TFClass.Medic].IsParsed);
        Assert.Contains("no key block", Assert.Single(log.Warnings()));
    }

    [Fact]
    public void The_table_loads_once()
    {
        var files = Files.Shipped();
        var table = new TFClassTable(new RecordingLog(), files);

        Assert.False(table.IsLoaded);
        Assert.Equal(10, table.Load());
        int asked = files.Asked.Count;
        Assert.True(asked >= 10);

        Assert.Equal(10, table.Load());
        _ = table[TFClass.Sniper];

        Assert.True(table.IsLoaded);
        Assert.Equal(asked, files.Asked.Count);
    }

    [Fact]
    public void A_read_before_any_load_loads_the_table()
    {
        var files = Files.Shipped();
        var table = new TFClassTable(new RecordingLog(), files);

        Assert.Equal(125, table[TFClass.Scout].MaxHealth);

        Assert.True(table.IsLoaded);
        Assert.NotEmpty(files.Asked);
    }

    [Fact]
    public void Undefined_is_the_special_case_and_reads_no_script()
    {
        var table = new TFClassTable(new RecordingLog(), Files.Shipped());

        Assert.Equal(TFClassData.Undefined with { Weapons = table[TFClass.Undefined].Weapons, Grenades = table[TFClass.Undefined].Grenades,
                                                  AmmoMax = table[TFClass.Undefined].AmmoMax, Buildables = table[TFClass.Undefined].Buildables,
                                                  DeathSounds = table[TFClass.Undefined].DeathSounds },
                     table[TFClass.Undefined]);
        Assert.Equal("models/player/scout.mdl", table[TFClass.Undefined].Model);
    }

    [Theory]
    [InlineData(TFClass.CountAll)]
    [InlineData(TFClass.Random)]
    [InlineData((TFClass)(-1))]
    public void A_class_outside_the_table_is_refused(TFClass c)
    {
        var table = new TFClassTable(new RecordingLog(), Files.Shipped());

        Assert.Throws<ArgumentOutOfRangeException>(() => table[c]);
    }

    [Fact]
    public void The_table_is_a_service_of_both_sides()
    {
        static Side[] Server(Type t) => t.GetCustomAttributes<SideServiceAttribute>().Select(a => a.Side).Order().ToArray();
        static Side[] Client(Type t) => t.GetCustomAttributes<ClientSideServiceAttribute>().Select(a => a.Side).Order().ToArray();
        Side[] both = [Side.Server, Side.Client];

        Assert.Equal(both.Order(), Server(typeof(TFClassTable)));
        Assert.Equal(both.Order(), Client(typeof(ClientTable)));
        Assert.Equal([Side.Server], Server(typeof(TFClassTableLoader)));
        Assert.Equal([Side.Client], Client(typeof(ClientLoader)));
    }

    [Fact]
    public void The_server_loader_loads_the_table_as_a_level_starts()
    {
        var table = new TFClassTable(new RecordingLog(), Files.Shipped());
        IGameLifecycle loader = new TFClassTableLoader(table);

        loader.OnPostInit();
        Assert.False(table.IsLoaded);

        loader.OnLevelInitPreEntity("ctf_2fort");

        Assert.True(table.IsLoaded);
        Assert.Equal(TFClassTable.LoadOrder, loader.LifecycleOrder);
    }

    [Fact]
    public void The_client_loader_loads_the_table_as_a_level_starts()
    {
        var table = new ClientTable(new RecordingLog(), new ClientFiles(Files.Shipped()));
        IClientLevelHook loader = new ClientLoader(table);

        Assert.False(table.IsLoaded);
        loader.OnLevelInit("ctf_2fort");

        Assert.True(table.IsLoaded);
        Assert.Equal(400f, table[client::TF2.Shared.TFClass.Scout].MaxSpeed);
        Assert.Equal(ClientTable.LoadOrder, ((ClientOrderedHook)loader).HookOrder);
    }

    [Fact]
    public void The_load_order_sits_between_the_core_stages_and_the_mods_range()
    {
        int highestCore = Enum.GetValues<LifecycleStage>().Max(s => (int)s);

        Assert.True(TFClassTable.LoadOrder > highestCore);
        Assert.True(TFClassTable.LoadOrder < ModStages.First);
    }
}
