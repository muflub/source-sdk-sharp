using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;
using TF2.Shared.Rules;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 main menu (stock's CHudMainMenuOverride with the matchmaking
/// dashboard's top bar): the boxes stand at mainmenuoverride.res's and
/// MatchMakingDashboard.res's places, each button does its stock command,
/// Quit and Disconnect ask first, the level swaps Quit for Disconnect and
/// adds Resume and the in-game buttons, and the art follows stock's picks.
/// </summary>
public class TFMainMenuTests
{
    private const int W = 1920, H = 1080;

    /// <summary>A library that binds every name, so the art arms draw.</summary>
    private sealed class AllArt : IUiTextureLibrary
    {
        public int Find(string name) => 1;
        public int Material(string name) => 1;
        public UiTextureInfo? About(string name) => new UiTextureInfo(1, 64, 64, "");
        public UiTextureIcon? Icon(string name) => null;
    }

    private sealed class Rig
    {
        public readonly FakeTFMainMenuSource Menu;
        public readonly FakeTFMainMenuActions Actions = new();
        public readonly TFMainMenu Screen;
        public readonly UiContext Ctx = Context();

        public Rig(bool inLevel, FakeTFMainMenuArt? art = null)
        {
            Menu = new FakeTFMainMenuSource { InLevel = inLevel };
            Screen = new TFMainMenu(Menu, art ?? new FakeTFMainMenuArt(), Actions, new AllArt());
            Draw();
        }

        public void Draw() => Frame(Ctx, UiTestKit.Screen(W, H), (ref UiFrame ui) => Screen.Describe(ref ui));

        public bool Has(string name) => Array.Exists(Elements(Ctx), e => NameOf(Ctx, e) == name);

        public UiRect Box(string name) => Named(Ctx, name).Rect;

        public void Click(string name) => ClickAt(Box(name));

        public void ClickText(string token)
        {
            var els = Elements(Ctx);
            int i = Array.FindIndex(els, e => RunText(Ctx, Array.IndexOf(els, e)) == token);
            Assert.True(i >= 0, $"no element drew {token}");
            ClickAt(els[i].Rect);
        }

        private void ClickAt(UiRect r)
        {
            float x = r.X + r.W / 2, y = r.Y + r.H / 2;
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Draw();
            Draw();
        }
    }

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 0.5f, expected + 0.5f);

    [Fact]
    public void It_claims_stock_s_main_menu_override_panel()
        => Assert.Equal("MainMenuOverride", new Rig(false).Screen.Panel);

    /// <summary>
    /// The stock places at 1920x1080 (2.25 du a unit, the screen 853.3 units
    /// wide): ITEMS at c-288, rs1-18, 100x26; the gears at c239 and c273,
    /// ypos 437, 25x25; Quit at rs1, 30 wide, and FIND A GAME one unit left of
    /// it, 120 wide; HOST A GAME at rs1-145, 115 wide; the logo at c-290 32.
    /// The reference screenshot of stock's build puts ITEMS at x 312 and
    /// FIND A GAME's plate from x 1580.
    /// </summary>
    [Fact]
    public void The_launch_menu_stands_at_stock_s_places()
    {
        var rig = new Rig(false);
        var items = rig.Box("items");
        Near(312f, items.X);
        Near(981f, items.Y);
        Near(225f, items.W);
        Near(58.5f, items.H);
        Near(960f + 239f * 2.25f, rig.Box("options").X);
        Near(960f + 273f * 2.25f, rig.Box("advanced options").X);
        Near(56.25f, rig.Box("options").W);
        Near(W - 30f * 2.25f, rig.Box("quit").X);
        Near(W - 151f * 2.25f, rig.Box("find").X);
        Near(270f, rig.Box("find").W);
        Near(W - 260f * 2.25f, rig.Box("host").X);
        Near(960f - 290f * 2.25f, rig.Box("logo").X);
        Near(32f * 2.25f, rig.Box("logo").Y);
    }

    /// <summary>
    /// The labels at stock's size: vgui makes HudFontSmallBold's 14 tall
    /// whole pixels (31 at 1080) and fills them with TF2 Build's win ascent
    /// and descent (2158 per 2048 em), so the em is 29.4 du. Live, at the
    /// TTF's em, FIND A GAME's label ran 210 px against stock's 194 and HOST
    /// A GAME's ran under FIND A GAME's plate. The narrowest unit keeps the
    /// label at the lint's large-text 24 du.
    /// </summary>
    [Fact]
    public void The_labels_take_stock_s_em()
    {
        Assert.InRange(TFMainMenu.LabelEm(2.25f), 29.3f, 29.5f);
        Assert.True(TFMainMenu.LabelEm(1.875f) >= 24f);
    }

    [Fact]
    public void The_launch_menu_has_quit_and_no_level_buttons()
    {
        var rig = new Rig(false);
        Assert.True(rig.Has("quit"));
        Assert.False(rig.Has("disconnect"));
        Assert.False(rig.Has("resume"));
        Assert.False(rig.Has("call vote"));
    }

    [Theory]
    [InlineData("find", "find")]
    [InlineData("host", "host")]
    [InlineData("items", "items")]
    [InlineData("options", "options")]
    [InlineData("advanced options", "advanced options")]
    public void Each_launch_button_does_its_command(string button, string done)
    {
        var rig = new Rig(false);
        rig.Click(button);
        Assert.Equal([done], rig.Actions.Done);
    }

    /// <summary>The dashboard's quit arm at the menu asks first
    /// (ShowConfirmDialog, tf_matchmaking_dashboard.cpp:389-399): the click
    /// alone quits nothing; Yes quits, No closes the ask.</summary>
    [Fact]
    public void Quit_asks_and_yes_quits()
    {
        var rig = new Rig(false);
        rig.Click("quit");
        Assert.Equal(0, rig.Menu.Quits);
        rig.ClickText("#TF2_MainMenu_Yes");
        Assert.Equal(1, rig.Menu.Quits);
    }

    [Fact]
    public void Quit_s_no_closes_the_ask_and_quits_nothing()
    {
        var rig = new Rig(false);
        rig.Click("quit");
        rig.ClickText("#TF2_MainMenu_No");
        Assert.Equal(0, rig.Menu.Quits);
        Assert.DoesNotContain(Elements(rig.Ctx), e => RunText(rig.Ctx, Array.IndexOf(Elements(rig.Ctx), e)) == "#TF2_MainMenu_No");
    }

    /// <summary>In a level Disconnect (115 wide) replaces Quit, FIND A GAME
    /// moves left of it and Resume left of that
    /// (UpdateDisconnectAndResume, :1280-1293).</summary>
    [Fact]
    public void A_level_swaps_quit_for_disconnect_and_adds_resume()
    {
        var rig = new Rig(true);
        Assert.False(rig.Has("quit"));
        Near(W - 115f * 2.25f, rig.Box("disconnect").X);
        Near(W - 236f * 2.25f, rig.Box("find").X);
        Near(W - 332f * 2.25f, rig.Box("resume").X);
        rig.Click("resume");
        Assert.Equal(1, rig.Menu.Resumes);
    }

    [Fact]
    public void Disconnect_asks_and_leave_disconnects()
    {
        var rig = new Rig(true);
        rig.Click("disconnect");
        Assert.Empty(rig.Actions.Done);
        rig.ClickText("#TF2_MainMenu_Leave");
        Assert.Equal(["disconnect"], rig.Actions.Done);
    }

    [Theory]
    [InlineData("report player")]
    [InlineData("call vote")]
    [InlineData("mute players")]
    [InlineData("request coach")]
    public void Each_in_game_button_does_its_command(string button)
    {
        var rig = new Rig(true);
        rig.Click(button);
        Assert.Equal([button], rig.Actions.Done);
    }

    /// <summary>The backdrop and the character show at the menu and hide in a
    /// level (OnUpdateMenu, :992-1005, :1087-1090).</summary>
    [Fact]
    public void The_art_shows_at_the_menu_and_not_in_a_level()
    {
        var art = new FakeTFMainMenuArt { Background = "console/title_fullmoon_widescreen" };
        var menu = new Rig(false, art);
        Assert.True(menu.Has("background"));
        Assert.True(menu.Has("character"));
        var level = new Rig(true, art);
        Assert.False(level.Has("background"));
        Assert.False(level.Has("character"));
    }

    // ---- the picks ----

    [Fact]
    public void Halloween_s_rolls_name_its_backdrops_and_two_hide_the_character()
    {
        Assert.Equal(("console/title_team_halloween2011_widescreen", true), TFMainMenuArtPicker.Background("halloween", 0));
        Assert.Equal(("console/title_team_halloween2014_widescreen", false), TFMainMenuArtPicker.Background("halloween", 3));
        Assert.Equal(("console/title_team_halloween2015_widescreen", false), TFMainMenuArtPicker.Background("halloween", 4));
        Assert.Equal(("console/title_scream_fortress_2017_widescreen", true), TFMainMenuArtPicker.Background("halloween", 5));
        Assert.Equal(("console/background_xmas2023_widescreen", true), TFMainMenuArtPicker.Background("christmas", 1));
        Assert.Equal(("console/title_fullmoon_widescreen", true), TFMainMenuArtPicker.Background("fullmoon", 0));
        Assert.Equal(((string?)null, true), TFMainMenuArtPicker.Background("birthday", 0));
        Assert.Equal(((string?)null, true), TFMainMenuArtPicker.Background(null, 0));
    }

    private const string Characters = """
        "characters"
        {
            "basic_heavy"   { "image" "../console/characters/heavy" }
            "basic_spy"     { "image" "../console/characters/spy" "weight" "2" }
            "spooky_heavy"  { "image" "../console/characters/hwn_heavy" "holiday_restriction" "halloween_or_fullmoon" }
            "xmas_scout"    { "image" "../console/characters/xmas_scout" "holiday_restriction" "christmas" "weight" "9" }
        }
        """;

    /// <summary>Every index the weighted list holds, by what stock's
    /// rand() % count would land on.</summary>
    private static List<string?> Every(Func<TFHoliday, bool> active)
    {
        var doc = KeyValuesText.Parse(Characters);
        int count = 0;
        TFMainMenuArtPicker.Character(doc, active, n => { count = n; return 0; });
        var all = new List<string?>();
        for (int i = 0; i < count; i++)
            all.Add(TFMainMenuArtPicker.Character(doc, active, _ => i));
        return all;
    }

    [Fact]
    public void An_ordinary_day_weighs_the_unrestricted_characters()
    {
        var all = Every(_ => false);
        Assert.Equal(["console/characters/heavy", "console/characters/spy", "console/characters/spy"], all);
    }

    /// <summary>A holiday's own characters weigh at least 6 while it is on,
    /// and a seasonal holiday zeroes the unrestricted ones.</summary>
    [Fact]
    public void Halloween_draws_only_its_own_characters_at_weight_six()
    {
        var all = Every(h => h is TFHoliday.HalloweenOrFullMoon or TFHoliday.Halloween);
        Assert.Equal(6, all.Count);
        Assert.All(all, a => Assert.Equal("console/characters/hwn_heavy", a));
    }

    [Fact]
    public void A_heavier_holiday_weight_stands()
    {
        var all = Every(h => h == TFHoliday.Christmas);
        Assert.Equal(9, all.Count);
        Assert.All(all, a => Assert.Equal("console/characters/xmas_scout", a));
    }

    [Fact]
    public void A_vgui_image_name_is_its_material_under_vgui()
    {
        Assert.Equal("console/characters/heavy", TFMainMenuArtPicker.MaterialOf("../console/characters/heavy"));
        Assert.Equal("vgui/glyph_items", TFMainMenuArtPicker.MaterialOf("glyph_items"));
    }

    // ---- the adapter's lines ----

    private sealed class NoFiles : IEngineFiles
    {
        public int GameFileSize(string path) => -1;
        public string? ReadGameFile(string path) => null;
        public byte[]? ReadGameFileBytes(string path) => null;
        public byte[]? ReadFileOnPath(string path, string pathId) => null;
    }

    private sealed class ClassMenu : ITFClassMenuActions
    {
        public int Edits { get; private set; }
        public void Opened() { }
        public void Closed() { }
        public void Choose(int classNumber) { }
        public void ChooseRandom() { }
        public void Cancel() { }
        public void EditLoadout() => Edits++;
        public void ResetLoadout() { }
    }

    private static (TFMainMenuUi Ui, List<string> Sent, ClassMenu Classes) Adapter()
    {
        var sent = new List<string>();
        var classes = new ClassMenu();
        return (new TFMainMenuUi(new ChatFeedTests.RecordingEngineClient(sent), new NoFiles(), TimeProvider.System,
                                 new DefaultForwarders.LowestRandom(), classes), sent, classes);
    }

    [Fact]
    public void The_buttons_send_stock_s_lines()
    {
        var (ui, sent, _) = Adapter();
        ui.FindAGame();
        ui.HostAGame();
        ui.Options();
        ui.AdvancedOptions();
        ui.Disconnect();
        ui.MutePlayers();
        ui.ReportPlayer();
        ui.RequestCoach();
        Assert.Equal([
            "gamemenucommand openserverbrowser",
            "gamemenucommand OpenCreateMultiplayerGameDialog",
            "gamemenucommand OpenOptionsDialog",
            "opentf2options",
            "disconnect",
            "gamemenucommand OpenMutePlayerDialog",
            "gamemenucommand OpenReportPlayerDialog",
            "cl_coach_find_coach",
        ], sent);
    }

    /// <summary>Call Vote is <c>callvote</c> and the GameUI's ResumeGame
    /// (:1923-1931); ITEMS hides the GameUI and raises the loadout on the
    /// spawned class.</summary>
    [Fact]
    public void Call_vote_resumes_and_items_raises_the_loadout()
    {
        var (ui, sent, classes) = Adapter();
        ui.CallVote();
        ui.Items();
        Assert.Equal(["callvote", "gameui_hide", "gameui_hide"], sent);
        Assert.Equal(1, classes.Edits);
    }
}
