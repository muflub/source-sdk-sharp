using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// THE SPECTATOR'S TOP BAR (stock CSpectatorGUI, the "specgui" panel): while
/// observing, a strip across the top with how they are watching on the left
/// and the map on the right. Who they watch is <see cref="SpectatorTarget"/>'s,
/// at the bottom. Nothing while playing.
/// </summary>
public sealed class SpecGui(IObserverSource observer) : UiScreen
{
    public override string Name => "specgui";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override void Describe(ref UiFrame ui)
    {
        var o = observer;
        var mode = o.Mode;
        if (mode == SpectatorMode.None)
            return;

        using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
        using (ui.Row(Space.L, Align.Centre).At(UiAnchor.Top).Width(Size.Fill).Fill(Tone.Window)
                 .Pad(Space.XL, Space.M).Name("spectator bar"))
        {
            ui.Label(ModeToken(mode), Tone.Accent).Name("mode");
            ui.Flex();
            if (o.MapName.Length > 0)
            {
                ui.Label("#SS_UI_Spec_Map", Tone.TextMuted);
                ui.Data(o.MapName, Text.Heading).Elide().MaxWidth(320).Name("map");
            }
        }
    }

    /// <summary>The mode's name, one token a mode.</summary>
    public static string ModeToken(SpectatorMode mode) => mode switch
    {
        SpectatorMode.DeathCam => "#SS_UI_Spec_Mode_DeathCam",
        SpectatorMode.FreezeCam => "#SS_UI_Spec_Mode_FreezeCam",
        SpectatorMode.Fixed => "#SS_UI_Spec_Mode_Fixed",
        SpectatorMode.InEye => "#SS_UI_Spec_Mode_InEye",
        SpectatorMode.Chase => "#SS_UI_Spec_Mode_Chase",
        SpectatorMode.PointOfInterest => "#SS_UI_Spec_Mode_PointOfInterest",
        SpectatorMode.Roaming => "#SS_UI_Spec_Mode_Roaming",
        _ => "#SS_UI_Spec_Mode_None",
    };
}
