extern alias client;

using Xunit;

using Abi = client::SourceSharp.Abi;
using ClientShared = client::SourceSharp.Shared;
using ClientSide = client::SourceSharp.Client;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Info = client::TF2.Client.Ui.TFMapInfoUi;             // the game half, internals visible
using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Standalone = client::SourceSharp.Client.Standalone;
using Ui = client::SourceSharp.Client.Ui;

namespace TF2.Tests.Client;

/// <summary>
/// The TF2 map info menu's game half: the map's file base off
/// <c>engine-&gt;GetLevelName()</c> (tf_mapinfomenu.cpp:99-105), the
/// display name and game type stock's cleanup arms derive from it
/// (tf_gamerules.cpp:18974-19206), the briefing off the
/// <c>#&lt;map&gt;_description</c> localisation with the one default
/// behind it (tf_mapinfomenu.cpp:379-387), the photo material
/// (:349-368), and what CONTINUE and BACK send (<c>OnCommand</c>,
/// :257-327).
/// </summary>
public sealed class TFMapInfoUiTests
{
    [Fact]
    public void The_display_name_comes_from_the_level_name_through_stock_cleanup()
    {
        Assert.Equal("2FORT", Info.DisplayName("ctf_2fort"));
        Assert.Equal("TURBINE", Info.DisplayName("ctf_turbine_final"));   // _final stripped
        Assert.Equal("BADLANDS", Info.DisplayName("koth_badlands_final1")); // ...and its _1
        Assert.Equal("WELL", Info.DisplayName("arena_well"));
        Assert.Equal("UPSTREAM", Info.DisplayName("pl_upstream"));
        Assert.Equal("SS SANDBOX", Info.DisplayName("ss_sandbox"));       // no prefix: underscores for spaces, LOUD
    }

    [Fact]
    public void The_game_type_names_the_mode_prefix_token()
    {
        Assert.Equal("#TF2_Gametype_Ctf", Info.GameType("ctf_2fort"));
        Assert.Equal("#TF2_Gametype_Koth", Info.GameType("koth_viaduct"));
        Assert.Equal("", Info.GameType("ss_sandbox"));                    // stock's no-rules arm
    }

    [Fact]
    public void The_menu_reads_the_level_once_and_names_it()
    {
        var (info, engine, client, _, mod) = Built("maps/ctf_2fort.bsp",
                                              mod: new Dictionary<string, string>
                                              {
                                                  ["#ctf_2fort_description"] = "The two bases face each other.",
                                                  ["#TF2_Gametype_Ctf"] = "Capture the Flag",
                                                  ["#TF2_MapInfo_Default"] = "Capture the enemy intelligence.",
                                              });

        Assert.Equal("2FORT", info.MapName);
        Assert.Equal("Capture the Flag", info.GameMode);
        Assert.Equal("The two bases face each other.", info.Briefing);
        Assert.Equal("vgui/maps/menu_photos_ctf_2fort", info.Photo);

        // The reads are per level: asking again re-answers, it does not
        // re-look (the per-level cache).
        var asked = mod.Finds;
        Assert.Equal("2FORT", info.MapName);
        Assert.Equal("Capture the Flag", info.GameMode);
        Assert.Equal(asked, mod.Finds);

        // A new level re-reads: the cache is keyed on the level name.
        client.Level = "maps/koth_viaduct.bsp";
        Assert.Equal("VIADUCT", info.MapName);
    }

    /// <summary>A map with no <c>#&lt;map&gt;_description</c> falls to the
    /// one default (tf_mapinfomenu.cpp:390+); with neither, stock's page
    /// is blank.</summary>
    [Fact]
    public void A_map_without_its_own_briefing_falls_back_to_the_default()
    {
        var (info, _, _, _, _) = Built("maps/ss_sandbox.bsp",
                                    mod: new Dictionary<string, string>
                                    {
                                        ["#TF2_MapInfo_Default"] = "Capture the enemy intelligence.",
                                    });
        Assert.Equal("Capture the enemy intelligence.", info.Briefing);

        var (blank, _, _, _, _) = Built("maps/ss_sandbox.bsp", mod: new Dictionary<string, string>());
        Assert.Equal("", blank.Briefing);
    }

