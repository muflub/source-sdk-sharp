using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;

namespace TF2.Client.Ui;

/// <summary>
/// Opening and closing the game's menu screens from TF2's own code (stage
/// 3.2): the <c>changeclass</c> command raises the class menu — things
/// stock's panels do from inside the panel itself, here done by name
/// through the live runtime (the ShowInfo/UiOpenCommand way, so facts can
/// hold a fake). The game's implementation is <see cref="TFMenuNavigation"/>,
/// over the loader's current UI; with no UI loaded the asks answer nobody
/// and do nothing.
/// </summary>
public interface ITFMenuNavigation
{
    /// <summary>Whether the layer holds a screen by that name.</summary>
    bool Has(string screen);

    /// <summary>Opens it (the choice's chain, the command's ask).</summary>
    void Open(string screen);

    /// <summary>Shows a viewport panel by name — the ask stock's panels
    /// make of their viewport (<c>ShowViewPortPanel</c>): the claimed
    /// screen learns which name opened it, the way the viewport's and the
    /// server's shows do.</summary>
    void OpenPanel(string panel);

    /// <summary>Closes it (a choice made, a cancel, an Esc elsewhere).</summary>
    void Close(string screen);
}

/// <summary>
/// The navigation in game: the loader's current UI, which a UI-only reload
/// may swap between the asks — so the loader is held, not its host
/// (the ShowInfo way of naming the runtime: <c>ui.Current</c> each ask).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMenuNavigation)])]
public sealed class TFMenuNavigation(UiLoader ui) : ITFMenuNavigation
{
    public bool Has(string screen) => ui.Current?.Has(screen) ?? false;

    public void Open(string screen) => ui.Current?.Open(screen);

    public void OpenPanel(string panel) => ui.Current?.ViewportPanel(panel, true);

    public void Close(string screen) => ui.Current?.Close(screen);
}
