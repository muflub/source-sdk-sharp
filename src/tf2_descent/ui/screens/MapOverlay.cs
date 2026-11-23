using System;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// THE MAP OVERLAY: Diablo's Tab map. The automap canvas drawn over the view,
/// translucent, on the HUD layer so the game and the HUD stay live under it
/// (no cursor, no window). Tab shows it and puts it down
/// (<see cref="IDescentMapOverlay"/>); M keeps the full automap window. The map
/// is the corner minimap's and the window's canvas, as large as the screen's
/// shorter side allows inside its margins, slid so the hero stands as near the
/// screen's centre as the map's own edges let him (the source fits every
/// revealed room in at zoom 1; <c>tf2_descent_automap_zoom</c> above 1 centres
/// the source on the hero, and the overlay then centres him on the screen).
/// Nothing revealed, nothing drawn.
/// </summary>
public sealed class MapOverlay(IDescentMapOverlay overlay, IDescentAutomap map, AutomapCanvas canvas) : UiScreen
{
    public override string Name => "mapoverlay";
    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>How opaque the overlay draws: the world reads through it.</summary>
    public const float Opacity = 0.75f;

    /// <summary>The canvas's grid, as <see cref="AutomapCanvas"/> lays the source's coordinates out.</summary>
    private const float Grid = 1024f;

    /// <summary>The marker glyphs, du: the full window's size, so the marks read at a glance.</summary>
    private const float MarkerDu = 16f;

    public override void Describe(ref UiFrame ui)
    {
        if (!overlay.Shown || map.RoomCount == 0)
            return;

        float viewW = ui.DesignWidth - 2 * (float)Space.Huge, viewH = ui.DesignHeight - 2 * (float)Space.Huge;
        float side = Math.Min(viewW, viewH);
        var (x, y) = Place(viewW, viewH, side, map.HeroX * side / Grid, map.HeroY * side / Grid);

        // The view: the screen inside its margins; the map square slides in it.
        using (ui.Box().At(UiAnchor.Centre).Size(Size.Du(viewW), Size.Du(viewH)).Opacity(Opacity).Key("view"))
            using (ui.Box().Size(Size.Du(side), Size.Du(side)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(x, false), new UiLength(y, false)))
                ui.Sub("map", canvas, new AutomapProps(side, side, HeroHeading: true, HeroFollowsStorey: true, MarkerDu: MarkerDu));
    }

    /// <summary>
    /// Where the map square's top-left goes in a <paramref name="viewW"/> by
    /// <paramref name="viewH"/> view: the hero (at <paramref name="heroX"/>,
    /// <paramref name="heroY"/> in the square) on the view's centre, as far as
    /// the square staying inside the view allows.
    /// </summary>
    public static (float X, float Y) Place(float viewW, float viewH, float side, float heroX, float heroY)
        => (Math.Clamp(viewW / 2 - heroX, 0f, Math.Max(0f, viewW - side)),
            Math.Clamp(viewH / 2 - heroY, 0f, Math.Max(0f, viewH - side)));
}
