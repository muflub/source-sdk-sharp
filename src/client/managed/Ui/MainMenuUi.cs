using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The main menu's buttons (the launch menu, and the pause menu stock's
/// ClientModeSingle::OnKeyDown shows): Play loads the map the mod's maplist
/// names, as stock's GameUI "StartNewGame" loads one; Resume hides the menu
/// through the engine's own command; Options shows the options window; Quit
/// asks first (stock's quit confirmation). The menu's other commands go by
/// stock's names to the GameUI's table (<see cref="MenuCommand"/>, wired once
/// the table is built). The host wires Closed and OptionsShown to its layer's opens
/// and closes; the buttons answer through them, so a sample can record clicks
/// without a host. Resume only when a level is up and Play only when the
/// maplist named one — the screen hides the rest.
/// </summary>
public sealed class MainMenuUi(IFwdEngineClient engine, ConfirmQuitUi quit, string? startMap = null)
    : IMainMenuSource, IMainMenuCommands
{
    public Action? Closed { get; set; }

    /// <summary>The GameUI's game menu table, which answers the menu's commands.</summary>
    public Func<string, bool>? MenuCommand { get; set; }

    public void Run(string command) => MenuCommand?.Invoke(command);

    public Action? OptionsShown { get; set; }

    /// <summary>A level is up: the engine names one. At the launch menu no map is
    /// loaded and the name is empty, so Resume hides with it.</summary>
    public bool InLevel => engine.LevelName() is { Length: > 0 };

    public bool CanPlay => startMap is not null;

    /// <summary>Stock's GameUI "StartNewGame": the map command, which starts the
    /// listen server on the named map. The menu goes with the GameUI's own hide
    /// when the level loads, the same edge that shows it.</summary>
    public void Play()
    {
        if (startMap is not { Length: > 0 } map)
            return;
        engine.SubmitConsoleLine($"map {map}");
    }

    /// <summary>Stock's GameUI "ResumeGame": run the engine's own hide command so engine and menu
    /// agree (the engine's OnGameUIHidden fires gameui_hidden and the host closes the screen with
    /// it); Closed answers directly too, so the button works where no engine queues the command
    /// (tests, samples).</summary>
    public void Resume()
    {
        engine.SubmitConsoleLine("gameui_hide");
        Closed?.Invoke();
    }

    public void OpenOptions() => OptionsShown?.Invoke();

    public void Quit() => quit.Ask();
}

/// <summary>
/// The mod's map list (plan_client_novalve §3.2a's create-server row):
/// <see cref="FileName"/> on the game's paths, one map per line, <c>//</c> and
/// <c>#</c> comment lines allowed. Its first entry is the map Play loads; a mod
/// with no list has no Play, and the menu says so once.
/// </summary>
public static class MenuMapList
{
    public const string FileName = "maplist.txt";

    public static string? First(IEngineFiles? files, ILog log)
    {
        if (files?.ReadGameFileBytes(FileName) is not { } bytes)
        {
            log.Msg($"[SourceSharp/Ui] no {FileName} on the game paths: the menu has no Play\n");
            return null;
        }
        return First(System.Text.Encoding.UTF8.GetString(bytes));
    }

    public static string? First(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string entry = raw.Trim();
            if (entry.Length > 0 && !entry.StartsWith("//", StringComparison.Ordinal) && entry[0] != '#')
                return entry;
        }
        return null;
    }
}
