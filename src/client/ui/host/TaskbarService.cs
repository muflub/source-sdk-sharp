using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The taskbar's data over the menu layer and its windows: the open windows in
/// the order they were first shown, cached until a screen opens, closes or
/// minimises (<see cref="UiLayer.OpenEpoch"/>), so the bar allocates nothing per
/// frame; a window's title is the token its title bar draws. A button opens
/// its window through the host's own Open (so it refreshes and logs like any
/// other way in), or minimises the top one. The clock reads from a table of
/// every minute built at construction. The composition root builds it before
/// the layer (the taskbar screen takes it) and <see cref="Bind"/>s the layer
/// and the host's Open once both exist.
/// </summary>
public sealed class TaskbarService(UiWindows windows, Func<DateTime>? wallClock = null) : ITaskbarWindows, ITaskbarTray
{
    /// <summary>The options window's screen: the tray's settings quick-open.</summary>
    public const string SettingsScreen = "options";

    private const string Untitled = "#SS_UI_Taskbar_Untitled";

    private readonly List<(string Screen, string WindowId, bool Minimised)> _open = [];
    private readonly string[] _clock = ClockTable();
    private readonly Func<DateTime> _wallClock = wallClock ?? (() => DateTime.Now);
    private UiLayer? _layer;
    private Action<string>? _openScreen;
    private long _seen = -1;

    /// <summary>The menu layer and the host's Open: each needs the other built first.</summary>
    public void Bind(UiLayer layer, Action<string> open)
    {
        _layer = layer;
        _openScreen = open;
        _seen = -1;
    }

    public int WindowCount
    {
        get
        {
            Refresh();
            return _open.Count;
        }
    }

    public UiTaskbarWindow Window(int i)
    {
        Refresh();
        var (screen, id, minimised) = _open[i];
        return new UiTaskbarWindow(screen, windows.TitleOf(id) ?? Untitled, !minimised && windows.Top == id, minimised);
    }

    public void Activate(int i)
    {
        Refresh();
        if (_layer is null || (uint)i >= (uint)_open.Count)
            return;
        var w = Window(i);
        if (w.Top)
            _layer.Minimise(w.Screen);
        else
            Open(w.Screen);
    }

    public bool SettingsOpen => _layer is { } layer && layer.IsOpen(SettingsScreen) && !layer.IsMinimised(SettingsScreen);

    public void OpenSettings() => Open(SettingsScreen);

    public string Clock
    {
        get
        {
            var now = _wallClock();
            return _clock[now.Hour * 60 + now.Minute];
        }
    }

    private void Open(string screen)
    {
        if (_layer is { } layer && layer.Has(screen))
            _openScreen?.Invoke(screen);
    }

    // The open windows in first-shown order, rebuilt only when the layer's open set changed.
    private void Refresh()
    {
        if (_layer is not { } layer || _seen == layer.OpenEpoch)
            return;
        _seen = layer.OpenEpoch;
        _open.Clear();
        var screens = layer.Screens;
        for (int i = 0; i < screens.Count; i++)
            if (layer.IsOpenAt(i) && layer.IsWindow(i))
                _open.Add((screens[i].Name, ((IUiWindowed)screens[i]).WindowId, layer.IsMinimisedAt(i)));
        _open.Sort((a, b) => Rank(a.WindowId).CompareTo(Rank(b.WindowId)));
    }

    // A window never shown yet (opened this frame) goes after those that were.
    private int Rank(string id) => windows.OpenedRank(id) is var r and >= 0 ? r : int.MaxValue;

    /// <summary>Every minute of the day as H:mm, built once so the clock allocates nothing when it turns.</summary>
    private static string[] ClockTable()
    {
        var table = new string[24 * 60];
        for (int m = 0; m < table.Length; m++)
            table[m] = $"{m / 60}:{m % 60:00}";
        return table;
    }
}

/// <summary>No mod buttons on the taskbar: the core's, and a mod's that brings none.</summary>
public sealed class NoTaskbarLaunchers : ITaskbarLaunchers
{
    public int LauncherCount => 0;

    public UiTaskbarLauncher Launcher(int i) => throw new ArgumentOutOfRangeException(nameof(i));

    public void Launch(int i) { }
}
