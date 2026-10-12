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

    /// <summary>Whether the layer opens it on its own: HUD screens are, menus wait to be asked.</summary>
    public virtual bool OpenByDefault => Layer == UiLayerKind.Hud;

    public abstract void Describe(ref UiFrame ui);
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
