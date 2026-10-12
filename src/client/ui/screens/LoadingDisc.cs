using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The loading and paused plaques (stock CLoadingDiscPanel): a small dark
/// plaque in the middle of the screen, the engine's text on it. Above
/// everything, taking no input.
/// </summary>
public sealed class LoadingDisc(ILoadingDiscSource disc) : UiScreen
{
    public override string Name => "loadingdisc";

    public override UiLayerKind Layer => UiLayerKind.Menu;

    public override bool OpenByDefault => true;

    public override bool WantsMouse => false;

    public override int Order => 100;

    public override void Describe(ref UiFrame ui)
    {
        var d = disc;
        if (!d.Loading && !d.Paused)
            return;
        using (ui.Box().Size(Size.Fill, Size.Fill))
        using (ui.Panel(Space.XL, Space.XS).At(UiAnchor.Centre).Pad(Space.XXL, Space.L))
            ui.Data(d.Loading ? d.LoadingText : d.PausedText, Text.Heading);
    }
}
