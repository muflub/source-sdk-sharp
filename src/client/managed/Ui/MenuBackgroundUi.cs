using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The menu background's source in game: the engine's main-menu background
/// (<see cref="IMenuBackgroundName"/>) as the stock GameUI named its material,
/// <c>console/&lt;name&gt;</c> and <c>console/&lt;name&gt;_widescreen</c>
/// (docs/gameui-census.md §5), and nothing while a level is up. The engine is
/// asked once, and again each time a level goes down (a background map can
/// change the name), so a frame at the menu reads two cached strings.
/// </summary>
public sealed class MenuBackgroundUi(IMenuBackgroundName engine, IMainMenuSource menu) : IMenuBackgroundSource
{
    private bool _asked;
    private string? _material;
    private string? _widescreen;

    public string? Material => Current() ? _material : null;

    public string? WidescreenMaterial => Current() ? _widescreen : null;

    private bool Current()
    {
        if (menu.InLevel)
        {
            _asked = false;
            return false;
        }
        if (!_asked)
        {
            _asked = true;
            string? name = engine.MainMenuBackgroundName();
            _material = name is null ? null : "console/" + name;
            _widescreen = name is null ? null : "console/" + name + "_widescreen";
        }
        return _material is not null;
    }
}
