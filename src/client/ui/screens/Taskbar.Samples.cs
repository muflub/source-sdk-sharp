using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A fixed taskbar: its windows, a mod's buttons, what each click and the settings asked for, and a set clock.</summary>
public sealed class FixedTaskbar(params UiTaskbarWindow[] windows) : ITaskbarWindows, ITaskbarTray, ITaskbarLaunchers
{
    public List<UiTaskbarLauncher> Launchers { get; init; } = [];

    public List<int> Launched { get; } = [];

    public int LauncherCount => Launchers.Count;

    public UiTaskbarLauncher Launcher(int i) => Launchers[i];

    public void Launch(int i) => Launched.Add(i);

    public List<int> Activated { get; } = [];

    public int SettingsOpened { get; private set; }

    public int WindowCount => windows.Length;

    public UiTaskbarWindow Window(int i) => windows[i];

    public void Activate(int i) => Activated.Add(i);

    public bool SettingsOpen { get; set; }

    public void OpenSettings() => SettingsOpened++;

    public string Clock { get; set; } = "21:47";
}

public static class TaskbarSamples
{
    public static FixedTaskbar Three() => new(
        new UiTaskbarWindow("console", "#SS_UI_Console_Title", false, false),
        new UiTaskbarWindow("serverbrowser", "#SS_UI_Browser_Title", true, false),
        new UiTaskbarWindow("options", "#SS_UI_Options_Title", false, true));

    private static UiScreen Build(UiSampleServices s, FixedTaskbar bar, UiRect? ghost = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<ITaskbarWindows>(bar).Add<ITaskbarTray>(bar).Add<ITaskbarLaunchers>(bar);
        var windows = s.Create<UiWindows>();
        windows.SnapGhost = ghost;
        return s.Create<Taskbar>();
    }

    [UiSample("three windows, the browser on top, options minimised")]
    private static UiScreen ThreeWindows(UiSampleServices s) => Build(s, Three());

    [UiSample("the main menu, no window open")]
    private static UiScreen MainMenu(UiSampleServices s) => Build(s, new FixedTaskbar());

    [UiSample("a mod's buttons, one window")]
    private static UiScreen ModButtons(UiSampleServices s) => Build(s, new FixedTaskbar(
        new UiTaskbarWindow("serverbrowser", "#SS_UI_Browser_Title", true, false))
    {
        Launchers = [new("#SS_UI_Main_Players", false), new("#SS_UI_Main_Disconnect", false), new("#SS_UI_Options_Title", true)],
    });

    [UiSample("one window, settings open")]
    private static UiScreen Settings(UiSampleServices s)
        => Build(s, new FixedTaskbar(new UiTaskbarWindow("options", "#SS_UI_Options_Title", true, false)) { SettingsOpen = true });

    [UiSample("eight windows, wrapping")]
    private static UiScreen Many(UiSampleServices s) => Build(s, new FixedTaskbar(
        new UiTaskbarWindow("console", "#SS_UI_Console_Title", false, false),
        new UiTaskbarWindow("serverbrowser", "#SS_UI_Browser_Title", false, false),
        new UiTaskbarWindow("options", "#SS_UI_Options_Title", false, false),
        new UiTaskbarWindow("mouseoptions", "#SS_UI_Mouse_Title", false, true),
        new UiTaskbarWindow("keybindings", "#SS_UI_Keys_Title", false, false),
        new UiTaskbarWindow("audiooptions", "#SS_UI_Audio_Title", false, false),
        new UiTaskbarWindow("voiceoptions", "#SS_UI_Voice_Title", false, false),
        new UiTaskbarWindow("scoreboard", "#SS_UI_Taskbar_Untitled", true, false)) { Clock = "9:05" });

    [UiSample("snapping a window to the left half")]
    private static UiScreen Snapping(UiSampleServices s)
    {
        var bar = Three();
        // The left half above a one-row bar on a 1920x1080 design screen.
        return Build(s, bar, new UiRect(0, 0, 960, 1080 - Taskbar.ReservedDu(3, Taskbar.RowLimit)));
    }
}
