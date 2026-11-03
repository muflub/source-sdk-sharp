using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The create-server window (plan_gameui_managed.md G3), as stock's create
/// multiplayer game dialog behaved: a random map first then the maps, the
/// server name, size and password from the mod's server script (or the
/// game's name and stock's 32), the script saved and the server started by
/// stock's command sequence.
/// </summary>
public class CreateServerTests
{
    private sealed class Rig
    {
        public readonly UiSampleServices Services = new();
        public readonly CreateServer Screen;

        public Rig(Action<UiSampleServices>? setup = null)
        {
            setup?.Invoke(Services);
            Screen = CreateServerSamples.Build(Services);
            Screen.Load();
        }

        public RecordingCreateServer Started => (RecordingCreateServer)Services.Create<ICreateServerActions>();
        public MemoryServerSettings Settings => (MemoryServerSettings)Services.Create<IServerSettingsSource>();
    }

    [Fact]
    public void The_maps_follow_a_random_map_and_the_server_fields_come_from_the_script()
    {
        var r = new Rig();
        Assert.Equal(0, r.Screen.Map);                                  // stock's first item: a random map
        Assert.Equal(8, r.Screen.Maps.Count);
        Assert.Equal(("Team Fortress 2", 24f, ""), (r.Screen.HostName, r.Screen.MaxPlayers, r.Screen.Password));
    }

    [Fact]
    public void Without_a_script_the_server_is_named_for_the_game_and_holds_stocks_32()
    {
        var r = new Rig(s => s.Add<IServerSettingsSource>(new MemoryServerSettings()));
        Assert.Equal(("Half-Life 2: Deathmatch", 32f), (r.Screen.HostName, r.Screen.MaxPlayers));
    }

    [Fact]
    public void Start_saves_the_script_with_the_server_fields_and_starts_the_chosen_map()
    {
        var r = new Rig();
        r.Screen.Map = 1;
        r.Screen.HostName = "lan party";
        r.Screen.MaxPlayers = 12;
        r.Screen.Password = "pw";
        r.Screen.Start(new Random(1));
        Assert.Equal(("dm_lockdown", 12, "lan party", "pw"), r.Started.Started.Single());
        string[] saved = r.Settings.Saved.Single();
        Assert.Equal(("lan party", "12", "pw"), (saved[0], saved[1], saved[2]));
    }

    [Fact]
    public void A_random_map_is_one_of_the_maps()
    {
        var r = new Rig();
        r.Screen.Start(new Random(7));
        Assert.Contains(r.Started.Started.Single().Map, r.Screen.Maps);
    }

    [Fact]
    public void With_no_maps_start_does_nothing()
    {
        var r = new Rig(s => s.Add<IMapListSource>(new FixedMapList("Game")));
        r.Screen.Start(new Random(1));
        Assert.Empty(r.Started.Started);
        Assert.Empty(r.Settings.Saved);
    }

    [Fact]
    public void The_server_starts_with_stocks_command_sequence()
    {
        var lines = new RecordingCommandLine();
        new CreateServerActions(lines).Start("dm_lockdown", 16, "my \"server\"", "");
        Assert.Equal(["disconnect", "wait", "wait", "sv_lan 1", "setmaster enable", "maxplayers 16", "sv_password \"\"",
                      "hostname \"my server\"", "progress_enable", "map dm_lockdown"], lines.Lines);
    }

    private sealed class Finder(params string[] files) : IFwdFileFinder
    {
        public List<string> Find(string wildcard, string? pathId, bool directories = false)
            => wildcard == "maps/*.bsp" && pathId == "GAME" && !directories ? [.. files] : [];
    }

    [Fact]
    public void The_map_list_is_the_bsps_by_name_sorted_less_the_hidden_maps()
    {
        const string gameInfo = """
            "GameInfo"
            {
            	game	"Half-Life 2: Deathmatch"
            	hidden_maps
            	{
            		"test_speakers"		1
            		"test_hardware"		1
            	}
            }
            """;
        var list = new EngineMapList(new Finder("dm_runoff.bsp", "test_speakers.bsp", "dm_lockdown.bsp", "DM_LOCKDOWN.bsp",
                                                "test_hardware.bsp", "readme.txt"), gameInfo);
        Assert.Equal(["dm_lockdown", "dm_runoff"], list.Maps());
        Assert.Equal("Half-Life 2: Deathmatch", list.GameName);
        Assert.Equal("Source", new EngineMapList(new Finder(), null).GameName);
    }

    private sealed class Files : IOptionsFiles
    {
        public Dictionary<string, byte[]> Stored { get; } = [];

        public byte[]? ReadAll(string fileName) => Stored.GetValueOrDefault(fileName);

        public void WriteAll(string fileName, byte[] bytes) => Stored[fileName] = bytes;
    }

    [Fact]
    public void The_server_script_is_the_settings_files_and_saves_as_settings_scr()
    {
        var files = new Files();
        files.Stored["cfg/settings_default.scr"] = Encoding.UTF8.GetBytes("""
            VERSION 1.0
            DESCRIPTION SERVER_OPTIONS
            {
            	"hostname" { "#Valve_Hostname" { STRING } { "Team Fortress 2" } }
            	"maxplayers" { "#Valve_Max_Players" { NUMBER 1 24 } { "24" } }
            }
            """);
        var lines = new RecordingCommandLine();
        var source = new ServerSettingsSource(files, new MemoryConVarStore().With("hostname", "SourceSharp"), lines);
        var entries = source.Load();
        Assert.Equal(["hostname", "maxplayers"], entries.Select(e => e.Name));
        Assert.Equal("Team Fortress 2", entries[0].Value);   // the script's, not the convar's, as stock's page
        source.Save(entries, ["a", "8"]);
        Assert.Equal(["hostname \"a\"", "maxplayers \"8.000000\""], lines.Lines);
        Assert.True(files.Stored.ContainsKey("cfg/settings.scr"));
        Assert.Contains("DESCRIPTION SERVER_OPTIONS", Encoding.UTF8.GetString(files.Stored["cfg/settings.scr"]));
    }
}
