using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The menu background: the stock GameUI's own backdrop, the engine's
/// main-menu background drawn full screen and opaque while no level is up
/// (docs/gameui-census.md §5). The lowest screen of the menu layer, open from
/// the start, taking no input; it draws nothing while a level is up (the
/// source names no material then). A material the art library cannot bind
/// leaves the window colour in its place, so the menu above still reads.
/// </summary>
public sealed class MenuBackground(IMenuBackgroundSource background, IUiTextureLibrary textures) : UiScreen
{
    /// <summary>Stock's widescreen test: aspect ratio 1.5999 or more.</summary>
    private const float WidescreenAspect = 1.5999f;

    public override string Name => "menubackground";

    public override bool OpenByDefault => true;

    public override bool WantsMouse => false;

    public override int Order => -100;

    public override void Describe(ref UiFrame ui)
    {
        bool wide = ui.Screen.W >= ui.Screen.H * WidescreenAspect;
        if ((wide ? background.WidescreenMaterial : background.Material) is not { } material)
            return;
        int handle = textures.Find(material);
        if (handle != 0)
            ui.Texture(handle, Size.Percent(100), Size.Percent(100)).Layered().Key("background");
        else
            ui.Rect(Tone.Window, Size.Percent(100), Size.Percent(100)).Layered().Key("background");
    }
}
