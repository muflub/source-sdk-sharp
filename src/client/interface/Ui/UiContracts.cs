namespace SourceSharp.Client.Ui;

// The UI's contracts with the core (plan_client_ui §4, §5): what a screen is,
// what the layer that owns screens answers, what a backend draws with. Data
// and interfaces only; the behaviour is SourceSharp.Client.Ui.Toolkit.

/// <summary>
/// The screen a frame is laid out for, read each frame and never cached.
/// <see cref="Scale"/> is pixels per design unit: min(H / 1080, W / 1280) ×
/// ui_scale (plan_client_ui §5.1, ruling 2026-10-05: 4:3 and wider scale by
/// height, skinnier screens by width against 1280 du, so they keep readable
/// text and use their narrow arrangements).
/// <see cref="Safe"/> is the inset (pixels) nothing may be placed inside.
/// </summary>
public readonly record struct ScreenInfo(int W, int H, float Scale, UiInsets Safe);

/// <summary>
/// One of nine points on a rectangle. An element is placed by matching a point
/// on itself to a point on its parent, plus an offset.
/// </summary>
public enum UiAnchor : byte
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Centre,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// How a size is given. Design units scale with the screen height; there is no
/// pixel unit (the §3.2 ruling: mods see anchors and units only).
/// </summary>
public enum UiUnit : byte
{
    /// <summary>Fits the content.</summary>
    Auto,
    /// <summary>Design units: 1 du is 1 px at 1080p.</summary>
    Du,
    /// <summary>A share of what is left after the fixed and auto siblings (grid columns, stack fill).</summary>
    Fraction,
    /// <summary>A percentage of the parent's content box.</summary>
    Percent,
    /// <summary>All the space the parent offers on that axis.</summary>
    Fill,
}

/// <summary>Which layer a screen is drawn in: the HUD takes no input, a menu does.</summary>
public enum UiLayerKind : byte
{
    Hud,
    Menu,
}

/// <summary>
/// A screen: a named thing the layer can open, close and draw. How it draws is
/// the toolkit's (<c>UiScreen.Describe</c>); this is what the core and the dev
/// API see of it.
/// </summary>
public interface IUiScreen
{
    /// <summary>The name /ui, the previews and <c>Open</c> use: "scoreboard".</summary>
    string Name { get; }

    UiLayerKind Layer { get; }

    /// <summary>Order within the layer: higher draws later (on top).</summary>
    int Order { get; }

    bool Visible { get; }

    /// <summary>Whether the cursor is on while this screen is the top menu.</summary>
    bool WantsMouse { get; }
}

/// <summary>
/// The open screens of one side, as the core drives them: one frame per paint,
/// input in between, open and close by name (console commands and the server's
/// VGUI menus map onto these one to one).
/// </summary>
public interface IUiLayer
{
    /// <summary>Lays out and draws every visible screen of <paramref name="layer"/> for this screen at this time.</summary>
    void Frame(UiLayerKind layer, in ScreenInfo screen, double now);

    /// <summary>Queues an input event for the next frame.</summary>
    void Input(in UiInputEvent input);

    void Open(string name);

    void Close(string name);

    bool IsOpen(string name);

    /// <summary>Whether any open screen wants the cursor.</summary>
    bool WantsMouse { get; }
}

/// <summary>Measures text the way the backend will draw it.</summary>
public interface IUiFontMetrics
{
    UiFontMetrics Metrics(in UiFontSpec font);

    /// <summary>How far the pen moves for <paramref name="c"/>, in pixels.</summary>
    float Advance(in UiFontSpec font, char c);
}

/// <summary>Draws a render list: vgui in game, SVG in previews and tests.</summary>
public interface IUiBackend : IUiFontMetrics
{
    void Draw(scoped in UiDrawList list);
}

/// <summary>What happened.</summary>
public enum UiInputKind : byte
{
    MouseMove,
    MouseDown,
    MouseUp,
    Wheel,
    Key,
    Char,
    FocusLost,
}

/// <summary>
/// One input event. <see cref="X"/>/<see cref="Y"/> are pixels; <see cref="Code"/>
/// is the mouse button or key code; <see cref="Char"/> is a typed character;
/// <see cref="Delta"/> is wheel notches.
/// </summary>
public readonly record struct UiInputEvent(
    UiInputKind Kind, float X, float Y, int Code, char Char, float Delta);

/// <summary>
/// The keys the UI acts on, as <see cref="UiInputEvent.Code"/> carries them:
/// vgui's ButtonCode_t values (inputsystem/ButtonCode.h), so the panel forwards
/// the engine's code unchanged.
/// </summary>
public enum UiKey
{
    None = 0,
    Enter = 64,
    Space = 65,
    Backspace = 66,
    Tab = 67,
    Escape = 70,
    Delete = 73,
    Home = 74,
    End = 75,
    PageUp = 76,
    PageDown = 77,
    Up = 88,
    Left = 89,
    Down = 90,
    Right = 91,
    MouseLeft = 107,
    MouseRight = 108,
}

/// <summary>
/// The icon atlas's pixels, for a backend that uploads them (the vgui backend):
/// one alpha byte a pixel, row by row, white where the art is. A render list's
/// textured quad with a negative texture id draws from it.
/// </summary>
public interface IUiIconAtlas
{
    int AtlasWidth { get; }

    int AtlasHeight { get; }

    byte[] Alpha { get; }
}