    [Fact]
    public void Continue_without_a_team_closes_and_raises_the_team_panel()
    {
        var (info, _, _, nav, _) = Built("maps/ctf_2fort.bsp", mod: []);
        Assert.Empty(nav.Closed);                             // before the click: nothing moved

        info.Continue();                                      // stock's no-team arm (:288-298)
        Assert.Equal(["tf2mapinfo"], nav.Closed);
        Assert.Equal(["team"], nav.Panels);
    }

    [Fact]
    public void Continue_on_a_team_closes_and_raises_nothing()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        var st = new Standalones();
        var resource = Arrive(st);
        resource.State.Team[2] = 2;                           // local (slot 2) on RED
        var nav = new Recorder();
        var info = new Info(st, engine, new EngineClient { Level = "maps/ctf_2fort.bsp" }, new ModLocaliser([]), nav);

        Assert.Equal(2, info.Team);
        info.Continue();
        Assert.Equal(["tf2mapinfo"], nav.Closed);
        Assert.Empty(nav.Panels);                             // the lobby case: stock sends nothing
    }

    [Fact]
    public void Back_raises_the_text_window()
    {
        var (info, _, _, nav, _) = Built("maps/ctf_2fort.bsp", mod: []);
        info.Back();                                          // stock's back arm (:261-269)
        Assert.Equal(["tf2mapinfo"], nav.Closed);
        Assert.Equal(["info"], nav.Panels);
    }

    // ---- the rig ----

    private static (Info Info, Engine Screen, EngineClient Client, Recorder Nav, ModLocaliser Mod) Built(
        string level, Dictionary<string, string> mod)
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 0);
        // The live engine's tables: a token asked comes back unchanged — the
        // mod's tokens are the mod's file's, which the engine does not hold.
        engine.ReturnsEachCall("Hud.Localise", () => (string)engine.Calls[^1].Arg(0)!);
        var client = new EngineClient { Level = level };
        var nav = new Recorder();
        var asks = new ModLocaliser(mod);
        return (new Info(new Standalones(), engine, client, asks, nav), engine, client, nav, asks);
    }

    /// <summary>The mod's token file, as the game's GameUiLocaliser reads it
    /// (tf2_english.txt first): the tokens the engine's tables lack.</summary>
    private sealed class ModLocaliser(Dictionary<string, string> tokens) : Ui.IUiLocalisation
    {
        /// <summary>How often a token was looked up: the per-level cache's proof.</summary>
        public int Finds;

        public string? Find(string token) { Finds++; return tokens.TryGetValue(token, out var text) ? text : null; }
    }

    private static Standalone.ClientPlayerResource Arrive(Standalones st)
    {
        var resource = new Standalone.ClientPlayerResource(new Engine());
        resource.State.BindLocal();
        resource.State.Flags[2] = ClientShared.PlayerResourceNetState.FlagConnected;
        st.All.Add(resource);
        return resource;
    }

    private sealed class Standalones : ClientSide.IClientStandalones
    {
        public readonly List<Standalone.ClientStandaloneEntity> All = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => All;

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class Recorder : Nav
    {
        public readonly List<string> Opened = [], Panels = [], Closed = [];

        public bool Has(string screen) => true;

        public void Open(string screen) => Opened.Add(screen);

        public void OpenPanel(string panel) => Panels.Add(panel);

        public void Close(string screen) => Closed.Add(screen);
    }

    private sealed class EngineClient : Abi.IFwdEngineClient
    {
        public string? Level;

        public string? LevelName() => Level;
        public void SubmitConsoleLine(string line) { }
        public void SpectatorCommand(Abi.SpectatorKeyCommand command) { }
        public void SpectatorServerCommand(string line) { }
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public int SentenceIndexFromName(string name) => -1;
        public int LocalPlayerSlot() => 0;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public bool CopyLocalFile(string source, string destination) => false;
        public bool TryGetPlayerInfo(int entIndex, out Abi.ClientPlayerInfo info) { info = default; return false; }
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        public bool TryLightForPoint(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
    }
}
