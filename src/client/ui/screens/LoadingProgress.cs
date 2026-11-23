using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The level load's progress dialog (stock GameUI's loading dialog): the
/// title, the engine's current step and a bar of the fraction loaded, in a
/// panel low in the middle of the screen. Up for every load, the dialog
/// asked for or not (stock showed it only when asked; a map from the
/// command line shows it too, by the user's ruling of 2026-11-15). Drawn
/// from inside the engine's load loop, from the source's held values only;
/// it takes no input.
/// </summary>
public sealed class LoadingProgress(ILoadingProgressSource loading) : UiScreen
{
    public override string Name => "loadingprogress";

    public override bool OpenByDefault => true;

    public override bool WantsMouse => false;

    /// <summary>Above the menus, below the loading plaque and the console.</summary>
    public override int Order => 90;

    public override void Describe(ref UiFrame ui)
    {
        if (!loading.Showing)
            return;
        using (ui.Box().Size(Size.Fill, Size.Fill))
        using (ui.Panel(Space.M).At(UiAnchor.Bottom).Inset(Space.None, Space.Huge).Width(Size.Du(560)))
        {
            ui.Text("#SS_UI_Loading", Text.Title);
            ui.Data(loading.Status, Text.Body).Elide();
            ui.Meter(loading.Progress, Tone.Accent, Size.Fill, 8);
        }
    }
}
