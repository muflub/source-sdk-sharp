namespace Debug.Api.Client.Interfaces;

/// <summary>One primitive the game's UI backend drew last frame (its draw ledger, plan_client_ui U2).</summary>
public sealed record DebugClientUiDraw(
    string Kind, int X0, int Y0, int X1, int Y1, string Colour, int Font, string Text, int Element);

/// <summary>
/// The UI as <c>/client/{slot}/ui</c> reports it: the screen it was laid out
/// for, what its frames cost, how many of each primitive the last frame drew,
/// and the ledger itself; with <c>?svg=1</c>, the same render list as SVG.
/// </summary>
public sealed record DebugClientUi(
    bool Built, int W, int H, float Scale, long Frames, double MeanMicroseconds, double LastMicroseconds,
    double MaxMicroseconds, IReadOnlyList<DebugCount> Kinds, IReadOnlyList<DebugClientUiDraw> Draws, string? Svg,
    IReadOnlyList<string>? Open = null, bool MenuInput = false);

/// <summary>One font the UI made: what it asked for, the vgui handle, and what the surface measures.</summary>
public sealed record DebugClientUiFont(
    string Family, int Px, int Weight, int Flags, int Handle, int Tall, int Ascent, int FirstChar,
    IReadOnlyList<int> Advances);

public interface IDebugClientUi
{
    DebugClientUi Read(bool svg);

    /// <summary>The menu layer's last frame, as <see cref="Read"/> reports the HUD's; with which screens are open.</summary>
    DebugClientUi ReadMenu(bool svg);

    /// <summary>
    /// A WRITE: one input event into the menu layer, as the menu root forwards
    /// them ("kind,x,y,code,char,delta"; kind as UiInputKind). For tests that
    /// click without a mouse.
    /// </summary>
    string Event(string argument);

    /// <summary>
    /// Every font the UI has made. With <paramref name="sweep"/>, each theme
    /// role's font at every preview size is made first, so the list is what
    /// the previews measure with (src/test/client/Fixtures/ui-fonts.tsv).
    /// </summary>
    IReadOnlyList<DebugClientUiFont> Fonts(bool sweep);
}
