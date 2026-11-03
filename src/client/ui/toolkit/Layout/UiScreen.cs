using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A screen: what the game opens by name and draws each frame. Implement
/// <see cref="Describe"/>; take what it shows by constructor injection, as one
/// of the data interfaces in SourceSharp.Client.Interface, so the screen
/// previews with sample data (plan_client_ui §4).
/// </summary>
public abstract class UiScreen : IUiScreen
{
    public abstract string Name { get; }

    public virtual UiLayerKind Layer => UiLayerKind.Menu;

    public virtual int Order => 0;

    /// <summary>Whether the screen draws when open. A HUD element hides itself here (no local player).</summary>
    public virtual bool Visible => true;

    public virtual bool WantsMouse => Layer == UiLayerKind.Menu;

    /// <summary>The stock viewport panel this screen speaks for (the "team"
    /// of the host's panel map): the server's VGUIMenu and the viewport's
    /// show of that panel open this screen instead of the core's, so a mod
    /// replaces a stock menu (TF2 stage 3.2's team menu). Null — the default
    /// — is a screen that is no panel's.</summary>
    public virtual string? Panel => null;

    /// <summary>The panel names this screen speaks for when it speaks for
    /// more than one (TF2 stage 3.2's class menu, one screen for
    /// <c>class</c>, <c>class_red</c> and <c>class_blue</c>): claimed
    /// alongside <see cref="Panel"/>, first claimer in layer order either
    /// way. Answer a field, not a new list per call.</summary>
    public virtual IReadOnlyList<string> Panels => [];

    /// <summary>Said just before a show of a claimed panel opens this screen,
    /// with the claimed name it came in under: how one screen told a
    /// <c>class_red</c> show from a <c>class_blue</c> one. The plain
    /// <see cref="UiLayer.Open"/> path — a chain, a command — says nothing.</summary>
    public virtual void PanelOpened(string panel) { }

    /// <summary>Said once after the host closes this screen, whichever way
    /// the close came — a named close, the top window's Escape, the
    /// window's own close button: the mirror of the show, so a screen can
    /// tell the game its visible state ended (a stock panel learns its
    /// hide from the same call that hides it). Never said for a screen
    /// that was not open.</summary>
    public virtual void Closed() { }

    /// <summary>Whether the layer opens it on its own: HUD screens are, menus wait to be asked.</summary>
    public virtual bool OpenByDefault => Layer == UiLayerKind.Hud;

    /// <summary>
    /// The named elements of other screens this one replaces while it is open and
    /// visible (a mod HUD's <c>hud.vitals</c>, <c>hud.ammo</c>): a screen that asks
    /// <see cref="UiFrame.Taken"/> for one leaves its own element out. Answer a
    /// field, not a new list per call.
    /// </summary>
    public virtual IReadOnlyList<string> Takes => [];

    public abstract void Describe(ref UiFrame ui);
}

/// <summary>
/// The screens an assembly ships (stage 0.9): every concrete, closed
/// <see cref="UiScreen"/> type in it, by full name, assembly by assembly. The
/// one list the game places a mod's screens from (ClientUi.ModScreens) and the
/// UI facts hold every screen's sample against, so the game and the preview
/// cannot disagree about what a UI assembly holds. Visibility is not a filter:
/// an internal screen dropped here would be a screen silently never shown.
/// </summary>
public static class UiScreens
{
    public static IReadOnlyList<Type> Discover(params System.Reflection.Assembly[] assemblies)
        => [.. assemblies.SelectMany(a => a.GetTypes()
                                          .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(UiScreen).IsAssignableFrom(t))
                                          .OrderBy(t => t.FullName, StringComparer.Ordinal))];
}

/// <summary>
/// A reusable sub-panel (plan_client_ui §5.7a): a class with its own fields,
/// embedded with <c>ui.Sub(key, panel, props)</c> under its own id scope. Game
/// data comes in by constructor (the data interfaces it reads, built by DI);
/// props are only what the parent decides per call (a team index, a tone, a
/// gap), a struct, so passing them allocates nothing.
/// </summary>
public abstract class UiPanel<TProps>
{
    public abstract void Describe(ref UiFrame ui, in TProps props);
}

/// <summary>
/// A sub-panel that needs nothing per call: everything it shows comes from the
/// data interfaces its constructor takes. Embedded with <c>ui.Sub(key, panel)</c>.
/// </summary>
public abstract class UiPanel
{
    public abstract void Describe(ref UiFrame ui);
}
