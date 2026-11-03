//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

using TF2.Shared.Rules;

namespace TF2.Client.Ui;

/// <summary>
/// The TF2 main menu in game (stock's <c>CHudMainMenuOverride</c> and the
/// matchmaking dashboard's top bar): the art, picked once on the first ask
/// as stock picks it when the panel is built, from the wall clock's
/// holiday, the engine's random stream and the game's
/// scripts/CharacterBackgrounds.txt; and the buttons, each the console line
/// stock's command sends.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMainMenuArt), typeof(ITFMainMenuActions)])]
public sealed class TFMainMenuUi(IFwdEngineClient engine, IEngineFiles files, TimeProvider time,
                                 IUniformRandom random, ITFClassMenuActions classMenu)
    : ITFMainMenuArt, ITFMainMenuActions
{
    internal const string CharacterFile = "scripts/CharacterBackgrounds.txt";

    private bool _picked;
    private string? _background;
    private string? _character;

    public string? Background
    {
        get
        {
            Pick();
            return _background;
        }
    }

    public string? Character
    {
        get
        {
            Pick();
            return _character;
        }
    }

    private void Pick()
    {
        if (_picked)
            return;
        _picked = true;

        DateTimeOffset now = time.GetLocalNow();
        // RandomInt( 0, 5 ) for Halloween, RandomInt( 0, 1 ) for Christmas
        // (:484, :496): only the active holiday's roll is ever made.
        string? holiday = TFHolidayCalendar.ActiveName(now);
        int roll = holiday switch
        {
            "halloween" => random.RandomInt(0, 5),
            "christmas" => random.RandomInt(0, 1),
            _ => 0,
        };
        var background = TFMainMenuArtPicker.Background(holiday, roll);
        _background = background.Image;
        if (!background.UsesCharacters)
            return;

        DateTime local = now.DateTime;
        _character = files.ReadGameFile(CharacterFile) is { } text
            ? TFMainMenuArtPicker.Character(KeyValuesText.Parse(text), h => TFHolidayCalendar.IsActive(h, local),
                                            count => random.RandomInt(0, count - 1))
            : null;
    }

    public void FindAGame() => engine.SubmitConsoleLine("gamemenucommand openserverbrowser");

    public void HostAGame() => engine.SubmitConsoleLine("gamemenucommand OpenCreateMultiplayerGameDialog");

    public void Options() => engine.SubmitConsoleLine("gamemenucommand OpenOptionsDialog");

    public void AdvancedOptions() => engine.SubmitConsoleLine("opentf2options");

    /// <summary>Stock's <c>open_charinfo</c> raises the character panel over
    /// the menu; the loadout here rides the server's inventory, so it rises
    /// in play on the spawned class (the class menu's Edit Loadout,
    /// tf_classmenu.cpp:1307-1320) once the GameUI hides. With no class
    /// there is nothing to edit and the menu stays.</summary>
    public void Items()
    {
        engine.SubmitConsoleLine("gameui_hide");
        classMenu.EditLoadout();
    }

    public void Disconnect() => engine.SubmitConsoleLine("disconnect");

    public void CallVote()
    {
        engine.SubmitConsoleLine("callvote");
        engine.SubmitConsoleLine("gameui_hide");
    }

    public void MutePlayers() => engine.SubmitConsoleLine("gamemenucommand OpenMutePlayerDialog");

    public void RequestCoach() => engine.SubmitConsoleLine("cl_coach_find_coach");

    public void ReportPlayer() => engine.SubmitConsoleLine("gamemenucommand OpenReportPlayerDialog");
}

/// <summary>
/// The main menu's two picks, kept free of the engine so the facts hold
/// stock's rules: the background mainmenuoverride.res names under each
/// holiday's condition, and the character <c>LoadCharacterImageFile</c>
/// draws from the script (tf_hud_mainmenuoverride.cpp:657-773).
/// </summary>
public static class TFMainMenuArtPicker
{
    /// <summary>
    /// The Background panel's image under the holiday's condition
    /// (mainmenuoverride.res if_halloween_0..5, if_fullmoon,
    /// if_christmas_0/1) and whether the character art shows over it:
    /// Halloween's rolls 3 and 4 hide it (<c>ApplySchemeSettings</c>,
    /// :486-492). Any other holiday, or none, names no image.
    /// </summary>
    public static (string? Image, bool UsesCharacters) Background(string? holiday, int roll) => holiday switch
    {
        "halloween" => (roll switch
        {
            0 => "console/title_team_halloween2011_widescreen",
            1 => "console/title_team_halloween2012_widescreen",
            2 => "console/title_team_halloween2013_widescreen",
            3 => "console/title_team_halloween2014_widescreen",
            4 => "console/title_team_halloween2015_widescreen",
            _ => "console/title_scream_fortress_2017_widescreen",
        }, roll is not (3 or 4)),
        "fullmoon" => ("console/title_fullmoon_widescreen", true),
        "christmas" => (roll == 0 ? "console/background_xmas2020_widescreen" : "console/background_xmas2023_widescreen", true),
        _ => (null, true),
    };

