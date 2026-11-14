using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The game-mode catalog (plan_bots_teams_tf2 D.2): the core's two modes with
/// their settings and when each applies, the rows a provider adds to every
/// mode, and which modes a map can play from the entity lump of its bsp.
/// </summary>
public class GameModeCatalogTests
{
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Level(string? map) : ILevelInfo
    {
        public string? Map => map;
        public bool IsReady => map is not null;
    }

    /// <summary>The GAME paths as a table of bsps, counting each read.</summary>
    internal sealed class Bsps : IEngineFiles
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Reads = [];
        public int GameFileSize(string path) => Files.TryGetValue(path, out var b) ? b.Length : -1;
        public string? ReadGameFile(string path) => Files.TryGetValue(path, out var b) ? Encoding.UTF8.GetString(b) : null;
        public byte[]? ReadGameFileBytes(string path)
        {
            Reads.Add(path);
            return Files.TryGetValue(path, out var b) ? b : null;
        }
        public byte[]? ReadFileOnPath(string path, string pathId) => ReadGameFileBytes(path);
    }

    /// <summary>A bsp whose entity lump (lump 0) holds one entity of each class, after a world.</summary>
    internal static byte[] Bsp(params string[] classes)
    {
        var text = new StringBuilder("{\n\"classname\" \"worldspawn\"\n\"skyname\" \"sky_day01_01\"\n}\n");
        foreach (string c in classes)
            text.Append("{\n\"origin\" \"0 0 0\"\n\"classname\" \"").Append(c).Append("\"\n}\n");
        byte[] lump = [.. Encoding.UTF8.GetBytes(text.ToString()), 0];
        const int header = 8 + 64 * 16;
        var bsp = new byte[header + lump.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bsp, 'V' | ('B' << 8) | ('S' << 16) | ('P' << 24));
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(4), 20);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(8), header);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(12), lump.Length);
        lump.CopyTo(bsp, header);
        return bsp;
    }

    /// <summary>A mode that plays only on a map with a flag, as TF2's CTF does.</summary>
    private sealed class FlagMode(IRoundFlow rounds) : IGameMode
    {
        public string Id => "ctf";
        public IRoundFlow Rounds => rounds;
        public bool Teamplay => true;
        public IModeSettings Settings => new ModeSettings("#CTF", [CoreModeSpecs.TimeLimit()], ["item_teamflag"]);
    }

    /// <summary>A mode that publishes nothing.</summary>
    private sealed class BareMode(IRoundFlow rounds) : IGameMode
    {
        public string Id => "bare";
        public IRoundFlow Rounds => rounds;
    }

    private sealed class Rows(params SettingSpec[] rows) : IModeSettingsProvider
    {
        public IEnumerable<SettingSpec> SettingsFor(ModeSpec mode) => rows;
    }

    private static (TeamDeathmatchTests.Rig Rig, Bsps Files) World()
    {
        var rig = new TeamDeathmatchTests.Rig();
        var files = new Bsps();
        files.Files["maps/dm_lockdown.bsp"] = Bsp("info_player_deathmatch", "weapon_smg1");
        files.Files["maps/ctf_2fort.bsp"] = Bsp("info_player_teamspawn", "item_teamflag", "item_teamflag");
        return (rig, files);
    }

    private static GameModeCatalog Catalog(TeamDeathmatchTests.Rig rig, IEngineFiles files, params IModeSettingsProvider[] providers)
        => new(rig.Modes, new BspEntityClasses(files), new Level("dm_lockdown"),
               providers.Length == 0 ? null : new ListHooks<IModeSettingsProvider>(providers));

    private static string Keys(ModeSpec mode) => string.Join(",", mode.Settings.Select(s => s.Key));

    [Fact]
    public void TheCatalogListsTheCoresTwoModesDeathmatchFirst()
    {
        var (rig, files) = World();

        var modes = Catalog(rig, files).Modes();

        Assert.Equal(["null", "tdm"], modes.Select(m => m.Id));
        Assert.Equal(["#SS_UI_Mode_Deathmatch", "#SS_UI_Mode_TeamDeathmatch"], modes.Select(m => m.Title));
        Assert.Equal([false, true], modes.Select(m => m.Teamplay));
    }

    [Fact]
    public void DeathmatchHasItsLimitsAndNoTeamRows()
    {
        var (rig, files) = World();

        Assert.Equal("mp_timelimit,mp_fraglimit", Keys(Catalog(rig, files).Find("null")!));
    }

    [Fact]
    public void TeamDeathmatchHasTheTeamRowsAndItsJoinLimitWaitsForTheNextRound()
    {
        var (rig, files) = World();

        var tdm = Catalog(rig, files).Find("tdm")!;

        Assert.Equal("mp_friendlyfire,mp_timelimit,mp_fraglimit,mp_tdm_teams,mp_teammates_collide,cs_sv_teams_unbalance_limit", Keys(tdm));
        Assert.Equal(SettingApplies.NextRound, tdm.Settings.Single(s => s.Key == "cs_sv_teams_unbalance_limit").Applies);
        Assert.All(tdm.Settings.Where(s => s.Key != "cs_sv_teams_unbalance_limit"), s => Assert.Equal(SettingApplies.Now, s.Applies));
    }

    [Fact]
    public void TheActiveModesSettingsFollowASwitch()
    {
        var (rig, files) = World();
        var catalog = Catalog(rig, files);
        Assert.Null(catalog.ActiveSetting("mp_tdm_teams"));

        rig.Tdm();

        Assert.Equal("tdm", catalog.Active!.Id);
        Assert.NotNull(catalog.ActiveSetting("mp_tdm_teams"));
    }

    [Fact]
    public void AProvidersRowsJoinEveryModeButNeverReplaceTheModesOwn()
    {
        var (rig, files) = World();
        var mine = CoreModeSpecs.Choice("mp_fraglimit", "99", ("99", "99"));
        var bots = CoreModeSpecs.Choice("ai_bot_count", "0", ("0", "0"), ("4", "4"));

        var catalog = Catalog(rig, files, new Rows(mine, bots));

        Assert.Equal("mp_timelimit,mp_fraglimit,ai_bot_count", Keys(catalog.Find("null")!));
        Assert.Contains("ai_bot_count", Keys(catalog.Find("tdm")!));
        Assert.Equal("0", catalog.Find("null")!.Settings.Single(s => s.Key == "mp_fraglimit").Default);
    }

    [Fact]
    public void AModeThatPublishesNothingIsListedByItsIdAndPlaysAnywhere()
    {
        var (rig, files) = World();
        rig.Services.AddInstance(new BareMode(rig.Rules));

        var catalog = Catalog(rig, files);

        var bare = catalog.Find("bare")!;
        Assert.Equal(("bare", "bare", 0), (bare.Id, bare.Title, bare.Settings.Count));
        Assert.True(catalog.Supports("ctf_2fort", "bare").Supported);
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void AMapWithoutAFlagCannotPlayCtfAndSaysWhy()
    {
        var (rig, files) = World();
        rig.Services.AddInstance(new FlagMode(rig.Rules));

        var catalog = Catalog(rig, files);

        Assert.Equal(new MapSupport(false, "dm_lockdown has no item_teamflag"), catalog.Supports("dm_lockdown", "ctf"));
        Assert.Equal(new MapSupport(true, ""), catalog.Supports("ctf_2fort", "ctf"));
    }

    [Fact]
    public void AMapThatIsNotInstalledSupportsNoModeThatNeedsAnEntity()
    {
        var (rig, files) = World();
        rig.Services.AddInstance(new FlagMode(rig.Rules));

        var catalog = Catalog(rig, files);

        Assert.Equal(new MapSupport(false, "ctf_gone's entities could not be read"), catalog.Supports("ctf_gone", "ctf"));
        Assert.True(catalog.Supports("ctf_gone", "tdm").Supported);
        Assert.False(catalog.Supports("dm_lockdown", "nosuch").Supported);
    }

    [Fact]
    public void TheLumpIsReadOncePerMap()
    {
        var files = new Bsps();
        files.Files["maps/ctf_2fort.bsp"] = Bsp("item_teamflag");
        var classes = new BspEntityClasses(files);

        Assert.Contains("item_teamflag", classes.Of("ctf_2fort")!);
        Assert.Contains("worldspawn", classes.Of("CTF_2FORT")!);

        Assert.Equal(["maps/ctf_2fort.bsp"], files.Reads);
    }

    [Fact]
    public void AMapNameThatClimbsOutOfMapsIsNeverRead()
    {
        var files = new Bsps();
        var classes = new BspEntityClasses(files);

        Assert.Null(classes.Of("../cfg/server"));
        Assert.Null(classes.Of("sub/map"));
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void ACompressedEntityLumpOrANonBspReadsAsUnknown()
    {
        byte[] bsp = Bsp("item_teamflag");
        Assert.NotNull(BspEntityClasses.Read(bsp));

        byte[] compressed = (byte[])bsp.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(compressed.AsSpan(20), 4096);
        byte[] notBsp = (byte[])bsp.Clone();
        notBsp[0] = (byte)'X';

        Assert.Null(BspEntityClasses.Read(compressed));
        Assert.Null(BspEntityClasses.Read(notBsp));
        Assert.Null(BspEntityClasses.Read(bsp.AsSpan(0, 100)));
    }

    [Fact]
    public void OnlyClassnameValuesAreClassesNotOtherKeysOrValues()
    {
        var classes = BspEntityClasses.ClassNames("{\n\"targetname\" \"item_teamflag\"\n\"classname\" \"info_target\"\n}\n{\n\"item_teamflag\" \"x\"\n}");

        Assert.Equal(["info_target"], classes.Order());
    }
}
