using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The taskbar: the menu layer's chrome along the screen's bottom, shown
/// exactly while the main or pause menu is up (an empty left with no window
/// open, the tray still there) and never in play. A mod's own buttons
/// (<see cref="ITaskbarLaunchers"/>: TF2's ITEMS and gears) stand first on
/// the left, or in the tray before Settings. Then one button per open window in the order
/// they were first shown, the top window's in the accent and a mark under each
/// shown (not minimised) one: clicking a window
/// under others (or minimised) brings it to the top, clicking the top one
/// minimises it. On the right, the tray: the settings quick-open (the options
/// window) and the local time. The bar draws above every window and takes the
/// screen's bottom from them (<see cref="UiWindows.DockReserved"/>: windows
/// resize, drag and snap above it); while a title bar is held at a screen edge
/// it draws the segment that edge snaps to, as a ghost over everything.
/// Buttons wrap into rows on a narrow screen rather than run off it.
/// </summary>
public sealed class Taskbar(ITaskbarWindows open, ITaskbarTray tray, ITaskbarLaunchers launchers, UiWindows windows) : UiScreen
{
    /// <summary>A window button's width, du, and the most per row at full width.</summary>
    public const float ButtonDu = 200;

    public const int RowLimit = 6;

    // The tray: the settings button and the clock beside it, du.
    private const float TrayDu = 240;

    // A row of buttons is keyed past every button's own key (its index).
    private const int RowKeyBase = 1 << 16;

    // A mod's button's room, du, as the window rows reckon it: its least width and a gap.
    private const float LauncherDu = 180;

    // A mod's left button is keyed past the window rows.
    private const int LauncherKeyBase = 1 << 17;

    public override string Name => "taskbar";

    public override bool Chrome => true;

    /// <summary>How many window buttons fit a row of a screen <paramref name="designWidthDu"/> wide beside the tray and <paramref name="launchers"/> mod buttons.</summary>
    public static int RowLimitFor(float designWidthDu, int launchers = 0)
    {
        int per = (int)((designWidthDu - TrayDu - launchers * LauncherDu - 2 * (float)Space.S) / (ButtonDu + (float)Space.S));
        return Math.Clamp(per, 1, RowLimit);
    }

    /// <summary>The bar's height for <paramref name="buttons"/> windows at <paramref name="perRow"/>: its rows, their gaps and its padding.</summary>
    public static float BarHeightDu(int buttons, int perRow)
    {
        int rows = (Math.Max(buttons, 1) + perRow - 1) / perRow;
        return rows * (40f + (float)Space.XS + 2f) + (rows - 1) * (float)Space.S + 2 * (float)Space.S;
    }

    /// <summary>What windows must stay above: the bar and a slack, so a snapped window's edge never grazes it.</summary>
    public static float ReservedDu(int buttons, int perRow) => BarHeightDu(buttons, perRow) + (float)Space.S;

    public override void Describe(ref UiFrame ui)
    {
        int n = open.WindowCount;
        int mods = launchers.LauncherCount;
        int per = RowLimitFor(ui.DesignWidth, mods);
        windows.DockReserved = ReservedDu(n, per);

        using var layer = ui.OverlayTop("taskbar", windows);
        // The snap ghost first: over the windows, kept off the bar by the reserved band.
        if (windows.SnapGhost is { } g)
            // Held to the screen: a ghost offered at another size (a resize between frames) never runs off it.
            ui.Rect(((UiPaint)Tone.Accent).WithOpacity(0.25f), Size.Du(MathF.Min(g.W, ui.DesignWidth - g.X)),
                    Size.Du(MathF.Min(g.H, ui.DesignHeight - g.Y)))
              .At(UiAnchor.TopLeft).Inset(new UiLength(g.X, false), new UiLength(g.Y, false))
              .Round(Radius.L).Border(((UiPaint)Tone.Accent).WithOpacity(0.9f), 2).Name("snap-ghost").Layered();

        using var bar = ui.Box("taskbar-bar").At(UiAnchor.Bottom, UiAnchor.Bottom).Width(Size.Fill)
                          .Fill(Tone.Window).Border(Tone.Line, 1).Raise(Elevation.Overlay).Pad(Space.S);
        using var split = ui.Row(Space.S, Align.End).Width(Size.Fill);
        // A mod's left buttons, on the window buttons' line, with the same room under them as their marks take.
        for (int m = 0; m < mods; m++)
        {
            var launcher = launchers.Launcher(m);
            if (launcher.Tray)
                continue;
            using (ui.Column(Space.XS, Align.Stretch).Key(LauncherKeyBase + m))
            {
                if (ui.Button(launcher.Token, launcher.Token).Clicked)
                    launchers.Launch(m);
                ui.Spacer(Size.Du(2), Size.Du(2));
            }
        }
        using (ui.Column(Space.S, Align.Start).Width(Size.Fr(1)).Key("taskbar-windows"))
        {
            for (int i = 0; i < n; i += per)
            {
                using var row = ui.Row(Space.S).Key(RowKeyBase + i / per);
                for (int j = i; j < Math.Min(i + per, n); j++)
                {
                    var w = open.Window(j);
                    // Under each shown window's button a mark; a minimised window's has none.
                    using (ui.Column(Space.XS, Align.Stretch).Key(j))
                    {
                        if (ui.Button(w.Screen, w.TitleToken, primary: w.Top, width: Size.Du(ButtonDu)).Clicked)
                            open.Activate(j);
                        ui.Rect(w.Minimised ? Tone.None : Tone.Accent, Size.Fill, Size.Du(2)).Round(Radius.S);
                    }
                }
            }
        }
        // The tray stands on the same line as the window buttons: the same room
        // under it as their marks take.
        using (ui.Column(Space.XS, Align.Stretch).Key("taskbar-tray"))
        {
            using (ui.Row(Space.M, Align.Centre))
            {
                for (int m = 0; m < mods; m++)
                {
                    var launcher = launchers.Launcher(m);
                    if (launcher.Tray && ui.Button(launcher.Token, launcher.Token).Clicked)
                        launchers.Launch(m);
                }
                if (ui.Button("taskbar-settings", "#SS_UI_Taskbar_Settings", primary: tray.SettingsOpen).Clicked)
                    tray.OpenSettings();
                ui.Data(tray.Clock, Text.Heading, Tone.Text).MinWidth(64).Name("taskbar-clock");
            }
            ui.Spacer(Size.Du(2), Size.Du(2));
        }
    }
}
