using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The engine's debug-overlay text (stock CDebugOverlay): each line where the
/// engine placed it, in its colour, in the fixed-width role. A developer's
/// view; each line on a small surface (stock drew bare outlined text) so it
/// reads over anything.
/// </summary>
public sealed class DebugOverlayText(IDebugOverlaySource overlay) : UiScreen
{
    public override string Name => "debugoverlay";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override int Order => 20;

    public override void Describe(ref UiFrame ui)
    {
        var o = overlay;
        if (o.Count == 0)
            return;
        float scale = ui.PixelScale;
        using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
        {
            for (int i = 0; i < o.Count; i++)
            {
                var l = o.Line(i);
                using (ui.Panel(Space.XS, Space.None, Elevation.Flat).At(UiAnchor.TopLeft)
                         .Inset(new UiLength(MathF.Round(l.X / scale), false), new UiLength(MathF.Round(l.Y / scale), false)).Key(i).Layered())
                    ui.Data(l.Text, Text.Code).Color(UiPaint.FromGame(l.Colour));
            }
        }
    }
}
