using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class ConsoleSamples
{
    /// <summary>The console from fakes; windows placed from <paramref name="store"/> (empty: the default size).</summary>
    public static UiScreen Build(UiSampleServices s, FakeConsole console, MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        return console.Register(s).Create<ConsoleScreen>();
    }

    public static FakeConsole Busy() => new()
    {
        Lines = FakeConsole.BusyLog(),
        Input = "sv_gravity 600",
        Caret = 14,
        HistoryCount = 6,
        HistoryIndex = 3,
    };

    [UiSample("busy log")]
    private static UiScreen BusyLog(UiSampleServices s) => Build(s, Busy());

    [UiSample("dragged to a corner")]
    private static UiScreen Corner(UiSampleServices s)
        => Build(s, Busy(), new MemoryWindowStore().With("console", new UiWindowPlacement(0, 0, 760, 420)));

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s, Autocompleting(), new MemoryWindowStore().With("console", new UiWindowPlacement(120, 80, 100, 100)));

    [UiSample("resized large")]
    private static UiScreen Large(UiSampleServices s)
        => Build(s, Busy(), new MemoryWindowStore().With("console", new UiWindowPlacement(0, 0, 100000, 100000)));

    [UiSample("partly off screen, clamped")]
    private static UiScreen OffScreen(UiSampleServices s)
        => Build(s, Busy(), new MemoryWindowStore().With("console", new UiWindowPlacement(1500, 700, 900, 600)));

    [UiSample("autocomplete open")]
    private static UiScreen Autocomplete(UiSampleServices s) => Build(s, Autocompleting());

    public static FakeConsole Autocompleting() => new()
    {
        Lines = FakeConsole.BusyLog(),
        Input = "cl_inter",
        Caret = 8,
        SelectionStart = 3,
        SelectionLength = 5,
        Completions =
        [
            new("cl_interp", "0.1", "Sets the interpolation amount (bounded on low side by server interp ratio settings).", false),
            new("cl_interp_ratio", "2", "Sets the interpolation amount (final amount is cl_interp_ratio / cl_updaterate).", false),
            new("cl_interp_all", "0", "Disable interpolation list optimizations.", false),
            new("cl_interp_npcs", "0.0", "Interpolate NPC positions starting this many seconds in past (or cl_interp, if greater).", false),
            new("cl_interpolate", "1", "Interpolate entities on the client.", false),
            new("cl_interp_watch", "", "Prints the interpolation history of the entity under the crosshair.", true),
        ],
        SelectedCompletion = 1,
    };

    [UiSample("empty console")]
    private static UiScreen Empty(UiSampleServices s) => Build(s, new FakeConsole { Caret = 0 });
}