    /// <summary>
    /// <c>LoadCharacterImageFile</c>'s draw: each character weighs its
    /// <c>weight</c> (1 by default); a holiday-restricted one weighs at least
    /// 6 while its holiday is active and nothing otherwise; while summer,
    /// Halloween or a full moon, or Christmas is on, every unrestricted one
    /// weighs nothing. <paramref name="pick"/> is stock's <c>rand() % count</c>
    /// over the weighted list. No war or operation is ever active in this
    /// port (the item schema's war definitions answer none), so their arms
    /// never weigh in. The image is a vgui image name, relative to
    /// materials/vgui/ (<c>../console/characters/heavy</c>), answered as a
    /// material name.
    /// </summary>
    public static string? Character(KeyValuesText document, Func<TFHoliday, bool> active, Func<int, int> pick)
    {
        if (document.Children.Count == 0)
            return null;

        bool seasonal = active(TFHoliday.Summer) || active(TFHoliday.HalloweenOrFullMoon) || active(TFHoliday.Christmas);
        var usable = new List<KeyValuesText>();
        foreach (var character in document.Children[0].Children)
        {
            if (!character.IsBlock)
                continue;
            TFHoliday holiday = TFHolidayCalendar.ForName(character.Find("holiday_restriction")?.Value);
            int weight = int.TryParse(character.Find("weight")?.Value, out int w) ? w : 1;
            if (holiday != TFHoliday.None)
                weight = active(holiday) ? Math.Max(weight, 6) : 0;
            else if (seasonal)
                weight = 0;
            for (int i = 0; i < weight; i++)
                usable.Add(character);
        }

        if (usable.Count == 0)
            return null;
        string? image = usable[pick(usable.Count)].Find("image")?.Value;
        return image is { Length: > 0 } ? MaterialOf(image) : null;
    }

    /// <summary>A vgui ImagePanel's image name as a material name: relative
    /// to materials/vgui/, so "../" climbs to materials/.</summary>
    public static string MaterialOf(string vguiImage)
        => vguiImage.StartsWith("../", StringComparison.Ordinal) ? vguiImage[3..] : "vgui/" + vguiImage;
}

/// <summary>
/// The main menu's second gear, <c>opentf2options</c> (stock: the TF client's
/// advanced options dialog over cfg/user_default.scr): the options window on
/// its multiplayer page, where the core draws that script's entries
/// (plan_gameui_managed.md G3).
/// </summary>
[ConCommand("opentf2options", Help = "Open the advanced multiplayer options")]
public sealed class TFAdvancedOptionsCommand(ITFMenuNavigation nav) : IConCommand
{
    public void Execute(string args) => nav.Open("multiplayeroptions");
}

/// <summary>
/// The TF2 main menu's bottom row on the core's taskbar, which is up with the
/// menu: ITEMS (stock's CharacterSetupButton) at the left, in a level
/// GameMenu.res's four in-game buttons beside it (ReportPlayer, CallVote,
/// MutePlayers, RequestCoach), and the advanced-options gear
/// (TF2SettingsButtonSDK) in the tray. The first gear (SettingsButtonSDK, the
/// options dialog) is the taskbar's own Settings.
/// </summary>
[SideService(Side.Client, As = [typeof(ITaskbarLaunchers)])]
public sealed class TFTaskbarLaunchers(ITFMainMenuActions actions, IFwdEngineClient engine) : ITaskbarLaunchers
{
    private const int Items = 0, Advanced = 1, ReportPlayer = 2, CallVote = 3, MutePlayers = 4, RequestCoach = 5;

    private readonly UiTaskbarLauncher[] _launchers =
    [
        new("#TF2_MainMenu_Items", false), new("#TF2_MainMenu_AdvancedOptions", true),
        new("#TF2_MainMenu_ReportPlayer", false), new("#TF2_MainMenu_CallVote", false),
        new("#TF2_MainMenu_MutePlayers", false), new("#TF2_MainMenu_RequestCoach", false),
    ];

    // A level is up: the engine names one (the menu's own InLevel answer).
    private bool InLevel => engine.LevelName() is { Length: > 0 };

    public int LauncherCount => InLevel ? _launchers.Length : 2;

    public UiTaskbarLauncher Launcher(int i) => _launchers[i];

    public void Launch(int i)
    {
        switch (i)
        {
            case Items: actions.Items(); break;
            case Advanced: actions.AdvancedOptions(); break;
            case ReportPlayer: actions.ReportPlayer(); break;
            case CallVote: actions.CallVote(); break;
            case MutePlayers: actions.MutePlayers(); break;
            case RequestCoach: actions.RequestCoach(); break;
        }
    }
}
