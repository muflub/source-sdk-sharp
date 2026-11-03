using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>The menu background's sample: no level up, the engine's background named.</summary>
internal static class MenuBackgroundSamples
{
    private sealed class Fake : IMenuBackgroundSource
    {
        public string? Material => "console/background01";

        public string? WidescreenMaterial => "console/background01_widescreen";
    }

    [UiSample("at the menu")]
    private static UiScreen AtTheMenu(UiSampleServices s)
        => s.Add<IMenuBackgroundSource>(new Fake()).Create<MenuBackground>();
}
